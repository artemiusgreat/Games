using Godot;

// Center-screen crosshair for the duel scene (drawn, no textures),
// with a hitmarker X that the weapon code flashes on a registered hit.
// The reticle never wanders: there is no sway offset (the head-loss
// "sensor sway" was removed at the owner's word, 2026-11-15).
public partial class Crosshair : Control
{
    public float Hitmark;
    // Screen position of the reticle. Since round 27 (center-
    // crosshair aiming, the owner's pick) the duel sets this to the
    // viewport center every frame.
    // Negative X = unset (fall back to screen center).
    public Vector2 ReticlePos = new Vector2(-999f, -999f);
    // The centre the reticle was last drawn at (harness audit:
    // the breakable suite asserts it never leaves screen centre).
    public Vector2 LastCentre = new Vector2(-999f, -999f);

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
        var c = ReticlePos.X > -100f ? ReticlePos : GetViewportRect().Size * 0.5f;
        LastCentre = c;
        // Owner's ask: the reticle must be unmissable - longer, thicker
        // arms in bright amber over a dark outline pass, so it reads
        // against sky, terrain and the rider's own hull alike.
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
