using Godot;
using System.Collections.Generic;

// Cockpit radar (owner's ask): a ROUND, semi-transparent,
// north-up, player-centered scope in the top-right corner.
// Range 90u; rings every 30u. The player is a heading triangle
// in the center, enemies are red contacts — buildings are NOT
// shown (owner). Pure presentation — it reads walker positions
// each redraw and owns no game state.
public partial class RadarView : Control
{
    public RiderWalker Player;
    public RiderWalker Enemy;
    public bool EnemyDown;
    public RiderWalker Enemy2;
    public bool Enemy2Down;

    private const float Range = 90f;

    private void DrawContact(RiderWalker w, bool down, Vector2 c, float px)
    {
        if (w == null || down || Player == null) return;
        var p = Player.RootPos;
        var e = w.RootPos;
        var d = new Vector2(e.X - p.X, e.Z - p.Z) * px;
        float maxR = c.X - 6f;
        if (d.Length() > maxR) d = d.Normalized() * maxR;
        DrawCircle(c + d, 4.5f, new Color(1f, 0.3f, 0.22f, 0.95f));
    }

    public override void _Draw()
    {
        var c = Size * 0.5f;
        float scopeR = Mathf.Min(Size.X, Size.Y) * 0.5f - 2f;
        DrawCircle(c, scopeR, new Color(0.02f, 0.045f, 0.07f, 0.45f));
        DrawArc(c, scopeR, 0f, Mathf.Tau, 64, new Color(0.4f, 0.85f, 0.9f, 0.75f), 2f);
        if (Player == null) return;

        float px = (Size.X * 0.5f - 6f) / Range; // pixels per world unit
        var ringCol = new Color(0.4f, 0.85f, 0.9f, 0.22f);
        for (int r = 1; r <= 3; r++)
            DrawArc(c, r * 30f * px, 0f, Mathf.Tau, 48, ringCol, 1f);
        DrawLine(c + new Vector2(-3f, 0f), c + new Vector2(3f, 0f), ringCol, 1f);
        DrawLine(c + new Vector2(0f, -3f), c + new Vector2(0f, 3f), ringCol, 1f);

        // Enemy contacts, clamped to the rim when out of range.
        DrawContact(Enemy, EnemyDown, c, px);
        DrawContact(Enemy2, Enemy2Down, c, px);
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
        DrawString(GetThemeDefaultFont(), new Vector2(c.X - 20f, 16f), "RADAR",
            HorizontalAlignment.Left, -1f, 10, new Color(0.4f, 0.85f, 0.9f, 0.8f));
    }
}
