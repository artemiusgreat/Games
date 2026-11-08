using Godot;

// Which world the duel scene builds, chosen in the front-end flow
// (map type -> planet -> bar -> lab) before PhotoDuel loads. The
// harness can force it with the AOT_MODE environment variable or
// the --mission argument. Default is Tournament, so every existing
// tool, test and editor run boots the arena exactly as before.
public static class WorldConfig
{
    public enum MapMode { Tournament, Mission }

    public static MapMode Mode = InitMode();

    // The map type (owner): every map — a tournament arena or a
    // mission contract's ground — is one of three biomes,
    // PREDEFINED by that arena/contract (never a player choice;
    // EnterLab copies the selected map's biome here). Default
    // Forest, which is the game's
    // original look, so harness runs and tests build exactly the
    // world they always have. The harness can force AOT_BIOME.
    public enum BiomeKind { Desert, Forest, City }

    public static BiomeKind Biome = InitBiome();

    private static BiomeKind InitBiome() =>
        System.Environment.GetEnvironmentVariable("AOT_BIOME") switch
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
        int.TryParse(System.Environment.GetEnvironmentVariable("AOT_CITYKIT"), out var k)
            ? k : -1;

    // Flat maps (owner): a contract whose streets must all be
    // level — The Harbor Stacks — skips the height map entirely;
    // its world builds at height 0 everywhere. EnterLab copies
    // the contract's flag here; AOT_FLAT=1 forces it.
    public static bool FlatWorld = InitFlat();

    private static bool InitFlat() =>
        System.Environment.GetEnvironmentVariable("AOT_FLAT") == "1";

    private static MapMode InitMode() =>
        System.Environment.GetEnvironmentVariable("AOT_MODE") == "mission"
            ? MapMode.Mission : MapMode.Tournament;

    public static bool IsMission => Mode == MapMode.Mission;

    // Tournament: the 260u arena. Mission: a 1900u front — bases sit
    // ~1075u apart, a ~4.5 minute walk or ~90 seconds at a run for a
    // Heavy rider, so crossing the map is a decision, not a stroll.
    public static int WorldSize => IsMission ? 1900 : 260;
    public static int TerrainSegs => IsMission ? 512 : 384;
    public static float WorldBound => IsMission ? 915f : 115f;
    public static float PadFlattenRadius => IsMission ? 60f : 18f;

    public static Vector2[] Pads => IsMission ? MissionPads : TournamentPads;

    private static readonly Vector2[] TournamentPads =
    {
        new(0f, 80f), new(-69.3f, -40f), new(69.3f, -40f)
    };

    // The arena pad triangle scaled x7.75: r=620, 120 degrees apart.
    private static readonly Vector2[] MissionPads =
    {
        new(0f, 620f), new(-537f, -310f), new(537f, -310f)
    };
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
    }

    public static readonly ArenaDef[] Arenas =
    {
        new ArenaDef
        {
            Name = "The Proving Grounds", Biome = WorldConfig.BiomeKind.Forest,
            Sub = "Forest · Tournament arena · 260u · Three riders · Free-for-all",
            Desc = "Rolling woodland. Tree crowns stop shells, rocks stop nothing but the eye.\n\n" +
                   "The Proving Grounds: a 260-unit basin ringed by high ground, with a flat-topped mesa at its heart. " +
                   "Three pads, three riders, no teams. Last machine standing takes the purse and the ground."
        },
        new ArenaDef
        {
            Name = "The Dry Basin", Biome = WorldConfig.BiomeKind.Desert,
            Sub = "Desert · Tournament arena · 260u · Three riders · Free-for-all",
            Desc = "Sand and rock under a hard sun. Long sight lines, almost no cover but the mesa and the ruins.\n\n" +
                   "The Dry Basin: a 260-unit desert basin ringed by high ground, with a flat-topped mesa at its heart. " +
                   "Three pads, three riders, no teams. Last machine standing takes the purse and the ground."
        },
        new ArenaDef
        {
            Name = "The Ruined Mile", Biome = WorldConfig.BiomeKind.City,
            Sub = "City · Tournament arena · 260u · Three riders · Free-for-all",
            Desc = "A dead city of towers and blocks. Streets channel movement, roofs are high ground, every building falls.\n\n" +
                   "The Ruined Mile: a 260-unit city basin ringed by high ground, with a flat-topped mesa at its heart. " +
                   "Three pads, three riders, no teams. Last machine standing takes the purse and the ground."
        }
    };
}
