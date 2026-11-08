using Godot;
using System.Collections.Generic;

// RiderDuel module: cockpit panel, radar readouts, cooldown rows, droop.
// Split from the single-file build (round 22 cleanup); the class
// stays one partial class — see RiderDuel.cs for the field block.
public partial class RiderDuel
{
    private static FontFile _hudFont; // IBM Plex Mono Medium, loaded once

    private static Label MakeLabel(int fontSize, Color col)
    {
        var l = new Label { Text = "" };
        // Dashboard text is BOLDER than the game's Light default
        // (owner): every HUD label renders in Plex Mono Medium.
        // Mono advance is identical across weights, so no layout
        // shifts — only the stroke gets heavier.
        _hudFont ??= GD.Load<FontFile>("res://assets/fonts/IBMPlexMono-Medium.ttf");
        if (_hudFont != null) l.AddThemeFontOverride("font", _hudFont);
        l.AddThemeFontSizeOverride("font_size", fontSize);
        l.AddThemeColorOverride("font_color", col);
        // Dashboard text carries a hard shadow (owner: HUD text
        // washed out against bright ground without one).
        l.AddThemeColorOverride("font_shadow_color", new Color(0f, 0f, 0f, 0.9f));
        l.AddThemeConstantOverride("shadow_offset_x", 1);
        l.AddThemeConstantOverride("shadow_offset_y", 1);
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
                // Enemy marker text is WHITE (owner: the salmon
                // washed out against bright ground); the black
                // outline stroke stays. Home keeps its green.
                var l = MakeLabel(13, i == 0 ? new Color(0.55f, 0.95f, 0.6f) : new Color(1f, 1f, 1f));
                l.AddThemeConstantOverride("outline_size", 8);
                l.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
                l.Visible = false;
                l.MouseFilter = Control.MouseFilterEnum.Ignore;
                layer.AddChild(l);
                _baseMarks[i] = l;
            }
        }
        // ---- Cockpit info blocks in the screen corners (owner:
        // no more bottom dashboard): weapon groups top-left, own
        // integrity bottom-left, target/systems bottom-right,
        // radar top-right. Each block is its own panel, fitted to
        // its content every frame (FitCornerPanels).
        PanelContainer CornerPanel(Control.LayoutPreset preset)
        {
            var p = new PanelContainer();
            p.SetAnchorsPreset(preset);
            var sb = new StyleBoxFlat
            {
                BgColor = new Color(0.055f, 0.075f, 0.105f, 0.58f),
                BorderColor = new Color(0.5f, 0.56f, 0.62f, 0.55f),
                ContentMarginLeft = 16, ContentMarginRight = 16,
                ContentMarginTop = 10, ContentMarginBottom = 10,
            };
            sb.SetBorderWidthAll(2);
            sb.SetCornerRadiusAll(8);
            p.AddThemeStyleboxOverride("panel", sb);
            layer.AddChild(p);
            return p;
        }
        _panelWeapons = CornerPanel(Control.LayoutPreset.TopLeft);
        _panelHealth = CornerPanel(Control.LayoutPreset.BottomLeft);
        _panelTarget = CornerPanel(Control.LayoutPreset.BottomRight);

        // Radar scope, top-right (owner): player-centered, north-up.
        _radar = new RadarView
        {
            Player = _player,
            Enemy = _enemy,
            CustomMinimumSize = new Vector2(168f, 168f)
        };
        _radar.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        _radar.OffsetLeft = -184f;
        _radar.OffsetTop = 16f;
        _radar.OffsetRight = -16f;
        _radar.OffsetBottom = 184f;
        _radar.MouseFilter = Control.MouseFilterEnum.Ignore;
        layer.AddChild(_radar);


        Label Title(string t)
        {
            var l = MakeLabel(12, new Color(0.62f, 0.7f, 0.78f));
            l.Text = t;
            return l;
        }

        // -- Left: own rider integrity + per-part status grid.
        var leftBox = new VBoxContainer { CustomMinimumSize = new Vector2(300, 0) }; // the shared dashboard width (owner: all panels match the target block)
        leftBox.AddThemeConstantOverride("separation", 4);
        _panelHealth.AddChild(leftBox);
        _lblPlayerHp = MakeLabel(16, new Color(0.85f, 0.95f, 0.85f));
        leftBox.AddChild(_lblPlayerHp);
        _barPlayer = MakeBar(new Color(0.3f, 0.85f, 0.32f), new Vector2(300, 20));
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
            var bar = MakeBar(new Color(0.45f, 0.75f, 0.5f), new Vector2(67, 8)); // 4 cells + gaps = the 300 dashboard width
            cell.AddChild(bar);
            grid.AddChild(cell);
            _partCells[id] = (lab, bar);
        }


        // -- Center: weapons. The selected row is marked with a pointer
        // and bright text (set in UpdateHud); the hint line teaches the
        // switching scheme in-game.
        var midBox = new VBoxContainer { CustomMinimumSize = new Vector2(300, 0) };
        midBox.AddThemeConstantOverride("separation", 4);
        _panelWeapons.AddChild(midBox);
        midBox.AddChild(Title("WEAPONS"));
        (Label, ProgressBar, TriMark) CdRow(string name)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            var lab = MakeLabel(14, new Color(0.9f, 0.9f, 0.9f, 0.9f));
            lab.Text = name;
            lab.CustomMinimumSize = new Vector2(168, 0); // one width for all rows; group compositions are longer than the old type tags
            lab.ClipText = true;
            row.AddChild(lab);
            var bar = MakeBar(new Color(0.95f, 0.8f, 0.3f), new Vector2(218, 13));
            bar.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            // Every bar stretches to the row's right edge (owner:
            // the bars' start and end X must match across rows —
            // a fixed label width plus an expanding bar leaves no
            // per-row drift from longer weapon names).
            bar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(bar);
            // The active-weapon triangle sits at the END of the
            // bar (owner) — a fixed slot after the bar, so rows
            // never shift when the selection moves. It is a DRAWN
            // polygon, not a font glyph: the glyph's ink sat
            // ~1.5px below the bar's center inside its line box
            // however the label was aligned (measured), while the
            // drawn triangle centers on the bar's own box exactly.
            var mark = new TriMark();
            mark.CustomMinimumSize = new Vector2(16, 13);
            mark.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(mark);
            midBox.AddChild(row);
            return (lab, bar, mark);
        }
        // One row per fitted WEAPON (owner, MW4-style): the
        // group number cell, the weapon's name, and its own
        // cooldown bar. Rows are pooled; UpdateHud fills them
        // in group order.
        for (int wi = 0; wi < MaxWeaponRows; wi++)
        {
            var wrow = new HBoxContainer();
            wrow.AddThemeConstantOverride("separation", 6);
            var gcell = MakeLabel(14, new Color(0.62f, 0.85f, 0.95f));
            gcell.CustomMinimumSize = new Vector2(20, 0);
            gcell.HorizontalAlignment = HorizontalAlignment.Center;
            wrow.AddChild(gcell);
            var wlab = MakeLabel(14, CdLabelNormal);
            wlab.CustomMinimumSize = new Vector2(144, 0);
            wlab.ClipText = true;
            wrow.AddChild(wlab);
            var wbar = MakeBar(new Color(0.95f, 0.8f, 0.3f), new Vector2(150, 13));
            wbar.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            wbar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            wrow.AddChild(wbar);
            var wmark = new TriMark { CustomMinimumSize = new Vector2(16, 13) };
            wmark.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            wrow.AddChild(wmark);
            midBox.AddChild(wrow);
            _wpnGroupCells[wi] = gcell; _wpnLabels[wi] = wlab;
            _wpnBars[wi] = wbar; _wpnMarks[wi] = wmark; _wpnRows[wi] = wrow;
        }
        var rowThr = CdRow("THRUST");
        _lblThr = rowThr.Item1; _cdBarThr = rowThr.Item2;
        // No controls hint line (owner: remove the introductory
        // text) — the panel ends at the THRUST row.
        _lblWeaponStatus = MakeLabel(12, new Color(1f, 0.42f, 0.32f));
        _lblWeaponStatus.AutowrapMode = TextServer.AutowrapMode.Word;
        midBox.AddChild(_lblWeaponStatus);


        // -- Right: target + own systems readouts.
        var rightBox = new VBoxContainer { CustomMinimumSize = new Vector2(300, 0) };
        rightBox.AddThemeConstantOverride("separation", 4);
        _panelTarget.AddChild(rightBox);
        _lblEnemyHp = MakeLabel(16, new Color(0.98f, 0.85f, 0.8f));
        rightBox.AddChild(_lblEnemyHp);
        _barEnemy = MakeBar(new Color(0.88f, 0.3f, 0.24f), new Vector2(300, 20));
        rightBox.AddChild(_barEnemy);
        // Second opponent (free-for-all): a compact readout under
        // the primary target's. Hidden when the foe is not fielded
        // (the precision test modes).
        _lblFoeHp = MakeLabel(13, new Color(0.98f, 0.85f, 0.8f));
        rightBox.AddChild(_lblFoeHp);
        _barFoe = MakeBar(new Color(0.88f, 0.45f, 0.24f), new Vector2(300, 12));
        rightBox.AddChild(_barFoe);
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

        // Leave-game confirmation (owner): Esc opens it, the
        // sim pauses behind it, YES exits the match. Styled
        // after the owner's reference: dark panel, white corner
        // brackets, white text, flat YES (dark) / NO (white)
        // buttons with NO as the safe default.
        _leaveDialog = new LeaveOverlay();
        _leaveDialog.LeaveRequested += OnLeaveConfirmed;
        _leaveDialog.Hidden += () => { if (GetTree().Paused) GetTree().Paused = false; };
        layer.AddChild(_leaveDialog);

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

    // The active-weapon marker: a triangle drawn centered in
    // its own rect (which is the cooldown bar's exact box), so
    // its center is the bar's center by construction.
    private sealed partial class TriMark : Control
    {
        public bool On;
        public override void _Draw()
        {
            if (!On) return;
            var c = Size / 2f;
            // Points LEFT, at the bar it marks (owner).
            DrawColoredPolygon(new[]
            {
                new Vector2(c.X + 4f, c.Y - 5f),
                new Vector2(c.X + 4f, c.Y + 5f),
                new Vector2(c.X - 5f, c.Y)
            }, new Color(1f, 0.95f, 0.6f));
        }
    }

    // The four white corner brackets of the exit popup,
    // drawn proud of the panel's corners (owner's reference).
    private sealed partial class CornerFrame : Control
    {
        public override void _Draw()
        {
            var col = new Color(1f, 1f, 1f, 0.9f);
            float L = 18f, w = 2f;
            var s = Size;
            DrawLine(new Vector2(1, L), new Vector2(1, 1), col, w);
            DrawLine(new Vector2(1, 1), new Vector2(L, 1), col, w);
            DrawLine(new Vector2(s.X - L, 1), new Vector2(s.X - 1, 1), col, w);
            DrawLine(new Vector2(s.X - 1, 1), new Vector2(s.X - 1, L), col, w);
            DrawLine(new Vector2(s.X - 1, s.Y - L), new Vector2(s.X - 1, s.Y - 1), col, w);
            DrawLine(new Vector2(s.X - 1, s.Y - 1), new Vector2(s.X - L, s.Y - 1), col, w);
            DrawLine(new Vector2(1, s.Y - 1), new Vector2(L, s.Y - 1), col, w);
            DrawLine(new Vector2(1, s.Y - L), new Vector2(1, s.Y - 1), col, w);
        }
    }

    // The exit confirmation (owner's reference): dimmed game
    // behind a dark panel with white corner brackets, white
    // title and subtitle, and two flat wide buttons — YES dark
    // on the left, NO white on the right and focused by default.
    // Runs on ProcessMode.Always so it works while the tree is
    // paused behind it; Esc inside it means NO.
    private sealed partial class LeaveOverlay : Control
    {
        public event System.Action LeaveRequested;
        private Button _noBtn;
        private ulong _openedAt;

        public LeaveOverlay()
        {
            ProcessMode = ProcessModeEnum.Always;
            Visible = false;
            SetAnchorsPreset(LayoutPreset.FullRect);
            MouseFilter = MouseFilterEnum.Stop;

            var dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.45f) };
            dim.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(dim);

            var center = new CenterContainer();
            center.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(center);

            var root = new Control { CustomMinimumSize = new Vector2(452, 204) };
            center.AddChild(root);

            var panel = new PanelContainer();
            panel.SetAnchorsPreset(LayoutPreset.FullRect);
            // Some transparency (owner): the game ghosts
            // through the panel; buttons stay solid.
            panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.015f, 0.03f, 0.07f, 0.85f) });
            root.AddChild(panel);

            var v = new VBoxContainer();
            v.SetAnchorsPreset(LayoutPreset.FullRect);
            v.OffsetLeft = 30; v.OffsetTop = 28; v.OffsetRight = -30; v.OffsetBottom = -26;
            v.AddThemeConstantOverride("separation", 10);
            root.AddChild(v);

            var title = new Label { Text = "Exit the game?", HorizontalAlignment = HorizontalAlignment.Center };
            title.AddThemeFontSizeOverride("font_size", 21);
            title.AddThemeColorOverride("font_color", Colors.White);
            v.AddChild(title);
            var sub = new Label { Text = "Any unsaved progress will be lost.", HorizontalAlignment = HorizontalAlignment.Center };
            sub.AddThemeFontSizeOverride("font_size", 13);
            sub.AddThemeColorOverride("font_color", Colors.White);
            v.AddChild(sub);
            v.AddChild(new Control { CustomMinimumSize = new Vector2(0, 16) });

            var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
            row.AddThemeConstantOverride("separation", 18);
            v.AddChild(row);
            var yes = MakeBtn("YES", dark: true);
            yes.Pressed += () => LeaveRequested?.Invoke();
            row.AddChild(yes);
            _noBtn = MakeBtn("NO", dark: false);
            _noBtn.Pressed += () => Visible = false;
            row.AddChild(_noBtn);

            var frame = new CornerFrame { MouseFilter = MouseFilterEnum.Ignore };
            frame.SetAnchorsPreset(LayoutPreset.FullRect);
            frame.OffsetLeft = -7; frame.OffsetTop = -7; frame.OffsetRight = 7; frame.OffsetBottom = 7;
            root.AddChild(frame);
        }

        private static Button MakeBtn(string text, bool dark)
        {
            var b = new Button { Text = text, CustomMinimumSize = new Vector2(148, 38) };
            b.AddThemeFontSizeOverride("font_size", 15);
            var bg = dark ? new Color(0.03f, 0.03f, 0.04f) : new Color(0.93f, 0.93f, 0.94f);
            var fg = dark ? Colors.White : new Color(0.05f, 0.05f, 0.06f);
            StyleBoxFlat S(Color c) => new() { BgColor = c };
            b.AddThemeStyleboxOverride("normal", S(bg));
            b.AddThemeStyleboxOverride("hover", S(dark ? new Color(0.13f, 0.13f, 0.15f) : Colors.White));
            b.AddThemeStyleboxOverride("pressed", S(dark ? new Color(0.22f, 0.22f, 0.24f) : new Color(0.78f, 0.78f, 0.8f)));
            b.AddThemeStyleboxOverride("focus", S(bg));
            b.AddThemeColorOverride("font_color", fg);
            b.AddThemeColorOverride("font_hover_color", fg);
            b.AddThemeColorOverride("font_pressed_color", fg);
            b.AddThemeColorOverride("font_focus_color", fg);
            return b;
        }

        public void Open()
        {
            _openedAt = Time.GetTicksMsec();
            Visible = true;
            _noBtn.GrabFocus();
        }

        public override void _Input(InputEvent e)
        {
            if (!Visible) return;
            // The Esc that OPENED the popup reaches this handler
            // in the same dispatch (parent first, then children)
            // — ignore Esc for a beat after opening.
            if (Time.GetTicksMsec() - _openedAt < 200) return;
            if (e is InputEventKey k && k.Pressed && !k.Echo && k.Keycode == Key.Escape)
            {
                Visible = false;
                GetViewport().SetInputAsHandled();
            }
        }
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

    private void OnLeaveConfirmed()
    {
        GetTree().Paused = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        if (FlowMain.FromFlow)
        {
            FlowMain.StartScreen = null; // back to the front door
            GetTree().ChangeSceneToFile("res://Flow/Flow.tscn");
        }
        else GetTree().Quit();
    }

    private void UpdateBaseMarks()
    {
        if (_baseMarks == null) return;
        bool show = _panelHealth.Visible && !_labOpen && _cam != null;
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
        // Group aliveness (owner's lab groups): a group is
        // dead when every mounting part that carries it is lost.
        var grpDead = new bool[_groups.Count];
        for (int gi = 0; gi < _groups.Count; gi++) grpDead[gi] = !WeaponAlive(gi);

        // ONE active enemy (owner): Tab cycles the living foes,
        // and the red bar belongs to the active one only — and
        // even then it appears only while at least one living
        // weapon group can reach that enemy.
        var foes = new (RiderSpec Spec, float Hp, bool Dead, RiderWalker W)[]
        {
            (_enemySpec, _hpEnemy, _enemyDead, _enemy),
            _foe != null ? (_foe.Spec, _foe.Hp, _foe.Dead, _foe.Walker) : (null, 0f, true, null)
        };
        if (foes[_targetIdx].Dead)
            for (int i = 0; i < 2; i++)
                if (!foes[i].Dead) { _targetIdx = i; break; }
        var tgt = foes[_targetIdx];
        float tgtRange = tgt.W != null
            ? new Vector2(tgt.W.RootPos.X - _player.RootPos.X,
                          tgt.W.RootPos.Z - _player.RootPos.Z).Length()
            : float.MaxValue;
        float reach = 0f;
        for (int gi = 0; gi < _groups.Count; gi++)
        {
            if (grpDead[gi]) continue;
            foreach (var g in _groups[gi].Guns)
                if (_partsPlayer.Alive(g.Slot))
                    reach = Mathf.Max(reach, g.Def.Range);
        }
        bool tgtShown = tgt.W != null && !tgt.Dead && tgtRange <= reach;
        _lblEnemyHp.Visible = tgtShown;
        _barEnemy.Visible = tgtShown;
        if (tgtShown)
        {
            _lblEnemyHp.Text = $"{tgt.Spec.Name} — {tgt.Hp:F0} HP";
            _barEnemy.Value = tgt.Hp / tgt.Spec.MaxHp;
        }
        // The second foe's own readout is retired — one target only.
        _lblFoeHp.Visible = false;
        _barFoe.Visible = false;
        // Per-weapon rows (owner, MW4-style): group number,
        // name, and the weapon's OWN cooldown bar; a destroyed
        // mount's weapons read red and empty. The selection
        // triangle rides the selected group's first live row.
        int wi = 0;
        bool markPlaced = false;
        for (int gi = 0; gi < _groups.Count && wi < MaxWeaponRows; gi++)
        {
            var grp = _groups[gi];
            foreach (var gun in grp.Guns)
            {
                if (wi >= MaxWeaponRows) break;
                bool dead = !_partsPlayer.Alive(gun.Slot);
                bool sel = _weaponSel == gi;
                _wpnRows[wi].Visible = true;
                _wpnGroupCells[wi].Text = grp.Num.ToString();
                _wpnGroupCells[wi].AddThemeColorOverride("font_color",
                    sel ? new Color(1f, 0.85f, 0.3f) : new Color(0.62f, 0.85f, 0.95f));
                _wpnLabels[wi].Text = gun.Def.Name;
                float wfrac = gun.Def.Cooldown > 0f ? 1f - gun.Cd / gun.Def.Cooldown : 1f;
                UpdateCdRow(_wpnBars[wi], _wpnLabels[wi], wfrac, dead, grp.DenyT);
                if (sel && !dead)
                    _wpnLabels[wi].AddThemeColorOverride("font_color", new Color(1f, 0.95f, 0.6f));
                bool mark = sel && !markPlaced;
                if (_wpnMarks[wi].On != mark) { _wpnMarks[wi].On = mark; _wpnMarks[wi].QueueRedraw(); }
                if (mark) markPlaced = true;
                wi++;
            }
        }
        for (; wi < MaxWeaponRows; wi++) _wpnRows[wi].Visible = false;
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

        // Systems readouts. TARGET RANGE is a reticle
        // rangefinder (owner): the distance from the rider to
        // whatever the crosshair rests on — building, tree,
        // enemy, ground — not just the active target. The cast
        // runs far past weapon reach (1500u) so Mission
        // sightlines read true; past that it reports the cap.
        // Non-chase views (harness cameras) keep the old
        // active-target reading.
        if (_camMode == CamModes.Chase && _cam != null)
        {
            var rHit = _hit.Ray(_cam.GlobalPosition, AimDirWorld(), Aim.FarRange, Roles.Player);
            _lblRange.Text = rHit.Hit
                ? $"TARGET RANGE: {_player.RootPos.DistanceTo(rHit.Point):F1} u"
                : $"TARGET RANGE: {Aim.FarRange:0}+ u";
        }
        else
            _lblRange.Text = tgt.W == null || tgt.Dead ? "TARGET: DESTROYED" : $"TARGET RANGE: {tgtRange:F1} u";
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
            _radar.Enemy2 = _foe?.Walker;
            _radar.Enemy2Down = _foe?.Dead ?? true;
            _radar.QueueRedraw();
        }
        if (_player.IsKnockedDown)
        {
            _lblLegs.Text = "STATUS: DOWN — LEG LOST";
            _lblLegs.AddThemeColorOverride("font_color", new Color(1f, 0.42f, 0.32f));
        }

        var status = new System.Collections.Generic.List<string>();
        for (int gi = 0; gi < _groups.Count; gi++)
            if (grpDead[gi]) status.Add($"GROUP {_groups[gi].Num} DESTROYED — ALL MOUNTS LOST");
        // Pod status: only for pods the chassis actually HAS (the
        // Hector carries a single rack — an absent right pod is not
        // a "lost" one). Wording follows the equipped pod weapon.
        string podName = (_defPodL ?? _defPodR)?.Short ?? "LRM";
        bool hasPodL = _partsPlayer.Has("pod_L"), hasPodR = _partsPlayer.Has("pod_R");
        int podRocketsL = 0, podRocketsR = 0;
        foreach (var g in _podGunsL) podRocketsL += g.Def.Rockets;
        foreach (var g in _podGunsR) podRocketsR += g.Def.Rockets;
        if (_defPodL != null && hasPodL && !_partsPlayer.Alive("pod_L") && (podRocketsR > 0 || !hasPodR)) status.Add($"{podName} DEGRADED — LEFT POD LOST ({podRocketsL} ROCKETS)");
        else if (_defPodR != null && hasPodR && !_partsPlayer.Alive("pod_R") && (podRocketsL > 0 || !hasPodL)) status.Add($"{podName} DEGRADED — RIGHT POD LOST ({podRocketsR} ROCKETS)");
        if (!_partsPlayer.Alive("head")) status.Add("SENSORS DESTROYED — CROSSHAIR SWAY");
        _lblWeaponStatus.Text = string.Join("\n", status);

        _feedLabel.Text = string.Join("\n",
            System.Linq.Enumerable.Select(_feed, e => e.Text));

        // Head-loss HUD disturbance: while the flicker timer runs, the
        // whole HUD stutters (seeded, visual only).
        float alpha = 1f;
        if (_hudFlickerT > 0f)
            alpha = _rng.NextDouble() < 0.38 ? 0.15f : 1f;
        foreach (var c in new Control[] { _panelHealth, _panelWeapons, _panelTarget, _feedLabel, _cross })
        {
            var m = c.Modulate;
            m.A = alpha;
            c.Modulate = m;
        }
        FitCornerPanels();
    }

    private void SetHudPanelsVisible(bool v)
    {
        if (_panelHealth != null) _panelHealth.Visible = v;
        if (_panelWeapons != null) _panelWeapons.Visible = v;
        if (_panelTarget != null) _panelTarget.Visible = v;
    }

    // Corner blocks hug their content: widths and heights come
    // from the panels' combined minimum sizes, re-fitted as
    // group rows and status lines come and go. The event feed
    // rides under the weapons block.
    private void FitCornerPanels()
    {
        if (_panelWeapons == null) return;
        // One width for every dashboard panel (owner): the
        // target block's - the narrowest - sets it; heights stay
        // per panel.
        float w = _panelTarget.GetCombinedMinimumSize().X;
        var ws = _panelWeapons.GetCombinedMinimumSize();
        _panelWeapons.OffsetLeft = 16; _panelWeapons.OffsetTop = 14;
        _panelWeapons.OffsetRight = 16 + w; _panelWeapons.OffsetBottom = 14 + ws.Y;
        var hs = _panelHealth.GetCombinedMinimumSize();
        _panelHealth.OffsetLeft = 16; _panelHealth.OffsetBottom = -14;
        _panelHealth.OffsetRight = 16 + w; _panelHealth.OffsetTop = -14 - hs.Y;
        var ts = _panelTarget.GetCombinedMinimumSize();
        _panelTarget.OffsetRight = -16; _panelTarget.OffsetBottom = -14;
        _panelTarget.OffsetLeft = -16 - w; _panelTarget.OffsetTop = -14 - ts.Y;
        if (_feedLabel != null) _feedLabel.OffsetTop = 14 + ws.Y + 10;
    }

    private void UpdateDroop(float dt)
    {
        if (_foe != null && _foe.Dead && _foe.DroopT < 1f)
        {
            _foe.DroopT = Mathf.Min(_foe.DroopT + dt / 1.5f, 1f);
            float t1 = _foe.DroopT;
            _foe.Walker.Droop = t1 * t1 * (3f - 2f * t1) * 0.5f;
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
