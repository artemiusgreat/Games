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
    public static readonly MechSpec TimberWolf = new()
    {
        Id = "timberwolf", Name = "TIMBER WOLF", Tonnage = 75f, JumpCapable = true,
        ModelPath = "res://assets/timberwolf.fbx", PartsId = "timberwolf", MaxHp = 260f,
        Height = 13f, Radius = 2.0f, StepTime = 0.46f, StepThreshold = 1.7f, StepLift = 0.9f, Accel = 4.2f, MaxSpeed = 4.2f,
        FlipKneePole = false,
        Slots = new[]
        {
            new SlotDef { Id = "arm_L", Display = "LEFT ARM", Type = WeaponSlotType.Energy, MaxWeight = 8f },
            new SlotDef { Id = "arm_R", Display = "RIGHT ARM", Type = WeaponSlotType.Ballistic, MaxWeight = 16f },
            new SlotDef { Id = "pod_L", Display = "LEFT POD", Type = WeaponSlotType.Missile, MaxWeight = 12f },
            new SlotDef { Id = "pod_R", Display = "RIGHT POD", Type = WeaponSlotType.Missile, MaxWeight = 12f },
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
            new SlotDef { Id = "arm_R", Display = "RIGHT ARM", Type = WeaponSlotType.Ballistic, MaxWeight = 18f },
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
    public readonly System.Collections.Generic.Dictionary<string, string> Equipped = new();

    // Jump jets (thrusters) fitted. Only meaningful on a JumpCapable
    // chassis; the duel's effective rule is spec.JumpCapable && JumpJets.
    public bool JumpJets;

    public string WeaponIn(string slotId) =>
        Equipped.TryGetValue(slotId, out var w) ? w : null;

    // Pure rule check (used live during drag hover): null = the weapon
    // fits, else the reason it does not.
    public string Check(MechSpec spec, string slotId, WeaponDef weapon)
    {
        var slot = spec.Slot(slotId);
        if (slot == null) return "no such slot";
        if (weapon == null) return "no weapon";
        if (weapon.Type != slot.Type)
            return $"{weapon.Name} is {weapon.Type} — the {slot.Display} hardpoint takes {slot.Type} only";
        if (weapon.Weight > slot.MaxWeight)
            return $"{weapon.Name} weighs {weapon.Weight:F0}t — the {slot.Display} hardpoint holds {slot.MaxWeight:F0}t max";
        return null;
    }

    // Returns null on success, else the reason the drop is refused.
    public string TryAssign(MechSpec spec, string slotId, WeaponDef weapon)
    {
        var err = Check(spec, slotId, weapon);
        if (err != null) return err;
        Equipped[slotId] = weapon.Id;
        return null;
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
            foreach (var id in Equipped.Values)
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
        l.Equipped["arm_R"] = "ac20";
        return l;
    }

    public static LoadoutState DefaultTimberWolf()
    {
        var l = new LoadoutState();
        l.JumpJets = true; // default fit: thrusters loaded
        l.Equipped["arm_L"] = "large_laser";
        l.Equipped["arm_R"] = "autocannon";
        l.Equipped["pod_L"] = "lrm";
        l.Equipped["pod_R"] = "lrm";
        return l;
    }
}
