using Godot;

// The map contract (owner, 2026-11-27): everything specific
// to one map lives in that map's own class under maps/ -
// its height recipe, its pads, its roads, its dressing, its
// guard muster. Generic code (Main, the walkers, the hit
// model, the flow) talks to the ACTIVE map through MapDef
// only: no IF or SWITCH anywhere checks a map's name or
// type. MapFactory.Create is the single place that picks
// the class, and it reads configuration DATA (the contract
// row's map kind, the mode, the flat flag), never a name.
//
// Which maps exist: BabylonMap (maps/Babylon.cs), MissionMap
// (maps/MissionMap.cs), ArenaMap (maps/Arena.cs), FlatMap
// (below). The shared relief recipe (hills, ridge, valley,
// ring, centre dome) lives here as protected statics because
// the arena and the mission front are two tunings of one
// recipe; each map class applies its own pad flattening and
// extras on top.
public enum MapKind { Standard, Babylon }

public abstract class MapDef
{
    // The spawn pads (index 0 is the player's).
    public abstract Vector2[] Pads { get; }

    // The map's analytic ground height (Main's grid is built
    // from it; Main.HeightAt samples the grid).
    public abstract float RawHeight(float x, float z);

    // Distance to the nearest road centreline, for the
    // terrain colouring. The default network joins the pads
    // in a triangle; a map with a different road shape
    // overrides it.
    public virtual float RoadDistance(float x, float z)
    {
        var p = Pads;
        float d = TerrainMath.DistToSegment(x, z, p[0].X, p[0].Y, p[1].X, p[1].Y);
        d = Mathf.Min(d, TerrainMath.DistToSegment(x, z, p[1].X, p[1].Y, p[2].X, p[2].Y));
        d = Mathf.Min(d, TerrainMath.DistToSegment(x, z, p[2].X, p[2].Y, p[0].X, p[0].Y));
        return d;
    }

    // Street-grid distance of a city district; only the
    // mission front has a district, the rest report the
    // sentinel.
    public const float NoStreetDistance = 1e6f; // "no street here"
    public virtual float StreetDistance(float x, float z) => NoStreetDistance;

    // The terrain mesh is built in this many chunks per side
    // (a streamed world unloads far chunks; the rest build
    // one mesh).
    public virtual int TerrainChunks => 1;

    // World dressing the map builds for itself inside the
    // Titans scene (default: none).
    public virtual void BuildDressing(Titans world) { }

    // The map's guard muster, if it fields any (default:
    // none). Called for every world; the harness gate sits
    // at the call site.
    public virtual void MusterGuards(Titans world) { }

    // Whether the world's statics register for sector
    // streaming at lab entry.
    public virtual bool StreamsStatics => false;

    // ---- The shared relief recipe (the arena and the plain
    // mission fronts): rolling hills everywhere, a craggy
    // ridge through the NW, a shallow valley through the SE,
    // a mountain ring at the rim, and the centre dome on top.
    // Field scales are the noise features' size in world
    // units; the +offset in each sample is a decorrelation
    // shift so two fields never align.
    private const float HillsFieldScale = 58f, HillsAmplitude = 6.5f;
    private const int HillsOctaves = 5;
    private const float MacroFieldScale = 420f, MacroAmplitude = 26f; // mission-only broad swells
    private const int MacroOctaves = 4;
    private const float MacroFadeStart = 120f, MacroFadeEnd = 300f;   // macro swells fade in over this radius
    private static readonly Vector2 RidgeStart = new(-88f, -28f), RidgeEnd = new(-24f, -88f); // the NW ridge line
    private const float RidgeWidth = 15f, RidgeBaseHeight = 5f, RidgeCragHeight = 7f;
    private const float CragFieldScale = 21f;
    private const int CragOctaves = 3;
    private static readonly Vector2 ValleyStart = new(88f, 34f), ValleyEnd = new(28f, -88f); // the SE valley line
    private const float ValleyDepth = 3.6f, ValleyWidth = 13f;
    private const float RingInsetFar = 42f, RingInsetNear = 2f; // the mountain ring rises over the last 40u to the rim
    private const float MountainBaseHeight = 9f, MountainVarHeight = 7f;
    private const float MountainFieldScale = 37f;
    private const int MountainOctaves = 4;
    private const float ForestHillsFactor = 0.45f, ForestRidgeFactor = 0.35f, ForestMountainFactor = 0.5f; // forest arenas run gentle (see below)
    private const float CenterAmplitudeFloor = 0.22f; // arena relief at the centre, as a fraction of full
    private const float FullAmplitudeStart = 54f, FullAmplitudeEnd = 94f; // ... reaching full over this radius

    protected static float BaseRelief(float x, float z, bool missionFront)
    {
        float r = Mathf.Sqrt(x * x + z * z);
        // Rolling hills, the base relief everywhere.
        float hills = TerrainMath.Fbm(x / HillsFieldScale, z / HillsFieldScale, HillsOctaves, Main.NoiseSeed) * HillsAmplitude;
        // Mission fronts get broad macro swells under the detail
        // noise, so a 1900u crossing has real country in it. Damped
        // near the center so the mesa keeps its clean plateau.
        if (missionFront)
            hills += TerrainMath.Fbm(x / MacroFieldScale + 7f, z / MacroFieldScale - 3f, MacroOctaves, Main.NoiseSeed) * MacroAmplitude * TerrainMath.Smooth(MacroFadeStart, MacroFadeEnd, r);
        // Craggy ridge line through the NW quadrant.
        float dRidge = TerrainMath.DistToSegment(x, z, RidgeStart.X, RidgeStart.Y, RidgeEnd.X, RidgeEnd.Y);
        float crag = 1f - Mathf.Abs(TerrainMath.Fbm(x / CragFieldScale + 40f, z / CragFieldScale - 17f, CragOctaves, Main.NoiseSeed));
        float ridge = Mathf.Exp(-(dRidge * dRidge) / (2f * RidgeWidth * RidgeWidth)) * (RidgeBaseHeight + RidgeCragHeight * crag);
        // Shallow valley through the SE quadrant.
        float dVal = TerrainMath.DistToSegment(x, z, ValleyStart.X, ValleyStart.Y, ValleyEnd.X, ValleyEnd.Y);
        float valley = -ValleyDepth * Mathf.Exp(-(dVal * dVal) / (2f * ValleyWidth * ValleyWidth));
        // Mountain ring bounding the map (square falloff follows the rim).
        float sq = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
        float half = Main.TerrainSize / 2f;
        float m = TerrainMath.Smooth(half - RingInsetFar, half - RingInsetNear, sq);
        float mtn = m * (MountainBaseHeight + MountainVarHeight * (TerrainMath.Fbm(x / MountainFieldScale - 9f, z / MountainFieldScale + 23f, MountainOctaves, Main.NoiseSeed) * 0.5f + 0.5f));
        // Forest arenas read as woodland, not a mountain range
        // (owner, 2026-11-14: Hyle "looks more like mountains"):
        // the relief set runs at half strength there, so slopes
        // stay gentle and the splat's rock gives way to grass.
        // The mesa dome below is untouched - it is the arena's
        // landmark. Missions and the other arenas keep theirs.
        if (!missionFront && WorldConfig.Biome == WorldConfig.BiomeKind.Forest)
        {
            hills *= ForestHillsFactor; ridge *= ForestRidgeFactor; mtn *= ForestMountainFactor;
        }
        float full = hills + ridge + valley + mtn;
        // Arena modulation: the centre keeps only the floor
        // fraction of the relief, full by the outer radius.
        // The Mission front keeps full amplitude everywhere.
        float t = missionFront ? 1f : TerrainMath.Smooth(FullAmplitudeStart, FullAmplitudeEnd, r);
        return full * (CenterAmplitudeFloor + (1f - CenterAmplitudeFloor) * t) + DomeHeight(x, z);
    }

    // The center dome (owner, 2026-10-31): a broad hill in the
    // middle of the map, ~34u over the arena floor, so the three
    // spawn pads have no sightline to each other. Measured
    // geometry: every pad pair's sightline passes r=40 from the
    // center, where the dome still stands ~23u - above the
    // highest pad eye line (pads flatten to at most 3u, +~11.5
    // eye height). Added AFTER the arena modulation so the
    // center damping cannot flatten it away. The top is a mesa
    // (flat inside r=14): the old pure Gaussian put the center
    // test marks on a 2.3u relative slope, which moved shot
    // attribution from torso to thigh - a fighting plateau is
    // also simply better ground to duel on.
    private const float DomeFlatRadius = 14f;  // the mesa top: flat inside this radius
    private const float DomePeakHeight = 34f;  // the dome's height over the arena floor
    private const float DomeFalloff = 30f;     // Gaussian sigma of the skirt beyond the flat top
    protected static float DomeHeight(float x, float z)
    {
        float r = Mathf.Sqrt(x * x + z * z);
        float rr = Mathf.Max(0f, r - DomeFlatRadius);
        return DomePeakHeight * Mathf.Exp(-(rr * rr) / (2f * DomeFalloff * DomeFalloff));
    }

    // Pad flattening shared by the relief maps: each pad
    // flattens toward its own centre height inside the
    // flatten radius, fully level inside the inner fraction.
    protected const float PadFlatInnerFraction = 0.44f; // a pad is fully at its centre height inside this fraction of the flatten radius
    protected float FlattenPads(float h, float x, float z, bool missionFront, float arenaPadMaxHeight)
    {
        float flatR = WorldConfig.PadFlattenRadius;
        foreach (var pad in Pads)
        {
            float d = new Vector2(x, z).DistanceTo(pad);
            if (d < flatR)
            {
                // Arena pads sit LOW (at most the cap) whatever the
                // local relief says - the hollows the dome math
                // assumes. Mission bases flatten to their own
                // plateau height.
                float padH = missionFront
                    ? BaseRelief(pad.X, pad.Y, true)
                    : Mathf.Min(BaseRelief(pad.X, pad.Y, false), arenaPadMaxHeight);
                h = Mathf.Lerp(h, padH, TerrainMath.Smooth(flatR, flatR * PadFlatInnerFraction, d));
            }
        }
        return h;
    }
}

// The flat world (owner): a contract whose ground must all
// be level skips the height map entirely - every point at
// height 0. Its pads follow the mode it was booted in.
public sealed class FlatMap : MapDef
{
    private readonly Vector2[] _pads;
    public FlatMap(Vector2[] pads) => _pads = pads;
    public override Vector2[] Pads => _pads;
    public override float RawHeight(float x, float z) => 0f;
}

// The one place a map class is chosen (owner, 2026-11-27):
// from the configuration DATA - the flat flag first (a flat
// contract overrides its own recipe, exactly as the old
// RawHeight order had it: the Babylon check came before the
// flat check there, so Babylon is tested first here too),
// then the mission's map kind, then the mode.
public static class MapFactory
{
    public static MapDef Create()
    {
        if (WorldConfig.IsMission && WorldConfig.Map == MapKind.Babylon)
            return new BabylonMap();
        if (WorldConfig.FlatWorld)
            return new FlatMap(WorldConfig.IsMission ? MissionMap.PadsTable : ArenaMap.PadsTable);
        if (WorldConfig.IsMission)
            return new MissionMap();
        return new ArenaMap();
    }
}

// The active map for this process. Built lazily on first
// use, like Main's grid: the flow sets WorldConfig before
// the duel scene loads, and whichever world is current at
// first access is the world this process runs.
public static class Maps
{
    private static MapDef _active;
    public static MapDef Active => _active ??= MapFactory.Create();
}
