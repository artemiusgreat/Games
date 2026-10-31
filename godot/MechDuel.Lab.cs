using Godot;
using System.Collections.Generic;

// MechDuel module: MechLab glue: construction from specs, selection, loadout application.
// Split from the single-file build (round 22 cleanup); the class
// stays one partial class — see MechDuel.cs for the field block.
public partial class MechDuel
{
    private void OpenLab()
    {
        if (_lab != null) { _lab.QueueFree(); _lab = null; }
        var lab = new MechLab
        {
            Spec = _playerSpec,
            Loadout = _loadout,
            PlayerSpecId = _playerSpec.Id,
            EnemySpecId = _enemySpec.Id
        };
        _lab = lab;
        lab.OnStart = () =>
        {
            ApplyLoadout();
            lab.QueueFree();
            if (_lab == lab) _lab = null;
            _labOpen = false;
            if (_labLight != null) _labLight.Visible = false;
            _panel.Visible = true;
            _radar.Visible = true;
            _feedLabel.Visible = true;
            Input.MouseMode = Input.MouseModeEnum.Captured;
            GD.Print("LAB deploy: duel started with the lab loadout");
        };
        lab.OnSelectionChanged = () => SetMechSelection(lab.PlayerSpecId, lab.EnemySpecId);
        _uiLayer.AddChild(lab);
    }

    // ---- mech construction from specs (round 22) ----
    // Everything model-bound comes from the MechSpec, so the lab
    // can field either chassis on either side.
    private MechWalker BuildMech(MechSpec spec, string nodeName, Vector3 pos, float yaw)
    {
        var w = new MechWalker { Name = nodeName };
        AddChild(w);
        w.Init(new MechWalker.Config
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
        _hit.Mechs.RemoveAll(m => m.Id == "PLAYER" || m.Id == "ENEMY");
        _hit.Mechs.Add(new HitModel.MechCap { Id = "PLAYER", Root = () => _player.RootPos, Radius = _playerSpec.Radius, Height = _playerSpec.Height });
        _hit.Mechs.Add(new HitModel.MechCap { Id = "ENEMY", Root = () => _enemy.RootPos, Radius = _enemySpec.Radius, Height = _enemySpec.Height });
    }

    private void RebuildMech(bool playerSide)
    {
        if (playerSide)
        {
            var pos = _player.RootPos; float yaw = _player.LegsYaw;
            _player.QueueFree();
            _player = BuildMech(_playerSpec, "Player", pos, yaw);
            _partsPlayer = new MechParts(_player, _playerSpec.PartsId);
            _partsPlayer.OnBreak = (id, c) => OnPartBroken(_partsPlayer, id, c);
            _player.RefreshMaterials();
            _hpPlayer = _playerSpec.MaxHp; _playerDead = false;
            _prevPlayerRoot = _player.RootPos;
        }
        else
        {
            var pos = _enemy.RootPos; float yaw = _enemy.LegsYaw;
            _enemy.QueueFree();
            _enemy = BuildMech(_enemySpec, "Enemy", pos, yaw);
            _partsEnemy = new MechParts(_enemy, _enemySpec.PartsId);
            _partsEnemy.OnBreak = (id, c) => OnPartBroken(_partsEnemy, id, c);
            _enemy.RefreshMaterials();
            _hpEnemy = _enemySpec.MaxHp; _enemyDead = false;
        }
        RefreshHitCaps();
        ApplyLoadout();
    }

    // Lab mech selection (round 22): swap either side's chassis.
    // The loadout resets to the new chassis's default fit; the lab
    // UI is recreated against the new spec.
    public void SetMechSelection(string playerSpecId, string enemySpecId)
    {
        bool pChanged = playerSpecId != _playerSpec.Id;
        bool eChanged = enemySpecId != _enemySpec.Id;
        if (!pChanged && !eChanged) return;
        if (pChanged)
        {
            _playerSpec = MechSpecs.ById(playerSpecId);
            _loadout = LoadoutState.DefaultFor(_playerSpec);
            RebuildMech(true);
        }
        if (eChanged)
        {
            _enemySpec = MechSpecs.ById(enemySpecId);
            _enemyLoadout = LoadoutState.DefaultFor(_enemySpec);
            RebuildMech(false);
        }
        GD.Print($"LAB selection player={_playerSpec.Id} enemy={_enemySpec.Id}");
        if (_lab != null) Callable.From(() => { if (_labOpen) OpenLab(); }).CallDeferred();
    }

    private static string SlotOf(MechSpec spec, WeaponSlotType type, int n = 0)
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
