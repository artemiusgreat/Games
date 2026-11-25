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
// built once at type load, never per draw. The corner-fan
// shape is likewise precomputed once as unit offsets and
// only rotated / scaled / translated into a reusable buffer
// per draw, so _Draw allocates nothing.
public partial class SniperScope : Control
{
    // Reticle geometry.
    private const float ReticleRadius = 150f;        // 300 px across, the radar plot's size
    private const int ReticleQuadrants = 4;          // corner fans + cardinal ticks, one per quadrant
    private const int FanArcSegments = 10;           // arc steps per corner fan
    private const int ArcSegments = 128;             // circle smoothness for both rings
    private const float FanCornerScale = 1.4143f;    // sqrt(2): the bounding square's corner distance
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

    // One quadrant's fan as unit offsets from the centre:
    // the bounding square's corner first, then the arc from
    // the quadrant's start to its end. Built once.
    private static readonly Vector2[] FanUnit = BuildFanUnit();
    private readonly Vector2[] _fan = new Vector2[FanUnit.Length];

    private static Vector2[] BuildFanUnit()
    {
        var pts = new Vector2[FanArcSegments + 2];
        pts[0] = new Vector2(Mathf.Cos(Mathf.Pi / 4f), Mathf.Sin(Mathf.Pi / 4f)) * FanCornerScale;
        for (int i = 0; i <= FanArcSegments; i++)
        {
            float a = (Mathf.Pi / 2f) * (i / (float)FanArcSegments);
            pts[i + 1] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
        }
        return pts;
    }

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
        // (owner, 2026-11-23): four slabs around the reticle's
        // bounding square, plus a fan in each corner of the
        // square covering the part outside the circle.
        DrawRect(new Rect2(0f, 0f, Mathf.Max(0f, c.X - r), size.Y), Backdrop);
        DrawRect(new Rect2(c.X + r, 0f, Mathf.Max(0f, size.X - (c.X + r)), size.Y), Backdrop);
        DrawRect(new Rect2(c.X - r, 0f, 2f * r, Mathf.Max(0f, c.Y - r)), Backdrop);
        DrawRect(new Rect2(c.X - r, c.Y + r, 2f * r, Mathf.Max(0f, size.Y - (c.Y + r))), Backdrop);
        if (size.X >= 2f * r && size.Y >= 2f * r)
            for (int q = 0; q < ReticleQuadrants; q++)
            {
                float rot = q * Mathf.Pi / 2f;
                float cos = Mathf.Cos(rot), sin = Mathf.Sin(rot);
                for (int i = 0; i < FanUnit.Length; i++)
                {
                    var u = FanUnit[i];
                    _fan[i] = c + new Vector2(u.X * cos - u.Y * sin, u.X * sin + u.Y * cos) * r;
                }
                DrawColoredPolygon(_fan, Backdrop);
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
