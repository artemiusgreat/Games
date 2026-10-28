// Pure game data — no engine references, so the catalog, the loadout
// rules and the knockdown math can move to another engine as-is.
// Weapon weights drive two things: the MechLab slot capacities and the
// knockdown rule (cumulative impact weight vs mech tonnage).

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

    public float PerRocketWeight => Rockets > 0 ? Weight / Rockets : Weight;
}

public static class WeaponCatalog
{
    public static readonly WeaponDef SmallLaser = new()
    { Id = "small_laser", Name = "Small Laser", Short = "S LAS", Type = WeaponSlotType.Energy, Weight = 1f, Damage = 5f, Cooldown = 0.25f };
    public static readonly WeaponDef MediumLaser = new()
    { Id = "medium_laser", Name = "Medium Laser", Short = "M LAS", Type = WeaponSlotType.Energy, Weight = 3f, Damage = 8f, Cooldown = 0.35f };
    public static readonly WeaponDef LargeLaser = new()
    { Id = "large_laser", Name = "Large Laser", Short = "LAS", Type = WeaponSlotType.Energy, Weight = 5f, Damage = 12f, Cooldown = 0.45f };
    public static readonly WeaponDef Ppc = new()
    { Id = "ppc", Name = "PPC", Short = "PPC", Type = WeaponSlotType.Energy, Weight = 9f, Damage = 22f, Cooldown = 1.4f };
    public static readonly WeaponDef MachineGun = new()
    { Id = "machine_gun", Name = "Machine Gun", Short = "MG", Type = WeaponSlotType.Ballistic, Weight = 2f, Damage = 3f, Cooldown = 0.12f };
    public static readonly WeaponDef Autocannon = new()
    { Id = "autocannon", Name = "Autocannon", Short = "AC", Type = WeaponSlotType.Ballistic, Weight = 12f, Damage = 26f, Cooldown = 0.85f };
    public static readonly WeaponDef HeavyCannon = new()
    { Id = "heavy_cannon", Name = "Heavy Cannon", Short = "HVY", Type = WeaponSlotType.Ballistic, Weight = 16f, Damage = 40f, Cooldown = 1.6f };
    // Missile packs are per-pod: a full salvo fires every alive pod's
    // pack. The default fit (LRM in both pods) reproduces the original
    // 8-rocket salvo, 4 with one pod lost.
    public static readonly WeaponDef Srm = new()
    { Id = "srm", Name = "SRM Pack", Short = "SRM", Type = WeaponSlotType.Missile, Weight = 4f, Damage = 5f, Cooldown = 2.0f, Rockets = 3, Splash = 2.0f };
    public static readonly WeaponDef Lrm = new()
    { Id = "lrm", Name = "LRM Pack", Short = "LRM", Type = WeaponSlotType.Missile, Weight = 10f, Damage = 7f, Cooldown = 3.5f, Rockets = 4, Splash = 3.0f };

    // The Atlas's fixed cannon (not player-mountable in the lab list,
    // but its weight still counts for knockdown).
    public static readonly WeaponDef AtlasAc20 = new()
    { Id = "ac20", Name = "AC/20", Short = "AC20", Type = WeaponSlotType.Ballistic, Weight = 16f, Damage = 18f, Cooldown = 2.4f };

    public static readonly WeaponDef[] All =
    {
        SmallLaser, MediumLaser, LargeLaser, Ppc,
        MachineGun, Autocannon, HeavyCannon,
        Srm, Lrm
    };

    public static WeaponDef ById(string id)
    {
        foreach (var w in All) if (w.Id == id) return w;
        return null;
    }
}
