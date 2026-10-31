using Godot;
using System.Collections.Generic;

// MechDuel module: cockpit panel, radar readouts, cooldown rows, droop.
// Split from the single-file build (round 22 cleanup); the class
// stays one partial class — see MechDuel.cs for the field block.
public partial class MechDuel
{
    private static Label MakeLabel(int fontSize, Color col)
    {
        var l = new Label { Text = "" };
        l.AddThemeFontSizeOverride("font_size", fontSize);
        l.AddThemeColorOverride("font_color", col);
        return l;
    }

    private void BuildHud(CanvasLayer layer)
    {
        // ---- Cockpit control panel: one console strip along the bottom
        // holding mech integrity, per-part status, weapon charge with the
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

        // -- Left: own mech integrity + per-part status grid.
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
            ("head", "HEAD"), ("arm_L", "ARM L"), ("arm_R", "ARM R"), ("pod_L", "POD L"),
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
        (Label, ProgressBar) CdRow(string name)
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
            midBox.AddChild(row);
            return (lab, bar);
        }
        var rowLaser = CdRow("1 LAS");
        _lblCdLaser = rowLaser.Item1; _cdBarLaser = rowLaser.Item2;
        var rowAc = CdRow("2 AC");
        _lblCdAc = rowAc.Item1; _cdBarAc = rowAc.Item2;
        var rowLrm = CdRow("3 LRM");
        _lblCdLrm = rowLrm.Item1; _cdBarLrm = rowLrm.Item2;
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

    // The selected weapon's panel row carries a pointer marker and, while
    // the weapon lives, bright text; destroyed rows keep their red.
    private static void SetSelLabel(Label lab, string baseText, bool selected, bool destroyed)
    {
        lab.Text = (selected ? "▶ " : "") + baseText;
        if (selected && !destroyed)
            lab.AddThemeColorOverride("font_color", new Color(1f, 0.95f, 0.6f));
    }

    private void UpdateHud()
    {
        if (_barPlayer == null) return;
        _barPlayer.Value = _hpPlayer / _playerSpec.MaxHp;
        _lblPlayerHp.Text = $"{_playerSpec.Name} — {_hpPlayer:F0} HP";
        _barEnemy.Visible = !_enemyDead;
        _lblEnemyHp.Visible = !_enemyDead;
        _barEnemy.Value = _hpEnemy / _enemySpec.MaxHp;
        _lblEnemyHp.Text = $"{_enemySpec.Name} — {_hpEnemy:F0} HP";

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
        SetSelLabel(_lblCdLaser, _wpnShort[0], _weaponSel == 0, lasDead);
        SetSelLabel(_lblCdAc, _wpnShort[1], _weaponSel == 1, acDead);
        SetSelLabel(_lblCdLrm, _wpnShort[2], _weaponSel == 2, lrmDead);

        // Per-part status cells (player mech).
        foreach (var (id, cell) in _partCells)
        {
            var part = _partsPlayer.GetPart(id);
            var cellBox = cell.Lab.GetParent() as Control;
            if (part == null) { if (cellBox != null) cellBox.Visible = false; continue; } // chassis lacks this part (Atlas: no pods)
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
        if (_radar != null) { _radar.EnemyDown = _enemyDead; _radar.QueueRedraw(); }
        if (_player.IsKnockedDown)
        {
            _lblLegs.Text = "STATUS: DOWN — LEG LOST";
            _lblLegs.AddThemeColorOverride("font_color", new Color(1f, 0.42f, 0.32f));
        }

        var status = new System.Collections.Generic.List<string>();
        if (lasDead && _defEnergy != null) status.Add("LASER DESTROYED — LEFT ARM LOST");
        if (acDead && _defBallistic != null) status.Add("AUTOCANNON DESTROYED — RIGHT ARM LOST");
        if (lrmDead && _defPodL != null && _defPodR != null) status.Add("LRM DESTROYED — BOTH PODS LOST");
        else if ((_defPodL != null || _defPodR != null) && !_partsPlayer.Alive("pod_L")) status.Add("LRM DEGRADED — LEFT POD LOST (4 ROCKETS)");
        else if ((_defPodL != null || _defPodR != null) && !_partsPlayer.Alive("pod_R")) status.Add("LRM DEGRADED — RIGHT POD LOST (4 ROCKETS)");
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

    // Atlas AC/20 gunnery: range + line-of-sight gated, leads the player's
    // velocity at time-of-flight, seeded ±1.2° inaccuracy cone.
}
