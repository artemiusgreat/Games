using Godot;

// RiderDuel module: the NPC teammates' brain and the leader's
// orders (owner, teams stage step 5). Allies run the SAME
// range game and cannon gunnery as the AI foes — no second AI
// framework; the team order only decides WHAT they engage and
// where they stand when there is nothing to engage:
//
//   SHIFT+1 Attack my target   — focus the leader's Tab-selected enemy
//   SHIFT+2 Attack nearby enemy — each ally picks its own nearest foe
//   SHIFT+3 Protect my target  — guard the leader: engage what
//                                closes on him, otherwise hold
//                                formation on him
//   SHIFT+4 Full stop          — freeze: no movement, no fire
//
// With no enemy in reach, allies fall back to a loose
// formation behind the leader and march at full chassis speed
// to keep up on the big map (PaceFor, the defenders' rule).
// The standing order before any key is pressed is SHIFT+1's
// (owner's ruling: the default behaviour is Attack my target).
public partial class RiderDuel
{
    private enum TeamOrder { AttackTarget, AttackNearby, ProtectLeader, Hold }

    private TeamOrder _teamOrder = TeamOrder.AttackTarget;
    private int _allyShots; // harness counter (the team test reads it)

    private void SetTeamOrder(TeamOrder o)
    {
        _teamOrder = o;
        AddFeed(o switch
        {
            TeamOrder.AttackTarget => "ORDER: ATTACK MY TARGET",
            TeamOrder.AttackNearby => "ORDER: ATTACK NEARBY ENEMY",
            TeamOrder.ProtectLeader => "ORDER: PROTECT MY TARGET",
            _ => "ORDER: FULL STOP"
        });
        GD.Print($"TEAM order={o}");
    }

    // The leader's Tab-selected enemy (the HUD's one active
    // target), or null when it is down.
    private RiderWalker ActiveTargetWalker()
    {
        var list = OpposingTargets();
        if (list.Count == 0) return null;
        if (_targetIdx < 0 || _targetIdx >= list.Count) _targetIdx = 0;
        var t = list[_targetIdx];
        return t.Dead ? null : t.W;
    }

    private RiderWalker NearestEnemyWalker(Vector3 from, float maxDist)
    {
        RiderWalker best = null;
        float bd = maxDist * maxDist;
        foreach (var t in OpposingTargets())
        {
            if (t.Dead || t.W == null) continue;
            float d = from.DistanceSquaredTo(t.W.RootPos);
            if (d < bd) { bd = d; best = t.W; }
        }
        return best;
    }

    // The ally's station when it is not engaging: a loose
    // echelon behind the leader, spread by team position.
    private Vector3 FormationPoint(Ally a)
    {
        var f = new Vector3(-Mathf.Sin(_playerYaw), 0f, -Mathf.Cos(_playerYaw)); // leader forward
        var r = new Vector3(-f.Z, 0f, f.X);                                       // leader right
        int idx = _allies.IndexOf(a);
        float side = idx == 0 ? -11f : idx == 1 ? 11f : 0f;
        float back = idx == 2 ? 22f : 13f;
        return _player.RootPos + r * side - f * back;
    }

    // Walk to a point and stop there — the patrol steering
    // idiom (turn-clamped heading, torso on the walk bearing).
    private void MoveToward(Ally a, Vector3 point, float dt)
    {
        var w = a.Walker;
        var to = new Vector3(point.X - w.RootPos.X, 0f, point.Z - w.RootPos.Z);
        float dist = to.Length();
        if (dist < 7f) { w.SetDrive(a.Yaw, 0f); return; }
        float yawTo = RiderWalker.YawOf(to.Normalized());
        float dy = RiderWalker.WrapAngle(yawTo - a.Yaw);
        float yaw = a.Yaw + Mathf.Clamp(dy, -0.7f * dt, 0.7f * dt);
        a.Yaw = yaw;
        w.SetDrive(yaw, PaceFor(a.Spec, dist));
        TrackTorso(w, a.Parts, yaw, yawTo, dt);
        w.TorsoPitch = 0f;
    }

    private void UpdateAlliesAI(float dt)
    {
        if (_allies.Count == 0) return;
        foreach (var a in _allies)
        {
            if (a.Dead) { a.Walker.SetDrive(a.Yaw, 0f); a.TargetW = null; continue; }
            // Tournament hostile records run the bracket brain
            // (Stage D3c), not the leader's orders.
            if (!WorldConfig.IsMission && a.Team != 0) continue;
            // No living leader to take orders from (the command
            // pass is pending or the team is wiped): hold still.
            if (_playerDead) { a.Walker.SetDrive(a.Yaw, 0f); continue; }
            if (_teamOrder == TeamOrder.Hold)
            {
                a.Walker.SetDrive(a.Yaw, 0f);
                a.TargetW = null;
                continue;
            }
            RiderWalker tgt = _teamOrder switch
            {
                TeamOrder.AttackTarget => ActiveTargetWalker() ?? NearestEnemyWalker(a.Walker.RootPos, 450f),
                TeamOrder.ProtectLeader => NearestEnemyWalker(_player.RootPos, 170f),
                _ => NearestEnemyWalker(a.Walker.RootPos, 450f)
            };
            if (tgt != null)
            {
                a.TargetW = tgt;
                float pace = PaceFor(a.Spec, a.Walker.RootPos.DistanceTo(tgt.RootPos));
                UpdateFoeMovement(a.Walker, a.Parts, pace,
                    ref a.Yaw, ref a.OrbitDir, ref a.OrbitTimer, tgt, dt);
                if (a.Cd <= 0f && a.DefBallistic != null)
                    UpdateFoeGunnery(a.Walker, a.Parts, a.DefBallistic, a.BallisticSlot,
                        a.Role, TagOf(tgt), tgt, VelOf(tgt),
                        () => { a.Cd = a.DefBallistic?.Cooldown ?? 2.4f; _allyShots++; });
            }
            else
            {
                a.TargetW = null;
                MoveToward(a, FormationPoint(a), dt);
            }
        }
    }

    private int _bracketShots;

    // Tournament hostile NPCs (Stage D3c): the seats' own
    // game with no leader to follow — PickTarget chooses the
    // nearest rider of another team (a teammate is never
    // picked), the shared range game closes on it, and the
    // shared cannon gunnery fires the record's ballistic
    // weapon, exactly as a seat fights.
    private void UpdateBracketAI(float dt)
    {
        if (WorldConfig.IsMission) return;
        foreach (var a in _allies)
        {
            if (a.Team == 0 || a.Walker == null) continue;
            if (a.Dead) { a.Walker.SetDrive(a.Yaw, 0f); a.TargetW = null; continue; }
            var (tgt, _, _) = PickTarget(a.Walker, a.TargetW, ref a.ThinkT, dt);
            a.TargetW = tgt;
            if (tgt == null) { a.Walker.SetDrive(a.Yaw, 0f); continue; }
            float pace = PaceFor(a.Spec, a.Walker.RootPos.DistanceTo(tgt.RootPos));
            UpdateFoeMovement(a.Walker, a.Parts, pace,
                ref a.Yaw, ref a.OrbitDir, ref a.OrbitTimer, tgt, dt);
            if (a.Cd <= 0f && a.DefBallistic != null)
                UpdateFoeGunnery(a.Walker, a.Parts, a.DefBallistic, a.BallisticSlot,
                    a.Role, TagOf(tgt), tgt, VelOf(tgt),
                    () => { a.Cd = a.DefBallistic?.Cooldown ?? 2.4f; _bracketShots++; });
        }
    }
}
