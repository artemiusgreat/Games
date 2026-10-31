using Godot;
using System.Collections.Generic;

// MechDuel module: test choreography: duel/weapons/breakable/terrain/photo drivers.
// Split from the single-file build (round 22 cleanup); the class
// stays one partial class — see MechDuel.cs for the field block.
public partial class MechDuel
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
                    // MechLab rules, exercised on the pure data layer:
                    // weight cap and slot type must both refuse.
                    var lab = new LoadoutState();
                    var spec = MechSpecs.TimberWolf;
                    var cap = lab.Check(spec, "arm_L", WeaponCatalog.Ppc);
                    if (cap == null || !cap.Contains("weighs"))
                    { GD.PrintErr($"DUEL FAIL: lab weight cap not enforced ({cap})"); GetTree().Quit(1); return; }
                    if (lab.Check(spec, "arm_R", WeaponCatalog.Ppc) == null)
                    { GD.PrintErr("DUEL FAIL: lab slot type not enforced"); GetTree().Quit(1); return; }
                    if (lab.TryAssign(spec, "arm_R", WeaponCatalog.Autocannon) != null
                        || lab.TryAssign(spec, "pod_L", WeaponCatalog.Lrm) != null)
                    { GD.PrintErr("DUEL FAIL: lab legal fits refused"); GetTree().Quit(1); return; }
                    if (Mathf.Abs(LoadoutState.DefaultTimberWolf().TotalWeaponWeight - 37f) > 0.01f)
                    { GD.PrintErr("DUEL FAIL: default fit tonnage != 37t"); GetTree().Quit(1); return; }
                    GD.Print("LABTEST PASS slot type + 8t arm cap enforced (PPC refused), legal fits accepted, default fit = 37t");
                    // Mech selection (round 22): swap both chassis and
                    // verify the duel follows — parts layout, pools
                    // and equipped defs all follow the spec.
                    SetMechSelection("atlas", "timberwolf");
                    if (_playerSpec.Id != "atlas" || _enemySpec.Id != "timberwolf"
                        || _hpPlayer != 340f || _hpEnemy != 260f
                        || _defBallistic?.Id != "ac20"
                        || !_partsEnemy.Alive("pod_L"))
                    { GD.PrintErr($"DUEL FAIL: mech selection swap wrong (hp {_hpPlayer}/{_hpEnemy}, def {_defBallistic?.Id})"); GetTree().Quit(1); return; }
                    _selStage = 1; _selHoldT = 0f;
                    GD.Print("LABTEST SELECTION swapped to Atlas player; holding across live frames (HUD must survive a pod-less chassis)");
                }
                // Keep the swapped chassis deployed across many real
                // frames before restoring: swapping forth and back
                // inside one call never lets UpdateHud run against
                // the Atlas parts — which is exactly how the pod_L
                // KeyNotFound crash shipped (it froze the player's
                // camera in the live game while this test stayed
                // green). If UpdateHud throws now, the frame aborts
                // before UpdateTest and this hold never completes.
                if (_selStage == 1)
                {
                    _selHoldT += delta;
                    if (_selHoldT > 1.2f)
                    {
                        SetMechSelection("timberwolf", "atlas");
                        if (_playerSpec.Id != "timberwolf" || _enemySpec.Id != "atlas"
                            || _hpPlayer != 260f || _hpEnemy != 340f || _defEnergy?.Id != "large_laser")
                        { GD.PrintErr("DUEL FAIL: mech selection restore wrong"); GetTree().Quit(1); return; }
                        _selStage = 2;
                        GD.Print("LABTEST SELECTION PASS chassis swap rebuilds walkers, parts, pools and defs; Atlas player survived 1.2s of live HUD frames; defaults restored");
                    }
                }
                if (_phaseT > 1.6 && _selStage != 1) SetPhase(1);
                break;
            case 1: // Atlas walks at the player; grab stride + wide frames
                _player.SetDrive(0f, 0f);
                _enemy.SetDrive(Mathf.Pi, 1.8f);
                if (_enemy.ConsumeStepMidCross() && _phaseT > 0.6)
                {
                    _strideShots++;
                    if (_strideShots == 1)
                    {
                        GD.Print($"DUEL ATLAS CONTACT analytic {_enemy.FootReport(false)}");
                        GD.Print($"DUEL ATLAS CONTACT engine   {_enemy.FootReport(true)}");
                        QueueCapture("res://proof/duel_atlas_stride.png", "stride", "enemySide");
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
                if (_phaseT > 12) { GD.PrintErr("DUEL FAIL: atlas stride shots missing"); GetTree().Quit(1); }
                break;
            case 2: // torso twist: legs walk -Z, torso twisted +66 deg
                _player.SetDrive(0f, 1.8f);
                _enemy.SetDrive(Mathf.Pi, 0f);
                if (_phaseT > 1.4 && !_twistLogged)
                {
                    _twistLogged = true;
                    float legs = _player.LegsYaw;
                    float torso = _player.EngineTorsoYaw();
                    float rel = Mathf.RadToDeg(MechWalker.WrapAngle(torso - legs));
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
            case 3: // STOMP: drive straight at the tree at (6,16) — it must NOT stop the mech; the mech crushes it and walks through
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
                        GD.Print($"DUEL STOMP PASS tree crushed underfoot (minSep={_minTreeSep:F2}), mech walks through");
                        QueueCapture("res://proof/duel_collision_prop.png", "prop", "prop", () =>
                        {
                            SetPhase(4);
                            _player.Teleport(new Vector3(0f, Main.HeightAt(0f, 10f), 10f), 0f);
                            _enemy.Teleport(new Vector3(0f, Main.HeightAt(0f, -2f), -2f), Mathf.Pi);
                            _minMechSep = 1e9f;
                        });
                    }
                    if (_player.RootPos.Z < 12.5f && (treeD == null || treeD.State == 0))
                    { GD.PrintErr("DUEL FAIL: player passed the tree without stomping it"); GetTree().Quit(1); }
                    if (_phaseT > 14) { GD.PrintErr("DUEL FAIL: stomp phase timeout"); GetTree().Quit(1); }
                }
                break;
            case 4: // mechs driven at each other
                _player.SetDrive(0f, 2.2f);
                _enemy.SetDrive(Mathf.Pi, 1.8f);
                {
                    float dist = new Vector2(_player.RootPos.X - _enemy.RootPos.X,
                                             _player.RootPos.Z - _enemy.RootPos.Z).Length();
                    _minMechSep = Mathf.Min(_minMechSep, dist);
                    if (_phaseT > 2.2f)
                    {
                        if (Mathf.Abs(dist - _minMechSep) < 0.001f && dist <= _player.CollisionRadius + _enemy.CollisionRadius + 0.05f)
                            _stallTimer += delta;
                        else _stallTimer = 0f;
                        if (_stallTimer > 0.5f)
                        {
                            GD.Print($"DUEL MECH minSep={_minMechSep:F3} finalSep={dist:F3} sumRadii={_player.CollisionRadius + _enemy.CollisionRadius:F2}");
                            QueueCapture("res://proof/duel_collision_mech.png", "mech", "mechSide");
                            SetPhase(5);
                        }
                    }
                    if (_phaseT > 16) { GD.PrintErr("DUEL FAIL: mech stall never detected"); GetTree().Quit(1); }
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
            case 6: // AIM: the aim ray follows the TORSO (owner's model):
                    // pitch and twist steer TorsoAimPoint, and the
                    // reticle's screen position rides the projection.
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
                    _camMode = "chase";
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
                    // WIDE AIM (round 13): the mouse target is not
                    // bound by the torso clamp — with AimPitchWide
                    // driven to -1.05 the convergence must land on
                    // the ground right in front of the feet, in the
                    // screen's lower half, while the torso's own
                    // pitch stays clamped at -0.32.
                    _player.TorsoTwist = 0f;
                    _player.TorsoPitch = 0f;
                    _player.AimPitchWide = -1.05f;
                    var aimWide = TorsoAimPoint();
                    var scrW = _cam.UnprojectPosition(aimWide);
                    var vpW = GetViewport().GetVisibleRect().Size;
                    if (_aimTarget != "TERRAIN" || aimWide.DistanceTo(_player.RootPos) > 30f || scrW.Y < vpW.Y * 0.5f)
                    { GD.PrintErr($"DUEL FAIL: wide aim cannot reach the feet (target='{_aimTarget}' dist={aimWide.DistanceTo(_player.RootPos):F1} scrY={scrW.Y:F0}/{vpW.Y:F0})"); GetTree().Quit(1); break; }
                    if (_player.TorsoPitch < -0.33f)
                    { GD.PrintErr("DUEL FAIL: torso pitch escaped its clamp"); GetTree().Quit(1); break; }
                    _player.AimPitchWide = float.NaN;
                    GD.Print($"DUEL WIDEAIM PASS free mouse pitch reaches the ground in front of the feet (dist {aimWide.DistanceTo(_player.RootPos):F1}u, screen y {scrW.Y:F0}/{vpW.Y:F0}); torso pitch stays clamped");
                    // Reticle TRUTH (round 8): sweep the pitch across
                    // the Atlas; at every step the reticle must sit
                    // exactly on the projection of the true aim point
                    // (the spot weapons converge on) — no parallax
                    // fiction between cursor and impact.
                    _player.TorsoTwist = 0f;
                    var vpAim = GetViewport().GetVisibleRect().Size;
                    float firstRy = 0f, lastRy = 0f;
                    for (int i = 0; i <= 5; i++)
                    {
                        _player.TorsoPitch = -0.05f + i * 0.02f;
                        _aimPoint = TorsoAimPoint();
                        UpdateReticle();
                        var exp = _cam.UnprojectPosition(_reticlePoint);
                        exp = new Vector2(
                            Mathf.Clamp(exp.X, 28f, vpAim.X - 28f),
                            Mathf.Clamp(exp.Y, 28f, vpAim.Y - 28f));
                        var got = _cross.ReticlePos;
                        if (Mathf.Abs(got.X - exp.X) > 6f || Mathf.Abs(got.Y - exp.Y) > 6f)
                        { GD.PrintErr($"DUEL FAIL: reticle is not on the aim point (step {i}: got {got}, expected {exp})"); _aimStep = 99; GetTree().Quit(1); break; }
                        if (i == 0) firstRy = got.Y;
                        lastRy = got.Y;
                    }
                    if (_aimStep == 99) break; // sweep failed above; Quit(1) already queued
                    if (lastRy > firstRy - 8f)
                    { GD.PrintErr($"DUEL FAIL: reticle barely rides the pitch sweep (first={firstRy:F0} last={lastRy:F0})"); GetTree().Quit(1); break; }
                    // The synthesis contract, tested through the REAL
                    // weapon: cursor low on the Atlas => the laser
                    // HITS him; cursor raised above him => it MISSES
                    // (round 9: the weapons converge on the cursor's
                    // own point, so the pointer and the shots agree).
                    _enemyDmgEvents = 0;
                    int rescues0 = _laserRescues;
                    _cdLaser = 0f;
                    _player.TorsoPitch = -0.10f;
                    _aimPoint = TorsoAimPoint();
                    FireLaser(_aimPoint);
                    if (_enemyDmgEvents < 1)
                    { GD.PrintErr("DUEL FAIL: cursor low on the Atlas but the laser missed him"); GetTree().Quit(1); break; }
                    // Regression guard scoped to THIS shot: a stable
                    // chase shot that HITS must come from the camera
                    // path unaided. (The raised shot below may
                    // legitimately rescue: its aim point is a
                    // synthetic free point over the Atlas, and the
                    // camera's far-terrain verdict disagrees on the
                    // endpoint while agreeing on the miss.)
                    if (_laserRescues != rescues0)
                    { GD.PrintErr("DUEL FAIL: the laser needed the harness fallback for a stable hitting shot — the live camera path is broken (round 20 regression guard)"); GetTree().Quit(1); break; }
                    _cdLaser = 0f;
                    _player.TorsoPitch = 0.30f;
                    _aimPoint = TorsoAimPoint();
                    FireLaser(_aimPoint);
                    if (_enemyDmgEvents > 1)
                    { GD.PrintErr("DUEL FAIL: cursor above the Atlas but a shot still hit him"); GetTree().Quit(1); break; }
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
                    GD.Print($"DUEL AIM PASS pitch aimY {aimDown.Y:F1} -> {aimUp.Y:F1}, twist dxz={dxz:F1}, reticle IS the convergence point: laser hits low, misses high; twist unclamped (full 360)");
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
                    // over the Atlas and loose a salvo — the first
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
                        float torsoDrift = Mathf.Abs(MechWalker.WrapAngle(_player.TorsoYawWorld - _aimWorld0));
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
                        // sits on the true aim point above the Atlas.
                        _player.TorsoTwist = 0f;
                        _player.TorsoPitch = 0.18f;
                        _aimPoint = TorsoAimPoint();
                        UpdateReticle();
                        _aimStep = 5;
                        QueueCapture("res://proof/duel_aim.png", "aim", "chase", () =>
                        {
                            QueueCapture("res://proof/duel_collision_mech.png", "mech", "mechSide");
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
        // Atlas torso tracks the player in every phase (gunnery muzzle and
        // visuals stay honest); both walkers otherwise hold position.
        var toP = _player.RootPos - _enemy.RootPos;
        if (toP.LengthSquared() > 0.01f)
            _enemy.TorsoTwist = MechWalker.WrapAngle(MechWalker.YawOf(toP.Normalized()) - _enemyYaw);
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
            case 0: // settle at the open lane: TW (0,18) vs Atlas (0,-12), 30u
                _player.SetDrive(0f, 0f);
                _enemy.SetDrive(Mathf.Pi, 0f);
                if (_wPhaseT > 1.2) SetWPhase(1);
                break;

            case 1: // laser x3 at a stationary Atlas: 3 x 12 = 36 exactly
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
                    if (Mathf.Abs(_hpEnemy - 304f) > 0.01f) WFail($"laser atlasHp={_hpEnemy:F1} (want 304)");
                    // Round 8: the reaction must carry the weapon —
                    // a laser hit flashes CYAN and lights the impact.
                    var lfc = _enemy.LastFlashColor;
                    if (!(lfc.B > lfc.R + 0.2f)) { WFail($"laser hit flashed {lfc} — want cyan"); return; }
                    if (_fxLights <= 0) { WFail("laser hit lit no impact light"); return; }
                    GD.Print($"WPNTEST LASER PASS events=3 dmg=36 atlasHp={_hpEnemy:F0} (hit reaction: cyan flash + impact light)");
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
                    if (Mathf.Abs(_hpEnemy - 252f) > 0.01f) WFail($"ac atlasHp={_hpEnemy:F1} (want 252 pool under terrain-step routing)");
                    if (Mathf.Abs(_partsEnemy.HpOf("leg_L") - 90f) > 0.01f)
                        WFail($"ac legL partHp={_partsEnemy.HpOf("leg_L"):F1} (want 90 = untouched, shells routed to torso)");
                    var cfc = _enemy.LastFlashColor;
                    if (!(cfc.R > cfc.B + 0.3f && cfc.G > cfc.B + 0.2f)) { WFail($"cannon hit flashed {cfc} — want warm white"); return; }
                    if (_wJoltSeen < 0.3f) { WFail($"cannon hits barely rocked the Atlas (jolt env peak {_wJoltSeen:F2})"); return; }
                    GD.Print($"WPNTEST AC PASS events=2 dmg=52 atlasHp={_hpEnemy:F0} legL partHp={_partsEnemy.HpOf("leg_L"):F0} (shells routed to torso; reaction: warm flash + jolt {_wJoltSeen:F2})");
                    _enemyDmgEvents = 0; _enemyDmgTotal = 0f;
                    SetWPhase(3);
                }
                break;

            case 3: // LRM salvo: count rockets that damage the Atlas (of 8)
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
                    GD.Print($"WPNTEST LRM PASS rocketsDamaging={_enemyDmgEvents}/8 dmg={_enemyDmgTotal:F1} atlasHp={_hpEnemy:F1} (reaction: orange flash)");
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
                    GD.Print("WPNTEST BLOCKED PASS building stopped the beam, atlasHp unchanged");
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

            case 5: // Atlas return fire vs a strafing player.
                    // The player reverses every 0.9s at full speed. At the
                    // original 50 u/s shell the 0.9s flight let a reversal
                    // land inside the lead window — strafing genuinely
                    // dodged. Since 2026-10-29 the AC/20 flies at 150
                    // (owner: Atlas matches the player's cannon changes,
                    // symmetric rules), flight is ~0.3s, the strafe
                    // displaces ~1.2u — inside the target's own radius —
                    // so return fire is RELIABLE now, by design. The
                    // owner flagged this as provisional: if Atlas feels
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
                    if (hits < 1) WFail($"atlas return fire: 0 hits in {_enemyShots} shots");
                    // Pass band at 150 u/s: 5..6 hits of 6. The one
                    // allowed miss is the seeded ±1.2° cone at long
                    // range plus pacing luck in the reversal cycle;
                    // anything less means return fire broke. The old
                    // 2..5 dodge band (measured at 50 u/s, verify21..26)
                    // died with the dodge window itself. The sharp
                    // assertion stays the exact damage total below
                    // (hits x 18, no tolerance games there).
                    if (hits < 5) WFail($"atlas return fire unreliable at matched speed: {hits} hits in {_enemyShots} shots");
                    if (Mathf.Abs(_playerDmgTotal - hits * 18f) > 0.01f)
                        WFail($"atlas dmg total={_playerDmgTotal:F1} != hits*18 ({hits * 18})");
                    GD.Print($"WPNTEST ATLASFIRE PASS shots={_enemyShots} hits={hits} misses={misses} playerHp={_hpPlayer:F0}");
                    _enemyMayFire = false;
                    _player.TorsoTwist = 0f;
                    ResetDuel();
                    WSetup(new Vector3(0f, Main.HeightAt(0f, 18f), 18f), new Vector3(0f, Main.HeightAt(0f, -12f), -12f));
                    SetWPhase(6);
                }
                if (_wPhaseT > 35) WFail($"atlas return fire timeout shots={_enemyShots}");
                break;

            case 6: // sustained fire -> Atlas destroyed, banner, slump frame
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
                    GD.Print($"WPNTEST KILL atlas destroyed at phaseT={_wPhaseT:F2}");
                }
                if (_enemyDead && _wPhaseT > _wDeathT + 1.8 && !_wDeadShotQueued)
                {
                    // Death comes only from the HP pool now (owner):
                    // wrecked legs limp, they never kill — so a dead
                    // Atlas must show hp == 0 exactly.
                    if (_hpEnemy != 0f)
                        WFail($"dead atlas hp={_hpEnemy:F1} (want pool kill, hp 0)");
                    if (!_enemy.Halted) WFail("dead atlas walker not halted");
                    if (!_banner.Visible || _banner.Text != "ATLAS DESTROYED")
                        WFail($"kill banner wrong: visible={_banner.Visible} text='{_banner.Text}'");
                    GD.Print($"WPNTEST KILL PASS death certified (hp={_hpEnemy:F0}), walker halted, banner 'ATLAS DESTROYED'");
                    _wDeadShotQueued = true;
                    QueueCapture("res://proof/weapons_atlas_dead.png", "wdead", "wpnDead", () => SetWPhase(7));
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
                        Target = "PLAYER",
                        Zone = "torso",
                        Point = _player.RootPos + Vector3.Up * 8f
                    };
                    ApplyDamage(_player, 9999f, dummy, "ENEMY");
                    if (!_playerDead) WFail("player death flag not set");
                    if (!_banner.Visible || _banner.Text != "MECH DESTROYED — press R to reset")
                        WFail($"player banner wrong: visible={_banner.Visible} text='{_banner.Text}'");
                    GD.Print("WPNTEST PLAYERDEATH PASS banner 'MECH DESTROYED — press R to reset'");
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
                    _player.Teleport(fp, MechWalker.YawOf((bc - fp).Normalized()));
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
                    var ray = _hit.Ray(from, (aimB - from).Normalized(), 30f, "PLAYER");
                    float distB = from.DistanceTo(aimB);
                    if (ray.Target == "PROP" && ray.T < distB + 2f) { WFail("rubble still blocks shots"); return; }
                    GD.Print("ENVTEST BUILDING PASS collapsed to rubble, colliders removed, shots pass through");
                    // Move to the tree: ONE hit of any kind destroys
                    // it, and its shot-blocking cylinder is gone from
                    // that same frame (owner's rule).
                    var tc = _wEnvT.Center;
                    var toC2 = (Vector3.Zero - tc); toC2.Y = 0f;
                    var fp2 = tc + toC2.Normalized() * 12f;
                    fp2.Y = Main.HeightAt(fp2.X, fp2.Z);
                    _player.Teleport(fp2, MechWalker.YawOf((tc - fp2).Normalized()));
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
                        if (_aimTarget == "PROP" && conv.DistanceTo(crownCtr) < 3.5f) { onCrown = true; break; }
                    }
                    if (!onCrown) { WFail("no pitch puts the cursor on the crown"); return; }
                    _wEnvChunks0 = _envChunks.Count; // re-baseline at the shot: building pieces dissolve on their own clock (round 21) and must not pollute the splinter delta
                    FireLaser(conv);
                    if (_wEnvT.State != 3) { WFail($"one hit did not destroy the tree (state={_wEnvT.State})"); return; }
                    var fromT = _player.RootPos + Vector3.Up * 8f;
                    var aimT = new Vector3(tc.X, tc.Y + 2f, tc.Z);
                    var rayT = _hit.Ray(fromT, (aimT - fromT).Normalized(), 30f, "PLAYER");
                    if (rayT.Target == "PROP" && rayT.T < fromT.DistanceTo(aimT) + 2f) { WFail("destroyed tree still blocks shots"); return; }
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
                    // far Atlas passed through it, masking the fact).
                    // Live play is always chase; restore it here.
                    _camMode = "chase";
                    // FAR TARGET (owner, round 13): at 85u the old
                    // 60u convergence cap rejected the cursor's hit
                    // and shots fell back to the free point — in the
                    // sky. Hunt the pitch until the cursor sits on
                    // the Atlas, then require exact laser damage.
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
                        if (_aimTarget == "ENEMY") { onFar = true; break; }
                    }
                    if (!onFar) { WFail("no pitch puts the cursor on the far Atlas"); return; }
                    _cdLaser = 0f;
                    FireLaser(convF);
                    if (340f - _hpEnemy < 11.9f) { WFail($"far cursor shot did not damage the Atlas (hp={_hpEnemy:F0})"); return; }
                    GD.Print($"ENVTEST FAR-TARGET PASS cursor convergence at 85u: laser hit the Atlas for {340f - _hpEnemy:F0}");
                    // FAR HILL (owner, round 13): the cursor on a
                    // distant hillside with a clear line must
                    // converge on the hill itself, not the sky. The
                    // Atlas steps aside. Staging is searched, not
                    // assumed: a continuous rise is met at its
                    // nearest point (~44u) and a low rim is overflown
                    // (both measured), so the harness tries pads x
                    // facings with the game's own aim math and keeps
                    // the first setup where a pitch hunt finds a
                    // far TERRAIN convergence — the same search a
                    // player does when he turns toward a slope.
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
                            for (float pp = 0.40f; pp >= -0.10f; pp -= 0.02f)
                            {
                                _player.TorsoPitch = pp;
                                convH = TorsoAimPoint();
                                if (_aimTarget == "TERRAIN" && convH.DistanceTo(_player.RootPos) > 60f) { onHill = true; break; }
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
    private void CraftHit(MechWalker target, string partId, float dmg, float weaponWeight = 0f)
    {
        var parts = target == _player ? _partsPlayer : _partsEnemy;
        var hit = new HitModel.Result
        {
            Hit = true,
            Target = target == _player ? "PLAYER" : "ENEMY",
            Zone = "torso",
            Point = parts.PartAimPoint(partId)
        };
        ApplyDamage(target, dmg, hit, target == _player ? "ENEMY" : "PLAYER", weaponWeight);
    }

    // Craft via a real HitModel ray: needed for the TW head, whose
    // volume interior is shadowed by the torso core's AABB (an interior
    // point attributes torso; a surface hit attributes head, exactly as
    // real aimed fire does). The head ray comes in angled onto the
    // canopy face: a purely vertical drop races the capsule's flat top
    // disc in the same column (sub-decimeter, decided by the root tilt
    // the terrain happens to give), while the angled ray meets the
    // head box where it protrudes past the capsule, like a real shot.
    private void CraftRayHit(MechWalker target, string partId, float dmg)
    {
        var parts = target == _player ? _partsPlayer : _partsEnemy;
        var aim = parts.PartAimPoint(partId);
        Vector3 from;
        if (partId == "head")
        {
            var fwd = MechWalker.HeadingVec(target.TorsoYawWorld);
            from = aim + Vector3.Up * 5f + fwd * 7f;
        }
        else from = aim + Vector3.Up * 15f;
        var res = _hit.Ray(from, (aim - from).Normalized(), 40f, target == _player ? "ENEMY" : "PLAYER");
        GD.Print($"BRK CRAFTRAY {partId} hit={res.Hit} target={res.Target} part={res.Part} point={res.Point}");
        if (!res.Hit || res.Part != partId)
        {
            BFail($"craft ray for {partId} attributed '{res.Part}'");
            return;
        }
        ApplyDamage(target, dmg, res, target == _player ? "ENEMY" : "PLAYER");
    }

    private void UpdateBreakTest(float dt)
    {
        _bPhaseT += dt;
        if (_bPhaseT > 90) { BFail($"phase {_bPhase} timeout"); return; }

        switch (_bPhase)
        {
            case 0: // settle on the lane: TW (0,18) vs Atlas (0,-12)
                _player.SetDrive(0f, 0f);
                _enemy.SetDrive(Mathf.Pi, 0f);
                if (_bPhaseT > 1.0) SetBPhase(1);
                break;

            case 1: // POD: part HP depletes per hit, breaks at <= 0, debris
                    // settles, then stays behind while the mech walks away
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
                    if (drift > 0.05f) BFail($"debris moved with the mech: drift={drift:F3}");
                    if (away < 3f) BFail($"mech did not walk away from debris: delta={away:F2}");
                    GD.Print($"BRKTEST DETACH PASS debris static (drift={drift:F3}) while mech walked {away:F1}u away");
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

            case 3: // ARM (Atlas): real AC fire breaks arm_R; its AC/20 stops
                _player.SetDrive(0f, 0f);
                if (_bStep == 0)
                {
                    _enemyMayFire = true;
                    _enemy.SetDrive(Mathf.Pi, 0f);
                    if (_enemyShots >= 2) BStep(1);
                    else if (_bPhaseT > 20) BFail($"atlas fired only {_enemyShots} shots pre-break");
                }
                else if (_bStep == 1)
                {
                    _enemy.SetDrive(Mathf.Pi, 0f);
                    if (_cdAc <= 0f) FireAutocannon(_partsEnemy.PartAimPoint("arm_R"));
                    if (!_partsEnemy.Alive("arm_R"))
                    {
                        GD.Print($"BRKTEST ARMBREAK PASS atlas arm_R destroyed by real AC fire (partHp logs above), atlasShots so far={_enemyShots}");
                        BStep(2);
                        _bCount1 = _enemyShots;
                    }
                    else if (BStepT > 15) BFail("atlas arm_R never broke under AC fire");
                }
                else if (_bStep == 2)
                {
                    _enemy.SetDrive(Mathf.Pi, 1.6f);
                    if (BStepT > 1.2 && !_bFlag1)
                    {
                        _bFlag1 = true;
                        QueueCapture("res://proof/break_atlas_noarm.png", "bnoarm", "brkAtlas");
                    }
                    if (BStepT > 5.5)
                    {
                        if (_enemyShots != _bCount1)
                            BFail($"atlas AC/20 fired after arm_R loss: {_bCount1} -> {_enemyShots}");
                        GD.Print($"BRKTEST ARMGUN PASS atlas AC/20 silent for 5.5s after arm_R loss (shots flat at {_enemyShots})");
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

            case 5: // LEG (Atlas): baseline speed, break leg_L, then the
                    // mech limps at the 0.55 factor with the wrecked leg
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
                        GD.Print($"BRKTEST BASESPEED atlas baseline={_bV1:F2} u/s (cmd 2.2; unified clock — the walker now covers the full commanded speed)");
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
                        if (_partsEnemy.Alive("leg_L")) BFail("atlas leg_L alive after 90 damage");
                        if (!_enemy.LegDisabled(true)) BFail("walker leg_L not disabled");
                        var legPart = _partsEnemy.GetPart("leg_L");
                        if (!legPart.Wrecked) BFail("leg_L not in wrecked state");
                        foreach (var rec in legPart.Meshes)
                            if (rec.Detached) BFail("leg_L mesh DETACHED — legs must never fall off");
                        if (_enemy.IsKnockedDown) BFail("leg wreck felled the mech (retired fall rule)");
                        if (Mathf.Abs(_enemy.SpeedFactor - 0.55f) > 0.01f) BFail($"speed factor {_enemy.SpeedFactor:F2} != 0.55 (limp)");
                        GD.Print("BRKTEST LEGWRECK PASS leg_L destroyed but ATTACHED (wrecked), mech upright, limp factor 0.55");
                        _bSawWreckSwing = false;
                        BStep(3);
                    }
                }
                else if (_bStep == 3)
                {
                    // Full drive on the wrecked leg: the mech must MOVE
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
                        QueueCapture("res://proof/break_immobile.png", "blimp", "brkAtlas");
                        _bCount1 = 2;
                    }
                    else if (_bCount1 == 2 && BStepT > 3.0)
                    {
                        var d = new Vector2(_enemy.RootPos.X - _bVec1.X, _enemy.RootPos.Z - _bVec1.Z).Length();
                        float v2 = d / BStepT;
                        float ratio = _bV1 > 0.1f ? v2 / _bV1 : 0f;
                        GD.Print($"BRKTEST LIMP atlas limpSpeed={v2:F2} baseline={_bV1:F2} ratio={ratio:F2}");
                        if (ratio < 0.35f || ratio > 0.78f) { BFail($"limp speed ratio {ratio:F2} (want ~0.55 of baseline)"); return; }
                        if (!_bSawWreckSwing) { BFail("wrecked leg never left the ground while limping — it must keep stepping, not drag"); return; }
                        GD.Print("BRKTEST LIMP PASS wrecked leg -> mech keeps walking at ~55% speed, leg attached AND still stepping");
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
                    if (_enemyDead) BFail("atlas DIED from leg damage — legs never kill (owner)");
                    if (_enemy.Halted) BFail("atlas walker halted by leg damage");
                    if (_banner.Visible) BFail($"banner shown for a leg-wrecked mech: '{_banner.Text}'");
                    if (_enemy.IsKnockedDown) BFail("both-legs wreck felled the mech");
                    var lr = _partsEnemy.GetPart("leg_R");
                    if (!lr.Wrecked) BFail("leg_R not wrecked");
                    foreach (var rec in lr.Meshes)
                        if (rec.Detached) BFail("leg_R mesh DETACHED");
                    if (Mathf.Abs(_enemy.SpeedFactor - 0.25f) > 0.01f)
                        BFail($"speed factor {_enemy.SpeedFactor:F2} != 0.25 (crawl)");
                    if (_hpEnemy <= 0f) BFail("pool hp drained by leg hits");
                    GD.Print($"BRKTEST BOTHLEGS PASS both legs wrecked -> CRAWL 0.25x, mech alive (pool hp={_hpEnemy:F0}), upright, legs attached");
                    BStep(1);
                }
                else if (_bStep == 1 && BStepT > 2.0)
                {
                    QueueCapture("res://proof/break_atlas_disabled.png", "bdisabled", "brkAtlas");
                    BStep(2);
                }
                else if (_bStep == 2 && _pendingPath == null && !_capturing)
                {
                    ResetDuel();
                    BSetup(new Vector3(0f, Main.HeightAt(0f, 18f), 18f), new Vector3(0f, Main.HeightAt(0f, -12f), -12f));
                    SetBPhase(7);
                }
                break;

            case 7: // HEAD (Atlas): pool rule + tracking lag; player sway
                _player.SetDrive(0f, 0f);
                _enemy.SetDrive(Mathf.Pi, 0f);
                if (_bStep == 0)
                {
                    CraftHit(_enemy, "head", 25f);
                    CraftHit(_enemy, "head", 25f);
                    if (_partsEnemy.Alive("head")) BFail("atlas head alive after 50 damage");
                    if (Mathf.Abs(_hpEnemy - 290f) > 0.01f)
                        BFail($"head->pool rule: atlasHp={_hpEnemy:F1} (want 290 = 340 - 50)");
                    GD.Print("BRKTEST HEADPOOL PASS head hits burned head HP and pool (340 -> 290)");
                    // Move the player to the Atlas's flank: desired twist ~1 rad.
                    _player.Teleport(new Vector3(20f, Main.HeightAt(20f, 2f), 2f), 0f);
                    BStep(1);
                }
                else if (_bStep == 1)
                {
                    var toP = _player.RootPos - _enemy.RootPos;
                    float yawTo = MechWalker.YawOf(toP.Normalized());
                    TrackEnemyTorso(dt, yawTo);
                    float desired = Mathf.Abs(MechWalker.WrapAngle(yawTo - _enemyYaw));
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
                    float yawTo = MechWalker.YawOf(toP.Normalized());
                    TrackEnemyTorso(dt, yawTo);
                    if (BStepT > 2.2)
                    {
                        float err = Mathf.Abs(MechWalker.WrapAngle(
                            MechWalker.WrapAngle(yawTo - _enemyYaw) - _enemy.TorsoTwist));
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
                        if (p.Destroyed) BFail($"atlas part {p.Id} still destroyed after reset");
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
                    if (_enemy.IsKnockedDown) BFail("weapon impacts toppled a mech (retired knockdown)");
                    GD.Print("KNOCKTEST NO-TOPPLE PASS 36t of impacts — the Atlas stands (no knockdown rule remains)");
                    _partsEnemy.DamagePart("leg_R", 999f);
                    BStep(2);
                }
                else if (_bStep == 2 && BStepT > 0.8f)
                {
                    if (_enemy.IsKnockedDown || _enemy.FallAmount > 0.01f)
                    { BFail("leg wreck felled the mech (retired fall rule)"); return; }
                    var lr = _partsEnemy.GetPart("leg_R");
                    if (!lr.Wrecked) { BFail("leg_R not wrecked"); return; }
                    foreach (var rec in lr.Meshes)
                        if (rec.Detached) { BFail("leg_R mesh DETACHED — legs never fall off"); return; }
                    if (Mathf.Abs(_enemy.SpeedFactor - 0.55f) > 0.01f)
                    { BFail($"speed factor {_enemy.SpeedFactor:F2} != 0.55 after leg wreck"); return; }
                    GD.Print("LEGRULE PASS leg wrecked: attached + darkened, mech upright, limping at 0.55x");
                    QueueCapture("res://proof/break_knockdown.png", "brkKnock", "brkAtlas");
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
            uphillYaw = MechWalker.YawOf(new Vector3(gx, 0f, gz).Normalized());
            spot = new Vector3(p.X, Main.HeightAt(p.X, p.Y), p.Y);
            bestG = g;
            found = true;
        }
        return found;
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
                        // Ground-level duel shot: Atlas 30u ahead of the player.
                        var fwd = MechWalker.HeadingVec(_player.LegsYaw);
                        var epos = _player.RootPos + fwd * 30f;
                        epos.X = Mathf.Clamp(epos.X, -60f, 60f);
                        epos.Z = Mathf.Clamp(epos.Z, -60f, 60f);
                        epos.Y = Main.HeightAt(epos.X, epos.Z);
                        _enemy.Teleport(epos, MechWalker.YawOf(-fwd));
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
                _player.SetDrive(0f, PlayerMaxSpeed * RunFactor);
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
                    GD.Print($"RUNTEST steadySpeed={avg:F2} (cmd {PlayerMaxSpeed * RunFactor:F1}) segDist={d:F1} maxSlide={_player.MaxSlide:F4} maxPlantedDiff={_player.MaxPlantedDiff():F3} maxAnkleSplit={_tRunMaxSplit:F2} maxAnkleLat={_tRunMaxLat:F2}");
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
                    case 0: // settle; the unfitted Atlas must not lift even with thrust commanded
                        _enemy.SetThrust(true);
                        if (_tPhaseT > 0.6)
                        {
                            if (_enemy.JumpHeight > 0.05f) { TFail("Atlas lifted without jets"); return; }
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
                            // forward flight moves the mech; the flyover check is LATERAL push only
                            if (Mathf.Abs(nowXZ.X - _tJumpXZ.X) > 0.05f) { TFail("flyover pushed the flying mech sideways"); return; }
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
                            if (_tBurnSpeed < 6.5f) { TFail($"burn-end speed {_tBurnSpeed:F1} < 6.5 - thrusters did not push the mech forward"); return; }
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
            QueueCapture("res://proof/gait_atlas.png", "gait", "enemySide", () =>
                QueueCapture("res://proof/gait_atlas_front.png", "gaitf", "enemyFront", () => GetTree().Quit()));
    }

}
