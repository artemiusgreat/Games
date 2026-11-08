// Pure game data — no engine references, so the catalog, the loadout
// rules and the knockdown math can move to another engine as-is.
// Weapon weights drive two things: the RiderLab slot capacities and the
// knockdown rule (cumulative impact weight vs rider tonnage).

public enum WeaponSlotType { Energy, Ballistic, Missile }

public sealed class WeaponDef
{
    public string Id;
    public string Name;
    public string Short;        // panel row label
    public WeaponSlotType Type;
    public float Weight;        // tons (per launcher for missile packs)
    public float Damage;        // per hit (per rocket for missile packs)
    public float Cooldown;      // seconds
    public int Rockets;         // missile packs only: rockets per salvo
    public float Splash;        // splash radius, 0 = direct only
    // Ballistic weapons only: the shell's muzzle velocity (u/s).
    // THE place to tune bullet speed — the player volley and the
    // AI gunnery both read it from here. All cannons share the
    // autocannon's 420 (owner: medium and large match the
    // smallest cannon); the machine gun keeps its slow 85 u/s
    // tracer stream (owner's standing exception).
    public float MuzzleSpeed;
    // Effective range in world units, as shown in the lab — and
    // ENFORCED by the firing code (owner): each weapon reaches
    // exactly its own Range and no further. Lasers/PPC resolve
    // per weapon on the view ray up to their Range; ballistic
    // shells burn out at theirs; missile salvos solve onto the
    // aim point clamped to theirs. The figures are MechWarrior
    // 4's, per Sarna's MW4 equipment table (owner: adopt Sarna):
    // SL 150, ML 300, LL 600, PPC 750, MG 150, SRM 250,
    // LRM 1000, AC/20 300 (the table's LBX AC 20). Where the
    // mapping to a Sarna row is ambiguous, the owner's rule is
    // to take the LONGEST range among the similar weapons:
    // Autocannon takes the AC 5's 600 (over the AC 10's 400),
    // Heavy Cannon the LBX AC 10's 450 (over the AC 10's 400
    // and the LBX AC 20's 300), Machine Gun the Clan Machine
    // Gun Array's 200 (over the IS array's 150).
    public float Range;

    public float PerRocketWeight => Rockets > 0 ? Weight / Rockets : Weight;
}

public static class WeaponCatalog
{
    public static readonly WeaponDef SmallLaser = new()
    { Id = "small_laser", Name = "Small Laser", Short = "S LAS", Type = WeaponSlotType.Energy, Weight = 1f, Damage = 5f, Cooldown = 0.25f, Range = 150f };
    public static readonly WeaponDef MediumLaser = new()
    { Id = "medium_laser", Name = "Medium Laser", Short = "M LAS", Type = WeaponSlotType.Energy, Weight = 3f, Damage = 8f, Cooldown = 0.35f, Range = 300f };
    public static readonly WeaponDef LargeLaser = new()
    { Id = "large_laser", Name = "Large Laser", Short = "LAS", Type = WeaponSlotType.Energy, Weight = 5f, Damage = 12f, Cooldown = 0.45f, Range = 600f };
    public static readonly WeaponDef Ppc = new()
    { Id = "ppc", Name = "PPC", Short = "PPC", Type = WeaponSlotType.Energy, Weight = 9f, Damage = 22f, Cooldown = 1.4f, Range = 750f };
    public static readonly WeaponDef MachineGun = new()
    { Id = "machine_gun", Name = "Machine Gun", Short = "MG", Type = WeaponSlotType.Ballistic, Weight = 2f, Damage = 3f, Cooldown = 0.12f, Range = 200f, MuzzleSpeed = 85f };
    public static readonly WeaponDef Autocannon = new()
    { Id = "autocannon", Name = "Autocannon", Short = "AC", Type = WeaponSlotType.Ballistic, Weight = 12f, Damage = 26f, Cooldown = 0.85f, Range = 600f, MuzzleSpeed = 420f };
    public static readonly WeaponDef HeavyCannon = new()
    { Id = "heavy_cannon", Name = "Heavy Cannon", Short = "HVY", Type = WeaponSlotType.Ballistic, Weight = 16f, Damage = 40f, Cooldown = 1.6f, Range = 450f, MuzzleSpeed = 420f };
    // Missile packs are per-pod: a full salvo fires every alive pod's
    // pack. The default fit (LRM in both pods) reproduces the original
    // 8-rocket salvo, 4 with one pod lost.
    public static readonly WeaponDef Srm = new()
    { Id = "srm", Name = "SRM Pack", Short = "SRM", Type = WeaponSlotType.Missile, Weight = 4f, Damage = 5f, Cooldown = 2.0f, Rockets = 3, Splash = 2.0f, Range = 250f };
    public static readonly WeaponDef Lrm = new()
    { Id = "lrm", Name = "LRM Pack", Short = "LRM", Type = WeaponSlotType.Missile, Weight = 10f, Damage = 7f, Cooldown = 3.5f, Rockets = 4, Splash = 3.0f, Range = 1000f };

    // The Hector's fixed cannon (not player-mountable in the lab list,
    // but its weight still counts for knockdown).
    public static readonly WeaponDef HectorAc20 = new()
    { Id = "ac20", Name = "AC/20", Short = "AC20", Type = WeaponSlotType.Ballistic, Weight = 16f, Damage = 18f, Cooldown = 2.4f, Range = 300f, MuzzleSpeed = 420f };

    public static readonly WeaponDef[] All =
    {
        SmallLaser, MediumLaser, LargeLaser, Ppc,
        MachineGun, Autocannon, HeavyCannon,
        Srm, Lrm
    };

    public static WeaponDef ById(string id)
    {
        foreach (var w in All) if (w.Id == id) return w;
        if (id == HectorAc20.Id) return HectorAc20; // fixed enemy cannon: resolvable, just not catalog-listed
        return null;
    }
}
