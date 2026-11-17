// Pure game data - no engine references. A rider spec is its tonnage
// plus the weapon slots the RiderLab can fill. Slot weight capacity is
// the rule that keeps a light rider from carrying a heavy rider's gun.

public sealed class SlotDef
{
    public string Id;           // matches the breakable-parts id
    public string Display;
    public WeaponSlotType Type;
    public float MaxWeight;     // tons
}

public sealed class RiderSpec
{
    public string Id;
    public string Name;
    public float Tonnage;
    // Weight class for the lab label (owner, 2026-11-01):
    // Light under 40t, Medium under 60t, Heavy from 60t.
    public string ClassLabel => Tonnage < 40f ? "LIGHT" : Tonnage < 60f ? "MEDIUM" : "HEAVY";
    public bool JumpCapable; // chassis can mount jump jets (thrusters)
    public SlotDef[] Slots;

    // Chassis data the duel builds the walker from (round 22: the
    // lab lets each side pick its rider, so everything model-bound
    // lives here, not in the duel's construction code).
    public string ModelPath;
    public string PartsId;      // breakable-parts layout key
    public float MaxHp;         // main pool
    public float Height;        // hit-capsule height
    public float Radius;        // collision / hit capsule radius
    public float StepTime, StepThreshold, StepLift, Accel;
    public float BaseSpeed;     // chassis walk speed before the weight-class factor.
                                // 50/9 u/s (owner, 2026-11-14): chosen so the class
                                // walks read round in kmh - Light 26, Medium 20,
                                // Heavy 15 (x3 run: 78 / 60 / 45).
    // Weight-class speed (owner, 2026-11-06): a MEDIUM rider moves at
    // the chassis' defined speed, a LIGHT rider faster, a HEAVY rider
    // slower. MaxSpeed is the effective walk speed every movement
    // reader uses; class thresholds match ClassLabel.
    public float ClassSpeedFactor => Tonnage < 40f ? 1.30f : Tonnage < 60f ? 1.00f : 0.75f;
    public float MaxSpeed => BaseSpeed * ClassSpeedFactor;
    public bool FlipKneePole;   // plantigrade: knees bend forward

    public SlotDef Slot(string id)
    {
        foreach (var s in Slots) if (s.Id == id) return s;
        return null;
    }
}

public static class RiderSpecs
{
    // Weapon capacity rule (owner, 2026-10-31, MW4 RiderLab
    // model): the chassis tonnage IS the weapon budget - the
    // slot caps sum to exactly the tonnage (75t rider -> 75t of
    // mounts, 100t -> 100t). Per-slot caps still gate what a
    // single mount can hold.

    public static readonly RiderSpec Enyo = new()
    {
        Id = "enyo", Name = "ENYO", Tonnage = 75f, JumpCapable = true,
        ModelPath = "res://assets/enyo.fbx", PartsId = "enyo", MaxHp = 260f,
        Height = 13f, Radius = 2.0f, StepTime = 0.46f, StepThreshold = 1.7f, StepLift = 0.9f, Accel = 4.2f, BaseSpeed = 50f / 9f,
        FlipKneePole = false,
        // Three types on every rider (owner, 2026-11-12):
        // Energy, Cannons, Rockets. Enyo's cannons ride a
        // chest mount (pool-owned torso part, like Hector's
        // chest slot). And because the chassis reads
        // naturally symmetrical, its slot tonnage splits
        // EQUALLY left/right (owner): arms 10 + 10, pods
        // 21 + 21, the 13t chest mount on the centreline -
        // caps still sum to the 75t budget.
        Slots = new[]
        {
            new SlotDef { Id = "arm_L", Display = "LEFT ARM", Type = WeaponSlotType.Energy, MaxWeight = 10f },
            new SlotDef { Id = "arm_R", Display = "RIGHT ARM", Type = WeaponSlotType.Energy, MaxWeight = 10f },
            new SlotDef { Id = "torso", Display = "CHEST", Type = WeaponSlotType.Ballistic, MaxWeight = 13f },
            new SlotDef { Id = "pod_L", Display = "LEFT POD", Type = WeaponSlotType.Missile, MaxWeight = 21f },
            new SlotDef { Id = "pod_R", Display = "RIGHT POD", Type = WeaponSlotType.Missile, MaxWeight = 21f },
        }
    };

    // Mirrored symmetry (owner, 2026-11-12, final word):
    // weapon symmetry binds the body's own mounts - arms,
    // legs, waist - so both arms of a rider carry the SAME
    // type and no weapon type fires from a single arm. The
    // other type lives on the centre mounts (waist gun,
    // turret, torso rack) or pairs on both arms itself
    // (Demeter / Theseus cannons). Pods sit outside the
    // rule, with one constraint: a rider whose rocket pods
    // exist on one side only uses them as rockets - every
    // pod on the roster is a missile mount. Every rider
    // also carries all three types (owner, same day):
    // Enyo mirrors energy on both arms and mounts its
    // cannon in the chest; Prometheus's rockets return on
    // the fused SRM housing (see its spec).
    public static readonly RiderSpec Hector = new()
    {
        Id = "hector", Name = "HECTOR", Tonnage = 100f, JumpCapable = false,
        ModelPath = "res://assets/hector.fbx", PartsId = "hector", MaxHp = 340f,
        Height = 14f, Radius = 1.90f, StepTime = 0.62f, StepThreshold = 1.6f, StepLift = 1.0f, Accel = 2.4f, BaseSpeed = 50f / 9f,
        // Radius 1.90 for the Hector-Front sculpt (2026-11-15):
        // the model is AABB-centred on its own axis and the
        // skull's face sits 2.03 from it; 1.90 lets the face
        // stand proud of the hit capsule so aimed fire can
        // attribute the head part. (Earlier builds: 2.4 Atlas,
        // 2.2 stride-posed Tripo, 1.55 re-posed Tripo - the
        // value tracks each sculpt's face distance.)
        // The segmented Tripo build (2026-11-15) rests with its
        // knees forward of the hip-ankle line (builder-measured
        // +0.65), unlike the old Atlas rig's backward-sitting
        // knees - no pole mirror.
        FlipKneePole = false,
        // Firepower DISTRIBUTED across the body (owner, 2026-10-31):
        // heavier riders get MORE hardpoints, not just fatter ones -
        // with everything on one arm, losing the arm disarms the
        // rider. Five mounts: both arms, the waist gun, a chest
        // mount, and the SRM rack. Caps sum to the 100t budget.
        Slots = new[]
        {
            new SlotDef { Id = "arm_L", Display = "LEFT ARM", Type = WeaponSlotType.Energy, MaxWeight = 18f },
            new SlotDef { Id = "gun", Display = "WAIST GUN", Type = WeaponSlotType.Ballistic, MaxWeight = 30f },
            new SlotDef { Id = "arm_R", Display = "RIGHT ARM", Type = WeaponSlotType.Energy, MaxWeight = 14f },
            new SlotDef { Id = "torso", Display = "CHEST", Type = WeaponSlotType.Energy, MaxWeight = 10f },
            new SlotDef { Id = "pod_L", Display = "SRM RACK", Type = WeaponSlotType.Missile, MaxWeight = 28f },
        }
    };

    // PROMETHEUS (Hellbringer kit, 65t Heavy): three mounts -
    // Four mounts: mirrored energy arms, a ballistic torso
    // rack, and the missile pod on the left shoulder (fused
    // into the torso mesh; RiderParts carves it out as a
    // real pod part from the measured census region). The
    // pod restores Prometheus's rockets under the owner's
    // three-types rule (Energy, Cannons, Rockets on every
    // rider, 2026-11-12) after the mirrored-arm pass had
    // left it with two. Caps sum to the 65t budget.
    public static readonly RiderSpec Prometheus = new()
    {
        Id = "prometheus", Name = "PROMETHEUS", Tonnage = 65f, JumpCapable = true,
        ModelPath = "res://assets/prometheus.fbx", PartsId = "prometheus", MaxHp = 235f,
        Height = 12.5f, Radius = 2.2f, StepTime = 0.54f, StepThreshold = 1.7f, StepLift = 0.9f, Accel = 3.2f, BaseSpeed = 50f / 9f,
        FlipKneePole = false,
        Slots = new[]
        {
            new SlotDef { Id = "arm_L", Display = "LEFT ARM", Type = WeaponSlotType.Energy, MaxWeight = 20f },
            new SlotDef { Id = "arm_R", Display = "RIGHT ARM", Type = WeaponSlotType.Energy, MaxWeight = 21f },
            new SlotDef { Id = "torso", Display = "TORSO RACK", Type = WeaponSlotType.Ballistic, MaxWeight = 14f },
            new SlotDef { Id = "pod_L", Display = "LEFT POD", Type = WeaponSlotType.Missile, MaxWeight = 10f },
        }
    };

    // Every chassis the lab can offer. Adding a rider = one new
    // RiderSpec + one entry here (+ its RiderParts layout and model).
    // AJAX (shield mech, original design, 85t Heavy): the wall -
    // a turret cannon up top, a twin-AC gun arm, a missile rack
    // on the left shoulder, and a small energy mount in the
    // shield arm. Caps sum to the 85t budget.
    public static readonly RiderSpec Medea = new()
    {
        Id = "medea", Name = "MEDEA", Tonnage = 85f, JumpCapable = false,
        ModelPath = "res://assets/medea.fbx", PartsId = "medea", MaxHp = 290f,
        Height = 12.0f, Radius = 2.2f, StepTime = 0.56f, StepThreshold = 1.7f, StepLift = 0.95f, Accel = 3.0f, BaseSpeed = 50f / 9f,
        FlipKneePole = false,
        Slots = new[]
        {
            new SlotDef { Id = "torso", Display = "TURRET", Type = WeaponSlotType.Ballistic, MaxWeight = 26f },
            new SlotDef { Id = "arm_R", Display = "RIGHT ARM", Type = WeaponSlotType.Energy, MaxWeight = 24f },
            new SlotDef { Id = "arm_L", Display = "LEFT ARM", Type = WeaponSlotType.Energy, MaxWeight = 15f },
            new SlotDef { Id = "pod_L", Display = "LEFT SHOULDER", Type = WeaponSlotType.Missile, MaxWeight = 20f },
        }
    };







    // DEMETER (Dire Wolf / Daishi, 100t Heavy): owner kitbash -
    // his hand-split Daishi upper body on the Enyo leg rig.
    // Caps sum to 100t.
    public static readonly RiderSpec Demeter = new()
    {
        Id = "demeter", Name = "DEMETER", Tonnage = 100f, JumpCapable = false,
        ModelPath = "res://assets/demeter.fbx", PartsId = "demeter", MaxHp = 340f,
        Height = 13.5f, Radius = 2.5f, StepTime = 0.62f, StepThreshold = 1.7f, StepLift = 0.95f, Accel = 3.0f, BaseSpeed = 50f / 9f,
        FlipKneePole = false,
        Slots = new[]
        {
            new SlotDef { Id = "arm_R", Display = "RIGHT ARM", Type = WeaponSlotType.Ballistic, MaxWeight = 28f },
            new SlotDef { Id = "arm_L", Display = "LEFT ARM", Type = WeaponSlotType.Ballistic, MaxWeight = 28f },
            new SlotDef { Id = "torso", Display = "TORSO", Type = WeaponSlotType.Energy, MaxWeight = 24f },
            new SlotDef { Id = "pod_L", Display = "LEFT SHOULDER", Type = WeaponSlotType.Missile, MaxWeight = 20f },
        }
    };


    // THESEUS (Hauptmann HA1-O, 95t Heavy): owner kitbash -
    // his hand-split Hauptmann upper body on the Enyo leg rig.
    // The sculpt carries its LRM rack on the RIGHT shoulder.
    // Caps sum to 95t.
    public static readonly RiderSpec Theseus = new()
    {
        Id = "theseus", Name = "THESEUS", Tonnage = 95f, JumpCapable = false,
        ModelPath = "res://assets/theseus.fbx", PartsId = "theseus", MaxHp = 320f,
        Height = 13.5f, Radius = 2.4f, StepTime = 0.60f, StepThreshold = 1.7f, StepLift = 0.95f, Accel = 3.0f, BaseSpeed = 50f / 9f,
        FlipKneePole = false,
        Slots = new[]
        {
            new SlotDef { Id = "arm_R", Display = "RIGHT ARM", Type = WeaponSlotType.Ballistic, MaxWeight = 28f },
            new SlotDef { Id = "arm_L", Display = "LEFT ARM", Type = WeaponSlotType.Ballistic, MaxWeight = 24f },
            new SlotDef { Id = "torso", Display = "TORSO", Type = WeaponSlotType.Energy, MaxWeight = 19f },
            new SlotDef { Id = "pod_R", Display = "RIGHT SHOULDER", Type = WeaponSlotType.Missile, MaxWeight = 24f },
        }
    };

    // HERMES (Orion, 75t Heavy): Tripo sculpt with its own
    // Mixamo skeleton, adopted 2026-11-16 at the owner's word
    // ("it should already contain rigs, use them") - the file's
    // joints are the game's joints, renamed to the game bone
    // vocabulary; fragments were split into loose islands and
    // bound rigidly, one bone per island, by surface-area
    // majority across the joint planes (the Hector shin rule).
    // The sculpt: autocannon right arm, laser left arm, the
    // LRM honeycomb on the left shoulder (its own pod part),
    // recessed cockpit between the shoulders (Radius 2.0 is the
    // torso's honest half-width; the head sits back, hard to
    // snipe from the front - sculpt-true). Knee pole measured
    // +0.58 forward of the hip-ankle line - no mirror. Caps
    // sum to the 75t budget; all three types aboard.
    public static readonly RiderSpec Hermes = new()
    {
        Id = "hermes", Name = "HERMES", Tonnage = 75f, JumpCapable = false,
        ModelPath = "res://assets/hermes.fbx", PartsId = "hermes", MaxHp = 260f,
        Height = 13f, Radius = 2.0f, StepTime = 0.46f, StepThreshold = 1.7f, StepLift = 0.9f, Accel = 4.2f, BaseSpeed = 50f / 9f,
        FlipKneePole = false,
        Slots = new[]
        {
            new SlotDef { Id = "arm_R", Display = "RIGHT ARM", Type = WeaponSlotType.Ballistic, MaxWeight = 24f },
            new SlotDef { Id = "arm_L", Display = "LEFT ARM", Type = WeaponSlotType.Energy, MaxWeight = 16f },
            new SlotDef { Id = "pod_L", Display = "LRM RACK", Type = WeaponSlotType.Missile, MaxWeight = 21f },
            new SlotDef { Id = "torso", Display = "CHEST", Type = WeaponSlotType.Energy, MaxWeight = 14f },
        }
    };


    // APOLLO (Tripo sculpt, 70t Heavy): the file's own Mixamo
    // skeleton again (owner uploads, 2026-11-16), islands bound
    // rigidly by surface-area majority with the arm corridor test
    // and the leg-axis guard. The sculpt: a heavy cannon on the
    // right arm, an energy left arm, the left shoulder cap split
    // out as the missile pod, antennaed helmet as the head.
    // Leanest of the four new chassis - the only one of them
    // rated for thrusters. Knees bend FORWARD (owner, 2026-11-17
    // playtest of the clean-split rebuild): the pole is flipped
    // even though the rest knees sit behind the hip-ankle line -
    // this sculpt reads plantigrade, not digitigrade.
    // Caps sum to the 70t budget; all three types aboard.
    public static readonly RiderSpec Apollo = new()
    {
        Id = "apollo", Name = "APOLLO", Tonnage = 70f, JumpCapable = true,
        ModelPath = "res://assets/apollo.fbx", PartsId = "apollo", MaxHp = 250f,
        Height = 12.5f, Radius = 2.1f, StepTime = 0.48f, StepThreshold = 1.7f, StepLift = 0.9f, Accel = 4.0f, BaseSpeed = 50f / 9f,
        FlipKneePole = true,
        Slots = new[]
        {
            new SlotDef { Id = "arm_R", Display = "RIGHT ARM", Type = WeaponSlotType.Ballistic, MaxWeight = 26f },
            new SlotDef { Id = "arm_L", Display = "LEFT ARM", Type = WeaponSlotType.Energy, MaxWeight = 16f },
            new SlotDef { Id = "torso", Display = "CHEST", Type = WeaponSlotType.Energy, MaxWeight = 13f },
            new SlotDef { Id = "pod_L", Display = "LEFT SHOULDER", Type = WeaponSlotType.Missile, MaxWeight = 15f },
        }
    };


    // HADES (Tripo sculpt, 75t Heavy): its own Mixamo skeleton,
    // same generic Tripo recipe. The sculpt: a heavy rotary
    // cannon on the right arm (its muzzle reaches past the fist -
    // the build's held-weapon reach rule binds it to the forearm),
    // a slim energy left arm, the left shoulder block as the
    // missile pod, a small head between two tall antennae (the
    // antennae ride the torso). Knee pole measured forward of
    // the hip-ankle line - no mirror. Caps sum to 75t.
    public static readonly RiderSpec Hades = new()
    {
        Id = "hades", Name = "HADES", Tonnage = 75f, JumpCapable = false,
        ModelPath = "res://assets/hades.fbx", PartsId = "hades", MaxHp = 265f,
        Height = 13.5f, Radius = 2.0f, StepTime = 0.52f, StepThreshold = 1.7f, StepLift = 0.95f, Accel = 3.4f, BaseSpeed = 50f / 9f,
        FlipKneePole = false,
        Slots = new[]
        {
            new SlotDef { Id = "arm_R", Display = "RIGHT ARM", Type = WeaponSlotType.Ballistic, MaxWeight = 28f },
            new SlotDef { Id = "arm_L", Display = "LEFT ARM", Type = WeaponSlotType.Energy, MaxWeight = 14f },
            new SlotDef { Id = "torso", Display = "CHEST", Type = WeaponSlotType.Energy, MaxWeight = 12f },
            new SlotDef { Id = "pod_L", Display = "LEFT SHOULDER", Type = WeaponSlotType.Missile, MaxWeight = 21f },
        }
    };

    // ARES (Tripo sculpt, 85t Heavy): its own Mixamo skeleton,
    // same build recipe. The sculpt: gun clusters under BOTH
    // forearms, a glass canopy for a head, the left shoulder
    // block as the missile pod. Knee pole measured forward -
    // no mirror. Caps sum to 85t; defaults fill every slot.
    public static readonly RiderSpec Ares = new()
    {
        Id = "ares", Name = "ARES", Tonnage = 85f, JumpCapable = false,
        ModelPath = "res://assets/ares.fbx", PartsId = "ares", MaxHp = 295f,
        Height = 13.0f, Radius = 2.3f, StepTime = 0.56f, StepThreshold = 1.7f, StepLift = 0.95f, Accel = 3.2f, BaseSpeed = 50f / 9f,
        FlipKneePole = false,
        Slots = new[]
        {
            new SlotDef { Id = "arm_R", Display = "RIGHT ARM", Type = WeaponSlotType.Ballistic, MaxWeight = 24f },
            new SlotDef { Id = "arm_L", Display = "LEFT ARM", Type = WeaponSlotType.Ballistic, MaxWeight = 20f },
            new SlotDef { Id = "torso", Display = "TORSO", Type = WeaponSlotType.Energy, MaxWeight = 21f },
            new SlotDef { Id = "pod_L", Display = "LEFT SHOULDER", Type = WeaponSlotType.Missile, MaxWeight = 20f },
        }
    };


    // AMADEUS (Tripo sculpt, 90t Heavy): the file came WITHOUT
    // a skeleton, so the rig is hand-authored from the sculpt's
    // measured landmarks (the Medea path) - pelvis at the waist
    // block, knees bowed 0.28 forward of the hip-ankle line (no
    // mirror). The sculpt: a boxy assault frame, forearm guns
    // under both arms, and a sensor box cluster on the RIGHT
    // shoulder - split out as the missile pod; the cockpit dome
    // is fused into the torso slab, so the head is carved from
    // it in RiderParts. Caps sum to 90t.
    public static readonly RiderSpec Amadeus = new()
    {
        Id = "amadeus", Name = "AMADEUS", Tonnage = 90f, JumpCapable = false,
        ModelPath = "res://assets/amadeus.fbx", PartsId = "amadeus", MaxHp = 310f,
        Height = 13.5f, Radius = 2.3f, StepTime = 0.58f, StepThreshold = 1.7f, StepLift = 0.95f, Accel = 3.0f, BaseSpeed = 50f / 9f,
        FlipKneePole = false,
        Slots = new[]
        {
            new SlotDef { Id = "arm_L", Display = "LEFT ARM", Type = WeaponSlotType.Ballistic, MaxWeight = 22f },
            new SlotDef { Id = "arm_R", Display = "RIGHT ARM", Type = WeaponSlotType.Ballistic, MaxWeight = 22f },
            new SlotDef { Id = "torso", Display = "TORSO", Type = WeaponSlotType.Energy, MaxWeight = 24f },
            new SlotDef { Id = "pod_R", Display = "RIGHT SHOULDER", Type = WeaponSlotType.Missile, MaxWeight = 22f },
        }
    };

    public static readonly RiderSpec[] All = { Enyo, Hector, Prometheus, Medea, Demeter, Theseus, Hermes, Apollo, Hades, Ares, Amadeus };

    // The free-for-all's third rider is drawn from this
    // priority list: the first entry that is not already the
    // enemy. Roster data (the two original chassis, in
    // seniority order) - extending the rotation is a data
    // edit, never an engine branch.
    public static readonly RiderSpec[] FfaPartnerPriority = { Hector, Enyo };

    public static RiderSpec ById(string id)
    {
        foreach (var s in All) if (s.Id == id) return s;
        return Enyo;
    }

    // Default fits (owner's rule: every slot filled) - pure
    // data, one row per rider: slot id -> weapon ids in fit
    // order. LoadoutState.DefaultFor builds any rider's factory
    // loadout from its row; there are no per-rider builder
    // methods and no id dispatch in the engine.
    public static readonly System.Collections.Generic.Dictionary<string, (string Slot, string[] Weapons)[]> DefaultFits = new()
    {
        ["enyo"] = new[]
        {
            ("arm_L", new[] { "large_laser", "large_laser" }),                      // 10/10t
            ("arm_R", new[] { "large_laser", "large_laser" }),                      // 10/10t
            ("torso", new[] { "autocannon" }),                                      // 12/13t
            ("pod_L", new[] { "lrm", "lrm" }),                                     // 20/21t
            ("pod_R", new[] { "lrm", "lrm" }),                                     // 20/21t
        },
        ["hector"] = new[]
        {
            ("arm_L", new[] { "ppc", "large_laser", "medium_laser" }),             // 17/18t
            ("gun", new[] { "ac20", "autocannon" }),                               // 28/30t
            ("arm_R", new[] { "large_laser", "large_laser", "medium_laser" }),     // 13/14t
            ("torso", new[] { "ppc" }),                                            // 9/10t
            ("pod_L", new[] { "lrm", "lrm", "srm", "srm" }),                       // 28/28t
        },
        ["prometheus"] = new[]
        {
            ("arm_L", new[] { "ppc", "large_laser", "large_laser", "small_laser" }), // 20/20t
            ("arm_R", new[] { "ppc", "ppc", "medium_laser" }),                        // 21/21t
            ("torso", new[] { "autocannon", "machine_gun" }),                         // 14/14t
            ("pod_L", new[] { "lrm" }),                                               // 10/10t
        },
        ["medea"] = new[]
        {
            ("torso", new[] { "autocannon", "autocannon", "machine_gun" }),       // 26/26t
            ("arm_R", new[] { "ppc", "large_laser", "large_laser", "medium_laser", "small_laser", "small_laser" }),                       // 24/24t
            ("arm_L", new[] { "ppc", "large_laser", "small_laser" }),              // 15/15t
            ("pod_L", new[] { "lrm", "lrm" }),                                     // 20/20t
        },
        ["demeter"] = new[]
        {
            ("arm_R", new[] { "autocannon", "autocannon", "machine_gun", "machine_gun" }), // 28/28t
            ("arm_L", new[] { "autocannon", "autocannon", "machine_gun", "machine_gun" }), // 28/28t
            ("torso", new[] { "ppc", "large_laser", "large_laser", "medium_laser", "small_laser", "small_laser" }), // 24/24t
            ("pod_L", new[] { "lrm", "lrm" }),                                     // 20/20t
        },
        ["theseus"] = new[]
        {
            ("arm_R", new[] { "autocannon", "autocannon", "machine_gun", "machine_gun" }), // 28/28t
            ("arm_L", new[] { "autocannon", "autocannon" }), // 24/24t
            ("torso", new[] { "ppc", "large_laser", "medium_laser", "small_laser", "small_laser" }), // 19/19t
            ("pod_R", new[] { "lrm", "lrm", "srm" }),                              // 24/24t
        },
        ["amadeus"] = new[]
        {
            ("arm_L", new[] { "heavy_cannon", "machine_gun", "machine_gun" }),   // 20/22t
            ("arm_R", new[] { "heavy_cannon", "machine_gun", "machine_gun" }),   // 20/22t
            ("torso", new[] { "ppc", "ppc", "large_laser", "small_laser" }),     // 24/24t
            ("pod_R", new[] { "lrm", "lrm" }),                                   // 20/22t
        },
        ["hades"] = new[]
        {
            ("arm_R", new[] { "ac20", "autocannon" }),                           // 28/28t
            ("arm_L", new[] { "ppc", "large_laser" }),                           // 14/14t
            ("torso", new[] { "large_laser", "large_laser", "small_laser", "small_laser" }), // 12/12t
            ("pod_L", new[] { "lrm", "lrm" }),                                   // 20/21t
        },
        ["ares"] = new[]
        {
            ("arm_R", new[] { "autocannon", "autocannon" }),                     // 24/24t
            ("arm_L", new[] { "heavy_cannon", "machine_gun", "machine_gun" }),    // 20/20t
            ("torso", new[] { "ppc", "ppc", "medium_laser" }),                    // 21/21t
            ("pod_L", new[] { "lrm", "lrm" }),                                   // 20/20t
        },
        ["apollo"] = new[]
        {
            ("arm_R", new[] { "autocannon", "autocannon", "machine_gun" }),      // 26/26t
            ("arm_L", new[] { "ppc", "large_laser", "small_laser" }),            // 15/16t
            ("torso", new[] { "large_laser", "large_laser", "medium_laser" }),   // 13/13t
            ("pod_L", new[] { "lrm", "srm" }),                                   // 14/15t
        },
        ["hermes"] = new[]
        {
            ("arm_R", new[] { "ac20", "machine_gun", "machine_gun" }),             // 20/24t
            ("arm_L", new[] { "ppc", "large_laser", "small_laser" }),              // 15/16t
            ("pod_L", new[] { "lrm", "lrm" }),                                     // 20/21t
            ("torso", new[] { "large_laser", "large_laser", "medium_laser", "small_laser" }), // 14/14t
        },
    };
}

// The player's current loadout: slot id -> weapon id (absent = empty).
// Assignment rules live here so the lab UI and any future UI share them.
public sealed class LoadoutState
{
    // A hardpoint fits as many weapons of its type as its tonnage
    // cap allows (owner, 2026-10-31: with one weapon per slot, the
    // 3-slot Hector fielded less than the 4-slot Enyo despite
    // the bigger budget - slot count beat tonnage). The tonnage is
    // the real limit now, exactly like the MW4 lab.
    public readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>> Equipped = new();

    // Firing groups (owner): every fitted weapon belongs to a
    // group the PLAYER picks in the lab (the G-button on each
    // fitted row) - the duel fires a group as one volley and the
    // HUD lists groups, not weapon types. Stored parallel to
    // Equipped (same slot, same index). A weapon fitted without
    // an explicit pick takes the type default, which reproduces
    // the old type grouping (energy 1, ballistic 2, missile 3).
    public readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<int>> GroupMap = new();
    public const int MaxGroup = 6;

    public static int DefaultGroupFor(WeaponDef def) =>
        def == null ? 1
        : def.Type == WeaponSlotType.Energy ? 1
        : def.Type == WeaponSlotType.Ballistic ? 2 : 3;

    public int GroupAt(string slotId, int index)
    {
        if (GroupMap.TryGetValue(slotId, out var g) && index >= 0 && index < g.Count)
            return g[index];
        var ids = WeaponsIn(slotId);
        return index >= 0 && index < ids.Count
            ? DefaultGroupFor(WeaponCatalog.ById(ids[index])) : 1;
    }

    public void SetGroup(string slotId, int index, int group)
    {
        var ids = WeaponsIn(slotId);
        if (index < 0 || index >= ids.Count) return;
        if (!GroupMap.TryGetValue(slotId, out var g))
        {
            g = new System.Collections.Generic.List<int>();
            for (int i = 0; i < ids.Count; i++) g.Add(DefaultGroupFor(WeaponCatalog.ById(ids[i])));
            GroupMap[slotId] = g;
        }
        g[index] = group < 1 ? 1 : group > MaxGroup ? MaxGroup : group;
    }

    // Jump jets (thrusters) fitted. Only meaningful on a JumpCapable
    // chassis; the duel's effective rule is spec.JumpCapable && JumpJets.
    public bool JumpJets;

    public System.Collections.Generic.IReadOnlyList<string> WeaponsIn(string slotId) =>
        Equipped.TryGetValue(slotId, out var l) ? l : System.Array.Empty<string>();

    public string WeaponIn(string slotId) =>
        Equipped.TryGetValue(slotId, out var l) && l.Count > 0 ? l[0] : null;

    public float SlotWeight(string slotId)
    {
        float t = 0f;
        foreach (var id in WeaponsIn(slotId))
        {
            var w = WeaponCatalog.ById(id);
            if (w != null) t += w.Weight;
        }
        return t;
    }

    // Pure rule check (used live during drag hover): null = the weapon
    // fits, else the reason it does not.
    public string Check(RiderSpec spec, string slotId, WeaponDef weapon)
    {
        var slot = spec.Slot(slotId);
        if (slot == null) return "no such slot";
        if (weapon == null) return "no weapon";
        if (weapon.Type != slot.Type)
            return $"{weapon.Name} is {weapon.Type} - the {slot.Display} hardpoint takes {slot.Type} only";
        float left = slot.MaxWeight - SlotWeight(slotId);
        if (weapon.Weight > left)
            return $"{weapon.Name} weighs {weapon.Weight:F0}t - only {left:F0}t left in the {slot.Display} hardpoint ({slot.MaxWeight:F0}t max)";
        return null;
    }

    // Adds the weapon to the hardpoint (multi-fit). Returns null on
    // success, else the reason the drop is refused.
    public string TryAssign(RiderSpec spec, string slotId, WeaponDef weapon)
    {
        var err = Check(spec, slotId, weapon);
        if (err != null) return err;
        if (!Equipped.TryGetValue(slotId, out var l))
            Equipped[slotId] = l = new System.Collections.Generic.List<string>();
        l.Add(weapon.Id);
        if (!GroupMap.TryGetValue(slotId, out var g))
            GroupMap[slotId] = g = new System.Collections.Generic.List<int>();
        g.Add(DefaultGroupFor(weapon));
        return null;
    }

    // Removes the most recently added weapon from the hardpoint.
    public bool RemoveLast(string slotId, out string removedId)
    {
        removedId = null;
        if (!Equipped.TryGetValue(slotId, out var l) || l.Count == 0) return false;
        removedId = l[l.Count - 1];
        l.RemoveAt(l.Count - 1);
        if (GroupMap.TryGetValue(slotId, out var g) && g.Count == l.Count + 1) g.RemoveAt(g.Count - 1);
        if (l.Count == 0) { Equipped.Remove(slotId); GroupMap.Remove(slotId); }
        return true;
    }

    // Removes one specific fitted weapon (the lab's list rows).
    public bool RemoveAt(string slotId, int index, out string removedId)
    {
        removedId = null;
        if (!Equipped.TryGetValue(slotId, out var l) || index < 0 || index >= l.Count) return false;
        removedId = l[index];
        l.RemoveAt(index);
        if (GroupMap.TryGetValue(slotId, out var g) && index < g.Count) g.RemoveAt(index);
        if (l.Count == 0) { Equipped.Remove(slotId); GroupMap.Remove(slotId); }
        return true;
    }

    public void Clear(string slotId) { Equipped.Remove(slotId); GroupMap.Remove(slotId); }

    public void Strip()
    {
        Equipped.Clear();
        GroupMap.Clear();
    }

    public float TotalWeaponWeight
    {
        get
        {
            float t = 0f;
            foreach (var l in Equipped.Values)
                foreach (var id in l)
                {
                    var w = WeaponCatalog.ById(id);
                    if (w != null) t += w.Weight;
                }
            return t;
        }
    }

    // The generic default-fit builder: any rider's factory
    // loadout is its row in RiderSpecs.DefaultFits, applied in
    // order. Thrusters come fitted exactly on the chassis that
    // can mount them (the old builders set the same values).
    public static LoadoutState DefaultFor(RiderSpec spec)
    {
        var l = new LoadoutState();
        l.JumpJets = spec.JumpCapable;
        if (RiderSpecs.DefaultFits.TryGetValue(spec.Id, out var fit))
            foreach (var (slot, weapons) in fit)
                foreach (var wid in weapons)
                    l.TryAssign(spec, slot, WeaponCatalog.ById(wid));
        return l;
    }
}
