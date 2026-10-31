using Godot;
using System.Collections.Generic;

// MechDuel module: the enemy brain: movement AI, torso tracking, gunnery.
// Split from the single-file build (round 22 cleanup); the class
// stays one partial class — see MechDuel.cs for the field block.
public partial class MechDuel
{
    private void UpdateEnemyAI(float delta)
    {
        var toP = _player.RootPos - _enemy.RootPos;
        float dist = new Vector2(toP.X, toP.Z).Length();
        float yawTo = MechWalker.YawOf(toP.Normalized());
        // (A downed Atlas has a leg gone, so the immobile branch below
        // handles it: no drive, but the torso still tracks and fires.)
        // (No immobile branch anymore: a leg-wrecked Atlas limps —
        // the walker's SpeedFactor throttles its drive — and keeps
        // playing its range game below.)
        float desiredYaw, spd;
        if (dist > 38f) { desiredYaw = yawTo; spd = EnemyPace; }
        else if (dist < 26f) { desiredYaw = yawTo; spd = -EnemyPace * 0.5f; }
        else
        {
            _orbitTimer += delta;
            if (_orbitTimer > 6f) { _orbitTimer = 0f; _orbitDir = -_orbitDir; }
            desiredYaw = yawTo + _orbitDir * Mathf.Pi / 2f;
            spd = EnemyPace * 0.7f;
        }
        float dy = MechWalker.WrapAngle(desiredYaw - _enemyYaw);
        float maxTurn = 0.7f * delta;
        _enemyYaw += Mathf.Clamp(dy, -maxTurn, maxTurn);
        _enemy.SetDrive(_enemyYaw, spd);
        TrackEnemyTorso(delta, yawTo);
        _enemy.TorsoPitch = 0f;
    }

    // Atlas torso tracking: snaps to the target bearing while the head
    // (sensor suite) lives; with the head destroyed the twist closes at
    // 0.6 rad/s -- a visible, measurable lag.
    private void TrackEnemyTorso(float dt, float yawTo)
    {
        float desiredTwist = MechWalker.WrapAngle(yawTo - _enemyYaw);
        if (_partsEnemy.Alive("head"))
        {
            _enemy.TorsoTwist = desiredTwist;
        }
        else
        {
            float cur = _enemy.TorsoTwist;
            float step = Mathf.Clamp(MechWalker.WrapAngle(desiredTwist - cur),
                -0.6f * dt, 0.6f * dt);
            _enemy.TorsoTwist = cur + step;
        }
    }

    private void UpdateEnemyGunnery(float dt)
    {
        if (_enemyDead || _playerDead) return;
        if (_cdEnemy > 0f) return;
        if (!_partsEnemy.Alive("arm_R")) return; // AC/20 mount lost with the right arm
        var muzzle = EnemyMuzzle();
        var target = _player.RootPos + Vector3.Up * 7.6f;
        float dist = muzzle.DistanceTo(target);
        if (dist > 75f) return;
        var los = _hit.Ray(muzzle, (target - muzzle).Normalized(), dist, "ENEMY");
        if (los.Hit && los.Target != "PLAYER") return; // blocked: building/terrain
        float tof = dist / 150f; // matches the shell speed below (50 x3, owner 2026-10-29) — lead and flight must use the same number
        var aim = target + _playerVel * tof;
        // Head destroyed: sensors gone, aim spread triples (1.2 -> 3.6 deg).
        var dir = ApplyInaccuracy((aim - muzzle).Normalized(),
            _partsEnemy.Alive("head") ? 1.2f : 3.6f);
        SpawnProjectile(muzzle, dir * 150f, 5f, _defEnemyBallistic?.Damage ?? 18f, 0f, "ENEMY", rocket: false, weaponWeight: _defEnemyBallistic?.Weight ?? 16f); // 50 x3 (owner: every cannon bullet 3x — the dodge window narrows with it)
        PlaySfx(_sfxCannon, muzzle, 0f, 0.78f);
        SpawnFlash(muzzle, new Color(1f, 0.6f, 0.2f), 0.5f, 0.08f);
        _cdEnemy = _defEnemyBallistic?.Cooldown ?? 2.4f;
        _enemyShots++;
        GD.Print($"WPN ATLASFIRE dist={dist:F1} tof={tof:F2} playerVel={_playerVel.Length():F1}");
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
