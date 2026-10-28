using Godot;
using System.Collections.Generic;

// MechDuel: playable two-mech duel on the round-1 terrain.
//   Player = Timber Wolf (MechWalker): W/S throttle along the legs heading,
//     A/D turn the legs, mouse twists/pitches the TORSO (clamped +-80 deg),
//     third-person chase camera aligned to the torso facing, crosshair.
//   Enemy  = Atlas (MechWalker + simple brain): advances when far, backs
//     off when close, orbits in the mid band; its torso tracks the player.
// Collisions are POC-grade kinematic circles resolved in code (no physics
// bodies): every tree/building registers one or two (x,z,radius) circles,
// each mech is a circle; after movement, overlaps are pushed out along the
// separation axis. Terrain collision stays with the gait's per-foot
// Main.HeightAt sampling.
// Verification: --duel-test runs a scripted choreography (Atlas stride +
// contact numbers, torso-twist frame + yaw log, tree-collision stall,
// mech-vs-mech stall) and captures proof frames.
public partial class MechDuel : Node3D
{
    private const float PlayerMaxSpeed = 4.2f;
    private const float AtlasMaxSpeed = 2.2f;
    private const float RunFactor = 1.5f; // SHIFT run mode: walk speed x1.5
    private const float TurnRate = 0.85f;
    private const float WorldBound = 80f; // heightfield world is 170 wide

    private struct Circle { public Vector2 P; public float R; }
    private readonly List<Circle> _props = new();

    private MechWalker _player;
    private MechWalker _enemy;
    private Camera3D _cam;
    private bool _test;
    private double _simT;

    // interactive state
    private float _playerYaw;
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
    private float _cdLaser, _cdAc, _cdLrm, _cdAtlas;
    private int _weaponSel; // selected weapon: 0 laser, 1 autocannon, 2 LRM
    private bool _running; // SHIFT held with W this frame (panel readout)
    private readonly System.Random _rng = new System.Random(20261027);
    private Vector3 _prevPlayerRoot;
    private Vector3 _playerVel;
    private readonly System.Collections.Generic.List<(float Delay, bool Left, Vector3 Aim, int Salvo)> _lrmQueue = new();
    private int _salvoSeq;
    // Per-salvo damage buckets for the knockdown rule: (sum, HP before).
    private readonly System.Collections.Generic.Dictionary<long, (float Sum, float HpBefore)> _salvoDmg = new();
    private readonly System.Collections.Generic.List<(MeshInstance3D Mi, float Life)> _puffs = new();
    private readonly System.Collections.Generic.Dictionary<Projectile, Vector3> _lastPuff = new();
    private readonly System.Collections.Generic.List<(Label3D L, float Life)> _dmgNums = new();
    // ---------- weapons test ----------
    private bool _weaponsTest;
    private bool _atlasMayFire;
    private int _wPhase;
    private double _wPhaseT;
    private int _wShotsFired;
    private bool _wCaptured, _wDeadShotQueued, _wDeathLogged;
    private double _wDeathT, _wStrafeT, _wLastShotT;
    private float _wStrafeDir = 1f;
    private int _wPrevShots;
    private float _wHpSnapshot;
    private int _atlasShots;
    private int _atlasDmgEvents, _twDmgEvents;
    private float _atlasDmgTotal, _twDmgTotal;

    // ---------- breakable test ----------
    private bool _breakTest;
    private int _bPhase, _bStep;
    private double _bPhaseT;
    private Vector3 _bVec1;
    private double _bT0;
    private float _bV1, _bDragMax;
    private Vector3 _bHomeLocal;
    private int _bCount1;
    private bool _bFlag1;
    private Vector3 _camFocus;

    // ---------- terrain test ----------
    private bool _terrainTest;
    private int _tPhase;
    private double _tPhaseT;
    private float _tSlopeYaw;
    private bool _tStrideLogged;
    private Vector3 _tRunFrom;

    private Crosshair _cross;
    private ProgressBar _barPlayer, _barEnemy, _cdBarLaser, _cdBarAc, _cdBarLrm;
    private Label _lblPlayerHp, _lblEnemyHp, _banner, _bannerSub;
    private Label _feedLabel, _lblWeaponStatus, _lblCdLaser, _lblCdAc, _lblCdLrm;
    private PanelContainer _panel;
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
        }
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
        AddChild(new WorldEnvironment
        {
            Environment = new Environment
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
            }
        });

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

        _player = new MechWalker { Name = "PlayerTimberwolf" };
        AddChild(_player);
        _player.Init(new MechWalker.Config
        {
            Label = "TW",
            ModelPath = "res://assets/timberwolf.fbx",
            TargetHeight = 13.0f,
            StepTime = 0.46f,
            StepThreshold = 1.7f,
            StepLift = 0.9f,
            Accel = 4.2f,
            CollisionRadius = 2.0f
        }, new Vector3(0f, Main.HeightAt(0f, 30f), 30f), 0f);

        _enemy = new MechWalker { Name = "EnemyAtlas" };
        AddChild(_enemy);
        _enemy.Init(new MechWalker.Config
        {
            Label = "ATLAS",
            ModelPath = "res://assets/atlas.fbx",
            TargetHeight = 14.0f,
            StepTime = 0.62f,
            StepThreshold = 1.6f,
            StepLift = 1.0f,
            Accel = 2.4f,
            CollisionRadius = 2.4f
        }, new Vector3(0f, Main.HeightAt(0f, -16f), -16f), Mathf.Pi);

        _hit.Mechs.Add(new HitModel.MechCap { Id = "TW", Root = () => _player.RootPos, Radius = _player.CollisionRadius, Height = 13f });
        _hit.Mechs.Add(new HitModel.MechCap { Id = "ATLAS", Root = () => _enemy.RootPos, Radius = _enemy.CollisionRadius, Height = 14f });
        _prevPlayerRoot = _player.RootPos;

        // Breakable-parts registries (may split the merged Torso meshes);
        // refresh walker materials afterwards so hit flashes cover the
        // replacement pieces too.
        _partsPlayer = new MechParts(_player, "TW");
        _partsEnemy = new MechParts(_enemy, "ATLAS");
        _player.RefreshMaterials();
        _enemy.RefreshMaterials();
        _hit.PartResolver = (id, pt) => id == "TW"
            ? _partsPlayer.AttributePoint(pt)
            : _partsEnemy.AttributePoint(pt);
        _hit.PartRayCast = (id, from, dir, maxDist) => id == "TW"
            ? _partsPlayer.RaycastParts(from, dir, maxDist)
            : _partsEnemy.RaycastParts(from, dir, maxDist);
        _partsPlayer.OnBreak = (id, c) => OnPartBroken(_partsPlayer, id, c);
        _partsEnemy.OnBreak = (id, c) => OnPartBroken(_partsEnemy, id, c);

        _cam = new Camera3D { Fov = 70f };
        AddChild(_cam);
        _cam.GlobalPosition = new Vector3(-14f, 9f, 44f);
        _cam.LookAt(new Vector3(0f, 6f, 18f), Vector3.Up);

        var layer = new CanvasLayer();
        AddChild(layer);
        _cross = new Crosshair();
        layer.AddChild(_cross);
        _cross.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        BuildHud(layer);

        _lastPlayerPos = _player.RootPos;
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

    // ---------- world ----------

    private void BuildTerrain()
    {
        // Shared heightfield mesh (vertices on grid nodes = HeightAt).
        AddChild(new MeshInstance3D { Mesh = Main.CreateTerrainMesh() });
    }

    private void AddTree(Vector3 pos) => AddTreeV(pos, 0, 1f, 0f);

    // Tree varieties: 0 classic (sphere canopy), 1 pine (stacked cones),
    // 2 broadleaf (wide squashed canopy). tint shifts the canopy hue
    // slightly. Collision circle + hit-model cylinder scale with the tree.
    private void AddTreeV(Vector3 pos, int variety, float scale, float tint)
    {
        pos.Y = Main.HeightAt(pos.X, pos.Z);
        var tree = new Node3D { Position = pos, Scale = Vector3.One * scale };
        var trunkMat = new StandardMaterial3D { AlbedoColor = new Color(0.4f, 0.26f, 0.13f) };
        float totalH;
        if (variety == 1)
        {
            var trunk = new MeshInstance3D();
            trunk.Mesh = new CylinderMesh { TopRadius = 0.2f, BottomRadius = 0.3f, Height = 3.0f, RadialSegments = 7 };
            trunk.MaterialOverride = trunkMat;
            trunk.Position = new Vector3(0f, 1.5f, 0f);
            tree.AddChild(trunk);
            var coneMat = new StandardMaterial3D { AlbedoColor = new Color(0.10f + tint * 0.05f, 0.34f + tint * 0.06f, 0.13f) };
            var cone1 = new MeshInstance3D();
            cone1.Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 1.7f, Height = 3.4f, RadialSegments = 8 };
            cone1.MaterialOverride = coneMat;
            cone1.Position = new Vector3(0f, 4.0f, 0f);
            var cone2 = new MeshInstance3D();
            cone2.Mesh = new CylinderMesh { TopRadius = 0.03f, BottomRadius = 1.15f, Height = 2.6f, RadialSegments = 8 };
            cone2.MaterialOverride = coneMat;
            cone2.Position = new Vector3(0f, 5.9f, 0f);
            tree.AddChild(cone1);
            tree.AddChild(cone2);
            totalH = 7.2f;
        }
        else if (variety == 2)
        {
            var trunk = new MeshInstance3D();
            trunk.Mesh = new CylinderMesh { TopRadius = 0.24f, BottomRadius = 0.34f, Height = 2.6f, RadialSegments = 7 };
            trunk.MaterialOverride = trunkMat;
            trunk.Position = new Vector3(0f, 1.3f, 0f);
            tree.AddChild(trunk);
            var canopy = new MeshInstance3D();
            canopy.Mesh = new SphereMesh { Radius = 2.3f, Height = 2.7f, RadialSegments = 11, Rings = 6 };
            canopy.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.17f + tint * 0.07f, 0.47f + tint * 0.05f, 0.15f) };
            canopy.Position = new Vector3(0f, 3.9f, 0f);
            tree.AddChild(canopy);
            totalH = 5.3f;
        }
        else
        {
            var trunk = new MeshInstance3D();
            trunk.Mesh = new CylinderMesh { TopRadius = 0.22f, BottomRadius = 0.3f, Height = 2.2f, RadialSegments = 7 };
            trunk.MaterialOverride = trunkMat;
            trunk.Position = new Vector3(0f, 1.1f, 0f);
            var canopy = new MeshInstance3D();
            canopy.Mesh = new SphereMesh { Radius = 1.7f, Height = 3.4f, RadialSegments = 10, Rings = 5 };
            canopy.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.13f + tint * 0.06f, 0.45f + tint * 0.05f, 0.16f) };
            canopy.Position = new Vector3(0f, 3.6f, 0f);
            tree.AddChild(trunk);
            tree.AddChild(canopy);
            totalH = 5.6f;
        }
        AddChild(tree);
        float r = 0.7f * scale;
        _props.Add(new Circle { P = new Vector2(pos.X, pos.Z), R = r });
        _hit.Props.Add(new HitModel.PropCyl { P = new Vector2(pos.X, pos.Z), R = r, BaseY = pos.Y, Height = totalH * scale });
    }

    // Low-poly rock: squashed, randomly tilted icosphere-ish blob. Gets a
    // collision circle + hit cylinder like every other prop.
    private void AddRock(Vector3 pos, Vector3 scl, float yaw, float tint)
    {
        float groundY = Main.HeightAt(pos.X, pos.Z);
        var mi = new MeshInstance3D();
        mi.Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 7, Rings = 3 };
        float g = 0.36f + tint * 0.14f;
        mi.MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = new Color(g, g * (0.99f + tint * 0.03f), g * (1.02f - tint * 0.02f)),
            Roughness = 0.97f
        };
        mi.Position = new Vector3(pos.X, groundY - 0.30f * scl.Y, pos.Z);
        mi.Scale = scl;
        mi.Rotation = new Vector3(tint * 0.22f - 0.11f, yaw, tint * 0.15f - 0.07f);
        AddChild(mi);
        float r = Mathf.Max(scl.X, scl.Z) * 0.92f;
        _props.Add(new Circle { P = new Vector2(pos.X, pos.Z), R = r });
        _hit.Props.Add(new HitModel.PropCyl
        {
            P = new Vector2(pos.X, pos.Z), R = r,
            BaseY = groundY - 0.35f * scl.Y, Height = 1.05f * scl.Y
        });
    }

    // Seeded nature scatter: ~33 more trees (40 total) and 25 rocks.
    // Placement respects the test choreography: duel/weapons/breakable
    // lanes and every spawn/teleport pad stay clear, props keep spacing,
    // trees stay off steep/high ground.
    private bool ScatterFree(Vector2 p, float clearR)
    {
        // lanes (x0, x1, z0, z1)
        if (p.X > -5.5f && p.X < 5.5f && p.Y > -23f && p.Y < 37f) return false;
        if (p.X > 16f && p.X < 28f && p.Y > -23f && p.Y < 31f) return false;
        if (p.X > 2f && p.X < 10.5f && p.Y > 9f && p.Y < 33f) return false;
        var pads = new (float X, float Z, float R)[]
        {
            (0f, 30f, 7f), (0f, -16f, 7f), (0f, 18f, 7f), (0f, -12f, 7f),
            (0f, 20f, 7f), (0f, -10f, 7f), (22f, 24f, 7f), (22f, -16f, 7f),
            (-38f, 38f, 7.5f), (0f, 24f, 7f), (20f, 2f, 6f), (6f, 27f, 5.5f),
            (0f, 10f, 6f), (0f, -2f, 6f)
        };
        foreach (var pad in pads)
            if (p.DistanceTo(new Vector2(pad.X, pad.Z)) < pad.R + clearR) return false;
        foreach (var c in _props)
            if (p.DistanceTo(c.P) < c.R + clearR + 2.0f) return false;
        return true;
    }

    private static float TerrainGrade(float x, float z)
    {
        const float e = 0.7f;
        float gx = (Main.HeightAt(x + e, z) - Main.HeightAt(x - e, z)) / (2f * e);
        float gz = (Main.HeightAt(x, z + e) - Main.HeightAt(x, z - e)) / (2f * e);
        return Mathf.Sqrt(gx * gx + gz * gz);
    }

    private void ScatterNature()
    {
        var rng = new System.Random(0x5EED);
        int trees = 0, tries = 0;
        while (trees < 33 && tries++ < 900)
        {
            float ang = (float)rng.NextDouble() * Mathf.Tau;
            float rad = 12f + 68f * Mathf.Sqrt((float)rng.NextDouble());
            var p = new Vector2(Mathf.Cos(ang) * rad, Mathf.Sin(ang) * rad);
            float sc = 0.85f + (float)rng.NextDouble() * 0.75f;
            if (!ScatterFree(p, 0.7f * sc + 0.6f)) continue;
            if (TerrainGrade(p.X, p.Y) > 0.5f) continue;
            if (Main.HeightAt(p.X, p.Y) > 12.5f) continue;
            AddTreeV(new Vector3(p.X, 0f, p.Y), rng.Next(3), sc, (float)rng.NextDouble());
            trees++;
        }
        int rocks = 0;
        tries = 0;
        while (rocks < 25 && tries++ < 900)
        {
            float ang = (float)rng.NextDouble() * Mathf.Tau;
            float rad = 10f + 70f * Mathf.Sqrt((float)rng.NextDouble());
            var p = new Vector2(Mathf.Cos(ang) * rad, Mathf.Sin(ang) * rad);
            var scl = new Vector3(
                1.2f + (float)rng.NextDouble() * 2.0f,
                0.8f + (float)rng.NextDouble() * 1.3f,
                1.2f + (float)rng.NextDouble() * 2.0f);
            scl.Y *= 0.62f; // squash
            if (!ScatterFree(p, Mathf.Max(scl.X, scl.Z))) continue;
            if (Main.HeightAt(p.X, p.Y) > 21f) continue;
            AddRock(new Vector3(p.X, 0f, p.Y), scl, (float)rng.NextDouble() * Mathf.Tau, (float)rng.NextDouble());
            rocks++;
        }
        GD.Print($"DUEL scatter trees+={trees} rocks={rocks} propsNow={_props.Count}");
    }

    private void AddBuilding(Vector3 pos, Vector3 size)
    {
        float groundY = Main.HeightAt(pos.X, pos.Z);
        pos.Y = groundY + size.Y / 2f - 0.3f;
        var mi = new MeshInstance3D();
        mi.Mesh = new BoxMesh { Size = size };
        mi.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.55f, 0.58f) };
        mi.Position = pos;
        AddChild(mi);
        // Circle colliders: one for near-square footprints, two along the
        // long axis for elongated ones. Radius covers the short side plus a
        // margin; extreme corners can poke slightly past a circle (POC).
        // The same circles register as hit-model cylinders (base sunk 0.3
        // with the mesh, full height) so buildings block shots too.
        float sx = size.X, sz = size.Z;
        if (Mathf.Max(sx, sz) / Mathf.Min(sx, sz) > 1.45f)
        {
            float off = (Mathf.Max(sx, sz) - Mathf.Min(sx, sz)) / 4f;
            float r = Mathf.Min(sx, sz) / 2f + 0.35f;
            var a = new Vector2(pos.X, pos.Z);
            var b = new Vector2(pos.X, pos.Z);
            if (sx > sz) { a.X -= off; b.X += off; } else { a.Y -= off; b.Y += off; }
            _props.Add(new Circle { P = a, R = r });
            _props.Add(new Circle { P = b, R = r });
            _hit.Props.Add(new HitModel.PropCyl { P = a, R = r, BaseY = groundY - 0.3f, Height = size.Y });
            _hit.Props.Add(new HitModel.PropCyl { P = b, R = r, BaseY = groundY - 0.3f, Height = size.Y });
        }
        else
        {
            float r = Mathf.Max(sx, sz) / 2f + 0.3f;
            _props.Add(new Circle { P = new Vector2(pos.X, pos.Z), R = r });
            _hit.Props.Add(new HitModel.PropCyl { P = new Vector2(pos.X, pos.Z), R = r, BaseY = groundY - 0.3f, Height = size.Y });
        }
    }

    // ---------- collisions ----------

    private static Vector2 PushOut(Vector2 p, float r, Circle c, out float sep)
    {
        var d = p - c.P;
        float dist = d.Length();
        float min = r + c.R;
        sep = dist;
        if (dist >= min) return p;
        if (dist < 1e-4f) d = new Vector2(1f, 0f);
        else d /= dist;
        return c.P + d * min;
    }

    private void ResolveCollisions()
    {
        ResolveOne(_player);
        ResolveOne(_enemy);
        // mech vs mech: split the overlap
        var a = new Vector2(_player.RootPos.X, _player.RootPos.Z);
        var b = new Vector2(_enemy.RootPos.X, _enemy.RootPos.Z);
        var d = b - a;
        float dist = d.Length();
        float min = _player.CollisionRadius + _enemy.CollisionRadius;
        if (dist < min)
        {
            if (dist < 1e-4f) d = new Vector2(0f, 1f);
            else d /= dist;
            float push = (min - dist) / 2f;
            a -= d * push;
            b += d * push;
            _player.ApplyRootXZ(a);
            _enemy.ApplyRootXZ(b);
        }
    }

    private void ResolveOne(MechWalker w)
    {
        var p = new Vector2(w.RootPos.X, w.RootPos.Z);
        for (int iter = 0; iter < 2; iter++)
        {
            foreach (var c in _props)
            {
                p = PushOut(p, w.CollisionRadius, c, out _);
            }
        }
        float lim = WorldBound - w.CollisionRadius;
        p.X = Mathf.Clamp(p.X, -lim, lim);
        p.Y = Mathf.Clamp(p.Y, -lim, lim);
        w.ApplyRootXZ(p);
    }

    // ---------- input ----------

    public override void _Input(InputEvent e)
    {
        if (e is InputEventKey rk && rk.Pressed && !rk.Echo && rk.Keycode == Key.R)
        {
            ResetDuel();
            return;
        }
        if (!_test && !_weaponsTest && !_breakTest && !_terrainTest)
        {
            // Weapon selection: keys 1/2/3, mouse wheel cycles.
            if (e is InputEventKey sk && sk.Pressed && !sk.Echo)
            {
                if (sk.Keycode == Key.Key1) _weaponSel = 0;
                else if (sk.Keycode == Key.Key2) _weaponSel = 1;
                else if (sk.Keycode == Key.Key3) _weaponSel = 2;
            }
            else if (e is InputEventMouseButton wb && wb.Pressed &&
                     (wb.ButtonIndex == MouseButton.WheelUp || wb.ButtonIndex == MouseButton.WheelDown))
            {
                int d = wb.ButtonIndex == MouseButton.WheelDown ? 1 : -1;
                _weaponSel = (_weaponSel + d + 3) % 3;
            }
        }
        if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            if (Input.MouseMode != Input.MouseModeEnum.Captured)
                Input.MouseMode = Input.MouseModeEnum.Captured;
        }
        else if (e is InputEventKey k && k.Pressed && k.Keycode == Key.Escape)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
        else if (e is InputEventMouseMotion mm && Input.MouseMode == Input.MouseModeEnum.Captured && !_test && !_player.IsKnockedDown)
        {
            _player.TorsoTwist -= mm.Relative.X * 0.0030f;
            _player.TorsoPitch -= mm.Relative.Y * 0.0025f;
        }
    }

    private void UpdatePlayerInput(float delta)
    {
        // A lost leg immobilizes the mech: no throttle, no leg turning.
        bool legsOk = _partsPlayer.Alive("leg_L") && _partsPlayer.Alive("leg_R");
        if (legsOk)
        {
            if (Input.IsPhysicalKeyPressed(Key.A)) _playerYaw += TurnRate * delta;
            if (Input.IsPhysicalKeyPressed(Key.D)) _playerYaw -= TurnRate * delta;
        }
        // SHIFT + W = run mode (1.5x walk speed); reverse stays at walk pace.
        _running = legsOk && Input.IsPhysicalKeyPressed(Key.Shift) && Input.IsPhysicalKeyPressed(Key.W);
        float spd = 0f;
        if (legsOk && Input.IsPhysicalKeyPressed(Key.W)) spd = PlayerMaxSpeed * (_running ? RunFactor : 1f);
        else if (legsOk && Input.IsPhysicalKeyPressed(Key.S)) spd = -PlayerMaxSpeed * 0.55f;
        _player.SetDrive(_playerYaw, spd);
    }

    private void UpdateEnemyAI(float delta)
    {
        var toP = _player.RootPos - _enemy.RootPos;
        float dist = new Vector2(toP.X, toP.Z).Length();
        float yawTo = MechWalker.YawOf(toP.Normalized());
        if (_enemy.IsKnockedDown) { _enemy.SetDrive(_enemyYaw, 0f); return; } // down: no drive, no tracking
        if (!_partsEnemy.Alive("leg_L") || !_partsEnemy.Alive("leg_R"))
        {
            // Immobile: legs frozen, but the torso still tracks and fires.
            _enemy.SetDrive(_enemyYaw, 0f);
            TrackAtlasTorso(delta, yawTo);
            _enemy.TorsoPitch = 0f;
            return;
        }
        float desiredYaw, spd;
        if (dist > 38f) { desiredYaw = yawTo; spd = AtlasMaxSpeed; }
        else if (dist < 26f) { desiredYaw = yawTo; spd = -AtlasMaxSpeed * 0.5f; }
        else
        {
            _orbitTimer += delta;
            if (_orbitTimer > 6f) { _orbitTimer = 0f; _orbitDir = -_orbitDir; }
            desiredYaw = yawTo + _orbitDir * Mathf.Pi / 2f;
            spd = AtlasMaxSpeed * 0.7f;
        }
        float dy = MechWalker.WrapAngle(desiredYaw - _enemyYaw);
        float maxTurn = 0.7f * delta;
        _enemyYaw += Mathf.Clamp(dy, -maxTurn, maxTurn);
        _enemy.SetDrive(_enemyYaw, spd);
        TrackAtlasTorso(delta, yawTo);
        _enemy.TorsoPitch = 0f;
    }

    // Atlas torso tracking: snaps to the target bearing while the head
    // (sensor suite) lives; with the head destroyed the twist closes at
    // 0.6 rad/s -- a visible, measurable lag.
    private void TrackAtlasTorso(float dt, float yawTo)
    {
        float desiredTwist = MechWalker.WrapAngle(yawTo - _enemyYaw);
        if (_partsEnemy.Alive("head"))
        {
            _enemy.TorsoTwist = desiredTwist;
        }
        else
        {
            float cur = _enemy.TorsoTwist;
            float step = Mathf.Clamp(MechWalker.WrapAngle(desiredTwist - cur),
                -0.6f * dt, 0.6f * dt);
            _enemy.TorsoTwist = cur + step;
        }
    }

    // ---------- weapons ----------

    // Crosshair aim: raycast the shared hit model from the camera; player
    // weapons converge on whatever it lands on (max 200u), not on the
    // camera axis at infinity.
    private Vector3 AimPointFromCamera()
    {
        var from = _cam.GlobalPosition;
        var dir = -_cam.GlobalTransform.Basis.Z.Normalized();
        return _hit.Ray(from, dir, 200f, "TW").Point;
    }

    // Muzzle hardpoints: the Timber Wolf's forearm cannons sit on the
    // elbow bones (the Hand_* parts are bound there), pushed forward along
    // the torso facing so the shot clears the arm. Laser = left, AC = right.
    private Vector3 PlayerMuzzle(bool left)
    {
        var bone = _player.BoneWorldPos(left ? "elbow_L" : "elbow_R");
        return bone + MechWalker.HeadingVec(_player.TorsoYawWorld) * 2.8f;
    }

    // Atlas AC/20: right-torso gun — torso bone origin plus a right/up/
    // forward offset in the torso frame.
    private Vector3 AtlasMuzzle()
    {
        var torso = _enemy.BoneWorldPos("torso");
        var fwd = MechWalker.HeadingVec(_enemy.TorsoYawWorld);
        var right = new Vector3(-fwd.Z, 0f, fwd.X);
        return torso + right * 1.7f + fwd * 1.4f + Vector3.Up * 2.6f;
    }

    private void SpawnBeam(Vector3 a, Vector3 b, Color col, float width, float life)
    {
        _beamsSpawned++;
        var beam = WeaponFx.MakeBeam(a, b, col, width);
        AddChild(beam);
        _fx.Add((beam, life, 0));
        var glow = WeaponFx.MakeBeam(a, b, new Color(col.R, col.G, col.B, 0.32f), width * 3.2f);
        AddChild(glow);
        _fx.Add((glow, life, 0));
    }

    private void SpawnFlash(Vector3 point, Color col, float radius, float life)
    {
        var f = WeaponFx.MakeFlash(point, col, radius);
        AddChild(f);
        _fx.Add((f, life, 0));
    }

    // A fire attempt on a weapon whose mounting part is destroyed:
    // logged and flashed on its cooldown bar (HUD step), no cooldown spent.
    private void DenyWeapon(string weapon)
    {
        _lastDenied = weapon;
        GD.Print($"WPN DENIED {weapon} (mounting part destroyed)");
        if (weapon == "laser") _denyLaserT = 0.5f;
        else if (weapon == "ac") _denyAcT = 0.5f;
        else if (weapon == "lrm") _denyLrmT = 0.5f;
    }

    private void FireLaser(Vector3 aimPoint)
    {
        if (_playerDead) return;
        if (!_partsPlayer.Alive("arm_L")) { DenyWeapon("laser"); return; }
        if (_cdLaser > 0f) return;
        _cdLaser = 0.45f;
        var muzzle = PlayerMuzzle(true);
        var toAim = aimPoint - muzzle;
        var dir = toAim.Length() > 0.5f ? toAim.Normalized() : MechWalker.HeadingVec(_player.TorsoYawWorld);
        var hit = _hit.Ray(muzzle, dir, 140f, "TW");
        SpawnBeam(muzzle, hit.Point, new Color(1f, 0.22f, 0.12f), 0.085f, 0.12f);
        SpawnFlash(muzzle, new Color(1f, 0.4f, 0.2f), 0.3f, 0.07f);
        if (hit.Hit)
        {
            SpawnFlash(hit.Point, new Color(1f, 0.45f, 0.2f), 0.38f, 0.12f);
            GD.Print($"WPN LASER hit={hit.Target} t={hit.T:F1} zone={hit.Zone} part={hit.Part} point={hit.Point}");
            if (hit.Target == "ATLAS") ApplyDamage(_enemy, 12f, hit, "TW");
        }
        else GD.Print("WPN LASER hit=none");
    }

    private void FireAutocannon(Vector3 aimPoint)
    {
        if (_playerDead) return;
        if (!_partsPlayer.Alive("arm_R")) { DenyWeapon("ac"); return; }
        if (_cdAc > 0f) return;
        _cdAc = 0.85f;
        var muzzle = PlayerMuzzle(false);
        var toAim = aimPoint - muzzle;
        var dir = toAim.Length() > 0.5f ? toAim.Normalized() : MechWalker.HeadingVec(_player.TorsoYawWorld);
        SpawnProjectile(muzzle, dir * 60f, 5f, 26f, 0f, "TW", rocket: false);
        SpawnFlash(muzzle, new Color(1f, 0.6f, 0.2f), 0.42f, 0.08f);
        GD.Print($"WPN AC fired dir={dir}");
    }

    // LRM salvo: 8 rockets staggered 0.07s apart, alternating ear pods.
    // Each rocket is dumb ballistics: velocity solved to land on the aim
    // point (up-compensated for gravity), seeded spread, 7 damage with
    // 3.0u splash falloff on impact. A lost pod removes its 4 rockets;
    // both pods gone = the LRM is offline.
    private void FireLrmSalvo(Vector3 aimPoint)
    {
        if (_playerDead) return;
        bool podL = _partsPlayer.Alive("pod_L"), podR = _partsPlayer.Alive("pod_R");
        if (!podL && !podR) { DenyWeapon("lrm"); return; }
        if (_cdLrm > 0f) return;
        _cdLrm = 3.5f;
        var sides = new System.Collections.Generic.List<bool>();
        if (podL && podR) { for (int i = 0; i < 8; i++) sides.Add(i % 2 == 0); }
        else if (podL) { for (int i = 0; i < 4; i++) sides.Add(true); }
        else { for (int i = 0; i < 4; i++) sides.Add(false); }
        int salvo = ++_salvoSeq;
        for (int i = 0; i < sides.Count; i++)
            _lrmQueue.Add((i * 0.07f, sides[i], aimPoint, salvo));
        GD.Print($"WPN LRM salvo queued ({sides.Count} rockets, pods L={podL} R={podR})");
    }

    private Vector3 PodMuzzle(bool left)
    {
        var torso = _player.BoneWorldPos("torso");
        var fwd = MechWalker.HeadingVec(_player.TorsoYawWorld);
        var right = new Vector3(-fwd.Z, 0f, fwd.X);
        return torso + right * (left ? -2.3f : 2.3f) + Vector3.Up * 3.6f + fwd * 0.4f;
    }

    private void LaunchRocket(Vector3 aimPoint, bool left, int salvoId = 0)
    {
        _rocketsLaunched++;
        var muzzle = PodMuzzle(left);
        var toAim = aimPoint - muzzle;
        float dist = Mathf.Max(toAim.Length(), 1f);
        var dir = ApplyInaccuracy(toAim.Normalized(), 1.1f);
        const float speed = 36f, grav = 11f;
        float tof = dist / speed;
        var vel = dir * speed + Vector3.Up * (0.5f * grav * tof);
        SpawnProjectile(muzzle, vel, grav, 7f, 3.0f, "TW", rocket: true, salvoId: salvoId);
        SpawnFlash(muzzle, new Color(1f, 0.55f, 0.2f), 0.3f, 0.09f);
    }

    private void OnRocketStep(Projectile p)
    {
        if (_lastPuff.TryGetValue(p, out var last) && last.DistanceSquaredTo(p.GlobalPosition) < 0.3f) return;
        _lastPuff[p] = p.GlobalPosition;
        var puff = WeaponFx.MakeTrailPuff(p.GlobalPosition);
        AddChild(puff);
        _puffs.Add((puff, 0.4f));
    }

    private void ExplodeSplash(HitModel.Result hit, Projectile p)
    {
        MechWalker direct = hit.Target == "TW" ? _player : hit.Target == "ATLAS" ? _enemy : null;
        if (direct != null) ApplyDamage(direct, p.Damage, hit, p.OwnerId, p.SalvoId);
        foreach (var (w, id) in new[] { (_player, "TW"), (_enemy, "ATLAS") })
        {
            if (w == direct) continue;
            float d = _hit.DistanceToMechSurface(hit.Point, id);
            if (d < p.SplashRadius)
            {
                var splashHit = hit;
                splashHit.Target = id;
                splashHit.Zone = "splash";
                ApplyDamage(w, p.Damage * (1f - d / p.SplashRadius), splashHit, p.OwnerId, p.SalvoId);
            }
        }
    }

    private Projectile SpawnProjectile(Vector3 pos, Vector3 vel, float gravity, float dmg,
        float splash, string owner, bool rocket, int salvoId = 0)
    {
        var p = new Projectile
        {
            Vel = vel,
            Gravity = gravity,
            Damage = dmg,
            SplashRadius = splash,
            OwnerId = owner,
            SalvoId = salvoId
        };
        p.AddChild(rocket ? WeaponFx.MakeRocket() : (Node3D)WeaponFx.MakeTracer());
        AddChild(p);
        p.GlobalPosition = pos;
        if (vel.LengthSquared() > 0.01f) p.LookAt(pos + vel, Vector3.Up);
        p.OnHit = OnProjectileHit;
        if (rocket) p.OnStep = OnRocketStep;
        _shots.Add(p);
        return p;
    }

    private void OnProjectileHit(HitModel.Result hit, Projectile p)
    {
        SpawnFlash(hit.Point, new Color(1f, 0.6f, 0.2f), p.SplashRadius > 0f ? 0.8f : 0.45f, 0.14f);
        GD.Print($"WPN PROJ owner={p.OwnerId} hit={hit.Target} zone={hit.Zone} part={hit.Part} t={hit.T:F1} point={hit.Point}");
        if (p.SplashRadius > 0f)
        {
            ExplodeSplash(hit, p);
            return;
        }
        if (hit.Target == "TW") ApplyDamage(_player, p.Damage, hit, p.OwnerId, p.SalvoId);
        else if (hit.Target == "ATLAS") ApplyDamage(_enemy, p.Damage, hit, p.OwnerId, p.SalvoId);
    }

    private void ApplyDamage(MechWalker target, float dmg, HitModel.Result hit, string source, int salvoId = 0)
    {
        bool isPlayer = target == _player;
        if (isPlayer ? _playerDead : _enemyDead) return;
        float hpBefore = isPlayer ? _hpPlayer : _hpEnemy;
        var parts = isPlayer ? _partsPlayer : _partsEnemy;
        // Local damage routing: torso hits burn the main pool; head hits
        // burn the head part AND the pool; every other part absorbs its
        // own hits. Counters still record the nominal damage once per
        // hit event. Attribution: a box-ray hit already carries its part
        // (a box surface point is unambiguous, while an interior SDF
        // resolve can name a neighbor whose AABB overlaps the spot);
        // capsule points, splash and crafted hits resolve by nearest
        // part volume at the point.
        string partId = !string.IsNullOrEmpty(hit.Part)
            ? hit.Part
            : parts.AttributePoint(hit.Point);
        if (isPlayer) { _twDmgEvents++; _twDmgTotal += dmg; }
        else { _atlasDmgEvents++; _atlasDmgTotal += dmg; }
        float poolDmg = 0f;
        if (partId == "torso") poolDmg = dmg;
        else if (partId == "head") { poolDmg = dmg; parts.DamagePart("head", dmg); }
        else parts.DamagePart(partId, dmg);
        if (poolDmg > 0f)
        {
            if (isPlayer) _hpPlayer = Mathf.Max(_hpPlayer - poolDmg, 0f);
            else _hpEnemy = Mathf.Max(_hpEnemy - poolDmg, 0f);
        }
        float hp = isPlayer ? _hpPlayer : _hpEnemy;
        GD.Print($"WPN DMG target={(isPlayer ? "TW" : "ATLAS")} dmg={dmg:F1} zone={hit.Zone} part={partId} poolDmg={poolDmg:F1} hp={hp:F1} src={source}");
        OnDamaged(target, dmg, hit.Point, source);
        if (hp <= 0f) { Die(target); return; }
        // Knockdown rule (owner): a single hit — or one full LRM salvo,
        // summed across its rockets — whose firepower exceeds 1/3 of the
        // target's remaining HP topples the mech. (Against MAX HP the
        // rule could never fire: the biggest single hit in the game is
        // the 26-damage autocannon vs an 87-point threshold.)
        if (salvoId > 0)
        {
            long key = ((long)salvoId << 1) | (isPlayer ? 1L : 0L);
            if (!_salvoDmg.TryGetValue(key, out var acc)) acc = (0f, hpBefore);
            acc.Sum += dmg;
            _salvoDmg[key] = acc;
            if (acc.Sum > acc.HpBefore / 3f)
            {
                _salvoDmg.Remove(key);
                DoKnockdown(target, $"salvo firepower {acc.Sum:F0} > remaining HP {acc.HpBefore:F0} / 3");
            }
        }
        else if (dmg > hpBefore / 3f)
        {
            DoKnockdown(target, $"hit firepower {dmg:F0} > remaining HP {hpBefore:F0} / 3");
        }
    }

    // Topple a mech away from its attacker. The walker animates the fall
    // (0.5s down, 1.6s on the ground, 1.0s rise, then 3s immunity); while
    // down it cannot drive, step, or fire.
    private void DoKnockdown(MechWalker target, string cause)
    {
        var other = target == _player ? _enemy : _player;
        var away = target.RootPos - other.RootPos;
        away.Y = 0f;
        if (away.LengthSquared() < 0.01f) away = -MechWalker.HeadingVec(target.LegsYaw);
        if (!target.TryKnockDown(away.Normalized())) return;
        AddFeed(target == _player ? "TIMBER WOLF KNOCKED DOWN" : "ATLAS KNOCKED DOWN");
        GD.Print($"KNOCK {(target == _player ? "TW" : "ATLAS")} down: {cause}");
        if (target == _player) _shake = Mathf.Max(_shake, 0.7f);
    }

    // A part just broke off (fired from MechParts.DamagePart): spark burst
    // at the separation point, HUD feed message, then the functional
    // consequences.
    private void OnPartBroken(MechParts parts, string partId, Vector3 center)
    {
        SpawnFlash(center, new Color(1f, 0.62f, 0.22f), 0.95f, 0.16f);
        SpawnFlash(center, new Color(1f, 0.88f, 0.55f), 0.45f, 0.1f);
        var part = parts.GetPart(partId);
        if (parts == _partsPlayer) AddFeed($"WARNING: {part.Display} DESTROYED");
        else AddFeed($"ATLAS: {part.Display} DESTROYED");
        ApplyPartConsequences(parts, partId);
    }

    private void AddFeed(string text)
    {
        _feed.Add((text, 6f));
        while (_feed.Count > 5) _feed.RemoveAt(0);
        GD.Print($"BRK FEED {text}");
    }

    // Functional fallout of losing a part. Weapon mounts are gated live at
    // the fire/gunnery checks (arm_L -> laser, arm_R -> AC and the Atlas's
    // AC/20, pods -> salvo size, Atlas head -> spread + tracking lag), so
    // the stateful consequences latched here are the legs and the player's
    // head (HUD disturbance timer; the sway itself is applied in the HUD
    // step from part state).
    private void ApplyPartConsequences(MechParts parts, string partId)
    {
        if (partId == "leg_L" || partId == "leg_R")
            SyncLegConsequences(parts == _partsPlayer ? _player : _enemy, parts);
        else if (partId == "head" && parts == _partsPlayer)
            _hudFlickerT = 1.2f;
        if (parts == _partsPlayer) AutoSwitchWeapon();
    }

    private bool WeaponAlive(int sel) => sel switch
    {
        0 => _partsPlayer.Alive("arm_L"),
        1 => _partsPlayer.Alive("arm_R"),
        _ => _partsPlayer.Alive("pod_L") || _partsPlayer.Alive("pod_R"),
    };

    // When the selected weapon is destroyed, fall to the next live one.
    private void AutoSwitchWeapon()
    {
        if (_partsPlayer == null || WeaponAlive(_weaponSel)) return;
        for (int i = 1; i <= 3; i++)
        {
            int cand = (_weaponSel + i) % 3;
            if (WeaponAlive(cand)) { _weaponSel = cand; return; }
        }
    }

    // Recompute a walker's leg state from its parts. Owner's rule: ANY
    // lost leg means the mech cannot walk — drive speed is zeroed and it
    // fights on as a stationary turret (torso twist and weapons still
    // work), leaning slightly onto the surviving leg. Both legs gone =
    // disabled, which counts as destroyed.
    private void SyncLegConsequences(MechWalker w, MechParts parts)
    {
        bool lDead = !parts.Alive("leg_L");
        bool rDead = !parts.Alive("leg_R");
        w.SetLegDisabled(true, lDead);
        w.SetLegDisabled(false, rDead);
        if (lDead && rDead)
        {
            w.SpeedFactor = 1f;
            w.LimpRoll = 0f;
            GD.Print($"BRK {(w == _player ? "TW" : "ATLAS")} both legs destroyed -> DISABLED (counts as destroyed)");
            if (w == _player ? !_playerDead : !_enemyDead) Die(w);
            return;
        }
        w.SpeedFactor = (lDead || rDead) ? 0f : 1f;
        w.LimpRoll = lDead ? 0.10f : rDead ? -0.10f : 0f;
        if (lDead || rDead)
            GD.Print($"BRK {(w == _player ? "TW" : "ATLAS")} leg lost -> IMMOBILE (speedFactor 0, turret mode)");
    }

    private void Die(MechWalker w)
    {
        w.Halted = true;
        w.SetDrive(w.LegsYaw, 0f);
        if (w == _player) { _playerDead = true; GD.Print("WPN DEATH TW"); }
        else { _enemyDead = true; GD.Print("WPN DEATH ATLAS"); }
        OnDeath(w);
    }

    // ---------- damage feedback / death / HUD ----------

    private void OnDamaged(MechWalker target, float dmg, Vector3 point, string source)
    {
        target.FlashPulse();
        bool onEnemy = target == _enemy;
        SpawnDmgNumber(point, dmg, onEnemy ? new Color(1f, 0.78f, 0.3f) : new Color(1f, 0.38f, 0.3f));
        if (onEnemy && source == "TW") _cross.Hitmark = 1f;
        if (!onEnemy) _shake = Mathf.Min(_shake + 0.22f, 0.5f);
    }

    private void OnDeath(MechWalker w)
    {
        w.SetDarkened(true);
        if (w == _enemy)
        {
            _banner.Text = "ATLAS DESTROYED";
            _banner.Modulate = new Color(1f, 0.85f, 0.35f);
            _bannerSub.Visible = true;
        }
        else
        {
            _banner.Text = "MECH DESTROYED — press R to reset";
            _banner.Modulate = new Color(1f, 0.35f, 0.28f);
            _bannerSub.Visible = false;
        }
        _banner.Visible = true;
    }

    private void SpawnDmgNumber(Vector3 point, float dmg, Color col)
    {
        var l = new Label3D
        {
            Text = $"{dmg:F0}",
            FontSize = 128,
            PixelSize = 0.016f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Modulate = col,
            OutlineSize = 16,
            OutlineModulate = new Color(0f, 0f, 0f, 0.9f),
            NoDepthTest = true
        };
        AddChild(l);
        l.GlobalPosition = point + Vector3.Up * 0.7f
            + new Vector3((float)_rng.NextDouble() - 0.5f, 0f, (float)_rng.NextDouble() - 0.5f) * 0.9f;
        _dmgNums.Add((l, 0.9f));
    }

    private void ResetDuel()
    {
        _hpPlayer = 260f;
        _hpEnemy = 340f;
        _playerDead = _enemyDead = false;
        _partsPlayer.RestoreAll();
        _partsEnemy.RestoreAll();
        SyncLegConsequences(_player, _partsPlayer);
        SyncLegConsequences(_enemy, _partsEnemy);
        _denyLaserT = _denyAcT = _denyLrmT = _hudFlickerT = 0f;
        _lastDenied = "";
        _feed.Clear();
        _cdLaser = _cdAc = _cdLrm = _cdAtlas = 0f;
        _weaponSel = 0;
        _lrmQueue.Clear();
        _salvoDmg.Clear();
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
        _playerYaw = 0f;
        _enemyYaw = Mathf.Pi;
        _player.TorsoTwist = 0f;
        _player.TorsoPitch = 0f;
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

    private static Label MakeLabel(int fontSize, Color col)
    {
        var l = new Label { Text = "" };
        l.AddThemeFontSizeOverride("font_size", fontSize);
        l.AddThemeColorOverride("font_color", col);
        return l;
    }

    private void BuildHud(CanvasLayer layer)
    {
        // ---- Cockpit control panel: one console strip along the bottom
        // holding mech integrity, per-part status, weapon charge with the
        // selected weapon marked, and target/system readouts.
        _panel = new PanelContainer();
        _panel.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        _panel.OffsetLeft = 16; _panel.OffsetRight = -16;
        _panel.OffsetTop = -172; _panel.OffsetBottom = -14;
        var pbg = new StyleBoxFlat
        {
            BgColor = new Color(0.055f, 0.075f, 0.105f, 0.94f),
            BorderColor = new Color(0.5f, 0.56f, 0.62f, 0.9f),
            ContentMarginLeft = 16, ContentMarginRight = 16,
            ContentMarginTop = 10, ContentMarginBottom = 10,
        };
        pbg.SetBorderWidthAll(2);
        pbg.SetCornerRadiusAll(8);
        _panel.AddThemeStyleboxOverride("panel", pbg);
        layer.AddChild(_panel);

        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 20);
        _panel.AddChild(hbox);

        Label Title(string t)
        {
            var l = MakeLabel(12, new Color(0.62f, 0.7f, 0.78f));
            l.Text = t;
            return l;
        }

        // -- Left: own mech integrity + per-part status grid.
        var leftBox = new VBoxContainer { CustomMinimumSize = new Vector2(372, 0) };
        leftBox.AddThemeConstantOverride("separation", 4);
        hbox.AddChild(leftBox);
        _lblPlayerHp = MakeLabel(16, new Color(0.85f, 0.95f, 0.85f));
        leftBox.AddChild(_lblPlayerHp);
        _barPlayer = MakeBar(new Color(0.3f, 0.85f, 0.32f), new Vector2(372, 20));
        leftBox.AddChild(_barPlayer);
        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 3);
        leftBox.AddChild(grid);
        foreach (var (id, name) in new[]
        {
            ("head", "HEAD"), ("arm_L", "ARM L"), ("arm_R", "ARM R"), ("pod_L", "POD L"),
            ("pod_R", "POD R"), ("leg_L", "LEG L"), ("leg_R", "LEG R"),
        })
        {
            var cell = new VBoxContainer();
            cell.AddThemeConstantOverride("separation", 1);
            var lab = MakeLabel(10, new Color(0.75f, 0.8f, 0.85f));
            lab.Text = name;
            cell.AddChild(lab);
            var bar = MakeBar(new Color(0.45f, 0.75f, 0.5f), new Vector2(84, 8));
            cell.AddChild(bar);
            grid.AddChild(cell);
            _partCells[id] = (lab, bar);
        }

        hbox.AddChild(new VSeparator());

        // -- Center: weapons. The selected row is marked with a pointer
        // and bright text (set in UpdateHud); the hint line teaches the
        // switching scheme in-game.
        var midBox = new VBoxContainer { CustomMinimumSize = new Vector2(308, 0) };
        midBox.AddThemeConstantOverride("separation", 4);
        hbox.AddChild(midBox);
        midBox.AddChild(Title("WEAPONS"));
        (Label, ProgressBar) CdRow(string name)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            var lab = MakeLabel(14, new Color(0.9f, 0.9f, 0.9f, 0.9f));
            lab.Text = name;
            lab.CustomMinimumSize = new Vector2(74, 0);
            row.AddChild(lab);
            var bar = MakeBar(new Color(0.95f, 0.8f, 0.3f), new Vector2(218, 13));
            bar.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(bar);
            midBox.AddChild(row);
            return (lab, bar);
        }
        var rowLaser = CdRow("1 LAS");
        _lblCdLaser = rowLaser.Item1; _cdBarLaser = rowLaser.Item2;
        var rowAc = CdRow("2 AC");
        _lblCdAc = rowAc.Item1; _cdBarAc = rowAc.Item2;
        var rowLrm = CdRow("3 LRM");
        _lblCdLrm = rowLrm.Item1; _cdBarLrm = rowLrm.Item2;
        var hint = MakeLabel(11, new Color(0.6f, 0.66f, 0.72f));
        hint.Text = "1 / 2 / 3 or wheel — select weapon · LMB — fire";
        midBox.AddChild(hint);
        _lblWeaponStatus = MakeLabel(12, new Color(1f, 0.42f, 0.32f));
        midBox.AddChild(_lblWeaponStatus);

        hbox.AddChild(new VSeparator());

        // -- Right: target + own systems readouts.
        var rightBox = new VBoxContainer { CustomMinimumSize = new Vector2(300, 0) };
        rightBox.AddThemeConstantOverride("separation", 4);
        hbox.AddChild(rightBox);
        _lblEnemyHp = MakeLabel(16, new Color(0.98f, 0.85f, 0.8f));
        rightBox.AddChild(_lblEnemyHp);
        _barEnemy = MakeBar(new Color(0.88f, 0.3f, 0.24f), new Vector2(300, 20));
        rightBox.AddChild(_barEnemy);
        _lblRange = MakeLabel(13, new Color(0.85f, 0.88f, 0.9f));
        rightBox.AddChild(_lblRange);
        _lblSpeed = MakeLabel(13, new Color(0.85f, 0.88f, 0.9f));
        rightBox.AddChild(_lblSpeed);
        _lblLegs = MakeLabel(13, new Color(0.85f, 0.88f, 0.9f));
        rightBox.AddChild(_lblLegs);

        // Event feed, top-left: part breaks and other combat events.
        _feedLabel = MakeLabel(15, new Color(0.98f, 0.88f, 0.62f));
        _feedLabel.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        _feedLabel.OffsetLeft = 18; _feedLabel.OffsetTop = 14;
        _feedLabel.OffsetRight = 560; _feedLabel.OffsetBottom = 130;
        layer.AddChild(_feedLabel);

        // Death banner.
        _banner = MakeLabel(46, Colors.White);
        _banner.SetAnchorsPreset(Control.LayoutPreset.Center);
        _banner.OffsetLeft = -450; _banner.OffsetTop = -150; _banner.OffsetRight = 450; _banner.OffsetBottom = -90;
        _banner.HorizontalAlignment = HorizontalAlignment.Center;
        _banner.Visible = false;
        layer.AddChild(_banner);
        _bannerSub = MakeLabel(19, new Color(0.9f, 0.9f, 0.9f));
        _bannerSub.Text = "press R to reset";
        _bannerSub.SetAnchorsPreset(Control.LayoutPreset.Center);
        _bannerSub.OffsetLeft = -450; _bannerSub.OffsetTop = -84; _bannerSub.OffsetRight = 450; _bannerSub.OffsetBottom = -56;
        _bannerSub.HorizontalAlignment = HorizontalAlignment.Center;
        _bannerSub.Visible = false;
        layer.AddChild(_bannerSub);
    }

    private static readonly Color CdNormal = new Color(0.95f, 0.8f, 0.3f);
    private static readonly Color CdDead = new Color(0.45f, 0.12f, 0.1f);
    private static readonly Color CdDeny = new Color(1f, 0.16f, 0.1f);
    private static readonly Color CdLabelNormal = new Color(0.9f, 0.9f, 0.9f, 0.9f);
    private static readonly Color CdLabelDead = new Color(1f, 0.4f, 0.32f);

    // One cooldown row's state: value, destroyed color, denied flash.
    private void UpdateCdRow(ProgressBar bar, Label lab, float value,
        bool destroyed, float denyT)
    {
        bar.Value = destroyed ? 0f : value;
        var fg = bar.GetThemeStylebox("fill") as StyleBoxFlat;
        if (fg != null)
        {
            if (denyT > 0f)
                fg.BgColor = Mathf.Sin((float)_simT * 42f) > 0f ? CdDeny : CdDead;
            else if (destroyed) fg.BgColor = CdDead;
            else fg.BgColor = CdNormal;
        }
        lab.AddThemeColorOverride("font_color", destroyed ? CdLabelDead : CdLabelNormal);
    }

    // The selected weapon's panel row carries a pointer marker and, while
    // the weapon lives, bright text; destroyed rows keep their red.
    private static void SetSelLabel(Label lab, string baseText, bool selected, bool destroyed)
    {
        lab.Text = (selected ? "▶ " : "") + baseText;
        if (selected && !destroyed)
            lab.AddThemeColorOverride("font_color", new Color(1f, 0.95f, 0.6f));
    }

    private void UpdateHud()
    {
        if (_barPlayer == null) return;
        _barPlayer.Value = _hpPlayer / 260f;
        _lblPlayerHp.Text = $"TIMBER WOLF — {_hpPlayer:F0} HP";
        _barEnemy.Visible = !_enemyDead;
        _lblEnemyHp.Visible = !_enemyDead;
        _barEnemy.Value = _hpEnemy / 340f;
        _lblEnemyHp.Text = $"ATLAS — {_hpEnemy:F0} HP";

        bool lasDead = !_partsPlayer.Alive("arm_L");
        bool acDead = !_partsPlayer.Alive("arm_R");
        bool lrmDead = !_partsPlayer.Alive("pod_L") && !_partsPlayer.Alive("pod_R");
        UpdateCdRow(_cdBarLaser, _lblCdLaser, 1f - _cdLaser / 0.45f, lasDead, _denyLaserT);
        UpdateCdRow(_cdBarAc, _lblCdAc, 1f - _cdAc / 0.85f, acDead, _denyAcT);
        UpdateCdRow(_cdBarLrm, _lblCdLrm, 1f - _cdLrm / 3.5f, lrmDead, _denyLrmT);
        SetSelLabel(_lblCdLaser, "1 LAS", _weaponSel == 0, lasDead);
        SetSelLabel(_lblCdAc, "2 AC", _weaponSel == 1, acDead);
        SetSelLabel(_lblCdLrm, "3 LRM", _weaponSel == 2, lrmDead);

        // Per-part status cells (player mech).
        foreach (var (id, cell) in _partCells)
        {
            var part = _partsPlayer.GetPart(id);
            float frac = part.MaxHp > 0f ? part.Hp / part.MaxHp : 0f;
            cell.Bar.Value = frac;
            var fg = cell.Bar.GetThemeStylebox("fill") as StyleBoxFlat;
            if (fg != null)
                fg.BgColor = part.Destroyed ? CdDead
                    : frac < 0.35f ? new Color(0.9f, 0.45f, 0.2f)
                    : new Color(0.45f, 0.75f, 0.5f);
            cell.Lab.AddThemeColorOverride("font_color",
                part.Destroyed ? CdLabelDead : new Color(0.75f, 0.8f, 0.85f));
        }

        // Systems readouts: target range, own speed, leg state.
        float range = new Vector2(_enemy.RootPos.X - _player.RootPos.X,
                                  _enemy.RootPos.Z - _player.RootPos.Z).Length();
        _lblRange.Text = _enemyDead ? "TARGET: DESTROYED" : $"TARGET RANGE: {range:F1} u";
        _lblSpeed.Text = $"SPEED: {_playerVel.Length():F1} u/s" + (_running && _playerVel.Length() > 0.5f ? " — RUN" : "");
        bool lLeg = _partsPlayer.Alive("leg_L"), rLeg = _partsPlayer.Alive("leg_R");
        _lblLegs.Text = lLeg && rLeg ? "LEGS: OK"
            : !lLeg && !rLeg ? "LEGS: BOTH LOST"
            : lLeg ? "LEGS: RIGHT LOST — IMMOBILE" : "LEGS: LEFT LOST — IMMOBILE";
        _lblLegs.AddThemeColorOverride("font_color",
            lLeg && rLeg ? new Color(0.85f, 0.88f, 0.9f) : new Color(1f, 0.42f, 0.32f));
        if (_player.IsKnockedDown)
        {
            _lblLegs.Text = "STATUS: KNOCKED DOWN";
            _lblLegs.AddThemeColorOverride("font_color", new Color(1f, 0.42f, 0.32f));
        }

        var status = new System.Collections.Generic.List<string>();
        if (lasDead) status.Add("LASER DESTROYED — LEFT ARM LOST");
        if (acDead) status.Add("AUTOCANNON DESTROYED — RIGHT ARM LOST");
        if (lrmDead) status.Add("LRM DESTROYED — BOTH PODS LOST");
        else if (!_partsPlayer.Alive("pod_L")) status.Add("LRM DEGRADED — LEFT POD LOST (4 ROCKETS)");
        else if (!_partsPlayer.Alive("pod_R")) status.Add("LRM DEGRADED — RIGHT POD LOST (4 ROCKETS)");
        if (!_partsPlayer.Alive("head")) status.Add("SENSORS DESTROYED — CROSSHAIR SWAY");
        _lblWeaponStatus.Text = string.Join("\n", status);

        _feedLabel.Text = string.Join("\n",
            System.Linq.Enumerable.Select(_feed, e => e.Text));

        // Head-loss HUD disturbance: while the flicker timer runs, the
        // whole HUD stutters (seeded, visual only).
        float alpha = 1f;
        if (_hudFlickerT > 0f)
            alpha = _rng.NextDouble() < 0.38 ? 0.15f : 1f;
        foreach (var c in new Control[] { _panel, _feedLabel, _cross })
        {
            var m = c.Modulate;
            m.A = alpha;
            c.Modulate = m;
        }
    }

    private void UpdateDroop(float dt)
    {
        if (_enemyDead && _enemyDroopT < 1f)
        {
            _enemyDroopT = Mathf.Min(_enemyDroopT + dt / 1.5f, 1f);
            float t = _enemyDroopT;
            _enemy.Droop = t * t * (3f - 2f * t) * 0.5f;
        }
        if (_playerDead && _playerDroopT < 1f)
        {
            _playerDroopT = Mathf.Min(_playerDroopT + dt / 1.5f, 1f);
            float t = _playerDroopT;
            _player.Droop = t * t * (3f - 2f * t) * 0.5f;
        }
    }

    // Atlas AC/20 gunnery: range + line-of-sight gated, leads the player's
    // velocity at time-of-flight, seeded ±1.2° inaccuracy cone.
    private void UpdateAtlasGunnery(float dt)
    {
        if (_enemyDead || _playerDead) return;
        if (_enemy.IsKnockedDown) return; // no firing from the ground
        if (_cdAtlas > 0f) return;
        if (!_partsEnemy.Alive("arm_R")) return; // AC/20 mount lost with the right arm
        var muzzle = AtlasMuzzle();
        var target = _player.RootPos + Vector3.Up * 7.6f;
        float dist = muzzle.DistanceTo(target);
        if (dist > 75f) return;
        var los = _hit.Ray(muzzle, (target - muzzle).Normalized(), dist, "ATLAS");
        if (los.Hit && los.Target != "TW") return; // blocked: building/terrain
        float tof = dist / 50f;
        var aim = target + _playerVel * tof;
        // Head destroyed: sensors gone, aim spread triples (1.2 -> 3.6 deg).
        var dir = ApplyInaccuracy((aim - muzzle).Normalized(),
            _partsEnemy.Alive("head") ? 1.2f : 3.6f);
        SpawnProjectile(muzzle, dir * 50f, 5f, 18f, 0f, "ATLAS", rocket: false);
        SpawnFlash(muzzle, new Color(1f, 0.6f, 0.2f), 0.5f, 0.08f);
        _cdAtlas = 2.4f;
        _atlasShots++;
        GD.Print($"WPN ATLASFIRE dist={dist:F1} tof={tof:F2} playerVel={_playerVel.Length():F1}");
    }

    private Vector3 ApplyInaccuracy(Vector3 dir, float maxDeg)
    {
        float theta = Mathf.DegToRad(maxDeg) * Mathf.Sqrt((float)_rng.NextDouble());
        float phi = (float)(_rng.NextDouble() * Mathf.Tau);
        var side = dir.Cross(Mathf.Abs(dir.Y) < 0.9f ? Vector3.Up : Vector3.Right).Normalized();
        var up2 = side.Cross(dir).Normalized();
        var axis = (side * Mathf.Cos(phi) + up2 * Mathf.Sin(phi)).Normalized();
        return dir.Rotated(axis, theta).Normalized();
    }

    private void UpdateCombat(float dt)
    {
        _cdLaser = Mathf.Max(_cdLaser - dt, 0f);
        _cdAc = Mathf.Max(_cdAc - dt, 0f);
        _cdLrm = Mathf.Max(_cdLrm - dt, 0f);
        _cdAtlas = Mathf.Max(_cdAtlas - dt, 0f);
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
            if (q.Delay <= 0f) { _lrmQueue.RemoveAt(i); LaunchRocket(q.Aim, q.Left, q.Salvo); }
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
        if (!_test && !_terrainTest && ((!_weaponsTest && !_breakTest) || _atlasMayFire)) UpdateAtlasGunnery(dt);
    }

    // ---------- test choreography ----------

    private void SetPhase(int p)
    {
        _phase = p;
        _phaseT = 0;
        _stallTimer = 0f;
        GD.Print($"DUEL phase={p} t={_simT:F2}");
    }

    private void UpdateTest(float delta)
    {
        _phaseT += delta;
        switch (_phase)
        {
            case 0: // settle
                _player.SetDrive(0f, 0f);
                _enemy.SetDrive(Mathf.Pi, 0f);
                if (_phaseT > 1.6) SetPhase(1);
                break;
            case 1: // Atlas walks at the player; grab stride + wide frames
                _player.SetDrive(0f, 0f);
                _enemy.SetDrive(Mathf.Pi, 1.8f);
                if (_enemy.ConsumeStepMidCross() && _phaseT > 0.6)
                {
                    _strideShots++;
                    if (_strideShots == 1)
                    {
                        GD.Print($"DUEL ATLAS CONTACT analytic {_enemy.FootReport(false)}");
                        GD.Print($"DUEL ATLAS CONTACT engine   {_enemy.FootReport(true)}");
                        QueueCapture("res://proof/duel_atlas_stride.png", "stride", "atlasSide");
                    }
                    else if (_strideShots == 2)
                    {
                        QueueCapture("res://proof/duel_main.png", "main", "wide", () =>
                        {
                            SetPhase(2);
                            _player.Teleport(new Vector3(0f, Main.HeightAt(0f, 26f), 26f), 0f);
                            _player.TorsoTwist = 1.15f;
                        });
                    }
                }
                if (_phaseT > 12) { GD.PrintErr("DUEL FAIL: atlas stride shots missing"); GetTree().Quit(1); }
                break;
            case 2: // torso twist: legs walk -Z, torso twisted +66 deg
                _player.SetDrive(0f, 1.8f);
                _enemy.SetDrive(Mathf.Pi, 0f);
                if (_phaseT > 1.4 && !_twistLogged)
                {
                    _twistLogged = true;
                    float legs = _player.LegsYaw;
                    float torso = _player.EngineTorsoYaw();
                    float rel = Mathf.RadToDeg(MechWalker.WrapAngle(torso - legs));
                    GD.Print($"DUEL TWIST legsYaw={Mathf.RadToDeg(legs):F1}deg torsoWorldYaw(engine)={Mathf.RadToDeg(torso):F1}deg relative={rel:F1}deg twistCmd={Mathf.RadToDeg(_player.TorsoTwist):F1}deg");
                    QueueCapture("res://proof/duel_torso_twist.png", "twist", "twist", () =>
                    {
                        SetPhase(3);
                        _player.TorsoTwist = 0f;
                        _player.Teleport(new Vector3(6f, Main.HeightAt(6f, 27f), 27f), 0f);
                        _minTreeSep = 1e9f;
                        _lastPlayerPos = _player.RootPos;
                    });
                }
                break;
            case 3: // drive straight at the tree at (6,16)
                _player.SetDrive(0f, 2.6f);
                _enemy.SetDrive(Mathf.Pi, 0f);
                {
                    float sep = new Vector2(_player.RootPos.X - 6f, _player.RootPos.Z - 16f).Length();
                    _minTreeSep = Mathf.Min(_minTreeSep, sep);
                    float moved = _player.RootPos.DistanceTo(_lastPlayerPos);
                    _lastPlayerPos = _player.RootPos;
                    if (_phaseT > 2.0)
                    {
                        if (moved < 0.004f) _stallTimer += delta; else _stallTimer = 0f;
                        if (_stallTimer > 0.5f)
                        {
                            GD.Print($"DUEL TREE minSep={_minTreeSep:F3} finalSep={sep:F3} sumRadii={0.7f + _player.CollisionRadius:F2} playerPos={_player.RootPos}");
                            QueueCapture("res://proof/duel_collision_prop.png", "prop", "prop", () =>
                            {
                                SetPhase(4);
                                _player.Teleport(new Vector3(0f, Main.HeightAt(0f, 10f), 10f), 0f);
                                _enemy.Teleport(new Vector3(0f, Main.HeightAt(0f, -2f), -2f), Mathf.Pi);
                                _minMechSep = 1e9f;
                            });
                        }
                    }
                    if (_phaseT > 14) { GD.PrintErr("DUEL FAIL: tree stall never detected"); GetTree().Quit(1); }
                }
                break;
            case 4: // mechs driven at each other
                _player.SetDrive(0f, 2.2f);
                _enemy.SetDrive(Mathf.Pi, 1.8f);
                {
                    float dist = new Vector2(_player.RootPos.X - _enemy.RootPos.X,
                                             _player.RootPos.Z - _enemy.RootPos.Z).Length();
                    _minMechSep = Mathf.Min(_minMechSep, dist);
                    if (_phaseT > 2.2f)
                    {
                        if (Mathf.Abs(dist - _minMechSep) < 0.001f && dist <= _player.CollisionRadius + _enemy.CollisionRadius + 0.05f)
                            _stallTimer += delta;
                        else _stallTimer = 0f;
                        if (_stallTimer > 0.5f)
                        {
                            GD.Print($"DUEL MECH minSep={_minMechSep:F3} finalSep={dist:F3} sumRadii={_player.CollisionRadius + _enemy.CollisionRadius:F2}");
                            QueueCapture("res://proof/duel_collision_mech.png", "mech", "mechSide");
                            SetPhase(5);
                        }
                    }
                    if (_phaseT > 16) { GD.PrintErr("DUEL FAIL: mech stall never detected"); GetTree().Quit(1); }
                }
                break;
        }
    }

    // ---------- weapons-test choreography ----------

    private void SetWPhase(int p)
    {
        _wPhase = p;
        _wPhaseT = 0;
        _wShotsFired = 0;
        _wCaptured = false;
        _wDeadShotQueued = false;
        _wDeathLogged = false;
        GD.Print($"WPNTEST phase={p} t={_simT:F2}");
    }

    private void WFail(string msg)
    {
        GD.PrintErr($"WPNTEST FAIL: {msg}");
        GetTree().Quit(1);
    }

    private Vector3 AtlasAim() => _enemy.RootPos + Vector3.Up * 8.2f;

    private void WSetup(Vector3 pPos, Vector3 ePos)
    {
        _player.Teleport(pPos, 0f);
        _enemy.Teleport(ePos, Mathf.Pi);
        _player.TorsoTwist = 0f;
        _player.TorsoPitch = 0f;
        _enemy.TorsoTwist = 0f;
        _enemy.TorsoPitch = 0f;
        _playerYaw = 0f;
        _enemyYaw = Mathf.Pi;
        _prevPlayerRoot = _player.RootPos;
        _playerVel = Vector3.Zero;
    }

    private void UpdateWeaponsTest(float dt)
    {
        _wPhaseT += dt;
        // Atlas torso tracks the player in every phase (gunnery muzzle and
        // visuals stay honest); both walkers otherwise hold position.
        var toP = _player.RootPos - _enemy.RootPos;
        if (toP.LengthSquared() > 0.01f)
            _enemy.TorsoTwist = MechWalker.WrapAngle(MechWalker.YawOf(toP.Normalized()) - _enemyYaw);
        if (_wPhaseT > 40) { WFail($"phase {_wPhase} timeout"); return; }

        switch (_wPhase)
        {
            case 0: // settle at the open lane: TW (0,18) vs Atlas (0,-12), 30u
                _player.SetDrive(0f, 0f);
                _enemy.SetDrive(Mathf.Pi, 0f);
                if (_wPhaseT > 1.2) SetWPhase(1);
                break;

            case 1: // laser x3 at a stationary Atlas: 3 x 12 = 36 exactly
                if (_wShotsFired == 0 && _wPhaseT > 0.1)
                {
                    FireLaser(AtlasAim());
                    _wShotsFired++;
                    QueueCapture("res://proof/weapons_laser.png", "wlaser", "wpnSide");
                }
                else if (_wShotsFired == 1 && _wPhaseT > 0.65) { FireLaser(AtlasAim()); _wShotsFired++; }
                else if (_wShotsFired == 2 && _wPhaseT > 1.2) { FireLaser(AtlasAim()); _wShotsFired++; }
                else if (_wShotsFired == 3 && _wPhaseT > 1.7)
                {
                    if (_atlasDmgEvents != 3 || Mathf.Abs(_atlasDmgTotal - 36f) > 0.01f)
                        WFail($"laser events={_atlasDmgEvents} dmg={_atlasDmgTotal:F1} (want 3 / 36)");
                    if (Mathf.Abs(_hpEnemy - 304f) > 0.01f) WFail($"laser atlasHp={_hpEnemy:F1} (want 304)");
                    GD.Print($"WPNTEST LASER PASS events=3 dmg=36 atlasHp={_hpEnemy:F0}");
                    _atlasDmgEvents = 0; _atlasDmgTotal = 0f;
                    SetWPhase(2);
                }
                break;

            case 2: // autocannon x2: 2 x 26 = 52 exactly. ROUTING DELTA
                    // (terrain step): on the old sine terrain the shells
                    // dropped ~0.6u in flight and landed on the pelvis/
                    // thigh junction, so leg_L absorbed the 52. On the
                    // heightfield the two stand heights are near-equal,
                    // the shells arrive higher and land mid-torso: the
                    // pool takes the 52 (304 -> 252) and leg_L stays
                    // untouched. Events/total are the invariant; the
                    // routing follows the terrain, as documented before.
                if (_wShotsFired == 0 && _wPhaseT > 0.1) { FireAutocannon(AtlasAim()); _wShotsFired++; }
                else if (_wShotsFired == 1 && _wPhaseT > 1.05) { FireAutocannon(AtlasAim()); _wShotsFired++; }
                else if (_wShotsFired == 2 && _wPhaseT > 2.8)
                {
                    if (_atlasDmgEvents != 2 || Mathf.Abs(_atlasDmgTotal - 52f) > 0.01f)
                        WFail($"ac events={_atlasDmgEvents} dmg={_atlasDmgTotal:F1} (want 2 / 52)");
                    if (Mathf.Abs(_hpEnemy - 252f) > 0.01f) WFail($"ac atlasHp={_hpEnemy:F1} (want 252 pool under terrain-step routing)");
                    if (Mathf.Abs(_partsEnemy.HpOf("leg_L") - 90f) > 0.01f)
                        WFail($"ac legL partHp={_partsEnemy.HpOf("leg_L"):F1} (want 90 = untouched, shells routed to torso)");
                    GD.Print($"WPNTEST AC PASS events=2 dmg=52 atlasHp={_hpEnemy:F0} legL partHp={_partsEnemy.HpOf("leg_L"):F0} (shells routed to torso)");
                    _atlasDmgEvents = 0; _atlasDmgTotal = 0f;
                    SetWPhase(3);
                }
                break;

            case 3: // LRM salvo: count rockets that damage the Atlas (of 8)
                if (_wShotsFired == 0 && _wPhaseT > 0.1) { FireLrmSalvo(AtlasAim()); _wShotsFired++; }
                if (!_wCaptured && _wPhaseT > 0.55)
                {
                    _wCaptured = true;
                    QueueCapture("res://proof/weapons_rockets.png", "wrockets", "wpnRockets");
                }
                if (_wPhaseT > 3.4)
                {
                    if (_atlasDmgEvents < 5) WFail($"lrm damaging rockets={_atlasDmgEvents}/8 dmg={_atlasDmgTotal:F1}");
                    GD.Print($"WPNTEST LRM PASS rocketsDamaging={_atlasDmgEvents}/8 dmg={_atlasDmgTotal:F1} atlasHp={_hpEnemy:F1}");
                    _atlasDmgEvents = 0; _atlasDmgTotal = 0f;
                    WSetup(new Vector3(22f, Main.HeightAt(22f, 24f), 24f), new Vector3(22f, Main.HeightAt(22f, -16f), -16f));
                    _wHpSnapshot = _hpEnemy;
                    SetWPhase(4);
                }
                break;

            case 4: // laser with the (22,8) building on the line: zero damage
                if (_wShotsFired == 0 && _wPhaseT > 0.3)
                {
                    FireLaser(_enemy.RootPos + Vector3.Up * 6.0f);
                    _wShotsFired++;
                    QueueCapture("res://proof/weapons_blocked.png", "wblocked", "wpnBlocked");
                }
                if (_wShotsFired == 1 && _wPhaseT > 1.2)
                {
                    if (_atlasDmgTotal > 0.01f || Mathf.Abs(_hpEnemy - _wHpSnapshot) > 0.01f)
                        WFail($"blocked laser leaked dmg={_atlasDmgTotal:F1} hp {_wHpSnapshot:F1}->{_hpEnemy:F1}");
                    GD.Print("WPNTEST BLOCKED PASS building stopped the beam, atlasHp unchanged");
                    // Phase 5 engages at 45u, not 30u: at 30u the shell
                    // TOF (0.6s) is shorter than the strafe reversal and
                    // the lead error stays inside the part-box volume
                    // (the digitigrade thighs protrude ~2.7u ahead of the
                    // capsule), so return fire could not miss at all. At
                    // 45u the TOF (0.9s) spans a full reversal and the
                    // dodge is measurable again.
                    WSetup(new Vector3(0f, Main.HeightAt(0f, 20f), 20f), new Vector3(0f, Main.HeightAt(0f, -25f), -25f));
                    _twDmgEvents = 0; _twDmgTotal = 0f; _atlasShots = 0; _wPrevShots = 0;
                    _atlasMayFire = true;
                    _wStrafeT = 0; _wStrafeDir = 1f;
                    SetWPhase(5);
                }
                break;

            case 5: // Atlas return fire vs a strafing player: hits AND misses.
                    // The player reverses every 0.9s at full speed — with a
                    // 0.6s shell flight, most shots have a reversal inside
                    // their lead window, which is what makes dodging real.
                _wStrafeT += dt;
                if (_wStrafeT > 0.9) { _wStrafeT = 0; _wStrafeDir = -_wStrafeDir; }
                float strafeYaw = _wStrafeDir > 0 ? -Mathf.Pi / 2f : Mathf.Pi / 2f;
                _player.SetDrive(strafeYaw, 4.2f);
                _player.TorsoTwist = -strafeYaw;
                _enemy.SetDrive(Mathf.Pi, 0f);
                if (_atlasShots != _wPrevShots) { _wPrevShots = _atlasShots; _wLastShotT = _wPhaseT; }
                if (_atlasShots >= 6 && _wPhaseT > _wLastShotT + 1.4)
                {
                    int hits = _twDmgEvents;
                    int misses = _atlasShots - hits;
                    if (hits < 1) WFail($"atlas return fire: 0 hits in {_atlasShots} shots");
                    if (misses < 1) WFail($"atlas return fire: 0 misses in {_atlasShots} shots (dodge impossible)");
                    if (Mathf.Abs(_twDmgTotal - hits * 18f) > 0.01f)
                        WFail($"atlas dmg total={_twDmgTotal:F1} != hits*18 ({hits * 18})");
                    GD.Print($"WPNTEST ATLASFIRE PASS shots={_atlasShots} hits={hits} misses={misses} playerHp={_hpPlayer:F0}");
                    _atlasMayFire = false;
                    _player.TorsoTwist = 0f;
                    ResetDuel();
                    WSetup(new Vector3(0f, Main.HeightAt(0f, 18f), 18f), new Vector3(0f, Main.HeightAt(0f, -12f), -12f));
                    SetWPhase(6);
                }
                if (_wPhaseT > 35) WFail($"atlas return fire timeout shots={_atlasShots}");
                break;

            case 6: // sustained fire -> Atlas destroyed, banner, slump frame
                if (!_enemyDead)
                {
                    FireLaser(AtlasAim());
                    FireAutocannon(AtlasAim());
                }
                if (!_wCaptured && _wPhaseT > 0.9)
                {
                    _wCaptured = true;
                    QueueCapture("res://proof/weapons_hud.png", "whud", "wpnHud");
                }
                if (_enemyDead && !_wDeathLogged)
                {
                    _wDeathLogged = true;
                    _wDeathT = _wPhaseT;
                    GD.Print($"WPNTEST KILL atlas destroyed at phaseT={_wPhaseT:F2}");
                }
                if (_enemyDead && _wPhaseT > _wDeathT + 1.8 && !_wDeadShotQueued)
                {
                    if (_hpEnemy != 0f) WFail($"dead atlas hp={_hpEnemy:F1} (want clamped 0)");
                    if (!_enemy.Halted) WFail("dead atlas walker not halted");
                    if (!_banner.Visible || _banner.Text != "ATLAS DESTROYED")
                        WFail($"kill banner wrong: visible={_banner.Visible} text='{_banner.Text}'");
                    GD.Print("WPNTEST KILL PASS hp clamped 0, walker halted, banner 'ATLAS DESTROYED'");
                    _wDeadShotQueued = true;
                    QueueCapture("res://proof/weapons_atlas_dead.png", "wdead", "wpnDead", () => SetWPhase(7));
                }
                if (_wPhaseT > 30) WFail("kill sequence timeout");
                break;

            case 7: // player death banner + R reset restores everything
                if (_wShotsFired == 0 && _wPhaseT > 0.3)
                {
                    _wShotsFired++;
                    var dummy = new HitModel.Result
                    {
                        Hit = true,
                        Target = "TW",
                        Zone = "torso",
                        Point = _player.RootPos + Vector3.Up * 8f
                    };
                    ApplyDamage(_player, 9999f, dummy, "ATLAS");
                    if (!_playerDead) WFail("player death flag not set");
                    if (!_banner.Visible || _banner.Text != "MECH DESTROYED — press R to reset")
                        WFail($"player banner wrong: visible={_banner.Visible} text='{_banner.Text}'");
                    GD.Print("WPNTEST PLAYERDEATH PASS banner 'MECH DESTROYED — press R to reset'");
                }
                if (_wShotsFired == 1 && _wPhaseT > 0.9)
                {
                    ResetDuel();
                    if (_hpPlayer != 260f || _hpEnemy != 340f || _playerDead || _enemyDead || _banner.Visible)
                        WFail($"reset state wrong hp={_hpPlayer}/{_hpEnemy} dead={_playerDead}/{_enemyDead} banner={_banner.Visible}");
                    GD.Print("WPNTEST RESET PASS hp 260/340 restored, deaths and banner cleared");
                    GD.Print("WPNTEST DONE");
                    GetTree().Quit(0);
                }
                break;
        }
    }

    // ---------- breakable-test choreography ----------

    private void SetBPhase(int p)
    {
        _bPhase = p;
        _bPhaseT = 0;
        _bStep = 0;
        _bT0 = 0;
        _bFlag1 = false;
        GD.Print($"BRKTEST phase={p} t={_simT:F2}");
    }

    private void BStep(int s)
    {
        _bStep = s;
        _bT0 = _bPhaseT;
        _bFlag1 = false;
        _bCount1 = 0;
    }

    private float BStepT => (float)(_bPhaseT - _bT0);

    private void BFail(string msg)
    {
        GD.PrintErr($"BRKTEST FAIL: {msg}");
        GetTree().Quit(1);
    }

    private void BSetup(Vector3 pPos, Vector3 ePos)
    {
        _player.Teleport(pPos, 0f);
        _enemy.Teleport(ePos, Mathf.Pi);
        _player.TorsoTwist = 0f;
        _player.TorsoPitch = 0f;
        _enemy.TorsoTwist = 0f;
        _enemy.TorsoPitch = 0f;
        _playerYaw = 0f;
        _enemyYaw = Mathf.Pi;
        _prevPlayerRoot = _player.RootPos;
        _playerVel = Vector3.Zero;
        _atlasMayFire = false;
    }

    // Harness damage through the real ApplyDamage path (attribution runs
    // on the part's own largest-volume center — a point guaranteed to
    // sit inside the part). Same precedent as the weapons test's crafted
    // player-death hit.
    private void CraftHit(MechWalker target, string partId, float dmg, int salvoId = 0)
    {
        var parts = target == _player ? _partsPlayer : _partsEnemy;
        var hit = new HitModel.Result
        {
            Hit = true,
            Target = target == _player ? "TW" : "ATLAS",
            Zone = "torso",
            Point = parts.PartAimPoint(partId)
        };
        ApplyDamage(target, dmg, hit, target == _player ? "ATLAS" : "TW", salvoId);
    }

    // Craft via a real HitModel ray: needed for the TW head, whose
    // volume interior is shadowed by the torso core's AABB (an interior
    // point attributes torso; a surface hit attributes head, exactly as
    // real aimed fire does). The head ray comes in angled onto the
    // canopy face: a purely vertical drop races the capsule's flat top
    // disc in the same column (sub-decimeter, decided by the root tilt
    // the terrain happens to give), while the angled ray meets the
    // head box where it protrudes past the capsule, like a real shot.
    private void CraftRayHit(MechWalker target, string partId, float dmg)
    {
        var parts = target == _player ? _partsPlayer : _partsEnemy;
        var aim = parts.PartAimPoint(partId);
        Vector3 from;
        if (partId == "head")
        {
            var fwd = MechWalker.HeadingVec(target.TorsoYawWorld);
            from = aim + Vector3.Up * 5f + fwd * 7f;
        }
        else from = aim + Vector3.Up * 15f;
        var res = _hit.Ray(from, (aim - from).Normalized(), 40f, target == _player ? "ATLAS" : "TW");
        GD.Print($"BRK CRAFTRAY {partId} hit={res.Hit} target={res.Target} part={res.Part} point={res.Point}");
        if (!res.Hit || res.Part != partId)
        {
            BFail($"craft ray for {partId} attributed '{res.Part}'");
            return;
        }
        ApplyDamage(target, dmg, res, target == _player ? "ATLAS" : "TW");
    }

    private void UpdateBreakTest(float dt)
    {
        _bPhaseT += dt;
        if (_bPhaseT > 90) { BFail($"phase {_bPhase} timeout"); return; }

        switch (_bPhase)
        {
            case 0: // settle on the lane: TW (0,18) vs Atlas (0,-12)
                _player.SetDrive(0f, 0f);
                _enemy.SetDrive(Mathf.Pi, 0f);
                if (_bPhaseT > 1.0) SetBPhase(1);
                break;

            case 1: // POD: part HP depletes per hit, breaks at <= 0, debris
                    // settles, then stays behind while the mech walks away
                _player.SetDrive(0f, _bStep >= 3 ? 2.4f : 0f);
                _enemy.SetDrive(Mathf.Pi, 0f);
                if (_bStep == 0 && _bPhaseT > 0.2)
                {
                    CraftHit(_player, "pod_L", 26f);
                    if (Mathf.Abs(_partsPlayer.HpOf("pod_L") - 19f) > 0.01f)
                        BFail($"pod hp after 26 dmg = {_partsPlayer.HpOf("pod_L"):F1} (want 19)");
                    GD.Print("BRKTEST PODHP PASS part HP 45 -> 19 after one 26 hit");
                    BStep(1);
                }
                else if (_bStep == 1 && BStepT > 0.3)
                {
                    CraftHit(_player, "pod_L", 26f);
                    if (_partsPlayer.Alive("pod_L")) BFail("pod_L alive after 52 total damage");
                    if (_partsPlayer.DebrisOf("pod_L") == null) BFail("pod_L debris missing");
                    GD.Print("BRKTEST PODBREAK PASS pod_L broke at <= 0 HP, debris spawned");
                    BStep(2);
                }
                else if (_bStep == 2)
                {
                    // Mid-fall frame at ~0.3s of debris age (the piece
                    // leaves at +2.8 u/s up, then falls ~11u: at 0.3s it
                    // is visibly airborne and clear of the torso).
                    if (!_bFlag1 && BStepT > 0.30)
                    {
                        _bFlag1 = true;
                        QueueCapture("res://proof/break_pod_fall.png", "bpodfall", "brkPlayer");
                    }
                    var d = _partsPlayer.DebrisOf("pod_L");
                    if (d != null && d.Settled)
                    {
                        if (!_partsPlayer.DebrisRestOk("pod_L", 0.15f, out float minY, out float gY))
                            BFail($"pod debris rest off: lowestCornerY={minY:F3} terrainThere={gY:F3}");
                        GD.Print($"BRKTEST DEBRISREST PASS pod_L lowest corner on terrain (y={minY:F3} vs {gY:F3})");
                        _bVec1 = d.Node.GlobalPosition;
                        _bV1 = _player.RootPos.DistanceTo(_bVec1);
                        _camFocus = _bVec1;
                        BStep(3);
                    }
                    else if (BStepT > 7) BFail("pod debris never settled");
                }
                else if (_bStep == 3 && BStepT > 3.0)
                {
                    _player.SetDrive(0f, 0f);
                    var d = _partsPlayer.DebrisOf("pod_L");
                    float drift = d.Node.GlobalPosition.DistanceTo(_bVec1);
                    float away = _player.RootPos.DistanceTo(d.Node.GlobalPosition) - _bV1;
                    if (drift > 0.05f) BFail($"debris moved with the mech: drift={drift:F3}");
                    if (away < 3f) BFail($"mech did not walk away from debris: delta={away:F2}");
                    GD.Print($"BRKTEST DETACH PASS debris static (drift={drift:F3}) while mech walked {away:F1}u away");
                    QueueCapture("res://proof/break_debris_rest.png", "bdebris", "brkDebris");
                    SetBPhase(2);
                }
                break;

            case 2: // SALVO: one pod gone -> 4 rockets; both gone -> denied
                _player.SetDrive(0f, 0f);
                _enemy.SetDrive(Mathf.Pi, 0f);
                if (_bStep == 0)
                {
                    BStep(1);
                    _bCount1 = _rocketsLaunched;
                    FireLrmSalvo(AtlasAim());
                }
                else if (_bStep == 1 && BStepT > 1.6)
                {
                    int n = _rocketsLaunched - _bCount1;
                    if (n != 4) BFail($"salvo with pod_L gone = {n} rockets (want 4)");
                    GD.Print("BRKTEST SALVO4 PASS one pod lost -> 4 rockets");
                    CraftHit(_player, "pod_R", 26f);
                    CraftHit(_player, "pod_R", 26f);
                    if (_partsPlayer.Alive("pod_R")) BFail("pod_R alive after 52 damage");
                    BStep(2);
                    _bCount1 = _rocketsLaunched;
                    _lastDenied = "";
                    FireLrmSalvo(AtlasAim());
                    if (_lastDenied != "lrm") BFail("LRM not denied with both pods gone");
                }
                else if (_bStep == 2 && BStepT > 0.8)
                {
                    if (_rocketsLaunched != _bCount1) BFail("denied LRM still launched rockets");
                    GD.Print("BRKTEST SALVODENIED PASS both pods gone -> LRM denied, 0 rockets");
                    ResetDuel();
                    BSetup(new Vector3(0f, Main.HeightAt(0f, 20f), 20f), new Vector3(0f, Main.HeightAt(0f, -10f), -10f));
                    SetBPhase(3);
                }
                break;

            case 3: // ARM (Atlas): real AC fire breaks arm_R; its AC/20 stops
                _player.SetDrive(0f, 0f);
                if (_bStep == 0)
                {
                    _atlasMayFire = true;
                    _enemy.SetDrive(Mathf.Pi, 0f);
                    if (_atlasShots >= 2) BStep(1);
                    else if (_bPhaseT > 20) BFail($"atlas fired only {_atlasShots} shots pre-break");
                }
                else if (_bStep == 1)
                {
                    _enemy.SetDrive(Mathf.Pi, 0f);
                    if (_cdAc <= 0f) FireAutocannon(_partsEnemy.PartAimPoint("arm_R"));
                    if (!_partsEnemy.Alive("arm_R"))
                    {
                        GD.Print($"BRKTEST ARMBREAK PASS atlas arm_R destroyed by real AC fire (partHp logs above), atlasShots so far={_atlasShots}");
                        BStep(2);
                        _bCount1 = _atlasShots;
                    }
                    else if (BStepT > 15) BFail("atlas arm_R never broke under AC fire");
                }
                else if (_bStep == 2)
                {
                    _enemy.SetDrive(Mathf.Pi, 1.6f);
                    if (BStepT > 1.2 && !_bFlag1)
                    {
                        _bFlag1 = true;
                        QueueCapture("res://proof/break_atlas_noarm.png", "bnoarm", "brkAtlas");
                    }
                    if (BStepT > 5.5)
                    {
                        if (_atlasShots != _bCount1)
                            BFail($"atlas AC/20 fired after arm_R loss: {_bCount1} -> {_atlasShots}");
                        GD.Print($"BRKTEST ARMGUN PASS atlas AC/20 silent for 5.5s after arm_R loss (shots flat at {_atlasShots})");
                        _atlasMayFire = false;
                        ResetDuel();
                        BSetup(new Vector3(0f, Main.HeightAt(0f, 18f), 18f), new Vector3(0f, Main.HeightAt(0f, -12f), -12f));
                        SetBPhase(4);
                    }
                }
                break;

            case 4: // PLAYER ARM: laser denied with zero beams; HUD frame
                _player.SetDrive(0f, 0f);
                _enemy.SetDrive(Mathf.Pi, 0f);
                if (_bStep == 0)
                {
                    int guard = 0;
                    while (_partsPlayer.Alive("arm_L") && guard++ < 5)
                        CraftHit(_player, "arm_L", 26f);
                    if (_partsPlayer.Alive("arm_L")) BFail("player arm_L would not break");
                    _bCount1 = _beamsSpawned;
                    _lastDenied = "";
                    FireLaser(AtlasAim());
                    if (_lastDenied != "laser") BFail("laser not denied after arm_L loss");
                    if (_beamsSpawned != _bCount1) BFail("denied laser still spawned a beam");
                    GD.Print("BRKTEST LASERDENIED PASS arm_L lost -> laser denied, 0 beams");
                    QueueCapture("res://proof/break_hud_destroyed.png", "bhud", "wpnHud");
                    BStep(1);
                }
                else if (_bStep == 1 && BStepT > 0.8)
                {
                    ResetDuel();
                    BSetup(new Vector3(-38f, Main.HeightAt(-38f, 38f), 38f), new Vector3(0f, Main.HeightAt(0f, 24f), 24f));
                    _bDragMax = 0f;
                    SetBPhase(5);
                }
                break;

            case 5: // LEG (Atlas): baseline speed, break leg_L, then the
                    // mech must NOT walk at all (owner's rule: a lost leg
                    // immobilizes; it fights on as a stationary turret)
                _player.SetDrive(0f, 0f);
                if (_bStep == 0)
                {
                    _enemy.SetDrive(Mathf.Pi, 0f);
                    if (BStepT > 1.0) BStep(1);
                }
                else if (_bStep == 1)
                {
                    _enemy.SetDrive(Mathf.Pi, 2.2f);
                    if (!_bFlag1 && BStepT > 1.0)
                    {
                        _bFlag1 = true;
                        _bVec1 = _enemy.RootPos;
                        _bT0 = _bPhaseT;
                    }
                    else if (_bFlag1 && BStepT > 3.0)
                    {
                        var d = new Vector2(_enemy.RootPos.X - _bVec1.X, _enemy.RootPos.Z - _bVec1.Z).Length();
                        _bV1 = d / BStepT;
                        GD.Print($"BRKTEST BASESPEED atlas baseline={_bV1:F2} u/s (cmd 2.2; unified clock — the walker now covers the full commanded speed)");
                        if (_bV1 < 1.2f) BFail($"baseline speed implausible: {_bV1:F2}");
                        BStep(2);
                    }
                }
                else if (_bStep == 2)
                {
                    _enemy.SetDrive(Mathf.Pi, 0f);
                    if (BStepT > 1.2)
                    {
                        CraftHit(_enemy, "leg_L", 30f);
                        CraftHit(_enemy, "leg_L", 30f);
                        CraftHit(_enemy, "leg_L", 30f);
                        if (_partsEnemy.Alive("leg_L")) BFail("atlas leg_L alive after 90 damage");
                        if (!_enemy.LegDisabled(true)) BFail("walker leg_L not disabled");
                        if (Mathf.Abs(_enemy.SpeedFactor) > 0.01f) BFail("speed factor not 0 (immobile)");
                        GD.Print("BRKTEST LEGBREAK PASS leg_L destroyed, walker IMMOBILE (speed factor 0)");
                        BStep(3);
                    }
                }
                else if (_bStep == 3)
                {
                    // Full drive command against the broken leg: the root
                    // must not translate (a small settle drift is allowed),
                    // and the good leg's planted contact must hold.
                    _enemy.SetDrive(Mathf.Pi, 2.2f);
                    if (_bCount1 == 0 && BStepT > 1.0)
                    {
                        _bVec1 = _enemy.RootPos;
                        _bT0 = _bPhaseT; // BStepT now measures the sample window
                        _bCount1 = 1;
                    }
                    else if (_bCount1 == 1 && BStepT > 2.0)
                    {
                        QueueCapture("res://proof/break_immobile.png", "blimp", "brkAtlas");
                        _bCount1 = 2;
                    }
                    else if (_bCount1 == 2 && BStepT > 3.0)
                    {
                        var d = new Vector2(_enemy.RootPos.X - _bVec1.X, _enemy.RootPos.Z - _bVec1.Z).Length();
                        float v2 = d / BStepT;
                        GD.Print($"BRKTEST IMMOBILE atlas driftSpeed={v2:F3} drift={d:F3} baseline={_bV1:F2} maxPlantedDiff={_enemy.MaxPlantedDiff():F3}");
                        if (d > 0.15f) { BFail($"immobile mech translated {d:F2}u under full drive"); return; }
                        if (_enemy.MaxPlantedDiff() > 0.08f) { BFail($"good-leg planted diff {_enemy.MaxPlantedDiff():F3} > 0.08"); return; }
                        GD.Print("BRKTEST IMMOBILE PASS leg lost -> zero translation under full drive, good-leg contact holds");
                        SetBPhase(6);
                    }
                }
                break;

            case 6: // BOTH LEGS: disabled counts as destroyed
                _player.SetDrive(0f, 0f);
                _enemy.SetDrive(Mathf.Pi, 0f);
                if (_bStep == 0)
                {
                    CraftHit(_enemy, "leg_R", 30f);
                    CraftHit(_enemy, "leg_R", 30f);
                    CraftHit(_enemy, "leg_R", 30f);
                    if (!_enemyDead) BFail("atlas not destroyed after both legs lost");
                    if (!_enemy.Halted) BFail("atlas walker not halted after both legs lost");
                    if (!_banner.Visible || _banner.Text != "ATLAS DESTROYED")
                        BFail($"both-legs banner wrong: '{_banner.Text}'");
                    GD.Print("BRKTEST BOTHLEGS PASS both legs lost -> disabled, halted, banner 'ATLAS DESTROYED'");
                    BStep(1);
                }
                else if (_bStep == 1 && BStepT > 2.0)
                {
                    QueueCapture("res://proof/break_atlas_disabled.png", "bdisabled", "brkAtlas");
                    BStep(2);
                }
                else if (_bStep == 2 && _pendingPath == null && !_capturing)
                {
                    // Reset only after the capture has actually rendered:
                    // resetting in the same frame as QueueCapture would
                    // photograph the restored mech instead of the wreck.
                    ResetDuel();
                    BSetup(new Vector3(0f, Main.HeightAt(0f, 18f), 18f), new Vector3(0f, Main.HeightAt(0f, -12f), -12f));
                    SetBPhase(7);
                }
                break;

            case 7: // HEAD (Atlas): pool rule + tracking lag; player sway
                _player.SetDrive(0f, 0f);
                _enemy.SetDrive(Mathf.Pi, 0f);
                if (_bStep == 0)
                {
                    CraftHit(_enemy, "head", 25f);
                    CraftHit(_enemy, "head", 25f);
                    if (_partsEnemy.Alive("head")) BFail("atlas head alive after 50 damage");
                    if (Mathf.Abs(_hpEnemy - 290f) > 0.01f)
                        BFail($"head->pool rule: atlasHp={_hpEnemy:F1} (want 290 = 340 - 50)");
                    GD.Print("BRKTEST HEADPOOL PASS head hits burned head HP and pool (340 -> 290)");
                    // Move the player to the Atlas's flank: desired twist ~1 rad.
                    _player.Teleport(new Vector3(20f, Main.HeightAt(20f, 2f), 2f), 0f);
                    BStep(1);
                }
                else if (_bStep == 1)
                {
                    var toP = _player.RootPos - _enemy.RootPos;
                    float yawTo = MechWalker.YawOf(toP.Normalized());
                    TrackAtlasTorso(dt, yawTo);
                    float desired = Mathf.Abs(MechWalker.WrapAngle(yawTo - _enemyYaw));
                    if (BStepT > 0.5)
                    {
                        float twist = Mathf.Abs(_enemy.TorsoTwist);
                        GD.Print($"BRKTEST HEADLAG desired={desired:F2} rad, twist after 0.5s={twist:F2} (lag cap 0.30 + eps)");
                        if (desired < 0.7f) BFail($"flank geometry wrong: desired twist {desired:F2}");
                        if (twist > 0.37f) BFail($"headless tracking did not lag: twist {twist:F2} in 0.5s");
                        BStep(2);
                    }
                }
                else if (_bStep == 2)
                {
                    var toP = _player.RootPos - _enemy.RootPos;
                    float yawTo = MechWalker.YawOf(toP.Normalized());
                    TrackAtlasTorso(dt, yawTo);
                    if (BStepT > 2.2)
                    {
                        float err = Mathf.Abs(MechWalker.WrapAngle(
                            MechWalker.WrapAngle(yawTo - _enemyYaw) - _enemy.TorsoTwist));
                        if (err > 0.08f) BFail($"headless tracking never caught up: err {err:F2}");
                        GD.Print($"BRKTEST HEADLAG PASS lag proven at 0.5s, caught up by 2.2s (err {err:F3})");
                        CraftRayHit(_player, "head", 25f);
                        CraftRayHit(_player, "head", 25f);
                        if (_partsPlayer.Alive("head")) { BFail("player head alive after 50 damage"); return; }
                        _bDragMax = 0f;
                        BStep(3);
                    }
                }
                else if (_bStep == 3)
                {
                    float s = _cross.Sway.Length();
                    if (s > _bDragMax) _bDragMax = s;
                    if (BStepT > 1.2)
                    {
                        if (_bDragMax < 3f) BFail($"crosshair sway too small: max {_bDragMax:F1}px");
                        GD.Print($"BRKTEST HEADSWAY PASS player head lost -> crosshair sway max {_bDragMax:F1}px");
                        ResetDuel();
                        BSetup(new Vector3(0f, Main.HeightAt(0f, 18f), 18f), new Vector3(0f, Main.HeightAt(0f, -12f), -12f));
                        SetBPhase(8);
                    }
                }
                break;

            case 8: // RESET: everything restored, salvo back to 8, rebind real
                _player.SetDrive(0f, 0f);
                _enemy.SetDrive(Mathf.Pi, 0f);
                if (_bStep == 0)
                {
                    foreach (var p in _partsPlayer.AllParts)
                        if (p.Destroyed) BFail($"player part {p.Id} still destroyed after reset");
                    foreach (var p in _partsEnemy.AllParts)
                        if (p.Destroyed) BFail($"atlas part {p.Id} still destroyed after reset");
                    if (_partsPlayer.DebrisList.Count != 0 || _partsEnemy.DebrisList.Count != 0)
                        BFail("debris remains after reset");
                    if (Mathf.Abs(_partsPlayer.HpOf("pod_L") - 45f) > 0.01f ||
                        Mathf.Abs(_partsEnemy.HpOf("leg_L") - 90f) > 0.01f ||
                        Mathf.Abs(_partsEnemy.HpOf("head") - 50f) > 0.01f)
                        BFail("part HP not refilled after reset");
                    if (_enemy.LegDisabled(true) || _player.LegDisabled(false) ||
                        Mathf.Abs(_enemy.SpeedFactor - 1f) > 0.01f)
                        BFail("walker leg state not cleared after reset");
                    var rec = _partsPlayer.GetPart("pod_L").Meshes[0];
                    if (rec.Detached || rec.Mi.Skin == null || !(rec.Mi.GetParent() is Skeleton3D))
                        BFail("pod_L mesh not rebound to the skeleton after reset");
                    GD.Print("BRKTEST RESTORE PASS parts alive, HP full, debris freed, meshes rebound, walker state cleared");
                    BStep(1);
                    _bCount1 = _rocketsLaunched;
                    FireLrmSalvo(AtlasAim());
                }
                else if (_bStep == 1 && BStepT > 1.6)
                {
                    int n = _rocketsLaunched - _bCount1;
                    if (n != 8) BFail($"post-reset salvo = {n} rockets (want 8)");
                    GD.Print("BRKTEST SALVO8 PASS salvo restored to 8 after reset");
                    SetBPhase(9);
                }
                break;
            case 9: // KNOCKDOWN: threshold, fall, recovery, immunity, salvo sum
                if (_bStep == 0)
                {
                    ResetDuel();
                    BSetup(new Vector3(0f, Main.HeightAt(0f, 18f), 18f), new Vector3(0f, Main.HeightAt(0f, -12f), -12f));
                    // 100 < 340/3 = 113.3: under the rule, no knockdown.
                    CraftHit(_enemy, "torso", 100f);
                    BStep(1);
                }
                else if (_bStep == 1 && BStepT > 0.6f)
                {
                    if (_enemy.IsKnockedDown) BFail("100 dmg knocked a full-HP Atlas (threshold 113)");
                    GD.Print("KNOCKTEST THRESHOLD PASS 100 dmg vs 113 threshold — no knockdown");
                    // Pool now 240; 100 > 240/3 = 80: this one topples it.
                    CraftHit(_enemy, "torso", 100f);
                    BStep(2);
                }
                else if (_bStep == 2 && BStepT > 1.0f)
                {
                    if (!_enemy.IsKnockedDown || _enemy.FallAmount < 0.75f)
                        BFail($"expected Atlas down (amt={_enemy.FallAmount:F2} down={_enemy.IsKnockedDown})");
                    GD.Print($"KNOCKTEST FALL PASS Atlas toppled (fallAmount={_enemy.FallAmount:F2})");
                    QueueCapture("res://proof/break_knockdown.png", "brkKnock", "brkAtlas");
                    BStep(3);
                }
                else if (_bStep == 3)
                {
                    if (BStepT > 9f) BFail("Atlas never got back up");
                    if (!_enemy.IsKnockedDown && BStepT > 0.5f)
                    {
                        GD.Print("KNOCKTEST RECOVERY PASS Atlas back on its feet");
                        // Inside the 3s immunity window this hit (100 >
                        // 140/3) must NOT re-knock.
                        CraftHit(_enemy, "torso", 100f);
                        BStep(4);
                    }
                }
                else if (_bStep == 4 && BStepT > 0.6f)
                {
                    if (_enemy.IsKnockedDown) BFail("immunity failed: re-knocked inside the window");
                    GD.Print("KNOCKTEST IMMUNITY PASS no chain-knockdown after recovery");
                    BStep(5);
                }
                else if (_bStep == 5 && BStepT > 3.4f)
                {
                    // Immunity over. Salvo accounting at pool 150: small
                    // torso hits set the pool without ever crossing 1/3.
                    ResetDuel();
                    BSetup(new Vector3(0f, Main.HeightAt(0f, 18f), 18f), new Vector3(0f, Main.HeightAt(0f, -12f), -12f));
                    for (int i = 0; i < 19; i++) CraftHit(_enemy, "torso", 10f);
                    if (_enemy.IsKnockedDown) BFail("small hits knocked the Atlas");
                    if (Mathf.Abs(_hpEnemy - 150f) > 0.01f) BFail($"salvo setup pool = {_hpEnemy:F1} (want 150)");
                    const int testSalvo = 777;
                    for (int i = 0; i < 7; i++) CraftHit(_enemy, "leg_L", 7f, testSalvo);
                    if (_enemy.IsKnockedDown) BFail("7 rockets (49) crossed a 50 threshold");
                    GD.Print("KNOCKTEST SALVO-HOLD PASS 7 rockets sum 49 < threshold 50");
                    CraftHit(_enemy, "leg_L", 7f, testSalvo); // sum 56 > 50
                    BStep(6);
                }
                else if (_bStep == 6 && BStepT > 0.9f)
                {
                    if (!_enemy.IsKnockedDown || _enemy.FallAmount < 0.7f)
                        BFail($"salvo knockdown missing (amt={_enemy.FallAmount:F2})");
                    GD.Print($"KNOCKTEST SALVO PASS full salvo (56 > 150/3) topples the Atlas (fallAmount={_enemy.FallAmount:F2})");
                    GD.Print("BRKTEST DONE");
                    GetTree().Quit(0);
                }
                break;
        }
    }

    // ---------- terrain-test choreography ----------

    private void TFail(string msg)
    {
        GD.PrintErr($"TERRAINTEST FAIL: {msg}");
        GetTree().Quit(1);
    }

    private void TerrainSelfCheck()
    {
        // HeightAt at every grid node must reproduce the stored grid
        // height exactly (bilinear weights are 1/0 at nodes).
        float maxDiff = 0f;
        float half = Main.TerrainSize / 2f;
        float step = (float)Main.TerrainSize / Main.TerrainSegs;
        for (int j = 0; j <= Main.TerrainSegs; j++)
        for (int i = 0; i <= Main.TerrainSegs; i++)
        {
            float d = Mathf.Abs(Main.HeightAt(-half + i * step, -half + j * step)
                              - Main.GridHeightAt(i, j));
            if (d > maxDiff) maxDiff = d;
        }
        GD.Print($"TERRAIN SELFCHECK nodes={(Main.TerrainSegs + 1) * (Main.TerrainSegs + 1)} maxNodeDiff={maxDiff:F5}");
        // Arena grade scan (the duel-playable core).
        float maxG = 0f; Vector2 atG = Vector2.Zero;
        for (float x = -36f; x <= 36f; x += 1.5f)
        for (float z = -36f; z <= 36f; z += 1.5f)
        {
            if (new Vector2(x, z).Length() > 36f) continue;
            float g = TerrainGrade(x, z);
            if (g > maxG) { maxG = g; atG = new Vector2(x, z); }
        }
        GD.Print($"TERRAIN ARENA maxGrade={maxG:F3} ({Mathf.RadToDeg(Mathf.Atan(maxG)):F1} deg) at {atG}");
        float hMin = 1e9f, hMax = -1e9f;
        for (int j = 0; j <= Main.TerrainSegs; j += 4)
        for (int i = 0; i <= Main.TerrainSegs; i += 4)
        {
            float h = Main.GridHeightAt(i, j);
            hMin = Mathf.Min(hMin, h); hMax = Mathf.Max(hMax, h);
        }
        GD.Print($"TERRAIN RANGE min={hMin:F1} max={hMax:F1}");
    }

    // The clearest steep spot the arena offers (the arena is gentle by
    // design — max grade ~0.08 — so take the best clear candidate).
    private bool FindSlopeSpot(out Vector3 spot, out float uphillYaw)
    {
        spot = Vector3.Zero; uphillYaw = 0f;
        float bestG = 0.045f;
        bool found = false;
        for (float rad = 34f; rad >= 14f; rad -= 2f)
        for (int a = 0; a < 48; a++)
        {
            float ang = a * Mathf.Tau / 48f;
            var p = new Vector2(Mathf.Cos(ang) * rad, Mathf.Sin(ang) * rad);
            float g = TerrainGrade(p.X, p.Y);
            if (g <= bestG || g > 0.35f) continue;
            if (!ScatterFree(p, 6.5f)) continue;
            const float e = 0.7f;
            float gx = (Main.HeightAt(p.X + e, p.Y) - Main.HeightAt(p.X - e, p.Y)) / (2f * e);
            float gz = (Main.HeightAt(p.X, p.Y + e) - Main.HeightAt(p.X, p.Y - e)) / (2f * e);
            uphillYaw = MechWalker.YawOf(new Vector3(gx, 0f, gz).Normalized());
            spot = new Vector3(p.X, Main.HeightAt(p.X, p.Y), p.Y);
            bestG = g;
            found = true;
        }
        return found;
    }

    private void UpdateTerrainTest(float dt)
    {
        _tPhaseT += dt;
        if (_tPhaseT > 45) { TFail($"phase {_tPhase} timeout"); return; }
        switch (_tPhase)
        {
            case 0: // vista over the whole valley, then move to the slope
                _player.SetDrive(0f, 0f);
                _enemy.SetDrive(Mathf.Pi, 0f);
                if (_tPhaseT > 0.8)
                {
                    QueueCapture("res://proof/terrain_vista.png", "tvista", "vista", () =>
                    {
                        if (!FindSlopeSpot(out var spot, out float yaw))
                        {
                            TFail("no slope spot found in the arena");
                            return;
                        }
                        _tSlopeYaw = yaw;
                        _player.Teleport(spot, yaw);
                        _enemy.Teleport(new Vector3(0f, Main.HeightAt(0f, -16f), -16f), Mathf.Pi);
                        GD.Print($"TERRAIN SLOPE spot={spot} grade={TerrainGrade(spot.X, spot.Z):F3} uphillYaw={Mathf.RadToDeg(yaw):F1}deg");
                        _tPhase = 1; _tPhaseT = 0; _tStrideLogged = false;
                    });
                    _tPhase = 99; // wait for the capture callback
                }
                break;
            case 1: // climb the slope; capture mid-stride, check contact
                _player.SetDrive(_tSlopeYaw, 2.2f);
                _enemy.SetDrive(Mathf.Pi, 0f);
                if (_tPhaseT > 0.8 && _player.ConsumeStepMidCross() && !_tStrideLogged)
                {
                    _tStrideLogged = true;
                    GD.Print($"TERRAIN STRIDE analytic {_player.FootReport(false)}");
                    GD.Print($"TERRAIN STRIDE engine   {_player.FootReport(true)}");
                    QueueCapture("res://proof/terrain_stride.png", "tstride", "slopeSide", () =>
                    {
                        float md = _player.MaxPlantedDiff();
                        GD.Print($"TERRAIN STRIDE maxPlantedDiff={md:F3}");
                        if (md > 0.08f) { TFail($"planted diff {md:F3} > 0.08 on arena slope"); return; }
                        // Ground-level duel shot: Atlas 30u ahead of the player.
                        var fwd = MechWalker.HeadingVec(_player.LegsYaw);
                        var epos = _player.RootPos + fwd * 30f;
                        epos.X = Mathf.Clamp(epos.X, -60f, 60f);
                        epos.Z = Mathf.Clamp(epos.Z, -60f, 60f);
                        epos.Y = Main.HeightAt(epos.X, epos.Z);
                        _enemy.Teleport(epos, MechWalker.YawOf(-fwd));
                        _player.SetDrive(_player.LegsYaw, 0f);
                        _tPhase = 2; _tPhaseT = 0;
                    });
                }
                break;
            case 2: // settle, then the ground-level duel frame + render info
                _player.SetDrive(_player.LegsYaw, 0f);
                _enemy.SetDrive(_enemy.LegsYaw, 0f);
                if (_tPhaseT > 1.2)
                {
                    QueueCapture("res://proof/terrain_duel.png", "tduel", "groundDuel", () =>
                    {
                        var vp = GetViewport();
                        GD.Print($"TERRAIN RENDER drawCalls={vp.GetRenderInfo(Viewport.RenderInfoType.Visible, Viewport.RenderInfo.DrawCallsInFrame)} prims={vp.GetRenderInfo(Viewport.RenderInfoType.Visible, Viewport.RenderInfo.PrimitivesInFrame)} objects={vp.GetRenderInfo(Viewport.RenderInfoType.Visible, Viewport.RenderInfo.ObjectsInFrame)}");
                        // RUN phase setup: back to the flat spawn, facing -Z.
                        _player.Teleport(new Vector3(0f, Main.HeightAt(0f, 34f), 34f), 0f);
                        _tRunFrom = _player.RootPos;
                        _tPhase = 3; _tPhaseT = 0;
                    });
                    _tPhase = 99;
                }
                break;
            case 3: // RUN: full run-mode speed (walk x1.5) — gait must hold
                _player.SetDrive(0f, PlayerMaxSpeed * RunFactor);
                _enemy.SetDrive(_enemy.LegsYaw, 0f);
                if (_tPhaseT > 3.5)
                {
                    var d = new Vector2(_player.RootPos.X - _tRunFrom.X, _player.RootPos.Z - _tRunFrom.Z).Length();
                    float avg = d / (float)_tPhaseT;
                    GD.Print($"RUNTEST avgSpeed={avg:F2} (cmd {PlayerMaxSpeed * RunFactor:F1}) dist={d:F1} maxSlide={_player.MaxSlide:F4} maxPlantedDiff={_player.MaxPlantedDiff():F3}");
                    if (avg < 5.2f) { TFail($"run avg speed {avg:F2} < 5.2"); return; }
                    if (_player.MaxPlantedDiff() > 0.08f) { TFail($"run planted diff {_player.MaxPlantedDiff():F3} > 0.08"); return; }
                    GD.Print("RUNTEST PASS run-mode speed reached, gait contact holds");
                    GD.Print("TERRAINTEST DONE");
                    GetTree().Quit(0);
                    _tPhase = 99;
                }
                break;
        }
    }

    // ---------- captures / camera ----------

    private void QueueCapture(string path, string tag, string camMode, System.Action after = null)
    {
        if (_pendingPath != null || _capturing) return;
        _pendingPath = path; _pendingTag = tag; _camMode = camMode;
        _afterCapture = after;
    }

    private async void DoCapture()
    {
        _capturing = true;
        _frozen = true;
        string path = _pendingPath, tag = _pendingTag;
        _pendingPath = null;
        UpdateCamera(0f);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var img = GetViewport().GetTexture().GetImage();
        var err = img.SavePng(path);
        GD.Print($"DUEL captured {path} ({tag}) size={img.GetSize()} err={err}");
        _frozen = false;
        _capturing = false;
        var after = _afterCapture;
        _afterCapture = null;
        after?.Invoke();
        if (_phase == 5 && tag == "mech")
        {
            GD.Print($"DUEL DONE playerSteps={_player.Steps} atlasSteps={_enemy.Steps} atlasMaxSlide={_enemy.MaxSlide:F4} twMaxSlide={_player.MaxSlide:F4}");
            // One frame from the interactive chase camera (the player's
            // actual view) before quitting — framing proof for cam changes.
            QueueCapture("res://proof/duel_chase.png", "chase", "chase");
        }
        else if (tag == "chase")
        {
            GetTree().Quit();
        }
    }

    private void UpdateCamera(float delta)
    {
        if (_camMode == "atlasSide")
        {
            var p = _enemy.RootPos;
            _cam.GlobalPosition = p + new Vector3(-17.5f, 5.5f, -3f);
            _cam.LookAt(p + new Vector3(0f, 6.8f, 0f), Vector3.Up);
        }
        else if (_camMode == "wide")
        {
            var mid = (_player.RootPos + _enemy.RootPos) / 2f;
            _cam.GlobalPosition = mid + new Vector3(-27f, 15f, -27f);
            _cam.LookAt(mid + new Vector3(0f, 5f, 0f), Vector3.Up);
        }
        else if (_camMode == "twist")
        {
            var p = _player.RootPos;
            _cam.GlobalPosition = p + new Vector3(-11f, 7f, -11f);
            _cam.LookAt(p + new Vector3(0f, 8f, 0f), Vector3.Up);
        }
        else if (_camMode == "prop")
        {
            var mid = (_player.RootPos + new Vector3(6f, 0f, 16f)) / 2f;
            _cam.GlobalPosition = mid + new Vector3(11f, 5f, 11f);
            _cam.LookAt(mid + new Vector3(0f, 3.5f, 0f), Vector3.Up);
        }
        else if (_camMode == "wpnSide" || _camMode == "wpnRockets")
        {
            var mid = (_player.RootPos + _enemy.RootPos) / 2f;
            float h = _camMode == "wpnRockets" ? 10.5f : 6.0f;
            _cam.GlobalPosition = mid + new Vector3(-26f, h, 3f);
            _cam.LookAt(mid + new Vector3(0f, 6.5f, 0f), Vector3.Up);
        }
        else if (_camMode == "wpnBlocked")
        {
            _cam.GlobalPosition = new Vector3(43f, 9.5f, 17f);
            _cam.LookAt(new Vector3(22f, 5.5f, 3f), Vector3.Up);
        }
        else if (_camMode == "wpnDead")
        {
            // From the player's side (sunlit front), pulled well back.
            var p = _enemy.RootPos;
            _cam.GlobalPosition = p + new Vector3(16f, 7f, 13f);
            _cam.LookAt(p + new Vector3(0f, 6.5f, 0f), Vector3.Up);
        }
        else if (_camMode == "wpnHud")
        {
            // Rear three-quarter: player mech in frame, Atlas visible past it.
            var torsoYaw = _player.TorsoYawWorld;
            var fwd = MechWalker.HeadingVec(torsoYaw);
            var right = new Vector3(-fwd.Z, 0f, fwd.X);
            _cam.GlobalPosition = _player.RootPos - fwd * 27f + right * 8.5f + Vector3.Up * 11f;
            _cam.LookAt(_player.RootPos + Vector3.Up * 6f + fwd * 9f, Vector3.Up);
        }
        else if (_camMode == "mechSide")
        {
            var mid = (_player.RootPos + _enemy.RootPos) / 2f;
            _cam.GlobalPosition = mid + new Vector3(-18f, 6.5f, 0f);
            _cam.LookAt(mid + new Vector3(0f, 6f, 0f), Vector3.Up);
        }
        else if (_camMode == "brkPlayer")
        {
            var p = _player.RootPos;
            _cam.GlobalPosition = p + new Vector3(-15f, 5.5f, 5f);
            _cam.LookAt(p + new Vector3(0f, 6.5f, 0f), Vector3.Up);
        }
        else if (_camMode == "brkAtlas")
        {
            var p = _enemy.RootPos;
            _cam.GlobalPosition = p + new Vector3(14.5f, 6.5f, -5f);
            _cam.LookAt(p + new Vector3(0f, 6.5f, 0f), Vector3.Up);
        }
        else if (_camMode == "brkDebris")
        {
            _cam.GlobalPosition = _camFocus + new Vector3(-8.5f, 3.4f, 6.5f);
            _cam.LookAt(_camFocus + Vector3.Up * 0.4f, Vector3.Up);
        }
        else if (_camMode == "vista")
        {
            _cam.GlobalPosition = new Vector3(-88f, 44f, 90f);
            _cam.LookAt(new Vector3(12f, 0f, -12f), Vector3.Up);
        }
        else if (_camMode == "slopeSide")
        {
            var p = _player.RootPos;
            var fwd = MechWalker.HeadingVec(_player.LegsYaw);
            var right = new Vector3(-fwd.Z, 0f, fwd.X);
            _cam.GlobalPosition = p - right * 15f + Vector3.Up * 4.2f;
            _cam.LookAt(p + Vector3.Up * 5.5f, Vector3.Up);
        }
        else if (_camMode == "groundDuel")
        {
            var p = _player.RootPos;
            var fwd = MechWalker.HeadingVec(_player.LegsYaw);
            _cam.GlobalPosition = p - fwd * 11f + Vector3.Up * 3.4f;
            _cam.LookAt(_enemy.RootPos + Vector3.Up * 6.5f, Vector3.Up);
        }
        else // chase (interactive)
        {
            var torsoYaw = _player.TorsoYawWorld;
            var back = -MechWalker.HeadingVec(torsoYaw);
            var target = _player.RootPos + back * 17.5f + Vector3.Up * 10.6f;
            if (delta > 0f)
                _cam.GlobalPosition = _cam.GlobalPosition.Lerp(target, 1f - Mathf.Exp(-6f * delta));
            else
                _cam.GlobalPosition = target;
            var look = _player.RootPos + Vector3.Up * 9.2f + MechWalker.HeadingVec(torsoYaw) * 6f;
            if (_cam.GlobalPosition.DistanceSquaredTo(look) > 0.01f)
                _cam.LookAt(look, Vector3.Up);
            if (delta > 0f && _shake > 0.002f)
            {
                _cam.GlobalPosition += new Vector3(
                    (float)_rng.NextDouble() * 2f - 1f,
                    (float)_rng.NextDouble() * 1.2f - 0.6f,
                    (float)_rng.NextDouble() * 2f - 1f) * (_shake * 0.3f);
            }
        }
    }

    // Substep the walkers so their 0.1s accumulator clamp never drops
    // simulation time on slow frames (llvmpipe renders this scene at a
    // few FPS): gait, projectiles and cooldowns then share one clock at
    // any frame rate. At 60 FPS this is a single call, unchanged.
    private static void StepWalker(MechWalker w, double delta)
    {
        int n = Mathf.Max(1, Mathf.CeilToInt((float)delta / 0.09f));
        double h = delta / n;
        for (int i = 0; i < n; i++) w.StepFrame(h);
    }

    public override void _Process(double delta)
    {
        if (_player == null || !_player.IsReady || _enemy == null || !_enemy.IsReady) return;
        float dt = (float)delta;

        if (!_frozen)
        {
            _simT += delta;
            if (_breakTest) UpdateBreakTest(dt);
            else if (_weaponsTest) UpdateWeaponsTest(dt);
            else if (_terrainTest) UpdateTerrainTest(dt);
            else if (_test) UpdateTest(dt);
            else
            {
                UpdatePlayerInput(dt);
                UpdateEnemyAI(dt);
                if (Input.MouseMode == Input.MouseModeEnum.Captured && !_playerDead && !_player.IsKnockedDown)
                {
                    // One trigger: the left mouse button fires the SELECTED
                    // weapon (1/2/3 or wheel to switch — see the panel).
                    var aim = AimPointFromCamera();
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
            ResolveCollisions();
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
        UpdateCamera(_frozen ? 0f : dt);
        if (_pendingPath != null && !_capturing) DoCapture();
    }
}
