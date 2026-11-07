using Godot;
using System.Collections.Generic;

// RiderDuel module: cockpit panel, radar readouts, cooldown rows, droop.
// Split from the single-file build (round 22 cleanup); the class
// stays one partial class — see RiderDuel.cs for the field block.
public partial class RiderDuel
{
    private static Label MakeLabel(int fontSize, Color col)
    {
        var l = new Label { Text = "" };
        l.AddThemeFontSizeOverride("font_size", fontSize);
        l.AddThemeColorOverride("font_color", col);
        return l;
    }

    // Mission fronts: objective markers for the three bases (the
    // 90u radar cannot navigate a 1900u map). Screen-projected,
    // clamped to the viewport edge when off-screen.
    private Label[] _baseMarks;

    private void BuildHud(CanvasLayer layer)
    {
        if (WorldConfig.IsMission)
        {
            _baseMarks = new Label[Main.SpawnPads.Length];
            for (int i = 0; i < _baseMarks.Length; i++)
            {
                var l = MakeLabel(13, i == 0 ? new Color(0.55f, 0.95f, 0.6f) : new Color(1f, 0.55f, 0.45f));
                l.AddThemeConstantOverride("outline_size", 8);
                l.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
                l.Visible = false;
                l.MouseFilter = Control.MouseFilterEnum.Ignore;
                layer.AddChild(l);
                _baseMarks[i] = l;
            }
        }
        // ---- Cockpit control panel: one console strip along the bottom
        // holding rider integrity, per-part status, weapon charge with the
        // selected weapon marked, and target/system readouts.
        _panel = new PanelContainer();
        _panel.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        _panel.OffsetLeft = 16; _panel.OffsetRight = -16;
        _panel.OffsetTop = -172; _panel.OffsetBottom = -14;
        var pbg = new StyleBoxFlat
        {
            BgColor = new Color(0.055f, 0.075f, 0.105f, 0.58f),
            BorderColor = new Color(0.5f, 0.56f, 0.62f, 0.55f),
            ContentMarginLeft = 16, ContentMarginRight = 16,
            ContentMarginTop = 10, ContentMarginBottom = 10,
        };
        pbg.SetBorderWidthAll(2);
        pbg.SetCornerRadiusAll(8);
        _panel.AddThemeStyleboxOverride("panel", pbg);
        layer.AddChild(_panel);

        // Radar scope, top-right (owner): player-centered, north-up.
        _radar = new RadarView
        {
            Player = _player,
            Enemy = _enemy,
            CustomMinimumSize = new Vector2(168f, 168f)
        };
        _radar.Buildings.AddRange(_buildingPts);
        _radar.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _radar.OffsetLeft = -184f;
        _radar.OffsetTop = 16f;
        _radar.OffsetRight = -16f;
        _radar.OffsetBottom = 184f;
        _radar.MouseFilter = Control.MouseFilterEnum.Ignore;
        layer.AddChild(_radar);

        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 20);
        _panel.AddChild(hbox);

        Label Title(string t)
        {
            var l = MakeLabel(12, new Color(0.62f, 0.7f, 0.78f));
            l.Text = t;
            return l;
        }

        // -- Left: own rider integrity + per-part status grid.
        var leftBox = new VBoxContainer { CustomMinimumSize = new Vector2(372, 0) };
        leftBox.AddThemeConstantOverride("separation", 4);
        hbox.AddChild(leftBox);
        _lblPlayerHp = MakeLabel(16, new Color(0.85f, 0.95f, 0.85f));
        leftBox.AddChild(_lblPlayerHp);
        _barPlayer = MakeBar(new Color(0.3f, 0.85f, 0.32f), new Vector2(372, 20));
        leftBox.AddChild(_barPlayer);
        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 10);
        grid.AddThemeConstantOverride("v_separation", 3);
        leftBox.AddChild(grid);
        foreach (var (id, name) in new[]
        {
            ("head", "HEAD"), ("arm_L", "ARM L"), ("arm_R", "ARM R"), ("gun", "GUN"), ("pod_L", "POD L"),
            ("pod_R", "POD R"), ("leg_L", "LEG L"), ("leg_R", "LEG R"),
        })
        {
            var cell = new VBoxContainer();
            cell.AddThemeConstantOverride("separation", 1);
            var lab = MakeLabel(10, new Color(0.75f, 0.8f, 0.85f));
            lab.Text = name;
            cell.AddChild(lab);
            var bar = MakeBar(new Color(0.45f, 0.75f, 0.5f), new Vector2(84, 8));
            cell.AddChild(bar);
            grid.AddChild(cell);
            _partCells[id] = (lab, bar);
        }

        hbox.AddChild(new VSeparator());

        // -- Center: weapons. The selected row is marked with a pointer
        // and bright text (set in UpdateHud); the hint line teaches the
        // switching scheme in-game.
        var midBox = new VBoxContainer { CustomMinimumSize = new Vector2(308, 0) };
        midBox.AddThemeConstantOverride("separation", 4);
        hbox.AddChild(midBox);
        midBox.AddChild(Title("WEAPONS"));
        (Label, ProgressBar, Label) CdRow(string name)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            var lab = MakeLabel(14, new Color(0.9f, 0.9f, 0.9f, 0.9f));
            lab.Text = name;
            lab.CustomMinimumSize = new Vector2(74, 0);
            row.AddChild(lab);
            var bar = MakeBar(new Color(0.95f, 0.8f, 0.3f), new Vector2(218, 13));
            bar.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(bar);
            // The active-weapon triangle sits at the END of the
            // bar (owner) — a fixed-width slot after the bar, so
            // rows never shift when the selection moves.
            var mark = MakeLabel(14, new Color(1f, 0.95f, 0.6f));
            mark.CustomMinimumSize = new Vector2(16, 0);
            row.AddChild(mark);
            midBox.AddChild(row);
            return (lab, bar, mark);
        }
        var rowLaser = CdRow("1 LAS");
        _lblCdLaser = rowLaser.Item1; _cdBarLaser = rowLaser.Item2; _markLaser = rowLaser.Item3;
        var rowAc = CdRow("2 AC");
        _lblCdAc = rowAc.Item1; _cdBarAc = rowAc.Item2; _markAc = rowAc.Item3;
        var rowLrm = CdRow("3 LRM");
        _lblCdLrm = rowLrm.Item1; _cdBarLrm = rowLrm.Item2; _markLrm = rowLrm.Item3;
        var rowThr = CdRow("THRUST");
        _lblThr = rowThr.Item1; _cdBarThr = rowThr.Item2;
        var hint = MakeLabel(11, new Color(0.6f, 0.66f, 0.72f));
        hint.Text = "1 / 2 / 3 or wheel — select weapon · LMB — fire · SPACE (hold) — thrusters";
        midBox.AddChild(hint);
        _lblWeaponStatus = MakeLabel(12, new Color(1f, 0.42f, 0.32f));
        midBox.AddChild(_lblWeaponStatus);

        hbox.AddChild(new VSeparator());

        // -- Right: target + own systems readouts.
        var rightBox = new VBoxContainer { CustomMinimumSize = new Vector2(300, 0) };
        rightBox.AddThemeConstantOverride("separation", 4);
        hbox.AddChild(rightBox);
        _lblEnemyHp = MakeLabel(16, new Color(0.98f, 0.85f, 0.8f));
        rightBox.AddChild(_lblEnemyHp);
        _barEnemy = MakeBar(new Color(0.88f, 0.3f, 0.24f), new Vector2(300, 20));
        rightBox.AddChild(_barEnemy);
        // Second opponent (free-for-all): a compact readout under
        // the primary target's. Hidden when foe 1 is not fielded
        // (the precision test modes).
        _lblFoe1Hp = MakeLabel(13, new Color(0.98f, 0.85f, 0.8f));
        rightBox.AddChild(_lblFoe1Hp);
        _barFoe1 = MakeBar(new Color(0.88f, 0.45f, 0.24f), new Vector2(300, 12));
        rightBox.AddChild(_barFoe1);
        _lblRange = MakeLabel(13, new Color(0.85f, 0.88f, 0.9f));
        rightBox.AddChild(_lblRange);
        _lblSpeed = MakeLabel(13, new Color(0.85f, 0.88f, 0.9f));
        rightBox.AddChild(_lblSpeed);
        _lblLegs = MakeLabel(13, new Color(0.85f, 0.88f, 0.9f));
        rightBox.AddChild(_lblLegs);

        // Event feed, top-left: part breaks and other combat events.
        _feedLabel = MakeLabel(15, new Color(0.98f, 0.88f, 0.62f));
        _feedLabel.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        _feedLabel.OffsetLeft = 18; _feedLabel.OffsetTop = 14;
        _feedLabel.OffsetRight = 560; _feedLabel.OffsetBottom = 130;
        layer.AddChild(_feedLabel);

        // Death banner.
        _banner = MakeLabel(46, Colors.White);
        _banner.SetAnchorsPreset(Control.LayoutPreset.Center);
        _banner.OffsetLeft = -450; _banner.OffsetTop = -150; _banner.OffsetRight = 450; _banner.OffsetBottom = -90;
        _banner.HorizontalAlignment = HorizontalAlignment.Center;
        _banner.Visible = false;
        layer.AddChild(_banner);
        _bannerSub = MakeLabel(19, new Color(0.9f, 0.9f, 0.9f));
        _bannerSub.Text = "press R to reset";
        _bannerSub.SetAnchorsPreset(Control.LayoutPreset.Center);
        _bannerSub.OffsetLeft = -450; _bannerSub.OffsetTop = -84; _bannerSub.OffsetRight = 450; _bannerSub.OffsetBottom = -56;
        _bannerSub.HorizontalAlignment = HorizontalAlignment.Center;
        _bannerSub.Visible = false;
        layer.AddChild(_bannerSub);
    }

    private static readonly Color CdNormal = new Color(0.95f, 0.8f, 0.3f);
    private static readonly Color CdDead = new Color(0.45f, 0.12f, 0.1f);
    private static readonly Color CdDeny = new Color(1f, 0.16f, 0.1f);
    private static readonly Color CdLabelNormal = new Color(0.9f, 0.9f, 0.9f, 0.9f);
    private static readonly Color CdLabelDead = new Color(1f, 0.4f, 0.32f);

    // One cooldown row's state: value, destroyed color, denied flash.
    private void UpdateCdRow(ProgressBar bar, Label lab, float value,
        bool destroyed, float denyT)
    {
        bar.Value = destroyed ? 0f : value;
        var fg = bar.GetThemeStylebox("fill") as StyleBoxFlat;
        if (fg != null)
        {
            if (denyT > 0f)
                fg.BgColor = Mathf.Sin((float)_simT * 42f) > 0f ? CdDeny : CdDead;
            else if (destroyed) fg.BgColor = CdDead;
            else fg.BgColor = CdNormal;
        }
        lab.AddThemeColorOverride("font_color", destroyed ? CdLabelDead : CdLabelNormal);
    }

    // The selected weapon's row shows its triangle at the end of
    // the cooldown bar (owner) and, while the weapon lives,
    // bright text; destroyed rows keep their red.
    private static void SetSelLabel(Label lab, Label mark, string baseText, bool selected, bool destroyed)
    {
        lab.Text = baseText;
        mark.Text = selected ? "▶" : "";
        if (selected && !destroyed)
            lab.AddThemeColorOverride("font_color", new Color(1f, 0.95f, 0.6f));
    }

    private void UpdateBaseMarks()
    {
        if (_baseMarks == null) return;
        bool show = _panel.Visible && !_labOpen && _cam != null;
        var vp = GetViewport().GetVisibleRect().Size;
        for (int i = 0; i < _baseMarks.Length; i++)
        {
            var l = _baseMarks[i];
            var pad = Main.SpawnPads[i];
            var world = new Vector3(pad.X, Main.HeightAt(pad.X, pad.Y) + 20f, pad.Y);
            float dist = _player.RootPos.DistanceTo(world);
            if (!show || dist < 45f) { l.Visible = false; continue; }
            Vector2 sp;
            if (_cam.IsPositionBehind(world))
            {
                // Mirror through the screen center, then clamp to the edge.
                var fwd = -_cam.GlobalTransform.Basis.Z;
                var to = (world - _cam.GlobalPosition).Normalized();
                float side = Mathf.Sign(to.Dot(_cam.GlobalTransform.Basis.X));
                sp = new Vector2(vp.X * (0.5f + 0.46f * side), vp.Y * 0.92f);
            }
            else sp = _cam.UnprojectPosition(world);
            sp = new Vector2(Mathf.Clamp(sp.X, 70f, vp.X - 190f), Mathf.Clamp(sp.Y, 30f, vp.Y - 210f));
            l.Position = sp - new Vector2(60f, 10f);
            l.Text = i == 0 ? $"◆ HOME BASE · {dist:F0} u" : $"◆ ENEMY BASE · {dist:F0} u";
            l.Visible = true;
        }
    }

    private void UpdateHud()
    {
        if (_barPlayer == null) return;
        UpdateBaseMarks();
        _barPlayer.Value = _hpPlayer / _playerSpec.MaxHp;
        _lblPlayerHp.Text = $"{_playerSpec.Name} — {_hpPlayer:F0} HP";
        _barEnemy.Visible = !_enemyDead;
        _lblEnemyHp.Visible = !_enemyDead;
        _barEnemy.Value = _hpEnemy / _enemySpec.MaxHp;
        _lblEnemyHp.Text = $"{_enemySpec.Name} — {_hpEnemy:F0} HP";
        bool foe1Shown = _foe1 != null && !_foe1.Dead;
        _barFoe1.Visible = foe1Shown;
        _lblFoe1Hp.Visible = foe1Shown;
        if (foe1Shown)
        {
            _barFoe1.Value = _foe1.Hp / _foe1.Spec.MaxHp;
            float r1 = new Vector2(_foe1.Walker.RootPos.X - _player.RootPos.X,
                                   _foe1.Walker.RootPos.Z - _player.RootPos.Z).Length();
            _lblFoe1Hp.Text = $"{_foe1.Spec.Name} — {_foe1.Hp:F0} HP · {r1:F0} u";
        }

        bool lasDead = !WeaponAlive(0);
        bool acDead = !WeaponAlive(1);
        bool lrmDead = !WeaponAlive(2);
        float lrmCd = Mathf.Max(_defPodL?.Cooldown ?? 0f, _defPodR?.Cooldown ?? 0f);
        if (lrmCd <= 0f) lrmCd = 3.5f;
        UpdateCdRow(_cdBarLaser, _lblCdLaser, 1f - _cdLaser / (_defEnergy?.Cooldown ?? 0.45f), lasDead, _denyLaserT);
        UpdateCdRow(_cdBarAc, _lblCdAc, 1f - _cdAc / (_defBallistic?.Cooldown ?? 0.85f), acDead, _denyAcT);
        UpdateCdRow(_cdBarLrm, _lblCdLrm, 1f - _cdLrm / lrmCd, lrmDead, _denyLrmT);
        // Thrusters (owner): charge readout alongside the weapon
        // cooldowns — teal when the full jump charge is ready.
        if (_cdBarThr != null)
        {
            bool fitted = _player.JumpJets;
            _cdBarThr.Value = fitted ? _player.JumpCharge : 0f;
            var tfg = _cdBarThr.GetThemeStylebox("fill") as StyleBoxFlat;
            if (tfg != null) tfg.BgColor = !fitted ? CdDead
                : _player.JumpCharge >= 0.999f ? new Color(0.35f, 0.9f, 0.75f) : CdNormal;
            _lblThr.AddThemeColorOverride("font_color", fitted ? CdLabelNormal : CdLabelDead);
            _lblThr.Text = !fitted ? "THRUST —" : _player.Airborne ? "THRUST ▲" : "THRUST";
        }
        SetSelLabel(_lblCdLaser, _markLaser, _wpnShort[0], _weaponSel == 0, lasDead);
        SetSelLabel(_lblCdAc, _markAc, _wpnShort[1], _weaponSel == 1, acDead);
        SetSelLabel(_lblCdLrm, _markLrm, _wpnShort[2], _weaponSel == 2, lrmDead);

        // Per-part status cells (player rider).
        foreach (var (id, cell) in _partCells)
        {
            var part = _partsPlayer.GetPart(id);
            var cellBox = cell.Lab.GetParent() as Control;
            if (part == null) { if (cellBox != null) cellBox.Visible = false; continue; } // chassis lacks this part (Hector: no pods)
            if (cellBox != null) cellBox.Visible = true;
            float frac = part.MaxHp > 0f ? part.Hp / part.MaxHp : 0f;
            cell.Bar.Value = frac;
            var fg = cell.Bar.GetThemeStylebox("fill") as StyleBoxFlat;
            if (fg != null)
                fg.BgColor = part.Destroyed ? CdDead
                    : frac < 0.35f ? new Color(0.9f, 0.45f, 0.2f)
                    : new Color(0.45f, 0.75f, 0.5f);
            cell.Lab.AddThemeColorOverride("font_color",
                part.Destroyed ? CdLabelDead : new Color(0.75f, 0.8f, 0.85f));
        }

        // Systems readouts: target range, own speed, leg state.
        float range = new Vector2(_enemy.RootPos.X - _player.RootPos.X,
                                  _enemy.RootPos.Z - _player.RootPos.Z).Length();
        _lblRange.Text = _enemyDead ? "TARGET: DESTROYED" : $"TARGET RANGE: {range:F1} u";
        _lblSpeed.Text = $"SPEED: {_playerVel.Length():F1} u/s" + (_running && _playerVel.Length() > 0.5f ? " — RUN" : "");
        bool lLeg = _partsPlayer.Alive("leg_L"), rLeg = _partsPlayer.Alive("leg_R");
        _lblLegs.Text = lLeg && rLeg ? "LEGS: OK"
            : !lLeg && !rLeg ? "LEGS: BOTH WRECKED — CRAWL"
            : lLeg ? "LEGS: RIGHT WRECKED — LIMPING" : "LEGS: LEFT WRECKED — LIMPING";
        _lblLegs.AddThemeColorOverride("font_color",
            lLeg && rLeg ? new Color(0.85f, 0.88f, 0.9f) : new Color(1f, 0.42f, 0.32f));
        if (_radar != null)
        {
            _radar.EnemyDown = _enemyDead;
            _radar.Enemy2 = _foe1?.Walker;
            _radar.Enemy2Down = _foe1?.Dead ?? true;
            _radar.QueueRedraw();
        }
        if (_player.IsKnockedDown)
        {
            _lblLegs.Text = "STATUS: DOWN — LEG LOST";
            _lblLegs.AddThemeColorOverride("font_color", new Color(1f, 0.42f, 0.32f));
        }

        var status = new System.Collections.Generic.List<string>();
        if (lasDead && _defEnergy != null) status.Add("ENERGY WEAPONS DESTROYED — ALL MOUNTS LOST");
        if (acDead && _defBallistic != null) status.Add("BALLISTIC WEAPONS DESTROYED — ALL MOUNTS LOST");
        // Pod status: only for pods the chassis actually HAS (the
        // Hector carries a single rack — an absent right pod is not
        // a "lost" one). Wording follows the equipped pod weapon.
        string podName = (_defPodL ?? _defPodR)?.Short ?? "LRM";
        bool hasPodL = _partsPlayer.Has("pod_L"), hasPodR = _partsPlayer.Has("pod_R");
        int podRocketsL = 0, podRocketsR = 0;
        foreach (var g in _podGunsL) podRocketsL += g.Def.Rockets;
        foreach (var g in _podGunsR) podRocketsR += g.Def.Rockets;
        if (lrmDead && (_defPodL != null || _defPodR != null))
            status.Add(hasPodL && hasPodR ? $"{podName} DESTROYED — BOTH PODS LOST" : $"{podName} DESTROYED — POD LOST");
        else if (_defPodL != null && hasPodL && !_partsPlayer.Alive("pod_L")) status.Add($"{podName} DEGRADED — LEFT POD LOST ({podRocketsL} ROCKETS)");
        else if (_defPodR != null && hasPodR && !_partsPlayer.Alive("pod_R")) status.Add($"{podName} DEGRADED — RIGHT POD LOST ({podRocketsR} ROCKETS)");
        if (!_partsPlayer.Alive("head")) status.Add("SENSORS DESTROYED — CROSSHAIR SWAY");
        _lblWeaponStatus.Text = string.Join("\n", status);

        _feedLabel.Text = string.Join("\n",
            System.Linq.Enumerable.Select(_feed, e => e.Text));

        // Head-loss HUD disturbance: while the flicker timer runs, the
        // whole HUD stutters (seeded, visual only).
        float alpha = 1f;
        if (_hudFlickerT > 0f)
            alpha = _rng.NextDouble() < 0.38 ? 0.15f : 1f;
        foreach (var c in new Control[] { _panel, _feedLabel, _cross })
        {
            var m = c.Modulate;
            m.A = alpha;
            c.Modulate = m;
        }
    }

    private void UpdateDroop(float dt)
    {
        if (_foe1 != null && _foe1.Dead && _foe1.DroopT < 1f)
        {
            _foe1.DroopT = Mathf.Min(_foe1.DroopT + dt / 1.5f, 1f);
            float t1 = _foe1.DroopT;
            _foe1.Walker.Droop = t1 * t1 * (3f - 2f * t1) * 0.5f;
        }
        if (_enemyDead && _enemyDroopT < 1f)
        {
            _enemyDroopT = Mathf.Min(_enemyDroopT + dt / 1.5f, 1f);
            float t = _enemyDroopT;
            _enemy.Droop = t * t * (3f - 2f * t) * 0.5f;
        }
        if (_playerDead && _playerDroopT < 1f)
        {
            _playerDroopT = Mathf.Min(_playerDroopT + dt / 1.5f, 1f);
            float t = _playerDroopT;
            _player.Droop = t * t * (3f - 2f * t) * 0.5f;
        }
    }

    // Hector AC/20 gunnery: range + line-of-sight gated, leads the player's
    // velocity at time-of-flight, seeded ±1.2° inaccuracy cone.
}
