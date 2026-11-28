namespace Titans;

// Which world the duel scene builds, chosen in the front-end flow
// (map type -> planet -> bar -> lab) before the duel scene loads. The
// harness can force it with the AOT_MODE environment variable or
// the --mission argument. Default is Tournament, so every existing
// tool, test and editor run boots the arena exactly as before.
public static class WorldConfig
{
  public enum MapMode { Tournament, Mission }

  public static MapMode Mode = InitMode();

  // The map type (owner): every map - a tournament arena or a
  // mission contract's ground - is one of three biomes,
  // PREDEFINED by that arena/contract (never a player choice;
  // EnterLab copies the selected map's biome here). Default
  // Forest, which is the game's
  // original look, so harness runs and tests build exactly the
  // world they always have. The harness can force AOT_BIOME.
  public enum BiomeKind { Desert, Forest, City }

  public static BiomeKind Biome = InitBiome();

  private static BiomeKind InitBiome() =>
      EnvVars.Value(EnvVars.Biome) switch
      {
        "desert" => BiomeKind.Desert,
        "city" => BiomeKind.City,
        _ => BiomeKind.Forest
      };

  // The contract signed in the flow (Mission path). Null on the
  // Tournament path and in harness runs.
  public static MissionDef SelectedMission;

  // The building kit for City maps (owner): each mission
  // contract names the kit its city is built from (real
  // building models converted from the map archives the owner
  // supplied, assets/citykits/kit<N>/). -1 = the procedural
  // buildings. EnterLab copies the contract's kit here;
  // AOT_CITYKIT forces one for harnesses.
  public static int CityKit = InitCityKit();

  private static int InitCityKit() =>
      int.TryParse(EnvVars.Value(EnvVars.CityKit), out var k)
          ? k : -1;

  // The picked map's painted sky (owner: supplied images on
  // City maps). EnterLab copies the contract's / arena's
  // SkyTex here; AOT_SKY=<file name in assets/skies/> forces
  // one for harnesses. "" = the procedural sky.
  public static string SkyTex = InitSky();

  // Procedural star layer over the painted sky (owner: hybrid
  // sky). EnterLab copies the contract's flag here;
  // AOT_STARS=1 forces it for harnesses.
  public static bool SkyStars = InitStars();

  private static bool InitStars() =>
      EnvVars.Flag(EnvVars.Stars);

  // Snow-covered ground (owner): the terrain wears snow
  // where the map's painted sky is a snowscape. EnterLab
  // copies the contract's flag here; AOT_SNOW=1 forces it.
  public static bool SnowGround = InitSnow();

  // Which map the mission is fought on (owner, 2026-11-27:
  // a data value the MapFactory reads - it replaced the
  // Ascent bool, which was a map's NAME worn as a flag).
  // EnterLab copies the contract's kind here; the
  // AOT_ASCENT env var (its historical wire name) forces
  // Babylon for harness boots.
  public static MapKind Map = InitMap();

  // Hills terrain (owner's Maps-Hills OBJ, 2026-11-22): the
  // Ascent heightfield's base is sampled from a grid baked
  // out of the OBJ instead of the analytic valley.
  // AOT_HILLS=1 forces it.
  public static bool Hills = InitHills();

  // The photoreal treatment in the standard scene
  // (owner, 2026-11-20): EnterLab copies the contract's
  // Photoreal flag here; AOT_PHOTOREAL=1 forces it.
  public static bool Photoreal = InitPhotoreal();

  private static bool InitPhotoreal() =>
      EnvVars.Flag(EnvVars.Photoreal);

  private static MapKind InitMap() =>
      EnvVars.Flag(EnvVars.Ascent) ? MapKind.Babylon : MapKind.Standard;

  private static bool InitHills() =>
      EnvVars.Flag(EnvVars.Hills);

  private static bool InitSnow() =>
      EnvVars.Flag(EnvVars.Snow);

  private static string InitSky()
  {
    var f = EnvVars.Value(EnvVars.Sky);
    return string.IsNullOrEmpty(f) ? "" : Paths.SkiesDir + f;
  }

  // Tiled sky (owner, 2026-11-25): a seamless tile repeated
  // over the dome by the triplanar sky shader instead of a
  // stretched panorama. EnterLab copies the contract's
  // SkyTile here; AOT_SKYTILE=<file name> forces one.
  public static string SkyTile = InitSkyTile();

  private static string InitSkyTile()
  {
    var f = EnvVars.Value(EnvVars.SkyTile);
    return string.IsNullOrEmpty(f) ? "" : Paths.SkiesDir + f;
  }

  // Flat maps (owner): a contract whose streets must all be
  // level - The Harbor Stacks - skips the height map entirely;
  // its world builds at height 0 everywhere. EnterLab copies
  // the contract's flag here; AOT_FLAT=1 forces it.
  public static bool FlatWorld = InitFlat();

  private static bool InitFlat() =>
      EnvVars.Flag(EnvVars.Flat);

  private static MapMode InitMode() =>
      EnvVars.Value(EnvVars.Mode) == EnvVars.ModeMission
          ? MapMode.Mission : MapMode.Tournament;

  public static bool IsMission => Mode == MapMode.Mission;

  // Front-door reset (owner, 2026-11-15): leaving a match
  // and starting a new Mission or Tournament must not
  // inherit the previous run's map - every selection this
  // class carries returns to its boot default (the harness
  // environment seeds included). Story progression is NOT
  // a selection and is untouched.
  public static void ResetSelections()
  {
    Mode = InitMode();
    Biome = InitBiome();
    SelectedMission = null;
    CityKit = InitCityKit();
    SkyTex = InitSky();
    SkyTile = InitSkyTile();
    SkyStars = InitStars();
    SnowGround = InitSnow();
    FlatWorld = InitFlat();
    Map = InitMap();
    Hills = InitHills();
    Photoreal = InitPhotoreal();
  }

  // Tournament: the 260u arena. Mission: a 1900u front - bases sit
  // ~1075u apart, a ~4.5 minute walk or ~90 seconds at a run for a
  // Heavy rider, so crossing the map is a decision, not a stroll.
  public const int MissionWorldSize = 1900;
  public const int TournamentWorldSize = 260;
  public const int MissionTerrainSegs = 512;
  public const int TournamentTerrainSegs = 384;
  public const float MissionWorldBound = 915f;
  public const float TournamentWorldBound = 115f;
  public const float MissionPadFlattenRadius = 60f;
  public const float TournamentPadFlattenRadius = 18f;
  public static int WorldSize => IsMission ? MissionWorldSize : TournamentWorldSize;
  public static int TerrainSegs => IsMission ? MissionTerrainSegs : TournamentTerrainSegs;
  public static float WorldBound => IsMission ? MissionWorldBound : TournamentWorldBound;
  public static float PadFlattenRadius => IsMission ? MissionPadFlattenRadius : TournamentPadFlattenRadius;


}

// Tournament arenas: fixed maps, each with its own predefined
// biome (owner's correction). MapScreen displays these;
// FlowMain.EnterLab applies the picked arena's biome.
public static class ArenaCatalog
{
  public sealed class ArenaDef
  {
    public string Name, Sub, Desc;
    public WorldConfig.BiomeKind Biome;
    // Painted panorama sky (see MissionDef.SkyTex). "" = procedural.
    public string SkyTex = "";
  }

  public static readonly ArenaDef[] Arenas =
  {
        new ArenaDef
        {
            Name = "Hyle", Biome = WorldConfig.BiomeKind.Forest,
            Sub = "Forest · Tournament arena · 0.26 km",
            Desc = "Rolling woodland. Tree crowns stop shells, rocks stop nothing but the eye.\n\n" +
                   "Hyle: a 260-unit basin ringed by high ground, with a flat-topped mesa at its heart. " +
                   "Teams are formed on the next step; the last team standing takes the purse and the ground."
        },
        new ArenaDef
        {
            Name = "Eremos", Biome = WorldConfig.BiomeKind.Desert,
            Sub = "Desert · Tournament arena · 0.26 km",
            Desc = "Sand and rock under a hard sun. Long sight lines, almost no cover but the mesa and the ruins.\n\n" +
                   "Eremos: a 260-unit desert basin ringed by high ground, with a flat-topped mesa at its heart. " +
                   "Teams are formed on the next step; the last team standing takes the purse and the ground."
        },
        new ArenaDef
        {
            Name = "Polis", Biome = WorldConfig.BiomeKind.City,
            SkyTex = Paths.SkyJungle,
            Sub = "City · Tournament arena · 0.26 km",
            Desc = "A dead city of towers and blocks. Streets channel movement, roofs are high ground, every building falls.\n\n" +
                   "Polis: a 260-unit city basin ringed by high ground, with a flat-topped mesa at its heart. " +
                   "Teams are formed on the next step; the last team standing takes the purse and the ground."
        }
    };
}
