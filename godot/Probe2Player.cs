using Godot;
using System.Collections.Generic;

// Probe 2 (Round 2, Phase B): physics player for the gameplay loop.
// CharacterBody3D capsule + REAL gravity / MoveAndSlide against the
// HeightMapShape3D terrain and building StaticBody3Ds. Owns the third-person
// camera (added as a sibling) and the shooting: a physics raycast from the
// camera through screen center. In --probe2-test it runs a scripted sequence:
//   Phase 1 BUILDINGTEST: sprint +X into building 2's collider, assert stop.
//   Phase 2 HUNT: teleport to the round-1 start, walk to the nearest enemy,
//   stop in range, fire until it dies, repeat until all 3 are gone / timeout.
public partial class Probe2Player : CharacterBody3D
{
    [Signal] public delegate void HitEventHandler(int hits, int total);
    [Signal] public delegate void EnemyDiedEventHandler(Vector3 at);

    private const float WalkSpeed = 5f;
    private const float RunSpeed = 9.5f;
    private const float HuntSpeed = 7f;
    private const float ShootRange = 24f;
    private const double ShootCooldown = 0.45;
    private const double HuntTimeout = 55.0;

    public List<Enemy> Enemies = new List<Enemy>();
    public int TotalEnemies = 3;
    public int Hits;
    public bool AutoMode;
    public bool HuntStarted;
    public bool Finished;
    public bool TimedOut;
    public bool BuildingTestPassed;

    private enum Phase { BuildingTest, Hunt, Done }
    private Phase _phase = Phase.Hunt;
    private Camera3D _cam;
    private int _frames;
    private int _phaseFrames;
    private Vector3 _buildingTestStart;
    private double _huntT;
    private double _shootCd;
    private Enemy _target;
    private readonly List<(Node3D Node, double Ttl)> _fx = new List<(Node3D, double)>();

    public Enemy CurrentTarget => _target;

    public override void _Ready()
    {
        FloorSnapLength = 0.4f;

        var col = new CollisionShape3D();
        col.Shape = new CapsuleShape3D { Radius = 0.4f, Height = 1.8f };
        AddChild(col);

        var vis = new MeshInstance3D();
        vis.Mesh = new CapsuleMesh { Radius = 0.4f, Height = 1.8f };
        vis.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.15f, 0.35f, 0.9f) };
        AddChild(vis);

        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg == "--probe2-test") AutoMode = true;
        if (AutoMode) _phase = Phase.BuildingTest;

        // Third-person camera as a sibling (position-only follow, like the
        // round-1 CameraRig) so player yaw never twists the aim.
        _cam = new Camera3D { Far = 500f };
        GetParent().AddChild(_cam);
    }

    public override void _PhysicsProcess(double delta)
    {
        _frames++;
        var dir = Vector3.Zero;
        float speed = WalkSpeed;

        if (AutoMode && _phase == Phase.BuildingTest)
        {
            if (_phaseFrames == 0) _buildingTestStart = GlobalPosition;
            dir = new Vector3(1f, 0f, 0f); // sprint straight at building 2 (x 19..25)
            speed = RunSpeed;
            if (++_phaseFrames == 72) // 1.2 s: unobstructed would reach x ~= 25.4
            {
                BuildingTestPassed = GlobalPosition.X < 18.85f && IsOnFloor();
                GD.Print($"PROBE2 BUILDINGTEST start={_buildingTestStart} end={GlobalPosition} endX={GlobalPosition.X:F2} faceX=19.00 onFloor={IsOnFloor()} result={(BuildingTestPassed ? "PASS" : "FAIL")}");
                GlobalPosition = new Vector3(0f, Main.HeightAt(0f, 8f) + 1.5f, 8f);
                Velocity = Vector3.Zero;
                _phase = Phase.Hunt;
                _phaseFrames = 0;
                _shootCd = 1.2; // let the early proof frame capture Hits: 0/3 first
                HuntStarted = true;
            }
        }
        else if (AutoMode && _phase == Phase.Hunt)
        {
            _huntT += delta;
            _target = NearestEnemy();
            if (_target == null)
            {
                _phase = Phase.Done;
                Finished = true;
                GD.Print($"PROBE2 HUNT complete t={_huntT:F1}s hits={Hits}/{TotalEnemies}");
            }
            else if (_huntT > HuntTimeout)
            {
                _phase = Phase.Done;
                Finished = true;
                TimedOut = true;
                GD.Print($"PROBE2 HUNT TIMEOUT t={_huntT:F1}s hits={Hits}/{TotalEnemies}");
            }
            else
            {
                var to = _target.GlobalPosition - GlobalPosition;
                to.Y = 0f;
                float dist = to.Length();
                var flat = dist > 0.01f ? to / dist : Vector3.Zero;
                if (dist > ShootRange)
                {
                    dir = flat;
                    speed = HuntSpeed;
                }
                else
                {
                    Rotation = new Vector3(0f, Mathf.LerpAngle(Rotation.Y, Mathf.Atan2(flat.X, flat.Z), 12f * (float)delta), 0f);
                    _shootCd -= delta;
                    if (_shootCd <= 0.0)
                    {
                        _shootCd = ShootCooldown;
                        TryShootAt(_target);
                    }
                }
                if (_frames % 30 == 0) LogState();
            }
        }
        else if (!AutoMode)
        {
            // Manual play: WASD + Shift run (round-1 scheme), Space / LMB shoots.
            var input = Vector2.Zero;
            if (Input.IsKeyPressed(Key.W)) input.Y -= 1f;
            if (Input.IsKeyPressed(Key.S)) input.Y += 1f;
            if (Input.IsKeyPressed(Key.A)) input.X -= 1f;
            if (Input.IsKeyPressed(Key.D)) input.X += 1f;
            _target = NearestEnemy();
            if (_cam != null)
            {
                var fwd = -_cam.GlobalTransform.Basis.Z; fwd.Y = 0f; fwd = fwd.Normalized();
                var right = _cam.GlobalTransform.Basis.X; right.Y = 0f; right = right.Normalized();
                dir = right * input.X + fwd * -input.Y;
                if (dir.LengthSquared() > 1f) dir = dir.Normalized();
            }
            speed = Input.IsKeyPressed(Key.Shift) ? RunSpeed : WalkSpeed;
            _shootCd -= delta;
            if (_shootCd <= 0.0 && (Input.IsKeyPressed(Key.Space) || Input.IsMouseButtonPressed(MouseButton.Left)))
            {
                _shootCd = 0.25;
                ShootCenter();
            }
        }

        var v = Velocity;
        if (!IsOnFloor())
            v += GetGravity() * (float)delta;
        else if (v.Y < 0f)
            v.Y = -1f;
        v.X = dir.X * speed;
        v.Z = dir.Z * speed;
        Velocity = v;
        MoveAndSlide();

        if (!AutoMode && dir.LengthSquared() > 0.001f)
            Rotation = new Vector3(0f, Mathf.LerpAngle(Rotation.Y, Mathf.Atan2(dir.X, dir.Z), 10f * (float)delta), 0f);
        else if (AutoMode && _phase == Phase.Hunt && dir.LengthSquared() > 0.001f)
            Rotation = new Vector3(0f, Mathf.LerpAngle(Rotation.Y, Mathf.Atan2(dir.X, dir.Z), 10f * (float)delta), 0f);
    }

    public override void _Process(double delta)
    {
        // Tracer lifetimes.
        for (int i = _fx.Count - 1; i >= 0; i--)
        {
            var (node, ttl) = _fx[i];
            ttl -= delta;
            if (ttl <= 0.0) { node.QueueFree(); _fx.RemoveAt(i); }
            else _fx[i] = (node, ttl);
        }

        if (_cam == null) return;
        if (AutoMode && _phase == Phase.Hunt && _target != null && IsInstanceValid(_target))
        {
            // Aim the camera at the target's chest from behind the player, so
            // the screen-center ray passes through the target.
            var chest = _target.GlobalPosition + new Vector3(0f, 0.3f, 0f);
            var back = chest - GlobalPosition; back.Y = 0f;
            back = back.LengthSquared() > 0.01f ? back.Normalized() : new Vector3(0f, 0f, 1f);
            _cam.GlobalPosition = GlobalPosition - back * 7.5f + new Vector3(0f, 3.0f, 0f);
            _cam.LookAt(chest, Vector3.Up);
        }
        else
        {
            _cam.GlobalPosition = GlobalPosition + new Vector3(0f, 3.2f, 7.5f);
            _cam.GlobalRotation = new Vector3(Mathf.DegToRad(-22f), 0f, 0f);
        }
    }

    private Enemy NearestEnemy()
    {
        Enemy best = null;
        float bestD = float.MaxValue;
        foreach (var e in Enemies)
        {
            if (e == null || !IsInstanceValid(e) || !e.Alive) continue;
            float d = GlobalPosition.DistanceTo(e.GlobalPosition);
            if (d < bestD) { bestD = d; best = e; }
        }
        return best;
    }

    private void LogState()
    {
        var parts = new List<string>();
        foreach (var e in Enemies)
        {
            if (e == null || !IsInstanceValid(e)) { parts.Add("dead"); continue; }
            parts.Add(GlobalPosition.DistanceTo(e.GlobalPosition).ToString("F1"));
        }
        GD.Print($"PROBE2 t={_huntT:F1} playerPos={GlobalPosition} onFloor={IsOnFloor()} enemyDists=[{string.Join(", ", parts)}] hits={Hits}/{TotalEnemies}");
    }

    // Raycast from the camera through screen center at the given enemy.
    private void TryShootAt(Enemy e)
    {
        if (_cam == null) return;
        var chest = e.GlobalPosition + new Vector3(0f, 0.3f, 0f);
        var from = _cam.GlobalPosition;
        var rayDir = (chest - from).Normalized();
        var hit = CastRay(from, rayDir);
        ResolveShot(hit, e);
    }

    private void ShootCenter()
    {
        if (_cam == null) return;
        var from = _cam.GlobalPosition;
        var rayDir = -_cam.GlobalTransform.Basis.Z.Normalized();
        ResolveShot(CastRay(from, rayDir), null);
    }

    private Godot.Collections.Dictionary CastRay(Vector3 from, Vector3 dir)
    {
        var query = PhysicsRayQueryParameters3D.Create(from, from + dir * 150f, 0xFFFFFFFF,
            new Godot.Collections.Array<Rid> { GetRid() });
        query.CollideWithBodies = true;
        query.CollideWithAreas = false;
        return GetWorld3D().DirectSpaceState.IntersectRay(query);
    }

    private void ResolveShot(Godot.Collections.Dictionary hit, Enemy intended)
    {
        if (hit.Count == 0)
        {
            GD.Print("PROBE2 SHOT miss (no hit)");
            return;
        }
        var hitPos = (Vector3)hit["position"];
        var collider = hit["collider"].AsGodotObject() as Node;
        SpawnTracer(_cam.GlobalPosition + new Vector3(0f, -0.4f, 0f), hitPos);
        if (collider is Enemy enemy && enemy.Alive)
        {
            float dist = GlobalPosition.DistanceTo(enemy.GlobalPosition);
            enemy.Die();
            Enemies.Remove(enemy);
            Hits++;
            GD.Print($"PROBE2 HIT enemy at {enemy.GlobalPosition} dist={dist:F1} hits={Hits}/{TotalEnemies}");
            EmitSignal(SignalName.EnemyDied, enemy.GlobalPosition);
            EmitSignal(SignalName.Hit, Hits, TotalEnemies);
            enemy.QueueFree();
            _target = null;
        }
        else
        {
            GD.Print($"PROBE2 SHOT blocked by {(collider != null ? collider.Name.ToString() : "?")} at {hitPos} (intended {(intended != null ? "enemy" : "center")})");
        }
    }

    private void SpawnTracer(Vector3 from, Vector3 to)
    {
        var len = from.DistanceTo(to);
        if (len < 0.1f) return;
        var root = new Node3D();
        var cyl = new MeshInstance3D();
        cyl.Mesh = new CylinderMesh { TopRadius = 0.03f, BottomRadius = 0.03f, Height = len };
        cyl.MaterialOverride = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(1.0f, 0.9f, 0.25f)
        };
        cyl.RotationDegrees = new Vector3(90f, 0f, 0f); // cylinder Y-axis -> node -Z
        root.AddChild(cyl);
        GetParent().AddChild(root);
        root.GlobalPosition = (from + to) * 0.5f;
        root.LookAt(to, Vector3.Up);
        _fx.Add((root, 0.09));
    }
}
