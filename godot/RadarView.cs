using Godot;
using System.Collections.Generic;

// Cockpit radar (owner): a SQUARE, semi-transparent panel in
// the top-right corner carrying a CIRCULAR north-up scope —
// the square stays (it matches the dashboard), the plot
// inside is round (owner, 2026-11-11: 'the inner part needs
// to be a circle'). Range 90u. Dressed like the reference
// instrument: a grid clipped to the circle (15u minor / 30u
// major), circular range rings at 30/60/90, bearing ticks
// around the rim every 10 degrees (major at 30) and an N
// marker. NO numbers anywhere (owner: 'remove numbers, they
// are fake anyway'). The player is a heading triangle in the
// center, enemies are red contacts clamped to the rim —
// buildings are NOT shown (owner). Pure presentation — it
// reads walker positions each redraw and owns no state.
public partial class RadarView : Control
{
    public RiderWalker Player;
    public RiderWalker Enemy;
    public bool EnemyDown;
    public RiderWalker Enemy2;
    public bool Enemy2Down;

    private const float Range = 90f;

    private void DrawContact(RiderWalker w, bool down, Vector2 c, float px, float maxOff)
    {
        if (w == null || down || Player == null) return;
        var p = Player.RootPos;
        var e = w.RootPos;
        var d = new Vector2(e.X - p.X, e.Z - p.Z) * px;
        if (d.Length() > maxOff) d = d.Normalized() * maxOff; // clamp to the rim
        DrawCircle(c + d, 4.5f, new Color(1f, 0.3f, 0.22f, 0.95f));
    }

    public override void _Draw()
    {
        var ink = new Color(0.45f, 0.9f, 0.95f);
        float W = Size.X, H = Size.Y;
        var border = new Rect2(1f, 1f, W - 2f, H - 2f);
        DrawRect(border, new Color(0.03f, 0.06f, 0.09f, 0.5f));
        var c = Size * 0.5f;
        float half = Mathf.Min(W, H) * 0.5f - 10f; // scope radius in px
        float px = half / Range;                  // pixels per world unit

        // Grid clipped to the circle: each line is drawn only
        // across its chord.
        var minor = new Color(ink, 0.10f);
        var major = new Color(ink, 0.20f);
        for (int k = -6; k <= 6; k++)
        {
            if (k == 0) continue;
            var col = k % 2 == 0 ? major : minor;
            float o = k * 15f * px;
            float ch = Mathf.Sqrt(Mathf.Max(0f, half * half - o * o));
            DrawLine(c + new Vector2(o, -ch), c + new Vector2(o, ch), col, 1f);
            DrawLine(c + new Vector2(-ch, o), c + new Vector2(ch, o), col, 1f);
        }
        // Centre cross.
        DrawLine(c + new Vector2(-half, 0f), c + new Vector2(half, 0f), major, 1f);
        DrawLine(c + new Vector2(0f, -half), c + new Vector2(0f, half), major, 1f);

        // Circular range rings at 30/60/90 (the rim is 90).
        for (int r = 1; r <= 3; r++)
        {
            float o = r * 30f * px;
            DrawArc(c, o, 0f, Mathf.Tau, 96, new Color(ink, r == 3 ? 0.4f : 0.28f), 1f);
        }

        // Bearing ticks around the rim every 10 degrees,
        // pointing inward; major at 30.
        for (int deg = 0; deg < 360; deg += 10)
        {
            float a = Mathf.DegToRad(deg);
            var dir = new Vector2(Mathf.Sin(a), -Mathf.Cos(a)); // 0 = north (up)
            var rim = c + dir * half;
            bool maj = deg % 30 == 0;
            DrawLine(rim, rim - dir * (maj ? 9f : 5f), new Color(ink, maj ? 0.85f : 0.5f), maj ? 1.5f : 1f);
        }
        // N marker just inside the rim (a letter, not a number).
        var font = GetThemeDefaultFont();
        DrawString(font, c + new Vector2(-3f, -half + 13f), "N",
            HorizontalAlignment.Left, -1f, 10, new Color(ink, 0.95f));

        // Sharp square border on top of the dressing.
        DrawRect(border, new Color(ink, 0.8f), false, 1.5f);
        if (Player == null) return;

        // Enemy contacts, clamped to the rim when out of range.
        DrawContact(Enemy, EnemyDown, c, px, half - 6f);
        DrawContact(Enemy2, Enemy2Down, c, px, half - 6f);
        // Player heading triangle (legs facing).
        var fwd = RiderWalker.HeadingVec(Player.LegsYaw);
        var f2 = new Vector2(fwd.X, fwd.Z);
        var r2 = new Vector2(-f2.Y, f2.X);
        var pts = new[]
        {
            c + f2 * 8f,
            c - f2 * 6f + r2 * 5f,
            c - f2 * 6f - r2 * 5f
        };
        DrawPolygon(pts, new[] { new Color(0.45f, 0.95f, 0.55f, 0.95f) });
        DrawString(font, new Vector2(6f, H - 5f), "RADAR",
            HorizontalAlignment.Left, -1f, 9, new Color(ink, 0.65f));
    }
}
