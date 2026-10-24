using Godot;

public partial class CameraRig : Node3D
{
    public Node3D Target;

    public override void _Ready()
    {
        var cam = new Camera3D();
        cam.Position = new Vector3(0f, 3.2f, 7.5f);
        cam.RotationDegrees = new Vector3(-22f, 0f, 0f);
        AddChild(cam);
    }

    public override void _Process(double delta)
    {
        if (Target != null)
            GlobalPosition = Target.GlobalPosition;
    }
}
