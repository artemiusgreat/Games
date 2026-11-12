using Godot;
using System.Collections.Generic;

// Cockpit radar (owner): a SQUARE, semi-transparent panel in
// the top-right corner carrying a CIRCULAR scope — the
// square stays (it matches the dashboard), the plot inside
// is round (owner, 2026-11-11: 'the inner part needs to be
// a circle'). HEADING-UP (owner, 2026-11-11): the plot
// rotates with the rider's legs, so a contact dead ahead
// sits at the top centre and the rim ticks read as relative
// bearing; the N marker travels around the rim to wherever
// north actually is. Range 90u. Dressed like the reference
// instrument: a grid clipped to the circle (15u minor / 30u
// major), circular range rings at 30/60/90 and bearing
// ticks around the rim every 10 degrees (major at 30). NO
// numbers anywhere (owner: 'remove numbers, they are fake
// anyway'). The player is a fixed up-triangle in the center,
// enemies are red contacts clamped to the rim — buildings
// are NOT shown (owner). Pure presentation — it reads
// walker positions each redraw and owns no state.
public partial class RadarView : Control
{
    public RiderWalker Player;
    public RiderWalker Enemy;
    public bool EnemyDown;
    public RiderWalker Enemy2;
    public bool Enemy2Down;

    // Scope range in world units (owner, 2026-11-11 round 6:
    // 'radar seems broken, dot positions don't change' — at
    // the old fixed 90u the mission enemies sat ~1,074u away,
    // so both contacts were rim-clamped for the entire
    // approach and only their bearing crept). Set by the HUD
    // from the world: 600u on missions (the firefight reads
    // in the inner half, bearings track along the rim before
    // that), 160u in the arena (spawns ~139u apart now read
    // just inside the rim instead of clamped on it).
    public float Range = 160f;

    // World XZ offset -> heading-up screen offset: the
    // player's forward maps to screen-up, their right to
    // screen-right (owner: walking straight at an enemy must
    // put it at the top centre).
    private Vector2 ToScope(Vector2 dWorld)
    {
        float yaw = Player.LegsYaw;
        var f = new Vector2(-Mathf.Sin(yaw), -Mathf.Cos(yaw)); // forward in (X, Z)
        var r = new Vector2(Mathf.Cos(yaw), -Mathf.Sin(yaw));  // right in (X, Z)
        return new Vector2(dWorld.Dot(r), -dWorld.Dot(f));
    }

    private void DrawContact(RiderWalker w, bool down, Vector2 c, float px, float maxOff)
    {
        if (w == null || down || Player == null) return;
        var p = Player.RootPos;
        var e = w.RootPos;
        var d = ToScope(new Vector2(e.X - p.X, e.Z - p.Z)) * px;
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
        float cell = Range / 12f;
        int cells = Mathf.CeilToInt(half / (cell * px)) + 1;
        for (int k = -cells; k <= cells; k++)
        {
            if (k == 0) continue;
            var col = k % 2 == 0 ? major : minor;
            float o = k * cell * px;
            float ch = Mathf.Sqrt(Mathf.Max(0f, half * half - o * o));
            DrawLine(c + new Vector2(o, -ch), c + new Vector2(o, ch), col, 1f);
            DrawLine(c + new Vector2(-ch, o), c + new Vector2(ch, o), col, 1f);
        }
        // Centre cross.
        DrawLine(c + new Vector2(-half, 0f), c + new Vector2(half, 0f), major, 1f);
        DrawLine(c + new Vector2(0f, -half), c + new Vector2(0f, half), major, 1f);

        // Circular range rings at Range/3 steps (the rim is Range).
        for (int r = 1; r <= 3; r++)
        {
            float o = r * (Range / 3f) * px;
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
        // N marker just inside the rim (a letter, not a
        // number), riding to wherever north falls under the
        // current heading.
        var font = GetThemeDefaultFont();
        if (Player != null)
        {
            var nd = ToScope(new Vector2(0f, -1f)); // world north in (X, Z)
            if (nd.LengthSquared() > 0.001f)
            {
                var np = c + nd.Normalized() * (half - 11f);
                DrawString(font, np + new Vector2(-3f, 3f), "N",
                    HorizontalAlignment.Left, -1f, 10, new Color(ink, 0.95f));
            }
        }

        // Sharp square border on top of the dressing.
        DrawRect(border, new Color(ink, 0.8f), false, 1.5f);
        if (Player == null) return;

        // Enemy contacts, clamped to the rim when out of range.
        DrawContact(Enemy, EnemyDown, c, px, half - 6f);
        DrawContact(Enemy2, Enemy2Down, c, px, half - 6f);
        // Player triangle: fixed, pointing up — up IS the
        // player's facing on a heading-up scope.
        var pts = new[]
        {
            c + new Vector2(0f, -8f),
            c + new Vector2(5f, 6f),
            c + new Vector2(-5f, 6f)
        };
        DrawPolygon(pts, new[] { new Color(0.45f, 0.95f, 0.55f, 0.95f) });
        DrawString(font, new Vector2(6f, H - 5f), "RADAR",
            HorizontalAlignment.Left, -1f, 9, new Color(ink, 0.65f));
    }
}
