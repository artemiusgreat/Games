using Godot;
using System.Collections.Generic;

// RiderDuel module: firing, projectiles, damage and part consequences.
// (Aiming — the aim ray and reticle — lives in Combat/RiderDuel.Aiming.cs;
// this file is part of the Combat folder: everything that aims or shoots.)
// Split from the single-file build (round 22 cleanup); the class
// stays one partial class — see RiderDuel.cs for the field block.
public partial class RiderDuel
{

    // Muzzle hardpoints: forearm weapons sit on the elbow bones
    // (the Hand_* parts are bound there), pushed forward along
    // the torso facing so the shot clears the arm.
    private Vector3 ElbowMuzzle(RiderWalker w, bool left)
    {
        var bone = w.BoneWorldPos(left ? "elbow_L" : "elbow_R");
        return bone + RiderWalker.HeadingVec(w.TorsoYawWorld) * 2.8f;
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
            var t = w.BoneWorldPos("torso");
            return t + Vector3.Up * 2.0f + RiderWalker.HeadingVec(w.TorsoYawWorld) * 1.6f;
        }
        return ElbowMuzzle(w, slot != "arm_R");
    }

    // Torso gun (the Hector's waist AC/20): torso bone origin plus a
    // right/up/forward offset in the torso frame. Used by whichever
    // seat pilots an Hector — its shells leave from the waist gun,
    // not the fist.
    private Vector3 TorsoGunMuzzle(RiderWalker w)
    {
        var torso = w.BoneWorldPos("torso");
        var fwd = RiderWalker.HeadingVec(w.TorsoYawWorld);
        var right = new Vector3(-fwd.Z, 0f, fwd.X);
        return torso + right * 1.7f + fwd * 1.4f + Vector3.Up * 2.6f;
    }

    private Vector3 EnemyMuzzle() => MuzzleFor(_enemy, _enemyBallisticSlot ?? SlotIds.ArmR);

    private void SpawnBeam(Vector3 a, Vector3 b, Color col, float width, float life)
    {
        _beamsSpawned++;
        var beam = WeaponFx.MakeBeam(a, b, col, width);
        AddChild(beam);
        _fx.Add((beam, life, 0));
        var glow = WeaponFx.MakeBeam(a, b, new Color(col.R, col.G, col.B, 0.32f), width * 3.2f);
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
        if (fired.Count == 0) { DenyWeapon("laser"); return; }
        if (_cdLaser > 0f) return;
        _cdLaser = 0f; foreach (var g in fired) _cdLaser = Mathf.Max(_cdLaser, g.Def.Cooldown);
        FireLaserVolley(aimPoint, fired);
    }

    private void FireLaserVolley(Vector3 aimPoint, System.Collections.Generic.List<(string Slot, WeaponDef Def)> fired)
    {
        var muzzle = MuzzleForSlot(fired[0].Slot, false);
        // Per-weapon reach (owner): the shared view-ray cast runs
        // to the LONGEST fitted range; each weapon then hits
        // only what lies within its own Range.
        float maxRange = 0f;
        foreach (var g in fired) maxRange = Mathf.Max(maxRange, g.Def.Range);
        // Round 20 (owner, two repros): the hit resolves on the
        // CAMERA's ray, not on a re-cast from the muzzle. The muzzle
        // re-cast kept disagreeing with the reticle by parallax —
        // aiming at sky just over a crest, the lower muzzle line
        // clipped the hill (a hit the cursor never touched); aiming
        // just above a building, the beam ran to the muzzle line's
        // far endpoint, past the cursor. What the reticle rests on
        // is what the laser hits, first thing on that line, at any
        // range; a clean sky shot hits nothing and the beam still
        // ends at the cursor's point. (Test harnesses keep a muzzle
        // fallback on a camera miss: they aim by explicit points
        // from posed cameras.)
        HitModel.Result hit;
        bool camTruth = _camMode == CamModes.Chase && !_labOpen;
        if (camTruth)
        {
            // The view line is the camera's axis — AimDirWorld,
            // the SAME direction the chase camera looks and
            // TorsoAimPoint casts. Round 20's first cut used the
            // camera's raw forward (-Basis.Z) under the OLD
            // fixed-tilt camera and hit the dirt at the rider's
            // feet; round 27's center-crosshair camera aims where
            // the mouse points by construction, so the axis is the
            // truth in play and in tests alike.
            var camDir = AimDirWorld();
            hit = _hit.Ray(_cam.GlobalPosition, camDir, maxRange, Roles.Player, 0.9f);
            bool inTest = _test || _weaponsTest || _breakTest || _terrainTest || _photoTest;
            if (inTest && (!hit.Hit || hit.Point.DistanceTo(aimPoint) > 6f))
            {
                _laserRescues++;
                GD.Print("WPN LASER fallback-rescue (camera verdict disagreed with the harness aim point)");
                // Harness shots aim by explicit point from cameras
                // that may still be settling between phases — when
                // the camera's verdict disagrees with the point the
                // harness asked for, the muzzle cast is the truth
                // there. Live play never takes this branch.
                var fbDir = (aimPoint - muzzle).Length() > 0.5f ? (aimPoint - muzzle).Normalized() : RiderWalker.HeadingVec(_player.TorsoYawWorld);
                hit = _hit.Ray(muzzle, fbDir, maxRange, Roles.Player, 0.9f);
            }
        }
        else
        {
            var dir = (aimPoint - muzzle).Length() > 0.5f ? (aimPoint - muzzle).Normalized() : RiderWalker.HeadingVec(_player.TorsoYawWorld);
            hit = _hit.Ray(muzzle, dir, maxRange, Roles.Player, 0.9f);
        }
        // The beams end where the shot ends (owner): on a hit a
        // beam ends on the hit when ITS weapon reaches it, else
        // in mid-air at its own range along the shot line; on a
        // miss — the sky — every beam ends at the cursor's own
        // point (the round-20 rule, test-asserted). Every fitted
        // laser fires its own beam from its own hardpoint
        // (multi-fit volley); the hit itself was resolved once,
        // on the camera's line, above.
        bool anyReach = false;
        if (hit.Hit) foreach (var g in fired) if (hit.T <= g.Def.Range) { anyReach = true; break; }
        for (int i = 0; i < fired.Count; i++)
        {
            var mz = MuzzleForSlot(fired[i].Slot, false);
            bool ppc = fired[i].Def.Beam == BeamKind.Ppc;
            var beamEnd = !hit.Hit ? aimPoint
                : hit.T <= fired[i].Def.Range ? hit.Point
                : camTruth ? _cam.GlobalPosition + AimDirWorld() * fired[i].Def.Range
                : mz + (aimPoint - mz).Normalized() * fired[i].Def.Range;
            SpawnBeam(mz, beamEnd, ppc ? new Color(0.55f, 0.8f, 1f) : new Color(1f, 0.22f, 0.12f), 0.15f, 0.12f); // laser core 0.085 -> 0.15 (owner: thicker)
            if (i == 0) PlaySfx(_sfxLaser, mz, 0f, ppc ? 0.9f : 1.125f); // pitch raised ~2 semitones (owner, 2026-11-13); PPC stays proportionally deeper
            SpawnFlash(mz, new Color(1f, 0.4f, 0.2f), 0.3f, 0.07f);
        }
        if (anyReach)
        {
            SpawnFlash(hit.Point, new Color(0.7f, 0.95f, 1f), 0.55f, 0.16f);
            SpawnSparks(hit.Point, new Color(0.65f, 0.95f, 1f), 16, 6.0f); // electric breaker spray
            SpawnImpactLight(hit.Point, new Color(0.55f, 0.9f, 1f), 2.4f, 10f, 0.3f);
            SpawnBurst(hit.Point, SparkParticleMat, 26, 0.6f, 6f, 11f, new Vector3(0f, -12f, 0f), 0.1f, 0.5f, 0.95f);
            GD.Print($"WPN LASER hit={hit.Target} t={hit.T:F1} zone={hit.Zone} part={hit.Part} point={hit.Point}");
            if (hit.Target == Roles.Enemy) foreach (var g in fired) { if (hit.T <= g.Def.Range) ApplyDamage(_enemy, g.Def.Damage, hit, Roles.Player, g.Def.Weight, "laser"); }
            else if (hit.Target == Roles.Foe && _foe != null) foreach (var g in fired) { if (hit.T <= g.Def.Range) ApplyDamage(_foe.Walker, g.Def.Damage, hit, Roles.Player, g.Def.Weight, "laser"); }
            else if (hit.Target == Roles.Prop) foreach (var g in fired) { if (hit.T <= g.Def.Range) DamageProp(hit.Point, g.Def.Damage, 0.9f); } // laser attribution reaches as far as its hit thickness
        }
        else GD.Print("WPN LASER hit=none");
    }

    // Player projectiles converge at most this far (round 7). Aiming
    // over a target puts the aim ray's hit on a hillside up to 200u
    // out; solving ballistics for THAT point made rockets mortar
    // almost vertically (their up-compensation grows with the solved
    // range) while flat cannon shells still crossed the enemy on the
    // line — the owner's screenshot. Capping the solution range
    // keeps every weapon's launch angle proportional to where the
    // pointer actually is. Real duel ranges are 15-60u: unaffected.
    private const float ConvergeMax = 75f;

    private void FireAutocannon(Vector3 aimPoint)
    {
        if (_playerDead) return;
        var fired = _gunsBallistic.FindAll(g => _partsPlayer.Alive(g.Slot));
        if (fired.Count == 0) { DenyWeapon("ac"); return; }
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
        // parallax. (Rockets were the mortar case — LaunchRocket.)
        foreach (var (slot, def) in fired)
        {
            var muzzle = MuzzleForSlot(slot, true);
            var toAim = aimPoint - muzzle;
            // Converge on the aim point when the crosshair has
            // one within reach. When it rests beyond the cast
            // (_aimParallel), converge on the point where it
            // actually rests (the long cast's distance, 1500u
            // for sky) instead of the 220u far point — shells
            // then land on the crosshair at any range instead
            // of crossing the sight line at 220u and sailing
            // over far targets.
            var dir = _aimParallel
                ? (_cam.GlobalPosition + _aimDir * _farConverge - muzzle).Normalized()
                : toAim.Length() > 0.5f ? toAim.Normalized() : RiderWalker.HeadingVec(_player.TorsoYawWorld);
            // Shell speed lives on the weapon def (WeaponDef.cs:
            // MuzzleSpeed) — one place to tune it for every
            // cannon, player and AI alike. Ballistic shells fly
            // STRAIGHT (owner): no gravity, no drop — the line
            // the crosshair draws is the line the shell flies.
            // (Rockets keep their solved arcs.)
            (float spd, float grav) = (def.MuzzleSpeed, 0f);
            var shell = SpawnProjectile(muzzle, dir * spd, grav, def.Damage, 0f, Roles.Player, rocket: false, weaponWeight: def.Weight);
            shell.Life = def.Range / spd; // burns out at the weapon's range (owner)
            PlaySfx(_sfxCannon, muzzle, def.SfxPitch, def.SfxRate);
            SpawnFlash(muzzle, new Color(1f, 0.6f, 0.2f), 0.42f, 0.08f);
            GD.Print($"WPN AC fired {def.Id} dir={dir}");
        }
    }

    // LRM salvo: 8 rockets staggered 0.07s apart, alternating ear pods.
    // Each rocket is dumb ballistics: velocity solved to land on the aim
    // point (up-compensated for gravity), seeded spread, 7 damage with
    // 3.0u splash falloff on impact. A lost pod removes its 4 rockets;
    // both pods gone = the LRM is offline.
    // The salvo's staggered launch queue (0.07s between
    // rockets): FireLrmSalvo fills it, the frame step drains it.
    private readonly System.Collections.Generic.List<(float Delay, bool Left, Vector3 Aim, WeaponDef Def)> _lrmQueue = new();

    private void ProcessLrmQueue(float dt)
    {
        for (int i = _lrmQueue.Count - 1; i >= 0; i--)
        {
            var q = _lrmQueue[i];
            q.Delay -= dt;
            if (q.Delay <= 0f) { _lrmQueue.RemoveAt(i); LaunchRocket(q.Aim, q.Left, q.Def); }
            else _lrmQueue[i] = q;
        }
    }

    private void FireLrmSalvo(Vector3 aimPoint)
    {
        if (_playerDead) return;
        bool podL = _podGunsL.Count > 0 && _partsPlayer.Alive(_podGunsL[0].Slot);
        bool podR = _podGunsR.Count > 0 && _partsPlayer.Alive(_podGunsR[0].Slot);
        if (!podL && !podR) { DenyWeapon("lrm"); return; }
        if (_cdLrm > 0f) return;
        _cdLrm = 0f;
        foreach (var g in _podGunsL) _cdLrm = Mathf.Max(_cdLrm, g.Def.Cooldown);
        foreach (var g in _podGunsR) _cdLrm = Mathf.Max(_cdLrm, g.Def.Cooldown);
        FireSalvoVolley(aimPoint,
            podL ? _podGunsL : new System.Collections.Generic.List<(string Slot, WeaponDef Def)>(),
            podR ? _podGunsR : new System.Collections.Generic.List<(string Slot, WeaponDef Def)>());
    }

    private void FireSalvoVolley(Vector3 aimPoint,
        System.Collections.Generic.List<(string Slot, WeaponDef Def)> podLGuns,
        System.Collections.Generic.List<(string Slot, WeaponDef Def)> podRGuns)
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
            _lrmQueue.Add((i * 0.07f, ordered[i].Left, aimPoint, ordered[i].Def));
        PlaySfx(_sfxRocket, PodMuzzle(podL), -1f);
        GD.Print($"WPN LRM salvo queued ({ordered.Count} rockets, pods L={podL} R={podR})");
    }

    private Vector3 PodMuzzle(bool left)
    {
        // Rig-true: rockets leave from the pod part's actual center —
        // a chassis may carry a single rack, on either shoulder.
        string id = left ? SlotIds.PodL : SlotIds.PodR;
        if (_partsPlayer != null && _partsPlayer.Has(id))
            return _partsPlayer.PartCenterWorld(id) + Vector3.Up * 0.3f;
        var torso = _player.BoneWorldPos("torso");
        var fwd = RiderWalker.HeadingVec(_player.TorsoYawWorld);
        var right = new Vector3(-fwd.Z, 0f, fwd.X);
        return torso + right * (left ? -2.3f : 2.3f) + Vector3.Up * 3.6f + fwd * 0.4f;
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


    private void LaunchRocket(Vector3 aimPoint, bool left, WeaponDef def)
    {
        _rocketsLaunched++;
        var muzzle = PodMuzzle(left);
        var toAim = aimPoint - muzzle;
        // The pack reaches its own Range and no further: a
        // farther aim point solves onto the max-range point on
        // the same line (the salvo falls short, honestly).
        if (toAim.Length() > def.Range) toAim = toAim.Normalized() * def.Range;
        // The lob is solved for the TRUE aim point at any range —
        // what is capped now is the TIME OF FLIGHT, not the range.
        // History: round 7 capped the solved range at 75u because
        // solving for a 200u hillside point mortared the rockets
        // vertically (the up-compensation grows with the solved
        // tof). But the range cap silently broke every longer
        // shot: measured by --lrm-probe (2026-10-31), a 110u shot
        // from the mesa landed 16/16 rockets on terrain, on
        // average 12u short and 9u BELOW the aim point — the
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
        // solved from a VIRTUAL MUZZLE — the pod's projection onto
        // the aim axis — and the rocket, still spawned at the pod,
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
        if (axisDir.LengthSquared() > 0.5f)
        {
            float tPod = (muzzle - axisOrigin).Dot(axisDir);
            float tAim = (muzzle + toAim - axisOrigin).Dot(axisDir);
            var onAxis = axisOrigin + axisDir * tPod;
            if ((muzzle - onAxis).Length() > 1.5f && tAim > tPod + 15f)
            {
                solveFrom = onAxis;
                guided = true;
            }
        }
        var toSolve = (muzzle + toAim) - solveFrom;
        float dist = Mathf.Max(toSolve.Length(), 1f);
        var dir = ApplyInaccuracy(toSolve.Normalized(), 1.1f);
        float tof = Mathf.Min(dist / baseSpeed, tofMax);
        float speed = dist / tof;
        var vel = dir * speed + Vector3.Up * (0.5f * grav * tof);
        var proj = SpawnProjectile(muzzle, vel, grav, def.Damage, def.Splash, Roles.Player, rocket: true, weaponWeight: def.PerRocketWeight);
        if (guided)
        {
            proj.GuideActive = true;
            proj.GuideOrigin = solveFrom;
            proj.GuideStart = muzzle;
            proj.GuideVel0 = vel;
            proj.GuideTime = 0f;
        }
        SpawnFlash(muzzle, new Color(1f, 0.55f, 0.2f), 0.3f, 0.09f);
    }

    private void OnRocketStep(Projectile p)
    {
        if (_lastPuff.TryGetValue(p, out var last) && last.DistanceSquaredTo(p.GlobalPosition) < 0.3f) return;
        _lastPuff[p] = p.GlobalPosition;
        var puff = WeaponFx.MakeTrailPuff(p.GlobalPosition);
        AddChild(puff);
        _puffs.Add((puff, 0.4f));
    }

    private void ExplodeSplash(HitModel.Result hit, Projectile p)
    {
        RiderWalker direct = hit.Target == Roles.Player ? _player
            : hit.Target == Roles.Foe && _foe != null ? _foe.Walker
            : hit.Target == Roles.Enemy ? _enemy : null;
        if (direct != null) ApplyDamage(direct, p.Damage, hit, p.OwnerId, p.WeaponWeight, "rocket");
        var roster = new System.Collections.Generic.List<(RiderWalker, string)> { (_player, Roles.Player), (_enemy, Roles.Enemy) };
        if (_foe != null) roster.Add((_foe.Walker, Roles.Foe));
        foreach (var (w, id) in roster)
        {
            if (w == direct) continue;
            float d = _hit.DistanceToRiderSurface(hit.Point, id);
            if (d < p.SplashRadius)
            {
                var splashHit = hit;
                splashHit.Target = id;
                splashHit.Zone = "splash";
                ApplyDamage(w, p.Damage * (1f - d / p.SplashRadius), splashHit, p.OwnerId, p.WeaponWeight, "rocket");
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
        vis.Scale = Vector3.One * (rocket ? 1.5f : 1.6f); // owner: projectiles slightly larger
        p.AddChild(vis);
        AddChild(p);
        p.GlobalPosition = pos;
        if (vel.LengthSquared() > 0.01f) p.LookAt(pos + vel, Vector3.Up);
        p.OnHit = OnProjectileHit;
        if (rocket) p.OnStep = OnRocketStep;
        _shots.Add(p);
        return p;
    }

    private void OnProjectileHit(HitModel.Result hit, Projectile p)
    {
        GD.Print($"WPN PROJ owner={p.OwnerId} hit={hit.Target} zone={hit.Zone} part={hit.Part} t={hit.T:F1} point={hit.Point}");
        if (_lrmProbe) _lrmImpacts.Add((hit.Target, hit.Point));
        if (p.SplashRadius > 0f)
        {
            SpawnExplosionFx(hit.Point); // rockets: mini-explosion
            SpawnFlash(hit.Point, new Color(1f, 0.62f, 0.25f), 1.7f, 0.3f);
            SpawnImpactLight(hit.Point, new Color(1f, 0.55f, 0.2f), 3.2f, 14f, 0.5f);
            SpawnBurst(hit.Point, FireParticleMat, 54, 1.15f, 5f, 10f, new Vector3(0f, -7f, 0f), 0.16f, 0.8f, 1.7f);
            SpawnBurst(hit.Point, SmokeParticleMat, 22, 2.6f, 1.4f, 3f, new Vector3(0f, 1.3f, 0f), 0.55f, 1.5f, 2.6f);
            ExplodeSplash(hit, p);
            if (hit.Target == Roles.Prop) DamageProp(hit.Point, p.Damage);
            return;
        }
        SpawnFlash(hit.Point, new Color(1f, 0.72f, 0.35f), 0.62f, 0.18f);
        SpawnSparks(hit.Point, new Color(1f, 0.8f, 0.5f), 10, 5.2f);
        _fxSparkBursts--; // cannon impact sparks are not a laser burst
        SpawnBurst(hit.Point, FireParticleMat, 16, 0.7f, 4.5f, 8.5f, new Vector3(0f, -9f, 0f), 0.13f, 0.6f, 1.1f);
        SpawnBurst(hit.Point, SmokeParticleMat, 9, 1.6f, 1f, 2.2f, new Vector3(0f, 1.2f, 0f), 0.5f, 1.2f, 2f);
        SpawnImpactLight(hit.Point, new Color(1f, 0.75f, 0.4f), 2.6f, 11f, 0.35f);
        if (hit.Target == Roles.Prop) DamageProp(hit.Point, p.Damage);
        // Cannon shells scorch what they hit.
        var nrm = p.Vel.LengthSquared() > 0.01f ? -p.Vel.Normalized() : Vector3.Up;
        if (hit.Target == Roles.Player) SpawnScorch(hit.Point, nrm, _player);
        else if (hit.Target == Roles.Enemy) SpawnScorch(hit.Point, nrm, _enemy);
        else if (hit.Target == Roles.Foe && _foe != null) SpawnScorch(hit.Point, nrm, _foe.Walker);
        else if (hit.Hit) SpawnScorch(hit.Point, nrm, this);
        string kind = p.WeaponWeight >= 10f ? "cannon" : "mg";
        if (hit.Target == Roles.Player) ApplyDamage(_player, p.Damage, hit, p.OwnerId, p.WeaponWeight, kind);
        else if (hit.Target == Roles.Enemy) ApplyDamage(_enemy, p.Damage, hit, p.OwnerId, p.WeaponWeight, kind);
        else if (hit.Target == Roles.Foe && _foe != null) ApplyDamage(_foe.Walker, p.Damage, hit, p.OwnerId, p.WeaponWeight, kind);
    }

    private void ApplyDamage(RiderWalker target, float dmg, HitModel.Result hit, string source, float weaponWeight = 0f, string kind = "")
    {
        bool isPlayer = target == _player;
        bool isFoe = _foe != null && target == _foe.Walker;
        if (isPlayer ? _playerDead : isFoe ? _foe.Dead : _enemyDead) return;
        var parts = isPlayer ? _partsPlayer : isFoe ? _foe.Parts : _partsEnemy;
        // Damage routing (owner, 2026-11-03): EVERY hit burns the
        // main pool, whatever part it lands on — arms, legs, head,
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
        float poolNow = isPlayer ? _hpPlayer : isFoe ? _foe.Hp : _hpEnemy;
        float poolMax = isPlayer ? _playerSpec.MaxHp : isFoe ? _foe.Spec.MaxHp : _enemySpec.MaxHp;
        // Arm-break gate (owner, 2026-11-12; PartRules):
        // while the pool after this hit is still above the
        // gate, arm damage is capped so the arm keeps 1 HP —
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
            else _hpEnemy = Mathf.Max(_hpEnemy - poolDmg, 0f);
        }
        float hp = isPlayer ? _hpPlayer : isFoe ? _foe.Hp : _hpEnemy;
        GD.Print($"WPN DMG target={(isPlayer ? Roles.Player : isFoe ? Roles.Foe : Roles.Enemy)} dmg={dmg:F1} zone={hit.Zone} part={partId} poolDmg={poolDmg:F1} hp={hp:F1} src={source}");
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
        SpawnFlash(center, new Color(1f, 0.62f, 0.22f), 0.95f, 0.16f);
        SpawnFlash(center, new Color(1f, 0.88f, 0.55f), 0.45f, 0.1f);
        var part = parts.GetPart(partId);
        if (parts == _partsPlayer) AddFeed($"WARNING: {part.Display} DESTROYED");
        else if (_foe != null && parts == _foe.Parts) AddFeed($"{_foe.Spec.Name}: {part.Display} DESTROYED");
        else AddFeed($"{_enemySpec.Name}: {part.Display} DESTROYED");
        ApplyPartConsequences(parts, partId);
    }

    private void AddFeed(string text)
    {
        _feed.Add((text, 6f));
        while (_feed.Count > 5) _feed.RemoveAt(0);
        GD.Print($"BRK FEED {text}");
    }

    // Functional fallout of losing a part. Weapon mounts are gated live at
    // the fire/gunnery checks — every mount answers to its own part
    // (arm_L -> lasers, the waist gun -> the Hector's AC/20, pods -> salvo
    // size, Hector head -> spread + tracking lag), so
    // the stateful consequences latched here are the legs and the player's
    // head (HUD disturbance timer; the sway itself is applied in the HUD
    // step from part state).
    private void ApplyPartConsequences(RiderParts parts, string partId)
    {
        if (partId == "leg_L" || partId == "leg_R")
            SyncLegConsequences(WalkerOf(parts), parts);
        else if (partId == "head" && parts == _partsPlayer)
            _hudFlickerT = 1.2f;
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
            if (g.Path == 0) las.Add(t);
            else if (g.Path == 1) can.Add(t);
            else if (g.Path == 2) podL.Add(t);
            else podR.Add(t);
        }
        if (!anyAlive)
        {
            grp.DenyT = 0.5f;
            GD.Print($"WPN DENIED group {grp.Num} (mounting parts destroyed)");
            return;
        }
        if (firedGuns.Count == 0) return; // alive but all cooling
        if (las.Count > 0) FireLaserVolley(aimPoint, las);
        if (can.Count > 0) FireCannonVolley(aimPoint, can);
        if (podL.Count > 0 || podR.Count > 0) FireSalvoVolley(aimPoint, podL, podR);
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
    // kills the rider — it stays attached, darkened and deformed, and
    // the rider LIMPS: one wrecked leg = 55% speed with a lean and the
    // wrecked leg keeps stepping at the throttled pace; both legs
    // wrecked = a 25% crawl.
    // Death comes only from the HP pool reaching zero.
    private void SyncLegConsequences(RiderWalker w, RiderParts parts)
    {
        bool lDead = !parts.Alive("leg_L");
        bool rDead = !parts.Alive("leg_R");
        w.SetLegDisabled(true, lDead);
        w.SetLegDisabled(false, rDead);
        if (lDead && rDead)
        {
            w.SpeedFactor = 0.25f;
            w.LimpRoll = 0f;
            GD.Print($"BRK {(w == _player ? Roles.Player : Roles.Enemy)} both legs wrecked -> CRAWL (speedFactor 0.25)");
        }
        else if (lDead || rDead)
        {
            w.SpeedFactor = 0.55f;
            w.LimpRoll = lDead ? 0.10f : -0.10f;
            GD.Print($"BRK {(w == _player ? Roles.Player : Roles.Enemy)} leg wrecked -> LIMP (speedFactor 0.55)");
        }
        else
        {
            w.SpeedFactor = 1f;
            w.LimpRoll = 0f;
        }
    }

    private void Die(RiderWalker w)
    {
        PlaySfx(_sfxExplosion, w.RootPos + Vector3.Up * 4f, 3f, 0.8f);
        w.Halted = true;
        w.SetDrive(w.LegsYaw, 0f);
        if (w == _player) { _playerDead = true; GD.Print("WPN DEATH PLAYER"); }
        else if (_foe != null && w == _foe.Walker) { _foe.Dead = true; GD.Print("WPN DEATH FOE"); }
        else { _enemyDead = true; GD.Print("WPN DEATH ENEMY"); }
        OnDeath(w);
        // Story progression (owner, 2026-11-13): the mission
        // is complete when the player outlives every defender.
        if (WorldConfig.IsMission && !_playerDead && _enemyDead && (_foe == null || _foe.Dead))
        {
            int before = StoryProgress.UnlockedCount;
            StoryProgress.Complete(FlowMain.PlanetIndex, FlowMain.MissionIndex);
            AddFeed(StoryProgress.UnlockedCount > before
                ? "MISSION COMPLETE — NEW MISSION AVAILABLE"
                : "MISSION COMPLETE");
        }
    }

}
