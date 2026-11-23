using Godot;

// Sniper scope overlay (owner, 2026-11-23, from his scope
// reference frame): while the sniper view is engaged, a
// semi-transparent backdrop dims the screen OUTSIDE a round
// reticle the size of the radar plot (300 px across) in the
// HUD's chrome colours, with cardinal ticks, a fine centre
// cross and a centre dot. Inside the reticle the view stays
// completely clear. Pure HUD - the aim stays the camera's
// axis exactly as before.
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
        // Reticle lines more transparent (owner, 2026-11-23).
        var cyan = new Color(0.45f, 0.85f, 1f, 0.55f);
        var red = new Color(1f, 0.38f, 0.28f, 0.40f);
        // Backdrop everywhere EXCEPT inside the reticle
        // (owner, 2026-11-23): four slabs around the reticle's
        // bounding square, plus a fan in each corner of the
        // square covering the part outside the circle.
        float r = 150f;
        var backdrop = new Color(0.02f, 0.05f, 0.10f, 0.60f);
        DrawRect(new Rect2(0f, 0f, Mathf.Max(0f, c.X - r), size.Y), backdrop);
        DrawRect(new Rect2(c.X + r, 0f, Mathf.Max(0f, size.X - (c.X + r)), size.Y), backdrop);
        DrawRect(new Rect2(c.X - r, 0f, 2f * r, Mathf.Max(0f, c.Y - r)), backdrop);
        DrawRect(new Rect2(c.X - r, c.Y + r, 2f * r, Mathf.Max(0f, size.Y - (c.Y + r))), backdrop);
        if (size.X >= 2f * r && size.Y >= 2f * r)
            for (int q = 0; q < 4; q++)
            {
                float a0 = q * Mathf.Pi / 2f;
                var pts = new System.Collections.Generic.List<Vector2>
                {
                    c + new Vector2(Mathf.Cos(a0 + Mathf.Pi / 4f), Mathf.Sin(a0 + Mathf.Pi / 4f)) * (r * 1.4143f),
                };
                for (int i = 0; i <= 10; i++)
                {
                    float a = a0 + (Mathf.Pi / 2f) * (i / 10f);
                    pts.Add(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
                }
                DrawColoredPolygon(pts.ToArray(), backdrop);
            }
        // Round reticle, the radar's size: 300 px across.
        DrawArc(c, r, 0f, Mathf.Tau, 128, cyan, 2f, true);
        DrawArc(c, r * 0.70f, 0f, Mathf.Tau, 128, red, 1.25f, true);
        for (int i = 0; i < 4; i++)
        {
            float a = i * Mathf.Pi / 2f;
            var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            DrawLine(c + d * (r * 0.84f), c + d * r, cyan, 2.5f, true);          // ring ticks
            DrawLine(c + d * 16f, c + d * (r * 0.66f), new Color(0.92f, 0.96f, 1f, 0.38f), 1.25f, true); // cross arms
        }
        DrawCircle(c, 2.6f, new Color(1f, 0.85f, 0.3f, 0.80f));
    }
}
