using Godot;

namespace Titans;

// The tournament arena's own definition data (owner,
// 2026-11-27: map-specific data lives with the map, like
// Babylon.cs - the shared environment
// code holds only the machinery that reads it).
public static class Arena
{
    // Scatter keep-out lanes around the arena origin
    // (x0, x1, z0, z1): the tournament test choreography
    // rides these lanes, so scatter stays out of them.
    public static readonly (float X0, float X1, float Z0, float Z1)[] ScatterLanes =
    {
        (-5.5f, 5.5f, -23f, 37f),
        (16f, 28f, -23f, 31f),
        (2f, 10.5f, 9f, 33f)
    };

    // Scatter keep-out pads (centre x/z, radius): the
    // choreography's standing spots.
    public static readonly (float X, float Z, float R)[] ScatterPads =
    {
        (0f, 30f, 7f), (0f, -16f, 7f), (0f, 18f, 7f), (0f, -12f, 7f),
        (0f, 20f, 7f), (0f, -10f, 7f), (22f, 24f, 7f), (22f, -16f, 7f),
        (-38f, 38f, 7.5f), (0f, 24f, 7f), (20f, 2f, 6f), (6f, 27f, 5.5f),
        (0f, 10f, 6f), (0f, -2f, 6f)
    };
}

// The tournament arena as a map (owner, 2026-11-27): the
// shared relief recipe with the arena's centre modulation
// and forest tuning, pads flattened LOW (the dome math's
// hollows), and the road cross through (-14, 6) as its road
// shape instead of the pad triangle.
public sealed class ArenaMap : MapDef
{
    public static readonly Vector2[] PadsTable =
    {
        new(0f, 80f), new(-69.3f, -40f), new(69.3f, -40f)
    };
    public override Vector2[] Pads => PadsTable;

    private const float ArenaPadMaxHeight = 3f; // arena pads sit low (see the DomeHeight note)
    private const float ArenaRoadCrossX = -14f, ArenaRoadCrossZ = 6f; // the two road strips cross here

    public override float RawHeight(float x, float z)
    {
        float h = BaseRelief(x, z, false);
        // Flatten each spawn pad toward its own center height, so
        // a rider starts on level ground in its hollow.
        return FlattenPads(h, x, z, false, ArenaPadMaxHeight);
    }

    public override float RoadDistance(float x, float z) =>
        Mathf.Min(Mathf.Abs(z - ArenaRoadCrossZ), Mathf.Abs(x - ArenaRoadCrossX));
}
