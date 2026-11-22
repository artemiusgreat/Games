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

    // The road: a winding S-curve up the valley's long
    // axis - two sine harmonics, so the run bends for real
    // instead of reading as a straight canyon with a lean
    // (owner, 2026-11-20).
    public static float RoadX(float z)
        => 150f * Mathf.Sin(z * 0.0021f + 0.4f)
         + 85f * Mathf.Sin(z * 0.0053f + 1.7f);

    // Deterministic value noise for the mountain detail.
    private static float Hash2(int ix, int iz)
    {
        int h = ix * 374761393 + iz * 668265263;
        h = (h ^ (h >> 13)) * 1274126177;
        return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
    }

    private static float Noise2(float x, float z)
    {
        int ix = Mathf.FloorToInt(x), iz = Mathf.FloorToInt(z);
        float fx = x - ix, fz = z - iz;
        float sx = fx * fx * (3f - 2f * fx), sz = fz * fz * (3f - 2f * fz);
        float a = Hash2(ix, iz), b = Hash2(ix + 1, iz);
        float c = Hash2(ix, iz + 1), d = Hash2(ix + 1, iz + 1);
        return a + (b - a) * sx + (c - a) * sz + (a - b - c + d) * sx * sz;
    }

    private static float Fbm(float x, float z)
        => Noise2(x, z) * 0.55f
         + Noise2(x * 2.13f + 7.3f, z * 2.13f + 3.1f) * 0.30f
         + Noise2(x * 4.31f + 13.7f, z * 4.31f + 9.2f) * 0.15f;

    private static float Noise1(float t) => Noise2(t, 3.77f);

    // Side canyons notched into the walls: (z, side sign).
    private static readonly (float Z, float Side)[] Gullies =
        { (120f, -1f), (-60f, 1f), (-560f, -1f) };

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
    // Hills terrain (owner's Maps-Hills OBJ, 2026-11-22):
    // a 512x512 float grid baked from the OBJ's top surface
    // (its solid base and side walls excluded), stretched
    // over the whole 1900u world with the relief tripled -
    // the OBJ spans 200u with 52u of relief natively, the
    // grid carries ~156u over 1900u. RawBase samples it in
    // place of the analytic valley when WorldConfig.Hills
    // is on; the site flattening in Height and the river
    // carves apply unchanged, so the villages, the road
    // dressing and the bridges sit on the new ground.
    private static float[] _hills;
    private static bool _hillsTried;

    private static float HillsSample(float x, float z)
    {
        if (!_hillsTried)
        {
            _hillsTried = true;
            using var f = FileAccess.Open("res://assets/hills/hills_height.bin", FileAccess.ModeFlags.Read);
            if (f != null)
            {
                var bytes = f.GetBuffer(512 * 512 * 4);
                if (bytes.Length == 512 * 512 * 4)
                {
                    _hills = new float[512 * 512];
                    System.Buffer.BlockCopy(bytes, 0, _hills, 0, bytes.Length);
                }
            }
        }
        if (_hills == null) return 0f;
        float fx = Mathf.Clamp((x + 950f) / 1900f * 512f - 0.5f, 0f, 510.999f);
        float fz = Mathf.Clamp((z + 950f) / 1900f * 512f - 0.5f, 0f, 510.999f);
        int ix = (int)fx, iz = (int)fz;
        float tx = fx - ix, tz = fz - iz;
        float top = _hills[iz * 512 + ix] * (1f - tx) + _hills[iz * 512 + ix + 1] * tx;
        float bot = _hills[(iz + 1) * 512 + ix] * (1f - tx) + _hills[(iz + 1) * 512 + ix + 1] * tx;
        return top * (1f - tz) + bot * tz;
    }

    private static float RawBase(float x, float z)
    {
        if (WorldConfig.Hills)
        {
            float hh = HillsSample(x, z);
            foreach (float rz in Rivers)
            {
                float bridgeMask = 1f - Smooth(20f, 36f, Mathf.Abs(x - RoadX(rz)));
                hh -= 8f * (1f - Smooth(0f, 30f, Mathf.Abs(z - rz))) * (1f - 0.65f * bridgeMask);
            }
            return hh;
        }
        float h = Profile(z);
        float d = Mathf.Abs(x - RoadX(z));
        // The south end opens out (owner, 2026-11-20):
        // mountains belong on the valley's flanks, not
        // behind the player's spawn - every mountain term
        // below fades to nothing past the muster field.
        float openS = 1f - Smooth(780f, 935f, z);
        // Valley walls: the floor stays open for ~110u either
        // side of the road, then the country rises steeply.
        // The wall height varies along the run (shoulders in
        // one stretch, towers in the next) and ridged noise
        // breaks the silhouette, so the mountains stop
        // repeating (owner, 2026-11-20).
        float wallAmp = 150f * (0.72f + 0.55f * Noise1(z * 0.0016f));
        h += Mathf.Pow(Smooth(110f, 430f, d), 1.5f) * wallAmp * openS;
        float ridge = 1f - Mathf.Abs(Fbm(x * 0.011f, z * 0.011f) * 2f - 1f);
        h += ridge * ridge * 52f * Smooth(120f, 260f, d) * (1f - Smooth(700f, 940f, d)) * openS;
        h += (Fbm(x * 0.045f, z * 0.045f) - 0.5f) * 14f * Smooth(90f, 220f, d) * openS;
        // Side canyons: gullies notched into the walls.
        float signedD = x - RoadX(z);
        foreach (var (gz, side) in Gullies)
        {
            if (signedD * side <= 0f) continue;
            float dz = Mathf.Abs(z - gz);
            h -= 30f * (1f - Smooth(0f, 70f, dz)) * Smooth(120f, 210f, d)
                 * (1f - Smooth(430f, 540f, d)) * openS;
        }
        // Gentle analytic detail on the slopes only - the road
        // corridor and the sites stay clean.
        h += Mathf.Sin(x * 0.021f + Mathf.Sin(z * 0.017f) * 2f) * Mathf.Cos(z * 0.019f)
            * 6f * Smooth(80f, 260f, d) * openS;
        // The map rim.
        float sq = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
        h += Smooth(860f, 950f, sq) * 70f * openS;
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
