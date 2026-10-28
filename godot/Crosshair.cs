using Godot;

// Center-screen crosshair for the duel scene (drawn, no textures),
// with a hitmarker X that the weapon code flashes on a registered hit.
// Sway (set by the duel when the player's head/sensors are destroyed)
// offsets the whole reticle from screen center.
public partial class Crosshair : Control
{
    public float Hitmark;
    public Vector2 Sway;
    // Screen position of the reticle, set by the duel every frame to
    // the projection of the torso's aim point — the reticle rides
    // where the torso actually points instead of sitting at screen
    // center. Negative X = unset (fall back to screen center).
    public Vector2 ReticlePos = new Vector2(-999f, -999f);

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Process(double delta)
    {
        if (Hitmark > 0f)
            Hitmark = Mathf.Max(Hitmark - (float)delta / 0.22f, 0f);
        // Redraw every frame: the first _Draw can run before layout
        // gives this control its size, and a reticle drawn once at a
        // zero size would otherwise sit in the corner forever.
        QueueRedraw();
    }

    public override void _Draw()
    {
        var c = (ReticlePos.X > -100f ? ReticlePos : GetViewportRect().Size * 0.5f) + Sway;
        // Owner's ask: the reticle must be unmissable — longer, thicker
        // arms in bright amber over a dark outline pass, so it reads
        // against sky, terrain and the mech's own hull alike.
        var col = new Color(1f, 0.88f, 0.25f, 1f);
        var outline = new Color(0.02f, 0.02f, 0.02f, 0.85f);
        foreach (var (colr, wdt) in new[] { (outline, 6.5f), (col, 3.2f) })
        {
            DrawLine(c + new Vector2(-26f, 0f), c + new Vector2(-8f, 0f), colr, wdt);
            DrawLine(c + new Vector2(8f, 0f), c + new Vector2(26f, 0f), colr, wdt);
            DrawLine(c + new Vector2(0f, -26f), c + new Vector2(0f, -8f), colr, wdt);
            DrawLine(c + new Vector2(0f, 8f), c + new Vector2(0f, 25f), colr, wdt);
        }
        DrawCircle(c, 4.6f, outline);
        DrawCircle(c, 2.8f, col);
        if (Hitmark > 0f)
        {
            var hm = new Color(1f, 1f, 1f, Hitmark);
            DrawLine(c + new Vector2(-22f, -22f), c + new Vector2(-12f, -12f), hm, 3.5f);
            DrawLine(c + new Vector2(22f, -22f), c + new Vector2(12f, -12f), hm, 3.5f);
            DrawLine(c + new Vector2(-22f, 22f), c + new Vector2(-12f, 12f), hm, 3.5f);
            DrawLine(c + new Vector2(22f, 22f), c + new Vector2(12f, 12f), hm, 3.5f);
        }
    }
}
