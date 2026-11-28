using Godot;
using Titans.Definitions;

namespace Titans;

// Babylon (New York, Suffolk): the valley campaign map -
// one long valley climbing from the lowland mouth in the
// south to the summit camp on the plateau in the north.
// Six villages sit along the climb, each with its own
// guard group; two rivers cross the valley floor, each
// forded where the road crosses (the deck bridges were
// removed with every other construction, 2026-11-27). The scenario is
// non-linear by construction: every village guards only its
// own ground (the standard guard gate), so the player can
// clear the valley village by village, slip past them all,
// or drive straight up the road to the main battlefield on
// the summit, where ten defenders wait (the two seats plus
// eight guards).
//
// This file is the map's DEFINITION - its layout table and
// height recipe. Map definitions live one per file under
// source/maps/; the shared generation helpers (noise,
// smoothstep) live in TerrainMath, and the builders (Main's
// heightfield, the Titans.Babylon dressing, the guard
// spawner) all read THIS table, so the ground, the dressing
// and the defenders can never disagree about where a
// village is. (The map was built as "the Ascent" in
// 2026-11-20; the contract row's MapKind.Babylon is what
// selects it now - the old WorldConfig.Ascent flag is gone.)
public static class Babylon
{
  // One village: centre, flatten radius, guard count.
  public readonly struct Site
  {
    public readonly Vector2 C; public readonly float R; public readonly int Guards;
    public Site(float x, float z, float r, int guards) { this.C = new Vector2(x, z); this.R = r; this.Guards = guards; }
  }

  // The road: a winding S-curve up the valley's long axis -
  // two sine harmonics summed, so the run bends for real
  // instead of reading as a straight canyon with a lean
  // (owner, 2026-11-20). The long harmonic (150u sway,
  // ~2990u wavelength - longer than the 1900u world, so it
  // sweeps once) lays the valley's broad bend; the short
  // one (85u, ~1185u) adds the second, tighter curve of the
  // S. The phases (0.4 / 1.7 rad) are where the bends fall
  // along the run - picked so the road passes each village
  // site and both river bridges.
  private const float RoadSwayLong = 150f, RoadSwayLongFreq = 0.0021f, RoadSwayLongPhase = 0.4f;
  private const float RoadSwayShort = 85f, RoadSwayShortFreq = 0.0053f, RoadSwayShortPhase = 1.7f;
  public static float RoadX(float z)
      => RoadSwayLong * Mathf.Sin(z * RoadSwayLongFreq + RoadSwayLongPhase)
       + RoadSwayShort * Mathf.Sin(z * RoadSwayShortFreq + RoadSwayShortPhase);

  // Elevation along the road: a smoothstep climb from the
  // valley mouth (z = +820, h = 0) to the summit plateau
  // (z = 820 - 1600 = -780, h = 168). The smoothstep's flat
  // ends are the point: the mouth and the summit apron are
  // level muster ground, the grade steepens mid-valley.
  private const float ProfileMouthZ = 820f;
  private const float ProfileRunLength = 1600f;
  private const float ProfileClimbHeight = 168f;
  public static float Profile(float z)
  {
    float t = Mathf.Clamp((ProfileMouthZ - z) / ProfileRunLength, 0f, 1f);
    return ProfileClimbHeight * t * t * (3f - 2f * t);
  }

  // Babylon's own octave recipe over the shared value
  // noise: three octaves with weights 0.55 / 0.30 / 0.15
  // (they sum to 1, so the output stays in the noise's own
  // 0..1 range), each about 2.13x finer than the last, with
  // irrational offsets so the octaves never align. (The
  // generic octave walk in TerrainMath.Fbm halves its
  // amplitudes instead - a different recipe; this weighting
  // is part of this map's terrain character.)
  private static float Fbm(float x, float z)
      => TerrainMath.ValueNoise(x, z) * 0.55f
       + TerrainMath.ValueNoise(x * 2.13f + 7.3f, z * 2.13f + 3.1f) * 0.30f
       + TerrainMath.ValueNoise(x * 4.31f + 13.7f, z * 4.31f + 9.2f) * 0.15f;

  // One-dimensional noise: a slice of the value noise at a
  // fixed, non-integer z, so a value that varies along one
  // axis (the wall height along the valley) never correlates
  // with the 2-D detail built from the same field.
  private static float Noise1(float t) => TerrainMath.ValueNoise(t, 3.77f);

  // River centrelines (z), south to north.
  public static readonly float[] Rivers = { 450f, -300f };

  // The villages, south to north: the climb gets richer and
  // better defended the higher it goes (2, 3, 3, 4, 4, 5).
  // Each centre sits a fixed offset off the road at its z,
  // alternating banks.
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

  // World pads for a Babylon mission: home, then the two
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

  // Side canyons notched into the walls: (z, side sign).
  private static readonly (float Z, float Side)[] Gullies =
      { (120f, -1f), (-60f, 1f), (-560f, -1f) };

  // Hills terrain (the owner's Maps-Hills OBJ, 2026-11-22):
  // a 512x512 float grid baked from the OBJ's top surface
  // (its solid base and side walls excluded), stretched
  // over the whole 1900u world with the relief tripled -
  // the OBJ spans 200u with 52u of relief natively, the
  // grid carries ~156u over 1900u. RawBase samples it in
  // place of the analytic valley when WorldConfig.Hills
  // is on; the site flattening in Height and the river
  // carves apply unchanged, so the villages, the road
  // dressing and the bridges sit on the new ground.
  private const int HillsGridSize = 512; // the baked hills grid is 512x512 floats
  private const float HillsGridMaxCoord = 510.999f; // last bilinear sample coordinate (grid size - 1.001)
  private const float HillsWorldSize = 1900f; // the grid stretches over the whole mission world
  private static float[] _hills;
  private static bool _hillsTried;

  private static float HillsSample(float x, float z)
  {
    if (!_hillsTried)
    {
      _hillsTried = true;
      using var f = FileAccess.Open(Paths.HillsHeightmap, FileAccess.ModeFlags.Read);
      if (f != null)
      {
        var bytes = f.GetBuffer(HillsGridSize * HillsGridSize * 4);
        if (bytes.Length == HillsGridSize * HillsGridSize * 4)
        {
          _hills = new float[HillsGridSize * HillsGridSize];
          System.Buffer.BlockCopy(bytes, 0, _hills, 0, bytes.Length);
        }
      }
    }
    if (_hills == null)
    {
      return 0f;
    }
    // World -> grid: shift by half the world, scale to the
    // grid, then half a texel back so samples land on
    // texel centres.
    float fx = Mathf.Clamp((x + HillsWorldSize * 0.5f) / HillsWorldSize * HillsGridSize - 0.5f, 0f, HillsGridMaxCoord);
    float fz = Mathf.Clamp((z + HillsWorldSize * 0.5f) / HillsWorldSize * HillsGridSize - 0.5f, 0f, HillsGridMaxCoord);
    int ix = (int)fx, iz = (int)fz;
    float tx = fx - ix, tz = fz - iz;
    float top = _hills[iz * HillsGridSize + ix] * (1f - tx) + _hills[iz * HillsGridSize + ix + 1] * tx;
    float bot = _hills[(iz + 1) * HillsGridSize + ix] * (1f - tx) + _hills[(iz + 1) * HillsGridSize + ix + 1] * tx;
    return top * (1f - tz) + bot * tz;
  }

  // Rivers: a channel carved across the whole valley,
  // shallower under the bridge span (the deck there is
  // registered by the environment build).
  private const float RiverDepth = 8f;
  private const float RiverHalfWidth = 30f;
  private const float BridgeHalfSpanInner = 20f, BridgeHalfSpanOuter = 36f; // the carve shallows within the deck's span
  private const float BridgeShallowFraction = 0.65f; // how much of the carve the bridge span removes
  private static float CarveRivers(float h, float x, float z)
  {
    foreach (float rz in Rivers)
    {
      float bridgeMask = 1f - TerrainMath.Smooth(BridgeHalfSpanInner, BridgeHalfSpanOuter, Mathf.Abs(x - RoadX(rz)));
      h -= RiverDepth * (1f - TerrainMath.Smooth(0f, RiverHalfWidth, Mathf.Abs(z - rz))) * (1f - BridgeShallowFraction * bridgeMask);
    }
    return h;
  }

  // The unflattened ground: profile + valley walls + rim +
  // river carves (or, with Hills on, the baked grid).
  private static float RawBase(float x, float z)
  {
    if (WorldConfig.Hills)
    {
      float hh = HillsSample(x, z);
      return CarveRivers(hh, x, z);
    }
    float h = Profile(z);
    float d = Mathf.Abs(x - RoadX(z));
    // The south end opens out (owner, 2026-11-20):
    // mountains belong on the valley's flanks, not
    // behind the player's spawn - every mountain term
    // below fades to nothing past the muster field.
    float openS = 1f - TerrainMath.Smooth(780f, 935f, z);
    // Valley walls: the floor stays open for ~110u either
    // side of the road, then the country rises steeply.
    // The wall height varies along the run (shoulders in
    // one stretch, towers in the next) and ridged noise
    // breaks the silhouette, so the mountains stop
    // repeating (owner, 2026-11-20).
    float wallAmp = 150f * (0.72f + 0.55f * Noise1(z * 0.0016f));
    h += Mathf.Pow(TerrainMath.Smooth(110f, 430f, d), 1.5f) * wallAmp * openS;
    float ridge = 1f - Mathf.Abs(Fbm(x * 0.011f, z * 0.011f) * 2f - 1f);
    h += ridge * ridge * 52f * TerrainMath.Smooth(120f, 260f, d) * (1f - TerrainMath.Smooth(700f, 940f, d)) * openS;
    h += (Fbm(x * 0.045f, z * 0.045f) - 0.5f) * 14f * TerrainMath.Smooth(90f, 220f, d) * openS;
    // Side canyons: gullies notched into the walls.
    float signedD = x - RoadX(z);
    foreach (var (gz, side) in Gullies)
    {
      if (signedD * side <= 0f)
      {
        continue;
      }

      float dz = Mathf.Abs(z - gz);
      h -= 30f * (1f - TerrainMath.Smooth(0f, 70f, dz)) * TerrainMath.Smooth(120f, 210f, d)
           * (1f - TerrainMath.Smooth(430f, 540f, d)) * openS;
    }
    // Gentle analytic detail on the slopes only - the road
    // corridor and the sites stay clean.
    h += Mathf.Sin(x * 0.021f + Mathf.Sin(z * 0.017f) * 2f) * Mathf.Cos(z * 0.019f)
        * 6f * TerrainMath.Smooth(80f, 260f, d) * openS;
    // The map rim.
    float sq = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
    h += TerrainMath.Smooth(860f, 950f, sq) * 70f * openS;
    return CarveRivers(h, x, z);
  }

  private static readonly float[] SiteH = BuildSiteH();

  private static float[] BuildSiteH()
  {
    var hs = new float[Sites.Length + 2];
    for (int i = 0; i < Sites.Length; i++)
    {
      hs[i] = RawBase(Sites[i].C.X, Sites[i].C.Y);
    }

    hs[Sites.Length] = RawBase(Home.X, Home.Y);
    hs[Sites.Length + 1] = RawBase(Summit.X, Summit.Y);
    return hs;
  }

  public static float SummitH => SiteH[Sites.Length + 1];

  // The heightfield entry (BabylonMap.RawHeight routes
  // here): RawBase with every site, the home muster and
  // the summit flattened toward its centre height -
  // villages and the citadel stand on level ground.
  private const float HomeFlatRadius = 100f; // the muster field flattens over this radius, fully level inside half of it
  private const float SummitFlatInner = 140f; // inside this radius the summit is fully at its centre height
  public static float Height(float x, float z)
  {
    float h = RawBase(x, z);
    var p = new Vector2(x, z);
    for (int i = 0; i < Sites.Length; i++)
    {
      float d = p.DistanceTo(Sites[i].C);
      if (d < Sites[i].R)
      {
        h = Mathf.Lerp(h, SiteH[i], TerrainMath.Smooth(Sites[i].R, Sites[i].R * 0.5f, d));
      }
    }
    float dh = p.DistanceTo(Home);
    if (dh < HomeFlatRadius)
    {
      h = Mathf.Lerp(h, SiteH[Sites.Length], TerrainMath.Smooth(HomeFlatRadius, HomeFlatRadius * 0.5f, dh));
    }

    float ds = p.DistanceTo(Summit);
    if (ds < SummitR)
    {
      h = Mathf.Lerp(h, SiteH[Sites.Length + 1], TerrainMath.Smooth(SummitR, SummitFlatInner, ds));
    }

    return h;
  }
}

// Babylon as a map (owner, 2026-11-27): the definition
// above, presented through the MapDef contract - its own
// height recipe, its own pads, chunked terrain for the
// streaming pass, the river dressing, the village guard
// muster, and streamed statics.
public sealed class BabylonMap : MapDef
{
  public override Vector2[] Pads => Babylon.Pads;
  public override float RawHeight(float x, float z) => Babylon.Height(x, z);
  public override int TerrainChunks => 4;
  public override bool StreamsStatics => true;
  public override void BuildDressing(Titans world) => world.BuildBabylonWorld();
  public override void MusterGuards(Titans world) => world.MusterBabylonGuards();
}
