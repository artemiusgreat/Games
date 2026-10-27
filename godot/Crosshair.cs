using Godot;

// Center-screen crosshair for the duel scene (drawn, no textures),
// with a hitmarker X that the weapon code flashes on a registered hit.
public partial class Crosshair : Control
{
    public float Hitmark;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta)
    {
        if (Hitmark > 0f)
        {
            Hitmark = Mathf.Max(Hitmark - (float)delta / 0.22f, 0f);
            QueueRedraw();
        }
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
        if (Hitmark > 0f)
        {
            var hm = new Color(1f, 1f, 1f, Hitmark);
            DrawLine(c + new Vector2(-14f, -14f), c + new Vector2(-7f, -7f), hm, 2.5f);
            DrawLine(c + new Vector2(14f, -14f), c + new Vector2(7f, -7f), hm, 2.5f);
            DrawLine(c + new Vector2(-14f, 14f), c + new Vector2(-7f, 7f), hm, 2.5f);
            DrawLine(c + new Vector2(14f, 14f), c + new Vector2(7f, 7f), hm, 2.5f);
        }
    }
}
