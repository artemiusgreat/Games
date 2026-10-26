using Godot;

// Probe 2 (Round 2, Phase B): the ONE enemy definition. Shipped as a
// PackedScene (Enemy.tscn, root = this script); GameProbe2 instantiates it
// x3. Movement is PHYSICS-DRIVEN: a CharacterBody3D with gravity +
// MoveAndSlide against the HeightMapShape3D terrain collider and the
// building StaticBody3Ds (no height sampling). Steers toward Target on XZ,
// stops at Standoff, stays grounded via floor snap.
public partial class Enemy : CharacterBody3D
{
    public Node3D Target;
    public float Speed = 3.5f;
    public float Standoff = 4.0f;
    public bool Alive = true;

    public override void _Ready()
    {
        FloorSnapLength = 0.4f;

        var col = new CollisionShape3D();
        col.Shape = new CapsuleShape3D { Radius = 0.4f, Height = 1.8f };
        AddChild(col);

        var vis = new MeshInstance3D();
        vis.Mesh = new CapsuleMesh { Radius = 0.4f, Height = 1.8f };
        vis.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(1.0f, 0.45f, 0.08f) };
        AddChild(vis);

        // Dark visor band so facing reads in proof frames.
        var visor = new MeshInstance3D();
        visor.Mesh = new BoxMesh { Size = new Vector3(0.5f, 0.18f, 0.1f) };
        visor.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.08f, 0.08f, 0.1f) };
        visor.Position = new Vector3(0f, 0.35f, -0.36f);
        AddChild(visor);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Alive || Target == null) return;

        var v = Velocity;
        if (!IsOnFloor())
            v += GetGravity() * (float)delta;
        else if (v.Y < 0f)
            v.Y = -1f;

        var to = Target.GlobalPosition - GlobalPosition;
        to.Y = 0f;
        float dist = to.Length();
        if (dist > Standoff)
        {
            var dir = to / dist;
            v.X = dir.X * Speed;
            v.Z = dir.Z * Speed;
            Rotation = new Vector3(0f, Mathf.LerpAngle(Rotation.Y, Mathf.Atan2(dir.X, dir.Z), 10f * (float)delta), 0f);
        }
        else
        {
            v.X = 0f;
            v.Z = 0f;
            // Still face the player while holding at standoff.
            if (dist > 0.01f)
            {
                var dir = to / dist;
                Rotation = new Vector3(0f, Mathf.LerpAngle(Rotation.Y, Mathf.Atan2(dir.X, dir.Z), 10f * (float)delta), 0f);
            }
        }
        Velocity = v;
        MoveAndSlide();
    }

    // Called by the shooter's raycast resolution. Returns true the first time.
    public bool Die()
    {
        if (!Alive) return false;
        Alive = false;
        return true;
    }
}
