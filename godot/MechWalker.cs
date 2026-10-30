using Godot;

// MechWalker: reusable procedural gait + two-bone IK for a rigged mech FBX,
// extracted from the original walker test scene and
// generalized: any heading (not just -Z), signed speed (reverse stepping),
// turn-in-place stepping, torso twist/pitch aim on the torso bone, and bone
// data derived per-model from the rest pose -- the knee pole comes from the
// rest pose, so digitigrade (Timber Wolf) and plantigrade (Atlas) legs both
// work with no hardcoded bend direction.
//
// Drive model: every frame the owner calls SetDrive(legsYaw, targetSpeed)
// and StepFrame(delta). The walker advances its root along the legs heading,
// plants feet in WORLD space (targets sampled from Main.HeightAt at each
// foot's own XZ), solves hip/knee/ankle by analytic two-bone IK written as
// skeleton global pose overrides, and sets the root height/tilt from the
// feet. Collisions are the owner's job: after stepping, the owner may call
// ApplyRootXZ to push the root out of an obstacle; planted feet stay put.
public partial class MechWalker : Node3D
{
    public struct Config
    {
        public string Label;
        public string ModelPath;
        public float TargetHeight;
        public float StepTime;
        public float StepThreshold;
        public float StepLift;
        public float Accel;
        public float CollisionRadius;
        public bool FlipKneePole; // plantigrade chassis (Atlas): mirror the rest-pose knee pole so the knee bends FORWARD, human-style (owner round 22)
    }

    private sealed class Leg
    {
        public string Name;
        public int Hip, Knee, Ankle;
        public Vector3 Hrest, Krest, Arest;
        public Basis HrestBasis, KrestBasis;
        public float L1, L2;
        public Vector3 ThighRestDir, ShinRestDir;
        public Vector3 PoleSk;
        public Vector3 SoleOffLocal;
        public Basis M0;
        public Vector3 SoleOffFrame;
        public Vector2 HomeLocal;
        public float AnkleAboveSoleW;
        public bool Planted = true;
        public Vector3 SoleTarget;
        public Basis PlantBasisW;
        public Basis CurBasisW;
        public Vector3 StepFrom, StepTo;
        public Basis StepBasisFrom, StepBasisTo;
        public float StepT = -1f;
        public Transform3D SolvedAnklePose;
        public Vector3 SolvedSoleWorld;
        public Vector3 PrevSoleWorld;
        public bool HasPrev;
        public float LastPlantedDiff;
        public bool Disabled;
    }

    private const float StandReachFactor = 0.90f;
    // Torso twist was clamped to ±135° (a 270° arc) until 2026-10-29,
    // when the owner removed the clamp: the torso rotates the full
    // 360°, including fully backward. The setter below no longer
    // clamps; angles wrap naturally through WrapAngle upstream.

    private Config _cfg;
    private Node3D _inner;
    private Skeleton3D _skel;
    private bool _ready;
    private float _scaleF = 1f;
    private Transform3D _chain;
    private Transform3D _holderXf;
    private Transform3D _skelGlobal;

    private readonly Leg _legL = new Leg { Name = "L" };
    private readonly Leg _legR = new Leg { Name = "R" };

    private int _torsoBone = -1;
    private Basis _torsoM0;
    private Vector3 _torsoRestOrigin;
    private Vector3 _torsoFwdBoneLocal = Vector3.Forward;

    // drive state
    private Vector3 _rootPos;
    private Basis _rootBasis = Basis.Identity;
    private Vector3 _upEased = Vector3.Up;
    private float _legsYaw;
    private float _prevYaw;
    private float _yawRate;
    private float _speed;
    private float _jumpH, _jumpVel;
    private float _groundY; // gait-followed root height; the jump arc rides on top unsmoothed
    public bool JumpJets; // thrusters fitted AND chassis capable (set by the duel from spec+loadout)
    public float JumpCharge = 1f; // thruster load 0..1: burning drains it (~2.2s of flight); recharges whenever not burning (~5s), even mid-air (owner round 22)
    public float JumpHeight => _jumpH;
    public bool Airborne => _jumpH > 0.02f;

    private bool _thrustHeld;
    public bool Thrusting { get; private set; }

    // Thrusters (owner, replacing the ballistic jump): HOLD the
    // key to fly. SetThrust is the held state; while held with
    // charge remaining the jets climb (capped ~10.5 u/s) and drain
    // the full charge in ~2.2s of burn — enough to rise well above
    // a 14u enemy. Release the key or empty the tanks and gravity
    // (24 u/s^2, terminal -18) brings the mech down. A partial
    // charge flies proportionally lower. The charge auto-refills
    // on the ground in ~5s.
    public void SetThrust(bool on) => _thrustHeld = on;
    private float _targetSpeed;
    private float _bob;
    private Vector3 _sway = Vector3.Zero;
    private double _acc;
    private int _steps;
    private float _maxSlide;
    private int _plantedFrames;
    private float _hipRiseW;
    private float _hipAboveHolderW;

    public bool StepMidCrossed { get; private set; }
    public string Label => _cfg.Label;
    public Skeleton3D Skeleton => _skel;
    public Node3D ModelRoot => _inner;

    // Re-run material collection (e.g. after the part system replaced a
    // mesh instance inside the model): clears the registry and duplicates
    // fresh per-instance materials for every mesh currently in the model.
    public void RefreshMaterials()
    {
        _mats.Clear();
        CollectMats(_inner);
    }
    public bool IsReady => _ready;
    public float Speed => _speed;
    public float LegsYaw => _legsYaw;
    public float CollisionRadius => _cfg.CollisionRadius;
    public Vector3 RootPos => _rootPos;
    public int Steps => _steps;
    public float MaxSlide => _maxSlide;

    private float _torsoTwist;
    private float _torsoPitch;
    public float TorsoTwist
    {
        get => _torsoTwist;
        set => _torsoTwist = value; // unclamped (owner, 2026-10-29): full 360° torso rotation
    }
    public float TorsoPitch
    {
        get => _torsoPitch;
        set => _torsoPitch = Mathf.Clamp(value, -0.32f, 0.48f);
    }
    // The AIM pitch (owner, round 13): the mouse target is NOT bound
    // by the torso's mechanical clamp — the reticle must reach the
    // ground in front of the feet and the sky above. The torso mesh
    // follows TorsoPitch to its stops; the aim direction uses this
    // wider range (MechDuel owns the live value; NaN there means
    // "follow TorsoPitch", which is what every harness phase sets).
    public float AimPitchWide = float.NaN;
    public float TorsoYawWorld => _legsYaw + _torsoTwist;

    // Combat state set by the owner: a halted walker keeps its last solved
    // pose (feet stay planted) and only the torso droop still animates.
    public bool Halted { get; set; }
    public float Droop { get; set; }

    // Breakable-parts state (owner-driven): a wrecked (disabled) leg
    // keeps stepping with the gait — the whole mech is slower via
    // SpeedFactor, and the wrecked leg moves at that same reduced
    // pace (owner's round-7 call; the earlier rigid drag read as the
    // leg floating). LimpRoll leans the body (toward the model's left
    // when positive).
    public float SpeedFactor = 1f;
    public float LimpRoll;

    // Fallen state (owner's rule, third iteration): a mech falls ONLY
    // when it loses a leg, and it never gets back up. The fall tips the
    // root toward _fallDirW by up to ~76 degrees over 0.5s and drops the
    // hips to rest height — then it stays there until reset. While down
    // the drive is suppressed and no new steps begin, but the torso can
    // still twist and the weapons still fire (a prone turret).
    private float _fallT = -1f;
    private float _fallAmt;
    private Vector3 _fallDirW = Vector3.Forward;
    public bool IsKnockedDown => _fallT >= 0f;
    public float FallAmount => _fallAmt;

    public bool TryKnockDown(Vector3 awayDirW)
    {
        if (_fallT >= 0f || Halted) return false;
        var d = new Vector3(awayDirW.X, 0f, awayDirW.Z);
        _fallDirW = d.LengthSquared() > 0.01f ? d.Normalized() : Vector3.Forward;
        _fallT = 0f;
        _fallAmt = 0f;
        return true;
    }

    public void SetLegDisabled(bool left, bool disabled)
    {
        var leg = left ? _legL : _legR;
        leg.Disabled = disabled;
        if (disabled) leg.HasPrev = false;
    }

    public bool LegDisabled(bool left) => (left ? _legL : _legR).Disabled;
    public Vector3 SoleWorld(bool left) => (left ? _legL : _legR).SolvedSoleWorld;
    public bool LegPlanted(bool left) => (left ? _legL : _legR).Planted;
    public Vector3 VelocityVec => HeadingVec(_legsYaw) * _speed;

    // World position of a skeleton bone (muzzle hardpoints, aim points).
    public Vector3 BoneWorldPos(string name)
    {
        if (_skel == null) return GlobalPosition;
        int idx = _skel.FindBone(name);
        if (idx < 0) return GlobalPosition;
        return _skel.GlobalTransform * _skel.GetBoneGlobalPose(idx).Origin;
    }

    public static Vector3 HeadingVec(float yaw) => new Vector3(-Mathf.Sin(yaw), 0f, -Mathf.Cos(yaw));
    public static float YawOf(Vector3 dir) => Mathf.Atan2(-dir.X, -dir.Z);
    public static float WrapAngle(float a)
    {
        while (a > Mathf.Pi) a -= Mathf.Tau;
        while (a < -Mathf.Pi) a += Mathf.Tau;
        return a;
    }

    // Call once after the walker is inside the tree.
    public void Init(Config cfg, Vector3 spawnPos, float legsYaw)
    {
        _cfg = cfg;
        var packed = GD.Load<PackedScene>(cfg.ModelPath);
        if (packed == null) { GD.PrintErr($"{cfg.Label} FAIL: {cfg.ModelPath} did not load"); return; }
        _inner = packed.Instantiate<Node3D>();
        AddChild(_inner);

        var raw = MeasureWorldAabb(_inner);
        float s = cfg.TargetHeight / raw.Size.Y;
        _scaleF = s;
        Scale = Vector3.One * s;
        var a = MeasureWorldAabb(_inner);
        var desiredMin = new Vector3(-a.Size.X / 2f, 0f, -a.Size.Z / 2f);
        _inner.Position += (desiredMin - a.Position) / s;
        var chk = MeasureWorldAabb(_inner);
        GD.Print($"{cfg.Label} normalized AABB pos={chk.Position} size={chk.Size} scale={s:F4}");

        // Blender FBX faces +Z after import; rotate so it faces -Z at yaw 0.
        Rotation = new Vector3(0f, Mathf.Pi, 0f);
        Position = spawnPos;

        _skel = FindSkeleton(_inner);
        if (_skel == null) { GD.PrintErr($"{cfg.Label} FAIL: no Skeleton3D"); return; }
        CollectMaterials();
        var names = new string[_skel.GetBoneCount()];
        for (int i = 0; i < names.Length; i++) names[i] = _skel.GetBoneName(i);
        GD.Print($"{cfg.Label} skeleton bones={names.Length}: {string.Join(",", names)}");

        _torsoBone = _skel.FindBone("torso");
        InitLegs(spawnPos);
        if (_legL.Hip < 0 || _legR.Hip < 0) { GD.PrintErr($"{cfg.Label} FAIL: leg bones not found"); return; }

        _legsYaw = legsYaw;
        _prevYaw = legsYaw;
        _rootPos = spawnPos;
        _rootBasis = Basis.Identity.Rotated(Vector3.Up, legsYaw + Mathf.Pi);
        _holderXf = GlobalTransform;
        _skelGlobal = _holderXf * _chain;
        _ready = true;
        GD.Print($"{cfg.Label} walker ready at {spawnPos} yaw={legsYaw:F2}");
    }

    private void InitLegs(Vector3 spawnPos)
    {
        var holderG = GlobalTransform;
        var skelG = _skel.GlobalTransform;
        _chain = holderG.AffineInverse() * skelG;
        var worldAabb = MeasureWorldAabb(_inner);
        float groundY = worldAabb.Position.Y;

        if (_torsoBone >= 0)
        {
            var rt = _skel.GetBoneGlobalRest(_torsoBone);
            _torsoRestOrigin = rt.Origin;
            _torsoM0 = skelG.Basis.Orthonormalized() * rt.Basis;
            // Facing reference: at rest (legsYaw = 0) the model faces world
            // -Z. Store that direction in torso-bone space so the rendered
            // torso yaw can be measured later regardless of bone frames.
            var w0 = skelG.Basis.Orthonormalized()
                * _skel.GetBoneGlobalPose(_torsoBone).Basis.Orthonormalized();
            _torsoFwdBoneLocal = w0.Transposed() * new Vector3(0f, 0f, -1f);
        }

        SetupLeg(_legL, "hip_L", "knee_L", "ankle_L", skelG, holderG, groundY);
        SetupLeg(_legR, "hip_R", "knee_R", "ankle_R", skelG, holderG, groundY);

        if (_cfg.FlipKneePole)
        {
            // Plantigrade flip (owner round 22): mirror ONLY the
            // pole's sagittal (forward/back) component across the
            // frontal plane — the lateral and vertical components
            // are preserved, so from the front the legs keep their
            // modeled stance and the bend shows only from the
            // side. (The first cut reflected across the leg axis,
            // which also mirrored the lateral component: the
            // knees bent forward AND toward each other.)
            var lateral = (_legR.Hrest - _legL.Hrest).Normalized();
            foreach (var leg in new[] { _legL, _legR })
            {
                var up = (leg.Hrest - leg.Arest).Normalized();
                var sag = up.Cross(lateral).Normalized(); // in the pole's plane, ⊥ leg axis and ⊥ lateral
                leg.PoleSk = (leg.PoleSk - 2f * leg.PoleSk.Dot(sag) * sag).Normalized();
                GD.Print($"{_cfg.Label} leg {leg.Name}: flipped pole={leg.PoleSk}");
            }
        }

        float reachW = 0.5f * ((_legL.L1 + _legL.L2) + (_legR.L1 + _legR.L2)) * skelG.Basis.X.Length();
        float horizHipAnkle = 0f;
        foreach (var leg in new[] { _legL, _legR })
        {
            var hipW = skelG * leg.Hrest;
            var ankW = skelG * leg.Arest;
            horizHipAnkle += new Vector2(ankW.X - hipW.X, ankW.Z - hipW.Z).Length();
        }
        horizHipAnkle /= 2f;
        float dStand = StandReachFactor * reachW;
        _hipRiseW = Mathf.Sqrt(Mathf.Max(dStand * dStand - horizHipAnkle * horizHipAnkle, 0.25f));
        var hipWL = skelG * _legL.Hrest;
        var hipWR = skelG * _legR.Hrest;
        _hipAboveHolderW = 0.5f * ((hipWL.Y - Position.Y) + (hipWR.Y - Position.Y));
        GD.Print($"{_cfg.Label} reachW={reachW:F3} dStand={dStand:F3} hipRiseW={_hipRiseW:F3} hipAboveHolderW={_hipAboveHolderW:F3}");
    }

    private void SetupLeg(Leg leg, string hipName, string kneeName, string ankleName,
        Transform3D skelG, Transform3D holderG, float groundY)
    {
        leg.Hip = _skel.FindBone(hipName);
        leg.Knee = _skel.FindBone(kneeName);
        leg.Ankle = _skel.FindBone(ankleName);
        var rh = _skel.GetBoneGlobalRest(leg.Hip);
        var rk = _skel.GetBoneGlobalRest(leg.Knee);
        var ra = _skel.GetBoneGlobalRest(leg.Ankle);
        leg.Hrest = rh.Origin; leg.Krest = rk.Origin; leg.Arest = ra.Origin;
        leg.HrestBasis = rh.Basis; leg.KrestBasis = rk.Basis;
        leg.L1 = (leg.Krest - leg.Hrest).Length();
        leg.L2 = (leg.Arest - leg.Krest).Length();
        leg.ThighRestDir = (leg.Krest - leg.Hrest).Normalized();
        leg.ShinRestDir = (leg.Arest - leg.Krest).Normalized();
        // Pole: rest knee offset from the hip->ankle line. Works for both
        // digitigrade (knee back) and plantigrade (knee forward) rigs.
        var ha = leg.Arest - leg.Hrest;
        var pole = (leg.Krest - leg.Hrest) - ha.Normalized() * (leg.Krest - leg.Hrest).Dot(ha.Normalized());
        leg.PoleSk = pole.Normalized();
        // The knee-pole flip itself runs in Init, once both legs
        // are known (it needs the lateral hip axis).
        var ankW = skelG * leg.Arest;
        var soleW = new Vector3(ankW.X, groundY, ankW.Z);
        var soleSk = skelG.AffineInverse() * soleW;
        leg.SoleOffLocal = ra.AffineInverse() * soleSk;
        leg.M0 = skelG.Basis.Orthonormalized() * ra.Basis;
        leg.SoleOffFrame = leg.M0 * leg.SoleOffLocal;
        leg.AnkleAboveSoleW = ankW.Y - groundY;
        var hipLocal = holderG.AffineInverse() * (skelG * leg.Hrest);
        leg.HomeLocal = new Vector2(hipLocal.X, hipLocal.Z);
        leg.SoleTarget = new Vector3(soleW.X, Main.HeightAt(soleW.X, soleW.Z), soleW.Z);
        leg.PlantBasisW = FootBasisAt(leg.SoleTarget, 0f);
        leg.CurBasisW = leg.PlantBasisW;
        leg.SolvedSoleWorld = leg.SoleTarget;
        GD.Print($"{_cfg.Label} leg {leg.Name}: L1={leg.L1:F2} L2={leg.L2:F2} pole={leg.PoleSk} homeLocal={leg.HomeLocal} plant={leg.SoleTarget}");
    }

    public void SetDrive(float legsYaw, float targetSpeed)
    {
        _legsYaw = legsYaw;
        _targetSpeed = targetSpeed;
    }

    // Hard placement for test choreography: feet re-plant at home slots.
    public void Teleport(Vector3 pos, float legsYaw)
    {
        _rootPos = pos;
        _legsYaw = legsYaw;
        _prevYaw = legsYaw;
        _speed = 0f;
        _jumpH = 0f; _jumpVel = 0f; _thrustHeld = false; Thrusting = false;
        _targetSpeed = 0f;
        _fallT = -1f;
        _fallAmt = 0f;
        _joltEnv = 0f;
        _sway = Vector3.Zero;
        _bob = 0f;
        _upEased = Vector3.Up;
        foreach (var leg in new[] { _legL, _legR })
        {
            leg.StepT = -1f;
            leg.Planted = true;
            var home = HomeSlot(leg, pos);
            leg.SoleTarget = new Vector3(home.X, Main.HeightAt(home.X, home.Z), home.Z);
            leg.PlantBasisW = FootBasisAt(leg.SoleTarget, legsYaw);
            leg.CurBasisW = leg.PlantBasisW;
            leg.HasPrev = false;
        }
        _groundY = DesiredRootY();
        _rootPos.Y = _groundY;
        RefreshPose();
    }

    // Collision push-out: move the root horizontally; planted feet stay.
    public void ApplyRootXZ(Vector2 xz)
    {
        var delta = new Vector3(xz.X - _rootPos.X, 0f, xz.Y - _rootPos.Z);
        if (delta.LengthSquared() < 1e-10f) return;
        _rootPos += delta;
        _holderXf = new Transform3D(_holderXf.Basis, _holderXf.Origin + delta);
        Transform = _holderXf;
        _skelGlobal = _holderXf * _chain;
        SolveAll();
    }

    public void StepFrame(double delta)
    {
        if (!_ready) return;
        DecayFlash((float)delta);
        if (Halted) { SolveAll(); return; }
        if (_fallT >= 0f)
        {
            _fallT += (float)delta;
            float t = _fallT;
            if (t < 0.5f) { float u = t / 0.5f; _fallAmt = u * u; }
            else _fallAmt = 1f; // down for good — no rise, ever
        }
        _acc += Mathf.Min(delta, 0.1);
        const float h = 1f / 120f;
        int guard = 0;
        while (_acc >= h && guard++ < 40)
        {
            GaitStep(h);
            _acc -= h;
        }
    }

    public bool ConsumeStepMidCross()
    {
        bool r = StepMidCrossed;
        StepMidCrossed = false;
        return r;
    }

    public bool AnySwinging => _legL.StepT >= 0f || _legR.StepT >= 0f;

    // ---------- gait ----------

    private static Vector3 TerrainNormal(float x, float z)
    {
        const float e = 0.7f;
        float dhdx = (Main.HeightAt(x + e, z) - Main.HeightAt(x - e, z)) / (2f * e);
        float dhdz = (Main.HeightAt(x, z + e) - Main.HeightAt(x, z - e)) / (2f * e);
        return new Vector3(-dhdx, 1f, -dhdz).Normalized();
    }

    private static Basis FootBasisAt(Vector3 sole, float yaw)
    {
        var n = TerrainNormal(sole.X, sole.Z);
        var back = HeadingVec(yaw) * -1f;
        var zA = (back - n * back.Dot(n)).Normalized();
        var xA = n.Cross(zA);
        return new Basis(xA, n, zA);
    }

    private Vector3 HomeSlot(Leg leg, Vector3 rootXZ)
    {
        var off = _rootBasis * new Vector3(leg.HomeLocal.X * _scaleF, 0f, leg.HomeLocal.Y * _scaleF);
        return new Vector3(rootXZ.X + off.X, 0f, rootXZ.Z + off.Z);
    }

    private Leg Other(Leg leg) => leg == _legL ? _legR : _legL;

    private void BeginStep(Leg leg)
    {
        var vel = HeadingVec(_legsYaw) * _speed;
        var predRoot = _rootPos + vel * (_cfg.StepTime + 0.10f);
        var home = HomeSlot(leg, predRoot) + vel * 0.30f;
        leg.StepFrom = leg.SoleTarget;
        leg.StepTo = new Vector3(home.X, Main.HeightAt(home.X, home.Z), home.Z);
        leg.StepBasisFrom = leg.CurBasisW;
        leg.StepBasisTo = FootBasisAt(leg.StepTo, _legsYaw);
        leg.StepT = 0f;
        leg.Planted = false;
    }

    private void AdvanceLeg(Leg leg, float h)
    {
        if (leg.StepT < 0f) return;
        float prev = leg.StepT;
        leg.StepT += h / _cfg.StepTime;
        float t = Mathf.Clamp(leg.StepT, 0f, 1f);
        float e = t * t * (3f - 2f * t);
        var pos = leg.StepFrom.Lerp(leg.StepTo, e);
        pos.Y += Mathf.Sin(Mathf.Pi * t) * _cfg.StepLift;
        leg.SoleTarget = pos;
        var qa = leg.StepBasisFrom.GetRotationQuaternion();
        var qb = leg.StepBasisTo.GetRotationQuaternion();
        leg.CurBasisW = new Basis(qa.Slerp(qb, e));
        if (prev < 0.5f && leg.StepT >= 0.5f) StepMidCrossed = true;
        if (leg.StepT >= 1f)
        {
            leg.StepT = -1f;
            leg.Planted = true;
            leg.SoleTarget = leg.StepTo;
            leg.PlantBasisW = leg.StepBasisTo;
            leg.CurBasisW = leg.StepBasisTo;
            _steps++;
        }
    }

    private void GaitStep(float h)
    {
        // Thruster flight, integrated per substep like the gait:
        // burn climbs and drains, otherwise gravity rules.
        Thrusting = _thrustHeld && JumpJets && JumpCharge > 0f && _fallT < 0f;
        if (Thrusting)
        {
            JumpCharge = Mathf.Max(0f, JumpCharge - h / 2.2f);
            _jumpVel = Mathf.Min(_jumpVel + 34f * h, 10.5f);
        }
        else if (_jumpH > 0f)
        {
            _jumpVel = Mathf.Max(_jumpVel - 24f * h, -18f);
        }
        else _jumpVel = 0f;
        if (_jumpH > 0f || _jumpVel > 0f)
        {
            _jumpH += _jumpVel * h;
            if (_jumpH <= 0f) { _jumpH = 0f; _jumpVel = 0f; }
        }
        if (!_thrustHeld && JumpCharge < 1f)
            JumpCharge = Mathf.Min(1f, JumpCharge + h / 5f); // recharge is gated on the KEY, not on thrust: it starts the moment SPACE is released — ground or air (owner round 22) — and a held key on empty tanks stays dead instead of sputtering
        _yawRate = Mathf.Abs(WrapAngle(_legsYaw - _prevYaw)) / h;
        _prevYaw = _legsYaw;

        float dv = _cfg.Accel * h;
        _speed = Mathf.MoveToward(_speed, _fallT >= 0f ? 0f : _targetSpeed * SpeedFactor, dv);
        var vel = HeadingVec(_legsYaw) * _speed;
        _rootPos += vel * h;

        var stepping = _legL.StepT >= 0f ? _legL : _legR.StepT >= 0f ? _legR : null;
        if (stepping == null && _fallT < 0f)
        {
            bool settling = Mathf.Abs(_targetSpeed) < 0.01f && Mathf.Abs(_speed) < 0.15f && _yawRate < 0.05f;
            float threshold = settling ? 0.10f : _cfg.StepThreshold;
            Leg best = null; float bestLag = threshold;
            foreach (var leg in new[] { _legL, _legR })
            {
                if (!leg.Planted || !Other(leg).Planted) continue;
                var home = HomeSlot(leg, _rootPos + vel * 0.30f);
                float lag = new Vector2(leg.SoleTarget.X - home.X, leg.SoleTarget.Z - home.Z).Length();
                if (lag > bestLag) { bestLag = lag; best = leg; }
            }
            if (best != null) BeginStep(best);
        }
        AdvanceLeg(_legL, h);
        AdvanceLeg(_legR, h);
        // A wrecked leg steps like any other (see the state comment
        // above): no drag override — its stride shortens naturally
        // with the mech's throttled speed.

        UpdateRoot(h);
        SolveAll();
        TrackSlide();
    }

    private float DesiredRootY()
    {
        float refL = (_legL.Planted ? _legL.SoleTarget.Y : _legL.StepTo.Y) + _legL.AnkleAboveSoleW;
        float refR = (_legR.Planted ? _legR.SoleTarget.Y : _legR.StepTo.Y) + _legR.AnkleAboveSoleW;
        return 0.5f * (refL + refR) + _hipRiseW - _hipAboveHolderW + _bob;
    }

    private void UpdateRoot(float h)
    {
        bool doubleSupport = _legL.Planted && _legR.Planted;
        float bobT = doubleSupport ? -0.09f : -0.02f;
        _bob = Mathf.Lerp(_bob, bobT, 1f - Mathf.Exp(-6f * h));
        float yT = DesiredRootY();
        // Knocked down: the hips drop to rest height (body on the ground).
        if (_fallAmt > 0.001f)
        {
            float restY = Mathf.Min(_legL.SoleTarget.Y, _legR.SoleTarget.Y) + 2.0f;
            yT = Mathf.Lerp(yT, restY, _fallAmt);
        }
        _groundY = Mathf.Lerp(_groundY, yT, 1f - Mathf.Exp(-8f * h));
        _rootPos.Y = _groundY + _jumpH; // the arc itself is NOT smoothed — full ballistic height

        var swayT = Vector3.Zero;
        if (_legL.StepT >= 0f) swayT = _rootBasis * new Vector3(_legR.HomeLocal.X * _scaleF, 0f, 0f) * 0.22f;
        else if (_legR.StepT >= 0f) swayT = _rootBasis * new Vector3(_legL.HomeLocal.X * _scaleF, 0f, 0f) * 0.22f;
        _sway = _sway.Lerp(swayT, 1f - Mathf.Exp(-5f * h));

        // Up vector: roll from the two feet heights across the lateral axis,
        // pitch from the terrain gradient along the heading.
        var sL = _legL.Planted ? _legL.SoleTarget : _legL.StepTo;
        var sR = _legR.Planted ? _legR.SoleTarget : _legR.StepTo;
        var right = new Vector3(Mathf.Cos(_legsYaw), 0f, -Mathf.Sin(_legsYaw));
        float latL = (sL - _rootPos).Dot(right);
        float latR = (sR - _rootPos).Dot(right);
        var sHi = latR > latL ? sR : sL;
        var sLo = latR > latL ? sL : sR;
        float span = Mathf.Max(Mathf.Abs(latR - latL), 0.5f);
        float dhLat = (sHi.Y - sLo.Y) / span;
        var fwd = HeadingVec(_legsYaw);
        const float ge = 1.5f;
        float dhFwd = (Main.HeightAt(_rootPos.X + fwd.X * ge, _rootPos.Z + fwd.Z * ge)
                     - Main.HeightAt(_rootPos.X - fwd.X * ge, _rootPos.Z - fwd.Z * ge)) / (2f * ge);
        var upT = (Vector3.Up - right * dhLat - fwd * dhFwd).Normalized();
        float tilt = Mathf.Acos(Mathf.Clamp(upT.Dot(Vector3.Up), -1f, 1f));
        if (tilt > 0.23f) upT = Vector3.Up.Lerp(upT, 0.23f / tilt).Normalized();
        // Limp: permanent lean toward a dead leg, on top of terrain tilt.
        // upT - right*sin leans the body's top toward its anatomical left.
        if (LimpRoll != 0f)
            upT = (upT - right * Mathf.Sin(LimpRoll)).Normalized();
        // Knockdown: the up vector tips toward the fall direction by up
        // to ~76 degrees, scaled by the fall amount.
        if (_fallAmt > 0.001f)
        {
            float ang = 1.32f * _fallAmt;
            var fallUp = (Vector3.Up * Mathf.Cos(ang) + _fallDirW * Mathf.Sin(ang)).Normalized();
            upT = (upT * (1f - _fallAmt) + fallUp * _fallAmt).Normalized();
        }
        _upEased = _upEased.Lerp(upT, 1f - Mathf.Exp(-(_fallAmt > 0.001f ? 10f : 3.5f) * h)).Normalized();

        var q = new Quaternion(Vector3.Up, _upEased);
        _rootBasis = new Basis(q) * Basis.Identity.Rotated(Vector3.Up, _legsYaw + Mathf.Pi);
        if (_joltEnv > 0.002f)
        {
            _rootBasis = _rootBasis
                * Basis.Identity.Rotated(Vector3.Right, _joltPitch * _joltEnv)
                * Basis.Identity.Rotated(Vector3.Forward, _joltRoll * _joltEnv);
            _joltEnv = Mathf.Max(_joltEnv - h * 2.4f, 0f);
        }

        RefreshPose();
    }

    private void RefreshPose()
    {
        var pos = _rootPos + _sway;
        _holderXf = new Transform3D(_rootBasis.Scaled(Vector3.One * _scaleF), pos);
        Transform = _holderXf;
        _skelGlobal = _holderXf * _chain;
        SolveAll();
    }

    // ---------- IK ----------

    private void SolveAll()
    {
        SolveLeg(_legL);
        SolveLeg(_legR);
        SolveTorso();
    }

    private void SolveTorso()
    {
        if (_torsoBone < 0 || _skel == null) return;
        // Desired torso world frame: the root frame, twisted by torso
        // yaw/pitch. The trailing Yaw(Pi) undoes the model's rest flip:
        // at twist = 0 this must reproduce the rest pose exactly
        // (root * Yaw(Pi)^-1 * M0 == M0 when legsYaw = 0), otherwise the
        // torso renders 180 degrees off the legs.
        // Pitch sign: the trailing Yaw(Pi) rest-flip conjugates the
        // X rotation, so feeding +pitch here rendered the torso nose-
        // DOWN while the aim model treats +pitch as up — mouse up
        // visibly dipped the mech (owner's report). Feed (Droop -
        // pitch): rendered pitch = pitch - Droop, so +pitch points
        // the torso UP (-18/+27 deg working range) and the death
        // droop still slumps it nose-down, up to ~49 deg.
        var f = _rootBasis.Orthonormalized()
            * Basis.Identity.Rotated(Vector3.Up, _torsoTwist)
            * Basis.Identity.Rotated(Vector3.Right, Mathf.Clamp(Droop - _torsoPitch, -0.85f, 0.32f))
            * Basis.Identity.Rotated(Vector3.Up, Mathf.Pi);
        var basisSk = _skelGlobal.Basis.Orthonormalized().Transposed() * (f * _torsoM0);
        _skel.SetBoneGlobalPoseOverride(_torsoBone,
            new Transform3D(basisSk, _torsoRestOrigin), 1f, true);
    }

    // World yaw of the torso as the engine renders it (verification):
    // track the rest facing direction through the bone's actual world basis.
    public float EngineTorsoYaw()
    {
        if (_torsoBone < 0 || _skel == null) return TorsoYawWorld;
        var f = EngineTorsoFacing();
        return YawOf(new Vector3(f.X, 0f, f.Z).Normalized());
    }

    // The torso's facing as the engine actually renders it, Y
    // included — the ground truth for pitch direction checks.
    public Vector3 EngineTorsoFacing()
    {
        if (_torsoBone < 0 || _skel == null) return Vector3.Forward;
        var pose = _skel.GetBoneGlobalPose(_torsoBone);
        var w = _skel.GlobalTransform.Basis.Orthonormalized() * pose.Basis.Orthonormalized();
        return (w * _torsoFwdBoneLocal).Normalized();
    }

    private void SolveLeg(Leg leg)
    {
        var sole = leg.SoleTarget;
        var basisW = leg.Planted ? leg.PlantBasisW : leg.CurBasisW;
        float st = _skelGlobal.Basis.X.Length();
        var ankleWorld = sole - st * (basisW * leg.SoleOffFrame);
        var inv = _skelGlobal.AffineInverse();
        var A = inv * ankleWorld;
        var H = leg.Hrest;
        var toA = A - H;
        float reach = leg.L1 + leg.L2;
        float d = Mathf.Clamp(toA.Length(), reach * 0.05f, reach * 0.999f);
        var dir = toA.Normalized();
        A = H + dir * d;
        float a = (leg.L1 * leg.L1 - leg.L2 * leg.L2 + d * d) / (2f * d);
        float hh = Mathf.Sqrt(Mathf.Max(leg.L1 * leg.L1 - a * a, 0f));
        var pole = leg.PoleSk - dir * leg.PoleSk.Dot(dir);
        if (pole.LengthSquared() < 1e-6f) pole = dir.Cross(Vector3.Right).Normalized();
        else pole = pole.Normalized();
        var K = H + dir * a + pole * hh;

        var qHip = new Quaternion(leg.ThighRestDir, (K - H).Normalized());
        _skel.SetBoneGlobalPoseOverride(leg.Hip, new Transform3D(new Basis(qHip) * leg.HrestBasis, H), 1f, true);
        var qKnee = new Quaternion(leg.ShinRestDir, (A - K).Normalized());
        _skel.SetBoneGlobalPoseOverride(leg.Knee, new Transform3D(new Basis(qKnee) * leg.KrestBasis, K), 1f, true);
        var ankleBasisSk = _skelGlobal.Basis.Orthonormalized().Transposed() * (basisW * leg.M0);
        leg.SolvedAnklePose = new Transform3D(ankleBasisSk, A);
        _skel.SetBoneGlobalPoseOverride(leg.Ankle, leg.SolvedAnklePose, 1f, true);
        leg.SolvedSoleWorld = _skelGlobal * (leg.SolvedAnklePose * leg.SoleOffLocal);
    }

    private void TrackSlide()
    {
        foreach (var leg in new[] { _legL, _legR })
        {
            if (leg.Planted)
            {
                var soleW = _skelGlobal * (leg.SolvedAnklePose * leg.SoleOffLocal);
                leg.LastPlantedDiff = soleW.Y - Main.HeightAt(soleW.X, soleW.Z);
            }
            // A wrecked leg still plants and steps; keep it exempt
            // from slide scoring while the limp lean settles.
            if (!leg.Disabled && leg.Planted && leg.HasPrev)
            {
                float drift = new Vector2(leg.SolvedSoleWorld.X - leg.PrevSoleWorld.X,
                                           leg.SolvedSoleWorld.Z - leg.PrevSoleWorld.Z).Length();
                if (drift > _maxSlide) _maxSlide = drift;
                _plantedFrames++;
            }
            leg.PrevSoleWorld = leg.SolvedSoleWorld;
            leg.HasPrev = leg.Planted && !leg.Disabled;
        }
    }

    public string FootReport(bool engine)
    {
        string One(Leg leg)
        {
            Vector3 sole;
            if (engine)
            {
                var pose = _skel.GetBoneGlobalPose(leg.Ankle);
                sole = _skel.GlobalTransform * (pose * leg.SoleOffLocal);
            }
            else sole = leg.SolvedSoleWorld;
            float terr = Main.HeightAt(sole.X, sole.Z);
            float diff = sole.Y - terr;
            return leg.Planted
                ? $"{leg.Name}: PLANTED soleY={sole.Y:F3} terrY={terr:F3} diff={diff:+0.000;-0.000}"
                : $"{leg.Name}: SWING soleY={sole.Y:F3} terrY={terr:F3} clearance={diff:F3}";
        }
        return $"{One(_legL)} | {One(_legR)}";
    }

    public float MaxPlantedDiff()
    {
        float m = 0f;
        foreach (var leg in new[] { _legL, _legR })
            if (leg.Planted) m = Mathf.Max(m, Mathf.Abs(leg.LastPlantedDiff));
        return m;
    }

    // ---------- weapon feedback (hit flash / death darkening) ----------

    private readonly System.Collections.Generic.List<(StandardMaterial3D Mat, Color Base, MeshInstance3D Mesh)> _mats = new();
    private readonly System.Collections.Generic.List<StandardMaterial3D> _flashMats = new();
    private float _flashT;

    // Materials are duplicated per walker instance at Init so a flash or
    // darkening on one mech never leaks into the other (the FBX mesh
    // resources themselves are shared).
    private void CollectMaterials() => CollectMats(_inner);

    private void CollectMats(Node n)
    {
        if (n is MeshInstance3D mi && mi.Mesh != null)
        {
            int count = mi.Mesh.GetSurfaceCount();
            for (int i = 0; i < count; i++)
            {
                var m = mi.GetSurfaceOverrideMaterial(i) ?? mi.Mesh.SurfaceGetMaterial(i);
                if (m is StandardMaterial3D sm)
                {
                    var dup = (StandardMaterial3D)sm.Duplicate();
                    mi.SetSurfaceOverrideMaterial(i, dup);
                    _mats.Add((dup, dup.AlbedoColor, mi));
                }
            }
        }
        foreach (var c in n.GetChildren()) CollectMats(c);
    }

    public Color LastFlashColor { get; private set; } = new Color(1f, 0.55f, 0.2f);
    public float LastFlashEnergy { get; private set; } = 2.4f;
    private float _flashEnergy = 2.4f;

    public void FlashPulse() => FlashPulse(new Color(1f, 0.55f, 0.2f), 2.4f);

    // Round 8: the hit flash carries the WEAPON's color and strength —
    // a laser sting is cyan and brief-feeling, a cannon hit is a warm
    // white slam, a rocket is deep orange. Before this every hit
    // blinked the same orange (owner's report).
    // Round 20 (owner): the flash is LOCAL to the hit — only meshes
    // whose bind-pose AABB center sits near the impact point glow.
    // The whole-mech white-out is gone; a leg hit lights the leg, a
    // shoulder hit lights the arm/pod area.
    public void FlashPulse(Color col, float energy) => FlashPulse(col, energy, Vector3.Zero, false);

    public void FlashPulse(Color col, float energy, Vector3 worldPoint) => FlashPulse(col, energy, worldPoint, true);

    private void FlashPulse(Color col, float energy, Vector3 worldPoint, bool local)
    {
        _flashT = 1f;
        _flashEnergy = energy;
        LastFlashColor = col;
        LastFlashEnergy = energy;
        foreach (var m in _flashMats) m.EmissionEnergyMultiplier = 0f;
        _flashMats.Clear();
        Vector3 lp = local ? _inner.ToLocal(worldPoint) : Vector3.Zero;
        float best = float.MaxValue;
        StandardMaterial3D bestMat = null;
        foreach (var (m, _, mesh) in _mats)
        {
            float d = local ? mesh.GetAabb().GetCenter().DistanceTo(lp) : 0f;
            if (d < best) { best = d; bestMat = m; }
            if (local && d > 4.6f) continue;
            m.EmissionEnabled = true;
            m.Emission = col;
            m.EmissionEnergyMultiplier = energy;
            _flashMats.Add(m);
        }
        if (_flashMats.Count == 0 && bestMat != null)
        {
            bestMat.EmissionEnabled = true;
            bestMat.Emission = col;
            bestMat.EmissionEnergyMultiplier = energy;
            _flashMats.Add(bestMat);
        }
    }

    // Impact jolt: the body rocks under a hit and eases back over
    // ~0.4s. Pitch/roll are the push direction decomposed against the
    // legs' facing at the moment of the hit.
    private float _joltPitch, _joltRoll, _joltEnv;
    public float JoltEnv => _joltEnv;
    public void Jolt(Vector3 worldPushDir, float strength)
    {
        if (worldPushDir.LengthSquared() < 0.01f || strength <= 0f) return;
        var fwd = HeadingVec(_legsYaw);
        var right = new Vector3(-fwd.Z, 0f, fwd.X);
        _joltPitch = -worldPushDir.Dot(fwd) * strength * 0.055f;
        _joltRoll = worldPushDir.Dot(right) * strength * 0.055f;
        _joltEnv = 1f;
    }

    public void SetDarkened(bool dark)
    {
        foreach (var (m, b, _) in _mats)
            m.AlbedoColor = dark ? b * 0.5f : b;
    }

    private void DecayFlash(float dt)
    {
        if (_flashT <= 0f) return;
        _flashT = Mathf.Max(_flashT - dt / 0.18f, 0f);
        foreach (var m in _flashMats)
            m.EmissionEnergyMultiplier = _flashEnergy * _flashT;
    }

    // ---------- helpers ----------

    private static Skeleton3D FindSkeleton(Node n)
    {
        if (n is Skeleton3D s) return s;
        foreach (var c in n.GetChildren())
        {
            var r = FindSkeleton(c);
            if (r != null) return r;
        }
        return null;
    }

    private static Aabb MeasureWorldAabb(Node node)
    {
        var box = new Aabb();
        bool first = true;
        Collect(node, ref box, ref first);
        return box;
    }

    private static void Collect(Node node, ref Aabb box, ref bool first)
    {
        if (node is MeshInstance3D mi)
        {
            var local = mi.GetAabb();
            var gt = mi.GlobalTransform;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? local.Position.X : local.End.X,
                    (i & 2) == 0 ? local.Position.Y : local.End.Y,
                    (i & 4) == 0 ? local.Position.Z : local.End.Z);
                var w = gt * corner;
                if (first) { box = new Aabb(w, Vector3.Zero); first = false; }
                else box = box.Expand(w);
            }
        }
        foreach (var child in node.GetChildren()) Collect(child, ref box, ref first);
    }
}
