using Godot;

// Sniper scope overlay (owner, 2026-11-23, from his scope
// reference frame): while the sniper view is engaged, a
// semi-transparent backdrop dims the whole screen, a clear
// scope disc sits in the middle, and inside it a round
// reticle the size of the radar plot (300 px across) in the
// HUD's chrome colours, with cardinal ticks, a fine centre
// cross and a centre dot. Pure HUD - the aim stays the
// camera's axis exactly as before.
public partial class SniperScope : Control
{
    public override void _Process(double delta)
    {
        // Redraw every frame while up, like the crosshair:
        // the first _Draw can run before layout gives this
        // control its size, and a scope drawn once at zero
        // size would otherwise stay blank forever (proven by
        // two scope-less sniper captures, 2026-11-23).
        if (Visible) QueueRedraw();
    }

    public override void _Draw()
    {
        var size = Size;
        if (size.X < 10f || size.Y < 10f) return;
        var c = size * 0.5f;
        var cyan = new Color(0.45f, 0.85f, 1f, 0.9f);
        var red = new Color(1f, 0.38f, 0.28f, 0.55f);
        // Semi-transparent backdrop over everything (owner,
        // 2026-11-23: less transparent than the first cut,
        // and the two big outer circles around the reticle
        // are gone - backdrop + reticle only).
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0.02f, 0.05f, 0.10f, 0.60f));
        // Round reticle, the radar's size: 300 px across.
        float r = 150f;
        DrawArc(c, r, 0f, Mathf.Tau, 128, cyan, 2f, true);
        DrawArc(c, r * 0.70f, 0f, Mathf.Tau, 128, red, 1.25f, true);
        for (int i = 0; i < 4; i++)
        {
            float a = i * Mathf.Pi / 2f;
            var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            DrawLine(c + d * (r * 0.84f), c + d * r, cyan, 2.5f, true);          // ring ticks
            DrawLine(c + d * 16f, c + d * (r * 0.66f), new Color(0.92f, 0.96f, 1f, 0.55f), 1.25f, true); // cross arms
        }
        DrawCircle(c, 2.6f, new Color(1f, 0.85f, 0.3f, 0.95f));
    }
}
