// One home for the values that repeat across the engine's
// files (owner: no magic strings or numbers scattered through
// the code). Pure data - no engine references, like the rest
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
    // Story team (owner, teams stage): an NPC teammate's role
    // is the prefix + its roster slot ("ALLY1", "ALLY2", ...).
    // Roles attach to the NPC seat, not the rider - when the
    // player takes command of a teammate, that walker becomes
    // Roles.Player and the old leader takes the freed seat's
    // role.
    public const string AllyPrefix = "ALLY";
    public const string Prop = "PROP";
    public const string Terrain = "TERRAIN";
}

/// <summary>
/// Aim-cast reaches (world units). ConvergenceRange is the
/// crosshair's close cast: inside it, the aim point is the
/// cast's hit exactly. FarRange is the far cast and the HUD
/// rangefinder's reach - what weapons converge on when the
/// crosshair rests past the close cast, out to the Mission
/// world's long sightlines.
/// </summary>
public static class Aim
{
    public const float ConvergenceRange = 220f;
    public const float FarRange = 1500f;
}

/// <summary>
/// Rest-pose adjustments the walker applies to every rider.
/// ElbowRaiseRad is the elbow bend (radians) added on top of each
/// rig's authored rest pose, so the forearms - and the guns
/// mounted on them - point forward in a shooting stance instead
/// of hanging straight down at the rider's sides (owner,
/// 2026-11-16: "raise elbows for all riders").
/// </summary>
public static class Pose
{
    public const float ElbowRaiseRad = 1.15f;
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
/// hardpoint - muzzle resolution, pod lookup - reads these
/// constants instead of spelling the ids itself. The same ids
/// key the breakable-parts layouts.
/// </summary>
/// <summary>
/// Part-break rules (owner, 2026-11-12): a rider cannot lose
/// an ARM while its body is sound - early arm loss wrecked
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

/// <summary>
/// Rider body separation (world units): every rider keeps
/// this much clear space beyond the two body radii, in
/// every mode and every pairing (owner, 2026-11-15 -
/// followers in Follow mode were shoving the leader). The
/// collision pass enforces it and NPC steering brakes for
/// it; the player himself is never displaced by another
/// rider - the push always lands on the NPC.
/// </summary>
public static class RiderBody
{
    public const float Gap = 3f;
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
/// <summary>
/// Display scale (owner, 2026-11-14): every label in the
/// game measures in kilometres and kilometres per hour.
/// One world unit is 0.001 km (1 m); a speed in units per
/// second converts at x3.6. The simulation keeps raw
/// units - only the labels convert, all through here, so
/// the fiction's scale is this one constant.
/// </summary>
public static class Units
{
    public const float KmPerUnit = 0.001f;
    public static float KmVal(float units) => units * KmPerUnit;
    // "1.5 km", "10 km", "0.35 km": up to two decimals,
    // trailing zeros trimmed.
    public static string Km(float units) => $"{KmVal(units):0.##} km";
    // "130 kmh": whole numbers.
    public static string Kmh(float unitsPerSec) => $"{unitsPerSec * KmPerUnit * 3600f:0} kmh";
}

public static class CityGrid
{
    public const float Pitch = 32f;      // plot pitch; streets run between plots
    public const float Radius = 280f;    // plot centers fill a disc of this radius
    public const float PlateauR = 320f;  // plateau blend-out radius
}
