using Godot;

namespace Titans;

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
  private const float LosSampleStep = 6f; // terrain sampling step along the sight line
  private const float LosTerrainClearance = 0.75f; // terrain this close under the sight line blocks it
  private const float PatrolPointSeconds = 16f; // a patrol point expires after this; a fresh one is picked
  private const float PatrolStopDist = 0.6f; // patrol halts this close to its point
  private const float RetreatSpeedFactor = 0.5f; // the range game: walk-out pace vs full pace
  private const float OrbitSpeedFactor = 0.7f; // the range game: in-band orbit pace vs full pace
  private const float OrbitFlipSeconds = 6f; // the orbit reverses direction this often
  private const float FoeFallbackCooldown = 0.85f; // the second foe seat's own no-def cooldown (differs from NpcFallbackCooldown by design)
  private const float GunneryAimHeight = 7.6f; // gunnery aims this far above the target's root (chest)
  private const float FallbackMuzzleSpeed = 420f; // shell speed when the shooter has no weapon def
  private const float FallbackShellDamage = 18f, FallbackShellWeight = 16f; // shell payload when there is no def
  private const float NpcCannonSfxRate = 0.78f; // NPC cannon report playback rate
  private const float NpcCannonFlashSize = 0.5f, NpcCannonFlashLife = 0.08f; // NPC muzzle flash
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
    var near = this.NearestTeamWalker(self.RootPos);
    if (near == null) { engaged = false; return false; }
    if (alerted) { engaged = true; return true; }
    float dp = self.RootPos.DistanceTo(near.RootPos);
    if (!engaged && dp < DetectRange && this.HasLineOfSight(self, near))
    {
      engaged = true;
    }
    else if (engaged && dp > LeashRange)
    {
      engaged = false;
    }

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
    int steps = Mathf.Max(2, (int)(dist / LosSampleStep));
    for (int i = 1; i < steps; i++)
    {
      var p = pa.Lerp(pb, (float)i / steps);
      if (Main.HeightAt(p.X, p.Z) > p.Y - LosTerrainClearance)
      {
        return false;
      }
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
      if (s == null)
      {
        break;
      }

      var id = load.WeaponIn(s.Value);
      var w = id == null ? null : WeaponCatalog.ById(id.Value);
      if (w != null && parts.Alive(s.Value)) { slot = s; def = w; return true; }
    }
    return false;
  }

  private bool EnsureBallistic(Ally a)
  {
    if (this.PickBallistic(a.Spec, a.Loadout, a.Parts, out var s, out var w)) { a.BallisticSlot = s; a.DefBallistic = w; return true; }
    a.DefBallistic = null;
    return false;
  }

  private bool EnsureBallistic(Foe f)
  {
    if (this.PickBallistic(f.Spec, f.Loadout, f.Parts, out var s, out var w)) { f.BallisticSlot = s; f.DefBallistic = w; return true; }
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
      float a = (float)this._rng.NextDouble() * Mathf.Tau;
      float r = PatrolRadius * Mathf.Sqrt((float)this._rng.NextDouble());
      point = home + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
      point.Y = Main.HeightAt(point.X, point.Z);
      timer = PatrolPointSeconds;
      tgt = new Vector2(point.X, point.Z);
    }
    var to = new Vector3(tgt.X - flat.X, 0f, tgt.Y - flat.Y);
    float dist = to.Length();
    if (dist < PatrolStopDist) { w.SetDrive(yaw, 0f); return; }
    float yawTo = RiderWalker.YawOf(to.Normalized());
    float dy = RiderWalker.WrapAngle(yawTo - yaw);
    yaw += Mathf.Clamp(dy, -NpcTurnRate * delta, NpcTurnRate * delta);
    float spd = dist > PatrolArriveDist ? pace : 0f;
    // Hold the universal separation (owner, 2026-11-15).
    if (spd > 0f && this.RiderAhead(w, yaw))
    {
      spd = 0f;
    }

    w.SetDrive(yaw, spd);
    this.TrackTorso(w, parts, yaw, yawTo, delta);
    w.TorsoPitch = 0f;
  }

  // The foe's current victim: nearest living opponent, re-picked
  // on a think timer so equidistant riders don't flip-flop.
  private (RiderWalker W, Vector3 Vel, string Tag) PickTarget(
      RiderWalker self, RiderWalker cur, ref float thinkT, float dt)
  {
    thinkT -= dt;
    if (cur != null && thinkT > 0f && !this.IsDead(cur))
    {
      return (cur, this.VelOf(cur), this.TagOf(cur));
    }

    thinkT = ThinkSeconds;
    // Nearest living rider of ANOTHER team (Stage D3):
    // the candidate pool is every rider on the field -
    // in the classic three-seat arena all teams differ,
    // so this reduces to the old free-for-all exactly.
    int myTeam = this.TeamOf(self);
    RiderWalker best = null;
    float bd = float.MaxValue;
    void Scan(RiderWalker cand)
    {
      if (cand == null || cand == self || this.IsDead(cand))
      {
        return;
      }

      if (this.TeamOf(cand) == myTeam)
      {
        return;
      }

      float d = self.RootPos.DistanceSquaredTo(cand.RootPos);
      if (d < bd) { bd = d; best = cand; }
    }
    Scan(this._player);
    Scan(this._enemy);
    if (this._foe != null)
    {
      Scan(this._foe.Walker);
    }

    foreach (var a in this._allies)
    {
      Scan(a.Walker);
    }

    return best == null ? (null, Vector3.Zero, "") : (best, this.VelOf(best), this.TagOf(best));
  }

  private bool IsDead(RiderWalker w) =>
      w == this._player ? this._playerDead
      : this._foe != null && w == this._foe.Walker ? this._foe.Dead
      : this.AllyOf(w) is { } da ? da.Dead
      : this._enemyDead;

  private Vector3 VelOf(RiderWalker w) =>
      w == this._player ? this._playerVel
      : this._foe != null && w == this._foe.Walker ? this._foe.Vel
      : this.AllyOf(w) is { } va ? va.Vel
      : this._enemyVel;

  private string TagOf(RiderWalker w) =>
      w == this._player ? Roles.Player
      : this._foe != null && w == this._foe.Walker ? Roles.Foe
      : this.AllyOf(w) is { } ta ? ta.Role
      : Roles.Enemy;

  private RiderWalker WalkerOf(RiderParts parts) =>
      parts == this._partsPlayer ? this._player
      : this._foe != null && parts == this._foe.Parts ? this._foe.Walker
      : this.AllyOfParts(parts) is { } wa ? wa.Walker
      : this._enemy;

  private RiderParts PartsOf(RiderWalker w) =>
      w == this._player ? this._partsPlayer
      : this._foe != null && w == this._foe.Walker ? this._foe.Parts
      : this.AllyOf(w) is { } pa ? pa.Parts
      : this._partsEnemy;

  private void UpdateEnemyAI(float delta)
  {
    if (WorldConfig.IsMission)
    {
      if (!this.GuardGate(this._enemy, ref this._foe0Engaged, this._foe0Alerted))
      {
        this._foe0TargetW = null;
        this.PatrolMove(this._enemy, this._partsEnemy, PadWorld(1), ref this._enemyYaw,
                    ref this._foe0Patrol, ref this._foe0PatrolT, Mathf.Min(this._enemySpec.MaxSpeed, EnemyMaxSpeed), delta);
        return;
      }
      this._foe0TargetW = this.NearestTeamWalker(this._enemy.RootPos) ?? this._player;
      this.UpdateFoeMovement(this._enemy, this._partsEnemy,
          this.PaceFor(this._enemySpec, this._enemy.RootPos.DistanceTo(this._foe0TargetW.RootPos)),
                ref this._enemyYaw, ref this._orbitDir, ref this._orbitTimer, this._foe0TargetW, delta);
      return;
    }
    var (target, _, _) = this.PickTarget(this._enemy, this._foe0TargetW, ref this._foe0ThinkT, delta);
    this._foe0TargetW = target;
    float pace0 = target != null ? this.PaceFor(this._enemySpec, this._enemy.RootPos.DistanceTo(target.RootPos)) : this.EnemyPace;
    this.UpdateFoeMovement(this._enemy, this._partsEnemy, pace0,
            ref this._enemyYaw, ref this._orbitDir, ref this._orbitTimer, target, delta);
  }

  private void UpdateFoeAI(float delta)
  {
    if (this._foe.Dead) { this._foe.Walker.SetDrive(this._foe.Yaw, 0f); return; }
    if (WorldConfig.IsMission)
    {
      if (!this.GuardGate(this._foe.Walker, ref this._foe.Engaged, this._foe.Alerted))
      {
        this._foeTargetW = null;
        this.PatrolMove(this._foe.Walker, this._foe.Parts, PadWorld(2), ref this._foe.Yaw,
                    ref this._foe.Patrol, ref this._foe.PatrolT, Mathf.Min(this._foe.Spec.MaxSpeed, EnemyMaxSpeed), delta);
        return;
      }
      this._foeTargetW = this.NearestTeamWalker(this._foe.Walker.RootPos) ?? this._player;
      this.UpdateFoeMovement(this._foe.Walker, this._foe.Parts,
                this.PaceFor(this._foe.Spec, this._foe.Walker.RootPos.DistanceTo(this._foeTargetW.RootPos)),
                ref this._foe.Yaw, ref this._foe.OrbitDir, ref this._foe.OrbitTimer, this._foeTargetW, delta);
      return;
    }
    var (target, _, _) = this.PickTarget(this._foe.Walker, this._foeTargetW, ref this._foe.ThinkT, delta);
    this._foeTargetW = target;
    float pace = target != null ? this.PaceFor(this._foe.Spec, this._foe.Walker.RootPos.DistanceTo(target.RootPos))
        : Mathf.Min(this._foe.Spec.MaxSpeed, EnemyMaxSpeed);
    this.UpdateFoeMovement(this._foe.Walker, this._foe.Parts, pace,
            ref this._foe.Yaw, ref this._foe.OrbitDir, ref this._foe.OrbitTimer, target, delta);
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
    else if (dist < RetreatRange) { desiredYaw = yawTo + Mathf.Pi; spd = pace * RetreatSpeedFactor; }
    else
    {
      orbitTimer += delta;
      if (orbitTimer > OrbitFlipSeconds) { orbitTimer = 0f; orbitDir = -orbitDir; }
      desiredYaw = yawTo + orbitDir * Mathf.Pi / 2f;
      spd = pace * OrbitSpeedFactor;
    }
    float dy = RiderWalker.WrapAngle(desiredYaw - yaw);
    float maxTurn = NpcTurnRate * delta;
    yaw += Mathf.Clamp(dy, -maxTurn, maxTurn);
    w.SetDrive(yaw, spd);
    this.TrackTorso(w, parts, yaw, yawTo, delta);
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
      this.TrackTorso(this._enemy, this._partsEnemy, this._enemyYaw, yawTo, dt);

  private void UpdateEnemyGunnery(float dt)
  {
    if (this._enemyDead)
    {
      return;
    }

    if (WorldConfig.IsMission && !this._foe0Engaged)
    {
      return;
    }

    if (this._cdEnemy > 0f)
    {
      return;
    }
    // Missions: gunnery shoots what the movement brain is
    // fighting - the nearest team rider (the guard gate's
    // pick). The Arena keeps the free-for-all re-pick.
    RiderWalker target; Vector3 vel; string tag;
    if (WorldConfig.IsMission)
    {
      target = this._foe0TargetW;
      if (target == null || this.IsDead(target))
      {
        return;
      }

      vel = this.VelOf(target); tag = this.TagOf(target);
    }
    else
    {
      var t = this.PickTarget(this._enemy, this._foe0TargetW, ref this._foe0ThinkT, 0f);
      if (t.W == null)
      {
        return;
      }

      (target, vel, tag) = t;
    }
    this.UpdateFoeGunnery(this._enemy, this._partsEnemy, this._defEnemyBallistic, this._enemyBallisticSlot,
            Roles.Enemy, tag, target, vel, () =>
            {
              this._cdEnemy = this._defEnemyBallistic?.Cooldown ?? NpcFallbackCooldown;
              this._enemyShots++;
            });
  }

  private void UpdateFoeGunnery(float dt)
  {
    if (this._foe.Dead || this._foe.Cd > 0f)
    {
      return;
    }

    if (WorldConfig.IsMission && !this._foe.Engaged)
    {
      return;
    }

    if (!this.EnsureBallistic(this._foe))
    {
      return;
    }

    RiderWalker target; Vector3 vel; string tag;
    if (WorldConfig.IsMission)
    {
      target = this._foeTargetW;
      if (target == null || this.IsDead(target))
      {
        return;
      }

      vel = this.VelOf(target); tag = this.TagOf(target);
    }
    else
    {
      var t = this.PickTarget(this._foe.Walker, this._foeTargetW, ref this._foe.ThinkT, 0f);
      if (t.W == null)
      {
        return;
      }

      (target, vel, tag) = t;
    }
    this.UpdateFoeGunnery(this._foe.Walker, this._foe.Parts, this._foe.DefBallistic, this._foe.BallisticSlot,
            Roles.Foe, tag, target, vel, () => { this._foe.Cd = this._foe.DefBallistic?.Cooldown ?? FoeFallbackCooldown; this._foeShots++; });
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
    if (parts == null || !parts.Alive(slot ?? PartId.ArmR))
    {
      return; // the mount is gone with its part
    }

    var muzzle = this.MuzzleFor(w, slot ?? PartId.ArmR);
    var tp = target.RootPos + Vector3.Up * GunneryAimHeight;
    float dist = muzzle.DistanceTo(tp);
    if (dist > NpcGunneryRange)
    {
      return;
    }

    var los = this._hit.Ray(muzzle, (tp - muzzle).Normalized(), dist, tag);
    if (los.Hit && los.Target != targetTag)
    {
      return; // blocked: building/terrain
    }

    float speed = def?.MuzzleSpeed ?? FallbackMuzzleSpeed;
    float tof = dist / speed;
    var aim = tp + targetVel * tof;
    var dir = this.ApplyInaccuracy((aim - muzzle).Normalized(),
        parts.Alive(PartId.Head) ? InaccuracySensedDeg : InaccuracyHeadlessDeg);
    var shell = this.SpawnProjectile(muzzle, dir * speed, 0f, def?.Damage ?? FallbackShellDamage, 0f, tag, rocket: false, weaponWeight: def?.Weight ?? FallbackShellWeight); // shells fly straight (owner)
    if (def != null && speed > 0f)
    {
      shell.Life = def.Range / speed; // and burn out at the weapon's range
    }

    this.PlaySfx(this._sfxCannon, muzzle, 0f, NpcCannonSfxRate);
    this.SpawnFlash(muzzle, CannonMuzzleColor, NpcCannonFlashSize, NpcCannonFlashLife); // the player's cannon flash colour - same shot, same look
    onFired?.Invoke();
    Dbg.Print($"WPN FOEFIRE {tag} dist={dist:F1} tof={tof:F2} targetVel={targetVel.Length():F1}");
  }
}
