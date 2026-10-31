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
        foreach (var cand in new[] { _player, _enemy, _foe1?.Walker })
        {
            if (cand == null || cand == self || IsDead(cand)) continue;
            float d = self.RootPos.DistanceSquaredTo(cand.RootPos);
            if (d < bd) { bd = d; best = cand; }
        }
        return best == null ? (null, Vector3.Zero, "") : (best, VelOf(best), TagOf(best));
    }

    private bool IsDead(RiderWalker w) =>
        w == _player ? _playerDead
        : _foe1 != null && w == _foe1.Walker ? _foe1.Dead
        : _enemyDead;

    private Vector3 VelOf(RiderWalker w) =>
        w == _player ? _playerVel
        : _foe1 != null && w == _foe1.Walker ? _foe1.Vel
        : _enemyVel;

    private string TagOf(RiderWalker w) =>
        w == _player ? "PLAYER"
        : _foe1 != null && w == _foe1.Walker ? "FOE1"
        : "ENEMY";

    private RiderWalker WalkerOf(RiderParts parts) =>
        parts == _partsPlayer ? _player
        : _foe1 != null && parts == _foe1.Parts ? _foe1.Walker
        : _enemy;

    private RiderParts PartsOf(RiderWalker w) =>
        w == _player ? _partsPlayer
        : _foe1 != null && w == _foe1.Walker ? _foe1.Parts
        : _partsEnemy;

    private void UpdateEnemyAI(float delta)
    {
        var (target, _, _) = PickTarget(_enemy, _foe0TargetW, ref _foe0ThinkT, delta);
        _foe0TargetW = target;
        UpdateFoeMovement(_enemy, _partsEnemy, EnemyPace,
            ref _enemyYaw, ref _orbitDir, ref _orbitTimer, target, delta);
    }

    private void UpdateFoe1AI(float delta)
    {
        if (_foe1.Dead) { _foe1.Walker.SetDrive(_foe1.Yaw, 0f); return; }
        var (target, _, _) = PickTarget(_foe1.Walker, _foe1TargetW, ref _foe1.ThinkT, delta);
        _foe1TargetW = target;
        float pace = Mathf.Min(_foe1.Spec.MaxSpeed, EnemyMaxSpeed);
        UpdateFoeMovement(_foe1.Walker, _foe1.Parts, pace,
            ref _foe1.Yaw, ref _foe1.OrbitDir, ref _foe1.OrbitTimer, target, delta);
    }

    private RiderWalker _foe1TargetW;

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
        if (_cdEnemy > 0f) return;
        var (target, vel, tag) = PickTarget(_enemy, _foe0TargetW, ref _foe0ThinkT, 0f);
        if (target == null) return;
        UpdateFoeGunnery(_enemy, _partsEnemy, _defEnemyBallistic, _enemyBallisticSlot,
            "ENEMY", tag, target, vel, () =>
            {
                _cdEnemy = _defEnemyBallistic?.Cooldown ?? 2.4f;
                _enemyShots++;
            });
    }

    private void UpdateFoe1Gunnery(float dt)
    {
        if (_foe1.Dead || _foe1.Cd > 0f) return;
        var (target, vel, tag) = PickTarget(_foe1.Walker, _foe1TargetW, ref _foe1.ThinkT, 0f);
        if (target == null) return;
        UpdateFoeGunnery(_foe1.Walker, _foe1.Parts, _foe1.DefBallistic, _foe1.BallisticSlot,
            "FOE1", tag, target, vel, () => { _foe1.Cd = _foe1.DefBallistic?.Cooldown ?? 0.85f; _foe1Shots++; });
    }

    // Cannon gunnery, shared: range + line-of-sight gated, leads
    // the target's velocity at time-of-flight, seeded inaccuracy
    // cone (1.2° with sensors, 3.6° once the head is gone). Shell
    // speed follows the weapon, as the player's does — the Hector's
    // AC/20 flies at 150 (50 x3, owner 2026-10-29).
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
        float speed = def?.Id == "ac20" ? 150f
            : def?.Id == "machine_gun" ? 85f
            : def?.Id == "heavy_cannon" ? 138f : 420f;
        float tof = dist / speed;
        var aim = tp + targetVel * tof;
        var dir = ApplyInaccuracy((aim - muzzle).Normalized(),
            parts.Alive("head") ? 1.2f : 3.6f);
        SpawnProjectile(muzzle, dir * speed, 5f, def?.Damage ?? 18f, 0f, tag, rocket: false, weaponWeight: def?.Weight ?? 16f);
        PlaySfx(_sfxCannon, muzzle, 0f, 0.78f);
        SpawnFlash(muzzle, new Color(1f, 0.6f, 0.2f), 0.5f, 0.08f);
        onFired?.Invoke();
        GD.Print($"WPN FOEFIRE {tag} dist={dist:F1} tof={tof:F2} targetVel={targetVel.Length():F1}");
    }

    private Vector3 ApplyInaccuracy(Vector3 dir, float maxDeg)
    {
        float theta = Mathf.DegToRad(maxDeg) * Mathf.Sqrt((float)_rng.NextDouble());
        float phi = (float)(_rng.NextDouble() * Mathf.Tau);
        var side = dir.Cross(Mathf.Abs(dir.Y) < 0.9f ? Vector3.Up : Vector3.Right).Normalized();
        var up2 = side.Cross(dir).Normalized();
        var axis = (side * Mathf.Cos(phi) + up2 * Mathf.Sin(phi)).Normalized();
        return dir.Rotated(axis, theta).Normalized();
    }

}
