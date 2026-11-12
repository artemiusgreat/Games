// Pure game data — no engine references. A rider spec is its tonnage
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
    public float BaseSpeed;     // chassis walk speed before the weight-class factor
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
    // model): the chassis tonnage IS the weapon budget — the
    // slot caps sum to exactly the tonnage (75t rider -> 75t of
    // mounts, 100t -> 100t). Per-slot caps still gate what a
    // single mount can hold.

    public static readonly RiderSpec Enyo = new()
    {
        Id = "enyo", Name = "ENYO", Tonnage = 75f, JumpCapable = true,
        ModelPath = "res://assets/enyo.fbx", PartsId = "enyo", MaxHp = 260f,
        Height = 13f, Radius = 2.0f, StepTime = 0.46f, StepThreshold = 1.7f, StepLift = 0.9f, Accel = 4.2f, BaseSpeed = 4.8f,
        FlipKneePole = false,
        Slots = new[]
        {
            new SlotDef { Id = "arm_L", Display = "LEFT ARM", Type = WeaponSlotType.Energy, MaxWeight = 8f },
            new SlotDef { Id = "arm_R", Display = "RIGHT ARM", Type = WeaponSlotType.Ballistic, MaxWeight = 25f },
            new SlotDef { Id = "pod_L", Display = "LEFT POD", Type = WeaponSlotType.Missile, MaxWeight = 21f },
            new SlotDef { Id = "pod_R", Display = "RIGHT POD", Type = WeaponSlotType.Missile, MaxWeight = 21f },
        }
    };

    // Arms-only symmetry (owner, 2026-11-12): every rider
    // carries one ENERGY arm and one BALLISTIC arm — Hector's
    // right arm flipped Energy -> Ballistic and Demeter's
    // left arm Ballistic -> Energy to make it so (pods and
    // centre mounts untouched). Roster pattern: energy left,
    // ballistic right; Prometheus is the designed mirror.
    public static readonly RiderSpec Hector = new()
    {
        Id = "hector", Name = "HECTOR", Tonnage = 100f, JumpCapable = false,
        ModelPath = "res://assets/hector.fbx", PartsId = "hector", MaxHp = 340f,
        Height = 14f, Radius = 2.4f, StepTime = 0.62f, StepThreshold = 1.6f, StepLift = 1.0f, Accel = 2.4f, BaseSpeed = 4.8f,
        FlipKneePole = true,
        // Firepower DISTRIBUTED across the body (owner, 2026-10-31):
        // heavier riders get MORE hardpoints, not just fatter ones —
        // with everything on one arm, losing the arm disarms the
        // rider. Five mounts: both arms, the waist gun, a chest
        // mount, and the SRM rack. Caps sum to the 100t budget.
        Slots = new[]
        {
            new SlotDef { Id = "arm_L", Display = "LEFT ARM", Type = WeaponSlotType.Energy, MaxWeight = 18f },
            new SlotDef { Id = "gun", Display = "WAIST GUN", Type = WeaponSlotType.Ballistic, MaxWeight = 30f },
            new SlotDef { Id = "arm_R", Display = "RIGHT ARM", Type = WeaponSlotType.Ballistic, MaxWeight = 14f },
            new SlotDef { Id = "torso", Display = "CHEST", Type = WeaponSlotType.Energy, MaxWeight = 10f },
            new SlotDef { Id = "pod_L", Display = "SRM RACK", Type = WeaponSlotType.Missile, MaxWeight = 28f },
        }
    };

    // PROMETHEUS (Hellbringer kit, 65t Heavy): three mounts —
    // the ballistic arm, the energy arm, and the torso missile
    // rack (the kit's SRM housing is fused into the torso mesh,
    // so the rack is a torso slot, not a breakable pod). Caps
    // sum to the 65t budget.
    public static readonly RiderSpec Prometheus = new()
    {
        Id = "prometheus", Name = "PROMETHEUS", Tonnage = 65f, JumpCapable = true,
        ModelPath = "res://assets/prometheus.fbx", PartsId = "prometheus", MaxHp = 235f,
        Height = 12.5f, Radius = 2.2f, StepTime = 0.54f, StepThreshold = 1.7f, StepLift = 0.9f, Accel = 3.2f, BaseSpeed = 4.8f,
        FlipKneePole = false,
        Slots = new[]
        {
            new SlotDef { Id = "arm_L", Display = "LEFT ARM", Type = WeaponSlotType.Ballistic, MaxWeight = 22f },
            new SlotDef { Id = "arm_R", Display = "RIGHT ARM", Type = WeaponSlotType.Energy, MaxWeight = 23f },
            new SlotDef { Id = "torso", Display = "TORSO RACK", Type = WeaponSlotType.Missile, MaxWeight = 20f },
        }
    };

    // Every chassis the lab can offer. Adding a rider = one new
    // RiderSpec + one entry here (+ its RiderParts layout and model).
    // AJAX (shield mech, original design, 85t Heavy): the wall —
    // a turret cannon up top, a twin-AC gun arm, a missile rack
    // on the left shoulder, and a small energy mount in the
    // shield arm. Caps sum to the 85t budget.
    public static readonly RiderSpec Medea = new()
    {
        Id = "medea", Name = "MEDEA", Tonnage = 85f, JumpCapable = false,
        ModelPath = "res://assets/medea.fbx", PartsId = "medea", MaxHp = 290f,
        Height = 12.0f, Radius = 2.2f, StepTime = 0.56f, StepThreshold = 1.7f, StepLift = 0.95f, Accel = 3.0f, BaseSpeed = 4.8f,
        FlipKneePole = false,
        Slots = new[]
        {
            new SlotDef { Id = "torso", Display = "TURRET", Type = WeaponSlotType.Ballistic, MaxWeight = 26f },
            new SlotDef { Id = "arm_R", Display = "RIGHT ARM", Type = WeaponSlotType.Ballistic, MaxWeight = 24f },
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
        Height = 13.5f, Radius = 2.5f, StepTime = 0.62f, StepThreshold = 1.7f, StepLift = 0.95f, Accel = 3.0f, BaseSpeed = 4.8f,
        FlipKneePole = false,
        Slots = new[]
        {
            new SlotDef { Id = "arm_R", Display = "RIGHT ARM", Type = WeaponSlotType.Ballistic, MaxWeight = 28f },
            new SlotDef { Id = "arm_L", Display = "LEFT ARM", Type = WeaponSlotType.Energy, MaxWeight = 28f },
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
        Height = 13.5f, Radius = 2.4f, StepTime = 0.60f, StepThreshold = 1.7f, StepLift = 0.95f, Accel = 3.0f, BaseSpeed = 4.8f,
        FlipKneePole = false,
        Slots = new[]
        {
            new SlotDef { Id = "arm_R", Display = "RIGHT ARM", Type = WeaponSlotType.Ballistic, MaxWeight = 28f },
            new SlotDef { Id = "arm_L", Display = "LEFT ARM", Type = WeaponSlotType.Energy, MaxWeight = 24f },
            new SlotDef { Id = "torso", Display = "TORSO", Type = WeaponSlotType.Energy, MaxWeight = 19f },
            new SlotDef { Id = "pod_R", Display = "RIGHT SHOULDER", Type = WeaponSlotType.Missile, MaxWeight = 24f },
        }
    };

    public static readonly RiderSpec[] All = { Enyo, Hector, Prometheus, Medea, Demeter, Theseus };

    // The free-for-all's third rider is drawn from this
    // priority list: the first entry that is not already the
    // enemy. Roster data (the two original chassis, in
    // seniority order) — extending the rotation is a data
    // edit, never an engine branch.
    public static readonly RiderSpec[] FfaPartnerPriority = { Hector, Enyo };

    public static RiderSpec ById(string id)
    {
        foreach (var s in All) if (s.Id == id) return s;
        return Enyo;
    }

    // Default fits (owner's rule: every slot filled) — pure
    // data, one row per rider: slot id -> weapon ids in fit
    // order. LoadoutState.DefaultFor builds any rider's factory
    // loadout from its row; there are no per-rider builder
    // methods and no id dispatch in the engine.
    public static readonly System.Collections.Generic.Dictionary<string, (string Slot, string[] Weapons)[]> DefaultFits = new()
    {
        ["enyo"] = new[]
        {
            ("arm_L", new[] { "large_laser", "medium_laser" }),                    // 8/8t
            ("arm_R", new[] { "autocannon", "machine_gun", "machine_gun" }),      // 16/25t
            ("pod_L", new[] { "lrm", "lrm" }),                                     // 20/21t
            ("pod_R", new[] { "lrm", "lrm" }),                                     // 20/21t
        },
        ["hector"] = new[]
        {
            ("arm_L", new[] { "ppc", "large_laser", "medium_laser" }),             // 17/18t
            ("gun", new[] { "ac20", "autocannon" }),                               // 28/30t
            ("arm_R", new[] { "autocannon", "machine_gun" }),     // 13/14t
            ("torso", new[] { "ppc" }),                                            // 9/10t
            ("pod_L", new[] { "lrm", "lrm", "srm", "srm" }),                       // 28/28t
        },
        ["prometheus"] = new[]
        {
            ("arm_L", new[] { "ac20", "machine_gun", "machine_gun", "machine_gun" }), // 22/22t
            ("arm_R", new[] { "ppc", "large_laser", "large_laser", "medium_laser", "small_laser" }), // 23/23t
            ("torso", new[] { "lrm", "lrm" }),                                     // 20/20t
        },
        ["medea"] = new[]
        {
            ("torso", new[] { "autocannon", "autocannon", "machine_gun" }),       // 26/26t
            ("arm_R", new[] { "autocannon", "autocannon" }),                       // 24/24t
            ("arm_L", new[] { "ppc", "large_laser", "small_laser" }),              // 15/15t
            ("pod_L", new[] { "lrm", "lrm" }),                                     // 20/20t
        },
        ["demeter"] = new[]
        {
            ("arm_R", new[] { "autocannon", "autocannon", "machine_gun", "machine_gun" }), // 28/28t
            ("arm_L", new[] { "ppc", "ppc", "large_laser", "medium_laser", "small_laser", "small_laser" }), // 28/28t
            ("torso", new[] { "ppc", "large_laser", "large_laser", "medium_laser", "small_laser", "small_laser" }), // 24/24t
            ("pod_L", new[] { "lrm", "lrm" }),                                     // 20/20t
        },
        ["theseus"] = new[]
        {
            ("arm_R", new[] { "autocannon", "autocannon", "machine_gun", "machine_gun" }), // 28/28t
            ("arm_L", new[] { "ppc", "large_laser", "large_laser", "medium_laser", "small_laser", "small_laser" }), // 24/24t
            ("torso", new[] { "ppc", "large_laser", "medium_laser", "small_laser", "small_laser" }), // 19/19t
            ("pod_R", new[] { "lrm", "lrm", "srm" }),                              // 24/24t
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
    // the bigger budget — slot count beat tonnage). The tonnage is
    // the real limit now, exactly like the MW4 lab.
    public readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>> Equipped = new();

    // Firing groups (owner): every fitted weapon belongs to a
    // group the PLAYER picks in the lab (the G-button on each
    // fitted row) — the duel fires a group as one volley and the
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
            return $"{weapon.Name} is {weapon.Type} — the {slot.Display} hardpoint takes {slot.Type} only";
        float left = slot.MaxWeight - SlotWeight(slotId);
        if (weapon.Weight > left)
            return $"{weapon.Name} weighs {weapon.Weight:F0}t — only {left:F0}t left in the {slot.Display} hardpoint ({slot.MaxWeight:F0}t max)";
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
