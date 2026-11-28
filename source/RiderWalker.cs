using Godot;
using System.Collections.Generic;

namespace Titans;

// RiderWalker: reusable procedural gait + two-bone IK for a rigged rider FBX,
// extracted from the original walker test scene and
// generalized: any heading (not just -Z), signed speed (reverse stepping),
// turn-in-place stepping, torso twist/pitch aim on the torso bone, and bone
// data derived per-model from the rest pose -- the knee pole comes from the
// rest pose, so digitigrade (Enyo) and plantigrade (Hector) legs both
// work with no hardcoded bend direction.
//
// Drive model: every frame the owner calls SetDrive(legsYaw, targetSpeed)
// and StepFrame(delta). The walker advances its root along the legs heading,
// plants feet in WORLD space (targets sampled from Main.HeightAt at each
// foot's own XZ), solves hip/knee/ankle by analytic two-bone IK written as
// skeleton global pose overrides, and sets the root height/tilt from the
// feet. Collisions are the owner's job: after stepping, the owner may call
// ApplyRootXZ to push the root out of an obstacle; planted feet stay put.
public partial class RiderWalker : Node3D
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
    public bool FlipKneePole; // plantigrade chassis (Hector): mirror the rest-pose knee pole so the knee bends FORWARD, human-style (owner round 22)
    public bool SagittalKneePole; // purify the rest pole to the sagittal plane, same side (Apollo: the rest pole's inward lateral part X-ed the legs)
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
    public Vector3 StepFromLocal, StepToLocal; // swing endpoints in the yaw (facing) frame
    public Basis StepBasisFrom, StepBasisTo;
    public float StepT = -1f;
    public float StepDur; // this step's swing duration (cfg StepTime, shortened when the stride budget caps the trail)
    public Transform3D SolvedAnklePose;
    public Vector3 SolvedSoleWorld;
    public Vector3 PrevSoleWorld;
    public bool HasPrev;
    public float LastPlantedDiff;
    public bool Disabled;
  }

  private const float StandReachFactor = 0.90f;
  private const float AirborneSoleDrop = 10.5f; // SoleLevel: the soles hang this far under the root when no foot is planted
                                                // Thruster tuning (owner, see the SetThrust comment):
                                                // burn time, recharge time, climb acceleration and cap,
                                                // fall gravity and terminal velocity - exact values.
  private const float ThrustBurnSeconds = 2.2f;
  private const float ThrustRechargeSeconds = 5f;
  private const float ThrustAccel = 34f;
  private const float ThrustClimbSpeed = 10.5f;
  private const float FallGravity = 24f;
  private const float FallTerminalSpeed = -18f;
  private const float JoltAngleScale = 0.055f; // impact jolt: push strength to rock angle
                                               // Gait and body tuning (single-use values included, per
                                               // the owner's tightened standard).
  private const float AirborneHeightEpsilon = 0.02f; // jump height above which the rider counts as airborne
  private const float SoleDropEngageHeight = 0.05f;  // jump height at which the sole level switches to the hang drop
  private const float TorsoPitchMin = -0.32f, TorsoPitchMax = 0.48f;   // the torso bone's pitch stops
  private const float TorsoRenderPitchMin = -0.85f, TorsoRenderPitchMax = 0.32f; // rendered pitch clamp (droop included)
  private const float MinDirLengthSq = 0.01f;        // squared length below which a direction is unusable
  private const float MinHipRiseSq = 0.25f;          // hip-rise radicand floor
  private const float StrideBudgetReserve = 0.97f;   // stride budget keeps this fraction of full reach (a whisker of bend)
  private const float MinStrideBudgetSq = 0.42f;     // stride-budget radicand floor
  private const float MinPushLengthSq = 1e-10f;      // push-out deltas below this are no-ops
  private const double MaxFrameDelta = 0.1;          // a frame's simulated time is capped here
  private const int MaxGaitSubsteps = 40;            // substep guard per frame
  private const float MinDriveSpeed = 0.01f;         // speeds at or below this read as stopped
  private const float MinStepDuration = 0.01f;       // step-duration divisor guard
  private const float FootClampEpsilon = 0.001f;     // a foot clamp counts as moved past this
  private const float StrideCapSlack = 0.001f;       // trail-vs-demand slack that detects the stride budget capping
  private const float BrakeDetectEpsilon = 0.001f;   // target-vs-speed slack in the braking test
  private const float BrakeOvershootFactor = 1.15f;  // brake rate overshoot (stop inside one second incl. detection lag)
  private const float CohesionWindowSeconds = 0.25f; // blocked-coherence sampling window
  private const float CohesionBlockedSeconds = 0.25f; // blocked must persist this long to read Blocked
  private const float CohesionMinWant = 0.02f;       // asked-for travel below this leaves the ratio at 1
  private const float CohesionBlockedRatio = 0.35f;  // progress ratio below which the way reads blocked
  private const float SettleSpeedThreshold = 0.15f;  // settling: live speed below this
  private const float SettleYawRateThreshold = 0.05f; // settling: yaw rate below this
  private const float SettleStepThreshold = 0.10f;   // settling: step-lift lag threshold
  private const float TrailFloor = 0.65f;            // the trail threshold never drops below this
  private const float GroundCarryEpsilon = 0.01f;    // a planted foot re-seats past this ground delta
  private const float GroundCarryDropMax = 2.0f;     // ...and follows a descending surface down at most this far
  private const float HangDropExtra = 0.5f;          // flight hang: soles sit this far past full leg extension
  private const float BobDoubleSupport = -0.09f, BobSingleSupport = -0.02f;
  private const float BobEaseRate = 6f, GroundFollowRate = 8f, SwayEaseRate = 5f;
  private const float FallRestHeight = 2.0f;         // knocked down: hips rest this far above the lower sole
  private const float FallAmountEpsilon = 0.001f;    // fall amount above which the fall pose applies
  private const float FallTipAngle = 1.32f;          // rad (~76 deg): the fall's full tip
  private const float FallUpEaseRate = 10f, UpEaseRate = 3.5f;
  private const float MinFootSpan = 0.5f;            // lateral foot-span floor for the roll estimate
  private const float MaxRootTilt = 0.25f;           // rad: the root's terrain tilt is capped here (0.23 -> 0.25 at the owner's word, 2026-11-27)
  private const float TiltGradientProbe = 1.5f;      // u: fore/aft probe distance for the pitch gradient
  private const float MinJoltEnvelope = 0.002f;      // jolt envelope below which the rock is dropped
  private const float JoltDecayRate = 2.4f;          // jolt envelope decay per second
  private const float IkMinReachFraction = 0.05f, IkMaxReachFraction = 0.999f; // IK target distance clamp, as reach fractions
  private const float MinPoleLengthSq = 1e-6f;       // IK pole-vector degeneracy guard
  private const float MinBindWeight = 0.01f;         // skin weights below this don't count toward a mesh's dominant bone
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
  private float _brakeV; // the current braking episode's captured rate (owner: no inertia)
  private float _jumpH, _jumpVel;
  private float _groundY; // gait-followed root height; the jump arc rides on top unsmoothed
  public bool JumpJets; // thrusters fitted AND chassis capable (set by the duel from spec+loadout)
  public float JumpCharge = 1f; // thruster load 0..1: burning drains it (~2.2s of flight); recharges whenever not burning (~5s), even mid-air (owner round 22)
  public float JumpHeight => this._jumpH;
  public bool Airborne => this._jumpH > AirborneHeightEpsilon;

  private bool _thrustHeld;
  public bool Thrusting { get; private set; }

  // Thrusters (owner, replacing the ballistic jump): HOLD the
  // key to fly. SetThrust is the held state; while held with
  // charge remaining the jets climb (capped ~10.5 u/s) and drain
  // the full charge in ~2.2s of burn - enough to rise well above
  // a 14u enemy. Release the key or empty the tanks and gravity
  // (24 u/s^2, terminal -18) brings the rider down. A partial
  // charge flies proportionally lower. The charge auto-refills
  // on the ground in ~5s.
  public void SetThrust(bool on) => this._thrustHeld = on;
  private float _targetSpeed;
  private float _bob;
  private Vector3 _sway = Vector3.Zero;
  private double _acc;
  private int _steps;
  private float _maxSlide;
  // Blocked coherence (owner, 2026-11-11): signed travel
  // along the drive heading, summed over a 0.25s window,
  // vs the travel the drive asked for in that window. The
  // duel's push-out resolves once per frame AFTER the
  // walker moves, so a wall's snap-back lands inside one
  // substep as backward travel: a per-substep MAGNITUDE
  // ratio reads it as full progress (advance +0.027, snap
  // back -0.026 -> |net| = the wanted step) and can never
  // fire - measured on the phase-12 trace, 2026-11-11.
  // Signed projection over a window cancels the pair:
  // advance and snap-back sum to ~0 while the asked-for
  // travel keeps accumulating. Sustained near-zero
  // progress while driving means the way is blocked.
  // TELEMETRY ONLY since round 3 (owner, 2026-11-11:
  // 'remove any special conditions for leg movement when
  // blocked - I want to see how it acts naturally at
  // colliders'): nothing in the gait reads this any more;
  // the harness uses it to time its phase-12 verdict.
  private Vector2 _cohPrev; private bool _cohInit;
  private float _cohWinNet; private float _cohWinWant; private float _cohWinT;
  private float _cohRatio = 1f; private float _cohBlockT;
  public bool Blocked => this._cohBlockT > CohesionBlockedSeconds;
  private bool _freezeLegs; // thruster flight: legs held (owner, 2026-11-11 round 4)
  private int _plantedFrames;
  private float _hipRiseW;
  private float _hipAboveHolderW;
  private float _strideBudgetW; // max half-stride (world units) the leg geometry can plant without over-extending

  public bool StepMidCrossed { get; private set; }
  public string Label => this._cfg.Label;
  public Skeleton3D Skeleton => this._skel;
  public Node3D ModelRoot => this._inner;

  // Re-run material collection (e.g. after the part system replaced a
  // mesh instance inside the model): clears the registry and duplicates
  // fresh per-instance materials for every mesh currently in the model.
  public void RefreshMaterials()
  {
    this._mats.Clear();
    this.CollectMats(this._inner);
  }
  public bool IsReady => this._ready;
  public float Speed => this._speed;

  // The walker's current sole level: the higher planted sole, or
  // - airborne - the soles hanging ~10.5u under the root. Roof
  // grounding (GroundHere) and the duel's roof collision gate
  // both key on this: it is what "already at roof level" means.
  public float SoleLevel
  {
    get
    {
      float s = float.MinValue;
      if (this._legL.Planted)
      {
        s = Mathf.Max(s, this._legL.SoleTarget.Y);
      }

      if (this._legR.Planted)
      {
        s = Mathf.Max(s, this._legR.SoleTarget.Y);
      }

      if (s == float.MinValue)
      {
        s = this._rootPos.Y - AirborneSoleDrop;
      }

      if (this._jumpH > SoleDropEngageHeight)
      {
        s = Mathf.Max(s, this._rootPos.Y - AirborneSoleDrop);
      }

      return s;
    }
  }

  // Foot ground: the terrain (owner, 2026-11-27: roofs and
  // decks are gone with the buildings - nothing else is
  // walkable).
  private float GroundHere(float x, float z) => Main.HeightAt(x, z);
  public float LegsYaw => this._legsYaw;
  public float CollisionRadius => this._cfg.CollisionRadius;
  public float Height => this._cfg.TargetHeight;
  public Vector3 RootPos => this._rootPos;
  public int Steps => this._steps;
  // Last foot plant (world position + foot basis), for the
  // snow footprints (Titans.Snow).
  public Vector3 LastPlantPos;
  public Basis LastPlantBasis = Basis.Identity;
  public float MaxSlide => this._maxSlide;

  private float _torsoTwist;
  private float _torsoPitch;
  public float TorsoTwist
  {
    get => this._torsoTwist;
    set => this._torsoTwist = value; // unclamped (owner, 2026-10-29): full 360° torso rotation
  }
  public float TorsoPitch
  {
    get => this._torsoPitch;
    set => this._torsoPitch = Mathf.Clamp(value, TorsoPitchMin, TorsoPitchMax);
  }
  // The AIM pitch (owner, round 13): the mouse target is NOT bound
  // by the torso's clamp - the reticle must reach the
  // ground in front of the feet and the sky above. The torso mesh
  // follows TorsoPitch to its stops; the aim direction uses this
  // wider range (Titans owns the live value; NaN there means
  // "follow TorsoPitch", which is what every harness phase sets).
  public float AimPitchWide = float.NaN;
  public float TorsoYawWorld => this._legsYaw + this._torsoTwist;

  // Combat state set by the owner: a halted walker keeps its last solved
  // pose (feet stay planted) and only the torso droop still animates.
  public bool Halted { get; set; }
  public float Droop { get; set; }

  // Breakable-parts state (owner-driven): a wrecked (disabled) leg
  // keeps stepping with the gait - the whole rider is slower via
  // SpeedFactor, and the wrecked leg moves at that same reduced
  // pace (owner's round-7 call; the earlier rigid drag read as the
  // leg floating). LimpRoll leans the body (toward the model's left
  // when positive).
  public float SpeedFactor = 1f;
  public float LimpRoll;

  // Fallen state (owner's rule, third iteration): a rider falls ONLY
  // when it loses a leg, and it never gets back up. The fall tips the
  // root toward _fallDirW by up to ~76 degrees over 0.5s and drops the
  // hips to rest height - then it stays there until reset. While down
  // the drive is suppressed and no new steps begin, but the torso can
  // still twist and the weapons still fire (a prone turret).
  private float _fallT = -1f;
  private float _fallAmt;
  private Vector3 _fallDirW = Vector3.Forward;
  public bool IsKnockedDown => this._fallT >= 0f;
  public float FallAmount => this._fallAmt;

  public bool TryKnockDown(Vector3 awayDirW)
  {
    if (this._fallT >= 0f || this.Halted)
    {
      return false;
    }

    var d = new Vector3(awayDirW.X, 0f, awayDirW.Z);
    this._fallDirW = d.LengthSquared() > MinDirLengthSq ? d.Normalized() : Vector3.Forward;
    this._fallT = 0f;
    this._fallAmt = 0f;
    return true;
  }

  public void SetLegDisabled(bool left, bool disabled)
  {
    var leg = left ? this._legL : this._legR;
    leg.Disabled = disabled;
    if (disabled)
    {
      leg.HasPrev = false;
    }
  }

  public bool LegDisabled(bool left) => (left ? this._legL : this._legR).Disabled;
  public Vector3 SoleWorld(bool left) => (left ? this._legL : this._legR).SolvedSoleWorld;
  public bool LegPlanted(bool left) => (left ? this._legL : this._legR).Planted;
  public Vector3 SolePos(bool left) => (left ? this._legL : this._legR).SoleTarget;

  // Foot-target clamp (owner, 2026-11-11: leg-collider
  // option 1 - 'clamp the foot targets'): the OWNER may
  // supply a clamp that pulls a step's landing point out
  // of obstacle footprints before the step begins, so a
  // foot plants short of a wall instead of inside it.
  // Null = no clamping (standalone walker use). The
  // stride simply shortens on the blocked side; no gait
  // mode changes - the same generic trigger fires.
  public System.Func<Vector3, Vector3> FootClamp;
  public Vector3 VelocityVec => HeadingVec(this._legsYaw) * this._speed;

  // World position of a skeleton bone (muzzle hardpoints, aim points).
  public Vector3 BoneWorldPos(string name)
  {
    if (this._skel == null)
    {
      return this.GlobalPosition;
    }

    int idx = this._skel.FindBone(name);
    if (idx < 0)
    {
      return this.GlobalPosition;
    }

    return this._skel.GlobalTransform * this._skel.GetBoneGlobalPose(idx).Origin;
  }

  // Does this rig carry the named bone? The harness bone
  // census guards the shared-armature contract (see Bones).
  public bool HasBone(string name) => this._skel != null && this._skel.FindBone(name) >= 0;

  public static Vector3 HeadingVec(float yaw) => new Vector3(-Mathf.Sin(yaw), 0f, -Mathf.Cos(yaw));
  public static float YawOf(Vector3 dir) => Mathf.Atan2(-dir.X, -dir.Z);
  public static float WrapAngle(float a)
  {
    while (a > Mathf.Pi)
    {
      a -= Mathf.Tau;
    }

    while (a < -Mathf.Pi)
    {
      a += Mathf.Tau;
    }

    return a;
  }

  // Call once after the walker is inside the tree.
  public void Init(Config cfg, Vector3 spawnPos, float legsYaw)
  {
    this._cfg = cfg;
    var packed = GD.Load<PackedScene>(cfg.ModelPath);
    if (packed == null) { Dbg.Err($"{cfg.Label} FAIL: {cfg.ModelPath} did not load"); return; }
    this._inner = packed.Instantiate<Node3D>();
    this.AddChild(this._inner);

    this._skel = FindSkeleton(this._inner);
    var armBones = ArmChainBones(this._skel);
    var raw = MeasureWorldAabb(this._inner);
    float s = cfg.TargetHeight / raw.Size.Y;
    this._scaleF = s;
    this.Scale = Vector3.One * s;
    var a = MeasureWorldAabb(this._inner);
    // Depth anchoring uses the CORE aabb (geometry bound outside
    // the arm chains): the arms are appendages whose rest swing -
    // e.g. a baked elbow raise - must not drag the body off its
    // root. Width, height and ground still use the full aabb.
    var core = MeasureWorldAabb(this._inner, armBones);
    bool haveCore = core.Size.Y > 0f;
    float corePosZ = haveCore ? core.Position.Z : a.Position.Z;
    float coreSizeZ = haveCore ? core.Size.Z : a.Size.Z;
    var shift = new Vector3(
        -a.Size.X / 2f - a.Position.X,
        -a.Position.Y,
        -coreSizeZ / 2f - corePosZ);
    this._inner.Position += shift / s;
    var chk = MeasureWorldAabb(this._inner);
    Dbg.Print($"{cfg.Label} normalized AABB pos={chk.Position} size={chk.Size} coreDepth={coreSizeZ:F3} scale={s:F4}");

    // Blender FBX faces +Z after import; rotate so it faces -Z at yaw 0.
    this.Rotation = new Vector3(0f, Mathf.Pi, 0f);
    this.Position = spawnPos;

    if (this._skel == null) { Dbg.Err($"{cfg.Label} FAIL: no Skeleton3D"); return; }
    this.CollectMaterials();
    var names = new string[this._skel.GetBoneCount()];
    for (int i = 0; i < names.Length; i++)
    {
      names[i] = this._skel.GetBoneName(i);
    }

    Dbg.Print($"{cfg.Label} skeleton bones={names.Length}: {string.Join(",", names)}");

    this._torsoBone = this._skel.FindBone(Bones.Torso);
    this.InitLegs(spawnPos);
    if (this._legL.Hip < 0 || this._legR.Hip < 0) { Dbg.Err($"{cfg.Label} FAIL: leg bones not found"); return; }

    this._legsYaw = legsYaw;
    this._prevYaw = legsYaw;
    this._rootPos = spawnPos;
    this._rootBasis = Basis.Identity.Rotated(Vector3.Up, legsYaw + Mathf.Pi);
    this._holderXf = this.GlobalTransform;
    this._skelGlobal = this._holderXf * this._chain;
    this._ready = true;
    Dbg.Print($"{cfg.Label} walker ready at {spawnPos} yaw={legsYaw:F2}");
  }

  private void InitLegs(Vector3 spawnPos)
  {
    var holderG = this.GlobalTransform;
    var skelG = this._skel.GlobalTransform;
    this._chain = holderG.AffineInverse() * skelG;
    var worldAabb = MeasureWorldAabb(this._inner);
    float groundY = worldAabb.Position.Y;

    if (this._torsoBone >= 0)
    {
      var rt = this._skel.GetBoneGlobalRest(this._torsoBone);
      this._torsoRestOrigin = rt.Origin;
      this._torsoM0 = skelG.Basis.Orthonormalized() * rt.Basis;
      // Facing reference: at rest (legsYaw = 0) the model faces world
      // -Z. Store that direction in torso-bone space so the rendered
      // torso yaw can be measured later regardless of bone frames.
      var w0 = skelG.Basis.Orthonormalized()
          * this._skel.GetBoneGlobalPose(this._torsoBone).Basis.Orthonormalized();
      this._torsoFwdBoneLocal = w0.Transposed() * new Vector3(0f, 0f, -1f);
    }

    this.SetupLeg(this._legL, Bones.HipL, Bones.KneeL, Bones.AnkleL, skelG, holderG, groundY);
    this.SetupLeg(this._legR, Bones.HipR, Bones.KneeR, Bones.AnkleR, skelG, holderG, groundY);

    if (this._cfg.FlipKneePole)
    {
      // Plantigrade flip (owner round 22, sharpened same
      // day): the pole becomes the PURE sagittal axis on the
      // flipped side of the leg - zero lateral component.
      // From the front or back the hip, knee and ankle then
      // share one lateral coordinate and the leg reads as a
      // straight line; the entire bend shows only from the
      // side. (The sculpt's rest pole carries a +-0.559
      // lateral part - knees splayed - and an axis
      // reflection mirrored it inward; both read as offsets
      // from the front, which the owner ruled out.)
      var lateral = (this._legR.Hrest - this._legL.Hrest).Normalized();
      foreach (var leg in new[] { this._legL, this._legR })
      {
        var up = (leg.Hrest - leg.Arest).Normalized();
        var sag = up.Cross(lateral).Normalized(); // in the pole's plane, ⊥ leg axis and ⊥ lateral
        leg.PoleSk = sag * -Mathf.Sign(leg.PoleSk.Dot(sag));
        Dbg.Print($"{this._cfg.Label} leg {leg.Name}: flipped pole={leg.PoleSk}");
      }
    }

    if (this._cfg.SagittalKneePole)
    {
      // Sagittal-only tracking (owner, Apollo 2026-11-17):
      // the leg bends purely in the forward/back plane -
      // the pole becomes the pure sagittal axis on the
      // SAME side as the rest pole (no mirror), so hip,
      // knee and ankle share one lateral coordinate and
      // the legs can never X. Apollo's rest pole carried
      // an inward lateral part (~22% of its length) that
      // pulled both knees toward the centreline on every
      // stride.
      var lateral = (this._legR.Hrest - this._legL.Hrest).Normalized();
      foreach (var leg in new[] { this._legL, this._legR })
      {
        var up = (leg.Hrest - leg.Arest).Normalized();
        var sag = up.Cross(lateral).Normalized();
        leg.PoleSk = sag * Mathf.Sign(leg.PoleSk.Dot(sag));
        Dbg.Print($"{this._cfg.Label} leg {leg.Name}: sagittal pole={leg.PoleSk}");
      }
    }

    float reachW = 0.5f * ((this._legL.L1 + this._legL.L2) + (this._legR.L1 + this._legR.L2)) * skelG.Basis.X.Length();
    float horizHipAnkle = 0f;
    foreach (var leg in new[] { this._legL, this._legR })
    {
      var hipW = skelG * leg.Hrest;
      var ankW = skelG * leg.Arest;
      horizHipAnkle += new Vector2(ankW.X - hipW.X, ankW.Z - hipW.Z).Length();
    }
    horizHipAnkle /= 2f;
    float dStand = StandReachFactor * reachW;
    this._hipRiseW = Mathf.Sqrt(Mathf.Max(dStand * dStand - horizHipAnkle * horizHipAnkle, MinHipRiseSq));
    var hipWL = skelG * this._legL.Hrest;
    var hipWR = skelG * this._legR.Hrest;
    this._hipAboveHolderW = 0.5f * ((hipWL.Y - this.Position.Y) + (hipWR.Y - this.Position.Y));
    // Stride budget: with the hips riding hipRiseW above the soles,
    // a planted foot can sit at most sqrt(reach^2 - hipRise^2) of
    // horizontal offset from the hip before the leg is bone-straight
    // and the IK clamp leaves the sole hovering. Near-straight rigs
    // (the Enyo-legged riders stand at 99% of reach in the sculpt)
    // blow this budget at run speed under the pendulum stride -
    // both feet at the extremes, neither touching (owner report,
    // 2026-11-16). The 0.97 keeps a whisker of bend in reserve.
    this._strideBudgetW = StrideBudgetReserve * Mathf.Sqrt(Mathf.Max(reachW * reachW - this._hipRiseW * this._hipRiseW, MinStrideBudgetSq));
    Dbg.Print($"{this._cfg.Label} reachW={reachW:F3} dStand={dStand:F3} hipRiseW={this._hipRiseW:F3} hipAboveHolderW={this._hipAboveHolderW:F3}");
  }

  private void SetupLeg(Leg leg, string hipName, string kneeName, string ankleName,
      Transform3D skelG, Transform3D holderG, float groundY)
  {
    leg.Hip = this._skel.FindBone(hipName);
    leg.Knee = this._skel.FindBone(kneeName);
    leg.Ankle = this._skel.FindBone(ankleName);
    var rh = this._skel.GetBoneGlobalRest(leg.Hip);
    var rk = this._skel.GetBoneGlobalRest(leg.Knee);
    var ra = this._skel.GetBoneGlobalRest(leg.Ankle);
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
    leg.SoleTarget = new Vector3(soleW.X, this.GroundHere(soleW.X, soleW.Z), soleW.Z);
    leg.PlantBasisW = FootBasisAt(leg.SoleTarget, 0f);
    leg.CurBasisW = leg.PlantBasisW;
    leg.SolvedSoleWorld = leg.SoleTarget;
    Dbg.Print($"{this._cfg.Label} leg {leg.Name}: L1={leg.L1:F2} L2={leg.L2:F2} pole={leg.PoleSk} homeLocal={leg.HomeLocal} plant={leg.SoleTarget}");
  }

  // Debug: world positions of a leg's joints (gait audits).
  public (string, Vector3, Vector3, Vector3, bool, float)[] DebugLegJoints()
  {
    var o = new (string, Vector3, Vector3, Vector3, bool, float)[2];
    var legs = new[] { this._legL, this._legR };
    var gt = this._skel.GlobalTransform;
    for (int i = 0; i < 2; i++)
    {
      o[i] = (legs[i].Name,
                gt * this._skel.GetBoneGlobalPose(legs[i].Hip).Origin,
                gt * this._skel.GetBoneGlobalPose(legs[i].Knee).Origin,
                gt * this._skel.GetBoneGlobalPose(legs[i].Ankle).Origin,
                legs[i].Planted, legs[i].StepT);
    }

    return o;
  }

  public void SetDrive(float legsYaw, float targetSpeed)
  {
    this._legsYaw = legsYaw;
    this._targetSpeed = targetSpeed;
  }

  // Hard placement for test choreography: feet re-plant at home slots.
  public void Teleport(Vector3 pos, float legsYaw)
  {
    this._rootPos = pos;
    // A teleport that asks for height (staging a rider ON
    // a deck) must ground against that height: the feet
    // plant via GroundHere, which keys on the CURRENT sole
    // level, so without this the deck under the spot is
    // never adopted and the rider ends up standing on the
    // terrain UNDER the platform. (Found by the deck-shot
    // harness: an enemy "teleported" onto a deck stood
    // beneath it, root Y -0.6 instead of 22.)
    if (pos.Y > this.SoleLevel)
    {
      foreach (var leg in new[] { this._legL, this._legR })
      {
        leg.SoleTarget = new Vector3(leg.SoleTarget.X, pos.Y, leg.SoleTarget.Z);
      }
    }

    this._legsYaw = legsYaw;
    this._prevYaw = legsYaw;
    this._speed = 0f;
    this._jumpH = 0f; this._jumpVel = 0f; this._thrustHeld = false; this.Thrusting = false; this._freezeLegs = false;
    this._targetSpeed = 0f;
    this._fallT = -1f;
    this._fallAmt = 0f;
    this._joltEnv = 0f;
    this._sway = Vector3.Zero;
    this._bob = 0f;
    this._upEased = Vector3.Up;
    foreach (var leg in new[] { this._legL, this._legR })
    {
      leg.StepT = -1f;
      leg.Planted = true;
      var home = this.HomeSlot(leg, pos);
      leg.SoleTarget = new Vector3(home.X, this.GroundHere(home.X, home.Z), home.Z);
      leg.PlantBasisW = FootBasisAt(leg.SoleTarget, legsYaw);
      leg.CurBasisW = leg.PlantBasisW;
      leg.HasPrev = false;
    }
    this._groundY = this.DesiredRootY();
    this._rootPos.Y = this._groundY;
    this.RefreshPose();
  }

  // Collision push-out: move the root horizontally; planted feet stay.
  public void ApplyRootXZ(Vector2 xz)
  {
    var delta = new Vector3(xz.X - this._rootPos.X, 0f, xz.Y - this._rootPos.Z);
    if (delta.LengthSquared() < MinPushLengthSq)
    {
      return;
    }

    this._rootPos += delta;
    this._holderXf = new Transform3D(this._holderXf.Basis, this._holderXf.Origin + delta);
    this.Transform = this._holderXf;
    this._skelGlobal = this._holderXf * this._chain;
    this.SolveAll();
  }

  public void StepFrame(double delta)
  {
    if (!this._ready)
    {
      return;
    }

    if (this.Halted) { this.SolveAll(); return; }
    if (this._fallT >= 0f)
    {
      const float FallSeconds = 0.5f; // the tip-over takes half a second
      this._fallT += (float)delta;
      float t = this._fallT;
      if (t < FallSeconds) { float u = t / FallSeconds; this._fallAmt = u * u; }
      else
      {
        this._fallAmt = 1f; // down for good - no rise, ever
      }
    }
    this._acc += Mathf.Min(delta, MaxFrameDelta);
    const float h = 1f / 120f;
    int guard = 0;
    while (this._acc >= h && guard++ < MaxGaitSubsteps)
    {
      this.GaitStep(h);
      this._acc -= h;
    }
  }

  public bool ConsumeStepMidCross()
  {
    bool r = this.StepMidCrossed;
    this.StepMidCrossed = false;
    return r;
  }

  public bool AnySwinging => this._legL.StepT >= 0f || this._legR.StepT >= 0f;

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

  // FootBasisAt for a level floor (normal = +Y): the hang
  // pose's foot basis. It MUST share FootBasisAt's axes -
  // the flight code used YawBasis() here before, which is
  // the same frame spun 180 degrees about Y, so the feet
  // rendered toes-back/twisted for the whole burn (owner's
  // report, 2026-11-13; measured foot yaw error ~174 deg
  // in the air vs ~0 planted). YawBasis stays correct for
  // its other job (step-space math), just not for feet.
  private static Basis FlatFootBasis(float yaw)
  {
    var back = HeadingVec(yaw) * -1f;
    var xA = Vector3.Up.Cross(back);
    return new Basis(xA, Vector3.Up, back);
  }

  private Vector3 HomeSlot(Leg leg, Vector3 rootXZ)
  {
    var off = this._rootBasis * new Vector3(leg.HomeLocal.X * this._scaleF, 0f, leg.HomeLocal.Y * this._scaleF);
    return new Vector3(rootXZ.X + off.X, 0f, rootXZ.Z + off.Z);
  }

  private Leg Other(Leg leg) => leg == this._legL ? this._legR : this._legL;

  // The walker's yaw (facing) frame: origin at the root, axes
  // from the legs yaw alone (no terrain tilt). Home slots and
  // swing paths are defined in this frame.
  private Basis YawBasis() => Basis.Identity.Rotated(Vector3.Up, this._legsYaw + Mathf.Pi);

  private void BeginStep(Leg leg)
  {
    var vel = HeadingVec(this._legsYaw) * this._speed;
    // Pendulum stride (owner, 2026-11-16): the plant point
    // is the home slot of the root predicted ahead so that,
    // after the swing's own step-time of root travel, the
    // foot lands the same distance AHEAD of the body that
    // the trigger lets it trail BEHIND (see GaitStep):
    // prediction time = StepTime + trail / speed, which is
    // exactly 1.5 step-times at the uncapped half-stride
    // trail. The old lead (StepTime + 0.10, plus a flat
    // vel*0.30) grew with speed while the lift threshold
    // stayed fixed, so at run speed the legs reached ~4.7u
    // ahead but lifted only ~2.0u behind (measured,
    // STRIDEDBG): thrown forward, no pendulum.
    float demand = Mathf.Abs(this._speed) * this._cfg.StepTime * 0.5f;
    float trail = Mathf.Min(demand, this._strideBudgetW);
    // Capped-stride cadence (owner playtest 2026-11-16):
    // when the budget caps the trail, the stance phase
    // shortens to 2*trail/speed but the swing still took the
    // full StepTime, so the support foot over-trailed ~2u
    // past the pendulum point at run (measured LEANPROBE:
    // Amadeus -4.68 dynamic vs Enyo -3.04) and the rider
    // read as leaning forward. The swing now takes the
    // stance's duration when capped - cadence rises with
    // speed, like a runner's - restoring the symmetric
    // pendulum. Uncapped (walk, crawl, turns): StepTime
    // exactly as before.
    leg.StepDur = (trail < demand - StrideCapSlack && Mathf.Abs(this._speed) > MinDriveSpeed)
        ? Mathf.Min(this._cfg.StepTime, 2f * trail / Mathf.Abs(this._speed))
        : this._cfg.StepTime;
    float predT = leg.StepDur + (Mathf.Abs(this._speed) > MinDriveSpeed ? trail / Mathf.Abs(this._speed) : 0f);
    var predRoot = this._rootPos + vel * predT;
    var home = this.HomeSlot(leg, predRoot);
    leg.StepFrom = leg.SoleTarget;
    leg.StepTo = new Vector3(home.X, this.GroundHere(home.X, home.Z), home.Z);
    if (this.FootClamp != null)
    {
      // Clamp the landing point out of obstacles, then
      // re-sample the ground at the clamped spot (the
      // ground there is the surrounding level, not
      // whatever the unclamped point sampled).
      var ct = this.FootClamp(leg.StepTo);
      if (Mathf.Abs(ct.X - leg.StepTo.X) > FootClampEpsilon || Mathf.Abs(ct.Z - leg.StepTo.Z) > FootClampEpsilon)
      {
        leg.StepTo = new Vector3(ct.X, this.GroundHere(ct.X, ct.Z), ct.Z);
      }
    }
    // Swing endpoints in the FACING frame (owner: leg motion
    // must read in the sagittal plane from the front - a
    // world-space lerp cuts across the body whenever the rider
    // turns mid-step, the 'needs to pee' scissor). AdvanceLeg
    // re-projects both endpoints through the CURRENT yaw every
    // substep, so the foot travels a clean in-frame line while
    // the body rotates under it.
    var inv = this.YawBasis().Inverse();
    leg.StepFromLocal = inv * (leg.StepFrom - this._rootPos);
    leg.StepToLocal = inv * (leg.StepTo - this._rootPos);
    leg.StepBasisFrom = leg.CurBasisW;
    leg.StepBasisTo = FootBasisAt(leg.StepTo, this._legsYaw);
    leg.StepT = 0f;
    leg.Planted = false;
  }

  private void AdvanceLeg(Leg leg, float h)
  {
    if (leg.StepT < 0f)
    {
      return;
    }

    float prev = leg.StepT;
    leg.StepT += h / (leg.StepDur > MinStepDuration ? leg.StepDur : this._cfg.StepTime);
    float t = Mathf.Clamp(leg.StepT, 0f, 1f);
    float e = t * t * (3f - 2f * t);
    // Swing path: forward and vertical ride the original
    // WORLD-space lerp (stride length exactly as tuned), but
    // the LATERAL coordinate is re-anchored to the CURRENT
    // facing frame \u2014 in-frame it lerps straight from the old
    // plant's lateral to the target's, so a mid-step turn
    // can't drag the foot across the body (the scissor), and
    // the target can't run away from the body either.
    // (The first cut re-projected ALL axes through the frame:
    // the forward prediction offset baked into the local
    // target kept advancing with the root, doubling the
    // stride at run speed \u2014 the Enyo's legs spread
    // enormously in run mode. Forward must stay world-anchored.)
    var pos = leg.StepFrom.Lerp(leg.StepTo, e);
    pos.Y += Mathf.Sin(Mathf.Pi * t) * this._cfg.StepLift;
    var yawB = this.YawBasis();
    var rel = yawB.Inverse() * (pos - this._rootPos);
    rel.X = leg.StepFromLocal.X + (leg.StepToLocal.X - leg.StepFromLocal.X) * e;
    pos = this._rootPos + yawB * rel;
    leg.SoleTarget = pos;
    if (t >= 1f)
    {
      leg.StepTo = pos; // plant at the corrected spot
    }

    var qa = leg.StepBasisFrom.GetRotationQuaternion();
    var qb = leg.StepBasisTo.GetRotationQuaternion();
    leg.CurBasisW = new Basis(qa.Slerp(qb, e));
    if (prev < 0.5f && leg.StepT >= 0.5f)
    {
      this.StepMidCrossed = true;
    }

    if (leg.StepT >= 1f)
    {
      leg.StepT = -1f;
      leg.Planted = true;
      leg.StepBasisTo = FootBasisAt(leg.StepTo, this._legsYaw); // refresh: the yaw may have turned during the step
      leg.SoleTarget = leg.StepTo;
      leg.PlantBasisW = leg.StepBasisTo;
      leg.CurBasisW = leg.StepBasisTo;
      this._steps++;
      this.LastPlantPos = leg.StepTo;
      this.LastPlantBasis = leg.PlantBasisW;
    }
  }

  private void GaitStep(float h)
  {
    // Thruster flight, integrated per substep like the gait:
    // burn climbs and drains, otherwise gravity rules.
    this.Thrusting = this._thrustHeld && this.JumpJets && this.JumpCharge > 0f && this._fallT < 0f;
    // Thruster flight: the legs hang (owner, 2026-11-11
    // rounds 4-5: no stepping while thrusters are in use,
    // and in the air the legs hang straight). The latch
    // runs from burn start until touchdown: liftoff
    // cancels any step in progress, no new steps start,
    // and both soles are pinned to the hang pose (set
    // after UpdateRoot, below).
    if (this.Thrusting && !this._freezeLegs)
    {
      this._freezeLegs = true;
      foreach (var leg in new[] { this._legL, this._legR }) { leg.StepT = -1f; leg.Planted = true; }
    }
    else if (!this.Thrusting && this._jumpH <= 0f)
    {
      this._freezeLegs = false;
    }

    if (this.Thrusting)
    {
      this.JumpCharge = Mathf.Max(0f, this.JumpCharge - h / ThrustBurnSeconds);
      this._jumpVel = Mathf.Min(this._jumpVel + ThrustAccel * h, ThrustClimbSpeed);
    }
    else if (this._jumpH > 0f)
    {
      this._jumpVel = Mathf.Max(this._jumpVel - FallGravity * h, FallTerminalSpeed);
    }
    else
    {
      this._jumpVel = 0f;
    }

    if (this._jumpH > 0f || this._jumpVel > 0f)
    {
      this._jumpH += this._jumpVel * h;
      if (this._jumpH <= 0f) { this._jumpH = 0f; this._jumpVel = 0f; }
    }
    if (!this._thrustHeld && this.JumpCharge < 1f)
    {
      this.JumpCharge = Mathf.Min(1f, this.JumpCharge + h / ThrustRechargeSeconds); // recharge is gated on the KEY, not on thrust: it starts the moment SPACE is released - ground or air (owner round 22) - and a held key on empty tanks stays dead instead of sputtering
    }

    this._yawRate = Mathf.Abs(WrapAngle(this._legsYaw - this._prevYaw)) / h;
    this._prevYaw = this._legsYaw;

    float tgt = this._fallT >= 0f ? 0f : this._targetSpeed * this.SpeedFactor;
    float dv = this._cfg.Accel * h;
    if (Mathf.Abs(tgt) < Mathf.Abs(this._speed) - BrakeDetectEpsilon)
    {
      // Braking (owner, 2026-10-31): no inertia - a stop from
      // ANY speed completes within one second, whatever the
      // chassis weighs (the spec Accel made the 100t Hector
      // glide 5+ seconds from a run). The brake rate is the
      // episode's peak speed, captured and held, so the stop
      // is linear at >= 1x speed per second - not an
      // exponential glide that never quite arrives. The 1.15
      // factor keeps the measured stop (which includes a
      // frame of detection lag) inside the second, and the
      // rate never drops below the spec's Accel: at low
      // speeds a bare 1.15x rate would brake SLOWER than the
      // mech used to accelerate (the duel's limp-reverse
      // phase caught exactly that).
      // Acceleration still uses the spec's Accel.
      this._brakeV = Mathf.Max(this._brakeV, Mathf.Max(Mathf.Abs(this._speed) * BrakeOvershootFactor, this._cfg.Accel));
      dv = this._brakeV * h;
    }
    else
    {
      this._brakeV = 0f;
    }

    this._speed = Mathf.MoveToward(this._speed, tgt, dv);
    var vel = HeadingVec(this._legsYaw) * this._speed;
    var rootXZ = new Vector2(this._rootPos.X, this._rootPos.Z);
    if (this._cohInit)
    {
      float spd = vel.Length();
      if (spd > MinDriveSpeed)
      {
        this._cohWinNet += (rootXZ - this._cohPrev).Dot(new Vector2(vel.X, vel.Z) / spd); // signed: snap-back subtracts
        this._cohWinWant += spd * h;
      }
      this._cohWinT += h;
      if (this._cohWinT >= CohesionWindowSeconds)
      {
        this._cohRatio = this._cohWinWant > CohesionMinWant ? Mathf.Clamp(this._cohWinNet / this._cohWinWant, 0f, 1f) : 1f;
        this._cohWinNet = 0f; this._cohWinWant = 0f; this._cohWinT = 0f;
      }
    }
    this._cohPrev = rootXZ; this._cohInit = true;
    bool cohBlocked = this._cohRatio < CohesionBlockedRatio && Mathf.Abs(this._targetSpeed) > MinDriveSpeed && this._fallT < 0f;
    this._cohBlockT = cohBlocked ? this._cohBlockT + h : 0f;
    this._rootPos += vel * h;

    var stepping = this._legL.StepT >= 0f ? this._legL : this._legR.StepT >= 0f ? this._legR : null;
    if (stepping == null && this._fallT < 0f && !this._freezeLegs)
    {
      bool settling = Mathf.Abs(this._targetSpeed) < MinDriveSpeed && Mathf.Abs(this._speed) < SettleSpeedThreshold && this._yawRate < SettleYawRateThreshold;
      // Pendulum pair to BeginStep's plant lead: a planted
      // foot lifts once it trails half a stride cycle
      // behind its home (lag measured against the CURRENT
      // root - the old +vel*0.30 reference and the fixed
      // StepThreshold made the trail speed-independent).
      // The 0.65 floor keeps crawl and turn-in-place steps
      // from shrinking to shuffles.
      float threshold = settling ? SettleStepThreshold : Mathf.Min(Mathf.Max(TrailFloor, this._speed * this._cfg.StepTime * 0.5f), Mathf.Max(this._strideBudgetW, TrailFloor));
      Leg best = null; float bestLag = threshold;
      foreach (var leg in new[] { this._legL, this._legR })
      {
        if (!leg.Planted || !this.Other(leg).Planted)
        {
          continue;
        }

        var home = this.HomeSlot(leg, this._rootPos);
        float lag = new Vector2(leg.SoleTarget.X - home.X, leg.SoleTarget.Z - home.Z).Length();
        if (lag > bestLag) { bestLag = lag; best = leg; }
      }
      // No blocked special case (owner, 2026-11-11
      // round 3): the two gait behaviours tried earlier
      // today - stand still, then march in place - are
      // both REMOVED. Against a collider the generic
      // trigger below is all there is: whatever the rig
      // does at a wall is what it does everywhere.
      if (best != null)
      {
        this.BeginStep(best);
      }
    }
    this.AdvanceLeg(this._legL, h);
    this.AdvanceLeg(this._legR, h);
    // Dynamic ground (owner: Harbor lift pads): a surface
    // moving under a planted foot carries the foot with
    // it - up always, down when the gap is small (a
    // descending pad). Static ground re-samples identical,
    // so ordinary walking is untouched.
    if (!this._freezeLegs)
    {
      foreach (var leg in new[] { this._legL, this._legR })
      {
        if (!leg.Planted)
        {
          continue;
        }

        float g = this.GroundHere(leg.SoleTarget.X, leg.SoleTarget.Z);
        float diff = g - leg.SoleTarget.Y;
        if (diff > GroundCarryEpsilon || diff < -GroundCarryEpsilon && diff > -GroundCarryDropMax)
        {
          leg.SoleTarget = new Vector3(leg.SoleTarget.X, g, leg.SoleTarget.Z);
        }
      }
    }
    // A wrecked leg steps like any other (see the state comment
    // above): no drag override - its stride shortens naturally
    // with the rider's throttled speed.

    this.UpdateRoot(h);
    if (this._freezeLegs)
    {
      // The hang pose (owner, 2026-11-11 round 5): each
      // sole sits directly under its hip, a touch below
      // full leg extension - SolveLeg clamps the ankle
      // to reach along the hip->target line, so an
      // over-long drop renders the leg straight down.
      // Recomputed from the live skeleton transform
      // every substep, so the legs ride the body without
      // stepping, swinging or stretching. (Round 4's
      // rigid carry of the liftoff pose is REMOVED: it
      // fed DesiredRootY and pumped the root's ground
      // reference to 'outer space' - see UpdateRoot.)
      float st = this._skelGlobal.Basis.X.Length();
      foreach (var leg in new[] { this._legL, this._legR })
      {
        var hipW = this._skelGlobal * leg.Hrest;
        float drop = (leg.L1 + leg.L2) * st + leg.AnkleAboveSoleW + HangDropExtra;
        leg.SoleTarget = new Vector3(hipW.X, hipW.Y - drop, hipW.Z);
        leg.PlantBasisW = FlatFootBasis(this._legsYaw);
        leg.CurBasisW = leg.PlantBasisW;
      }
    }
    this.SolveAll();
    this.TrackSlide();
  }

  private float DesiredRootY()
  {
    float refL = (this._legL.Planted ? this._legL.SoleTarget.Y : this._legL.StepTo.Y) + this._legL.AnkleAboveSoleW;
    float refR = (this._legR.Planted ? this._legR.SoleTarget.Y : this._legR.StepTo.Y) + this._legR.AnkleAboveSoleW;
    return 0.5f * (refL + refR) + this._hipRiseW - this._hipAboveHolderW + this._bob;
  }

  private void UpdateRoot(float h)
  {
    bool doubleSupport = this._legL.Planted && this._legR.Planted;
    float bobT = doubleSupport ? BobDoubleSupport : BobSingleSupport;
    this._bob = Mathf.Lerp(this._bob, bobT, 1f - Mathf.Exp(-BobEaseRate * h));
    // In thruster flight the root's ground reference is
    // the surface under the ROOT, never the feet (owner
    // round 5: with the feet feeding DesiredRootY, the
    // root chased its own carried feet upward - the
    // rider climbed to 'outer space' and hovered there
    // until a step re-planted a foot on real ground).
    float yT = this._freezeLegs
        ? this.GroundHere(this._rootPos.X, this._rootPos.Z) + 0.5f * (this._legL.AnkleAboveSoleW + this._legR.AnkleAboveSoleW) + this._hipRiseW - this._hipAboveHolderW + this._bob
        : this.DesiredRootY();
    // Knocked down: the hips drop to rest height (body on the ground).
    if (this._fallAmt > FallAmountEpsilon)
    {
      float restY = Mathf.Min(this._legL.SoleTarget.Y, this._legR.SoleTarget.Y) + FallRestHeight;
      yT = Mathf.Lerp(yT, restY, this._fallAmt);
    }
    this._groundY = Mathf.Lerp(this._groundY, yT, 1f - Mathf.Exp(-GroundFollowRate * h));
    this._rootPos.Y = this._groundY + this._jumpH; // the arc itself is NOT smoothed - full ballistic height

    // Lateral pelvis sway REMOVED (owner, round 22 follow-up):
    // shifting the pelvis sideways over the stance foot slants
    // the support leg inward against its planted foot - from
    // the front the legs must read straight, so the pelvis
    // stays centered between the feet and only the vertical
    // bob remains. (GAITDBG audit: sway displaced the hips
    // 0.23u against ankles planted exactly at home +-1.956.)
    var swayT = Vector3.Zero;
    this._sway = this._sway.Lerp(swayT, 1f - Mathf.Exp(-SwayEaseRate * h));

    // Up vector: roll from the two feet heights across the lateral axis,
    // pitch from the terrain gradient along the heading.
    var sL = this._legL.Planted ? this._legL.SoleTarget : this._legL.StepTo;
    var sR = this._legR.Planted ? this._legR.SoleTarget : this._legR.StepTo;
    var right = new Vector3(Mathf.Cos(this._legsYaw), 0f, -Mathf.Sin(this._legsYaw));
    float latL = (sL - this._rootPos).Dot(right);
    float latR = (sR - this._rootPos).Dot(right);
    var sHi = latR > latL ? sR : sL;
    var sLo = latR > latL ? sL : sR;
    float span = Mathf.Max(Mathf.Abs(latR - latL), MinFootSpan);
    float dhLat = (sHi.Y - sLo.Y) / span;
    var fwd = HeadingVec(this._legsYaw);
    float dhFwd = (Main.HeightAt(this._rootPos.X + fwd.X * TiltGradientProbe, this._rootPos.Z + fwd.Z * TiltGradientProbe)
                 - Main.HeightAt(this._rootPos.X - fwd.X * TiltGradientProbe, this._rootPos.Z - fwd.Z * TiltGradientProbe)) / (2f * TiltGradientProbe);
    var upT = (Vector3.Up - right * dhLat - fwd * dhFwd).Normalized();
    float tilt = Mathf.Acos(Mathf.Clamp(upT.Dot(Vector3.Up), -1f, 1f));
    if (tilt > MaxRootTilt)
    {
      upT = Vector3.Up.Lerp(upT, MaxRootTilt / tilt).Normalized();
    }
    // Limp: permanent lean toward a dead leg, on top of terrain tilt.
    // upT - right*sin leans the body's top toward its anatomical left.
    if (this.LimpRoll != 0f)
    {
      upT = (upT - right * Mathf.Sin(this.LimpRoll)).Normalized();
    }
    // Knockdown: the up vector tips toward the fall direction by up
    // to ~76 degrees, scaled by the fall amount.
    if (this._fallAmt > FallAmountEpsilon)
    {
      float ang = FallTipAngle * this._fallAmt;
      var fallUp = (Vector3.Up * Mathf.Cos(ang) + this._fallDirW * Mathf.Sin(ang)).Normalized();
      upT = (upT * (1f - this._fallAmt) + fallUp * this._fallAmt).Normalized();
    }
    this._upEased = this._upEased.Lerp(upT, 1f - Mathf.Exp(-(this._fallAmt > FallAmountEpsilon ? FallUpEaseRate : UpEaseRate) * h)).Normalized();

    var q = new Quaternion(Vector3.Up, this._upEased);
    this._rootBasis = new Basis(q) * Basis.Identity.Rotated(Vector3.Up, this._legsYaw + Mathf.Pi);
    if (this._joltEnv > MinJoltEnvelope)
    {
      this._rootBasis = this._rootBasis
                * Basis.Identity.Rotated(Vector3.Right, this._joltPitch * this._joltEnv)
                * Basis.Identity.Rotated(Vector3.Forward, this._joltRoll * this._joltEnv);
      this._joltEnv = Mathf.Max(this._joltEnv - h * JoltDecayRate, 0f);
    }

    this.RefreshPose();
  }

  private void RefreshPose()
  {
    var pos = this._rootPos + this._sway;
    this._holderXf = new Transform3D(this._rootBasis.Scaled(Vector3.One * this._scaleF), pos);
    this.Transform = this._holderXf;
    this._skelGlobal = this._holderXf * this._chain;
    this.SolveAll();
  }

  // ---------- IK ----------

  private void SolveAll()
  {
    this.SolveLeg(this._legL);
    this.SolveLeg(this._legR);
    this.SolveTorso();
  }

  private void SolveTorso()
  {
    if (this._torsoBone < 0 || this._skel == null)
    {
      return;
    }
    // Desired torso world frame: the root frame, twisted by torso
    // yaw/pitch. The trailing Yaw(Pi) undoes the model's rest flip:
    // at twist = 0 this must reproduce the rest pose exactly
    // (root * Yaw(Pi)^-1 * M0 == M0 when legsYaw = 0), otherwise the
    // torso renders 180 degrees off the legs.
    // Pitch sign: the trailing Yaw(Pi) rest-flip conjugates the
    // X rotation, so feeding +pitch here rendered the torso nose-
    // DOWN while the aim model treats +pitch as up - mouse up
    // visibly dipped the rider (owner's report). Feed (Droop -
    // pitch): rendered pitch = pitch - Droop, so +pitch points
    // the torso UP (-18/+27 deg working range) and the death
    // droop still slumps it nose-down, up to ~49 deg.
    var f = this._rootBasis.Orthonormalized()
            * Basis.Identity.Rotated(Vector3.Up, this._torsoTwist)
            * Basis.Identity.Rotated(Vector3.Right, Mathf.Clamp(this.Droop - this._torsoPitch, TorsoRenderPitchMin, TorsoRenderPitchMax))
            * Basis.Identity.Rotated(Vector3.Up, Mathf.Pi);
    var basisSk = this._skelGlobal.Basis.Orthonormalized().Transposed() * (f * this._torsoM0);
    // CS0618: Godot deprecated SetBoneGlobalPoseOverride with no drop-in
    // replacement (the amount blend + persistence have no equivalent short
    // of a SkeletonModifier3D re-architecture - owner's call, flagged
    // 2026-11-28). Scoped suppression; the gait depends on this API.
#pragma warning disable CS0618
    this._skel.SetBoneGlobalPoseOverride(this._torsoBone,
            new Transform3D(basisSk, this._torsoRestOrigin), 1f, true);
#pragma warning restore CS0618
  }

  // World yaw of the torso as the engine renders it (verification):
  // track the rest facing direction through the bone's actual world basis.
  public float EngineTorsoYaw()
  {
    if (this._torsoBone < 0 || this._skel == null)
    {
      return this.TorsoYawWorld;
    }

    var f = this.EngineTorsoFacing();
    return YawOf(new Vector3(f.X, 0f, f.Z).Normalized());
  }

  // The torso's facing as the engine actually renders it, Y
  // included - the ground truth for pitch direction checks.
  public Vector3 EngineTorsoFacing()
  {
    if (this._torsoBone < 0 || this._skel == null)
    {
      return Vector3.Forward;
    }

    var pose = this._skel.GetBoneGlobalPose(this._torsoBone);
    var w = this._skel.GlobalTransform.Basis.Orthonormalized() * pose.Basis.Orthonormalized();
    return (w * this._torsoFwdBoneLocal).Normalized();
  }

  private void SolveLeg(Leg leg)
  {
    var sole = leg.SoleTarget;
    var basisW = leg.Planted ? leg.PlantBasisW : leg.CurBasisW;
    float st = this._skelGlobal.Basis.X.Length();
    var ankleWorld = sole - st * (basisW * leg.SoleOffFrame);
    var inv = this._skelGlobal.AffineInverse();
    var A = inv * ankleWorld;
    var H = leg.Hrest;
    var toA = A - H;
    float reach = leg.L1 + leg.L2;
    float d = Mathf.Clamp(toA.Length(), reach * IkMinReachFraction, reach * IkMaxReachFraction);
    var dir = toA.Normalized();
    A = H + dir * d;
    float a = (leg.L1 * leg.L1 - leg.L2 * leg.L2 + d * d) / (2f * d);
    float hh = Mathf.Sqrt(Mathf.Max(leg.L1 * leg.L1 - a * a, 0f));
    var pole = leg.PoleSk - dir * leg.PoleSk.Dot(dir);
    if (pole.LengthSquared() < MinPoleLengthSq)
    {
      pole = dir.Cross(Vector3.Right).Normalized();
    }
    else
    {
      pole = pole.Normalized();
    }

    var K = H + dir * a + pole * hh;

    var qHip = new Quaternion(leg.ThighRestDir, (K - H).Normalized());
    // CS0618: Godot deprecated SetBoneGlobalPoseOverride with no drop-in
    // replacement (the amount blend + persistence have no equivalent short
    // of a SkeletonModifier3D re-architecture - owner's call, flagged
    // 2026-11-28). Scoped suppression; the gait depends on this API.
#pragma warning disable CS0618
    this._skel.SetBoneGlobalPoseOverride(leg.Hip, new Transform3D(new Basis(qHip) * leg.HrestBasis, H), 1f, true);
    var qKnee = new Quaternion(leg.ShinRestDir, (A - K).Normalized());
    this._skel.SetBoneGlobalPoseOverride(leg.Knee, new Transform3D(new Basis(qKnee) * leg.KrestBasis, K), 1f, true);
    var ankleBasisSk = this._skelGlobal.Basis.Orthonormalized().Transposed() * (basisW * leg.M0);
    leg.SolvedAnklePose = new Transform3D(ankleBasisSk, A);
    this._skel.SetBoneGlobalPoseOverride(leg.Ankle, leg.SolvedAnklePose, 1f, true);
#pragma warning restore CS0618
    leg.SolvedSoleWorld = this._skelGlobal * (leg.SolvedAnklePose * leg.SoleOffLocal);
  }

  private void TrackSlide()
  {
    foreach (var leg in new[] { this._legL, this._legR })
    {
      if (leg.Planted)
      {
        var soleW = this._skelGlobal * (leg.SolvedAnklePose * leg.SoleOffLocal);
        leg.LastPlantedDiff = soleW.Y - Main.HeightAt(soleW.X, soleW.Z);
      }
      // A wrecked leg still plants and steps; keep it exempt
      // from slide scoring while the limp lean settles.
      if (!leg.Disabled && leg.Planted && leg.HasPrev)
      {
        float drift = new Vector2(leg.SolvedSoleWorld.X - leg.PrevSoleWorld.X,
                                   leg.SolvedSoleWorld.Z - leg.PrevSoleWorld.Z).Length();
        if (drift > this._maxSlide)
        {
          this._maxSlide = drift;
        }

        this._plantedFrames++;
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
        var pose = this._skel.GetBoneGlobalPose(leg.Ankle);
        sole = this._skel.GlobalTransform * (pose * leg.SoleOffLocal);
      }
      else
      {
        sole = leg.SolvedSoleWorld;
      }

      float terr = Main.HeightAt(sole.X, sole.Z);
      float diff = sole.Y - terr;
      return leg.Planted
          ? $"{leg.Name}: PLANTED soleY={sole.Y:F3} terrY={terr:F3} diff={diff:+0.000;-0.000}"
          : $"{leg.Name}: SWING soleY={sole.Y:F3} terrY={terr:F3} clearance={diff:F3}";
    }
    return $"{One(this._legL)} | {One(this._legR)}";
  }

  public float MaxPlantedDiff()
  {
    float m = 0f;
    foreach (var leg in new[] { this._legL, this._legR })
    {
      if (leg.Planted)
      {
        m = Mathf.Max(m, Mathf.Abs(leg.LastPlantedDiff));
      }
    }

    return m;
  }

  // ---------- weapon feedback (death darkening) ----------

  private readonly System.Collections.Generic.List<(StandardMaterial3D Mat, Color Base, MeshInstance3D Mesh)> _mats = new();

  // Materials are duplicated per walker instance at Init so a
  // darkening on one rider never leaks into the other (the FBX mesh
  // resources themselves are shared).
  private void CollectMaterials() => this.CollectMats(this._inner);

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
          this._mats.Add((dup, dup.AlbedoColor, mi));
        }
      }
    }
    foreach (var c in n.GetChildren())
    {
      this.CollectMats(c);
    }
  }


  // Impact jolt: the body rocks under a hit and eases back over
  // ~0.4s. Pitch/roll are the push direction decomposed against the
  // legs' facing at the moment of the hit.
  private float _joltPitch, _joltRoll, _joltEnv;
  public float JoltEnv => this._joltEnv;
  public void Jolt(Vector3 worldPushDir, float strength)
  {
    if (worldPushDir.LengthSquared() < MinDirLengthSq || strength <= 0f)
    {
      return;
    }

    var fwd = HeadingVec(this._legsYaw);
    var right = new Vector3(-fwd.Z, 0f, fwd.X);
    this._joltPitch = -worldPushDir.Dot(fwd) * strength * JoltAngleScale;
    this._joltRoll = worldPushDir.Dot(right) * strength * JoltAngleScale;
    this._joltEnv = 1f;
  }

  public void SetDarkened(bool dark)
  {
    foreach (var (m, b, _) in this._mats)
    {
      m.AlbedoColor = dark ? b * 0.5f : b;
    }
  }

  // ---------- helpers ----------

  private static Skeleton3D FindSkeleton(Node n)
  {
    if (n is Skeleton3D s)
    {
      return s;
    }

    foreach (var c in n.GetChildren())
    {
      var r = FindSkeleton(c);
      if (r != null)
      {
        return r;
      }
    }
    return null;
  }

  private static Aabb MeasureWorldAabb(Node node)
  {
    var box = new Aabb();
    bool first = true;
    Collect(node, ref box, ref first, null);
    return box;
  }

  private static Aabb MeasureWorldAabb(Node node, HashSet<string> excludeBones)
  {
    var box = new Aabb();
    bool first = true;
    Collect(node, ref box, ref first, excludeBones);
    return box;
  }

  // Bones of both arm chains (clavicle/shoulder/elbow and their
  // descendants) by name, from the rig contract every rider shares.
  private static HashSet<string> ArmChainBones(Skeleton3D skel)
  {
    var set = new HashSet<string>();
    if (skel == null)
    {
      return set;
    }

    for (int i = 0; i < skel.GetBoneCount(); i++)
    {
      int b = i;
      while (b >= 0)
      {
        string n = skel.GetBoneName(b);
        if (Bones.ArmChainRoots.Contains(n))
        { set.Add(skel.GetBoneName(i)); break; }
        b = skel.GetBoneParent(b);
      }
    }
    return set;
  }

  // The bind bone carrying most of a mesh's vertex weight (same
  // tally as RiderParts.DominantBone), or "" when unskinned.
  private static string DominantBindName(MeshInstance3D mi)
  {
    if (mi.Skin == null || mi.Mesh == null)
    {
      return "";
    }

    var tally = new Dictionary<int, double>();
    for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
    {
      var arrays = mi.Mesh.SurfaceGetArrays(s);
      var bones = arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
      var weights = arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
      int nv = weights.Length / 4;
      for (int v = 0; v < nv; v++)
      {
        for (int k = 0; k < 4; k++)
        {
          float w = weights[v * 4 + k];
          if (w > MinBindWeight)
          {
            int b = bones[v * 4 + k];
            tally[b] = (tally.TryGetValue(b, out var c) ? c : 0) + w;
          }
        }
      }
    }
    int best = -1; double bv = 0;
    foreach (var kv in tally)
    {
      if (kv.Value > bv) { bv = kv.Value; best = kv.Key; }
    }

    if (best < 0 || best >= mi.Skin.GetBindCount())
    {
      return "";
    }

    return mi.Skin.GetBindName(best);
  }

  private static void Collect(Node node, ref Aabb box, ref bool first, HashSet<string> excludeBones)
  {
    if (node is MeshInstance3D mi && (excludeBones == null || excludeBones.Count == 0
        || !excludeBones.Contains(DominantBindName(mi))))
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
        else
        {
          box = box.Expand(w);
        }
      }
    }
    foreach (var child in node.GetChildren())
    {
      Collect(child, ref box, ref first, excludeBones);
    }
  }
}
