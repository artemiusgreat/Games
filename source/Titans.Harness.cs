using Godot;
using System.Collections.Generic;

// Titans module: test choreography: duel/weapons/breakable/terrain/photo drivers.
// Split from the single-file build (round 22 cleanup); the class
// stays one partial class - see Titans.cs for the field block.
public partial class Titans
{
    // ---------- test choreography ----------

    // Staging values shared by the probes (owner, 2026-11-25:
    // named constants, rounded per the rounding rule): the
    // speed staged walkers drive at, and the limp phase's
    // measurement window in seconds.
    private const float StagingDriveSpeed = 3f;
    private const float FlatsTrioPlayerZ = 85f; // the flats trio's player mark (was the boom probe's photo mark)
    private const string ProofDir = "res://proof/"; // every harness capture writes under here
    private const float LimpMeasureSeconds = 3f;

    // The calibrated duel marks the precision suites stage
    // on: the player at (0, 18) facing the enemy at (0, -12).
    private static Vector3 TestMarkPlayer => new(0f, Main.HeightAt(0f, 18f), 18f);
    private static Vector3 TestMarkEnemy => new(0f, Main.HeightAt(0f, -12f), -12f);

    // The flats trio several camera probes stage identically:
    // player (0, 85), enemy (0, -100), foe (60, -100), all
    // facing yaw 0 on the arena flats.
    private void StageFlatsTrio()
    {
        _player.Teleport(new Vector3(0f, Main.HeightAt(0f, FlatsTrioPlayerZ), FlatsTrioPlayerZ), 0f);
        _enemy.Teleport(new Vector3(0f, Main.HeightAt(0f, ParkedEnemyZ), ParkedEnemyZ), 0f);
        _foe.Walker.Teleport(new Vector3(60f, Main.HeightAt(60f, -100f), -100f), 0f);
    }

    // ---------- stand separation test (--stand-test) ----------
    // The owner's leak (2026-11-15): cycle riders in the open
    // lab (each cycle re-enters OpenLab), then deploy and
    // count the stands. Exactly one may exist in the lab and
    // none may survive into the game world.
    private bool _splatDump;
    private bool _slopeShot; // --slope-shot: staged close views of the center relief
    private int _slopeShotFrames; // --splat-dump: bake the terrain splat to proof/splat.png and quit
    private bool _standTest;
    private enum StandTestStage { FirstSwap, SecondSwap, Deploy, AfterDeploy }
    private const int StandFirstSwapFrame = 25;
    private const int StandSecondSwapFrame = 50;
    private const int StandDeployFrame = 75;
    private const int StandWorldBackFrame = 100;
    private StandTestStage _standTestStage;
    private int _standTestFrames;

    private int StandCount()
    {
        int n = 0;
        foreach (var ch in GetChildren()) if (ch.Name == "LabStand") n++;
        return n;
    }

    private void UpdateSplatDump()
    {
        var simg = PhotoEnv.SplatImage();
        EnsureSaveDir(ProofDir + "splat.png");
        simg.SavePng(ProofDir + "splat.png");
        Dbg.Print($"SPLATDUMP saved {simg.GetWidth()}x{simg.GetHeight()}");
        GetTree().Quit(0);
    }

    private const int SlopeShotMinFrames = 4; // frames before the capture may fire
    private const int SlopeShotWallMs = 20000; // wall-ms gate (see below)
    private const int SlopeShotFiredFrames = -100000; // fire-once sentinel parked in the frame counter

    private void UpdateSlopeShot()
    {
        _slopeShotFrames++;
        // Wall time, not a frame count: on the software
        // rasterizer the first frames cost ~19s each, so a
        // 40-frame gate outlives any sane timeout.
        if (_slopeShotFrames < SlopeShotMinFrames || Time.GetTicksMsec() < SlopeShotWallMs) return;
        _slopeShotFrames = SlopeShotFiredFrames; // fire once
        QueueCapture(ProofDir + "slope_vista.png", CaptureTags.SlopeVista, CamMode.SlopeVista, () =>
            QueueCapture(ProofDir + "slope_close.png", CaptureTags.SlopeClose, CamMode.SlopeClose));
    }

    private void UpdateStandTest()
    {
        _standTestFrames++;
        switch (_standTestStage)
        {
            case StandTestStage.FirstSwap:
                if (_standTestFrames > StandFirstSwapFrame) { SetRiderSelection(RiderId.Hector, RiderId.Enyo); _standTestStage = StandTestStage.SecondSwap; }
                break;
            case StandTestStage.SecondSwap:
                if (_standTestFrames > StandSecondSwapFrame) { SetRiderSelection(RiderId.Enyo, RiderId.Hector); _standTestStage = StandTestStage.Deploy; }
                break;
            case StandTestStage.Deploy:
                if (_standTestFrames > StandDeployFrame)
                {
                    int n = StandCount();
                    Dbg.Print($"STANDTEST in-lab stands={n} (expect 1)");
                    if (n != 1) { Dbg.Err("STANDTEST FAIL: stands stacked in the lab"); GetTree().Quit(1); return; }
                    _lab.OnStart?.Invoke();
                    _standTestStage = StandTestStage.AfterDeploy;
                }
                break;
            case StandTestStage.AfterDeploy:
                if (_standTestFrames > StandWorldBackFrame)
                {
                    int n = StandCount();
                    bool worldBack = _enemy != null && _enemy.Visible;
                    Dbg.Print($"STANDTEST after-deploy stands={n} (expect 0), enemy visible={worldBack}");
                    if (n != 0 || !worldBack) { Dbg.Err("STANDTEST FAIL: stand leaked into the game world"); GetTree().Quit(1); return; }
                    Dbg.Print("STANDTEST PASS one stand per lab session, none survives the deploy");
                    GetTree().Quit(0);
                }
                break;
        }
    }

    private enum DuelPhase { Settle, EnemyWalk, Twist, Stomp, HeadOn, PlayerLimp, Aim, Captures }
    // Which stride capture the enemy-walk phase has taken
    // (owner, 2026-11-27: a counted state is an enum, not
    // a bare integer compared against literals).
    private enum StrideShot { None, Contact, Main }
    private enum SelectionStage { Idle, HoldingSwap, Restored }
    private enum PlayerLimpStep { Wreck, Forward, Reverse, Turn, Done }
    private enum AimStep { InitialChecks, RenderedPitchUp, RenderedPitchDown, RocketLob, TorsoIndependence, ProofFrame, Failed = 99 }

    // Duel gates, tolerances and staging (exact values).
    private const float LabTonnageTolerance = 0.01f;
    private const float SelHoldSeconds = 1.2f;
    private const double SettlePhaseSeconds = 1.6;
    private const double StrideGateSeconds = 0.6;
    private const double StrideTimeoutSeconds = 12;
    private const double TwistLogSeconds = 1.4;
    private const float UnmeasuredSeparation = 1e9f; // min-separation sentinel before any sample
    private const float DuelStartLineZ = 26f; // the +Z start line the twist and limp fixtures share
    private const float StompTreeX = 6f;
    private const float StompTreeZ = 16f;
    private const float StompPlayerStartZ = 27f;
    private const float StompTreeMatchTolerance = 0.6f;
    private const float StompCrushSeparation = 2.6f;
    private const float StompTreeApproachZ = 12.5f; // passed-the-tree line: beyond this Z the tree must be crushed
    private const double StompTimeoutSeconds = 14;
    private const float HeadOnPlayerStartZ = 10f;
    private const float HeadOnEnemyStartZ = -2f;
    private const double HeadOnSettleSeconds = 2.2f;
    private const float HeadOnSepDriftTolerance = 0.001f;
    private const float HeadOnContactSlack = 0.05f;
    private const float HeadOnStallSeconds = 0.5f;
    private const double HeadOnTimeoutSeconds = 16;
    private const double PlayerLimpTimeoutSeconds = 34;
    private const double LimpWreckGateSeconds = 0.15f;
    private const float LimpWreckDamage = 999f;
    private const float LimpFactorExpected = 0.55f;
    private const float LimpFactorTolerance = 0.01f;
    private const float LimpEnemyTeleportX = 30f;
    private const float LimpEnemyTeleportZ = -30f;
    private const float LimpForwardMin = 3.5f;
    private const float LimpSpeedMin = 1.5f;
    private const float LimpSpeedMax = 3.1f;
    private const float LimpReverseSeconds = 2.2f;
    private const float LimpReverseMin = 0.5f;
    private const float LimpReverseSpeed = -0.8f;
    private const float LimpTurnSeconds = 0.6f;
    private const float LimpTurnGainMin = 0.15f;
    private const double AimTimeoutSeconds = 20;
    private const double AimStepGateSeconds = 0.15f;
    private const float AimPitchHeightGap = 8f;
    private const float AimTwistSteerMin = 5f;
    private const float AimWideReachMax = 30f;
    private const float AimWideCenterTolerance = 10f;
    private const float TorsoPitchClampThreshold = -0.33f;
    private const int ReticleSweepSteps = 5;
    private const float ReticlePitchStart = -0.05f;
    private const float ReticlePitchStep = 0.02f;
    private const float ReticleCenterTolerance = 1f;
    private const float ReticleProjectionTolerance = 8f;
    private const float PitchSweepStart = -0.05f;
    private const float PitchSweepEnd = -0.50f;
    private const float PitchSweepStep = 0.025f;
    private const float BeamLengthTolerance = 3f;
    private const float TwistUnclampedThreshold = 3.39f;
    private const double AimRenderWaitSeconds = 0.3;
    private const float AimFrameUpMinY = 0.15f;
    private const float AimFrameDownMaxY = -0.08f;
    private const float RocketElevMax = 0.66f;
    private const float RocketElevMin = 0.15f;
    private const double AimSalvoTimeoutSeconds = 3;
    private const double AimTorsoCheckSeconds = 1.0;
    private const float AimLegTurnMin = 0.6f;
    private const float AimTorsoDriftMax = 0.05f;
    private const float EnemyAimHeight = 8.2f;

    private void SetPhase(DuelPhase p)
    {
        _phase = p;
        _phaseT = 0;
        _stallTimer = 0f;
        Dbg.Print($"DUEL phase={(int)p} t={_simT:F2}");
    }

    private void UpdateTest(float delta)
    {
        _phaseT += delta;
        switch (_phase)
        {
            case DuelPhase.Settle: // settle
                _player.SetDrive(0f, 0f);
                _enemy.SetDrive(Mathf.Pi, 0f);
                if (!_labChecked)
                {
                    _labChecked = true;
                    // RiderLab rules, exercised on the pure data layer:
                    // weight cap and slot type must both refuse.
                    var lab = new LoadoutState();
                    var spec = RiderSpecs.Enyo;
                    var cap = lab.Check(spec, PartId.Torso, WeaponCatalog.HeavyCannon);
                    if (cap == null || !cap.Contains("weighs"))
                    { Dbg.Err($"DUEL FAIL: lab weight cap not enforced ({cap})"); GetTree().Quit(1); return; }
                    if (lab.Check(spec, PartId.ArmR, WeaponCatalog.Autocannon) == null)
                    { Dbg.Err("DUEL FAIL: lab slot type not enforced"); GetTree().Quit(1); return; }
                    if (lab.TryAssign(spec, PartId.ArmR, WeaponCatalog.LargeLaser) != null
                        || lab.TryAssign(spec, PartId.PodL, WeaponCatalog.Lrm) != null)
                    { Dbg.Err("DUEL FAIL: lab legal fits refused"); GetTree().Quit(1); return; }
                    if (Mathf.Abs(LoadoutState.DefaultFor(RiderSpecs.Enyo).TotalWeaponWeight - 72f) > LabTonnageTolerance)
                    { Dbg.Err("DUEL FAIL: default fit tonnage != 72t"); GetTree().Quit(1); return; }
                    Dbg.Print("LABTEST PASS slot type + 13t chest cap enforced (HC refused on weight, AC refused on the mirrored energy arm), legal fits accepted, default fit = 72t (every slot filled)");
                    // Capacity rule (owner, MW4 model): every chassis'
                    // slot caps sum to exactly its tonnage.
                    foreach (var sp in RiderSpecs.All)
                    {
                        float sum = 0f; foreach (var sl in sp.Slots) sum += sl.MaxWeight;
                        if (Mathf.Abs(sum - sp.Tonnage) > LabTonnageTolerance)
                        { Dbg.Err($"DUEL FAIL: {sp.Id} slot caps sum {sum:F0}t != tonnage {sp.Tonnage:F0}t"); GetTree().Quit(1); return; }
                    }
                    Dbg.Print("LABTEST CAPACITY PASS slot caps sum to chassis tonnage for every rider");
                    // Multi-fit (owner): a hardpoint fits several
                    // weapons up to its cap - cumulative tonnage is
                    // the limit, not one weapon per slot.
                    var multi = new LoadoutState();
                    if (multi.TryAssign(spec, PartId.ArmL, WeaponCatalog.MediumLaser) != null
                        || multi.TryAssign(spec, PartId.ArmL, WeaponCatalog.MediumLaser) != null
                        || multi.TryAssign(spec, PartId.ArmL, WeaponCatalog.MediumLaser) != null
                        || multi.TryAssign(spec, PartId.ArmL, WeaponCatalog.MediumLaser) == null)
                    { Dbg.Err("DUEL FAIL: multi-fit cumulative arm cap wrong (3x ML = 9t fits the 10t arm, 4x = 12t must refuse)"); GetTree().Quit(1); return; }
                    if (multi.TryAssign(spec, PartId.PodL, WeaponCatalog.Lrm) != null
                        || multi.TryAssign(spec, PartId.PodL, WeaponCatalog.Srm) != null)
                    { Dbg.Err("DUEL FAIL: multi-fit pod refused (LRM 10t + SRM 4t = 14t <= 21t cap)"); GetTree().Quit(1); return; }
                    if (!multi.RemoveLast(PartId.PodL, out var peeled) || peeled != WeaponId.Srm || multi.WeaponIn(PartId.PodL) != WeaponId.Lrm)
                    { Dbg.Err("DUEL FAIL: multi-fit peel wrong"); GetTree().Quit(1); return; }
                    Dbg.Print("LABTEST MULTIFIT PASS hardpoints fit multiple weapons up to their tonnage cap; peel removes the last one");
                    // Distribution (owner): heavier riders carry MORE
                    // hardpoints, so their firepower survives losing
                    // a single mount.
                    if (RiderSpecs.Hector.Slots.Length < RiderSpecs.Enyo.Slots.Length)
                    { Dbg.Err("DUEL FAIL: the 100t Hector must carry at least as many hardpoints as the 75t Enyo"); GetTree().Quit(1); return; }
                    Dbg.Print($"LABTEST DISTRIBUTION PASS hardpoints: Enyo {RiderSpecs.Enyo.Slots.Length}, Hector {RiderSpecs.Hector.Slots.Length} (tied since Enyo's three-type chest mount; Hector's mounts are the roster's largest - 30t gun, 28t pod)");
                    // Rider selection (round 22): swap both chassis and
                    // verify the duel follows - parts layout, pools
                    // and equipped defs all follow the spec.
                    SetRiderSelection(RiderId.Hector, RiderId.Enyo);
                    if (_playerSpec.Id != RiderId.Hector || _enemySpec.Id != RiderId.Enyo
                        || _hpPlayer != 340f || _hpEnemy != 260f
                        || _defBallistic?.Id != WeaponId.HeavyCannon
                        || !_partsEnemy.Alive(PartId.PodL))
                    { Dbg.Err($"DUEL FAIL: rider selection swap wrong (hp {_hpPlayer}/{_hpEnemy}, def {_defBallistic?.Id})"); GetTree().Quit(1); return; }
                    _selStage = SelectionStage.HoldingSwap; _selHoldT = 0f;
                    Dbg.Print("LABTEST SELECTION swapped to Hector player; holding across live frames (HUD must survive a pod-less chassis)");
                }
                // Keep the swapped chassis deployed across many real
                // frames before restoring: swapping forth and back
                // inside one call never lets UpdateHud run against
                // the Hector parts - which is exactly how the pod_L
                // KeyNotFound crash shipped (it froze the player's
                // camera in the live game while this test stayed
                // green). If UpdateHud throws now, the frame aborts
                // before UpdateTest and this hold never completes.
                if (_selStage == SelectionStage.HoldingSwap)
                {
                    _selHoldT += delta;
                    if (_selHoldT > SelHoldSeconds)
                    {
                        SetRiderSelection(RiderId.Enyo, RiderId.Hector);
                        if (_playerSpec.Id != RiderId.Enyo || _enemySpec.Id != RiderId.Hector
                            || _hpPlayer != 260f || _hpEnemy != 340f || _defEnergy?.Id != WeaponId.LargeLaser)
                        { Dbg.Err("DUEL FAIL: rider selection restore wrong"); GetTree().Quit(1); return; }
                        _selStage = SelectionStage.Restored;
                        Dbg.Print("LABTEST SELECTION PASS chassis swap rebuilds walkers, parts, pools and defs; Hector player survived 1.2s of live HUD frames; defaults restored");
                    }
                }
                if (_phaseT > SettlePhaseSeconds && _selStage != SelectionStage.HoldingSwap) SetPhase(DuelPhase.EnemyWalk);
                break;
            case DuelPhase.EnemyWalk: // the enemy walks at the player; grab stride + wide frames
                _player.SetDrive(0f, 0f);
                _enemy.SetDrive(Mathf.Pi, 1.8f);
                if (_enemy.ConsumeStepMidCross() && _phaseT > StrideGateSeconds)
                {
                    if (_strideShot == StrideShot.None)
                    {
                        _strideShot = StrideShot.Contact;
                        Dbg.Print($"DUEL {_enemySpec.Name} CONTACT analytic {_enemy.FootReport(false)}");
                        Dbg.Print($"DUEL {_enemySpec.Name} CONTACT engine   {_enemy.FootReport(true)}");
                        QueueCapture(ProofDir + "duel_hector_stride.png", CaptureTags.Stride, CamMode.EnemySide);
                    }
                    else if (_strideShot == StrideShot.Contact)
                    {
                        _strideShot = StrideShot.Main;
                        QueueCapture(ProofDir + "duel_main.png", CaptureTags.Main, CamMode.Wide, () =>
                        {
                            SetPhase(DuelPhase.Twist);
                            _player.Teleport(new Vector3(0f, Main.HeightAt(0f, DuelStartLineZ), DuelStartLineZ), 0f);
                            _player.TorsoTwist = 1.15f;
                        });
                    }
                }
                if (_phaseT > StrideTimeoutSeconds) { Dbg.Err("DUEL FAIL: enemy stride shots missing"); GetTree().Quit(1); }
                break;
            case DuelPhase.Twist: // torso twist: legs walk -Z, torso twisted +66 deg
                _player.SetDrive(0f, 1.8f);
                _enemy.SetDrive(Mathf.Pi, 0f);
                if (_phaseT > TwistLogSeconds && !_twistLogged)
                {
                    _twistLogged = true;
                    float legs = _player.LegsYaw;
                    float torso = _player.EngineTorsoYaw();
                    float rel = Mathf.RadToDeg(RiderWalker.WrapAngle(torso - legs));
                    Dbg.Print($"DUEL TWIST legsYaw={Mathf.RadToDeg(legs):F1}deg torsoWorldYaw(engine)={Mathf.RadToDeg(torso):F1}deg relative={rel:F1}deg twistCmd={Mathf.RadToDeg(_player.TorsoTwist):F1}deg");
                    QueueCapture(ProofDir + "duel_torso_twist.png","twist",CamMode.Twist, () =>
                    {
                        SetPhase(DuelPhase.Stomp);
                        _player.TorsoTwist = 0f;
                        _player.Teleport(new Vector3(StompTreeX, Main.HeightAt(StompTreeX, StompPlayerStartZ), StompPlayerStartZ), 0f);
                        _minTreeSep = UnmeasuredSeparation;
                        _lastPlayerPos = _player.RootPos;
                    });
                }
                break;
            case DuelPhase.Stomp: // STOMP: drive straight at the tree at (6,16) - it must NOT stop the rider; the rider crushes it and walks through
                _player.SetDrive(0f, 2.6f);
                _enemy.SetDrive(Mathf.Pi, 0f);
                {
                    float sep = new Vector2(_player.RootPos.X - StompTreeX, _player.RootPos.Z - StompTreeZ).Length();
                    _minTreeSep = Mathf.Min(_minTreeSep, sep);
                    Destructible treeD = null;
                    foreach (var d in _destructibles)
                        if (d.Kind == DestrKind.Tree && Mathf.Abs(d.Center.X - StompTreeX) < StompTreeMatchTolerance && Mathf.Abs(d.Center.Z - StompTreeZ) < StompTreeMatchTolerance)
                            treeD = d;
                    if (treeD != null && treeD.State != DestrState.Intact && _minTreeSep < StompCrushSeparation)
                    {
                        Dbg.Print($"DUEL STOMP PASS tree crushed underfoot (minSep={_minTreeSep:F2}), rider walks through");
                        QueueCapture(ProofDir + "duel_collision_prop.png","prop",CamMode.Prop, () =>
                        {
                            SetPhase(DuelPhase.HeadOn);
                            _player.Teleport(new Vector3(0f, Main.HeightAt(0f, HeadOnPlayerStartZ), HeadOnPlayerStartZ), 0f);
                            _enemy.Teleport(new Vector3(0f, Main.HeightAt(0f, HeadOnEnemyStartZ), HeadOnEnemyStartZ), Mathf.Pi);
                            _minRiderSep = UnmeasuredSeparation;
                        });
                    }
                    if (_player.RootPos.Z < StompTreeApproachZ && (treeD == null || treeD.State == DestrState.Intact))
                    { Dbg.Err("DUEL FAIL: player passed the tree without stomping it"); GetTree().Quit(1); }
                    if (_phaseT > StompTimeoutSeconds) { Dbg.Err("DUEL FAIL: stomp phase timeout"); GetTree().Quit(1); }
                }
                break;
            case DuelPhase.HeadOn: // riders driven at each other
                _player.SetDrive(0f, 2.2f);
                _enemy.SetDrive(Mathf.Pi, 1.8f);
                {
                    float dist = new Vector2(_player.RootPos.X - _enemy.RootPos.X,
                                             _player.RootPos.Z - _enemy.RootPos.Z).Length();
                    _minRiderSep = Mathf.Min(_minRiderSep, dist);
                    if (_phaseT > HeadOnSettleSeconds)
                    {
                        if (Mathf.Abs(dist - _minRiderSep) < HeadOnSepDriftTolerance && dist <= _player.CollisionRadius + _enemy.CollisionRadius + RiderBody.Gap + HeadOnContactSlack)
                            _stallTimer += delta;
                        else _stallTimer = 0f;
                        if (_stallTimer > HeadOnStallSeconds)
                        {
                            Dbg.Print($"DUEL RIDER minSep={_minRiderSep:F3} finalSep={dist:F3} sumRadii={_player.CollisionRadius + _enemy.CollisionRadius:F2}");
                            QueueCapture(ProofDir + "duel_collision_rider.png", CaptureTags.Rider, CamMode.RiderSide);
                            SetPhase(DuelPhase.PlayerLimp);
                        }
                    }
                    if (_phaseT > HeadOnTimeoutSeconds) { Dbg.Err("DUEL FAIL: rider stall never detected"); GetTree().Quit(1); }
                }
                break;
            case DuelPhase.PlayerLimp: // PLAYERLIMP: wreck the PLAYER's leg_L, then drive
                    // through the real input path (ApplyPlayerInput):
                    // forward, reverse and turning must all still answer
                    // at limp throttle. Regression for the retired
                    // legsOk gate that left the player permanently stuck.
                _enemy.SetDrive(Mathf.Pi, 0f);
                if (_phaseT > PlayerLimpTimeoutSeconds) { Dbg.Err("DUEL FAIL: playerlimp phase timeout"); GetTree().Quit(1); }
                if (_pLimpStep == PlayerLimpStep.Wreck && _phaseT > LimpWreckGateSeconds)
                {
                    ResetDuel();
                    _partsPlayer.DamagePart(PartId.LegL, LimpWreckDamage);
                    if (_partsPlayer.Alive(PartId.LegL)) { Dbg.Err("DUEL FAIL: player leg_L not wrecked"); GetTree().Quit(1); break; }
                    if (Mathf.Abs(_player.SpeedFactor - LimpFactorExpected) > LimpFactorTolerance) { Dbg.Err($"DUEL FAIL: limp factor {_player.SpeedFactor:F2} != 0.55"); GetTree().Quit(1); break; }
                    _player.Teleport(new Vector3(0f, Main.HeightAt(0f, DuelStartLineZ), DuelStartLineZ), 0f);
                    _enemy.Teleport(new Vector3(LimpEnemyTeleportX, Main.HeightAt(LimpEnemyTeleportX, LimpEnemyTeleportZ), LimpEnemyTeleportZ), Mathf.Pi);
                    _playerYaw = 0f; _enemyYaw = Mathf.Pi;
                    _pLimpStart = _player.RootPos;
                    _pLimpT0 = _phaseT;
                    _pLimpStep = PlayerLimpStep.Forward;
                }
                else if (_pLimpStep == PlayerLimpStep.Forward)
                {
                    ApplyPlayerInput(delta, true, false, false, false, false);
                    if (_phaseT - _pLimpT0 > LimpMeasureSeconds)
                    {
                        float fwd = _pLimpStart.Z - _player.RootPos.Z;
                        float spdNow = _player.Speed;
                        if (fwd < LimpForwardMin || spdNow < LimpSpeedMin || spdNow > LimpSpeedMax)
                        { Dbg.Err($"DUEL FAIL: limping player barely moved fwd={fwd:F2} spd={spdNow:F2}"); GetTree().Quit(1); break; }
                        Dbg.Print($"DUEL PLAYERLIMP FWD fwd={fwd:F2} over {LimpMeasureSeconds:F0}s at spd={spdNow:F2} (limp 0.55x)");
                        _pLimpStart = _player.RootPos;
                        _pLimpT0 = _phaseT;
                        _pLimpStep = PlayerLimpStep.Reverse;
                    }
                }
                else if (_pLimpStep == PlayerLimpStep.Reverse)
                {
                    ApplyPlayerInput(delta, false, true, false, false, false);
                    if (_phaseT - _pLimpT0 > LimpReverseSeconds)
                    {
                        // Net displacement is small because the step
                        // starts at full forward speed: the walker first
                        // coasts ~1u forward while decelerating through
                        // zero. The crisp signals: net backward travel
                        // AND a live negative drive speed at the end.
                        float rev = _player.RootPos.Z - _pLimpStart.Z;
                        if (rev < LimpReverseMin || _player.Speed > LimpReverseSpeed)
                        { Dbg.Err($"DUEL FAIL: limping player cannot reverse rev={rev:F2} spd={_player.Speed:F2}"); GetTree().Quit(1); break; }
                        Dbg.Print($"DUEL PLAYERLIMP REV rev={rev:F2} net over 2.2s (after the forward coast), spd={_player.Speed:F2}");
                        _pLimpYaw0 = _playerYaw;
                        _pLimpT0 = _phaseT;
                        _pLimpStep = PlayerLimpStep.Turn;
                    }
                }
                else if (_pLimpStep == PlayerLimpStep.Turn)
                {
                    ApplyPlayerInput(delta, false, false, true, false, false);
                    if (_phaseT - _pLimpT0 > LimpTurnSeconds)
                    {
                        float gain = _playerYaw - _pLimpYaw0;
                        if (gain < LimpTurnGainMin)
                        { Dbg.Err($"DUEL FAIL: limping player cannot turn gain={gain:F3}"); GetTree().Quit(1); break; }
                        Dbg.Print($"DUEL PLAYERLIMP PASS fwd+rev+turn all answer with leg_L wrecked (turn gain={gain:F3} in 0.6s)");
                        _pLimpStep = PlayerLimpStep.Done;
                        ResetDuel();
                        SetPhase(DuelPhase.Aim);
                    }
                }
                break;
            case DuelPhase.Aim: // AIM: the aim follows the CAMERA AXIS (owner's
                    // round-27 model): pitch and twist steer
                    // TorsoAimPoint, and the crosshair is the screen
                    // center the axis projects to.
                _player.SetDrive(0f, 0f);
                _enemy.SetDrive(Mathf.Pi, 0f);
                if (_phaseT > AimTimeoutSeconds) { Dbg.Err("DUEL FAIL: aim phase timeout"); GetTree().Quit(1); }
                if (_aimStep == AimStep.InitialChecks && _phaseT > AimStepGateSeconds)
                {
                    ResetDuel();
                    _player.Teleport(TestMarkPlayer, 0f);
                    _enemy.Teleport(TestMarkEnemy, Mathf.Pi);
                    _playerYaw = 0f; _enemyYaw = Mathf.Pi;
                    _playerVel = Vector3.Zero;
                    _prevPlayerRoot = _player.RootPos;
                    _camMode = CamMode.Chase;
                    // Project through the camera the capture will use:
                    // run one camera update now, otherwise the reticle
                    // measurements use last phase's camera pose.
                    UpdateCamera(0f);
                    _player.TorsoTwist = 0f;
                    _player.TorsoPitch = 0.40f;
                    var aimUp = TorsoAimPoint();
                    _player.TorsoPitch = -0.30f;
                    var aimDown = TorsoAimPoint();
                    if (aimUp.Y < aimDown.Y + AimPitchHeightGap)
                    { Dbg.Err($"DUEL FAIL: torso pitch does not steer the aim (up Y={aimUp.Y:F1} down Y={aimDown.Y:F1})"); GetTree().Quit(1); break; }
                    _player.TorsoPitch = 0f;
                    var aimC = TorsoAimPoint();
                    _player.TorsoTwist = 1.2f;
                    var aimT = TorsoAimPoint();
                    float dxz = new Vector2(aimT.X - aimC.X, aimT.Z - aimC.Z).Length();
                    if (dxz < AimTwistSteerMin)
                    { Dbg.Err($"DUEL FAIL: torso twist does not steer the aim (dxz={dxz:F2})"); GetTree().Quit(1); break; }
                    // WIDE AIM (round 13, round-27 form): the mouse
                    // target is not bound by the torso clamp - with
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
                    if (_aimTarget != Roles.Terrain || aimWide.DistanceTo(_player.RootPos) > AimWideReachMax || scrW.DistanceTo(vpW * 0.5f) > AimWideCenterTolerance)
                    { Dbg.Err($"DUEL FAIL: wide aim cannot reach the feet (target='{_aimTarget}' dist={aimWide.DistanceTo(_player.RootPos):F1} scr=({scrW.X:F0},{scrW.Y:F0}) center=({vpW.X * 0.5f:F0},{vpW.Y * 0.5f:F0})"); GetTree().Quit(1); break; }
                    if (_player.TorsoPitch < TorsoPitchClampThreshold)
                    { Dbg.Err("DUEL FAIL: torso pitch escaped its clamp"); GetTree().Quit(1); break; }
                    _player.AimPitchWide = float.NaN;
                    Dbg.Print($"DUEL WIDEAIM PASS free mouse pitch reaches the ground in front of the feet, on the crosshair (dist {aimWide.DistanceTo(_player.RootPos):F1}u); torso pitch stays clamped");
                    // Reticle TRUTH (round 27): at every pitch the
                    // crosshair must BE the screen center, and the
                    // convergence point (what the weapons converge
                    // on) must project onto that same center - the
                    // camera axis is the aim, so cursor and impact
                    // can never disagree. (The round-8 contract,
                    // restated for center-crosshair aiming.)
                    _player.TorsoTwist = 0f;
                    var vpAim = GetViewport().GetVisibleRect().Size;
                    var ctrAim = vpAim * 0.5f;
                    for (int i = 0; i <= ReticleSweepSteps; i++)
                    {
                        _player.TorsoPitch = ReticlePitchStart + i * ReticlePitchStep;
                        UpdateCamera(0f);
                        _aimPoint = TorsoAimPoint();
                        UpdateReticle();
                        if (_cross.ReticlePos.DistanceTo(ctrAim) > ReticleCenterTolerance)
                        { Dbg.Err($"DUEL FAIL: crosshair is not at screen center (step {i}: got {_cross.ReticlePos}, center {ctrAim})"); _aimStep = AimStep.Failed; GetTree().Quit(1); break; }
                        var proj = _cam.UnprojectPosition(_reticlePoint);
                        if (proj.DistanceTo(ctrAim) > ReticleProjectionTolerance)
                        { Dbg.Err($"DUEL FAIL: convergence point off the crosshair (step {i}: proj ({proj.X:F0},{proj.Y:F0}), center ({ctrAim.X:F0},{ctrAim.Y:F0}))"); _aimStep = AimStep.Failed; GetTree().Quit(1); break; }
                    }
                    if (_aimStep == AimStep.Failed) break; // sweep failed above; Quit(1) already queued
                    // The synthesis contract, tested through the REAL
                    // weapon: cursor low on the Hector => the laser
                    // HITS him; cursor raised above him => it MISSES
                    // (the guns fire along their own lines through
                    // the cursor's point, so the pointer and the
                    // shots agree).
                    _enemyDmgEvents = 0;
                    _cdLaser = 0f;
                    // Round 27: hunt the pitch that puts the center
                    // crosshair on the Hector, the way a player
                    // does - the fixed -0.10 fixture belonged to
                    // the old free-reticle geometry.
                    bool onFoe = false;
                    for (float tp = PitchSweepStart; tp >= PitchSweepEnd; tp -= PitchSweepStep)
                    {
                        _player.TorsoPitch = tp;
                        UpdateCamera(0f);
                        _aimPoint = TorsoAimPoint();
                        if (_aimTarget == Roles.Enemy) { onFoe = true; break; }
                    }
                    if (!onFoe)
                    { Dbg.Err("DUEL FAIL: no pitch puts the crosshair on the Hector"); GetTree().Quit(1); break; }
                    FireLaser(_aimPoint);
                    if (_enemyDmgEvents < 1)
                    { Dbg.Err("DUEL FAIL: cursor low on the Hector but the laser missed him"); GetTree().Quit(1); break; }
                    int evAfterHit = _enemyDmgEvents;
                    _cdLaser = 0f;
                    // 1.0 rad, not the old 0.30: on the domed map
                    // (round 25) the test pair stands on the center
                    // hill, and the crest behind the Hector is higher
                    // than his head - a 0.30 line to the crest point
                    // legitimately clips his torso box on the way.
                    // The phase's contract is "cursor clearly above
                    // him => a miss", so the fixture aims clearly
                    // above: sky, not crest.
                    _player.TorsoPitch = 1.0f;
                    _aimPoint = TorsoAimPoint();
                    FireLaser(_aimPoint);
                    if (_enemyDmgEvents > evAfterHit)
                    { Dbg.Err("DUEL FAIL: cursor above the Hector but a shot still hit him"); GetTree().Quit(1); break; }
                    // Beam length on a miss (owner, 2026-11-25,
                    // physical beams): the beam runs its gun's
                    // full range along its own line, PAST the
                    // cursor's point - the round-20 rule that it
                    // stops at the cursor is retired.
                    float beamLen = -1f;
                    for (int i = _fx.Count - 1; i >= 0; i--)
                        if (_fx[i].Node is MeshInstance3D bmi && bmi.Mesh is BoxMesh bxm) { beamLen = bxm.Size.Z; break; }
                    float wantLen = _gunsEnergy[_gunsEnergy.Count - 1].Def.Range;
                    if (beamLen < 0f || Mathf.Abs(beamLen - wantLen) > BeamLengthTolerance)
                    { Dbg.Err($"DUEL FAIL: sky-shot beam runs {beamLen:F1}u but the firing gun's range is {wantLen:F1}u"); GetTree().Quit(1); break; }
                    Dbg.Print($"DUEL BEAMEND PASS sky-shot beam runs its gun's full range ({beamLen:F1}u) past the cursor point");
                    // Torso twist is UNCLAMPED (owner, 2026-10-29):
                    // a 195° command must stick - the torso rotates
                    // the full 360°, fully backward included.
                    _player.TorsoTwist = 3.4f;
                    if (_player.TorsoTwist < TwistUnclampedThreshold)
                    { Dbg.Err($"DUEL FAIL: torso twist {Mathf.RadToDeg(_player.TorsoTwist):F0}deg was clamped (full 360 required)"); _aimStep = AimStep.Failed; GetTree().Quit(1); break; }
                    _player.TorsoTwist = -3.4f;
                    if (_player.TorsoTwist > -TwistUnclampedThreshold)
                    { Dbg.Err($"DUEL FAIL: torso twist {Mathf.RadToDeg(_player.TorsoTwist):F0}deg was clamped (full 360 required)"); _aimStep = AimStep.Failed; GetTree().Quit(1); break; }
                    _player.TorsoTwist = 0f;
                    Dbg.Print($"DUEL AIM PASS pitch aimY {aimDown.Y:F1} -> {aimUp.Y:F1}, twist dxz={dxz:F1}, crosshair IS the convergence point: laser hits low, misses high; twist unclamped (full 360)");
                    // Rendered-pitch check next (steps 1-2): the AIM
                    // math above is property-based; these read the
                    // skeleton's actual facing after a StepWalker ran.
                    _player.TorsoTwist = 0f;
                    _player.TorsoPitch = 0.35f;
                    _aimT0 = _phaseT;
                    _aimStep = AimStep.RenderedPitchUp;
                }
                else if (_aimStep == AimStep.RenderedPitchUp && _phaseT - _aimT0 > AimRenderWaitSeconds)
                {
                    var fUp = _player.EngineTorsoFacing();
                    if (fUp.Y < AimFrameUpMinY)
                    { Dbg.Err($"DUEL FAIL: rendered torso does not pitch UP with +pitch (facingY={fUp.Y:F3})"); GetTree().Quit(1); break; }
                    _aimUpY = fUp.Y;
                    _player.TorsoPitch = -0.25f;
                    _aimT0 = _phaseT;
                    _aimStep = AimStep.RenderedPitchDown;
                }
                else if (_aimStep == AimStep.RenderedPitchDown && _phaseT - _aimT0 > AimRenderWaitSeconds)
                {
                    var fDn = _player.EngineTorsoFacing();
                    if (fDn.Y > AimFrameDownMaxY)
                    { Dbg.Err($"DUEL FAIL: rendered torso does not pitch DOWN with -pitch (facingY={fDn.Y:F3})"); GetTree().Quit(1); break; }
                    Dbg.Print($"DUEL AIM RENDER PASS rendered torso follows the pitch: up facingY={_aimUpY:F3}, down facingY={fDn.Y:F3}");
                    // Rocket-lob check (round 7): pitch up at the sky
                    // over the Hector and loose a salvo - the first
                    // rocket's launch elevation must track the
                    // pointer (bounded lob), not mortar vertically.
                    _player.TorsoTwist = 0f;
                    _player.TorsoPitch = 0.30f;
                    _cdLrm = 0f;
                    _aimProj0 = _shots.Count;
                    FireLrmSalvo(TorsoAimPoint());
                    _aimT0 = _phaseT;
                    _aimStep = AimStep.RocketLob;
                }
                else if (_aimStep == AimStep.RocketLob)
                {
                    if (_shots.Count > _aimProj0)
                    {
                        var pr = _shots[_shots.Count - 1];
                        float elev = Mathf.Asin(Mathf.Clamp(pr.Vel.Y / Mathf.Max(pr.Vel.Length(), 0.01f), -1f, 1f));
                        if (elev > RocketElevMax)
                        { Dbg.Err($"DUEL FAIL: sky-aim rocket mortared at elevation {elev:F2} rad (pointer was 0.30)"); GetTree().Quit(1); break; }
                        if (elev < RocketElevMin)
                        { Dbg.Err($"DUEL FAIL: sky-aim rocket barely rose (elevation {elev:F2})"); GetTree().Quit(1); break; }
                        Dbg.Print($"DUEL AIM ROCKET PASS sky-aim launch elevation={elev:F2} rad vs pointer 0.30 - the lob is bounded");
                        // Torso-independence setup (round 7): anchor
                        // the mouse bearing here, then spin the LEGS
                        // for a second - the torso's world bearing
                        // must not follow them.
                        _torsoWorldYaw = _player.TorsoYawWorld;
                        _aimWorld0 = _player.TorsoYawWorld;
                        _aimLegs0 = _playerYaw;
                        _aimT0 = _phaseT;
                        _aimStep = AimStep.TorsoIndependence;
                    }
                    else if (_phaseT - _aimT0 > AimSalvoTimeoutSeconds) { Dbg.Err("DUEL FAIL: sky-aim salvo never launched"); GetTree().Quit(1); }
                }
                else if (_aimStep == AimStep.TorsoIndependence)
                {
                    // Legs rotate at the A/D rate for ~1s while the
                    // mouse bearing stays put: MaintainTorsoWorldYaw
                    // (the live path's own method) must hold the
                    // torso's world yaw steady throughout.
                    _playerYaw += 0.85f * delta;
                    _player.SetDrive(_playerYaw, 0f);
                    MaintainTorsoWorldYaw();
                    if (_phaseT - _aimT0 > AimTorsoCheckSeconds)
                    {
                        float legsTurned = _playerYaw - _aimLegs0;
                        float torsoDrift = Mathf.Abs(RiderWalker.WrapAngle(_player.TorsoYawWorld - _aimWorld0));
                        if (legsTurned < AimLegTurnMin)
                        { Dbg.Err($"DUEL FAIL: torso-indep setup only turned the legs {legsTurned:F2} rad"); GetTree().Quit(1); break; }
                        if (torsoDrift > AimTorsoDriftMax)
                        { Dbg.Err($"DUEL FAIL: legs dragged the torso by {torsoDrift:F3} rad (mouse must own the bearing)"); GetTree().Quit(1); break; }
                        Dbg.Print($"DUEL AIM TORSO-INDEP PASS legs turned {legsTurned:F2} rad under a fixed mouse bearing; torso drift {torsoDrift:F3} rad");
                        // Restore the original facing for the proof
                        // frame (the spin left the legs yawed away).
                        _playerYaw = _aimLegs0;
                        _player.SetDrive(_playerYaw, 0f);
                        // Proof frame: torso pitched up - the reticle
                        // sits on the true aim point above the Hector.
                        _player.TorsoTwist = 0f;
                        _player.TorsoPitch = 0.18f;
                        _aimPoint = TorsoAimPoint();
                        UpdateReticle();
                        _aimStep = AimStep.ProofFrame;
                        QueueCapture(ProofDir + "duel_aim.png", CaptureTags.Aim, CamMode.Chase, () =>
                        {
                            QueueCapture(ProofDir + "duel_collision_rider.png", CaptureTags.Rider, CamMode.RiderSide);
                            SetPhase(DuelPhase.Captures);
                        });
                    }
                }
                break;
        }
    }

    // ---------- weapons-test choreography ----------

    private enum WeaponsPhase { Settle, Laser, Autocannon, Lrm, Blocked, ReturnFire, Kill, PlayerDeath, Environment }
    private enum WeaponsEnvStep { Stage, TreeHit, TreeSmoke, FarTargets }

    // Weapons-test timers, tolerances and staging (exact values).
    private const double WPhaseTimeoutSeconds = 40;
    private const double WSettleSeconds = 1.2;
    private const double WFirstShotSeconds = 0.1;
    private const double WLaserSecondShotSeconds = 0.65;
    private const double WLaserThirdShotSeconds = 1.2;
    private const double WLaserVerdictSeconds = 1.7;
    private const double WAcSecondShotSeconds = 1.05;
    private const double WAcVerdictSeconds = 2.8;
    private const double WLrmCaptureSeconds = 0.55;
    private const double WLrmVerdictSeconds = 3.4;
    private const int WLrmMinDamagingRockets = 5;
    private const double WBlockedShotSeconds = 0.3;
    private const double WBlockedVerdictSeconds = 1.2;
    private const float WBlockedAimHeight = 6.0f;
    private const float WBlockedTreeX = 22f, WBlockedTreeZ = -7f; // the planted blocker: on the LRM-flat line (x=22), 9u in front of the enemy
    private const float WBlockedTreeScale = 2.2f; // trunk radius 1.54u, crown radius 4.4u at this scale
    private const float WTolerance = 0.01f;
    private const float WLaserExpectedDamage = 36f;
    private const float WLaserHpExpected = 304f;
    private const float WAcExpectedDamage = 52f;
    private const float WAcHpExpected = 252f;
    private const float WAcLegHpExpected = 90f;
    private const float WJoltMin = 0.3f;
    private const float WReturnFirePlayerZ = 20f;
    private const float WReturnFireEnemyZ = -25f;
    private const double WReturnFireStrafePeriod = 0.9;
    private const float WReturnFireStrafeSpeed = 4.2f;
    private const int WReturnFireShots = 6;
    private const double WReturnFireSettleSeconds = 1.4;
    private const int WReturnFireMinHits = 5;
    private const float WReturnFireDamagePerHit = 40f; // the Heavy Cannon's damage (WeaponDef): Hector's waist gun since the AC/20 was deleted (2026-11-25) - the 18 this held was the AC/20's
    private const double WReturnFireTimeoutSeconds = 35;
    private const double WKillCaptureSeconds = 0.9;
    private const double WKillVerdictDelay = 1.8;
    private const double WKillTimeoutSeconds = 30;
    private const double WDeathShotSeconds = 0.3;
    private const double WDeathResetSeconds = 0.9;
    private const float WDeathDamage = 9999f;
    private const float WDeathHitHeight = 8f;
    private const float WEnvShotOriginHeight = 8f;
    private const float WEnvRubbleAimHeight = 2f;
    private const float WEnvRayRange = 30f;
    private const float WEnvRubbleSlack = 2f;
    private const float WEnvTreeDistance = 12f;
    private const float WEnvCrownHeightFactor = 0.6f;
    private const float WEnvCrownPitchStart = 0.15f;
    private const float WEnvCrownPitchEnd = -0.55f;
    private const float WEnvCrownPitchStep = 0.025f;
    private const float WEnvCrownTolerance = 3.5f;
    private const double WEnvSmokeTimeoutSeconds = 40;
    private const float WFarPlayerX = 77f, WFarPlayerZ = 11f;   // the far pair stands on the SE valley lane (see the FarTargets step)
    // A far-lane endpoint must stand clear of the seeded tree
    // scatter by this much: a muzzle inside a canopy cylinder
    // starts its cast inside the prop and the shot dies at T=0
    // (measured 2026-11-27: the ArmL muzzle sat inside a crown
    // 2.4u off the (77,11) root). Muzzle offset ~2.4u + the
    // largest crown radius ~2.6u, rounded up.
    private const float WFarClearRadius = 5.5f;
    private const int BearingSweepSteps = 72;               // the bearing search walks the full circle, 5 degrees at a time
    // One bearing-sweep step, shared by every bearing
    // search in this file (owner, 2026-11-27: the same
    // value with the same meaning is one constant): five
    // degrees - at 15 the LRM survey's best clear cast
    // fell short of the true diameter.
    private const float BearingSweepStep = Mathf.Pi / 36f;
    private const float WFarSearchBound = 100f;           // search candidates stay inside this (arena bound is 115)
    private static readonly Vector2[] WFarSpotNudges =
        { Vector2.Zero, new(5, 0), new(-5, 0), new(0, 5), new(0, -5),
          new(5, 5), new(-5, -5), new(5, -5), new(-5, 5),
          new(9, 0), new(-9, 0), new(0, 9), new(0, -9) };
    // The first nudge of the wanted spot that stands clear of
    // every standing tree by WFarClearRadius (XZ), on the ground.
    private Vector3 WFarClearSpot(float x, float z)
    {
        foreach (var n in WFarSpotNudges)
        {
            float cx = x + n.X, cz = z + n.Y;
            bool clear = true;
            foreach (var d in _destructibles)
            {
                if (d.State == DestrState.Gone) continue;
                float dx = d.Center.X - cx, dz = d.Center.Z - cz;
                if (dx * dx + dz * dz < WFarClearRadius * WFarClearRadius) { clear = false; break; }
            }
            if (clear) return new Vector3(cx, Main.HeightAt(cx, cz), cz);
        }
        return new Vector3(x, Main.HeightAt(x, z), z);
    }
    private const float WFarEnemyX = 39f, WFarEnemyZ = -65f;    // 85u from the player along the valley
    private const float WFarPitchStart = 0.30f;
    private const float WFarPitchEnd = -0.30f;
    private const float WFarPitchStep = 0.01f;
    private const float WFarHillPitchStart = 0.40f;
    private const float WFarHillPitchEnd = -0.60f;
    private const float WFarHillPitchStep = 0.02f;
    private const float WFarHillMinDistance = 60f;
    private const float WFarMinDamage = 11.9f;

    private void SetWPhase(WeaponsPhase p)
    {
        _wPhase = p;
        _wPhaseT = 0;
        _wShotsFired = 0;
        _wCaptured = false;
        _wDeadShotQueued = false;
        _wDeathLogged = false;
        Dbg.Print($"WPNTEST phase={(int)p} t={_simT:F2}");
    }

    private void WFail(string msg)
    {
        Dbg.Err($"WPNTEST FAIL: {msg}");
        GetTree().Quit(1);
    }

    private Vector3 EnemyAim() => _enemy.RootPos + Vector3.Up * EnemyAimHeight;

    // ---------- LRM impact probe ----------
    // Owner report (2026-10-31): LRM rockets land LOWER than the
    // cursor when shooting from the hill. The salvo solve is
    // exact on paper, so measure the real impacts: four staged
    // geometries, full salvos at a stationary target's chest,
    // every impact logged against the aim point. Cases: flat
    // control, mesa edge, mesa setback, and a >75u long shot
    // (past the ConvergeMax solve cap).
    private enum LrmCase { NotStarted = -1, FlatControl, MesaEdge, MesaSetback, Long, HomingLocked, HomingControl, Acquisition }

    // LRM probe gates and staging (exact values).
    private const float InvulnerableHp = 100000f; // staging HP parked far above any salvo the probes trade
    private const float LrmSettleSeconds = 0.6f;
    private const float LrmAcquisitionSettleSeconds = 3.0f;
    private const float LrmCaseTimeoutSeconds = 14f;
    private const int LrmSurveySteps = 72;
    private const float LrmSurveyRingRadius = 110f;
    private const float LrmSurveyChestHeight = 7.6f;
    private const float LrmSurveyOriginHeight = 15f;
    private const float LrmSurveyCastRadius = 2.5f;
    private const float LrmTargetWalkSpeed = 3.5f;
    private static Vector3 LrmFlatPlayerPos => new(22f, Main.HeightAt(22f, 24f), 24f);
    private static Vector3 LrmFlatEnemyPos => new(22f, Main.HeightAt(22f, -16f), -16f);
    private static Vector3 LrmMesaPlayerPos => new(0f, Main.HeightAt(0f, 10f), 10f);
    private static Vector3 LrmMesaEdgeEnemyPos => new(0f, Main.HeightAt(0f, -58f), -58f);
    private static Vector3 LrmSetbackPlayerPos => new(0f, Main.HeightAt(0f, 2f), 2f);
    private static Vector3 LrmSetbackEnemyPos => new(0f, Main.HeightAt(0f, -46f), -46f);
    private static Vector3 LrmHomingEnemyPos => new(22f, Main.HeightAt(22f, -52f), -52f);
    private static Vector3 LrmAcquisitionPlayerPos => new(110f, Main.HeightAt(110f, 30f), 30f);
    private static Vector3 LrmAcquisitionEnemyPos => new(-95f, Main.HeightAt(-95f, -65f), -65f);
    private static Vector3 LrmLongEnemyPos => new(0f, Main.HeightAt(0f, -100f), -100f);
    private LrmCase _lrmCase = LrmCase.NotStarted;
    private bool _lrmFired;
    private int _lrmImpactBase;
    private double _lrmT;
    private int _lrmLaunched0;
    private Vector3 _lrmAim;

    private void UpdateLrmProbe(float dt)
    {
        _lrmT += dt;
        if (_lrmCase < LrmCase.FlatControl)
        {
            _lrmCase = LrmCase.FlatControl;
            // AOT_HECTOR=1: run the same cases with a Hector
            // player - its pod_L is folded into the shoulder
            // mesh (no volumes of its own), the case that
            // salvoed into its own feet (owner, 2026-11-23).
            if (EnvVars.IsSet(EnvVars.Hector))
            { SetRiderSelection(RiderId.Hector, RiderId.Enyo); ApplyLoadout(); }
            LrmStage(LrmCase.FlatControl);
            return;
        }
        if (_lrmT < (_lrmCase == LrmCase.Acquisition ? LrmAcquisitionSettleSeconds : LrmSettleSeconds)) return; // settle after teleport (case 6 waits longer: the chase camera needs real frames to swing behind the player before the aim is staged from it)
        if (!_lrmFired)
        {
            _lrmFired = true;
            _lrmImpactBase = _lrmImpacts.Count; // return-fire impacts don't count
            _lrmAim = EnemyAim();
            _cdLrm = 0f;
            Dbg.Print($"LRMPROBE muzzleL={PodMuzzle(true)} muzzleR={PodMuzzle(false)} root={_player.RootPos}");
            // Homing cases (owner, 2026-11-25): case 4 fires
            // with a lock on the enemy, case 5 without - in
            // both the enemy then walks sideways out of the
            // aim point, so only the locked salvo can hit.
            if (_lrmCase == LrmCase.Acquisition)
            {
                // Acquisition case: the lock must come from
                // the REAL path - chase camera updated, torso
                // aimed at the enemy 226u away, AimLockWalker
                // asked. (Cases 4/5 hand the lock in, which is
                // how the 220u acquisition cap shipped
                // unnoticed in batch 22.)
                _camMode = CamMode.Chase;
                // Survey first: this arena's plain undulates
                // +-15u and the mesa stands in the middle, so
                // most 226u chords have no line of sight at
                // all (measured: three fixed chords, three
                // honest TERRAIN blocks). Rotate the enemy
                // around the player on the 226u ring (clamped
                // inside the world bound) and keep the first
                // bearing where the far cast actually reaches
                // him - the same thing a player does when he
                // picks a target he can see. The two-pass aim
                // (aim from the rider, let the boom place the
                // camera, re-aim from the camera) is how live
                // play converges; AimDirWorld's heading is
                // the NEGATED HeadingVec, so the aiming yaw
                // is atan2 of the negated offset.
                // Survey ALL bearings and keep the LONGEST
                // clear cast that locks: the rim-side bearings
                // clamp short (a 122u clear one was found
                // first and proves nothing about the old cap).
                RiderWalker lk6 = null;
                float castT6 = 0f;
                Vector3 best6 = Vector3.Zero;
                for (int step = 0; step < LrmSurveySteps; step++)
                {
                    // The enemy rides the bound RIM (r=110),
                    // not a ring around the player: from a
                    // rim-side player, ring points overshoot
                    // the disc and radial clamping dragged
                    // them along the rim, shortening every
                    // chord (longest found: 205u). Rim bearings
                    // reach the true antipode, ~220u away. The
                    // step is 5 degrees: at 15 the best clear
                    // cast measured 218.4u - the exact diameter
                    // is mesa-blocked, and the line that clears
                    // the shoulder past the 220u cap is narrow.
                    float ang = step * BearingSweepStep;
                    var ePos = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang)) * LrmSurveyRingRadius;
                    ePos.Y = Main.HeightAt(ePos.X, ePos.Z);
                    _enemy.Teleport(ePos, 0f);
                    var chest6 = _enemy.RootPos + Vector3.Up * LrmSurveyChestHeight;
                    for (int pass = 0; pass < 2; pass++)
                    {
                        var origin6 = pass == 0 ? _player.RootPos + Vector3.Up * LrmSurveyOriginHeight : _cam.GlobalPosition;
                        var from6 = chest6 - origin6;
                        _player.AimPitchWide = Mathf.Atan2(from6.Y, new Vector2(from6.X, from6.Z).Length());
                        _player.TorsoTwist = Mathf.Atan2(-from6.X, -from6.Z) - _player.LegsYaw;
                        UpdateCamera(0f);
                    }
                    TorsoAimPoint();
                    var cast6 = AimRay(_cam.GlobalPosition, AimDirWorld(), Aim.FarRange, LrmSurveyCastRadius);
                    if (cast6.Hit && cast6.Target == Roles.Enemy && cast6.T > castT6)
                    {
                        var lkTry = AimLockWalker();
                        if (lkTry == _enemy) { castT6 = cast6.T; best6 = ePos; lk6 = lkTry; }
                        Dbg.Print($"LRMPROBE case=6 survey step={step} enemy={ePos} castT={cast6.T:F1} lock={(lkTry == _enemy ? "ENEMY" : lkTry == null ? "none" : "other")}");
                    }
                }
                if (lk6 != _enemy) { Dbg.Err("LRMPROBE FAIL acquisition: no clear sightline in the arena survey locked the enemy"); GetTree().Quit(1); return; }
                // The arena's longest clear cast measures
                // 218.4u (survey, 5-degree steps): the exact
                // diameter is mesa-blocked and the shoulder
                // lines cap just under the old 220u
                // ConvergenceRange, so this case proves
                // acquisition through the real path at the
                // longest sightline the arena offers. The far
                // branch's extra reach is the rangefinder's
                // own FarRange cast - the same AimRay call
                // TorsoAimPoint already runs at 1500u every
                // frame - so the untested delta is the range
                // argument, not new machinery.
                Dbg.Print($"LRMPROBE case=6 note: longest arena sightline castT={castT6:F1}u (old cap 220u)");
                // Re-stage the winning bearing and fire on it.
                _enemy.Teleport(best6, 0f);
                _lrmAim = EnemyAim();
                var chest6w = _enemy.RootPos + Vector3.Up * LrmSurveyChestHeight;
                for (int pass = 0; pass < 2; pass++)
                {
                    var origin6 = pass == 0 ? _player.RootPos + Vector3.Up * LrmSurveyOriginHeight : _cam.GlobalPosition;
                    var from6 = chest6w - origin6;
                    _player.AimPitchWide = Mathf.Atan2(from6.Y, new Vector2(from6.X, from6.Z).Length());
                    _player.TorsoTwist = Mathf.Atan2(-from6.X, -from6.Z) - _player.LegsYaw;
                    UpdateCamera(0f);
                }
                TorsoAimPoint();
                lk6 = AimLockWalker();
                Dbg.Print($"LRMPROBE case=6 firing: enemy={best6} castT={castT6:F1} lock={(lk6 == _enemy ? "ENEMY" : lk6 == null ? "none" : "other")}");
                if (lk6 != _enemy) { Dbg.Err("LRMPROBE FAIL acquisition: the lock did not take on the re-staged winning bearing"); GetTree().Quit(1); return; }
                FireLrmSalvo(_lrmAim, lk6);
                return;
            }
            FireLrmSalvo(_lrmAim, _lrmCase == LrmCase.HomingLocked ? _enemy : null);
            return;
        }
        if ((_lrmCase == LrmCase.HomingLocked || _lrmCase == LrmCase.HomingControl || _lrmCase == LrmCase.Acquisition) && _lrmFired)
        {
            // 3.5 u/s over the ~2.1 s flight = ~7.4u of drift:
            // far outside the stale point's capsule + splash
            // for the control, well inside what tracking must
            // correct for the locked salvo.
            var ep = _enemy.RootPos + new Vector3(dt * LrmTargetWalkSpeed, 0f, 0f);
            ep.Y = Main.HeightAt(ep.X, ep.Z);
            _enemy.Teleport(ep, 0f);
        }
        int launched = _rocketsLaunched - _lrmLaunched0;
        int mine = _lrmImpacts.Count - _lrmImpactBase;
        // The queue must be EMPTY too: an early impact (a
        // rocket clipping the slope in its first steps) used
        // to complete the case at launched=1 while 15 rockets
        // were still queued, and they spilled into the next
        // case's counts (seen 2026-11-25, case 2 -> 3).
        if ((mine >= launched && launched > 0 && _lrmQueue.Count == 0) || _lrmT > LrmCaseTimeoutSeconds)
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
            Dbg.Print($"LRMPROBE case={(int)_lrmCase} launched={launched} impacts={mine} enemy={onTarget} terrain={onTerrain} other={other}" +
                (onTerrain > 0 ? $" terrainHitsMeanShort={horiz / onTerrain:F1}u meanBelowAim={vert / onTerrain:F1}u" : "") +
                $" aim={_lrmAim} playerPos={_player.RootPos} enemyPos={_enemy.RootPos}");
            if (_lrmCase == LrmCase.HomingLocked && onTarget < launched / 2)
            { Dbg.Err($"LRMPROBE FAIL homing: locked salvo hit the moved enemy only {onTarget}/{launched}"); GetTree().Quit(1); return; }
            if (_lrmCase == LrmCase.HomingControl && onTarget > 0)
            { Dbg.Err($"LRMPROBE FAIL homing control: UNLOCKED salvo hit the moved enemy {onTarget}x - the aim point was stale, so any hit means the control is broken"); GetTree().Quit(1); return; }
            if (_lrmCase == LrmCase.Acquisition && onTarget < launched / 2)
            { Dbg.Err($"LRMPROBE FAIL acquisition-homing: reticle-acquired salvo hit the moved enemy only {onTarget}/{launched}"); GetTree().Quit(1); return; }
            _lrmCase++;
            if (_lrmCase > LrmCase.Acquisition) { Dbg.Print("LRMPROBE DONE"); GetTree().Quit(0); return; }
            LrmStage(_lrmCase);
        }
    }

    private void LrmStage(LrmCase c)
    {
        _lrmT = 0f;
        _lrmFired = false;
        _lrmQueue.Clear(); // a staged case never inherits a previous salvo's tail
        _lrmImpacts.Clear();
        _lrmLaunched0 = _rocketsLaunched;
        (Vector3 pp, Vector3 ep) = c switch
        {
            LrmCase.FlatControl => (LrmFlatPlayerPos, LrmFlatEnemyPos),   // flat control, 40u
            LrmCase.MesaEdge => (LrmMesaPlayerPos, LrmMesaEdgeEnemyPos),      // mesa edge -> foot, 68u
            LrmCase.MesaSetback => (LrmSetbackPlayerPos, LrmSetbackEnemyPos),        // mesa setback -> slope foot, 48u
            LrmCase.HomingLocked => (LrmFlatPlayerPos, LrmHomingEnemyPos),  // homing: locked, target walks off (76u, flat line east of the mesa)
            LrmCase.HomingControl => (LrmFlatPlayerPos, LrmHomingEnemyPos),  // homing control: unlocked, same walk
            LrmCase.Acquisition => (LrmAcquisitionPlayerPos, LrmAcquisitionEnemyPos), // acquisition: lock via the REAL reticle path at 226u (past the old 220u cap; the arena's 115u world bound forbids a longer chord), target walks off
            _ => (LrmMesaPlayerPos, LrmLongEnemyPos),    // long, 110u (past the 75u solve cap)
        };
        _player.Teleport(pp, Mathf.Pi);
        _enemy.Teleport(ep, 0f);
        _hpPlayer = InvulnerableHp; // the target shoots back (shared combat step) - keep the shooter whole
        _partsPlayer.RestoreAll(); // and the pods intact, so every case fires a full salvo
        // The target must be whole too (found 2026-11-25):
        // cases 0-3 deal ~400 damage, so by the homing cases
        // he was DEAD - and a lock correctly refuses to track
        // a corpse, which read as homing not working.
        _hpEnemy = InvulnerableHp; _enemyDead = false;
        _partsEnemy.RestoreAll();
        Dbg.Print($"LRMPROBE stage case={(int)c} player={pp} enemy={ep}");
    }





    // ---------- Friendly-fire aim probe ----------
    // Owner (2026-11-23): with a teammate close BEHIND him,
    // missiles fired the opposite way still hit the teammate.
    // Suspect: TorsoAimPoint's convergence cast excludes only
    // the player, so a teammate standing in the camera's line
    // becomes the aim surface - the reticle point lands on his
    // back, behind the pods, and the salvo solves backward
    // onto him. Stage exactly that: teammate 8u behind the
    // player on the camera axis, enemy 120u ahead; aim at the
    // enemy, fire the salvo, watch both riders' HP.
    private enum FfPhase { Stage, ConstructLine, ResolveBehind, AimAndFireBehind, AimAndFireFront, ResolveFront }
    private const float FfStageSeconds = 1.2f;
    private const float FfAimTimeoutSeconds = 2.5f;
    private const float FfResolveSeconds = 7f;
    private const float FfHpTolerance = 0.01f;
    private const float FfEnemyAheadZ = -120f;
    private const float FfMateBehindZ = 8f;
    private const float FfMateFrontZ = -25f;
    private const float FfLineEnemyDistance = 170f;
    private const float FfMechanismCastRange = 60f;
    private const float FfMateChestHeight = 7f;
    private const float FfAimHeight = 7.6f;
    private const float FfCameraBack = 26f;
    private const float FfCameraUp = 9.5f;
    private const float FfFrontEnemyDistance = 55f;
    private const float FfFrontLineDistance = 110f;
    private FfPhase _ffPhase;
    private double _ffT;
    private Ally _ffMate;
    private float _ffMateHp0, _ffEnemyHp0;
    private bool _ffCase1;

    private void UpdateFfProbe(float dt)
    {
        _ffT += dt;
        _player.SetDrive(_playerYaw, 0f);
        if (_ffPhase == FfPhase.Stage && _ffT > FfStageSeconds)
        {
            _camMode = CamMode.Chase;
            var home = PadWorld(0); home.Y = Main.HeightAt(home.X, home.Z);
            _player.Teleport(home, 0f);
            _hpPlayer = InvulnerableHp; _hpEnemy = InvulnerableHp;
            var eSpot = home + new Vector3(0f, 0f, FfEnemyAheadZ); eSpot.Y = Main.HeightAt(eSpot.X, eSpot.Z);
            _enemy.Teleport(eSpot, Mathf.Pi);
            var mSpot = home + new Vector3(0f, 0f, FfMateBehindZ); mSpot.Y = Main.HeightAt(mSpot.X, mSpot.Z);
            _ffMate = MakeAlly(TeamRoster.Make(RiderSpecs.ById(RiderId.Hector)), Roles.AllyPrefix + "FF", 50, 0, mSpot, 0f, "FFMate");
            _ffMate.Hp = InvulnerableHp;
            _allies.Add(_ffMate);
            RefreshHitCaps(); // a hand-made ally needs its hit capsule registered, like the roster-built ones
            _ffMateHp0 = _ffMate.Hp; _ffEnemyHp0 = _hpEnemy;
            _ffPhase = FfPhase.ConstructLine; _ffT = 0f;
        }
        else if (_ffPhase == FfPhase.ConstructLine)
        {
            // Construct the live-play geometry exactly: the
            // enemy stands on the camera->mate line, beyond
            // the mate, so aiming at the enemy means looking
            // THROUGH the teammate's chest. (Free placement
            // missed twice: the boom holds the camera ~18u up,
            // and a shallow ray to a far target passes over a
            // same-height rider's head - the bug needs the
            // steeper lines of real fights and slopes.)
            var camPos = _cam.GlobalPosition;
            var mateChest = _ffMate.Walker.RootPos + Vector3.Up * FfMateChestHeight;
            var dirM = (mateChest - camPos).Normalized();
            var eSpot = camPos + dirM * FfLineEnemyDistance;
            eSpot.Y = Main.HeightAt(eSpot.X, eSpot.Z);
            _enemy.Teleport(eSpot, Mathf.Pi);
            var mech = _hit.Ray(camPos, dirM, FfMechanismCastRange, Roles.Player);
            Dbg.Print($"FFPROBE mechanism cast through mate chest: target='{mech.Target}' (mate role '{_ffMate.Role}')");
            _ffPhase = FfPhase.AimAndFireBehind; _ffT = 0f;
        }
        else if (_ffPhase == FfPhase.AimAndFireBehind)
        {
            AimAxisAt(_enemy.RootPos + Vector3.Up * FfAimHeight);
            // The harness camera parks ~18u over the player -
            // higher than the live chase boom - and from up
            // there the aim ray sails over a standing rider's
            // head. Put the camera where the boom puts it in
            // play (26u behind the torso heading, ~9.5u up)
            // before asking for the aim point.
            var hd = AimDirWorld(); hd.Y = 0f; hd = hd.Normalized();
            _cam.GlobalPosition = _player.RootPos - hd * FfCameraBack + Vector3.Up * FfCameraUp;
            // The enemy stands on THIS camera's line through
            // the mate's chest, 55u out - in clear view past
            // him. (The far placement from the parked harness
            // camera ended behind a crest from here.)
            var mateChest = _ffMate.Walker.RootPos + Vector3.Up * FfMateChestHeight;
            var dirM = (mateChest - _cam.GlobalPosition).Normalized();
            var eSpot = _cam.GlobalPosition + dirM * FfFrontEnemyDistance;
            eSpot.Y = Main.HeightAt(eSpot.X, eSpot.Z);
            _enemy.Teleport(eSpot, Mathf.Pi);
            AimAxisAt(_enemy.RootPos + Vector3.Up * FfAimHeight);
            var aim = TorsoAimPoint();
            if (_aimTarget == Roles.Enemy || _ffT > FfAimTimeoutSeconds)
            {
                Dbg.Print($"FFPROBE aim: target='{_aimTarget}' reticle={aim} player={_player.RootPos} mate={_ffMate.Walker.RootPos} cam={_cam.GlobalPosition}");
                FireSalvoVolley(aim, _podGunsL, _podGunsR);
                _ffPhase = FfPhase.ResolveBehind; _ffT = 0f;
            }
        }
        else if (_ffPhase == FfPhase.ResolveBehind && _ffT > FfResolveSeconds)
        {
            bool mateSafe = _ffMate.Hp >= _ffMateHp0 - FfHpTolerance;
            bool enemyHit = _hpEnemy < _ffEnemyHp0 - FfHpTolerance;
            Dbg.Print($"FFPROBE mate {_ffMateHp0:F0} -> {_ffMate.Hp:F0}; enemy {_ffEnemyHp0:F0} -> {_hpEnemy:F0}");
            Dbg.Print(mateSafe && enemyHit ? "FFPROBE PASS" : "FFPROBE FAIL");
            _ffCase1 = mateSafe && enemyHit;
            // Case 2 (owner's follow-up, same day): the mate in
            // FRONT, in the line of fire - now he must BLOCK:
            // the salvo stops on him and the enemy stays clean.
            var home = PadWorld(0); home.Y = Main.HeightAt(home.X, home.Z);
            _player.Teleport(home, 0f);
            var mSpot = home + new Vector3(0f, 0f, FfMateFrontZ); mSpot.Y = Main.HeightAt(mSpot.X, mSpot.Z);
            _ffMate.Walker.Teleport(mSpot, 0f);
            _ffMate.Hp = InvulnerableHp; _hpEnemy = InvulnerableHp;
            _ffMateHp0 = _ffMate.Hp; _ffEnemyHp0 = _hpEnemy;
            _ffPhase = FfPhase.AimAndFireFront; _ffT = 0f;
        }
        else if (_ffPhase == FfPhase.AimAndFireFront)
        {
            var hd = AimDirWorld(); hd.Y = 0f; hd = hd.Normalized();
            _cam.GlobalPosition = _player.RootPos - hd * FfCameraBack + Vector3.Up * FfCameraUp;
            var mateChest = _ffMate.Walker.RootPos + Vector3.Up * FfMateChestHeight;
            var dirM = (mateChest - _cam.GlobalPosition).Normalized();
            var eSpot = _cam.GlobalPosition + dirM * FfFrontLineDistance;
            eSpot.Y = Main.HeightAt(eSpot.X, eSpot.Z);
            _enemy.Teleport(eSpot, Mathf.Pi);
            AimAxisAt(_enemy.RootPos + Vector3.Up * FfAimHeight);
            var aim = TorsoAimPoint();
            if (_aimTarget == _ffMate.Role || _ffT > FfAimTimeoutSeconds)
            {
                Dbg.Print($"FFPROBE2 aim: target='{_aimTarget}' (mate role '{_ffMate.Role}') reticle={aim} player={_player.RootPos} mate={_ffMate.Walker.RootPos}");
                FireSalvoVolley(aim, _podGunsL, _podGunsR);
                _ffPhase = FfPhase.ResolveFront; _ffT = 0f;
            }
        }
        else if (_ffPhase == FfPhase.ResolveFront && _ffT > FfResolveSeconds)
        {
            bool mateHit = _ffMate.Hp < _ffMateHp0 - FfHpTolerance;
            bool enemyClean = _hpEnemy >= _ffEnemyHp0 - FfHpTolerance;
            Dbg.Print($"FFPROBE2 mate {_ffMateHp0:F0} -> {_ffMate.Hp:F0}; enemy {_ffEnemyHp0:F0} -> {_hpEnemy:F0}");
            Dbg.Print(mateHit && enemyClean ? "FFPROBE2 PASS" : "FFPROBE2 FAIL");
            GetTree().Quit(_ffCase1 && mateHit && enemyClean ? 0 : 1);
        }
    }

    // ---------- Bone probe ----------
    // Owner (2026-11-25, Prometheus screenshot): on the
    // centre-line build a laser still appeared to cross the
    // hill and the impact sat off the reticle vertical. The
    // centre line's origin is the rider's TORSO BONE - if a
    // rig parks that bone off the body's centre axis, the
    // "centre" line is off-centre for that rider only (the
    // hill probe's Enyo measures clean). This probe swaps
    // every chassis into the player seat and prints the
    // torso bone's offset in rider-local space (lateral /
    // forward / height above the root), plus each fitted
    // energy muzzle's lateral offset for scale.
    private double _boneT;
    private const float BoneSettleSeconds = 0.6f;
    private int _boneIdx = -1;
    private void UpdateBoneProbe(float dt)
    {
        _boneT += dt;
        _player.SetDrive(_playerYaw, 0f);
        if (_boneT < BoneSettleSeconds) return;
        _boneT = 0f;
        _boneIdx++;
        if (_boneIdx >= RiderSpecs.All.Length)
        {
            Dbg.Print("BONEPROBE DONE");
            GetTree().Quit(0);
            return;
        }
        var spec = RiderSpecs.All[_boneIdx];
        SetRiderSelection(spec.Id, RiderId.Enyo);
        // Bone census (owner, 2026-11-25): logic names bones
        // generically (Bones constants) - EVERY rider must
        // define every one of them, or the generic code is
        // silently rider-specific. A missing bone fails here.
        foreach (var bn in Bones.All)
            if (!_player.HasBone(bn))
            { Dbg.Err($"BONEPROBE FAIL: {spec.Id} is missing bone {bn} - the shared-armature contract is broken"); GetTree().Quit(1); return; }
        var root = _player.RootPos;
        var h = RiderWalker.HeadingVec(_player.LegsYaw);
        var right = new Vector3(-h.Z, 0f, h.X);
        var off = _player.BoneWorldPos(Bones.Torso) - root;
        string muzzles = "";
        foreach (var g in _gunsEnergy)
        {
            var mo = MuzzleForSlot(g.Slot, false) - root;
            muzzles += $" {g.Slot}:lat{mo.Dot(right):F2}";
        }
        Dbg.Print($"BONEPROBE {spec.Id}: torso local lat={off.Dot(right):F2} fwd={off.Dot(h):F2} h={off.Y:F2} | energy muzzles:{muzzles}");
    }

    // ---------- Bindings probe ----------
    // Owner (2026-11-23): the TAB family and the re-mapped
    // orders. Synthetic key events go through the REAL _Input
    // path (Input.ParseInputEvent), so the modifier routing
    // itself is what is on trial: CTRL+TAB cycles the active
    // companion, SHIFT+TAB locks the closest enemy, SHIFT+4
    // is Stop, SHIFT+5 is Protect-my-companion. The sniper
    // phases (added 2026-11-25) put the T -> RMB move on
    // trial the same way: RMB toggles, T must not.
    private enum BindingsPhase { Stage, CycleOne, CycleTwo, ClosestEnemy, OrderStop, OrderProtect, SniperOn, SniperTKey, SniperOff }
    private const float BpStageSeconds = 1.2f;
    private const float BpStepSeconds = 0.4f;
    private BindingsPhase _bpPhase;
    private double _bpT;
    private Ally _bpMate1, _bpMate2;

    private static void SendKey(Key code, bool shift = false, bool ctrl = false)
    {
        Input.ParseInputEvent(new InputEventKey
        {
            Keycode = code,
            Pressed = true,
            ShiftPressed = shift,
            CtrlPressed = ctrl
        });
    }

    private static void SendMouse(MouseButton btn)
    {
        Input.ParseInputEvent(new InputEventMouseButton
        {
            ButtonIndex = btn,
            Pressed = true
        });
    }

    private void UpdateBindingsProbe(float dt)
    {
        _bpT += dt;
        _player.SetDrive(_playerYaw, 0f);
        if (_bpPhase == BindingsPhase.Stage && _bpT > BpStageSeconds)
        {
            var home = PadWorld(0); home.Y = Main.HeightAt(home.X, home.Z);
            _player.Teleport(home, 0f);
            var s1 = home + new Vector3(15f, 0f, 0f); s1.Y = Main.HeightAt(s1.X, s1.Z);
            var s2 = home + new Vector3(30f, 0f, 0f); s2.Y = Main.HeightAt(s2.X, s2.Z);
            _bpMate1 = MakeAlly(TeamRoster.Make(RiderSpecs.ById(RiderId.Hector)), Roles.AllyPrefix + "BP1", 60, 0, s1, 0f, "BPMate1");
            _bpMate2 = MakeAlly(TeamRoster.Make(RiderSpecs.ById(RiderId.Enyo)), Roles.AllyPrefix + "BP2", 61, 0, s2, 0f, "BPMate2");
            _allies.Add(_bpMate1); _allies.Add(_bpMate2);
            var living = CompanionList().FindAll(a => !a.Dead);
            Dbg.Print($"BINDPROBE staged: companions={living.Count} first={(living.Count > 0 ? living[0].Spec.Name : "-")}");
            SendKey(Key.Tab, ctrl: true);
            _bpPhase = BindingsPhase.CycleOne; _bpT = 0f;
        }
        else if (_bpPhase == BindingsPhase.CycleOne && _bpT > BpStepSeconds)
        {
            var living = CompanionList().FindAll(a => !a.Dead);
            var cur = ActiveCompanion();
            Dbg.Print($"BINDPROBE ctrl+tab#1 -> companion={(cur != null ? cur.Spec.Name : "null")} (expect living[1] of {living.Count})");
            if (living.Count < 2 || cur != living[1]) { Dbg.Print("BINDPROBE FAIL companion cycle 1"); GetTree().Quit(1); return; }
            SendKey(Key.Tab, ctrl: true);
            _bpPhase = BindingsPhase.CycleTwo; _bpT = 0f;
        }
        else if (_bpPhase == BindingsPhase.CycleTwo && _bpT > BpStepSeconds)
        {
            var living = CompanionList().FindAll(a => !a.Dead);
            var cur = ActiveCompanion();
            int expect = living.Count >= 3 ? 2 : 0;
            Dbg.Print($"BINDPROBE ctrl+tab#2 -> companion={(cur != null ? cur.Spec.Name : "null")} (expect living[{expect}])");
            if (cur != living[expect]) { Dbg.Print("BINDPROBE FAIL companion cycle 2"); GetTree().Quit(1); return; }
            SendKey(Key.Tab, shift: true);
            _bpPhase = BindingsPhase.ClosestEnemy; _bpT = 0f;
        }
        else if (_bpPhase == BindingsPhase.ClosestEnemy && _bpT > BpStepSeconds)
        {
            var cyc = OpposingTargets();
            int best = -1; float bd = float.MaxValue;
            for (int i = 0; i < cyc.Count; i++)
            {
                if (cyc[i].Dead || cyc[i].W == null) continue;
                float d = _player.RootPos.DistanceSquaredTo(cyc[i].W.RootPos);
                if (d < bd) { bd = d; best = i; }
            }
            var tgt = ActiveTargetWalker();
            Dbg.Print($"BINDPROBE shift+tab -> targetIdx={_targetIdx} expect={best} dist={(tgt != null ? _player.RootPos.DistanceTo(tgt.RootPos) : -1f):F1}");
            if (best < 0 || _targetIdx != best) { Dbg.Print("BINDPROBE FAIL closest enemy"); GetTree().Quit(1); return; }
            SendKey(Key.Key4, shift: true);
            _bpPhase = BindingsPhase.OrderStop; _bpT = 0f;
        }
        else if (_bpPhase == BindingsPhase.OrderStop && _bpT > BpStepSeconds)
        {
            Dbg.Print($"BINDPROBE shift+4 -> order={_teamOrder} (expect Hold)");
            if (_teamOrder != TeamOrder.Hold) { Dbg.Print("BINDPROBE FAIL order 4"); GetTree().Quit(1); return; }
            SendKey(Key.Key5, shift: true);
            _bpPhase = BindingsPhase.OrderProtect; _bpT = 0f;
        }
        else if (_bpPhase == BindingsPhase.OrderProtect && _bpT > BpStepSeconds)
        {
            Dbg.Print($"BINDPROBE shift+5 -> order={_teamOrder} (expect ProtectCompanion); anchor={ProtectAnchor()} companion={(ActiveCompanion() != null ? ActiveCompanion().Walker.RootPos.ToString() : "null")}");
            if (_teamOrder != TeamOrder.ProtectCompanion) { Dbg.Print("BINDPROBE FAIL order 5"); GetTree().Quit(1); return; }
            // Sniper view moved T -> RMB (owner, 2026-11-25):
            // RMB toggles it on, T must do nothing, RMB again
            // toggles it off - all through the real _Input.
            SendMouse(MouseButton.Right);
            _bpPhase = BindingsPhase.SniperOn; _bpT = 0f;
        }
        else if (_bpPhase == BindingsPhase.SniperOn && _bpT > BpStepSeconds)
        {
            Dbg.Print($"BINDPROBE rmb#1 -> sniper={_sniper} (expect True)");
            if (!_sniper) { Dbg.Print("BINDPROBE FAIL sniper on RMB"); GetTree().Quit(1); return; }
            SendKey(Key.T);
            _bpPhase = BindingsPhase.SniperTKey; _bpT = 0f;
        }
        else if (_bpPhase == BindingsPhase.SniperTKey && _bpT > BpStepSeconds)
        {
            Dbg.Print($"BINDPROBE T -> sniper={_sniper} (expect True - T is unbound)");
            if (!_sniper) { Dbg.Print("BINDPROBE FAIL T still toggles sniper"); GetTree().Quit(1); return; }
            SendMouse(MouseButton.Right);
            _bpPhase = BindingsPhase.SniperOff; _bpT = 0f;
        }
        else if (_bpPhase == BindingsPhase.SniperOff && _bpT > BpStepSeconds)
        {
            Dbg.Print($"BINDPROBE rmb#2 -> sniper={_sniper} (expect False)");
            bool ok = !_sniper;
            Dbg.Print(ok ? "BINDPROBE PASS" : "BINDPROBE FAIL sniper off RMB");
            GetTree().Quit(ok ? 0 : 1);
        }
    }

    // ---------- Slope shot ----------
    // Owner (2026-11-23): "I still don't see any stones in
    // the mountains." The slope-rock mix in the snow shader
    // was never frame-verified (the Babylon vista capture
    // times out on this VM). Stage the player at the foot of
    // the steepest HIGH slope on the real gameplay surface
    // and take the standard chase capture facing uphill.
    private enum StoneShotPhase { Search, Captured }
    private const float SsStageSeconds = 1.2f;
    private const float StoneScanBound = 860f;
    private const float StoneScanStep = 8f;
    private const float StoneMinHeight = 300f;
    private const float StoneMinSlope = 0.6f;
    private const float StoneSlopeProbeOffset = 6f;
    private const float StoneNeighborhoodOffset = 24f;
    private const float StoneCamBack = 260f;
    private const float StoneCamSide = 90f;
    private const float StoneCamUp = 40f;
    private const float StoneCamGroundClearance = 10f;
    private const float StoneCamTargetUp = 10f;
    private StoneShotPhase _ssPhase;
    private double _ssT;

    private void UpdateStoneShot(float dt)
    {
        _ssT += dt;
        _player.SetDrive(_playerYaw, 0f);
        if (_ssPhase == StoneShotPhase.Search && _ssT > SsStageSeconds)
        {
            // A BROAD face, not the single steepest cell: the
            // first staging picked a 76-degree needle remnant
            // and the rider slid off it. Score each candidate
            // by the WEAKEST slope in its +-24u neighbourhood -
            // the winner is steep everywhere around it.
            float SlopeAt(float x, float z)
            {
                float gx = (Main.HeightAt(x + StoneSlopeProbeOffset, z) - Main.HeightAt(x - StoneSlopeProbeOffset, z)) / 12f;
                float gz = (Main.HeightAt(x, z + StoneSlopeProbeOffset) - Main.HeightAt(x, z - StoneSlopeProbeOffset)) / 12f;
                return Mathf.Sqrt(gx * gx + gz * gz);
            }
            Vector3 best = Vector3.Zero, bgrad = Vector3.Zero;
            float bs = 0f, bh = 0f;
            for (float x = -StoneScanBound; x <= StoneScanBound; x += StoneScanStep)
            for (float z = -StoneScanBound; z <= StoneScanBound; z += StoneScanStep)
            {
                float h = Main.HeightAt(x, z);
                if (h < StoneMinHeight) continue; // high faces only - no river banks or site rims
                float s0 = SlopeAt(x, z);
                if (s0 < StoneMinSlope) continue; // ~31 deg at the centre, minimum
                float sMin = Mathf.Min(Mathf.Min(SlopeAt(x - StoneNeighborhoodOffset, z), SlopeAt(x + StoneNeighborhoodOffset, z)),
                                       Mathf.Min(SlopeAt(x, z - StoneNeighborhoodOffset), SlopeAt(x, z + StoneNeighborhoodOffset)));
                float score = Mathf.Min(s0, sMin);
                if (score > bs)
                {
                    bs = score;
                    best = new Vector3(x, h, z); bh = h;
                    float gx = (Main.HeightAt(x + StoneSlopeProbeOffset, z) - Main.HeightAt(x - StoneSlopeProbeOffset, z)) / 12f;
                    float gz = (Main.HeightAt(x, z + StoneSlopeProbeOffset) - Main.HeightAt(x, z - StoneSlopeProbeOffset)) / 12f;
                    bgrad = new Vector3(gx, 0f, gz);
                }
            }
            var up = bgrad.Normalized();
            // Park the camera off the face (the chase boom
            // buries itself in a 50-degree hillside): downhill
            // and to the side, above its own ground, looking
            // at the face point and a little up it.
            var side = new Vector3(-up.Z, 0f, up.X);
            // ~280u out (owner's flat-mountains verdict, 2026-11-23):
            // his screenshots are gameplay-distance views, and the
            // terrain shader fades its detail with range - a 60u
            // park never exercised the fade and certified a look
            // he never sees. Frame from the distance he plays at.
            var camPos = best - up * StoneCamBack + side * StoneCamSide + Vector3.Up * StoneCamUp;
            float camGround = Main.HeightAt(camPos.X, camPos.Z) + StoneCamGroundClearance;
            if (camPos.Y < camGround) camPos.Y = camGround;
            _stoneCamPos = camPos;
            _stoneCamTarget = best + up * StoneCamTargetUp;
            Dbg.Print($"STONESHOT point={best} h={bh:F0} weakest-slope={Mathf.RadToDeg(Mathf.Atan(bs)):F1}deg cam={camPos}");
            QueueCapture(ProofDir + "stone.png","stone",CamMode.Stone, () => GetTree().Quit());
            _ssPhase = StoneShotPhase.Captured;
        }
    }

    // ---------- Controls popup capture ----------
    private const float CsDialogSeconds = 1.5f;
    private const float CsCaptureSeconds = 2.6f;
    private double _csT;
    private void UpdateControlsShot(float dt)
    {
        _csT += dt;
        if (_csT > CsDialogSeconds && _csT <= CsDialogSeconds + dt && _controlsDialog != null) _controlsDialog.Open();
        if (_csT > CsCaptureSeconds)
        {
            var img = GetViewport().GetTexture().GetImage();
            img.SavePng(ProofDir + "controls.png");
            Dbg.Print("CONTROLSSHOT saved");
            GetTree().Quit(0);
        }
    }

    // ---------- Guard targeting probe ----------
    // Owner reports (2026-11-23): defenders walked backward
    // and ignored the team member shooting them - traced to
    // NearestTeamWalker's missing team filter (a guard's
    // nearest "team rider" was itself). Stage it: put the
    // player 30u from a village guard, read its target; then
    // put a TEAMMATE 12u from it and read again - the guard
    // must engage the player first, then the nearer teammate.
    private enum GuardPhase { Stage, PlayerTarget, MateTarget, BlockedStage, AlertCheck }
    private const float GpStageSeconds = 1.5f;
    private const float GpObserveSeconds = 4f;
    private const float GpBlockedSeconds = 3.5f;
    private const float GpAlertSeconds = 3f;
    private const float GpPlayerSpotOffsetX = 30f;
    private const float GpMateSpotOffsetX = -12f;
    private const float GpChestHeight = 8f;
    private const int GpSpotRingSteps = 16;
    // Ring radii (world units) the hidden-spot search tries
    // around the second guard's home, nearest first.
    private static readonly float[] GpSpotRadii = { 70f, 100f, 130f, 160f };
    private const float GpLosStepSize = 6f;
    private const float GpLosClearance = 0.75f;
    private const float GpAlertDamage = 5f;
    private const float GpAlertHitDistance = 10f;
    private GuardPhase _gpPhase;
    private double _gpT;
    private Ally _gpGuard, _gpMate, _gpGuard2;
    private int _gpShots0;

    private void UpdateGuardProbe(float dt)
    {
        // The harness dispatch replaces the live sim block, so
        // step the guard AI here (no harness ever needed it
        // before this probe existed).
        UpdateGuardAI(dt);
        _gpT += dt;
        if (_gpPhase == GuardPhase.Stage && _gpT > GpStageSeconds)
        {
            foreach (var a in _allies)
            {
                if (a.Guard && !a.Dead && _gpGuard == null) _gpGuard = a;
                if (!a.Guard && a.Team == 0 && !a.Dead && _gpMate == null) _gpMate = a;
            }
            if (_gpGuard == null) { Dbg.Print("GUARDPROBE no guard staged"); GetTree().Quit(1); return; }
            _hpPlayer = InvulnerableHp;
            var spot = _gpGuard.GuardHome + new Vector3(GpPlayerSpotOffsetX, 0f, 0f);
            spot.Y = Main.HeightAt(spot.X, spot.Z);
            _player.Teleport(spot, -Mathf.Pi / 2f);
            _gpShots0 = _allyShots;
            Dbg.Print($"GUARDPROBE guard={_gpGuard.Role} home={_gpGuard.GuardHome} mate={(_gpMate != null ? _gpMate.Role : "none")}");
            _gpPhase = GuardPhase.PlayerTarget; _gpT = 0f;
        }
        else if (_gpPhase == GuardPhase.PlayerTarget && _gpT > GpObserveSeconds)
        {
            var nearW = NearestTeamWalker(_gpGuard.Walker.RootPos);
            Dbg.Print($"GUARDPROBE t4 target={(_gpGuard.TargetW != null ? TagOf(_gpGuard.TargetW) : "none")} engaged={_gpGuard.Engaged} guardShots={_allyShots - _gpShots0} playerPos={_player.RootPos} guardPos={_gpGuard.Walker.RootPos} dist={_gpGuard.Walker.RootPos.DistanceTo(_player.RootPos):F0} nearTeam={(nearW != null ? TagOf(nearW) : "null")} tickReady={TickReady(_gpGuard.Walker)} isMission={WorldConfig.IsMission}");
            if (_gpMate != null)
            {
                var spot = _gpGuard.GuardHome + new Vector3(GpMateSpotOffsetX, 0f, 0f);
                spot.Y = Main.HeightAt(spot.X, spot.Z);
                _gpMate.Walker.Teleport(spot, Mathf.Pi / 2f);
            }
            _gpPhase = GuardPhase.MateTarget; _gpT = 0f;
        }
        else if (_gpPhase == GuardPhase.MateTarget && _gpT > GpObserveSeconds)
        {
            Dbg.Print($"GUARDPROBE t8 target={(_gpGuard.TargetW != null ? TagOf(_gpGuard.TargetW) : "none")} engaged={_gpGuard.Engaged} guardShots={_allyShots - _gpShots0}");
            // Detection phases (owner, 2026-11-25): a SECOND
            // guard, never engaged - stage the player close
            // but behind terrain and expect NO engagement;
            // then land a player hit on it and expect the
            // alert to engage it sight unseen.
            foreach (var a in _allies)
                if (a.Guard && !a.Dead && a != _gpGuard && _gpGuard2 == null) _gpGuard2 = a;
            if (_gpGuard2 == null) { Dbg.Print("GUARDPROBE no second guard"); GetTree().Quit(1); return; }
            var home2 = _gpGuard2.GuardHome;
            var gChest = _gpGuard2.Walker.RootPos + Vector3.Up * GpChestHeight;
            Vector3 spot = Vector3.Zero; bool found = false;
            foreach (float r in GpSpotRadii)
            {
                for (int k = 0; k < GpSpotRingSteps && !found; k++)
                {
                    float ang = k * Mathf.Tau / GpSpotRingSteps;
                    var cand = home2 + new Vector3(Mathf.Cos(ang) * r, 0f, Mathf.Sin(ang) * r);
                    cand.Y = Main.HeightAt(cand.X, cand.Z);
                    var pChest = cand + Vector3.Up * GpChestHeight;
                    bool blocked = false;
                    int steps = Mathf.Max(2, (int)(gChest.DistanceTo(pChest) / GpLosStepSize));
                    for (int i = 1; i < steps; i++)
                    {
                        var p = gChest.Lerp(pChest, (float)i / steps);
                        if (Main.HeightAt(p.X, p.Z) > p.Y - GpLosClearance) { blocked = true; break; }
                    }
                    if (blocked) { spot = cand; found = true; }
                }
                if (found) break;
            }
            if (!found) { Dbg.Print("GUARDPROBE no blocked spot found near guard2"); GetTree().Quit(1); return; }
            _gpGuard2.Engaged = false; _gpGuard2.Alerted = false;
            _player.Teleport(spot, 0f);
            Dbg.Print($"GUARDPROBE los-stage guard2={_gpGuard2.Role} spot={spot} dist={_gpGuard2.Walker.RootPos.DistanceTo(spot):F0} losClear={HasLineOfSight(_gpGuard2.Walker, _player)}");
            _gpPhase = GuardPhase.BlockedStage; _gpT = 0f;
        }
        else if (_gpPhase == GuardPhase.BlockedStage && _gpT > GpBlockedSeconds)
        {
            Dbg.Print($"GUARDPROBE los-blocked engaged={_gpGuard2.Engaged} target={(_gpGuard2.TargetW != null ? TagOf(_gpGuard2.TargetW) : "none")} (expect False/none)");
            var hit = new HitModel.Result { Hit = true, T = 1f, Point = _gpGuard2.Walker.RootPos + Vector3.Up * GpChestHeight, Target = _gpGuard2.Role, Zone = HitZone.Torso, ZoneFrac = 0.6f, Part = PartId.None };
            ApplyDamage(_gpGuard2.Walker, GpAlertDamage, hit, Roles.Player, GpAlertHitDistance, "cannon");
            Dbg.Print($"GUARDPROBE alert-applied alerted={_gpGuard2.Alerted} (expect True)");
            _gpPhase = GuardPhase.AlertCheck; _gpT = 0f;
        }
        else if (_gpPhase == GuardPhase.AlertCheck && _gpT > GpAlertSeconds)
        {
            Dbg.Print($"GUARDPROBE los-alerted engaged={_gpGuard2.Engaged} target={(_gpGuard2.TargetW != null ? TagOf(_gpGuard2.TargetW) : "none")} (expect True/player)");
            Dbg.Print("GUARDPROBE DONE");
            GetTree().Quit(0);
        }
    }

    // ---------- Debris-on-a-slope probe ----------
    // Owner report (2026-11-23): after shooting a Hector's
    // leg, a chunk hung in the air beside him. Stage it: a
    // Hector on the steepest slope near the home pad, break
    // everything that detaches, wreck a leg, walk him away
    // for five seconds, then dump every debris piece's rest
    // state against the terrain under its own box and take
    // the chase frame.
    // Debris dump labels: the state at the break, then
    // after the walk-away (the t0/t5 tags the dump prints).
    private const string DumpTagAtBreak = "t0";
    private const string DumpTagAfterWalk = "t5";
    private enum DebrisPhase { FindSlope, BreakParts, WalkAway, Finish }
    private const float DbStageSeconds = 1.2f;
    private const float DbBreakSeconds = 0.5f;
    private const float DbWalkSeconds = 5f;
    private const float DbFinishSeconds = 1.5f;
    private const float DbScanBound = 900f;
    private const float DbScanStep = 25f;
    private const float DbSlopeProbeOffset = 3f;
    private const float DbBreakDamage = 100000f;
    private const float DbWalkDriveSpeed = 0.55f;
    private DebrisPhase _dbPhase;
    private double _dbT;

    private void UpdateDebrisProbe(float dt)
    {
        _dbT += dt;
        if (_dbPhase == DebrisPhase.FindSlope && _dbT > DbStageSeconds)
        {
            SetRiderSelection(RiderId.Hector, RiderId.Enyo); ApplyLoadout();
            _camMode = CamMode.Chase;
            // Find the steepest ground on the map (the pad
            // fan only found flattened site ground, ~12 deg -
            // the slope conform never engaged there).
            Vector3 best = PadWorld(0); float bestSlope = 0f;
            for (float gx = -DbScanBound; gx <= DbScanBound; gx += DbScanStep)
            for (float gz = -DbScanBound; gz <= DbScanBound; gz += DbScanStep)
            {
                float sl = Mathf.Abs(Main.HeightAt(gx + DbSlopeProbeOffset, gz) - Main.HeightAt(gx - DbSlopeProbeOffset, gz))
                         + Mathf.Abs(Main.HeightAt(gx, gz + DbSlopeProbeOffset) - Main.HeightAt(gx, gz - DbSlopeProbeOffset));
                if (sl > bestSlope) { bestSlope = sl; best = new Vector3(gx, Main.HeightAt(gx, gz), gz); }
            }
            _player.Teleport(best, 0f);
            _hpPlayer = InvulnerableHp; _hpEnemy = InvulnerableHp;
            Dbg.Print($"DEBRISPROBE staged at {best} slopeMetric={bestSlope:F1}");
            _dbPhase = DebrisPhase.BreakParts; _dbT = 0f;
        }
        else if (_dbPhase == DebrisPhase.BreakParts && _dbT > DbBreakSeconds)
        {
            _partsPlayer.DamagePart(PartId.ArmL, DbBreakDamage);
            if (_partsPlayer.Has(PartId.Gun)) _partsPlayer.DamagePart(PartId.Gun, DbBreakDamage);
            _partsPlayer.DamagePart(PartId.LegL, DbBreakDamage);
            _partsPlayer.DumpDebris(DumpTagAtBreak);
            _dbPhase = DebrisPhase.WalkAway; _dbT = 0f;
        }
        else if (_dbPhase == DebrisPhase.WalkAway)
        {
            _player.SetDrive(0f, DbWalkDriveSpeed); // walk away from the wreck site
            if (_dbT > DbWalkSeconds)
            {
                _player.SetDrive(0f, 0f);
                _partsPlayer.DumpDebris(DumpTagAfterWalk);
                QueueCapture(ProofDir + "debris_probe.png", CaptureTags.Debris, CamMode.Chase);
                _dbPhase = DebrisPhase.Finish; _dbT = 0f;
            }
        }
        else if (_dbPhase == DebrisPhase.Finish && _dbT > DbFinishSeconds)
        {
            Dbg.Print("DEBRISPROBE DONE");
            GetTree().Quit(0);
        }
    }

    // ---------- PPC long-lane probe ----------
    // Owner report (2026-11-23): a target read 0.91 km on the
    // panel and the PPC did no damage. Stage the exact geometry
    // in the mission world: the enemy 910u out on a lane the
    // game's own cast (at the PPC's 1000 reach) confirms rests
    // on the enemy, then fire the energy rack and read the HP
    // delta. PASS = the PPC damages her at that distance.
    private enum PpcPhase { StageRack, FindLane, CheckDamage }
    private const float PpcStageSeconds = 1.2f;
    private const float PpcRequiredRange = 1000f; // a rack whose longest energy reach is the PPC's 1000u
    private const float PpcLaneDistance = 910f;
    private const float PpcLaneStepDegrees = 12f;
    private const int PpcMaxTries = 24;
    private const float PpcCheckSeconds = 0.6f;
    private const float PpcHpTolerance = 0.01f;
    private const float PpcAimHeight = 7.6f;
    private PpcPhase _ppcPhase;
    private double _ppcT;
    private int _ppcTry;
    private float _ppcHp0;
    private float _ppcCastT;

    private void UpdatePpcProbe(float dt)
    {
        _ppcT += dt;
        _player.SetDrive(_playerYaw, 0f);
        if (_ppcPhase == PpcPhase.StageRack && _ppcT > PpcStageSeconds)
        {
            _camMode = CamMode.Chase;
            // Stage a rider whose rack HAS a PPC (the mission
            // default player's longest energy reach is the Large
            // Laser's 750 - staging his lane proved nothing).
            SetRiderSelection(RiderId.Hector, RiderId.Enyo); ApplyLoadout();
            float rackMax = 0f; foreach (var g in _gunsEnergy) rackMax = Mathf.Max(rackMax, g.Def.Range);
            Dbg.Print($"PPCCPROBE rack: energyMax={rackMax:F0} (PPC fitted: {rackMax >= PpcRequiredRange})");
            if (rackMax < PpcRequiredRange) { Dbg.Err("PPCCPROBE FAIL: staged rack has no PPC"); GetTree().Quit(1); return; }
            var home = PadWorld(0); home.Y = Main.HeightAt(home.X, home.Z);
            _player.Teleport(home, 0f);
            _hpPlayer = InvulnerableHp; _hpEnemy = InvulnerableHp;
            _ppcPhase = PpcPhase.FindLane; _ppcT = 0f; _ppcTry = 0;
        }
        else if (_ppcPhase == PpcPhase.FindLane)
        {
            float ang = _ppcTry * Mathf.DegToRad(PpcLaneStepDegrees);
            var dirE = new Vector3(0f, 0f, -1f).Rotated(Vector3.Up, ang);
            var spot = _player.RootPos + dirE * PpcLaneDistance;
            spot.Y = Main.HeightAt(spot.X, spot.Z);
            _enemy.Teleport(spot, RiderWalker.YawOf(-dirE));
            AimAxisAt(_enemy.RootPos + Vector3.Up * PpcAimHeight);
            var cast = _hit.Ray(_cam.GlobalPosition, AimDirWorld(), PpcRequiredRange, Roles.Player);
            if (!cast.Hit || cast.Target != Roles.Enemy)
            {
                if (++_ppcTry > PpcMaxTries) { Dbg.Err("PPCCPROBE FAIL: no clear 910u lane found for staging"); GetTree().Quit(1); }
                _ppcT = 0f;
                return;
            }
            _ppcCastT = cast.T;
            _ppcHp0 = _hpEnemy;
            FireLaser(TorsoAimPoint());
            Dbg.Print($"PPCCPROBE fired: laneTry={_ppcTry} castT={cast.T:F1} playerToEnemy={_player.RootPos.DistanceTo(_enemy.RootPos):F1} enemyHp={_ppcHp0:F0}");
            _ppcPhase = PpcPhase.CheckDamage; _ppcT = 0f;
        }
        else if (_ppcPhase == PpcPhase.CheckDamage && _ppcT > PpcCheckSeconds)
        {
            if (_hpEnemy < _ppcHp0 - PpcHpTolerance)
                Dbg.Print($"PPCCPROBE PASS: PPC damaged the enemy at castT={_ppcCastT:F1} (hp {_ppcHp0:F0} -> {_hpEnemy:F0})");
            else
                Dbg.Err($"PPCCPROBE FAIL: no damage at castT={_ppcCastT:F1} (hp {_ppcHp0:F0} -> {_hpEnemy:F0})");
            GetTree().Quit(_hpEnemy < _ppcHp0 - PpcHpTolerance ? 0 : 1);
        }
    }

    // ---------- free-for-all probe ----------
    // Stages all three riders 45u apart on the mesa and lets the
    // two AIs play: PASS requires both foes to have closed with
    // someone, both to have fired, and real damage somewhere.
    private const float FfaTimeoutSeconds = 40f;
    private const float FfaMoveMin = 8f;
    private const float FfaPlayerZ = 52f;
    private const float FfaEnemyX = 52f;
    private const float FfaEnemyZ = -30f;
    private const float FfaFoeX = -52f;
    private double _ffaT;
    private bool _ffaStaged;
    private Vector3 _ffaHome0, _ffaHome1;

    // --view-shot: the owner's hilltop case - the player low at
    // the mesa foot, a foe standing on the top. Round 27 form:
    // BEFORE frame at a level view (crosshair on the flats, foe
    // above it); then the wide pitch sweeps its full travel and
    // the foe's chest must cross the screen center; AFTER frame
    // captured at the crossing pitch, crosshair on the foe.
    private float _viewT;
    private enum ViewStage { NotStarted = -1, Stage, BeforeCapture, Sweep, HoldBest, CaptureAfter }
    private const float ViewStageSeconds = 0.6f;
    private const float ViewPitchStart = -1.35f;
    private const float ViewPitchStep = 0.03f;
    private const float ViewPitchEnd = 1.35f;
    private const float ViewReachableTolerance = 10f;
    private const float ViewAfterSeconds = 0.4f;
    private const float ViewBestUnmeasured = 1e9f;
    private const float ViewChestHeightFactor = 0.68f;
    private ViewStage _viewStage = ViewStage.NotStarted;
    private float _viewPitch, _viewBest, _viewBestPitch;
    // --point-test (owner, 2026-11-01: "lookup is clamped - I
    // couldn't point the reticle at the top of the hill"): a
    // player simulation. The foe stands on the mesa top; the
    // player stands at a ladder of ranges on the flats and the
    // skirt, and the wide pitch sweeps its full travel through
    // the real per-frame path. For each stand, report the closest
    // the foe's chest ever gets to the SCREEN CENTER on screen:
    // under round-27 center-crosshair aiming the center IS the
    // reticle - if no pitch puts the chest on it, pointing at
    // the hilltop is impossible from there - measured, not argued.
    // (The probe originally measured the free reticle point and
    // found the close-steep dead zone that decided round 27.)
    private static readonly float[] PointRanges = { 80f, 65f, 58f, 45f, 35f, 26f };
    private static readonly float[] PointRangesClose = { 24f, 21f, 18f, 16f };
    private float[] ActivePointRanges => _pointClose ? PointRangesClose : PointRanges;
    private const int PointStageNone = -1; // no stand staged yet / stand finished
    private const int PointStageDone = -99; // all stands measured
    private const float PointSettleSeconds = 0.25f;
    private const float PointSweepStartPitch = -1.34f;
    private const float PointPitchStart = -1.35f;
    private const float PointPitchDbgStart = -1.33f;
    private const float PointPitchDbgStep = 0.15f;
    private const float PointPitchStep = 0.03f;
    private const float PointPitchEnd = 1.35f;
    private const float PointReachableTolerance = 10f;
    private const float PointWalkDriveSpeed = 2.2f;
    private const float PointChestHeightFactor = 0.68f;
    private const float PointBestUnmeasured = 1e9f; // best-miss sentinel before any sample
    private const float PointFoeCloseZ = 11f;
    private const float PointFoeOffsetZ = 10f;
    private int _pointNext = 0;

    private void UpdatePointTest(float dt)
    {
        _aimPoint = TorsoAimPoint();
        _pointT += dt;
        if (_pointStage == PointStageNone)
        {
            if (_pointNext >= ActivePointRanges.Length)
            {
                Dbg.Print("POINTTEST DONE");
                GetTree().Quit(0);
                _pointStage = PointStageDone;
                return;
            }
            int idx = _pointNext;
            _pointStage = idx;
            float r = ActivePointRanges[idx];
            _player.Teleport(new Vector3(0f, Main.HeightAt(0f, r), r), 0f); // yaw 0 faces -Z (the mesa) directly - no staging turn to pollute the early samples
            float foeZ = _pointClose ? PointFoeCloseZ : (_pointCenter ? 0f : PointFoeOffsetZ);
            _enemy.Teleport(new Vector3(0f, Main.HeightAt(0f, foeZ), foeZ), 0f);
            _player.SetDrive(0f, 0f);
            _player.TorsoTwist = 0f;
            _hpPlayer = InvulnerableHp; _hpEnemy = InvulnerableHp;
            _pointPitch = PointPitchStart; // round 27: sweep the FULL wide travel - under center-crosshair aiming a close top-edge target is crossed at NEGATIVE pitch (the camera hovers above its chest)
            _pointDbgNext = PointPitchDbgStart;
            _pointBest = PointBestUnmeasured;
            _pointBestPitch = 0f;
            _pointT = 0f;
            Dbg.Print($"POINTTEST staged {(_pointHector ? RiderId.Hector : RiderId.Enyo)} r={r:F0} labOpen={_labOpen} camMode={_camMode} frozen={_frozen} playerSpec={_playerSpec.Id}");
            return;
        }
        if (_pointStage >= 0)
        {
            if (_pointT < PointSettleSeconds) return; // settle after the teleport
            if (_pointPitch <= PointSweepStartPitch) Dbg.Print($"POINTSWEEP start stage={_pointStage} walk={_pointWalk} dbgNext={_pointDbgNext:F2}");
            if (_pointWalk) _player.SetDrive(0f, PointWalkDriveSpeed); // yaw 0 faces -Z: toward the mesa from the +Z stands (HeadingVec = (-sin, -cos))
            _player.AimPitchWide = _pointPitch;
            _player.TorsoPitch = _pointPitch;
            var chest = _enemy.RootPos + Vector3.Up * (_enemySpec.Height * PointChestHeightFactor);
            bool chestBehind = _cam.IsPositionBehind(chest);
            var fs = _cam.UnprojectPosition(chest);
            var ctrP = GetViewport().GetVisibleRect().Size * 0.5f;
            if ((_pointStage == 0 || _pointStage == ActivePointRanges.Length - 1) && _pointPitch >= _pointDbgNext)
            {
                _pointDbgNext += PointPitchDbgStep;
                Dbg.Print($"POINTDBG w={_pointWalk} p={_pointPitch:F2} behind={chestBehind} ctr=({ctrP.X:F0},{ctrP.Y:F0}) chest=({fs.X:F0},{fs.Y:F0}) legsYaw={_player.LegsYaw:F2} torsoYaw={_player.TorsoYawWorld:F2} root={_player.RootPos} spd={_player.Speed:F1} camPos={_cam.GlobalPosition} retPt={_reticlePoint}");
            }
            if (!chestBehind)
            {
                float d = (fs - ctrP).Length();
                if (d < _pointBest) { _pointBest = d; _pointBestPitch = _pointPitch; }
            }
            _pointPitch += PointPitchStep;
            if (_pointPitch > PointPitchEnd)
            {
                int idx = _pointStage;
                float r = ActivePointRanges[idx];
                Dbg.Print($"POINTTEST {(_pointHector ? RiderId.Hector : RiderId.Enyo)} stand r={r:F0} bestCenterMiss={_pointBest:F0}px at pitch {_pointBestPitch:F2} " +
                    (_pointBest <= PointReachableTolerance ? "REACHABLE" : "UNREACHABLE - the crosshair never lands on the hilltop chest"));
                _pointNext = idx + 1;
                _pointStage = PointStageNone;
            }
        }
    }

    // --boom-shot (round 28, owner: "the player's rider should
    // be always visible"): sweep the full pitch travel on two
    // stands (flats, mesa skirt) and print the rider's head/feet
    // screen positions per step - the measured visibility
    // envelope of the boom camera. Captures at the extremes and
    // level on the flats.
    private enum BoomStage { NotStarted = -1, Flats, Skirt, Finished }
    private const float BoomShotGateSeconds = 0.3f;
    private const float BoomShotPitchStart = -1.35f;
    private const float BoomShotPitchStep = 0.15f;
    private const float BoomShotPitchEnd = 1.36f;
    private const float BoomShotCapPitch = 1.35f;
    private const float BoomShotCapTolerance = 0.01f;
    private const float BoomShotSkirtZ = 45f;
    private BoomStage _boomStage = BoomStage.NotStarted;
    private float _boomT, _boomPitch;
    private void UpdateBoomShot(float dt)
    {
        _aimPoint = TorsoAimPoint();
        _boomT += dt;
        if (_boomStage < BoomStage.Flats)
        {
            _boomStage = BoomStage.Flats;
            _boomT = 0f;
            _boomPitch = BoomShotPitchStart;
            StageFlatsTrio();
            _player.SetDrive(0f, 0f);
            _player.TorsoTwist = 0f;
            _hpPlayer = InvulnerableHp;
            Dbg.Print("BOOMSHOT staged: sweep on the flats (0,85), then the skirt (0,45)");
            return;
        }
        if (_pendingPath != null || _capturing) return;
        if (_boomStage <= BoomStage.Skirt)
        {
            if (_boomT < BoomShotGateSeconds) return;
            _boomT = 0f;
            _player.AimPitchWide = _boomPitch;
            _player.TorsoPitch = _boomPitch;
            UpdateCamera(0f);
            var vp = GetViewport().GetVisibleRect().Size;
            var head = _cam.UnprojectPosition(_player.RootPos + Vector3.Up * _playerSpec.Height);
            var feet = _cam.UnprojectPosition(_player.RootPos);
            bool headIn = head.X > 0f && head.X < vp.X && head.Y > 0f && head.Y < vp.Y && !_cam.IsPositionBehind(_player.RootPos + Vector3.Up * _playerSpec.Height);
            bool feetIn = feet.X > 0f && feet.X < vp.X && feet.Y > 0f && feet.Y < vp.Y && !_cam.IsPositionBehind(_player.RootPos);
            Dbg.Print($"BOOMSWEEP stand={(_boomStage == BoomStage.Flats ? "flats" : "skirt")} pitch={_boomPitch:F2} head=({head.X:F0},{head.Y:F0}) feet=({feet.X:F0},{feet.Y:F0}) headIn={headIn} feetIn={feetIn} camPos={_cam.GlobalPosition}");
            if (_boomStage == BoomStage.Flats)
            {
                string cap = Mathf.Abs(_boomPitch + BoomShotCapPitch) < BoomShotCapTolerance ? "down" : Mathf.Abs(_boomPitch) < BoomShotCapTolerance ? "level" : Mathf.Abs(_boomPitch - BoomShotCapPitch) < BoomShotCapTolerance ? "up" : null;
                if (cap != null) QueueCapture($"{ProofDir}boom_{cap}.png", $"{CaptureTags.Boom}{cap}", CamMode.Chase);
            }
            _boomPitch += BoomShotPitchStep;
            if (_boomPitch > BoomShotPitchEnd)
            {
                _boomStage++;
                _boomPitch = BoomShotPitchStart;
                if (_boomStage == BoomStage.Skirt)
                {
                    _player.Teleport(new Vector3(0f, Main.HeightAt(0f, BoomShotSkirtZ), BoomShotSkirtZ), 0f);
                    _player.SetDrive(0f, 0f);
                    _player.TorsoTwist = 0f;
                }
                else { Dbg.Print("BOOMSHOT DONE"); GetTree().Quit(); }
            }
        }
    }

    // --flipbook-test (Freelancer explosion prototype): stage the
    // player on the flats, fire one rocket-sized and one death-sized
    // flipbook fireball ahead of the chase camera, capture mid-boil
    // (t=0.30s) and late (t=0.75s, orange/red + smoke), then quit.
    private enum FlipStage { NotStarted = -1, StageFireballs, MidCapture, LateCapture }
    private const float FlipMidSeconds = 0.30f;
    private const float FlipLateSeconds = 0.75f;
    private FlipStage _flipStage = FlipStage.NotStarted;
    private float _flipT;
    private void UpdateFlipbookTest(float dt)
    {
        _aimPoint = TorsoAimPoint();
        _flipT += dt;
        if (_flipStage < FlipStage.StageFireballs)
        {
            _flipStage = FlipStage.StageFireballs;
            _flipT = 0f;
            StageFlatsTrio();
            _player.SetDrive(0f, 0f);
            _player.TorsoTwist = 0f;
            _player.AimPitchWide = 0f;
            _player.TorsoPitch = 0f;
            _hpPlayer = InvulnerableHp;
            float gy = Main.HeightAt(0f, 67f);
            SpawnFlipbookFireball(new Vector3(-4f, gy + 4f, 67f), 3.4f, 0.9f);
            SpawnFlipbookFireball(new Vector3(5f, gy + 5f, 62f), 10f, 1.2f);
            Dbg.Print("FLIPBOOK staged: rocket + death fireballs ahead of the player");
            return;
        }
        if (_pendingPath != null || _capturing) return;
        if (_flipStage == FlipStage.StageFireballs && _flipT >= FlipMidSeconds)
        {
            _flipStage = FlipStage.MidCapture;
            UpdateCamera(0f);
            QueueCapture(ProofDir + "flipbook_mid.png", CaptureTags.FlipMid, CamMode.Chase);
        }
        else if (_flipStage == FlipStage.MidCapture && _flipT >= FlipLateSeconds)
        {
            _flipStage = FlipStage.LateCapture;
            UpdateCamera(0f);
            QueueCapture(ProofDir + "flipbook_late.png", CaptureTags.FlipLate, CamMode.Chase, () => GetTree().Quit());
        }
    }

    // --boom-check (round 28 follow-up, owner repeated "the
    // player's rider should be always visible" after the boom
    // shipped): stage the live-play cases the static sweep did
    // not cover - the rider backed up against a building (does
    // the boom put the camera inside it?) at level and at a
    // mesa-aim pitch, and (photo scene) a level chase frame.
    // Round 29 probe (owner reports): (1) camera lag - one frame
    // after a hard mouse flick, how far is the chase camera from
    // its boom target, and how many frames until it settles?
    // (2) rocks - can the rider walk THROUGH a rock's center
    // (only tall objects should block movement)? (3) trees - a
    // rider staged just outside the old stomp trigger but inside
    // body reach: does the tree die before contact?
    private enum LagStage { NotStarted = -1, Setup, TreeRun, TreeVerdict }
    // The frame delta the lag check simulates with: one
    // 60 fps frame (owner, 2026-11-27: the expected frame
    // rate is a constant, not a literal at each use).
    private const float SimulatedFrameDelta = 1f / 60f;
    private const float LagFlickYaw = 1.2f;
    private const float LagFlickPitch = 0.8f;
    private const int LagSettleFrameCap = 240;
    private const float LagSettleTolerance = 0.05f;
    private const float LagTreeMaxCenterY = 12f;
    private const float LagTreeStageGap = 0.5f;
    private const float LagTreeVerdictSeconds = 0.5f;
    private LagStage _lagStage = LagStage.NotStarted;
    private float _lagT;
    private Destructible _lagTree;
    private void UpdateLagCheck(float dt)
    {
        _lagT += dt;
        if (_lagStage < LagStage.Setup)
        {
            _lagStage = LagStage.Setup;
            _lagT = 0f;
            StageFlatsTrio();
            _player.SetDrive(0f, 0f);
            _player.TorsoTwist = 0f;
            _player.TorsoPitch = 0f;
            _player.AimPitchWide = 0f;
            _playerYaw = 0f;
            _torsoWorldYaw = 0f;
            _hpPlayer = InvulnerableHp;
            UpdateCamera(0f); // settle exactly on the current boom target
            _torsoWorldYaw = LagFlickYaw;
            MaintainTorsoWorldYaw();
            _player.AimPitchWide = LagFlickPitch;
            _player.TorsoPitch = LagFlickPitch;
            UpdateCamera(SimulatedFrameDelta); // ONE frame after the flick
            var posA = _cam.GlobalPosition;
            UpdateCamera(0f);       // the exact target, by construction
            var posB = _cam.GlobalPosition;
            float lag0 = posA.DistanceTo(posB);
            _cam.GlobalPosition = posA;
            int settle = 0;
            while (settle < LagSettleFrameCap && _cam.GlobalPosition.DistanceTo(posB) > LagSettleTolerance)
            {
                UpdateCamera(SimulatedFrameDelta);
                settle++;
            }
            Dbg.Print($"LAGCHECK cameraLag={lag0:F2}u one frame after a 1.2rad yaw + 0.8rad pitch flick; settleFrames={settle} to within 0.05u (0 = camera exact every frame)");
            // The rock stage is gone with the rocks (owner,
            // 2026-11-20) and the prop circles with the
            // buildings (2026-11-27): the tree stomp verdict
            // stages directly from Setup now.
            _lagStage = LagStage.TreeRun;
            _lagT = 0f;
            _lagTree = null;
            foreach (var d in _destructibles)
                if (d.Kind == DestrKind.Tree && d.State == DestrState.Intact && d.Center.Y < LagTreeMaxCenterY) { _lagTree = d; break; }
            if (_lagTree != null)
            {
                float off = _player.CollisionRadius + _lagTree.Radius + LagTreeStageGap;
                var sp = new Vector3(_lagTree.Center.X + off, 0f, _lagTree.Center.Z);
                sp.Y = Main.HeightAt(sp.X, sp.Z);
                _player.Teleport(sp, 0f);
                _player.SetDrive(0f, 0f);
                Dbg.Print($"LAGCHECK tree at ({_lagTree.Center.X:F0},{_lagTree.Center.Z:F0}) trunkR={_lagTree.Radius:F2} stagedDist={off:F2} oldTrigger={_player.CollisionRadius + 0.35f:F2} bodyReach={_player.CollisionRadius + _lagTree.Radius:F2}");
            }
            else Dbg.Print("LAGCHECK no tree candidate found");
            return;
        }
        if (_lagStage == LagStage.TreeRun && _lagT > LagTreeVerdictSeconds)
        {
            _lagStage = LagStage.TreeVerdict;
            Dbg.Print(_lagTree != null
                ? $"LAGCHECK TREE state={_lagTree.State} {(_lagTree.State != DestrState.Intact ? "STOMPED at the staged distance" : "still standing")}"
                : "LAGCHECK TREE skipped");
            Dbg.Print("LAGCHECK DONE");
            GetTree().Quit();
        }
    }

    private void UpdateViewShot(float dt)
    {
        _aimPoint = TorsoAimPoint(); // live play refreshes the aim every frame; the staging must too
        _viewT += dt;
        if (_viewStage < ViewStage.Stage)
        {
            _viewStage = ViewStage.Stage;
            _viewT = 0f;
            _player.Teleport(new Vector3(0f, Main.HeightAt(0f, 58f), 58f), 0f);
            _enemy.Teleport(new Vector3(0f, Main.HeightAt(0f, 10f), 10f), 0f);
            _player.SetDrive(0f, 0f);
            _player.TorsoTwist = 0f;
            _player.TorsoPitch = 0f;
            _player.AimPitchWide = 0f; // looking level: the crosshair rests on the flats
            _hpPlayer = InvulnerableHp;
            Dbg.Print("VIEWSHOT staged: player at the mesa foot, foe on the top, view level");
            return;
        }
        if (_pendingPath != null || _capturing) return;
        var chest = _enemy.RootPos + Vector3.Up * (_enemySpec.Height * ViewChestHeightFactor);
        var vp = GetViewport().GetVisibleRect().Size;
        var ctr = vp * 0.5f;
        if (_viewStage == ViewStage.Stage && _viewT > ViewStageSeconds)
        {
            _viewStage = ViewStage.BeforeCapture;
            var scr = _cam.UnprojectPosition(chest);
            Dbg.Print($"VIEWSHOT BEFORE foe chest screen=({scr.X:F0},{scr.Y:F0}) center=({ctr.X:F0},{ctr.Y:F0})");
            QueueCapture(ProofDir + "view_hilltop_before.png", CaptureTags.VBefore, CamMode.Chase, () =>
            {
                _viewPitch = ViewPitchStart;
                _viewBest = ViewBestUnmeasured;
                _viewBestPitch = 0f;
                _viewStage = ViewStage.Sweep;
                _viewT = 0f;
            });
        }
        else if (_viewStage == ViewStage.Sweep)
        {
            // The mouse sweeps its full travel: the foe's chest
            // must cross the screen center (the crosshair) at some
            // pitch - the round-27 acceptance for the owner's
            // hilltop case.
            _player.AimPitchWide = _viewPitch;
            _player.TorsoPitch = _viewPitch;
            if (!_cam.IsPositionBehind(chest))
            {
                float d = (_cam.UnprojectPosition(chest) - ctr).Length();
                if (d < _viewBest) { _viewBest = d; _viewBestPitch = _viewPitch; }
            }
            _viewPitch += ViewPitchStep;
            if (_viewPitch > ViewPitchEnd)
            {
                _viewStage = ViewStage.HoldBest;
                _viewT = 0f;
                _player.AimPitchWide = _viewBestPitch;
                _player.TorsoPitch = _viewBestPitch;
                Dbg.Print($"VIEWSHOT SWEEP bestCenterMiss={_viewBest:F0}px at pitch {_viewBestPitch:F2}");
                Dbg.Print(_viewBest <= ViewReachableTolerance
                    ? "VIEWSHOT PASS the hilltop foe crosses the center crosshair"
                    : "VIEWSHOT FAIL the hilltop foe never reaches the crosshair");
            }
        }
        else if (_viewStage == ViewStage.HoldBest && _viewT > ViewAfterSeconds)
        {
            _viewStage = ViewStage.CaptureAfter;
            QueueCapture(ProofDir + "view_hilltop.png", CaptureTags.VAfter, CamMode.Chase, () => GetTree().Quit());
        }
    }

    // --brake-test: the owner's no-inertia rule (2026-10-31) -
    // from a full RUN (12.6 u/s) with the throttle released, EVERY
    // chassis must be at a standstill within one second. The
    // subjects are DATA (owner, 2026-11-27: no rider names in
    // the stage machinery): each subject in turn is swapped
    // into the player seat and measured.
    private enum BrakeStage { NotStarted = -1, Accelerate, Stopping }
    // Brake subjects in order: a light chassis, then the heavy
    // one whose spec Accel used to glide 5+ seconds.
    private static readonly RiderId[] BrakeSubjects = { RiderId.Enyo, RiderId.Hector };
    private int _brakeSubject;
    private const float ParkedEnemyZ = -100f; // where idle fixtures park the enemy, off every lane
    private const float BrakeRunSpeed = 12.6f;
    private const float BrakeReleaseSpeed = 12.5f;
    private const float BrakeStoppedSpeed = 0.02f;
    private const float BrakeTimeoutSeconds = 3f;
    private const float BrakePassSeconds = 1.05f;
    private const float BrakeStageX = 22f;
    private const float BrakeStageZ = 24f;
    private BrakeStage _brakeStage = BrakeStage.NotStarted;
    private float _brakeT;
    private void UpdateBrakeTest(float dt)
    {
        if (_brakeStage < BrakeStage.Accelerate)
        {
            _brakeStage = BrakeStage.Accelerate;
            _enemy.Teleport(new Vector3(0f, Main.HeightAt(0f, ParkedEnemyZ), ParkedEnemyZ), 0f);
            _player.Teleport(new Vector3(BrakeStageX, Main.HeightAt(BrakeStageX, BrakeStageZ), BrakeStageZ), Mathf.Pi);
            _player.SetDrive(Mathf.Pi, BrakeRunSpeed);
            Dbg.Print($"BRAKE staged: {_playerSpec.Name} accelerating to run speed");
            return;
        }
        if (_brakeStage == BrakeStage.Accelerate)
        {
            if (_player.Speed >= BrakeReleaseSpeed)
            {
                _brakeT = 0f;
                _player.SetDrive(_player.LegsYaw, 0f);
                _brakeStage++;
                Dbg.Print($"BRAKE released at speed {_player.Speed:F2} - timing the stop");
            }
            return;
        }
        _brakeT += dt;
        if (_player.Speed <= BrakeStoppedSpeed || _brakeT > BrakeTimeoutSeconds)
        {
            string who = _playerSpec.Name;
            bool pass = _player.Speed <= BrakeStoppedSpeed && _brakeT <= BrakePassSeconds;
            Dbg.Print($"BRAKE {who} stopTime={_brakeT:F2}s fromSpeed={BrakeRunSpeed} finalSpeed={_player.Speed:F2}");
            Dbg.Print(pass ? $"BRAKE {who} PASS stopped within one second" : $"BRAKE {who} FAIL stop took too long");
            int next = _brakeSubject + 1;
            if (next < BrakeSubjects.Length)
            {
                SetRiderSelection(BrakeSubjects[next], _playerSpec.Id);
                ApplyLoadout();
                _player.Teleport(new Vector3(BrakeStageX, Main.HeightAt(BrakeStageX, BrakeStageZ), BrakeStageZ), Mathf.Pi);
                _player.SetDrive(Mathf.Pi, BrakeRunSpeed);
                _brakeSubject = next;
                _brakeStage = BrakeStage.Accelerate;
                Dbg.Print($"BRAKE staged: {_playerSpec.Name} accelerating to run speed");
            }
            else GetTree().Quit();
        }
    }

    private void UpdateFfaTest(float dt)
    {
        _ffaT += dt;
        if (!_ffaStaged)
        {
            _ffaStaged = true;
            var c = new Vector3(0f, 0f, 0f);
            _player.Teleport(new Vector3(0f, Main.HeightAt(0f, FfaPlayerZ), FfaPlayerZ),
                RiderWalker.YawOf((c - _player.RootPos).Normalized()));
            _enemy.Teleport(new Vector3(FfaEnemyX, Main.HeightAt(FfaEnemyX, FfaEnemyZ), FfaEnemyZ),
                RiderWalker.YawOf((c - _enemy.RootPos).Normalized()));
            _foe.Walker.Teleport(new Vector3(FfaFoeX, Main.HeightAt(FfaFoeX, FfaEnemyZ), FfaEnemyZ),
                RiderWalker.YawOf((c - _foe.Walker.RootPos).Normalized()));
            _ffaHome0 = _enemy.RootPos;
            _ffaHome1 = _foe.Walker.RootPos;
            Dbg.Print("FFATEST staged: three riders, 97u triangle around the mesa");
            return;
        }
        UpdateEnemyAI(dt);
        UpdateFoeAI(dt);
        if (_ffaT > FfaTimeoutSeconds)
        {
            float moved0 = new Vector2(_enemy.RootPos.X - _ffaHome0.X, _enemy.RootPos.Z - _ffaHome0.Z).Length();
            float moved1 = new Vector2(_foe.Walker.RootPos.X - _ffaHome1.X, _foe.Walker.RootPos.Z - _ffaHome1.Z).Length();
            bool dmg = _hpPlayer < _playerSpec.MaxHp || _hpEnemy < _enemySpec.MaxHp || _foe.Hp < _foe.Spec.MaxHp
                || AnyPartDamaged(_partsPlayer) || AnyPartDamaged(_partsEnemy) || AnyPartDamaged(_foe.Parts);
            Dbg.Print($"FFATEST moved foe0={moved0:F1} foe={moved1:F1} shots foe0={_enemyShots} foe={_foeShots} anyDamage={dmg} hp P={_hpPlayer:F0} E={_hpEnemy:F0} F1={_foe.Hp:F0}");
            if (moved0 > FfaMoveMin && moved1 > FfaMoveMin && _enemyShots > 0 && _foeShots > 0 && dmg)
            {
                Dbg.Print("FFATEST PASS both opponents closed, fired, and drew blood in the free-for-all");
                GetTree().Quit(0);
            }
            else
            {
                Dbg.Err("FFATEST FAIL: the free-for-all did not develop as required");
                GetTree().Quit(1);
            }
        }
    }

    // ---------- team test (--team-test) ----------
    // The Story team under fire: three roster riders staged in
    // engagement range of defender 1's base. Asserts, in order:
    // the allies close and fire under the default order while
    // the defender engages the team; FULL STOP freezes movement
    // and fire; killing the leader passes command to the first
    // living ally; killing both defenders completes the mission
    // on the team rule.
    private float _teamT;
    private bool _teamStaged;
    private enum TeamPhase { Forming = -1, Fight, Hold, PassSetup, PassCheck, End }
    private const float TeamKillDamage = 99999f;
    private const float TeamMinSepUnmeasured = 9999f;
    private const float TeamAllyAhead0 = 30f;
    private const float TeamAllyAhead1 = 36f;
    private const float TeamAllySide1 = 12f;
    private const float TeamFormSeconds = 24f;
    private const float TeamFormDriftMax = 0.5f;
    private const float TeamFormReachMax = 30f;
    private const float TeamSepSlack = 0.75f;
    private const float TeamFightPlayerOffset = 150f;
    private const float TeamFightAllyOffset = 138f;
    private const float TeamFightAllySide = 14f;
    private const float TeamFightSeconds = 26f;
    private const float TeamFightMoveMin = 15f;
    private const float TeamHoldSeconds = 31f;
    private const float TeamHoldDriftMax = 2.5f;
    private const float TeamEndSeconds = 35f;
    private TeamPhase _teamPhase;
    private Vector3 _teamAllyHome0, _teamAllyHome1, _teamHoldPos;
    private Vector3 _teamPlayerHome;
    private float _teamMinSep;
    private int _teamHoldShots, _teamWantSlot;
    private RiderId _teamWantSpec;

    private static float FlatDist(Vector3 a, Vector3 b) =>
        new Vector2(a.X - b.X, a.Z - b.Z).Length();

    // A fabricated torso hit for kill staging: the bracket
    // and team suites put riders down through the REAL
    // damage path (ApplyDamage, with its death handling,
    // debris and HUD fallout) without firing a weapon, so
    // they need a HitModel.Result from nowhere. The point
    // sits at the chest anchor - the same fraction of the
    // walker's height the LRM lock tracks.
    private static HitModel.Result DummyHit(RiderWalker w, string role) => new()
    {
        Hit = true, Target = role, Zone = HitZone.Torso,
        Point = w.RootPos + Vector3.Up * (w.Height * LockTrackHeightFraction)
    };

    private void TeamFail(string why)
    {
        Dbg.Err($"TEAMTEST FAIL: {why}");
        GetTree().Quit(1);
    }

    // ---------- Deploy rebuild probe (owner's bug, 2026-11-14) ----------

    private const float DeployTimeoutSeconds = 60f;
    private float _deployT;
    private bool _deployStaged;

    private void DeployFail(string why)
    {
        Dbg.Err($"DEPLOYTEST FAIL: {why}");
        GetTree().Quit(1);
    }

    // The reported bug, staged exactly: the scene loads with
    // a one-rider roster (no NPC records), members are added
    // through the lab's own Add path, and the DEPLOY rebuild
    // must field them.
    private void UpdateDeployTest(float dt)
    {
        _deployT += dt;
        if (_deployT > DeployTimeoutSeconds) { DeployFail("timeout"); return; }
        if (!_deployStaged)
        {
            _deployStaged = true;
            if (_allies.Count != 0) { DeployFail($"expected a solo deploy start, got {_allies.Count} allies"); return; }
            AddStoryMember();
            AddStoryMember();
            if (TeamRoster.Story.Count != 3) { DeployFail($"lab Add path should give 3 roster members, got {TeamRoster.Story.Count}"); return; }
            RebuildAlliesForDeploy();
            if (_allies.Count != 2) { DeployFail($"deploy rebuild fielded {_allies.Count} allies, expected 2"); return; }
            foreach (var a in _allies)
            {
                if (a.Walker == null || !IsInstanceValid(a.Walker)) { DeployFail("rebuilt ally has no live walker"); return; }
                bool capped = false;
                foreach (var cap in _hit.Riders) if (cap.Id == a.Role) capped = true;
                if (!capped) { DeployFail($"rebuilt ally {a.Role} missing from the hit caps"); return; }
            }
            Dbg.Print($"DEPLOYTEST REBUILD PASS lab-added members take the field: {_allies[0].Spec.Id} + {_allies[1].Spec.Id}");
            // A second deploy with an unchanged roster is a
            // no-op: the same walkers stay.
            var w0 = _allies[0].Walker;
            RebuildAlliesForDeploy();
            if (_allies.Count != 2 || _allies[0].Walker != w0) { DeployFail("unchanged roster rebuilt anyway"); return; }
            Dbg.Print("DEPLOYTEST NOOP PASS an unchanged roster deploys as-is");
            Dbg.Print("DEPLOYTEST DONE");
            GetTree().Quit(0);
        }
    }

    // ---------- Tournament bracket probe (Stage D3) ----------

    private float _bracketT;
    private enum BracketPhase { Fight, End }
    private const float BracketTimeoutSeconds = 150f;
    private const float BracketAnchorTolerance = 20f;
    private const float BracketFightSeconds = 8f;
    private const float BracketNpcOffsetX = 30f;
    private const float BracketMoveMin = 2f;
    private const float BracketKillDamage = 99999f;
    private BracketPhase _bracketPhase;
    private bool _bracketStaged;
    private Vector3 _bracketNpcHome;
    private Ally _bracketNpc;   // Medea, team 3 (index 2)
    private Ally _bracketFriend; // Demeter, team 1 (index 0)

    private void BracketFail(string why)
    {
        Dbg.Err($"BRACKETTEST FAIL: {why}");
        GetTree().Quit(1);
    }

    // The seeded bracket: team 1 = Enyo (driven) + Demeter,
    // team 2 = Hector (enemy seat) + Prometheus (foe seat),
    // team 3 = Medea (hostile NPC record).
    private void SeedBracketTest()
    {
        TeamRoster.Tourney.Clear();
        TeamRoster.Tourney.Add(new List<TeamMember> { TeamRoster.Make(RiderSpecs.Enyo), TeamRoster.Make(RiderSpecs.Demeter) });
        TeamRoster.Tourney.Add(new List<TeamMember> { TeamRoster.Make(RiderSpecs.Hector), TeamRoster.Make(RiderSpecs.Prometheus) });
        TeamRoster.Tourney.Add(new List<TeamMember> { TeamRoster.Make(RiderSpecs.Medea) });
        TeamRoster.TourneyTeam = 0;
    }

    private void UpdateBracketTest(float dt)
    {
        _bracketT += dt;
        if (_bracketT > BracketTimeoutSeconds) { BracketFail("timeout"); return; }
        if (!_bracketStaged)
        {
            _bracketStaged = true;
            if (!_bracketActive) { BracketFail("bracket not active"); return; }
            if (_playerSpec.Id != RiderId.Enyo || _enemySpec.Id != RiderId.Hector)
            { BracketFail($"seats wrong: player={_playerSpec.Id} enemy={_enemySpec.Id}"); return; }
            if (_foe == null || _foe.Spec.Id != RiderId.Prometheus)
            { BracketFail($"foe seat should host Prometheus, got {_foe?.Spec.Id.ToString() ?? "none"}"); return; }
            if (_allies.Count != 2)
            { BracketFail($"expected 2 NPC records, got {_allies.Count}"); return; }
            foreach (var a in _allies)
            {
                if (a.Spec.Id == RiderId.Demeter) _bracketFriend = a;
                if (a.Spec.Id == RiderId.Medea) _bracketNpc = a;
            }
            if (_bracketFriend == null || _bracketFriend.Team != 0)
            { BracketFail("Demeter should be a team-1 NPC record"); return; }
            if (_bracketNpc == null || _bracketNpc.Team != 2)
            { BracketFail("Medea should be a team-3 NPC record"); return; }
            // Muster check: every bracket rider stands at its
            // own team's anchor, not on a fixed pad - the foe
            // seat (Prometheus, team 2) included.
            if (FlatDist(_foe.Walker.RootPos, TeamAnchor(1)) > BracketAnchorTolerance)
            { BracketFail($"foe seat musters off its team anchor: {FlatDist(_foe.Walker.RootPos, TeamAnchor(1)):F1}u"); return; }
            if (FlatDist(_bracketNpc.Walker.RootPos, TeamAnchor(2)) > BracketAnchorTolerance)
            { BracketFail("Medea musters off the team-3 anchor"); return; }
            if (FlatDist(_bracketFriend.Walker.RootPos, TeamAnchor(0)) > BracketAnchorTolerance)
            { BracketFail("Demeter musters off the team-1 anchor"); return; }
            Dbg.Print("BRACKETTEST STAGE PASS seats host members; NPC records carry teams; musters are per-team");
            // Stage the fight: Medea 30u from the driven rider.
            var s = PadPos(Main.SpawnPads[0]);
            var mp = s + new Vector3(BracketNpcOffsetX, 0f, 0f);
            mp.Y = Main.HeightAt(mp.X, mp.Z);
            _bracketNpc.Walker.Teleport(mp, -Mathf.Pi / 2f);
            _bracketNpc.Yaw = -Mathf.Pi / 2f;
            _bracketNpc.PrevRoot = _bracketNpc.Walker.RootPos;
            _bracketNpcHome = mp;
            return;
        }
        UpdateEnemyAI(dt);
        if (_foe != null) UpdateFoeAI(dt);
        UpdateAlliesAI(dt);
        UpdateBracketAI(dt);
        _player.SetDrive(_playerYaw, 0f);

        if (_bracketPhase == BracketPhase.Fight)
        {
            // The team filter, checked live: Prometheus's
            // nearest rider is his own teammate Hector (~12u);
            // he must never pick him.
            if (_foeTargetW == _enemy) { BracketFail("foe seat targeted its own teammate"); return; }
            if (_bracketT > BracketFightSeconds)
            {
                float moved = FlatDist(_bracketNpc.Walker.RootPos, _bracketNpcHome);
                Dbg.Print($"BRACKETTEST fight: npcMoved={moved:F1} bracketShots={_bracketShots} npcTarget={( _bracketNpc.TargetW == null ? "none" : TeamOf(_bracketNpc.TargetW).ToString())} playerHp={_hpPlayer:F0}");
                if (_bracketNpc.TargetW == null) { BracketFail("hostile NPC never picked a target"); return; }
                if (TeamOf(_bracketNpc.TargetW) == 2) { BracketFail("hostile NPC targeted its own team"); return; }
                if (moved <= BracketMoveMin && _bracketShots <= 0) { BracketFail("hostile NPC neither moved nor fired"); return; }
                Dbg.Print("BRACKETTEST FIGHT PASS hostile NPC engages another team; the foe seat never targets its teammate");
                _bracketPhase = BracketPhase.End;
            }
        }
        else if (_bracketPhase == BracketPhase.End)
        {
            // Last team standing: kill team 2's seats - teams
            // 1 and 3 remain, so no decision yet. Then kill
            // Medea: team 1 alone remains and the arena is
            // announced decided.
            if (!_enemyDead) ApplyDamage(_enemy, BracketKillDamage, DummyHit(_enemy, Roles.Enemy), Roles.Player);
            if (_foe != null && !_foe.Dead) ApplyDamage(_foe.Walker, BracketKillDamage, DummyHit(_foe.Walker, Roles.Foe), Roles.Player);
            if (_bracketDecided) { BracketFail("arena decided with two teams still standing"); return; }
            ApplyDamage(_bracketNpc.Walker, BracketKillDamage, DummyHit(_bracketNpc.Walker, _bracketNpc.Role), Roles.Player);
            if (!_bracketDecided)
            { BracketFail($"arena not decided when the second-to-last team fell (decided={_bracketDecided})"); return; }
            Dbg.Print("BRACKETTEST END PASS last team standing decided exactly when the second-to-last team fell");
            Dbg.Print("BRACKETTEST DONE");
            GetTree().Quit(0);
        }
    }

    private void UpdateTeamTest(float dt)
    {
        _teamT += dt;
        if (!_teamStaged)
        {
            _teamStaged = true;
            if (_allies.Count != 2) { TeamFail($"expected 2 allies from the seeded roster, got {_allies.Count}"); return; }
            Dbg.Print($"TEAMTEST staged: leader slot {_leaderSlot} {_playerSpec.Id}, allies {_allies[0].Spec.Id} + {_allies[1].Spec.Id}");
            // FORM phase first (owner, 2026-11-15): the
            // leader stands still at the home pad, both
            // followers start AHEAD of him - ally 0 dead ahead,
            // its station dead astern, the hardest case - and
            // must form up behind him without ever displacing
            // him or closing inside the universal separation.
            var hBase = PadPos(Main.SpawnPads[0]);
            var eBase = PadPos(Main.SpawnPads[1]);
            var dir = eBase - hBase; dir.Y = 0; dir = dir.Normalized();
            float face = RiderWalker.YawOf(dir);
            var pPos = hBase; pPos.Y = Main.HeightAt(pPos.X, pPos.Z);
            _player.Teleport(pPos, face);
            _playerYaw = face;
            _teamPlayerHome = _player.RootPos;
            _teamMinSep = TeamMinSepUnmeasured;
            var rgt = new Vector3(-dir.Z, 0, dir.X);
            var ap0 = pPos + dir * TeamAllyAhead0; ap0.Y = Main.HeightAt(ap0.X, ap0.Z);
            _allies[0].Walker.Teleport(ap0, face + Mathf.Pi);
            _allies[0].Yaw = face + Mathf.Pi;
            _allies[0].PrevRoot = _allies[0].Walker.RootPos;
            var ap1 = pPos + dir * TeamAllyAhead1 + rgt * TeamAllySide1; ap1.Y = Main.HeightAt(ap1.X, ap1.Z);
            _allies[1].Walker.Teleport(ap1, face + Mathf.Pi);
            _allies[1].Yaw = face + Mathf.Pi;
            _allies[1].PrevRoot = _allies[1].Walker.RootPos;
            _teamPhase = TeamPhase.Forming;
            return;
        }
        PassCommandIfNeeded();
        UpdateEnemyAI(dt);
        if (_foe != null) UpdateFoeAI(dt);
        UpdateAlliesAI(dt);
        _player.SetDrive(_playerYaw, 0f);

        if (_teamPhase == TeamPhase.Forming)
        {
            foreach (var a in _allies)
                _teamMinSep = Mathf.Min(_teamMinSep, FlatDist(a.Walker.RootPos, _player.RootPos));
            if (_teamT > TeamFormSeconds)
            {
                float drift = FlatDist(_player.RootPos, _teamPlayerHome);
                float need = _player.CollisionRadius + RiderBody.Gap - TeamSepSlack
                    + Mathf.Min(_allies[0].Walker.CollisionRadius, _allies[1].Walker.CollisionRadius);
                float d0 = FlatDist(_allies[0].Walker.RootPos, _player.RootPos);
                float d1 = FlatDist(_allies[1].Walker.RootPos, _player.RootPos);
                Dbg.Print($"TEAMTEST form: drift={drift:F2} minSep={_teamMinSep:F2} (need >= {need:F2}) allyDist={d0:F1}/{d1:F1}");
                if (drift > TeamFormDriftMax) { TeamFail("a follower displaced the stationary leader"); return; }
                if (_teamMinSep < need) { TeamFail("a follower closed inside the universal separation"); return; }
                if (d0 > TeamFormReachMax || d1 > TeamFormReachMax) { TeamFail("followers did not reach the formation"); return; }
                Dbg.Print("TEAMTEST FORM PASS followers formed up without touching the leader");
                // Re-stage the fight (the original staging).
                var eBase = PadPos(Main.SpawnPads[1]);
                var hBase = PadPos(Main.SpawnPads[0]);
                var dir = hBase - eBase; dir.Y = 0; dir = dir.Normalized();
                var pPos = eBase + dir * TeamFightPlayerOffset; pPos.Y = Main.HeightAt(pPos.X, pPos.Z);
                float face = RiderWalker.YawOf(-dir);
                _player.Teleport(pPos, face);
                _playerYaw = face;
                foreach (var a in _allies)
                {
                    var ap = eBase + dir * TeamFightAllyOffset + new Vector3(-dir.Z, 0, dir.X) * (a.Slot == 1 ? -TeamFightAllySide : TeamFightAllySide);
                    ap.Y = Main.HeightAt(ap.X, ap.Z);
                    a.Walker.Teleport(ap, face);
                    a.Yaw = face;
                    a.PrevRoot = a.Walker.RootPos;
                }
                _teamAllyHome0 = _allies[0].Walker.RootPos;
                _teamAllyHome1 = _allies[1].Walker.RootPos;
                _teamPhase = TeamPhase.Fight;
                _teamT = 0f;
            }
            return;
        }

        if (_teamPhase == TeamPhase.Fight && _teamT > TeamFightSeconds)
        {
            float m0 = FlatDist(_allies[0].Walker.RootPos, _teamAllyHome0);
            float m1 = FlatDist(_allies[1].Walker.RootPos, _teamAllyHome1);
            Dbg.Print($"TEAMTEST fight: allyMoved={m0:F1}/{m1:F1} allyShots={_allyShots} enemyShots={_enemyShots} engaged={_foe0Engaged} hpE={_hpEnemy:F0} hpF={(_foe?.Hp ?? -1):F0} teamHp={_hpPlayer:F0}/{_allies[0].Hp:F0}/{_allies[1].Hp:F0}");
            if (m0 + m1 <= TeamFightMoveMin) { TeamFail("allies did not move to engage"); return; }
            if (_allyShots <= 0) { TeamFail("allies never fired"); return; }
            if (!_foe0Engaged) { TeamFail("defender never engaged the team"); return; }
            Dbg.Print("TEAMTEST FIGHT PASS allies closed and fired; the defender engaged the team");
            QueueCapture("/tmp/team_fight.png", CaptureTags.TeamFight, CamMode.Chase);
            SetTeamOrder(TeamOrder.Hold);
            _teamHoldPos = _allies[0].Walker.RootPos + _allies[1].Walker.RootPos;
            _teamHoldShots = _allyShots;
            _teamPhase = TeamPhase.Hold;
        }
        else if (_teamPhase == TeamPhase.Hold && _teamT > TeamHoldSeconds)
        {
            var now = _allies[0].Walker.RootPos + _allies[1].Walker.RootPos;
            float drift = (now - _teamHoldPos).Length();
            Dbg.Print($"TEAMTEST hold: drift={drift:F2} shots {_teamHoldShots}->{_allyShots}");
            if (drift > TeamHoldDriftMax) { TeamFail("FULL STOP did not freeze the team"); return; }
            if (_allyShots != _teamHoldShots) { TeamFail("allies fired under FULL STOP"); return; }
            Dbg.Print("TEAMTEST HOLD PASS full stop froze movement and fire");
            _teamPhase = TeamPhase.PassSetup;
        }
        else if (_teamPhase == TeamPhase.PassSetup)
        {
            Ally want = null;
            foreach (var a in _allies) if (!a.Dead && (want == null || a.Slot < want.Slot)) want = a;
            if (want == null) { TeamFail("no living ally to take command"); return; }
            _teamWantSlot = want.Slot;
            _teamWantSpec = want.Spec.Id;
            ApplyDamage(_player, TeamKillDamage, DummyHit(_player, Roles.Player), Roles.Enemy);
            if (!_playerDead) { TeamFail("leader death did not register"); return; }
            _teamPhase = TeamPhase.PassCheck; // the pass lands at the top of the next frame
        }
        else if (_teamPhase == TeamPhase.PassCheck)
        {
            if (_leaderSlot != _teamWantSlot || _playerSpec.Id != _teamWantSpec)
            { TeamFail($"command pass wrong: leaderSlot={_leaderSlot} spec={_playerSpec.Id}, wanted {_teamWantSlot}/{_teamWantSpec}"); return; }
            if (_playerDead) { TeamFail("the new leader is still flagged dead"); return; }
            Dbg.Print($"TEAMTEST PASS PASS command passed to slot {_leaderSlot} ({_playerSpec.Id})");
            _teamDialog?.Open(TeamRows());
            QueueCapture("/tmp/team_screen.png", CaptureTags.TeamScreen, CamMode.Chase);
            _teamPhase = TeamPhase.End;
        }
        else if (_teamPhase == TeamPhase.End && _teamT > TeamEndSeconds)
        {
            if (!_enemyDead) ApplyDamage(_enemy, TeamKillDamage, DummyHit(_enemy, Roles.Enemy), Roles.Player);
            if (_foe != null && !_foe.Dead) ApplyDamage(_foe.Walker, TeamKillDamage, DummyHit(_foe.Walker, Roles.Foe), Roles.Player);
            if (!_missionDone) { TeamFail("mission did not complete with both defenders down and the team standing"); return; }
            Dbg.Print("TEAMTEST END PASS mission completed on the team rule");
            Dbg.Print("TEAMTEST DONE");
            GetTree().Quit(0);
        }
    }

    private const float PartDamageTolerance = 0.01f;

    private static bool AnyPartDamaged(RiderParts parts)
    {
        foreach (var id in new[] { PartId.Head, PartId.Torso, PartId.ArmL, PartId.ArmR, PartId.Gun, PartId.PodL, PartId.PodR, PartId.LegL, PartId.LegR })
            if (parts.Has(id) && parts.HpOf(id) < parts.GetPart(id).MaxHp - PartDamageTolerance) return true;
        return false;
    }

    // The firing suites fire ONE weapon per group: their exact
    // expectations (3x12 laser, 2x26 cannon, 8-rocket salvo, 4
    // rockets from one pod) were calibrated on single fits, and
    // the filled default loadouts (owner, 2026-10-31) would move
    // every number without testing anything more. The weapons and
    // break suites therefore start from this range fit; the duel
    // suite and the live game use the real defaults.
    // Weapons-range fixture (2026-11-12): the firing
    // suites (weapons + breakable) calibrate weapon
    // BEHAVIOUR - damage, routing, reactions, salvo counts -
    // on single fits: one energy arm, one ballistic arm, two
    // missile pods. Since the owner's mirrored-symmetry
    // ruling, no shipped rider has that layout (Enyo, the
    // suites' original body, now mirrors energy on both
    // arms), so the suites fit their range loadout on this
    // harness-only spec: Enyo's body numbers with the legacy
    // slot layout. It is not in RiderSpecs.All and never
    // reaches the game; loadout legality on the shipped
    // riders is asserted by LABTEST and the duel suite on
    // the real specs and real defaults.
    private static readonly RiderSpec RangeFixtureSpec = new()
    {
        Id = RiderId.Enyo, Name = "RANGE FIXTURE", Tonnage = 75f, JumpCapable = true,
        ModelPath = Paths.EnyoModel, MaxHp = 260f,
        Height = 13f, Radius = 2.0f, StepTime = 0.46f, StepThreshold = 1.7f, StepLift = 0.9f, Accel = 4.2f, BaseSpeed = 4.8f,
        FlipKneePole = false,
        Slots = new[]
        {
            new SlotDef { Id = PartId.ArmL, Display = "LEFT ARM", Type = WeaponSlotType.Energy, MaxWeight = 8f },
            new SlotDef { Id = PartId.ArmR, Display = "RIGHT ARM", Type = WeaponSlotType.Ballistic, MaxWeight = 25f },
            new SlotDef { Id = PartId.PodL, Display = "LEFT POD", Type = WeaponSlotType.Missile, MaxWeight = 21f },
            new SlotDef { Id = PartId.PodR, Display = "RIGHT POD", Type = WeaponSlotType.Missile, MaxWeight = 21f },
        }
    };

    private void ApplyRangeFit()
    {
        _playerSpec = RangeFixtureSpec;
        _loadout = new LoadoutState { JumpJets = true };
        _loadout.TryAssign(_playerSpec, PartId.ArmL, WeaponCatalog.LargeLaser);
        _loadout.TryAssign(_playerSpec, PartId.ArmR, WeaponCatalog.Autocannon);
        _loadout.TryAssign(_playerSpec, PartId.PodL, WeaponCatalog.Lrm);
        _loadout.TryAssign(_playerSpec, PartId.PodR, WeaponCatalog.Lrm);
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
        if (_wPhaseT > WPhaseTimeoutSeconds) { WFail($"phase {(int)_wPhase} timeout"); return; }

        if (_wPhase == WeaponsPhase.Autocannon && _enemyDmgEvents > 0)
            _wJoltSeen = Mathf.Max(_wJoltSeen, _enemy.JoltEnv);
        if (_wPhase == WeaponsPhase.Lrm && _enemyDmgEvents > 0 && !_wHitCaptured)
        {
            // Capture a rocket impact at burst peak (round 9: effects
            // are proven by the frame, not only by counters).
            _wHitCaptured = true;
            QueueCapture(ProofDir + "wpn_rocket_hit.png", CaptureTags.WpnHit, CamMode.Chase);
        }
        switch (_wPhase)
        {
            case WeaponsPhase.Settle: // settle at the open lane: TW (0,18) vs Hector (0,-12), 30u
                _player.SetDrive(0f, 0f);
                _enemy.SetDrive(Mathf.Pi, 0f);
                if (_wPhaseT > WSettleSeconds) SetWPhase(WeaponsPhase.Laser);
                break;

            case WeaponsPhase.Laser: // laser x3 at a stationary Hector: 3 x 12 = 36 exactly
                if (_wShotsFired == 0 && _wPhaseT > WFirstShotSeconds)
                {
                    FireLaser(EnemyAim());
                    _wShotsFired++;
                    QueueCapture(ProofDir + "weapons_laser.png", CaptureTags.WLaser, CamMode.WpnSide);
                }
                else if (_wShotsFired == 1 && _wPhaseT > WLaserSecondShotSeconds) { FireLaser(EnemyAim()); _wShotsFired++; }
                else if (_wShotsFired == 2 && _wPhaseT > WLaserThirdShotSeconds) { FireLaser(EnemyAim()); _wShotsFired++; }
                else if (_wShotsFired == 3 && _wPhaseT > WLaserVerdictSeconds)
                {
                    if (_enemyDmgEvents != 3 || Mathf.Abs(_enemyDmgTotal - WLaserExpectedDamage) > WTolerance)
                        WFail($"laser events={_enemyDmgEvents} dmg={_enemyDmgTotal:F1} (want 3 / 36)");
                    if (Mathf.Abs(_hpEnemy - WLaserHpExpected) > WTolerance) WFail($"laser hectorHp={_hpEnemy:F1} (want 304)");
                    if (_fxLights <= 0) { WFail("laser hit lit no impact light"); return; }
                    Dbg.Print($"WPNTEST LASER PASS events=3 dmg=36 hectorHp={_hpEnemy:F0} (hit reaction: impact light)");
                    _enemyDmgEvents = 0; _enemyDmgTotal = 0f;
                    _wJoltSeen = 0f;
                    SetWPhase(WeaponsPhase.Autocannon);
                }
                break;

            case WeaponsPhase.Autocannon: // autocannon x2: 2 x 26 = 52 exactly. ROUTING DELTA
                    // (terrain step): on the old sine terrain the shells
                    // dropped ~0.6u in flight and landed on the pelvis/
                    // thigh junction, so leg_L absorbed the 52. On the
                    // heightfield the two stand heights are near-equal,
                    // the shells arrive higher and land mid-torso: the
                    // pool takes the 52 (304 -> 252) and leg_L stays
                    // untouched. Events/total are the invariant; the
                    // routing follows the terrain, as documented before.
                if (_wShotsFired == 0 && _wPhaseT > WFirstShotSeconds) { FireAutocannon(EnemyAim()); _wShotsFired++; }
                else if (_wShotsFired == 1 && _wPhaseT > WAcSecondShotSeconds) { FireAutocannon(EnemyAim()); _wShotsFired++; }
                else if (_wShotsFired == 2 && _wPhaseT > WAcVerdictSeconds)
                {
                    if (_enemyDmgEvents != 2 || Mathf.Abs(_enemyDmgTotal - WAcExpectedDamage) > WTolerance)
                        WFail($"ac events={_enemyDmgEvents} dmg={_enemyDmgTotal:F1} (want 2 / 52)");
                    if (Mathf.Abs(_hpEnemy - WAcHpExpected) > WTolerance) WFail($"ac hectorHp={_hpEnemy:F1} (want 252 pool under terrain-step routing)");
                    if (Mathf.Abs(_partsEnemy.HpOf(PartId.LegL) - WAcLegHpExpected) > WTolerance)
                        WFail($"ac legL partHp={_partsEnemy.HpOf(PartId.LegL):F1} (want 90 = untouched, shells routed to torso)");
                    if (_wJoltSeen < WJoltMin) { WFail($"cannon hits barely rocked the Hector (jolt env peak {_wJoltSeen:F2})"); return; }
                    Dbg.Print($"WPNTEST AC PASS events=2 dmg=52 hectorHp={_hpEnemy:F0} legL partHp={_partsEnemy.HpOf(PartId.LegL):F0} (shells routed to torso; reaction: jolt {_wJoltSeen:F2})");
                    _enemyDmgEvents = 0; _enemyDmgTotal = 0f;
                    SetWPhase(WeaponsPhase.Lrm);
                }
                break;

            case WeaponsPhase.Lrm: // LRM salvo: count rockets that damage the Hector (of 8)
                if (_wShotsFired == 0 && _wPhaseT > WFirstShotSeconds) { FireLrmSalvo(EnemyAim()); _wShotsFired++; }
                if (!_wCaptured && _wPhaseT > WLrmCaptureSeconds)
                {
                    _wCaptured = true;
                    QueueCapture(ProofDir + "weapons_rockets.png", CaptureTags.WRockets, CamMode.WpnRockets);
                }
                if (_wPhaseT > WLrmVerdictSeconds)
                {
                    if (_enemyDmgEvents < WLrmMinDamagingRockets) WFail($"lrm damaging rockets={_enemyDmgEvents}/8 dmg={_enemyDmgTotal:F1}");
                    Dbg.Print($"WPNTEST LRM PASS rocketsDamaging={_enemyDmgEvents}/8 dmg={_enemyDmgTotal:F1} hectorHp={_hpEnemy:F1}");
                    _enemyDmgEvents = 0; _enemyDmgTotal = 0f;
                    WSetup(LrmFlatPlayerPos, LrmFlatEnemyPos);
                    // The blocker on the line (owner, 2026-11-27:
                    // the (22,8) building this phase was written
                    // against is gone with the buildings): a big
                    // tree planted 9u in front of the enemy - clear
                    // of his stomp reach (radius + crown + margin
                    // = 7.5u at this scale), and wide enough that
                    // every converging beam meets its trunk or
                    // crown before the aim point.
                    AddTreeV(new Vector3(WBlockedTreeX, Main.HeightAt(WBlockedTreeX, WBlockedTreeZ), WBlockedTreeZ), 0, WBlockedTreeScale, 0f);
                    _wHpSnapshot = _hpEnemy;
                    SetWPhase(WeaponsPhase.Blocked);
                }
                break;

            case WeaponsPhase.Blocked: // laser with a tree on the line, 9u in front of the enemy: zero damage
                if (_wShotsFired == 0 && _wPhaseT > WBlockedShotSeconds)
                {
                    FireLaser(_enemy.RootPos + Vector3.Up * WBlockedAimHeight);
                    _wShotsFired++;
                    QueueCapture(ProofDir + "weapons_blocked.png", CaptureTags.WBlocked, CamMode.WpnBlocked);
                }
                if (_wShotsFired == 1 && _wPhaseT > WBlockedVerdictSeconds)
                {
                    if (_enemyDmgTotal > WTolerance || Mathf.Abs(_hpEnemy - _wHpSnapshot) > WTolerance)
                        WFail($"blocked laser leaked dmg={_enemyDmgTotal:F1} hp {_wHpSnapshot:F1}->{_hpEnemy:F1}");
                    Dbg.Print("WPNTEST BLOCKED PASS tree stopped the beam, hectorHp unchanged");
                    // Phase 5 engages at 45u, not 30u: at 30u the shell
                    // TOF (0.6s) is shorter than the strafe reversal and
                    // the lead error stays inside the part-box volume
                    // (the digitigrade thighs protrude ~2.7u ahead of the
                    // capsule), so return fire could not miss at all. At
                    // 45u the TOF (0.9s) spans a full reversal and the
                    // dodge is measurable again.
                    WSetup(new Vector3(0f, Main.HeightAt(0f, WReturnFirePlayerZ), WReturnFirePlayerZ), new Vector3(0f, Main.HeightAt(0f, WReturnFireEnemyZ), WReturnFireEnemyZ));
                    _playerDmgEvents = 0; _playerDmgTotal = 0f; _enemyShots = 0; _wPrevShots = 0;
                    _enemyMayFire = true;
                    _wStrafeT = 0; _wStrafeDir = 1f;
                    _wKnocked = false;
                    SetWPhase(WeaponsPhase.ReturnFire);
                }
                break;

            case WeaponsPhase.ReturnFire: // Hector return fire vs a strafing player.
                    // The player reverses every 0.9s at full speed. At the
                    // original 50 u/s shell the 0.9s flight let a reversal
                    // land inside the lead window - strafing genuinely
                    // dodged. Since 2026-10-29 enemy shells fly at the unified cannon speed
                    // (owner: Hector matches the player's cannon changes,
                    // symmetric rules), flight is ~0.3s, the strafe
                    // displaces ~1.2u - inside the target's own radius -
                    // so return fire is RELIABLE now, by design. The
                    // owner flagged this as provisional: if Hector feels
                    // unfair in his visual test, the speed reverts.
                _wStrafeT += dt;
                if (_player.IsKnockedDown) _wKnocked = true;
                if (_wStrafeT > WReturnFireStrafePeriod) { _wStrafeT = 0; _wStrafeDir = -_wStrafeDir; }
                float strafeYaw = _wStrafeDir > 0 ? -Mathf.Pi / 2f : Mathf.Pi / 2f;
                _player.SetDrive(strafeYaw, WReturnFireStrafeSpeed);
                _player.TorsoTwist = -strafeYaw;
                _enemy.SetDrive(Mathf.Pi, 0f);
                if (_enemyShots != _wPrevShots) { _wPrevShots = _enemyShots; _wLastShotT = _wPhaseT; }
                if (_enemyShots >= WReturnFireShots && _wPhaseT > _wLastShotT + WReturnFireSettleSeconds)
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
                    // (hits x 40, no tolerance games there).
                    if (hits < WReturnFireMinHits) WFail($"hector return fire unreliable at matched speed: {hits} hits in {_enemyShots} shots");
                    if (Mathf.Abs(_playerDmgTotal - hits * WReturnFireDamagePerHit) > WTolerance)
                        WFail($"hector dmg total={_playerDmgTotal:F1} != hits*40 ({hits * 40})");
                    Dbg.Print($"WPNTEST HECTORFIRE PASS shots={_enemyShots} hits={hits} misses={misses} playerHp={_hpPlayer:F0}");
                    _enemyMayFire = false;
                    _player.TorsoTwist = 0f;
                    ResetDuel();
                    WSetup(TestMarkPlayer, TestMarkEnemy);
                    SetWPhase(WeaponsPhase.Kill);
                }
                if (_wPhaseT > WReturnFireTimeoutSeconds) WFail($"hector return fire timeout shots={_enemyShots}");
                break;

            case WeaponsPhase.Kill: // sustained fire -> Hector destroyed, banner, slump frame
                if (!_enemyDead)
                {
                    FireLaser(EnemyAim());
                    FireAutocannon(EnemyAim());
                }
                if (!_wCaptured && _wPhaseT > WKillCaptureSeconds)
                {
                    _wCaptured = true;
                    QueueCapture(ProofDir + "weapons_hud.png", CaptureTags.WHud, CamMode.WpnHud);
                }
                if (_enemyDead && !_wDeathLogged)
                {
                    _wDeathLogged = true;
                    _wDeathT = _wPhaseT;
                    Dbg.Print($"WPNTEST KILL hector destroyed at phaseT={_wPhaseT:F2}");
                }
                if (_enemyDead && _wPhaseT > _wDeathT + WKillVerdictDelay && !_wDeadShotQueued)
                {
                    // Death comes only from the HP pool now (owner):
                    // wrecked legs limp, they never kill - so a dead
                    // Hector must show hp == 0 exactly.
                    if (_hpEnemy != 0f)
                    { WFail($"dead hector hp={_hpEnemy:F1} (want pool kill, hp 0)"); return; }
                    if (!_enemy.Halted) { WFail("dead hector walker not halted"); return; }
                    // No centre banner for a destruction (owner,
                    // round 4 - the game's current contract: the
                    // wreck, the feed and the target readout
                    // carry the news). The banner must stay DOWN
                    // and the readout must say DESTROYED.
                    if (_banner.Visible)
                    { WFail($"kill raised the centre banner ('{_banner.Text}') - destructions must not"); return; }
                    if (!_lblRange.Text.Contains("DESTROYED"))
                    { WFail($"target readout does not carry the kill: '{_lblRange.Text}'"); return; }
                    Dbg.Print($"WPNTEST KILL PASS death certified (hp={_hpEnemy:F0}), walker halted, no banner, readout '{_lblRange.Text}'");
                    _wDeadShotQueued = true;
                    QueueCapture(ProofDir + "weapons_hector_dead.png", CaptureTags.WDead, CamMode.WpnDead, () => SetWPhase(WeaponsPhase.PlayerDeath));
                }
                if (_wPhaseT > WKillTimeoutSeconds) WFail("kill sequence timeout");
                break;

            case WeaponsPhase.PlayerDeath: // player death banner + R reset restores everything
                if (_wShotsFired == 0 && _wPhaseT > WDeathShotSeconds)
                {
                    _wShotsFired++;
                    var dummy = new HitModel.Result
                    {
                        Hit = true,
                        Target = Roles.Player,
                        Zone = HitZone.Torso,
                        Point = _player.RootPos + Vector3.Up * WDeathHitHeight
                    };
                    ApplyDamage(_player, WDeathDamage, dummy, Roles.Enemy);
                    if (!_playerDead) { WFail("player death flag not set"); return; }
                    // Same no-banner contract for the player's
                    // own death (owner, round 4): R still resets,
                    // but nothing raises the centre banner.
                    if (_banner.Visible)
                    { WFail($"player death raised the centre banner ('{_banner.Text}') - it must not"); return; }
                    Dbg.Print("WPNTEST PLAYERDEATH PASS death certified, no banner (round-4 contract)");
                }
                if (_wShotsFired == 1 && _wPhaseT > WDeathResetSeconds)
                {
                    ResetDuel();
                    if (_hpPlayer != _playerSpec.MaxHp || _hpEnemy != _enemySpec.MaxHp || _playerDead || _enemyDead || _banner.Visible)
                        WFail($"reset state wrong hp={_hpPlayer}/{_hpEnemy} dead={_playerDead}/{_enemyDead} banner={_banner.Visible}");
                    Dbg.Print("WPNTEST RESET PASS hp pools restored, deaths and banner cleared");
                    SetWPhase(WeaponsPhase.Environment);
                }
                break;

            case WeaponsPhase.Environment: // ENV: destructible trees (owner, 2026-11-27: the building half went with the buildings)
                if (_wEnvStep == WeaponsEnvStep.Stage)
                {
                    ResetDuel();
                    _wEnvT = null;
                    foreach (var d in _destructibles)
                        if (d.Kind == DestrKind.Tree && d.State == DestrState.Intact && _wEnvT == null) _wEnvT = d;
                    if (_wEnvT == null) { WFail("no intact tree for the env phase"); return; }
                    _wEnvFx0 = _envFxEvents; _wEnvChunks0 = _envChunks.Count; _wEnvParticles0 = _fxParticles;
                    // Firing position: toward the arena center from the tree.
                    var tc0 = _wEnvT.Center;
                    var toC0 = (Vector3.Zero - tc0); toC0.Y = 0f;
                    var fp0 = tc0 + toC0.Normalized() * WEnvTreeDistance;
                    fp0.Y = Main.HeightAt(fp0.X, fp0.Z);
                    _player.Teleport(fp0, RiderWalker.YawOf((tc0 - fp0).Normalized()));
                    _playerYaw = _player.LegsYaw;
                    _wEnvStep = WeaponsEnvStep.TreeHit;
                }
                else if (_wEnvStep == WeaponsEnvStep.TreeHit)
                {
                    // ONE hit of any kind destroys the tree, and its
                    // shot-blocking cylinder is gone from that same
                    // frame (owner's rule).
                    var tc = _wEnvT.Center;
                    // The owner's live scenario, scripted - twice
                    // over. He aims at the tree's VISIBLE center: the
                    // crown, ~1.5u off the trunk axis at 0.75 height.
                    // Round 11 converged on the trunk but the crown
                    // had no collider at all, so the shot still went
                    // through the leaves. Now the canopy collides:
                    // the chest ray must catch it, convergence must
                    // land on it, and one hit must drop the tree.
                    _player.TorsoTwist = 0f;
                    // Aim the way the player does: hunt the pitch
                    // until the RETICLE sits on the crown - the
                    // camera-ray convergence reports the prop. With
                    // the high camera that pitch is steeper than the
                    // torso-to-crown line (the round-12 offset the
                    // owner reported: cursor on target, hits low).
                    _cdLaser = 0f;
                    Vector3 conv = Vector3.Zero; bool onCrown = false;
                    var crownCtr = new Vector3(tc.X, tc.Y + _wEnvT.Height * WEnvCrownHeightFactor, tc.Z);
                    for (float pp = WEnvCrownPitchStart; pp >= WEnvCrownPitchEnd; pp -= WEnvCrownPitchStep)
                    {
                        _player.TorsoPitch = pp;
                        conv = TorsoAimPoint();
                        // Any PROP along the view line is not enough -
                        // the convergence must be ON this tree's crown.
                        if (_aimTarget == Roles.Prop && conv.DistanceTo(crownCtr) < WEnvCrownTolerance) { onCrown = true; break; }
                    }
                    if (!onCrown) { WFail("no pitch puts the cursor on the crown"); return; }
                    _wEnvChunks0 = _envChunks.Count; // re-baseline at the shot so the splinter delta is the tree's alone
                    FireLaser(conv);
                    if (_wEnvT.State != DestrState.SmokingOut) { WFail($"one hit did not destroy the tree (state={_wEnvT.State})"); return; }
                    var fromT = _player.RootPos + Vector3.Up * WEnvShotOriginHeight;
                    var aimT = new Vector3(tc.X, tc.Y + WEnvRubbleAimHeight, tc.Z);
                    var rayT = _hit.Ray(fromT, (aimT - fromT).Normalized(), WEnvRayRange, Roles.Player);
                    if (rayT.Target == Roles.Prop && rayT.T < fromT.DistanceTo(aimT) + WEnvRubbleSlack) { WFail("destroyed tree still blocks shots"); return; }
                    if (_envFxEvents <= _wEnvFx0) { WFail("tree died with NO destruction FX"); return; }
                    int splinters = _envChunks.Count - _wEnvChunks0;
                    if (splinters < 5) { WFail($"tree threw only {splinters} splinters (want 5)"); return; }
                    int tp = _fxParticles - _wEnvParticles0;
                    if (tp < 3) { WFail($"tree death spawned only {tp} particle systems (want fire burst + burning wreck)"); return; }
                    Dbg.Print($"ENVTEST TREE-HIT PASS laser aimed at the visible crown (1.5u off-axis) converged on it and destroyed the tree in one hit, colliders removed at once, FX fired (+{splinters} splinters, {tp} particle systems)");
                    _wEnvStep = WeaponsEnvStep.TreeSmoke;
                }
                else if (_wEnvStep == WeaponsEnvStep.TreeSmoke)
                {
                    if (!_wCaptured) { _wCaptured = true; QueueCapture(ProofDir + "env_tree_destroyed.png", CaptureTags.EnvTreeDown, CamMode.Chase); }
                    if (_wPhaseT > WEnvSmokeTimeoutSeconds) { WFail("destroyed tree never finished smoking out"); return; }
                    if (_wEnvT.State == DestrState.Gone)
                    {
                        if (GodotObject.IsInstanceValid(_wEnvT.Node)) { WFail("destroyed tree node still alive"); return; }
                        Dbg.Print("ENVTEST TREE PASS destroyed on one hit, smoked out in ~1s, node freed");
                        Dbg.Print($"WPNTEST FX sparks={_fxSparkBursts} booms={_fxBooms}");
                        if (_fxSparkBursts <= 0) WFail("no laser spark bursts spawned");
                        if (_fxBooms <= 0) WFail("no rocket explosions spawned");
                        if (_fxParticles <= 0) WFail("no particle systems spawned");
                        Dbg.Print("WPNTEST FX PASS laser sparks and rocket explosions fired");
                        _wEnvStep = WeaponsEnvStep.FarTargets; _wPhaseT = 0f;
                    }
                }
                else if (_wEnvStep == WeaponsEnvStep.FarTargets)
                {
                    // The weapons suite's captures leave _camMode on
                    // a proof pose, which disables TorsoAimPoint's
                    // camera-ray branch - every ENV hunt before this
                    // line silently ran on the chest fallback (the
                    // far Hector passed through it, masking the fact).
                    // Live play is always chase; restore it here.
                    _camMode = CamMode.Chase;
                    // FAR TARGET (owner, round 13): at 85u the old
                    // 60u convergence cap rejected the cursor's hit
                    // and shots fell back to the free point - in the
                    // sky. Hunt the pitch until the cursor sits on
                    // the Hector, then require exact laser damage.
                    // The bearing search starts on the SE valley
                    // lane: the old z-line crosses the centre dome, and since
                    // the per-gun beam model (2026-11-25) the
                    // muzzles - lower than the camera - strike its
                    // skirt while the cursor clears it (the owner's
                    // accepted crest trade-off), so that line can
                    // no longer certify convergence. The endpoints
                    // are nudged clear of the tree scatter first
                    // (WFarClearSpot): a muzzle that starts inside
                    // a canopy cylinder loses its shot at T=0.
                    _hpEnemy = _enemySpec.MaxHp;
                    var farE = WFarClearSpot(WFarEnemyX, WFarEnemyZ);
                    float farR = new Vector2(WFarPlayerX - farE.X, WFarPlayerZ - farE.Z).Length();
                    float farBaseAng = Mathf.Atan2(WFarPlayerZ - farE.Z, WFarPlayerX - farE.X);
                    Vector3 convF = Vector3.Zero; bool onFar = false;
                    // Per-gun beams (owner, 2026-11-25): the
                    // cursor's convergence no longer certifies a
                    // hit by itself - a muzzle sits lower than the
                    // camera and offset from it, so a crest (or a
                    // tree at the muzzle) can eat the volley while
                    // the cursor sits on the target; that is the
                    // owner's accepted trade-off of the physical-
                    // beam model. The far shot is certified the way
                    // live play works: a bearing and pitch where
                    // the cursor is on the Hector AND at least one
                    // gun's own cast reaches him. The valley lane
                    // is tried first; if its muzzle line is crest-
                    // eaten the search walks the whole 85u circle
                    // around the Hector and keeps the first clear
                    // bearing - the same searched staging the FAR
                    // HILL step below runs for its hillside.
                    for (int k = 0; k < BearingSweepSteps && !onFar; k++)
                    {
                        float ang = farBaseAng + k * BearingSweepStep;
                        var cand = new Vector2(farE.X + farR * Mathf.Cos(ang), farE.Z + farR * Mathf.Sin(ang));
                        if (Mathf.Abs(cand.X) > WFarSearchBound || Mathf.Abs(cand.Y) > WFarSearchBound) continue;
                        var farP = WFarClearSpot(cand.X, cand.Y);
                        float farYawP = RiderWalker.YawOf(farE - farP);
                        float farYawE = RiderWalker.YawOf(farP - farE);
                        _player.Teleport(farP, farYawP);
                        _enemy.Teleport(farE, farYawE);
                        _playerYaw = farYawP; _enemyYaw = farYawE;
                        _player.SetDrive(farYawP, 0f); _enemy.SetDrive(farYawE, 0f);
                        _player.TorsoTwist = 0f;
                        _playerVel = Vector3.Zero; _prevPlayerRoot = _player.RootPos;
                        UpdateCamera(0f);
                        for (float pp = WFarPitchStart; pp >= WFarPitchEnd && !onFar; pp -= WFarPitchStep)
                        {
                            _player.TorsoPitch = pp;
                            var conv = TorsoAimPoint();
                            if (_aimTarget != Roles.Enemy) continue;
                            foreach (var g in _gunsEnergy)
                            {
                                if (!_partsPlayer.Alive(g.Slot)) continue;
                                var mz = MuzzleForSlot(g.Slot, false);
                                var toC = conv - mz;
                                if (toC.Length() < MinAimDirLength) continue;
                                var cast = AimRay(mz, toC.Normalized(), g.Def.Range, LaserHitThickness);
                                if (cast.Hit && cast.Target == Roles.Enemy) { convF = conv; onFar = true; break; }
                            }
                        }
                    }
                    if (!onFar) { WFail("no bearing on the 85u circle gives the cursor and a gun a clear line to the far Hector"); return; }
                    _cdLaser = 0f;
                    FireLaser(convF);
                    if (340f - _hpEnemy < WFarMinDamage) { WFail($"far cursor shot did not damage the Hector (hp={_hpEnemy:F0})"); return; }
                    Dbg.Print($"ENVTEST FAR-TARGET PASS cursor convergence at 85u: laser hit the Hector for {340f - _hpEnemy:F0}");
                    // FAR HILL (owner, round 13): the cursor on a
                    // distant hillside with a clear line must
                    // converge on the hill itself, not the sky. The
                    // Hector steps aside. Staging is searched, not
                    // assumed, so the harness tries pads x facings
                    // with the game's own aim math and keeps the
                    // first setup where a pitch hunt finds a far
                    // TERRAIN convergence - the same search a player
                    // does when he turns toward a slope. Round 27:
                    // the hunt band extends to -0.60 - the old
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
                            // walker step - the first search swept one
                            // lane sixteen times (measured: no far
                            // convergence anywhere, because every
                            // 'facing' was still yaw 0).
                            _player.Teleport(new Vector3(pad.X, Main.HeightAt(pad.X, pad.Y), pad.Y), a);
                            _playerYaw = a;
                            _player.TorsoTwist = 0f;
                            UpdateCamera(0f);
                            for (float pp = WFarHillPitchStart; pp >= WFarHillPitchEnd; pp -= WFarHillPitchStep)
                            {
                                _player.TorsoPitch = pp;
                                convH = TorsoAimPoint();
                                if (_aimTarget == Roles.Terrain && convH.DistanceTo(_player.RootPos) > WFarHillMinDistance) { onHill = true; break; }
                            }
                        }
                    }
                    if (!onHill) { WFail("no pad/facing puts the cursor on a far hillside"); return; }
                    Dbg.Print($"ENVTEST FAR-HILL PASS cursor converges on the hillside at {convH.DistanceTo(_player.RootPos):F0}u with a clear line");
                    Dbg.Print("WPNTEST DONE");
                    GetTree().Quit(0);
                }
                break;
        }
    }

    // ---------- breakable-test choreography ----------

    private enum BreakPhase { Settle, Pod, Salvo, Gun, PlayerArm, Leg, BothLegs, Head, Reset, LegRule }
    private enum BreakStep { First, Second, Third, Fourth }

    // Break-test timers, tolerances and damage fixtures (exact values).
    private const double BPhaseTimeoutSeconds = 90;
    private const double BSettleSeconds = 1.0;
    private const float BTolerance = 0.01f;
    private const float BPodHpAfterHit = 19f;
    private const float BArmGateHp = 1f;
    private const float BPodFullHp = 45f;
    private const float BLegFullHp = 90f;
    private const float BHeadPoolAfterHits = 290f;
    private const float BLimpBaselineMinSpeed = 0.1f;
    private const float BCrawlFactorExpected = 0.25f;
    private const float BFullSpeedFactor = 1f;
    private const float BPodHitDamage = 26f; // one autocannon hit, crafted
    private const float BLegHitDamage = 30f;
    private const float BHeadHitDamage = 25f;
    private const float BTorsoHitDamage = 12f; // one laser hit, crafted
    private const double BPodFirstHitSeconds = 0.2;
    private const float BPodSecondHitSeconds = 0.3f;
    private const float BPodDebrisSeconds = 0.30f;
    private const float BPodDebrisTimeoutSeconds = 7f;
    private const float BPodWalkSeconds = 3.0f;
    private const float BDebrisDriftMax = 0.05f;
    private const float BDebrisWalkAwayMin = 3f;
    private const float BSalvoVerdictSeconds = 1.6f;
    private const float BDenyCheckSeconds = 0.8f;
    private const double BGunPreBreakSeconds = 20;
    private const float BGunBreakTimeoutSeconds = 15f;
    private const float BGunSilenceStageSeconds = 1.2f;
    private const float BGunSilenceSeconds = 5.5f;
    private const float BSpeedStageSeconds = 1.0f;
    private const float BSpeedCaptureSeconds = 1.0f;
    private const float BSpeedBaselineSeconds = 3.0f;
    private const float BSpeedBreakCheckSeconds = 1.2f;
    private const float BBaselineSpeedMin = 1.2f;
    private const float BLimpCount1Seconds = 1.0f;
    private const float BLimpCount2Seconds = 2.0f;
    private const float BLimpCount3Seconds = 3.0f;
    private const float BLimpRatioMin = 0.35f;
    private const float BLimpRatioMax = 0.78f;
    private const float BDisabledCaptureSeconds = 2.0f;
    private const float BHeadFlankX = 20f;
    private const float BHeadFlankZ = 2f;
    private const float BHeadLagSeconds = 0.5f;
    private const float BHeadDesiredMin = 0.7f;
    private const float BHeadLagTwistMax = 0.37f;
    private const float BHeadCatchUpSeconds = 2.2f;
    private const float BHeadCatchUpErrMax = 0.08f;
    private const float BHeadStillSeconds = 1.2f;
    private const float BReticleDevMax = 1f;
    private const float BResetSalvoSeconds = 1.6f;
    private const float BLegRuleSettleSeconds = 0.6f;
    private const float BLegRuleWreckSeconds = 0.8f;

    private void SetBPhase(BreakPhase p)
    {
        _bPhase = p;
        _bPhaseT = 0;
        _bStep = BreakStep.First;
        _bT0 = 0;
        _bFlag1 = false;
        Dbg.Print($"BRKTEST phase={(int)p} t={_simT:F2}");
    }

    private void BStep(BreakStep s)
    {
        _bStep = s;
        _bT0 = _bPhaseT;
        _bFlag1 = false;
        _bCount1 = 0;
    }

    private float BStepT => (float)(_bPhaseT - _bT0);

    private void BFail(string msg)
    {
        Dbg.Err($"BRKTEST FAIL: {msg}");
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
    // on the part's own largest-volume center - a point guaranteed to
    // sit inside the part). Same precedent as the weapons test's crafted
    // player-death hit.
    private void CraftHit(RiderWalker target, PartId partId, float dmg, float weaponWeight = 0f)
    {
        var parts = target == _player ? _partsPlayer : _partsEnemy;
        var hit = new HitModel.Result
        {
            Hit = true,
            Target = target == _player ? Roles.Player : Roles.Enemy,
            Zone = HitZone.Torso,
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
    private void CraftRayHit(RiderWalker target, PartId partId, float dmg)
    {
        var parts = target == _player ? _partsPlayer : _partsEnemy;
        var aim = parts.PartAimPoint(partId);
        Vector3 from;
        if (partId == PartId.Head)
        {
            var fwd = RiderWalker.HeadingVec(target.TorsoYawWorld);
            from = aim + Vector3.Up * 5f + fwd * 7f;
        }
        else from = aim + Vector3.Up * 15f;
        var res = _hit.Ray(from, (aim - from).Normalized(), 40f, target == _player ? Roles.Enemy : Roles.Player);
        Dbg.Print($"BRK CRAFTRAY {partId} hit={res.Hit} target={res.Target} part={res.Part} point={res.Point}");
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
        if (_bPhaseT > BPhaseTimeoutSeconds) { BFail($"phase {(int)_bPhase} timeout"); return; }

        switch (_bPhase)
        {
            case BreakPhase.Settle: // settle on the lane: TW (0,18) vs Hector (0,-12)
                _player.SetDrive(0f, 0f);
                _enemy.SetDrive(Mathf.Pi, 0f);
                if (_bPhaseT > BSettleSeconds) SetBPhase(BreakPhase.Pod);
                break;

            case BreakPhase.Pod: // POD: part HP depletes per hit, breaks at <= 0, debris
                    // settles, then stays behind while the rider walks away
                _player.SetDrive(0f, _bStep >= BreakStep.Fourth ? 2.4f : 0f);
                _enemy.SetDrive(Mathf.Pi, 0f);
                if (_bStep == BreakStep.First && _bPhaseT > BPodFirstHitSeconds)
                {
                    CraftHit(_player, PartId.PodL, BPodHitDamage);
                    if (Mathf.Abs(_partsPlayer.HpOf(PartId.PodL) - BPodHpAfterHit) > BTolerance)
                        BFail($"pod hp after 26 dmg = {_partsPlayer.HpOf(PartId.PodL):F1} (want 19)");
                    Dbg.Print("BRKTEST PODHP PASS part HP 45 -> 19 after one 26 hit");
                    BStep(BreakStep.Second);
                }
                else if (_bStep == BreakStep.Second && BStepT > BPodSecondHitSeconds)
                {
                    CraftHit(_player, PartId.PodL, BPodHitDamage);
                    if (_partsPlayer.Alive(PartId.PodL)) BFail("pod_L alive after 52 total damage");
                    if (_partsPlayer.DebrisOf(PartId.PodL) == null) BFail("pod_L debris missing");
                    Dbg.Print("BRKTEST PODBREAK PASS pod_L broke at <= 0 HP, debris spawned");
                    BStep(BreakStep.Third);
                }
                else if (_bStep == BreakStep.Third)
                {
                    // Mid-fall frame at ~0.3s of debris age (the piece
                    // leaves at +2.8 u/s up, then falls ~11u: at 0.3s it
                    // is visibly airborne and clear of the torso).
                    if (!_bFlag1 && BStepT > BPodDebrisSeconds)
                    {
                        _bFlag1 = true;
                        QueueCapture(ProofDir + "break_pod_fall.png", CaptureTags.BPodFall, CamMode.BrkPlayer);
                    }
                    var d = _partsPlayer.DebrisOf(PartId.PodL);
                    if (d != null && d.Settled)
                    {
                        if (!_partsPlayer.DebrisRestOk(PartId.PodL, 0.15f, out float minY, out float gY))
                            BFail($"pod debris rest off: lowestCornerY={minY:F3} terrainThere={gY:F3}");
                        Dbg.Print($"BRKTEST DEBRISREST PASS pod_L lowest corner on terrain (y={minY:F3} vs {gY:F3})");
                        _bVec1 = d.Node.GlobalPosition;
                        _bV1 = _player.RootPos.DistanceTo(_bVec1);
                        _camFocus = _bVec1;
                        BStep(BreakStep.Fourth);
                    }
                    else if (BStepT > BPodDebrisTimeoutSeconds) BFail("pod debris never settled");
                }
                else if (_bStep == BreakStep.Fourth && BStepT > BPodWalkSeconds)
                {
                    _player.SetDrive(0f, 0f);
                    var d = _partsPlayer.DebrisOf(PartId.PodL);
                    float drift = d.Node.GlobalPosition.DistanceTo(_bVec1);
                    float away = _player.RootPos.DistanceTo(d.Node.GlobalPosition) - _bV1;
                    if (drift > BDebrisDriftMax) BFail($"debris moved with the rider: drift={drift:F3}");
                    if (away < BDebrisWalkAwayMin) BFail($"rider did not walk away from debris: delta={away:F2}");
                    Dbg.Print($"BRKTEST DETACH PASS debris static (drift={drift:F3}) while rider walked {away:F1}u away");
                    QueueCapture(ProofDir + "break_debris_rest.png", CaptureTags.BDebris, CamMode.BrkDebris);
                    SetBPhase(BreakPhase.Salvo);
                }
                break;

            case BreakPhase.Salvo: // SALVO: one pod gone -> 4 rockets; both gone -> denied
                _player.SetDrive(0f, 0f);
                _enemy.SetDrive(Mathf.Pi, 0f);
                if (_bStep == BreakStep.First)
                {
                    BStep(BreakStep.Second);
                    _bCount1 = _rocketsLaunched;
                    FireLrmSalvo(EnemyAim());
                }
                else if (_bStep == BreakStep.Second && BStepT > BSalvoVerdictSeconds)
                {
                    int n = _rocketsLaunched - _bCount1;
                    if (n != 4) BFail($"salvo with pod_L gone = {n} rockets (want 4)");
                    Dbg.Print("BRKTEST SALVO4 PASS one pod lost -> 4 rockets");
                    CraftHit(_player, PartId.PodR, BPodHitDamage);
                    CraftHit(_player, PartId.PodR, BPodHitDamage);
                    if (_partsPlayer.Alive(PartId.PodR)) BFail("pod_R alive after 52 damage");
                    BStep(BreakStep.Third);
                    _bCount1 = _rocketsLaunched;
                    _lastDenied = DenyKind.None;
                    FireLrmSalvo(EnemyAim());
                    if (_lastDenied != DenyKind.Lrm) BFail("LRM not denied with both pods gone");
                }
                else if (_bStep == BreakStep.Third && BStepT > BDenyCheckSeconds)
                {
                    if (_rocketsLaunched != _bCount1) BFail("denied LRM still launched rockets");
                    Dbg.Print("BRKTEST SALVODENIED PASS both pods gone -> LRM denied, 0 rockets");
                    ResetDuel();
                    BSetup(new Vector3(0f, Main.HeightAt(0f, 20f), 20f), new Vector3(0f, Main.HeightAt(0f, -10f), -10f));
                    SetBPhase(BreakPhase.Gun);
                }
                break;

            case BreakPhase.Gun: // GUN (Hector): real AC fire breaks the waist gun; its cannons stop
                _player.SetDrive(0f, 0f);
                if (_bStep == BreakStep.First)
                {
                    _enemyMayFire = true;
                    _enemy.SetDrive(Mathf.Pi, 0f);
                    if (_enemyShots >= 2) BStep(BreakStep.Second);
                    else if (_bPhaseT > BGunPreBreakSeconds) BFail($"hector fired only {_enemyShots} shots pre-break");
                }
                else if (_bStep == BreakStep.Second)
                {
                    _enemy.SetDrive(Mathf.Pi, 0f);
                    if (_cdAc <= 0f) FireAutocannon(_partsEnemy.PartAimPoint(PartId.Gun));
                    if (!_partsEnemy.Alive(PartId.Gun))
                    {
                        Dbg.Print($"BRKTEST GUNBREAK PASS hector waist gun destroyed by real AC fire (partHp logs above), hectorShots so far={_enemyShots}");
                        BStep(BreakStep.Third);
                        _bCount1 = _enemyShots;
                    }
                    else if (BStepT > BGunBreakTimeoutSeconds) BFail("hector waist gun never broke under AC fire");
                }
                else if (_bStep == BreakStep.Third)
                {
                    _enemy.SetDrive(Mathf.Pi, 1.6f);
                    if (BStepT > BGunSilenceStageSeconds && !_bFlag1)
                    {
                        _bFlag1 = true;
                        QueueCapture(ProofDir + "break_hector_nogun.png", CaptureTags.BNoGun, CamMode.BrkTarget);
                    }
                    if (BStepT > BGunSilenceSeconds)
                    {
                        if (_enemyShots != _bCount1)
                            BFail($"hector waist gun fired after its loss: {_bCount1} -> {_enemyShots}");
                        Dbg.Print($"BRKTEST GUNPASS PASS hector waist gun silent for 5.5s after its loss (shots flat at {_enemyShots})");
                        _enemyMayFire = false;
                        ResetDuel();
                        BSetup(TestMarkPlayer, TestMarkEnemy);
                        SetBPhase(BreakPhase.PlayerArm);
                    }
                }
                break;

            case BreakPhase.PlayerArm: // PLAYER ARM: arm-break gate first, then laser denied; HUD frame
                _player.SetDrive(0f, 0f);
                _enemy.SetDrive(Mathf.Pi, 0f);
                if (_bStep == BreakStep.First)
                {
                    // Arm-break gate (owner, 2026-11-12): at a
                    // FULL pool, four 26-dmg arm hits must NOT
                    // break the arm - damage caps at 1 HP left.
                    _hpPlayer = _playerSpec.MaxHp;
                    for (int i = 0; i < 4; i++) CraftHit(_player, PartId.ArmL, BPodHitDamage);
                    if (!_partsPlayer.Alive(PartId.ArmL)) BFail("arm_L broke at full pool - gate not holding");
                    if (Mathf.Abs(_partsPlayer.HpOf(PartId.ArmL) - BArmGateHp) > BTolerance)
                        BFail($"arm_L hp after gated hits = {_partsPlayer.HpOf(PartId.ArmL):F1} (want exactly 1)");
                    Dbg.Print($"BRKTEST ARMGATE PASS arm capped at 1 HP while pool healthy (pool {_hpPlayer:F0}/{_playerSpec.MaxHp:F0})");
                    BStep(BreakStep.Second);
                }
                else if (_bStep == BreakStep.Second)
                {
                    // Pool critical (19%): the gate lifts and
                    // one more hit takes the arm off.
                    _hpPlayer = _playerSpec.MaxHp * 0.19f;
                    CraftHit(_player, PartId.ArmL, BPodHitDamage);
                    if (_partsPlayer.Alive(PartId.ArmL)) BFail("arm_L survived at 19% pool - gate did not lift");
                    if (_partsPlayer.DebrisOf(PartId.ArmL) == null) BFail("arm_L debris missing after gated break");
                    Dbg.Print("BRKTEST ARMGATE-OPEN PASS arm breaks once the pool is critical");
                    _bCount1 = _beamsSpawned;
                    _lastDenied = DenyKind.None;
                    FireLaser(EnemyAim());
                    if (_lastDenied != DenyKind.Laser) BFail("laser not denied after arm_L loss");
                    if (_beamsSpawned != _bCount1) BFail("denied laser still spawned a beam");
                    Dbg.Print("BRKTEST LASERDENIED PASS arm_L lost -> laser denied, 0 beams");
                    QueueCapture(ProofDir + "break_hud_destroyed.png", CaptureTags.BHud, CamMode.WpnHud);
                    BStep(BreakStep.Third);
                }
                else if (_bStep == BreakStep.Third && BStepT > BDenyCheckSeconds)
                {
                    ResetDuel();
                    BSetup(new Vector3(-38f, Main.HeightAt(-38f, 38f), 38f), new Vector3(0f, Main.HeightAt(0f, 24f), 24f));
                    _bDragMax = 0f;
                    SetBPhase(BreakPhase.Leg);
                }
                break;

            case BreakPhase.Leg: // LEG (Hector): baseline speed, break leg_L, then the
                    // rider limps at the 0.55 factor with the wrecked leg
                    // still stepping (round 7: no rigid drag)
                _player.SetDrive(0f, 0f);
                if (_bStep == BreakStep.First)
                {
                    _enemy.SetDrive(Mathf.Pi, 0f);
                    if (BStepT > BSpeedStageSeconds) BStep(BreakStep.Second);
                }
                else if (_bStep == BreakStep.Second)
                {
                    _enemy.SetDrive(Mathf.Pi, 2.2f);
                    if (!_bFlag1 && BStepT > BSpeedCaptureSeconds)
                    {
                        _bFlag1 = true;
                        _bVec1 = _enemy.RootPos;
                        _bT0 = _bPhaseT;
                    }
                    else if (_bFlag1 && BStepT > BSpeedBaselineSeconds)
                    {
                        var d = new Vector2(_enemy.RootPos.X - _bVec1.X, _enemy.RootPos.Z - _bVec1.Z).Length();
                        _bV1 = d / BStepT;
                        Dbg.Print($"BRKTEST BASESPEED hector baseline={_bV1:F2} u/s (cmd 2.2; unified clock - the walker now covers the full commanded speed)");
                        if (_bV1 < BBaselineSpeedMin) BFail($"baseline speed implausible: {_bV1:F2}");
                        BStep(BreakStep.Third);
                    }
                }
                else if (_bStep == BreakStep.Third)
                {
                    _enemy.SetDrive(Mathf.Pi, 0f);
                    if (BStepT > BSpeedBreakCheckSeconds)
                    {
                        CraftHit(_enemy, PartId.LegL, BLegHitDamage);
                        CraftHit(_enemy, PartId.LegL, BLegHitDamage);
                        CraftHit(_enemy, PartId.LegL, BLegHitDamage);
                        if (_partsEnemy.Alive(PartId.LegL)) BFail("hector leg_L alive after 90 damage");
                        if (!_enemy.LegDisabled(true)) BFail("walker leg_L not disabled");
                        var legPart = _partsEnemy.GetPart(PartId.LegL);
                        if (!legPart.Wrecked) BFail("leg_L not in wrecked state");
                        foreach (var rec in legPart.Meshes)
                            if (rec.Detached) BFail("leg_L mesh DETACHED - legs must never fall off");
                        if (_enemy.IsKnockedDown) BFail("leg wreck felled the rider (retired fall rule)");
                        if (Mathf.Abs(_enemy.SpeedFactor - LimpFactorExpected) > LimpFactorTolerance) BFail($"speed factor {_enemy.SpeedFactor:F2} != 0.55 (limp)");
                        Dbg.Print("BRKTEST LEGWRECK PASS leg_L destroyed but ATTACHED (wrecked), rider upright, limp factor 0.55");
                        _bSawWreckSwing = false;
                        BStep(BreakStep.Fourth);
                    }
                }
                else if (_bStep == BreakStep.Fourth)
                {
                    // Full drive on the wrecked leg: the rider must MOVE
                    // (it limps, it is not immobile) at roughly the 0.55
                    // limp factor of its measured baseline speed.
                    _enemy.SetDrive(Mathf.Pi, 2.2f);
                    if (!_enemy.LegPlanted(true)) _bSawWreckSwing = true;
                    if (_bCount1 == 0 && BStepT > BLimpCount1Seconds)
                    {
                        _bVec1 = _enemy.RootPos;
                        _bT0 = _bPhaseT; // BStepT now measures the sample window
                        _bCount1 = 1;
                    }
                    else if (_bCount1 == 1 && BStepT > BLimpCount2Seconds)
                    {
                        QueueCapture(ProofDir + "break_immobile.png", CaptureTags.BLimp, CamMode.BrkTarget);
                        _bCount1 = 2;
                    }
                    else if (_bCount1 == 2 && BStepT > BLimpCount3Seconds)
                    {
                        var d = new Vector2(_enemy.RootPos.X - _bVec1.X, _enemy.RootPos.Z - _bVec1.Z).Length();
                        float v2 = d / BStepT;
                        float ratio = _bV1 > BLimpBaselineMinSpeed ? v2 / _bV1 : 0f;
                        Dbg.Print($"BRKTEST LIMP hector limpSpeed={v2:F2} baseline={_bV1:F2} ratio={ratio:F2}");
                        if (ratio < BLimpRatioMin || ratio > BLimpRatioMax) { BFail($"limp speed ratio {ratio:F2} (want ~0.55 of baseline)"); return; }
                        if (!_bSawWreckSwing) { BFail("wrecked leg never left the ground while limping - it must keep stepping, not drag"); return; }
                        Dbg.Print("BRKTEST LIMP PASS wrecked leg -> rider keeps walking at ~55% speed, leg attached AND still stepping");
                        SetBPhase(BreakPhase.BothLegs);
                    }
                }
                break;

            case BreakPhase.BothLegs: // BOTH LEGS WRECKED: crawl at 25% - alive, upright, attached
                _player.SetDrive(0f, 0f);
                _enemy.SetDrive(Mathf.Pi, 0f);
                if (_bStep == BreakStep.First)
                {
                    CraftHit(_enemy, PartId.LegR, BLegHitDamage);
                    CraftHit(_enemy, PartId.LegR, BLegHitDamage);
                    CraftHit(_enemy, PartId.LegR, BLegHitDamage);
                    if (_enemyDead) BFail("hector DIED from leg damage - legs never kill (owner)");
                    if (_enemy.Halted) BFail("hector walker halted by leg damage");
                    if (_banner.Visible) BFail($"banner shown for a leg-wrecked rider: '{_banner.Text}'");
                    if (_enemy.IsKnockedDown) BFail("both-legs wreck felled the rider");
                    var lr = _partsEnemy.GetPart(PartId.LegR);
                    if (!lr.Wrecked) BFail("leg_R not wrecked");
                    foreach (var rec in lr.Meshes)
                        if (rec.Detached) BFail("leg_R mesh DETACHED");
                    if (Mathf.Abs(_enemy.SpeedFactor - BCrawlFactorExpected) > BTolerance)
                        BFail($"speed factor {_enemy.SpeedFactor:F2} != 0.25 (crawl)");
                    if (_hpEnemy <= 0f) BFail("pool hp drained by leg hits");
                    Dbg.Print($"BRKTEST BOTHLEGS PASS both legs wrecked -> CRAWL 0.25x, rider alive (pool hp={_hpEnemy:F0}), upright, legs attached");
                    BStep(BreakStep.Second);
                }
                else if (_bStep == BreakStep.Second && BStepT > BDisabledCaptureSeconds)
                {
                    QueueCapture(ProofDir + "break_hector_disabled.png", CaptureTags.BDisabled, CamMode.BrkTarget);
                    BStep(BreakStep.Third);
                }
                else if (_bStep == BreakStep.Third && _pendingPath == null && !_capturing)
                {
                    ResetDuel();
                    BSetup(TestMarkPlayer, TestMarkEnemy);
                    SetBPhase(BreakPhase.Head);
                }
                break;

            case BreakPhase.Head: // HEAD (Hector): pool rule + tracking lag; player reticle stays centred
                _player.SetDrive(0f, 0f);
                _enemy.SetDrive(Mathf.Pi, 0f);
                // The angled head ray races a sub-decimetre margin at
                // the cylinder rim (see CraftRayHit); the margin rides
                // on the model's AABB-centred placement in
                // RiderWalker.Init, so a model whose AABB grows (e.g.
                // a baked pose change) can flip it without any code
                // change here.
                if (_bStep == BreakStep.First)
                {
                    CraftRayHit(_enemy, PartId.Head, BHeadHitDamage);
                    CraftRayHit(_enemy, PartId.Head, BHeadHitDamage);
                    if (_partsEnemy.Alive(PartId.Head)) BFail("hector head alive after 50 damage");
                    if (Mathf.Abs(_hpEnemy - BHeadPoolAfterHits) > BTolerance)
                        BFail($"head->pool rule: hectorHp={_hpEnemy:F1} (want 290 = 340 - 50)");
                    Dbg.Print("BRKTEST HEADPOOL PASS head hits burned head HP and pool (340 -> 290)");
                    // Proof frame: the wrecked head must stay ON the
                    // neck, charred and lolled in place (owner,
                    // 2026-11-15: pieces sank/floated when the wreck
                    // deformation pivoted on the model origin).
                    QueueCapture(ProofDir + "break_hector_head.png", CaptureTags.BHead, CamMode.BrkTarget);
                    // Move the player to the Hector's flank: desired twist ~1 rad.
                    _player.Teleport(new Vector3(BHeadFlankX, Main.HeightAt(BHeadFlankX, BHeadFlankZ), BHeadFlankZ), 0f);
                    BStep(BreakStep.Second);
                }
                else if (_bStep == BreakStep.Second)
                {
                    var toP = _player.RootPos - _enemy.RootPos;
                    float yawTo = RiderWalker.YawOf(toP.Normalized());
                    TrackEnemyTorso(dt, yawTo);
                    float desired = Mathf.Abs(RiderWalker.WrapAngle(yawTo - _enemyYaw));
                    if (BStepT > BHeadLagSeconds)
                    {
                        float twist = Mathf.Abs(_enemy.TorsoTwist);
                        Dbg.Print($"BRKTEST HEADLAG desired={desired:F2} rad, twist after 0.5s={twist:F2} (lag cap 0.30 + eps)");
                        if (desired < BHeadDesiredMin) BFail($"flank geometry wrong: desired twist {desired:F2}");
                        if (twist > BHeadLagTwistMax) BFail($"headless tracking did not lag: twist {twist:F2} in 0.5s");
                        BStep(BreakStep.Third);
                    }
                }
                else if (_bStep == BreakStep.Third)
                {
                    var toP = _player.RootPos - _enemy.RootPos;
                    float yawTo = RiderWalker.YawOf(toP.Normalized());
                    TrackEnemyTorso(dt, yawTo);
                    if (BStepT > BHeadCatchUpSeconds)
                    {
                        float err = Mathf.Abs(RiderWalker.WrapAngle(
                            RiderWalker.WrapAngle(yawTo - _enemyYaw) - _enemy.TorsoTwist));
                        if (err > BHeadCatchUpErrMax) BFail($"headless tracking never caught up: err {err:F2}");
                        Dbg.Print($"BRKTEST HEADLAG PASS lag proven at 0.5s, caught up by 2.2s (err {err:F3})");
                        CraftRayHit(_player, PartId.Head, BHeadHitDamage);
                        CraftRayHit(_player, PartId.Head, BHeadHitDamage);
                        if (_partsPlayer.Alive(PartId.Head)) { BFail("player head alive after 50 damage"); return; }
                        _bDragMax = 0f;
                        BStep(BreakStep.Fourth);
                    }
                }
                else if (_bStep == BreakStep.Fourth)
                {
                    // Owner (2026-11-15): no crosshair sway - with
                    // the player's head gone the reticle must stay
                    // exactly on screen centre.
                    if (_cross.LastCentre.X > -100f)
                    {
                        float dev = (_cross.LastCentre - _cross.GetViewportRect().Size * 0.5f).Length();
                        if (dev > _bDragMax) _bDragMax = dev;
                    }
                    if (BStepT > BHeadStillSeconds)
                    {
                        if (_bDragMax > BReticleDevMax) BFail($"reticle wandered {_bDragMax:F1}px with the head gone (owner: no sway)");
                        Dbg.Print($"BRKTEST HEADSTILL PASS player head lost -> reticle stays centred (max dev {_bDragMax:F2}px)");
                        ResetDuel();
                        BSetup(TestMarkPlayer, TestMarkEnemy);
                        SetBPhase(BreakPhase.Reset);
                    }
                }
                break;

            case BreakPhase.Reset: // RESET: everything restored, salvo back to 8, rebind real
                _player.SetDrive(0f, 0f);
                _enemy.SetDrive(Mathf.Pi, 0f);
                if (_bStep == BreakStep.First)
                {
                    foreach (var p in _partsPlayer.AllParts)
                        if (p.Destroyed) BFail($"player part {p.Id} still destroyed after reset");
                    foreach (var p in _partsEnemy.AllParts)
                        if (p.Destroyed) BFail($"hector part {p.Id} still destroyed after reset");
                    if (_partsPlayer.DebrisList.Count != 0 || _partsEnemy.DebrisList.Count != 0)
                        BFail("debris remains after reset");
                    if (Mathf.Abs(_partsPlayer.HpOf(PartId.PodL) - BPodFullHp) > BTolerance ||
                        Mathf.Abs(_partsEnemy.HpOf(PartId.LegL) - BLegFullHp) > BTolerance ||
                        Mathf.Abs(_partsEnemy.HpOf(PartId.Head) - 50f) > BTolerance)
                        BFail("part HP not refilled after reset");
                    if (_enemy.LegDisabled(true) || _player.LegDisabled(false) ||
                        Mathf.Abs(_enemy.SpeedFactor - BFullSpeedFactor) > BTolerance)
                        BFail("walker leg state not cleared after reset");
                    var rec = _partsPlayer.GetPart(PartId.PodL).Meshes[0];
                    if (rec.Detached || rec.Mi.Skin == null || !(rec.Mi.GetParent() is Skeleton3D))
                        BFail("pod_L mesh not rebound to the skeleton after reset");
                    Dbg.Print("BRKTEST RESTORE PASS parts alive, HP full, debris freed, meshes rebound, walker state cleared");
                    BStep(BreakStep.Second);
                    _bCount1 = _rocketsLaunched;
                    FireLrmSalvo(EnemyAim());
                }
                else if (_bStep == BreakStep.Second && BStepT > BResetSalvoSeconds)
                {
                    int n = _rocketsLaunched - _bCount1;
                    if (n != 8) BFail($"post-reset salvo = {n} rockets (want 8)");
                    Dbg.Print("BRKTEST SALVO8 PASS salvo restored to 8 after reset");
                    SetBPhase(BreakPhase.LegRule);
                }
                break;
            case BreakPhase.LegRule: // LEG RULE (owner, current): impacts never fell; legs wreck, never detach, never fell
                if (_bStep == BreakStep.First)
                {
                    ResetDuel();
                    BSetup(TestMarkPlayer, TestMarkEnemy);
                    // 36t of autocannon impacts - far past every retired
                    // knockdown threshold. Legs intact.
                    CraftHit(_enemy, PartId.Torso, BTorsoHitDamage, WeaponCatalog.Autocannon.Weight);
                    CraftHit(_enemy, PartId.Torso, BTorsoHitDamage, WeaponCatalog.Autocannon.Weight);
                    CraftHit(_enemy, PartId.Torso, BTorsoHitDamage, WeaponCatalog.Autocannon.Weight);
                    BStep(BreakStep.Second);
                }
                else if (_bStep == BreakStep.Second && BStepT > BLegRuleSettleSeconds)
                {
                    if (_enemy.IsKnockedDown) BFail("weapon impacts toppled a rider (retired knockdown)");
                    Dbg.Print("KNOCKTEST NO-TOPPLE PASS 36t of impacts - the Hector stands (no knockdown rule remains)");
                    _partsEnemy.DamagePart(PartId.LegR, LimpWreckDamage);
                    BStep(BreakStep.Third);
                }
                else if (_bStep == BreakStep.Third && BStepT > BLegRuleWreckSeconds)
                {
                    if (_enemy.IsKnockedDown || _enemy.FallAmount > BTolerance)
                    { BFail("leg wreck felled the rider (retired fall rule)"); return; }
                    var lr = _partsEnemy.GetPart(PartId.LegR);
                    if (!lr.Wrecked) { BFail("leg_R not wrecked"); return; }
                    foreach (var rec in lr.Meshes)
                        if (rec.Detached) { BFail("leg_R mesh DETACHED - legs never fall off"); return; }
                    if (Mathf.Abs(_enemy.SpeedFactor - LimpFactorExpected) > LimpFactorTolerance)
                    { BFail($"speed factor {_enemy.SpeedFactor:F2} != 0.55 after leg wreck"); return; }
                    Dbg.Print("LEGRULE PASS leg wrecked: attached + darkened, rider upright, limping at 0.55x");
                    QueueCapture(ProofDir + "break_knockdown.png", CaptureTags.BrkKnock, CamMode.BrkTarget);
                    ResetDuel();
                    if (_partsEnemy.GetPart(PartId.LegR).Wrecked || !_partsEnemy.Alive(PartId.LegR))
                    { BFail("reset did not un-wreck leg_R"); return; }
                    if (Mathf.Abs(_enemy.SpeedFactor - BFullSpeedFactor) > BTolerance)
                    { BFail("reset did not restore full speed"); return; }
                    Dbg.Print("LEGRULE RESET PASS R reset un-wrecks the leg and restores speed");
                    Dbg.Print("BRKTEST DONE");
                    GetTree().Quit(0);
                }
                break;
        }
    }

    // ---------- terrain-test choreography ----------

    private void TFail(string msg)
    {
        Dbg.Err($"TERRAINTEST FAIL: {msg}");
        GetTree().Quit(1);
    }

    private const float HeightScanInit = 1e9f; // min/max scan seed, far outside any terrain height

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
        Dbg.Print($"TERRAIN SELFCHECK nodes={(Main.TerrainSegs + 1) * (Main.TerrainSegs + 1)} maxNodeDiff={maxDiff:F5}");
        // Arena grade scan (the duel-playable core).
        float maxG = 0f; Vector2 atG = Vector2.Zero;
        for (float x = -36f; x <= 36f; x += 1.5f)
        for (float z = -36f; z <= 36f; z += 1.5f)
        {
            if (new Vector2(x, z).Length() > 36f) continue;
            float g = TerrainGrade(x, z);
            if (g > maxG) { maxG = g; atG = new Vector2(x, z); }
        }
        Dbg.Print($"TERRAIN ARENA maxGrade={maxG:F3} ({Mathf.RadToDeg(Mathf.Atan(maxG)):F1} deg) at {atG}");
        float hMin = HeightScanInit, hMax = -HeightScanInit;
        for (int j = 0; j <= Main.TerrainSegs; j += 4)
        for (int i = 0; i <= Main.TerrainSegs; i += 4)
        {
            float h = Main.GridHeightAt(i, j);
            hMin = Mathf.Min(hMin, h); hMax = Mathf.Max(hMax, h);
        }
        Dbg.Print($"TERRAIN RANGE min={hMin:F1} max={hMax:F1}");
    }

    // The clearest steep spot the arena offers (the arena is gentle by
    // design - max grade ~0.08 - so take the best clear candidate).
    private const float SlopeMinGrade = 0.045f;
    private const float SlopeMaxGrade = 0.35f;
    private const float SlopeClearance = 6.5f;

    private bool FindSlopeSpot(out Vector3 spot, out float uphillYaw)
    {
        spot = Vector3.Zero; uphillYaw = 0f;
        float bestG = SlopeMinGrade;
        bool found = false;
        for (float rad = 34f; rad >= 14f; rad -= 2f)
        for (int a = 0; a < 48; a++)
        {
            float ang = a * Mathf.Tau / 48f;
            var p = new Vector2(Mathf.Cos(ang) * rad, Mathf.Sin(ang) * rad);
            float g = TerrainGrade(p.X, p.Y);
            if (g <= bestG || g > SlopeMaxGrade) continue;
            if (!ScatterFree(p, SlopeClearance)) continue;
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

    private enum MissionPhase { WorldAudit, Patrol, Aggro, CapturesPending = 99 }
    private const float MAuditSeconds = 1.2f;
    private const float MBaseFlatTolerance = 0.05f;
    private const float MBaseRingRadius = 20f;
    private const float MPatrolMinDisplacement = 10f;
    private const float MBaseLeashRadius = 130f;
    private const float MAggroTeleportDistance = 190f;
    private const float MAggroMinApproach = 12f;
    private const float MDumpIntervalSeconds = 0.25f;
    private const float MPatrolSeconds = 25f;
    private const float MAggroSeconds = 15f;
    private const float MTimeoutSeconds = 90f;
    private MissionPhase _mPhase;
    private float _mPhaseT, _mDistE0, _mDistF0, _mDumpT;
    private Vector3 _mEnemyStart, _mFoeStart;
    private float _mMaxDisp, _mMaxHome, _mMaxHomeF, _mAggroD0;

    // Mission front smoke: world facts (size, pad plateau flatness,
    // base buildings), base + vista captures, then 50 s of AI march
    // - both foes must close real distance toward their targets.
    // --longshot-test: the owner's 500u+ repro, staged exactly.
    // The player holds at the home base and fires ONE ballistic
    // volley along the chase axis (pitched up 2 degrees, nothing
    // within the 220u convergence cast - the far-target case).
    // The shell's perpendicular distance to the SIGHT LINE is
    // measured when its along-axis distance reaches 600u: with
    // convergence on the 220u far point it must read the muzzle
    // offset amplified ~2.7x (the sail-over); with parallel fire
    // it reads the bare muzzle offset.
    private enum LongshotPhase { Stage, Fire, Track }
    private const float LsStageSeconds = 1.2f;
    private const float LsFireSeconds = 0.4f;
    private const float LsTimeoutSeconds = 30f;
    private const float LsStagePitch = 0.035f;
    private const float LsRetryPitch = 0.09f;
    private const float LsParallelPitchMax = 0.1f;
    private const float LsShellMinSpeed = 200f;
    private const float LsJudgeDistance = 600f;
    private const float LsMaxPerp = 7f;
    private float _lsT; private LongshotPhase _lsPhase;
    private Projectile _lsProj; private Vector3 _lsOrigin, _lsDir;
    private float _lsLastAlong, _lsLastPerp, _lsDeathAlong;
    private void UpdateLongshotTest(float dt)
    {
        _lsT += dt;
        _player.SetDrive(_playerYaw, 0f);
        if (_lsPhase == LongshotPhase.Stage && _lsT > LsStageSeconds)
        {
            var toEnemy = _enemy.RootPos - _player.RootPos; toEnemy.Y = 0f;
            _playerYaw = RiderWalker.YawOf(toEnemy);
            _player.TorsoTwist = 0f;
            _player.AimPitchWide = LsStagePitch;
            _camMode = CamMode.Chase;
            Dbg.Print($"LONGSHOT staged: distToEnemy={toEnemy.Length():F0}u pitch=2deg");
            _lsPhase = LongshotPhase.Fire; _lsT = 0f;
        }
        else if (_lsPhase == LongshotPhase.Fire && _lsT > LsFireSeconds)
        {
            UpdateCamera(0f);
            var ap = TorsoAimPoint();
            if (!_aimParallel && _player.AimPitchWide < LsParallelPitchMax)
            {
                // Something pokes into the cast - raise the line
                // once and retry, the case under test is the
                // no-hit one.
                _player.AimPitchWide = LsRetryPitch; _lsT = 0f;
                Dbg.Print("LONGSHOT retrying with a higher line (cast was blocked)");
                return;
            }
            int before = _shots.Count;
            FireAutocannon(ap);
            if (_shots.Count <= before) { Dbg.Err("LONGSHOT FAIL: no shell spawned"); GetTree().Quit(1); return; }
            // Track the autocannon shell, not the volley's last
            // shell: the same slot also fires the rider's machine
            // guns, whose shells (85 u/s) burn out at their own
            // 200u range - tracking one of those reads as a
            // vanish at ~200u. (The range test learned this first.)
            _lsProj = null;
            for (int i = before; i < _shots.Count; i++)
                if (_shots[i].Vel.Length() > LsShellMinSpeed) _lsProj = _shots[i];
            if (_lsProj == null) { Dbg.Err("LONGSHOT FAIL: no autocannon shell in the volley"); GetTree().Quit(1); return; }
            _lsOrigin = _cam.GlobalPosition;
            _lsDir = _aimDir;
            var rel0 = _lsProj.GlobalPosition - _lsOrigin;
            float perp0 = (rel0 - _lsDir * rel0.Dot(_lsDir)).Length();
            Dbg.Print($"LONGSHOT fired: parallel={_aimParallel} muzzlePerp={perp0:F2}u");
            _lsPhase = LongshotPhase.Track; _lsT = 0f;
        }
        else if (_lsPhase == LongshotPhase.Track)
        {
            // The shell now burns out at its 600u range (owner's
            // per-weapon caps), and under llvmpipe a frame can
            // step ~100u of flight - the shell may die between
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
            if ((!gone && _lsLastAlong >= LsJudgeDistance) || (gone && _lsDeathAlong >= LsJudgeDistance))
            {
                Dbg.Print($"LONGSHOT RESULT along={_lsLastAlong:F0}u (burnout {(_lsDeathAlong):F0}u) perp={_lsLastPerp:F2}u");
                if (_lsLastPerp > LsMaxPerp) { Dbg.Err("LONGSHOT FAIL: shell off the sight line"); GetTree().Quit(1); return; }
                Dbg.Print("LONGSHOT TEST DONE");
                GetTree().Quit(0);
            }
            if (gone) { Dbg.Err($"LONGSHOT FAIL: shell died at {_lsDeathAlong:F0}u, before 600u"); GetTree().Quit(1); return; }
            if (_lsT > LsTimeoutSeconds) { Dbg.Err("LONGSHOT FAIL: timeout"); GetTree().Quit(1); }
        }
    }

    // --range-test: per-weapon Range enforcement (owner's rule:
    // a weapon reaches its catalog Range and no further), staged
    // in the Mission world against the stock Enyo loadout with
    // the Sarna MW4 ranges (Large Laser 600, Autocannon 600,
    // LRM 1000):
    //   A: laser at the enemy 1074u away - no damage (600 < 1074)
    //   B: laser at 180u - damage lands (180 < 600)
    //   C: LRM salvo base-to-base at 1074u - clamps to 1000
    //   D: autocannon at the sky - the shell dies at ~600u along
    // Aiming: the chase axis is iterated onto the enemy's chest
    // from the settled camera (3 passes), so the ray is exact.
    private enum RangePhase { FarLaser, CheckFar, StageNear, CheckNear, MissileCheck, TrackShell }
    private const float RgFarSeconds = 1.2f;
    private const float RgCheckFarSeconds = 0.6f;
    private const float RgStageNearSeconds = 0.3f;
    private const float RgCheckNearSeconds = 0.6f;
    private const float RgMissileSeconds = 6f;
    private const float RgTrackTimeoutSeconds = 20f;
    private const float RgPhaseTimeoutSeconds = 120f;
    private const float RgBurnoutMinAlong = 540f;
    private const float RgBurnoutMaxAlong = 700f;
    private float _rgT; private RangePhase _rgPhase;
    private float _rgHp0; private Projectile _rgShell; private Vector3 _rgOrigin, _rgDir;
    private float _rgLastAlong; private int _rgTry;
    private bool AimAxisAt(Vector3 p)
    {
        // Yaw is set by teleport (instant, exact - the walker's
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
        if (_rgPhase == RangePhase.FarLaser && _rgT > RgFarSeconds)
        {
            _camMode = CamMode.Chase;
            float d0 = _player.RootPos.DistanceTo(_enemy.RootPos);
            AimAxisAt(chest);
            _rgHp0 = _hpEnemy;
            FireLaser(TorsoAimPoint());
            Dbg.Print($"RANGE A fired: dist={d0:F0} enemyHp={_rgHp0:F0} (Large Laser range 600 - must NOT reach)");
            _rgPhase = RangePhase.CheckFar; _rgT = 0f;
        }
        else if (_rgPhase == RangePhase.CheckFar && _rgT > RgCheckFarSeconds)
        {
            if (_hpEnemy < _rgHp0 - 0.01f) { Dbg.Err($"RANGE FAIL A: laser damaged the enemy past its range ({_rgHp0:F0} -> {_hpEnemy:F0})"); GetTree().Quit(1); return; }
            Dbg.Print("RANGE A PASS: no damage beyond range");
            var home = PadWorld(0);
            var dirIn = (home - _enemy.RootPos); dirIn.Y = 0f; dirIn = dirIn.Normalized();
            var spot = _enemy.RootPos + dirIn * 600f;
            spot.Y = Main.HeightAt(spot.X, spot.Z);
            _player.Teleport(spot, RiderWalker.YawOf(-dirIn));
            _rgPhase = RangePhase.StageNear; _rgT = 0f;
        }
        else if (_rgPhase == RangePhase.StageNear && _rgT > RgStageNearSeconds)
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
                if (++_rgTry > 10) { Dbg.Err("RANGE FAIL B: no clear lane found for staging"); GetTree().Quit(1); }
                _rgT = 0f;
                return;
            }
            _rgHp0 = _hpEnemy;
            FireLaser(TorsoAimPoint());
            Dbg.Print($"RANGE B fired at 180u (lane try {_rgTry}), enemyHp={_rgHp0:F0} (must drop)");
            _rgPhase = RangePhase.CheckNear; _rgT = 0f;
        }
        else if (_rgPhase == RangePhase.CheckNear && _rgT > RgCheckNearSeconds)
        {
            if (_hpEnemy > _rgHp0 - 0.01f) { Dbg.Err("RANGE FAIL B: laser did no damage inside its range"); GetTree().Quit(1); return; }
            Dbg.Print($"RANGE B PASS: hp {_rgHp0:F0} -> {_hpEnemy:F0}");
            _rgHp0 = _hpEnemy;
            // Re-stage base-to-base (1074u) for the missile
            // clamp: the LRM's Sarna range is 1000, so from the
            // home pad the salvo must fall ~74u short of the
            // enemy base. No lane search - it just must not arrive.
            var homeC = PadWorld(0); homeC.Y = Main.HeightAt(homeC.X, homeC.Z);
            var baseC = PadWorld(1); baseC.Y = Main.HeightAt(baseC.X, baseC.Z);
            var dirC = (baseC - homeC); dirC.Y = 0f; dirC = dirC.Normalized();
            _player.Teleport(homeC, RiderWalker.YawOf(dirC));
            _enemy.Teleport(baseC, RiderWalker.YawOf(-dirC));
            AimAxisAt(_enemy.RootPos + Vector3.Up * 7.6f);
            FireLrmSalvo(TorsoAimPoint());
            Dbg.Print("RANGE C salvo fired base-to-base at 1074u (LRM range 1000 - must fall short)");
            _rgPhase = RangePhase.MissileCheck; _rgT = 0f;
        }
        else if (_rgPhase == RangePhase.MissileCheck && _rgT > RgMissileSeconds)
        {
            if (_hpEnemy < _rgHp0 - 0.01f) { Dbg.Err("RANGE FAIL C: rockets reached past their range"); GetTree().Quit(1); return; }
            if (_rocketsLaunched == 0) { Dbg.Err("RANGE FAIL C: no rockets launched"); GetTree().Quit(1); return; }
            Dbg.Print($"RANGE C PASS: {_rocketsLaunched} rockets, enemy untouched");
            _player.AimPitchWide = 0.16f; // climbs clear of the proven laser lane - only the range cap may end this flight
            UpdateCamera(0f);
            int before = _shots.Count;
            FireAutocannon(TorsoAimPoint());
            if (_shots.Count <= before) { Dbg.Err("RANGE FAIL D: no shell"); GetTree().Quit(1); return; }
            // The volley also fires the rider's machine guns
            // (85 u/s - an early run tracked one of those by
            // mistake and watched IT burn out at its own cap).
            // Track the fast shell: the autocannon's.
            _rgShell = null;
            for (int i = before; i < _shots.Count; i++)
                if (_shots[i].Vel.Length() > 200f) _rgShell = _shots[i];
            if (_rgShell == null) { Dbg.Err("RANGE FAIL D: no autocannon shell in the volley"); GetTree().Quit(1); return; }
            Dbg.Print($"RANGE D tracking the autocannon shell (speed {_rgShell.Vel.Length():F0})");
            _rgOrigin = _cam.GlobalPosition; _rgDir = _aimDir;
            _rgPhase = RangePhase.TrackShell; _rgT = 0f;
        }
        else if (_rgPhase == RangePhase.TrackShell)
        {
            // The shared step removes a dead shell the same frame
            // it dies, so track the last live along-axis reading
            // and judge the burnout from it.
            bool gone = _rgShell == null || _rgShell.Dead || !_shots.Contains(_rgShell);
            if (!gone)
            {
                _rgLastAlong = (_rgShell.GlobalPosition - _rgOrigin).Dot(_rgDir);
                if (_rgT > RgTrackTimeoutSeconds) { Dbg.Err("RANGE FAIL D: timeout"); GetTree().Quit(1); }
            }
            else
            {
                Dbg.Print($"RANGE D: shell end along={_rgLastAlong:F0}u (autocannon range 600)");
                if (_rgLastAlong < RgBurnoutMinAlong || _rgLastAlong > RgBurnoutMaxAlong) { Dbg.Err("RANGE FAIL D: burnout off the weapon's range"); GetTree().Quit(1); return; }
                Dbg.Print("RANGE TEST DONE");
                GetTree().Quit(0);
            }
        }
        if (_rgT > RgPhaseTimeoutSeconds && _rgPhase < RangePhase.TrackShell) { Dbg.Err("RANGE FAIL: phase timeout"); GetTree().Quit(1); }
    }


    // Thruster leg freeze test (AOT_TERRATEST=14, staged on
    // the open home plaza - no terrace hooks; it outlived the
    // terraced-city removal as its own probe because what it
    // verifies, no steps completing in flight, is generic
    // walker machinery): walk 1.5s, burn to empty, coast and
    // fall, on a 9s cycle. No step may complete from burn
    // start to touchdown, and the rider lands every cycle.
    private int _fzSteps0, _fzStepsFrozen; private float _fzMaxH, _fzMaxY; private bool _fzWasAir;
    private float _fzT; private bool _fzStaged;
    private const float FreezeBackOff = 25f;
    private const float FreezeCycleSeconds = 9f;
    private const float FreezeWalkSeconds = 1.5f;
    private const float FreezeBurnEndSeconds = 4.5f;
    private const float FreezeTotalSeconds = 27.5f;
    private const float FreezeMinJumpHeight = 3f;
    private const float FreezeMaxRootY = 55f;
    private const float FreezeSettleMaxY = 0.7f;

    private void UpdateFreezeTest(float dt)
    {
        if (!_fzStaged)
        {
            _fzStaged = true;
            _fzSteps0 = -1; _fzStepsFrozen = 0; _fzMaxH = 0f; _fzMaxY = 0f; _fzWasAir = false;
            var hp = Main.SpawnPads[0];
            _player.Teleport(new Vector3(hp.X, 0f, hp.Y - FreezeBackOff), 0f);
        }
        _fzT += dt;
        // Cycle timeline (9s each): walk 1.5s, burn to empty
        // (~2.2s), coast/fall the rest.
        float ct = _fzT % FreezeCycleSeconds;
        int cyc = (int)(_fzT / FreezeCycleSeconds);
        if (ct < FreezeWalkSeconds) { _player.SetDrive(1f, 0f); _player.SetThrust(false); }
        else if (ct < FreezeBurnEndSeconds) { _player.SetDrive(1f, 0f); _player.SetThrust(true); }
        else { _player.SetDrive(1f, 0f); _player.SetThrust(false); }
        bool air = _player.Airborne;
        if (air && !_fzWasAir && cyc == 0 && _fzSteps0 < 0) _fzSteps0 = _player.Steps;
        if (air && _fzSteps0 >= 0) _fzStepsFrozen = Mathf.Max(_fzStepsFrozen, _player.Steps - _fzSteps0);
        _fzWasAir = air;
        _fzMaxH = Mathf.Max(_fzMaxH, _player.JumpHeight);
        _fzMaxY = Mathf.Max(_fzMaxY, _player.RootPos.Y);
        if (_fzT > FreezeTotalSeconds)
        {
            Dbg.Print($"FREEZE stepsDuringFlight={_fzStepsFrozen} maxJumpH={_fzMaxH:F1} maxRootY={_fzMaxY:F1} settleY={_player.RootPos.Y:F1} settleSole={_player.SoleLevel:F1}");
            // Settled = on the ground HERE, not at an absolute
            // height: the original staging plaza was the flat
            // Harbor floor at 0; on the standard terrain the
            // rider ends wherever the cycles carried it, so the
            // settle term reads the ground under its feet.
            float groundY = Main.HeightAt(_player.RootPos.X, _player.RootPos.Z);
            if (_fzSteps0 >= 0 && _fzStepsFrozen == 0 && _fzMaxH > FreezeMinJumpHeight && _fzMaxY < FreezeMaxRootY && !_player.Airborne && _player.RootPos.Y <= groundY + FreezeSettleMaxY)
            { Dbg.Print("FREEZE PASS: no steps in flight, no altitude ratchet, lands every cycle"); GetTree().Quit(0); return; }
            Dbg.Err("FREEZE FAIL"); GetTree().Quit(1);
        }
    }

    private void UpdateMissionTest(float dt)
    {
        _mPhaseT += dt;
        if (_mPhase == MissionPhase.WorldAudit && _mPhaseT > MAuditSeconds)
        {
            Dbg.Print($"MISSION WORLD size={Main.TerrainSize} segs={Main.TerrainSegs} bound={WorldConfig.WorldBound}");
            for (int i = 0; i < Main.SpawnPads.Length; i++)
            {
                var pad = Main.SpawnPads[i];
                float hc = Main.HeightAt(pad.X, pad.Y);
                float dev = 0f;
                for (int k = 0; k < 16; k++)
                {
                    float a = k / 16f * Mathf.Tau;
                    dev = Mathf.Max(dev, Mathf.Abs(Main.HeightAt(pad.X + Mathf.Cos(a) * MBaseRingRadius, pad.Y + Mathf.Sin(a) * MBaseRingRadius) - hc));
                }
                Dbg.Print($"MISSION BASE {i} pad=({pad.X:F0},{pad.Y:F0}) h={hc:F2} maxDevR20={dev:F3}");
                if (dev > MBaseFlatTolerance) { Dbg.Err("MISSION FAIL: base plateau not flat"); GetTree().Quit(1); return; }
            }
            _mDistE0 = _enemy.RootPos.DistanceTo(_player.RootPos);
            _mDistF0 = _foe != null ? _foe.Walker.RootPos.DistanceTo(_player.RootPos) : 0f;
            _mEnemyStart = _enemy.RootPos; _mFoeStart = _foe?.Walker.RootPos ?? Vector3.Zero;
            Dbg.Print($"MISSION PATROL start: enemy distToPlayer={_mDistE0:F0} foe distToPlayer={_mDistF0:F0}");
            QueueCapture(ProofDir + "mission_base.png", CaptureTags.MBase, CamMode.MissionBase, () =>
            QueueCapture(ProofDir + "mission_vista.png", CaptureTags.MVista, CamMode.MissionVista, () =>
            QueueCapture(ProofDir + "gate_sky.png", CaptureTags.GSky, CamMode.Gatesky, () =>
            {
                // AOT_SLOPE=1: two extra staged portraits of the
                // center relief (the owner's streaked-mountains
                // screenshot), then quit - the patrol/aggro
                // phases below are not part of a portrait run.
                if (EnvVars.Flag(EnvVars.Slope))
                {
                    QueueCapture(ProofDir + "slope_vista.png", CaptureTags.SlopeVista, CamMode.SlopeVista, () =>
                    QueueCapture(ProofDir + "slope_close.png", CaptureTags.SlopeClose, CamMode.SlopeClose, () =>
                    { Dbg.Print("SLOPE SHOTS DONE"); GetTree().Quit(0); }));
                    return;
                }
                _mPhase = MissionPhase.Patrol; _mPhaseT = 0f;
            })));
            _mPhase = MissionPhase.CapturesPending;
        }
        else if (_mPhase == MissionPhase.Patrol)
        {
            // Patrol phase: the player holds at home, 1074u away.
            // Both defenders must MOVE (patrol) yet STAY at their
            // own bases - the old march assertion is inverted.
            _player.SetDrive(_playerYaw, 0f);
            UpdateEnemyAI(dt);                 // test branches drive the AI themselves (cf. FFA test)
            if (_foe != null) UpdateFoeAI(dt);
            // Ground-contact audit (owner, 2026-11-15: patrolling
            // riders' legs "don't touch the ground"): planted feet
            // must read toe AND heel at ~0; swing feet show their
            // clearance and the patrol speed for context.
            _mDumpT += dt;
            if (_mDumpT > MDumpIntervalSeconds)
            {
                _mDumpT = 0f;
                AuditPatrol("ENEMY", _enemy, _partsEnemy);
                if (_foe != null) AuditPatrol("FOE", _foe.Walker, _foe.Parts);
            }
            _mMaxDisp = Mathf.Max(_mMaxDisp, _enemy.RootPos.DistanceTo(_mEnemyStart));
            _mMaxHome = Mathf.Max(_mMaxHome, _enemy.RootPos.DistanceTo(PadWorld(1)));
            if (_foe != null) _mMaxHomeF = Mathf.Max(_mMaxHomeF, _foe.Walker.RootPos.DistanceTo(PadWorld(2)));
            if (_mPhaseT > MPatrolSeconds)
            {
                Dbg.Print($"MISSION PATROL end: enemy maxDisp={_mMaxDisp:F0}u maxDistFromHome={_mMaxHome:F0}u; foe maxDistFromHome={_mMaxHomeF:F0}u");
                if (_mMaxDisp < MPatrolMinDisplacement) { Dbg.Err("MISSION FAIL: enemy did not patrol"); GetTree().Quit(1); return; }
                if (_mMaxHome > MBaseLeashRadius || _mMaxHomeF > MBaseLeashRadius) { Dbg.Err("MISSION FAIL: a defender left its base"); GetTree().Quit(1); return; }
                // Aggro phase: put the player 190u from the enemy
                // base - inside the 260u aggro ring - and watch the
                // defender come out to fight.
                var home = PadWorld(1);
                var dirIn = (Vector3.Zero - home); dirIn.Y = 0f; dirIn = dirIn.Normalized();
                var spot = home + dirIn * MAggroTeleportDistance;
                spot.Y = Main.HeightAt(spot.X, spot.Z);
                _player.Teleport(spot, RiderWalker.YawOf(-dirIn));
                _mAggroD0 = _enemy.RootPos.DistanceTo(_player.RootPos);
                Dbg.Print($"MISSION AGGRO start: player teleported, enemy distToPlayer={_mAggroD0:F0}");
                _mPhase = MissionPhase.Aggro; _mPhaseT = 0f;
            }
        }
        else if (_mPhase == MissionPhase.Aggro)
        {
            _player.SetDrive(_playerYaw, 0f);
            UpdateEnemyAI(dt);
            if (_foe != null) UpdateFoeAI(dt);
            if (_mPhaseT > MAggroSeconds)
            {
                float dE = _enemy.RootPos.DistanceTo(_player.RootPos);
                Dbg.Print($"MISSION AGGRO end: enemy distToPlayer {_mAggroD0:F0}->{dE:F0}");
                if (dE > _mAggroD0 - MAggroMinApproach) { Dbg.Err("MISSION FAIL: defender did not engage the player"); GetTree().Quit(1); return; }
                Dbg.Print("MISSION TEST DONE");
                GetTree().Quit(0);
            }
        }
        if (_mPhaseT > MTimeoutSeconds && _mPhase != MissionPhase.CapturesPending) { Dbg.Err("MISSION FAIL: timeout"); GetTree().Quit(1); }
    }

    private void AuditPatrol(string tag, RiderWalker w, RiderParts parts)
    {
        var joints = w.DebugLegJoints();
        var fwdW = RiderWalker.HeadingVec(w.LegsYaw);
        for (int i = 0; i < 2; i++)
        {
            PartId partId = i == 0 ? PartId.LegL : PartId.LegR;
            string side = i == 0 ? "L" : "R";
            if (joints[i].Item5)
            {
                var pg = parts.FootPitchGaps(partId, joints[i].Item4, fwdW);
                Dbg.Print($"PATROL {tag} {side} PLANTED toe={pg.Toe:F3} heel={pg.Heel:F3} soleDiff={w.MaxPlantedDiff():F3} speed={w.Speed:F2}");
            }
            else
            {
                float gap = parts.FootVisualGap(partId);
                Dbg.Print($"PATROL {tag} {side} SWING clearance={gap:F3} stepT={joints[i].Item6:F2} speed={w.Speed:F2}");
            }
        }
    }

    private enum TerrainPhase { Vista, SlopeClimb, DuelFrame, Run, Fly, CapturesPending = 99 }
    private enum FlyStage { Settle, FullBurn, Descend, PartialRecharge, PartialBurn, FullRecharge }

    private const int TPhaseTimeoutSeconds = 45;
    private const float TVistaCaptureSeconds = 0.8f;
    private const float TSlopeCaptureSeconds = 0.8f;
    private const float TSlopeDriveSpeed = 2.2f;
    private const float TSlopeEnemyPosZ = -16f;
    private const float TPlantedDiffMax = 0.08f;
    private const float TDuelAheadDistance = 30f;
    private const float TDuelClamp = 60f;
    private const float TDuelSettleSeconds = 1.2f;
    private const float TSpawnZ = 34f;
    private const float TRunReadyFraction = 0.95f;
    private const float TRunMarkTimeoutSeconds = 5.5f;
    private const float TRunSegmentSeconds = 2.2f;
    private const float TRunMinSpeed = 11.4f;
    private const float TRunMaxSplit = 10f;
    private const double TFlyPhaseTimeoutSeconds = 26.0;
    private const float TFlySettleSeconds = 0.6f;
    private const float TFlyUnfittedLiftMax = 0.05f;
    private const float TFlyParkHeight = 12f;
    private const double TFlyParkDelaySeconds = 0.3;
    private const float TFlyLateralTolerance = 0.05f;
    private const float TFlyParkPos = 60f;
    private const float TFlyParkMinX = -50f;
    private const float TFlyDescendSeconds = 3.4f;
    private const float TFlyMinApex = 14f;
    private const float TFlyMinBurnSpeed = 6.5f;
    private const float TFlyMinBurnDistance = 8.5f;
    private const float TFlyPartialCharge = 0.45f;
    private const float TFlyPartialSeenMin = 0.5f;
    private const float TFlyPartialMinApex = 3f;
    private const float TFlyPartialMargin = 4f;
    private const float TFlyFullCharge = 0.999f;
    private const float GrassSettleSeconds = 2.0f;
    private const int GrassMinInstances = 100;
    private const float PhotoFpsWarmupSeconds = 0.5f;
    private const float PhotoSettleSeconds = 2.5f;
    private const float GaitDumpStartSeconds = 0.6f;
    private const float GaitDumpIntervalSeconds = 0.25f;
    private const float GaitCaptureSeconds = 1.9f;

    private void UpdateTerrainTest(float dt)
    {
        _tPhaseT += dt;
        if (_tPhaseT > TPhaseTimeoutSeconds) { TFail($"phase {(int)_tPhase} timeout"); return; }
        switch (_tPhase)
        {
            case TerrainPhase.Vista: // vista over the whole valley, then move to the slope
                _player.SetDrive(0f, 0f);
                _enemy.SetDrive(Mathf.Pi, 0f);
                if (_tPhaseT > TVistaCaptureSeconds)
                {
                    QueueCapture(ProofDir + "env_detail_building.png", CaptureTags.TDetB, CamMode.DetailBuilding, () =>
                    QueueCapture(ProofDir + "env_detail_tree.png", CaptureTags.TDetT, CamMode.DetailTree, () =>
                    QueueCapture(ProofDir + "terrain_vista.png", CaptureTags.TVista, CamMode.Vista, () =>
                    {
                        if (!FindSlopeSpot(out var spot, out float yaw))
                        {
                            TFail("no slope spot found in the arena");
                            return;
                        }
                        _tSlopeYaw = yaw;
                        _player.Teleport(spot, yaw);
                        _enemy.Teleport(new Vector3(0f, Main.HeightAt(0f, TSlopeEnemyPosZ), TSlopeEnemyPosZ), Mathf.Pi);
                        Dbg.Print($"TERRAIN SLOPE spot={spot} grade={TerrainGrade(spot.X, spot.Z):F3} uphillYaw={Mathf.RadToDeg(yaw):F1}deg");
                        _tPhase = TerrainPhase.SlopeClimb; _tPhaseT = 0; _tStrideLogged = false;
                    })));
                    _tPhase = TerrainPhase.CapturesPending; // wait for the capture callback
                }
                break;
            case TerrainPhase.SlopeClimb: // climb the slope; capture mid-stride, check contact
                _player.SetDrive(_tSlopeYaw, TSlopeDriveSpeed);
                _enemy.SetDrive(Mathf.Pi, 0f);
                if (_tPhaseT > TSlopeCaptureSeconds && _player.ConsumeStepMidCross() && !_tStrideLogged)
                {
                    _tStrideLogged = true;
                    Dbg.Print($"TERRAIN STRIDE analytic {_player.FootReport(false)}");
                    Dbg.Print($"TERRAIN STRIDE engine   {_player.FootReport(true)}");
                    QueueCapture(ProofDir + "terrain_stride.png", CaptureTags.TStride, CamMode.SlopeSide, () =>
                    {
                        float md = _player.MaxPlantedDiff();
                        Dbg.Print($"TERRAIN STRIDE maxPlantedDiff={md:F3}");
                        if (md > TPlantedDiffMax) { TFail($"planted diff {md:F3} > 0.08 on arena slope"); return; }
                        // Ground-level duel shot: Hector 30u ahead of the player.
                        var fwd = RiderWalker.HeadingVec(_player.LegsYaw);
                        var epos = _player.RootPos + fwd * TDuelAheadDistance;
                        epos.X = Mathf.Clamp(epos.X, -TDuelClamp, TDuelClamp);
                        epos.Z = Mathf.Clamp(epos.Z, -TDuelClamp, TDuelClamp);
                        epos.Y = Main.HeightAt(epos.X, epos.Z);
                        _enemy.Teleport(epos, RiderWalker.YawOf(-fwd));
                        _player.SetDrive(_player.LegsYaw, 0f);
                        _tPhase = TerrainPhase.DuelFrame; _tPhaseT = 0;
                    });
                }
                break;
            case TerrainPhase.DuelFrame: // settle, then the ground-level duel frame + render info
                _player.SetDrive(_player.LegsYaw, 0f);
                _enemy.SetDrive(_enemy.LegsYaw, 0f);
                if (_tPhaseT > TDuelSettleSeconds)
                {
                    QueueCapture(ProofDir + "terrain_duel.png", CaptureTags.TDuel, CamMode.GroundDuel, () =>
                    {
                        var vp = GetViewport();
                        Dbg.Print($"TERRAIN RENDER drawCalls={vp.GetRenderInfo(Viewport.RenderInfoType.Visible, Viewport.RenderInfo.DrawCallsInFrame)} prims={vp.GetRenderInfo(Viewport.RenderInfoType.Visible, Viewport.RenderInfo.PrimitivesInFrame)} objects={vp.GetRenderInfo(Viewport.RenderInfoType.Visible, Viewport.RenderInfo.ObjectsInFrame)}");
                        // RUN phase setup: back to the flat spawn, facing -Z.
                        _player.Teleport(new Vector3(0f, Main.HeightAt(0f, TSpawnZ), TSpawnZ), 0f);
                        _tRunFrom = _player.RootPos;
                        _tPhase = TerrainPhase.Run; _tPhaseT = 0;
                    });
                    _tPhase = TerrainPhase.CapturesPending;
                }
                break;
            case TerrainPhase.Run: // RUN: full run-mode speed (walk x RunFactor) - gait must hold.
                    // Speed is judged on the steady-state segment (after the
                    // 4.2 u/s^2 acceleration ramp), not the whole window.
                _player.SetDrive(0f, _playerSpec.MaxSpeed * RunFactor);
                _enemy.SetDrive(_enemy.LegsYaw, 0f);
                // Steady segment starts once the rider is actually at
                // run speed (spec Accel varies 2.4-4.2 across the roster;
                // a fixed 2.0s mark measured the acceleration ramp of the
                // slow-Accel riders and failed them on speed alone).
                if (_tRunMark < 0f && (_player.Speed >= TRunReadyFraction * _playerSpec.MaxSpeed * RunFactor || _tPhaseT > TRunMarkTimeoutSeconds)) { _tRunFrom = _player.RootPos; _tRunMark = (float)_tPhaseT; }
                if (_tRunMark >= 0f) // stance audit: how far apart do the feet get at run speed?
                {
                    var j = _player.DebugLegJoints();
                    var fwdR = new Vector3(Mathf.Sin(_player.LegsYaw), 0f, Mathf.Cos(_player.LegsYaw));
                    var rgtR = new Vector3(fwdR.Z, 0f, -fwdR.X);
                    var rel = j[1].Item4 - j[0].Item4;
                    _tRunMaxSplit = Mathf.Max(_tRunMaxSplit, Mathf.Abs(rel.Dot(fwdR)));
                    _tRunMaxLat = Mathf.Max(_tRunMaxLat, Mathf.Abs(rel.Dot(rgtR)));
                }
                if (_tRunMark >= 0f && _tPhaseT - _tRunMark > TRunSegmentSeconds)
                {
                    var d = new Vector2(_player.RootPos.X - _tRunFrom.X, _player.RootPos.Z - _tRunFrom.Z).Length();
                    float avg = d / ((float)_tPhaseT - _tRunMark);
                    Dbg.Print($"RUNTEST steadySpeed={avg:F2} (cmd {_playerSpec.MaxSpeed * RunFactor:F1}) segDist={d:F1} maxSlide={_player.MaxSlide:F4} maxPlantedDiff={_player.MaxPlantedDiff():F3} maxAnkleSplit={_tRunMaxSplit:F2} maxAnkleLat={_tRunMaxLat:F2}");
                    if (_tRunMaxSplit > TRunMaxSplit) { TFail($"run ankle split {_tRunMaxSplit:F2} > 10 (legs spread)"); return; }
                    if (avg < TRunMinSpeed) { TFail($"run steady speed {avg:F2} < 11.4"); return; }
                    if (_player.MaxPlantedDiff() > TPlantedDiffMax) { TFail($"run planted diff {_player.MaxPlantedDiff():F3} > 0.08"); return; }
                    Dbg.Print("RUNTEST PASS run-mode speed reached, gait contact holds");
                    // FLY setup: flat spawn, jets fitted, tanks full.
                    _player.Teleport(new Vector3(0f, Main.HeightAt(0f, TSpawnZ), TSpawnZ), 0f);
                    _player.JumpJets = true;
                    _player.JumpCharge = 1f;
                    _playerYaw = 0f;
                    _tJumpApex = 0f; _tApex2 = 0f; _tJumpTried = false; _tJumpStage = FlyStage.Settle; _tParked = false;
                    _tPhase = TerrainPhase.Fly; _tPhaseT = 0;
                }
                break;
            case TerrainPhase.Fly: // FLY: hold-to-thrust - burn, flyover, descent, partial burn, recharge.
                if (_tJumpStage == FlyStage.FullBurn) ApplyPlayerInput(0.016f, true, false, false, false, false); // W held: drive + jet push
                else _player.SetDrive(0f, 0f);
                _enemy.SetDrive(_enemy.LegsYaw, 0f);
                if (_tJumpStage >= FlyStage.FullBurn && _tJumpStage <= FlyStage.Descend)
                {
                    float hgtNow = _player.RootPos.Y - _tJumpGround;
                    if (hgtNow > _tJumpApex) _tJumpApex = hgtNow;
                }
                if (_tJumpStage == FlyStage.PartialBurn)
                {
                    float hgtNow = _player.RootPos.Y - _tJumpGround;
                    if (hgtNow > _tApex2) _tApex2 = hgtNow;
                }
                if (_tPhaseT > TFlyPhaseTimeoutSeconds) { TFail($"FLYTEST timeout in stage {(int)_tJumpStage} (charge {_player.JumpCharge:F2}, h {_player.JumpHeight:F1})"); return; }
                switch (_tJumpStage)
                {
                    case FlyStage.Settle: // settle; the unfitted Hector must not lift even with thrust commanded
                        _enemy.SetThrust(true);
                        if (_tPhaseT > TFlySettleSeconds)
                        {
                            if (_enemy.JumpHeight > TFlyUnfittedLiftMax) { TFail("Hector lifted without jets"); return; }
                            _enemy.SetThrust(false);
                            _tJumpGround = _player.RootPos.Y;
                            _tFlyStart = new Vector2(_player.RootPos.X, _player.RootPos.Z);
                            _player.SetThrust(true);
                            _tJumpStage = FlyStage.FullBurn;
                        }
                        break;
                    case FlyStage.FullBurn: // full burn, W held - climb past enemy height while the jets push forward
                        if (!_tParked && _player.JumpHeight > TFlyParkHeight)
                        {
                            _tJumpXZ = new Vector2(_player.RootPos.X, _player.RootPos.Z);
                            _enemy.Teleport(new Vector3(_tJumpXZ.X, Main.HeightAt(_tJumpXZ.X, _tJumpXZ.Y), _tJumpXZ.Y), 0f);
                            _tJumpMarkT = (float)_tPhaseT; _tParked = true;
                        }
                        if (_tParked && _tPhaseT > _tJumpMarkT + TFlyParkDelaySeconds && _enemy.RootPos.X > TFlyParkMinX)
                        {
                            var nowXZ = new Vector2(_player.RootPos.X, _player.RootPos.Z);
                            // forward flight moves the rider; the flyover check is LATERAL push only
                            if (Mathf.Abs(nowXZ.X - _tJumpXZ.X) > TFlyLateralTolerance) { TFail("flyover pushed the flying rider sideways"); return; }
                            _enemy.Teleport(new Vector3(TFlyParkPos, Main.HeightAt(TFlyParkPos, TFlyParkPos), TFlyParkPos), 0f);
                        }
                        if (_player.JumpCharge <= 0f)
                        {
                            _tBurnDist = new Vector2(_player.RootPos.X - _tFlyStart.X, _player.RootPos.Z - _tFlyStart.Y).Length();
                            _tBurnSpeed = _player.Speed;
                            _player.SetThrust(false);
                            _tJumpStage = FlyStage.Descend;
                        }
                        break;
                    case FlyStage.Descend: // tanks empty: gravity takes it back down
                        if (_player.JumpHeight <= 0f && _tPhaseT > TFlyDescendSeconds)
                        {
                            Dbg.Print($"FLYTEST burn apex={_tJumpApex:F1} burnDist={_tBurnDist:F1} burnEndSpeed={_tBurnSpeed:F1}");
                            if (_tJumpApex < TFlyMinApex) { TFail($"full-burn apex {_tJumpApex:F1} < 14 - cannot clear an enemy"); return; }
                            // The push is judged on SPEED at burn end:
                            // walk alone caps at 4.2; jets+W must read
                            // ~7.2 (the accel ramp eats the distance
                            // signal: 9.1u with push vs ~7.1 without).
                            if (_tBurnSpeed < TFlyMinBurnSpeed) { TFail($"burn-end speed {_tBurnSpeed:F1} < 6.5 - thrusters did not push the rider forward"); return; }
                            if (_tBurnDist < TFlyMinBurnDistance) { TFail($"burn travel {_tBurnDist:F1} < 8.5"); return; }
                            _tJumpStage = FlyStage.PartialRecharge;
                        }
                        break;
                    case FlyStage.PartialRecharge: // partial recharge -> a shorter, lower flight
                        if (_player.JumpCharge >= TFlyPartialCharge) { _player.SetThrust(true); _tJumpStage = FlyStage.PartialBurn; }
                        break;
                    case FlyStage.PartialBurn: // partial burn spent -> descend and land
                        if (_tApex2 > TFlyPartialSeenMin && _player.JumpHeight <= 0f)
                        {
                            _player.SetThrust(false);
                            Dbg.Print($"FLYTEST partial apex={_tApex2:F1} (full {_tJumpApex:F1})");
                            if (_tApex2 < TFlyPartialMinApex) { TFail($"partial-charge flight too low ({_tApex2:F1})"); return; }
                            if (_tApex2 > _tJumpApex - TFlyPartialMargin) { TFail($"partial flight ({_tApex2:F1}) not lower than full ({_tJumpApex:F1})"); return; }
                            _tJumpStage = FlyStage.FullRecharge;
                        }
                        break;
                    case FlyStage.FullRecharge: // full recharge on the ground completes the contract
                        if (_player.JumpCharge >= TFlyFullCharge)
                        {
                            Dbg.Print("FLYTEST PASS hold-to-fly: full burn clears enemy height with forward push and no flyover shove, empty tanks descend, partial charge flies lower, ground recharge refills");
                            Dbg.Print("TERRAINTEST DONE");
                            GetTree().Quit(0);
                            _tPhase = TerrainPhase.CapturesPending;
                        }
                        break;
                }
                break;
        }
    }

    // ---------- captures / camera ----------

    // Photoreal probe driver: settle, capture the overview and the
    // ground frame, report draw calls / primitives / fps (fps on
    // this GPU-less VM is llvmpipe software rendering - the number
    // that matters is measured on the owner's laptop).
    // TEMP probe (square-grass report): census the hero-clump
    // MultiMesh - which sub-mesh it carries, its AABB and tri
    // count - then capture one instance close, from directly
    // above, and the field from a chase-like elevation.
    private void UpdateGrassShot(float dt)
    {
        _photoT += dt;
        if (_grassStaged || _photoT < GrassSettleSeconds) return;
        _grassStaged = true;
        MultiMeshInstance3D found = null;
        var stack = new System.Collections.Generic.Stack<Node>();
        stack.Push(this);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n is MultiMeshInstance3D mm && mm.Multimesh != null
                && mm.Multimesh.InstanceCount >= GrassMinInstances) { found = mm; break; }
            foreach (var c in n.GetChildren()) stack.Push(c);
        }
        if (found == null) { Dbg.Err("GRASSSHOT FAIL: no clump multimesh found"); GetTree().Quit(1); return; }
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
        Dbg.Print($"GRASSSHOT instances={m.InstanceCount} meshAabb={m.Mesh.GetAabb()} trisPerInstance={tris} surfaces={(am != null ? am.GetSurfaceCount() : -1)}");
        var t0 = m.GetInstanceTransform(0);
        var t5 = m.GetInstanceTransform(5);
        Dbg.Print($"GRASSSHOT inst0 pos={t0.Origin} scale={t0.Basis.Scale} inst5 pos={t5.Origin} scale={t5.Basis.Scale}");
        _grassPos = t5.Origin;
        QueueCapture(ProofDir + "grass_close.png", CaptureTags.GClose, CamMode.GrassClose, () =>
        QueueCapture(ProofDir + "grass_top.png", CaptureTags.GTop, CamMode.GrassTop, () =>
        QueueCapture(ProofDir + "grass_field.png", CaptureTags.GField, CamMode.GrassField, () =>
        {
            Dbg.Print("GRASSSHOT DONE");
            GetTree().Quit(0);
        })));
    }

    private enum PhotoPhase { Settle, Capture }

    private void UpdatePhotoTest(float dt)
    {
        _photoT += dt;
        if (_photoT > PhotoFpsWarmupSeconds) { _fpsSum += (float)Engine.GetFramesPerSecond(); _fpsN++; }
        if (_photoPhase == PhotoPhase.Settle && _photoT > PhotoSettleSeconds)
        {
            _photoPhase = PhotoPhase.Capture;
            QueueCapture(ProofDir + "photo_tuft.png", CaptureTags.PTuft, CamMode.PhotoTuft, () =>
            QueueCapture(ProofDir + "photo_overview.png", CaptureTags.POver, CamMode.PhotoOver, () =>
                QueueCapture(ProofDir + "photo_ground.png", CaptureTags.PGround, CamMode.PhotoGround, () =>
                {
                    long draws = (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame);
                    long prims = (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame);
                    Dbg.Print($"PHOTO STATS drawCalls={draws} primitives={prims} avgFps={(_fpsN > 0 ? _fpsSum / _fpsN : 0f):F1} (llvmpipe - not representative)");
                    Dbg.Print("PHOTO DONE");
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
        if (_gaitT > GaitDumpStartSeconds && _gaitDumpT > GaitDumpIntervalSeconds)
        {
            _gaitDumpT = 0f;
            var fwd = new Vector3(Mathf.Sin(_enemy.LegsYaw), 0f, Mathf.Cos(_enemy.LegsYaw));
            var right = new Vector3(fwd.Z, 0f, -fwd.X);
            foreach (var (legName, hip, knee, ankle, planted, stepT) in _enemy.DebugLegJoints())
            {
                float Lat(Vector3 p) => (p - _enemy.RootPos).Dot(right);
                Dbg.Print($"GAITDBG t={_gaitT:F2} {legName}: hipLat={Lat(hip):F3} kneeLat={Lat(knee):F3} ankleLat={Lat(ankle):F3} planted={planted} stepT={stepT:F2}");
            }
            // Ground-contact audit (owner, 2026-11-15: "legs don't
            // touch the ground, gap between leg and shadow"): the
            // walker's sole-point error next to the VISUAL foot
            // bottom (lowest skinned foot vertex vs the terrain
            // under it), so a solver error and a bind/geometry
            // mismatch read as different numbers.
            Dbg.Print($"GAITDBG t={_gaitT:F2} CONTACT soleDiffMax={_enemy.MaxPlantedDiff():F3} " +
                $"footGapL={_partsEnemy.FootVisualGap(PartId.LegL):F3} footGapR={_partsEnemy.FootVisualGap(PartId.LegR):F3}");
            var joints = _enemy.DebugLegJoints();
            var fwdW = RiderWalker.HeadingVec(_enemy.LegsYaw);
            for (int i = 0; i < 2; i++)
            {
                if (!joints[i].Item5) continue; // planted legs only
                var pg = _partsEnemy.FootPitchGaps(i == 0 ? PartId.LegL : PartId.LegR, joints[i].Item4, fwdW);
                Dbg.Print($"GAITDBG t={_gaitT:F2} PITCH {(i == 0 ? "L" : "R")}: toeGap={pg.Toe:F3} heelGap={pg.Heel:F3}");
            }
        }
        if (_gaitT > GaitCaptureSeconds && _camMode != CamMode.EnemySide && _camMode != CamMode.EnemyFront)
            QueueCapture(ProofDir + "gait_hector.png", CaptureTags.Gait, CamMode.EnemySide, () =>
                QueueCapture(ProofDir + "gait_hector_front.png", CaptureTags.GaitF, CamMode.EnemyFront, () => GetTree().Quit()));
    }

    // ---------- SFX pool probe ----------
    // Owner (2026-11-23): the laser sound sometimes disappears
    // under quick intermittent fire. The pool was round-robin:
    // after 10 further PlaySfx calls (steps, hits, NPC fire -
    // all one pool) the wrap reassigned the laser's player
    // mid-clip. Stage it: fire the laser, then burst 12 more
    // sounds; the laser's player must still hold the laser
    // stream (stream identity is the steal evidence - it is
    // exact even on the headless dummy audio driver).
    private enum SfxPhase { LaserBurst, CannonBurst, HitBurst }
    private const double SfxLaserSeconds = 1.0;
    private const double SfxStepSeconds = 0.3;
    private const int SfxLaserBurstCount = 12;
    private const int SfxBurstCount = 24;
    private SfxPhase _sfxPhase;
    private double _sfxT;

    private void UpdateSfxProbe(float dt)
    {
        _sfxT += dt;
        if (_sfxPhase == SfxPhase.LaserBurst && _sfxT > SfxLaserSeconds)
        {
            PlaySfx(_sfxLaser, _player.RootPos);
            for (int i = 0; i < SfxLaserBurstCount; i++)
                PlaySfx(i % 2 == 0 ? _sfxStepA : _sfxHit, _player.RootPos);
            _sfxPhase = SfxPhase.CannonBurst; _sfxT = 0;
        }
        else if (_sfxPhase == SfxPhase.CannonBurst && _sfxT > SfxStepSeconds)
        {
            int holders = 0; bool playing = false;
            foreach (var p in _sfxPool)
                if (p.Stream == _sfxLaser) { holders++; playing |= p.Playing; }
            Dbg.Print($"SFXPROBE laser stream holders={holders} anyPlaying={playing} pool={_sfxPool.Count}");
            if (holders != 1) { Dbg.Print("SFXPROBE FAIL laser"); GetTree().Quit(1); return; }
            // Phase 2 (2026-11-24): the cannon's turn - its
            // clip runs 1.92s, and a 24-sound expendable
            // burst (more than the whole pool) must not take
            // its player: protected voices are never stolen
            // while an expendable one is playing.
            PlaySfx(_sfxCannon, _player.RootPos);
            for (int i = 0; i < SfxBurstCount; i++)
                PlaySfx(i % 2 == 0 ? _sfxStepA : _sfxHit, _player.RootPos);
            int cHolders = 0;
            foreach (var p in _sfxPool)
                if (p.Stream == _sfxCannon) cHolders++;
            Dbg.Print($"SFXPROBE cannon stream holders={cHolders}");
            if (cHolders != 1) { Dbg.Print("SFXPROBE FAIL cannon"); GetTree().Quit(1); return; }
            _sfxPhase = SfxPhase.HitBurst; _sfxT = 0;
        }
        else if (_sfxPhase == SfxPhase.HitBurst && _sfxT > SfxStepSeconds)
        {
            // Phase 3 (2026-11-24): the hit's turn - his
            // explosion recording (1.5s) was the one fight
            // sound left expendable, and salvo traffic kept
            // cutting it to a 'plastic bottle' pop. Reset the
            // pool, stage one hit, burst 24 expendable steps:
            // the hit's player must survive.
            foreach (var p in _sfxPool) { p.Stop(); p.Stream = null; }
            for (int i = 0; i < _sfxProtected.Count; i++) _sfxProtected[i] = false;
            PlaySfx(_sfxHit, _player.RootPos);
            for (int i = 0; i < SfxBurstCount; i++)
                PlaySfx(_sfxStepA, _player.RootPos);
            int hHolders = 0;
            foreach (var p in _sfxPool)
                if (p.Stream == _sfxHit) hHolders++;
            Dbg.Print($"SFXPROBE hit stream holders={hHolders}");
            Dbg.Print(hHolders == 1 ? "SFXPROBE PASS" : "SFXPROBE FAIL hit");
            GetTree().Quit(hHolders == 1 ? 0 : 1);
        }
    }

    // ---------- Target-pick probe ----------
    // Owner (2026-11-23): TARGET RANGE always read ~1.49 km
    // on Babylon. Stage the mission spawn: the HUD's first
    // pick must be a NEAR foe, and after it dies the pick
    // must move to the next nearest - never snap back to
    // the far anchor seat.
    private enum TargetPhase { FirstPick, AfterKill }
    private const double TgtFirstPickSeconds = 2.5;
    private const double TgtAfterKillSeconds = 2.0;
    private const float TgtNearRangeMax = 1000f;
    private const float TgtRangeSlack = 1f;
    private TargetPhase _tgtPhase;
    private double _tgtT;
    private int _tgtFirstIdx = -2;
    private float _tgtFirstRange;

    private (int Idx, float Range, string Name) ActiveTargetReadout()
    {
        var foes = OpposingTargets();
        if (_targetIdx < 0 || _targetIdx >= foes.Count) return (-1, -1f, "?");
        var t = foes[_targetIdx];
        float r = t.W != null
            ? new Vector2(t.W.RootPos.X - _player.RootPos.X, t.W.RootPos.Z - _player.RootPos.Z).Length()
            : -1f;
        return (_targetIdx, r, t.Spec != null ? t.Spec.Name : "?");
    }

    private void KillTarget(int idx)
    {
        var foes = OpposingTargets();
        var t = foes[idx];
        if (idx == 0) { _hpEnemy = 0f; _enemyDead = true; return; }
        if (_foe != null && t.W == _foe.Walker) { _foe.Hp = 0f; _foe.Dead = true; return; }
        foreach (var a in _allies)
            if (a.Walker == t.W) { a.Hp = 0f; a.Dead = true; return; }
    }

    private void UpdateTargetProbe(float dt)
    {
        _tgtT += dt;
        if (_tgtPhase == TargetPhase.FirstPick && _tgtT > TgtFirstPickSeconds)
        {
            var (idx, range, name) = ActiveTargetReadout();
            Dbg.Print($"TGTPROBE first pick idx={idx} name={name} range={range:F0}");
            Dbg.Print($"TGTPROBE labels [{_lblRange.Text}] [{_lblReticle.Text}]");
            _tgtFirstIdx = idx; _tgtFirstRange = range;
            if (idx >= 0) KillTarget(idx);
            _tgtPhase = TargetPhase.AfterKill; _tgtT = 0;
        }
        else if (_tgtPhase == TargetPhase.AfterKill && _tgtT > TgtAfterKillSeconds)
        {
            var (idx, range, name) = ActiveTargetReadout();
            Dbg.Print($"TGTPROBE after kill idx={idx} name={name} range={range:F0}");
            bool ok = _tgtFirstIdx >= 0 && _tgtFirstRange > 0f && _tgtFirstRange < TgtNearRangeMax
                      && idx != _tgtFirstIdx && range > 0f && range < TgtNearRangeMax && range >= _tgtFirstRange - TgtRangeSlack;
            Dbg.Print(ok ? "TGTPROBE PASS" : "TGTPROBE FAIL");
            GetTree().Quit(ok ? 0 : 1);
        }
    }


    // ---------- Feed probe ----------
    // Owner (2026-11-24): after his whole wing was destroyed
    // an order produced no feed line, and after an R reset
    // orders never announced again. Stage both: reset first
    // (the old ResetDuel hid the feed label permanently),
    // order ATTACK MY TARGET; then kill the whole wing and
    // order ATTACK CLOSEST ENEMY. Both lines must be in the
    // feed with the label visible.
    private enum FeedPhase { ResetOrder, CheckReset, CheckWing }
    private const double FpResetSeconds = 2.5;
    private const double FpCheckSeconds = 0.6;
    private FeedPhase _fpPhase;
    private double _fpT;

    private bool FeedHas(string frag)
    {
        foreach (var e in _feed) if (e.Text.Contains(frag)) return true;
        return false;
    }

    private void UpdateFeedProbe(float dt)
    {
        _fpT += dt;
        if (_fpPhase == FeedPhase.ResetOrder && _fpT > FpResetSeconds)
        {
            ResetDuel();
            SendKey(Key.Key2, shift: true);
            _fpPhase = FeedPhase.CheckReset; _fpT = 0;
        }
        else if (_fpPhase == FeedPhase.CheckReset && _fpT > FpCheckSeconds)
        {
            Dbg.Print($"FEEDPROBE after reset: order={_teamOrder} feedAttack={FeedHas("ATTACK MY TARGET")} labelVisible={(_feedLabel != null && _feedLabel.Visible)} labelText=[{(_feedLabel != null ? _feedLabel.Text : "null")}]");
            if (_teamOrder != TeamOrder.AttackTarget || !FeedHas("ATTACK MY TARGET") || _feedLabel == null || !_feedLabel.Visible)
            { Dbg.Print("FEEDPROBE FAIL reset"); GetTree().Quit(1); return; }
            foreach (var a in _allies) if (a.Team == 0 && !a.Guard) { a.Hp = 0f; a.Dead = true; }
            SendKey(Key.Key3, shift: true);
            _fpPhase = FeedPhase.CheckWing; _fpT = 0;
        }
        else if (_fpPhase == FeedPhase.CheckWing && _fpT > FpCheckSeconds)
        {
            Dbg.Print($"FEEDPROBE wing dead: order={_teamOrder} feedClosest={FeedHas("ATTACK CLOSEST ENEMY")} labelVisible={(_feedLabel != null && _feedLabel.Visible)}");
            bool ok = _teamOrder == TeamOrder.AttackNearby && FeedHas("ATTACK CLOSEST ENEMY") && _feedLabel != null && _feedLabel.Visible;
            Dbg.Print(ok ? "FEEDPROBE PASS" : "FEEDPROBE FAIL wing");
            GetTree().Quit(ok ? 0 : 1);
        }
    }


    // ---------- Ambient probe ----------
    // Owner (2026-11-24): the wind and servo beds. The
    // harness replaces the live step, so the probe steps
    // StepAmbient itself: at rest the wind must sit at its
    // field level and the servo at the floor; with the
    // player at full walk speed (fed into _playerVel, the
    // field the live step maintains) the servo must rise.
    private enum AmbientPhase { Rest, Walking }
    private const float AmbStepSeconds = 0.05f;
    private const double AmbRestSeconds = 5.0;
    private const double AmbWalkSeconds = 2.0;
    private const float AmbWalkSpeedZ = 4.2f;
    private const float AmbWindDbMin = -41f;
    private const float AmbWindDbMax = -35f;
    private const float AmbRestServoMaxDb = -50f;
    private const float AmbWalkServoMinDb = -25f;
    private AmbientPhase _ambPhase;
    private double _ambT;
    private float _ambRestServo;

    private void UpdateAmbientProbe(float dt)
    {
        _ambT += dt;
        StepAmbient(AmbStepSeconds);
        if (_ambPhase == AmbientPhase.Rest && _ambT > AmbRestSeconds)
        {
            _ambRestServo = _servoPlayer.VolumeDb;
            Dbg.Print($"AMBPROBE rest: windPlaying={_windPlayer.Playing} windDb={_windPlayer.VolumeDb:F1} servoPlaying={_servoPlayer.Playing} servoDb={_ambRestServo:F1}");
            _ambPhase = AmbientPhase.Walking; _ambT = 0;
        }
        else if (_ambPhase == AmbientPhase.Walking)
        {
            _playerVel = new Vector3(0f, 0f, AmbWalkSpeedZ);
            if (_ambT > AmbWalkSeconds)
            {
                Dbg.Print($"AMBPROBE walking: servoDb={_servoPlayer.VolumeDb:F1} pitch={_servoPlayer.PitchScale:F2}");
                // Playing itself is not asserted: on this
                // VM's headless dummy audio driver it reads
                // False even for the pool voices that are
                // audibly fine on the owner's build (the sfx
                // probe showed the same). Stream assignment
                // and the gain/pitch mapping are the parts
                // observable here; audibility is his playtest.
                bool ok = _windPlayer.Stream == _sfxWind && _servoPlayer.Stream == _sfxServo
                          && _windPlayer.VolumeDb > AmbWindDbMin && _windPlayer.VolumeDb < AmbWindDbMax
                          && _ambRestServo <= AmbRestServoMaxDb && _servoPlayer.VolumeDb > AmbWalkServoMinDb;
                Dbg.Print(ok ? "AMBPROBE PASS" : "AMBPROBE FAIL");
                GetTree().Quit(ok ? 0 : 1);
            }
        }
    }


    // ---------- Beam shot ----------
    // Owner (2026-11-24): lasers yellow, the PPC a white
    // lightning bolt. Stage it: swap the player to Hector
    // (his rack carries the PPC) by the lab ritual, park
    // the enemy 70u ahead, fire one PPC volley and one
    // laser volley, and save the frame after each.
    private enum BeamPhase { Stage, PpcFire, PpcFrame, LaserFrameAndLongStage, LongFire, LongFrame }
    private const double BsStageSeconds = 1.2;
    private const double BsFireSeconds = 0.4;
    private const double BsFrameSeconds = 0.05;
    private const double BsLongFireSeconds = 0.5;
    private const float BsEnemyAhead = 70f;
    private const float BsAimHeight = 8f;
    private const float BsLongDistance = 500f;
    private const int BsScanBearings = 24;
    private const int BsScanSteps = 20;
    private const float BsClearance = 1f;
    private BeamPhase _bsPhase;
    private double _bsT;

    private void UpdateBeamShot(float dt)
    {
        _bsT += dt;
        if (_bsPhase == BeamPhase.Stage && _bsT > BsStageSeconds)
        {
            SetRiderSelection(RiderId.Hector, RiderId.Enyo);
            ApplyLoadout();
            if (_lab != null) { _lab.QueueFree(); _lab = null; }
            _labOpen = false;
            if (_labLight != null) _labLight.Visible = false;
            SetHudPanelsVisible(true); if (_radar != null) _radar.Visible = true;
            var ahead = _player.RootPos + RiderWalker.HeadingVec(_player.TorsoYawWorld) * BsEnemyAhead;
            ahead.Y = Main.HeightAt(ahead.X, ahead.Z);
            _enemy.Teleport(ahead, _player.TorsoYawWorld + Mathf.Pi);
            _bsPhase = BeamPhase.PpcFire; _bsT = 0;
        }
        else if (_bsPhase == BeamPhase.PpcFire && _bsT > BsFireSeconds)
        {
            var aim = _enemy.RootPos + Vector3.Up * BsAimHeight;
            var ppc = _gunsEnergy.FindAll(g => g.Def.Beam == BeamKind.Ppc);
            Dbg.Print($"BEAMSHOT ppc guns={ppc.Count} energy guns={_gunsEnergy.Count}");
            if (ppc.Count > 0) FireLaserVolley(aim, ppc);
            _bsPhase = BeamPhase.PpcFrame; _bsT = 0;
        }
        else if (_bsPhase == BeamPhase.PpcFrame && _bsT > BsFrameSeconds)
        {
            GetViewport().GetTexture().GetImage().SavePng(ProofDir + "beam_ppc.png");
            var aim = _enemy.RootPos + Vector3.Up * BsAimHeight;
            var las = _gunsEnergy.FindAll(g => g.Def.Beam != BeamKind.Ppc);
            if (las.Count > 0) FireLaserVolley(aim, las);
            _bsPhase = BeamPhase.LaserFrameAndLongStage; _bsT = 0;
        }
        else if (_bsPhase == BeamPhase.LaserFrameAndLongStage && _bsT > BsFrameSeconds)
        {
            GetViewport().GetTexture().GetImage().SavePng(ProofDir + "beam_laser.png");
            // Long-range stage (owner's repro, 2026-11-25):
            // the enemy 500u out, past the 220u convergence
            // cast - the state where the physical beams used
            // to cross at 220u and straddle him.
            // Run with --mission-test --beam-shot: the
            // mission world (bound 915) is the only one with
            // room for a 500u sightline - the arena's 115u
            // bound clamp drags a staged rider back inside.
            // Babylon's hills block most bearings, so scan
            // for a bearing whose chest-to-chest line clears
            // the terrain along its whole length.
            var stageDir = Vector3.Zero;
            var far = Vector3.Zero;
            var origin = _player.RootPos + Vector3.Up * (_player.Height * LockTrackHeightFraction);
            for (int k = 0; k < BsScanBearings && stageDir == Vector3.Zero; k++)
            {
                var d = RiderWalker.HeadingVec(k / (float)BsScanBearings * Mathf.Tau);
                var fp = _player.RootPos + d * BsLongDistance;
                var dest = new Vector3(fp.X, Main.HeightAt(fp.X, fp.Z) + _enemy.Height * LockTrackHeightFraction, fp.Z);
                bool clear = true;
                for (int s = 1; s < BsScanSteps && clear; s++)
                {
                    var p = origin.Lerp(dest, s / (float)BsScanSteps);
                    if (p.Y < Main.HeightAt(p.X, p.Z) + BsClearance) clear = false;
                }
                if (clear) { stageDir = d; far = fp; }
            }
            if (stageDir == Vector3.Zero)
            {
                Dbg.Print("BEAMSHOT LONG no clear 500u sightline from spawn");
                _bsPhase = BeamPhase.LongFrame; _bsT = 0;
                return;
            }
            _player.Teleport(_player.RootPos, RiderWalker.YawOf(stageDir));
            far.Y = Main.HeightAt(far.X, far.Z);
            _enemy.Teleport(far, RiderWalker.YawOf(-stageDir));
            _bsPhase = BeamPhase.LongFire; _bsT = 0;
        }
        else if (_bsPhase == BeamPhase.LongFire && _bsT > BsLongFireSeconds)
        {
            // Reproduce TorsoAimPoint's parallel state
            // exactly as the input loop hands it to the
            // volley: aim point = the 220u point on the axis,
            // _farConverge = where the crosshair really rests.
            var chest = _enemy.RootPos + Vector3.Up * (_enemy.Height * LockTrackHeightFraction);
            var camPos = _cam.GlobalPosition;
            _aimDir = (chest - camPos).Normalized();
            _farConverge = camPos.DistanceTo(chest);
            _aimParallel = true;
            float hpBefore = _hpEnemy;
            // ARM guns only: their muzzles sit ~4.5u off the
            // axis, so the pre-fix volley straddles the target
            // at this range (the owner's repro exactly) while
            // a torso gun rides the axis and hits either way.
            var arm = _gunsEnergy.FindAll(g => g.Slot == PartId.ArmL || g.Slot == PartId.ArmR);
            FireLaserVolley(camPos + _aimDir * Aim.ConvergenceRange, arm);
            Dbg.Print($"BEAMSHOT LONG range={_farConverge:F0} rootDist={_player.RootPos.DistanceTo(_enemy.RootPos):F0} armGuns={arm.Count} hp {hpBefore:F0} -> {_hpEnemy:F0} {(_hpEnemy < hpBefore ? "HIT" : "MISS")}");
            _aimParallel = false;
            _bsPhase = BeamPhase.LongFrame; _bsT = 0;
        }
        else if (_bsPhase == BeamPhase.LongFrame && _bsT > BsFrameSeconds)
        {
            GetViewport().GetTexture().GetImage().SavePng(ProofDir + "beam_long.png");
            Dbg.Print("BEAMSHOT DONE");
            GetTree().Quit(0);
        }
    }

}
