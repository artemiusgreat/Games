using Godot;
using System.Collections.Generic;

// MechDuel: two-mech duel — a PLAYER mech vs an ENEMY mech, each
// fielding a chassis chosen in the MechLab (Timber Wolf / Atlas
// are the two chassis in Data/MechSpec.cs; code names the ROLES,
// the specs name the machines). W/S throttle along the legs
// heading, A/D turn the legs, mouse twists/pitches the torso
// (full 360), chase camera on the torso facing, crosshair.
// The enemy brain advances when far, backs off when close and
// orbits in the mid band; its torso tracks the player.
// Collisions are kinematic circles resolved in code (no physics
// bodies); terrain height stays with Main.HeightAt.
//
// This class is split into partial modules (round 22):
//   MechDuel.cs            fields, _Ready, _Process, combat stepper, reset
//   MechDuel.Player.cs     input            MechDuel.Enemy.cs    AI + gunnery
//   MechDuel.Weapons.cs    aim/fire/damage  MechDuel.Fx.cs       particles/feedback
//   MechDuel.Audio.cs      sound            MechDuel.Environment.cs world + props
//   MechDuel.Lab.cs        lab/construction MechDuel.Hud.cs      cockpit panel
//   MechDuel.Camera.cs     captures/camera  MechDuel.Harness.cs  test drivers
// The photoreal scene (photo/PhotoDuel.tscn, the project's main
// scene) runs this same class with the photo dressing on.
// Verification: --duel-test / --weapons-test / --breakable-test /
// --terrain-test / --photo-test run scripted choreographies and
// capture proof frames.

public partial class MechDuel : Node3D
{
    private const float PlayerMaxSpeed = 4.2f;
    private const float EnemyMaxSpeed = 2.2f;
    private const float RunFactor = 3.0f; // SHIFT run mode: walk speed x3 (owner, 2026-10-29; was x1.65)
    private const float TurnRate = 0.85f;
    private const float WorldBound = 80f; // heightfield world is 170 wide

    private MechWalker _player;
    private MechWalker _enemy;
    private Camera3D _cam;
    private DirectionalLight3D _labLight; // lab-only fill: the mech must read as the centerpiece, not a silhouette
    private bool _test;
    private bool _labShot; // dev flag: capture the live MechLab to /tmp/lab_live.png and quit
    private bool _atlasCam; // dev flag: swap player to Atlas, force-deploy, capture the chase view
    private bool _atlasCamDone; private float _atlasCamT;
    private bool _photo; // photoreal probe environment (PhotoEnv / photo/)
    private bool _photoTest; // --photo-test: capture overview + ground frames, print stats, quit
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
    private string _pendingPath, _pendingTag, _camMode = "chase";
    private System.Action _afterCapture;

    // test choreography
    private int _phase;
    private double _phaseT;
    private int _strideShots;
    private float _minTreeSep, _minMechSep;
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
    private MechParts _partsPlayer, _partsEnemy;
    private readonly System.Collections.Generic.List<Projectile> _shots = new();
    private readonly System.Collections.Generic.List<(Node3D Node, float Life, int Frames)> _fx = new();
    private float _hpPlayer = 260f, _hpEnemy = 340f;
    private bool _playerDead, _enemyDead;
    private float _cdLaser, _cdAc, _cdLrm, _cdEnemy;
    private int _weaponSel; // selected weapon: 0 laser, 1 autocannon, 2 LRM
    // MechLab loadout (owner): the equipped weapon defs drive damage,
    // cooldowns, salvo composition and knockdown weight. Defaults equal
    // the original fixed fit, so every harness number is unchanged.
    private LoadoutState _loadout = LoadoutState.DefaultTimberWolf();
    private LoadoutState _enemyLoadout = LoadoutState.DefaultAtlas();
    // Which mech each side fields (round 22: chosen in the lab).
    private MechSpec _playerSpec = MechSpecs.TimberWolf;
    private MechSpec _enemySpec = MechSpecs.Atlas;
    private WeaponDef _defEnergy, _defBallistic, _defPodL, _defPodR, _defEnemyBallistic;
    private readonly string[] _wpnShort = { "1 LAS", "2 AC", "3 LRM" };
    private bool _labOpen;
    private MechLab _lab;
    private CanvasLayer _uiLayer;
    private bool _labChecked;
    private int _selStage; private float _selHoldT;
    private bool _running; // SHIFT held with W this frame (panel readout)
    private readonly System.Random _rng = new System.Random(20261027);
    private Vector3 _prevPlayerRoot;
    private Vector3 _playerVel;
    private readonly System.Collections.Generic.List<(float Delay, bool Left, Vector3 Aim, WeaponDef Def)> _lrmQueue = new();
    // Knockdown by weight (owner): impact weight accumulates over a
    // short window; past 20% of the mech's tonnage it topples.
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
    private ProgressBar _barPlayer, _barEnemy, _cdBarLaser, _cdBarAc, _cdBarLrm, _cdBarThr;
    private Label _lblPlayerHp, _lblEnemyHp, _banner, _bannerSub;
    private Label _feedLabel, _lblWeaponStatus, _lblCdLaser, _lblCdAc, _lblCdLrm, _lblThr;
    private PanelContainer _panel;
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
            if (arg == "--weapons-test") _weaponsTest = true;
            if (arg == "--breakable-test") _breakTest = true;
            if (arg == "--terrain-test") _terrainTest = true;
            if (arg == "--lab-shot") _labShot = true;
            if (arg == "--photo") _photo = true;
            if (arg == "--photo-test") { _photo = true; _photoTest = true; }
            if (arg == "--gait-shot") _gaitShot = true;
            if (arg == "--atlas-cam") _atlasCam = true;
        }
        if (GetTree().CurrentScene != null && GetTree().CurrentScene.Name == "PhotoDuel")
            _photo = true; // launched from the PhotoDuel scene copy (F6 / editor)
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
                // color (owner: 'mechs look like solid default material').
                // Lower ambient + stronger sun lets the texture and normal
                // maps model the surface again; fog thinned to match.
                AmbientLightEnergy = 0.34f,
                ReflectedLightSource = Environment.ReflectionSource.Sky,
                TonemapMode = Environment.ToneMapper.Aces,
                FogEnabled = true,
                FogLightColor = new Color(0.63f, 0.73f, 0.84f),
                FogDensity = 0.0011f,
                FogSunScatter = 0.25f
            };
        if (_photo) PhotoEnv.Tune(duelEnv, sun);
        AddChild(new WorldEnvironment { Environment = duelEnv });

        BuildTerrain();
        // Round-1 props (same spots as Main) + a few more for a fuller map.
        AddTree(new Vector3(-12f, 0f, -8f));
        AddTree(new Vector3(14f, 0f, -18f));
        AddTree(new Vector3(6f, 0f, 16f));
        AddTree(new Vector3(-26f, 0f, 26f));
        AddTree(new Vector3(30f, 0f, -30f));
        AddTree(new Vector3(-32f, 0f, -6f));
        AddTree(new Vector3(26f, 0f, 30f));
        AddBuilding(new Vector3(-20f, 0f, 14f), new Vector3(7f, 5f, 6f));
        AddBuilding(new Vector3(22f, 0f, 8f), new Vector3(6f, 7f, 6f));
        AddBuilding(new Vector3(14f, 0f, -26f), new Vector3(8f, 6f, 5f));
        AddBuilding(new Vector3(-14f, 0f, -26f), new Vector3(5f, 9f, 5f));
        ScatterNature();
        if (_photo)
        {
            var propPts = new System.Collections.Generic.List<(Vector2 P, float R)>();
            foreach (var c in _props) propPts.Add((c.P, c.R));
            PhotoEnv.AddGrassField(this, _buildingPts, propPts);
            PhotoEnv.AddBackdrop(this);
        }

        _player = BuildMech(_playerSpec, "Player", new Vector3(0f, Main.HeightAt(0f, 30f), 30f), 0f);
        _enemy = BuildMech(_enemySpec, "Enemy", new Vector3(0f, Main.HeightAt(0f, -16f), -16f), Mathf.Pi);

        // Breakable-parts registries (may split the merged Torso meshes);
        // refresh walker materials afterwards so hit flashes cover the
        // replacement pieces too.
        _partsPlayer = new MechParts(_player, _playerSpec.PartsId);
        _partsEnemy = new MechParts(_enemy, _enemySpec.PartsId);
        _player.RefreshMaterials();
        _enemy.RefreshMaterials();
        RefreshHitCaps();
        _prevPlayerRoot = _player.RootPos;
        _hit.PartResolver = (id, pt) => id == "PLAYER"
            ? _partsPlayer.AttributePoint(pt)
            : _partsEnemy.AttributePoint(pt);
        _hit.PartRayCast = (id, from, dir, maxDist) => id == "PLAYER"
            ? _partsPlayer.RaycastParts(from, dir, maxDist)
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
        if (!_test && !_weaponsTest && !_breakTest && !_terrainTest && !_photoTest && !_gaitShot)
        {
            // Pre-game MechLab (owner): the duel waits behind the lab
            // until DEPLOY. Test modes skip straight to the duel.
            _labOpen = true;
            Input.MouseMode = Input.MouseModeEnum.Visible;
            _panel.Visible = false;
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
            SetWPhase(0);
        }
        if (_breakTest)
        {
            BSetup(new Vector3(0f, Main.HeightAt(0f, 18f), 18f), new Vector3(0f, Main.HeightAt(0f, -12f), -12f));
            SetBPhase(0);
        }
    }

    private void ResetDuel()
    {
        _hpPlayer = _playerSpec.MaxHp;
        _hpEnemy = _enemySpec.MaxHp;
        _playerDead = _enemyDead = false;
        _partsPlayer.RestoreAll();
        _partsEnemy.RestoreAll();
        SyncLegConsequences(_player, _partsPlayer);
        SyncLegConsequences(_enemy, _partsEnemy);
        _denyLaserT = _denyAcT = _denyLrmT = _hudFlickerT = 0f;
        _lastDenied = "";
        _feed.Clear();
        _cdLaser = _cdAc = _cdLrm = _cdEnemy = 0f;
        _weaponSel = 0;
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
        _player.Teleport(new Vector3(0f, Main.HeightAt(0f, 30f), 30f), 0f);
        _enemy.Teleport(new Vector3(0f, Main.HeightAt(0f, -16f), -16f), Mathf.Pi);
        _player.AimPitchWide = float.NaN;
        _playerYaw = 0f;
        _enemyYaw = Mathf.Pi;
        _player.TorsoTwist = 0f;
        _player.TorsoPitch = 0f;
        _torsoWorldYaw = 0f;
        _enemy.TorsoTwist = 0f;
        _prevPlayerRoot = _player.RootPos;
        _playerVel = Vector3.Zero;
        _banner.Visible = false;
        _bannerSub.Visible = false;
        GD.Print("WPN RESET both mechs restored to spawn, HP full");
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
        for (int i = _lrmQueue.Count - 1; i >= 0; i--)
        {
            var q = _lrmQueue[i];
            q.Delay -= dt;
            if (q.Delay <= 0f) { _lrmQueue.RemoveAt(i); LaunchRocket(q.Aim, q.Left, q.Def); }
            else _lrmQueue[i] = q;
        }
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
    }

    private static void StepWalker(MechWalker w, double delta)
    {
        int n = Mathf.Max(1, Mathf.CeilToInt((float)delta / 0.09f));
        double h = delta / n;
        for (int i = 0; i < n; i++) w.StepFrame(h);
    }

    public override void _Process(double delta)
    {
        if (_atlasCam && !_atlasCamDone && _player != null && _player.IsReady)
        {
            _atlasCamDone = true;
            SetMechSelection("atlas", "timberwolf");
            ApplyLoadout();
            if (_lab != null) { _lab.QueueFree(); _lab = null; }
            _labOpen = false;
            if (_labLight != null) _labLight.Visible = false;
            _panel.Visible = true; _radar.Visible = true; _feedLabel.Visible = true;
            GD.Print("ATLASCAM swapped player to Atlas and deployed");
        }
        if (_atlasCam && _atlasCamDone && !_labOpen)
        {
            _atlasCamT += (float)delta;
            if (_atlasCamT > 2.5f && _camMode == "chase")
            {
                var back = -MechWalker.HeadingVec(_player.TorsoYawWorld);
                var camRel = _cam.GlobalPosition - _player.RootPos;
                GD.Print($"ATLASCAM legsYaw={_player.LegsYaw:F2} torsoWorld={_player.TorsoYawWorld:F2} engineTorso={_player.EngineTorsoYaw():F2} camBehind={camRel.Dot(back):F2} camSide={camRel.Dot(new Vector3(-back.Z, 0f, back.X)):F2}");
                QueueCapture("res://proof/atlas_player_chase.png", "atlasP", "chase", () => GetTree().Quit());
                _atlasCamT = -999f;
            }
        }

        if (_labShot && Time.GetTicksMsec() > 20000)
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
            // The duel waits behind the MechLab: a showroom view
            // (owner, MW4-style) — the mech stands front three-
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
                switch (_warmPose)
                {
                    case 0:
                        _cam.GlobalPosition = new Vector3(58f, 34f, 64f);
                        _cam.LookAt(new Vector3(-6f, 0f, -6f), Vector3.Up);
                        break;
                    case 1:
                        _cam.GlobalPosition = new Vector3(-21f, Main.HeightAt(-21f, 0f) + 2.1f, -1f);
                        _cam.LookAt(_player.RootPos + Vector3.Up * 5.5f, Vector3.Up);
                        break;
                    case 2:
                        _cam.GlobalPosition = new Vector3(-88f, 44f, 90f);
                        _cam.LookAt(new Vector3(12f, 0f, -12f), Vector3.Up);
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
            var center = _player.RootPos + Vector3.Up * 5.6f;
            var fwd = new Vector3(Mathf.Sin(_player.LegsYaw), 0f, Mathf.Cos(_player.LegsYaw));
            var viewDir = fwd.Rotated(Vector3.Up, 2.72f + sway); // mostly in front of the mech, slightly to its side
            _cam.GlobalPosition = center + viewDir * 14.5f + Vector3.Up * 3.4f;
            _cam.LookAt(center, Vector3.Up);
            _cam.RotateY(0.46f); // pan in camera space: mech right, centered under its slot panels (owner, adjusted twice at his word)
            _labLight.GlobalRotation = _cam.GlobalRotation;
            return;
        }

        if (!_frozen)
        {
            _simT += delta;
            if (_breakTest) UpdateBreakTest(dt);
            else if (_weaponsTest) UpdateWeaponsTest(dt);
            else if (_terrainTest) UpdateTerrainTest(dt);
            else if (_photoTest) UpdatePhotoTest(dt);
            else if (_test) UpdateTest(dt);
            else
            {
                if (_gaitShot) UpdateGaitShot(dt); // dev capture rides the live branch (AI walks the enemy)
                UpdatePlayerInput(dt);
                MaintainTorsoWorldYaw();
                UpdateEnemyAI(dt);
                _aimPoint = TorsoAimPoint();
                if (Input.MouseMode == Input.MouseModeEnum.Captured && !_playerDead)
                {
                    // One trigger: the left mouse button fires the SELECTED
                    // weapon (1/2/3 or wheel to switch — see the panel).
                    var aim = _aimPoint;
                    if (Input.IsMouseButtonPressed(MouseButton.Left))
                    {
                        if (_weaponSel == 0) FireLaser(aim);
                        else if (_weaponSel == 1) FireAutocannon(aim);
                        else FireLrmSalvo(aim);
                    }
                }
            }
            StepWalker(_player, delta);
            StepWalker(_enemy, delta);
            StepSounds();
            ResolveCollisions();
            StompCheck();
            if (dt > 0f)
            {
                var v = (_player.RootPos - _prevPlayerRoot) / dt;
                if (v.Length() > 8f) v = v.Normalized() * 8f; // teleports are not velocity
                _playerVel = _playerVel.Lerp(v, 0.5f);
                _prevPlayerRoot = _player.RootPos;
            }
            UpdateCombat(dt);
            UpdateDroop(dt);
            _partsPlayer.StepDebris(dt);
            _partsEnemy.StepDebris(dt);
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

