using Godot;

// TimberwolfTest: the user's STL print kit (53 STLs, variant kit by BobertmaX)
// assembled + rigged in Blender (~/workspace/timberwolf/build_timberwolf.py) and
// imported here as the player character on the round-1 terrain.
// Config: Torso_Opt2 (LRM-20 missile ears, matches the user's printed photo),
// Hand_Opt5 forearm cannons + ERPPCShort barrels, Leftleg_Lower_Opt2; the kit
// ships left side only, right side is mirrored in Blender.
//
// This version replaces the original root-slide "walk" (frozen bind pose, feet
// burying/floating on slopes) with a REAL procedural gait:
//   - Gait state machine: each foot has a planted target in WORLD space. A leg
//     steps when its foot lags its home slot past a threshold while the other
//     leg is planted; the swinging foot arcs to a target whose Y is sampled
//     from Main.HeightAt at the FOOT's own (x,z) -- never the root's height.
//   - Two-bone IK per leg every frame (analytic, skeleton space): hip/knee
//     solved by law of cosines with the knee pole taken from the REST pose
//     (digitigrade: the knee bends backward), written as global pose
//     overrides; the ankle override keeps the foot flat on the local slope.
//   - Root Y is derived from the feet (mean ankle height + hip rise for
//     0.90x leg reach), with a double-support bob, sway toward the planted
//     leg, and pitch/roll eased toward the slope implied by the feet.
// Verification (--timberwolf-test): per-foot contact numbers at every capture
// (planted sole Y vs HeightAt at the sole's own XZ, analytic AND read back
// from the engine's rendered skeleton), planted-foot slide tracking,
// a 4-frame stride sequence on a slope + a crest frame, stand regression.
public partial class TimberwolfTest : Node3D
{
    private const float WalkSpeed = 2.4f;
    private const float TargetHeight = 13.0f;
    private const float StepTime = 0.52f;
    private const float StepThreshold = 1.55f;
    private const float StepLift = 0.85f;
    private const float StandReachFactor = 0.90f; // hip-ankle dist / max reach
    private const float Accel = 3.2f;

    private sealed class Leg
    {
        public string Name;
        public int Hip, Knee, Ankle;
        public Vector3 Hrest, Krest, Arest;       // skeleton-space rest origins
        public Basis HrestBasis, KrestBasis;      // skeleton-space rest bases
        public float L1, L2;                      // segment lengths (skeleton units)
        public Vector3 ThighRestDir, ShinRestDir; // skeleton space, normalized
        public Vector3 PoleSk;                    // knee pole dir, skeleton space
        public Vector3 SoleOffLocal;              // sole point in ankle-bone space
        public Basis M0;                          // ankle rest world rotation (foot frame -> bone)
        public Vector3 SoleOffFrame;              // sole offset in world foot frame (= M0 * SoleOffLocal)
        public Vector2 HomeLocal;                 // hip offset in holder space (raw units, x/z)
        public float AnkleAboveSoleW;             // world units
        // gait state
        public bool Planted = true;
        public Vector3 SoleTarget;                // current desired sole (world)
        public Basis PlantBasisW;                 // foot world basis while planted
        public Basis CurBasisW;                   // foot world basis this frame
        public Vector3 StepFrom, StepTo;
        public Basis StepBasisFrom, StepBasisTo;
        public float StepT = -1f;                 // <0 = planted
        public Transform3D SolvedAnklePose;       // skeleton space, this frame
        public Vector3 SolvedSoleWorld;           // world, this frame
        public Vector3 PrevSoleWorld;
        public bool HasPrev;
    }

    private Node3D _holder;
    private Node3D _inner;
    private Skeleton3D _skel;
    private Camera3D _cam;
    private bool _test;
    private float _scaleF = 1f;
    private Transform3D _chain;        // holder -> skeleton, constant
    private Transform3D _holderXf;     // analytic holder global transform
    private Transform3D _skelGlobal;   // analytic skeleton global transform
    private Vector3 _startPos;

    private readonly Leg _legL = new Leg { Name = "L" };
    private readonly Leg _legR = new Leg { Name = "R" };

    // gait / root state
    private Vector3 _rootPos;
    private Basis _rootBasis;
    private Vector3 _upEased = Vector3.Up;
    private float _speed;
    private float _targetSpeed;
    private float _bob;
    private Vector3 _sway = Vector3.Zero;
    private double _simT;
    private double _acc;
    private int _phase; // 0 idle, 1 walking, 2 stopping, 3 done (test choreography)
    private int _steps;
    private float _maxSlide;
    private int _plantedFrames;

    // shared world-space leg constants (from rest snapshot)
    private float _hipRiseW;       // hip height above ankle at stand distance
    private float _hipAboveHolderW;

    // capture state
    private bool _frozen, _capturing;
    private string _pendingPath, _pendingTag;
    private string _camMode = "classic";
    private bool _shotStand;
    private int _gaitShots;
    private bool _crestPending, _crestShot;
    private bool _shotFinal;

    private static readonly Basis YawBasis = new Basis(Vector3.Up, Mathf.Pi);
    private static readonly Vector3 Heading = new Vector3(0f, 0f, -1f);

    public override void _Ready()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg == "--timberwolf-test") _test = true;

        var sun = new DirectionalLight3D { RotationDegrees = new Vector3(-50f, -30f, 0f) };
        AddChild(sun);
        var we = new WorldEnvironment
        {
            Environment = new Environment
            {
                BackgroundMode = Environment.BGMode.Color,
                BackgroundColor = new Color(0.35f, 0.5f, 0.7f)
            }
        };
        AddChild(we);

        BuildTerrain();
        AddTree(new Vector3(-13f, 0f, 4f));
        AddTree(new Vector3(11f, 0f, -2f));
        AddBuilding(new Vector3(18f, 0f, 18f), new Vector3(6f, 7f, 6f));

        var packed = GD.Load<PackedScene>("res://assets/timberwolf.fbx");
        if (packed == null) { GD.PrintErr("TW FAIL: timberwolf.fbx did not load as PackedScene"); GetTree().Quit(1); return; }
        _inner = packed.Instantiate<Node3D>();

        _holder = new Node3D { Name = "MechHolder" };
        AddChild(_holder);
        _holder.AddChild(_inner);

        var raw = MeasureWorldAabb(_inner);
        GD.Print($"TW raw AABB pos={raw.Position} size={raw.Size}");
        float s = TargetHeight / raw.Size.Y;
        _scaleF = s;
        _holder.Scale = Vector3.One * s;
        var a = MeasureWorldAabb(_inner);
        var desiredMin = new Vector3(-a.Size.X / 2f, 0f, -a.Size.Z / 2f);
        _inner.Position += (desiredMin - a.Position) / s;
        var chk = MeasureWorldAabb(_inner);
        GD.Print($"TW normalized AABB pos={chk.Position} size={chk.Size} scale={s}");

        // Blender FBX: model front faces +Z after import; face -Z (walk dir).
        _holder.Rotation = new Vector3(0f, Mathf.Pi, 0f);

        _skel = FindSkeleton(_inner);
        if (_skel == null) { GD.PrintErr("TW FAIL: no Skeleton3D in timberwolf.fbx"); GetTree().Quit(1); return; }
        var names = new string[_skel.GetBoneCount()];
        for (int i = 0; i < names.Length; i++) names[i] = _skel.GetBoneName(i);
        GD.Print($"TW skeleton bones={names.Length}: {string.Join(",", names)}");

        _startPos = new Vector3(0f, Main.HeightAt(0f, 16f), 16f);
        _holder.Position = _startPos;
        GD.Print($"TW spawn pos={_holder.Position} terrainH={Main.HeightAt(0f, 16f)}");

        _cam = new Camera3D();
        AddChild(_cam);

        InitLegs();
        if (_legL.Hip < 0 || _legR.Hip < 0) { GD.PrintErr("TW FAIL: leg bones not found"); GetTree().Quit(1); return; }

        _rootPos = _startPos;
        _rootBasis = YawBasis;
        _holderXf = _holder.GlobalTransform;
        _skelGlobal = _holderXf * _chain;
    }

    private void InitLegs()
    {
        // Snapshot transforms at rest (holder at spawn, bind pose).
        var holderG = _holder.GlobalTransform;
        var skelG = _skel.GlobalTransform;
        _chain = holderG.AffineInverse() * skelG;
        var worldAabb = MeasureWorldAabb(_inner);
        float groundY = worldAabb.Position.Y;

        SetupLeg(_legL, "hip_L", "knee_L", "ankle_L", skelG, holderG, groundY);
        SetupLeg(_legR, "hip_R", "knee_R", "ankle_R", skelG, holderG, groundY);

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
        _hipAboveHolderW = 0.5f * ((hipWL.Y - _holder.Position.Y) + (hipWR.Y - _holder.Position.Y));
        GD.Print($"TW leg reachW={reachW:F3} dStand={dStand:F3} hipRiseW={_hipRiseW:F3} hipAboveHolderW={_hipAboveHolderW:F3} horizHipAnkle={horizHipAnkle:F3} groundY={groundY:F3}");
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
        // Pole: rest knee offset from the hip->ankle line (digitigrade-safe).
        var ha = leg.Arest - leg.Hrest;
        var pole = (leg.Krest - leg.Hrest) - ha.Normalized() * (leg.Krest - leg.Hrest).Dot(ha.Normalized());
        leg.PoleSk = pole.Normalized();
        // Sole point: directly under the ankle at the model's rest ground level.
        var ankW = skelG * leg.Arest;
        var soleW = new Vector3(ankW.X, groundY, ankW.Z);
        var soleSk = skelG.AffineInverse() * soleW;
        leg.SoleOffLocal = ra.AffineInverse() * soleSk;
        // The ankle bone's axes are rotated vs the foot's visual frame: the
        // bone basis that renders the foot flat is its REST world rotation M0,
        // not the terrain frame itself. Desired bone world rotation is
        // terrainFrame * M0; the sole offset in the world foot frame is
        // M0 * SoleOffLocal (constant).
        leg.M0 = skelG.Basis.Orthonormalized() * ra.Basis;
        leg.SoleOffFrame = leg.M0 * leg.SoleOffLocal;
        leg.AnkleAboveSoleW = ankW.Y - groundY;
        var hipLocal = holderG.AffineInverse() * (skelG * leg.Hrest);
        leg.HomeLocal = new Vector2(hipLocal.X, hipLocal.Z);
        // Initial plant: rest sole XZ, snapped to the terrain under that foot.
        leg.SoleTarget = new Vector3(soleW.X, Main.HeightAt(soleW.X, soleW.Z), soleW.Z);
        leg.PlantBasisW = FootBasisAt(leg.SoleTarget);
        leg.CurBasisW = leg.PlantBasisW;
        leg.SolvedSoleWorld = leg.SoleTarget;
        GD.Print($"TW leg {leg.Name}: hip={leg.Hip} knee={leg.Knee} ankle={leg.Ankle} L1={leg.L1:F2} L2={leg.L2:F2} pole={leg.PoleSk} soleOff={leg.SoleOffLocal} homeLocal={leg.HomeLocal} ankleAboveSoleW={leg.AnkleAboveSoleW:F3} plant={leg.SoleTarget}");
    }

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

    private void BuildTerrain()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetMaterial(new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.5f, 0.22f) });
        float half = Main.TerrainSize / 2f;
        float step = (float)Main.TerrainSize / Main.TerrainSegs;
        for (int j = 0; j < Main.TerrainSegs; j++)
        for (int i = 0; i < Main.TerrainSegs; i++)
        {
            float x0 = -half + i * step, x1 = x0 + step;
            float z0 = -half + j * step, z1 = z0 + step;
            var a = new Vector3(x0, Main.HeightAt(x0, z0), z0);
            var b = new Vector3(x1, Main.HeightAt(x1, z0), z0);
            var c = new Vector3(x0, Main.HeightAt(x0, z1), z1);
            var d = new Vector3(x1, Main.HeightAt(x1, z1), z1);
            st.AddVertex(a); st.AddVertex(b); st.AddVertex(c);
            st.AddVertex(b); st.AddVertex(d); st.AddVertex(c);
        }
        st.GenerateNormals();
        AddChild(new MeshInstance3D { Mesh = st.Commit() });
    }

    private void AddTree(Vector3 pos)
    {
        pos.Y = Main.HeightAt(pos.X, pos.Z);
        var tree = new Node3D { Position = pos };
        var trunk = new MeshInstance3D();
        trunk.Mesh = new CylinderMesh { TopRadius = 0.22f, BottomRadius = 0.3f, Height = 2.2f };
        trunk.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.4f, 0.26f, 0.13f) };
        trunk.Position = new Vector3(0f, 1.1f, 0f);
        var canopy = new MeshInstance3D();
        canopy.Mesh = new SphereMesh { Radius = 1.7f, Height = 3.4f };
        canopy.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.13f, 0.45f, 0.16f) };
        canopy.Position = new Vector3(0f, 3.6f, 0f);
        tree.AddChild(trunk);
        tree.AddChild(canopy);
        AddChild(tree);
    }

    private void AddBuilding(Vector3 pos, Vector3 size)
    {
        pos.Y = Main.HeightAt(pos.X, pos.Z) + size.Y / 2f - 0.3f;
        var mi = new MeshInstance3D();
        mi.Mesh = new BoxMesh { Size = size };
        mi.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.55f, 0.58f) };
        mi.Position = pos;
        AddChild(mi);
    }

    // ---------- gait ----------

    private static Vector3 TerrainNormal(float x, float z)
    {
        const float e = 0.7f;
        float dhdx = (Main.HeightAt(x + e, z) - Main.HeightAt(x - e, z)) / (2f * e);
        float dhdz = (Main.HeightAt(x, z + e) - Main.HeightAt(x, z - e)) / (2f * e);
        return new Vector3(-dhdx, 1f, -dhdz).Normalized();
    }

    // Foot world basis: sole aligned to the terrain slope at the foot, toes
    // pointing along the (fixed) heading.
    private static Basis FootBasisAt(Vector3 sole)
    {
        var n = TerrainNormal(sole.X, sole.Z);
        var back = new Vector3(0f, 0f, 1f); // heading is -Z, so back is +Z
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
        var vel = Heading * _speed;
        var predRoot = _rootPos + vel * (StepTime + 0.10f);
        var home = HomeSlot(leg, predRoot) + vel * 0.30f;
        leg.StepFrom = leg.SoleTarget;
        leg.StepTo = new Vector3(home.X, Main.HeightAt(home.X, home.Z), home.Z);
        leg.StepBasisFrom = leg.CurBasisW;
        leg.StepBasisTo = FootBasisAt(leg.StepTo);
        leg.StepT = 0f;
        leg.Planted = false;
    }

    private void AdvanceLeg(Leg leg, float h)
    {
        if (leg.StepT < 0f) return;
        float prev = leg.StepT;
        leg.StepT += h / StepTime;
        float t = Mathf.Clamp(leg.StepT, 0f, 1f);
        float e = t * t * (3f - 2f * t);
        var pos = leg.StepFrom.Lerp(leg.StepTo, e);
        pos.Y += Mathf.Sin(Mathf.Pi * t) * StepLift;
        leg.SoleTarget = pos;
        var qa = leg.StepBasisFrom.GetRotationQuaternion();
        var qb = leg.StepBasisTo.GetRotationQuaternion();
        leg.CurBasisW = new Basis(qa.Slerp(qb, e));
        // mid-swing crossing -> capture trigger (test choreography)
        if (_phase == 1 && prev < 0.5f && leg.StepT >= 0.5f && _pendingPath == null)
        {
            if (_crestPending && !_crestShot)
            {
                _crestShot = true;
                QueueCapture("res://proof/timberwolf_gait_crest.png", "crest", "side");
            }
            else if (_gaitShots < 4)
            {
                _gaitShots++;
                QueueCapture($"res://proof/timberwolf_gait_{_gaitShots}.png", $"gait_{_gaitShots}", "side");
            }
        }
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
        _simT += h;
        // choreography
        if (_test)
        {
            if (_phase == 0 && _simT >= 1.5 && !_shotStand)
            {
                _shotStand = true;
                QueueCapture("res://proof/timberwolf_stand.png", "stand", "classic");
            }
            if (_phase == 0 && _simT >= 2.2) { _phase = 1; _targetSpeed = WalkSpeed; GD.Print("TW state=walk"); }
            if (_phase == 1 && _rootPos.Z <= -6f) { _phase = 2; _targetSpeed = 0f; GD.Print($"TW state=stop pos={_rootPos}"); }
            if (_phase == 1 && !_crestShot && !_crestPending && _rootPos.Z <= 6.4f) _crestPending = true;
        }
        else
        {
            if (_phase == 0 && _simT >= 1.2) { _phase = 1; _targetSpeed = WalkSpeed; }
            if (_rootPos.Z <= -44f) _targetSpeed = 0f;
        }

        // speed + root advance
        float dv = Accel * h;
        _speed = Mathf.MoveToward(_speed, _targetSpeed, dv);
        var vel = Heading * _speed;
        _rootPos += vel * h;

        // step triggers: a leg steps when its planted foot lags its (led) home
        // slot past the threshold, the other leg is planted, no step running.
        var stepping = _legL.StepT >= 0f ? _legL : _legR.StepT >= 0f ? _legR : null;
        if (stepping == null && (_legL.Planted || _legR.Planted))
        {
            // While stopping/idle, use a small threshold so feet settle home.
            float threshold = (_targetSpeed < 0.01f && _speed < 0.15f) ? 0.10f : StepThreshold;
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

        UpdateRoot(h);
        SolveAll();
        TrackSlide();
    }

    private void UpdateRoot(float h)
    {
        // Root Y from the feet: mean ankle reference height + stand hip rise.
        float refL = (_legL.Planted ? _legL.SoleTarget.Y : _legL.StepTo.Y) + _legL.AnkleAboveSoleW;
        float refR = (_legR.Planted ? _legR.SoleTarget.Y : _legR.StepTo.Y) + _legR.AnkleAboveSoleW;
        // bob: deeper settle in double support, slight rise mid-swing
        bool doubleSupport = _legL.Planted && _legR.Planted;
        float bobT = doubleSupport ? -0.09f : -0.02f;
        _bob = Mathf.Lerp(_bob, bobT, 1f - Mathf.Exp(-6f * h));
        float yT = 0.5f * (refL + refR) + _hipRiseW - _hipAboveHolderW + _bob;
        _rootPos.Y = Mathf.Lerp(_rootPos.Y, yT, 1f - Mathf.Exp(-8f * h));

        // sway toward the planted leg while the other swings
        var swayT = Vector3.Zero;
        if (_legL.StepT >= 0f) swayT = _rootBasis * new Vector3(_legR.HomeLocal.X * _scaleF, 0f, 0f) * 0.22f;
        else if (_legR.StepT >= 0f) swayT = _rootBasis * new Vector3(_legL.HomeLocal.X * _scaleF, 0f, 0f) * 0.22f;
        _sway = _sway.Lerp(swayT, 1f - Mathf.Exp(-5f * h));

        // up vector: roll from the two feet heights, pitch from forward gradient
        var sL = _legL.Planted ? _legL.SoleTarget : _legL.StepTo;
        var sR = _legR.Planted ? _legR.SoleTarget : _legR.StepTo;
        var plus = sR.X > sL.X ? sR : sL;
        var minus = sR.X > sL.X ? sL : sR;
        float dhdx = (plus.Y - minus.Y) / Mathf.Max(plus.X - minus.X, 0.5f);
        const float ge = 1.5f;
        float dhdz = (Main.HeightAt(_rootPos.X, _rootPos.Z + ge) - Main.HeightAt(_rootPos.X, _rootPos.Z - ge)) / (2f * ge);
        var upT = new Vector3(-dhdx, 1f, -dhdz).Normalized();
        // clamp tilt to ~13 degrees
        float tilt = Mathf.Acos(Mathf.Clamp(upT.Dot(Vector3.Up), -1f, 1f));
        if (tilt > 0.23f) upT = Vector3.Up.Lerp(upT, 0.23f / tilt).Normalized();
        _upEased = _upEased.Lerp(upT, 1f - Mathf.Exp(-3.5f * h)).Normalized();

        var q = new Quaternion(Vector3.Up, _upEased);
        _rootBasis = new Basis(q) * YawBasis;

        var pos = _rootPos + _sway;
        _holderXf = new Transform3D(_rootBasis.Scaled(Vector3.One * _scaleF), pos);
        _holder.Transform = _holderXf;
        _skelGlobal = _holderXf * _chain;
    }

    // ---------- IK ----------

    private void SolveAll()
    {
        SolveLeg(_legL);
        SolveLeg(_legR);
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
            if (leg.Planted && leg.HasPrev)
            {
                float drift = new Vector2(leg.SolvedSoleWorld.X - leg.PrevSoleWorld.X,
                                           leg.SolvedSoleWorld.Z - leg.PrevSoleWorld.Z).Length();
                if (drift > _maxSlide) _maxSlide = drift;
                _plantedFrames++;
            }
            leg.PrevSoleWorld = leg.SolvedSoleWorld;
            leg.HasPrev = leg.Planted;
        }
    }

    // ---------- verification / captures ----------

    private void QueueCapture(string path, string tag, string camMode)
    {
        if (_pendingPath != null || _capturing) return;
        _pendingPath = path; _pendingTag = tag; _camMode = camMode;
    }

    private string FootReport(Leg leg, bool engine)
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
            ? $"{leg.Name}: PLANTED sole={sole} terrY={terr:F3} diff={diff:+0.000;-0.000}"
            : $"{leg.Name}: SWING soleY={sole.Y:F3} terrY={terr:F3} clearance={diff:F3}";
    }

    private void LogContact(string tag, bool engine)
    {
        GD.Print($"TW CONTACT[{tag}]{(engine ? " engine" : " analytic")} {FootReport(_legL, engine)} | {FootReport(_legR, engine)} root={_rootPos} speed={_speed:F2}");
    }

    private async void DoCapture()
    {
        _capturing = true;
        _frozen = true;
        string path = _pendingPath, tag = _pendingTag;
        _pendingPath = null;
        LogContact(tag, false);
        UpdateCamera();
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        LogContact(tag, true);
        var img = GetViewport().GetTexture().GetImage();
        var err = img.SavePng(path);
        GD.Print($"TW captured {path} size={img.GetSize()} err={err}");
        _frozen = false;
        _capturing = false;

        if (tag == "stand") { _phase = 0; /* stay idle until t>=2.2 flips to walk */ }
        if (_phase == 2 && !_crestShot)
        {
            _crestShot = true; // crest window missed; don't stall the run
        }
        CheckFinish();
    }

    private void CheckFinish()
    {
        if (!_test || _phase != 2 || _shotFinal) return;
        if (_speed > 0.03f) return;
        if (_legL.StepT >= 0f || _legR.StepT >= 0f) return;
        var homeL = HomeSlot(_legL, _rootPos);
        var homeR = HomeSlot(_legR, _rootPos);
        float dL = new Vector2(_legL.SoleTarget.X - homeL.X, _legL.SoleTarget.Z - homeL.Z).Length();
        float dR = new Vector2(_legR.SoleTarget.X - homeR.X, _legR.SoleTarget.Z - homeR.Z).Length();
        if (dL > 0.45f || dR > 0.45f) return;
        _shotFinal = true;
        _phase = 3;
        QueueCapture("res://proof/timberwolf_walk.png", "final", "classic");
    }

    private void Finish()
    {
        float moved = _startPos.DistanceTo(_rootPos);
        GD.Print($"TW DONE moved={moved:F2} start={_startPos} end={_rootPos} steps={_steps} gaitShots={_gaitShots} crest={_crestShot} maxSlide={_maxSlide:F4} plantedFrames={_plantedFrames}");
        GetTree().Quit();
    }

    private void UpdateCamera()
    {
        var mp = _rootPos;
        if (_camMode == "side")
        {
            _cam.GlobalPosition = mp + new Vector3(-16.5f, 5.0f, -2.0f);
            _cam.LookAt(mp + new Vector3(0f, 4.6f, 0f), Vector3.Up);
        }
        else
        {
            _cam.GlobalPosition = mp + new Vector3(-11.5f, 6.5f, -15.5f);
            _cam.LookAt(mp + new Vector3(0f, 5.5f, 0f), Vector3.Up);
        }
    }

    public override void _Process(double delta)
    {
        if (_holder == null || _skel == null || _legL.Hip < 0) return;

        if (!_frozen)
        {
            _acc += Mathf.Min(delta, 0.1);
            const float h = 1f / 120f;
            int guard = 0;
            while (_acc >= h && guard++ < 40)
            {
                GaitStep(h);
                _acc -= h;
                if (_pendingPath != null) break; // capture at a clean gait state
            }
            if (_test && _phase == 2) CheckFinish();
            if (_test && _phase == 3 && _shotFinal && _pendingPath == null && !_capturing)
            {
                Finish();
                return;
            }
        }
        UpdateCamera();
        if (_pendingPath != null && !_capturing) DoCapture();
    }
}
