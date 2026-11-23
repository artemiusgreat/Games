// Pure game data - no engine references, so the catalog, the loadout
// rules and the knockdown math can move to another engine as-is.
// Weapon weights drive two things: the RiderLab slot capacities and the
// knockdown rule (cumulative impact weight vs rider tonnage).

public enum WeaponSlotType { Energy, Ballistic, Missile }

// Energy beam look: the PPC's blue bolt vs the lasers' red.
public enum BeamKind { Laser, Ppc }

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
    // THE place to tune bullet speed - the player volley and the
    // AI gunnery both read it from here. All cannons share the
    // autocannon's 420 (owner: medium and large match the
    // smallest cannon); the machine gun keeps its slow 85 u/s
    // tracer stream (owner's standing exception).
    public float MuzzleSpeed;
    // Effective range in world units, as shown in the lab - and
    // ENFORCED by the firing code (owner): each weapon reaches
    // exactly its own Range and no further. Lasers/PPC resolve
    // per weapon on the view ray up to their Range; ballistic
    // shells burn out at theirs; missile salvos solve onto the
    // aim point clamped to theirs. The figures sit on the
    // owner's round-kilometre ladder (2026-11-14, superseding
    // the Sarna MW4 table): 0.25 / 0.5 / 0.75 / 1 / 1.25 / 1.5
    // km - SL, MG and SRM at 0.25; ML and AC/20 at 0.5; LL,
    // Autocannon and Heavy Cannon at 0.75; PPC at 1; LRM at
    // 1.5. In world units (1 unit = 0.001 km): 250 / 500 /
    // 750 / 1000 / 1250 / 1500.
    public float Range;
    // True when the RiderLab offers this weapon for mounting.
    // Resolution (ById, loadouts, knockdown weight) never
    // consults it - it only filters the lab's pick list.
    public bool CatalogListed = true;
    // Presentation and ballistics that used to be per-id
    // conditionals in the firing code - data now, read
    // generically like every other field.
    public BeamKind Beam;          // energy weapons: beam look
    public float SfxPitch;         // cannon report: pitch offset (semitones)
    public float SfxRate = 1f;     // cannon report: rate scale
    public float RocketBaseSpeed;  // missile packs: solver base speed
    public float RocketGravity;    // missile packs: solver gravity
    public float RocketTofMax;     // missile packs: time-of-flight cap
    public float RocketSpread = 1.1f; // missile packs: per-rocket cone in degrees (LRM: 0 - converges on the reticle, owner 2026-11-23)

    public float PerRocketWeight => Rockets > 0 ? Weight / Rockets : Weight;
}

public static class WeaponCatalog
{
    public static readonly WeaponDef SmallLaser = new()
    { Id = "small_laser", Name = "Small Laser", Short = "S LAS", Type = WeaponSlotType.Energy, Weight = 1f, Damage = 5f, Cooldown = 0.25f, Range = 250f };
    public static readonly WeaponDef MediumLaser = new()
    { Id = "medium_laser", Name = "Medium Laser", Short = "M LAS", Type = WeaponSlotType.Energy, Weight = 3f, Damage = 8f, Cooldown = 0.35f, Range = 500f };
    public static readonly WeaponDef LargeLaser = new()
    { Id = "large_laser", Name = "Large Laser", Short = "LAS", Type = WeaponSlotType.Energy, Weight = 5f, Damage = 12f, Cooldown = 0.45f, Range = 750f };
    public static readonly WeaponDef Ppc = new()
    { Id = "ppc", Name = "PPC", Short = "PPC", Type = WeaponSlotType.Energy, Weight = 9f, Damage = 22f, Cooldown = 1.4f, Range = 1000f, Beam = BeamKind.Ppc };
    public static readonly WeaponDef MachineGun = new()
    { Id = "machine_gun", Name = "Machine Gun", Short = "MG", Type = WeaponSlotType.Ballistic, Weight = 2f, Damage = 3f, Cooldown = 0.12f, Range = 250f, MuzzleSpeed = 4000f, SfxPitch = -7f, SfxRate = 1.65f };
    public static readonly WeaponDef Autocannon = new()
    { Id = "autocannon", Name = "Autocannon", Short = "AC", Type = WeaponSlotType.Ballistic, Weight = 12f, Damage = 26f, Cooldown = 0.85f, Range = 750f, MuzzleSpeed = 4000f };
    public static readonly WeaponDef HeavyCannon = new()
    { Id = "heavy_cannon", Name = "Heavy Cannon", Short = "HVY", Type = WeaponSlotType.Ballistic, Weight = 16f, Damage = 40f, Cooldown = 1.6f, Range = 750f, MuzzleSpeed = 4000f, SfxRate = 0.72f };
    // Missile packs are per-pod: a full salvo fires every alive pod's
    // pack. The default fit (LRM in both pods) reproduces the original
    // 8-rocket salvo, 4 with one pod lost.
    public static readonly WeaponDef Srm = new()
    { Id = "srm", Name = "SRM Pack", Short = "SRM", Type = WeaponSlotType.Missile, Weight = 4f, Damage = 5f, Cooldown = 2.0f, Rockets = 3, Splash = 2.0f, Range = 250f, RocketBaseSpeed = 46f, RocketGravity = 9f, RocketTofMax = 1.6f };
    public static readonly WeaponDef Lrm = new()
    { Id = "lrm", Name = "LRM Pack", Short = "LRM", Type = WeaponSlotType.Missile, Weight = 10f, Damage = 7f, Cooldown = 3.5f, Rockets = 4, Splash = 3.0f, Range = 1500f, RocketBaseSpeed = 36f, RocketGravity = 11f, RocketTofMax = 2.2f, RocketSpread = 0f };

    // The AC/20 is a catalog weapon like any other - it ships
    // fitted in the default fits that carry it (Hector's waist
    // gun, Prometheus's arm). It is excluded only from the
    // lab's mount list (CatalogListed = false), never from
    // resolution: no per-rider special cases in the engine.
    public static readonly WeaponDef Ac20 = new()
    { Id = "ac20", Name = "AC/20", Short = "AC20", Type = WeaponSlotType.Ballistic, Weight = 16f, Damage = 18f, Cooldown = 2.4f, Range = 500f, MuzzleSpeed = 4000f, CatalogListed = false };

    public static readonly WeaponDef[] All =
    {
        SmallLaser, MediumLaser, LargeLaser, Ppc,
        MachineGun, Autocannon, HeavyCannon, Ac20,
        Srm, Lrm
    };

    public static WeaponDef ById(string id)
    {
        foreach (var w in All) if (w.Id == id) return w;
        return null;
    }
}
