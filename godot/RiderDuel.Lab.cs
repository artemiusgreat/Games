using Godot;
using System.Collections.Generic;

// RiderDuel module: RiderLab glue: construction from specs, selection, loadout application.
// Split from the single-file build (round 22 cleanup); the class
// stays one partial class — see RiderDuel.cs for the field block.
public partial class RiderDuel
{
    // Viewport position of a slot's part on the player's rider —
    // the lab's paperdoll dots are drawn there, so the mapping
    // survives any window size or aspect (owner). Null when the
    // chassis lacks the part or it is off-screen: the lab falls
    // back to its fractional anchors then.
    private Vector2? ProjectSlotAnchor(string slotId)
    {
        if (_partsPlayer == null || _cam == null || !_partsPlayer.Has(slotId)) return null;
        var w = _partsPlayer.PartCenterWorld(slotId);
        if (_cam.IsPositionBehind(w)) return null;
        var sp = _cam.UnprojectPosition(w);
        var vp = GetViewport().GetVisibleRect().Size;
        if (sp.X < -40f || sp.Y < -40f || sp.X > vp.X + 40f || sp.Y > vp.Y + 40f) return null;
        return sp;
    }

    // Viewport position of the rider's pelvis — the paperdoll's
    // radial center for panel placement. The slot anchors all
    // sit in the upper body, so their own centroid rides too
    // high and the panels bunch at the top; the pelvis is the
    // body's real middle.
    private Vector2? ProjectBodyCenter()
    {
        if (_player == null || _cam == null) return null;
        var w = _player.RootPos + Vector3.Up * (_playerSpec.Height * 0.45f);
        if (_cam.IsPositionBehind(w)) return null;
        var sp = _cam.UnprojectPosition(w);
        var vp = GetViewport().GetVisibleRect().Size;
        if (sp.X < -40f || sp.Y < -40f || sp.X > vp.X + 40f || sp.Y > vp.Y + 40f) return null;
        return sp;
    }

    private void OpenLab()
    {
        if (_lab != null) { _lab.QueueFree(); _lab = null; }
        var lab = new RiderLab
        {
            Spec = _playerSpec,
            Loadout = _loadout,
            PlayerSpecId = _playerSpec.Id,
            EnemySpecId = _enemySpec.Id,
            AnchorProjector = ProjectSlotAnchor,
            CenterProjector = ProjectBodyCenter
        };
        _lab = lab;
        lab.OnStart = () =>
        {
            ApplyLoadout();
            lab.QueueFree();
            if (_lab == lab) _lab = null;
            _labOpen = false;
            _cross.Visible = true; // the target cursor returns on deploy (owner: it must not show in the lab)
            if (_labLight != null) _labLight.Visible = false;
            _panel.Visible = true;
            _radar.Visible = true;
            _feedLabel.Visible = true;
            Input.MouseMode = Input.MouseModeEnum.Captured;
            GD.Print("LAB deploy: duel started with the lab loadout");
        };
        lab.OnSelectionChanged = () => SetRiderSelection(lab.PlayerSpecId, lab.EnemySpecId);
        _cross.Visible = false; // no target cursor in the lab view (owner)
        _uiLayer.AddChild(lab);
    }

    // ---- rider construction from specs (round 22) ----
    // Everything model-bound comes from the RiderSpec, so the lab
    // can field either chassis on either side.
    private RiderWalker BuildRider(RiderSpec spec, string nodeName, Vector3 pos, float yaw)
    {
        var w = new RiderWalker { Name = nodeName };
        AddChild(w);
        w.Init(new RiderWalker.Config
        {
            Label = nodeName.ToUpperInvariant(),
            ModelPath = spec.ModelPath,
            TargetHeight = spec.Height,
            StepTime = spec.StepTime,
            StepThreshold = spec.StepThreshold,
            StepLift = spec.StepLift,
            Accel = spec.Accel,
            CollisionRadius = spec.Radius,
            FlipKneePole = spec.FlipKneePole
        }, pos, yaw);
        return w;
    }

    private void RefreshHitCaps()
    {
        _hit.Riders.RemoveAll(m => m.Id == Roles.Player || m.Id == Roles.Enemy || m.Id == Roles.Foe1);
        _hit.Riders.Add(new HitModel.RiderCap { Id = Roles.Player, Root = () => _player.RootPos, Radius = _playerSpec.Radius, Height = _playerSpec.Height });
        _hit.Riders.Add(new HitModel.RiderCap { Id = Roles.Enemy, Root = () => _enemy.RootPos, Radius = _enemySpec.Radius, Height = _enemySpec.Height });
        if (_foe1?.Walker != null)
            _hit.Riders.Add(new HitModel.RiderCap { Id = Roles.Foe1, Root = () => _foe1.Walker.RootPos, Radius = _foe1.Spec.Radius, Height = _foe1.Spec.Height });
    }

    // Foe 1 (free-for-all): the partner chassis — the first
    // entry on the roster's partner-priority list that the
    // enemy slot does not already hold. No chassis names in
    // engine code; the list is roster data.
    private static RiderSpec FfaPartner(RiderSpec enemy)
    {
        foreach (var s in RiderSpecs.FfaPartnerPriority) if (s.Id != enemy.Id) return s;
        foreach (var s in RiderSpecs.All) if (s.Id != enemy.Id) return s;
        return enemy;
    }

    private void BuildFoe1()
    {
        var spec = FfaPartner(_enemySpec);
        _foe1 = new Foe { Spec = spec, Loadout = LoadoutState.DefaultFor(spec) };
        var pad = Main.SpawnPads[2];
        _foe1.Walker = BuildRider(spec, "Enemy2", PadPos(pad), SpawnYaw(pad));
        _foe1.Yaw = SpawnYaw(pad);
        _foe1.Parts = new RiderParts(_foe1.Walker, spec.PartsId);
        _foe1.Parts.OnBreak = (id, c) => OnPartBroken(_foe1.Parts, id, c);
        _foe1.Walker.RefreshMaterials();
        _foe1.Hp = spec.MaxHp;
        _foe1.PrevRoot = _foe1.Walker.RootPos;
        string eb = SlotOf(spec, WeaponSlotType.Ballistic);
        _foe1.BallisticSlot = eb;
        _foe1.DefBallistic = eb == null ? null : WeaponCatalog.ById(_foe1.Loadout.WeaponIn(eb));
        GD.Print($"FFA foe1 built: {spec.Id} at pad 2, ballistic={_foe1.DefBallistic?.Id ?? "none"}");
    }

    private void RebuildFoe1()
    {
        var want = FfaPartner(_enemySpec);
        if (_foe1 != null && _foe1.Spec.Id == want.Id) return;
        var pos = _foe1?.Walker.RootPos ?? PadPos(Main.SpawnPads[2]);
        float yaw = _foe1?.Walker.LegsYaw ?? SpawnYaw(Main.SpawnPads[2]);
        _foe1?.Walker.QueueFree();
        var spec = want;
        _foe1 = new Foe { Spec = spec, Loadout = LoadoutState.DefaultFor(spec) };
        _foe1.Walker = BuildRider(spec, "Enemy2", pos, yaw);
        _foe1.Yaw = yaw;
        _foe1.Parts = new RiderParts(_foe1.Walker, spec.PartsId);
        _foe1.Parts.OnBreak = (id, c) => OnPartBroken(_foe1.Parts, id, c);
        _foe1.Walker.RefreshMaterials();
        _foe1.Hp = spec.MaxHp;
        _foe1.PrevRoot = _foe1.Walker.RootPos;
        string eb = SlotOf(spec, WeaponSlotType.Ballistic);
        _foe1.BallisticSlot = eb;
        _foe1.DefBallistic = eb == null ? null : WeaponCatalog.ById(_foe1.Loadout.WeaponIn(eb));
        RefreshHitCaps();
    }

    private void RebuildRider(bool playerSide)
    {
        if (playerSide)
        {
            var pos = _player.RootPos; float yaw = _player.LegsYaw;
            _player.QueueFree();
            _player = BuildRider(_playerSpec, "Player", pos, yaw);
            _partsPlayer = new RiderParts(_player, _playerSpec.PartsId);
            _partsPlayer.OnBreak = (id, c) => OnPartBroken(_partsPlayer, id, c);
            _player.RefreshMaterials();
            _hpPlayer = _playerSpec.MaxHp; _playerDead = false;
            _prevPlayerRoot = _player.RootPos;
        }
        else
        {
            var pos = _enemy.RootPos; float yaw = _enemy.LegsYaw;
            _enemy.QueueFree();
            _enemy = BuildRider(_enemySpec, "Enemy", pos, yaw);
            _partsEnemy = new RiderParts(_enemy, _enemySpec.PartsId);
            _partsEnemy.OnBreak = (id, c) => OnPartBroken(_partsEnemy, id, c);
            _enemy.RefreshMaterials();
            _hpEnemy = _enemySpec.MaxHp; _enemyDead = false;
        }
        RefreshHitCaps();
        ApplyLoadout();
    }

    // Lab rider selection (round 22): swap either side's chassis.
    // The loadout resets to the new chassis's default fit; the lab
    // UI is recreated against the new spec.
    public void SetRiderSelection(string playerSpecId, string enemySpecId)
    {
        bool pChanged = playerSpecId != _playerSpec.Id;
        bool eChanged = enemySpecId != _enemySpec.Id;
        if (!pChanged && !eChanged) return;
        if (pChanged)
        {
            _playerSpec = RiderSpecs.ById(playerSpecId);
            _loadout = LoadoutState.DefaultFor(_playerSpec);
            RebuildRider(true);
        }
        if (eChanged)
        {
            _enemySpec = RiderSpecs.ById(enemySpecId);
            _enemyLoadout = LoadoutState.DefaultFor(_enemySpec);
            RebuildRider(false);
            if (_foe1 != null) RebuildFoe1(); // the FFA partner re-picks from the priority data
        }
        GD.Print($"LAB selection player={_playerSpec.Id} enemy={_enemySpec.Id}");
        if (_lab != null) Callable.From(() => { if (_labOpen) OpenLab(); }).CallDeferred();
    }

    private static string SlotOf(RiderSpec spec, WeaponSlotType type, int n = 0)
    {
        foreach (var s in spec.Slots) if (s.Type == type && n-- == 0) return s.Id;
        return null;
    }

    private void ApplyLoadout()
    {
        // Jump jets (owner): effective only when the chassis can
        // mount them AND the loadout has them fitted.
        _player.JumpJets = _playerSpec.JumpCapable && _loadout.JumpJets;
        _enemy.JumpJets = _enemySpec.JumpCapable;
        // Firing groups (multi-fit): every weapon fitted in a slot
        // of the matching type, in spec slot order. The single
        // _def* fields stay as each group's representative (the
        // slowest-cooldown weapon) for HUD bars and labels.
        _gunsEnergy.Clear(); _gunsBallistic.Clear(); _podGunsL.Clear(); _podGunsR.Clear();
        int missileIdx = 0;
        foreach (var slot in _playerSpec.Slots)
        {
            var bucket = slot.Type == WeaponSlotType.Energy ? _gunsEnergy
                : slot.Type == WeaponSlotType.Ballistic ? _gunsBallistic
                : missileIdx++ == 0 ? _podGunsL : _podGunsR; // missile slots in order: left pod, right pod
            foreach (var wid in _loadout.WeaponsIn(slot.Id))
            {
                var def = WeaponCatalog.ById(wid);
                if (def != null) bucket.Add((slot.Id, def));
            }
        }
        _defEnergy = Slowest(_gunsEnergy);
        _defBallistic = Slowest(_gunsBallistic);
        _defPodL = Slowest(_podGunsL);
        _defPodR = Slowest(_podGunsR);
        string eb = SlotOf(_enemySpec, WeaponSlotType.Ballistic);
        _enemyBallisticSlot = eb;
        _defEnemyBallistic = eb == null ? null : WeaponCatalog.ById(_enemyLoadout.WeaponIn(eb));
        _wpnShort[0] = "1 " + GroupLabel(_gunsEnergy);
        _wpnShort[1] = "2 " + GroupLabel(_gunsBallistic);
        _wpnShort[2] = "3 " + GroupLabel(_podGunsL, _podGunsR);
        GD.Print($"LAB loadout energy={_defEnergy?.Id ?? "none"} ballistic={_defBallistic?.Id ?? "none"} podL={_defPodL?.Id ?? "none"} podR={_defPodR?.Id ?? "none"} total={_loadout.TotalWeaponWeight:F0}t");
    }

    private static WeaponDef Slowest(System.Collections.Generic.List<(string Slot, WeaponDef Def)> guns)
    {
        WeaponDef best = null;
        foreach (var g in guns) if (best == null || g.Def.Cooldown > best.Cooldown) best = g.Def;
        return best;
    }

    private static string GroupLabel(params System.Collections.Generic.List<(string Slot, WeaponDef Def)>[] groups)
    {
        int n = 0; string first = null;
        foreach (var g in groups) foreach (var gun in g) { if (first == null) first = gun.Def.Short; n++; }
        return first == null ? "—" : n > 1 ? $"{first} ×{n}" : first;
    }

}
