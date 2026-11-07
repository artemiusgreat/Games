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
    // mission contract's ground — is one of three biomes, picked
    // on the map screen. Default Forest, which is the game's
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
