using Godot;
using System.Collections.Generic;

// MechDuel module: aim, firing, projectiles, damage and part consequences.
// Split from the single-file build (round 22 cleanup); the class
// stays one partial class — see MechDuel.cs for the field block.
public partial class MechDuel
{
    // ---------- weapons ----------

    // Torso aim (owner's model): the aim ray leaves the chest along
    // the TORSO's facing — mouse twist and pitch steer it — and the
    // weapons converge on whatever it lands on (max 200u). The reticle
    // is drawn at this point's screen projection (UpdateReticle), so
    // the cursor target visibly rides the torso instead of sitting on
    // the camera axis, which crossed the player's own back.
    private Vector3 _aimPoint;
    private Vector3 _aimFrom, _aimDir;
    private Vector3 TorsoAimPoint()
    {
        float yaw = _player.TorsoYawWorld;
        // Aim direction: the wide mouse pitch when the player has
        // taken it past the torso clamp, else the torso's own pitch
        // (every harness phase drives TorsoPitch directly).
        float p = float.IsNaN(_player.AimPitchWide) ? _player.TorsoPitch : _player.AimPitchWide;
        var h = MechWalker.HeadingVec(yaw);
        var dir = new Vector3(h.X * Mathf.Cos(p), Mathf.Sin(p), h.Z * Mathf.Cos(p));
        var from = _player.BoneWorldPos("torso") + dir * 3.2f;
        _aimFrom = from;
        _aimDir = dir;
        _reticlePoint = from + dir * AimDepth;
        // Round 12: converge on the point UNDER THE CURSOR — a ray
        // from the CAMERA through the reticle. The high camera broke
        // the chest-ray convergence: the view line and the torso
        // line now diverge steeply, so shots converging on the chest
        // ray's hit landed visibly BELOW the cursor (reticle on the
        // Atlas's chest, beam in its legs — the owner's screenshot).
        // The camera ray defines what the player is pointing at, at
        // any camera height: its first hit within 60u of the player
        // is where every weapon goes. Live play always runs the
        // chase camera; harness proof poses keep the chest logic.
        if (_camMode == "chase")
        {
            var camPos = _cam.GlobalPosition;
            var viewHit = _hit.Ray(camPos, (_reticlePoint - camPos).Normalized(), 220f, "PLAYER");
            // MECH and PROP hits converge at ANY range (owner,
            // round 13): a 60u cap from the chest-ray era silently
            // rejected the cursor's hit on far targets and the shot
            // fell back to the free point — in the sky. TERRAIN
            // converges within 60u outright (the ground at your
            // feet, a near slope). Beyond 60u the hillside is a real
            // target too (owner: shooting at the hill shot the sky)
            // — but only when the chest's own line to that point is
            // clear of mechs and props. If a mech stands in front of
            // the backdrop, the hillside stays backdrop: converging
            // on it would drag the muzzle's line down through the
            // mech (the AIM sweep's raised-cursor case), so the shot
            // follows the aim line over him instead.
            if (viewHit.Hit)
            {
                bool farTerrain = viewHit.Target == "TERRAIN" && viewHit.Point.DistanceTo(_player.RootPos) > 60f;
                if (!farTerrain)
                {
                    _aimTarget = viewHit.Target;
                    return viewHit.Point;
                }
                var toPt = viewHit.Point - from;
                var block = _hit.Ray(from, toPt.Normalized(), toPt.Length(), "PLAYER");
                if (!block.Hit || block.Target == "TERRAIN")
                {
                    _aimTarget = "TERRAIN";
                    return viewHit.Point;
                }
                // Blocked by a mech/prop: backdrop — fall through.
            }
        }
        // Fallback (round 11): the chest ray's MECH/PROP hit within
        // weapon reach, else the free 40u point.
        var hit = _hit.Ray(from, dir, 200f, "PLAYER");
        _aimTarget = hit.Hit ? hit.Target : "";
        if (hit.Hit && hit.T <= 140f && (hit.Target == "ENEMY" || hit.Target == "PROP"))
            return hit.Point;
        return _reticlePoint;
    }

    // The reticle rides a fixed-depth point on the aim ray — AND
    // (round 9) the weapons converge on that same point, which is
    // what makes both of the owner's demands true at once:
    // round 6: the target must move FREELY (projecting the ray's
    // hit point made it stick to surfaces and jump between boxes —
    // round 8's model, and he reported it as grid-snapped within
    // the day). Round 8: shots must land where the cursor points
    // (a free cursor the weapons ignored was a lie — cannons hit
    // under a high cursor because the chest ray still crossed the
    // enemy). Now the cursor IS the convergence point: every
    // weapon's line is drawn to it, and the first thing on that
    // line is what gets hit. Cursor above him = the lines pass
    // above him. When the torso points behind the camera (deep
    // twist), the projection mirrors — flip through the center and
    // clamp to the screen edge.
    private string _aimTarget = "";
    private Vector3 _reticlePoint;
    private const float AimDepth = 40f;
    private void UpdateReticle()
    {
        var pt = _reticlePoint;
        var vp = GetViewport().GetVisibleRect().Size;
        var sp = _cam.UnprojectPosition(pt);
        if (_cam.IsPositionBehind(pt)) sp = vp - sp;
        _cross.ReticlePos = new Vector2(
            Mathf.Clamp(sp.X, 28f, vp.X - 28f),
            Mathf.Clamp(sp.Y, 28f, vp.Y - 28f));
    }

    // Muzzle hardpoints: the Timber Wolf's forearm cannons sit on the
    // elbow bones (the Hand_* parts are bound there), pushed forward along
    // the torso facing so the shot clears the arm. Laser = left, AC = right.
    private Vector3 PlayerMuzzle(bool left)
    {
        var bone = _player.BoneWorldPos(left ? "elbow_L" : "elbow_R");
        return bone + MechWalker.HeadingVec(_player.TorsoYawWorld) * 2.8f;
    }

    // Atlas AC/20: right-torso gun — torso bone origin plus a right/up/
    // forward offset in the torso frame.
    private Vector3 EnemyMuzzle()
    {
        var torso = _enemy.BoneWorldPos("torso");
        var fwd = MechWalker.HeadingVec(_enemy.TorsoYawWorld);
        var right = new Vector3(-fwd.Z, 0f, fwd.X);
        return torso + right * 1.7f + fwd * 1.4f + Vector3.Up * 2.6f;
    }

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

    private void FireLaser(Vector3 aimPoint)
    {
        if (_playerDead) return;
        if (!_partsPlayer.Alive("arm_L") || _defEnergy == null) { DenyWeapon("laser"); return; }
        if (_cdLaser > 0f) return;
        _cdLaser = _defEnergy.Cooldown;
        var muzzle = PlayerMuzzle(true);
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
        bool camTruth = _camMode == "chase" && !_labOpen;
        if (camTruth)
        {
            // The view line is camera -> _reticlePoint (the torso
            // aim's 40u point) — the SAME line TorsoAimPoint casts.
            // Round 20's first cut used the camera's raw forward
            // (-Basis.Z) instead: from the high chase rig that ray
            // always lands on the ground in front of the mech, so
            // every live shot hit the dirt at its feet no matter
            // where the cursor was (owner's screenshot; the suites
            // passed only because the test fallback below rescued
            // them). The reticle line is the truth in play and in
            // tests alike.
            var camDir = (_reticlePoint - _cam.GlobalPosition).Normalized();
            hit = _hit.Ray(_cam.GlobalPosition, camDir, 220f, "PLAYER", 0.9f);
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
                var fbDir = (aimPoint - muzzle).Length() > 0.5f ? (aimPoint - muzzle).Normalized() : MechWalker.HeadingVec(_player.TorsoYawWorld);
                hit = _hit.Ray(muzzle, fbDir, 140f, "PLAYER", 0.9f);
            }
        }
        else
        {
            var dir = (aimPoint - muzzle).Length() > 0.5f ? (aimPoint - muzzle).Normalized() : MechWalker.HeadingVec(_player.TorsoYawWorld);
            hit = _hit.Ray(muzzle, dir, 140f, "PLAYER", 0.9f);
        }
        bool ppc = _defEnergy.Id == "ppc";
        // The beam ends where the shot ends (owner): on a hit,
        // the hit point; on a miss — the sky — the cursor's own
        // point. It used to draw to the raycast's no-hit endpoint
        // (muzzle + dir x 140), streaking far past the reticle.
        SpawnBeam(muzzle, hit.Hit ? hit.Point : aimPoint, ppc ? new Color(0.55f, 0.8f, 1f) : new Color(1f, 0.22f, 0.12f), ppc ? 0.15f : 0.15f, 0.12f); // laser core 0.085 -> 0.15 (owner: thicker)
        PlaySfx(_sfxLaser, muzzle, 0f, ppc ? 0.8f : 1f);
        SpawnFlash(muzzle, new Color(1f, 0.4f, 0.2f), 0.3f, 0.07f);
        if (hit.Hit)
        {
            SpawnFlash(hit.Point, new Color(0.7f, 0.95f, 1f), 0.55f, 0.16f);
            SpawnSparks(hit.Point, new Color(0.65f, 0.95f, 1f), 16, 6.0f); // electric breaker spray
            SpawnImpactLight(hit.Point, new Color(0.55f, 0.9f, 1f), 2.4f, 10f, 0.3f);
            SpawnBurst(hit.Point, SparkParticleMat, 26, 0.6f, 6f, 11f, new Vector3(0f, -12f, 0f), 0.1f, 0.5f, 0.95f);
            GD.Print($"WPN LASER hit={hit.Target} t={hit.T:F1} zone={hit.Zone} part={hit.Part} point={hit.Point}");
            if (hit.Target == "ENEMY") ApplyDamage(_enemy, _defEnergy.Damage, hit, "PLAYER", _defEnergy.Weight, "laser");
            else if (hit.Target == "PROP") DamageProp(hit.Point, _defEnergy.Damage, 0.9f); // laser attribution reaches as far as its hit thickness
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
        if (!_partsPlayer.Alive("arm_R") || _defBallistic == null) { DenyWeapon("ac"); return; }
        if (_cdAc > 0f) return;
        _cdAc = _defBallistic.Cooldown;
        var muzzle = PlayerMuzzle(false);
        var toAim = aimPoint - muzzle;
        // No clamp needed here: the cannon fires the pointer line
        // directly with no range compensation, so a far aim point
        // changes nothing but parallax. (Rockets were the mortar
        // case — see LaunchRocket.)
        var dir = toAim.Length() > 0.5f ? toAim.Normalized() : MechWalker.HeadingVec(_player.TorsoYawWorld);
        (float spd, float grav) = _defBallistic.Id switch
        {
            "machine_gun" => (85f, 3f),
            "heavy_cannon" => (138f, 7f), // 46 x3 (owner, 2026-10-29: every cannon bullet 3x faster)
            _ => (420f, 5f) // autocannon 140 x3 (same owner instruction; the 60->95->140 history is in git)
        };
        SpawnProjectile(muzzle, dir * spd, grav, _defBallistic.Damage, 0f, "PLAYER", rocket: false, weaponWeight: _defBallistic.Weight);
        PlaySfx(_sfxCannon, muzzle, _defBallistic.Id == "machine_gun" ? -7f : 0f,
            _defBallistic.Id == "heavy_cannon" ? 0.72f : _defBallistic.Id == "machine_gun" ? 1.65f : 1f);
        SpawnFlash(muzzle, new Color(1f, 0.6f, 0.2f), 0.42f, 0.08f);
        GD.Print($"WPN AC fired dir={dir}");
    }

    // LRM salvo: 8 rockets staggered 0.07s apart, alternating ear pods.
    // Each rocket is dumb ballistics: velocity solved to land on the aim
    // point (up-compensated for gravity), seeded spread, 7 damage with
    // 3.0u splash falloff on impact. A lost pod removes its 4 rockets;
    // both pods gone = the LRM is offline.
    private void FireLrmSalvo(Vector3 aimPoint)
    {
        if (_playerDead) return;
        bool podL = _partsPlayer.Alive("pod_L") && _defPodL != null;
        bool podR = _partsPlayer.Alive("pod_R") && _defPodR != null;
        if (!podL && !podR) { DenyWeapon("lrm"); return; }
        if (_cdLrm > 0f) return;
        _cdLrm = Mathf.Max(podL ? _defPodL.Cooldown : 0f, podR ? _defPodR.Cooldown : 0f);
        // Each alive, equipped pod fires its own pack; pods interleave.
        var left = new System.Collections.Generic.List<WeaponDef>();
        var right = new System.Collections.Generic.List<WeaponDef>();
        if (podL) for (int i = 0; i < _defPodL.Rockets; i++) left.Add(_defPodL);
        if (podR) for (int i = 0; i < _defPodR.Rockets; i++) right.Add(_defPodR);
        var ordered = new System.Collections.Generic.List<(bool Left, WeaponDef Def)>();
        int li = 0, ri = 0;
        while (li < left.Count || ri < right.Count)
        {
            if (li < left.Count) ordered.Add((true, left[li++]));
            if (ri < right.Count) ordered.Add((false, right[ri++]));
        }
        for (int i = 0; i < ordered.Count; i++)
            _lrmQueue.Add((i * 0.07f, ordered[i].Left, aimPoint, ordered[i].Def));
        PlaySfx(_sfxRocket, PodMuzzle(true), -1f);
        GD.Print($"WPN LRM salvo queued ({ordered.Count} rockets, pods L={podL} R={podR})");
    }

    private Vector3 PodMuzzle(bool left)
    {
        var torso = _player.BoneWorldPos("torso");
        var fwd = MechWalker.HeadingVec(_player.TorsoYawWorld);
        var right = new Vector3(-fwd.Z, 0f, fwd.X);
        return torso + right * (left ? -2.3f : 2.3f) + Vector3.Up * 3.6f + fwd * 0.4f;
    }

    private void LaunchRocket(Vector3 aimPoint, bool left, WeaponDef def)
    {
        _rocketsLaunched++;
        var muzzle = PodMuzzle(left);
        var toAim = aimPoint - muzzle;
        // The lob is solved for the CAPPED range (see ConvergeMax):
        // direction AND time-of-flight use the same clamped point,
        // so the launch elevation tracks the pointer instead of the
        // hillside behind the target.
        float dist = Mathf.Min(Mathf.Max(toAim.Length(), 1f), ConvergeMax);
        var dir = ApplyInaccuracy(toAim.Normalized(), 1.1f);
        (float speed, float grav) = def.Id == "srm" ? (46f, 9f) : (36f, 11f);
        float tof = dist / speed;
        var vel = dir * speed + Vector3.Up * (0.5f * grav * tof);
        SpawnProjectile(muzzle, vel, grav, def.Damage, def.Splash, "PLAYER", rocket: true, weaponWeight: def.PerRocketWeight);
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
        MechWalker direct = hit.Target == "PLAYER" ? _player : hit.Target == "ENEMY" ? _enemy : null;
        if (direct != null) ApplyDamage(direct, p.Damage, hit, p.OwnerId, p.WeaponWeight, "rocket");
        foreach (var (w, id) in new[] { (_player, "PLAYER"), (_enemy, "ENEMY") })
        {
            if (w == direct) continue;
            float d = _hit.DistanceToMechSurface(hit.Point, id);
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
        if (p.SplashRadius > 0f)
        {
            SpawnExplosionFx(hit.Point); // rockets: mini-explosion
            SpawnFlash(hit.Point, new Color(1f, 0.62f, 0.25f), 1.7f, 0.3f);
            SpawnImpactLight(hit.Point, new Color(1f, 0.55f, 0.2f), 3.2f, 14f, 0.5f);
            SpawnBurst(hit.Point, FireParticleMat, 54, 1.15f, 5f, 10f, new Vector3(0f, -7f, 0f), 0.16f, 0.8f, 1.7f);
            SpawnBurst(hit.Point, SmokeParticleMat, 22, 2.6f, 1.4f, 3f, new Vector3(0f, 1.3f, 0f), 0.55f, 1.5f, 2.6f);
            ExplodeSplash(hit, p);
            if (hit.Target == "PROP") DamageProp(hit.Point, p.Damage);
            return;
        }
        SpawnFlash(hit.Point, new Color(1f, 0.72f, 0.35f), 0.62f, 0.18f);
        SpawnSparks(hit.Point, new Color(1f, 0.8f, 0.5f), 10, 5.2f);
        _fxSparkBursts--; // cannon impact sparks are not a laser burst
        SpawnBurst(hit.Point, FireParticleMat, 16, 0.7f, 4.5f, 8.5f, new Vector3(0f, -9f, 0f), 0.13f, 0.6f, 1.1f);
        SpawnBurst(hit.Point, SmokeParticleMat, 9, 1.6f, 1f, 2.2f, new Vector3(0f, 1.2f, 0f), 0.5f, 1.2f, 2f);
        SpawnImpactLight(hit.Point, new Color(1f, 0.75f, 0.4f), 2.6f, 11f, 0.35f);
        if (hit.Target == "PROP") DamageProp(hit.Point, p.Damage);
        // Cannon shells scorch what they hit.
        var nrm = p.Vel.LengthSquared() > 0.01f ? -p.Vel.Normalized() : Vector3.Up;
        if (hit.Target == "PLAYER") SpawnScorch(hit.Point, nrm, _player);
        else if (hit.Target == "ENEMY") SpawnScorch(hit.Point, nrm, _enemy);
        else if (hit.Hit) SpawnScorch(hit.Point, nrm, this);
        string kind = p.WeaponWeight >= 10f ? "cannon" : "mg";
        if (hit.Target == "PLAYER") ApplyDamage(_player, p.Damage, hit, p.OwnerId, p.WeaponWeight, kind);
        else if (hit.Target == "ENEMY") ApplyDamage(_enemy, p.Damage, hit, p.OwnerId, p.WeaponWeight, kind);
    }

    private void ApplyDamage(MechWalker target, float dmg, HitModel.Result hit, string source, float weaponWeight = 0f, string kind = "")
    {
        bool isPlayer = target == _player;
        if (isPlayer ? _playerDead : _enemyDead) return;
        var parts = isPlayer ? _partsPlayer : _partsEnemy;
        // Local damage routing: torso hits burn the main pool; head hits
        // burn the head part AND the pool; every other part absorbs its
        // own hits. Counters still record the nominal damage once per
        // hit event. Attribution: a box-ray hit already carries its part
        // (a box surface point is unambiguous, while an interior SDF
        // resolve can name a neighbor whose AABB overlaps the spot);
        // capsule points, splash and crafted hits resolve by nearest
        // part volume at the point.
        string partId = !string.IsNullOrEmpty(hit.Part)
            ? hit.Part
            : parts.AttributePoint(hit.Point);
        if (isPlayer) { _playerDmgEvents++; _playerDmgTotal += dmg; }
        else { _enemyDmgEvents++; _enemyDmgTotal += dmg; }
        float poolDmg = 0f;
        if (partId == "torso") poolDmg = dmg;
        else if (partId == "head") { poolDmg = dmg; parts.DamagePart("head", dmg); }
        else parts.DamagePart(partId, dmg);
        if (poolDmg > 0f)
        {
            if (isPlayer) _hpPlayer = Mathf.Max(_hpPlayer - poolDmg, 0f);
            else _hpEnemy = Mathf.Max(_hpEnemy - poolDmg, 0f);
        }
        float hp = isPlayer ? _hpPlayer : _hpEnemy;
        GD.Print($"WPN DMG target={(isPlayer ? "PLAYER" : "ENEMY")} dmg={dmg:F1} zone={hit.Zone} part={partId} poolDmg={poolDmg:F1} hp={hp:F1} src={source}");
        OnDamaged(target, dmg, hit.Point, source, kind);
        if (hp <= 0f) { Die(target); return; }
        // Knockdown (owner's rule, current): weapon impacts NEVER topple
        // a mech, however heavy. A mech falls only when it loses a leg
        // (see SyncLegConsequences), and then it stays down.
    }

    // A part just broke off (fired from MechParts.DamagePart): spark burst
    // at the separation point, HUD feed message, then the functional
    // consequences.
    private void OnPartBroken(MechParts parts, string partId, Vector3 center)
    {
        // During a death explosion every part breaks at once: no feed
        // spam, no per-part consequences (leg loss would re-enter Die).
        if (_exploding) return;
        SpawnFlash(center, new Color(1f, 0.62f, 0.22f), 0.95f, 0.16f);
        SpawnFlash(center, new Color(1f, 0.88f, 0.55f), 0.45f, 0.1f);
        var part = parts.GetPart(partId);
        if (parts == _partsPlayer) AddFeed($"WARNING: {part.Display} DESTROYED");
        else AddFeed($"ATLAS: {part.Display} DESTROYED");
        ApplyPartConsequences(parts, partId);
    }

    private void AddFeed(string text)
    {
        _feed.Add((text, 6f));
        while (_feed.Count > 5) _feed.RemoveAt(0);
        GD.Print($"BRK FEED {text}");
    }

    // Functional fallout of losing a part. Weapon mounts are gated live at
    // the fire/gunnery checks (arm_L -> laser, arm_R -> AC and the Atlas's
    // AC/20, pods -> salvo size, Atlas head -> spread + tracking lag), so
    // the stateful consequences latched here are the legs and the player's
    // head (HUD disturbance timer; the sway itself is applied in the HUD
    // step from part state).
    private void ApplyPartConsequences(MechParts parts, string partId)
    {
        if (partId == "leg_L" || partId == "leg_R")
            SyncLegConsequences(parts == _partsPlayer ? _player : _enemy, parts);
        else if (partId == "head" && parts == _partsPlayer)
            _hudFlickerT = 1.2f;
        if (parts == _partsPlayer) AutoSwitchWeapon();
    }

    private bool WeaponAlive(int sel) => sel switch
    {
        0 => _partsPlayer.Alive("arm_L") && _defEnergy != null,
        1 => _partsPlayer.Alive("arm_R") && _defBallistic != null,
        _ => (_partsPlayer.Alive("pod_L") && _defPodL != null) || (_partsPlayer.Alive("pod_R") && _defPodR != null),
    };

    // When the selected weapon is destroyed, fall to the next live one.
    private void AutoSwitchWeapon()
    {
        if (_partsPlayer == null || WeaponAlive(_weaponSel)) return;
        for (int i = 1; i <= 3; i++)
        {
            int cand = (_weaponSel + i) % 3;
            if (WeaponAlive(cand)) { _weaponSel = cand; return; }
        }
    }

    // Recompute a walker's leg state from its parts. Owner's rule
    // (current): a destroyed leg never falls off and never fells or
    // kills the mech — it stays attached, darkened and deformed, and
    // the mech LIMPS: one wrecked leg = 55% speed with a lean and the
    // wrecked leg keeps stepping at the throttled pace; both legs
    // wrecked = a 25% crawl.
    // Death comes only from the HP pool reaching zero.
    private void SyncLegConsequences(MechWalker w, MechParts parts)
    {
        bool lDead = !parts.Alive("leg_L");
        bool rDead = !parts.Alive("leg_R");
        w.SetLegDisabled(true, lDead);
        w.SetLegDisabled(false, rDead);
        if (lDead && rDead)
        {
            w.SpeedFactor = 0.25f;
            w.LimpRoll = 0f;
            GD.Print($"BRK {(w == _player ? "PLAYER" : "ENEMY")} both legs wrecked -> CRAWL (speedFactor 0.25)");
        }
        else if (lDead || rDead)
        {
            w.SpeedFactor = 0.55f;
            w.LimpRoll = lDead ? 0.10f : -0.10f;
            GD.Print($"BRK {(w == _player ? "PLAYER" : "ENEMY")} leg wrecked -> LIMP (speedFactor 0.55)");
        }
        else
        {
            w.SpeedFactor = 1f;
            w.LimpRoll = 0f;
        }
    }

    private void Die(MechWalker w)
    {
        PlaySfx(_sfxExplosion, w.RootPos + Vector3.Up * 4f, 3f, 0.8f);
        w.Halted = true;
        w.SetDrive(w.LegsYaw, 0f);
        if (w == _player) { _playerDead = true; GD.Print("WPN DEATH PLAYER"); }
        else { _enemyDead = true; GD.Print("WPN DEATH ENEMY"); }
        OnDeath(w);
    }

}
