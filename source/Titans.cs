using Godot;

namespace Titans;

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
  private float EnemyPace => Mathf.Min(this._enemySpec.MaxSpeed, EnemyMaxSpeed);
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
  private bool PrecisionTest => this._test || this._weaponsTest || this._breakTest || this._terrainTest;
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
  private Destructible _wEnvT;
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
    await this.ToSignal(this.GetTree(), SceneTree.SignalName.ProcessFrame);
  }

  public override async void _Ready()
  {
    UiSfx.Install(this); // UI clicks on the lab + every popup (owner, 2026-11-24)
    foreach (var arg in OS.GetCmdlineUserArgs())
    {
      if (arg == "--duel-test")
      {
        this._test = true;
      }

      if (arg == "--mission")
      {
        WorldConfig.Mode = WorldConfig.MapMode.Mission; // must land before BuildTerrain below
      }

      if (arg == "--mission-test") { this._missionTest = true; WorldConfig.Mode = WorldConfig.MapMode.Mission; }
      if (arg == "--mission-test" && EnvVars.Value(EnvVars.TerraTest) == "14")
      {
        this._freezeTest = true;
      }

      if (arg == "--longshot-test") { this._longshotTest = true; WorldConfig.Mode = WorldConfig.MapMode.Mission; }
      if (arg == "--range-test") { this._rangeTest = true; WorldConfig.Mode = WorldConfig.MapMode.Mission; }
      if (arg == "--weapons-test")
      {
        this._weaponsTest = true;
      }

      if (arg == "--breakable-test")
      {
        this._breakTest = true;
      }

      if (arg == "--terrain-test")
      {
        this._terrainTest = true;
      }

      if (arg == "--lab-shot")
      {
        this._labShot = true;
      }

      if (arg == "--stand-test")
      {
        this._standTest = true;
      }

      if (arg == "--splat-dump")
      {
        this._splatDump = true;
      }

      if (arg == "--slope-shot")
      {
        this._slopeShot = true;
      }

      if (arg == "--lab-tab=equip")
      {
        this._labEquipTab = true;
      }

      if (arg == "--photo")
      {
        this._photo = true;
      }

      if (arg == "--photo-test") { this._photo = true; this._photoTest = true; }
      if (arg == "--grass-shot") { this._photo = true; this._grassShot = true; }
      if (arg == "--boom-shot")
      {
        this._boomShot = true;
      }

      if (arg == "--flipbook-test")
      {
        this._flipbookTest = true;
      }

      if (arg == "--leave-shot")
      {
        this._leaveShot = true;
      }

      if (arg == "--lag-check")
      {
        this._lagCheck = true;
      }

      if (arg == "--gait-shot")
      {
        this._gaitShot = true;
      }

      if (arg == "--hector-cam")
      {
        this._hectorCam = true;
      }

      if (arg == "--multifit-shot")
      {
        this._multiShot = true;
      }

      if (arg == "--hector-lab")
      {
        this._hectorLab = true;
      }

      if (arg.StartsWith("--lab-rider=", System.StringComparison.Ordinal) && System.Enum.TryParse<RiderId>(arg.Substring("--lab-rider=".Length), true, out var lr))
      {
        this._labRiderId = lr;
      }

      if (arg.StartsWith("--player-rider=", System.StringComparison.Ordinal) && System.Enum.TryParse<RiderId>(arg.Substring("--player-rider=".Length), true, out var pr))
      {
        this._playerSpec = RiderSpecs.ById(pr);
      }

      if (arg.StartsWith("--enemy-rider=", System.StringComparison.Ordinal) && System.Enum.TryParse<RiderId>(arg.Substring("--enemy-rider=".Length), true, out var er))
      {
        this._enemySpec = RiderSpecs.ById(er);
      }

      if (arg == "--ffa-test")
      {
        this._ffaTest = true;
      }

      if (arg == "--team-test") { this._teamTest = true; WorldConfig.Mode = WorldConfig.MapMode.Mission; }
      if (arg == "--bracket-test") { this._bracketTest = true; WorldConfig.Mode = WorldConfig.MapMode.Tournament; }
      if (arg == "--deploy-test") { this._deployTest = true; WorldConfig.Mode = WorldConfig.MapMode.Mission; }
      if (arg == "--lrm-probe")
      {
        this._lrmProbe = true;
      }

      if (arg == "--bone-probe")
      {
        this._boneProbe = true;
      }

      if (arg == "--ppc-probe") { this._ppcProbe = true; WorldConfig.Mode = WorldConfig.MapMode.Mission; }
      if (arg == "--debris-probe") { this._debrisProbe = true; WorldConfig.Mode = WorldConfig.MapMode.Mission; }
      if (arg == "--guard-probe") { this._guardProbe = true; WorldConfig.Mode = WorldConfig.MapMode.Mission; }
      if (arg == "--ff-probe") { this._ffProbe = true; WorldConfig.Mode = WorldConfig.MapMode.Mission; }
      if (arg == "--sfx-probe") { this._sfxProbe = true; }
      if (arg == "--target-probe") { this._targetProbe = true; WorldConfig.Mode = WorldConfig.MapMode.Mission; }
      if (arg == "--feed-probe") { this._feedProbe = true; WorldConfig.Mode = WorldConfig.MapMode.Mission; }
      if (arg == "--ambient-probe") { this._ambientProbe = true; }
      if (arg == "--beam-shot") { this._beamShot = true; }
      if (arg == "--bindings-probe") { this._bindingsProbe = true; WorldConfig.Mode = WorldConfig.MapMode.Mission; }
      if (arg == "--stone-shot") { this._stoneShot = true; WorldConfig.Mode = WorldConfig.MapMode.Mission; }
      if (arg == "--controls-shot") { this._controlsShot = true; }
      if (arg == "--view-shot")
      {
        this._viewShot = true;
      }

      if (arg == "--point-test")
      {
        this._pointTest = true;
      }

      if (arg == "--point-hector")
      {
        this._pointHectorFirst = true;
      }

      if (arg == "--point-center")
      {
        this._pointCenter = true;
      }

      if (arg == "--point-walk")
      {
        this._pointWalk = true;
      }

      if (arg == "--point-close")
      {
        this._pointClose = true;
      }

      if (arg == "--brake-test")
      {
        this._brakeTest = true;
      }

      if (arg.StartsWith("--team-seed=", System.StringComparison.Ordinal))
      {
        // Harness: pre-form a Story team of N riders
        // (roster order) so the team lab can be captured.
        int nSeed = int.Parse(System.MemoryExtensions.AsSpan(arg, "--team-seed=".Length), System.Globalization.CultureInfo.InvariantCulture);
        TeamRoster.Story.Clear();
        for (int si = 0; si < nSeed && si < RiderSpecs.All.Length; si++)
        {
          TeamRoster.Story.Add(TeamRoster.Make(RiderSpecs.All[si]));
        }
      }
      if (arg.StartsWith("--team-slot=", System.StringComparison.Ordinal))
      {
        TeamRoster.StorySlot = int.Parse(System.MemoryExtensions.AsSpan(arg, "--team-slot=".Length), System.Globalization.CultureInfo.InvariantCulture);
      }
    }

    // Diagnostics gate (owner, 2026-11-22): gameplay Dbg
    // lines print only in a test/shot mode or with
    // AOT_VERBOSE set - normal play stays quiet.
    Dbg.On = this._test || this._weaponsTest || this._breakTest || this._missionTest || this._terrainTest || this._lagCheck
        || this._labShot || this._photo || this._photoTest || this._ffaTest || this._teamTest || this._bracketTest
        || this._deployTest || this._pointTest || this._longshotTest || this._rangeTest || this._standTest || this._ppcProbe || this._debrisProbe || this._guardProbe || this._ffProbe || this._sfxProbe || this._targetProbe || this._feedProbe || this._ambientProbe || this._beamShot || this._controlsShot || this._bindingsProbe || this._stoneShot || this._boneProbe
        || this._brakeTest || this._lrmProbe || this._gaitShot || this._viewShot || this._flipbookTest || this._grassShot || this._boomShot || this._leaveShot
        || OS.HasEnvironment("AOT_VERBOSE");
    if (this._bracketTest)
    {
      this.SeedBracketTest();
    }
    // Story team (owner, teams stage): the player rider is
    // the roster's currently edited slot - its spec and
    // loadout drive the lab preview now and the leader at
    // deploy. A --player-rider harness flag outranks it.
    if (WorldConfig.IsMission)
    {
      TeamRoster.EnsureStory();
      this._enemyTeam = 1; this._foeTeam = 1; // both defenders guard the same side
      bool playerArg = false;
      foreach (var arg in OS.GetCmdlineUserArgs())
      {
        if (arg.StartsWith("--player-rider=", System.StringComparison.Ordinal))
        {
          playerArg = true;
        }
      }

      if (!playerArg)
      {
        var m0 = TeamRoster.Story[TeamRoster.StorySlot];
        this._playerSpec = m0.Spec;
        this._loadout = m0.Loadout;
      }
    }
    else if (!this.AnyHarness)
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
      this._bracketActive = true;
      bool pArg = false, eArg = false;
      foreach (var arg in OS.GetCmdlineUserArgs())
      {
        if (arg.StartsWith("--player-rider=", System.StringComparison.Ordinal))
        {
          pArg = true;
        }

        if (arg.StartsWith("--enemy-rider=", System.StringComparison.Ordinal))
        {
          eArg = true;
        }
      }
      if (!pArg)
      {
        var m = TeamRoster.Tourney[0][0];
        this._playerSpec = m.Spec; this._loadout = m.Loadout;
      }
      if (!eArg)
      {
        this._seatEnemyMember = TeamRoster.Tourney[1][0];
        this._enemySpec = this._seatEnemyMember.Spec; this._enemyLoadout = this._seatEnemyMember.Loadout;
      }
      this._enemyTeam = 1;
      this._foeMember = null; this._foeMemberTeam = 2;
      for (int t = 1; t < TeamRoster.Tourney.Count && this._foeMember == null; t++)
      {
        for (int s = 0; s < TeamRoster.Tourney[t].Count; s++)
        {
          var m = TeamRoster.Tourney[t][s];
          if (m == this._seatEnemyMember)
          {
            continue;
          }

          this._foeMember = m; this._foeMemberTeam = t; break;
        }
      }
      this._foeTeam = this._foeMemberTeam;
    }
    if (this.PhotoMode)
    {
      this._photo = true; // the photoreal scene's own declaration
    }

    this._hit = new HitModel();

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
    this.AddChild(sun);
    this._labLight = new DirectionalLight3D
    {
      LightEnergy = LabFillEnergy,
      LightColor = LabFillColor,
      ShadowEnabled = false,
      Visible = false
    };
    this.AddChild(this._labLight);
    this._heroLight = new OmniLight3D
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
    this.AddChild(this._heroLight);
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
    if (this._photo || WorldConfig.Photoreal)
    {
      PhotoEnv.Tune(duelEnv, sun);
    }

    this.ApplyMapSky(duelEnv, sun);
    this.AddChild(new WorldEnvironment { Environment = duelEnv });

    // Staged build (owner, 2026-11-14: 'ripple effect is
    // not visible on load, line is not moving'): the duel
    // scene used to build everything in one synchronous
    // _Ready, so the loading cover's last frame froze for
    // the whole load. The cover goes up first and the
    // world builds in chunks between frames - the ripple
    // keeps sweeping while terrain, dressing and riders
    // stream in. _Process and _Input stay gated on _built
    // until the last chunk lands.
    this._buildCover = new CanvasLayer { Layer = BuildCoverLayer };
    var buildBg = new ColorRect { Color = FlowUi.LoadingBg };
    buildBg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    this._buildCover.AddChild(buildBg);
    this._buildCover.AddChild(FlowUi.MakeLoadingArt());
    this.AddChild(this._buildCover);
    await this.BuildFrame();

    var terrainMi = this.BuildTerrainMesh();
    await this.BuildFrame(); // staged build: mesh in, skin next
    this.SkinTerrain(terrainMi);
    await this.BuildFrame(); // staged build: ground done, dressing next
                             // Round-1 ARENA trees at fixed spots around the origin.
                             // Arena only: in a 1900u mission world the origin is
                             // open wilderness; missions dress themselves via their
                             // own builders/ScatterNature below.
    if (!WorldConfig.IsMission)
    {
      this.AddTree(new Vector3(-12f, 0f, -8f));
      this.AddTree(new Vector3(14f, 0f, -18f));
      this.AddTree(new Vector3(6f, 0f, 16f));
      this.AddTree(new Vector3(-26f, 0f, 26f));
      this.AddTree(new Vector3(30f, 0f, -30f));
      this.AddTree(new Vector3(-32f, 0f, -6f));
      this.AddTree(new Vector3(26f, 0f, 30f));
    }
    if (WorldConfig.IsMission)
    {
      Maps.Active.BuildDressing(this);
    }
    await this.BuildFrame(); // staged build: built ground in, nature next
    this.ScatterNature();
    if (this._photo)
    {
      var propPts = new System.Collections.Generic.List<(Vector2 P, float R)>();
      foreach (var c in this._hit.Props)
      {
        propPts.Add((c.P, c.R));
      }

      if (WorldConfig.Biome == WorldConfig.BiomeKind.Forest)
      {
        PhotoEnv.AddGrassField(this, propPts); // the scanned grass is lush green - forest only
      }

      PhotoEnv.AddBackdrop(this);
    }

    await this.BuildFrame(); // staged build: dressing done, riders next

    // Spawns: the three pads (owner: a bigger map, a hill in
    // the middle, no sightlines at load). Test choreographies
    // teleport to their own marks immediately after.
    var padP = Main.SpawnPads[0];
    var padE = Main.SpawnPads[1];
    this._player = this.BuildRider(this._playerSpec, "Player", PadPos(padP), SpawnYaw(padP));
    this._enemy = this.BuildRider(this._enemySpec, "Enemy", PadPos(padE), SpawnYaw(padE));
    this._playerYaw = SpawnYaw(padP);
    this._enemyYaw = SpawnYaw(padE);

    // Breakable-parts registries (may split the merged Torso meshes);
    // refresh walker materials afterwards so hit flashes cover the
    // replacement pieces too.
    this._partsPlayer = new RiderParts(this._player, this._playerSpec.Id);
    this._partsEnemy = new RiderParts(this._enemy, this._enemySpec.Id);
    this._player.RefreshMaterials();
    this._enemy.RefreshMaterials();
    await this.BuildFrame(); // staged build: player + enemy in, foe next
    if (((!this.PrecisionTest && !this._lrmProbe && !this._boneProbe && !this._ppcProbe && !this._debrisProbe && !this._guardProbe && !this._ffProbe && !this._sfxProbe && !this._targetProbe && !this._feedProbe && !this._ambientProbe && !this._beamShot && !this._controlsShot && !this._bindingsProbe && !this._stoneShot && !this._viewShot && !this._brakeTest) || this._ffaTest)
        && (!this._bracketActive || this._foeMember != null))
    {
      this.BuildFoe();
    }

    if (this._bracketActive && this._foe != null && this._foeMember != null && this._foeMemberTeam != 2)
    {
      // The foe seat spawns on pad 3 (index 2), but a
      // bracket member musters with its own team.
      var anchor = TeamAnchor(this._foeMemberTeam);
      var pos = anchor + new Vector3(Mathf.Sin(-0.8f) * 12f, 0f, Mathf.Cos(-0.8f) * 12f);
      pos.Y = Main.HeightAt(pos.X, pos.Z);
      float yaw = AnchorYaw(anchor);
      this._foe.Walker.Teleport(pos, yaw);
      this._foe.Yaw = yaw; this._foe.PrevRoot = pos;
    }
    await this.BuildFrame(); // staged build: seats done, teammates and the HUD next
    this.BuildAllies();
    if (!this.AnyHarness || this._guardProbe)
    {
      Maps.Active.MusterGuards(this); // the guard probe musters them on purpose
    }

    this.BuildBracketNPCs();
    this.RefreshHitCaps();
    this._targetIdx = -1; // the HUD may have latched a pick during the staged build, before the guards mustered and the riders were placed - the first live frame re-picks the nearest (2026-11-23)
    this._prevPlayerRoot = this._player.RootPos;
    this._prevEnemyRoot = this._enemy.RootPos;
    this._hit.PartResolver = (id, pt) => this.PartsForRole(id).AttributePoint(pt);
    this._hit.PartRayCast = (id, from, dir, maxDist) => this.PartsForRole(id).RaycastParts(from, dir, maxDist);
    this._partsPlayer.OnBreak = (id, c) => this.OnPartBroken(this._partsPlayer, id, c);
    this._partsEnemy.OnBreak = (id, c) => this.OnPartBroken(this._partsEnemy, id, c);

    this._cam = new Camera3D { Fov = BaseFov };
    this.AddChild(this._cam);
    this._cam.GlobalPosition = new Vector3(-14f, 9f, 44f);
    this._cam.LookAt(new Vector3(0f, 6f, 18f), Vector3.Up);

    var layer = new CanvasLayer();
    this._uiLayer = layer;
    this.AddChild(layer);
    this._cross = new Crosshair();
    layer.AddChild(this._cross);
    this._cross.SetAnchorsPreset(Control.LayoutPreset.FullRect);
    this._scope = new SniperScope { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
    layer.AddChild(this._scope);
    // SetAnchorsAndOffsetsPreset, NOT SetAnchorsPreset:
    // the anchors-only preset preserves the current rect,
    // and a control born at zero size stays zero size
    // forever (measured offsets (0,0,-1280,-1024) after
    // the call) - the scope drew nothing through three
    // captures while the crosshair, which draws from the
    // viewport rect rather than its own Size, hid the
    // same trap on the line above (2026-11-23).
    this._scope.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    if (EnvVars.Flag(EnvVars.Sniper))
    {
      this._sniper = true; // harness: scope captures
    }

    this.BuildHud(layer);
    this.ApplyLoadout();
    if (!this._test && !this._weaponsTest && !this._breakTest && !this._terrainTest && !this._photoTest && !this._gaitShot && !this._ffaTest && !this._lrmProbe && !this._boneProbe && !this._ppcProbe && !this._debrisProbe && !this._guardProbe && !this._ffProbe && !this._sfxProbe && !this._targetProbe && !this._feedProbe && !this._ambientProbe && !this._beamShot && !this._controlsShot && !this._bindingsProbe && !this._stoneShot && !this._viewShot && !this._brakeTest && !this._pointTest && !this._grassShot && !this._boomShot && !this._lagCheck && !this._flipbookTest && !this._missionTest && !this._leaveShot && !this._longshotTest && !this._rangeTest && !this._teamTest && !this._bracketTest && !this._deployTest)
    {
      // Pre-game RiderLab (owner): the duel waits behind the lab
      // until DEPLOY. Test modes skip straight to the duel.
      this._labOpen = true;
      this.BuildLabLoadOverlay();
      Input.MouseMode = Input.MouseModeEnum.Visible;
      this.SetHudPanelsVisible(false);
      this._radar.Visible = false;
      this.OpenLab();
    }

    this._lastPlayerPos = this._player.RootPos;
    if (Maps.Active.StreamsStatics)
    {
      this.RegisterStreaming();
    }

    this.InitSfx();
    this._built = true;
    this._buildCover.QueueFree();
    this._buildCover = null;
    Dbg.Print($"DUEL ready test={this._test} weaponsTest={this._weaponsTest} breakTest={this._breakTest} terrainTest={this._terrainTest}");
    if (this._terrainTest)
    {
      this.TerrainSelfCheck();
    }

    if (this._weaponsTest)
    {
      this.WSetup(TestMarkPlayer, TestMarkEnemy);
      this.ApplyRangeFit();
      this.SetWPhase(0);
    }
    if (this._breakTest)
    {
      this.BSetup(TestMarkPlayer, TestMarkEnemy);
      this.ApplyRangeFit();
      this.SetBPhase(0);
    }
  }

  private static Vector3 PadPos(Vector2 pad) =>
      new(pad.X, Main.HeightAt(pad.X, pad.Y), pad.Y);

  // Yaw that faces a pad spawn toward the map center (matches
  // the legacy marks: (0,30) -> 0, (0,-16) -> PI).
  private static float SpawnYaw(Vector2 pad) => Mathf.Atan2(pad.X, pad.Y);

  private void ResetDuel()
  {
    this._feed.Clear(); // emptied, not hidden: the next order's AddFeed shows the feed again (2026-11-24)
    this._hpPlayer = this._playerSpec.MaxHp;
    this._hpEnemy = this._enemySpec.MaxHp;
    this._playerDead = this._enemyDead = false;
    this._targetIdx = -1; // a reset re-picks the nearest living foe
    this._foe0Alerted = false; // a reset also clears what the defenders know
    this._partsPlayer.RestoreAll();
    this._partsEnemy.RestoreAll();
    this.SyncLegConsequences(this._player, this._partsPlayer);
    this.SyncLegConsequences(this._enemy, this._partsEnemy);
    if (this._foe != null)
    {
      this._foe.Hp = this._foe.Spec.MaxHp;
      this._foe.Dead = false;
      this._foe.Alerted = false;
      this._foe.Cd = 0f;
      this._foe.DroopT = 0f;
      this._foe.Parts.RestoreAll();
      this.SyncLegConsequences(this._foe.Walker, this._foe.Parts);
      this._foe.Walker.Halted = false;
      this._foe.Walker.Droop = 0f;
      this._foe.Walker.SetDarkened(false);
    }
    this.ResetAllies(live: !this._test && !this._weaponsTest && !this._breakTest && !this._terrainTest && !this._photoTest);
    this._missionDone = false;
    this._bracketDecided = false;
    this._pendingLeaderPass = false;
    this._denyLaserT = this._denyAcT = this._denyLrmT = this._hudFlickerT = 0f;
    this._lastDenied = DenyKind.None;
    this._cdLaser = this._cdAc = this._cdLrm = this._cdEnemy = 0f;
    this._weaponSel = 0;
    foreach (var grp in this._groups)
    {
      grp.DenyT = 0f;
      foreach (var g in grp.Guns)
      {
        g.Cd = 0f;
      }
    }
    this._lrmQueue.Clear();
    foreach (var p in this._shots)
    {
      p.QueueFree();
    }

    this._shots.Clear();
    this._lastPuff.Clear();
    foreach (var (l, _) in this._dmgNums)
    {
      l.QueueFree();
    }

    this._dmgNums.Clear();
    this._player.Halted = false;
    this._enemy.Halted = false;
    this._player.Droop = 0f;
    this._enemy.Droop = 0f;
    this._playerDroopT = 0f;
    this._enemyDroopT = 0f;
    this._player.SetDarkened(false);
    this._enemy.SetDarkened(false);
    // Live resets return to the pads; the test suites keep
    // their calibrated center marks.
    bool live = !this._test && !this._weaponsTest && !this._breakTest && !this._terrainTest && !this._photoTest;
    if (live)
    {
      this._player.Teleport(PadPos(Main.SpawnPads[0]), SpawnYaw(Main.SpawnPads[0]));
      this._enemy.Teleport(PadPos(Main.SpawnPads[1]), SpawnYaw(Main.SpawnPads[1]));
      this._playerYaw = SpawnYaw(Main.SpawnPads[0]);
      this._enemyYaw = SpawnYaw(Main.SpawnPads[1]);
      if (this._foe != null)
      {
        this._foe.Walker.Teleport(PadPos(Main.SpawnPads[2]), SpawnYaw(Main.SpawnPads[2]));
        this._foe.Yaw = SpawnYaw(Main.SpawnPads[2]);
        this._foe.Walker.TorsoTwist = 0f;
      }
    }
    else
    {
      this._player.Teleport(new Vector3(0f, Main.HeightAt(0f, 30f), 30f), 0f);
      this._enemy.Teleport(new Vector3(0f, Main.HeightAt(0f, -16f), -16f), Mathf.Pi);
      this._playerYaw = 0f;
      this._enemyYaw = Mathf.Pi;
    }
    this._player.AimPitchWide = float.NaN;
    this._player.TorsoTwist = 0f;
    this._player.TorsoPitch = 0f;
    this._torsoWorldYaw = 0f;
    this._enemy.TorsoTwist = 0f;
    this._prevPlayerRoot = this._player.RootPos;
    this._playerVel = Vector3.Zero;
    this._banner.Visible = false;
    this._bannerSub.Visible = false;
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
    this._combatClock += dt;
    this.UpdateDestructibles(dt);
    this.StepEnvChunks(dt);
    this.StepImpactLights(dt);
    this.StepTimedFx(dt);
    this._cdLaser = Mathf.Max(this._cdLaser - dt, 0f);
    this._cdAc = Mathf.Max(this._cdAc - dt, 0f);
    this._cdLrm = Mathf.Max(this._cdLrm - dt, 0f);
    this._cdEnemy = Mathf.Max(this._cdEnemy - dt, 0f);
    if (this._foe != null)
    {
      this._foe.Cd = Mathf.Max(this._foe.Cd - dt, 0f);
    }

    foreach (var a in this._allies)
    {
      a.Cd = Mathf.Max(a.Cd - dt, 0f);
    }

    foreach (var grp in this._groups)
    {
      grp.DenyT = Mathf.Max(grp.DenyT - dt, 0f);
      foreach (var g in grp.Guns)
      {
        g.Cd = Mathf.Max(g.Cd - dt, 0f);
      }
    }
    this._denyLaserT = Mathf.Max(this._denyLaserT - dt, 0f);
    this._denyAcT = Mathf.Max(this._denyAcT - dt, 0f);
    this._denyLrmT = Mathf.Max(this._denyLrmT - dt, 0f);
    this._hudFlickerT = Mathf.Max(this._hudFlickerT - dt, 0f);
    this.ProcessLrmQueue(dt);
    for (int i = this._shots.Count - 1; i >= 0; i--)
    {
      var p = this._shots[i];
      p.Step(dt, this._hit);
      if (p.Dead) { this._shots.RemoveAt(i); this._lastPuff.Remove(p); p.QueueFree(); }
    }
    this._shake = Mathf.Max(this._shake - dt * ShakeDecayRate, 0f);
    for (int i = this._dmgNums.Count - 1; i >= 0; i--)
    {
      var (l, life0) = this._dmgNums[i];
      float life = life0 - dt;
      if (life <= 0f) { l.QueueFree(); this._dmgNums.RemoveAt(i); continue; }
      l.GlobalPosition += Vector3.Up * DmgNumberRiseSpeed * dt;
      var m = l.Modulate;
      m.A = Mathf.Clamp(life / DmgNumberLifeSeconds, 0f, 1f);
      l.Modulate = m;
      this._dmgNums[i] = (l, life);
    }
    for (int i = this._puffs.Count - 1; i >= 0; i--)
    {
      float life = this._puffs[i].Life - dt;
      if (life <= 0f) { this._puffs[i].Mi.QueueFree(); this._puffs.RemoveAt(i); }
      else
      {
        var mi = this._puffs[i].Mi;
        if (mi.MaterialOverride is StandardMaterial3D sm)
        {
          var c = sm.AlbedoColor;
          sm.AlbedoColor = new Color(c.R, c.G, c.B, PuffFadePeakAlpha * (life / PuffLife));
        }
        this._puffs[i] = (mi, life);
      }
    }
    for (int i = this._sparks.Count - 1; i >= 0; i--)
    {
      var (mi, vel, life) = this._sparks[i];
      life -= dt;
      if (life <= 0f) { mi.QueueFree(); this._sparks.RemoveAt(i); continue; }
      vel.Y -= SparkGravity * dt;
      mi.GlobalPosition += vel * dt;
      this._sparks[i] = (mi, vel, life);
    }

    for (int i = this._fx.Count - 1; i >= 0; i--)
    {
      // Lifetime is in seconds, but an FX node must also survive at
      // least 3 rendered frames: under a slow renderer a single
      // frame's dt can exceed a beam's whole 0.12s life and it
      // would be freed before ever being drawn.
      float life = this._fx[i].Life - dt;
      int frames = this._fx[i].Frames + 1;
      if (life <= 0f && frames >= FxMinFrames) { this._fx[i].Node.QueueFree(); this._fx.RemoveAt(i); }
      else
      {
        this._fx[i] = (this._fx[i].Node, life, frames);
      }
    }
    if (!this._test && !this._terrainTest && !this._photoTest && ((!this._weaponsTest && !this._breakTest) || this._enemyMayFire))
    {
      this.UpdateEnemyGunnery(dt);
    }

    if (this._foe != null && !this._test && !this._terrainTest && !this._photoTest && !this._weaponsTest && !this._breakTest)
    {
      this.UpdateFoeGunnery(dt);
    }
  }

  private static void StepWalker(RiderWalker w, double delta)
  {
    int n = Mathf.Max(1, Mathf.CeilToInt((float)delta / MaxSimStepSeconds));
    double h = delta / n;
    for (int i = 0; i < n; i++)
    {
      w.StepFrame(h);
    }
  }

  public override void _Process(double delta)
  {
    if (!this._built)
    {
      return; // staged build in progress - the loading cover is up
    }

    if (this._pointTest && this._pointHectorFirst && !this._pointSwapDone && this._player != null && this._player.IsReady)
    {
      // Select Hector the way the owner does - in the lab,
      // before the duel starts (hectorcam's ritual). A
      // MID-RUN swap is invalid measurement: a stale scorch
      // entry from the old walker makes UpdateCombat throw
      // every frame and freezes the reticle/camera updates
      // downstream of it (found by this probe, 2026-11-01).
      this._pointSwapDone = true;
      this.SetRiderSelection(RiderId.Hector, RiderId.Enyo);
      this.ApplyLoadout();
      if (this._lab != null) { this._lab.QueueFree(); this._lab = null; }
      this._labOpen = false;
      if (this._labLight != null)
      {
        this._labLight.Visible = false;
      }

      this.SetHudPanelsVisible(true); this._radar.Visible = true;
      if (this._feedLabel != null)
      {
        this._feedLabel.Visible = true;
      }

      this._pointHector = true;
      Dbg.Print("POINTTEST selected Hector at start (lab path)");
    }
    if (this._leaveShot && !this._leaveShotDone && this._player != null && this._player.IsReady)
    {
      // Frame the exit popup over the live duel (proof only;
      // the pause itself is the Esc path's business).
      this._leaveShotDone = true;
      this.SetHudPanelsVisible(true); this._radar.Visible = true;
      if (this._feedLabel != null)
      {
        this._feedLabel.Visible = true;
      }

      this._leaveDialog.Open();
    }
    if (this._leaveShot && this._leaveShotDone && !this._leaveShotSaved)
    {
      this._leaveShotT += (float)delta;
      if (this._leaveShotT > 0.6f)
      {
        this._leaveShotSaved = true;
        this.GetViewport().GetTexture().GetImage().SavePng("/tmp/leave_popup.png");
        Dbg.Print("LEAVESHOT saved");
        this.GetTree().Quit();
      }
    }
    if (this._hectorCam && !this._hectorCamDone && this._player != null && this._player.IsReady)
    {
      this._hectorCamDone = true;
      this.SetRiderSelection(RiderId.Hector, RiderId.Enyo);
      this.ApplyLoadout();
      if (this._lab != null) { this._lab.QueueFree(); this._lab = null; }
      this._labOpen = false;
      if (this._labLight != null)
      {
        this._labLight.Visible = false;
      }

      this.SetHudPanelsVisible(true); this._radar.Visible = true;
      if (this._feedLabel != null)
      {
        this._feedLabel.Visible = true;
      }

      Dbg.Print("HECTORCAM swapped player to Hector and deployed");
    }
    if (this._hectorCam && this._hectorCamDone && !this._labOpen)
    {
      this._hectorCamT += (float)delta;
      if (!this._hectorCamFired && this._hectorCamT > 1.2f)
      {
        this._hectorCamFired = true;
        Dbg.Print($"HECTORCAM defs energy={this._defEnergy?.Id} ballistic={this._defBallistic?.Id} podL={this._defPodL?.Id} podR={this._defPodR?.Id}");
        this.FireLaser(this._enemy.RootPos + Vector3.Up * 6f);
        this.FireLrmSalvo(this._enemy.RootPos);
      }
      if (this._hectorCamT > 2.5f && this._camMode == CamMode.Chase)
      {
        var back = -RiderWalker.HeadingVec(this._player.TorsoYawWorld);
        var camRel = this._cam.GlobalPosition - this._player.RootPos;
        Dbg.Print($"HECTORCAM legsYaw={this._player.LegsYaw:F2} torsoWorld={this._player.TorsoYawWorld:F2} engineTorso={this._player.EngineTorsoYaw():F2} camBehind={camRel.Dot(back):F2} camSide={camRel.Dot(new Vector3(-back.Z, 0f, back.X)):F2}");
        this.QueueCapture(HectorChaseCapture, "hectorP", CamMode.Chase, () => this.GetTree().Quit());
        this._hectorCamT = -999f;
      }
    }

    if (this._hectorLab && this._labOpen && this._lab != null)
    {
      this._hectorLab = false;
      this.SetRiderSelection(RiderId.Hector, this._enemySpec.Id);
      Dbg.Print("HECTORLAB lab reopened on the Hector");
    }

    if (this._labRiderId != null && this._labOpen && this._lab != null)
    {
      var id = this._labRiderId.Value;
      this._labRiderId = null;
      this.SetRiderSelection(id, this._enemySpec.Id);
      Dbg.Print($"LABRIDER lab reopened on {id}");
    }

    if (this._multiShot && this._labOpen && this._lab != null)
    {
      this._multiShot = false;
      this._loadout.TryAssign(this._playerSpec, PartId.ArmL, WeaponCatalog.MediumLaser); // LL 5 + ML 3 = 8t = arm cap exactly
      this._loadout.TryAssign(this._playerSpec, PartId.PodL, WeaponCatalog.Srm);          // LRM 10 + SRM 4 in one pod
      this._loadout.TryAssign(this._playerSpec, PartId.ArmR, WeaponCatalog.HeavyCannon);  // AC 12 + HC 16 = 28 > 25 cap: must refuse
      var err = this._loadout.Check(this._playerSpec, PartId.ArmR, WeaponCatalog.HeavyCannon);
      this._lab.Refresh();
      Dbg.Print($"MULTISHOT fitted arm_L={string.Join(",", this._loadout.WeaponsIn(PartId.ArmL))} pod_L={string.Join(",", this._loadout.WeaponsIn(PartId.PodL))} arm_R refused: {err}");
    }

    // The capture waits for BOTH wall time and a frame
    // count: on a software rasterizer the first frame alone
    // can take ~19s (shader compilation), so a wall-clock-only
    // trigger photographed frame 1 - before the recreated
    // lab's first _Process had run its live layout even once.
    if (this._splatDump)
    {
      this.UpdateSplatDump();
    }

    if (this._slopeShot)
    {
      this.UpdateSlopeShot();
    }

    if (this._standTest)
    {
      this.UpdateStandTest();
    }

    if (this._labShot)
    {
      this._labShotFrames++;
    }

    if (this._labShot && this._lab != null && Time.GetTicksMsec() > 19500 && this._labShotFrames > 45 && !this._layoutDumped)
    {
      this._layoutDumped = true;
      if (this._labEquipTab)
      {
        this._lab.OpenOnEquipmentTab();
      }

      Dbg.Print(this._lab.DumpLayout());
    }

    if (this._labShot && Time.GetTicksMsec() > 20000 && this._labShotFrames > 50)
    {
      this._labShot = false;
      this.GetViewport().GetTexture().GetImage().SavePng("/tmp/lab_live.png");
      Dbg.Print("LABSHOT saved");
      this.GetTree().Quit();
      return;
    }

    if (this._player == null || !this._player.IsReady || this._enemy == null || !this._enemy.IsReady)
    {
      return;
    }

    float dt = (float)delta;

    if (this._labOpen)
    {
      // The loading cover stays up until the warmup sweep
      // has run (owner): the lab is revealed only in its
      // settled showroom framing, never mid-sweep.
      if (this._labLoad != null)
      {
        this._labLoad.Visible = this._photo && this._warmPose < WarmPose.Done;
      }

      if (this._labLoad != null && this._labLoad.Visible)
      {
        this._labLoadT += delta;
      }
      // The duel waits behind the RiderLab: a showroom view
      // (owner, MW4-style) - the rider stands front three-
      // quarter in the paperdoll area, lit by a lab fill, so
      // the slot panels sit ON the machine. A fixed angle
      // replaces the orbit (the slow sway it once carried
      // is gone, owner 2026-11-25): the paperdoll mapping
      // only makes sense from the front.
      this._labLight.Visible = true;
      if (this._photo && this._warmPose < WarmPose.Done)
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
        if (this._warmStart == 0)
        {
          this._warmStart = Time.GetTicksMsec();
        }

        this._warmPoseFrames++;
        ulong warmEl = Time.GetTicksMsec() - this._warmStart;
        var wc = this._player.RootPos + Vector3.Up * LabCamCenterHeight;
        // The poses ride on the player's position (owner:
        // "riders are not visible in the lab"): framed on
        // the arena origin, in a mission world they stared
        // at empty terrain 620u from the rider for the
        // whole sweep, and the lab read as broken. The
        // rider is in frame at every pose now.
        var pp = this._player.RootPos;
        switch (this._warmPose)
        {
          case WarmPose.Overhead:
            this._cam.GlobalPosition = pp + WarmOverheadOffset;
            this._cam.LookAt(pp + Vector3.Up * WarmLookHeight, Vector3.Up);
            break;
          case WarmPose.Ground:
            var low = pp + WarmGroundOffset;
            low.Y = Main.HeightAt(low.X, low.Z) + WarmGroundEyeHeight;
            this._cam.GlobalPosition = low;
            this._cam.LookAt(pp + Vector3.Up * WarmGroundLookHeight, Vector3.Up);
            break;
          case WarmPose.Vista:
            this._cam.GlobalPosition = pp + WarmVistaOffset;
            this._cam.LookAt(pp + Vector3.Up * WarmLookHeight, Vector3.Up);
            break;
          default:
            this._cam.GlobalPosition = wc + WarmLabOffset;
            this._cam.LookAt(wc, Vector3.Up);
            break;
        }
        this._labLight.GlobalRotation = this._cam.GlobalRotation;
        if (!this._warmFx && this._warmPose >= WarmPose.Ground)
        {
          this._warmFx = true;
          var under = new Vector3(0f, -60f, 0f);
          this.SpawnBeam(under, under + new Vector3(6f, 2f, 0f), new Color(1f, 0.2f, 0.15f), 0.15f, 0.4f);
          this.SpawnExplosionFx(under + new Vector3(4f, 0f, 4f));
          this.SpawnImpactLight(under, new Color(1f, 0.6f, 0.3f), 3f, 12f, 0.4f);
          Dbg.Print("PHOTO warmup: camera sweep + underground FX burst");
        }
        ulong sliceEnd = this._warmPose switch { WarmPose.Overhead => WarmOverheadSliceEndMs, WarmPose.Ground => WarmGroundSliceEndMs, WarmPose.Vista => WarmVistaSliceEndMs, _ => WarmLabSliceEndMs };
        if (warmEl > sliceEnd && this._warmPoseFrames >= WarmMinFramesPerPose) { this._warmPose = (WarmPose)((int)this._warmPose + 1); this._warmPoseFrames = 0; }
        return;
      }
      float labK = this._playerSpec.Height / ReferenceChassisHeight; // showroom framing scales with the chassis, like the chase cam
      var center = this._player.RootPos + Vector3.Up * (LabCamCenterHeight * labK);
      var fwd = new Vector3(Mathf.Sin(this._player.LegsYaw), 0f, Mathf.Cos(this._player.LegsYaw));
      // Static showroom camera (owner, 2026-11-25): the
      // slow sinusoidal sway that used to ride this angle
      // is gone - the frame is fixed, straight and level
      // with the horizon (LookAt on world Up, no roll).
      var viewDir = fwd.Rotated(Vector3.Up, LabCamViewAngle); // mostly in front of the rider, slightly to its side
      this._cam.GlobalPosition = center + viewDir * (LabCamDistance * labK) + Vector3.Up * (LabCamHeight * labK);
      this._cam.LookAt(center, Vector3.Up);
      this._cam.RotateY(LabCamPanAngle); // pan in camera space: rider right, centered under its slot panels (owner, adjusted twice at his word)
                                         // Deck alignment (owner, 2026-11-25): the lab
                                         // stand's plate grid keeps its local X parallel
                                         // to the camera's right vector - the transverse
                                         // seams are then level with the horizon BY
                                         // CONSTRUCTION, on every entry path and rider.
                                         // (The camera itself was measured level - right
                                         // vector Y = 0.0000; an earlier deck yaw computed
                                         // from the rider's facing in OpenLab still read
                                         // rotated in the owner's play.)
      if (this._labDeck != null)
      {
        var r = this._cam.GlobalBasis.X;
        this._labDeck.Rotation = new Vector3(0f, Mathf.Atan2(-r.Z, r.X), 0f);
      }
      this._labLight.GlobalRotation = this._cam.GlobalRotation;
      return;
    }

    if (!this._frozen)
    {
      this._simT += delta;
      if (this._viewShot)
      {
        this.UpdateViewShot(dt);
      }
      else if (this._pointTest)
      {
        this.UpdatePointTest(dt);
      }
      else if (this._brakeTest)
      {
        this.UpdateBrakeTest(dt);
      }
      else if (this._lrmProbe)
      {
        this.UpdateLrmProbe(dt);
      }
      else if (this._boneProbe)
      {
        this.UpdateBoneProbe(dt);
      }
      else if (this._ppcProbe)
      {
        this.UpdatePpcProbe(dt);
      }
      else if (this._debrisProbe)
      {
        this.UpdateDebrisProbe(dt);
      }
      else if (this._guardProbe)
      {
        this.UpdateGuardProbe(dt);
      }
      else if (this._ffProbe)
      {
        this.UpdateFfProbe(dt);
      }
      else if (this._sfxProbe)
      {
        this.UpdateSfxProbe(dt);
      }
      else if (this._targetProbe)
      {
        this.UpdateTargetProbe(dt);
      }
      else if (this._feedProbe)
      {
        this.UpdateFeedProbe(dt);
      }
      else if (this._ambientProbe)
      {
        this.UpdateAmbientProbe(dt);
      }
      else if (this._beamShot)
      {
        this.UpdateBeamShot(dt);
      }
      else if (this._bindingsProbe)
      {
        this.UpdateBindingsProbe(dt);
      }
      else if (this._stoneShot)
      {
        this.UpdateStoneShot(dt);
      }
      else if (this._controlsShot)
      {
        this.UpdateControlsShot(dt);
      }
      else if (this._ffaTest)
      {
        this.UpdateFfaTest(dt);
      }
      else if (this._teamTest)
      {
        this.UpdateTeamTest(dt);
      }
      else if (this._bracketTest)
      {
        this.UpdateBracketTest(dt);
      }
      else if (this._deployTest)
      {
        this.UpdateDeployTest(dt);
      }
      else if (this._freezeTest)
      {
        this.UpdateFreezeTest(dt);
      }
      else if (this._missionTest)
      {
        this.UpdateMissionTest(dt);
      }
      else if (this._longshotTest)
      {
        this.UpdateLongshotTest(dt);
      }
      else if (this._rangeTest)
      {
        this.UpdateRangeTest(dt);
      }
      else if (this._breakTest)
      {
        this.UpdateBreakTest(dt);
      }
      else if (this._weaponsTest)
      {
        this.UpdateWeaponsTest(dt);
      }
      else if (this._terrainTest)
      {
        this.UpdateTerrainTest(dt);
      }
      else if (this._photoTest)
      {
        this.UpdatePhotoTest(dt);
      }
      else if (this._grassShot)
      {
        this.UpdateGrassShot(dt);
      }
      else if (this._boomShot)
      {
        this.UpdateBoomShot(dt);
      }
      else if (this._flipbookTest)
      {
        this.UpdateFlipbookTest(dt);
      }
      else if (this._lagCheck)
      {
        this.UpdateLagCheck(dt);
      }
      else if (this._test)
      {
        this.UpdateTest(dt);
      }
      else
      {
        if (this._gaitShot)
        {
          this.UpdateGaitShot(dt); // dev capture rides the live branch (AI walks the enemy)
        }

        this.UpdateStreaming(dt);
        this.PassCommandIfNeeded();
        this.UpdatePlayerInput(dt);
        this.MaintainTorsoWorldYaw();
        if (this.TickReady(this._enemy))
        {
          this.UpdateEnemyAI(this.TickDt(this._enemy, dt));
        }

        if (this._foe != null && this.TickReady(this._foe.Walker))
        {
          this.UpdateFoeAI(this.TickDt(this._foe.Walker, dt));
        }

        this.UpdateAlliesAI(dt);
        this.UpdateBracketAI(dt);
        this.UpdateGuardAI(dt);
        this._aimPoint = this.TorsoAimPoint();
        if (Input.MouseMode == Input.MouseModeEnum.Captured && !this._playerDead)
        {
          // One trigger: the left mouse button fires the SELECTED
          // weapon (1/2/3 or wheel to switch - see the panel).
          var aim = this._aimPoint;
          if (Input.IsMouseButtonPressed(MouseButton.Left))
          {
            this.FireSelected(aim);
          }
        }
      }
      StepWalker(this._player, delta);
      if (this.TickReady(this._enemy))
      {
        StepWalker(this._enemy, this.TickDt(this._enemy, (float)delta));
      }

      if (this._foe != null && this._foe.Walker.IsReady && this.TickReady(this._foe.Walker))
      {
        StepWalker(this._foe.Walker, this.TickDt(this._foe.Walker, (float)delta));
      }

      foreach (var a in this._allies)
      {
        if (a.Walker.IsReady && this.TickReady(a.Walker))
        {
          StepWalker(a.Walker, this.TickDt(a.Walker, (float)delta));
        }
      }

      this.StepSounds();
      this.StepSnowFx((float)delta);
      this.ThrusterSounds();
      this.StepAmbient((float)delta);
      this.ResolveCollisions();
      this.StompCheck();
      if (dt > 0f)
      {
        var v = (this._player.RootPos - this._prevPlayerRoot) / dt;
        if (v.Length() > VelEstimateCap)
        {
          v = v.Normalized() * VelEstimateCap; // teleports are not velocity
        }

        this._playerVel = this._playerVel.Lerp(v, VelEstimateBlend);
        this._prevPlayerRoot = this._player.RootPos;
      }
      if (dt > 0f)
      {
        if (this.TickReady(this._enemy))
        {
          var ev = (this._enemy.RootPos - this._prevEnemyRoot) / this.TickDt(this._enemy, dt);
          if (ev.Length() > VelEstimateCap)
          {
            ev = ev.Normalized() * VelEstimateCap;
          }

          this._enemyVel = this._enemyVel.Lerp(ev, VelEstimateBlend);
          this._prevEnemyRoot = this._enemy.RootPos;
        }
        if (this._foe != null && this.TickReady(this._foe.Walker))
        {
          var fv = (this._foe.Walker.RootPos - this._foe.PrevRoot) / this.TickDt(this._foe.Walker, dt);
          if (fv.Length() > VelEstimateCap)
          {
            fv = fv.Normalized() * VelEstimateCap;
          }

          this._foe.Vel = this._foe.Vel.Lerp(fv, VelEstimateBlend);
          this._foe.PrevRoot = this._foe.Walker.RootPos;
        }
        foreach (var a in this._allies)
        {
          if (!this.TickReady(a.Walker))
          {
            continue;
          }

          var av = (a.Walker.RootPos - a.PrevRoot) / this.TickDt(a.Walker, dt);
          if (av.Length() > VelEstimateCap)
          {
            av = av.Normalized() * VelEstimateCap;
          }

          a.Vel = a.Vel.Lerp(av, VelEstimateBlend);
          a.PrevRoot = a.Walker.RootPos;
        }
      }
      this.UpdateCombat(dt);
      this.UpdateDroop(dt);
      this._partsPlayer.StepDebris(dt);
      this._partsEnemy.StepDebris(dt);
      if (this._foe != null)
      {
        this._foe.Parts.StepDebris(dt);
      }

      foreach (var a in this._allies)
      {
        a.Parts.StepDebris(dt);
      }
    }
    for (int i = this._feed.Count - 1; i >= 0; i--)
    {
      float life = this._feed[i].Life - dt;
      if (life <= 0f)
      {
        this._feed.RemoveAt(i);
      }
      else
      {
        this._feed[i] = (this._feed[i].Text, life);
      }
    }
    this.UpdateHud();
    // No crosshair sway: the reticle stays on screen centre
    // whatever state the rider is in (owner, 2026-11-15 - the
    // head-destroyed "sensor sway" drew a wandering figure-8
    // and was removed at his word).
    if (!this._test && !this._weaponsTest && !this._breakTest && !this._terrainTest && !this._photoTest && !this._labOpen)
    {
      this.UpdateReticle();
    }

    this.UpdateCamera(this._frozen ? 0f : dt);
    if (this._pendingPath != null && !this._capturing)
    {
      this.DoCapture();
    }
  }
}

