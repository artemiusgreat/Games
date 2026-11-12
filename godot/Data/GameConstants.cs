// One home for the values that repeat across the engine's
// files (owner: no magic strings or numbers scattered through
// the code). Pure data — no engine references, like the rest
// of the Data folder, so every subsystem reads the same values.

/// <summary>
/// Combat identities: who a shot belongs to and what a hit
/// resolved against. The hit model emits these as its target
/// vocabulary; damage attribution, gunnery and the HUD all
/// compare against the same constants.
/// </summary>
public static class Roles
{
    public const string Player = "PLAYER";
    public const string Enemy = "ENEMY";
    public const string Foe = "FOE";
    public const string Prop = "PROP";
    public const string Terrain = "TERRAIN";
}

/// <summary>
/// Aim-cast reaches (world units). ConvergenceRange is the
/// crosshair's close cast: inside it, the aim point is the
/// cast's hit exactly. FarRange is the far cast and the HUD
/// rangefinder's reach — what weapons converge on when the
/// crosshair rests past the close cast, out to the Mission
/// world's long sightlines.
/// </summary>
public static class Aim
{
    public const float ConvergenceRange = 220f;
    public const float FarRange = 1500f;
}

/// <summary>Camera modes (the duel camera's state vocabulary).</summary>
public static class CamModes
{
    public const string Chase = "chase";
}

/// <summary>
/// The well-known hardpoint ids. Each rider's Slots table in
/// the roster data defines which of these it actually carries
/// (and their capacities); engine code that dispatches on a
/// hardpoint — muzzle resolution, pod lookup — reads these
/// constants instead of spelling the ids itself. The same ids
/// key the breakable-parts layouts.
/// </summary>
/// <summary>
/// Part-break rules (owner, 2026-11-12): a rider cannot lose
/// an ARM while its body is sound — early arm loss wrecked
/// the opening of a duel. Arm parts take damage as normal,
/// but the break itself is gated: an arm comes off only
/// once the main pool is at or below this fraction. (Legs
/// wreck and heads wreck under their own long-standing
/// rules; pods and the waist gun are not arms and are not
/// gated.)
/// </summary>
public static class PartRules
{
    public const float ArmBreakPoolFraction = 0.20f;
}

public static class SlotIds
{
    public const string ArmL = "arm_L";
    public const string ArmR = "arm_R";
    public const string Gun = "gun";
    public const string Torso = "torso";
    public const string PodL = "pod_L";
    public const string PodR = "pod_R";
}

// The city district built at the center of a kit contract's
// mission map (Harbor Stacks, Gate City): a street grid of
// building plots on a level plateau. Shared by the heightfield
// (Main), both ground colorings (Main.TerrainColor, PhotoEnv)
// and the building placement (RiderDuel ScatterCity).
public static class CityGrid
{
    public const float Pitch = 32f;      // plot pitch; streets run between plots
    public const float Radius = 280f;    // plot centers fill a disc of this radius
    public const float PlateauR = 320f;  // plateau blend-out radius
}
