// Pure game data — no engine references. A mech spec is its tonnage
// plus the weapon slots the MechLab can fill. Slot weight capacity is
// the rule that keeps a light mech from carrying a heavy mech's gun.

public sealed class SlotDef
{
    public string Id;           // matches the breakable-parts id
    public string Display;
    public WeaponSlotType Type;
    public float MaxWeight;     // tons
}

public sealed class MechSpec
{
    public string Id;
    public string Name;
    public float Tonnage;
    public bool JumpCapable; // chassis can mount jump jets (thrusters)
    public SlotDef[] Slots;

    // Chassis data the duel builds the walker from (round 22: the
    // lab lets each side pick its mech, so everything model-bound
    // lives here, not in the duel's construction code).
    public string ModelPath;
    public string PartsId;      // breakable-parts layout key
    public float MaxHp;         // main pool
    public float Height;        // hit-capsule height
    public float Radius;        // collision / hit capsule radius
    public float StepTime, StepThreshold, StepLift, Accel;
    public float MaxSpeed;      // chassis top speed (walk) in either seat
    public bool FlipKneePole;   // plantigrade: knees bend forward

    public SlotDef Slot(string id)
    {
        foreach (var s in Slots) if (s.Id == id) return s;
        return null;
    }
}

public static class MechSpecs
{
    // Weapon capacity rule (owner, 2026-10-31, MW4 MechLab
    // model): the chassis tonnage IS the weapon budget — the
    // slot caps sum to exactly the tonnage (75t mech -> 75t of
    // mounts, 100t -> 100t). Per-slot caps still gate what a
    // single mount can hold.

    public static readonly MechSpec TimberWolf = new()
    {
        Id = "timberwolf", Name = "TIMBER WOLF", Tonnage = 75f, JumpCapable = true,
        ModelPath = "res://assets/timberwolf.fbx", PartsId = "timberwolf", MaxHp = 260f,
        Height = 13f, Radius = 2.0f, StepTime = 0.46f, StepThreshold = 1.7f, StepLift = 0.9f, Accel = 4.2f, MaxSpeed = 4.2f,
        FlipKneePole = false,
        Slots = new[]
        {
            new SlotDef { Id = "arm_L", Display = "LEFT ARM", Type = WeaponSlotType.Energy, MaxWeight = 8f },
            new SlotDef { Id = "arm_R", Display = "RIGHT ARM", Type = WeaponSlotType.Ballistic, MaxWeight = 25f },
            new SlotDef { Id = "pod_L", Display = "LEFT POD", Type = WeaponSlotType.Missile, MaxWeight = 21f },
            new SlotDef { Id = "pod_R", Display = "RIGHT POD", Type = WeaponSlotType.Missile, MaxWeight = 21f },
        }
    };

    public static readonly MechSpec Atlas = new()
    {
        Id = "atlas", Name = "ATLAS", Tonnage = 100f, JumpCapable = false,
        ModelPath = "res://assets/atlas.fbx", PartsId = "atlas", MaxHp = 340f,
        Height = 14f, Radius = 2.4f, StepTime = 0.62f, StepThreshold = 1.6f, StepLift = 1.0f, Accel = 2.4f, MaxSpeed = 4.2f,
        FlipKneePole = true,
        Slots = new[]
        {
            new SlotDef { Id = "arm_L", Display = "LEFT ARM", Type = WeaponSlotType.Energy, MaxWeight = 30f },
            new SlotDef { Id = "arm_R", Display = "RIGHT ARM", Type = WeaponSlotType.Ballistic, MaxWeight = 40f },
            new SlotDef { Id = "pod_L", Display = "SRM RACK", Type = WeaponSlotType.Missile, MaxWeight = 30f },
        }
    };

    // Every chassis the lab can offer. Adding a mech = one new
    // MechSpec + one entry here (+ its MechParts layout and model).
    public static readonly MechSpec[] All = { TimberWolf, Atlas };

    public static MechSpec ById(string id)
    {
        foreach (var s in All) if (s.Id == id) return s;
        return TimberWolf;
    }
}

// The player's current loadout: slot id -> weapon id (absent = empty).
// Assignment rules live here so the lab UI and any future UI share them.
public sealed class LoadoutState
{
    // A hardpoint fits as many weapons of its type as its tonnage
    // cap allows (owner, 2026-10-31: with one weapon per slot, the
    // 3-slot Atlas fielded less than the 4-slot Timber Wolf despite
    // the bigger budget — slot count beat tonnage). The tonnage is
    // the real limit now, exactly like the MW4 lab.
    public readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<string>> Equipped = new();

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
    public string Check(MechSpec spec, string slotId, WeaponDef weapon)
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
    public string TryAssign(MechSpec spec, string slotId, WeaponDef weapon)
    {
        var err = Check(spec, slotId, weapon);
        if (err != null) return err;
        if (!Equipped.TryGetValue(slotId, out var l))
            Equipped[slotId] = l = new System.Collections.Generic.List<string>();
        l.Add(weapon.Id);
        return null;
    }

    // Removes the most recently added weapon from the hardpoint.
    public bool RemoveLast(string slotId, out string removedId)
    {
        removedId = null;
        if (!Equipped.TryGetValue(slotId, out var l) || l.Count == 0) return false;
        removedId = l[l.Count - 1];
        l.RemoveAt(l.Count - 1);
        if (l.Count == 0) Equipped.Remove(slotId);
        return true;
    }

    public void Clear(string slotId) => Equipped.Remove(slotId);

    public void Strip()
    {
        Equipped.Clear();
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

    public static LoadoutState DefaultFor(MechSpec spec) =>
        spec.Id == "atlas" ? DefaultAtlas() : DefaultTimberWolf();

    public static LoadoutState DefaultAtlas()
    {
        var l = new LoadoutState();
        l.JumpJets = false; // the chassis cannot mount them
        l.TryAssign(MechSpecs.Atlas, "arm_L", WeaponCatalog.LargeLaser);
        l.TryAssign(MechSpecs.Atlas, "arm_R", WeaponCatalog.ById("ac20"));
        l.TryAssign(MechSpecs.Atlas, "pod_L", WeaponCatalog.Srm);
        return l;
    }

    public static LoadoutState DefaultTimberWolf()
    {
        var l = new LoadoutState();
        l.JumpJets = true; // default fit: thrusters loaded
        l.TryAssign(MechSpecs.TimberWolf, "arm_L", WeaponCatalog.LargeLaser);
        l.TryAssign(MechSpecs.TimberWolf, "arm_R", WeaponCatalog.Autocannon);
        l.TryAssign(MechSpecs.TimberWolf, "pod_L", WeaponCatalog.Lrm);
        l.TryAssign(MechSpecs.TimberWolf, "pod_R", WeaponCatalog.Lrm);
        return l;
    }
}
