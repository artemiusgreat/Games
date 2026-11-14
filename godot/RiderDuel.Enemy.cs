using Godot;
using System.Collections.Generic;

// RiderDuel module: the opponent brains: movement AI, torso tracking,
// gunnery. Split from the single-file build (round 22 cleanup); the
// class stays one partial class — see RiderDuel.cs for the field
// block.
//
// Free-for-all (owner, 2026-10-31): two AI opponents share this
// brain. Each picks the nearest living opponent — the player or
// the other AI — and plays the same range game against it. No
// teams, no collaboration: a foe will happily finish off the
// other foe while the player watches.
public partial class RiderDuel
{
    // Mission guard duty (owner, 2026-11-06): on the big map the
    // defenders PATROL their own base and only engage when the
    // player closes inside AggroRange; past LeashRange they break
    // off and go back to patrolling. No new AI framework — a
    // two-state latch (patrol/engaged) around the existing range
    // game, which still runs the fight once engaged. Tournament
    // keeps the free-for-all hunt untouched.
    private const float AggroRange = 260f;
    private const float LeashRange = 360f;
    private const float PatrolRadius = 55f; // the base plateau's flat disc is 60
    private bool _foe0Engaged;
    private Vector3 _foe0Patrol;
    private float _foe0PatrolT;

    private static Vector3 PadWorld(int i)
    {
        var p = Main.SpawnPads[i];
        return new Vector3(p.X, Main.HeightAt(p.X, p.Y), p.Y);
    }

    // The engagement latch, with hysteresis: trips in at
    // AggroRange, out at LeashRange, so a defender on the boundary
    // does not flicker between states. The reference is the
    // nearest living rider of the player's TEAM (teams stage) —
    // with a solo team that is the player, the original rule.
    private bool GuardGate(RiderWalker self, ref bool engaged)
    {
        var near = NearestTeamWalker(self.RootPos);
        if (near == null) { engaged = false; return false; }
        float dp = self.RootPos.DistanceTo(near.RootPos);
        if (!engaged && dp < AggroRange) engaged = true;
        else if (engaged && dp > LeashRange) engaged = false;
        return engaged;
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
        if (timer <= 0f || flat.DistanceTo(tgt) < 8f)
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
        yaw += Mathf.Clamp(dy, -0.7f * delta, 0.7f * delta);
        w.SetDrive(yaw, dist > 8f ? pace : 0f);
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
        thinkT = 0.75f;
        RiderWalker best = null;
        float bd = float.MaxValue;
        foreach (var cand in new[] { _player, _enemy, _foe?.Walker })
        {
            if (cand == null || cand == self || IsDead(cand)) continue;
            float d = self.RootPos.DistanceSquaredTo(cand.RootPos);
            if (d < bd) { bd = d; best = cand; }
        }
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
            if (!GuardGate(_enemy, ref _foe0Engaged))
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
            if (!GuardGate(_foe.Walker, ref _foe.Engaged))
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
    // back off inside 26u, orbit in the band; the torso tracks the
    // target (snapping while the head lives, lagging at 0.6 rad/s
    // once it is gone). A downed foe has a leg gone, so it limps —
    // the walker's SpeedFactor throttles its drive — and keeps
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
        if (dist > 38f) { desiredYaw = yawTo; spd = pace; }
        else if (dist < 26f) { desiredYaw = yawTo; spd = -pace * 0.5f; }
        else
        {
            orbitTimer += delta;
            if (orbitTimer > 6f) { orbitTimer = 0f; orbitDir = -orbitDir; }
            desiredYaw = yawTo + orbitDir * Mathf.Pi / 2f;
            spd = pace * 0.7f;
        }
        float dy = RiderWalker.WrapAngle(desiredYaw - yaw);
        float maxTurn = 0.7f * delta;
        yaw += Mathf.Clamp(dy, -maxTurn, maxTurn);
        w.SetDrive(yaw, spd);
        TrackTorso(w, parts, yaw, yawTo, delta);
        w.TorsoPitch = 0f;
    }

    // Torso tracking: snaps to the target bearing while the head
    // (sensor suite) lives; head destroyed, the twist closes at
    // 0.6 rad/s — a visible, measurable lag.
    private void TrackTorso(RiderWalker w, RiderParts parts, float yaw, float yawTo, float dt)
    {
        float desiredTwist = RiderWalker.WrapAngle(yawTo - yaw);
        if (parts.Alive("head"))
        {
            w.TorsoTwist = desiredTwist;
        }
        else
        {
            float cur = w.TorsoTwist;
            float step = Mathf.Clamp(RiderWalker.WrapAngle(desiredTwist - cur),
                -0.6f * dt, 0.6f * dt);
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
        // fighting — the nearest team rider (the guard gate's
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
                _cdEnemy = _defEnemyBallistic?.Cooldown ?? 2.4f;
                _enemyShots++;
            });
    }

    private void UpdateFoeGunnery(float dt)
    {
        if (_foe.Dead || _foe.Cd > 0f) return;
        if (WorldConfig.IsMission && !_foe.Engaged) return;
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
    private void UpdateFoeGunnery(RiderWalker w, RiderParts parts, WeaponDef def, string slot,
        string tag, string targetTag, RiderWalker target, Vector3 targetVel,
        System.Action onFired)
    {
        if (parts == null || !parts.Alive(slot ?? "arm_R")) return; // the mount is gone with its part
        var muzzle = MuzzleFor(w, slot ?? "arm_R");
        var tp = target.RootPos + Vector3.Up * 7.6f;
        float dist = muzzle.DistanceTo(tp);
        if (dist > 75f) return;
        var los = _hit.Ray(muzzle, (tp - muzzle).Normalized(), dist, tag);
        if (los.Hit && los.Target != targetTag) return; // blocked: building/terrain
        float speed = def?.MuzzleSpeed ?? 420f;
        float tof = dist / speed;
        var aim = tp + targetVel * tof;
        var dir = ApplyInaccuracy((aim - muzzle).Normalized(),
            parts.Alive("head") ? 1.2f : 3.6f);
        var shell = SpawnProjectile(muzzle, dir * speed, 0f, def?.Damage ?? 18f, 0f, tag, rocket: false, weaponWeight: def?.Weight ?? 16f); // shells fly straight (owner)
        if (def != null && speed > 0f) shell.Life = def.Range / speed; // and burn out at the weapon's range
        PlaySfx(_sfxCannon, muzzle, 0f, 0.78f);
        SpawnFlash(muzzle, new Color(1f, 0.6f, 0.2f), 0.5f, 0.08f);
        onFired?.Invoke();
        GD.Print($"WPN FOEFIRE {tag} dist={dist:F1} tof={tof:F2} targetVel={targetVel.Length():F1}");
    }
}
