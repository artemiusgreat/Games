using Godot;
using System.Collections.Generic;
using System.Linq;

// Titans module: firing, projectiles, damage and part consequences.
// (Aiming - the aim ray and reticle - lives in Combat/Titans.Aiming.cs;
// this file is part of the Combat folder: everything that aims or shoots.)
// Split from the single-file build (round 22 cleanup); the class
// stays one partial class - see Titans.cs for the field block.
public partial class Titans
{

    // Muzzle hardpoints: forearm weapons sit on the elbow bones
    // (the Hand_* parts are bound there), pushed forward along
    // the torso facing so the shot clears the arm.
    private Vector3 ElbowMuzzle(RiderWalker w, bool left)
    {
        var bone = w.BoneWorldPos(left ? Bones.ElbowL : Bones.ElbowR);
        return bone + RiderWalker.HeadingVec(w.TorsoYawWorld) * ElbowMuzzleForward;
    }

    private Vector3 PlayerMuzzle(bool left) => ElbowMuzzle(_player, left);

    // Where a fitted weapon fires from, by hardpoint: the waist
    // gun slot shoots from the waist gun, a chest mount from the
    // upper torso, arm slots from their elbows.
    private Vector3 MuzzleFor(RiderWalker w, string slot)
    {
        if (slot == SlotIds.Gun) return TorsoGunMuzzle(w);
        if (slot == SlotIds.Torso)
        {
            var t = w.BoneWorldPos(Bones.Torso);
            return t + Vector3.Up * TorsoMuzzleUp + RiderWalker.HeadingVec(w.TorsoYawWorld) * TorsoMuzzleForward;
        }
        return ElbowMuzzle(w, slot != SlotIds.ArmR);
    }

    // Torso gun (the Hector's waist gun): torso bone origin plus a
    // right/up/forward offset in the torso frame. Used by whichever
    // seat pilots an Hector - its shells leave from the waist gun,
    // not the fist.
    private Vector3 TorsoGunMuzzle(RiderWalker w)
    {
        var torso = w.BoneWorldPos(Bones.Torso);
        var fwd = RiderWalker.HeadingVec(w.TorsoYawWorld);
        var right = new Vector3(-fwd.Z, 0f, fwd.X);
        return torso + right * GunMuzzleRight + fwd * GunMuzzleForward + Vector3.Up * GunMuzzleUp;
    }

    private Vector3 EnemyMuzzle() => MuzzleFor(_enemy, _enemyBallisticSlot ?? SlotIds.ArmR);

    // Beam glow pass: the soft halo beam is this many times
    // wider than the core beam (owner, 2026-11-25: named
    // constants for the bare values).
    private const float BeamGlowWidthScale = 3f;
    // Rocket-splash impact light intensity.
    private const float SplashLightIntensity = 3f;

    // Muzzle hardpoint offsets (owner, 2026-11-25: named and
    // rounded like everything else). Elbow guns sit this far
    // ahead of the elbow bone; the chest mount rides above
    // and ahead of the torso bone; the waist gun sits right /
    // forward / up in the torso frame; a pod with no volumes
    // of its own falls back to a shoulder spot beside the
    // torso (a volume-backed pod fires from its real centre,
    // lifted a touch).
    private const float ElbowMuzzleForward = 3f;
    private const float TorsoMuzzleUp = 2f;
    private const float TorsoMuzzleForward = 1.5f;
    private const float GunMuzzleRight = 1.5f;
    private const float GunMuzzleForward = 1.5f;
    private const float GunMuzzleUp = 2.5f;
    private const float PodMuzzleLift = 0.3f;
    private const float PodFallbackSide = 2.5f;
    private const float PodFallbackUp = 3.5f;
    private const float PodFallbackForward = 0.5f;

    // Beam and muzzle-flash recipe.
    private const float BeamWidth = 0.15f;
    private const float BeamLife = 0.1f;
    private const float BeamGlowAlpha = 0.3f;
    private const float MuzzleFlashSize = 0.3f;
    private const float CannonMuzzleFlashSize = 0.4f;
    private const float MuzzleFlashLife = 0.1f;
    // A laser's hit thickness: the cast inflate and the
    // prop-damage attribution reach are the same number.
    private const float LaserHitThickness = 1f;
    // Volley guns converge: impact FX spawn once per point
    // cluster this wide, damage stays per gun.
    private const float ImpactFxMergeRadius = 1f;
    // Below this aim distance the direction is meaningless;
    // the torso heading takes over.
    private const float MinAimDirLength = 0.5f;

    // Rocket guidance and lock (LaunchRocket): the axis
    // solve engages past this off-axis distance and this
    // much target lead; the blend onto the solved arc runs
    // at least this long and over this fraction of the
    // flight; a locked salvo tracks this fraction up the
    // target's height (his chest, from his own Height).
    private const float GuidanceOffAxisMin = 1.5f;
    private const float GuidanceMinRange = 15f;
    private const float GuideBlendMin = 0.55f;
    private const float GuideBlendFraction = 0.8f;
    private const float LockTrackHeightFraction = 0.6f;
    private const float LockCastInflate = 2.5f;
    private const float MinAxisLengthSq = 0.5f;
    private const float SalvoStagger = 0.05f;

    // Projectile dressing: ONE visual scale for rockets and
    // tracers alike (owner, 2026-11-25: a 1.5 vs 1.6 split
    // is not a distinction anyone can see - it is just more
    // code), and the speed below which a spawn does not
    // bother orienting to its velocity.
    private const float ProjectileScale = 1.5f;
    private const float MinOrientSpeedSq = 0.01f;
    // Trail puffs: spawn spacing (squared) and life.
    private const float PuffSpacingSq = 0.3f;
    private const float PuffLife = 0.4f;
    // At or above this weapon weight a shell hit reports
    // the heavy "cannon" feedback kind, below it "mg".
    private const float HeavyKindWeight = 10f;

    // Part consequences (owner's rules): one wrecked leg
    // limps at this speed factor with this lean; the head
    // hit flickers the HUD this long; the death explosion
    // sounds from this height at this pitch.
    private const float LimpSpeedFactor = 0.55f;
    private const float CrawlSpeedFactor = 0.25f;
    private const float LimpRollAngle = 0.1f;
    private const float HeadHitFlickerSeconds = 1.2f;
    private const float ExplosionSoundHeight = 4f;
    private const float ExplosionSoundPitch = 0.8f;

    // Weapon FX colours, created once and named for what
    // they paint (owner, 2026-11-25: a named colour reads
    // better than a bare Color at every Spawn call, whether
    // or not it allocates).
    private static readonly Color LaserBeamColor = new(1f, 0.82f, 0.18f);
    private static readonly Color PpcBeamColor = new(1f, 1f, 1f);
    private static readonly Color LaserMuzzleColor = new(1f, 0.8f, 0.25f);
    private static readonly Color LaserImpactColor = new(1f, 0.88f, 0.35f);
    private static readonly Color PpcImpactColor = new(0.7f, 0.95f, 1f);
    private static readonly Color LaserSparkColor = new(1f, 0.82f, 0.3f);
    private static readonly Color PpcSparkColor = new(0.65f, 0.95f, 1f);
    private static readonly Color LaserLightColor = new(1f, 0.78f, 0.3f);
    private static readonly Color PpcLightColor = new(0.55f, 0.9f, 1f);
    private static readonly Color CannonMuzzleColor = new(1f, 0.6f, 0.2f);
    private static readonly Color RocketColor = new(1f, 0.55f, 0.2f);
    private static readonly Color SplashFlashColor = new(1f, 0.62f, 0.25f);
    private static readonly Color CannonImpactColor = new(1f, 0.72f, 0.35f);
    private static readonly Color CannonSparkColor = new(1f, 0.8f, 0.5f);
    private static readonly Color CannonLightColor = new(1f, 0.75f, 0.4f);
    private static readonly Color BreakFlashColor = new(1f, 0.62f, 0.22f);
    private static readonly Color BreakFlashHotColor = new(1f, 0.88f, 0.55f);

    private void SpawnBeam(Vector3 a, Vector3 b, Color col, float width, float life)
    {
        _beamsSpawned++;
        var beam = WeaponFx.MakeBeam(a, b, col, width);
        AddChild(beam);
        _fx.Add((beam, life, 0));
        var glow = WeaponFx.MakeBeam(a, b, new Color(col.R, col.G, col.B, BeamGlowAlpha), width * BeamGlowWidthScale);
        AddChild(glow);
        _fx.Add((glow, life, 0));
    }

    // Legacy type volleys (FireLaser / FireAutocannon /
    // FireLrmSalvo) keep their signatures for the test
    // harnesses; live play fires the player's lab groups
    // through FireSelected, which runs the same volley bodies
    // per group subset.
    private void FireLaser(Vector3 aimPoint)
    {
        if (_playerDead) return;
        var fired = _gunsEnergy.FindAll(g => _partsPlayer.Alive(g.Slot));
        if (fired.Count == 0) { DenyWeapon(HitKinds.Laser); return; }
        if (_cdLaser > 0f) return;
        _cdLaser = 0f; foreach (var g in fired) _cdLaser = Mathf.Max(_cdLaser, g.Def.Cooldown);
        FireLaserVolley(aimPoint, fired);
    }

    private void FireLaserVolley(Vector3 aimPoint, System.Collections.Generic.List<(string Slot, WeaponDef Def)> fired)
    {
        // Physical beams (owner, 2026-11-25): every gun casts
        // from its OWN muzzle along its line through the aim
        // point and hits the first surface within its own
        // range - the same honesty as a cannon shell. A sky
        // aim sends the beam past the reticle point out to
        // its range. This replaces the round-20 camera-truth
        // model and everything that grew on it (the camera
        // verdict, the test rescue, the centre-line
        // obstruction pass, the per-beam clip): the owner
        // accepted beams striking a crest the reticle clears
        // in exchange for deleting that machinery. One cast
        // per gun is the whole model now.
        // Past the close cast, the converge point is the FAR
        // point - the same rule shells and rockets apply
        // (FireCannonVolley / FireSalvoVolley). Converging on
        // the 220u point instead makes the beams cross there
        // and diverge beyond it: at 500u+ the volley straddles
        // the rider under the reticle and can only hit things
        // behind him (owner's repro, 2026-11-25 - the beam
        // rewrite dropped this rule; the other volleys kept
        // it, which is why only energy weapons missed).
        if (_aimParallel) aimPoint = _cam.GlobalPosition + _aimDir * _farConverge;
        bool anyPpc = false;
        foreach (var g in fired) if (g.Def.Beam == BeamKind.Ppc) { anyPpc = true; break; }
        var fxPoints = new System.Collections.Generic.List<Vector3>();
        bool anyHit = false;
        for (int i = 0; i < fired.Count; i++)
        {
            var mz = MuzzleForSlot(fired[i].Slot, false);
            bool ppc = fired[i].Def.Beam == BeamKind.Ppc;
            var toAim = aimPoint - mz;
            var dir = toAim.Length() > MinAimDirLength ? toAim.Normalized() : RiderWalker.HeadingVec(_player.TorsoYawWorld);
            var hit = AimRay(mz, dir, fired[i].Def.Range, LaserHitThickness);
            var beamEnd = hit.Hit ? hit.Point : mz + dir * fired[i].Def.Range;
            // Beam colours (owner, 2026-11-24; PPC reworked
            // 2026-11-25): ALL lasers are yellow; the PPC is
            // the same beam in white.
            if (ppc) SpawnBeam(mz, beamEnd, PpcBeamColor, BeamWidth, BeamLife);
            else SpawnBeam(mz, beamEnd, LaserBeamColor, BeamWidth, BeamLife);
            if (i == 0) PlaySfx(ppc ? _sfxPpc : _sfxLaser, mz, 0f, 1f); // the owner's recordings play at natural pitch; PPC has its own (2026-11-22)
            SpawnFlash(mz, ppc ? PpcBeamColor : LaserMuzzleColor, MuzzleFlashSize, MuzzleFlashLife);
            if (!hit.Hit) continue;
            anyHit = true;
            // Impact FX once per distinct hit point (a
            // volley's guns converge, so most share one);
            // damage is per gun, on its own hit. Impact tint
            // follows the volley (owner, 2026-11-24):
            // electric blue-white when a PPC is in it, warm
            // yellow for a pure laser volley.
            bool fresh = true;
            foreach (var pt in fxPoints) if (pt.DistanceTo(hit.Point) < ImpactFxMergeRadius) { fresh = false; break; }
            if (fresh)
            {
                fxPoints.Add(hit.Point);
                SpawnFlash(hit.Point, anyPpc ? PpcImpactColor : LaserImpactColor, 0.5f, 0.15f);
                SpawnSparks(hit.Point, anyPpc ? PpcSparkColor : LaserSparkColor, 16, 6.0f); // electric breaker spray
                SpawnImpactLight(hit.Point, anyPpc ? PpcLightColor : LaserLightColor, 2.5f, 10f, 0.3f);
                SpawnBurst(hit.Point, SparkParticleMat, 26, 0.6f, 6f, 11f, new Vector3(0f, -12f, 0f), 0.1f, 0.5f, 1f);
            }
            Dbg.Print($"WPN LASER hit={hit.Target} t={hit.T:F1} zone={hit.Zone} part={hit.Part} point={hit.Point}");
            if (hit.Target == Roles.Enemy) ApplyDamage(_enemy, fired[i].Def.Damage, hit, Roles.Player, fired[i].Def.Weight, HitKinds.Laser);
            else if (hit.Target == Roles.Foe && _foe != null) ApplyDamage(_foe.Walker, fired[i].Def.Damage, hit, Roles.Player, fired[i].Def.Weight, HitKinds.Laser);
            else if (AllyByRole(hit.Target) is { } la) ApplyDamage(la.Walker, fired[i].Def.Damage, hit, Roles.Player, fired[i].Def.Weight, HitKinds.Laser);
            else if (hit.Target == Roles.Prop) DamageProp(hit.Point, fired[i].Def.Damage, LaserHitThickness); // laser attribution reaches as far as its hit thickness
        }
        if (!anyHit) Dbg.Print("WPN LASER hit=none");
    }

    // Player projectiles converge at most this far (round 7). Aiming
    // over a target puts the aim ray's hit on a hillside up to 200u
    // out; solving ballistics for THAT point made rockets mortar
    // almost vertically (their up-compensation grows with the solved
    // range) while flat cannon shells still crossed the enemy on the
    // line - the owner's screenshot. Capping the solution range
    // keeps every weapon's launch angle proportional to where the
    // pointer actually is. Real duel ranges are 15-60u: unaffected.
    private const float ConvergeMax = 75f;

    private void FireAutocannon(Vector3 aimPoint)
    {
        if (_playerDead) return;
        var fired = _gunsBallistic.FindAll(g => _partsPlayer.Alive(g.Slot));
        if (fired.Count == 0) { DenyWeapon(WeaponIds.Autocannon); return; }
        if (_cdAc > 0f) return;
        _cdAc = 0f; foreach (var g in fired) _cdAc = Mathf.Max(_cdAc, g.Def.Cooldown);
        FireCannonVolley(aimPoint, fired);
    }

    private void FireCannonVolley(Vector3 aimPoint, System.Collections.Generic.List<(string Slot, WeaponDef Def)> fired)
    {
        // Every fitted cannon fires (multi-fit volley), each from
        // its own hardpoint on the pointer line. No clamp needed:
        // the cannon fires the line directly with no range
        // compensation, so a far aim point changes nothing but
        // parallax. (Rockets were the mortar case - LaunchRocket.)
        foreach (var (slot, def) in fired)
        {
            var muzzle = MuzzleForSlot(slot, true);
            var toAim = aimPoint - muzzle;
            // Converge on the aim point when the crosshair has
            // one within reach. When it rests beyond the cast
            // (_aimParallel), converge on the point where it
            // actually rests (the long cast's distance, 1500u
            // for sky) instead of the 220u far point - shells
            // then land on the crosshair at any range instead
            // of crossing the sight line at 220u and sailing
            // over far targets.
            var dir = _aimParallel
                ? (_cam.GlobalPosition + _aimDir * _farConverge - muzzle).Normalized()
                : toAim.Length() > MinAimDirLength ? toAim.Normalized() : RiderWalker.HeadingVec(_player.TorsoYawWorld);
            // Shell speed lives on the weapon def (WeaponDef.cs:
            // MuzzleSpeed) - one place to tune it for every
            // cannon, player and AI alike. Ballistic shells fly
            // STRAIGHT (owner): no gravity, no drop - the line
            // the crosshair draws is the line the shell flies.
            // (Rockets keep their solved arcs.)
            (float spd, float grav) = (def.MuzzleSpeed, 0f);
            var shell = SpawnProjectile(muzzle, dir * spd, grav, def.Damage, 0f, Roles.Player, rocket: false, weaponWeight: def.Weight);
            shell.Life = def.Range / spd; // burns out at the weapon's range (owner)
            PlaySfx(_sfxCannon, muzzle, def.SfxPitch, def.SfxRate);
            SpawnFlash(muzzle, CannonMuzzleColor, CannonMuzzleFlashSize, MuzzleFlashLife);
            Dbg.Print($"WPN AC fired {def.Id} dir={dir}");
        }
    }

    // LRM salvo: rockets staggered SalvoStagger apart, alternating ear pods.
    // Each rocket is dumb ballistics: velocity solved to land on the aim
    // point (up-compensated for gravity), seeded spread, 7 damage with
    // 3.0u splash falloff on impact. A lost pod removes its 4 rockets;
    // both pods gone = the LRM is offline.
    // The salvo's staggered launch queue (0.07s between
    // rockets): FireLrmSalvo fills it, the frame step drains it.
    private readonly System.Collections.Generic.List<(float Delay, bool Left, Vector3 Aim, WeaponDef Def, RiderWalker Lock)> _lrmQueue = new();

    private void ProcessLrmQueue(float dt)
    {
        for (int i = _lrmQueue.Count - 1; i >= 0; i--)
        {
            var q = _lrmQueue[i];
            q.Delay -= dt;
            if (q.Delay <= 0f) { _lrmQueue.RemoveAt(i); LaunchRocket(q.Aim, q.Left, q.Def, q.Lock); }
            else _lrmQueue[i] = q;
        }
    }

    private void FireLrmSalvo(Vector3 aimPoint, RiderWalker lockTarget = null)
    {
        if (_playerDead) return;
        bool podL = _podGunsL.Count > 0 && _partsPlayer.Alive(_podGunsL[0].Slot);
        bool podR = _podGunsR.Count > 0 && _partsPlayer.Alive(_podGunsR[0].Slot);
        if (!podL && !podR) { DenyWeapon(WeaponIds.Lrm); return; }
        if (_cdLrm > 0f) return;
        _cdLrm = 0f;
        foreach (var g in _podGunsL) _cdLrm = Mathf.Max(_cdLrm, g.Def.Cooldown);
        foreach (var g in _podGunsR) _cdLrm = Mathf.Max(_cdLrm, g.Def.Cooldown);
        FireSalvoVolley(aimPoint,
            podL ? _podGunsL : new System.Collections.Generic.List<(string Slot, WeaponDef Def)>(),
            podR ? _podGunsR : new System.Collections.Generic.List<(string Slot, WeaponDef Def)>(),
            lockTarget);
    }

    private void FireSalvoVolley(Vector3 aimPoint,
        System.Collections.Generic.List<(string Slot, WeaponDef Def)> podLGuns,
        System.Collections.Generic.List<(string Slot, WeaponDef Def)> podRGuns,
        RiderWalker lockTarget = null)
    {
        // Past the close cast, the salvo solves onto the point
        // the crosshair actually rests on (the far cast's point);
        // LaunchRocket clamps it to each pack's own Range.
        if (_aimParallel) aimPoint = _cam.GlobalPosition + _aimDir * _farConverge;
        bool podL = podLGuns.Count > 0, podR = podRGuns.Count > 0;
        // Each pod fires every pack fitted in it (multi-fit);
        // pods interleave.
        var left = new System.Collections.Generic.List<WeaponDef>();
        var right = new System.Collections.Generic.List<WeaponDef>();
        foreach (var (_, def) in podLGuns) for (int i = 0; i < def.Rockets; i++) left.Add(def);
        foreach (var (_, def) in podRGuns) for (int i = 0; i < def.Rockets; i++) right.Add(def);
        var ordered = new System.Collections.Generic.List<(bool Left, WeaponDef Def)>();
        int li = 0, ri = 0;
        while (li < left.Count || ri < right.Count)
        {
            if (li < left.Count) ordered.Add((true, left[li++]));
            if (ri < right.Count) ordered.Add((false, right[ri++]));
        }
        for (int i = 0; i < ordered.Count; i++)
            _lrmQueue.Add((i * SalvoStagger, ordered[i].Left, aimPoint, ordered[i].Def, lockTarget));
        PlaySfx(_sfxRocket, PodMuzzle(podL), -1f);
        Dbg.Print($"WPN LRM salvo queued ({ordered.Count} rockets, pods L={podL} R={podR})");
    }

    private Vector3 PodMuzzle(bool left)
    {
        // Rig-true: rockets leave from the pod part's actual center -
        // a chassis may carry a single rack, on either shoulder.
        string id = left ? SlotIds.PodL : SlotIds.PodR;
        // A part with no volumes has no centre of its own:
        // PartCenterWorld falls back to the rider's ROOT - the
        // ground at its feet - and rockets spawned there die on
        // the first rise of ground (owner, 2026-11-23: a Hector,
        // whose pod_L is folded into the shoulder mesh, salvoed
        // into the slope at its own feet). The torso-bone
        // fallback below is the sane shoulder spot for such pods.
        if (_partsPlayer != null && _partsPlayer.PartHasVolumes(id))
            return _partsPlayer.PartCenterWorld(id) + Vector3.Up * PodMuzzleLift;
        var torso = _player.BoneWorldPos(Bones.Torso);
        var fwd = RiderWalker.HeadingVec(_player.TorsoYawWorld);
        var right = new Vector3(-fwd.Z, 0f, fwd.X);
        return torso + right * (left ? -PodFallbackSide : PodFallbackSide) + Vector3.Up * PodFallbackUp + fwd * PodFallbackForward;
    }

    // Shared shot spread (player rockets and foe gunnery):
    // a random cone perturbation, sqrt-distributed so most
    // shots land near the center of the cone.
    private Vector3 ApplyInaccuracy(Vector3 dir, float maxDeg)
    {
        float theta = Mathf.DegToRad(maxDeg) * Mathf.Sqrt((float)_rng.NextDouble());
        float phi = (float)(_rng.NextDouble() * Mathf.Tau);
        var side = dir.Cross(Mathf.Abs(dir.Y) < 0.9f ? Vector3.Up : Vector3.Right).Normalized();
        var up2 = side.Cross(dir).Normalized();
        var axis = (side * Mathf.Cos(phi) + up2 * Mathf.Sin(phi)).Normalized();
        return dir.Rotated(axis, theta).Normalized();
    }


    // Is this walker still in the fight? (For the LRM
    // lock's tracking closure.)
    private bool WalkerAlive(RiderWalker w) =>
        w == _player ? !_playerDead
        : w == _enemy ? !_enemyDead
        : _foe != null && w == _foe.Walker ? !_foe.Dead
        : AllyOf(w) is { } a ? !a.Dead : false;

    // The rider under the reticle right now, if it is an
    // ENEMY - the LRM lock candidate (owner, 2026-11-25).
    // A teammate under the reticle never locks: the aim
    // rests on him, but the salvo stays a reticle salvo.
    private RiderWalker AimLockWalker()
    {
        // Fast path: the rider the reticle already names.
        var w = LockWalkerForRole(_aimTarget);
        if (w != null) return w;
        // LRM reach (owner, 2026-11-25: "LRM don't seem to
        // follow the target after it's moved"): the cast
        // that fills _aimTarget (TorsoAimPoint) only reaches
        // Aim.ConvergenceRange = 220u, so at real LRM ranges
        // no lock was EVER acquired in live play - the
        // batch-22 probe passed the lock in directly and
        // never tested acquisition. The lock gets its own
        // cast at full FarRange along the live aim, with a
        // small inflate so "reticle on the enemy" stays
        // achievable by hand at 1000u+. First hit wins:
        // terrain or a teammate in the line blocks the lock
        // exactly as it blocks the shot (AimRay's team rule).
        var hit = AimRay(_cam.GlobalPosition, AimDirWorld(), Aim.FarRange, LockCastInflate);
        return LockWalkerForRole(hit.Hit ? hit.Target : "");
    }

    private RiderWalker LockWalkerForRole(string role)
    {
        RiderWalker w = role == Roles.Enemy ? _enemy
            : role == Roles.Foe && _foe != null ? _foe.Walker
            : AllyByRole(role)?.Walker;
        return w != null && TeamOf(w) > 0 ? w : null;
    }

    private void LaunchRocket(Vector3 aimPoint, bool left, WeaponDef def, RiderWalker lockTarget = null)
    {
        _rocketsLaunched++;
        var muzzle = PodMuzzle(left);
        var toAim = aimPoint - muzzle;
        // The pack reaches its own Range and no further: a
        // farther aim point solves onto the max-range point on
        // the same line (the salvo falls short, honestly).
        if (toAim.Length() > def.Range) toAim = toAim.Normalized() * def.Range;
        // The lob is solved for the TRUE aim point at any range -
        // what is capped now is the TIME OF FLIGHT, not the range.
        // History: round 7 capped the solved range at 75u because
        // solving for a 200u hillside point mortared the rockets
        // vertically (the up-compensation grows with the solved
        // tof). But the range cap silently broke every longer
        // shot: measured by --lrm-probe (2026-10-31), a 110u shot
        // from the mesa landed 16/16 rockets on terrain, on
        // average 12u short and 9u BELOW the aim point - the
        // owner's "rockets land lower than the cursor from the
        // hill". Capping tof bounds the lob AND the flight time
        // (long shots simply fly faster past ~79u), and the solve
        // stays exact: the rocket passes through the aim point.
        (float baseSpeed, float grav, float tofMax) = (def.RocketBaseSpeed, def.RocketGravity, def.RocketTofMax);
        // Axis-converged solve (owner's Atlantis lip, 2026-11-13):
        // the crosshair is CAMERA truth, but the pod hangs below
        // and behind that line, so a salvo solved from the pod
        // flies the pod's shallower line and clips terrace lips
        // the crosshair clears by metres. The arc is therefore
        // solved from a VIRTUAL MUZZLE - the pod's projection onto
        // the aim axis - and the rocket, still spawned at the pod,
        // opens with guidance (Projectile.Step) that bends it onto
        // that arc within the first fraction of a second; after
        // that it flies the exact solution and passes through the
        // aim point as before. Engages only when the pod sits
        // meaningfully off the axis and the target is well past
        // the rider; close shots and on-axis pods keep the plain
        // pod solve (solveFrom == muzzle, identical numbers).
        var axisOrigin = (_camMode == CamModes.Chase && _cam != null) ? _cam.GlobalPosition : _aimFrom;
        var axisDir = _aimDir;
        Vector3 solveFrom = muzzle;
        bool guided = false;
        if (axisDir.LengthSquared() > MinAxisLengthSq)
        {
            float tPod = (muzzle - axisOrigin).Dot(axisDir);
            float tAim = (muzzle + toAim - axisOrigin).Dot(axisDir);
            var onAxis = axisOrigin + axisDir * tPod;
            if ((muzzle - onAxis).Length() > GuidanceOffAxisMin && tAim > tPod + GuidanceMinRange)
            {
                solveFrom = onAxis;
                guided = true;
            }
        }
        var toSolve = (muzzle + toAim) - solveFrom;
        float dist = Mathf.Max(toSolve.Length(), 1f);
        // Per-pack dispersion (owner, 2026-11-23 + 2026-11-25):
        // both packs converge on the reticle - spread 0, every
        // rocket flies the exact solve - strictly inside its
        // Range (the clamp above).
        var dir = def.RocketSpread > 0f ? ApplyInaccuracy(toSolve.Normalized(), def.RocketSpread) : toSolve.Normalized();
        float tof = Mathf.Min(dist / baseSpeed, tofMax);
        float speed = dist / tof;
        var vel = dir * speed + Vector3.Up * (0.5f * grav * tof);
        var proj = SpawnProjectile(muzzle, vel, grav, def.Damage, def.Splash, Roles.Player, rocket: true, weaponWeight: def.PerRocketWeight);
        if (guided)
        {
            proj.GuideActive = true;
            proj.GuideOrigin = solveFrom;
            proj.GuideStart = muzzle;
            // Converge AT the reticle, not out of the tubes
            // (owner, 2026-11-23): the blend used to finish in
            // 0.55 s, so the salvo merged into a single file
            // almost at the muzzle and flew most of the way as
            // one dotted line. The blend now runs over 80% of
            // the flight - the rockets ride their own offset
            // paths and curve together only near the target,
            // still passing exactly through the aim point
            // (the blend completes before arrival).
            proj.GuideDur = Mathf.Max(GuideBlendMin, tof * GuideBlendFraction);
            proj.GuideVel0 = vel;
            proj.GuideTime = 0f;
        }
        // LRM lock (owner, 2026-11-25): the salvo was fired
        // with the reticle on an enemy - the rockets home on
        // THAT rider, re-solving at his live chest (the
        // Projectile.Step GuideTrack branch), instead of the
        // fixed point. An unguided launch gets the guidance
        // frame switched on with the pod's own solve, so the
        // re-solve has an arc to steer. If the target dies
        // mid-flight the tracking stops and the salvo
        // finishes at his last position.
        if (lockTarget != null && def.Id == WeaponIds.Lrm)
        {
            if (!guided)
            {
                proj.GuideActive = true;
                proj.GuideOrigin = muzzle;
                proj.GuideStart = muzzle;
                proj.GuideVel0 = vel;
                proj.GuideTime = 0f;
                proj.GuideDur = Mathf.Max(GuideBlendMin, tof * GuideBlendFraction);
            }
            proj.GuideEnd = muzzle + toAim;
            proj.GuideBaseSpeed = baseSpeed;
            proj.GuideTofMax = tofMax;
            var lt = lockTarget;
            proj.GuideTrack = () => WalkerAlive(lt) ? lt.RootPos + Vector3.Up * (lt.Height * LockTrackHeightFraction) : (Vector3?)null;
            Dbg.Print($"LRM LOCK on rider at {lt.RootPos}");
        }
        SpawnFlash(muzzle, RocketColor, MuzzleFlashSize, MuzzleFlashLife);
    }

    private void OnRocketStep(Projectile p)
    {
        if (_lastPuff.TryGetValue(p, out var last) && last.DistanceSquaredTo(p.GlobalPosition) < PuffSpacingSq) return;
        _lastPuff[p] = p.GlobalPosition;
        var puff = WeaponFx.MakeTrailPuff(p.GlobalPosition);
        AddChild(puff);
        _puffs.Add((puff, PuffLife));
    }

    private void ExplodeSplash(HitModel.Result hit, Projectile p)
    {
        RiderWalker direct = hit.Target == Roles.Player ? _player
            : hit.Target == Roles.Foe && _foe != null ? _foe.Walker
            : hit.Target == Roles.Enemy ? _enemy
            : AllyByRole(hit.Target)?.Walker;
        if (direct != null) ApplyDamage(direct, p.Damage, hit, p.OwnerId, p.WeaponWeight, HitKinds.Rocket);
        var roster = new System.Collections.Generic.List<(RiderWalker, string)> { (_player, Roles.Player), (_enemy, Roles.Enemy) };
        if (_foe != null) roster.Add((_foe.Walker, Roles.Foe));
        foreach (var a in _allies) roster.Add((a.Walker, a.Role));
        foreach (var (w, id) in roster)
        {
            if (w == direct) continue;
            float d = _hit.DistanceToRiderSurface(hit.Point, id);
            if (d < p.SplashRadius)
            {
                var splashHit = hit;
                splashHit.Target = id;
                splashHit.Zone = HitZone.Splash;
                ApplyDamage(w, p.Damage * (1f - d / p.SplashRadius), splashHit, p.OwnerId, p.WeaponWeight, HitKinds.Rocket);
            }
        }
    }

    private Projectile SpawnProjectile(Vector3 pos, Vector3 vel, float gravity, float dmg,
        float splash, string owner, bool rocket, float weaponWeight = 0f)
    {
        var p = new Projectile
        {
            Vel = vel,
            Gravity = gravity,
            Damage = dmg,
            SplashRadius = splash,
            OwnerId = owner,
            WeaponWeight = weaponWeight
        };
        var vis = rocket ? WeaponFx.MakeRocket() : (Node3D)WeaponFx.MakeTracer();
        vis.Scale = Vector3.One * ProjectileScale; // owner: projectiles slightly larger, one scale for both kinds (2026-11-25)
        p.AddChild(vis);
        AddChild(p);
        p.GlobalPosition = pos;
        if (vel.LengthSquared() > MinOrientSpeedSq) p.LookAt(pos + vel, Vector3.Up);
        p.OnHit = OnProjectileHit;
        if (rocket) p.OnStep = OnRocketStep;
        _shots.Add(p);
        return p;
    }

    private void OnProjectileHit(HitModel.Result hit, Projectile p)
    {
        Dbg.Print($"WPN PROJ owner={p.OwnerId} hit={hit.Target} zone={hit.Zone} part={hit.Part} t={hit.T:F1} point={hit.Point}");
        if (_lrmProbe) _lrmImpacts.Add((hit.Target, hit.Point));
        if (p.SplashRadius > 0f)
        {
            SpawnExplosionFx(hit.Point); // rockets: mini-explosion
            SpawnFlash(hit.Point, SplashFlashColor, 1.5f, 0.3f);
            SpawnImpactLight(hit.Point, RocketColor, SplashLightIntensity, 14f, 0.5f);
            SpawnBurst(hit.Point, FireParticleMat, 54, 1f, 5f, 10f, new Vector3(0f, -7f, 0f), 0.15f, 0.8f, 1.5f);
            ExplodeSplash(hit, p);
            if (hit.Target == Roles.Prop) DamageProp(hit.Point, p.Damage);
            return;
        }
        SpawnFlash(hit.Point, CannonImpactColor, 0.6f, 0.2f);
        SpawnSparks(hit.Point, CannonSparkColor, 10, 5f);
        _fxSparkBursts--; // cannon impact sparks are not a laser burst
        SpawnBurst(hit.Point, FireParticleMat, 16, 0.7f, 4.5f, 8.5f, new Vector3(0f, -9f, 0f), 0.15f, 0.6f, 1f);
        SpawnImpactLight(hit.Point, CannonLightColor, 2.5f, 11f, 0.35f);
        if (hit.Target == Roles.Prop) DamageProp(hit.Point, p.Damage);
        string kind = p.WeaponWeight >= HeavyKindWeight ? HitKinds.Cannon : HitKinds.Mg;
        if (hit.Target == Roles.Player) ApplyDamage(_player, p.Damage, hit, p.OwnerId, p.WeaponWeight, kind);
        else if (hit.Target == Roles.Enemy) ApplyDamage(_enemy, p.Damage, hit, p.OwnerId, p.WeaponWeight, kind);
        else if (hit.Target == Roles.Foe && _foe != null) ApplyDamage(_foe.Walker, p.Damage, hit, p.OwnerId, p.WeaponWeight, kind);
        else if (AllyByRole(hit.Target) is { } pa) ApplyDamage(pa.Walker, p.Damage, hit, p.OwnerId, p.WeaponWeight, kind);
    }

    private void ApplyDamage(RiderWalker target, float dmg, HitModel.Result hit, string source, float weaponWeight = 0f, string kind = "")
    {
        bool isPlayer = target == _player;
        bool isFoe = _foe != null && target == _foe.Walker;
        var ally = isPlayer || isFoe ? null : AllyOf(target);
        bool isAlly = ally != null;
        if (isPlayer ? _playerDead : isFoe ? _foe.Dead : isAlly ? ally.Dead : _enemyDead) return;
        var parts = isPlayer ? _partsPlayer : isFoe ? _foe.Parts : isAlly ? ally.Parts : _partsEnemy;
        // Damage routing (owner, 2026-11-03): EVERY hit burns the
        // main pool, whatever part it lands on - arms, legs, head,
        // pods all count. Parts additionally take their own damage
        // toward breaking or wrecking, exactly as before. Counters still record the nominal damage once per
        // hit event. Attribution: a box-ray hit already carries its part
        // (a box surface point is unambiguous, while an interior SDF
        // resolve can name a neighbor whose AABB overlaps the spot);
        // capsule points, splash and crafted hits resolve by nearest
        // part volume at the point.
        string partId = !string.IsNullOrEmpty(hit.Part)
            ? hit.Part
            : parts.AttributePoint(hit.Point);
        if (isPlayer) { _playerDmgEvents++; _playerDmgTotal += dmg; }
        else if (!isFoe) { _enemyDmgEvents++; _enemyDmgTotal += dmg; }
        float poolNow = isPlayer ? _hpPlayer : isFoe ? _foe.Hp : isAlly ? ally.Hp : _hpEnemy;
        float poolMax = isPlayer ? _playerSpec.MaxHp : isFoe ? _foe.Spec.MaxHp : isAlly ? ally.Spec.MaxHp : _enemySpec.MaxHp;
        // Arm-break gate (owner, 2026-11-12; PartRules):
        // while the pool after this hit is still above the
        // gate, arm damage is capped so the arm keeps 1 HP -
        // shot to pieces but attached. Once the rider is
        // critical the cap lifts and the arm can come off.
        // The pool itself burns exactly as before.
        float partDmg = dmg;
        if ((partId == SlotIds.ArmL || partId == SlotIds.ArmR)
            && poolNow - dmg > poolMax * PartRules.ArmBreakPoolFraction)
            partDmg = Mathf.Min(dmg, Mathf.Max(parts.HpOf(partId) - 1f, 0f));
        float poolDmg = dmg;
        parts.DamagePart(partId, partDmg); // torso is pool-owned: a no-op there
        if (poolDmg > 0f)
        {
            if (isPlayer) _hpPlayer = Mathf.Max(_hpPlayer - poolDmg, 0f);
            else if (isFoe) _foe.Hp = Mathf.Max(_foe.Hp - poolDmg, 0f);
            else if (isAlly) ally.Hp = Mathf.Max(ally.Hp - poolDmg, 0f);
            else _hpEnemy = Mathf.Max(_hpEnemy - poolDmg, 0f);
        }
        float hp = isPlayer ? _hpPlayer : isFoe ? _foe.Hp : isAlly ? ally.Hp : _hpEnemy;
        Dbg.Print($"WPN DMG target={(isPlayer ? Roles.Player : isFoe ? Roles.Foe : isAlly ? ally.Role : Roles.Enemy)} dmg={dmg:F1} zone={hit.Zone} part={partId} poolDmg={poolDmg:F1} hp={hp:F1} src={source}");
        // Detection (owner, 2026-11-25): being HIT by the
        // player alerts a defender at any distance - it knows
        // it is under attack and engages, sight or no sight.
        if (source == Roles.Player)
        {
            if (isFoe) _foe.Alerted = true;
            else if (isAlly && ally.Guard) ally.Alerted = true;
            else if (!isPlayer && !isAlly) _foe0Alerted = true;
        }
        OnDamaged(target, dmg, hit.Point, source, kind);
        if (hp <= 0f) { Die(target); return; }
        // Knockdown (owner's rule, current): weapon impacts NEVER topple
        // a rider, however heavy. A rider falls only when it loses a leg
        // (see SyncLegConsequences), and then it stays down.
    }

    // A part just broke off (fired from RiderParts.DamagePart): spark burst
    // at the separation point, HUD feed message, then the functional
    // consequences.
    private void OnPartBroken(RiderParts parts, string partId, Vector3 center)
    {
        // During a death explosion every part breaks at once: no feed
        // spam, no per-part consequences (leg loss would re-enter Die).
        if (_exploding) return;
        SpawnFlash(center, BreakFlashColor, 1f, 0.15f);
        SpawnFlash(center, BreakFlashHotColor, 0.5f, 0.1f);
        var part = parts.GetPart(partId);
        ApplyPartConsequences(parts, partId);
    }

    // Functional fallout of losing a part. Weapon mounts are gated live at
    // the fire/gunnery checks - every mount answers to its own part
    // (arm_L -> lasers, the waist gun -> its cannons, pods -> salvo
    // size, Hector head -> spread + tracking lag), so
    // the stateful consequences latched here are the legs and the player's
    // head (HUD disturbance timer; the sway itself is applied in the HUD
    // step from part state).
    private void ApplyPartConsequences(RiderParts parts, string partId)
    {
        if (partId == PartIds.LegL || partId == PartIds.LegR)
            SyncLegConsequences(WalkerOf(parts), parts);
        else if (partId == PartIds.Head && parts == _partsPlayer)
            _hudFlickerT = HeadHitFlickerSeconds;
        if (parts == _partsPlayer) AutoSwitchWeapon();
    }

    private bool AnyGunAlive(System.Collections.Generic.List<(string Slot, WeaponDef Def)> guns)
    {
        foreach (var g in guns) if (_partsPlayer.Alive(g.Slot)) return true;
        return false;
    }

    private bool WeaponAlive(int sel)
    {
        if (sel < 0 || sel >= _groups.Count) return false;
        foreach (var g in _groups[sel].Guns)
            if (_partsPlayer.Alive(g.Slot)) return true;
        return false;
    }

    // Live fire (owner's groups): the selected lab group fires
    // as ONE volley - lasers through the beam path, cannons
    // through the shell path, pod weapons as a salvo. Cooldowns
    // are per weapon (owner): a weapon still cooling sits the
    // volley out; every fired weapon starts its own clock.
    private void FireSelected(Vector3 aimPoint)
    {
        if (_playerDead || _groups.Count == 0) return;
        var grp = _groups[Mathf.Clamp(_weaponSel, 0, _groups.Count - 1)];
        var las = new System.Collections.Generic.List<(string Slot, WeaponDef Def)>();
        var can = new System.Collections.Generic.List<(string Slot, WeaponDef Def)>();
        var podL = new System.Collections.Generic.List<(string Slot, WeaponDef Def)>();
        var podR = new System.Collections.Generic.List<(string Slot, WeaponDef Def)>();
        var firedGuns = new System.Collections.Generic.List<WpnGun>();
        bool anyAlive = false;
        foreach (var g in grp.Guns)
        {
            if (!_partsPlayer.Alive(g.Slot)) continue;
            anyAlive = true;
            if (g.Cd > 0f) continue; // still cooling: sits this volley out
            firedGuns.Add(g);
            var t = (g.Slot, g.Def);
            if (g.Path == FirePath.Energy) las.Add(t);
            else if (g.Path == FirePath.Cannon) can.Add(t);
            else if (g.Path == FirePath.PodL) podL.Add(t);
            else podR.Add(t);
        }
        if (!anyAlive)
        {
            grp.DenyT = 0.5f;
            Dbg.Print($"WPN DENIED group {grp.Num} (mounting parts destroyed)");
            return;
        }
        if (firedGuns.Count == 0) return; // alive but all cooling
        if (las.Count > 0) FireLaserVolley(aimPoint, las);
        if (can.Count > 0) FireCannonVolley(aimPoint, can);
        if (podL.Count > 0 || podR.Count > 0) FireSalvoVolley(aimPoint, podL, podR, AimLockWalker());
        foreach (var g in firedGuns) g.Cd = g.Def.Cooldown;
    }

    private Vector3 MuzzleForSlot(string slot, bool ballistic) => MuzzleFor(_player, slot);

    // When the selected weapon is destroyed, fall to the next live one.
    private void AutoSwitchWeapon()
    {
        if (_partsPlayer == null || WeaponAlive(_weaponSel)) return;
        for (int i = 1; i <= _groups.Count; i++)
        {
            int cand = (_weaponSel + i) % _groups.Count;
            if (WeaponAlive(cand)) { _weaponSel = cand; return; }
        }
    }

    // Recompute a walker's leg state from its parts. Owner's rule
    // (current): a destroyed leg never falls off and never fells or
    // kills the rider - it stays attached, darkened and deformed, and
    // the rider LIMPS: one wrecked leg = 55% speed with a lean and the
    // wrecked leg keeps stepping at the throttled pace; both legs
    // wrecked = a 25% crawl.
    // Death comes only from the HP pool reaching zero.
    private void SyncLegConsequences(RiderWalker w, RiderParts parts)
    {
        bool lDead = !parts.Alive(PartIds.LegL);
        bool rDead = !parts.Alive(PartIds.LegR);
        w.SetLegDisabled(true, lDead);
        w.SetLegDisabled(false, rDead);
        if (lDead && rDead)
        {
            w.SpeedFactor = CrawlSpeedFactor;
            w.LimpRoll = 0f;
            Dbg.Print($"BRK {(w == _player ? Roles.Player : Roles.Enemy)} both legs wrecked -> CRAWL (speedFactor 0.25)");
        }
        else if (lDead || rDead)
        {
            w.SpeedFactor = LimpSpeedFactor;
            w.LimpRoll = lDead ? LimpRollAngle : -LimpRollAngle;
            Dbg.Print($"BRK {(w == _player ? Roles.Player : Roles.Enemy)} leg wrecked -> LIMP (speedFactor 0.55)");
        }
        else
        {
            w.SpeedFactor = 1f;
            w.LimpRoll = 0f;
        }
    }

    private void Die(RiderWalker w)
    {
        PlaySfx(_sfxExplosion, w.RootPos + Vector3.Up * ExplosionSoundHeight, 3f, ExplosionSoundPitch);
        w.Halted = true;
        w.SetDrive(w.LegsYaw, 0f);
        if (w == _player) { _playerDead = true; Dbg.Print("WPN DEATH PLAYER"); }
        else if (_foe != null && w == _foe.Walker) { _foe.Dead = true; Dbg.Print("WPN DEATH FOE"); }
        else if (AllyOf(w) is { } da)
        {
            da.Dead = true;
            Dbg.Print($"WPN DEATH {da.Role}");
        }
        else { _enemyDead = true; Dbg.Print("WPN DEATH ENEMY"); }
        OnDeath(w);
        // The driven rider is down but the team fights on:
        // command passes to a living teammate (owner, teams
        // stage step 4 - the leader is whoever the player
        // drives). Deferred to the next frame: Die runs inside
        // damage resolution, and swapping the driven rider
        // re-enters ApplyLoadout.
        if (w == _player && WorldConfig.IsMission && _allies.Any(a => !a.Dead && a.Team == 0))
            _pendingLeaderPass = true;
        CheckMissionEnd();
        CheckBracketEnd();
    }

    // Story progression (owner, 2026-11-13; teams stage): the
    // mission is complete when every defender is down and at
    // least one team rider still stands; it fails when the
    // whole team is destroyed. A solo team reduces to the
    // pre-teams rule (the player must outlive the defenders).
    private void CheckMissionEnd()
    {
        if (!WorldConfig.IsMission || _missionDone) return;
        // The Ascent fields its defenders as guard records:
        // every one of them counts (on other contracts there
        // are none, and this reduces to the two seats).
        bool defendersDead = _enemyDead && (_foe == null || _foe.Dead)
            && !_allies.Any(a => a.Team == 1 && !a.Dead);
        if (defendersDead && TeamAlive)
        {
            _missionDone = true;
            StoryProgress.Complete(FlowMain.PlanetIndex, FlowMain.MissionIndex);
        }
        else if (!TeamAlive)
        {
            _missionDone = true;
        }
    }

}
