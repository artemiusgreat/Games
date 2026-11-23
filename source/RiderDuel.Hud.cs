using Godot;
using System.Collections.Generic;

// RiderDuel module: cockpit panel, radar readouts, cooldown rows, droop.
// Split from the single-file build (round 22 cleanup); the class
// stays one partial class - see RiderDuel.cs for the field block.
public partial class RiderDuel
{
    private static FontFile _hudFont; // IBM Plex Mono Medium, loaded once

    private static Label MakeLabel(int fontSize, Color col)
    {
        var l = new Label { Text = "" };
        // Dashboard text is BOLDER than the game's default
        // (owner): every HUD label renders in Open Sans Semibold
        // (the game-wide font change, 2026-11-13 - it succeeds
        // Plex Mono Medium in this role).
        _hudFont ??= GD.Load<FontFile>("res://assets/fonts/OpenSans-SemiBold.ttf");
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

    // No base markers at all (owner, 2026-11-11: the enemy
    // marker went in round 2, 'remove Home base indicator'
    // in round 3 - the projected base labels are gone).

    // Team HP rows inside the health panel (owner, 2026-11-14):
    // one compact name + bar block per NPC teammate, stacked
    // under the player's own block in the same idiom.
    private readonly (Label Lab, ProgressBar Bar, VBoxContainer Box)[] _teamHpRows =
        new (Label, ProgressBar, VBoxContainer)[3];

    private void BuildHud(CanvasLayer layer)
    {
        // ---- Cockpit info blocks in the screen corners (owner:
        // no more bottom dashboard): weapon groups top-left, own
        // integrity bottom-left, target/systems bottom-right,
        // radar top-right. Each block is its own panel, fitted to
        // its content every frame (FitCornerPanels).
        PanelContainer CornerPanel(Control.LayoutPreset preset, Color accent)
        {
            var p = new ChromePanel { Accent = accent };
            p.SetAnchorsPreset(preset);
            var sb = new StyleBoxFlat
            {
                BgColor = new Color(0, 0, 0, 0),
                ContentMarginLeft = 16, ContentMarginRight = 16,
                ContentMarginTop = 12, ContentMarginBottom = 12,
            };
            p.AddThemeStyleboxOverride("panel", sb);
            layer.AddChild(p);
            return p;
        }
        _panelWeapons = CornerPanel(Control.LayoutPreset.TopLeft, HudCyan);
        _panelHealth = CornerPanel(Control.LayoutPreset.BottomLeft, HudCyan);
        _panelTarget = CornerPanel(Control.LayoutPreset.BottomRight, HudRed);

        // Team-command feed, top-left: rides under the weapons
        // block (owner, 2026-11-21 - restored for team orders
        // only; the 2026-11-20 event feed stays removed).
        _feedLabel = MakeLabel(15, new Color(0.98f, 0.88f, 0.62f));
        _feedLabel.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        _feedLabel.OffsetLeft = 18; _feedLabel.OffsetTop = 14;
        _feedLabel.OffsetRight = 560; _feedLabel.OffsetBottom = 130;
        layer.AddChild(_feedLabel);

        // Radar, top-right (owner): a SQUARE semi-transparent
        // panel the same width as the range/speed block below
        // it, with the exit popup's white corner brackets. The
        // scope stays circular and north-up inside it.
        _panelRadar = new ChromePanel { Accent = HudCyan };
        _panelRadar.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        var rsb = new StyleBoxFlat
        {
            BgColor = new Color(0, 0, 0, 0),
            ContentMarginLeft = 16, ContentMarginRight = 16,
            ContentMarginTop = 12, ContentMarginBottom = 12,
        };
        _panelRadar.AddThemeStyleboxOverride("panel", rsb);
        layer.AddChild(_panelRadar);
        var radarBox = new VBoxContainer();
        radarBox.AddThemeConstantOverride("separation", 8);
        _panelRadar.AddChild(radarBox);
        radarBox.AddChild(PanelHeader("RADAR", 1, HudCyan));
        _radar = new RadarView
        {
            Player = _player,
            Enemy = _enemy,
            CustomMinimumSize = new Vector2(300f, 300f),
            Range = WorldConfig.IsMission ? 600f : 160f
        };
        _radar.MouseFilter = Control.MouseFilterEnum.Ignore;
        radarBox.AddChild(_radar);
        // No outer corner-bracket frame around the radar
        // (owner, 2026-11-11 round 4: 'remove one external
        // border around radar') - the panel's own border is
        // the only frame now.
        _targetFrame = new TargetFrame { MouseFilter = Control.MouseFilterEnum.Ignore };
        _targetFrame.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(_targetFrame);


        // Panel header in the owner's example style: a line-icon
        // badge, the panel name in pale caps, and a hairline in
        // the panel's accent under it.
        HBoxContainer PanelHeader(string t, int icon, Color accent)
        {
            var h = new HBoxContainer();
            h.AddThemeConstantOverride("separation", 8);
            h.AddChild(new IconMark { Kind = icon, Accent = accent, CustomMinimumSize = new Vector2(20, 20), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
            var l = MakeLabel(15, new Color(0.90f, 0.96f, 1.0f));
            l.Text = t;
            l.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            h.AddChild(l);
            return h;
        }
        Control PanelDivider(Color accent)
        {
            // Extra air above and below the hairline (owner,
            // 2026-11-22): the title row and the body should
            // not crowd the separator.
            var m = new MarginContainer();
            m.AddThemeConstantOverride("margin_top", 5);
            m.AddThemeConstantOverride("margin_bottom", 5);
            m.AddChild(new ColorRect { Color = new Color(accent, 0.35f), CustomMinimumSize = new Vector2(0, 1) });
            return m;
        }

        // -- Left: own rider integrity + per-part status grid.
        var leftBox = new VBoxContainer { CustomMinimumSize = new Vector2(300, 0) }; // the shared dashboard width (owner: all panels match the target block)
        leftBox.AddThemeConstantOverride("separation", 4);
        _panelHealth.AddChild(leftBox);
        leftBox.AddChild(PanelHeader("STATUS", 2, HudCyan));
        leftBox.AddChild(PanelDivider(HudCyan));
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
        // Teammates under the player's own block (owner,
        // 2026-11-14): name line + pool bar each, filled per
        // frame in UpdateHud; hidden while the team is solo.
        for (int i = 0; i < _teamHpRows.Length; i++)
        {
            var tbox = new VBoxContainer();
            tbox.AddThemeConstantOverride("separation", 1);
            var tlab = MakeLabel(13, new Color(0.85f, 0.95f, 0.85f));
            tbox.AddChild(tlab);
            var tbar = MakeBar(new Color(0.3f, 0.85f, 0.32f), new Vector2(300, 12));
            tbox.AddChild(tbar);
            tbox.Visible = false;
            leftBox.AddChild(tbox);
            _teamHpRows[i] = (tlab, tbar, tbox);
        }


        // -- Center: weapons. The selected row is marked with a pointer
        // and bright text (set in UpdateHud); the hint line teaches the
        // switching scheme in-game.
        var midBox = new VBoxContainer { CustomMinimumSize = new Vector2(300, 0) };
        midBox.AddThemeConstantOverride("separation", 4);
        _panelWeapons.AddChild(midBox);
        midBox.AddChild(PanelHeader("WEAPONS", 0, HudCyan));
        midBox.AddChild(PanelDivider(HudCyan));
        (Label, ProgressBar, TriMark) CdRow(string name)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            var lab = MakeLabel(14, new Color(0.9f, 0.9f, 0.9f, 0.9f));
            lab.Text = name;
            lab.CustomMinimumSize = new Vector2(136, 0); // THRUST row: sized so its bar starts on the weapon bars' line (112 name + digit cell)
            lab.ClipText = true;
            lab.HorizontalAlignment = HorizontalAlignment.Right; // panel labels right-aligned (owner, 2026-11-22)
            row.AddChild(lab);
            var bar = MakeBar(new Color(0.95f, 0.8f, 0.3f), new Vector2(100, 13));
            bar.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            // Every bar stretches to the row's right edge (owner:
            // the bars' start and end X must match across rows -
            // a fixed label width plus an expanding bar leaves no
            // per-row drift from longer weapon names).
            bar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(bar);
            // The active-weapon triangle sits at the END of the
            // bar (owner) - a fixed slot after the bar, so rows
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
            wlab.CustomMinimumSize = new Vector2(112, 0); // tight to the longest name (owner: less space before the bar)
            wlab.ClipText = true;
            wlab.HorizontalAlignment = HorizontalAlignment.Right; // panel labels right-aligned (owner, 2026-11-22)
            wrow.AddChild(wlab);
            var wbar = MakeBar(new Color(0.95f, 0.8f, 0.3f), new Vector2(100, 13));
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
        // text) - the panel ends at the THRUST row.
        _lblWeaponStatus = MakeLabel(12, new Color(1f, 0.42f, 0.32f));
        _lblWeaponStatus.AutowrapMode = TextServer.AutowrapMode.Word;
        midBox.AddChild(_lblWeaponStatus);


        // -- Right: target + own systems readouts.
        var rightBox = new VBoxContainer { CustomMinimumSize = new Vector2(300, 0) };
        rightBox.AddThemeConstantOverride("separation", 4);
        _panelTarget.AddChild(rightBox);
        rightBox.AddChild(PanelHeader("TARGET", 3, HudRed));
        rightBox.AddChild(PanelDivider(HudRed));
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
        // No LEGS line in this panel (owner, 2026-11-22: the
        // "Legs: OK" readout is removed).

        // Leave-game confirmation (owner): Esc opens it, the
        // sim pauses behind it, YES exits the match. Styled
        // after the owner's reference: dark panel, white corner
        // brackets, white text, flat YES (dark) / NO (white)
        // buttons with NO as the safe default.
        _leaveDialog = new LeaveOverlay();
        _leaveDialog.LeaveRequested += OnLeaveConfirmed;
        _leaveDialog.Hidden += () => { if (GetTree().Paused) GetTree().Paused = false; RecaptureMouse(); };
        layer.AddChild(_leaveDialog);

        // The controls screen rides the same contract too.
        _controlsDialog = new ControlsOverlay();
        _controlsDialog.Hidden += () => { if (GetTree().Paused) GetTree().Paused = false; RecaptureMouse(); };
        layer.AddChild(_controlsDialog);

        // The team screen rides the same pause/unpause contract.
        _teamDialog = new TeamOverlay();
        _teamDialog.LeaderChosen += slot =>
        {
            SetLeader(slot);
        };
        _teamDialog.LeaveRequested += OnLeaveConfirmed;
        _teamDialog.Hidden += () => { if (GetTree().Paused) GetTree().Paused = false; RecaptureMouse(); };
        layer.AddChild(_teamDialog);

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

    // HUD accent colours for the panel chrome (owner's example,
    // 2026-11-22): cyan for own-side panels, red for the target.
    private static readonly Color HudCyan = new(0.30f, 0.80f, 0.95f);
    private static readonly Color HudRed = new(1.0f, 0.35f, 0.22f);

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

    // HUD panel chrome in the owner's example style (2026-11-22):
    // deep-navy glass, clipped top-left and bottom-right corners,
    // and a glowing accent frame (cyan; red for the target panel).
    // The panel's own stylebox is transparent - this _Draw paints
    // the whole frame under the content.
    private sealed partial class ChromePanel : PanelContainer
    {
        public Color Accent = new(0.30f, 0.80f, 0.95f);

        public override void _Notification(int what)
        {
            if (what == NotificationResized) QueueRedraw();
        }

        public override void _Draw()
        {
            // Rounded corners (owner, 2026-11-23: 'make borders
            // round for all 4 sections in the corners of active
            // game' - the clipped corners are retired). The
            // frame stays the standing spec: one solid thin
            // line at half alpha (2026-11-22).
            var sb = new StyleBoxFlat
            {
                BgColor = new Color(0.010f, 0.028f, 0.058f, 0.84f),
                BorderColor = new Color(Accent, 0.5f),
                AntiAliasing = true,
            };
            sb.SetBorderWidthAll(1);
            sb.SetCornerRadiusAll(12);
            DrawStyleBox(sb, new Rect2(Vector2.Zero, Size));
        }
    }

    // Small line icons for the panel headers: uniform round
    // badges of one size (owner, 2026-11-23), glyphs inside:
    // 0 weapons (lines), 1 radar (fan + sweep), 2 status
    // (check), 3 target (reticle).
    private sealed partial class IconMark : Control
    {
        public int Kind;
        public Color Accent = new(0.30f, 0.80f, 0.95f);

        public override void _Draw()
        {
            var c = Size / 2f;
            var col = Accent;
            // Uniform round badges (owner, 2026-11-23: 'use
            // round icons of the same size for all 4 corner
            // sections... I would prefer icons to be more
            // alike' - the elongated shield is gone). Every
            // kind wears the same ring at the same radius; the
            // glyph inside stays within the same inner zone.
            float badge = Mathf.Min(Size.X, Size.Y) * 0.5f - 1f;
            DrawArc(c, badge, 0, Mathf.Tau, 32, col, 1.6f, true);
            switch (Kind)
            {
                case 0: // weapons: three plain lines
                    for (int i = 0; i < 3; i++)
                    {
                        float y = c.Y - 3.2f + i * 3.2f;
                        DrawLine(new Vector2(c.X - 4.2f, y), new Vector2(c.X + 4.2f, y), col, 1.7f, true);
                    }
                    break;
                case 1: // radar: fan + sweep, centred in the badge
                    DrawArc(c, 2.6f, -2.4f, -0.7f, 10, col, 1.6f, true);
                    DrawArc(c, 5.0f, -2.4f, -0.7f, 12, col, 1.6f, true);
                    DrawLine(c, c + new Vector2(3.4f, -3.4f), col, 1.6f, true);
                    DrawCircle(c, 1.3f, col);
                    break;
                case 2: // status: a check (the shield is retired)
                    DrawLine(c + new Vector2(-4f, 0.2f), c + new Vector2(-1.3f, 3f), col, 1.9f, true);
                    DrawLine(c + new Vector2(-1.3f, 3f), c + new Vector2(4.2f, -3.2f), col, 1.9f, true);
                    break;
                default: // target: inner ring, dot, four ticks
                    DrawArc(c, 2.8f, 0, Mathf.Tau, 20, col, 1.6f, true);
                    DrawCircle(c, 1.1f, col);
                    DrawLine(c + new Vector2(-6.2f, 0), c + new Vector2(-4.2f, 0), col, 1.6f, true);
                    DrawLine(c + new Vector2(4.2f, 0), c + new Vector2(6.2f, 0), col, 1.6f, true);
                    DrawLine(c + new Vector2(0, -6.2f), c + new Vector2(0, -4.2f), col, 1.6f, true);
                    DrawLine(c + new Vector2(0, 4.2f), c + new Vector2(0, 6.2f), col, 1.6f, true);
                    break;
            }
        }
    }

    // The active target's in-world frame (owner, 2026-11-11:
    // 'when I have some enemy selected, add a frame around
    // it'; his follow-up the same day: frame ONLY, no marker
    // on top): red corner brackets around the target's
    // projected silhouette. UpdateTargetFrame projects the
    // eight corners of the walker's capsule box each frame.
    private sealed partial class TargetFrame : Control
    {
        public bool Show;
        public Rect2 Box;
        public override void _Draw()
        {
            if (!Show) return;
            var col = new Color(1f, 0.30f, 0.22f, 0.95f);
            float L = Mathf.Min(20f, Box.Size.X * 0.3f), w = 2f;
            var a = Box.Position; var b = Box.Position + Box.Size;
            DrawLine(a + new Vector2(0, L), a, col, w);
            DrawLine(a, a + new Vector2(L, 0), col, w);
            DrawLine(new Vector2(b.X - L, a.Y), new Vector2(b.X, a.Y), col, w);
            DrawLine(new Vector2(b.X, a.Y), new Vector2(b.X, a.Y + L), col, w);
            DrawLine(new Vector2(b.X, b.Y - L), b, col, w);
            DrawLine(b, new Vector2(b.X - L, b.Y), col, w);
            DrawLine(new Vector2(a.X, b.Y), new Vector2(a.X + L, b.Y), col, w);
            DrawLine(new Vector2(a.X, b.Y - L), new Vector2(a.X, b.Y), col, w);
            // Frame only - no marker on top (owner, 2026-11-11:
            // 'remove triangle on top of the enemy, keep only
            // frame'; the chevron from the same day's first
            // cut is gone again).
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
    // title and subtitle, and two flat wide buttons - YES dark
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
            // Cinzel for the popup's title (owner, 2026-11-13);
            // the rest of the game UI is Open Sans.
            title.AddThemeFontOverride("font", FlowUi.HeadFont(true));
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

        internal static Button MakeBtn(string text, bool dark)
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
            // - ignore Esc for a beat after opening.
            if (Time.GetTicksMsec() - _openedAt < 200) return;
            if (e is InputEventKey k && k.Pressed && !k.Echo && k.Keycode == Key.Escape)
            {
                Visible = false;
                GetViewport().SetInputAsHandled();
            }
        }
    }

    // The team screen (owner, teams stage step 4): in a mission,
    // F1 opens THIS (owner, 2026-11-17 - Esc is the exit only)
    // confirmation - the player's riders listed with their
    // state; clicking a living teammate takes command of it
    // (the leader is whoever the player drives), Resume closes,
    // Leave exits. Same visual idiom as the exit popup:
    // dimmed game, dark panel, corner brackets, Cinzel title.
    // Runs on ProcessMode.Always behind the pause. (Owner,
    // 2026-11-14: the rows wear the lab weapon rows' dress -
    // bordered box, framed initials tile, name over a state
    // line; both buttons sit on the right, Leave first; only
    // the title is uppercase, everything else sentence case.)
    private sealed partial class TeamOverlay : Control
    {
        public event System.Action<int> LeaderChosen;
        public event System.Action LeaveRequested;
        private readonly VBoxContainer _rows;
        private readonly Control _root;
        private ulong _openedAt;

        public TeamOverlay()
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

            _root = new Control { CustomMinimumSize = new Vector2(520, 300) };
            center.AddChild(_root);

            var panel = new PanelContainer();
            panel.SetAnchorsPreset(LayoutPreset.FullRect);
            panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.015f, 0.03f, 0.07f, 0.88f) });
            _root.AddChild(panel);

            var v = new VBoxContainer();
            v.SetAnchorsPreset(LayoutPreset.FullRect);
            v.OffsetLeft = 30; v.OffsetTop = 26; v.OffsetRight = -30; v.OffsetBottom = -24;
            v.AddThemeConstantOverride("separation", 10);
            _root.AddChild(v);

            var title = new Label { Text = "TEAM", HorizontalAlignment = HorizontalAlignment.Center };
            title.AddThemeFontOverride("font", FlowUi.HeadFont(true));
            title.AddThemeFontSizeOverride("font_size", 21);
            title.AddThemeColorOverride("font_color", Colors.White);
            v.AddChild(title);
            var sub = new Label
            {
                Text = "Select a rider to take command of it, or leave the mission.",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            sub.AddThemeFontSizeOverride("font_size", 13);
            sub.AddThemeColorOverride("font_color", new Color(0.85f, 0.88f, 0.92f));
            v.AddChild(sub);

            _rows = new VBoxContainer();
            _rows.AddThemeConstantOverride("separation", 8);
            v.AddChild(_rows);

            v.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            v.AddChild(row);
            // Both buttons on the right (owner, 2026-11-14):
            // Leave first, then Resume.
            row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
            var exit = LeaveOverlay.MakeBtn("Leave", dark: false); // same face as Resume (owner, 2026-11-15)
            exit.Pressed += () => LeaveRequested?.Invoke();
            row.AddChild(exit);
            var resume = LeaveOverlay.MakeBtn("Resume", dark: false);
            resume.Pressed += () => Visible = false;
            row.AddChild(resume);

            var frame = new CornerFrame { MouseFilter = MouseFilterEnum.Ignore };
            frame.SetAnchorsPreset(LayoutPreset.FullRect);
            frame.OffsetLeft = -7; frame.OffsetTop = -7; frame.OffsetRight = 7; frame.OffsetBottom = 7;
            _root.AddChild(frame);
        }

        public void Open(System.Collections.Generic.List<(string Name, float HpFrac, bool Dead, bool Leader, int Slot)> members)
        {
            foreach (var ch in _rows.GetChildren()) ch.QueueFree();
            foreach (var m in members)
            {
                int slot = m.Slot;
                string pct = $"{Mathf.RoundToInt(Mathf.Clamp(m.HpFrac, 0f, 1f) * 100f)}%";
                // A lab-style row: bordered box, framed
                // initials tile, name over a state line.
                var gold = new Color(0.98f, 0.80f, 0.35f);
                var steel = new Color(0.5f, 0.56f, 0.62f, 0.65f);
                var ink = new Color(0.92f, 0.94f, 0.96f);
                var dim = new Color(0.55f, 0.58f, 0.62f);
                Color border = m.Leader ? gold : steel;
                Color nameCol = m.Dead ? dim : m.Leader ? gold : ink;
                string state = m.Dead ? "Destroyed"
                    : m.Leader ? $"Leader · HP {pct}"
                    : $"Take command · HP {pct}";
                var b = new Button { CustomMinimumSize = new Vector2(0, 52) };
                StyleBoxFlat S(Color bgc, Color bdc)
                {
                    var sb = new StyleBoxFlat { BgColor = bgc, BorderColor = bdc };
                    sb.SetBorderWidthAll(1);
                    return sb;
                }
                var bg = m.Leader ? new Color(0.16f, 0.13f, 0.04f, 0.95f)
                    : m.Dead ? new Color(0.05f, 0.05f, 0.06f, 0.9f)
                    : new Color(0.08f, 0.11f, 0.17f, 0.95f);
                if (m.Dead || m.Leader)
                {
                    b.Disabled = true;
                    b.AddThemeStyleboxOverride("disabled", S(bg, border));
                }
                else
                {
                    b.AddThemeStyleboxOverride("normal", S(bg, border));
                    b.AddThemeStyleboxOverride("hover", S(new Color(0.14f, 0.18f, 0.26f, 0.98f), gold));
                    b.AddThemeStyleboxOverride("pressed", S(new Color(0.20f, 0.17f, 0.08f, 0.98f), gold));
                    b.AddThemeStyleboxOverride("focus", S(bg, border));
                    b.Pressed += () => { Visible = false; LeaderChosen?.Invoke(slot); };
                }
                var margin = new MarginContainer();
                margin.SetAnchorsPreset(LayoutPreset.FullRect);
                margin.AddThemeConstantOverride("margin_left", 10);
                margin.AddThemeConstantOverride("margin_right", 10);
                margin.AddThemeConstantOverride("margin_top", 6);
                margin.AddThemeConstantOverride("margin_bottom", 6);
                b.AddChild(margin);
                var h = new HBoxContainer();
                h.AddThemeConstantOverride("separation", 12);
                margin.AddChild(h);
                var tile = new PanelContainer { CustomMinimumSize = new Vector2(40, 40) };
                var tsb = new StyleBoxFlat { BgColor = new Color(0.10f, 0.13f, 0.19f, 0.98f), BorderColor = border };
                tsb.SetBorderWidthAll(1);
                tile.AddThemeStyleboxOverride("panel", tsb);
                var glyph = new Label
                {
                    Text = FlowUi.NameCase(m.Name.Substring(0, 2)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                glyph.AddThemeFontSizeOverride("font_size", 14);
                glyph.AddThemeColorOverride("font_color", nameCol);
                tile.AddChild(glyph);
                h.AddChild(tile);
                var texts = new VBoxContainer();
                texts.AddThemeConstantOverride("separation", 1);
                texts.SizeFlagsVertical = SizeFlags.ShrinkCenter;
                h.AddChild(texts);
                var nameL = new Label { Text = FlowUi.NameCase(m.Name) }; // no row numbers (owner, 2026-11-15)
                nameL.AddThemeFontSizeOverride("font_size", 15);
                nameL.AddThemeColorOverride("font_color", nameCol);
                texts.AddChild(nameL);
                var stateL = new Label { Text = state };
                stateL.AddThemeFontSizeOverride("font_size", 12);
                stateL.AddThemeColorOverride("font_color", m.Leader ? gold : dim);
                texts.AddChild(stateL);
                IgnoreMouse(b);
                _rows.AddChild(b);
            }
            _root.CustomMinimumSize = new Vector2(520, 210 + members.Count * 60);
            _openedAt = Time.GetTicksMsec();
            Visible = true;
        }

        private static void IgnoreMouse(Control c)
        {
            foreach (var ch in c.GetChildren())
                if (ch is Control cc) { cc.MouseFilter = MouseFilterEnum.Ignore; IgnoreMouse(cc); }
        }

        public override void _Input(InputEvent e)
        {
            if (!Visible) return;
            if (Time.GetTicksMsec() - _openedAt < 200) return; // the opening key
            if (e is InputEventKey k && k.Pressed && !k.Echo && (k.Keycode == Key.Escape || k.Keycode == Key.F2))
            {
                Visible = false;
                GetViewport().SetInputAsHandled();
            }
        }
    }

    // The controls screen (owner, 2026-11-23): F1 in a match
    // lists the game's bindings - movement, weapons, team
    // orders, views. Static content, built once; F1 or Esc
    // closes it, Resume does the same with the mouse. Same
    // visual idiom as the team screen.
    // The controls screen (owner, 2026-11-23): F1 opens it in
    // any match mode. Same idiom as the exit/team popups -
    // dimmed game, dark panel, white corner brackets, Cinzel
    // title - with the bindings grouped in sections
    // (Movement / Fire / Commands / System), each row a
    // bulleted list entry: label left-aligned, key after it.
    // The list scrolls; the panel is sized to the viewport
    // on every open.
    private sealed partial class ControlsOverlay : Control
    {
        private ulong _openedAt;
        private Control _root;

        private static readonly (string Title, (string What, string Key)[] Items)[] Sections =
        {
            ("Movement", new (string, string)[]
            {
                ("Move forward", "W"),
                ("Move backward", "S"),
                ("Turn left", "A"),
                ("Turn right", "D"),
                ("Torso", "MOUSE"),
                ("Run", "CAPSLOCK"),
                ("Thrusters", "SPACE"),
            }),
            ("Fire", new (string, string)[]
            {
                ("Fire", "LMB"),
                ("Weapon group", "1 - 6 or MOUSE WHEEL"),
                ("Next enemy", "TAB"),
                ("Closest enemy", "SHIFT + TAB"),
                ("Next companion", "CTRL + TAB"),
                ("Sniper view", "T"),
            }),
            ("Commands", new (string, string)[]
            {
                ("Follow me", "SHIFT + 1"),
                ("Attack my target", "SHIFT + 2"),
                ("Attack closest enemy", "SHIFT + 3"),
                ("Stop", "SHIFT + 4"),
                ("Protect my companion", "SHIFT + 5"),
            }),
            ("System", new (string, string)[]
            {
                ("Leave", "ESC"),
                ("Reset", "R"),
                ("Controls", "F1"),
                ("Team", "F2"),
            }),
        };

        public ControlsOverlay()
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

            _root = new Control { CustomMinimumSize = new Vector2(600, 700) };
            center.AddChild(_root);

            var panel = new PanelContainer();
            panel.SetAnchorsPreset(LayoutPreset.FullRect);
            panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.015f, 0.03f, 0.07f, 0.88f) });
            _root.AddChild(panel);

            var v = new VBoxContainer();
            v.SetAnchorsPreset(LayoutPreset.FullRect);
            v.OffsetLeft = 30; v.OffsetTop = 26; v.OffsetRight = -30; v.OffsetBottom = -30;
            v.AddThemeConstantOverride("separation", 7);
            _root.AddChild(v);

            var title = new Label { Text = "CONTROLS", HorizontalAlignment = HorizontalAlignment.Center };
            title.AddThemeFontOverride("font", FlowUi.HeadFont(true));
            title.AddThemeFontSizeOverride("font_size", 21);
            title.AddThemeColorOverride("font_color", new Color(0.92f, 0.94f, 0.96f));
            v.AddChild(title);

            var gold = new Color(0.98f, 0.80f, 0.35f);
            var ink = new Color(0.92f, 0.94f, 0.96f);

            var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
            scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            v.AddChild(scroll);
            var list = new VBoxContainer();
            list.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            list.AddThemeConstantOverride("separation", 5);
            scroll.AddChild(list);

            bool firstSection = true;
            foreach (var (secTitle, items) in Sections)
            {
                if (!firstSection)
                {
                    // A hairline between sections (owner),
                    // evenly spaced above and below.
                    list.AddChild(new Control { CustomMinimumSize = new Vector2(0, 7) });
                    var sep = new ColorRect { Color = new Color(0.80f, 0.86f, 0.96f, 0.28f), CustomMinimumSize = new Vector2(0, 1) };
                    sep.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                    list.AddChild(sep);
                    list.AddChild(new Control { CustomMinimumSize = new Vector2(0, 7) });
                }
                firstSection = false;
                // The subtitle wears a band (owner): body-size
                // text on a padded, semi-transparent panel
                // with a semi-transparent border.
                var headBand = new PanelContainer();
                var hsb = new StyleBoxFlat
                {
                    BgColor = new Color(0.55f, 0.65f, 0.85f, 0.13f),
                    BorderColor = new Color(0.75f, 0.82f, 0.95f, 0.40f),
                    ContentMarginLeft = 10, ContentMarginTop = 4,
                    ContentMarginRight = 10, ContentMarginBottom = 4
                };
                hsb.SetBorderWidthAll(1);
                headBand.AddThemeStyleboxOverride("panel", hsb);
                var head = new Label { Text = secTitle };
                head.AddThemeFontSizeOverride("font_size", 14);
                head.AddThemeColorOverride("font_color", gold);
                headBand.AddChild(head);
                list.AddChild(headBand);
                foreach (var (what, key) in items)
                {
                    var h = new HBoxContainer();
                    h.AddThemeConstantOverride("separation", 10);
                    var bullet = new Label { Text = "\u2022", CustomMinimumSize = new Vector2(12, 0) };
                    bullet.AddThemeFontSizeOverride("font_size", 14);
                    bullet.AddThemeColorOverride("font_color", gold);
                    h.AddChild(bullet);
                    var w = new Label { Text = what, HorizontalAlignment = HorizontalAlignment.Left };
                    w.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                    w.AddThemeFontSizeOverride("font_size", 14);
                    w.AddThemeColorOverride("font_color", ink);
                    h.AddChild(w);
                    var k = new Label { Text = key, CustomMinimumSize = new Vector2(205, 0) };
                    k.AddThemeFontSizeOverride("font_size", 14);
                    k.AddThemeColorOverride("font_color", gold);
                    h.AddChild(k);
                    list.AddChild(h);
                }
            }

            // Extra air above the button and the deeper bottom
            // inset (owner): Resume gets room at the bottom.
            v.AddChild(new Control { CustomMinimumSize = new Vector2(0, 12) });
            var resume = new Button { Text = "Resume", CustomMinimumSize = new Vector2(120, 36) };
            resume.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
            var sb = new StyleBoxFlat { BgColor = new Color(0.08f, 0.11f, 0.17f, 0.95f), BorderColor = new Color(0.5f, 0.56f, 0.62f, 0.65f) };
            sb.SetBorderWidthAll(1);
            resume.AddThemeStyleboxOverride("normal", sb);
            resume.AddThemeStyleboxOverride("hover", sb);
            resume.AddThemeStyleboxOverride("pressed", sb);
            resume.AddThemeStyleboxOverride("focus", sb);
            resume.AddThemeColorOverride("font_color", ink);
            resume.Pressed += () => Visible = false;
            v.AddChild(resume);

            // The white corner brackets, same as the exit and
            // team popups (owner).
            var frame = new CornerFrame { MouseFilter = MouseFilterEnum.Ignore };
            frame.SetAnchorsPreset(LayoutPreset.FullRect);
            frame.OffsetLeft = -7; frame.OffsetTop = -7; frame.OffsetRight = 7; frame.OffsetBottom = 7;
            _root.AddChild(frame);
        }

        public void Open()
        {
            _openedAt = Time.GetTicksMsec();
            // Fit the panel to the screen (owner): capped at
            // the design size, never closer than 32px to the
            // edges; the list scrolls when the cap bites.
            var vs = GetViewportRect().Size;
            _root.CustomMinimumSize = new Vector2(
                Mathf.Min(600f, vs.X - 32f),
                Mathf.Min(760f, vs.Y - 48f));
            Visible = true;
        }

        public override void _Input(InputEvent e)
        {
            if (!Visible) return;
            if (Time.GetTicksMsec() - _openedAt < 200) return; // the opening key
            if (e is InputEventKey k && k.Pressed && !k.Echo && (k.Keycode == Key.Escape || k.Keycode == Key.F1))
            {
                Visible = false;
                GetViewport().SetInputAsHandled();
            }
        }
    }

    // The pause dialogs (Esc leave, F1 team) release the
    // mouse while they are up; when one closes back into a
    // live duel the capture must return on its own (owner,
    // 2026-11-18: after picking a leader on the F1 screen
    // the mouse stayed free until he clicked the game). The
    // click that closes a dialog lands while the tree is
    // still paused, so play input's click-to-capture branch
    // never sees it - the Hidden handlers restore it here.
    // Leaving the match sets the mode itself afterwards.
    private void RecaptureMouse()
    {
        if (_test || _labOpen || _playerDead) return;
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    // Rows for the team screen, in slot order.
    private System.Collections.Generic.List<(string Name, float HpFrac, bool Dead, bool Leader, int Slot)> TeamRows()
    {
        var rows = new System.Collections.Generic.List<(string Name, float HpFrac, bool Dead, bool Leader, int Slot)>();
        foreach (var m in TeamMembers())
            rows.Add((m.Spec.Name, m.Spec.MaxHp > 0f ? m.Hp / m.Spec.MaxHp : 0f, m.Dead, m.Slot == _leaderSlot, m.Slot));
        rows.Sort((x, y) => x.Slot.CompareTo(y.Slot));
        return rows;
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
            GetTree().ChangeSceneToFile("res://flow/Flow.tscn");
        }
        else GetTree().Quit();
    }

    // Project the active target's capsule to a screen box for
    // the TargetFrame overlay. Hidden when there is no living
    // active target, when it is behind the camera, or when the
    // HUD is down (lab open / panels hidden). The marker is
    // NOT range-gated: finding the selected enemy across the
    // map is its whole point.
    private void UpdateTargetFrame()
    {
        if (_targetFrame == null) return;
        bool show = _panelHealth.Visible && !_labOpen && _cam != null;
        RiderWalker w = null; RiderSpec spec = null; bool dead = true;
        var tlist = OpposingTargets();
        if (tlist.Count > 0)
        {
            if (_targetIdx < 0 || _targetIdx >= tlist.Count) _targetIdx = 0;
            var t = tlist[_targetIdx];
            w = t.W; spec = t.Spec; dead = t.Dead;
        }
        if (!show || w == null || spec == null || dead) { _targetFrame.Show = false; _targetFrame.QueueRedraw(); return; }
        var org = w.RootPos;
        float r = spec.Radius, hgt = spec.Height;
        var vp = GetViewport().GetVisibleRect().Size;
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3(
                org.X + ((i & 1) == 0 ? -r : r),
                org.Y + ((i & 4) == 0 ? 0f : hgt),
                org.Z + ((i & 2) == 0 ? -r : r));
            if (_cam.IsPositionBehind(corner)) { _targetFrame.Show = false; _targetFrame.QueueRedraw(); return; }
            var sp = _cam.UnprojectPosition(corner);
            minX = Mathf.Min(minX, sp.X); maxX = Mathf.Max(maxX, sp.X);
            minY = Mathf.Min(minY, sp.Y); maxY = Mathf.Max(maxY, sp.Y);
        }
        if (maxX < -40f || minX > vp.X + 40f || maxY < -40f || minY > vp.Y + 40f)
        { _targetFrame.Show = false; _targetFrame.QueueRedraw(); return; }
        float pad = 6f;
        _targetFrame.Box = new Rect2(
            Mathf.Clamp(minX - pad, 2f, vp.X - 2f), Mathf.Clamp(minY - pad, 16f, vp.Y - 2f),
            Mathf.Clamp(maxX + pad, 2f, vp.X - 2f) - Mathf.Clamp(minX - pad, 2f, vp.X - 2f),
            Mathf.Clamp(maxY + pad, 2f, vp.Y - 2f) - Mathf.Clamp(minY - pad, 16f, vp.Y - 2f));
        _targetFrame.Show = true;
        _targetFrame.QueueRedraw();
    }

    // Team-command feed (owner, 2026-11-21): the only lines it
    // ever shows are the orders the player gives the team
    // (SHIFT+1..5, written from the input handler). Combat events,
    // part breaks and leader changes do NOT write here - that
    // feed was removed on 2026-11-20 and stays removed.
    private void AddFeed(string text)
    {
        _feed.Add((text, 6f));
        while (_feed.Count > 5) _feed.RemoveAt(0);
    }

    private void UpdateHud()
    {
        if (_barPlayer == null) return;
        UpdateTargetFrame();
        _barPlayer.Value = _hpPlayer / _playerSpec.MaxHp;
        _lblPlayerHp.Text = $"{_playerSpec.Name} - {_hpPlayer:F0} HP";
        // Team HP (owner, 2026-11-14): every NPC teammate of
        // the player's own team, in the player's own idiom.
        int ti = 0;
        foreach (var a in _allies)
        {
            if (a.Team != 0 || ti >= _teamHpRows.Length) continue;
            var (tlab, tbar, tbox) = _teamHpRows[ti++];
            tbox.Visible = true;
            tlab.Text = a.Dead ? $"{a.Spec.Name} - DESTROYED" : $"{a.Spec.Name} - {a.Hp:F0} HP";
            tlab.AddThemeColorOverride("font_color",
                a.Dead ? CdLabelDead : new Color(0.85f, 0.95f, 0.85f));
            tbar.Value = a.Spec.MaxHp > 0f ? a.Hp / a.Spec.MaxHp : 0f;
            var tfill = tbar.GetThemeStylebox("fill") as StyleBoxFlat;
            if (tfill != null)
                tfill.BgColor = a.Dead ? CdDead : new Color(0.3f, 0.85f, 0.32f);
        }
        for (; ti < _teamHpRows.Length; ti++) _teamHpRows[ti].Box.Visible = false;
        // Group aliveness (owner's lab groups): a group is
        // dead when every mounting part that carries it is lost.
        var grpDead = new bool[_groups.Count];
        for (int gi = 0; gi < _groups.Count; gi++) grpDead[gi] = !WeaponAlive(gi);

        // ONE active enemy (owner): Tab cycles the living foes,
        // and the red bar belongs to the active one only - and
        // even then it appears only while at least one living
        // weapon group can reach that enemy.
        var foes = OpposingTargets();
        if (_targetIdx < 0 || _targetIdx >= foes.Count) _targetIdx = 0;
        if (foes[_targetIdx].Dead)
            for (int i = 0; i < foes.Count; i++)
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
            _lblEnemyHp.Text = $"{tgt.Spec.Name} - {tgt.Hp:F0} HP";
            _barEnemy.Value = tgt.Hp / tgt.Spec.MaxHp;
        }
        // The second foe's own readout is retired - one target only.
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
        // cooldowns - teal when the full jump charge is ready.
        if (_cdBarThr != null)
        {
            bool fitted = _player.JumpJets;
            _cdBarThr.Value = fitted ? _player.JumpCharge : 0f;
            var tfg = _cdBarThr.GetThemeStylebox("fill") as StyleBoxFlat;
            if (tfg != null) tfg.BgColor = !fitted ? CdDead
                : _player.JumpCharge >= 0.999f ? new Color(0.35f, 0.9f, 0.75f) : CdNormal;
            _lblThr.AddThemeColorOverride("font_color", fitted ? CdLabelNormal : CdLabelDead);
            _lblThr.Text = fitted ? "THRUST" : "THRUST -";
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

        // Systems readouts. TARGET RANGE is the distance to
        // the ACTIVE TARGET (owner, corrected 2026-11-23: the
        // reticle rangefinder reading was mistaken for the
        // target's range at 0.91 km - the number now always
        // names the target). The old rangefinder survives only
        // as the no-target fallback (harness cameras, no foes).
        bool liveTgt = tgt.W != null && !tgt.Dead;
        if (_camMode == CamModes.Chase && _cam != null && !liveTgt && tgt.W == null)
        {
            var rHit = _hit.Ray(_cam.GlobalPosition, AimDirWorld(), Aim.FarRange, Roles.Player, 0f, Team0Ids());
            _lblRange.Text = rHit.Hit
                ? $"TARGET RANGE: {Units.Km(_player.RootPos.DistanceTo(rHit.Point))}"
                : $"TARGET RANGE: {Units.KmVal(Aim.FarRange):0.##}+ km";
        }
        else
            _lblRange.Text = !liveTgt ? "TARGET: DESTROYED" : $"TARGET RANGE: {Units.Km(tgtRange)}";
        _lblSpeed.Text = $"SPEED: {Units.Kmh(_playerVel.Length())}" + (_running && _playerVel.Length() > 0.5f ? " - RUN" : "");

        if (_radar != null)
        {
            // Refresh EVERY reference every frame (owner,
            // 2026-11-11 round 7: 'radar is still broken' -
            // measured off his screenshot: both contacts sat
            // at exactly +/-30 degrees, rim-clamped, the view
            // from the HOME PAD at spawn yaw. The lab rebuilds
            // the player and enemy walkers on a rider swap
            // (QueueFree + BuildRider), so the references the
            // radar captured at BuildHud went stale and it
            // plotted from the first walker, parked at spawn,
            // forever). Enemy2 was already refreshed here;
            // Player and Enemy now are too.
            _radar.Player = _player;
            _radar.Enemy = _enemy;
            _radar.EnemyDown = _enemyDead;
            _radar.Enemy2 = _foe?.Walker;
            _radar.Enemy2Down = _foe?.Dead ?? true;
            _radar.Allies.Clear();
            _radar.Foes.Clear();
            foreach (var a in _allies)
            {
                if (WorldConfig.IsMission || a.Team == 0) _radar.Allies.Add((a.Walker, a.Dead));
                else _radar.Foes.Add((a.Walker, a.Dead));
            }
            _radar.QueueRedraw();
        }
        var status = new System.Collections.Generic.List<string>();
        for (int gi = 0; gi < _groups.Count; gi++)
            if (grpDead[gi]) status.Add($"GROUP {_groups[gi].Num} DESTROYED - ALL MOUNTS LOST");
        // Pod status: only for pods the chassis actually HAS (the
        // Hector carries a single rack - an absent right pod is not
        // a "lost" one). Wording follows the equipped pod weapon.
        string podName = (_defPodL ?? _defPodR)?.Short ?? "LRM";
        bool hasPodL = _partsPlayer.Has("pod_L"), hasPodR = _partsPlayer.Has("pod_R");
        int podRocketsL = 0, podRocketsR = 0;
        foreach (var g in _podGunsL) podRocketsL += g.Def.Rockets;
        foreach (var g in _podGunsR) podRocketsR += g.Def.Rockets;
        if (_defPodL != null && hasPodL && !_partsPlayer.Alive("pod_L") && (podRocketsR > 0 || !hasPodR)) status.Add($"{podName} DEGRADED - LEFT POD LOST ({podRocketsL} ROCKETS)");
        else if (_defPodR != null && hasPodR && !_partsPlayer.Alive("pod_R") && (podRocketsL > 0 || !hasPodL)) status.Add($"{podName} DEGRADED - RIGHT POD LOST ({podRocketsR} ROCKETS)");
        if (!_partsPlayer.Alive("head")) status.Add("SENSORS DESTROYED");
        _lblWeaponStatus.Text = string.Join("\n", status);
        if (_feedLabel != null)
            _feedLabel.Text = string.Join("\n",
                System.Linq.Enumerable.Select(_feed, e => e.Text));

        // Head-loss HUD disturbance: while the flicker timer runs, the
        // whole HUD stutters (seeded, visual only).
        float alpha = 1f;
        if (_hudFlickerT > 0f)
            alpha = _rng.NextDouble() < 0.38 ? 0.15f : 1f;
        // Sniper scope overlay (owner, 2026-11-23): shown
        // while the sniper view is on, replacing the plain
        // crosshair. Toggled here, not in the camera step -
        // the camera's ease block is delta-gated and harness
        // cameras call it with delta 0, which left the scope
        // stuck hidden in captures.
        if (_scope != null)
        {
            bool show = _sniper && !_labOpen;
            _scope.Visible = show;
            if (show) _cross.Visible = false;
            else if (!_labOpen && _camMode == CamModes.Chase) _cross.Visible = true;
        }

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
        if (_panelRadar != null) _panelRadar.Visible = v;
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
        if (_feedLabel != null) _feedLabel.OffsetTop = 14 + ws.Y + 10;
        var hs = _panelHealth.GetCombinedMinimumSize();
        _panelHealth.OffsetLeft = 16; _panelHealth.OffsetBottom = -14;
        _panelHealth.OffsetRight = 16 + w; _panelHealth.OffsetTop = -14 - hs.Y;
        var ts = _panelTarget.GetCombinedMinimumSize();
        _panelTarget.OffsetRight = -16; _panelTarget.OffsetBottom = -14;
        _panelTarget.OffsetLeft = -16 - w; _panelTarget.OffsetTop = -14 - ts.Y;
        if (_panelRadar != null)
        {
            var rs = _panelRadar.GetCombinedMinimumSize();
            _panelRadar.OffsetRight = -16; _panelRadar.OffsetTop = 14;
            _panelRadar.OffsetLeft = -16 - rs.X; _panelRadar.OffsetBottom = 14 + rs.Y;
        }
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
        foreach (var a in _allies)
        {
            if (!a.Dead || a.DroopT >= 1f) continue;
            a.DroopT = Mathf.Min(a.DroopT + dt / 1.5f, 1f);
            float t = a.DroopT;
            a.Walker.Droop = t * t * (3f - 2f * t) * 0.5f;
        }
    }

    // Hector AC/20 gunnery: range + line-of-sight gated, leads the player's
    // velocity at time-of-flight, seeded ±1.2° inaccuracy cone.
}
