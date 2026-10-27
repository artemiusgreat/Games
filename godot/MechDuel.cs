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
    private const float TurnRate = 0.85f;
    private const float WorldBound = 47f;

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
    private readonly System.Random _rng = new System.Random(20261027);
    private Vector3 _prevPlayerRoot;
    private Vector3 _playerVel;
    private readonly System.Collections.Generic.List<(float Delay, bool Left, Vector3 Aim)> _lrmQueue = new();
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

    private Crosshair _cross;
    private ProgressBar _barPlayer, _barEnemy, _cdBarLaser, _cdBarAc, _cdBarLrm;
    private Label _lblPlayerHp, _lblEnemyHp, _banner, _bannerSub;
    private float _shake;
    private float _playerDroopT, _enemyDroopT;

    public override void _Ready()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg == "--duel-test") _test = true;
            if (arg == "--weapons-test") _weaponsTest = true;
        }
        _hit = new HitModel();

        var sun = new DirectionalLight3D { RotationDegrees = new Vector3(-50f, -30f, 0f) };
        AddChild(sun);
        AddChild(new WorldEnvironment
        {
            Environment = new Environment
            {
                BackgroundMode = Environment.BGMode.Color,
                BackgroundColor = new Color(0.35f, 0.5f, 0.7f)
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
        GD.Print($"DUEL ready test={_test} weaponsTest={_weaponsTest} props={_props.Count} circles");
        if (_weaponsTest)
        {
            WSetup(new Vector3(0f, Main.HeightAt(0f, 18f), 18f), new Vector3(0f, Main.HeightAt(0f, -12f), -12f));
            SetWPhase(0);
        }
    }

    // ---------- world ----------

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
        _props.Add(new Circle { P = new Vector2(pos.X, pos.Z), R = 0.7f });
        _hit.Props.Add(new HitModel.PropCyl { P = new Vector2(pos.X, pos.Z), R = 0.7f, BaseY = pos.Y, Height = 5.6f });
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
        if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            if (Input.MouseMode != Input.MouseModeEnum.Captured)
                Input.MouseMode = Input.MouseModeEnum.Captured;
        }
        else if (e is InputEventKey k && k.Pressed && k.Keycode == Key.Escape)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
        else if (e is InputEventMouseMotion mm && Input.MouseMode == Input.MouseModeEnum.Captured && !_test)
        {
            _player.TorsoTwist -= mm.Relative.X * 0.0030f;
            _player.TorsoPitch -= mm.Relative.Y * 0.0025f;
        }
    }

    private void UpdatePlayerInput(float delta)
    {
        if (Input.IsPhysicalKeyPressed(Key.A)) _playerYaw += TurnRate * delta;
        if (Input.IsPhysicalKeyPressed(Key.D)) _playerYaw -= TurnRate * delta;
        float spd = 0f;
        if (Input.IsPhysicalKeyPressed(Key.W)) spd = PlayerMaxSpeed;
        else if (Input.IsPhysicalKeyPressed(Key.S)) spd = -PlayerMaxSpeed * 0.55f;
        _player.SetDrive(_playerYaw, spd);
    }

    private void UpdateEnemyAI(float delta)
    {
        var toP = _player.RootPos - _enemy.RootPos;
        float dist = new Vector2(toP.X, toP.Z).Length();
        float yawTo = MechWalker.YawOf(toP.Normalized());
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
        // Torso tracks the player.
        _enemy.TorsoTwist = MechWalker.WrapAngle(yawTo - _enemyYaw);
        _enemy.TorsoPitch = 0f;
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

    private void FireLaser(Vector3 aimPoint)
    {
        if (_cdLaser > 0f || _playerDead) return;
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
        if (_cdAc > 0f || _playerDead) return;
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
    // 3.0u splash falloff on impact.
    private void FireLrmSalvo(Vector3 aimPoint)
    {
        if (_cdLrm > 0f || _playerDead) return;
        _cdLrm = 3.5f;
        for (int i = 0; i < 8; i++)
            _lrmQueue.Add((i * 0.07f, i % 2 == 0, aimPoint));
        GD.Print("WPN LRM salvo queued (8 rockets)");
    }

    private Vector3 PodMuzzle(bool left)
    {
        var torso = _player.BoneWorldPos("torso");
        var fwd = MechWalker.HeadingVec(_player.TorsoYawWorld);
        var right = new Vector3(-fwd.Z, 0f, fwd.X);
        return torso + right * (left ? -2.3f : 2.3f) + Vector3.Up * 3.6f + fwd * 0.4f;
    }

    private void LaunchRocket(Vector3 aimPoint, bool left)
    {
        var muzzle = PodMuzzle(left);
        var toAim = aimPoint - muzzle;
        float dist = Mathf.Max(toAim.Length(), 1f);
        var dir = ApplyInaccuracy(toAim.Normalized(), 1.1f);
        const float speed = 36f, grav = 11f;
        float tof = dist / speed;
        var vel = dir * speed + Vector3.Up * (0.5f * grav * tof);
        SpawnProjectile(muzzle, vel, grav, 7f, 3.0f, "TW", rocket: true);
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
        if (direct != null) ApplyDamage(direct, p.Damage, hit, p.OwnerId);
        foreach (var (w, id) in new[] { (_player, "TW"), (_enemy, "ATLAS") })
        {
            if (w == direct) continue;
            float d = _hit.DistanceToMechSurface(hit.Point, id);
            if (d < p.SplashRadius)
            {
                var splashHit = hit;
                splashHit.Target = id;
                splashHit.Zone = "splash";
                ApplyDamage(w, p.Damage * (1f - d / p.SplashRadius), splashHit, p.OwnerId);
            }
        }
    }

    private Projectile SpawnProjectile(Vector3 pos, Vector3 vel, float gravity, float dmg,
        float splash, string owner, bool rocket)
    {
        var p = new Projectile
        {
            Vel = vel,
            Gravity = gravity,
            Damage = dmg,
            SplashRadius = splash,
            OwnerId = owner
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
        if (hit.Target == "TW") ApplyDamage(_player, p.Damage, hit, p.OwnerId);
        else if (hit.Target == "ATLAS") ApplyDamage(_enemy, p.Damage, hit, p.OwnerId);
    }

    private void ApplyDamage(MechWalker target, float dmg, HitModel.Result hit, string source)
    {
        bool isPlayer = target == _player;
        if (isPlayer ? _playerDead : _enemyDead) return;
        var parts = isPlayer ? _partsPlayer : _partsEnemy;
        // Local damage routing: the hit point attributes to the nearest
        // part volume. Torso hits burn the main pool; head hits burn the
        // head part AND the pool; every other part absorbs its own hits.
        // Counters still record the nominal damage once per hit event.
        string partId = parts.AttributePoint(hit.Point);
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
        if (hp <= 0f) Die(target);
    }

    // A part just broke off (fired from MechParts.DamagePart): spark burst
    // at the separation point; functional consequences live in
    // ApplyPartConsequences.
    private void OnPartBroken(MechParts parts, string partId, Vector3 center)
    {
        SpawnFlash(center, new Color(1f, 0.62f, 0.22f), 0.95f, 0.16f);
        SpawnFlash(center, new Color(1f, 0.88f, 0.55f), 0.45f, 0.1f);
        ApplyPartConsequences(parts, partId);
    }

    // Functional fallout of losing a part (weapons, gait, sensors).
    // Filled in by the consequences step.
    private void ApplyPartConsequences(MechParts parts, string partId)
    {
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
        _cdLaser = _cdAc = _cdLrm = _cdAtlas = 0f;
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
        // Player HP, bottom-left.
        _lblPlayerHp = MakeLabel(15, new Color(0.85f, 0.95f, 0.85f));
        _lblPlayerHp.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        _lblPlayerHp.OffsetLeft = 18; _lblPlayerHp.OffsetTop = -90; _lblPlayerHp.OffsetRight = 320; _lblPlayerHp.OffsetBottom = -68;
        layer.AddChild(_lblPlayerHp);
        _barPlayer = MakeBar(new Color(0.3f, 0.85f, 0.32f), new Vector2(280, 22));
        _barPlayer.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        _barPlayer.OffsetLeft = 18; _barPlayer.OffsetTop = -64; _barPlayer.OffsetRight = 298; _barPlayer.OffsetBottom = -40;
        layer.AddChild(_barPlayer);

        // Atlas HP, top-center (hidden once it dies; the banner takes over).
        _lblEnemyHp = MakeLabel(15, new Color(0.98f, 0.85f, 0.8f));
        _lblEnemyHp.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _lblEnemyHp.OffsetLeft = -150; _lblEnemyHp.OffsetTop = 14; _lblEnemyHp.OffsetRight = 150; _lblEnemyHp.OffsetBottom = 36;
        _lblEnemyHp.HorizontalAlignment = HorizontalAlignment.Center;
        layer.AddChild(_lblEnemyHp);
        _barEnemy = MakeBar(new Color(0.88f, 0.3f, 0.24f), new Vector2(300, 20));
        _barEnemy.SetAnchorsPreset(Control.LayoutPreset.CenterTop);
        _barEnemy.OffsetLeft = -150; _barEnemy.OffsetTop = 38; _barEnemy.OffsetRight = 150; _barEnemy.OffsetBottom = 58;
        layer.AddChild(_barEnemy);

        // Weapon cooldowns, just right/below the crosshair.
        var vbox = new VBoxContainer();
        vbox.SetAnchorsPreset(Control.LayoutPreset.Center);
        vbox.OffsetLeft = 34; vbox.OffsetTop = 30; vbox.OffsetRight = 220; vbox.OffsetBottom = 118;
        vbox.AddThemeConstantOverride("separation", 5);
        layer.AddChild(vbox);
        (Label, ProgressBar) CdRow(string name)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            var lab = MakeLabel(13, new Color(0.9f, 0.9f, 0.9f, 0.9f));
            lab.Text = name;
            lab.CustomMinimumSize = new Vector2(42, 0);
            row.AddChild(lab);
            var bar = MakeBar(new Color(0.95f, 0.8f, 0.3f), new Vector2(120, 11));
            bar.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(bar);
            vbox.AddChild(row);
            return (lab, bar);
        }
        _cdBarLaser = CdRow("LAS").Item2;
        _cdBarAc = CdRow("AC").Item2;
        _cdBarLrm = CdRow("LRM").Item2;

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

    private void UpdateHud()
    {
        if (_barPlayer == null) return;
        _barPlayer.Value = _hpPlayer / 260f;
        _lblPlayerHp.Text = $"TIMBER WOLF — {_hpPlayer:F0} HP";
        _barEnemy.Visible = !_enemyDead;
        _lblEnemyHp.Visible = !_enemyDead;
        _barEnemy.Value = _hpEnemy / 340f;
        _lblEnemyHp.Text = $"ATLAS — {_hpEnemy:F0} HP";
        _cdBarLaser.Value = 1f - _cdLaser / 0.45f;
        _cdBarAc.Value = 1f - _cdAc / 0.85f;
        _cdBarLrm.Value = 1f - _cdLrm / 3.5f;
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
        if (_cdAtlas > 0f) return;
        var muzzle = AtlasMuzzle();
        var target = _player.RootPos + Vector3.Up * 7.6f;
        float dist = muzzle.DistanceTo(target);
        if (dist > 75f) return;
        var los = _hit.Ray(muzzle, (target - muzzle).Normalized(), dist, "ATLAS");
        if (los.Hit && los.Target != "TW") return; // blocked: building/terrain
        float tof = dist / 50f;
        var aim = target + _playerVel * tof;
        var dir = ApplyInaccuracy((aim - muzzle).Normalized(), 1.2f);
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
        for (int i = _lrmQueue.Count - 1; i >= 0; i--)
        {
            var q = _lrmQueue[i];
            q.Delay -= dt;
            if (q.Delay <= 0f) { _lrmQueue.RemoveAt(i); LaunchRocket(q.Aim, q.Left); }
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
        if (!_test && (!_weaponsTest || _atlasMayFire)) UpdateAtlasGunnery(dt);
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

            case 2: // autocannon x2: 2 x 26 = 52 exactly. DELTA vs the
                    // pre-breakable expectation (pool 252): the shells drop
                    // ~0.6u in flight and land on the pelvis/thigh junction;
                    // nearest-volume attribution assigns them to leg_L, so
                    // the part absorbs 52 (90 -> 38) and the pool only lost
                    // the laser's 36. Both numbers are asserted.
                if (_wShotsFired == 0 && _wPhaseT > 0.1) { FireAutocannon(AtlasAim()); _wShotsFired++; }
                else if (_wShotsFired == 1 && _wPhaseT > 1.05) { FireAutocannon(AtlasAim()); _wShotsFired++; }
                else if (_wShotsFired == 2 && _wPhaseT > 2.8)
                {
                    if (_atlasDmgEvents != 2 || Mathf.Abs(_atlasDmgTotal - 52f) > 0.01f)
                        WFail($"ac events={_atlasDmgEvents} dmg={_atlasDmgTotal:F1} (want 2 / 52)");
                    if (Mathf.Abs(_hpEnemy - 304f) > 0.01f) WFail($"ac atlasHp={_hpEnemy:F1} (want 304 pool under part routing)");
                    if (Mathf.Abs(_partsEnemy.HpOf("leg_L") - 38f) > 0.01f)
                        WFail($"ac legL partHp={_partsEnemy.HpOf("leg_L"):F1} (want 38 = 90 - 52)");
                    GD.Print($"WPNTEST AC PASS events=2 dmg=52 atlasHp={_hpEnemy:F0} legL partHp={_partsEnemy.HpOf("leg_L"):F0} (shells routed to leg_L)");
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
                    WSetup(new Vector3(0f, Main.HeightAt(0f, 20f), 20f), new Vector3(0f, Main.HeightAt(0f, -10f), -10f));
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
        else // chase (interactive)
        {
            var torsoYaw = _player.TorsoYawWorld;
            var back = -MechWalker.HeadingVec(torsoYaw);
            var target = _player.RootPos + back * 16.5f + Vector3.Up * 7.2f;
            if (delta > 0f)
                _cam.GlobalPosition = _cam.GlobalPosition.Lerp(target, 1f - Mathf.Exp(-6f * delta));
            else
                _cam.GlobalPosition = target;
            var look = _player.RootPos + Vector3.Up * 8.2f + MechWalker.HeadingVec(torsoYaw) * 5f;
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

    public override void _Process(double delta)
    {
        if (_player == null || !_player.IsReady || _enemy == null || !_enemy.IsReady) return;
        float dt = (float)delta;

        if (!_frozen)
        {
            _simT += delta;
            if (_weaponsTest) UpdateWeaponsTest(dt);
            else if (_test) UpdateTest(dt);
            else
            {
                UpdatePlayerInput(dt);
                UpdateEnemyAI(dt);
                if (Input.MouseMode == Input.MouseModeEnum.Captured && !_playerDead)
                {
                    var aim = AimPointFromCamera();
                    if (Input.IsMouseButtonPressed(MouseButton.Left)) FireLaser(aim);
                    if (Input.IsMouseButtonPressed(MouseButton.Right)) FireAutocannon(aim);
                    if (Input.IsPhysicalKeyPressed(Key.Space)) FireLrmSalvo(aim);
                }
            }
            _player.StepFrame(delta);
            _enemy.StepFrame(delta);
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
        UpdateCamera(_frozen ? 0f : dt);
        if (_pendingPath != null && !_capturing) DoCapture();
    }
}
