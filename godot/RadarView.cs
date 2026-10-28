using Godot;
using System.Collections.Generic;

// Cockpit radar (owner's ask): a north-up, player-centered scope in the
// top-right corner. Range 90u; rings every 30u. The player is a heading
// triangle in the center, the enemy a red contact, buildings gray
// blocks. Pure presentation — it reads walker positions each redraw
// and owns no game state.
public partial class RadarView : Control
{
    public MechWalker Player;
    public MechWalker Enemy;
    public bool EnemyDown;
    public readonly List<Vector2> Buildings = new();

    private const float Range = 90f;

    public override void _Draw()
    {
        var rect = new Rect2(Vector2.Zero, Size);
        DrawRect(rect, new Color(0.02f, 0.045f, 0.07f, 0.55f));
        DrawRect(rect, new Color(0.4f, 0.85f, 0.9f, 0.75f), filled: false, width: 2f);
        if (Player == null) return;

        var c = Size * 0.5f;
        float px = (Size.X * 0.5f - 6f) / Range; // pixels per world unit
        var ringCol = new Color(0.4f, 0.85f, 0.9f, 0.22f);
        for (int r = 1; r <= 3; r++)
            DrawArc(c, r * 30f * px, 0f, Mathf.Tau, 48, ringCol, 1f);
        DrawLine(c + new Vector2(-3f, 0f), c + new Vector2(3f, 0f), ringCol, 1f);
        DrawLine(c + new Vector2(0f, -3f), c + new Vector2(0f, 3f), ringCol, 1f);

        var p = Player.RootPos;
        // Buildings (world XZ -> radar XY; north = -Z = up).
        foreach (var b in Buildings)
        {
            var d = (b - new Vector2(p.X, p.Z)) * px;
            if (Mathf.Abs(d.X) > c.X - 4f || Mathf.Abs(d.Y) > c.Y - 4f) continue;
            DrawRect(new Rect2(c + d - new Vector2(3f, 3f), new Vector2(6f, 6f)),
                new Color(0.62f, 0.65f, 0.68f, 0.8f));
        }
        // Enemy contact, clamped to the rim when out of range.
        if (Enemy != null && !EnemyDown)
        {
            var e = Enemy.RootPos;
            var d = new Vector2(e.X - p.X, e.Z - p.Z) * px;
            float maxR = c.X - 6f;
            if (d.Length() > maxR) d = d.Normalized() * maxR;
            DrawCircle(c + d, 4.5f, new Color(1f, 0.3f, 0.22f, 0.95f));
        }
        // Player heading triangle (legs facing).
        var fwd = MechWalker.HeadingVec(Player.LegsYaw);
        var f2 = new Vector2(fwd.X, fwd.Z);
        var r2 = new Vector2(-f2.Y, f2.X);
        var pts = new[]
        {
            c + f2 * 8f,
            c - f2 * 6f + r2 * 5f,
            c - f2 * 6f - r2 * 5f
        };
        DrawPolygon(pts, new[] { new Color(0.45f, 0.95f, 0.55f, 0.95f) });
        DrawString(GetThemeDefaultFont(), new Vector2(6f, 14f), "RADAR",
            HorizontalAlignment.Left, -1f, 10, new Color(0.4f, 0.85f, 0.9f, 0.8f));
    }
}
