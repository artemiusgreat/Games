using Godot;
using System.Collections.Generic;

// Titans module: the opponent brains: movement AI, torso tracking,
// gunnery. Split from the single-file build (round 22 cleanup); the
// class stays one partial class - see Titans.cs for the field
// block.
//
// Free-for-all (owner, 2026-10-31): two AI opponents share this
// brain. Each picks the nearest living opponent - the player or
// the other AI - and plays the same range game against it. No
// teams, no collaboration: a foe will happily finish off the
// other foe while the player watches.
public partial class Titans
{
    // Mission guard duty (owner, 2026-11-06; detection rule
    // reworked 2026-11-25): on the big map the defenders
    // PATROL their own base and engage only on literal sight -
    // the nearest team rider within DetectRange with an
    // unobstructed chest-to-chest line - or when the player
    // has HIT them, which alerts at any distance and never
    // lapses. Past LeashRange an unalerted defender breaks
    // off and goes back to patrolling. No new AI framework -
    // a two-state latch (patrol/engaged) around the existing
    // range game, which still runs the fight once engaged.
    // Gunnery was already ray-gated, so an engaged defender
    // still cannot shoot through a hill; it has to crest it.
    private const float DetectRange = 500f;
    private const float LeashRange = 560f;
    private const float PatrolRadius = 55f; // the base plateau's flat disc is 60
    // Shared NPC tuning (the foe seats, the guards and the
    // teammates all steer and shoot by these - one value, one
    // meaning, named once for the whole partial class).
    private const float NpcTurnRate = 0.7f; // rad/s, every NPC steering clamp
    private const float NpcFallbackCooldown = 2.4f; // cannon cooldown when a record has no def (defensive default)
    private const float NpcGunneryRange = 75f; // the shared cannon gunnery's range cap
    private const float AdvanceRange = 38f; // the range game: advance beyond this
    private const float RetreatRange = 26f; // walk out inside this; orbit in the band between
    private const float HeadlessTorsoRate = 0.6f; // rad/s torso lag once the head (sensors) is gone
    private const float InaccuracySensedDeg = 1.2f; // gunnery cone with sensors alive
    private const float InaccuracyHeadlessDeg = 3.6f; // gunnery cone once the head is gone
    private const float PatrolArriveDist = 8f; // a patrol point counts as reached inside this
    private const float LosChestHeight = 8f; // chest height for the sight line's endpoints
    private const float ThinkSeconds = 0.75f; // target re-pick cadence
    private bool _foe0Engaged;
    private bool _foe0Alerted;
    private Vector3 _foe0Patrol;
    private float _foe0PatrolT;

    private static Vector3 PadWorld(int i)
    {
        var p = Main.SpawnPads[i];
        return new Vector3(p.X, Main.HeightAt(p.X, p.Y), p.Y);
    }

    // The engagement latch, with hysteresis: trips in on
    // sight inside DetectRange, out at LeashRange, so a
    // defender on the boundary does not flicker between
    // states. The reference is the nearest living rider of
    // the player's TEAM (teams stage) - with a solo team
    // that is the player, the original rule.
    private bool GuardGate(RiderWalker self, ref bool engaged, bool alerted)
    {
        var near = NearestTeamWalker(self.RootPos);
        if (near == null) { engaged = false; return false; }
        if (alerted) { engaged = true; return true; }
        float dp = self.RootPos.DistanceTo(near.RootPos);
        if (!engaged && dp < DetectRange && HasLineOfSight(self, near)) engaged = true;
        else if (engaged && dp > LeashRange) engaged = false;
        return engaged;
    }

    // Literal sight between two riders: the straight line
    // from chest to chest, sampled against the terrain
    // every few units - a crest between them blocks the
    // view even at close range (owner's screenshot, 2026-
    // 11-25: hidden behind the hilltop, already hunted).
    private bool HasLineOfSight(RiderWalker a, RiderWalker b)
    {
        var pa = a.RootPos + Vector3.Up * LosChestHeight;
        var pb = b.RootPos + Vector3.Up * LosChestHeight;
        float dist = pa.DistanceTo(pb);
        int steps = Mathf.Max(2, (int)(dist / 6f));
        for (int i = 1; i < steps; i++)
        {
            var p = pa.Lerp(pb, (float)i / steps);
            if (Main.HeightAt(p.X, p.Z) > p.Y - 0.75f) return false;
        }
        return true;
    }

    // An NPC fires exactly ONE ballistic weapon - the one picked
    // at spawn. When that weapon's mount is shot away (or the
    // slot never had a weapon), the record used to go silent
    // forever: the shared gunnery returns on the dead mount and
    // nothing ever re-picked, so a disarmed Hector stood inert
    // at point blank (owner, 2026-11-22). Re-pick any other
    // fitted cannon on a living part; only a rider with no live
    // cannon mount at all is truly disarmed.
    private bool PickBallistic(RiderSpec spec, LoadoutState load, RiderParts parts, out PartId? slot, out WeaponDef def)
    {
        slot = null; def = null;
        for (int n = 0; ; n++)
        {
            var s = SlotOf(spec, WeaponSlotType.Ballistic, n);
            if (s == null) break;
            var id = load.WeaponIn(s.Value);
            var w = id == null ? null : WeaponCatalog.ById(id.Value);
            if (w != null && parts.Alive(s.Value)) { slot = s; def = w; return true; }
        }
        return false;
    }

    private bool EnsureBallistic(Ally a)
    {
        if (PickBallistic(a.Spec, a.Loadout, a.Parts, out var s, out var w)) { a.BallisticSlot = s; a.DefBallistic = w; return true; }
        a.DefBallistic = null;
        return false;
    }

    private bool EnsureBallistic(Foe f)
    {
        if (PickBallistic(f.Spec, f.Loadout, f.Parts, out var s, out var w)) { f.BallisticSlot = s; f.DefBallistic = w; return true; }
        f.DefBallistic = null;
        return false;
    }

    // Walk a slow circuit of the home base: pick a point on the
    // plateau disc, stroll to it, pick another. The torso faces
    // the walk direction; weapons stay silent (gunnery is gated
    // on the same latch).
    private void PatrolMove(RiderWalker w, RiderParts parts, Vector3 home,
        ref float yaw, ref Vector3 point, ref float timer, float pace, float delta)
    {
        timer -= delta;
        var flat = new Vector2(w.RootPos.X, w.RootPos.Z);
        var tgt = new Vector2(point.X, point.Z);
        if (timer <= 0f || flat.DistanceTo(tgt) < PatrolArriveDist)
        {
            float a = (float)_rng.NextDouble() * Mathf.Tau;
            float r = PatrolRadius * Mathf.Sqrt((float)_rng.NextDouble());
            point = home + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            point.Y = Main.HeightAt(point.X, point.Z);
            timer = 16f;
            tgt = new Vector2(point.X, point.Z);
        }
        var to = new Vector3(tgt.X - flat.X, 0f, tgt.Y - flat.Y);
        float dist = to.Length();
        if (dist < 0.6f) { w.SetDrive(yaw, 0f); return; }
        float yawTo = RiderWalker.YawOf(to.Normalized());
        float dy = RiderWalker.WrapAngle(yawTo - yaw);
        yaw += Mathf.Clamp(dy, -NpcTurnRate * delta, NpcTurnRate * delta);
        float spd = dist > PatrolArriveDist ? pace : 0f;
        // Hold the universal separation (owner, 2026-11-15).
        if (spd > 0f && RiderAhead(w, yaw)) spd = 0f;
        w.SetDrive(yaw, spd);
        TrackTorso(w, parts, yaw, yawTo, delta);
        w.TorsoPitch = 0f;
    }

    // The foe's current victim: nearest living opponent, re-picked
    // on a think timer so equidistant riders don't flip-flop.
    private (RiderWalker W, Vector3 Vel, string Tag) PickTarget(
        RiderWalker self, RiderWalker cur, ref float thinkT, float dt)
    {
        thinkT -= dt;
        if (cur != null && thinkT > 0f && !IsDead(cur)) return (cur, VelOf(cur), TagOf(cur));
        thinkT = ThinkSeconds;
        // Nearest living rider of ANOTHER team (Stage D3):
        // the candidate pool is every rider on the field -
        // in the classic three-seat arena all teams differ,
        // so this reduces to the old free-for-all exactly.
        int myTeam = TeamOf(self);
        RiderWalker best = null;
        float bd = float.MaxValue;
        void Scan(RiderWalker cand)
        {
            if (cand == null || cand == self || IsDead(cand)) return;
            if (TeamOf(cand) == myTeam) return;
            float d = self.RootPos.DistanceSquaredTo(cand.RootPos);
            if (d < bd) { bd = d; best = cand; }
        }
        Scan(_player);
        Scan(_enemy);
        if (_foe != null) Scan(_foe.Walker);
        foreach (var a in _allies) Scan(a.Walker);
        return best == null ? (null, Vector3.Zero, "") : (best, VelOf(best), TagOf(best));
    }

    private bool IsDead(RiderWalker w) =>
        w == _player ? _playerDead
        : _foe != null && w == _foe.Walker ? _foe.Dead
        : AllyOf(w) is { } da ? da.Dead
        : _enemyDead;

    private Vector3 VelOf(RiderWalker w) =>
        w == _player ? _playerVel
        : _foe != null && w == _foe.Walker ? _foe.Vel
        : AllyOf(w) is { } va ? va.Vel
        : _enemyVel;

    private string TagOf(RiderWalker w) =>
        w == _player ? Roles.Player
        : _foe != null && w == _foe.Walker ? Roles.Foe
        : AllyOf(w) is { } ta ? ta.Role
        : Roles.Enemy;

    private RiderWalker WalkerOf(RiderParts parts) =>
        parts == _partsPlayer ? _player
        : _foe != null && parts == _foe.Parts ? _foe.Walker
        : AllyOfParts(parts) is { } wa ? wa.Walker
        : _enemy;

    private RiderParts PartsOf(RiderWalker w) =>
        w == _player ? _partsPlayer
        : _foe != null && w == _foe.Walker ? _foe.Parts
        : AllyOf(w) is { } pa ? pa.Parts
        : _partsEnemy;

    private void UpdateEnemyAI(float delta)
    {
        if (WorldConfig.IsMission)
        {
            if (!GuardGate(_enemy, ref _foe0Engaged, _foe0Alerted))
            {
                _foe0TargetW = null;
                PatrolMove(_enemy, _partsEnemy, PadWorld(1), ref _enemyYaw,
                    ref _foe0Patrol, ref _foe0PatrolT, Mathf.Min(_enemySpec.MaxSpeed, EnemyMaxSpeed), delta);
                return;
            }
            _foe0TargetW = NearestTeamWalker(_enemy.RootPos) ?? _player;
            UpdateFoeMovement(_enemy, _partsEnemy,
                PaceFor(_enemySpec, _enemy.RootPos.DistanceTo(_foe0TargetW.RootPos)),
                ref _enemyYaw, ref _orbitDir, ref _orbitTimer, _foe0TargetW, delta);
            return;
        }
        var (target, _, _) = PickTarget(_enemy, _foe0TargetW, ref _foe0ThinkT, delta);
        _foe0TargetW = target;
        float pace0 = target != null ? PaceFor(_enemySpec, _enemy.RootPos.DistanceTo(target.RootPos)) : EnemyPace;
        UpdateFoeMovement(_enemy, _partsEnemy, pace0,
            ref _enemyYaw, ref _orbitDir, ref _orbitTimer, target, delta);
    }

    private void UpdateFoeAI(float delta)
    {
        if (_foe.Dead) { _foe.Walker.SetDrive(_foe.Yaw, 0f); return; }
        if (WorldConfig.IsMission)
        {
            if (!GuardGate(_foe.Walker, ref _foe.Engaged, _foe.Alerted))
            {
                _foeTargetW = null;
                PatrolMove(_foe.Walker, _foe.Parts, PadWorld(2), ref _foe.Yaw,
                    ref _foe.Patrol, ref _foe.PatrolT, Mathf.Min(_foe.Spec.MaxSpeed, EnemyMaxSpeed), delta);
                return;
            }
            _foeTargetW = NearestTeamWalker(_foe.Walker.RootPos) ?? _player;
            UpdateFoeMovement(_foe.Walker, _foe.Parts,
                PaceFor(_foe.Spec, _foe.Walker.RootPos.DistanceTo(_foeTargetW.RootPos)),
                ref _foe.Yaw, ref _foe.OrbitDir, ref _foe.OrbitTimer, _foeTargetW, delta);
            return;
        }
        var (target, _, _) = PickTarget(_foe.Walker, _foeTargetW, ref _foe.ThinkT, delta);
        _foeTargetW = target;
        float pace = target != null ? PaceFor(_foe.Spec, _foe.Walker.RootPos.DistanceTo(target.RootPos))
            : Mathf.Min(_foe.Spec.MaxSpeed, EnemyMaxSpeed);
        UpdateFoeMovement(_foe.Walker, _foe.Parts, pace,
            ref _foe.Yaw, ref _foe.OrbitDir, ref _foe.OrbitTimer, target, delta);
    }

    private RiderWalker _foeTargetW;

    // The range game, shared by both foes: advance beyond 38u,
    // walk out inside 26u (turned away, torso still tracking),
    // orbit in the band; the torso tracks the
    // target (snapping while the head lives, lagging at 0.6 rad/s
    // once it is gone). A downed foe has a leg gone, so it limps -
    // the walker's SpeedFactor throttles its drive - and keeps
    // playing.
    private void UpdateFoeMovement(RiderWalker w, RiderParts parts, float pace,
        ref float yaw, ref float orbitDir, ref float orbitTimer,
        RiderWalker target, float delta)
    {
        if (target == null) { w.SetDrive(yaw, 0f); return; }
        var toT = target.RootPos - w.RootPos;
        float dist = new Vector2(toT.X, toT.Z).Length();
        float yawTo = RiderWalker.YawOf(toT.Normalized());
        float desiredYaw, spd;
        if (dist > AdvanceRange) { desiredYaw = yawTo; spd = pace; }
        // Backing off used to be a NEGATIVE throttle on the
        // target bearing - the legs stepped backward while the
        // torso faced the target, which reads as walking
        // backward (owner, 2026-11-23). Now the legs turn away
        // and WALK out of the bubble at the same half pace; the
        // torso keeps tracking the target on its own twist.
        else if (dist < RetreatRange) { desiredYaw = yawTo + Mathf.Pi; spd = pace * 0.5f; }
        else
        {
            orbitTimer += delta;
            if (orbitTimer > 6f) { orbitTimer = 0f; orbitDir = -orbitDir; }
            desiredYaw = yawTo + orbitDir * Mathf.Pi / 2f;
            spd = pace * 0.7f;
        }
        float dy = RiderWalker.WrapAngle(desiredYaw - yaw);
        float maxTurn = NpcTurnRate * delta;
        yaw += Mathf.Clamp(dy, -maxTurn, maxTurn);
        w.SetDrive(yaw, spd);
        TrackTorso(w, parts, yaw, yawTo, delta);
        w.TorsoPitch = 0f;
    }

    // Torso tracking: snaps to the target bearing while the head
    // (sensor suite) lives; head destroyed, the twist closes at
    // 0.6 rad/s - a visible, measurable lag.
    private void TrackTorso(RiderWalker w, RiderParts parts, float yaw, float yawTo, float dt)
    {
        float desiredTwist = RiderWalker.WrapAngle(yawTo - yaw);
        if (parts.Alive(PartId.Head))
        {
            w.TorsoTwist = desiredTwist;
        }
        else
        {
            float cur = w.TorsoTwist;
            float step = Mathf.Clamp(RiderWalker.WrapAngle(desiredTwist - cur),
                -HeadlessTorsoRate * dt, HeadlessTorsoRate * dt);
            w.TorsoTwist = cur + step;
        }
    }

    // Legacy entry for the break suite, which drives foe 0's
    // tracking directly to measure the head-loss lag.
    private void TrackEnemyTorso(float dt, float yawTo) =>
        TrackTorso(_enemy, _partsEnemy, _enemyYaw, yawTo, dt);

    private void UpdateEnemyGunnery(float dt)
    {
        if (_enemyDead) return;
        if (WorldConfig.IsMission && !_foe0Engaged) return;
        if (_cdEnemy > 0f) return;
        // Missions: gunnery shoots what the movement brain is
        // fighting - the nearest team rider (the guard gate's
        // pick). The Arena keeps the free-for-all re-pick.
        RiderWalker target; Vector3 vel; string tag;
        if (WorldConfig.IsMission)
        {
            target = _foe0TargetW;
            if (target == null || IsDead(target)) return;
            vel = VelOf(target); tag = TagOf(target);
        }
        else
        {
            var t = PickTarget(_enemy, _foe0TargetW, ref _foe0ThinkT, 0f);
            if (t.W == null) return;
            (target, vel, tag) = t;
        }
        UpdateFoeGunnery(_enemy, _partsEnemy, _defEnemyBallistic, _enemyBallisticSlot,
            Roles.Enemy, tag, target, vel, () =>
            {
                _cdEnemy = _defEnemyBallistic?.Cooldown ?? NpcFallbackCooldown;
                _enemyShots++;
            });
    }

    private void UpdateFoeGunnery(float dt)
    {
        if (_foe.Dead || _foe.Cd > 0f) return;
        if (WorldConfig.IsMission && !_foe.Engaged) return;
        if (!EnsureBallistic(_foe)) return;
        RiderWalker target; Vector3 vel; string tag;
        if (WorldConfig.IsMission)
        {
            target = _foeTargetW;
            if (target == null || IsDead(target)) return;
            vel = VelOf(target); tag = TagOf(target);
        }
        else
        {
            var t = PickTarget(_foe.Walker, _foeTargetW, ref _foe.ThinkT, 0f);
            if (t.W == null) return;
            (target, vel, tag) = t;
        }
        UpdateFoeGunnery(_foe.Walker, _foe.Parts, _foe.DefBallistic, _foe.BallisticSlot,
            Roles.Foe, tag, target, vel, () => { _foe.Cd = _foe.DefBallistic?.Cooldown ?? 0.85f; _foeShots++; });
    }

    // Cannon gunnery, shared: range + line-of-sight gated, leads
    // the target's velocity at time-of-flight, seeded inaccuracy
    // cone (1.2° with sensors, 3.6° once the head is gone). Shell
    // speed follows the weapon def (MuzzleSpeed), as the
    // player's does.
    private void UpdateFoeGunnery(RiderWalker w, RiderParts parts, WeaponDef def, PartId? slot,
        string tag, string targetTag, RiderWalker target, Vector3 targetVel,
        System.Action onFired)
    {
        if (parts == null || !parts.Alive(slot ?? PartId.ArmR)) return; // the mount is gone with its part
        var muzzle = MuzzleFor(w, slot ?? PartId.ArmR);
        var tp = target.RootPos + Vector3.Up * 7.6f;
        float dist = muzzle.DistanceTo(tp);
        if (dist > NpcGunneryRange) return;
        var los = _hit.Ray(muzzle, (tp - muzzle).Normalized(), dist, tag);
        if (los.Hit && los.Target != targetTag) return; // blocked: building/terrain
        float speed = def?.MuzzleSpeed ?? 420f;
        float tof = dist / speed;
        var aim = tp + targetVel * tof;
        var dir = ApplyInaccuracy((aim - muzzle).Normalized(),
            parts.Alive(PartId.Head) ? InaccuracySensedDeg : InaccuracyHeadlessDeg);
        var shell = SpawnProjectile(muzzle, dir * speed, 0f, def?.Damage ?? 18f, 0f, tag, rocket: false, weaponWeight: def?.Weight ?? 16f); // shells fly straight (owner)
        if (def != null && speed > 0f) shell.Life = def.Range / speed; // and burn out at the weapon's range
        PlaySfx(_sfxCannon, muzzle, 0f, 0.78f);
        SpawnFlash(muzzle, CannonMuzzleColor, 0.5f, 0.08f); // the player's cannon flash colour - same shot, same look
        onFired?.Invoke();
        Dbg.Print($"WPN FOEFIRE {tag} dist={dist:F1} tof={tof:F2} targetVel={targetVel.Length():F1}");
    }
}
