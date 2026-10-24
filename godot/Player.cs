using Godot;

public partial class Player : CharacterBody3D
{
    private const float WalkSpeed = 5f;
    private const float RunSpeed = 9.5f;

    private bool _testMode;
    private int _testFrames;
    private Vector3 _startPos;
    private bool _started;

    public override void _Ready()
    {
        FloorSnapLength = 0.4f;

        var capsule = new CollisionShape3D();
        capsule.Shape = new CapsuleShape3D { Radius = 0.4f, Height = 1.8f };
        AddChild(capsule);

        var visual = new MeshInstance3D();
        visual.Mesh = new CapsuleMesh { Radius = 0.4f, Height = 1.8f };
        visual.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.15f, 0.35f, 0.9f) };
        AddChild(visual);

        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg == "--walk-test") _testMode = true;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_started) { _startPos = GlobalPosition; _started = true; }

        // Input: WASD (Shift = run). In test mode, hold "W" automatically.
        var input = Vector2.Zero;
        if (_testMode)
        {
            input = new Vector2(0f, -1f);
        }
        else
        {
            if (Input.IsKeyPressed(Key.W)) input.Y -= 1f;
            if (Input.IsKeyPressed(Key.S)) input.Y += 1f;
            if (Input.IsKeyPressed(Key.A)) input.X -= 1f;
            if (Input.IsKeyPressed(Key.D)) input.X += 1f;
        }
        bool running = _testMode || Input.IsKeyPressed(Key.Shift);
        float speed = running ? RunSpeed : WalkSpeed;

        // Camera-relative movement direction (flattened to XZ).
        var camBasis = GetViewport().GetCamera3D().GlobalTransform.Basis;
        var fwd = -camBasis.Z; fwd.Y = 0f; fwd = fwd.Normalized();
        var right = camBasis.X; right.Y = 0f; right = right.Normalized();
        var dir = (right * input.X + fwd * -input.Y);
        if (dir.LengthSquared() > 1f) dir = dir.Normalized();

        var v = Velocity;
        if (!IsOnFloor())
            v += GetGravity() * (float)delta;
        else if (v.Y < 0f)
            v.Y = -1f;
        v.X = dir.X * speed;
        v.Z = dir.Z * speed;
        Velocity = v;
        MoveAndSlide();

        // Face the movement direction.
        if (dir.LengthSquared() > 0.001f)
        {
            float target = Mathf.Atan2(dir.X, dir.Z);
            Rotation = new Vector3(0f, Mathf.LerpAngle(Rotation.Y, target, 10f * (float)delta), 0f);
        }

        if (_testMode && ++_testFrames == 180)
        {
            GD.Print($"SMOKE start={_startPos} end={GlobalPosition} moved={GlobalPosition.DistanceTo(_startPos):F2} onFloor={IsOnFloor()}");
            GetTree().Quit();
        }
    }
}
