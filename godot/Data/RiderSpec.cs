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
    public float MaxSpeed;      // chassis top speed (walk) in either seat
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

    public static readonly RiderSpec Hector = new()
    {
        Id = "hector", Name = "HECTOR", Tonnage = 100f, JumpCapable = false,
        ModelPath = "res://assets/hector.fbx", PartsId = "hector", MaxHp = 340f,
        Height = 14f, Radius = 2.4f, StepTime = 0.62f, StepThreshold = 1.6f, StepLift = 1.0f, Accel = 2.4f, MaxSpeed = 4.2f,
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
            new SlotDef { Id = "arm_R", Display = "RIGHT ARM", Type = WeaponSlotType.Energy, MaxWeight = 14f },
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
        Id = "prometheus", Name = "PROMETHEUS", Tonnage = 65f, JumpCapable = false,
        ModelPath = "res://assets/prometheus.fbx", PartsId = "prometheus", MaxHp = 235f,
        Height = 12.5f, Radius = 2.2f, StepTime = 0.54f, StepThreshold = 1.7f, StepLift = 0.9f, Accel = 3.2f, MaxSpeed = 4.2f,
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
    public static readonly RiderSpec Ajax = new()
    {
        Id = "ajax", Name = "AJAX", Tonnage = 85f, JumpCapable = false,
        ModelPath = "res://assets/ajax.fbx", PartsId = "ajax", MaxHp = 290f,
        Height = 12.0f, Radius = 2.2f, StepTime = 0.56f, StepThreshold = 1.7f, StepLift = 0.95f, Accel = 3.0f, MaxSpeed = 4.2f,
        FlipKneePole = false,
        Slots = new[]
        {
            new SlotDef { Id = "torso", Display = "TURRET", Type = WeaponSlotType.Ballistic, MaxWeight = 26f },
            new SlotDef { Id = "arm_R", Display = "RIGHT ARM", Type = WeaponSlotType.Ballistic, MaxWeight = 24f },
            new SlotDef { Id = "arm_L", Display = "LEFT ARM", Type = WeaponSlotType.Energy, MaxWeight = 15f },
            new SlotDef { Id = "pod_L", Display = "LEFT SHOULDER", Type = WeaponSlotType.Missile, MaxWeight = 20f },
        }
    };






    public static readonly RiderSpec[] All = { Enyo, Hector, Prometheus, Ajax };

    public static RiderSpec ById(string id)
    {
        foreach (var s in All) if (s.Id == id) return s;
        return Enyo;
    }
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

    // Removes one specific fitted weapon (the lab's list rows).
    public bool RemoveAt(string slotId, int index, out string removedId)
    {
        removedId = null;
        if (!Equipped.TryGetValue(slotId, out var l) || index < 0 || index >= l.Count) return false;
        removedId = l[index];
        l.RemoveAt(index);
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

    public static LoadoutState DefaultFor(RiderSpec spec) =>
        spec.Id == "hector" ? DefaultHector()
        : spec.Id == "prometheus" ? DefaultPrometheus()
        : spec.Id == "ajax" ? DefaultAjax()
        : DefaultEnyo();






    public static LoadoutState DefaultAjax()
    {
        var l = new LoadoutState();
        l.JumpJets = false; // the chassis cannot mount them
        // Every slot filled: 85t of 85t.
        var aj = RiderSpecs.Ajax;
        l.TryAssign(aj, "torso", WeaponCatalog.Autocannon);
        l.TryAssign(aj, "torso", WeaponCatalog.Autocannon);
        l.TryAssign(aj, "torso", WeaponCatalog.MachineGun);          // 26/26t
        l.TryAssign(aj, "arm_R", WeaponCatalog.Autocannon);
        l.TryAssign(aj, "arm_R", WeaponCatalog.Autocannon);          // 24/24t
        l.TryAssign(aj, "arm_L", WeaponCatalog.Ppc);
        l.TryAssign(aj, "arm_L", WeaponCatalog.LargeLaser);
        l.TryAssign(aj, "arm_L", WeaponCatalog.SmallLaser);          // 15/15t
        l.TryAssign(aj, "pod_L", WeaponCatalog.Lrm);
        l.TryAssign(aj, "pod_L", WeaponCatalog.Lrm);                 // 20/20t
        return l;
    }

    public static LoadoutState DefaultPrometheus()
    {
        var l = new LoadoutState();
        l.JumpJets = false; // the chassis cannot mount them
        // Every slot filled (owner, 2026-10-31): 65t of 65t.
        var pr = RiderSpecs.Prometheus;
        l.TryAssign(pr, "arm_L", WeaponCatalog.ById("ac20"));
        l.TryAssign(pr, "arm_L", WeaponCatalog.MachineGun);
        l.TryAssign(pr, "arm_L", WeaponCatalog.MachineGun);
        l.TryAssign(pr, "arm_L", WeaponCatalog.MachineGun);        // 22/22t
        l.TryAssign(pr, "arm_R", WeaponCatalog.Ppc);
        l.TryAssign(pr, "arm_R", WeaponCatalog.LargeLaser);
        l.TryAssign(pr, "arm_R", WeaponCatalog.LargeLaser);
        l.TryAssign(pr, "arm_R", WeaponCatalog.MediumLaser);
        l.TryAssign(pr, "arm_R", WeaponCatalog.SmallLaser);        // 23/23t
        l.TryAssign(pr, "torso", WeaponCatalog.Lrm);
        l.TryAssign(pr, "torso", WeaponCatalog.Lrm);               // 20/20t
        return l;
    }

    public static LoadoutState DefaultHector()
    {
        var l = new LoadoutState();
        l.JumpJets = false; // the chassis cannot mount them
        // Every slot filled (owner, 2026-10-31): 95t of 100t.
        var at = RiderSpecs.Hector;
        l.TryAssign(at, "arm_L", WeaponCatalog.Ppc);
        l.TryAssign(at, "arm_L", WeaponCatalog.LargeLaser);
        l.TryAssign(at, "arm_L", WeaponCatalog.MediumLaser);       // 17/18t
        l.TryAssign(at, "gun", WeaponCatalog.ById("ac20"));
        l.TryAssign(at, "gun", WeaponCatalog.Autocannon);          // 28/30t
        l.TryAssign(at, "arm_R", WeaponCatalog.LargeLaser);
        l.TryAssign(at, "arm_R", WeaponCatalog.LargeLaser);
        l.TryAssign(at, "arm_R", WeaponCatalog.MediumLaser);       // 13/14t
        l.TryAssign(at, "torso", WeaponCatalog.Ppc);               // 9/10t
        l.TryAssign(at, "pod_L", WeaponCatalog.Lrm);
        l.TryAssign(at, "pod_L", WeaponCatalog.Lrm);
        l.TryAssign(at, "pod_L", WeaponCatalog.Srm);
        l.TryAssign(at, "pod_L", WeaponCatalog.Srm);               // 28/28t
        return l;
    }

    public static LoadoutState DefaultEnyo()
    {
        var l = new LoadoutState();
        l.JumpJets = true; // default fit: thrusters loaded
        // Every slot filled (owner, 2026-10-31): 64t of 75t.
        var tw = RiderSpecs.Enyo;
        l.TryAssign(tw, "arm_L", WeaponCatalog.LargeLaser);
        l.TryAssign(tw, "arm_L", WeaponCatalog.MediumLaser);       // 8/8t
        l.TryAssign(tw, "arm_R", WeaponCatalog.Autocannon);
        l.TryAssign(tw, "arm_R", WeaponCatalog.MachineGun);
        l.TryAssign(tw, "arm_R", WeaponCatalog.MachineGun);        // 16/25t
        l.TryAssign(tw, "pod_L", WeaponCatalog.Lrm);
        l.TryAssign(tw, "pod_L", WeaponCatalog.Lrm);               // 20/21t
        l.TryAssign(tw, "pod_R", WeaponCatalog.Lrm);
        l.TryAssign(tw, "pod_R", WeaponCatalog.Lrm);               // 20/21t
        return l;
    }
}
