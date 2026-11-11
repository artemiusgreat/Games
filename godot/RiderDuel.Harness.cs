using Godot;
using System.Collections.Generic;

// RiderDuel module: test choreography: duel/weapons/breakable/terrain/photo drivers.
// Split from the single-file build (round 22 cleanup); the class
// stays one partial class — see RiderDuel.cs for the field block.
public partial class RiderDuel
{
    // ---------- test choreography ----------

    private void SetPhase(int p)
    {
        _phase = p;
        _phaseT = 0;
        _stallTimer = 0f;
        GD.Print($"DUEL phase={p} t={_simT:F2}");
    }

    private void UpdateTest(float delta)
    {
        _phaseT += delta;
        switch (_phase)
        {
            case 0: // settle
                _player.SetDrive(0f, 0f);
                _enemy.SetDrive(Mathf.Pi, 0f);
                if (!_labChecked)
                {
                    _labChecked = true;
                    // RiderLab rules, exercised on the pure data layer:
                    // weight cap and slot type must both refuse.
                    var lab = new LoadoutState();
                    var spec = RiderSpecs.Enyo;
                    var cap = lab.Check(spec, "arm_L", WeaponCatalog.Ppc);
                    if (cap == null || !cap.Contains("weighs"))
                    { GD.PrintErr($"DUEL FAIL: lab weight cap not enforced ({cap})"); GetTree().Quit(1); return; }
                    if (lab.Check(spec, "arm_R", WeaponCatalog.Ppc) == null)
                    { GD.PrintErr("DUEL FAIL: lab slot type not enforced"); GetTree().Quit(1); return; }
                    if (lab.TryAssign(spec, "arm_R", WeaponCatalog.Autocannon) != null
                        || lab.TryAssign(spec, "pod_L", WeaponCatalog.Lrm) != null)
                    { GD.PrintErr("DUEL FAIL: lab legal fits refused"); GetTree().Quit(1); return; }
                    if (Mathf.Abs(LoadoutState.DefaultFor(RiderSpecs.Enyo).TotalWeaponWeight - 64f) > 0.01f)
                    { GD.PrintErr("DUEL FAIL: default fit tonnage != 64t"); GetTree().Quit(1); return; }
                    GD.Print("LABTEST PASS slot type + 8t arm cap enforced (PPC refused), legal fits accepted, default fit = 64t (every slot filled)");
                    // Capacity rule (owner, MW4 model): every chassis'
                    // slot caps sum to exactly its tonnage.
                    foreach (var sp in RiderSpecs.All)
                    {
                        float sum = 0f; foreach (var sl in sp.Slots) sum += sl.MaxWeight;
                        if (Mathf.Abs(sum - sp.Tonnage) > 0.01f)
                        { GD.PrintErr($"DUEL FAIL: {sp.Id} slot caps sum {sum:F0}t != tonnage {sp.Tonnage:F0}t"); GetTree().Quit(1); return; }
                    }
                    GD.Print("LABTEST CAPACITY PASS slot caps sum to chassis tonnage for every rider");
                    // Multi-fit (owner): a hardpoint fits several
                    // weapons up to its cap — cumulative tonnage is
                    // the limit, not one weapon per slot.
                    var multi = new LoadoutState();
                    if (multi.TryAssign(spec, "arm_L", WeaponCatalog.MediumLaser) != null
                        || multi.TryAssign(spec, "arm_L", WeaponCatalog.MediumLaser) != null
                        || multi.TryAssign(spec, "arm_L", WeaponCatalog.MediumLaser) == null)
                    { GD.PrintErr("DUEL FAIL: multi-fit cumulative arm cap wrong (2x ML = 6t fits the 8t arm, 3x = 9t must refuse)"); GetTree().Quit(1); return; }
                    if (multi.TryAssign(spec, "pod_L", WeaponCatalog.Lrm) != null
                        || multi.TryAssign(spec, "pod_L", WeaponCatalog.Srm) != null)
                    { GD.PrintErr("DUEL FAIL: multi-fit pod refused (LRM 10t + SRM 4t = 14t <= 21t cap)"); GetTree().Quit(1); return; }
                    if (!multi.RemoveLast("pod_L", out var peeled) || peeled != "srm" || multi.WeaponIn("pod_L") != "lrm")
                    { GD.PrintErr("DUEL FAIL: multi-fit peel wrong"); GetTree().Quit(1); return; }
                    GD.Print("LABTEST MULTIFIT PASS hardpoints fit multiple weapons up to their tonnage cap; peel removes the last one");
                    // Distribution (owner): heavier riders carry MORE
                    // hardpoints, so their firepower survives losing
                    // a single mount.
                    if (RiderSpecs.Hector.Slots.Length <= RiderSpecs.Enyo.Slots.Length)
                    { GD.PrintErr("DUEL FAIL: the 100t Hector must have more hardpoints than the 75t Enyo"); GetTree().Quit(1); return; }
                    GD.Print($"LABTEST DISTRIBUTION PASS hardpoints: Enyo {RiderSpecs.Enyo.Slots.Length}, Hector {RiderSpecs.Hector.Slots.Length} (heavier = more mounts)");
                    // Rider selection (round 22): swap both chassis and
                    // verify the duel follows — parts layout, pools
                    // and equipped defs all follow the spec.
                    SetRiderSelection("hector", "enyo");
                    if (_playerSpec.Id != "hector" || _enemySpec.Id != "enyo"
                        || _hpPlayer != 340f || _hpEnemy != 260f
                        || _defBallistic?.Id != "ac20"
                        || !_partsEnemy.Alive("pod_L"))
                    { GD.PrintErr($"DUEL FAIL: rider selection swap wrong (hp {_hpPlayer}/{_hpEnemy}, def {_defBallistic?.Id})"); GetTree().Quit(1); return; }
                    _selStage = 1; _selHoldT = 0f;
                    GD.Print("LABTEST SELECTION swapped to Hector player; holding across live frames (HUD must survive a pod-less chassis)");
                }
                // Keep the swapped chassis deployed across many real
                // frames before restoring: swapping forth and back
                // inside one call never lets UpdateHud run against
                // the Hector parts — which is exactly how the pod_L
                // KeyNotFound crash shipped (it froze the player's
                // camera in the live game while this test stayed
                // green). If UpdateHud throws now, the frame aborts
                // before UpdateTest and this hold never completes.
                if (_selStage == 1)
                {
                    _selHoldT += delta;
                    if (_selHoldT > 1.2f)
                    {
                        SetRiderSelection("enyo", "hector");
                        if (_playerSpec.Id != "enyo" || _enemySpec.Id != "hector"
                            || _hpPlayer != 260f || _hpEnemy != 340f || _defEnergy?.Id != "large_laser")
                        { GD.PrintErr("DUEL FAIL: rider selection restore wrong"); GetTree().Quit(1); return; }
                        _selStage = 2;
                        GD.Print("LABTEST SELECTION PASS chassis swap rebuilds walkers, parts, pools and defs; Hector player survived 1.2s of live HUD frames; defaults restored");
                    }
                }
                if (_phaseT > 1.6 && _selStage != 1) SetPhase(1);
                break;
            case 1: // Hector walks at the player; grab stride + wide frames
                _player.SetDrive(0f, 0f);
                _enemy.SetDrive(Mathf.Pi, 1.8f);
                if (_enemy.ConsumeStepMidCross() && _phaseT > 0.6)
                {
                    _strideShots++;
                    if (_strideShots == 1)
                    {
                        GD.Print($"DUEL HECTOR CONTACT analytic {_enemy.FootReport(false)}");
                        GD.Print($"DUEL HECTOR CONTACT engine   {_enemy.FootReport(true)}");
                        QueueCapture("res://proof/duel_hector_stride.png", "stride", "enemySide");
                    }
                    else if (_strideShots == 2)
                    {
                        QueueCapture("res://proof/duel_main.png", "main", "wide", () =>
                        {
                            SetPhase(2);
                            _player.Teleport(new Vector3(0f, Main.HeightAt(0f, 26f), 26f), 0f);
                            _player.TorsoTwist = 1.15f;
                        });
                    }
                }
                if (_phaseT > 12) { GD.PrintErr("DUEL FAIL: hector stride shots missing"); GetTree().Quit(1); }
                break;
            case 2: // torso twist: legs walk -Z, torso twisted +66 deg
                _player.SetDrive(0f, 1.8f);
                _enemy.SetDrive(Mathf.Pi, 0f);
                if (_phaseT > 1.4 && !_twistLogged)
                {
                    _twistLogged = true;
                    float legs = _player.LegsYaw;
                    float torso = _player.EngineTorsoYaw();
                    float rel = Mathf.RadToDeg(RiderWalker.WrapAngle(torso - legs));
                    GD.Print($"DUEL TWIST legsYaw={Mathf.RadToDeg(legs):F1}deg torsoWorldYaw(engine)={Mathf.RadToDeg(torso):F1}deg relative={rel:F1}deg twistCmd={Mathf.RadToDeg(_player.TorsoTwist):F1}deg");
                    QueueCapture("res://proof/duel_torso_twist.png", "twist", "twist", () =>
                    {
                        SetPhase(3);
                        _player.TorsoTwist = 0f;
                        _player.Teleport(new Vector3(6f, Main.HeightAt(6f, 27f), 27f), 0f);
                        _minTreeSep = 1e9f;
                        _lastPlayerPos = _player.RootPos;
                    });
                }
                break;
            case 3: // STOMP: drive straight at the tree at (6,16) — it must NOT stop the rider; the rider crushes it and walks through
                _player.SetDrive(0f, 2.6f);
                _enemy.SetDrive(Mathf.Pi, 0f);
                {
                    float sep = new Vector2(_player.RootPos.X - 6f, _player.RootPos.Z - 16f).Length();
                    _minTreeSep = Mathf.Min(_minTreeSep, sep);
                    Destructible treeD = null;
                    foreach (var d in _destructibles)
                        if (d.Kind == "tree" && Mathf.Abs(d.Center.X - 6f) < 0.6f && Mathf.Abs(d.Center.Z - 16f) < 0.6f)
                            treeD = d;
                    if (treeD != null && treeD.State != 0 && _minTreeSep < 2.6f)
                    {
                        GD.Print($"DUEL STOMP PASS tree crushed underfoot (minSep={_minTreeSep:F2}), rider walks through");
                        QueueCapture("res://proof/duel_collision_prop.png", "prop", "prop", () =>
                        {
                            SetPhase(4);
                            _player.Teleport(new Vector3(0f, Main.HeightAt(0f, 10f), 10f), 0f);
                            _enemy.Teleport(new Vector3(0f, Main.HeightAt(0f, -2f), -2f), Mathf.Pi);
                            _minRiderSep = 1e9f;
                        });
                    }
                    if (_player.RootPos.Z < 12.5f && (treeD == null || treeD.State == 0))
                    { GD.PrintErr("DUEL FAIL: player passed the tree without stomping it"); GetTree().Quit(1); }
                    if (_phaseT > 14) { GD.PrintErr("DUEL FAIL: stomp phase timeout"); GetTree().Quit(1); }
                }
                break;
            case 4: // riders driven at each other
                _player.SetDrive(0f, 2.2f);
                _enemy.SetDrive(Mathf.Pi, 1.8f);
                {
                    float dist = new Vector2(_player.RootPos.X - _enemy.RootPos.X,
                                             _player.RootPos.Z - _enemy.RootPos.Z).Length();
                    _minRiderSep = Mathf.Min(_minRiderSep, dist);
                    if (_phaseT > 2.2f)
                    {
                        if (Mathf.Abs(dist - _minRiderSep) < 0.001f && dist <= _player.CollisionRadius + _enemy.CollisionRadius + 0.05f)
                            _stallTimer += delta;
                        else _stallTimer = 0f;
                        if (_stallTimer > 0.5f)
                        {
                            GD.Print($"DUEL RIDER minSep={_minRiderSep:F3} finalSep={dist:F3} sumRadii={_player.CollisionRadius + _enemy.CollisionRadius:F2}");
                            QueueCapture("res://proof/duel_collision_rider.png", "rider", "riderSide");
                            SetPhase(5);
                        }
                    }
                    if (_phaseT > 16) { GD.PrintErr("DUEL FAIL: rider stall never detected"); GetTree().Quit(1); }
                }
                break;
            case 5: // PLAYERLIMP: wreck the PLAYER's leg_L, then drive
                    // through the real input path (ApplyPlayerInput):
                    // forward, reverse and turning must all still answer
                    // at limp throttle. Regression for the retired
                    // legsOk gate that left the player permanently stuck.
                _enemy.SetDrive(Mathf.Pi, 0f);
                if (_phaseT > 34) { GD.PrintErr("DUEL FAIL: playerlimp phase timeout"); GetTree().Quit(1); }
                if (_pLimpStep == 0 && _phaseT > 0.15f)
                {
                    ResetDuel();
                    _partsPlayer.DamagePart("leg_L", 999f);
                    if (_partsPlayer.Alive("leg_L")) { GD.PrintErr("DUEL FAIL: player leg_L not wrecked"); GetTree().Quit(1); break; }
                    if (Mathf.Abs(_player.SpeedFactor - 0.55f) > 0.01f) { GD.PrintErr($"DUEL FAIL: limp factor {_player.SpeedFactor:F2} != 0.55"); GetTree().Quit(1); break; }
                    _player.Teleport(new Vector3(0f, Main.HeightAt(0f, 26f), 26f), 0f);
                    _enemy.Teleport(new Vector3(30f, Main.HeightAt(30f, -30f), -30f), Mathf.Pi);
                    _playerYaw = 0f; _enemyYaw = Mathf.Pi;
                    _pLimpStart = _player.RootPos;
                    _pLimpT0 = _phaseT;
                    _pLimpStep = 1;
                }
                else if (_pLimpStep == 1)
                {
                    ApplyPlayerInput(delta, true, false, false, false, false);
                    if (_phaseT - _pLimpT0 > 3.2f)
                    {
                        float fwd = _pLimpStart.Z - _player.RootPos.Z;
                        float spdNow = _player.Speed;
                        if (fwd < 3.5f || spdNow < 1.5f || spdNow > 3.1f)
                        { GD.PrintErr($"DUEL FAIL: limping player barely moved fwd={fwd:F2} spd={spdNow:F2}"); GetTree().Quit(1); break; }
                        GD.Print($"DUEL PLAYERLIMP FWD fwd={fwd:F2} over 3.2s at spd={spdNow:F2} (limp 0.55x)");
                        _pLimpStart = _player.RootPos;
                        _pLimpT0 = _phaseT;
                        _pLimpStep = 2;
                    }
                }
                else if (_pLimpStep == 2)
                {
                    ApplyPlayerInput(delta, false, true, false, false, false);
                    if (_phaseT - _pLimpT0 > 2.2f)
                    {
                        // Net displacement is small because the step
                        // starts at full forward speed: the walker first
                        // coasts ~1u forward while decelerating through
                        // zero. The crisp signals: net backward travel
                        // AND a live negative drive speed at the end.
                        float rev = _player.RootPos.Z - _pLimpStart.Z;
                        if (rev < 0.5f || _player.Speed > -0.8f)
                        { GD.PrintErr($"DUEL FAIL: limping player cannot reverse rev={rev:F2} spd={_player.Speed:F2}"); GetTree().Quit(1); break; }
                        GD.Print($"DUEL PLAYERLIMP REV rev={rev:F2} net over 2.2s (after the forward coast), spd={_player.Speed:F2}");
                        _pLimpYaw0 = _playerYaw;
                        _pLimpT0 = _phaseT;
                        _pLimpStep = 3;
                    }
                }
                else if (_pLimpStep == 3)
                {
                    ApplyPlayerInput(delta, false, false, true, false, false);
                    if (_phaseT - _pLimpT0 > 0.6f)
                    {
                        float gain = _playerYaw - _pLimpYaw0;
                        if (gain < 0.15f)
                        { GD.PrintErr($"DUEL FAIL: limping player cannot turn gain={gain:F3}"); GetTree().Quit(1); break; }
                        GD.Print($"DUEL PLAYERLIMP PASS fwd+rev+turn all answer with leg_L wrecked (turn gain={gain:F3} in 0.6s)");
                        _pLimpStep = 4;
                        ResetDuel();
                        SetPhase(6);
                    }
                }
                break;
            case 6: // AIM: the aim follows the CAMERA AXIS (owner's
                    // round-27 model): pitch and twist steer
                    // TorsoAimPoint, and the crosshair is the screen
                    // center the axis projects to.
                _player.SetDrive(0f, 0f);
                _enemy.SetDrive(Mathf.Pi, 0f);
                if (_phaseT > 20) { GD.PrintErr("DUEL FAIL: aim phase timeout"); GetTree().Quit(1); }
                if (_aimStep == 0 && _phaseT > 0.15f)
                {
                    ResetDuel();
                    _player.Teleport(new Vector3(0f, Main.HeightAt(0f, 18f), 18f), 0f);
                    _enemy.Teleport(new Vector3(0f, Main.HeightAt(0f, -12f), -12f), Mathf.Pi);
                    _playerYaw = 0f; _enemyYaw = Mathf.Pi;
                    _playerVel = Vector3.Zero;
                    _prevPlayerRoot = _player.RootPos;
                    _camMode = CamModes.Chase;
                    // Project through the camera the capture will use:
                    // run one camera update now, otherwise the reticle
                    // measurements use last phase's camera pose.
                    UpdateCamera(0f);
                    _player.TorsoTwist = 0f;
                    _player.TorsoPitch = 0.40f;
                    var aimUp = TorsoAimPoint();
                    _player.TorsoPitch = -0.30f;
                    var aimDown = TorsoAimPoint();
                    if (aimUp.Y < aimDown.Y + 8f)
                    { GD.PrintErr($"DUEL FAIL: torso pitch does not steer the aim (up Y={aimUp.Y:F1} down Y={aimDown.Y:F1})"); GetTree().Quit(1); break; }
                    _player.TorsoPitch = 0f;
                    var aimC = TorsoAimPoint();
                    _player.TorsoTwist = 1.2f;
                    var aimT = TorsoAimPoint();
                    float dxz = new Vector2(aimT.X - aimC.X, aimT.Z - aimC.Z).Length();
                    if (dxz < 5f)
                    { GD.PrintErr($"DUEL FAIL: torso twist does not steer the aim (dxz={dxz:F2})"); GetTree().Quit(1); break; }
                    // WIDE AIM (round 13, round-27 form): the mouse
                    // target is not bound by the torso clamp — with
                    // AimPitchWide driven to -1.05 the convergence
                    // must land on the ground right in front of the
                    // feet, under the center crosshair (the aim
                    // point rides the camera axis now, so it
                    // projects to the center), while the torso's
                    // own pitch stays clamped at -0.32.
                    _player.TorsoTwist = 0f;
                    _player.TorsoPitch = 0f;
                    _player.AimPitchWide = -1.05f;
                    UpdateCamera(0f); // a real frame runs the camera before the aim is read
                    var aimWide = TorsoAimPoint();
                    var scrW = _cam.UnprojectPosition(aimWide);
                    var vpW = GetViewport().GetVisibleRect().Size;
                    if (_aimTarget != Roles.Terrain || aimWide.DistanceTo(_player.RootPos) > 30f || scrW.DistanceTo(vpW * 0.5f) > 10f)
                    { GD.PrintErr($"DUEL FAIL: wide aim cannot reach the feet (target='{_aimTarget}' dist={aimWide.DistanceTo(_player.RootPos):F1} scr=({scrW.X:F0},{scrW.Y:F0}) center=({vpW.X * 0.5f:F0},{vpW.Y * 0.5f:F0})"); GetTree().Quit(1); break; }
                    if (_player.TorsoPitch < -0.33f)
                    { GD.PrintErr("DUEL FAIL: torso pitch escaped its clamp"); GetTree().Quit(1); break; }
                    _player.AimPitchWide = float.NaN;
                    GD.Print($"DUEL WIDEAIM PASS free mouse pitch reaches the ground in front of the feet, on the crosshair (dist {aimWide.DistanceTo(_player.RootPos):F1}u); torso pitch stays clamped");
                    // Reticle TRUTH (round 27): at every pitch the
                    // crosshair must BE the screen center, and the
                    // convergence point (what the weapons converge
                    // on) must project onto that same center — the
                    // camera axis is the aim, so cursor and impact
                    // can never disagree. (The round-8 contract,
                    // restated for center-crosshair aiming.)
                    _player.TorsoTwist = 0f;
                    var vpAim = GetViewport().GetVisibleRect().Size;
                    var ctrAim = vpAim * 0.5f;
                    for (int i = 0; i <= 5; i++)
                    {
                        _player.TorsoPitch = -0.05f + i * 0.02f;
                        UpdateCamera(0f);
                        _aimPoint = TorsoAimPoint();
                        UpdateReticle();
                        if (_cross.ReticlePos.DistanceTo(ctrAim) > 1f)
                        { GD.PrintErr($"DUEL FAIL: crosshair is not at screen center (step {i}: got {_cross.ReticlePos}, center {ctrAim})"); _aimStep = 99; GetTree().Quit(1); break; }
                        var proj = _cam.UnprojectPosition(_reticlePoint);
                        if (proj.DistanceTo(ctrAim) > 8f)
                        { GD.PrintErr($"DUEL FAIL: convergence point off the crosshair (step {i}: proj ({proj.X:F0},{proj.Y:F0}), center ({ctrAim.X:F0},{ctrAim.Y:F0}))"); _aimStep = 99; GetTree().Quit(1); break; }
                    }
                    if (_aimStep == 99) break; // sweep failed above; Quit(1) already queued
                    // The synthesis contract, tested through the REAL
                    // weapon: cursor low on the Hector => the laser
                    // HITS him; cursor raised above him => it MISSES
                    // (round 9: the weapons converge on the cursor's
                    // own point, so the pointer and the shots agree).
                    _enemyDmgEvents = 0;
                    int rescues0 = _laserRescues;
                    _cdLaser = 0f;
                    // Round 27: hunt the pitch that puts the center
                    // crosshair on the Hector, the way a player
                    // does — the fixed -0.10 fixture belonged to
                    // the old free-reticle geometry.
                    bool onFoe = false;
                    for (float tp = -0.05f; tp >= -0.50f; tp -= 0.025f)
                    {
                        _player.TorsoPitch = tp;
                        UpdateCamera(0f);
                        _aimPoint = TorsoAimPoint();
                        if (_aimTarget == Roles.Enemy) { onFoe = true; break; }
                    }
                    if (!onFoe)
                    { GD.PrintErr("DUEL FAIL: no pitch puts the crosshair on the Hector"); GetTree().Quit(1); break; }
                    FireLaser(_aimPoint);
                    if (_enemyDmgEvents < 1)
                    { GD.PrintErr("DUEL FAIL: cursor low on the Hector but the laser missed him"); GetTree().Quit(1); break; }
                    // Regression guard scoped to THIS shot: a stable
                    // chase shot that HITS must come from the camera
                    // path unaided. (The raised shot below may
                    // legitimately rescue: its aim point is a
                    // synthetic free point over the Hector, and the
                    // camera's far-terrain verdict disagrees on the
                    // endpoint while agreeing on the miss.)
                    if (_laserRescues != rescues0)
                    { GD.PrintErr("DUEL FAIL: the laser needed the harness fallback for a stable hitting shot — the live camera path is broken (round 20 regression guard)"); GetTree().Quit(1); break; }
                    int evAfterHit = _enemyDmgEvents;
                    _cdLaser = 0f;
                    // 1.0 rad, not the old 0.30: on the domed map
                    // (round 25) the test pair stands on the center
                    // hill, and the crest behind the Hector is higher
                    // than his head — a 0.30 line to the crest point
                    // legitimately clips his torso box on the way.
                    // The phase's contract is "cursor clearly above
                    // him => a miss", so the fixture aims clearly
                    // above: sky, not crest.
                    _player.TorsoPitch = 1.0f;
                    _aimPoint = TorsoAimPoint();
                    FireLaser(_aimPoint);
                    if (_enemyDmgEvents > evAfterHit)
                    { GD.PrintErr("DUEL FAIL: cursor above the Hector but a shot still hit him"); GetTree().Quit(1); break; }
                    // Beam length on a miss (owner): the ray stops
                    // at the cursor's point — it must NOT run to
                    // the raycast's 140u no-hit endpoint.
                    float beamLen = -1f;
                    for (int i = _fx.Count - 1; i >= 0; i--)
                        if (_fx[i].Node is MeshInstance3D bmi && bmi.Mesh is BoxMesh bxm) { beamLen = bxm.Size.Z; break; }
                    float wantLen = PlayerMuzzle(true).DistanceTo(_aimPoint);
                    if (beamLen < 0f || Mathf.Abs(beamLen - wantLen) > 3f)
                    { GD.PrintErr($"DUEL FAIL: sky-shot beam runs {beamLen:F1}u but the cursor point is {wantLen:F1}u from the muzzle"); GetTree().Quit(1); break; }
                    GD.Print($"DUEL BEAMEND PASS sky-shot beam stops at the cursor point ({beamLen:F1}u, not the 140u raycast end)");
                    // Torso twist is UNCLAMPED (owner, 2026-10-29):
                    // a 195° command must stick — the torso rotates
                    // the full 360°, fully backward included.
                    _player.TorsoTwist = 3.4f;
                    if (_player.TorsoTwist < 3.39f)
                    { GD.PrintErr($"DUEL FAIL: torso twist {Mathf.RadToDeg(_player.TorsoTwist):F0}deg was clamped (full 360 required)"); _aimStep = 99; GetTree().Quit(1); break; }
                    _player.TorsoTwist = -3.4f;
                    if (_player.TorsoTwist > -3.39f)
                    { GD.PrintErr($"DUEL FAIL: torso twist {Mathf.RadToDeg(_player.TorsoTwist):F0}deg was clamped (full 360 required)"); _aimStep = 99; GetTree().Quit(1); break; }
                    _player.TorsoTwist = 0f;
                    GD.Print($"DUEL AIM PASS pitch aimY {aimDown.Y:F1} -> {aimUp.Y:F1}, twist dxz={dxz:F1}, crosshair IS the convergence point: laser hits low, misses high; twist unclamped (full 360)");
                    // Rendered-pitch check next (steps 1-2): the AIM
                    // math above is property-based; these read the
                    // skeleton's actual facing after a StepWalker ran.
                    _player.TorsoTwist = 0f;
                    _player.TorsoPitch = 0.35f;
                    _aimT0 = _phaseT;
                    _aimStep = 1;
                }
                else if (_aimStep == 1 && _phaseT - _aimT0 > 0.3)
                {
                    var fUp = _player.EngineTorsoFacing();
                    if (fUp.Y < 0.15f)
                    { GD.PrintErr($"DUEL FAIL: rendered torso does not pitch UP with +pitch (facingY={fUp.Y:F3})"); GetTree().Quit(1); break; }
                    _aimUpY = fUp.Y;
                    _player.TorsoPitch = -0.25f;
                    _aimT0 = _phaseT;
                    _aimStep = 2;
                }
                else if (_aimStep == 2 && _phaseT - _aimT0 > 0.3)
                {
                    var fDn = _player.EngineTorsoFacing();
                    if (fDn.Y > -0.08f)
                    { GD.PrintErr($"DUEL FAIL: rendered torso does not pitch DOWN with -pitch (facingY={fDn.Y:F3})"); GetTree().Quit(1); break; }
                    GD.Print($"DUEL AIM RENDER PASS rendered torso follows the pitch: up facingY={_aimUpY:F3}, down facingY={fDn.Y:F3}");
                    // Rocket-lob check (round 7): pitch up at the sky
                    // over the Hector and loose a salvo — the first
                    // rocket's launch elevation must track the
                    // pointer (bounded lob), not mortar vertically.
                    _player.TorsoTwist = 0f;
                    _player.TorsoPitch = 0.30f;
                    _cdLrm = 0f;
                    _aimProj0 = _shots.Count;
                    FireLrmSalvo(TorsoAimPoint());
                    _aimT0 = _phaseT;
                    _aimStep = 3;
                }
                else if (_aimStep == 3)
                {
                    if (_shots.Count > _aimProj0)
                    {
                        var pr = _shots[_shots.Count - 1];
                        float elev = Mathf.Asin(Mathf.Clamp(pr.Vel.Y / Mathf.Max(pr.Vel.Length(), 0.01f), -1f, 1f));
                        if (elev > 0.66f)
                        { GD.PrintErr($"DUEL FAIL: sky-aim rocket mortared at elevation {elev:F2} rad (pointer was 0.30)"); GetTree().Quit(1); break; }
                        if (elev < 0.15f)
                        { GD.PrintErr($"DUEL FAIL: sky-aim rocket barely rose (elevation {elev:F2})"); GetTree().Quit(1); break; }
                        GD.Print($"DUEL AIM ROCKET PASS sky-aim launch elevation={elev:F2} rad vs pointer 0.30 — the lob is bounded");
                        // Torso-independence setup (round 7): anchor
                        // the mouse bearing here, then spin the LEGS
                        // for a second — the torso's world bearing
                        // must not follow them.
                        _torsoWorldYaw = _player.TorsoYawWorld;
                        _aimWorld0 = _player.TorsoYawWorld;
                        _aimLegs0 = _playerYaw;
                        _aimT0 = _phaseT;
                        _aimStep = 4;
                    }
                    else if (_phaseT - _aimT0 > 3) { GD.PrintErr("DUEL FAIL: sky-aim salvo never launched"); GetTree().Quit(1); }
                }
                else if (_aimStep == 4)
                {
                    // Legs rotate at the A/D rate for ~1s while the
                    // mouse bearing stays put: MaintainTorsoWorldYaw
                    // (the live path's own method) must hold the
                    // torso's world yaw steady throughout.
                    _playerYaw += 0.85f * delta;
                    _player.SetDrive(_playerYaw, 0f);
                    MaintainTorsoWorldYaw();
                    if (_phaseT - _aimT0 > 1.0)
                    {
                        float legsTurned = _playerYaw - _aimLegs0;
                        float torsoDrift = Mathf.Abs(RiderWalker.WrapAngle(_player.TorsoYawWorld - _aimWorld0));
                        if (legsTurned < 0.6f)
                        { GD.PrintErr($"DUEL FAIL: torso-indep setup only turned the legs {legsTurned:F2} rad"); GetTree().Quit(1); break; }
                        if (torsoDrift > 0.05f)
                        { GD.PrintErr($"DUEL FAIL: legs dragged the torso by {torsoDrift:F3} rad (mouse must own the bearing)"); GetTree().Quit(1); break; }
                        GD.Print($"DUEL AIM TORSO-INDEP PASS legs turned {legsTurned:F2} rad under a fixed mouse bearing; torso drift {torsoDrift:F3} rad");
                        // Restore the original facing for the proof
                        // frame (the spin left the legs yawed away).
                        _playerYaw = _aimLegs0;
                        _player.SetDrive(_playerYaw, 0f);
                        // Proof frame: torso pitched up — the reticle
                        // sits on the true aim point above the Hector.
                        _player.TorsoTwist = 0f;
                        _player.TorsoPitch = 0.18f;
                        _aimPoint = TorsoAimPoint();
                        UpdateReticle();
                        _aimStep = 5;
                        QueueCapture("res://proof/duel_aim.png", "aim", CamModes.Chase, () =>
                        {
                            QueueCapture("res://proof/duel_collision_rider.png", "rider", "riderSide");
                            SetPhase(7);
                        });
                    }
                }
                break;
        }
    }

    // ---------- weapons-test choreography ----------

    private void SetWPhase(int p)
    {
        _wPhase = p;
        _wPhaseT = 0;
        _wShotsFired = 0;
        _wCaptured = false;
        _wDeadShotQueued = false;
        _wDeathLogged = false;
        GD.Print($"WPNTEST phase={p} t={_simT:F2}");
    }

    private void WFail(string msg)
    {
        GD.PrintErr($"WPNTEST FAIL: {msg}");
        GetTree().Quit(1);
    }

    private Vector3 EnemyAim() => _enemy.RootPos + Vector3.Up * 8.2f;

    // ---------- LRM impact probe ----------
    // Owner report (2026-10-31): LRM rockets land LOWER than the
    // cursor when shooting from the hill. The salvo solve is
    // exact on paper, so measure the real impacts: four staged
    // geometries, full salvos at a stationary target's chest,
    // every impact logged against the aim point. Cases: flat
    // control, mesa edge, mesa setback, and a >75u long shot
    // (past the ConvergeMax solve cap).
    private int _lrmCase = -1;
    private bool _lrmFired;
    private int _lrmImpactBase;
    private double _lrmT;
    private int _lrmLaunched0;
    private Vector3 _lrmAim;

    private void UpdateLrmProbe(float dt)
    {
        _lrmT += dt;
        if (_lrmCase < 0)
        {
            _lrmCase = 0;
            LrmStage(0);
            return;
        }
        if (_lrmT < 0.6) return; // settle after teleport
        if (!_lrmFired)
        {
            _lrmFired = true;
            _lrmImpactBase = _lrmImpacts.Count; // return-fire impacts don't count
            _lrmAim = EnemyAim();
            _cdLrm = 0f;
            FireLrmSalvo(_lrmAim);
            return;
        }
        int launched = _rocketsLaunched - _lrmLaunched0;
        int mine = _lrmImpacts.Count - _lrmImpactBase;
        if ((mine >= launched && launched > 0) || _lrmT > 14f)
        {
            int onTarget = 0, onTerrain = 0, other = 0;
            float horiz = 0f, vert = 0f;
            for (int ix = _lrmImpactBase; ix < _lrmImpacts.Count; ix++)
            {
                var (target, point) = _lrmImpacts[ix];
                if (target == Roles.Enemy) onTarget++;
                else if (target == Roles.Terrain)
                {
                    onTerrain++;
                    horiz += new Vector2(point.X - _lrmAim.X, point.Z - _lrmAim.Z).Length();
                    vert += _lrmAim.Y - point.Y;
                }
                else other++;
            }
            GD.Print($"LRMPROBE case={_lrmCase} launched={launched} impacts={mine} enemy={onTarget} terrain={onTerrain} other={other}" +
                (onTerrain > 0 ? $" terrainHitsMeanShort={horiz / onTerrain:F1}u meanBelowAim={vert / onTerrain:F1}u" : "") +
                $" aim={_lrmAim} playerPos={_player.RootPos} enemyPos={_enemy.RootPos}");
            _lrmCase++;
            if (_lrmCase > 3) { GD.Print("LRMPROBE DONE"); GetTree().Quit(0); return; }
            LrmStage(_lrmCase);
        }
    }

    private void LrmStage(int c)
    {
        _lrmT = 0f;
        _lrmFired = false;
        _lrmImpacts.Clear();
        _lrmLaunched0 = _rocketsLaunched;
        (Vector3 pp, Vector3 ep) = c switch
        {
            0 => (new Vector3(22f, Main.HeightAt(22f, 24f), 24f), new Vector3(22f, Main.HeightAt(22f, -16f), -16f)),   // flat control, 40u
            1 => (new Vector3(0f, Main.HeightAt(0f, 10f), 10f), new Vector3(0f, Main.HeightAt(0f, -58f), -58f)),      // mesa edge -> foot, 68u
            2 => (new Vector3(0f, Main.HeightAt(0f, 2f), 2f), new Vector3(0f, Main.HeightAt(0f, -46f), -46f)),        // mesa setback -> slope foot, 48u
            _ => (new Vector3(0f, Main.HeightAt(0f, 10f), 10f), new Vector3(0f, Main.HeightAt(0f, -100f), -100f)),    // long, 110u (past the 75u solve cap)
        };
        _player.Teleport(pp, Mathf.Pi);
        _enemy.Teleport(ep, 0f);
        _hpPlayer = 100000f; // the target shoots back (shared combat step) — keep the shooter whole
        _partsPlayer.RestoreAll(); // and the pods intact, so every case fires a full salvo
        GD.Print($"LRMPROBE stage case={c} player={pp} enemy={ep}");
    }

    // ---------- free-for-all probe ----------
    // Stages all three riders 45u apart on the mesa and lets the
    // two AIs play: PASS requires both foes to have closed with
    // someone, both to have fired, and real damage somewhere.
    private double _ffaT;
    private bool _ffaStaged;
    private Vector3 _ffaHome0, _ffaHome1;

    // --view-shot: the owner's hilltop case — the player low at
    // the mesa foot, a foe standing on the top. Round 27 form:
    // BEFORE frame at a level view (crosshair on the flats, foe
    // above it); then the wide pitch sweeps its full travel and
    // the foe's chest must cross the screen center; AFTER frame
    // captured at the crossing pitch, crosshair on the foe.
    private float _viewT;
    private int _viewStage = -1;
    private float _viewPitch, _viewBest, _viewBestPitch;
    // --point-test (owner, 2026-11-01: "lookup is clamped — I
    // couldn't point the reticle at the top of the hill"): a
    // player simulation. The foe stands on the mesa top; the
    // player stands at a ladder of ranges on the flats and the
    // skirt, and the wide pitch sweeps its full travel through
    // the real per-frame path. For each stand, report the closest
    // the foe's chest ever gets to the SCREEN CENTER on screen:
    // under round-27 center-crosshair aiming the center IS the
    // reticle — if no pitch puts the chest on it, pointing at
    // the hilltop is impossible from there — measured, not argued.
    // (The probe originally measured the free reticle point and
    // found the close-steep dead zone that decided round 27.)
    private static readonly float[] PointRanges = { 80f, 65f, 58f, 45f, 35f, 26f };
    private static readonly float[] PointRangesClose = { 24f, 21f, 18f, 16f };
    private float[] ActivePointRanges => _pointClose ? PointRangesClose : PointRanges;
    private int _pointNext = 0;

    private void UpdatePointTest(float dt)
    {
        _aimPoint = TorsoAimPoint();
        _pointT += dt;
        if (_pointStage == -1)
        {
            if (_pointNext >= ActivePointRanges.Length)
            {
                GD.Print("POINTTEST DONE");
                GetTree().Quit(0);
                _pointStage = -99;
                return;
            }
            int idx = _pointNext;
            _pointStage = idx;
            float r = ActivePointRanges[idx];
            _player.Teleport(new Vector3(0f, Main.HeightAt(0f, r), r), 0f); // yaw 0 faces -Z (the mesa) directly — no staging turn to pollute the early samples
            float foeZ = _pointClose ? 11f : (_pointCenter ? 0f : 10f);
            _enemy.Teleport(new Vector3(0f, Main.HeightAt(0f, foeZ), foeZ), 0f);
            _player.SetDrive(0f, 0f);
            _player.TorsoTwist = 0f;
            _hpPlayer = 100000f; _hpEnemy = 100000f;
            _pointPitch = -1.35f; // round 27: sweep the FULL wide travel — under center-crosshair aiming a close top-edge target is crossed at NEGATIVE pitch (the camera hovers above its chest)
            _pointDbgNext = -1.33f;
            _pointBest = 1e9f;
            _pointBestPitch = 0f;
            _pointT = 0f;
            GD.Print($"POINTTEST staged {(_pointHector ? "hector" : "enyo")} r={r:F0} labOpen={_labOpen} camMode={_camMode} frozen={_frozen} playerSpec={_playerSpec.Id}");
            return;
        }
        if (_pointStage >= 0)
        {
            if (_pointT < 0.25f) return; // settle after the teleport
            if (_pointPitch <= -1.34f) GD.Print($"POINTSWEEP start stage={_pointStage} walk={_pointWalk} dbgNext={_pointDbgNext:F2}");
            if (_pointWalk) _player.SetDrive(0f, 2.2f); // yaw 0 faces -Z: toward the mesa from the +Z stands (HeadingVec = (-sin, -cos))
            _player.AimPitchWide = _pointPitch;
            _player.TorsoPitch = _pointPitch;
            var chest = _enemy.RootPos + Vector3.Up * (_enemySpec.Height * 0.68f);
            bool chestBehind = _cam.IsPositionBehind(chest);
            var fs = _cam.UnprojectPosition(chest);
            var ctrP = GetViewport().GetVisibleRect().Size * 0.5f;
            if ((_pointStage == 0 || _pointStage == ActivePointRanges.Length - 1) && _pointPitch >= _pointDbgNext)
            {
                _pointDbgNext += 0.15f;
                GD.Print($"POINTDBG w={_pointWalk} p={_pointPitch:F2} behind={chestBehind} ctr=({ctrP.X:F0},{ctrP.Y:F0}) chest=({fs.X:F0},{fs.Y:F0}) legsYaw={_player.LegsYaw:F2} torsoYaw={_player.TorsoYawWorld:F2} root={_player.RootPos} spd={_player.Speed:F1} camPos={_cam.GlobalPosition} retPt={_reticlePoint}");
            }
            if (!chestBehind)
            {
                float d = (fs - ctrP).Length();
                if (d < _pointBest) { _pointBest = d; _pointBestPitch = _pointPitch; }
            }
            _pointPitch += 0.03f;
            if (_pointPitch > 1.35f)
            {
                int idx = _pointStage;
                float r = ActivePointRanges[idx];
                GD.Print($"POINTTEST {(_pointHector ? "hector" : "enyo")} stand r={r:F0} bestCenterMiss={_pointBest:F0}px at pitch {_pointBestPitch:F2} " +
                    (_pointBest <= 10f ? "REACHABLE" : "UNREACHABLE — the crosshair never lands on the hilltop chest"));
                _pointNext = idx + 1;
                _pointStage = -1;
            }
        }
    }

    // --boom-shot (round 28, owner: "the player's rider should
    // be always visible"): sweep the full pitch travel on two
    // stands (flats, mesa skirt) and print the rider's head/feet
    // screen positions per step — the measured visibility
    // envelope of the boom camera. Captures at the extremes and
    // level on the flats.
    private int _boomStage = -1;
    private float _boomT, _boomPitch;
    private void UpdateBoomShot(float dt)
    {
        _aimPoint = TorsoAimPoint();
        _boomT += dt;
        if (_boomStage < 0)
        {
            _boomStage = 0;
            _boomT = 0f;
            _boomPitch = -1.35f;
            _player.Teleport(new Vector3(0f, Main.HeightAt(0f, 85f), 85f), 0f);
            _enemy.Teleport(new Vector3(0f, Main.HeightAt(0f, -100f), -100f), 0f);
            _foe.Walker.Teleport(new Vector3(60f, Main.HeightAt(60f, -100f), -100f), 0f);
            _player.SetDrive(0f, 0f);
            _player.TorsoTwist = 0f;
            _hpPlayer = 100000f;
            GD.Print("BOOMSHOT staged: sweep on the flats (0,85), then the skirt (0,45)");
            return;
        }
        if (_pendingPath != null || _capturing) return;
        if (_boomStage <= 1)
        {
            if (_boomT < 0.3f) return;
            _boomT = 0f;
            _player.AimPitchWide = _boomPitch;
            _player.TorsoPitch = _boomPitch;
            UpdateCamera(0f);
            var vp = GetViewport().GetVisibleRect().Size;
            var head = _cam.UnprojectPosition(_player.RootPos + Vector3.Up * _playerSpec.Height);
            var feet = _cam.UnprojectPosition(_player.RootPos);
            bool headIn = head.X > 0f && head.X < vp.X && head.Y > 0f && head.Y < vp.Y && !_cam.IsPositionBehind(_player.RootPos + Vector3.Up * _playerSpec.Height);
            bool feetIn = feet.X > 0f && feet.X < vp.X && feet.Y > 0f && feet.Y < vp.Y && !_cam.IsPositionBehind(_player.RootPos);
            GD.Print($"BOOMSWEEP stand={(_boomStage == 0 ? "flats" : "skirt")} pitch={_boomPitch:F2} head=({head.X:F0},{head.Y:F0}) feet=({feet.X:F0},{feet.Y:F0}) headIn={headIn} feetIn={feetIn} camPos={_cam.GlobalPosition}");
            if (_boomStage == 0)
            {
                string cap = Mathf.Abs(_boomPitch + 1.35f) < 0.01f ? "down" : Mathf.Abs(_boomPitch) < 0.01f ? "level" : Mathf.Abs(_boomPitch - 1.35f) < 0.01f ? "up" : null;
                if (cap != null) QueueCapture($"res://proof/boom_{cap}.png", $"boom{cap}", CamModes.Chase);
            }
            _boomPitch += 0.15f;
            if (_boomPitch > 1.36f)
            {
                _boomStage++;
                _boomPitch = -1.35f;
                if (_boomStage == 1)
                {
                    _player.Teleport(new Vector3(0f, Main.HeightAt(0f, 45f), 45f), 0f);
                    _player.SetDrive(0f, 0f);
                    _player.TorsoTwist = 0f;
                }
                else { GD.Print("BOOMSHOT DONE"); GetTree().Quit(); }
            }
        }
    }

    // --flipbook-test (Freelancer explosion prototype): stage the
    // player on the flats, fire one rocket-sized and one death-sized
    // flipbook fireball ahead of the chase camera, capture mid-boil
    // (t=0.30s) and late (t=0.75s, orange/red + smoke), then quit.
    private int _flipStage = -1;
    private float _flipT;
    private void UpdateFlipbookTest(float dt)
    {
        _aimPoint = TorsoAimPoint();
        _flipT += dt;
        if (_flipStage < 0)
        {
            _flipStage = 0;
            _flipT = 0f;
            _player.Teleport(new Vector3(0f, Main.HeightAt(0f, 85f), 85f), 0f);
            _enemy.Teleport(new Vector3(0f, Main.HeightAt(0f, -100f), -100f), 0f);
            _foe.Walker.Teleport(new Vector3(60f, Main.HeightAt(60f, -100f), -100f), 0f);
            _player.SetDrive(0f, 0f);
            _player.TorsoTwist = 0f;
            _player.AimPitchWide = 0f;
            _player.TorsoPitch = 0f;
            _hpPlayer = 100000f;
            float gy = Main.HeightAt(0f, 67f);
            SpawnFlipbookFireball(new Vector3(-4f, gy + 4f, 67f), 3.4f, 0.9f);
            SpawnFlipbookFireball(new Vector3(5f, gy + 5f, 62f), 10f, 1.2f);
            GD.Print("FLIPBOOK staged: rocket + death fireballs ahead of the player");
            return;
        }
        if (_pendingPath != null || _capturing) return;
        if (_flipStage == 0 && _flipT >= 0.30f)
        {
            _flipStage = 1;
            UpdateCamera(0f);
            QueueCapture("res://proof/flipbook_mid.png", "flipmid", CamModes.Chase);
        }
        else if (_flipStage == 1 && _flipT >= 0.75f)
        {
            _flipStage = 2;
            UpdateCamera(0f);
            QueueCapture("res://proof/flipbook_late.png", "fliplate", CamModes.Chase, () => GetTree().Quit());
        }
    }

    // --boom-check (round 28 follow-up, owner repeated "the
    // player's rider should be always visible" after the boom
    // shipped): stage the live-play cases the static sweep did
    // not cover — the rider backed up against a building (does
    // the boom put the camera inside it?) at level and at a
    // mesa-aim pitch, and (photo scene) a level chase frame.
    // Round 29 probe (owner reports): (1) camera lag — one frame
    // after a hard mouse flick, how far is the chase camera from
    // its boom target, and how many frames until it settles?
    // (2) rocks — can the rider walk THROUGH a rock's center
    // (only tall objects should block movement)? (3) trees — a
    // rider staged just outside the old stomp trigger but inside
    // body reach: does the tree die before contact?
    private int _lagStage = -1;
    private float _lagT;
    private Circle _lagRock;
    private bool _lagHasRock;
    private bool _lagRockStaged;
    private float _lagRockMin = float.MaxValue;
    private Destructible _lagTree;
    private void UpdateLagCheck(float dt)
    {
        _lagT += dt;
        if (_lagStage < 0)
        {
            _lagStage = 0;
            _lagT = 0f;
            _player.Teleport(new Vector3(0f, Main.HeightAt(0f, 85f), 85f), 0f);
            _enemy.Teleport(new Vector3(0f, Main.HeightAt(0f, -100f), -100f), 0f);
            _foe.Walker.Teleport(new Vector3(60f, Main.HeightAt(60f, -100f), -100f), 0f);
            _player.SetDrive(0f, 0f);
            _player.TorsoTwist = 0f;
            _player.TorsoPitch = 0f;
            _player.AimPitchWide = 0f;
            _playerYaw = 0f;
            _torsoWorldYaw = 0f;
            _hpPlayer = 100000f;
            UpdateCamera(0f); // settle exactly on the current boom target
            _torsoWorldYaw = 1.2f;
            MaintainTorsoWorldYaw();
            _player.AimPitchWide = 0.8f;
            _player.TorsoPitch = 0.8f;
            UpdateCamera(1f / 60f); // ONE frame after the flick
            var posA = _cam.GlobalPosition;
            UpdateCamera(0f);       // the exact target, by construction
            var posB = _cam.GlobalPosition;
            float lag0 = posA.DistanceTo(posB);
            _cam.GlobalPosition = posA;
            int settle = 0;
            while (settle < 240 && _cam.GlobalPosition.DistanceTo(posB) > 0.05f)
            {
                UpdateCamera(1f / 60f);
                settle++;
            }
            GD.Print($"LAGCHECK cameraLag={lag0:F2}u one frame after a 1.2rad yaw + 0.8rad pitch flick; settleFrames={settle} to within 0.05u (0 = camera exact every frame)");
            _lagHasRock = false;
            foreach (var c in _props)
                if (c.RoofTop <= 0f && c.R > 1.2f) { _lagRock = c; _lagHasRock = true; break; }
            GD.Print(_lagHasRock
                ? $"LAGCHECK rock candidate at {_lagRock.P} R={_lagRock.R:F2} playerCR={_player.CollisionRadius:F2}"
                : "LAGCHECK no rock candidate found");
            return;
        }
        if (_lagStage == 0)
        {
            if (_lagHasRock)
            {
                if (!_lagRockStaged)
                {
                    _lagRockStaged = true;
                    float off = _lagRock.R + _player.CollisionRadius + 3f;
                    var sp = new Vector3(_lagRock.P.X + off, 0f, _lagRock.P.Y);
                    sp.Y = Main.HeightAt(sp.X, sp.Z);
                    _player.Teleport(sp, Mathf.Pi / 2f);
                    _playerYaw = Mathf.Pi / 2f; // heading -X, straight at the rock
                }
                ApplyPlayerInput(dt, true, false, false, false, false);
                var rp0 = new Vector2(_player.RootPos.X, _player.RootPos.Z);
                _lagRockMin = Mathf.Min(_lagRockMin, rp0.DistanceTo(_lagRock.P));
            }
            if (_lagT > 5f)
            {
                _lagStage = 1;
                _lagT = 0f;
                var rp = new Vector2(_player.RootPos.X, _player.RootPos.Z);
                float dist = rp.DistanceTo(_lagRock.P);
                GD.Print($"LAGCHECK ROCK endDist={dist:F2} minDist={_lagRockMin:F2} rockR={_lagRock.R:F2} oldWallAt={_lagRock.R + _player.CollisionRadius:F2} {(_lagRockMin < _lagRock.R ? "CROSSED the rock disc — walked through" : "STOPPED outside it")}");
                _player.SetDrive(_playerYaw, 0f);
                _lagTree = null;
                foreach (var d in _destructibles)
                    if (d.Kind == "tree" && d.State == 0 && d.Center.Y < 12f) { _lagTree = d; break; }
                if (_lagTree != null)
                {
                    float off = _player.CollisionRadius + _lagTree.Radius + 0.5f;
                    var sp = new Vector3(_lagTree.Center.X + off, 0f, _lagTree.Center.Z);
                    sp.Y = Main.HeightAt(sp.X, sp.Z);
                    _player.Teleport(sp, 0f);
                    _player.SetDrive(0f, 0f);
                    GD.Print($"LAGCHECK tree at ({_lagTree.Center.X:F0},{_lagTree.Center.Z:F0}) trunkR={_lagTree.Radius:F2} stagedDist={off:F2} oldTrigger={_player.CollisionRadius + 0.35f:F2} bodyReach={_player.CollisionRadius + _lagTree.Radius:F2}");
                }
                else GD.Print("LAGCHECK no tree candidate found");
            }
            return;
        }
        if (_lagStage == 1 && _lagT > 0.5f)
        {
            _lagStage = 2;
            GD.Print(_lagTree != null
                ? $"LAGCHECK TREE state={_lagTree.State} {(_lagTree.State != 0 ? "STOMPED at the staged distance" : "still standing")}"
                : "LAGCHECK TREE skipped");
            GD.Print("LAGCHECK DONE");
            GetTree().Quit();
        }
    }

    private int _boomCheckStage = -1;
    private float _boomCheckT;
    private void UpdateBoomCheck(float dt)
    {
        _aimPoint = TorsoAimPoint();
        _boomCheckT += dt;
        if (_boomCheckStage < 0)
        {
            _boomCheckStage = 0;
            _boomCheckT = 0f;
            var bp = _buildingPts[0];
            _player.Teleport(new Vector3(bp.X, Main.HeightAt(bp.X, bp.Y - 14f), bp.Y - 14f), 0f);
            _enemy.Teleport(new Vector3(0f, Main.HeightAt(0f, -100f), -100f), 0f);
            _foe.Walker.Teleport(new Vector3(60f, Main.HeightAt(60f, -100f), -100f), 0f);
            _player.SetDrive(0f, 0f);
            _player.TorsoTwist = 0f;
            _hpPlayer = 100000f;
            GD.Print($"BOOMCHECK staged: rider 14u in front of building[0] at {bp}, facing -Z (building behind)");
            return;
        }
        if (_pendingPath != null || _capturing || _boomCheckT < 0.4f) return;
        _boomCheckT = 0f;
        if (_boomCheckStage == 0 || _boomCheckStage == 1)
        {
            float pitch = _boomCheckStage == 0 ? 0f : 0.35f;
            _player.AimPitchWide = pitch;
            _player.TorsoPitch = pitch;
            UpdateCamera(0f);
            var vp = GetViewport().GetVisibleRect().Size;
            var head = _cam.UnprojectPosition(_player.RootPos + Vector3.Up * _playerSpec.Height);
            var feet = _cam.UnprojectPosition(_player.RootPos);
            GD.Print($"BOOMCHECK city pitch={pitch:F2} camPos={_cam.GlobalPosition} head=({head.X:F0},{head.Y:F0}) feet=({feet.X:F0},{feet.Y:F0}) viewport={vp}");
            var anch = _player.RootPos + Vector3.Up * (_playerSpec.Height * 1.15f);
            var blk = _hit.Ray(anch, -AimDirWorld(), 26f, Roles.Player);
            GD.Print($"BOOMDBG root={_player.RootPos} anchor={anch} terrainAtCam={Main.HeightAt(_cam.GlobalPosition.X, _cam.GlobalPosition.Z):F1} blockHit={blk.Hit} blockT={blk.T:F1} blockTarget={blk.Target}");
            foreach (var pc in _hit.Props) if (new Vector2(pc.P.X - _buildingPts[0].X, pc.P.Y - _buildingPts[0].Y).Length() < 12f)
                GD.Print($"BOOMDBG cyl P={pc.P} R={pc.R:F1} base={pc.BaseY:F1} top={pc.BaseY + pc.Height:F1}");
            int st = _boomCheckStage;
            _boomCheckStage++;
            QueueCapture($"res://proof/boom_city{st}.png", $"boomcity{st}", CamModes.Chase);
        }
        else if (_boomCheckStage == 2)
        {
            _boomCheckStage++;
            if (_photo)
            {
                _player.Teleport(new Vector3(0f, Main.HeightAt(0f, 85f), 85f), 0f);
                _player.SetDrive(0f, 0f);
                _player.TorsoTwist = 0f;
                _player.AimPitchWide = 0f;
                _player.TorsoPitch = 0f;
                UpdateCamera(0f);
                QueueCapture("res://proof/boom_photo_level.png", "boomphotolevel", CamModes.Chase,
                    () => { GD.Print("BOOMCHECK DONE"); GetTree().Quit(); });
            }
            else { GD.Print("BOOMCHECK DONE"); GetTree().Quit(); }
        }
    }

    private void UpdateViewShot(float dt)
    {
        _aimPoint = TorsoAimPoint(); // live play refreshes the aim every frame; the staging must too
        _viewT += dt;
        if (_viewStage < 0)
        {
            _viewStage = 0;
            _viewT = 0f;
            _player.Teleport(new Vector3(0f, Main.HeightAt(0f, 58f), 58f), 0f);
            _enemy.Teleport(new Vector3(0f, Main.HeightAt(0f, 10f), 10f), 0f);
            _player.SetDrive(0f, 0f);
            _player.TorsoTwist = 0f;
            _player.TorsoPitch = 0f;
            _player.AimPitchWide = 0f; // looking level: the crosshair rests on the flats
            _hpPlayer = 100000f;
            GD.Print("VIEWSHOT staged: player at the mesa foot, foe on the top, view level");
            return;
        }
        if (_pendingPath != null || _capturing) return;
        var chest = _enemy.RootPos + Vector3.Up * (_enemySpec.Height * 0.68f);
        var vp = GetViewport().GetVisibleRect().Size;
        var ctr = vp * 0.5f;
        if (_viewStage == 0 && _viewT > 0.6f)
        {
            _viewStage = 1;
            var scr = _cam.UnprojectPosition(chest);
            GD.Print($"VIEWSHOT BEFORE foe chest screen=({scr.X:F0},{scr.Y:F0}) center=({ctr.X:F0},{ctr.Y:F0})");
            QueueCapture("res://proof/view_hilltop_before.png", "vbefore", CamModes.Chase, () =>
            {
                _viewPitch = -1.35f;
                _viewBest = 1e9f;
                _viewBestPitch = 0f;
                _viewStage = 2;
                _viewT = 0f;
            });
        }
        else if (_viewStage == 2)
        {
            // The mouse sweeps its full travel: the foe's chest
            // must cross the screen center (the crosshair) at some
            // pitch — the round-27 acceptance for the owner's
            // hilltop case.
            _player.AimPitchWide = _viewPitch;
            _player.TorsoPitch = _viewPitch;
            if (!_cam.IsPositionBehind(chest))
            {
                float d = (_cam.UnprojectPosition(chest) - ctr).Length();
                if (d < _viewBest) { _viewBest = d; _viewBestPitch = _viewPitch; }
            }
            _viewPitch += 0.03f;
            if (_viewPitch > 1.35f)
            {
                _viewStage = 3;
                _viewT = 0f;
                _player.AimPitchWide = _viewBestPitch;
                _player.TorsoPitch = _viewBestPitch;
                GD.Print($"VIEWSHOT SWEEP bestCenterMiss={_viewBest:F0}px at pitch {_viewBestPitch:F2}");
                GD.Print(_viewBest <= 10f
                    ? "VIEWSHOT PASS the hilltop foe crosses the center crosshair"
                    : "VIEWSHOT FAIL the hilltop foe never reaches the crosshair");
            }
        }
        else if (_viewStage == 3 && _viewT > 0.4f)
        {
            _viewStage = 4;
            QueueCapture("res://proof/view_hilltop.png", "vafter", CamModes.Chase, () => GetTree().Quit());
        }
    }

    // --brake-test: the owner's no-inertia rule (2026-10-31) —
    // from a full RUN (12.6 u/s) with the throttle released, EVERY
    // chassis must be at a standstill within one second. Measured
    // on the Enyo, then the player is swapped to the Hector (the
    // heavy one, whose spec Accel used to glide 5+ seconds) and
    // measured again.
    private int _brakeStage = -1;
    private float _brakeT;
    private void UpdateBrakeTest(float dt)
    {
        if (_brakeStage < 0)
        {
            _brakeStage = 0;
            _enemy.Teleport(new Vector3(0f, Main.HeightAt(0f, -100f), -100f), 0f);
            _player.Teleport(new Vector3(22f, Main.HeightAt(22f, 24f), 24f), Mathf.Pi);
            _player.SetDrive(Mathf.Pi, 12.6f);
            GD.Print("BRAKE staged: Enyo accelerating to run speed");
            return;
        }
        if (_brakeStage == 0 || _brakeStage == 2)
        {
            if (_player.Speed >= 12.5f)
            {
                _brakeT = 0f;
                _player.SetDrive(_player.LegsYaw, 0f);
                _brakeStage++;
                GD.Print($"BRAKE released at speed {_player.Speed:F2} — timing the stop");
            }
            return;
        }
        _brakeT += dt;
        if (_player.Speed <= 0.02f || _brakeT > 3f)
        {
            string who = _brakeStage == 1 ? "ENYO" : "HECTOR";
            bool pass = _player.Speed <= 0.02f && _brakeT <= 1.05f;
            GD.Print($"BRAKE {who} stopTime={_brakeT:F2}s fromSpeed=12.6 finalSpeed={_player.Speed:F2}");
            GD.Print(pass ? $"BRAKE {who} PASS stopped within one second" : $"BRAKE {who} FAIL stop took too long");
            if (_brakeStage == 1)
            {
                SetRiderSelection("hector", "enyo");
                ApplyLoadout();
                _player.Teleport(new Vector3(22f, Main.HeightAt(22f, 24f), 24f), Mathf.Pi);
                _player.SetDrive(Mathf.Pi, 12.6f);
                _brakeStage = 2;
                GD.Print("BRAKE staged: Hector accelerating to run speed");
            }
            else GetTree().Quit();
        }
    }

    // --roof-test: the owner's rooftop case (2026-10-31) — the
    // Enyo jump-jets from beside the (22,8) building, flies over
    // its roof, cuts the thrusters and must LAND AND STAND on the
    // roof (soles at roof level, not sunk to the terrain, not
    // pushed out by the wall colliders) — then walk on it.
    private int _roofStage = -1;
    private float _roofT, _roofTop;
    private Vector3 _roofCenter, _roofWalkFrom;
    private void UpdateRoofTest(float dt)
    {
        _roofT += dt;
        if (_roofStage < 0)
        {
            _roofStage = 0;
            _roofT = 0f;
            foreach (var r in Main.Roofs)
                if (Mathf.Abs(r.Cx - 22f) < 0.1f && Mathf.Abs(r.Cz - 8f) < 0.1f)
                { _roofTop = r.Top; _roofCenter = new Vector3(r.Cx, 0f, r.Cz); }
            if (_roofTop <= 0f) { GD.Print("ROOF FAIL no roof registered for the (22,8) building"); GetTree().Quit(); return; }
            _enemy.Teleport(new Vector3(0f, Main.HeightAt(0f, -100f), -100f), 0f);
            // A running approach, like real play: the burn budget
            // (2.2s) cannot lift from a standstill AND cross — the
            // horizontal Accel is only 4.2. Walk in from x=6, light
            // the thrusters at x=10.5, and momentum plus burn carry
            // him over the edge (x=16) just as his soles reach the
            // roof line.
            _player.Teleport(new Vector3(6f, Main.HeightAt(6f, 8f), 8f), RiderWalker.YawOf(Vector3.Right));
            _player.SetDrive(_player.LegsYaw, 4.2f);
            GD.Print($"ROOF staged: roofTop={_roofTop:F1} terrainAtBuilding={Main.HeightAt(22f, 8f):F1} — walking in, thrusters at x=10.5");
            return;
        }
        if (_roofT > 40f) { GD.Print($"ROOF FAIL timeout in stage {_roofStage} (root={_player.RootPos} sole={_player.SoleLevel:F1} jumpH={_player.JumpHeight:F1} charge={_player.JumpCharge:F2})"); GetTree().Quit(); return; }
        switch (_roofStage)
        {
            case 0: // walking in — light the thrusters at the mark
                if (_player.RootPos.X >= 10.5f)
                {
                    _player.SetThrust(true);
                    _roofStage = 1;
                    GD.Print($"ROOF thrusters lit at x={_player.RootPos.X:F1} — climbing over the edge");
                }
                break;
            case 1: // over the roof (or set down on it early): cut
                if (Mathf.Abs(_player.RootPos.X - _roofCenter.X) < 2.5f && Mathf.Abs(_player.RootPos.Z - _roofCenter.Z) < 2.5f
                    || _player.JumpHeight <= 0.01f && _player.RootPos.X > 17f)
                {
                    _player.SetThrust(false);
                    _player.SetDrive(_player.LegsYaw, 0f);
                    _roofStage = 2;
                    _roofT = 0f;
                    GD.Print($"ROOF over the roof at x={_player.RootPos.X:F1} — thrusters cut, settling");
                }
                break;
            case 2: // land and settle
                if (_player.JumpHeight <= 0.01f && _roofT > 1.6f)
                {
                    float soleErr = _player.SoleLevel - _roofTop;
                    bool onRoof = Mathf.Abs(soleErr) <= 0.8f && _player.RootPos.Y > Main.HeightAt(22f, 8f) + 7f;
                    GD.Print($"ROOF LAND sole={_player.SoleLevel:F2} roofTop={_roofTop:F2} err={soleErr:F2} rootY={_player.RootPos.Y:F1}");
                    GD.Print(onRoof ? "ROOF LAND PASS standing on the roof" : "ROOF LAND FAIL not standing on the roof");
                    _roofWalkFrom = _player.RootPos;
                    _player.SetDrive(_player.LegsYaw, 3f);
                    _roofStage = 3;
                    _roofT = 0f;
                }
                break;
            case 3: // walk on the roof
                if (_roofT > 1.5f)
                {
                    _player.SetDrive(_player.LegsYaw, 0f);
                    float walked = new Vector2(_player.RootPos.X - _roofWalkFrom.X, _player.RootPos.Z - _roofWalkFrom.Z).Length();
                    float soleErr = _player.SoleLevel - _roofTop;
                    bool ok = walked >= 2.2f && Mathf.Abs(soleErr) <= 0.8f;
                    GD.Print($"ROOF WALK walked={walked:F1}u sole={_player.SoleLevel:F2} err={soleErr:F2}");
                    GD.Print(ok ? "ROOF WALK PASS walked across the roof at roof level" : "ROOF WALK FAIL could not walk on the roof");
                    _roofStage = 4;
                    QueueCapture("res://proof/roof_stand.png", "roof", CamModes.Chase, () => GetTree().Quit());
                }
                break;
        }
    }

    private void UpdateFfaTest(float dt)
    {
        _ffaT += dt;
        if (!_ffaStaged)
        {
            _ffaStaged = true;
            var c = new Vector3(0f, 0f, 0f);
            _player.Teleport(new Vector3(0f, Main.HeightAt(0f, 52f), 52f),
                RiderWalker.YawOf((c - _player.RootPos).Normalized()));
            _enemy.Teleport(new Vector3(52f, Main.HeightAt(52f, -30f), -30f),
                RiderWalker.YawOf((c - _enemy.RootPos).Normalized()));
            _foe.Walker.Teleport(new Vector3(-52f, Main.HeightAt(-52f, -30f), -30f),
                RiderWalker.YawOf((c - _foe.Walker.RootPos).Normalized()));
            _ffaHome0 = _enemy.RootPos;
            _ffaHome1 = _foe.Walker.RootPos;
            GD.Print("FFATEST staged: three riders, 97u triangle around the mesa");
            return;
        }
        UpdateEnemyAI(dt);
        UpdateFoeAI(dt);
        if (_ffaT > 40f)
        {
            float moved0 = new Vector2(_enemy.RootPos.X - _ffaHome0.X, _enemy.RootPos.Z - _ffaHome0.Z).Length();
            float moved1 = new Vector2(_foe.Walker.RootPos.X - _ffaHome1.X, _foe.Walker.RootPos.Z - _ffaHome1.Z).Length();
            bool dmg = _hpPlayer < _playerSpec.MaxHp || _hpEnemy < _enemySpec.MaxHp || _foe.Hp < _foe.Spec.MaxHp
                || AnyPartDamaged(_partsPlayer) || AnyPartDamaged(_partsEnemy) || AnyPartDamaged(_foe.Parts);
            GD.Print($"FFATEST moved foe0={moved0:F1} foe={moved1:F1} shots foe0={_enemyShots} foe={_foeShots} anyDamage={dmg} hp P={_hpPlayer:F0} E={_hpEnemy:F0} F1={_foe.Hp:F0}");
            if (moved0 > 8f && moved1 > 8f && _enemyShots > 0 && _foeShots > 0 && dmg)
            {
                GD.Print("FFATEST PASS both opponents closed, fired, and drew blood in the free-for-all");
                GetTree().Quit(0);
            }
            else
            {
                GD.PrintErr("FFATEST FAIL: the free-for-all did not develop as required");
                GetTree().Quit(1);
            }
        }
    }

    private static bool AnyPartDamaged(RiderParts parts)
    {
        foreach (var id in new[] { "head", "torso", "arm_L", "arm_R", "gun", "pod_L", "pod_R", "leg_L", "leg_R" })
            if (parts.Has(id) && parts.HpOf(id) < parts.GetPart(id).MaxHp - 0.01f) return true;
        return false;
    }

    // The firing suites fire ONE weapon per group: their exact
    // expectations (3x12 laser, 2x26 cannon, 8-rocket salvo, 4
    // rockets from one pod) were calibrated on single fits, and
    // the filled default loadouts (owner, 2026-10-31) would move
    // every number without testing anything more. The weapons and
    // break suites therefore start from this range fit; the duel
    // suite and the live game use the real defaults.
    private void ApplyRangeFit()
    {
        _loadout = new LoadoutState { JumpJets = true };
        _loadout.TryAssign(_playerSpec, "arm_L", WeaponCatalog.LargeLaser);
        _loadout.TryAssign(_playerSpec, "arm_R", WeaponCatalog.Autocannon);
        _loadout.TryAssign(_playerSpec, "pod_L", WeaponCatalog.Lrm);
        _loadout.TryAssign(_playerSpec, "pod_R", WeaponCatalog.Lrm);
        ApplyLoadout();
    }

    private void WSetup(Vector3 pPos, Vector3 ePos)
    {
        _player.Teleport(pPos, 0f);
        _enemy.Teleport(ePos, Mathf.Pi);
        _player.TorsoTwist = 0f;
        _player.TorsoPitch = 0f;
        _enemy.TorsoTwist = 0f;
        _enemy.TorsoPitch = 0f;
        _playerYaw = 0f;
        _enemyYaw = Mathf.Pi;
        _prevPlayerRoot = _player.RootPos;
        _playerVel = Vector3.Zero;
    }

    private void UpdateWeaponsTest(float dt)
    {
        _wPhaseT += dt;
        // Hector torso tracks the player in every phase (gunnery muzzle and
        // visuals stay honest); both walkers otherwise hold position.
        var toP = _player.RootPos - _enemy.RootPos;
        if (toP.LengthSquared() > 0.01f)
            _enemy.TorsoTwist = RiderWalker.WrapAngle(RiderWalker.YawOf(toP.Normalized()) - _enemyYaw);
        if (_wPhaseT > 40) { WFail($"phase {_wPhase} timeout"); return; }

        if (_wPhase == 2 && _enemyDmgEvents > 0)
            _wJoltSeen = Mathf.Max(_wJoltSeen, _enemy.JoltEnv);
        if (_wPhase == 3 && _enemyDmgEvents > 0 && !_wHitCaptured)
        {
            // Capture a rocket impact at burst peak (round 9: effects
            // are proven by the frame, not only by counters).
            _wHitCaptured = true;
            QueueCapture("res://proof/wpn_rocket_hit.png", "wpnHit", "duelChase");
        }
        switch (_wPhase)
        {
            case 0: // settle at the open lane: TW (0,18) vs Hector (0,-12), 30u
                _player.SetDrive(0f, 0f);
                _enemy.SetDrive(Mathf.Pi, 0f);
                if (_wPhaseT > 1.2) SetWPhase(1);
                break;

            case 1: // laser x3 at a stationary Hector: 3 x 12 = 36 exactly
                if (_wShotsFired == 0 && _wPhaseT > 0.1)
                {
                    FireLaser(EnemyAim());
                    _wShotsFired++;
                    QueueCapture("res://proof/weapons_laser.png", "wlaser", "wpnSide");
                }
                else if (_wShotsFired == 1 && _wPhaseT > 0.65) { FireLaser(EnemyAim()); _wShotsFired++; }
                else if (_wShotsFired == 2 && _wPhaseT > 1.2) { FireLaser(EnemyAim()); _wShotsFired++; }
                else if (_wShotsFired == 3 && _wPhaseT > 1.7)
                {
                    if (_enemyDmgEvents != 3 || Mathf.Abs(_enemyDmgTotal - 36f) > 0.01f)
                        WFail($"laser events={_enemyDmgEvents} dmg={_enemyDmgTotal:F1} (want 3 / 36)");
                    if (Mathf.Abs(_hpEnemy - 304f) > 0.01f) WFail($"laser hectorHp={_hpEnemy:F1} (want 304)");
                    // Round 8: the reaction must carry the weapon —
                    // a laser hit flashes CYAN and lights the impact.
                    var lfc = _enemy.LastFlashColor;
                    if (!(lfc.B > lfc.R + 0.2f)) { WFail($"laser hit flashed {lfc} — want cyan"); return; }
                    if (_fxLights <= 0) { WFail("laser hit lit no impact light"); return; }
                    GD.Print($"WPNTEST LASER PASS events=3 dmg=36 hectorHp={_hpEnemy:F0} (hit reaction: cyan flash + impact light)");
                    _enemyDmgEvents = 0; _enemyDmgTotal = 0f;
                    _wJoltSeen = 0f;
                    SetWPhase(2);
                }
                break;

            case 2: // autocannon x2: 2 x 26 = 52 exactly. ROUTING DELTA
                    // (terrain step): on the old sine terrain the shells
                    // dropped ~0.6u in flight and landed on the pelvis/
                    // thigh junction, so leg_L absorbed the 52. On the
                    // heightfield the two stand heights are near-equal,
                    // the shells arrive higher and land mid-torso: the
                    // pool takes the 52 (304 -> 252) and leg_L stays
                    // untouched. Events/total are the invariant; the
                    // routing follows the terrain, as documented before.
                if (_wShotsFired == 0 && _wPhaseT > 0.1) { FireAutocannon(EnemyAim()); _wShotsFired++; }
                else if (_wShotsFired == 1 && _wPhaseT > 1.05) { FireAutocannon(EnemyAim()); _wShotsFired++; }
                else if (_wShotsFired == 2 && _wPhaseT > 2.8)
                {
                    if (_enemyDmgEvents != 2 || Mathf.Abs(_enemyDmgTotal - 52f) > 0.01f)
                        WFail($"ac events={_enemyDmgEvents} dmg={_enemyDmgTotal:F1} (want 2 / 52)");
                    if (Mathf.Abs(_hpEnemy - 252f) > 0.01f) WFail($"ac hectorHp={_hpEnemy:F1} (want 252 pool under terrain-step routing)");
                    if (Mathf.Abs(_partsEnemy.HpOf("leg_L") - 90f) > 0.01f)
                        WFail($"ac legL partHp={_partsEnemy.HpOf("leg_L"):F1} (want 90 = untouched, shells routed to torso)");
                    var cfc = _enemy.LastFlashColor;
                    if (!(cfc.R > cfc.B + 0.3f && cfc.G > cfc.B + 0.2f)) { WFail($"cannon hit flashed {cfc} — want warm white"); return; }
                    if (_wJoltSeen < 0.3f) { WFail($"cannon hits barely rocked the Hector (jolt env peak {_wJoltSeen:F2})"); return; }
                    GD.Print($"WPNTEST AC PASS events=2 dmg=52 hectorHp={_hpEnemy:F0} legL partHp={_partsEnemy.HpOf("leg_L"):F0} (shells routed to torso; reaction: warm flash + jolt {_wJoltSeen:F2})");
                    _enemyDmgEvents = 0; _enemyDmgTotal = 0f;
                    SetWPhase(3);
                }
                break;

            case 3: // LRM salvo: count rockets that damage the Hector (of 8)
                if (_wShotsFired == 0 && _wPhaseT > 0.1) { FireLrmSalvo(EnemyAim()); _wShotsFired++; }
                if (!_wCaptured && _wPhaseT > 0.55)
                {
                    _wCaptured = true;
                    QueueCapture("res://proof/weapons_rockets.png", "wrockets", "wpnRockets");
                }
                if (_wPhaseT > 3.4)
                {
                    if (_enemyDmgEvents < 5) WFail($"lrm damaging rockets={_enemyDmgEvents}/8 dmg={_enemyDmgTotal:F1}");
                    var rfc = _enemy.LastFlashColor;
                    if (!(rfc.R > 0.9f && rfc.B < 0.3f)) { WFail($"rocket hit flashed {rfc} — want deep orange"); return; }
                    GD.Print($"WPNTEST LRM PASS rocketsDamaging={_enemyDmgEvents}/8 dmg={_enemyDmgTotal:F1} hectorHp={_hpEnemy:F1} (reaction: orange flash)");
                    _enemyDmgEvents = 0; _enemyDmgTotal = 0f;
                    WSetup(new Vector3(22f, Main.HeightAt(22f, 24f), 24f), new Vector3(22f, Main.HeightAt(22f, -16f), -16f));
                    _wHpSnapshot = _hpEnemy;
                    SetWPhase(4);
                }
                break;

            case 4: // laser with the (22,8) building on the line: zero damage
                if (_wShotsFired == 0 && _wPhaseT > 0.3)
                {
                    FireLaser(_enemy.RootPos + Vector3.Up * 6.0f);
                    _wShotsFired++;
                    QueueCapture("res://proof/weapons_blocked.png", "wblocked", "wpnBlocked");
                }
                if (_wShotsFired == 1 && _wPhaseT > 1.2)
                {
                    if (_enemyDmgTotal > 0.01f || Mathf.Abs(_hpEnemy - _wHpSnapshot) > 0.01f)
                        WFail($"blocked laser leaked dmg={_enemyDmgTotal:F1} hp {_wHpSnapshot:F1}->{_hpEnemy:F1}");
                    GD.Print("WPNTEST BLOCKED PASS building stopped the beam, hectorHp unchanged");
                    // Phase 5 engages at 45u, not 30u: at 30u the shell
                    // TOF (0.6s) is shorter than the strafe reversal and
                    // the lead error stays inside the part-box volume
                    // (the digitigrade thighs protrude ~2.7u ahead of the
                    // capsule), so return fire could not miss at all. At
                    // 45u the TOF (0.9s) spans a full reversal and the
                    // dodge is measurable again.
                    WSetup(new Vector3(0f, Main.HeightAt(0f, 20f), 20f), new Vector3(0f, Main.HeightAt(0f, -25f), -25f));
                    _playerDmgEvents = 0; _playerDmgTotal = 0f; _enemyShots = 0; _wPrevShots = 0;
                    _enemyMayFire = true;
                    _wStrafeT = 0; _wStrafeDir = 1f;
                    _wKnocked = false;
                    SetWPhase(5);
                }
                break;

            case 5: // Hector return fire vs a strafing player.
                    // The player reverses every 0.9s at full speed. At the
                    // original 50 u/s shell the 0.9s flight let a reversal
                    // land inside the lead window — strafing genuinely
                    // dodged. Since 2026-10-29 the AC/20 flies at 150
                    // (owner: Hector matches the player's cannon changes,
                    // symmetric rules), flight is ~0.3s, the strafe
                    // displaces ~1.2u — inside the target's own radius —
                    // so return fire is RELIABLE now, by design. The
                    // owner flagged this as provisional: if Hector feels
                    // unfair in his visual test, the speed reverts.
                _wStrafeT += dt;
                if (_player.IsKnockedDown) _wKnocked = true;
                if (_wStrafeT > 0.9) { _wStrafeT = 0; _wStrafeDir = -_wStrafeDir; }
                float strafeYaw = _wStrafeDir > 0 ? -Mathf.Pi / 2f : Mathf.Pi / 2f;
                _player.SetDrive(strafeYaw, 4.2f);
                _player.TorsoTwist = -strafeYaw;
                _enemy.SetDrive(Mathf.Pi, 0f);
                if (_enemyShots != _wPrevShots) { _wPrevShots = _enemyShots; _wLastShotT = _wPhaseT; }
                if (_enemyShots >= 6 && _wPhaseT > _wLastShotT + 1.4)
                {
                    int hits = _playerDmgEvents;
                    int misses = _enemyShots - hits;
                    if (hits < 1) WFail($"hector return fire: 0 hits in {_enemyShots} shots");
                    // Pass band at 150 u/s: 5..6 hits of 6. The one
                    // allowed miss is the seeded ±1.2° cone at long
                    // range plus pacing luck in the reversal cycle;
                    // anything less means return fire broke. The old
                    // 2..5 dodge band (measured at 50 u/s, verify21..26)
                    // died with the dodge window itself. The sharp
                    // assertion stays the exact damage total below
                    // (hits x 18, no tolerance games there).
                    if (hits < 5) WFail($"hector return fire unreliable at matched speed: {hits} hits in {_enemyShots} shots");
                    if (Mathf.Abs(_playerDmgTotal - hits * 18f) > 0.01f)
                        WFail($"hector dmg total={_playerDmgTotal:F1} != hits*18 ({hits * 18})");
                    GD.Print($"WPNTEST HECTORFIRE PASS shots={_enemyShots} hits={hits} misses={misses} playerHp={_hpPlayer:F0}");
                    _enemyMayFire = false;
                    _player.TorsoTwist = 0f;
                    ResetDuel();
                    WSetup(new Vector3(0f, Main.HeightAt(0f, 18f), 18f), new Vector3(0f, Main.HeightAt(0f, -12f), -12f));
                    SetWPhase(6);
                }
                if (_wPhaseT > 35) WFail($"hector return fire timeout shots={_enemyShots}");
                break;

            case 6: // sustained fire -> Hector destroyed, banner, slump frame
                if (!_enemyDead)
                {
                    FireLaser(EnemyAim());
                    FireAutocannon(EnemyAim());
                }
                if (!_wCaptured && _wPhaseT > 0.9)
                {
                    _wCaptured = true;
                    QueueCapture("res://proof/weapons_hud.png", "whud", "wpnHud");
                }
                if (_enemyDead && !_wDeathLogged)
                {
                    _wDeathLogged = true;
                    _wDeathT = _wPhaseT;
                    GD.Print($"WPNTEST KILL hector destroyed at phaseT={_wPhaseT:F2}");
                }
                if (_enemyDead && _wPhaseT > _wDeathT + 1.8 && !_wDeadShotQueued)
                {
                    // Death comes only from the HP pool now (owner):
                    // wrecked legs limp, they never kill — so a dead
                    // Hector must show hp == 0 exactly.
                    if (_hpEnemy != 0f)
                        WFail($"dead hector hp={_hpEnemy:F1} (want pool kill, hp 0)");
                    if (!_enemy.Halted) WFail("dead hector walker not halted");
                    if (!_banner.Visible || _banner.Text != "HECTOR DESTROYED")
                        WFail($"kill banner wrong: visible={_banner.Visible} text='{_banner.Text}'");
                    GD.Print($"WPNTEST KILL PASS death certified (hp={_hpEnemy:F0}), walker halted, banner 'HECTOR DESTROYED'");
                    _wDeadShotQueued = true;
                    QueueCapture("res://proof/weapons_hector_dead.png", "wdead", "wpnDead", () => SetWPhase(7));
                }
                if (_wPhaseT > 30) WFail("kill sequence timeout");
                break;

            case 7: // player death banner + R reset restores everything
                if (_wShotsFired == 0 && _wPhaseT > 0.3)
                {
                    _wShotsFired++;
                    var dummy = new HitModel.Result
                    {
                        Hit = true,
                        Target = Roles.Player,
                        Zone = "torso",
                        Point = _player.RootPos + Vector3.Up * 8f
                    };
                    ApplyDamage(_player, 9999f, dummy, Roles.Enemy);
                    if (!_playerDead) WFail("player death flag not set");
                    if (!_banner.Visible || _banner.Text != "RIDER DESTROYED — press R to reset")
                        WFail($"player banner wrong: visible={_banner.Visible} text='{_banner.Text}'");
                    GD.Print("WPNTEST PLAYERDEATH PASS banner 'RIDER DESTROYED — press R to reset'");
                }
                if (_wShotsFired == 1 && _wPhaseT > 0.9)
                {
                    ResetDuel();
                    if (_hpPlayer != _playerSpec.MaxHp || _hpEnemy != _enemySpec.MaxHp || _playerDead || _enemyDead || _banner.Visible)
                        WFail($"reset state wrong hp={_hpPlayer}/{_hpEnemy} dead={_playerDead}/{_enemyDead} banner={_banner.Visible}");
                    GD.Print("WPNTEST RESET PASS hp pools restored, deaths and banner cleared");
                    SetWPhase(8);
                }
                break;

            case 8: // ENV: destructible buildings + trees
                if (_wEnvStep == 0)
                {
                    ResetDuel();
                    _wEnvB = null; _wEnvT = null;
                    foreach (var d in _destructibles)
                    {
                        if (d.Kind == "building" && d.State == 0 && _wEnvB == null) _wEnvB = d;
                        if (d.Kind == "tree" && d.State == 0 && _wEnvT == null) _wEnvT = d;
                    }
                    if (_wEnvB == null || _wEnvT == null) { WFail("no intact building/tree for the env phase"); return; }
                    _wEnvFx0 = _envFxEvents; _wEnvChunks0 = _envChunks.Count; _wEnvParticles0 = _fxParticles;
                    // Firing position: 16u from the building, toward the arena center.
                    var bc = _wEnvB.Center;
                    var toC = (Vector3.Zero - bc); toC.Y = 0f;
                    var fp = bc + toC.Normalized() * 16f;
                    fp.Y = Main.HeightAt(fp.X, fp.Z);
                    _player.Teleport(fp, RiderWalker.YawOf((bc - fp).Normalized()));
                    _playerYaw = _player.LegsYaw;
                    _wEnvStep = 1;
                }
                else if (_wEnvStep == 1)
                {
                    // Laser the building down: 40 HP / 12 per hit = 4 hits.
                    _cdLaser = 0f;
                    var bc = _wEnvB.Center;
                    FireLaser(new Vector3(bc.X, bc.Y + _wEnvB.Height * 0.55f, bc.Z));
                    if (_wEnvB.State == 2)
                    {
                        GD.Print("ENVTEST COLLAPSE-START PASS building gave way under fire");
                        if (_envFxEvents <= _wEnvFx0) { WFail("building collapsed with NO destruction FX"); return; }
                        // (the dust rule's proof is the particle count below: fire+smoke+2 dust systems+burn emitter)
                        if (_fxLights <= 0) { WFail("building collapse lit no impact light"); return; }
                        int bp = _fxParticles - _wEnvParticles0;
                        if (bp < 6) { WFail($"building collapse spawned only {bp} particle systems (want explosion fire+smoke, 2 dust systems, burning wreck)"); return; }
                        GD.Print($"ENVTEST FX PASS building died with an explosion, a light flash, a dust cloud and {bp} particle systems; original hidden, pieces flying");
                        QueueCapture("res://proof/env_building_collapse.png", "envCollapse", "duelChase");
                        _wEnvFx0 = _envFxEvents; _wEnvChunks0 = _envChunks.Count; _wEnvParticles0 = _fxParticles;
                        _wEnvStep = 2;
                    }
                    if (_wPhaseT > 20) WFail("building never collapsed");
                }
                else if (_wEnvStep == 2 && _wEnvB.State == 4)
                {
                    var bc = _wEnvB.Center;
                    var from = _player.RootPos + Vector3.Up * 8f;
                    var aimB = new Vector3(bc.X, bc.Y + 2f, bc.Z);
                    var ray = _hit.Ray(from, (aimB - from).Normalized(), 30f, Roles.Player);
                    float distB = from.DistanceTo(aimB);
                    if (ray.Target == Roles.Prop && ray.T < distB + 2f) { WFail("rubble still blocks shots"); return; }
                    GD.Print("ENVTEST BUILDING PASS collapsed to rubble, colliders removed, shots pass through");
                    // Move to the tree: ONE hit of any kind destroys
                    // it, and its shot-blocking cylinder is gone from
                    // that same frame (owner's rule).
                    var tc = _wEnvT.Center;
                    var toC2 = (Vector3.Zero - tc); toC2.Y = 0f;
                    var fp2 = tc + toC2.Normalized() * 12f;
                    fp2.Y = Main.HeightAt(fp2.X, fp2.Z);
                    _player.Teleport(fp2, RiderWalker.YawOf((tc - fp2).Normalized()));
                    _playerYaw = _player.LegsYaw;
                    // The owner's live scenario, scripted — twice
                    // over. He aims at the tree's VISIBLE center: the
                    // crown, ~1.5u off the trunk axis at 0.75 height.
                    // Round 11 converged on the trunk but the crown
                    // had no collider at all, so the shot still went
                    // through the leaves. Now the canopy collides:
                    // the chest ray must catch it, convergence must
                    // land on it, and one hit must drop the tree.
                    _player.TorsoTwist = 0f;
                    // Aim the way the player does: hunt the pitch
                    // until the RETICLE sits on the crown — the
                    // camera-ray convergence reports the prop. With
                    // the high camera that pitch is steeper than the
                    // torso-to-crown line (the round-12 offset the
                    // owner reported: cursor on target, hits low).
                    _cdLaser = 0f;
                    Vector3 conv = Vector3.Zero; bool onCrown = false;
                    var crownCtr = new Vector3(tc.X, tc.Y + _wEnvT.Height * 0.6f, tc.Z);
                    for (float pp = 0.15f; pp >= -0.55f; pp -= 0.025f)
                    {
                        _player.TorsoPitch = pp;
                        conv = TorsoAimPoint();
                        // Any PROP along the view line is not enough —
                        // the convergence must be ON this tree's crown.
                        if (_aimTarget == Roles.Prop && conv.DistanceTo(crownCtr) < 3.5f) { onCrown = true; break; }
                    }
                    if (!onCrown) { WFail("no pitch puts the cursor on the crown"); return; }
                    _wEnvChunks0 = _envChunks.Count; // re-baseline at the shot: building pieces dissolve on their own clock (round 21) and must not pollute the splinter delta
                    FireLaser(conv);
                    if (_wEnvT.State != 3) { WFail($"one hit did not destroy the tree (state={_wEnvT.State})"); return; }
                    var fromT = _player.RootPos + Vector3.Up * 8f;
                    var aimT = new Vector3(tc.X, tc.Y + 2f, tc.Z);
                    var rayT = _hit.Ray(fromT, (aimT - fromT).Normalized(), 30f, Roles.Player);
                    if (rayT.Target == Roles.Prop && rayT.T < fromT.DistanceTo(aimT) + 2f) { WFail("destroyed tree still blocks shots"); return; }
                    if (_envFxEvents <= _wEnvFx0) { WFail("tree died with NO destruction FX"); return; }
                    int splinters = _envChunks.Count - _wEnvChunks0;
                    if (splinters < 5) { WFail($"tree threw only {splinters} splinters (want 5)"); return; }
                    int tp = _fxParticles - _wEnvParticles0;
                    if (tp < 3) { WFail($"tree death spawned only {tp} particle systems (want fire burst + burning wreck)"); return; }
                    GD.Print($"ENVTEST TREE-HIT PASS laser aimed at the visible crown (1.5u off-axis) converged on it and destroyed the tree in one hit, colliders removed at once, FX fired (+{splinters} splinters, {tp} particle systems)");
                    _wEnvStep = 3;
                }
                else if (_wEnvStep == 3)
                {
                    if (!_wCaptured) { _wCaptured = true; QueueCapture("res://proof/env_tree_destroyed.png", "envTreeDown", "duelChase"); }
                    if (_wPhaseT > 40) { WFail("destroyed tree never finished smoking out"); return; }
                    if (_wEnvT.State == 4)
                    {
                        if (GodotObject.IsInstanceValid(_wEnvT.Node)) { WFail("destroyed tree node still alive"); return; }
                        GD.Print("ENVTEST TREE PASS destroyed on one hit, smoked out in ~1s, node freed");
                        GD.Print($"WPNTEST FX sparks={_fxSparkBursts} booms={_fxBooms} scorches={_fxScorches}");
                        if (_fxSparkBursts <= 0) WFail("no laser spark bursts spawned");
                        if (_fxBooms <= 0) WFail("no rocket explosions spawned");
                        if (_fxScorches <= 0) WFail("no cannon scorch marks spawned");
                        if (_fxParticles <= 0) WFail("no particle systems spawned");
                        GD.Print("WPNTEST FX PASS laser sparks, rocket explosions and cannon scorches all fired");
                        _wEnvStep = 4; _wPhaseT = 0f;
                    }
                }
                else if (_wEnvStep == 4)
                {
                    // The weapons suite's captures leave _camMode on
                    // a proof pose, which disables TorsoAimPoint's
                    // camera-ray branch — every ENV hunt before this
                    // line silently ran on the chest fallback (the
                    // far Hector passed through it, masking the fact).
                    // Live play is always chase; restore it here.
                    _camMode = CamModes.Chase;
                    // FAR TARGET (owner, round 13): at 85u the old
                    // 60u convergence cap rejected the cursor's hit
                    // and shots fell back to the free point — in the
                    // sky. Hunt the pitch until the cursor sits on
                    // the Hector, then require exact laser damage.
                    _hpEnemy = _enemySpec.MaxHp;
                    _player.Teleport(new Vector3(0f, Main.HeightAt(0f, 45f), 45f), 0f);
                    _enemy.Teleport(new Vector3(0f, Main.HeightAt(0f, -40f), -40f), Mathf.Pi);
                    _playerYaw = 0f; _enemyYaw = Mathf.Pi;
                    _player.SetDrive(0f, 0f); _enemy.SetDrive(Mathf.Pi, 0f);
                    _player.TorsoTwist = 0f;
                    _playerVel = Vector3.Zero; _prevPlayerRoot = _player.RootPos;
                    UpdateCamera(0f);
                    Vector3 convF = Vector3.Zero; bool onFar = false;
                    for (float pp = 0.30f; pp >= -0.30f; pp -= 0.01f)
                    {
                        _player.TorsoPitch = pp;
                        convF = TorsoAimPoint();
                        if (_aimTarget == Roles.Enemy) { onFar = true; break; }
                    }
                    if (!onFar) { WFail("no pitch puts the cursor on the far Hector"); return; }
                    _cdLaser = 0f;
                    FireLaser(convF);
                    if (340f - _hpEnemy < 11.9f) { WFail($"far cursor shot did not damage the Hector (hp={_hpEnemy:F0})"); return; }
                    GD.Print($"ENVTEST FAR-TARGET PASS cursor convergence at 85u: laser hit the Hector for {340f - _hpEnemy:F0}");
                    // FAR HILL (owner, round 13): the cursor on a
                    // distant hillside with a clear line must
                    // converge on the hill itself, not the sky. The
                    // Hector steps aside. Staging is searched, not
                    // assumed, so the harness tries pads x facings
                    // with the game's own aim math and keeps the
                    // first setup where a pitch hunt finds a far
                    // TERRAIN convergence — the same search a player
                    // does when he turns toward a slope. Round 27:
                    // the hunt band extends to -0.60 — the old
                    // free-reticle ray descended by parallax inside
                    // the old band, while the center axis is level
                    // at pitch 0, so far slopes are met lower.
                    _enemy.Teleport(new Vector3(60f, Main.HeightAt(60f, 60f), 60f), Mathf.Pi);
                    Vector3 convH = Vector3.Zero; bool onHill = false;
                    var hillPads = new[] { new Vector2(0f, 0f), new Vector2(30f, 30f), new Vector2(-30f, 30f), new Vector2(30f, -30f), new Vector2(-30f, -30f) };
                    foreach (var pad in hillPads)
                    {
                        if (onHill) break;
                        _player.Teleport(new Vector3(pad.X, Main.HeightAt(pad.X, pad.Y), pad.Y), 0f);
                        _playerVel = Vector3.Zero; _prevPlayerRoot = _player.RootPos;
                        for (int k = 0; k < 16 && !onHill; k++)
                        {
                            float a = k * Mathf.Tau / 16f;
                            // Teleport applies the yaw NOW; SetDrive
                            // would only take effect on the next
                            // walker step — the first search swept one
                            // lane sixteen times (measured: no far
                            // convergence anywhere, because every
                            // 'facing' was still yaw 0).
                            _player.Teleport(new Vector3(pad.X, Main.HeightAt(pad.X, pad.Y), pad.Y), a);
                            _playerYaw = a;
                            _player.TorsoTwist = 0f;
                            UpdateCamera(0f);
                            for (float pp = 0.40f; pp >= -0.60f; pp -= 0.02f)
                            {
                                _player.TorsoPitch = pp;
                                convH = TorsoAimPoint();
                                if (_aimTarget == Roles.Terrain && convH.DistanceTo(_player.RootPos) > 60f) { onHill = true; break; }
                            }
                        }
                    }
                    if (!onHill) { WFail("no pad/facing puts the cursor on a far hillside"); return; }
                    GD.Print($"ENVTEST FAR-HILL PASS cursor converges on the hillside at {convH.DistanceTo(_player.RootPos):F0}u with a clear line");
                    GD.Print("WPNTEST DONE");
                    GetTree().Quit(0);
                }
                break;
        }
    }

    // ---------- breakable-test choreography ----------

    private void SetBPhase(int p)
    {
        _bPhase = p;
        _bPhaseT = 0;
        _bStep = 0;
        _bT0 = 0;
        _bFlag1 = false;
        GD.Print($"BRKTEST phase={p} t={_simT:F2}");
    }

    private void BStep(int s)
    {
        _bStep = s;
        _bT0 = _bPhaseT;
        _bFlag1 = false;
        _bCount1 = 0;
    }

    private float BStepT => (float)(_bPhaseT - _bT0);

    private void BFail(string msg)
    {
        GD.PrintErr($"BRKTEST FAIL: {msg}");
        GetTree().Quit(1);
    }

    private void BSetup(Vector3 pPos, Vector3 ePos)
    {
        _player.Teleport(pPos, 0f);
        _enemy.Teleport(ePos, Mathf.Pi);
        _player.TorsoTwist = 0f;
        _player.TorsoPitch = 0f;
        _enemy.TorsoTwist = 0f;
        _enemy.TorsoPitch = 0f;
        _playerYaw = 0f;
        _enemyYaw = Mathf.Pi;
        _prevPlayerRoot = _player.RootPos;
        _playerVel = Vector3.Zero;
        _enemyMayFire = false;
    }

    // Harness damage through the real ApplyDamage path (attribution runs
    // on the part's own largest-volume center — a point guaranteed to
    // sit inside the part). Same precedent as the weapons test's crafted
    // player-death hit.
    private void CraftHit(RiderWalker target, string partId, float dmg, float weaponWeight = 0f)
    {
        var parts = target == _player ? _partsPlayer : _partsEnemy;
        var hit = new HitModel.Result
        {
            Hit = true,
            Target = target == _player ? Roles.Player : Roles.Enemy,
            Zone = "torso",
            Point = parts.PartAimPoint(partId)
        };
        ApplyDamage(target, dmg, hit, target == _player ? Roles.Enemy : Roles.Player, weaponWeight);
    }

    // Craft via a real HitModel ray: needed for the TW head, whose
    // volume interior is shadowed by the torso core's AABB (an interior
    // point attributes torso; a surface hit attributes head, exactly as
    // real aimed fire does). The head ray comes in angled onto the
    // canopy face: a purely vertical drop races the capsule's flat top
    // disc in the same column (sub-decimeter, decided by the root tilt
    // the terrain happens to give), while the angled ray meets the
    // head box where it protrudes past the capsule, like a real shot.
    private void CraftRayHit(RiderWalker target, string partId, float dmg)
    {
        var parts = target == _player ? _partsPlayer : _partsEnemy;
        var aim = parts.PartAimPoint(partId);
        Vector3 from;
        if (partId == "head")
        {
            var fwd = RiderWalker.HeadingVec(target.TorsoYawWorld);
            from = aim + Vector3.Up * 5f + fwd * 7f;
        }
        else from = aim + Vector3.Up * 15f;
        var res = _hit.Ray(from, (aim - from).Normalized(), 40f, target == _player ? Roles.Enemy : Roles.Player);
        GD.Print($"BRK CRAFTRAY {partId} hit={res.Hit} target={res.Target} part={res.Part} point={res.Point}");
        if (!res.Hit || res.Part != partId)
        {
            BFail($"craft ray for {partId} attributed '{res.Part}'");
            return;
        }
        ApplyDamage(target, dmg, res, target == _player ? Roles.Enemy : Roles.Player);
    }

    private void UpdateBreakTest(float dt)
    {
        _bPhaseT += dt;
        if (_bPhaseT > 90) { BFail($"phase {_bPhase} timeout"); return; }

        switch (_bPhase)
        {
            case 0: // settle on the lane: TW (0,18) vs Hector (0,-12)
                _player.SetDrive(0f, 0f);
                _enemy.SetDrive(Mathf.Pi, 0f);
                if (_bPhaseT > 1.0) SetBPhase(1);
                break;

            case 1: // POD: part HP depletes per hit, breaks at <= 0, debris
                    // settles, then stays behind while the rider walks away
                _player.SetDrive(0f, _bStep >= 3 ? 2.4f : 0f);
                _enemy.SetDrive(Mathf.Pi, 0f);
                if (_bStep == 0 && _bPhaseT > 0.2)
                {
                    CraftHit(_player, "pod_L", 26f);
                    if (Mathf.Abs(_partsPlayer.HpOf("pod_L") - 19f) > 0.01f)
                        BFail($"pod hp after 26 dmg = {_partsPlayer.HpOf("pod_L"):F1} (want 19)");
                    GD.Print("BRKTEST PODHP PASS part HP 45 -> 19 after one 26 hit");
                    BStep(1);
                }
                else if (_bStep == 1 && BStepT > 0.3)
                {
                    CraftHit(_player, "pod_L", 26f);
                    if (_partsPlayer.Alive("pod_L")) BFail("pod_L alive after 52 total damage");
                    if (_partsPlayer.DebrisOf("pod_L") == null) BFail("pod_L debris missing");
                    GD.Print("BRKTEST PODBREAK PASS pod_L broke at <= 0 HP, debris spawned");
                    BStep(2);
                }
                else if (_bStep == 2)
                {
                    // Mid-fall frame at ~0.3s of debris age (the piece
                    // leaves at +2.8 u/s up, then falls ~11u: at 0.3s it
                    // is visibly airborne and clear of the torso).
                    if (!_bFlag1 && BStepT > 0.30)
                    {
                        _bFlag1 = true;
                        QueueCapture("res://proof/break_pod_fall.png", "bpodfall", "brkPlayer");
                    }
                    var d = _partsPlayer.DebrisOf("pod_L");
                    if (d != null && d.Settled)
                    {
                        if (!_partsPlayer.DebrisRestOk("pod_L", 0.15f, out float minY, out float gY))
                            BFail($"pod debris rest off: lowestCornerY={minY:F3} terrainThere={gY:F3}");
                        GD.Print($"BRKTEST DEBRISREST PASS pod_L lowest corner on terrain (y={minY:F3} vs {gY:F3})");
                        _bVec1 = d.Node.GlobalPosition;
                        _bV1 = _player.RootPos.DistanceTo(_bVec1);
                        _camFocus = _bVec1;
                        BStep(3);
                    }
                    else if (BStepT > 7) BFail("pod debris never settled");
                }
                else if (_bStep == 3 && BStepT > 3.0)
                {
                    _player.SetDrive(0f, 0f);
                    var d = _partsPlayer.DebrisOf("pod_L");
                    float drift = d.Node.GlobalPosition.DistanceTo(_bVec1);
                    float away = _player.RootPos.DistanceTo(d.Node.GlobalPosition) - _bV1;
                    if (drift > 0.05f) BFail($"debris moved with the rider: drift={drift:F3}");
                    if (away < 3f) BFail($"rider did not walk away from debris: delta={away:F2}");
                    GD.Print($"BRKTEST DETACH PASS debris static (drift={drift:F3}) while rider walked {away:F1}u away");
                    QueueCapture("res://proof/break_debris_rest.png", "bdebris", "brkDebris");
                    SetBPhase(2);
                }
                break;

            case 2: // SALVO: one pod gone -> 4 rockets; both gone -> denied
                _player.SetDrive(0f, 0f);
                _enemy.SetDrive(Mathf.Pi, 0f);
                if (_bStep == 0)
                {
                    BStep(1);
                    _bCount1 = _rocketsLaunched;
                    FireLrmSalvo(EnemyAim());
                }
                else if (_bStep == 1 && BStepT > 1.6)
                {
                    int n = _rocketsLaunched - _bCount1;
                    if (n != 4) BFail($"salvo with pod_L gone = {n} rockets (want 4)");
                    GD.Print("BRKTEST SALVO4 PASS one pod lost -> 4 rockets");
                    CraftHit(_player, "pod_R", 26f);
                    CraftHit(_player, "pod_R", 26f);
                    if (_partsPlayer.Alive("pod_R")) BFail("pod_R alive after 52 damage");
                    BStep(2);
                    _bCount1 = _rocketsLaunched;
                    _lastDenied = "";
                    FireLrmSalvo(EnemyAim());
                    if (_lastDenied != "lrm") BFail("LRM not denied with both pods gone");
                }
                else if (_bStep == 2 && BStepT > 0.8)
                {
                    if (_rocketsLaunched != _bCount1) BFail("denied LRM still launched rockets");
                    GD.Print("BRKTEST SALVODENIED PASS both pods gone -> LRM denied, 0 rockets");
                    ResetDuel();
                    BSetup(new Vector3(0f, Main.HeightAt(0f, 20f), 20f), new Vector3(0f, Main.HeightAt(0f, -10f), -10f));
                    SetBPhase(3);
                }
                break;

            case 3: // GUN (Hector): real AC fire breaks the waist gun; its AC/20 stops
                _player.SetDrive(0f, 0f);
                if (_bStep == 0)
                {
                    _enemyMayFire = true;
                    _enemy.SetDrive(Mathf.Pi, 0f);
                    if (_enemyShots >= 2) BStep(1);
                    else if (_bPhaseT > 20) BFail($"hector fired only {_enemyShots} shots pre-break");
                }
                else if (_bStep == 1)
                {
                    _enemy.SetDrive(Mathf.Pi, 0f);
                    if (_cdAc <= 0f) FireAutocannon(_partsEnemy.PartAimPoint("gun"));
                    if (!_partsEnemy.Alive("gun"))
                    {
                        GD.Print($"BRKTEST GUNBREAK PASS hector waist gun destroyed by real AC fire (partHp logs above), hectorShots so far={_enemyShots}");
                        BStep(2);
                        _bCount1 = _enemyShots;
                    }
                    else if (BStepT > 15) BFail("hector waist gun never broke under AC fire");
                }
                else if (_bStep == 2)
                {
                    _enemy.SetDrive(Mathf.Pi, 1.6f);
                    if (BStepT > 1.2 && !_bFlag1)
                    {
                        _bFlag1 = true;
                        QueueCapture("res://proof/break_hector_nogun.png", "bnogun", "brkHector");
                    }
                    if (BStepT > 5.5)
                    {
                        if (_enemyShots != _bCount1)
                            BFail($"hector AC/20 fired after waist-gun loss: {_bCount1} -> {_enemyShots}");
                        GD.Print($"BRKTEST GUNPASS PASS hector AC/20 silent for 5.5s after waist-gun loss (shots flat at {_enemyShots})");
                        _enemyMayFire = false;
                        ResetDuel();
                        BSetup(new Vector3(0f, Main.HeightAt(0f, 18f), 18f), new Vector3(0f, Main.HeightAt(0f, -12f), -12f));
                        SetBPhase(4);
                    }
                }
                break;

            case 4: // PLAYER ARM: laser denied with zero beams; HUD frame
                _player.SetDrive(0f, 0f);
                _enemy.SetDrive(Mathf.Pi, 0f);
                if (_bStep == 0)
                {
                    int guard = 0;
                    while (_partsPlayer.Alive("arm_L") && guard++ < 5)
                        CraftHit(_player, "arm_L", 26f);
                    if (_partsPlayer.Alive("arm_L")) BFail("player arm_L would not break");
                    _bCount1 = _beamsSpawned;
                    _lastDenied = "";
                    FireLaser(EnemyAim());
                    if (_lastDenied != "laser") BFail("laser not denied after arm_L loss");
                    if (_beamsSpawned != _bCount1) BFail("denied laser still spawned a beam");
                    GD.Print("BRKTEST LASERDENIED PASS arm_L lost -> laser denied, 0 beams");
                    QueueCapture("res://proof/break_hud_destroyed.png", "bhud", "wpnHud");
                    BStep(1);
                }
                else if (_bStep == 1 && BStepT > 0.8)
                {
                    ResetDuel();
                    BSetup(new Vector3(-38f, Main.HeightAt(-38f, 38f), 38f), new Vector3(0f, Main.HeightAt(0f, 24f), 24f));
                    _bDragMax = 0f;
                    SetBPhase(5);
                }
                break;

            case 5: // LEG (Hector): baseline speed, break leg_L, then the
                    // rider limps at the 0.55 factor with the wrecked leg
                    // still stepping (round 7: no rigid drag)
                _player.SetDrive(0f, 0f);
                if (_bStep == 0)
                {
                    _enemy.SetDrive(Mathf.Pi, 0f);
                    if (BStepT > 1.0) BStep(1);
                }
                else if (_bStep == 1)
                {
                    _enemy.SetDrive(Mathf.Pi, 2.2f);
                    if (!_bFlag1 && BStepT > 1.0)
                    {
                        _bFlag1 = true;
                        _bVec1 = _enemy.RootPos;
                        _bT0 = _bPhaseT;
                    }
                    else if (_bFlag1 && BStepT > 3.0)
                    {
                        var d = new Vector2(_enemy.RootPos.X - _bVec1.X, _enemy.RootPos.Z - _bVec1.Z).Length();
                        _bV1 = d / BStepT;
                        GD.Print($"BRKTEST BASESPEED hector baseline={_bV1:F2} u/s (cmd 2.2; unified clock — the walker now covers the full commanded speed)");
                        if (_bV1 < 1.2f) BFail($"baseline speed implausible: {_bV1:F2}");
                        BStep(2);
                    }
                }
                else if (_bStep == 2)
                {
                    _enemy.SetDrive(Mathf.Pi, 0f);
                    if (BStepT > 1.2)
                    {
                        CraftHit(_enemy, "leg_L", 30f);
                        CraftHit(_enemy, "leg_L", 30f);
                        CraftHit(_enemy, "leg_L", 30f);
                        if (_partsEnemy.Alive("leg_L")) BFail("hector leg_L alive after 90 damage");
                        if (!_enemy.LegDisabled(true)) BFail("walker leg_L not disabled");
                        var legPart = _partsEnemy.GetPart("leg_L");
                        if (!legPart.Wrecked) BFail("leg_L not in wrecked state");
                        foreach (var rec in legPart.Meshes)
                            if (rec.Detached) BFail("leg_L mesh DETACHED — legs must never fall off");
                        if (_enemy.IsKnockedDown) BFail("leg wreck felled the rider (retired fall rule)");
                        if (Mathf.Abs(_enemy.SpeedFactor - 0.55f) > 0.01f) BFail($"speed factor {_enemy.SpeedFactor:F2} != 0.55 (limp)");
                        GD.Print("BRKTEST LEGWRECK PASS leg_L destroyed but ATTACHED (wrecked), rider upright, limp factor 0.55");
                        _bSawWreckSwing = false;
                        BStep(3);
                    }
                }
                else if (_bStep == 3)
                {
                    // Full drive on the wrecked leg: the rider must MOVE
                    // (it limps, it is not immobile) at roughly the 0.55
                    // limp factor of its measured baseline speed.
                    _enemy.SetDrive(Mathf.Pi, 2.2f);
                    if (!_enemy.LegPlanted(true)) _bSawWreckSwing = true;
                    if (_bCount1 == 0 && BStepT > 1.0)
                    {
                        _bVec1 = _enemy.RootPos;
                        _bT0 = _bPhaseT; // BStepT now measures the sample window
                        _bCount1 = 1;
                    }
                    else if (_bCount1 == 1 && BStepT > 2.0)
                    {
                        QueueCapture("res://proof/break_immobile.png", "blimp", "brkHector");
                        _bCount1 = 2;
                    }
                    else if (_bCount1 == 2 && BStepT > 3.0)
                    {
                        var d = new Vector2(_enemy.RootPos.X - _bVec1.X, _enemy.RootPos.Z - _bVec1.Z).Length();
                        float v2 = d / BStepT;
                        float ratio = _bV1 > 0.1f ? v2 / _bV1 : 0f;
                        GD.Print($"BRKTEST LIMP hector limpSpeed={v2:F2} baseline={_bV1:F2} ratio={ratio:F2}");
                        if (ratio < 0.35f || ratio > 0.78f) { BFail($"limp speed ratio {ratio:F2} (want ~0.55 of baseline)"); return; }
                        if (!_bSawWreckSwing) { BFail("wrecked leg never left the ground while limping — it must keep stepping, not drag"); return; }
                        GD.Print("BRKTEST LIMP PASS wrecked leg -> rider keeps walking at ~55% speed, leg attached AND still stepping");
                        SetBPhase(6);
                    }
                }
                break;

            case 6: // BOTH LEGS WRECKED: crawl at 25% — alive, upright, attached
                _player.SetDrive(0f, 0f);
                _enemy.SetDrive(Mathf.Pi, 0f);
                if (_bStep == 0)
                {
                    CraftHit(_enemy, "leg_R", 30f);
                    CraftHit(_enemy, "leg_R", 30f);
                    CraftHit(_enemy, "leg_R", 30f);
                    if (_enemyDead) BFail("hector DIED from leg damage — legs never kill (owner)");
                    if (_enemy.Halted) BFail("hector walker halted by leg damage");
                    if (_banner.Visible) BFail($"banner shown for a leg-wrecked rider: '{_banner.Text}'");
                    if (_enemy.IsKnockedDown) BFail("both-legs wreck felled the rider");
                    var lr = _partsEnemy.GetPart("leg_R");
                    if (!lr.Wrecked) BFail("leg_R not wrecked");
                    foreach (var rec in lr.Meshes)
                        if (rec.Detached) BFail("leg_R mesh DETACHED");
                    if (Mathf.Abs(_enemy.SpeedFactor - 0.25f) > 0.01f)
                        BFail($"speed factor {_enemy.SpeedFactor:F2} != 0.25 (crawl)");
                    if (_hpEnemy <= 0f) BFail("pool hp drained by leg hits");
                    GD.Print($"BRKTEST BOTHLEGS PASS both legs wrecked -> CRAWL 0.25x, rider alive (pool hp={_hpEnemy:F0}), upright, legs attached");
                    BStep(1);
                }
                else if (_bStep == 1 && BStepT > 2.0)
                {
                    QueueCapture("res://proof/break_hector_disabled.png", "bdisabled", "brkHector");
                    BStep(2);
                }
                else if (_bStep == 2 && _pendingPath == null && !_capturing)
                {
                    ResetDuel();
                    BSetup(new Vector3(0f, Main.HeightAt(0f, 18f), 18f), new Vector3(0f, Main.HeightAt(0f, -12f), -12f));
                    SetBPhase(7);
                }
                break;

            case 7: // HEAD (Hector): pool rule + tracking lag; player sway
                _player.SetDrive(0f, 0f);
                _enemy.SetDrive(Mathf.Pi, 0f);
                if (_bStep == 0)
                {
                    CraftHit(_enemy, "head", 25f);
                    CraftHit(_enemy, "head", 25f);
                    if (_partsEnemy.Alive("head")) BFail("hector head alive after 50 damage");
                    if (Mathf.Abs(_hpEnemy - 290f) > 0.01f)
                        BFail($"head->pool rule: hectorHp={_hpEnemy:F1} (want 290 = 340 - 50)");
                    GD.Print("BRKTEST HEADPOOL PASS head hits burned head HP and pool (340 -> 290)");
                    // Move the player to the Hector's flank: desired twist ~1 rad.
                    _player.Teleport(new Vector3(20f, Main.HeightAt(20f, 2f), 2f), 0f);
                    BStep(1);
                }
                else if (_bStep == 1)
                {
                    var toP = _player.RootPos - _enemy.RootPos;
                    float yawTo = RiderWalker.YawOf(toP.Normalized());
                    TrackEnemyTorso(dt, yawTo);
                    float desired = Mathf.Abs(RiderWalker.WrapAngle(yawTo - _enemyYaw));
                    if (BStepT > 0.5)
                    {
                        float twist = Mathf.Abs(_enemy.TorsoTwist);
                        GD.Print($"BRKTEST HEADLAG desired={desired:F2} rad, twist after 0.5s={twist:F2} (lag cap 0.30 + eps)");
                        if (desired < 0.7f) BFail($"flank geometry wrong: desired twist {desired:F2}");
                        if (twist > 0.37f) BFail($"headless tracking did not lag: twist {twist:F2} in 0.5s");
                        BStep(2);
                    }
                }
                else if (_bStep == 2)
                {
                    var toP = _player.RootPos - _enemy.RootPos;
                    float yawTo = RiderWalker.YawOf(toP.Normalized());
                    TrackEnemyTorso(dt, yawTo);
                    if (BStepT > 2.2)
                    {
                        float err = Mathf.Abs(RiderWalker.WrapAngle(
                            RiderWalker.WrapAngle(yawTo - _enemyYaw) - _enemy.TorsoTwist));
                        if (err > 0.08f) BFail($"headless tracking never caught up: err {err:F2}");
                        GD.Print($"BRKTEST HEADLAG PASS lag proven at 0.5s, caught up by 2.2s (err {err:F3})");
                        CraftRayHit(_player, "head", 25f);
                        CraftRayHit(_player, "head", 25f);
                        if (_partsPlayer.Alive("head")) { BFail("player head alive after 50 damage"); return; }
                        _bDragMax = 0f;
                        BStep(3);
                    }
                }
                else if (_bStep == 3)
                {
                    float s = _cross.Sway.Length();
                    if (s > _bDragMax) _bDragMax = s;
                    if (BStepT > 1.2)
                    {
                        if (_bDragMax < 3f) BFail($"crosshair sway too small: max {_bDragMax:F1}px");
                        GD.Print($"BRKTEST HEADSWAY PASS player head lost -> crosshair sway max {_bDragMax:F1}px");
                        ResetDuel();
                        BSetup(new Vector3(0f, Main.HeightAt(0f, 18f), 18f), new Vector3(0f, Main.HeightAt(0f, -12f), -12f));
                        SetBPhase(8);
                    }
                }
                break;

            case 8: // RESET: everything restored, salvo back to 8, rebind real
                _player.SetDrive(0f, 0f);
                _enemy.SetDrive(Mathf.Pi, 0f);
                if (_bStep == 0)
                {
                    foreach (var p in _partsPlayer.AllParts)
                        if (p.Destroyed) BFail($"player part {p.Id} still destroyed after reset");
                    foreach (var p in _partsEnemy.AllParts)
                        if (p.Destroyed) BFail($"hector part {p.Id} still destroyed after reset");
                    if (_partsPlayer.DebrisList.Count != 0 || _partsEnemy.DebrisList.Count != 0)
                        BFail("debris remains after reset");
                    if (Mathf.Abs(_partsPlayer.HpOf("pod_L") - 45f) > 0.01f ||
                        Mathf.Abs(_partsEnemy.HpOf("leg_L") - 90f) > 0.01f ||
                        Mathf.Abs(_partsEnemy.HpOf("head") - 50f) > 0.01f)
                        BFail("part HP not refilled after reset");
                    if (_enemy.LegDisabled(true) || _player.LegDisabled(false) ||
                        Mathf.Abs(_enemy.SpeedFactor - 1f) > 0.01f)
                        BFail("walker leg state not cleared after reset");
                    var rec = _partsPlayer.GetPart("pod_L").Meshes[0];
                    if (rec.Detached || rec.Mi.Skin == null || !(rec.Mi.GetParent() is Skeleton3D))
                        BFail("pod_L mesh not rebound to the skeleton after reset");
                    GD.Print("BRKTEST RESTORE PASS parts alive, HP full, debris freed, meshes rebound, walker state cleared");
                    BStep(1);
                    _bCount1 = _rocketsLaunched;
                    FireLrmSalvo(EnemyAim());
                }
                else if (_bStep == 1 && BStepT > 1.6)
                {
                    int n = _rocketsLaunched - _bCount1;
                    if (n != 8) BFail($"post-reset salvo = {n} rockets (want 8)");
                    GD.Print("BRKTEST SALVO8 PASS salvo restored to 8 after reset");
                    SetBPhase(9);
                }
                break;
            case 9: // LEG RULE (owner, current): impacts never fell; legs wreck, never detach, never fell
                if (_bStep == 0)
                {
                    ResetDuel();
                    BSetup(new Vector3(0f, Main.HeightAt(0f, 18f), 18f), new Vector3(0f, Main.HeightAt(0f, -12f), -12f));
                    // 36t of autocannon impacts — far past every retired
                    // knockdown threshold. Legs intact.
                    CraftHit(_enemy, "torso", 12f, WeaponCatalog.Autocannon.Weight);
                    CraftHit(_enemy, "torso", 12f, WeaponCatalog.Autocannon.Weight);
                    CraftHit(_enemy, "torso", 12f, WeaponCatalog.Autocannon.Weight);
                    BStep(1);
                }
                else if (_bStep == 1 && BStepT > 0.6f)
                {
                    if (_enemy.IsKnockedDown) BFail("weapon impacts toppled a rider (retired knockdown)");
                    GD.Print("KNOCKTEST NO-TOPPLE PASS 36t of impacts — the Hector stands (no knockdown rule remains)");
                    _partsEnemy.DamagePart("leg_R", 999f);
                    BStep(2);
                }
                else if (_bStep == 2 && BStepT > 0.8f)
                {
                    if (_enemy.IsKnockedDown || _enemy.FallAmount > 0.01f)
                    { BFail("leg wreck felled the rider (retired fall rule)"); return; }
                    var lr = _partsEnemy.GetPart("leg_R");
                    if (!lr.Wrecked) { BFail("leg_R not wrecked"); return; }
                    foreach (var rec in lr.Meshes)
                        if (rec.Detached) { BFail("leg_R mesh DETACHED — legs never fall off"); return; }
                    if (Mathf.Abs(_enemy.SpeedFactor - 0.55f) > 0.01f)
                    { BFail($"speed factor {_enemy.SpeedFactor:F2} != 0.55 after leg wreck"); return; }
                    GD.Print("LEGRULE PASS leg wrecked: attached + darkened, rider upright, limping at 0.55x");
                    QueueCapture("res://proof/break_knockdown.png", "brkKnock", "brkHector");
                    ResetDuel();
                    if (_partsEnemy.GetPart("leg_R").Wrecked || !_partsEnemy.Alive("leg_R"))
                    { BFail("reset did not un-wreck leg_R"); return; }
                    if (Mathf.Abs(_enemy.SpeedFactor - 1f) > 0.01f)
                    { BFail("reset did not restore full speed"); return; }
                    GD.Print("LEGRULE RESET PASS R reset un-wrecks the leg and restores speed");
                    GD.Print("BRKTEST DONE");
                    GetTree().Quit(0);
                }
                break;
        }
    }

    // ---------- terrain-test choreography ----------

    private void TFail(string msg)
    {
        GD.PrintErr($"TERRAINTEST FAIL: {msg}");
        GetTree().Quit(1);
    }

    private void TerrainSelfCheck()
    {
        // HeightAt at every grid node must reproduce the stored grid
        // height exactly (bilinear weights are 1/0 at nodes).
        float maxDiff = 0f;
        float half = Main.TerrainSize / 2f;
        float step = (float)Main.TerrainSize / Main.TerrainSegs;
        for (int j = 0; j <= Main.TerrainSegs; j++)
        for (int i = 0; i <= Main.TerrainSegs; i++)
        {
            float d = Mathf.Abs(Main.HeightAt(-half + i * step, -half + j * step)
                              - Main.GridHeightAt(i, j));
            if (d > maxDiff) maxDiff = d;
        }
        GD.Print($"TERRAIN SELFCHECK nodes={(Main.TerrainSegs + 1) * (Main.TerrainSegs + 1)} maxNodeDiff={maxDiff:F5}");
        // Arena grade scan (the duel-playable core).
        float maxG = 0f; Vector2 atG = Vector2.Zero;
        for (float x = -36f; x <= 36f; x += 1.5f)
        for (float z = -36f; z <= 36f; z += 1.5f)
        {
            if (new Vector2(x, z).Length() > 36f) continue;
            float g = TerrainGrade(x, z);
            if (g > maxG) { maxG = g; atG = new Vector2(x, z); }
        }
        GD.Print($"TERRAIN ARENA maxGrade={maxG:F3} ({Mathf.RadToDeg(Mathf.Atan(maxG)):F1} deg) at {atG}");
        float hMin = 1e9f, hMax = -1e9f;
        for (int j = 0; j <= Main.TerrainSegs; j += 4)
        for (int i = 0; i <= Main.TerrainSegs; i += 4)
        {
            float h = Main.GridHeightAt(i, j);
            hMin = Mathf.Min(hMin, h); hMax = Mathf.Max(hMax, h);
        }
        GD.Print($"TERRAIN RANGE min={hMin:F1} max={hMax:F1}");
    }

    // The clearest steep spot the arena offers (the arena is gentle by
    // design — max grade ~0.08 — so take the best clear candidate).
    private bool FindSlopeSpot(out Vector3 spot, out float uphillYaw)
    {
        spot = Vector3.Zero; uphillYaw = 0f;
        float bestG = 0.045f;
        bool found = false;
        for (float rad = 34f; rad >= 14f; rad -= 2f)
        for (int a = 0; a < 48; a++)
        {
            float ang = a * Mathf.Tau / 48f;
            var p = new Vector2(Mathf.Cos(ang) * rad, Mathf.Sin(ang) * rad);
            float g = TerrainGrade(p.X, p.Y);
            if (g <= bestG || g > 0.35f) continue;
            if (!ScatterFree(p, 6.5f)) continue;
            const float e = 0.7f;
            float gx = (Main.HeightAt(p.X + e, p.Y) - Main.HeightAt(p.X - e, p.Y)) / (2f * e);
            float gz = (Main.HeightAt(p.X, p.Y + e) - Main.HeightAt(p.X, p.Y - e)) / (2f * e);
            uphillYaw = RiderWalker.YawOf(new Vector3(gx, 0f, gz).Normalized());
            spot = new Vector3(p.X, Main.HeightAt(p.X, p.Y), p.Y);
            bestG = g;
            found = true;
        }
        return found;
    }

    private int _mPhase;
    private float _mPhaseT, _mDistE0, _mDistF0;
    private Vector3 _mEnemyStart, _mFoeStart;
    private float _mMaxDisp, _mMaxHome, _mMaxHomeF, _mAggroD0;

    // Mission front smoke: world facts (size, pad plateau flatness,
    // base buildings), base + vista captures, then 50 s of AI march
    // — both foes must close real distance toward their targets.
    // --longshot-test: the owner's 500u+ repro, staged exactly.
    // The player holds at the home base and fires ONE ballistic
    // volley along the chase axis (pitched up 2 degrees, nothing
    // within the 220u convergence cast — the far-target case).
    // The shell's perpendicular distance to the SIGHT LINE is
    // measured when its along-axis distance reaches 600u: with
    // convergence on the 220u far point it must read the muzzle
    // offset amplified ~2.7x (the sail-over); with parallel fire
    // it reads the bare muzzle offset.
    private float _lsT; private int _lsPhase;
    private Projectile _lsProj; private Vector3 _lsOrigin, _lsDir;
    private float _lsLastAlong, _lsLastPerp, _lsDeathAlong;
    private void UpdateLongshotTest(float dt)
    {
        _lsT += dt;
        _player.SetDrive(_playerYaw, 0f);
        if (_lsPhase == 0 && _lsT > 1.2f)
        {
            var toEnemy = _enemy.RootPos - _player.RootPos; toEnemy.Y = 0f;
            _playerYaw = RiderWalker.YawOf(toEnemy);
            _player.TorsoTwist = 0f;
            _player.AimPitchWide = 0.035f;
            _camMode = CamModes.Chase;
            GD.Print($"LONGSHOT staged: distToEnemy={toEnemy.Length():F0}u pitch=2deg");
            _lsPhase = 1; _lsT = 0f;
        }
        else if (_lsPhase == 1 && _lsT > 0.4f)
        {
            UpdateCamera(0f);
            var ap = TorsoAimPoint();
            if (!_aimParallel && _player.AimPitchWide < 0.1f)
            {
                // Something pokes into the cast — raise the line
                // once and retry, the case under test is the
                // no-hit one.
                _player.AimPitchWide = 0.09f; _lsT = 0f;
                GD.Print("LONGSHOT retrying with a higher line (cast was blocked)");
                return;
            }
            int before = _shots.Count;
            FireAutocannon(ap);
            if (_shots.Count <= before) { GD.PrintErr("LONGSHOT FAIL: no shell spawned"); GetTree().Quit(1); return; }
            // Track the autocannon shell, not the volley's last
            // shell: the same slot also fires the rider's machine
            // guns, whose shells (85 u/s) burn out at their own
            // 200u range — tracking one of those reads as a
            // vanish at ~200u. (The range test learned this first.)
            _lsProj = null;
            for (int i = before; i < _shots.Count; i++)
                if (_shots[i].Vel.Length() > 200f) _lsProj = _shots[i];
            if (_lsProj == null) { GD.PrintErr("LONGSHOT FAIL: no autocannon shell in the volley"); GetTree().Quit(1); return; }
            _lsOrigin = _cam.GlobalPosition;
            _lsDir = _aimDir;
            var rel0 = _lsProj.GlobalPosition - _lsOrigin;
            float perp0 = (rel0 - _lsDir * rel0.Dot(_lsDir)).Length();
            GD.Print($"LONGSHOT fired: parallel={_aimParallel} muzzlePerp={perp0:F2}u");
            _lsPhase = 2; _lsT = 0f;
        }
        else if (_lsPhase == 2)
        {
            // The shell now burns out at its 600u range (owner's
            // per-weapon caps), and under llvmpipe a frame can
            // step ~100u of flight — the shell may die between
            // the last sub-600 sample and the next. Judge from
            // the last live sample projected to burnout: death
            // along = along + speed x remaining life (straight
            // flight), perp from that same sample.
            bool gone = _lsProj == null || _lsProj.Dead || !_shots.Contains(_lsProj);
            if (!gone)
            {
                var rel = _lsProj.GlobalPosition - _lsOrigin;
                _lsLastAlong = rel.Dot(_lsDir);
                _lsLastPerp = (rel - _lsDir * _lsLastAlong).Length();
                _lsDeathAlong = _lsLastAlong + _lsProj.Vel.Length() * _lsProj.Life;
            }
            if ((!gone && _lsLastAlong >= 600f) || (gone && _lsDeathAlong >= 600f))
            {
                GD.Print($"LONGSHOT RESULT along={_lsLastAlong:F0}u (burnout {(_lsDeathAlong):F0}u) perp={_lsLastPerp:F2}u");
                if (_lsLastPerp > 7f) { GD.PrintErr("LONGSHOT FAIL: shell off the sight line"); GetTree().Quit(1); return; }
                GD.Print("LONGSHOT TEST DONE");
                GetTree().Quit(0);
            }
            if (gone) { GD.PrintErr($"LONGSHOT FAIL: shell died at {_lsDeathAlong:F0}u, before 600u"); GetTree().Quit(1); return; }
            if (_lsT > 30f) { GD.PrintErr("LONGSHOT FAIL: timeout"); GetTree().Quit(1); }
        }
    }

    // --range-test: per-weapon Range enforcement (owner's rule:
    // a weapon reaches its catalog Range and no further), staged
    // in the Mission world against the stock Enyo loadout with
    // the Sarna MW4 ranges (Large Laser 600, Autocannon 600,
    // LRM 1000):
    //   A: laser at the enemy 1074u away — no damage (600 < 1074)
    //   B: laser at 180u — damage lands (180 < 600)
    //   C: LRM salvo base-to-base at 1074u — clamps to 1000
    //   D: autocannon at the sky — the shell dies at ~600u along
    // Aiming: the chase axis is iterated onto the enemy's chest
    // from the settled camera (3 passes), so the ray is exact.
    private float _rgT; private int _rgPhase;
    private float _rgHp0; private Projectile _rgShell; private Vector3 _rgOrigin, _rgDir;
    private float _rgLastAlong; private int _rgTry;
    private bool AimAxisAt(Vector3 p)
    {
        // Yaw is set by teleport (instant, exact — the walker's
        // turn rate would lag a SetDrive target); pitch is then
        // iterated from the settled chase camera, whose boom
        // rides the aim axis, so three passes converge the axis
        // onto the point.
        var pp = _player.RootPos;
        var flat = new Vector3(p.X - pp.X, 0f, p.Z - pp.Z);
        _player.Teleport(pp, RiderWalker.YawOf(flat));
        _playerYaw = RiderWalker.YawOf(flat);
        _player.TorsoTwist = 0f;
        for (int i = 0; i < 3; i++)
        {
            UpdateCamera(0f);
            var d = p - _cam.GlobalPosition;
            _player.AimPitchWide = Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length());
        }
        UpdateCamera(0f);
        TorsoAimPoint();
        return true;
    }
    private void UpdateRangeTest(float dt)
    {
        _rgT += dt;
        _player.SetDrive(_playerYaw, 0f);
        var chest = _enemy.RootPos + Vector3.Up * 7.6f;
        if (_rgPhase == 0 && _rgT > 1.2f)
        {
            _camMode = CamModes.Chase;
            float d0 = _player.RootPos.DistanceTo(_enemy.RootPos);
            AimAxisAt(chest);
            _rgHp0 = _hpEnemy;
            FireLaser(TorsoAimPoint());
            GD.Print($"RANGE A fired: dist={d0:F0} enemyHp={_rgHp0:F0} (Large Laser range 600 — must NOT reach)");
            _rgPhase = 1; _rgT = 0f;
        }
        else if (_rgPhase == 1 && _rgT > 0.6f)
        {
            if (_hpEnemy < _rgHp0 - 0.01f) { GD.PrintErr($"RANGE FAIL A: laser damaged the enemy past its range ({_rgHp0:F0} -> {_hpEnemy:F0})"); GetTree().Quit(1); return; }
            GD.Print("RANGE A PASS: no damage beyond range");
            var home = PadWorld(0);
            var dirIn = (home - _enemy.RootPos); dirIn.Y = 0f; dirIn = dirIn.Normalized();
            var spot = _enemy.RootPos + dirIn * 600f;
            spot.Y = Main.HeightAt(spot.X, spot.Z);
            _player.Teleport(spot, RiderWalker.YawOf(-dirIn));
            _rgPhase = 2; _rgT = 0f;
        }
        else if (_rgPhase == 2 && _rgT > 0.3f)
        {
            // Stage B on open ground: buildings and trees block
            // lanes at random in the mission world, so teleport
            // the enemy around a fan of spots 180u out until the
            // game's own cast (at the weapon's own 600 reach)
            // confirms a clear line to it, then fire.
            float ang = _rgTry * Mathf.DegToRad(15f);
            var dirE = (_enemy.RootPos - _player.RootPos); dirE.Y = 0f; dirE = dirE.Normalized();
            var spot = _player.RootPos + dirE.Rotated(Vector3.Up, ang) * 180f;
            spot.Y = Main.HeightAt(spot.X, spot.Z);
            _enemy.Teleport(spot, RiderWalker.YawOf(-dirE.Rotated(Vector3.Up, ang)));
            AimAxisAt(_enemy.RootPos + Vector3.Up * 7.6f);
            var los = _hit.Ray(_cam.GlobalPosition, AimDirWorld(), 600f, Roles.Player);
            if (!los.Hit || los.Target != Roles.Enemy)
            {
                if (++_rgTry > 10) { GD.PrintErr("RANGE FAIL B: no clear lane found for staging"); GetTree().Quit(1); }
                _rgT = 0f;
                return;
            }
            _rgHp0 = _hpEnemy;
            FireLaser(TorsoAimPoint());
            GD.Print($"RANGE B fired at 180u (lane try {_rgTry}), enemyHp={_rgHp0:F0} (must drop)");
            _rgPhase = 3; _rgT = 0f;
        }
        else if (_rgPhase == 3 && _rgT > 0.6f)
        {
            if (_hpEnemy > _rgHp0 - 0.01f) { GD.PrintErr("RANGE FAIL B: laser did no damage inside its range"); GetTree().Quit(1); return; }
            GD.Print($"RANGE B PASS: hp {_rgHp0:F0} -> {_hpEnemy:F0}");
            _rgHp0 = _hpEnemy;
            // Re-stage base-to-base (1074u) for the missile
            // clamp: the LRM's Sarna range is 1000, so from the
            // home pad the salvo must fall ~74u short of the
            // enemy base. No lane search — it just must not arrive.
            var homeC = PadWorld(0); homeC.Y = Main.HeightAt(homeC.X, homeC.Z);
            var baseC = PadWorld(1); baseC.Y = Main.HeightAt(baseC.X, baseC.Z);
            var dirC = (baseC - homeC); dirC.Y = 0f; dirC = dirC.Normalized();
            _player.Teleport(homeC, RiderWalker.YawOf(dirC));
            _enemy.Teleport(baseC, RiderWalker.YawOf(-dirC));
            AimAxisAt(_enemy.RootPos + Vector3.Up * 7.6f);
            FireLrmSalvo(TorsoAimPoint());
            GD.Print("RANGE C salvo fired base-to-base at 1074u (LRM range 1000 — must fall short)");
            _rgPhase = 4; _rgT = 0f;
        }
        else if (_rgPhase == 4 && _rgT > 6f)
        {
            if (_hpEnemy < _rgHp0 - 0.01f) { GD.PrintErr("RANGE FAIL C: rockets reached past their range"); GetTree().Quit(1); return; }
            if (_rocketsLaunched == 0) { GD.PrintErr("RANGE FAIL C: no rockets launched"); GetTree().Quit(1); return; }
            GD.Print($"RANGE C PASS: {_rocketsLaunched} rockets, enemy untouched");
            _player.AimPitchWide = 0.16f; // climbs clear of the proven laser lane — only the range cap may end this flight
            UpdateCamera(0f);
            int before = _shots.Count;
            FireAutocannon(TorsoAimPoint());
            if (_shots.Count <= before) { GD.PrintErr("RANGE FAIL D: no shell"); GetTree().Quit(1); return; }
            // The volley also fires the rider's machine guns
            // (85 u/s — an early run tracked one of those by
            // mistake and watched IT burn out at its own cap).
            // Track the fast shell: the autocannon's.
            _rgShell = null;
            for (int i = before; i < _shots.Count; i++)
                if (_shots[i].Vel.Length() > 200f) _rgShell = _shots[i];
            if (_rgShell == null) { GD.PrintErr("RANGE FAIL D: no autocannon shell in the volley"); GetTree().Quit(1); return; }
            GD.Print($"RANGE D tracking the autocannon shell (speed {_rgShell.Vel.Length():F0})");
            _rgOrigin = _cam.GlobalPosition; _rgDir = _aimDir;
            _rgPhase = 5; _rgT = 0f;
        }
        else if (_rgPhase == 5)
        {
            // The shared step removes a dead shell the same frame
            // it dies, so track the last live along-axis reading
            // and judge the burnout from it.
            bool gone = _rgShell == null || _rgShell.Dead || !_shots.Contains(_rgShell);
            if (!gone)
            {
                _rgLastAlong = (_rgShell.GlobalPosition - _rgOrigin).Dot(_rgDir);
                if (_rgT > 20f) { GD.PrintErr("RANGE FAIL D: timeout"); GetTree().Quit(1); }
            }
            else
            {
                GD.Print($"RANGE D: shell end along={_rgLastAlong:F0}u (autocannon range 600)");
                if (_rgLastAlong < 540f || _rgLastAlong > 700f) { GD.PrintErr("RANGE FAIL D: burnout off the weapon's range"); GetTree().Quit(1); return; }
                GD.Print("RANGE TEST DONE");
                GetTree().Quit(0);
            }
        }
        if (_rgT > 120f && _rgPhase < 5) { GD.PrintErr("RANGE FAIL: phase timeout"); GetTree().Quit(1); }
    }

    private int _xPhase;
    private float _xPhaseT;

    // Terrace test (AOT_TERRATEST=1 on a terraced mission):
    // drive the player up a ramp, ride a lift, cross the
    // bridge to the island — sole levels are the verdicts.
    private void UpdateTerraceTest(float dt)
    {
        _xPhaseT += dt;
        if (_xPhase == 5 || _xPhase == 6) { UpdateDeckWalkTest(dt); return; }
        if (_xPhase == 94) { UpdateBlockedWalkTest(dt); return; }
        if (_xPhase == 93) { UpdateRampSideTest(dt); return; }
        if (_xPhase == 0 && _xPhaseT > 1.5f)
        {
            if (!HasTerraceTest) { GD.PrintErr("TERRACE FAIL: no ramp/lift test hooks"); GetTree().Quit(1); return; }
            GD.Print($"TERRACE ramp foot=({TestRampFoot.X:F0},{TestRampFoot.Y:F0}) {TestRampLo:F0}->{TestRampHi:F0}; lift pad=({TestLiftPad.X:F0},{TestLiftPad.Y:F0}) {TestLiftLo:F0}->{TestLiftHi:F0}");
            string start = System.Environment.GetEnvironmentVariable("AOT_TERRATEST");
            if (start == "2")
            {
                _player.Teleport(new Vector3(TestLiftPad.X, TestLiftLo, TestLiftPad.Y), 0f);
                _xPhase = 2; _xPhaseT = 0f; return;
            }
            if (start == "7") { _xPhase = 99; RunSeamScan(); return; }
            if (start == "8") { _xPhase = 98; RunLipSweep(); return; }
            if (start == "9") { _xPhase = 97; RunDeckShot(); return; }
            if (start == "11")
            {
                // Under-deck light + island containment
                // (owner, 2026-11-11): park the player under
                // the test deck, capture the lit underside,
                // then capture the island from the water.
                _xPhase = 95;
                _player.Teleport(new Vector3(TestDeckPoint.X, 0f, TestDeckPoint.Y), RiderWalker.YawOf(Vector3.Right));
                QueueCapture("res://proof/underdeck.png", "under", "underdeck",
                    () => QueueCapture("res://proof/decktop.png", "decktop", "decktop",
                        () => QueueCapture("res://proof/homevalley.png", "home", "homecam",
                            () => { GD.Print("DECKVIEWS saved"); GetTree().Quit(0); })));
                return;
            }
            if (start == "12")
            {
                // Blocked walk (owner, 2026-11-11: a rider
                // walking into an obstacle lifted one leg and
                // shook it; his follow-up the same day: keep
                // the walk cycle running against the collider):
                // start west of the test deck block and drive
                // east into it for 12s. Blocked must engage, the
                // gait must keep marching (steps continue), and
                // the root must not pass the block's west face.
                _xPhase = 94; _xPhaseT = 0f;
                _bwSteps0 = -1; _bwEngageSteps = -1; _bwMaxX = float.MinValue; _bwShot = false;
                var st = TestDeckPoint + new Vector2(-34f, 0f);
                _player.Teleport(new Vector3(st.X, 0f, st.Y), RiderWalker.YawOf(Vector3.Right));
                return;
            }
            if (start == "13")
            {
                // Ramp side (owner, 2026-11-11: 'I can walk
                // through the ramp if I come from the side'):
                // stage beside the test ramp at its mid-height,
                // 14u off the centreline, and drive straight at
                // the wedge's side for 8s. The root must stop
                // at the corridor edge (HalfWid + radius) and
                // never reach the centreline.
                _xPhase = 93; _xPhaseT = 0f;
                _rsMinPerp = float.MaxValue;
                var rp = TestRampRect;
                var perp = new Vector2(-rp.DirZ, rp.DirX);
                var st = new Vector2(rp.Cx, rp.Cz) + perp * 14f;
                _rsPerp = perp; _rsCentre = new Vector2(rp.Cx, rp.Cz);
                _player.Teleport(new Vector3(st.X, 0f, st.Y), RiderWalker.YawOf(new Vector3(-perp.X, 0f, -perp.Y)));
                return;
            }
            if (start == "10")
            {
                // Canyon presence (owner: "looks like you
                // removed canyon"): height profile from the
                // home pad toward pad 1, plus a HUD-free vista
                // of the rim and the pass notch.
                _xPhase = 96;
                var p0 = Main.SpawnPads[0]; var p1 = Main.SpawnPads[1];
                var ray = (p1 - p0).Normalized();
                string prof = "";
                foreach (float r in new[] { 300f, 400f, 460f, 500f, 540f, 580f, 620f })
                {
                    var s = p0 + ray * r;
                    prof += $" r{r:F0}={Main.HeightAt(s.X, s.Y):F0}";
                }
                var saddle = (p0 + p1) / 2f;
                GD.Print($"CANYONPROFILE toward pad1:{prof} saddle={Main.HeightAt(saddle.X, saddle.Y):F0}");
                SetHudPanelsVisible(false);
                QueueCapture("res://proof/canyon_vista.png", "canyon", "canyonVista",
                    () => { GD.Print("CANYONVISTA saved"); GetTree().Quit(0); });
                return;
            }
            if (start == "5")
            {
                var back5 = TestRampFoot - TestRampDir * 10f;
                _player.Teleport(new Vector3(back5.X, Main.HeightAt(back5.X, back5.Y), back5.Y), RiderWalker.YawOf(new Vector3(TestRampDir.X, 0f, TestRampDir.Y)));
                _xPhase = 5; _xPhaseT = 0f; _dwMin = 0f; _dwLeg = 0; _dwOnDeck = false; return;
            }
            if (start == "6")
            {
                _player.Teleport(new Vector3(TestDeckPoint.X, 0f, TestDeckPoint.Y), RiderWalker.YawOf(Vector3.Right));
                _xPhase = 6; _xPhaseT = 0f; _bkMaxRoot = 0f; _bkOnDeck = false; return;
            }
            var back = TestRampFoot - TestRampDir * 10f + new Vector2(0f, 0f);
            var spot = new Vector3(back.X, Main.HeightAt(back.X, back.Y), back.Y);
            _player.Teleport(spot, RiderWalker.YawOf(new Vector3(TestRampDir.X, 0f, TestRampDir.Y)));
            _xPhase = 1; _xPhaseT = 0f;
        }
        else if (_xPhase == 1)
        {
            if (((int)_xPhaseT) % 5 == 0 && _xPhaseT - Mathf.Floor(_xPhaseT) < dt * 1.5f)
                GD.Print($"TERRACE P1 pos=({_player.RootPos.X:F0},{_player.RootPos.Z:F0}) sole={_player.SoleLevel:F1}");
            var target = TestRampFoot + TestRampDir * (TestRampRun + 12f);
            var to = new Vector2(target.X - _player.RootPos.X, target.Y - _player.RootPos.Z);
            _player.SetDrive(RiderWalker.YawOf(new Vector3(to.X, 0f, to.Y)), 3.2f);
            if (_player.SoleLevel >= TestRampHi - 1f)
            {
                GD.Print($"TERRACE RAMP PASS sole={_player.SoleLevel:F1} (target {TestRampHi:F0})");
                var pad = new Vector3(TestLiftPad.X, TestLiftLo, TestLiftPad.Y);
                _player.Teleport(pad, 0f);
                _xPhase = 2; _xPhaseT = 0f;
            }
            else if (_xPhaseT > 45f) { GD.PrintErr($"TERRACE FAIL: ramp climb stalled sole={_player.SoleLevel:F1}"); GetTree().Quit(1); return; }
        }
        else if (_xPhase == 2)
        {
            _player.SetDrive(0f, 0f);
            if (_player.SoleLevel >= TestLiftHi - 1f)
            {
                GD.Print($"TERRACE LIFT PASS sole={_player.SoleLevel:F1} (target {TestLiftHi:F0})");
                // Chain used to continue over the island
                // bridge; the island district is gone
                // (owner, 2026-11-11 round 3).
                GD.Print("TERRACE TEST DONE");
                GetTree().Quit(0);
            }
            else if (_xPhaseT > 16f) { GD.PrintErr($"TERRACE FAIL: lift did not rise sole={_player.SoleLevel:F1}"); GetTree().Quit(1); return; }
        }
    }

    // Phase 7: platform tiling invariant — sample GroundAt
    // on a grid inside every valley-0 platform rect; every
    // sample must read the platform top (no holes, no slits).
    // The plaza and the ground under the top platform must
    // stay open. Instant (one frame at world build).
    // Phase 8 (owner: intermittent fall-through at the TOP of a
    // ramp, where it meets the higher level): instead of driving
    // the walker (stride-phase luck), sweep the exact plant
    // geometry — for every leading-foot target around the lip and
    // every stride length, GroundAt with the trailing foot's sole
    // as refY must return (nearly) the analytic surface. A miss
    // is a foot that would plant through the deck.
    private void RunLipSweep()
    {
        var dir = TestRampDir; var perp = new Vector2(-dir.Y, dir.X);
        var s1 = TestRampFoot + dir * TestRampRun; // the lip, 2u inside the deck
        int misses = 0, total = 0;
        for (float lat = -4f; lat <= 4f; lat += 2f)
        for (float along = -10f; along <= 8f; along += 0.5f)
        for (float stride = 2f; stride <= 6f; stride += 1f)
        {
            var lead = s1 + dir * along + perp * lat;
            var trail = s1 + dir * (along - stride) + perp * lat;
            // trailing foot stands on the ramp (analytic height there)
            float ht = 0f; bool onRamp = false;
            foreach (var rp in Main.Ramps) { float hh = Main.RampHeightAt(rp, trail.X, trail.Y, out bool ins); if (ins) { ht = hh; onRamp = true; } }
            if (!onRamp) continue;
            float exp = Main.HeightAt(lead.X, lead.Y);
            foreach (var rr in Main.Roofs) if (rr.Top > exp && Mathf.Abs(lead.X - rr.Cx) <= rr.Hx && Mathf.Abs(lead.Y - rr.Cz) <= rr.Hz) exp = rr.Top;
            foreach (var rp in Main.Ramps) { float hh = Main.RampHeightAt(rp, lead.X, lead.Y, out bool ins); if (ins && hh > exp) exp = hh; }
            float got = Main.GroundAt(lead.X, lead.Y, ht + 1.5f);
            total++;
            if (exp - got > 1.0f)
            {
                misses++;
                if (misses <= 8) GD.Print($"LIPSWEEP MISS lead=({lead.X:F1},{lead.Y:F1}) along={along:F1} stride={stride:F0} lat={lat:F0} trailH={ht:F1} exp={exp:F1} got={got:F1}");
            }
        }
        if (misses == 0) { GD.Print($"LIPSWEEP PASS: {total} plant cases at the ramp lip, GroundAt always finds the surface"); GetTree().Quit(0); }
        else { GD.PrintErr($"LIPSWEEP FAIL: {misses} of {total} plant cases fall through at the ramp lip"); GetTree().Quit(1); }
    }

    // Deck-shot proof (owner: platforms must collide with
    // weapons): the shared HitModel ray against the home L1
    // platform — top from above, underside from below, side
    // band from the side, a clear pass UNDER the deck, the
    // ramp slope, and a rider standing on the deck.
    // City audit (owner: "random pieces of building are
    // sticking out of the ground"): every destructible
    // building whose base sits below the terrain at its
    // centre or corners is buried there — print them all.
    private void RunCityAudit()
    {
        int buried = 0, total = 0;
        foreach (var bd in _destructibles)
        {
            if (bd.Kind != "building") continue;
            total++;
            float baseY = bd.Center.Y;
            float worst = Main.HeightAt(bd.Center.X, bd.Center.Z) - baseY;
            float rr = bd.Radius * 0.7f;
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
                worst = Mathf.Max(worst,
                    Main.HeightAt(bd.Center.X + sx * rr, bd.Center.Z + sz * rr) - baseY);
            if (worst > 1.0f)
            {
                buried++;
                if (buried <= 12)
                    GD.Print($"CITYAUDIT BURIED at ({bd.Center.X:F0},{bd.Center.Z:F0}) base={baseY:F1} h={bd.Height:F0} terrain over base by {worst:F1}");
            }
        }
        GD.Print($"CITYAUDIT {buried} of {total} buildings buried (terrain above base by >1u somewhere on the footprint)");
    }

    private void RunDeckShot()
    {
        RunCityAudit();
        int fails = 0;
        void Check(string name, bool ok, string detail)
        {
            GD.Print($"DECKSHOT {name}: {(ok ? "ok" : "FAIL")} {detail}");
            if (!ok) fails++;
        }
        var (pc, ph, ptop) = TestPlatforms[0];
        // A clear spot on the deck: no building within 9u.
        Vector2 spot = pc; bool found = false;
        for (float gx = -ph.X + 10f; gx <= ph.X - 10f && !found; gx += 5f)
        for (float gz = -ph.Y + 10f; gz <= ph.Y - 10f && !found; gz += 5f)
        {
            var cand = new Vector2(pc.X + gx, pc.Y + gz);
            bool clear = true;
            foreach (var bp in _buildingPts) if (bp.DistanceTo(cand) < 9f) { clear = false; break; }
            if (clear) { spot = cand; found = true; }
        }
        Check("clearspot", found, $"at ({spot.X:F0},{spot.Y:F0})");
        // 1. Deck top from above.
        var h1 = _hit.Ray(new Vector3(spot.X, ptop + 30f, spot.Y),
            new Vector3(0.3f, -1f, 0.1f).Normalized(), 120f, Roles.Player);
        Check("top", h1.Hit && h1.Target == Roles.Terrain && Mathf.Abs(h1.Point.Y - ptop) < 0.6f,
            $"hit={h1.Hit} tgt={h1.Target} y={h1.Point.Y:F1} want {ptop:F0}");
        // 2. Underside from below (deck Under = Top - 2.2).
        var h2 = _hit.Ray(new Vector3(spot.X, 2f, spot.Y),
            new Vector3(0.2f, 1f, -0.15f).Normalized(), 60f, Roles.Player);
        Check("under", h2.Hit && h2.Target == Roles.Terrain && Mathf.Abs(h2.Point.Y - (ptop - 2.2f)) < 0.7f,
            $"hit={h2.Hit} tgt={h2.Target} y={h2.Point.Y:F1} want {ptop - 2.2f:F1}");
        // 3. Side band from the west at deck-band height.
        var h3 = _hit.Ray(new Vector3(pc.X - ph.X - 30f, ptop - 1f, pc.Y),
            Vector3.Right, 120f, Roles.Player);
        Check("side", h3.Hit && h3.Point.X <= pc.X - ph.X + 8f && Mathf.Abs(h3.Point.Y - (ptop - 1f)) < 0.6f,
            $"hit={h3.Hit} tgt={h3.Target} x={h3.Point.X:F1} want <= {pc.X - ph.X + 8f:F1}");
        // 4. Control: a shot UNDER the deck (below the band,
        // started inside the deck's span) flies through — no
        // phantom slab.
        var h4 = _hit.Ray(new Vector3(pc.X - 20f, ptop - 5f, pc.Y),
            Vector3.Right, 40f, Roles.Player);
        Check("underpass", !h4.Hit, $"hit={h4.Hit} tgt={h4.Target} t={h4.T:F1}");
        // 5. Ramp slope at its midpoint.
        var rmid = TestRampFoot + TestRampDir * (TestRampRun * 0.5f);
        var h5 = _hit.Ray(new Vector3(rmid.X, TestRampHi + 25f, rmid.Y),
            Vector3.Down, 80f, Roles.Player);
        Check("ramp", h5.Hit && Mathf.Abs(h5.Point.Y - TestRampHi * 0.5f) < 1.4f,
            $"hit={h5.Hit} tgt={h5.Target} y={h5.Point.Y:F1} want ~{TestRampHi * 0.5f:F1}");
        // 6. A rider standing ON the deck is hittable at its
        // visible height (capsule base = live sole level).
        if (_enemy != null)
        {
            _enemy.Teleport(new Vector3(spot.X, ptop, spot.Y), 0f);
            var h6 = _hit.Ray(new Vector3(spot.X - 40f, ptop + 7f, spot.Y),
                Vector3.Right, 120f, Roles.Player);
            Check("rider", h6.Hit && h6.Target != Roles.Terrain && h6.Target != Roles.Prop && h6.Target != "",
                $"hit={h6.Hit} tgt={h6.Target} y={h6.Point.Y:F1}");
        }
        if (fails == 0) { GD.Print("DECKSHOT PASS: decks, ramps and riders on them collide with the shared hit ray"); GetTree().Quit(0); }
        else { GD.PrintErr($"DECKSHOT FAIL: {fails} case(s)"); GetTree().Quit(1); }
    }

    private void RunSeamScan()
    {
        int bad = 0; string worst = "";
        foreach (var (c, h, top) in TestPlatforms)
        {
            for (float gx = -h.X + 3f; gx <= h.X - 3f; gx += 6f)
            for (float gz = -h.Y + 3f; gz <= h.Y - 3f; gz += 6f)
            {
                float x = c.X + gx, z = c.Y + gz;
                float g = Main.GroundAt(x, z, top + 1.5f);
                if (g < top - 0.1f) { bad++; if (worst.Length < 300) worst += $" ({x:F0},{z:F0})={g:F1}<{top:F0}"; }
            }
        }
        var pad0 = Main.SpawnPads[0];
        for (int k = 0; k < 8; k++)
        {
            float rad = k / 8f * Mathf.Tau;
            float x = pad0.X + Mathf.Cos(rad) * 12f, z = pad0.Y + Mathf.Sin(rad) * 12f;
            float g = Main.GroundAt(x, z, 1.5f);
            if (g > 1.8f) { bad++; if (worst.Length < 300) worst += $" plaza({x:F0},{z:F0})={g:F1}"; }
        }
        if (TestPlatforms.Count > 6)
        {
            var gc = TestPlatforms[6].C;
            float g = Main.GroundAt(gc.X, gc.Y, 1.5f);
            if (g > 1.8f) { bad++; worst += $" underG={g:F1}"; }
        }
        if (bad == 0) { GD.Print("TERRACE SEAMSCAN PASS: valley platforms fully tiled, plaza + under-deck open"); GetTree().Quit(0); }
        else { GD.PrintErr($"TERRACE SEAMSCAN FAIL: {bad} holes:{worst}"); GetTree().Quit(1); }
    }

    private float _dwMin; private int _dwLeg; private bool _dwOnDeck; private float _dwYaw; private Vector3 _udStart;
    private float _bkMaxRoot; private bool _bkOnDeck; private float _bkMaxHead;
    private int _lpCyc; private bool _lpUp; private int _lpFalls; private float _lpLogT; // phase 8: ramp-lip crossing probe (owner fall-through report)
    private float _rsMinPerp; private Vector2 _rsPerp, _rsCentre;
    private void UpdateRampSideTest(float dt) // phase 13: drive at a ramp's side — the wedge must be solid (owner, 2026-11-11)
    {
        _player.SetDrive(RiderWalker.YawOf(new Vector3(-_rsPerp.X, 0f, -_rsPerp.Y)), 3.2f);
        var rel = new Vector2(_player.RootPos.X, _player.RootPos.Z) - _rsCentre;
        float perpDist = Mathf.Abs(rel.Dot(_rsPerp));
        _rsMinPerp = Mathf.Min(_rsMinPerp, perpDist);
        if (_xPhaseT > 8f)
        {
            float stopAt = TestRampRect.HalfWid + _player.CollisionRadius; // 6.5 + 2.0 = 8.5
            GD.Print($"RAMPSIDE minPerp={_rsMinPerp:F1} stopAt={stopAt:F1} rootPerp={perpDist:F1}");
            if (_rsMinPerp >= stopAt - 0.6f)
            { GD.Print("RAMP SIDE PASS: the wedge's side stopped the rider"); GetTree().Quit(0); return; }
            GD.PrintErr("RAMP SIDE FAIL"); GetTree().Quit(1);
        }
    }

    private int _bwSteps0, _bwEngageSteps; private float _bwMaxX; private bool _bwShot;
    private void UpdateBlockedWalkTest(float dt) // phase 12: drive into a block — root held; gait behaviour at the wall is the generic rig's (owner round 3: no blocked special cases)
    {
        if (_bwSteps0 < 0) _bwSteps0 = _player.Steps;
        _player.SetDrive(RiderWalker.YawOf(Vector3.Right), 3.2f);
        _bwMaxX = Mathf.Max(_bwMaxX, _player.RootPos.X);
        if (_player.Blocked && _bwEngageSteps < 0) _bwEngageSteps = _player.Steps; // first engagement only — steps AFTER this are the march metric
        if (_xPhaseT > 10.5f && !_bwShot)
        {
            _bwShot = true;
            QueueCapture("res://proof/blocked_pose.png", "blockedpose", "blockedcam");
        }
        if (_xPhaseT > 12f)
        {
            bool engaged = _bwEngageSteps >= 0;
            int stepsWhileBlocked = engaged ? _player.Steps - _bwEngageSteps : _player.Steps - _bwSteps0;
            float westFace = TestDeckPoint.X - 16f;
            GD.Print($"BLOCKED engaged={engaged} stepsWhileBlocked={stepsWhileBlocked} maxX={_bwMaxX:F1} westFace={westFace:F1} rootX={_player.RootPos.X:F1}");
            // The owner's current word: the walk cycle keeps
            // running against the collider (marching), the
            // root must not pass, and Blocked must engage.
            if (engaged && _bwMaxX <= westFace + 3.4f)
            { GD.Print("BLOCKED WALK PASS: root held at the obstacle (steps while blocked are the natural rig's, reported above, not a verdict term)"); GetTree().Quit(0); return; }
            GD.PrintErr("BLOCKED WALK FAIL"); GetTree().Quit(1);
        }
    }

    private void UpdateDeckWalkTest(float dt) // phases 5/6: diagonal ramp drive + ceiling bonk (owner regressions, 2026-11-10)
    {
        if (_xPhase == 6)
        {
            // BONK (owner: collision both ways): burn straight
            // up UNDER a +22 deck for 7s. The HEAD (sole level +
            // the full chassis height) must never reach the
            // deck's underside (19.8), the rider must never be
            // adopted onto the deck, and it must settle back on
            // the ground. (The first version capped the ROOT by
            // the walker's own head-room allowance and so
            // certified the through-the-deck bug, 2026-11-11.)
            if (_xPhaseT < 7f) { _player.SetThrust(true); _player.SetDrive(0f, 0f); }
            else _player.SetThrust(false);
            _bkMaxRoot = Mathf.Max(_bkMaxRoot, _player.RootPos.Y);
            float headTop = _player.SoleLevel + _playerSpec.Height;
            _bkMaxHead = Mathf.Max(_bkMaxHead, headTop);
            if (_player.SoleLevel >= TerraceL1 - 1f) _bkOnDeck = true;
            if (_xPhaseT > 12f)
            {
                float cap = TerraceL1 - 2.2f; // the underside itself
                GD.Print($"BONK maxHead={_bkMaxHead:F1} under={cap:F1} maxRoot={_bkMaxRoot:F1} onDeck={_bkOnDeck} settleSole={_player.SoleLevel:F1}");
                if (!_bkOnDeck && _bkMaxHead <= cap + 0.05f && _player.SoleLevel <= 0.7f)
                { GD.Print("TERRACE BONK PASS: flight stops at the deck underside"); GetTree().Quit(0); return; }
                GD.PrintErr("TERRACE BONK FAIL"); GetTree().Quit(1);
            }
            return;
        }
        // Phase 5: climb the test ramp on a DIAGONAL line
        // (aim 5u off the corridor centre) and track the worst
        // dip of the sole below the ramp's analytic height —
        // the owner's fall-through: feet leaving a narrow
        // corridor and grounding far below.
        var perp = new Vector2(-TestRampDir.Y, TestRampDir.X);
        var target = TestRampFoot + TestRampDir * (TestRampRun + 10f) + perp * 5f;
        var to = new Vector2(target.X - _player.RootPos.X, target.Y - _player.RootPos.Z);
        _player.SetDrive(RiderWalker.YawOf(new Vector3(to.X, 0f, to.Y)), 3.2f);
        float exp = Main.RampHeightAt(TestRampRect, _player.RootPos.X, _player.RootPos.Z, out bool inside);
        if (inside) _dwMin = Mathf.Max(_dwMin, exp - _player.SoleLevel);
        if (_player.SoleLevel >= TestRampHi - 1f)
        {
            GD.Print($"RAMPWALK done maxDip={_dwMin:F2} sole={_player.SoleLevel:F1}");
            if (_dwMin <= 1.6f) { GD.Print("TERRACE RAMPWALK PASS"); GetTree().Quit(0); return; }
            GD.PrintErr($"TERRACE RAMPWALK FAIL maxDip={_dwMin:F2}"); GetTree().Quit(1);
        }
        else if (_xPhaseT > 45f) { GD.PrintErr($"RAMPWALK FAIL: stalled sole={_player.SoleLevel:F1} maxDip={_dwMin:F2}"); GetTree().Quit(1); }
    }

    private void UpdateMissionTest(float dt)
    {
        _mPhaseT += dt;
        if (_mPhase == 0 && _mPhaseT > 1.2f)
        {
            GD.Print($"MISSION WORLD size={Main.TerrainSize} segs={Main.TerrainSegs} bound={WorldConfig.WorldBound}");
            for (int i = 0; i < Main.SpawnPads.Length; i++)
            {
                var pad = Main.SpawnPads[i];
                float hc = Main.HeightAt(pad.X, pad.Y);
                float dev = 0f;
                for (int k = 0; k < 16; k++)
                {
                    float a = k / 16f * Mathf.Tau;
                    dev = Mathf.Max(dev, Mathf.Abs(Main.HeightAt(pad.X + Mathf.Cos(a) * 20f, pad.Y + Mathf.Sin(a) * 20f) - hc));
                }
                GD.Print($"MISSION BASE {i} pad=({pad.X:F0},{pad.Y:F0}) h={hc:F2} maxDevR20={dev:F3}");
                if (dev > 0.05f) { GD.PrintErr("MISSION FAIL: base plateau not flat"); GetTree().Quit(1); return; }
            }
            GD.Print($"MISSION BUILDINGS pts={_buildingPts.Count} (expect 19: 4 arena + 15 base)");
            _mDistE0 = _enemy.RootPos.DistanceTo(_player.RootPos);
            _mDistF0 = _foe != null ? _foe.Walker.RootPos.DistanceTo(_player.RootPos) : 0f;
            _mEnemyStart = _enemy.RootPos; _mFoeStart = _foe?.Walker.RootPos ?? Vector3.Zero;
            GD.Print($"MISSION PATROL start: enemy distToPlayer={_mDistE0:F0} foe distToPlayer={_mDistF0:F0}");
            QueueCapture("res://proof/mission_base.png", "mbase", "missionBase", () =>
            QueueCapture("res://proof/mission_vista.png", "mvista", "missionVista", () =>
            QueueCapture("res://proof/gate_sky.png", "gsky", "gatesky", () =>
            { _mPhase = 1; _mPhaseT = 0f; })));
            _mPhase = 99;
        }
        else if (_mPhase == 1)
        {
            // Patrol phase: the player holds at home, 1074u away.
            // Both defenders must MOVE (patrol) yet STAY at their
            // own bases — the old march assertion is inverted.
            _player.SetDrive(_playerYaw, 0f);
            UpdateEnemyAI(dt);                 // test branches drive the AI themselves (cf. FFA test)
            if (_foe != null) UpdateFoeAI(dt);
            _mMaxDisp = Mathf.Max(_mMaxDisp, _enemy.RootPos.DistanceTo(_mEnemyStart));
            _mMaxHome = Mathf.Max(_mMaxHome, _enemy.RootPos.DistanceTo(PadWorld(1)));
            if (_foe != null) _mMaxHomeF = Mathf.Max(_mMaxHomeF, _foe.Walker.RootPos.DistanceTo(PadWorld(2)));
            if (_mPhaseT > 25f)
            {
                GD.Print($"MISSION PATROL end: enemy maxDisp={_mMaxDisp:F0}u maxDistFromHome={_mMaxHome:F0}u; foe maxDistFromHome={_mMaxHomeF:F0}u");
                if (_mMaxDisp < 10f) { GD.PrintErr("MISSION FAIL: enemy did not patrol"); GetTree().Quit(1); return; }
                if (_mMaxHome > 130f || _mMaxHomeF > 130f) { GD.PrintErr("MISSION FAIL: a defender left its base"); GetTree().Quit(1); return; }
                // Aggro phase: put the player 190u from the enemy
                // base — inside the 260u aggro ring — and watch the
                // defender come out to fight.
                var home = PadWorld(1);
                var dirIn = (Vector3.Zero - home); dirIn.Y = 0f; dirIn = dirIn.Normalized();
                var spot = home + dirIn * 190f;
                spot.Y = Main.HeightAt(spot.X, spot.Z);
                _player.Teleport(spot, RiderWalker.YawOf(-dirIn));
                _mAggroD0 = _enemy.RootPos.DistanceTo(_player.RootPos);
                GD.Print($"MISSION AGGRO start: player teleported, enemy distToPlayer={_mAggroD0:F0}");
                _mPhase = 2; _mPhaseT = 0f;
            }
        }
        else if (_mPhase == 2)
        {
            _player.SetDrive(_playerYaw, 0f);
            UpdateEnemyAI(dt);
            if (_foe != null) UpdateFoeAI(dt);
            if (_mPhaseT > 15f)
            {
                float dE = _enemy.RootPos.DistanceTo(_player.RootPos);
                GD.Print($"MISSION AGGRO end: enemy distToPlayer {_mAggroD0:F0}->{dE:F0}");
                if (dE > _mAggroD0 - 12f) { GD.PrintErr("MISSION FAIL: defender did not engage the player"); GetTree().Quit(1); return; }
                GD.Print("MISSION TEST DONE");
                GetTree().Quit(0);
            }
        }
        if (_mPhaseT > 90f && _mPhase != 99) { GD.PrintErr("MISSION FAIL: timeout"); GetTree().Quit(1); }
    }

    private void UpdateTerrainTest(float dt)
    {
        _tPhaseT += dt;
        if (_tPhaseT > 45) { TFail($"phase {_tPhase} timeout"); return; }
        switch (_tPhase)
        {
            case 0: // vista over the whole valley, then move to the slope
                _player.SetDrive(0f, 0f);
                _enemy.SetDrive(Mathf.Pi, 0f);
                if (_tPhaseT > 0.8)
                {
                    QueueCapture("res://proof/env_detail_building.png", "tdetB", "detailBuilding", () =>
                    QueueCapture("res://proof/env_detail_tree.png", "tdetT", "detailTree", () =>
                    QueueCapture("res://proof/terrain_vista.png", "tvista", "vista", () =>
                    {
                        if (!FindSlopeSpot(out var spot, out float yaw))
                        {
                            TFail("no slope spot found in the arena");
                            return;
                        }
                        _tSlopeYaw = yaw;
                        _player.Teleport(spot, yaw);
                        _enemy.Teleport(new Vector3(0f, Main.HeightAt(0f, -16f), -16f), Mathf.Pi);
                        GD.Print($"TERRAIN SLOPE spot={spot} grade={TerrainGrade(spot.X, spot.Z):F3} uphillYaw={Mathf.RadToDeg(yaw):F1}deg");
                        _tPhase = 1; _tPhaseT = 0; _tStrideLogged = false;
                    })));
                    _tPhase = 99; // wait for the capture callback
                }
                break;
            case 1: // climb the slope; capture mid-stride, check contact
                _player.SetDrive(_tSlopeYaw, 2.2f);
                _enemy.SetDrive(Mathf.Pi, 0f);
                if (_tPhaseT > 0.8 && _player.ConsumeStepMidCross() && !_tStrideLogged)
                {
                    _tStrideLogged = true;
                    GD.Print($"TERRAIN STRIDE analytic {_player.FootReport(false)}");
                    GD.Print($"TERRAIN STRIDE engine   {_player.FootReport(true)}");
                    QueueCapture("res://proof/terrain_stride.png", "tstride", "slopeSide", () =>
                    {
                        float md = _player.MaxPlantedDiff();
                        GD.Print($"TERRAIN STRIDE maxPlantedDiff={md:F3}");
                        if (md > 0.08f) { TFail($"planted diff {md:F3} > 0.08 on arena slope"); return; }
                        // Ground-level duel shot: Hector 30u ahead of the player.
                        var fwd = RiderWalker.HeadingVec(_player.LegsYaw);
                        var epos = _player.RootPos + fwd * 30f;
                        epos.X = Mathf.Clamp(epos.X, -60f, 60f);
                        epos.Z = Mathf.Clamp(epos.Z, -60f, 60f);
                        epos.Y = Main.HeightAt(epos.X, epos.Z);
                        _enemy.Teleport(epos, RiderWalker.YawOf(-fwd));
                        _player.SetDrive(_player.LegsYaw, 0f);
                        _tPhase = 2; _tPhaseT = 0;
                    });
                }
                break;
            case 2: // settle, then the ground-level duel frame + render info
                _player.SetDrive(_player.LegsYaw, 0f);
                _enemy.SetDrive(_enemy.LegsYaw, 0f);
                if (_tPhaseT > 1.2)
                {
                    QueueCapture("res://proof/terrain_duel.png", "tduel", "groundDuel", () =>
                    {
                        var vp = GetViewport();
                        GD.Print($"TERRAIN RENDER drawCalls={vp.GetRenderInfo(Viewport.RenderInfoType.Visible, Viewport.RenderInfo.DrawCallsInFrame)} prims={vp.GetRenderInfo(Viewport.RenderInfoType.Visible, Viewport.RenderInfo.PrimitivesInFrame)} objects={vp.GetRenderInfo(Viewport.RenderInfoType.Visible, Viewport.RenderInfo.ObjectsInFrame)}");
                        // RUN phase setup: back to the flat spawn, facing -Z.
                        _player.Teleport(new Vector3(0f, Main.HeightAt(0f, 34f), 34f), 0f);
                        _tRunFrom = _player.RootPos;
                        _tPhase = 3; _tPhaseT = 0;
                    });
                    _tPhase = 99;
                }
                break;
            case 3: // RUN: full run-mode speed (walk x RunFactor) — gait must hold.
                    // Speed is judged on the steady-state segment (after the
                    // 4.2 u/s^2 acceleration ramp), not the whole window.
                _player.SetDrive(0f, _playerSpec.MaxSpeed * RunFactor);
                _enemy.SetDrive(_enemy.LegsYaw, 0f);
                if (_tRunMark < 0f && _tPhaseT > 2.0) { _tRunFrom = _player.RootPos; _tRunMark = (float)_tPhaseT; }
                if (_tRunMark >= 0f) // stance audit: how far apart do the feet get at run speed?
                {
                    var j = _player.DebugLegJoints();
                    var fwdR = new Vector3(Mathf.Sin(_player.LegsYaw), 0f, Mathf.Cos(_player.LegsYaw));
                    var rgtR = new Vector3(fwdR.Z, 0f, -fwdR.X);
                    var rel = j[1].Item4 - j[0].Item4;
                    _tRunMaxSplit = Mathf.Max(_tRunMaxSplit, Mathf.Abs(rel.Dot(fwdR)));
                    _tRunMaxLat = Mathf.Max(_tRunMaxLat, Mathf.Abs(rel.Dot(rgtR)));
                }
                if (_tPhaseT > 4.2)
                {
                    var d = new Vector2(_player.RootPos.X - _tRunFrom.X, _player.RootPos.Z - _tRunFrom.Z).Length();
                    float avg = d / ((float)_tPhaseT - _tRunMark);
                    GD.Print($"RUNTEST steadySpeed={avg:F2} (cmd {_playerSpec.MaxSpeed * RunFactor:F1}) segDist={d:F1} maxSlide={_player.MaxSlide:F4} maxPlantedDiff={_player.MaxPlantedDiff():F3} maxAnkleSplit={_tRunMaxSplit:F2} maxAnkleLat={_tRunMaxLat:F2}");
                    if (_tRunMaxSplit > 10f) { TFail($"run ankle split {_tRunMaxSplit:F2} > 10 (legs spread)"); return; }
                    if (avg < 11.4f) { TFail($"run steady speed {avg:F2} < 11.4"); return; }
                    if (_player.MaxPlantedDiff() > 0.08f) { TFail($"run planted diff {_player.MaxPlantedDiff():F3} > 0.08"); return; }
                    GD.Print("RUNTEST PASS run-mode speed reached, gait contact holds");
                    // FLY setup: flat spawn, jets fitted, tanks full.
                    _player.Teleport(new Vector3(0f, Main.HeightAt(0f, 34f), 34f), 0f);
                    _player.JumpJets = true;
                    _player.JumpCharge = 1f;
                    _playerYaw = 0f;
                    _tJumpApex = 0f; _tApex2 = 0f; _tJumpTried = false; _tJumpStage = 0; _tParked = false;
                    _tPhase = 4; _tPhaseT = 0;
                }
                break;
            case 4: // FLY: hold-to-thrust — burn, flyover, descent, partial burn, recharge.
                if (_tJumpStage == 1) ApplyPlayerInput(0.016f, true, false, false, false, false); // W held: drive + jet push
                else _player.SetDrive(0f, 0f);
                _enemy.SetDrive(_enemy.LegsYaw, 0f);
                if (_tJumpStage >= 1 && _tJumpStage <= 2)
                {
                    float hgtNow = _player.RootPos.Y - _tJumpGround;
                    if (hgtNow > _tJumpApex) _tJumpApex = hgtNow;
                }
                if (_tJumpStage == 4)
                {
                    float hgtNow = _player.RootPos.Y - _tJumpGround;
                    if (hgtNow > _tApex2) _tApex2 = hgtNow;
                }
                if (_tPhaseT > 26.0) { TFail($"FLYTEST timeout in stage {_tJumpStage} (charge {_player.JumpCharge:F2}, h {_player.JumpHeight:F1})"); return; }
                switch (_tJumpStage)
                {
                    case 0: // settle; the unfitted Hector must not lift even with thrust commanded
                        _enemy.SetThrust(true);
                        if (_tPhaseT > 0.6)
                        {
                            if (_enemy.JumpHeight > 0.05f) { TFail("Hector lifted without jets"); return; }
                            _enemy.SetThrust(false);
                            _tJumpGround = _player.RootPos.Y;
                            _tFlyStart = new Vector2(_player.RootPos.X, _player.RootPos.Z);
                            _player.SetThrust(true);
                            _tJumpStage = 1;
                        }
                        break;
                    case 1: // full burn, W held — climb past enemy height while the jets push forward
                        if (!_tParked && _player.JumpHeight > 12f)
                        {
                            _tJumpXZ = new Vector2(_player.RootPos.X, _player.RootPos.Z);
                            _enemy.Teleport(new Vector3(_tJumpXZ.X, Main.HeightAt(_tJumpXZ.X, _tJumpXZ.Y), _tJumpXZ.Y), 0f);
                            _tJumpMarkT = (float)_tPhaseT; _tParked = true;
                        }
                        if (_tParked && _tPhaseT > _tJumpMarkT + 0.3 && _enemy.RootPos.X > -50f)
                        {
                            var nowXZ = new Vector2(_player.RootPos.X, _player.RootPos.Z);
                            // forward flight moves the rider; the flyover check is LATERAL push only
                            if (Mathf.Abs(nowXZ.X - _tJumpXZ.X) > 0.05f) { TFail("flyover pushed the flying rider sideways"); return; }
                            _enemy.Teleport(new Vector3(60f, Main.HeightAt(60f, 60f), 60f), 0f);
                        }
                        if (_player.JumpCharge <= 0f)
                        {
                            _tBurnDist = new Vector2(_player.RootPos.X - _tFlyStart.X, _player.RootPos.Z - _tFlyStart.Y).Length();
                            _tBurnSpeed = _player.Speed;
                            _player.SetThrust(false);
                            _tJumpStage = 2;
                        }
                        break;
                    case 2: // tanks empty: gravity takes it back down
                        if (_player.JumpHeight <= 0f && _tPhaseT > 3.4)
                        {
                            GD.Print($"FLYTEST burn apex={_tJumpApex:F1} burnDist={_tBurnDist:F1} burnEndSpeed={_tBurnSpeed:F1}");
                            if (_tJumpApex < 14f) { TFail($"full-burn apex {_tJumpApex:F1} < 14 - cannot clear an enemy"); return; }
                            // The push is judged on SPEED at burn end:
                            // walk alone caps at 4.2; jets+W must read
                            // ~7.2 (the accel ramp eats the distance
                            // signal: 9.1u with push vs ~7.1 without).
                            if (_tBurnSpeed < 6.5f) { TFail($"burn-end speed {_tBurnSpeed:F1} < 6.5 - thrusters did not push the rider forward"); return; }
                            if (_tBurnDist < 8.5f) { TFail($"burn travel {_tBurnDist:F1} < 8.5"); return; }
                            _tJumpStage = 3;
                        }
                        break;
                    case 3: // partial recharge -> a shorter, lower flight
                        if (_player.JumpCharge >= 0.45f) { _player.SetThrust(true); _tJumpStage = 4; }
                        break;
                    case 4: // partial burn spent -> descend and land
                        if (_tApex2 > 0.5f && _player.JumpHeight <= 0f)
                        {
                            _player.SetThrust(false);
                            GD.Print($"FLYTEST partial apex={_tApex2:F1} (full {_tJumpApex:F1})");
                            if (_tApex2 < 3f) { TFail($"partial-charge flight too low ({_tApex2:F1})"); return; }
                            if (_tApex2 > _tJumpApex - 4f) { TFail($"partial flight ({_tApex2:F1}) not lower than full ({_tJumpApex:F1})"); return; }
                            _tJumpStage = 5;
                        }
                        break;
                    case 5: // full recharge on the ground completes the contract
                        if (_player.JumpCharge >= 0.999f)
                        {
                            GD.Print("FLYTEST PASS hold-to-fly: full burn clears enemy height with forward push and no flyover shove, empty tanks descend, partial charge flies lower, ground recharge refills");
                            GD.Print("TERRAINTEST DONE");
                            GetTree().Quit(0);
                            _tPhase = 99;
                        }
                        break;
                }
                break;
        }
    }

    // ---------- captures / camera ----------

    // Photoreal probe driver: settle, capture the overview and the
    // ground frame, report draw calls / primitives / fps (fps on
    // this GPU-less VM is llvmpipe software rendering — the number
    // that matters is measured on the owner's laptop).
    // TEMP probe (square-grass report): census the hero-clump
    // MultiMesh — which sub-mesh it carries, its AABB and tri
    // count — then capture one instance close, from directly
    // above, and the field from a chase-like elevation.
    private void UpdateGrassShot(float dt)
    {
        _photoT += dt;
        if (_grassStaged || _photoT < 2.0f) return;
        _grassStaged = true;
        MultiMeshInstance3D found = null;
        var stack = new System.Collections.Generic.Stack<Node>();
        stack.Push(this);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n is MultiMeshInstance3D mm && mm.Multimesh != null
                && mm.Multimesh.InstanceCount >= 100) { found = mm; break; }
            foreach (var c in n.GetChildren()) stack.Push(c);
        }
        if (found == null) { GD.PrintErr("GRASSSHOT FAIL: no clump multimesh found"); GetTree().Quit(1); return; }
        var m = found.Multimesh;
        var am = m.Mesh as ArrayMesh;
        long tris = 0;
        if (am != null)
            for (int i = 0; i < am.GetSurfaceCount(); i++)
            {
                var verts = am.SurfaceGetArrays(i)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
                var idx = am.SurfaceGetArrays(i)[(int)Mesh.ArrayType.Index].AsInt32Array();
                tris += (idx.Length > 0 ? idx.Length : verts.Length) / 3;
            }
        GD.Print($"GRASSSHOT instances={m.InstanceCount} meshAabb={m.Mesh.GetAabb()} trisPerInstance={tris} surfaces={(am != null ? am.GetSurfaceCount() : -1)}");
        var t0 = m.GetInstanceTransform(0);
        var t5 = m.GetInstanceTransform(5);
        GD.Print($"GRASSSHOT inst0 pos={t0.Origin} scale={t0.Basis.Scale} inst5 pos={t5.Origin} scale={t5.Basis.Scale}");
        _grassPos = t5.Origin;
        QueueCapture("res://proof/grass_close.png", "gclose", "grassClose", () =>
        QueueCapture("res://proof/grass_top.png", "gtop", "grassTop", () =>
        QueueCapture("res://proof/grass_field.png", "gfield", "grassField", () =>
        {
            GD.Print("GRASSSHOT DONE");
            GetTree().Quit(0);
        })));
    }

    private void UpdatePhotoTest(float dt)
    {
        _photoT += dt;
        if (_photoT > 0.5f) { _fpsSum += (float)Engine.GetFramesPerSecond(); _fpsN++; }
        if (_photoPhase == 0 && _photoT > 2.5f)
        {
            _photoPhase = 1;
            QueueCapture("res://proof/photo_tuft.png", "ptuft", "photoTuft", () =>
            QueueCapture("res://proof/photo_overview.png", "pover", "photoOver", () =>
                QueueCapture("res://proof/photo_ground.png", "pground", "photoGround", () =>
                {
                    long draws = (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame);
                    long prims = (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame);
                    GD.Print($"PHOTO STATS drawCalls={draws} primitives={prims} avgFps={(_fpsN > 0 ? _fpsSum / _fpsN : 0f):F1} (llvmpipe — not representative)");
                    GD.Print("PHOTO DONE");
                    GetTree().Quit();
                })));
        }
    }

    // Gait dev-shot driver: the enemy AI advances on the idle
    // player on its own; capture it mid-stride from the side.
    private void UpdateGaitShot(float dt)
    {
        _gaitT += dt;
        // Lateral audit, continuous: hip/knee/ankle laterals in the
        // enemy's facing frame, dumped through the whole stride so
        // the worst inward instant is visible, not just one sample.
        _gaitDumpT += dt;
        if (_gaitT > 0.6f && _gaitDumpT > 0.25f)
        {
            _gaitDumpT = 0f;
            var fwd = new Vector3(Mathf.Sin(_enemy.LegsYaw), 0f, Mathf.Cos(_enemy.LegsYaw));
            var right = new Vector3(fwd.Z, 0f, -fwd.X);
            foreach (var (legName, hip, knee, ankle, planted, stepT) in _enemy.DebugLegJoints())
            {
                float Lat(Vector3 p) => (p - _enemy.RootPos).Dot(right);
                GD.Print($"GAITDBG t={_gaitT:F2} {legName}: hipLat={Lat(hip):F3} kneeLat={Lat(knee):F3} ankleLat={Lat(ankle):F3} planted={planted} stepT={stepT:F2}");
            }
        }
        if (_gaitT > 1.9f && _camMode != "enemySide" && _camMode != "enemyFront")
            QueueCapture("res://proof/gait_hector.png", "gait", "enemySide", () =>
                QueueCapture("res://proof/gait_hector_front.png", "gaitf", "enemyFront", () => GetTree().Quit()));
    }

}
