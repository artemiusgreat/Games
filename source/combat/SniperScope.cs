using Godot;

// Sniper scope overlay (owner, 2026-11-23, from his scope
// reference frame): while the sniper view is engaged, a
// semi-transparent backdrop dims the screen OUTSIDE a round
// reticle the size of the radar plot (300 px across) in the
// HUD's chrome colours, with cardinal ticks, a fine centre
// cross and a centre dot. Inside the reticle the view stays
// completely clear. Pure HUD - the aim stays the camera's
// axis exactly as before.
//
// Look-and-feel lives in the constants and static colours
// below (owner, 2026-11-25): every value that shapes the
// scope is named once, here, instead of being hardcoded
// inside the draw calls. The colours are static readonly -
// built once at type load, never per draw. The backdrop is
// ONE keyhole polygon (the screen rect with the reticle
// disc cut out, traced along a slit), refilled into a
// reusable buffer per draw, so _Draw allocates nothing -
// and a single fill cannot seam: the earlier slabs-plus-
// corner-fans build double-drew the backdrop along the
// bounding square's bottom edge, which showed as a dark
// line under the reticle (owner, 2026-11-25).
public partial class SniperScope : Control
{
    // Reticle geometry.
    private const float ReticleRadius = 150f;        // 300 px across, the radar plot's size
    private const int ReticleQuadrants = 4;          // cardinal ticks + cross arms, one per quadrant
    private const int ArcSegments = 128;             // circle smoothness for both rings and the backdrop cutout
    private const float InnerRingScale = 0.70f;      // red ring radius, as a fraction of the reticle's
    private const float TickInnerScale = 0.84f;      // ring ticks start this far out
    private const float CrossArmOuterScale = 0.66f;  // cross arms end this far out
    private const float CrossArmInner = 16f;         // cross arms start this far from the centre, px

    // Line widths and the centre dot.
    private const float RingWidth = 2f;
    private const float InnerRingWidth = 1.25f;
    private const float TickWidth = 2.5f;
    private const float CrossArmWidth = 1.25f;
    private const float CentreDotRadius = 2.6f;

    // Colours (reticle lines softened, owner 2026-11-23).
    private static readonly Color ReticleCyan = new(0.45f, 0.85f, 1f, 0.55f);
    private static readonly Color InnerRingRed = new(1f, 0.38f, 0.28f, 0.40f);
    private static readonly Color Backdrop = new(0.02f, 0.05f, 0.10f, 0.60f);
    private static readonly Color CrossArmColour = new(0.92f, 0.96f, 1f, 0.38f);
    private static readonly Color CentreDotColour = new(1f, 0.85f, 0.3f, 0.80f);

    // The keyhole backdrop's point buffer: screen corners,
    // the slit entry, the disc traced once, the slit entry
    // again. Refilled per draw; never reallocated.
    private readonly Vector2[] _backdrop = new Vector2[4 + 1 + (ArcSegments + 1) + 1];

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
        float r = ReticleRadius;
        // Backdrop everywhere EXCEPT inside the reticle
        // (owner, 2026-11-23): one keyhole polygon - the
        // screen rect traced clockwise, a slit in from the
        // left edge, the disc traced back the other way, and
        // the slit retraced on the way out. A single fill has
        // no internal edges, so no seam can form (the slab +
        // fan build double-drew its bottom edge).
        if (size.X >= 2f * r && size.Y >= 2f * r)
        {
            int n = 0;
            _backdrop[n++] = new Vector2(0f, 0f);
            _backdrop[n++] = new Vector2(size.X, 0f);
            _backdrop[n++] = new Vector2(size.X, size.Y);
            _backdrop[n++] = new Vector2(0f, size.Y);
            _backdrop[n++] = new Vector2(0f, c.Y);
            for (int i = 0; i <= ArcSegments; i++)
            {
                float a = Mathf.Pi - Mathf.Tau * (i / (float)ArcSegments);
                _backdrop[n++] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            }
            _backdrop[n++] = new Vector2(0f, c.Y);
            DrawColoredPolygon(_backdrop, Backdrop);
        }
        else
        {
            // Degenerate window (smaller than the reticle):
            // dim everything; the scope is unusable anyway.
            DrawRect(new Rect2(Vector2.Zero, size), Backdrop);
        }
        // Round reticle, the radar's size: 300 px across.
        DrawArc(c, r, 0f, Mathf.Tau, ArcSegments, ReticleCyan, RingWidth, true);
        DrawArc(c, r * InnerRingScale, 0f, Mathf.Tau, ArcSegments, InnerRingRed, InnerRingWidth, true);
        for (int i = 0; i < ReticleQuadrants; i++)
        {
            float a = i * Mathf.Pi / 2f;
            var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            DrawLine(c + d * (r * TickInnerScale), c + d * r, ReticleCyan, TickWidth, true); // ring ticks
            DrawLine(c + d * CrossArmInner, c + d * (r * CrossArmOuterScale), CrossArmColour, CrossArmWidth, true); // cross arms
        }
        DrawCircle(c, CentreDotRadius, CentreDotColour);
    }
}
