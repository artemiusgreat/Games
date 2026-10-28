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
    public SlotDef[] Slots;

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
        Id = "timberwolf", Name = "TIMBER WOLF", Tonnage = 75f,
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
        Id = "atlas", Name = "ATLAS", Tonnage = 100f,
        Slots = new[]
        {
            new SlotDef { Id = "arm_R", Display = "RIGHT ARM", Type = WeaponSlotType.Ballistic, MaxWeight = 18f },
        }
    };
}

// The player's current loadout: slot id -> weapon id (absent = empty).
// Assignment rules live here so the lab UI and any future UI share them.
public sealed class LoadoutState
{
    public readonly System.Collections.Generic.Dictionary<string, string> Equipped = new();

    public string WeaponIn(string slotId) =>
        Equipped.TryGetValue(slotId, out var w) ? w : null;

    // Returns null on success, else the reason the drop is refused.
    public string TryAssign(MechSpec spec, string slotId, WeaponDef weapon)
    {
        var slot = spec.Slot(slotId);
        if (slot == null) return "no such slot";
        if (weapon == null) return "no weapon";
        if (weapon.Type != slot.Type)
            return $"{weapon.Name} is {weapon.Type} — the {slot.Display} hardpoint takes {slot.Type} only";
        if (weapon.Weight > slot.MaxWeight)
            return $"{weapon.Name} weighs {weapon.Weight:F0}t — the {slot.Display} hardpoint holds {slot.MaxWeight:F0}t max";
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

    public static LoadoutState DefaultTimberWolf()
    {
        var l = new LoadoutState();
        l.Equipped["arm_L"] = "large_laser";
        l.Equipped["arm_R"] = "autocannon";
        l.Equipped["pod_L"] = "lrm";
        l.Equipped["pod_R"] = "lrm";
        return l;
    }
}
