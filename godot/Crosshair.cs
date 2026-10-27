using Godot;

// Center-screen crosshair for the duel scene (drawn, no textures).
public partial class Crosshair : Control
{
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        var c = Size * 0.5f;
        var col = new Color(1f, 0.85f, 0.3f, 0.95f);
        DrawLine(c + new Vector2(-15f, 0f), c + new Vector2(-5f, 0f), col, 2f);
        DrawLine(c + new Vector2(5f, 0f), c + new Vector2(15f, 0f), col, 2f);
        DrawLine(c + new Vector2(0f, -15f), c + new Vector2(0f, -5f), col, 2f);
        DrawLine(c + new Vector2(0f, 5f), c + new Vector2(0f, 14f), col, 2f);
        DrawCircle(c, 1.6f, col);
    }
}
