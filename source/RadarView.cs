using Godot;
using System.Collections.Generic;

// Cockpit radar (owner): a SQUARE, semi-transparent panel in
// the top-right corner carrying a CIRCULAR scope - the
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
// enemies are red contacts clamped to the rim - buildings
// are NOT shown (owner). Pure presentation - it reads
// walker positions each redraw and owns no state.
public partial class RadarView : Control
{
    public RiderWalker Player;
    public RiderWalker Enemy;
    public bool EnemyDown;
    public RiderWalker Enemy2;
    public bool Enemy2Down;
    // Story teammates (owner, teams stage): friendly contacts
    // in the player's own green. Refreshed by the HUD each
    // frame like the enemy references (walkers are rebuilt on
    // lab swaps and leader changes).
    public readonly List<(RiderWalker W, bool Down)> Allies = new();
    // Tournament bracket (Stage D3): opposing-team NPC riders
    // beyond the two seats, drawn as ordinary enemy contacts.
    public readonly List<(RiderWalker W, bool Down)> Foes = new();

    // Scope range in world units (owner, 2026-11-11 round 6:
    // 'radar seems broken, dot positions don't change' - at
    // the old fixed 90u the mission enemies sat ~1,074u away,
    // so both contacts were rim-clamped for the entire
    // approach and only their bearing crept). Set by the HUD
    // from the world: 600u on missions (the firefight reads
    // in the inner half, bearings track along the rim before
    // that), 160u in the arena (spawns ~139u apart now read
    // just inside the rim instead of clamped on it).
    public float Range = 160f;

    // Scope palette (named once; the grid alphas derive
    // from the ink colour per element in _Draw).
    private static readonly Color ScopeInkColor = new(0.45f, 0.9f, 0.95f);
    private static readonly Color PanelColor = new(0.03f, 0.06f, 0.09f, 0.5f);
    private static readonly Color EnemyContactColor = new(1f, 0.3f, 0.22f, 0.95f);
    private static readonly Color FriendlyColor = new(0.45f, 0.95f, 0.55f, 0.95f);
    private static readonly Color HeightDeepColor = new(0.05f, 0.10f, 0.13f);
    private static readonly Color HeightMidColor = new(0.13f, 0.23f, 0.22f);
    private static readonly Color HeightCrestColor = new(0.42f, 0.48f, 0.42f);
    private static readonly Color HeightTintColor = new(1f, 1f, 1f, 0.5f);
    private const float ContactRadius = 4.5f;
    private const float ContactRimInset = 6f;  // contacts clamp this far inside the rim
    private const float ScopeInset = 10f;      // scope disc inset from the panel edge
    // Scope dressing: grid, rings, ticks and labels.
    private const int HeightFanSegments = 48;  // heightmap underlay fan resolution
    private const float GridDivisions = 12f;   // grid cell = Range / this
    private const float GridMinorAlpha = 0.10f, GridMajorAlpha = 0.20f;
    private const int RingCount = 3;           // range rings at Range/3 steps
    private const int RingSegments = 96;
    private const float RingAlpha = 0.28f, RimRingAlpha = 0.4f, RimAlpha = 0.55f;
    private const int TickStepDeg = 10, MajorTickDeg = 30;
    private const float TickLength = 5f, MajorTickLength = 9f;
    private const float TickAlpha = 0.5f, MajorTickAlpha = 0.85f;
    private const float MajorTickWidth = 1.5f;
    private const float MinNorthDirSq = 0.001f; // squared-length guard for the north marker's direction
    private const float NorthLabelInset = 11f;
    private const int NorthLabelFontSize = 10;
    private const float NorthLabelAlpha = 0.95f;
    private const int RadarLabelFontSize = 9;
    private const float RadarLabelAlpha = 0.65f;

    // Heightmap underlay (owner ask, 2026-11-20): a semi-
    // transparent relief of the current world's heightfield
    // inside the scope. Generated lazily on first draw from
    // Main.HeightAt (the world's own grid - mesas, valleys,
    // river carves and the ascent all included), hillshaded
    // under a fixed NW light with a hypsometric ramp in the
    // scope's ink family. Drawn as a triangle fan whose rim
    // vertices carry world-mapped UVs, so the layer rotates
    // heading-up exactly like the contacts and is clipped
    // to the circle by construction.
    public Texture2D HeightTex;

    private static Vector2 WorldUv(Vector2 w)
    {
        float halfW = Main.TerrainSize * 0.5f;
        return new Vector2((w.X + halfW) / Main.TerrainSize,
                           (w.Y + halfW) / Main.TerrainSize);
    }

    // Inverse of ToScope for a scope offset in world units.
    private Vector2 ScopeToWorld(Vector2 sc)
    {
        float yaw = Player.LegsYaw;
        float cy = Mathf.Cos(yaw), sy = Mathf.Sin(yaw);
        return new Vector2(sc.X * cy + sc.Y * sy, -sc.X * sy + sc.Y * cy);
    }

    public static Texture2D BuildHeightTexture()
    {
        const int N = 256;
        float halfW = Main.TerrainSize * 0.5f;
        var hs = new float[N * N];
        float lo = float.MaxValue, hi = float.MinValue;
        for (int j = 0; j < N; j++)
            for (int i = 0; i < N; i++)
            {
                float wx = -halfW + (i + 0.5f) / N * Main.TerrainSize;
                float wz = -halfW + (j + 0.5f) / N * Main.TerrainSize;
                float h = Main.HeightAt(wx, wz);
                hs[j * N + i] = h;
                if (h < lo) lo = h;
                if (h > hi) hi = h;
            }
        float span = Mathf.Max(hi - lo, 1f);
        float cellW = Main.TerrainSize / N;
        var light = new Vector3(-0.55f, 0.75f, -0.35f).Normalized();
        var img = Image.CreateEmpty(N, N, false, Image.Format.Rgb8);
        var deep = HeightDeepColor;
        var mid = HeightMidColor;
        var crest = HeightCrestColor;
        for (int j = 0; j < N; j++)
            for (int i = 0; i < N; i++)
            {
                int iL = Mathf.Max(i - 1, 0), iR = Mathf.Min(i + 1, N - 1);
                int jU = Mathf.Max(j - 1, 0), jD = Mathf.Min(j + 1, N - 1);
                var n = new Vector3(
                    (hs[j * N + iL] - hs[j * N + iR]) / (2f * cellW), 1f,
                    (hs[jU * N + i] - hs[jD * N + i]) / (2f * cellW)).Normalized();
                float shade = 0.45f + 0.55f * Mathf.Max(0f, n.Dot(light));
                float t = Mathf.Clamp((hs[j * N + i] - lo) / span, 0f, 1f);
                var col = t < 0.5f
                    ? deep.Lerp(mid, t * 2f)
                    : mid.Lerp(crest, (t - 0.5f) * 2f);
                img.SetPixel(i, j, col * shade);
            }
        return ImageTexture.CreateFromImage(img);
    }

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

    // Scope offset of a world position, in world units,
    // unclamped: (0, -dist) is dead ahead, (+dist, 0) is
    // dead right. Public so the harness can assert the
    // instrument's geometry directly (phase 17).
    public Vector2 ScopeOf(Vector3 world)
    {
        var p = Player.RootPos;
        return ToScope(new Vector2(world.X - p.X, world.Z - p.Z));
    }

    private void DrawContact(RiderWalker w, bool down, Vector2 c, float px, float maxOff,
        Color? col = null)
    {
        if (w == null || down || Player == null) return;
        var d = ScopeOf(w.RootPos) * px;
        if (d.Length() > maxOff) d = d.Normalized() * maxOff; // clamp to the rim
        DrawCircle(c + d, ContactRadius, col ?? EnemyContactColor);
    }

    public override void _Draw()
    {
        var ink = ScopeInkColor;
        float W = Size.X, H = Size.Y;
        var border = new Rect2(1f, 1f, W - 2f, H - 2f);
        DrawRect(border, PanelColor);
        var c = Size * 0.5f;
        float half = Mathf.Min(W, H) * 0.5f - ScopeInset; // scope radius in px
        float px = half / Range;                  // pixels per world unit

        // Heightmap underlay: a fan over the scope disc; the
        // centre vertex maps to the player's position, each
        // rim vertex to the world point at bearing/Range, so
        // the relief tracks and rotates with the scope.
        if (Player != null)
        {
            HeightTex ??= BuildHeightTexture();
            // Per-triangle draws: a single fan polygon with a
            // centre vertex does NOT triangulate reliably in
            // Godot (only one wedge of it rendered - the layer
            // was effectively invisible in play, 2026-11-20).
            // Triangles always rasterize.
            const int SEG = HeightFanSegments;
            var tint = HeightTintColor;
            var pw = new Vector2(Player.RootPos.X, Player.RootPos.Z);
            var uvC = WorldUv(pw);
            for (int k = 0; k < SEG; k++)
            {
                float a0 = (float)k / SEG * Mathf.Tau;
                float a1 = (float)(k + 1) / SEG * Mathf.Tau;
                var d0 = new Vector2(Mathf.Sin(a0), -Mathf.Cos(a0)); // 0 = ahead (up)
                var d1 = new Vector2(Mathf.Sin(a1), -Mathf.Cos(a1));
                DrawPolygon(
                    new[] { c, c + d0 * half, c + d1 * half },
                    new[] { tint, tint, tint },
                    new[] { uvC, WorldUv(pw + ScopeToWorld(d0 * Range)), WorldUv(pw + ScopeToWorld(d1 * Range)) },
                    HeightTex);
            }
        }

        // Grid clipped to the circle: each line is drawn only
        // across its chord.
        var minor = new Color(ink, GridMinorAlpha);
        var major = new Color(ink, GridMajorAlpha);
        float cell = Range / GridDivisions;
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
        for (int r = 1; r <= RingCount; r++)
        {
            float o = r * (Range / RingCount) * px;
            DrawArc(c, o, 0f, Mathf.Tau, RingSegments, new Color(ink, r == RingCount ? RimRingAlpha : RingAlpha), 1f);
        }

        // Bearing ticks around the rim every 10 degrees,
        // pointing inward; major at 30.
        for (int deg = 0; deg < 360; deg += TickStepDeg)
        {
            float a = Mathf.DegToRad(deg);
            var dir = new Vector2(Mathf.Sin(a), -Mathf.Cos(a)); // 0 = north (up)
            var rim = c + dir * half;
            bool maj = deg % MajorTickDeg == 0;
            DrawLine(rim, rim - dir * (maj ? MajorTickLength : TickLength), new Color(ink, maj ? MajorTickAlpha : TickAlpha), maj ? MajorTickWidth : 1f);
        }
        // N marker just inside the rim (a letter, not a
        // number), riding to wherever north falls under the
        // current heading.
        var font = GetThemeDefaultFont();
        if (Player != null)
        {
            var nd = ToScope(new Vector2(0f, -1f)); // world north in (X, Z)
            if (nd.LengthSquared() > MinNorthDirSq)
            {
                var np = c + nd.Normalized() * (half - NorthLabelInset);
                DrawString(font, np + new Vector2(-3f, 3f), "N",
                    HorizontalAlignment.Left, -1f, NorthLabelFontSize, new Color(ink, NorthLabelAlpha));
            }
        }

        // Thin round rim on top of the dressing (owner,
        // 2026-11-23: the internal border goes thinner and
        // round - a ring on the scope disc, not the square).
        DrawArc(c, half, 0f, Mathf.Tau, RingSegments, new Color(ink, RimAlpha), 1.0f, true);
        if (Player == null) return;

        // Enemy contacts, clamped to the rim when out of range.
        DrawContact(Enemy, EnemyDown, c, px, half - ContactRimInset);
        DrawContact(Enemy2, Enemy2Down, c, px, half - ContactRimInset);
        foreach (var (fw, fdown) in Foes)
            DrawContact(fw, fdown, c, px, half - ContactRimInset);
        // Friendly contacts in the player's green.
        foreach (var (aw, adown) in Allies)
            DrawContact(aw, adown, c, px, half - ContactRimInset, FriendlyColor);
        // Player triangle: fixed, pointing up - up IS the
        // player's facing on a heading-up scope.
        var pts = new[]
        {
            c + new Vector2(0f, -8f),
            c + new Vector2(5f, 6f),
            c + new Vector2(-5f, 6f)
        };
        DrawPolygon(pts, new[] { FriendlyColor });
        DrawString(font, new Vector2(6f, H - 5f), "RADAR",
            HorizontalAlignment.Left, -1f, RadarLabelFontSize, new Color(ink, RadarLabelAlpha));
    }
}
