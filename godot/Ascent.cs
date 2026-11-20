using Godot;

// The Ascent (owner, 2026-11-20): a Story-mode campaign map -
// one long valley climbing from the lowland mouth in the south
// to a layered citadel city on the summit plateau in the north.
// Six villages sit along the climb, each with its own guard
// group; two rivers cross the valley floor, each bridged where
// the road crosses. The scenario is non-linear by construction:
// every village guards only its own ground (the standard
// 260u aggro / 360u leash), so the player can clear the valley
// village by village, slip past them all, or drive straight up
// the road to the main battlefield on the summit, where ten
// defenders wait (the two seats plus eight guards).
//
// Pure layout + height math: Main's heightfield, the
// environment build and the guard spawner all read THIS table,
// so the ground, the buildings and the defenders can never
// disagree about where a village is.
public static class Ascent
{
    // One village: centre, flatten radius, guard count.
    public readonly struct Site
    {
        public readonly Vector2 C; public readonly float R; public readonly int Guards;
        public Site(float x, float z, float r, int guards) { C = new Vector2(x, z); R = r; Guards = guards; }
    }

    // The road: a gentle S-curve up the valley's long axis.
    public static float RoadX(float z) => 150f * Mathf.Sin(z * 0.0021f + 0.4f);

    // Elevation along the road: smoothstep from the valley
    // mouth (z = +820, h = 0) to the summit (z = -780, h = 168).
    public static float Profile(float z)
    {
        float t = Mathf.Clamp((820f - z) / 1600f, 0f, 1f);
        return 168f * t * t * (3f - 2f * t);
    }

    // River centrelines (z), south to north.
    public static readonly float[] Rivers = { 450f, -300f };

    // The villages, south to north: the climb gets richer and
    // better defended the higher it goes (2, 3, 3, 4, 4, 5).
    public static readonly Site[] Sites =
    {
        new(RoadX(640f) - 135f, 640f, 85f, 2),
        new(RoadX(270f) + 145f, 270f, 85f, 3),
        new(RoadX(50f) - 150f, 50f, 90f, 3),
        new(RoadX(-160f) + 135f, -160f, 90f, 4),
        new(RoadX(-420f) - 145f, -420f, 90f, 4),
        new(RoadX(-510f) + 125f, -510f, 90f, 5),
    };

    // The summit plateau and the home muster at the valley mouth.
    public static readonly Vector2 Summit = new(RoadX(-720f), -720f);
    public const float SummitR = 175f;
    public const int SummitGuards = 8; // + the two seats = the 10-rider main battlefield
    public static readonly Vector2 Home = new(RoadX(820f), 820f);

    // World pads for an Ascent mission: home, then the two
    // summit seats on the open apron south of the platform
    // decks (the decks span z -58..+74 relative to the summit
    // centre; the apron at +118 stands clear of them and of
    // the ramp corridor between).
    public static readonly Vector2[] Pads =
    {
        Home,
        Summit + new Vector2(-58f, 100f),
        Summit + new Vector2(58f, 100f),
    };

    // Where the summit guard group musters and patrols: the
    // same south apron as the seats.
    public static Vector2 SummitApron => Summit + new Vector2(24f, 106f);

    private static float Smooth(float a, float b, float v)
    {
        float t = Mathf.Clamp((v - a) / (b - a), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    // The unflattened ground: profile + valley walls + rim +
    // river carves.
    private static float RawBase(float x, float z)
    {
        float h = Profile(z);
        float d = Mathf.Abs(x - RoadX(z));
        // Valley walls: the floor stays open for ~110u either
        // side of the road, then the country rises steeply.
        h += Mathf.Pow(Smooth(110f, 430f, d), 1.5f) * 150f;
        // Gentle analytic detail on the slopes only - the road
        // corridor and the sites stay clean.
        h += Mathf.Sin(x * 0.021f + Mathf.Sin(z * 0.017f) * 2f) * Mathf.Cos(z * 0.019f)
            * 6f * Smooth(80f, 260f, d);
        // The map rim.
        float sq = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
        h += Smooth(860f, 950f, sq) * 70f;
        // Rivers: a channel carved across the whole valley,
        // shallower under the bridge span (the deck there is
        // registered by the environment build).
        foreach (float rz in Rivers)
        {
            float bridgeMask = 1f - Smooth(20f, 36f, Mathf.Abs(x - RoadX(rz)));
            h -= 8f * (1f - Smooth(0f, 30f, Mathf.Abs(z - rz))) * (1f - 0.65f * bridgeMask);
        }
        return h;
    }

    private static readonly float[] SiteH = BuildSiteH();

    private static float[] BuildSiteH()
    {
        var hs = new float[Sites.Length + 2];
        for (int i = 0; i < Sites.Length; i++) hs[i] = RawBase(Sites[i].C.X, Sites[i].C.Y);
        hs[Sites.Length] = RawBase(Home.X, Home.Y);
        hs[Sites.Length + 1] = RawBase(Summit.X, Summit.Y);
        return hs;
    }

    public static float SummitH => SiteH[Sites.Length + 1];

    // The heightfield entry (Main.RawHeight dispatches here on
    // an Ascent contract): RawBase with every site, the home
    // muster and the summit flattened toward its centre height -
    // villages and the citadel stand on level ground.
    public static float Height(float x, float z)
    {
        float h = RawBase(x, z);
        var p = new Vector2(x, z);
        for (int i = 0; i < Sites.Length; i++)
        {
            float d = p.DistanceTo(Sites[i].C);
            if (d < Sites[i].R) h = Mathf.Lerp(h, SiteH[i], Smooth(Sites[i].R, Sites[i].R * 0.5f, d));
        }
        float dh = p.DistanceTo(Home);
        if (dh < 100f) h = Mathf.Lerp(h, SiteH[Sites.Length], Smooth(100f, 50f, dh));
        float ds = p.DistanceTo(Summit);
        if (ds < SummitR) h = Mathf.Lerp(h, SiteH[Sites.Length + 1], Smooth(SummitR, 140f, ds));
        return h;
    }
}
