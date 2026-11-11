using Godot;
using System.Collections.Generic;

// Cockpit radar (owner's ask): a SQUARE, semi-transparent,
// north-up, player-centered scope in the top-right corner —
// sharp corners, no round border. Range 90u. Dressed like
// the reference instrument: a square grid (15u minor / 30u
// major), concentric SQUARE range rings at 30/60/90 with
// scale numbers on the east axis, bearing ticks around the
// rim every 10 degrees (major at 30, labelled at the
// cardinals) and an N marker. The player is a heading
// triangle in the center, enemies are red contacts clamped
// to the square rim — buildings are NOT shown (owner).
// Pure presentation — it reads walker positions each
// redraw and owns no game state.
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
        float m = Mathf.Max(Mathf.Abs(d.X), Mathf.Abs(d.Y));
        if (m > maxOff) d *= maxOff / m; // clamp to the square rim
        DrawCircle(c + d, 4.5f, new Color(1f, 0.3f, 0.22f, 0.95f));
    }

    public override void _Draw()
    {
        var ink = new Color(0.45f, 0.9f, 0.95f);
        float W = Size.X, H = Size.Y;
        var border = new Rect2(1f, 1f, W - 2f, H - 2f);
        DrawRect(border, new Color(0.03f, 0.06f, 0.09f, 0.5f));
        var c = Size * 0.5f;
        float half = Mathf.Min(W, H) * 0.5f - 10f; // plot half-extent
        float px = half / Range;                  // pixels per world unit

        // Square grid: minor every 15u, major every 30u.
        var minor = new Color(ink, 0.10f);
        var major = new Color(ink, 0.20f);
        for (int k = -6; k <= 6; k++)
        {
            if (k == 0) continue;
            var col = k % 2 == 0 ? major : minor;
            float o = k * 15f * px;
            DrawLine(c + new Vector2(o, -half), c + new Vector2(o, half), col, 1f);
            DrawLine(c + new Vector2(-half, o), c + new Vector2(half, o), col, 1f);
        }
        // Centre cross.
        DrawLine(c + new Vector2(-half, 0f), c + new Vector2(half, 0f), major, 1f);
        DrawLine(c + new Vector2(0f, -half), c + new Vector2(0f, half), major, 1f);

        // Concentric square range rings + scale numbers east.
        var font = GetThemeDefaultFont();
        for (int r = 1; r <= 3; r++)
        {
            float o = r * 30f * px;
            DrawRect(new Rect2(c - new Vector2(o, o), new Vector2(o, o) * 2f),
                new Color(ink, r == 3 ? 0.4f : 0.28f), false, 1f);
            if (r < 3) // the rim itself is the 90 ring; its cardinal label lives there
                DrawString(font, c + new Vector2(o - 15f, -4f), $"{r * 30}",
                    HorizontalAlignment.Left, -1f, 9, new Color(ink, 0.75f));
        }

        // Bearing ticks around the rim: a tick where the ray
        // at each bearing leaves the square, pointing inward.
        for (int deg = 0; deg < 360; deg += 10)
        {
            float a = Mathf.DegToRad(deg);
            var dir = new Vector2(Mathf.Sin(a), -Mathf.Cos(a)); // 0 = north (up)
            float t = half / Mathf.Max(Mathf.Abs(dir.X), Mathf.Abs(dir.Y));
            var rim = c + dir * t;
            bool maj = deg % 30 == 0;
            DrawLine(rim, rim - dir * (maj ? 9f : 5f), new Color(ink, maj ? 0.85f : 0.5f), maj ? 1.5f : 1f);
        }
        // Cardinal labels + N arrow, just inside the rim.
        DrawString(font, c + new Vector2(-3f, -half + 13f), "N",
            HorizontalAlignment.Left, -1f, 10, new Color(ink, 0.95f));
        DrawString(font, c + new Vector2(half - 24f, 3f), "90",
            HorizontalAlignment.Left, -1f, 9, new Color(ink, 0.7f));
        DrawString(font, c + new Vector2(-4f, half - 4f), "180",
            HorizontalAlignment.Left, -1f, 9, new Color(ink, 0.7f));
        DrawString(font, c + new Vector2(-half + 4f, 3f), "270",
            HorizontalAlignment.Left, -1f, 9, new Color(ink, 0.7f));

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
