using Godot;
using System.Collections.Generic;

// RiderDuel: two-rider duel — a PLAYER rider vs an ENEMY rider, each
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
//   RiderDuel.cs            fields, _Ready, _Process, combat stepper, reset
//   RiderDuel.Player.cs     input            RiderDuel.Enemy.cs    AI + gunnery
//   RiderDuel.Weapons.cs    aim/fire/damage  RiderDuel.Fx.cs       particles/feedback
//   RiderDuel.Audio.cs      sound            RiderDuel.Environment.cs world + props
//   RiderDuel.Lab.cs        lab/construction RiderDuel.Hud.cs      cockpit panel
//   RiderDuel.Camera.cs     captures/camera  RiderDuel.Harness.cs  test drivers
// The photoreal scene (photo/PhotoDuel.tscn, the project's main
// scene) runs this same class with the photo dressing on.
// Verification: --duel-test / --weapons-test / --breakable-test /
// --terrain-test / --photo-test run scripted choreographies and
// capture proof frames.

public partial class RiderDuel : Node3D
{
    private const float EnemyMaxSpeed = 2.2f; // AI tactical pace cap; the chassis cap is RiderSpec.MaxSpeed
    private float EnemyPace => Mathf.Min(_enemySpec.MaxSpeed, EnemyMaxSpeed);
    // Mission fronts: beyond engagement range the AI MARCHES at full
    // chassis speed toward its target instead of creeping at the
    // tactical cap — a 1000u approach at 2.2 u/s would take 8 minutes.
    private float PaceFor(RiderSpec spec, float distToTarget) =>
        WorldConfig.IsMission && distToTarget > 130f ? spec.MaxSpeed
        : Mathf.Min(spec.MaxSpeed, EnemyMaxSpeed);
    private const float RunFactor = 3.0f; // SHIFT run mode: walk speed x3 (owner, 2026-10-29; was x1.65)
    private const float TurnRate = 0.85f;
    private static float WorldBound => WorldConfig.WorldBound; // arena 115 (260 world); Mission front 915 (1900 world)

    private RiderWalker _player;
    private RiderWalker _enemy;
    private Camera3D _cam;
    private DirectionalLight3D _labLight; // lab-only fill: the rider must read as the centerpiece, not a silhouette
    private bool _test;
    private bool _labShot; // dev flag: capture the live RiderLab to /tmp/lab_live.png and quit
    private bool _hectorCam; // dev flag: swap player to Hector, force-deploy, capture the chase view
    private bool _multiShot; // dev flag: multi-fit the lab loadout before the --lab-shot capture
    private bool _hectorLab; // dev flag: open the lab on the Hector for the --lab-shot capture
    private string _labRiderId; // dev flag: --lab-rider=<id> opens the lab on that rider for the --lab-shot capture
    private bool _layoutDumped;
    private int _labShotFrames;
    private bool _hectorCamDone; private float _hectorCamT; private bool _hectorCamFired;
    // Set by the photoreal scene itself (photo/PhotoDuel.tscn
    // carries PhotoMode = true in its root's scene data): the
    // scene declares what it is, the engine never sniffs names.
    [Export] public bool PhotoMode;
    private bool _photo; // photoreal environment (PhotoEnv / photo/)
    private bool _photoTest; // --photo-test: capture overview + ground frames, print stats, quit
    private bool _grassShot; // TEMP probe: census + capture the grass clump instances
    private bool _boomShot; // TEMP probe: rider visibility across the pitch travel (round 28)
    private bool _leaveShot, _leaveShotDone, _leaveShotSaved; // proof hook: frame the exit popup
    private float _leaveShotT;
    private bool _boomCheck; // TEMP probe: boom vs buildings + photo level frame (round 28 follow-up)
    private bool _lagCheck; // TEMP probe: camera lag + rock/tree movement rules (round 29)
    private bool _flipbookTest; // TEMP probe: Freelancer flipbook fireball (explosion prototype)
    private bool _grassStaged;
    private Vector3 _grassPos;
    private bool _gaitShot; // --gait-shot: dev flag — capture the enemy mid-stride (enemySide), quit
    private float _gaitT;
    private float _gaitDumpT;
    private int _photoPhase;
    private float _photoT, _fpsSum;
    private ulong _warmStart; // photo-mode shader warmup (lab): wall-clock start, 0 = not begun
    private int _warmPose;    // warmup camera pose index (4 = done)
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
    private string _pendingPath, _pendingTag, _camMode = CamModes.Chase;
    private System.Action _afterCapture;

    // test choreography
    private int _phase;
    private double _phaseT;
    private int _strideShots;
    private float _minTreeSep, _minRiderSep;
    private int _pLimpStep;
    private int _aimStep;
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
    // opponents — a Enyo and an Hector — and every rider
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
        public string BallisticSlot;
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
        public Vector3 Patrol;
        public float PatrolT;
    }
    private Foe _foe;
    private bool PrecisionTest => _test || _weaponsTest || _breakTest || _terrainTest;
    private bool _ffaTest; // --ffa-test: staged three-way AI probe
    private bool _missionTest; // --mission-test: Mission front smoke (world facts, base/vista captures, AI march)
    private bool _longshotTest; // --longshot-test: shell line vs sight line at 600u (parallel-fire proof)
    private bool _rangeTest; // --range-test: per-weapon Range enforcement proofs (laser reach, LRM clamp, shell burnout)
    private int _foeShots;
    private bool _lrmProbe; // --lrm-probe: staged rocket-impact measurement
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
    private bool _roofTest; // --roof-test: jump onto a building roof and stand there
    private readonly System.Collections.Generic.List<(string Target, Vector3 Point)> _lrmImpacts = new();
    private Vector3 _enemyVel;
    private Vector3 _prevEnemyRoot;
    private RiderWalker _foe0TargetW; // foe 0's current AI target (re-picked on a think timer)
    private float _foe0ThinkT;
    // Which rider each side fields (round 22: chosen in the lab).
    private RiderSpec _playerSpec = RiderSpecs.Enyo;
    private RiderSpec _enemySpec = RiderSpecs.Hector;
    private WeaponDef _defEnergy, _defBallistic, _defPodL, _defPodR, _defEnemyBallistic;
    private string _enemyBallisticSlot;
    private readonly System.Collections.Generic.List<(string Slot, WeaponDef Def)> _gunsEnergy = new(), _gunsBallistic = new(), _podGunsL = new(), _podGunsR = new();
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
        public string Slot;
        public WeaponDef Def;
        public int Path;
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
    private int _selStage; private float _selHoldT;
    private bool _running; // SHIFT held with W this frame (panel readout)
    private readonly System.Random _rng = new System.Random(20261027);
    private Vector3 _prevPlayerRoot;
    private Vector3 _playerVel;
    // Knockdown by weight (owner): impact weight accumulates over a
    // short window; past 20% of the rider's tonnage it topples.
    private float _combatClock;
    private readonly System.Collections.Generic.List<(MeshInstance3D Mi, float Life)> _puffs = new();
    // Per-weapon hit effects: spark bursts (laser), explosion bundles
    // (rockets), scorch decals (cannon shells).
    private readonly System.Collections.Generic.List<(MeshInstance3D Mi, Vector3 Vel, float Life)> _sparks = new();
    private readonly System.Collections.Generic.List<(MeshInstance3D Mi, float Age)> _scorches = new();
    private int _fxSparkBursts, _fxBooms, _fxScorches;
    private readonly RandomNumberGenerator _fxRng = new() { Seed = 4242 };
    private bool _exploding; // true while a death explosion breaks every part at once
    private static readonly BoxMesh SparkMesh = new() { Size = new Vector3(0.11f, 0.11f, 0.11f) };
    private readonly System.Collections.Generic.Dictionary<Projectile, Vector3> _lastPuff = new();
    private readonly System.Collections.Generic.List<(Label3D L, float Life)> _dmgNums = new();
    // ---------- weapons test ----------
    private bool _weaponsTest;
    private bool _enemyMayFire;
    private int _wPhase;
    private double _wPhaseT;
    private int _wShotsFired;
    private bool _wCaptured, _wDeadShotQueued, _wDeathLogged;
    private Destructible _wEnvB, _wEnvT;
    private int _wEnvFx0, _wEnvChunks0, _wEnvParticles0;
    private float _wJoltSeen;
    private bool _wHitCaptured;
    private int _wEnvStep;
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
    private int _bPhase, _bStep;
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
    private int _tPhase;
    private double _tPhaseT;
    private float _tSlopeYaw;
    private bool _tStrideLogged;
    private Vector3 _tRunFrom;
    private float _tRunMark = -1f;
    private float _tRunMaxSplit, _tRunMaxLat;
    private float _tJumpApex, _tJumpGround;
    private bool _tJumpTried;
    private int _tJumpStage;
    private float _tJumpMarkT;
    private Vector2 _tJumpXZ;
    private float _tApex2, _tBurnDist, _tBurnSpeed;
    private Vector2 _tFlyStart;
    private bool _tParked;

    private Crosshair _cross;
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
    private int _targetIdx; // the ONE active enemy (owner: Tab switches); 0 = primary foe, 1 = second foe
    private LeaveOverlay _leaveDialog;
    private CanvasLayer _labLoad; // loading cover held over the lab until the warmup settles (owner)
    private Control _labLoadArt; // the blinking loading art inside the cover (owner)
    private double _labLoadT;
    private PanelContainer _panelHealth, _panelWeapons, _panelTarget; // corner blocks (owner: no bottom dashboard)
    private PanelContainer _panelRadar; // square radar panel, top-right (owner)
    private Control _radarFrame; // white corner brackets around the radar panel
    private RadarView _radar;
    private Label _lblRange, _lblSpeed, _lblLegs;
    private readonly System.Collections.Generic.Dictionary<string, (Label Lab, ProgressBar Bar)> _partCells = new();
    private readonly System.Collections.Generic.List<(string Text, float Life)> _feed = new();
    private float _shake;
    private float _playerDroopT, _enemyDroopT;
    // breakable consequences, transient timers
    private float _denyLaserT, _denyAcT, _denyLrmT, _hudFlickerT;
    private string _lastDenied = "";
    private int _rocketsLaunched, _beamsSpawned;

    public override void _Ready()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--duel-test") _test = true;
            if (arg == "--mission") WorldConfig.Mode = WorldConfig.MapMode.Mission; // must land before BuildTerrain below
            if (arg == "--mission-test") { _missionTest = true; WorldConfig.Mode = WorldConfig.MapMode.Mission; }
            if (arg == "--longshot-test") { _longshotTest = true; WorldConfig.Mode = WorldConfig.MapMode.Mission; }
            if (arg == "--range-test") { _rangeTest = true; WorldConfig.Mode = WorldConfig.MapMode.Mission; }
            if (arg == "--weapons-test") _weaponsTest = true;
            if (arg == "--breakable-test") _breakTest = true;
            if (arg == "--terrain-test") _terrainTest = true;
            if (arg == "--lab-shot") _labShot = true;
            if (arg == "--photo") _photo = true;
            if (arg == "--photo-test") { _photo = true; _photoTest = true; }
            if (arg == "--grass-shot") { _photo = true; _grassShot = true; }
            if (arg == "--boom-shot") _boomShot = true;
            if (arg == "--flipbook-test") _flipbookTest = true;
            if (arg == "--leave-shot") _leaveShot = true;
            if (arg == "--boom-check") _boomCheck = true;
            if (arg == "--lag-check") _lagCheck = true;
            if (arg == "--gait-shot") _gaitShot = true;
            if (arg == "--hector-cam") _hectorCam = true;
            if (arg == "--multifit-shot") _multiShot = true;
            if (arg == "--hector-lab") _hectorLab = true;
            if (arg.StartsWith("--lab-rider=")) _labRiderId = arg.Substring("--lab-rider=".Length);
            if (arg.StartsWith("--player-rider=")) _playerSpec = RiderSpecs.ById(arg.Substring("--player-rider=".Length));
            if (arg.StartsWith("--enemy-rider=")) _enemySpec = RiderSpecs.ById(arg.Substring("--enemy-rider=".Length));
            if (arg == "--ffa-test") _ffaTest = true;
            if (arg == "--lrm-probe") _lrmProbe = true;
            if (arg == "--view-shot") _viewShot = true;
            if (arg == "--point-test") _pointTest = true;
            if (arg == "--point-hector") _pointHectorFirst = true;
            if (arg == "--point-center") _pointCenter = true;
            if (arg == "--point-walk") _pointWalk = true;
            if (arg == "--point-close") _pointClose = true;
            if (arg == "--brake-test") _brakeTest = true;
            if (arg == "--roof-test") _roofTest = true;
        }
        if (PhotoMode) _photo = true; // the photoreal scene's own declaration
        _hit = new HitModel();

        var sun = new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-48f, -35f, 0f),
            ShadowEnabled = true,
            LightEnergy = 1.45f,
            LightColor = new Color(1f, 0.955f, 0.885f)
        };
        sun.DirectionalShadowMaxDistance = 320f;
        AddChild(sun);
        _labLight = new DirectionalLight3D
        {
            LightEnergy = 0.85f,
            LightColor = new Color(0.9f, 0.95f, 1f),
            ShadowEnabled = false,
            Visible = false
        };
        AddChild(_labLight);
        var skyMat = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color(0.20f, 0.40f, 0.71f),
            SkyHorizonColor = new Color(0.63f, 0.73f, 0.84f),
            SkyCurve = 0.09f,
            SkyEnergyMultiplier = 1.0f,
            GroundBottomColor = new Color(0.16f, 0.145f, 0.12f),
            GroundHorizonColor = new Color(0.56f, 0.56f, 0.52f),
            GroundCurve = 0.08f
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
                AmbientLightEnergy = 0.34f,
                ReflectedLightSource = Environment.ReflectionSource.Sky,
                TonemapMode = Environment.ToneMapper.Aces,
                FogEnabled = true,
                FogLightColor = WorldConfig.Biome switch
                {
                    WorldConfig.BiomeKind.Desert => new Color(0.82f, 0.72f, 0.56f),
                    WorldConfig.BiomeKind.City => new Color(0.45f, 0.54f, 0.62f),
                    _ => new Color(0.63f, 0.73f, 0.84f)
                },
                FogDensity = WorldConfig.IsMission ? 0.00042f : 0.0011f,
                FogSunScatter = 0.25f
            };
        if (_photo) PhotoEnv.Tune(duelEnv, sun);
        ApplyMapSky(duelEnv, sun);
        AddChild(new WorldEnvironment { Environment = duelEnv });

        BuildTerrain();
        Main.Roofs.Clear(); // rebuilt by the AddBuilding calls below
        Main.Ramps.Clear(); Main.DiscRoofs.Clear(); Main.Walls.Clear();
        // Round-1 ARENA props at fixed spots around the origin.
        // Arena only (owner: "random pieces of building are
        // sticking out of the ground" — in a 1900u mission
        // world the origin is open wilderness; on Harbor it is
        // a mountain, so these four buildings planted
        // themselves on the slope there, half-buried. Missions
        // dress themselves via BuildBases/ScatterCity/
        // ScatterNature below.)
        if (!WorldConfig.IsMission)
        {
            AddTree(new Vector3(-12f, 0f, -8f));
            AddTree(new Vector3(14f, 0f, -18f));
            AddTree(new Vector3(6f, 0f, 16f));
            AddTree(new Vector3(-26f, 0f, 26f));
            AddTree(new Vector3(30f, 0f, -30f));
            AddTree(new Vector3(-32f, 0f, -6f));
            AddTree(new Vector3(26f, 0f, 30f));
            // Larger (owner, 2026-10-31): footprints 10-15u, heights
            // 11-16u — real cover next to a 13-14u rider, and every
            // roof stays under the ~24u thruster apex so a jump-jet
            // rider can land on top.
            AddBuilding(new Vector3(-20f, 0f, 14f), new Vector3(13f, 11f, 11f));
            AddBuilding(new Vector3(22f, 0f, 8f), new Vector3(12f, 13f, 12f));
            AddBuilding(new Vector3(14f, 0f, -26f), new Vector3(15f, 12f, 10f));
            AddBuilding(new Vector3(-14f, 0f, -26f), new Vector3(10f, 16f, 10f));
        }
        if (WorldConfig.IsMission) BuildBases();
        if (WorldConfig.Biome == WorldConfig.BiomeKind.City) ScatterCity();
        ScatterNature();
        if (_photo)
        {
            var propPts = new System.Collections.Generic.List<(Vector2 P, float R)>();
            foreach (var c in _props) propPts.Add((c.P, c.R));
            if (WorldConfig.Biome == WorldConfig.BiomeKind.Forest)
                PhotoEnv.AddGrassField(this, _buildingPts, propPts); // the scanned grass is lush green — forest only
            PhotoEnv.AddBackdrop(this);
        }

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
        _partsPlayer = new RiderParts(_player, _playerSpec.PartsId);
        _partsEnemy = new RiderParts(_enemy, _enemySpec.PartsId);
        _player.RefreshMaterials();
        _enemy.RefreshMaterials();
        if ((!PrecisionTest && !_lrmProbe && !_viewShot && !_brakeTest && !_roofTest) || _ffaTest) BuildFoe();
        RefreshHitCaps();
        _prevPlayerRoot = _player.RootPos;
        _prevEnemyRoot = _enemy.RootPos;
        _hit.PartResolver = (id, pt) => id == Roles.Player
            ? _partsPlayer.AttributePoint(pt)
            : id == Roles.Foe && _foe != null ? _foe.Parts.AttributePoint(pt)
            : _partsEnemy.AttributePoint(pt);
        _hit.PartRayCast = (id, from, dir, maxDist) => id == Roles.Player
            ? _partsPlayer.RaycastParts(from, dir, maxDist)
            : id == Roles.Foe && _foe != null ? _foe.Parts.RaycastParts(from, dir, maxDist)
            : _partsEnemy.RaycastParts(from, dir, maxDist);
        _partsPlayer.OnBreak = (id, c) => OnPartBroken(_partsPlayer, id, c);
        _partsEnemy.OnBreak = (id, c) => OnPartBroken(_partsEnemy, id, c);

        _cam = new Camera3D { Fov = 70f };
        AddChild(_cam);
        _cam.GlobalPosition = new Vector3(-14f, 9f, 44f);
        _cam.LookAt(new Vector3(0f, 6f, 18f), Vector3.Up);

        var layer = new CanvasLayer();
        _uiLayer = layer;
        AddChild(layer);
        _cross = new Crosshair();
        layer.AddChild(_cross);
        _cross.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        BuildHud(layer);
        ApplyLoadout();
        if (!_test && !_weaponsTest && !_breakTest && !_terrainTest && !_photoTest && !_gaitShot && !_ffaTest && !_lrmProbe && !_viewShot && !_brakeTest && !_roofTest && !_pointTest && !_grassShot && !_boomShot && !_boomCheck && !_lagCheck && !_flipbookTest && !_missionTest && !_leaveShot && !_longshotTest && !_rangeTest)
        {
            // Pre-game RiderLab (owner): the duel waits behind the lab
            // until DEPLOY. Test modes skip straight to the duel.
            _labOpen = true;
            BuildLabLoadOverlay();
            Input.MouseMode = Input.MouseModeEnum.Visible;
            SetHudPanelsVisible(false);
            _radar.Visible = false;
            _feedLabel.Visible = false;
            OpenLab();
        }

        _lastPlayerPos = _player.RootPos;
        InitSfx();
        GD.Print($"DUEL ready test={_test} weaponsTest={_weaponsTest} breakTest={_breakTest} terrainTest={_terrainTest} props={_props.Count} circles");
        if (_terrainTest) TerrainSelfCheck();
        if (_weaponsTest)
        {
            WSetup(new Vector3(0f, Main.HeightAt(0f, 18f), 18f), new Vector3(0f, Main.HeightAt(0f, -12f), -12f));
            ApplyRangeFit();
            SetWPhase(0);
        }
        if (_breakTest)
        {
            BSetup(new Vector3(0f, Main.HeightAt(0f, 18f), 18f), new Vector3(0f, Main.HeightAt(0f, -12f), -12f));
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
        _hpPlayer = _playerSpec.MaxHp;
        _hpEnemy = _enemySpec.MaxHp;
        _playerDead = _enemyDead = false;
        _partsPlayer.RestoreAll();
        _partsEnemy.RestoreAll();
        SyncLegConsequences(_player, _partsPlayer);
        SyncLegConsequences(_enemy, _partsEnemy);
        if (_foe != null)
        {
            _foe.Hp = _foe.Spec.MaxHp;
            _foe.Dead = false;
            _foe.Cd = 0f;
            _foe.DroopT = 0f;
            _foe.Parts.RestoreAll();
            SyncLegConsequences(_foe.Walker, _foe.Parts);
            _foe.Walker.Halted = false;
            _foe.Walker.Droop = 0f;
            _foe.Walker.SetDarkened(false);
        }
        _denyLaserT = _denyAcT = _denyLrmT = _hudFlickerT = 0f;
        _lastDenied = "";
        _feed.Clear();
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
        GD.Print("WPN RESET both riders restored to spawn, HP full");
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
        var bg = new StyleBoxFlat { BgColor = new Color(0.05f, 0.05f, 0.07f, 0.8f) };
        bg.SetCornerRadiusAll(3);
        var fg = new StyleBoxFlat { BgColor = fill };
        fg.SetCornerRadiusAll(3);
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
        foreach (var grp in _groups)
        {
            grp.DenyT = Mathf.Max(grp.DenyT - dt, 0f);
            foreach (var g in grp.Guns) g.Cd = Mathf.Max(g.Cd - dt, 0f);
        }
        _denyLaserT = Mathf.Max(_denyLaserT - dt, 0f);
        _denyAcT = Mathf.Max(_denyAcT - dt, 0f);
        _denyLrmT = Mathf.Max(_denyLrmT - dt, 0f);
        _hudFlickerT = Mathf.Max(_hudFlickerT - dt, 0f);
        for (int i = _feed.Count - 1; i >= 0; i--)
        {
            float life = _feed[i].Life - dt;
            if (life <= 0f) _feed.RemoveAt(i);
            else _feed[i] = (_feed[i].Text, life);
        }
        ProcessLrmQueue(dt);
        for (int i = _shots.Count - 1; i >= 0; i--)
        {
            var p = _shots[i];
            p.Step(dt, _hit);
            if (p.Dead) { _shots.RemoveAt(i); _lastPuff.Remove(p); p.QueueFree(); }
        }
        _shake = Mathf.Max(_shake - dt * 1.6f, 0f);
        for (int i = _dmgNums.Count - 1; i >= 0; i--)
        {
            var (l, life0) = _dmgNums[i];
            float life = life0 - dt;
            if (life <= 0f) { l.QueueFree(); _dmgNums.RemoveAt(i); continue; }
            l.GlobalPosition += Vector3.Up * 1.7f * dt;
            var m = l.Modulate;
            m.A = Mathf.Clamp(life / 0.9f, 0f, 1f);
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
                    sm.AlbedoColor = new Color(c.R, c.G, c.B, 0.45f * (life / 0.4f));
                }
                _puffs[i] = (mi, life);
            }
        }
        for (int i = _sparks.Count - 1; i >= 0; i--)
        {
            var (mi, vel, life) = _sparks[i];
            life -= dt;
            if (life <= 0f) { mi.QueueFree(); _sparks.RemoveAt(i); continue; }
            vel.Y -= 3.4f * dt;
            mi.GlobalPosition += vel * dt;
            _sparks[i] = (mi, vel, life);
        }
        for (int i = _scorches.Count - 1; i >= 0; i--)
        {
            var (mi, age) = _scorches[i];
            age += dt;
            if (age > 7f) { mi.QueueFree(); _scorches.RemoveAt(i); continue; }
            if (age > 5f && mi.MaterialOverride is StandardMaterial3D sm)
            {
                var c = sm.AlbedoColor;
                sm.AlbedoColor = new Color(c.R, c.G, c.B, 0.82f * (7f - age) / 2f);
            }
            _scorches[i] = (mi, age);
        }
        for (int i = _fx.Count - 1; i >= 0; i--)
        {
            // Lifetime is in seconds, but an FX node must also survive at
            // least 3 rendered frames: under a slow renderer a single
            // frame's dt can exceed a beam's whole 0.12s life and it
            // would be freed before ever being drawn.
            float life = _fx[i].Life - dt;
            int frames = _fx[i].Frames + 1;
            if (life <= 0f && frames >= 3) { _fx[i].Node.QueueFree(); _fx.RemoveAt(i); }
            else _fx[i] = (_fx[i].Node, life, frames);
        }
        if (!_test && !_terrainTest && !_photoTest && ((!_weaponsTest && !_breakTest) || _enemyMayFire)) UpdateEnemyGunnery(dt);
        if (_foe != null && !_test && !_terrainTest && !_photoTest && !_weaponsTest && !_breakTest) UpdateFoeGunnery(dt);
    }

    private static void StepWalker(RiderWalker w, double delta)
    {
        int n = Mathf.Max(1, Mathf.CeilToInt((float)delta / 0.09f));
        double h = delta / n;
        for (int i = 0; i < n; i++) w.StepFrame(h);
    }

    public override void _Process(double delta)
    {
        if (_pointTest && _pointHectorFirst && !_pointSwapDone && _player != null && _player.IsReady)
        {
            // Select Hector the way the owner does — in the lab,
            // before the duel starts (hectorcam's ritual). A
            // MID-RUN swap is invalid measurement: a stale scorch
            // entry from the old walker makes UpdateCombat throw
            // every frame and freezes the reticle/camera updates
            // downstream of it (found by this probe, 2026-11-01).
            _pointSwapDone = true;
            SetRiderSelection("hector", "enyo");
            ApplyLoadout();
            if (_lab != null) { _lab.QueueFree(); _lab = null; }
            _labOpen = false;
            if (_labLight != null) _labLight.Visible = false;
            SetHudPanelsVisible(true); _radar.Visible = true; _feedLabel.Visible = true;
            _pointHector = true;
            GD.Print("POINTTEST selected Hector at start (lab path)");
        }
        if (_leaveShot && !_leaveShotDone && _player != null && _player.IsReady)
        {
            // Frame the exit popup over the live duel (proof only;
            // the pause itself is the Esc path's business).
            _leaveShotDone = true;
            SetHudPanelsVisible(true); _radar.Visible = true; _feedLabel.Visible = true;
            _leaveDialog.Open();
        }
        if (_leaveShot && _leaveShotDone && !_leaveShotSaved)
        {
            _leaveShotT += (float)delta;
            if (_leaveShotT > 0.6f)
            {
                _leaveShotSaved = true;
                GetViewport().GetTexture().GetImage().SavePng("/tmp/leave_popup.png");
                GD.Print("LEAVESHOT saved");
                GetTree().Quit();
            }
        }
        if (_hectorCam && !_hectorCamDone && _player != null && _player.IsReady)
        {
            _hectorCamDone = true;
            SetRiderSelection("hector", "enyo");
            ApplyLoadout();
            if (_lab != null) { _lab.QueueFree(); _lab = null; }
            _labOpen = false;
            if (_labLight != null) _labLight.Visible = false;
            SetHudPanelsVisible(true); _radar.Visible = true; _feedLabel.Visible = true;
            GD.Print("HECTORCAM swapped player to Hector and deployed");
        }
        if (_hectorCam && _hectorCamDone && !_labOpen)
        {
            _hectorCamT += (float)delta;
            if (!_hectorCamFired && _hectorCamT > 1.2f)
            {
                _hectorCamFired = true;
                GD.Print($"HECTORCAM defs energy={_defEnergy?.Id ?? "none"} ballistic={_defBallistic?.Id ?? "none"} podL={_defPodL?.Id ?? "none"} podR={_defPodR?.Id ?? "none"}");
                FireLaser(_enemy.RootPos + Vector3.Up * 6f);
                FireLrmSalvo(_enemy.RootPos);
            }
            if (_hectorCamT > 2.5f && _camMode == CamModes.Chase)
            {
                var back = -RiderWalker.HeadingVec(_player.TorsoYawWorld);
                var camRel = _cam.GlobalPosition - _player.RootPos;
                GD.Print($"HECTORCAM legsYaw={_player.LegsYaw:F2} torsoWorld={_player.TorsoYawWorld:F2} engineTorso={_player.EngineTorsoYaw():F2} camBehind={camRel.Dot(back):F2} camSide={camRel.Dot(new Vector3(-back.Z, 0f, back.X)):F2}");
                QueueCapture("res://proof/hector_player_chase.png", "hectorP", CamModes.Chase, () => GetTree().Quit());
                _hectorCamT = -999f;
            }
        }

        if (_hectorLab && _labOpen && _lab != null)
        {
            _hectorLab = false;
            SetRiderSelection("hector", _enemySpec.Id);
            GD.Print("HECTORLAB lab reopened on the Hector");
        }

        if (_labRiderId != null && _labOpen && _lab != null)
        {
            var id = _labRiderId;
            _labRiderId = null;
            SetRiderSelection(id, _enemySpec.Id);
            GD.Print($"LABRIDER lab reopened on {id}");
        }

        if (_multiShot && _labOpen && _lab != null)
        {
            _multiShot = false;
            _loadout.TryAssign(_playerSpec, "arm_L", WeaponCatalog.MediumLaser); // LL 5 + ML 3 = 8t = arm cap exactly
            _loadout.TryAssign(_playerSpec, "pod_L", WeaponCatalog.Srm);          // LRM 10 + SRM 4 in one pod
            _loadout.TryAssign(_playerSpec, "arm_R", WeaponCatalog.HeavyCannon);  // AC 12 + HC 16 = 28 > 25 cap: must refuse
            var err = _loadout.Check(_playerSpec, "arm_R", WeaponCatalog.HeavyCannon);
            _lab.Refresh();
            GD.Print($"MULTISHOT fitted arm_L={string.Join(",", _loadout.WeaponsIn("arm_L"))} pod_L={string.Join(",", _loadout.WeaponsIn("pod_L"))} arm_R refused: {err}");
        }

        // The capture waits for BOTH wall time and a frame
        // count: on a software rasterizer the first frame alone
        // can take ~19s (shader compilation), so a wall-clock-only
        // trigger photographed frame 1 — before the recreated
        // lab's first _Process had run its live layout even once.
        if (_labShot) _labShotFrames++;
        if (_labShot && _lab != null && Time.GetTicksMsec() > 19500 && _labShotFrames > 45 && !_layoutDumped)
        {
            _layoutDumped = true;
            GD.Print(_lab.DumpLayout());
        }

        if (_labShot && Time.GetTicksMsec() > 20000 && _labShotFrames > 50)
        {
            _labShot = false;
            GetViewport().GetTexture().GetImage().SavePng("/tmp/lab_live.png");
            GD.Print("LABSHOT saved");
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
            if (_labLoad != null) _labLoad.Visible = _photo && _warmPose < 4;
            if (_labLoad != null && _labLoad.Visible)
            {
                _labLoadT += delta;
                FlowUi.PulseLoadingArt(_labLoadArt, _labLoadT); // slow blink (owner)
            }
            // The duel waits behind the RiderLab: a showroom view
            // (owner, MW4-style) — the rider stands front three-
            // quarter in the paperdoll area, lit by a lab fill, so
            // the slot panels sit ON the machine. A fixed angle
            // (plus a barely-there sway) replaces the orbit: the
            // paperdoll mapping only makes sense from the front.
            _labLight.Visible = true;
            if (_photo && _warmPose < 4)
            {
                // Shader warmup (#6, photo mode only): while the lab
                // is up, sweep the camera through the duel's
                // viewpoints and fire one underground FX burst, so
                // every pipeline — splat terrain, foliage, fog,
                // beams, particles — compiles BEFORE the duel
                // instead of hitching mid-fight. Timing is
                // wall-clock with a 2-frame minimum per pose, so the
                // sweep completes on any hardware (a raw delta sum
                // was skipped entirely by llvmpipe's giant first
                // frames).
                if (_warmStart == 0) _warmStart = Time.GetTicksMsec();
                _warmPoseFrames++;
                ulong warmEl = Time.GetTicksMsec() - _warmStart;
                var wc = _player.RootPos + Vector3.Up * 5.6f;
                // The poses ride on the player's position (owner:
                // "riders are not visible in the lab"): framed on
                // the arena origin, in a mission world they stared
                // at empty terrain 620u from the rider for the
                // whole sweep, and the lab read as broken. The
                // rider is in frame at every pose now.
                var pp = _player.RootPos;
                switch (_warmPose)
                {
                    case 0:
                        _cam.GlobalPosition = pp + new Vector3(58f, 34f, 64f);
                        _cam.LookAt(pp + Vector3.Up * 2f, Vector3.Up);
                        break;
                    case 1:
                        var low = pp + new Vector3(-21f, 0f, -1f);
                        low.Y = Main.HeightAt(low.X, low.Z) + 2.1f;
                        _cam.GlobalPosition = low;
                        _cam.LookAt(pp + Vector3.Up * 5.5f, Vector3.Up);
                        break;
                    case 2:
                        _cam.GlobalPosition = pp + new Vector3(-88f, 44f, 90f);
                        _cam.LookAt(pp + Vector3.Up * 2f, Vector3.Up);
                        break;
                    default:
                        _cam.GlobalPosition = wc + new Vector3(10f, 3.4f, 10f);
                        _cam.LookAt(wc, Vector3.Up);
                        break;
                }
                _labLight.GlobalRotation = _cam.GlobalRotation;
                if (!_warmFx && _warmPose >= 1)
                {
                    _warmFx = true;
                    var under = new Vector3(0f, -60f, 0f);
                    SpawnBeam(under, under + new Vector3(6f, 2f, 0f), new Color(1f, 0.2f, 0.15f), 0.15f, 0.4f);
                    SpawnExplosionFx(under + new Vector3(4f, 0f, 4f));
                    SpawnImpactLight(under, new Color(1f, 0.6f, 0.3f), 3f, 12f, 0.4f);
                    GD.Print("PHOTO warmup: camera sweep + underground FX burst");
                }
                ulong sliceEnd = _warmPose switch { 0 => 900UL, 1 => 1800UL, 2 => 2600UL, _ => 3400UL };
                if (warmEl > sliceEnd && _warmPoseFrames >= 2) { _warmPose++; _warmPoseFrames = 0; }
                return;
            }
            float sway = Mathf.Sin((float)Time.GetTicksMsec() / 1000f * 0.15f) * 0.045f;
            float labK = _playerSpec.Height / 13f; // showroom framing scales with the chassis, like the chase cam
            var center = _player.RootPos + Vector3.Up * (5.6f * labK);
            var fwd = new Vector3(Mathf.Sin(_player.LegsYaw), 0f, Mathf.Cos(_player.LegsYaw));
            var viewDir = fwd.Rotated(Vector3.Up, 2.72f + sway); // mostly in front of the rider, slightly to its side
            _cam.GlobalPosition = center + viewDir * (14.5f * labK) + Vector3.Up * (3.4f * labK);
            _cam.LookAt(center, Vector3.Up);
            _cam.RotateY(0.46f); // pan in camera space: rider right, centered under its slot panels (owner, adjusted twice at his word)
            _labLight.GlobalRotation = _cam.GlobalRotation;
            return;
        }

        if (!_frozen)
        {
            _simT += delta;
            if (_viewShot) UpdateViewShot(dt);
            else if (_pointTest) UpdatePointTest(dt);
            else if (_brakeTest) UpdateBrakeTest(dt);
            else if (_roofTest) UpdateRoofTest(dt);
            else if (_lrmProbe) UpdateLrmProbe(dt);
            else if (_ffaTest) UpdateFfaTest(dt);
            else if (_missionTest && !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("AOT_TERRATEST")) && WorldConfig.TerracedCity) UpdateTerraceTest(dt);
            else if (_missionTest) UpdateMissionTest(dt);
            else if (_longshotTest) UpdateLongshotTest(dt);
            else if (_rangeTest) UpdateRangeTest(dt);
            else if (_breakTest) UpdateBreakTest(dt);
            else if (_weaponsTest) UpdateWeaponsTest(dt);
            else if (_terrainTest) UpdateTerrainTest(dt);
            else if (_photoTest) UpdatePhotoTest(dt);
            else if (_grassShot) UpdateGrassShot(dt);
            else if (_boomShot) UpdateBoomShot(dt);
            else if (_boomCheck) UpdateBoomCheck(dt);
            else if (_flipbookTest) UpdateFlipbookTest(dt);
            else if (_lagCheck) UpdateLagCheck(dt);
            else if (_test) UpdateTest(dt);
            else
            {
                if (_gaitShot) UpdateGaitShot(dt); // dev capture rides the live branch (AI walks the enemy)
                UpdatePlayerInput(dt);
                MaintainTorsoWorldYaw();
                UpdateEnemyAI(dt);
                if (_foe != null) UpdateFoeAI(dt);
                _aimPoint = TorsoAimPoint();
                if (Input.MouseMode == Input.MouseModeEnum.Captured && !_playerDead)
                {
                    // One trigger: the left mouse button fires the SELECTED
                    // weapon (1/2/3 or wheel to switch — see the panel).
                    var aim = _aimPoint;
                    if (Input.IsMouseButtonPressed(MouseButton.Left))
                        FireSelected(aim);
                }
            }
            StepWalker(_player, delta);
            StepWalker(_enemy, delta);
            if (_foe != null && _foe.Walker.IsReady) StepWalker(_foe.Walker, delta);
            StepSounds();
            UpdateLifts((float)delta);
            ResolveCollisions();
            StompCheck();
            if (dt > 0f)
            {
                var v = (_player.RootPos - _prevPlayerRoot) / dt;
                if (v.Length() > 8f) v = v.Normalized() * 8f; // teleports are not velocity
                _playerVel = _playerVel.Lerp(v, 0.5f);
                _prevPlayerRoot = _player.RootPos;
            }
            if (dt > 0f)
            {
                var ev = (_enemy.RootPos - _prevEnemyRoot) / dt;
                if (ev.Length() > 8f) ev = ev.Normalized() * 8f;
                _enemyVel = _enemyVel.Lerp(ev, 0.5f);
                _prevEnemyRoot = _enemy.RootPos;
                if (_foe != null)
                {
                    var fv = (_foe.Walker.RootPos - _foe.PrevRoot) / dt;
                    if (fv.Length() > 8f) fv = fv.Normalized() * 8f;
                    _foe.Vel = _foe.Vel.Lerp(fv, 0.5f);
                    _foe.PrevRoot = _foe.Walker.RootPos;
                }
            }
            UpdateCombat(dt);
            UpdateDroop(dt);
            _partsPlayer.StepDebris(dt);
            _partsEnemy.StepDebris(dt);
            if (_foe != null) _foe.Parts.StepDebris(dt);
        }
        UpdateHud();
        // Player head destroyed: the reticle wanders (sensor loss).
        if (!_partsPlayer.Alive("head") && !_playerDead)
        {
            _cross.Sway = new Vector2(
                Mathf.Sin((float)_simT * 1.9f) * 13f,
                Mathf.Sin((float)_simT * 2.7f + 0.8f) * 9f);
        }
        else _cross.Sway = Vector2.Zero;
        if (!_test && !_weaponsTest && !_breakTest && !_terrainTest && !_photoTest && !_labOpen)
            UpdateReticle();
        UpdateCamera(_frozen ? 0f : dt);
        if (_pendingPath != null && !_capturing) DoCapture();
    }
}

