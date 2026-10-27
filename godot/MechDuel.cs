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
    private readonly System.Collections.Generic.List<Projectile> _shots = new();
    private readonly System.Collections.Generic.List<(Node3D Node, float Life)> _fx = new();
    private float _hpPlayer = 260f, _hpEnemy = 340f;
    private bool _playerDead, _enemyDead;
    private float _cdLaser, _cdAc, _cdLrm, _cdAtlas;
    private readonly System.Random _rng = new System.Random(20261027);
    private Vector3 _prevPlayerRoot;
    private Vector3 _playerVel;
    private readonly System.Collections.Generic.List<(float Delay, bool Left, Vector3 Aim)> _lrmQueue = new();
    private readonly System.Collections.Generic.List<(MeshInstance3D Mi, float Life)> _puffs = new();
    private readonly System.Collections.Generic.Dictionary<Projectile, Vector3> _lastPuff = new();

    public override void _Ready()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg == "--duel-test") _test = true;
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

        _cam = new Camera3D { Fov = 70f };
        AddChild(_cam);
        _cam.GlobalPosition = new Vector3(-14f, 9f, 44f);
        _cam.LookAt(new Vector3(0f, 6f, 18f), Vector3.Up);

        var layer = new CanvasLayer();
        AddChild(layer);
        var cross = new Crosshair();
        layer.AddChild(cross);
        cross.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        _lastPlayerPos = _player.RootPos;
        GD.Print($"DUEL ready test={_test} props={_props.Count} circles");
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
        _fx.Add((beam, life));
        var glow = WeaponFx.MakeBeam(a, b, new Color(col.R, col.G, col.B, 0.32f), width * 3.2f);
        AddChild(glow);
        _fx.Add((glow, life));
    }

    private void SpawnFlash(Vector3 point, Color col, float radius, float life)
    {
        var f = WeaponFx.MakeFlash(point, col, radius);
        AddChild(f);
        _fx.Add((f, life));
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
            GD.Print($"WPN LASER hit={hit.Target} t={hit.T:F1} zone={hit.Zone} point={hit.Point}");
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
        GD.Print($"WPN PROJ owner={p.OwnerId} hit={hit.Target} zone={hit.Zone} t={hit.T:F1} point={hit.Point}");
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
        if (isPlayer) _hpPlayer = Mathf.Max(_hpPlayer - dmg, 0f);
        else _hpEnemy = Mathf.Max(_hpEnemy - dmg, 0f);
        float hp = isPlayer ? _hpPlayer : _hpEnemy;
        GD.Print($"WPN DMG target={(isPlayer ? "TW" : "ATLAS")} dmg={dmg:F1} zone={hit.Zone} hp={hp:F1} src={source}");
        OnDamaged(target, dmg, hit.Point, source);
        if (hp <= 0f) Die(target);
    }

    private void Die(MechWalker w)
    {
        w.Halted = true;
        w.SetDrive(w.LegsYaw, 0f);
        if (w == _player) { _playerDead = true; GD.Print("WPN DEATH TW"); }
        else { _enemyDead = true; GD.Print("WPN DEATH ATLAS"); }
        OnDeath(w);
    }

    // Presentation hooks — bodies land with the damage-feedback step
    // (flash pulse, damage numbers, hitmarker, shake, droop, HUD).
    private void OnDamaged(MechWalker target, float dmg, Vector3 point, string source) { }
    private void OnDeath(MechWalker w) { }

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
            float life = _fx[i].Life - dt;
            if (life <= 0f) { _fx[i].Node.QueueFree(); _fx.RemoveAt(i); }
            else _fx[i] = (_fx[i].Node, life);
        }
        if (!_test) UpdateAtlasGunnery(dt);
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
        }
    }

    public override void _Process(double delta)
    {
        if (_player == null || !_player.IsReady || _enemy == null || !_enemy.IsReady) return;
        float dt = (float)delta;

        if (!_frozen)
        {
            _simT += delta;
            if (_test) UpdateTest(dt);
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
                _playerVel = _playerVel.Lerp(v, 0.5f);
                _prevPlayerRoot = _player.RootPos;
            }
            UpdateCombat(dt);
        }
        UpdateCamera(_frozen ? 0f : dt);
        if (_pendingPath != null && !_capturing) DoCapture();
    }
}
