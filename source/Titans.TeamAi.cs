using Godot;
using System.Collections.Generic;

namespace Titans;

// Titans module: the NPC teammates' brain and the leader's
// orders (owner, teams stage step 5). Allies run the SAME
// range game and cannon gunnery as the AI foes - no second AI
// framework; the team order only decides WHAT they engage and
// where they stand when there is nothing to engage:
//
//   SHIFT+1 Follow me          - hold formation on the leader;
//                                engage only what closes within
//                                260u of the ally itself
//   SHIFT+2 Attack my target   - focus the leader's Tab-selected enemy
//   SHIFT+3 Attack nearby enemy - each ally picks its own nearest foe
//   SHIFT+4 Protect my target  - guard the leader: engage what
//                                closes on him, otherwise hold
//                                formation on him
//   SHIFT+5 Full stop          - freeze: no movement, no fire
//
// With no enemy in reach, allies fall back to a loose
// formation behind the leader and march at full chassis speed
// to keep up on the big map (PaceFor, the defenders' rule).
// The standing order before any key is pressed is SHIFT+1's
// (owner's ruling, 2026-11-14 evening - supersedes the same
// day's Attack-my-target default: the default is Follow me).
public partial class Titans
{
    private enum TeamOrder { Follow, AttackTarget, AttackNearby, ProtectCompanion, Hold }

    // Per-order engagement reach (owner, teams stage): how
    // close a foe must close before an ally on that order
    // engages it.
    private const float FollowEngageRange = 260f;
    private const float AttackEngageRange = 450f;
    private const float ProtectEngageRange = 170f;

    // Follow formation (FormationPoint / MoveToward): the
    // echelon's lateral spread and depth behind the leader,
    // and the approach stop distance.
    private const float FormationSideOffset = 17f;
    private const float FormationBackOffset = 20f;
    private const float FormationBackOffsetRear = 31f; // the third follower rides deeper
    private const float FormationStopDist = 7f;

    private TeamOrder _teamOrder = TeamOrder.Follow;
    private int _allyShots; // harness counter (the team test reads it)

    private void SetTeamOrder(TeamOrder o)
    {
        _teamOrder = o;
        Dbg.Print($"TEAM order={o}");
        AnnounceOrder(o); // the voice clip rides the feed line (owner, 2026-11-25)
    }

    // The active companion (owner, 2026-11-23): CTRL+TAB
    // cycles the player's chosen teammate, and SHIFT+5's
    // protect order anchors on him. Defaults to the first
    // living teammate; village guards never count - they
    // hold their posts, not the player's wing.
    private Ally _companion;

    private List<Ally> CompanionList()
    {
        var list = new List<Ally>();
        foreach (var a in _allies)
            if (a.Team == 0 && !a.Guard && a.Walker != null) list.Add(a);
        return list;
    }

    private Ally ActiveCompanion()
    {
        var list = CompanionList();
        if (_companion != null && !_companion.Dead && list.Contains(_companion)) return _companion;
        foreach (var a in list)
            if (!a.Dead) { _companion = a; return a; }
        return null;
    }

    private void CycleCompanion()
    {
        var living = CompanionList().FindAll(a => !a.Dead);
        if (living.Count == 0) return;
        var cur = ActiveCompanion();
        int i = cur == null ? -1 : living.IndexOf(cur);
        _companion = living[(i + 1) % living.Count];
        AddFeed("COMPANION: " + _companion.Spec.Name.ToUpperInvariant());
    }

    // Where the protect order anchors: the active companion,
    // or the player himself when no companion is standing.
    private Vector3 ProtectAnchor()
    {
        var c = ActiveCompanion();
        return c != null ? c.Walker.RootPos : _player.RootPos;
    }

    // The leader's Tab-selected enemy (the HUD's one active
    // target), or null when it is down.
    private RiderWalker ActiveTargetWalker()
    {
        var list = OpposingTargets();
        if (list.Count == 0) return null;
        var t = list[PickTargetIdx(list)];
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
    // Widened 2026-11-18 (owner): more minimum distance
    // between all riders of the team under Follow me -
    // leader to follower ~26-31u, follower to follower
    // ~34u (was ~17-22u / 22u).
    private Vector3 FormationPoint(Ally a)
    {
        var f = new Vector3(-Mathf.Sin(_playerYaw), 0f, -Mathf.Cos(_playerYaw)); // leader forward
        var r = new Vector3(-f.Z, 0f, f.X);                                       // leader right
        int idx = _allies.IndexOf(a);
        float side = idx == 0 ? -FormationSideOffset : idx == 1 ? FormationSideOffset : 0f;
        float back = idx == 2 ? FormationBackOffsetRear : FormationBackOffset;
        return _player.RootPos + r * side - f * back;
    }

    // Walk to a point and stop there - the patrol steering
    // idiom (turn-clamped heading, torso on the walk bearing).
    private void MoveToward(Ally a, Vector3 point, float dt)
    {
        var w = a.Walker;
        var to = new Vector3(point.X - w.RootPos.X, 0f, point.Z - w.RootPos.Z);
        float dist = to.Length();
        if (dist < FormationStopDist) { w.SetDrive(a.Yaw, 0f); return; }
        float yawTo = RiderWalker.YawOf(to.Normalized());
        float dy = RiderWalker.WrapAngle(yawTo - a.Yaw);
        float yaw = a.Yaw + Mathf.Clamp(dy, -NpcTurnRate * dt, NpcTurnRate * dt);
        a.Yaw = yaw;
        float pace = PaceFor(a.Spec, dist);
        // Hold the universal separation (owner, 2026-11-15):
        // never steer into another rider's space, the leader's
        // above all - the formation stop radius alone let a
        // follower end its approach inside the leader's
        // separation and grind against the collision pass.
        // When the way to the station is blocked, sidestep
        // around the rider at the boundary instead of halting
        // nose-first: the station may lie straight past him.
        const float SidestepAngle = 1.35f; // yaw offset tried on each side when the way ahead is blocked
        if (RiderAhead(w, yaw))
        {
            if (!RiderAhead(w, yaw + SidestepAngle)) { yaw += SidestepAngle; a.Yaw = yaw; }
            else if (!RiderAhead(w, yaw - SidestepAngle)) { yaw -= SidestepAngle; a.Yaw = yaw; }
            else pace = 0f;
        }
        w.SetDrive(yaw, pace);
        TrackTorso(w, a.Parts, yaw, yawTo, dt);
        w.TorsoPitch = 0f;
    }

    private void UpdateAlliesAI(float dt)
    {
        if (_allies.Count == 0) return;
        float dtFrame = dt;
        foreach (var a in _allies)
        {
            // Ascent dormancy: a distant rider ticks at 5 Hz
            // with its accumulated step, or not at all.
            if (!TickReady(a.Walker)) continue;
            dt = TickDt(a.Walker, dtFrame);
            if (a.Dead) { a.Walker.SetDrive(a.Yaw, 0f); a.TargetW = null; continue; }
            // Ascent guards run their own duty brain below,
            // never the leader's orders.
            if (a.Guard) continue;
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
                TeamOrder.Follow => NearestEnemyWalker(a.Walker.RootPos, FollowEngageRange),
                TeamOrder.AttackTarget => ActiveTargetWalker() ?? NearestEnemyWalker(a.Walker.RootPos, AttackEngageRange),
                TeamOrder.ProtectCompanion => NearestEnemyWalker(ProtectAnchor(), ProtectEngageRange),
                _ => NearestEnemyWalker(a.Walker.RootPos, AttackEngageRange)
            };
            if (tgt != null)
            {
                a.TargetW = tgt;
                float pace = PaceFor(a.Spec, a.Walker.RootPos.DistanceTo(tgt.RootPos));
                UpdateFoeMovement(a.Walker, a.Parts, pace,
                    ref a.Yaw, ref a.OrbitDir, ref a.OrbitTimer, tgt, dt);
                if (a.Cd <= 0f && EnsureBallistic(a))
                    UpdateFoeGunnery(a.Walker, a.Parts, a.DefBallistic, a.BallisticSlot,
                        a.Role, TagOf(tgt), tgt, VelOf(tgt),
                        () => { a.Cd = a.DefBallistic?.Cooldown ?? NpcFallbackCooldown; _allyShots++; });
            }
            else
            {
                a.TargetW = null;
                // Under the protect order the formation itself
                // re-anchors on the companion, not the leader.
                var fp = FormationPoint(a);
                if (_teamOrder == TeamOrder.ProtectCompanion)
                {
                    var c = ActiveCompanion();
                    if (c != null) fp = c.Walker.RootPos + (fp - _player.RootPos);
                }
                MoveToward(a, fp, dt);
            }
        }
    }

    // Ascent guard duty (owner, 2026-11-20): the defenders'
    // own rule, per site - patrol the home disc, engage the
    // nearest team rider inside AggroRange, break off past
    // LeashRange (GuardGate is the seats' latch). This is what
    // makes the valley non-linear: a village the player never
    // approaches never leaves its patrol, and one he slips
    // past lets him go at the leash line.
    private void UpdateGuardAI(float dt)
    {
        if (!WorldConfig.IsMission) return;
        float dtFrame = dt;
        foreach (var a in _allies)
        {
            if (!a.Guard) continue;
            if (!TickReady(a.Walker)) continue;
            dt = TickDt(a.Walker, dtFrame);
            if (a.Dead) { a.Walker.SetDrive(a.Yaw, 0f); a.TargetW = null; continue; }
            if (!GuardGate(a.Walker, ref a.Engaged, a.Alerted))
            {
                a.TargetW = null;
                PatrolMove(a.Walker, a.Parts, a.GuardHome, ref a.Yaw,
                    ref a.Patrol, ref a.PatrolT, Mathf.Min(a.Spec.MaxSpeed, EnemyMaxSpeed), dt);
                continue;
            }
            var tgt = NearestTeamWalker(a.Walker.RootPos) ?? _player;
            if (tgt != a.TargetW) Dbg.Print($"GUARD {a.Role} target -> {TagOf(tgt)}");
            a.TargetW = tgt;
            float pace = PaceFor(a.Spec, a.Walker.RootPos.DistanceTo(tgt.RootPos));
            UpdateFoeMovement(a.Walker, a.Parts, pace,
                ref a.Yaw, ref a.OrbitDir, ref a.OrbitTimer, tgt, dt);
            if (a.Cd <= 0f && EnsureBallistic(a))
                UpdateFoeGunnery(a.Walker, a.Parts, a.DefBallistic, a.BallisticSlot,
                    a.Role, TagOf(tgt), tgt, VelOf(tgt),
                    () => { a.Cd = a.DefBallistic?.Cooldown ?? NpcFallbackCooldown; _allyShots++; });
        }
    }

    private int _bracketShots;

    // Tournament hostile NPCs (Stage D3c): the seats' own
    // game with no leader to follow - PickTarget chooses the
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
            if (a.Cd <= 0f && EnsureBallistic(a))
                UpdateFoeGunnery(a.Walker, a.Parts, a.DefBallistic, a.BallisticSlot,
                    a.Role, TagOf(tgt), tgt, VelOf(tgt),
                    () => { a.Cd = a.DefBallistic?.Cooldown ?? NpcFallbackCooldown; _bracketShots++; });
        }
    }
}
