using Godot;
using System.Collections.Generic;

// Titans: two-rider duel - a PLAYER rider vs an ENEMY rider, each
// fielding a chassis chosen in the RiderLab (Enyo / Hector
// are the two chassis in Data/RiderSpec.cs; code names the ROLES,
// the specs name the machines). W/S throttle along the legs
// heading, A/D turn the legs, mouse twists/pitches the torso
// (full 360), chase camera on the torso facing, crosshair.
// The enemy brain advances when far, backs off when close and
// orbits in the mid band; its torso tracks the player.
// Collisions are kinematic circles resolved in code (no physics
// bodies); terrain height stays with Main.HeightAt.
//
// This class is split into partial modules (round 22):
//   Titans.cs            fields, _Ready, _Process, combat stepper, reset
//   Titans.Player.cs     input            Titans.Enemy.cs    AI + gunnery
//   Titans.Weapons.cs    aim/fire/damage  Titans.Fx.cs       particles/feedback
//   Titans.Audio.cs      sound            Titans.Environment.cs world + props
//   Titans.Lab.cs        lab/construction Titans.Hud.cs      cockpit panel
//   Titans.Camera.cs     captures/camera  Titans.Harness.cs  test drivers
// The duel scene (Duel.tscn, the project's main
// scene) runs this same class with the photo dressing on.
// Verification: --duel-test / --weapons-test / --breakable-test /
// --terrain-test / --photo-test run scripted choreographies and
// capture proof frames.

public partial class Titans : Node3D
{
    private const float EnemyMaxSpeed = 2.2f; // AI tactical pace cap; the chassis cap is RiderSpec.MaxSpeed
    private const string HectorChaseCapture = "res://proof/hector_player_chase.png"; // harness capture output
    private float EnemyPace => Mathf.Min(_enemySpec.MaxSpeed, EnemyMaxSpeed);
    // Mission fronts: beyond engagement range the AI MARCHES at full
    // chassis speed toward its target instead of creeping at the
    // tactical cap - a 1000u approach at 2.2 u/s would take 8 minutes.
    private float PaceFor(RiderSpec spec, float distToTarget) =>
        WorldConfig.IsMission && distToTarget > MarchRange ? spec.MaxSpeed
        : Mathf.Min(spec.MaxSpeed, EnemyMaxSpeed);
    private const float MarchRange = 130f; // mission approach distance past which the AI marches at full chassis speed (see PaceFor)
    private const float RunFactor = 3.0f; // SHIFT run mode: walk speed x3 (owner, 2026-10-29; was x1.65)
    private const float TurnRate = 1.35f; // A/D leg turn, rad/s (owner, 2026-11-11: 'keys A and D should rotate rider faster' - was 0.85)
    private const float ShakeDecayRate = 1.6f; // camera shake bleeds off at this rate
    private const int FxMinFrames = 3; // an FX node must survive at least this many rendered frames (see the _fx step)
    private const float MaxSimStepSeconds = 0.09f; // walker substep: the largest simulated step
    // Lab showroom camera (owner's tuned framing): centre
    // height, view angle, distance and pan, scaled by the
    // chassis over ReferenceChassisHeight like the chase cam.
    private const float LabCamCenterHeight = 5.6f;
    private const float LabCamViewAngle = 2.72f;
    private const float LabCamDistance = 14.5f;
    private const float LabCamHeight = 3.4f;
    private const float LabCamPanAngle = 0.46f;
    private static float WorldBound => WorldConfig.WorldBound; // arena 115 (260 world); Mission front 915 (1900 world)

    // Scene look (owner: look-and-feel values live in named
    // fields): the duel's sun, lab fill, hero fill, sky and
    // fog colours and the HUD bar glass, defined once here
    // and consumed by the _Ready scene build.
    private static readonly Color SunColor = new(1f, 0.955f, 0.885f);
    private static readonly Color LabFillColor = new(0.9f, 0.95f, 1f);
    private static readonly Color HeroFillColor = new(0.85f, 0.92f, 1f);
    private static readonly Color DuelSkyTopColor = new(0.20f, 0.40f, 0.71f);
    private static readonly Color DuelSkyHorizonColor = new(0.63f, 0.73f, 0.84f);
    private static readonly Color DuelGroundBottomColor = new(0.16f, 0.145f, 0.12f);
    private static readonly Color DuelGroundHorizonColor = new(0.56f, 0.56f, 0.52f);
    private static readonly Color FogDesertColor = new(0.82f, 0.72f, 0.56f);
    private static readonly Color FogCityColor = new(0.45f, 0.54f, 0.62f);
    private static readonly Color FogForestColor = new(0.63f, 0.73f, 0.84f); // matches the sky horizon by design
    private static readonly Color BarTrackColor = new(0.03f, 0.05f, 0.08f, 0.9f);
    private static readonly Color BarBorderColor = new(0.4f, 0.6f, 0.7f, 0.35f);

    // Scene rig numbers (the _Ready build): sun angle/energy/
    // shadow, the lab and hero fills, sky curves, ambient and
    // fog, and the staged-build cover's canvas layer.
    private const float SunPitchDeg = -48f, SunYawDeg = -35f;
    private const float SunShadowOpacity = 0.55f, SunEnergy = 1.45f;
    private const float SunShadowMaxDistance = 320f;
    private const float LabFillEnergy = 0.85f;
    private const float HeroFillEnergy = 0.30f, HeroFillRange = 24f;
    private const float DuelSkyCurve = 0.09f, DuelGroundCurve = 0.08f;
    private const float DuelAmbientEnergy = 0.34f;
    private const float FogDensityMission = 0.00042f, FogDensityArena = 0.0011f;
    private const float FogSunScatter = 0.25f;
    private const int BuildCoverLayer = 10;

    private RiderWalker _player;
    private RiderWalker _enemy;
    private Camera3D _cam;
    private DirectionalLight3D _labLight; // lab-only fill: the rider must read as the centerpiece, not a silhouette
    private OmniLight3D _heroLight; // soft shadowless pool over the player rider: it must stay readable in ANY shadow (owner, 2026-11-11)
    private bool _test;
    private bool _labShot; // dev flag: capture the live RiderLab to /tmp/lab_live.png and quit
    private bool _labEquipTab; // dev flag: --lab-tab=equip opens the lab capture on the Equipment tab
    private bool _hectorCam; // dev flag: swap player to Hector, force-deploy, capture the chase view
    private bool _multiShot; // dev flag: multi-fit the lab loadout before the --lab-shot capture
    private bool _hectorLab; // dev flag: open the lab on the Hector for the --lab-shot capture
    private RiderId? _labRiderId; // dev flag: --lab-rider=<id> opens the lab on that rider for the --lab-shot capture
    private bool _layoutDumped;
    private int _labShotFrames;
    private bool _hectorCamDone; private float _hectorCamT; private bool _hectorCamFired;
    // Set by the photoreal scene itself (Duel.tscn
    // carries PhotoMode = true in its root's scene data): the
    // scene declares what it is, the engine never sniffs names.
    [Export] public bool PhotoMode;
    private bool _photo; // photoreal environment (PhotoEnv / photo/)
    private bool _photoTest; // --photo-test: capture overview + ground frames, print stats, quit
    private bool _grassShot; // TEMP probe: census + capture the grass clump instances
    private bool _boomShot; // TEMP probe: rider visibility across the pitch travel (round 28)
    private bool _leaveShot, _leaveShotDone, _leaveShotSaved; // proof hook: frame the exit popup
    private float _leaveShotT;
    private bool _lagCheck; // TEMP probe: camera lag + rock/tree movement rules (round 29)
    private bool _flipbookTest; // TEMP probe: Freelancer flipbook fireball (explosion prototype)
    private bool _grassStaged;
    private Vector3 _grassPos;
    private bool _gaitShot; // --gait-shot: dev flag - capture the enemy mid-stride (enemySide), quit
    private float _gaitT;
    private float _gaitDumpT;
    private PhotoPhase _photoPhase;
    private float _photoT, _fpsSum;
    private ulong _warmStart; // photo-mode shader warmup (lab): wall-clock start, 0 = not begun
    // Warmup camera poses (owner, 2026-11-27: an enum, not a
    // bare index - Done ends the sweep).
    private enum WarmPose { Overhead, Ground, Vista, Lab, Done }
    private WarmPose _warmPose;
    // The warmup poses' framing (rider-relative offsets) and
    // their render-time slices.
    private static readonly Vector3 WarmOverheadOffset = new(58f, 34f, 64f);
    private static readonly Vector3 WarmGroundOffset = new(-21f, 0f, -1f);
    private static readonly Vector3 WarmVistaOffset = new(-88f, 44f, 90f);
    private static readonly Vector3 WarmLabOffset = new(10f, 3.4f, 10f);
    private const float WarmLookHeight = 2f, WarmGroundEyeHeight = 2.1f, WarmGroundLookHeight = 5.5f;
    private const ulong WarmOverheadSliceEndMs = 900, WarmGroundSliceEndMs = 1800, WarmVistaSliceEndMs = 2600, WarmLabSliceEndMs = 3400;
    private const int WarmMinFramesPerPose = 2;
    private int _warmPoseFrames; // frames rendered at the current pose
    private bool _warmFx;   // warmup FX burst fired
    private int _fpsN;
    private double _simT;

    // interactive state
    private float _playerYaw;
    private float _torsoWorldYaw; // mouse-owned torso bearing (round 7)
    private float _enemyYaw = Mathf.Pi;
    private float _orbitDir = 1f;
    private float _orbitTimer;

    // capture state
    private bool _frozen, _capturing;
    private string _pendingPath, _pendingTag;
    private CamMode _camMode = CamMode.Chase;
    private System.Action _afterCapture;

    // test choreography
    private DuelPhase _phase;
    private double _phaseT;
    private StrideShot _strideShot;
    private float _minTreeSep, _minRiderSep;
    private PlayerLimpStep _pLimpStep;
    private AimStep _aimStep;
    private double _aimT0;
    private int _aimProj0;
    private float _aimWorld0, _aimLegs0;
    private float _aimUpY;
    private double _pLimpT0;
    private Vector3 _pLimpStart;
    private float _pLimpYaw0;
    private Vector3 _lastPlayerPos;
    private float _stallTimer;
    private bool _twistLogged;

    // ---------- combat ----------
    private HitModel _hit;
    private RiderParts _partsPlayer, _partsEnemy;
    private readonly System.Collections.Generic.List<Projectile> _shots = new();
    private readonly System.Collections.Generic.List<(Node3D Node, float Life, int Frames)> _fx = new();
    private float _hpPlayer = 260f, _hpEnemy = 340f;
    private bool _playerDead, _enemyDead;
    private float _cdLaser, _cdAc, _cdLrm, _cdEnemy;
    private int _weaponSel; // index into _groups (the player's lab-assigned firing groups)
    // RiderLab loadout (owner): the equipped weapon defs drive damage,
    // cooldowns, salvo composition and knockdown weight. Defaults equal
    // the original fixed fit, so every harness number is unchanged.
    private LoadoutState _loadout = LoadoutState.DefaultFor(RiderSpecs.Enyo);
    private LoadoutState _enemyLoadout = LoadoutState.DefaultFor(RiderSpecs.Hector);

    // Free-for-all (owner, 2026-10-31): the match fields TWO AI
    // opponents - a Enyo and an Hector - and every rider
    // fights every other; there are no teams. The legacy enemy
    // keeps its fields above; the foe is this record. The foe
    // is not built in the precision test modes (duel / weapons /
    // breakable / terrain), whose choreographies assume exactly
    // two riders on the field.
    private sealed class Foe
    {
        public RiderWalker Walker;
        public RiderParts Parts;
        public RiderSpec Spec;
        public LoadoutState Loadout;
        public float Hp;
        public bool Dead;
        public WeaponDef DefBallistic;
        public PartId? BallisticSlot;
        public float Cd;
        public float Yaw = Mathf.Pi;
        public float OrbitDir = 1f;
        public float OrbitTimer;
        public float ThinkT;
        public float DroopT;
        public Vector3 PrevRoot;
        public Vector3 Vel;
        // Mission guard duty: engagement latch + patrol point.
        public bool Engaged;
        public bool Alerted; // hit by the player: engages at any distance (owner, 2026-11-25)
        public Vector3 Patrol;
        public float PatrolT;
    }
    private Foe _foe;
    private bool PrecisionTest => _test || _weaponsTest || _breakTest || _terrainTest;
    private bool _ffaTest; // --ffa-test: staged three-way AI probe
    private bool _teamTest; // --team-test: staged Story-team probe (allies fight, orders, leader pass, mission end)
    private bool _bracketTest; // --bracket-test: staged Tournament bracket probe (NOT in AnyHarness - the bracket alignment under test is live-path code)
    private bool _deployTest; // --deploy-test: staged lab->deploy roster rebuild probe (also NOT in AnyHarness, same reason)
    private bool _missionTest; // --mission-test: Mission front smoke (world facts, base/vista captures, AI march)
    private bool _freezeTest; // AOT_TERRATEST=14: thruster leg freeze probe (own dispatch - stages on the home plaza, no terrace hooks)
    private bool _longshotTest; // --longshot-test: shell line vs sight line at 600u (parallel-fire proof)
    private bool _rangeTest; // --range-test: per-weapon Range enforcement proofs (laser reach, LRM clamp, shell burnout)
    private int _foeShots;
    private bool _lrmProbe; // --lrm-probe: staged rocket-impact measurement
    private bool _boneProbe; // --bone-probe: per-rider torso-bone local offsets (the obstruction origin audit)
    private bool _ppcProbe; // --ppc-probe: PPC damage at a staged 910u lane (owner report 2026-11-23)
    private bool _debrisProbe; // --debris-probe: break a Hector on a steep slope, walk him off, dump the debris rest state
    private bool _guardProbe; // --guard-probe: stage the player (then a teammate) beside a village guard, print its target
    private bool _ffProbe; // --ff-probe: teammate behind the player on the camera axis, salvo fired forward - does he get hit?
    private bool _sfxProbe; // --sfx-probe: burst of pool sounds after a laser shot - does the laser's player get stolen mid-clip?
    private bool _targetProbe; // --target-probe: Babylon spawn - is the active target the nearest foe, and after its death the next nearest?
    private bool _feedProbe; // --feed-probe: after an R reset (and with the whole wing dead) do team orders still announce in the feed?
    private bool _ambientProbe; // --ambient-probe: wind bed at level in the field, servo bed silent at rest and up at walk speed?
    private bool _beamShot; // --beam-shot: Hector player fires a PPC volley and a laser volley at a staged enemy, one frame captured after each
    private bool _bindingsProbe; // --bindings-probe: synthetic TAB-family + order keys through the real _Input path
    private bool _stoneShot; // --stone-shot: close capture of the steepest high slope on the mission map (the mountain-stone check)
    private Vector3 _stoneCamPos, _stoneCamTarget; // --stone-shot: where the stone camera parks and what it faces
    private bool _controlsShot; // --controls-shot: open the F1 controls popup and capture it
    private bool _viewShot; // --view-shot: hilltop view-pitch proof frames
    private bool _pointTest; // --point-test: can the reticle be put ON a hilltop foe, per range?
    private bool _pointHectorFirst; // --point-hector: select Hector at start (the lab path)
    private bool _pointCenter; // --point-center: the foe stands at the mesa top CENTER
    private bool _pointWalk; // --point-walk: the player walks at the hill during the sweep
    private bool _pointClose; // --point-close: close-steep stands just under the rim (the owner's screenshot position, 2026-11-01)
    private bool _pointSwapDone;
    private int _pointStage = -1;
    private float _pointPitch, _pointBest, _pointBestPitch, _pointT;
    private float _pointDbgNext;
    private bool _pointHector;
    private bool _brakeTest; // --brake-test: stop-within-1s measurement, both chassis
    private readonly System.Collections.Generic.List<(string Target, Vector3 Point)> _lrmImpacts = new();
    private Vector3 _enemyVel;
    private Vector3 _prevEnemyRoot;
    private RiderWalker _foe0TargetW; // foe 0's current AI target (re-picked on a think timer)
    private float _foe0ThinkT;
    // Which rider each side fields (round 22: chosen in the lab).
    private RiderSpec _playerSpec = RiderSpecs.Enyo;
    private RiderSpec _enemySpec = RiderSpecs.Hector;
    private WeaponDef _defEnergy, _defBallistic, _defPodL, _defPodR, _defEnemyBallistic;
    private PartId? _enemyBallisticSlot;
    private readonly System.Collections.Generic.List<(PartId Slot, WeaponDef Def)> _gunsEnergy = new(), _gunsBallistic = new(), _podGunsL = new(), _podGunsR = new();
    // Firing groups (owner): the player assigns every fitted
    // weapon to a group in the lab; the duel fires the selected
    // group as one volley (each weapon through its own firing
    // path - beams, shells, or a pod salvo) and the group's
    // cooldown is its slowest fired weapon's, as volleys always
    // were. Path: 0 laser, 1 cannon, 2 pod L, 3 pod R.
    // One fitted weapon inside a group. Cooldowns are PER
    // WEAPON (owner): the dashboard shows a bar per weapon, so
    // a group volley fires whichever of its weapons are ready
    // and each fired weapon cools down on its own clock - the
    // old group-cools-by-its-slowest rule is gone.
    private sealed class WpnGun
    {
        public PartId Slot;
        public WeaponDef Def;
        public FirePath Path;
        public float Cd;
    }
    private sealed class WpnGroup
    {
        public int Num;
        public readonly System.Collections.Generic.List<WpnGun> Guns = new();
        public float DenyT;
    }
    private readonly System.Collections.Generic.List<WpnGroup> _groups = new();
    private bool _labOpen;
    private RiderLab _lab;
    private CanvasLayer _uiLayer;
    private bool _labChecked;
    private SelectionStage _selStage; private float _selHoldT;
    private bool _running; // SHIFT held with W this frame (panel readout)
    private readonly System.Random _rng = new System.Random(20261027);
    private Vector3 _prevPlayerRoot;
    private Vector3 _playerVel;
    // Knockdown by weight (owner): impact weight accumulates over a
    // short window; past 20% of the rider's tonnage it topples.
    private float _combatClock;
    private readonly System.Collections.Generic.List<(MeshInstance3D Mi, float Life)> _puffs = new();
    // Per-weapon hit effects: spark bursts (laser), explosion bundles
    // (rockets). (Cannon scorch decals removed 2026-11-19, owner: the
    // dark squares read fake; do not re-add.)
    private readonly System.Collections.Generic.List<(MeshInstance3D Mi, Vector3 Vel, float Life)> _sparks = new();
    private int _fxSparkBursts, _fxBooms;
    private readonly RandomNumberGenerator _fxRng = new() { Seed = 4242 };
    private bool _exploding; // true while a death explosion breaks every part at once
    private static readonly BoxMesh SparkMesh = new() { Size = new Vector3(0.11f, 0.11f, 0.11f) };
    private readonly System.Collections.Generic.Dictionary<Projectile, Vector3> _lastPuff = new();
    private readonly System.Collections.Generic.List<(Label3D L, float Life)> _dmgNums = new();
    // ---------- weapons test ----------
    private bool _weaponsTest;
    private bool _enemyMayFire;
    private WeaponsPhase _wPhase;
    private double _wPhaseT;
    private int _wShotsFired;
    private bool _wCaptured, _wDeadShotQueued, _wDeathLogged;
    private Destructible _wEnvB, _wEnvT;
    private int _wEnvFx0, _wEnvChunks0, _wEnvParticles0;
    private float _wJoltSeen;
    private bool _wHitCaptured;
    private WeaponsEnvStep _wEnvStep;
    private bool _wKnocked;
    private double _wDeathT, _wStrafeT, _wLastShotT;
    private float _wStrafeDir = 1f;
    private int _wPrevShots;
    private float _wHpSnapshot;
    private int _enemyShots;
    private int _enemyDmgEvents, _playerDmgEvents;
    private float _enemyDmgTotal, _playerDmgTotal;

    // ---------- breakable test ----------
    private bool _breakTest;
    private BreakPhase _bPhase; private BreakStep _bStep;
    private double _bPhaseT;
    private Vector3 _bVec1;
    private double _bT0;
    private float _bV1, _bDragMax;
    private Vector3 _bHomeLocal;
    private int _bCount1, _bCount2;
    private bool _bFlag1;
    private bool _bSawWreckSwing;
    private Vector3 _camFocus;

    // ---------- terrain test ----------
    private bool _terrainTest;
    private TerrainPhase _tPhase;
    private double _tPhaseT;
    private float _tSlopeYaw;
    private bool _tStrideLogged;
    private Vector3 _tRunFrom;
    private float _tRunMark = -1f;
    private float _tRunMaxSplit, _tRunMaxLat;
    private float _tJumpApex, _tJumpGround;
    private bool _tJumpTried;
    private FlyStage _tJumpStage;
    private float _tJumpMarkT;
    private Vector2 _tJumpXZ;
    private float _tApex2, _tBurnDist, _tBurnSpeed;
    private Vector2 _tFlyStart;
    private bool _tParked;

    private Crosshair _cross;
    private SniperScope _scope;
    private ProgressBar _barPlayer, _barEnemy, _barFoe, _cdBarThr;
    // The weapons panel lists EVERY fitted weapon (owner,
    // MW4-style): a row per weapon instance, pooled here.
    private const int MaxWeaponRows = 18;
    private readonly ProgressBar[] _wpnBars = new ProgressBar[MaxWeaponRows];
    private readonly Label[] _wpnLabels = new Label[MaxWeaponRows];
    private readonly Label[] _wpnGroupCells = new Label[MaxWeaponRows];
    private readonly TriMark[] _wpnMarks = new TriMark[MaxWeaponRows];
    private readonly Control[] _wpnRows = new Control[MaxWeaponRows];
    private Label _lblFoeHp;
    private Label _lblPlayerHp, _lblEnemyHp, _banner, _bannerSub;
    private Label _feedLabel, _lblWeaponStatus, _lblThr;
    private readonly System.Collections.Generic.List<(string Text, float Life)> _feed = new();
    private int _targetIdx = -1; // the ONE active enemy (owner: Tab switches) - an index into OpposingTargets(); -1 = not yet picked (the HUD's first pick is the NEAREST living foe, 2026-11-23)
    private LeaveOverlay _leaveDialog;
    private TeamOverlay _teamDialog;
    private ControlsOverlay _controlsDialog;
    private CanvasLayer _labLoad; // loading cover held over the lab until the warmup settles (owner)
    private Control _labLoadArt; // the blinking loading art inside the cover (owner)
    private double _labLoadT;
    private PanelContainer _panelHealth, _panelWeapons, _panelTarget; // corner blocks (owner: no bottom dashboard)
    private PanelContainer _panelRadar; // square radar panel, top-right (owner)
    private RadarView _radar;
    private TargetFrame _targetFrame; // screen-space frame on the active target (owner, 2026-11-11)
    private Label _lblRange, _lblReticle, _lblSpeed;
    private readonly System.Collections.Generic.Dictionary<PartId, (Label Lab, ProgressBar Bar)> _partCells = new();
    private float _shake;
    private float _playerDroopT, _enemyDroopT;
    // breakable consequences, transient timers
    private float _denyLaserT, _denyAcT, _denyLrmT, _hudFlickerT;
    private DenyKind _lastDenied = DenyKind.None;
    private int _rocketsLaunched, _beamsSpawned;

    // Velocity estimation for aim lead (owner): each rider's
    // per-frame velocity is capped so teleports do not read
    // as motion, then blended toward the new sample.
    private const float VelEstimateCap = 8f;
    private const float VelEstimateBlend = 0.5f;

    private bool _built; // staged build finished (loading fix, owner 2026-11-14)
    private CanvasLayer _buildCover;

    private async System.Threading.Tasks.Task BuildFrame()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    public override async void _Ready()
    {
        UiSfx.Install(this); // UI clicks on the lab + every popup (owner, 2026-11-24)
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--duel-test") _test = true;
            if (arg == "--mission") WorldConfig.Mode = WorldConfig.MapMode.Mission; // must land before BuildTerrain below
            if (arg == "--mission-test") { _missionTest = true; WorldConfig.Mode = WorldConfig.MapMode.Mission; }
            if (arg == "--mission-test" && EnvVars.Value(EnvVars.TerraTest) == "14") _freezeTest = true;
            if (arg == "--longshot-test") { _longshotTest = true; WorldConfig.Mode = WorldConfig.MapMode.Mission; }
            if (arg == "--range-test") { _rangeTest = true; WorldConfig.Mode = WorldConfig.MapMode.Mission; }
            if (arg == "--weapons-test") _weaponsTest = true;
            if (arg == "--breakable-test") _breakTest = true;
            if (arg == "--terrain-test") _terrainTest = true;
            if (arg == "--lab-shot") _labShot = true;
            if (arg == "--stand-test") _standTest = true;
            if (arg == "--splat-dump") _splatDump = true;
            if (arg == "--slope-shot") _slopeShot = true;
            if (arg == "--lab-tab=equip") _labEquipTab = true;
            if (arg == "--photo") _photo = true;
            if (arg == "--photo-test") { _photo = true; _photoTest = true; }
            if (arg == "--grass-shot") { _photo = true; _grassShot = true; }
            if (arg == "--boom-shot") _boomShot = true;
            if (arg == "--flipbook-test") _flipbookTest = true;
            if (arg == "--leave-shot") _leaveShot = true;
            if (arg == "--lag-check") _lagCheck = true;
            if (arg == "--gait-shot") _gaitShot = true;
            if (arg == "--hector-cam") _hectorCam = true;
            if (arg == "--multifit-shot") _multiShot = true;
            if (arg == "--hector-lab") _hectorLab = true;
            if (arg.StartsWith("--lab-rider=") && System.Enum.TryParse<RiderId>(arg.Substring("--lab-rider=".Length), true, out var lr)) _labRiderId = lr;
            if (arg.StartsWith("--player-rider=") && System.Enum.TryParse<RiderId>(arg.Substring("--player-rider=".Length), true, out var pr)) _playerSpec = RiderSpecs.ById(pr);
            if (arg.StartsWith("--enemy-rider=") && System.Enum.TryParse<RiderId>(arg.Substring("--enemy-rider=".Length), true, out var er)) _enemySpec = RiderSpecs.ById(er);
            if (arg == "--ffa-test") _ffaTest = true;
            if (arg == "--team-test") { _teamTest = true; WorldConfig.Mode = WorldConfig.MapMode.Mission; }
            if (arg == "--bracket-test") { _bracketTest = true; WorldConfig.Mode = WorldConfig.MapMode.Tournament; }
            if (arg == "--deploy-test") { _deployTest = true; WorldConfig.Mode = WorldConfig.MapMode.Mission; }
            if (arg == "--lrm-probe") _lrmProbe = true;
            if (arg == "--bone-probe") _boneProbe = true;
            if (arg == "--ppc-probe") { _ppcProbe = true; WorldConfig.Mode = WorldConfig.MapMode.Mission; }
            if (arg == "--debris-probe") { _debrisProbe = true; WorldConfig.Mode = WorldConfig.MapMode.Mission; }
            if (arg == "--guard-probe") { _guardProbe = true; WorldConfig.Mode = WorldConfig.MapMode.Mission; }
            if (arg == "--ff-probe") { _ffProbe = true; WorldConfig.Mode = WorldConfig.MapMode.Mission; }
            if (arg == "--sfx-probe") { _sfxProbe = true; }
            if (arg == "--target-probe") { _targetProbe = true; WorldConfig.Mode = WorldConfig.MapMode.Mission; }
            if (arg == "--feed-probe") { _feedProbe = true; WorldConfig.Mode = WorldConfig.MapMode.Mission; }
            if (arg == "--ambient-probe") { _ambientProbe = true; }
            if (arg == "--beam-shot") { _beamShot = true; }
            if (arg == "--bindings-probe") { _bindingsProbe = true; WorldConfig.Mode = WorldConfig.MapMode.Mission; }
            if (arg == "--stone-shot") { _stoneShot = true; WorldConfig.Mode = WorldConfig.MapMode.Mission; }
            if (arg == "--controls-shot") { _controlsShot = true; }
            if (arg == "--view-shot") _viewShot = true;
            if (arg == "--point-test") _pointTest = true;
            if (arg == "--point-hector") _pointHectorFirst = true;
            if (arg == "--point-center") _pointCenter = true;
            if (arg == "--point-walk") _pointWalk = true;
            if (arg == "--point-close") _pointClose = true;
            if (arg == "--brake-test") _brakeTest = true;
            if (arg.StartsWith("--team-seed="))
            {
                // Harness: pre-form a Story team of N riders
                // (roster order) so the team lab can be captured.
                int nSeed = int.Parse(arg.Substring("--team-seed=".Length));
                TeamRoster.Story.Clear();
                for (int si = 0; si < nSeed && si < RiderSpecs.All.Length; si++)
                    TeamRoster.Story.Add(TeamRoster.Make(RiderSpecs.All[si]));
            }
            if (arg.StartsWith("--team-slot="))
                TeamRoster.StorySlot = int.Parse(arg.Substring("--team-slot=".Length));
        }

        // Diagnostics gate (owner, 2026-11-22): gameplay Dbg
        // lines print only in a test/shot mode or with
        // AOT_VERBOSE set - normal play stays quiet.
        Dbg.On = _test || _weaponsTest || _breakTest || _missionTest || _terrainTest || _lagCheck
            || _labShot || _photo || _photoTest || _ffaTest || _teamTest || _bracketTest
            || _deployTest || _pointTest || _longshotTest || _rangeTest || _standTest || _ppcProbe || _debrisProbe || _guardProbe || _ffProbe || _sfxProbe || _targetProbe || _feedProbe || _ambientProbe || _beamShot || _controlsShot || _bindingsProbe || _stoneShot || _boneProbe
            || _brakeTest || _lrmProbe || _gaitShot || _viewShot || _flipbookTest || _grassShot || _boomShot || _leaveShot
            || OS.HasEnvironment("AOT_VERBOSE");
        if (_bracketTest) SeedBracketTest();
        // Story team (owner, teams stage): the player rider is
        // the roster's currently edited slot - its spec and
        // loadout drive the lab preview now and the leader at
        // deploy. A --player-rider harness flag outranks it.
        if (WorldConfig.IsMission)
        {
            TeamRoster.EnsureStory();
            _enemyTeam = 1; _foeTeam = 1; // both defenders guard the same side
            bool playerArg = false;
            foreach (var arg in OS.GetCmdlineUserArgs())
                if (arg.StartsWith("--player-rider=")) playerArg = true;
            if (!playerArg)
            {
                var m0 = TeamRoster.Story[TeamRoster.StorySlot];
                _playerSpec = m0.Spec;
                _loadout = m0.Loadout;
            }
        }
        else if (!AnyHarness)
        {
            // Tournament (Stage D3): the bracket formed on the
            // team selector takes the field. The driven rider is
            // team 1's first slot (bracket index 0); the enemy
            // seat hosts the opposing team's first rider; the
            // foe seat hosts the next opposing rider in (team,
            // slot) order - or is not built at all when the
            // bracket fields a single opposing rider, so a
            // formed 1v1 stays a 1v1. Explicit --player-rider /
            // --enemy-rider arguments outrank the bracket.
            TeamRoster.EnsureTourney();
            _bracketActive = true;
            bool pArg = false, eArg = false;
            foreach (var arg in OS.GetCmdlineUserArgs())
            {
                if (arg.StartsWith("--player-rider=")) pArg = true;
                if (arg.StartsWith("--enemy-rider=")) eArg = true;
            }
            if (!pArg)
            {
                var m = TeamRoster.Tourney[0][0];
                _playerSpec = m.Spec; _loadout = m.Loadout;
            }
            if (!eArg)
            {
                _seatEnemyMember = TeamRoster.Tourney[1][0];
                _enemySpec = _seatEnemyMember.Spec; _enemyLoadout = _seatEnemyMember.Loadout;
            }
            _enemyTeam = 1;
            _foeMember = null; _foeMemberTeam = 2;
            for (int t = 1; t < TeamRoster.Tourney.Count && _foeMember == null; t++)
            {
                for (int s = 0; s < TeamRoster.Tourney[t].Count; s++)
                {
                    var m = TeamRoster.Tourney[t][s];
                    if (m == _seatEnemyMember) continue;
                    _foeMember = m; _foeMemberTeam = t; break;
                }
            }
            _foeTeam = _foeMemberTeam;
        }
        if (PhotoMode) _photo = true; // the photoreal scene's own declaration
        _hit = new HitModel();

        var sun = new DirectionalLight3D
        {
            RotationDegrees = new Vector3(SunPitchDeg, SunYawDeg, 0f),
            ShadowEnabled = true,
            // Shadow depth (owner, 2026-11-11 round 6:
            // 'rider's shadow looks too bright and
            // unrealistic - make it darker, but keep the
            // under-city illumination bright'): 0.28 leaked
            // so much sun that a rider in shadow read as
            // fully lit. 0.55 restores real shade; the
            // under-deck stays readable on its ceiling
            // lights and ambient, not on leaked sunlight.
            ShadowOpacity = SunShadowOpacity,
            LightEnergy = SunEnergy,
            LightColor = SunColor
        };
        sun.DirectionalShadowMaxDistance = SunShadowMaxDistance;
        AddChild(sun);
        _labLight = new DirectionalLight3D
        {
            LightEnergy = LabFillEnergy,
            LightColor = LabFillColor,
            ShadowEnabled = false,
            Visible = false
        };
        AddChild(_labLight);
        _heroLight = new OmniLight3D
        {
            // Gentler fill (owner round 6): at 0.55 the
            // shadowless pool flattened the rider - no dark
            // side anywhere. 0.30 keeps him readable in shade
            // while his shadow side can actually go dark.
            LightEnergy = HeroFillEnergy,
            LightColor = HeroFillColor,
            OmniRange = HeroFillRange,
            ShadowEnabled = false
        };
        AddChild(_heroLight);
        var skyMat = new ProceduralSkyMaterial
        {
            SkyTopColor = DuelSkyTopColor,
            SkyHorizonColor = DuelSkyHorizonColor,
            SkyCurve = DuelSkyCurve,
            SkyEnergyMultiplier = 1.0f,
            GroundBottomColor = DuelGroundBottomColor,
            GroundHorizonColor = DuelGroundHorizonColor,
            GroundCurve = DuelGroundCurve
        };
        var duelEnv = new Environment
            {
                BackgroundMode = Environment.BGMode.Sky,
                Sky = new Sky { SkyMaterial = skyMat },
                AmbientLightSource = Environment.AmbientSource.Sky,
                // 0.62 washed the baked panel/normal detail out to flat
                // color (owner: 'riders look like solid default material').
                // Lower ambient + stronger sun lets the texture and normal
                // maps model the surface again; fog thinned to match.
                AmbientLightEnergy = DuelAmbientEnergy,
                ReflectedLightSource = Environment.ReflectionSource.Sky,
                TonemapMode = Environment.ToneMapper.Aces,
                FogEnabled = true,
                FogLightColor = WorldConfig.Biome switch
                {
                    WorldConfig.BiomeKind.Desert => FogDesertColor,
                    WorldConfig.BiomeKind.City => FogCityColor,
                    _ => FogForestColor
                },
                FogDensity = WorldConfig.IsMission ? FogDensityMission : FogDensityArena,
                FogSunScatter = FogSunScatter
            };
        // A Photoreal contract takes the photo environment
        // tune in the standard scene too (owner, 2026-11-20,
        // consolidated on Babylon): SDFGI bounce, SSAO,
        // volumetric haze, glow and the deeper contrast look.
        if (_photo || WorldConfig.Photoreal) PhotoEnv.Tune(duelEnv, sun);
        ApplyMapSky(duelEnv, sun);
        AddChild(new WorldEnvironment { Environment = duelEnv });

        // Staged build (owner, 2026-11-14: 'ripple effect is
        // not visible on load, line is not moving'): the duel
        // scene used to build everything in one synchronous
        // _Ready, so the loading cover's last frame froze for
        // the whole load. The cover goes up first and the
        // world builds in chunks between frames - the ripple
        // keeps sweeping while terrain, dressing and riders
        // stream in. _Process and _Input stay gated on _built
        // until the last chunk lands.
        _buildCover = new CanvasLayer { Layer = BuildCoverLayer };
        var buildBg = new ColorRect { Color = FlowUi.LoadingBg };
        buildBg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _buildCover.AddChild(buildBg);
        _buildCover.AddChild(FlowUi.MakeLoadingArt());
        AddChild(_buildCover);
        await BuildFrame();

        var terrainMi = BuildTerrainMesh();
        await BuildFrame(); // staged build: mesh in, skin next
        SkinTerrain(terrainMi);
        await BuildFrame(); // staged build: ground done, dressing next
        // Round-1 ARENA trees at fixed spots around the origin.
        // Arena only: in a 1900u mission world the origin is
        // open wilderness; missions dress themselves via their
        // own builders/ScatterNature below.
        if (!WorldConfig.IsMission)
        {
            AddTree(new Vector3(-12f, 0f, -8f));
            AddTree(new Vector3(14f, 0f, -18f));
            AddTree(new Vector3(6f, 0f, 16f));
            AddTree(new Vector3(-26f, 0f, 26f));
            AddTree(new Vector3(30f, 0f, -30f));
            AddTree(new Vector3(-32f, 0f, -6f));
            AddTree(new Vector3(26f, 0f, 30f));
        }
        if (WorldConfig.IsMission)
        {
            Maps.Active.BuildDressing(this);
        }
        await BuildFrame(); // staged build: built ground in, nature next
        ScatterNature();
        if (_photo)
        {
            var propPts = new System.Collections.Generic.List<(Vector2 P, float R)>();
            foreach (var c in _hit.Props) propPts.Add((c.P, c.R));
            if (WorldConfig.Biome == WorldConfig.BiomeKind.Forest)
                PhotoEnv.AddGrassField(this, propPts); // the scanned grass is lush green - forest only
            PhotoEnv.AddBackdrop(this);
        }

        await BuildFrame(); // staged build: dressing done, riders next

        // Spawns: the three pads (owner: a bigger map, a hill in
        // the middle, no sightlines at load). Test choreographies
        // teleport to their own marks immediately after.
        var padP = Main.SpawnPads[0];
        var padE = Main.SpawnPads[1];
        _player = BuildRider(_playerSpec, "Player", PadPos(padP), SpawnYaw(padP));
        _enemy = BuildRider(_enemySpec, "Enemy", PadPos(padE), SpawnYaw(padE));
        _playerYaw = SpawnYaw(padP);
        _enemyYaw = SpawnYaw(padE);

        // Breakable-parts registries (may split the merged Torso meshes);
        // refresh walker materials afterwards so hit flashes cover the
        // replacement pieces too.
        _partsPlayer = new RiderParts(_player, _playerSpec.Id);
        _partsEnemy = new RiderParts(_enemy, _enemySpec.Id);
        _player.RefreshMaterials();
        _enemy.RefreshMaterials();
        await BuildFrame(); // staged build: player + enemy in, foe next
        if (((!PrecisionTest && !_lrmProbe && !_boneProbe && !_ppcProbe && !_debrisProbe && !_guardProbe && !_ffProbe && !_sfxProbe && !_targetProbe && !_feedProbe && !_ambientProbe && !_beamShot && !_controlsShot && !_bindingsProbe && !_stoneShot && !_viewShot && !_brakeTest) || _ffaTest)
            && (!_bracketActive || _foeMember != null)) BuildFoe();
        if (_bracketActive && _foe != null && _foeMember != null && _foeMemberTeam != 2)
        {
            // The foe seat spawns on pad 3 (index 2), but a
            // bracket member musters with its own team.
            var anchor = TeamAnchor(_foeMemberTeam);
            var pos = anchor + new Vector3(Mathf.Sin(-0.8f) * 12f, 0f, Mathf.Cos(-0.8f) * 12f);
            pos.Y = Main.HeightAt(pos.X, pos.Z);
            float yaw = AnchorYaw(anchor);
            _foe.Walker.Teleport(pos, yaw);
            _foe.Yaw = yaw; _foe.PrevRoot = pos;
        }
        await BuildFrame(); // staged build: seats done, teammates and the HUD next
        BuildAllies();
        if (!AnyHarness || _guardProbe) Maps.Active.MusterGuards(this); // the guard probe musters them on purpose
        BuildBracketNPCs();
        RefreshHitCaps();
        _targetIdx = -1; // the HUD may have latched a pick during the staged build, before the guards mustered and the riders were placed - the first live frame re-picks the nearest (2026-11-23)
        _prevPlayerRoot = _player.RootPos;
        _prevEnemyRoot = _enemy.RootPos;
        _hit.PartResolver = (id, pt) => PartsForRole(id).AttributePoint(pt);
        _hit.PartRayCast = (id, from, dir, maxDist) => PartsForRole(id).RaycastParts(from, dir, maxDist);
        _partsPlayer.OnBreak = (id, c) => OnPartBroken(_partsPlayer, id, c);
        _partsEnemy.OnBreak = (id, c) => OnPartBroken(_partsEnemy, id, c);

        _cam = new Camera3D { Fov = BaseFov };
        AddChild(_cam);
        _cam.GlobalPosition = new Vector3(-14f, 9f, 44f);
        _cam.LookAt(new Vector3(0f, 6f, 18f), Vector3.Up);

        var layer = new CanvasLayer();
        _uiLayer = layer;
        AddChild(layer);
        _cross = new Crosshair();
        layer.AddChild(_cross);
        _cross.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _scope = new SniperScope { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        layer.AddChild(_scope);
        // SetAnchorsAndOffsetsPreset, NOT SetAnchorsPreset:
        // the anchors-only preset preserves the current rect,
        // and a control born at zero size stays zero size
        // forever (measured offsets (0,0,-1280,-1024) after
        // the call) - the scope drew nothing through three
        // captures while the crosshair, which draws from the
        // viewport rect rather than its own Size, hid the
        // same trap on the line above (2026-11-23).
        _scope.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        if (EnvVars.Flag(EnvVars.Sniper)) _sniper = true; // harness: scope captures
        BuildHud(layer);
        ApplyLoadout();
        if (!_test && !_weaponsTest && !_breakTest && !_terrainTest && !_photoTest && !_gaitShot && !_ffaTest && !_lrmProbe && !_boneProbe && !_ppcProbe && !_debrisProbe && !_guardProbe && !_ffProbe && !_sfxProbe && !_targetProbe && !_feedProbe && !_ambientProbe && !_beamShot && !_controlsShot && !_bindingsProbe && !_stoneShot && !_viewShot && !_brakeTest && !_pointTest && !_grassShot && !_boomShot && !_lagCheck && !_flipbookTest && !_missionTest && !_leaveShot && !_longshotTest && !_rangeTest && !_teamTest && !_bracketTest && !_deployTest)
        {
            // Pre-game RiderLab (owner): the duel waits behind the lab
            // until DEPLOY. Test modes skip straight to the duel.
            _labOpen = true;
            BuildLabLoadOverlay();
            Input.MouseMode = Input.MouseModeEnum.Visible;
            SetHudPanelsVisible(false);
            _radar.Visible = false;
            OpenLab();
        }

        _lastPlayerPos = _player.RootPos;
        if (Maps.Active.StreamsStatics) RegisterStreaming();
        InitSfx();
        _built = true;
        _buildCover.QueueFree();
        _buildCover = null;
        Dbg.Print($"DUEL ready test={_test} weaponsTest={_weaponsTest} breakTest={_breakTest} terrainTest={_terrainTest}");
        if (_terrainTest) TerrainSelfCheck();
        if (_weaponsTest)
        {
            WSetup(TestMarkPlayer, TestMarkEnemy);
            ApplyRangeFit();
            SetWPhase(0);
        }
        if (_breakTest)
        {
            BSetup(TestMarkPlayer, TestMarkEnemy);
            ApplyRangeFit();
            SetBPhase(0);
        }
    }

    private static Vector3 PadPos(Vector2 pad) =>
        new(pad.X, Main.HeightAt(pad.X, pad.Y), pad.Y);

    // Yaw that faces a pad spawn toward the map center (matches
    // the legacy marks: (0,30) -> 0, (0,-16) -> PI).
    private static float SpawnYaw(Vector2 pad) => Mathf.Atan2(pad.X, pad.Y);

    private void ResetDuel()
    {
        _feed.Clear(); // emptied, not hidden: the next order's AddFeed shows the feed again (2026-11-24)
        _hpPlayer = _playerSpec.MaxHp;
        _hpEnemy = _enemySpec.MaxHp;
        _playerDead = _enemyDead = false;
        _targetIdx = -1; // a reset re-picks the nearest living foe
        _foe0Alerted = false; // a reset also clears what the defenders know
        _partsPlayer.RestoreAll();
        _partsEnemy.RestoreAll();
        SyncLegConsequences(_player, _partsPlayer);
        SyncLegConsequences(_enemy, _partsEnemy);
        if (_foe != null)
        {
            _foe.Hp = _foe.Spec.MaxHp;
            _foe.Dead = false;
            _foe.Alerted = false;
            _foe.Cd = 0f;
            _foe.DroopT = 0f;
            _foe.Parts.RestoreAll();
            SyncLegConsequences(_foe.Walker, _foe.Parts);
            _foe.Walker.Halted = false;
            _foe.Walker.Droop = 0f;
            _foe.Walker.SetDarkened(false);
        }
        ResetAllies(live: !_test && !_weaponsTest && !_breakTest && !_terrainTest && !_photoTest);
        _missionDone = false;
        _bracketDecided = false;
        _pendingLeaderPass = false;
        _denyLaserT = _denyAcT = _denyLrmT = _hudFlickerT = 0f;
        _lastDenied = DenyKind.None;
        _cdLaser = _cdAc = _cdLrm = _cdEnemy = 0f;
        _weaponSel = 0;
        foreach (var grp in _groups)
        {
            grp.DenyT = 0f;
            foreach (var g in grp.Guns) g.Cd = 0f;
        }
        _lrmQueue.Clear();
        foreach (var p in _shots) p.QueueFree();
        _shots.Clear();
        _lastPuff.Clear();
        foreach (var (l, _) in _dmgNums) l.QueueFree();
        _dmgNums.Clear();
        _player.Halted = false;
        _enemy.Halted = false;
        _player.Droop = 0f;
        _enemy.Droop = 0f;
        _playerDroopT = 0f;
        _enemyDroopT = 0f;
        _player.SetDarkened(false);
        _enemy.SetDarkened(false);
        // Live resets return to the pads; the test suites keep
        // their calibrated center marks.
        bool live = !_test && !_weaponsTest && !_breakTest && !_terrainTest && !_photoTest;
        if (live)
        {
            _player.Teleport(PadPos(Main.SpawnPads[0]), SpawnYaw(Main.SpawnPads[0]));
            _enemy.Teleport(PadPos(Main.SpawnPads[1]), SpawnYaw(Main.SpawnPads[1]));
            _playerYaw = SpawnYaw(Main.SpawnPads[0]);
            _enemyYaw = SpawnYaw(Main.SpawnPads[1]);
            if (_foe != null)
            {
                _foe.Walker.Teleport(PadPos(Main.SpawnPads[2]), SpawnYaw(Main.SpawnPads[2]));
                _foe.Yaw = SpawnYaw(Main.SpawnPads[2]);
                _foe.Walker.TorsoTwist = 0f;
            }
        }
        else
        {
            _player.Teleport(new Vector3(0f, Main.HeightAt(0f, 30f), 30f), 0f);
            _enemy.Teleport(new Vector3(0f, Main.HeightAt(0f, -16f), -16f), Mathf.Pi);
            _playerYaw = 0f;
            _enemyYaw = Mathf.Pi;
        }
        _player.AimPitchWide = float.NaN;
        _player.TorsoTwist = 0f;
        _player.TorsoPitch = 0f;
        _torsoWorldYaw = 0f;
        _enemy.TorsoTwist = 0f;
        _prevPlayerRoot = _player.RootPos;
        _playerVel = Vector3.Zero;
        _banner.Visible = false;
        _bannerSub.Visible = false;
        Dbg.Print("WPN RESET both riders restored to spawn, HP full");
    }

    private static ProgressBar MakeBar(Color fill, Vector2 minSize)
    {
        var bar = new ProgressBar
        {
            MinValue = 0,
            MaxValue = 1,
            Value = 1,
            ShowPercentage = false,
            CustomMinimumSize = minSize
        };
        // Pill bars in the owner's example style (2026-11-22):
        // full-height rounding on a dark glass track.
        int pill = Mathf.Max(2, Mathf.CeilToInt(minSize.Y / 2f));
        var bg = new StyleBoxFlat { BgColor = BarTrackColor, BorderColor = BarBorderColor };
        bg.SetBorderWidthAll(1);
        bg.SetCornerRadiusAll(pill);
        var fg = new StyleBoxFlat { BgColor = fill };
        fg.SetCornerRadiusAll(pill);
        bar.AddThemeStyleboxOverride("background", bg);
        bar.AddThemeStyleboxOverride("fill", fg);
        return bar;
    }

    private void UpdateCombat(float dt)
    {
        _combatClock += dt;
        UpdateDestructibles(dt);
        StepEnvChunks(dt);
        StepImpactLights(dt);
        StepTimedFx(dt);
        _cdLaser = Mathf.Max(_cdLaser - dt, 0f);
        _cdAc = Mathf.Max(_cdAc - dt, 0f);
        _cdLrm = Mathf.Max(_cdLrm - dt, 0f);
        _cdEnemy = Mathf.Max(_cdEnemy - dt, 0f);
        if (_foe != null) _foe.Cd = Mathf.Max(_foe.Cd - dt, 0f);
        foreach (var a in _allies) a.Cd = Mathf.Max(a.Cd - dt, 0f);
        foreach (var grp in _groups)
        {
            grp.DenyT = Mathf.Max(grp.DenyT - dt, 0f);
            foreach (var g in grp.Guns) g.Cd = Mathf.Max(g.Cd - dt, 0f);
        }
        _denyLaserT = Mathf.Max(_denyLaserT - dt, 0f);
        _denyAcT = Mathf.Max(_denyAcT - dt, 0f);
        _denyLrmT = Mathf.Max(_denyLrmT - dt, 0f);
        _hudFlickerT = Mathf.Max(_hudFlickerT - dt, 0f);
        ProcessLrmQueue(dt);
        for (int i = _shots.Count - 1; i >= 0; i--)
        {
            var p = _shots[i];
            p.Step(dt, _hit);
            if (p.Dead) { _shots.RemoveAt(i); _lastPuff.Remove(p); p.QueueFree(); }
        }
        _shake = Mathf.Max(_shake - dt * ShakeDecayRate, 0f);
        for (int i = _dmgNums.Count - 1; i >= 0; i--)
        {
            var (l, life0) = _dmgNums[i];
            float life = life0 - dt;
            if (life <= 0f) { l.QueueFree(); _dmgNums.RemoveAt(i); continue; }
            l.GlobalPosition += Vector3.Up * DmgNumberRiseSpeed * dt;
            var m = l.Modulate;
            m.A = Mathf.Clamp(life / DmgNumberLifeSeconds, 0f, 1f);
            l.Modulate = m;
            _dmgNums[i] = (l, life);
        }
        for (int i = _puffs.Count - 1; i >= 0; i--)
        {
            float life = _puffs[i].Life - dt;
            if (life <= 0f) { _puffs[i].Mi.QueueFree(); _puffs.RemoveAt(i); }
            else
            {
                var mi = _puffs[i].Mi;
                if (mi.MaterialOverride is StandardMaterial3D sm)
                {
                    var c = sm.AlbedoColor;
                    sm.AlbedoColor = new Color(c.R, c.G, c.B, PuffFadePeakAlpha * (life / PuffLife));
                }
                _puffs[i] = (mi, life);
            }
        }
        for (int i = _sparks.Count - 1; i >= 0; i--)
        {
            var (mi, vel, life) = _sparks[i];
            life -= dt;
            if (life <= 0f) { mi.QueueFree(); _sparks.RemoveAt(i); continue; }
            vel.Y -= SparkGravity * dt;
            mi.GlobalPosition += vel * dt;
            _sparks[i] = (mi, vel, life);
        }

        for (int i = _fx.Count - 1; i >= 0; i--)
        {
            // Lifetime is in seconds, but an FX node must also survive at
            // least 3 rendered frames: under a slow renderer a single
            // frame's dt can exceed a beam's whole 0.12s life and it
            // would be freed before ever being drawn.
            float life = _fx[i].Life - dt;
            int frames = _fx[i].Frames + 1;
            if (life <= 0f && frames >= FxMinFrames) { _fx[i].Node.QueueFree(); _fx.RemoveAt(i); }
            else _fx[i] = (_fx[i].Node, life, frames);
        }
        if (!_test && !_terrainTest && !_photoTest && ((!_weaponsTest && !_breakTest) || _enemyMayFire)) UpdateEnemyGunnery(dt);
        if (_foe != null && !_test && !_terrainTest && !_photoTest && !_weaponsTest && !_breakTest) UpdateFoeGunnery(dt);
    }

    private static void StepWalker(RiderWalker w, double delta)
    {
        int n = Mathf.Max(1, Mathf.CeilToInt((float)delta / MaxSimStepSeconds));
        double h = delta / n;
        for (int i = 0; i < n; i++) w.StepFrame(h);
    }

    public override void _Process(double delta)
    {
        if (!_built) return; // staged build in progress - the loading cover is up
        if (_pointTest && _pointHectorFirst && !_pointSwapDone && _player != null && _player.IsReady)
        {
            // Select Hector the way the owner does - in the lab,
            // before the duel starts (hectorcam's ritual). A
            // MID-RUN swap is invalid measurement: a stale scorch
            // entry from the old walker makes UpdateCombat throw
            // every frame and freezes the reticle/camera updates
            // downstream of it (found by this probe, 2026-11-01).
            _pointSwapDone = true;
            SetRiderSelection(RiderId.Hector, RiderId.Enyo);
            ApplyLoadout();
            if (_lab != null) { _lab.QueueFree(); _lab = null; }
            _labOpen = false;
            if (_labLight != null) _labLight.Visible = false;
            SetHudPanelsVisible(true); _radar.Visible = true;
            if (_feedLabel != null) _feedLabel.Visible = true;
            _pointHector = true;
            Dbg.Print("POINTTEST selected Hector at start (lab path)");
        }
        if (_leaveShot && !_leaveShotDone && _player != null && _player.IsReady)
        {
            // Frame the exit popup over the live duel (proof only;
            // the pause itself is the Esc path's business).
            _leaveShotDone = true;
            SetHudPanelsVisible(true); _radar.Visible = true;
            if (_feedLabel != null) _feedLabel.Visible = true;
            _leaveDialog.Open();
        }
        if (_leaveShot && _leaveShotDone && !_leaveShotSaved)
        {
            _leaveShotT += (float)delta;
            if (_leaveShotT > 0.6f)
            {
                _leaveShotSaved = true;
                GetViewport().GetTexture().GetImage().SavePng("/tmp/leave_popup.png");
                Dbg.Print("LEAVESHOT saved");
                GetTree().Quit();
            }
        }
        if (_hectorCam && !_hectorCamDone && _player != null && _player.IsReady)
        {
            _hectorCamDone = true;
            SetRiderSelection(RiderId.Hector, RiderId.Enyo);
            ApplyLoadout();
            if (_lab != null) { _lab.QueueFree(); _lab = null; }
            _labOpen = false;
            if (_labLight != null) _labLight.Visible = false;
            SetHudPanelsVisible(true); _radar.Visible = true;
            if (_feedLabel != null) _feedLabel.Visible = true;
            Dbg.Print("HECTORCAM swapped player to Hector and deployed");
        }
        if (_hectorCam && _hectorCamDone && !_labOpen)
        {
            _hectorCamT += (float)delta;
            if (!_hectorCamFired && _hectorCamT > 1.2f)
            {
                _hectorCamFired = true;
                Dbg.Print($"HECTORCAM defs energy={_defEnergy?.Id} ballistic={_defBallistic?.Id} podL={_defPodL?.Id} podR={_defPodR?.Id}");
                FireLaser(_enemy.RootPos + Vector3.Up * 6f);
                FireLrmSalvo(_enemy.RootPos);
            }
            if (_hectorCamT > 2.5f && _camMode == CamMode.Chase)
            {
                var back = -RiderWalker.HeadingVec(_player.TorsoYawWorld);
                var camRel = _cam.GlobalPosition - _player.RootPos;
                Dbg.Print($"HECTORCAM legsYaw={_player.LegsYaw:F2} torsoWorld={_player.TorsoYawWorld:F2} engineTorso={_player.EngineTorsoYaw():F2} camBehind={camRel.Dot(back):F2} camSide={camRel.Dot(new Vector3(-back.Z, 0f, back.X)):F2}");
                QueueCapture(HectorChaseCapture, "hectorP", CamMode.Chase, () => GetTree().Quit());
                _hectorCamT = -999f;
            }
        }

        if (_hectorLab && _labOpen && _lab != null)
        {
            _hectorLab = false;
            SetRiderSelection(RiderId.Hector, _enemySpec.Id);
            Dbg.Print("HECTORLAB lab reopened on the Hector");
        }

        if (_labRiderId != null && _labOpen && _lab != null)
        {
            var id = _labRiderId.Value;
            _labRiderId = null;
            SetRiderSelection(id, _enemySpec.Id);
            Dbg.Print($"LABRIDER lab reopened on {id}");
        }

        if (_multiShot && _labOpen && _lab != null)
        {
            _multiShot = false;
            _loadout.TryAssign(_playerSpec, PartId.ArmL, WeaponCatalog.MediumLaser); // LL 5 + ML 3 = 8t = arm cap exactly
            _loadout.TryAssign(_playerSpec, PartId.PodL, WeaponCatalog.Srm);          // LRM 10 + SRM 4 in one pod
            _loadout.TryAssign(_playerSpec, PartId.ArmR, WeaponCatalog.HeavyCannon);  // AC 12 + HC 16 = 28 > 25 cap: must refuse
            var err = _loadout.Check(_playerSpec, PartId.ArmR, WeaponCatalog.HeavyCannon);
            _lab.Refresh();
            Dbg.Print($"MULTISHOT fitted arm_L={string.Join(",", _loadout.WeaponsIn(PartId.ArmL))} pod_L={string.Join(",", _loadout.WeaponsIn(PartId.PodL))} arm_R refused: {err}");
        }

        // The capture waits for BOTH wall time and a frame
        // count: on a software rasterizer the first frame alone
        // can take ~19s (shader compilation), so a wall-clock-only
        // trigger photographed frame 1 - before the recreated
        // lab's first _Process had run its live layout even once.
        if (_splatDump) UpdateSplatDump();
        if (_slopeShot) UpdateSlopeShot();
        if (_standTest) UpdateStandTest();
        if (_labShot) _labShotFrames++;
        if (_labShot && _lab != null && Time.GetTicksMsec() > 19500 && _labShotFrames > 45 && !_layoutDumped)
        {
            _layoutDumped = true;
            if (_labEquipTab) _lab.OpenOnEquipmentTab();
            Dbg.Print(_lab.DumpLayout());
        }

        if (_labShot && Time.GetTicksMsec() > 20000 && _labShotFrames > 50)
        {
            _labShot = false;
            GetViewport().GetTexture().GetImage().SavePng("/tmp/lab_live.png");
            Dbg.Print("LABSHOT saved");
            GetTree().Quit();
            return;
        }

        if (_player == null || !_player.IsReady || _enemy == null || !_enemy.IsReady) return;
        float dt = (float)delta;

        if (_labOpen)
        {
            // The loading cover stays up until the warmup sweep
            // has run (owner): the lab is revealed only in its
            // settled showroom framing, never mid-sweep.
            if (_labLoad != null) _labLoad.Visible = _photo && _warmPose < WarmPose.Done;
            if (_labLoad != null && _labLoad.Visible)
            {
                _labLoadT += delta;
            }
            // The duel waits behind the RiderLab: a showroom view
            // (owner, MW4-style) - the rider stands front three-
            // quarter in the paperdoll area, lit by a lab fill, so
            // the slot panels sit ON the machine. A fixed angle
            // replaces the orbit (the slow sway it once carried
            // is gone, owner 2026-11-25): the paperdoll mapping
            // only makes sense from the front.
            _labLight.Visible = true;
            if (_photo && _warmPose < WarmPose.Done)
            {
                // Shader warmup (#6, photo mode only): while the lab
                // is up, sweep the camera through the duel's
                // viewpoints and fire one underground FX burst, so
                // every pipeline - splat terrain, foliage, fog,
                // beams, particles - compiles BEFORE the duel
                // instead of hitching mid-fight. Timing is
                // wall-clock with a 2-frame minimum per pose, so the
                // sweep completes on any hardware (a raw delta sum
                // was skipped entirely by llvmpipe's giant first
                // frames).
                if (_warmStart == 0) _warmStart = Time.GetTicksMsec();
                _warmPoseFrames++;
                ulong warmEl = Time.GetTicksMsec() - _warmStart;
                var wc = _player.RootPos + Vector3.Up * LabCamCenterHeight;
                // The poses ride on the player's position (owner:
                // "riders are not visible in the lab"): framed on
                // the arena origin, in a mission world they stared
                // at empty terrain 620u from the rider for the
                // whole sweep, and the lab read as broken. The
                // rider is in frame at every pose now.
                var pp = _player.RootPos;
                switch (_warmPose)
                {
                    case WarmPose.Overhead:
                        _cam.GlobalPosition = pp + WarmOverheadOffset;
                        _cam.LookAt(pp + Vector3.Up * WarmLookHeight, Vector3.Up);
                        break;
                    case WarmPose.Ground:
                        var low = pp + WarmGroundOffset;
                        low.Y = Main.HeightAt(low.X, low.Z) + WarmGroundEyeHeight;
                        _cam.GlobalPosition = low;
                        _cam.LookAt(pp + Vector3.Up * WarmGroundLookHeight, Vector3.Up);
                        break;
                    case WarmPose.Vista:
                        _cam.GlobalPosition = pp + WarmVistaOffset;
                        _cam.LookAt(pp + Vector3.Up * WarmLookHeight, Vector3.Up);
                        break;
                    default:
                        _cam.GlobalPosition = wc + WarmLabOffset;
                        _cam.LookAt(wc, Vector3.Up);
                        break;
                }
                _labLight.GlobalRotation = _cam.GlobalRotation;
                if (!_warmFx && _warmPose >= WarmPose.Ground)
                {
                    _warmFx = true;
                    var under = new Vector3(0f, -60f, 0f);
                    SpawnBeam(under, under + new Vector3(6f, 2f, 0f), new Color(1f, 0.2f, 0.15f), 0.15f, 0.4f);
                    SpawnExplosionFx(under + new Vector3(4f, 0f, 4f));
                    SpawnImpactLight(under, new Color(1f, 0.6f, 0.3f), 3f, 12f, 0.4f);
                    Dbg.Print("PHOTO warmup: camera sweep + underground FX burst");
                }
                ulong sliceEnd = _warmPose switch { WarmPose.Overhead => WarmOverheadSliceEndMs, WarmPose.Ground => WarmGroundSliceEndMs, WarmPose.Vista => WarmVistaSliceEndMs, _ => WarmLabSliceEndMs };
                if (warmEl > sliceEnd && _warmPoseFrames >= WarmMinFramesPerPose) { _warmPose = (WarmPose)((int)_warmPose + 1); _warmPoseFrames = 0; }
                return;
            }
            float labK = _playerSpec.Height / ReferenceChassisHeight; // showroom framing scales with the chassis, like the chase cam
            var center = _player.RootPos + Vector3.Up * (LabCamCenterHeight * labK);
            var fwd = new Vector3(Mathf.Sin(_player.LegsYaw), 0f, Mathf.Cos(_player.LegsYaw));
            // Static showroom camera (owner, 2026-11-25): the
            // slow sinusoidal sway that used to ride this angle
            // is gone - the frame is fixed, straight and level
            // with the horizon (LookAt on world Up, no roll).
            var viewDir = fwd.Rotated(Vector3.Up, LabCamViewAngle); // mostly in front of the rider, slightly to its side
            _cam.GlobalPosition = center + viewDir * (LabCamDistance * labK) + Vector3.Up * (LabCamHeight * labK);
            _cam.LookAt(center, Vector3.Up);
            _cam.RotateY(LabCamPanAngle); // pan in camera space: rider right, centered under its slot panels (owner, adjusted twice at his word)
            // Deck alignment (owner, 2026-11-25): the lab
            // stand's plate grid keeps its local X parallel
            // to the camera's right vector - the transverse
            // seams are then level with the horizon BY
            // CONSTRUCTION, on every entry path and rider.
            // (The camera itself was measured level - right
            // vector Y = 0.0000; an earlier deck yaw computed
            // from the rider's facing in OpenLab still read
            // rotated in the owner's play.)
            if (_labDeck != null)
            {
                var r = _cam.GlobalBasis.X;
                _labDeck.Rotation = new Vector3(0f, Mathf.Atan2(-r.Z, r.X), 0f);
            }
            _labLight.GlobalRotation = _cam.GlobalRotation;
            return;
        }

        if (!_frozen)
        {
            _simT += delta;
            if (_viewShot) UpdateViewShot(dt);
            else if (_pointTest) UpdatePointTest(dt);
            else if (_brakeTest) UpdateBrakeTest(dt);
            else if (_lrmProbe) UpdateLrmProbe(dt);
            else if (_boneProbe) UpdateBoneProbe(dt);
            else if (_ppcProbe) UpdatePpcProbe(dt);
            else if (_debrisProbe) UpdateDebrisProbe(dt);
            else if (_guardProbe) UpdateGuardProbe(dt);
            else if (_ffProbe) UpdateFfProbe(dt);
            else if (_sfxProbe) UpdateSfxProbe(dt);
            else if (_targetProbe) UpdateTargetProbe(dt);
            else if (_feedProbe) UpdateFeedProbe(dt);
            else if (_ambientProbe) UpdateAmbientProbe(dt);
            else if (_beamShot) UpdateBeamShot(dt);
            else if (_bindingsProbe) UpdateBindingsProbe(dt);
            else if (_stoneShot) UpdateStoneShot(dt);
            else if (_controlsShot) UpdateControlsShot(dt);
            else if (_ffaTest) UpdateFfaTest(dt);
            else if (_teamTest) UpdateTeamTest(dt);
            else if (_bracketTest) UpdateBracketTest(dt);
            else if (_deployTest) UpdateDeployTest(dt);
            else if (_freezeTest) UpdateFreezeTest(dt);
            else if (_missionTest) UpdateMissionTest(dt);
            else if (_longshotTest) UpdateLongshotTest(dt);
            else if (_rangeTest) UpdateRangeTest(dt);
            else if (_breakTest) UpdateBreakTest(dt);
            else if (_weaponsTest) UpdateWeaponsTest(dt);
            else if (_terrainTest) UpdateTerrainTest(dt);
            else if (_photoTest) UpdatePhotoTest(dt);
            else if (_grassShot) UpdateGrassShot(dt);
            else if (_boomShot) UpdateBoomShot(dt);
            else if (_flipbookTest) UpdateFlipbookTest(dt);
            else if (_lagCheck) UpdateLagCheck(dt);
            else if (_test) UpdateTest(dt);
            else
            {
                if (_gaitShot) UpdateGaitShot(dt); // dev capture rides the live branch (AI walks the enemy)
                UpdateStreaming(dt);
                PassCommandIfNeeded();
                UpdatePlayerInput(dt);
                MaintainTorsoWorldYaw();
                if (TickReady(_enemy)) UpdateEnemyAI(TickDt(_enemy, dt));
                if (_foe != null && TickReady(_foe.Walker)) UpdateFoeAI(TickDt(_foe.Walker, dt));
                UpdateAlliesAI(dt);
                UpdateBracketAI(dt);
                UpdateGuardAI(dt);
                _aimPoint = TorsoAimPoint();
                if (Input.MouseMode == Input.MouseModeEnum.Captured && !_playerDead)
                {
                    // One trigger: the left mouse button fires the SELECTED
                    // weapon (1/2/3 or wheel to switch - see the panel).
                    var aim = _aimPoint;
                    if (Input.IsMouseButtonPressed(MouseButton.Left))
                        FireSelected(aim);
                }
            }
            StepWalker(_player, delta);
            if (TickReady(_enemy)) StepWalker(_enemy, TickDt(_enemy, (float)delta));
            if (_foe != null && _foe.Walker.IsReady && TickReady(_foe.Walker)) StepWalker(_foe.Walker, TickDt(_foe.Walker, (float)delta));
            foreach (var a in _allies) if (a.Walker.IsReady && TickReady(a.Walker)) StepWalker(a.Walker, TickDt(a.Walker, (float)delta));
            StepSounds();
            StepSnowFx((float)delta);
            ThrusterSounds();
            StepAmbient((float)delta);
            ResolveCollisions();
            StompCheck();
            if (dt > 0f)
            {
                var v = (_player.RootPos - _prevPlayerRoot) / dt;
                if (v.Length() > VelEstimateCap) v = v.Normalized() * VelEstimateCap; // teleports are not velocity
                _playerVel = _playerVel.Lerp(v, VelEstimateBlend);
                _prevPlayerRoot = _player.RootPos;
            }
            if (dt > 0f)
            {
                if (TickReady(_enemy))
                {
                    var ev = (_enemy.RootPos - _prevEnemyRoot) / TickDt(_enemy, dt);
                    if (ev.Length() > VelEstimateCap) ev = ev.Normalized() * VelEstimateCap;
                    _enemyVel = _enemyVel.Lerp(ev, VelEstimateBlend);
                    _prevEnemyRoot = _enemy.RootPos;
                }
                if (_foe != null && TickReady(_foe.Walker))
                {
                    var fv = (_foe.Walker.RootPos - _foe.PrevRoot) / TickDt(_foe.Walker, dt);
                    if (fv.Length() > VelEstimateCap) fv = fv.Normalized() * VelEstimateCap;
                    _foe.Vel = _foe.Vel.Lerp(fv, VelEstimateBlend);
                    _foe.PrevRoot = _foe.Walker.RootPos;
                }
                foreach (var a in _allies)
                {
                    if (!TickReady(a.Walker)) continue;
                    var av = (a.Walker.RootPos - a.PrevRoot) / TickDt(a.Walker, dt);
                    if (av.Length() > VelEstimateCap) av = av.Normalized() * VelEstimateCap;
                    a.Vel = a.Vel.Lerp(av, VelEstimateBlend);
                    a.PrevRoot = a.Walker.RootPos;
                }
            }
            UpdateCombat(dt);
            UpdateDroop(dt);
            _partsPlayer.StepDebris(dt);
            _partsEnemy.StepDebris(dt);
            if (_foe != null) _foe.Parts.StepDebris(dt);
            foreach (var a in _allies) a.Parts.StepDebris(dt);
        }
        for (int i = _feed.Count - 1; i >= 0; i--)
        {
            float life = _feed[i].Life - dt;
            if (life <= 0f) _feed.RemoveAt(i);
            else _feed[i] = (_feed[i].Text, life);
        }
        UpdateHud();
        // No crosshair sway: the reticle stays on screen centre
        // whatever state the rider is in (owner, 2026-11-15 - the
        // head-destroyed "sensor sway" drew a wandering figure-8
        // and was removed at his word).
        if (!_test && !_weaponsTest && !_breakTest && !_terrainTest && !_photoTest && !_labOpen)
            UpdateReticle();
        UpdateCamera(_frozen ? 0f : dt);
        if (_pendingPath != null && !_capturing) DoCapture();
    }
}

