using Godot;
using System.Collections.Generic;

// Titans module: cockpit panel, radar readouts, cooldown rows, droop.
// Split from the single-file build (round 22 cleanup); the class
// stays one partial class - see Titans.cs for the field block.
public partial class Titans
{
    private static FontFile _hudFont; // IBM Plex Mono Medium, loaded once

    private static Label MakeLabel(int fontSize, Color col)
    {
        var l = new Label { Text = "" };
        // Dashboard text is BOLDER than the game's default
        // (owner): every HUD label renders in Open Sans Semibold
        // (the game-wide font change, 2026-11-13 - it succeeds
        // Plex Mono Medium in this role).
        _hudFont ??= GD.Load<FontFile>(Paths.OpenSansSemiBold);
        if (_hudFont != null) l.AddThemeFontOverride("font", _hudFont);
        l.AddThemeFontSizeOverride("font_size", fontSize);
        l.AddThemeColorOverride("font_color", col);
        // Dashboard text carries a hard shadow (owner: HUD text
        // washed out against bright ground without one).
        l.AddThemeColorOverride("font_shadow_color", LabelShadowColor);
        l.AddThemeConstantOverride("shadow_offset_x", LabelShadowOffset);
        l.AddThemeConstantOverride("shadow_offset_y", LabelShadowOffset);
        return l;
    }

    // No base markers at all (owner, 2026-11-11: the enemy
    // marker went in round 2, 'remove Home base indicator'
    // in round 3 - the projected base labels are gone).

    // Team HP rows inside the health panel (owner, 2026-11-14):
    // one compact name + bar block per NPC teammate, stacked
    // under the player's own block in the same idiom.
    private readonly (Label Lab, ProgressBar Bar, VBoxContainer Box)[] _teamHpRows =
        new (Label, ProgressBar, VBoxContainer)[MaxTeamHpRows];

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
                BgColor = PanelFillTransparent,
                ContentMarginLeft = PanelContentMarginX, ContentMarginRight = PanelContentMarginX,
                ContentMarginTop = PanelContentMarginY, ContentMarginBottom = PanelContentMarginY,
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
        _feedLabel = MakeLabel(FeedFontSize, FeedTextColor);
        _feedLabel.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        _feedLabel.OffsetLeft = FeedLabelLeft; _feedLabel.OffsetTop = FeedLabelTop;
        _feedLabel.OffsetRight = FeedLabelRight; _feedLabel.OffsetBottom = FeedLabelBottom;
        layer.AddChild(_feedLabel);

        // Loss messages (owner, 2026-11-25): NOT inside the
        // weapons panel - a free label under it, stacked
        // between the panel and the order feed; FitCornerPanels
        // positions it every frame.
        _lblWeaponStatus = MakeLabel(WeaponStatusFontSize, WeaponStatusColor);
        _lblWeaponStatus.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        _lblWeaponStatus.AutowrapMode = TextServer.AutowrapMode.Word;
        _lblWeaponStatus.OffsetLeft = FeedLabelLeft; _lblWeaponStatus.OffsetTop = FeedLabelTop;
        _lblWeaponStatus.OffsetRight = FeedLabelRight; _lblWeaponStatus.OffsetBottom = FeedLabelBottom;
        layer.AddChild(_lblWeaponStatus);

        // Radar, top-right (owner): a SQUARE semi-transparent
        // panel the same width as the range/speed block below
        // it, with the exit popup's white corner brackets. The
        // scope stays circular and north-up inside it.
        _panelRadar = new ChromePanel { Accent = HudCyan };
        _panelRadar.SetAnchorsPreset(Control.LayoutPreset.TopRight);
        var rsb = new StyleBoxFlat
        {
            BgColor = PanelFillTransparent,
            ContentMarginLeft = PanelContentMarginX, ContentMarginRight = PanelContentMarginX,
            ContentMarginTop = PanelContentMarginY, ContentMarginBottom = PanelContentMarginY,
        };
        _panelRadar.AddThemeStyleboxOverride("panel", rsb);
        layer.AddChild(_panelRadar);
        var radarBox = new VBoxContainer();
        radarBox.AddThemeConstantOverride("separation", RadarBoxSeparation);
        _panelRadar.AddChild(radarBox);
        radarBox.AddChild(PanelHeader("", IconKind.Radar, HudCyan)); // owner, 2026-11-27: no RADAR label - the icon alone heads the panel
        _radar = new RadarView
        {
            Player = _player,
            Enemy = _enemy,
            CustomMinimumSize = new Vector2(DashboardWidth, DashboardWidth),
            Range = WorldConfig.IsMission ? RadarMissionRange : RadarArenaRange
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
        HBoxContainer PanelHeader(string t, IconKind icon, Color accent)
        {
            var h = new HBoxContainer();
            h.AddThemeConstantOverride("separation", PanelHeaderSeparation);
            h.AddChild(new IconMark { Kind = icon, Accent = accent, CustomMinimumSize = new Vector2(HeaderIconSize, HeaderIconSize), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter });
            if (t.Length > 0) // an empty title means icon only (the radar panel, owner, 2026-11-27)
            {
                var l = MakeLabel(HeaderFontSize, HeaderTextColor);
                l.Text = t;
                l.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
                h.AddChild(l);
            }
            return h;
        }
        Control PanelDivider(Color accent)
        {
            // Extra air above and below the hairline (owner,
            // 2026-11-22): the title row and the body should
            // not crowd the separator.
            var m = new MarginContainer();
            m.AddThemeConstantOverride("margin_top", PanelDividerMargin);
            m.AddThemeConstantOverride("margin_bottom", PanelDividerMargin);
            m.AddChild(new ColorRect { Color = new Color(accent, DividerAccentAlpha), CustomMinimumSize = new Vector2(0, DividerHeight) });
            return m;
        }

        // -- Left: own rider integrity + per-part status grid.
        var leftBox = new VBoxContainer { CustomMinimumSize = new Vector2(DashboardWidth, 0) }; // the shared dashboard width (owner: all panels match the target block)
        leftBox.AddThemeConstantOverride("separation", PanelColumnSeparation);
        _panelHealth.AddChild(leftBox);
        leftBox.AddChild(PanelHeader("STATUS", IconKind.Status, HudCyan));
        leftBox.AddChild(PanelDivider(HudCyan));
        _lblPlayerHp = MakeLabel(MainHealthFontSize, HealthTextColor);
        leftBox.AddChild(_lblPlayerHp);
        _barPlayer = MakeBar(HealthFillColor, new Vector2(DashboardWidth, MainBarHeight));
        leftBox.AddChild(_barPlayer);
        var grid = new GridContainer { Columns = PartGridColumns };
        grid.AddThemeConstantOverride("h_separation", PartGridHSeparation);
        grid.AddThemeConstantOverride("v_separation", PartGridVSeparation);
        leftBox.AddChild(grid);
        foreach (var (id, name) in new[]
        {
            (PartId.Head, "HEAD"), (PartId.ArmL, "ARM L"), (PartId.ArmR, "ARM R"), (PartId.Gun, "GUN"), (PartId.PodL, "POD L"),
            (PartId.PodR, "POD R"), (PartId.LegL, "LEG L"), (PartId.LegR, "LEG R"),
        })
        {
            var cell = new VBoxContainer();
            cell.AddThemeConstantOverride("separation", CompactRowSeparation);
            var lab = MakeLabel(PartLabelFontSize, PartTextColor);
            lab.Text = name;
            cell.AddChild(lab);
            var bar = MakeBar(PartFillColor, new Vector2(PartBarWidth, PartBarHeight)); // 4 cells + gaps = the 300 dashboard width
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
            tbox.AddThemeConstantOverride("separation", CompactRowSeparation);
            var tlab = MakeLabel(TeamHpFontSize, HealthTextColor);
            tbox.AddChild(tlab);
            var tbar = MakeBar(HealthFillColor, new Vector2(DashboardWidth, TeamBarHeight));
            tbox.AddChild(tbar);
            tbox.Visible = false;
            leftBox.AddChild(tbox);
            _teamHpRows[i] = (tlab, tbar, tbox);
        }


        // -- Center: weapons. The selected row is marked with a pointer
        // and bright text (set in UpdateHud); the hint line teaches the
        // switching scheme in-game.
        var midBox = new VBoxContainer { CustomMinimumSize = new Vector2(DashboardWidth, 0) };
        midBox.AddThemeConstantOverride("separation", PanelColumnSeparation);
        _panelWeapons.AddChild(midBox);
        midBox.AddChild(PanelHeader("WEAPONS", IconKind.Weapons, HudCyan));
        midBox.AddChild(PanelDivider(HudCyan));
        (Label, ProgressBar, TriMark) CdRow(string name)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", CdRowSeparation);
            var lab = MakeLabel(WeaponRowFontSize, CdLabelNormal);
            lab.Text = name;
            lab.CustomMinimumSize = new Vector2(ThrustLabelWidth, 0); // THRUST row: sized so its bar starts on the weapon bars' line (112 name + digit cell)
            lab.ClipText = true;
            lab.HorizontalAlignment = HorizontalAlignment.Right; // panel labels right-aligned (owner, 2026-11-22)
            row.AddChild(lab);
            var bar = MakeBar(CdNormal, new Vector2(CdBarMinWidth, CdBarHeight));
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
            mark.CustomMinimumSize = new Vector2(TriMarkWidth, TriMarkHeight);
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
            wrow.AddThemeConstantOverride("separation", WeaponRowSeparation);
            var gcell = MakeLabel(WeaponRowFontSize, GroupTextColor);
            gcell.CustomMinimumSize = new Vector2(GroupCellWidth, 0);
            gcell.HorizontalAlignment = HorizontalAlignment.Center;
            wrow.AddChild(gcell);
            var wlab = MakeLabel(WeaponRowFontSize, CdLabelNormal);
            wlab.CustomMinimumSize = new Vector2(WeaponNameWidth, 0); // tight to the longest name (owner: less space before the bar)
            wlab.ClipText = true;
            wlab.HorizontalAlignment = HorizontalAlignment.Right; // panel labels right-aligned (owner, 2026-11-22)
            wrow.AddChild(wlab);
            var wbar = MakeBar(CdNormal, new Vector2(CdBarMinWidth, CdBarHeight));
            wbar.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            wbar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            wrow.AddChild(wbar);
            var wmark = new TriMark { CustomMinimumSize = new Vector2(TriMarkWidth, TriMarkHeight) };
            wmark.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            wrow.AddChild(wmark);
            midBox.AddChild(wrow);
            _wpnGroupCells[wi] = gcell; _wpnLabels[wi] = wlab;
            _wpnBars[wi] = wbar; _wpnMarks[wi] = wmark; _wpnRows[wi] = wrow;
        }
        var rowThr = CdRow("Thrusters");
        _lblThr = rowThr.Item1; _cdBarThr = rowThr.Item2;
        // No controls hint line (owner: remove the introductory
        // text) - the panel ends at the THRUST row. The loss
        // messages moved OUT of the panel (owner, 2026-11-25):
        // _lblWeaponStatus is a free label under it, built above.


        // -- Right: target + own systems readouts.
        var rightBox = new VBoxContainer { CustomMinimumSize = new Vector2(DashboardWidth, 0) };
        rightBox.AddThemeConstantOverride("separation", PanelColumnSeparation);
        _panelTarget.AddChild(rightBox);
        rightBox.AddChild(PanelHeader("TARGET", IconKind.Target, HudRed));
        rightBox.AddChild(PanelDivider(HudRed));
        _lblEnemyHp = MakeLabel(MainHealthFontSize, EnemyTextColor);
        rightBox.AddChild(_lblEnemyHp);
        _barEnemy = MakeBar(EnemyBarFillColor, new Vector2(DashboardWidth, MainBarHeight));
        rightBox.AddChild(_barEnemy);
        // Second opponent (free-for-all): a compact readout under
        // the primary target's. Hidden when the foe is not fielded
        // (the precision test modes).
        _lblFoeHp = MakeLabel(ReadoutFontSize, EnemyTextColor);
        rightBox.AddChild(_lblFoeHp);
        _barFoe = MakeBar(FoeBarFillColor, new Vector2(DashboardWidth, FoeBarHeight));
        rightBox.AddChild(_barFoe);
        _lblRange = MakeLabel(ReadoutFontSize, ReadoutTextColor);
        rightBox.AddChild(_lblRange);
        _lblReticle = MakeLabel(ReadoutFontSize, ReadoutTextColor);
        rightBox.AddChild(_lblReticle);
        _lblSpeed = MakeLabel(ReadoutFontSize, ReadoutTextColor);
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
        _banner = MakeLabel(BannerFontSize, Colors.White);
        _banner.SetAnchorsPreset(Control.LayoutPreset.Center);
        _banner.OffsetLeft = -BannerHalfWidth; _banner.OffsetTop = BannerTop; _banner.OffsetRight = BannerHalfWidth; _banner.OffsetBottom = BannerBottom;
        _banner.HorizontalAlignment = HorizontalAlignment.Center;
        _banner.Visible = false;
        layer.AddChild(_banner);
        _bannerSub = MakeLabel(BannerSubFontSize, BannerSubTextColor);
        _bannerSub.Text = "press R to reset";
        _bannerSub.SetAnchorsPreset(Control.LayoutPreset.Center);
        _bannerSub.OffsetLeft = -BannerHalfWidth; _bannerSub.OffsetTop = BannerSubTop; _bannerSub.OffsetRight = BannerHalfWidth; _bannerSub.OffsetBottom = BannerSubBottom;
        _bannerSub.HorizontalAlignment = HorizontalAlignment.Center;
        _bannerSub.Visible = false;
        layer.AddChild(_bannerSub);
    }

    // HUD accent colours for the panel chrome (owner's example,
    // 2026-11-22): cyan for own-side panels, red for the target.
    private static readonly Color HudCyan = new(0.30f, 0.80f, 0.95f);
    private static readonly Color HudRed = new(1.0f, 0.35f, 0.22f);

    // Shared dashboard colours (Stage B, 2026-11-26: the
    // repeated look-and-feel recipes get one home each; every
    // value is EXACTLY the inline value it replaces).
    private static readonly Color LabelShadowColor = new(0f, 0f, 0f, 0.9f);
    private static readonly Color PanelFillTransparent = new(0, 0, 0, 0);
    private static readonly Color FeedTextColor = new(0.98f, 0.88f, 0.62f);
    private static readonly Color HeaderTextColor = new(0.90f, 0.96f, 1.0f);
    private static readonly Color HealthTextColor = new(0.85f, 0.95f, 0.85f);
    private static readonly Color HealthFillColor = new(0.3f, 0.85f, 0.32f);
    private static readonly Color PartTextColor = new(0.75f, 0.8f, 0.85f);
    private static readonly Color PartFillColor = new(0.45f, 0.75f, 0.5f);
    private static readonly Color GroupTextColor = new(0.62f, 0.85f, 0.95f);
    private static readonly Color EnemyTextColor = new(0.98f, 0.85f, 0.8f);
    private static readonly Color ReadoutTextColor = new(0.85f, 0.88f, 0.9f);
    private static readonly Color MarkerTextColor = new(1f, 0.95f, 0.6f);
    private static readonly Color SelectedTextColor = new(1f, 0.85f, 0.3f);
    private static readonly Color ThrusterReadyColor = new(0.35f, 0.9f, 0.75f);
    private static readonly Color PartWarningFillColor = new(0.9f, 0.45f, 0.2f);
    // Popup family (leave / team / controls overlays).
    private static readonly Color OverlayDimColor = new(0f, 0f, 0f, 0.93f);
    private static readonly Color PopupPanelColor = new(0.015f, 0.03f, 0.07f, 0.85f);
    private static readonly Color PopupPanelColorDense = new(0.015f, 0.03f, 0.07f, 0.88f);
    private static readonly Color PopupBorderColor = new(1f, 1f, 1f, 0.18f);
    private static readonly Color PopupGoldColor = new(0.98f, 0.80f, 0.35f);
    private static readonly Color PopupInkColor = new(0.92f, 0.94f, 0.96f);
    private static readonly Color PopupSteelColor = new(0.5f, 0.56f, 0.62f, 0.65f);
    private static readonly Color PopupMutedTextColor = new(0.55f, 0.58f, 0.62f);
    private static readonly Color PopupFaceColor = new(0.08f, 0.11f, 0.17f, 0.95f);
    private static readonly Color CornerBracketColor = new(1f, 1f, 1f, 0.9f);

    // Dashboard layout + behaviour constants (exact values).
    private const float DashboardWidth = 300f;       // the shared panel width (owner)
    private const float PanelContentMarginX = 16f;   // corner-panel inner margins
    private const float PanelContentMarginY = 12f;
    private const float PanelScreenMarginX = 16f;    // corner-panel screen insets (FitCornerPanels)
    private const float PanelScreenMarginY = 14f;
    private const float DividerAccentAlpha = 0.35f;  // PanelDivider's hairline
    private const float PanelAccentBorderAlpha = 0.18f; // ChromePanel's hairline
    private const float FeedLifetimeSeconds = 6f;    // an order line's feed life
    // The feed and weapon-status labels share one rect
    // under the weapons panel (both are positioned from it).
    private const int FeedLabelLeft = 18, FeedLabelTop = 14;
    private const int FeedLabelRight = 560, FeedLabelBottom = 130;
    private static readonly Color WeaponStatusColor = new(1f, 0.42f, 0.32f); // loss-message red
    // Target frame: padding around the projected box and
    // the screen-edge clamps (the top margin clears the
    // top panels).
    private const float TargetFramePad = 6f;
    private const float TargetFrameScreenMargin = 2f;
    private const float TargetFrameTopMargin = 16f;
    private const int FeedMaxLines = 5;              // the feed keeps at most this many
    private const ulong OpeningKeyGuardMsec = 200;   // a popup ignores its opening key this long
    private const float DenyFlashFrequency = 42f;    // the denied-row strobe rate
    private const float ThrusterFullChargeFraction = 0.999f; // charge reads "ready" at this fill
    private const float PartLowHealthFraction = 0.35f;       // a part cell warns below this HP fraction
    private const double HudFlickerChance = 0.38;    // head-loss flicker: dark-frame probability
    private const float HudFlickerAlpha = 0.15f;     // head-loss flicker: dark-frame alpha
    private const float DroopDurationSeconds = 1.5f; // a wreck's droop ease duration
    private const float DroopScale = 0.5f;           // a wreck's droop reaches half the curve's full bend
    // BuildHud layout (exact values): label shadow, font
    // sizes, bar geometry and separations for the corner
    // panels and their rows.
    private const int LabelShadowOffset = 1;
    private const int MaxTeamHpRows = 3;
    private const int FeedFontSize = 15;
    private const int WeaponStatusFontSize = 12;
    private const int RadarBoxSeparation = 8;
    private const int PanelHeaderSeparation = 8;
    private const float HeaderIconSize = 20f;
    private const int HeaderFontSize = 15;
    private const int PanelDividerMargin = 5;
    private const float DividerHeight = 1f;
    private const int PanelColumnSeparation = 4;
    private const int MainHealthFontSize = 16;
    private const float MainBarHeight = 20f;
    private const int PartGridColumns = 4;
    private const int PartGridHSeparation = 10;
    private const int PartGridVSeparation = 3;
    private const int CompactRowSeparation = 1;
    private const int PartLabelFontSize = 10;
    private const float PartBarWidth = 67f;
    private const float PartBarHeight = 8f;
    private const int TeamHpFontSize = 13;
    private const float TeamBarHeight = 12f;
    private const int CdRowSeparation = 8;
    private const int WeaponRowFontSize = 14;
    private const float ThrustLabelWidth = 136f;
    private const float CdBarMinWidth = 100f;
    private const float CdBarHeight = 13f;
    private const float TriMarkWidth = 16f;
    private const float TriMarkHeight = 13f;
    private const int WeaponRowSeparation = 6;
    private const float GroupCellWidth = 20f;
    private const float WeaponNameWidth = 112f;
    private const int ReadoutFontSize = 13;
    private const float FoeBarHeight = 12f;
    private static readonly Color EnemyBarFillColor = new(0.88f, 0.3f, 0.24f);
    private static readonly Color FoeBarFillColor = new(0.88f, 0.45f, 0.24f);
    private const int BannerFontSize = 46;
    private const float BannerHalfWidth = 450f;
    private const float BannerTop = -150f;
    private const float BannerBottom = -90f;
    private const int BannerSubFontSize = 19;
    private static readonly Color BannerSubTextColor = new(0.9f, 0.9f, 0.9f);
    private const float BannerSubTop = -84f;
    private const float BannerSubBottom = -56f;
    // Radar ranges (the RadarView's Range): missions read
    // wider than the arena.
    private const float RadarMissionRange = 600f;
    private const float RadarArenaRange = 160f;
    // Popup family shared geometry (leave / team / controls).
    private const int PopupBorderWidth = 1;
    private const int PopupTitleFontSize = 21;
    private const int PopupBodyFontSize = 13;
    private const int PopupContentSeparation = 10;
    private const float PopupContentSideMargin = 30f;
    private const float PopupFrameOutset = 7f; // the corner frame stands this far proud of the panel
    // FitCornerPanels: the status/feed stack under the
    // weapons panel.
    private const float FeedTopGap = 10f;
    private const float StatusLabelLeft = 18f;
    private const float StatusTopGap = 8f;
    private const float FeedAfterStatusGap = 8f;
    private const float RunIndicatorMinSpeed = 0.5f; // the SPEED line's RUN suffix threshold

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
            }, MarkerTextColor);
        }
    }

    // HUD panel chrome in the owner's example style (2026-11-22):
    // deep-navy glass, clipped top-left and bottom-right corners,
    // and a glowing accent frame (cyan; red for the target panel).
    // The panel's own stylebox is transparent - this _Draw paints
    // the whole frame under the content.
    private sealed partial class ChromePanel : PanelContainer
    {
        public Color Accent = HudCyan;

        private const float BracketOutset = 6f;
        private const float BracketArm = 17f;
        private const float BracketWidth = 2f;
        private const float PanelBorderWidth = 1f; // the hairline border's stroke

        public override void _Notification(int what)
        {
            if (what == NotificationResized) QueueRedraw();
        }

        public override void _Draw()
        {
            // Exit-popup chrome (owner, 2026-11-24: 'style all
            // 4 corner info sections the same way as exit
            // confirmation popup') - the rounded accent frame
            // is retired. The panel is the popup's own square
            // semi-transparent fill, and the white corner
            // brackets replicate CornerFrame exactly: same
            // colour and weight, arms 17 long, standing 6px
            // proud of the panel's corners (the popup's frame
            // sits 7 out with its lines 1 in).
            DrawRect(new Rect2(Vector2.Zero, Size), PopupPanelColor);
            // Barely-there hairline border (owner, 2026-11-24):
            // the header separator's own recipe - the panel's
            // accent at 0.35 alpha, 1px (see PanelDivider).
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(Accent, PanelAccentBorderAlpha), false, PanelBorderWidth);
            var col = CornerBracketColor;
            float o = BracketOutset, L = BracketArm, w = BracketWidth;
            var s = Size;
            DrawLine(new Vector2(-o, -o + L), new Vector2(-o, -o), col, w);
            DrawLine(new Vector2(-o, -o), new Vector2(-o + L, -o), col, w);
            DrawLine(new Vector2(s.X + o - L, -o), new Vector2(s.X + o, -o), col, w);
            DrawLine(new Vector2(s.X + o, -o), new Vector2(s.X + o, -o + L), col, w);
            DrawLine(new Vector2(s.X + o, s.Y + o - L), new Vector2(s.X + o, s.Y + o), col, w);
            DrawLine(new Vector2(s.X + o, s.Y + o), new Vector2(s.X + o - L, s.Y + o), col, w);
            DrawLine(new Vector2(-o + L, s.Y + o), new Vector2(-o, s.Y + o), col, w);
            DrawLine(new Vector2(-o, s.Y + o - L), new Vector2(-o, s.Y + o), col, w);
        }
    }

    // Small line icons for the panel headers: uniform round
    // badges of one size (owner, 2026-11-23), glyphs inside:
    // 0 weapons (lines), 1 radar (fan + sweep), 2 status
    // (check), 3 target (reticle).
    // HUD panel header icons (owner, 2026-11-27: an enum, not
    // magic numbers at the PanelHeader call sites).
    public enum IconKind { Weapons, Radar, Status, Target }

    private sealed partial class IconMark : Control
    {
        // Glyph metrics (owner, 2026-11-25: named constants).
        private const float GlyphLineSpacing = 3f; // the weapons glyph's three lines
        private const float CheckTipRise = 3f;     // the status check's rising tip
        private const float GlyphLineWidth = 1.6f; // the badges' shared stroke
        private const float GlyphHalfWidth = 4.2f; // the glyphs' shared half-extent
        private const float WeaponsGlyphLineWidth = 1.7f; // the weapons glyph's stroke (slightly heavier than the shared stroke)
        private const float CheckGlyphLineWidth = 1.9f;   // the status check's stroke
        private const float RadarGlyphRingRadius = 5.5f;  // the radar glyph's inner ring (spans most of the badge)
        private const float RadarGlyphDotRadius = 1.1f;   // the radar glyph's centre dot
        private const float RadarGlyphBlipOffset = 3f;    // the radar glyph's blip, up-right inside the ring
        private const float RadarGlyphBlipRadius = 1.3f;  // the radar glyph's blip dot
        private const float CheckGlyphLeftX = -4f, CheckGlyphLeftY = 0.2f, CheckGlyphMidX = -1.3f, CheckGlyphMidY = 3f;
        private const float TargetGlyphRingRadius = 2.8f, TargetGlyphDotRadius = 1.1f, TargetGlyphTickReach = 6.2f;

        public IconKind Kind;
        public Color Accent = HudCyan;

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
            DrawArc(c, badge, 0, Mathf.Tau, 32, col, GlyphLineWidth, true);
            switch (Kind)
            {
                case IconKind.Weapons: // three plain lines
                    for (int i = 0; i < 3; i++)
                    {
                        float y = c.Y + (i - 1) * GlyphLineSpacing;
                        DrawLine(new Vector2(c.X - GlyphHalfWidth, y), new Vector2(c.X + GlyphHalfWidth, y), col, WeaponsGlyphLineWidth, true);
                    }
                    break;
                case IconKind.Radar: // radar (owner, 2026-11-27: the old fan +
                    // sweep sat in the middle and left the badge
                    // empty - a miniature scope instead: an inner
                    // ring and a cross spanning it fill the badge
                    // evenly, with a centre dot and one blip).
                    DrawArc(c, RadarGlyphRingRadius, 0, Mathf.Tau, 24, col, GlyphLineWidth, true);
                    DrawLine(c + new Vector2(-RadarGlyphRingRadius, 0), c + new Vector2(RadarGlyphRingRadius, 0), col, GlyphLineWidth, true);
                    DrawLine(c + new Vector2(0, -RadarGlyphRingRadius), c + new Vector2(0, RadarGlyphRingRadius), col, GlyphLineWidth, true);
                    DrawCircle(c, RadarGlyphDotRadius, col);
                    DrawCircle(c + new Vector2(RadarGlyphBlipOffset, -RadarGlyphBlipOffset), RadarGlyphBlipRadius, col);
                    break;
                case IconKind.Status: // a check (the shield is retired)
                    DrawLine(c + new Vector2(CheckGlyphLeftX, CheckGlyphLeftY), c + new Vector2(CheckGlyphMidX, CheckGlyphMidY), col, CheckGlyphLineWidth, true);
                    DrawLine(c + new Vector2(CheckGlyphMidX, CheckGlyphMidY), c + new Vector2(GlyphHalfWidth, -CheckTipRise), col, CheckGlyphLineWidth, true);
                    break;
                case IconKind.Target:
                default: // target: inner ring, dot, four ticks
                    DrawArc(c, TargetGlyphRingRadius, 0, Mathf.Tau, 20, col, GlyphLineWidth, true);
                    DrawCircle(c, TargetGlyphDotRadius, col);
                    DrawLine(c + new Vector2(-TargetGlyphTickReach, 0), c + new Vector2(-GlyphHalfWidth, 0), col, GlyphLineWidth, true);
                    DrawLine(c + new Vector2(GlyphHalfWidth, 0), c + new Vector2(TargetGlyphTickReach, 0), col, GlyphLineWidth, true);
                    DrawLine(c + new Vector2(0, -TargetGlyphTickReach), c + new Vector2(0, -GlyphHalfWidth), col, GlyphLineWidth, true);
                    DrawLine(c + new Vector2(0, GlyphHalfWidth), c + new Vector2(0, TargetGlyphTickReach), col, GlyphLineWidth, true);
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
        private static readonly Color FrameColor = new(1f, 0.30f, 0.22f, 0.95f);
        private const float BracketMaxLength = 20f;    // a corner bracket's longest arm
        private const float BracketLengthFraction = 0.3f; // a bracket's arm as a fraction of the box width
        private const float FrameLineWidth = 2f;
        public override void _Draw()
        {
            if (!Show) return;
            var col = FrameColor;
            float L = Mathf.Min(BracketMaxLength, Box.Size.X * BracketLengthFraction), w = FrameLineWidth;
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
        private const float BracketLength = 18f;
        private const float BracketLineWidth = 2f;
        private const float BracketInset = 1f; // the frame draws this far inside the rect's edges

        public override void _Draw()
        {
            var col = CornerBracketColor;
            float L = BracketLength, w = BracketLineWidth;
            var s = Size;
            DrawLine(new Vector2(BracketInset, L), new Vector2(BracketInset, BracketInset), col, w);
            DrawLine(new Vector2(BracketInset, BracketInset), new Vector2(L, BracketInset), col, w);
            DrawLine(new Vector2(s.X - L, BracketInset), new Vector2(s.X - BracketInset, BracketInset), col, w);
            DrawLine(new Vector2(s.X - BracketInset, BracketInset), new Vector2(s.X - BracketInset, L), col, w);
            DrawLine(new Vector2(s.X - BracketInset, s.Y - L), new Vector2(s.X - BracketInset, s.Y - BracketInset), col, w);
            DrawLine(new Vector2(s.X - BracketInset, s.Y - BracketInset), new Vector2(s.X - L, s.Y - BracketInset), col, w);
            DrawLine(new Vector2(BracketInset, s.Y - BracketInset), new Vector2(L, s.Y - BracketInset), col, w);
            DrawLine(new Vector2(BracketInset, s.Y - L), new Vector2(BracketInset, s.Y - BracketInset), col, w);
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
        // Leave popup geometry and button faces (exact values).
        private const float RootWidth = 452f;
        private const float RootHeight = 204f;
        private const float ContentTop = 28f;
        private const float ContentBottom = 26f;
        private const float SpacerHeight = 16f;
        private const int ButtonRowSeparation = 18;
        private const float ButtonWidth = 148f;
        private const float ButtonHeight = 38f;
        private const int ButtonFontSize = 15;
        private static readonly Color ButtonDarkFace = new(0.16f, 0.19f, 0.25f);
        private static readonly Color ButtonLightFace = new(0.93f, 0.93f, 0.94f);
        private static readonly Color ButtonDarkText = new(0.05f, 0.05f, 0.06f);
        private static readonly Color ButtonDarkHover = new(0.24f, 0.28f, 0.36f);
        private static readonly Color ButtonDarkPressed = new(0.33f, 0.38f, 0.47f);
        private static readonly Color ButtonLightPressed = new(0.78f, 0.78f, 0.8f);

        public event System.Action LeaveRequested;
        private Button _noBtn;
        private ulong _openedAt;

        public LeaveOverlay()
        {
            ProcessMode = ProcessModeEnum.Always;
            Visible = false;
            SetAnchorsPreset(LayoutPreset.FullRect);
            MouseFilter = MouseFilterEnum.Stop;

            var dim = new ColorRect { Color = OverlayDimColor };
            dim.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(dim);

            var center = new CenterContainer();
            center.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(center);

            var root = new Control { CustomMinimumSize = new Vector2(RootWidth, RootHeight) };
            center.AddChild(root);

            var panel = new PanelContainer();
            panel.SetAnchorsPreset(LayoutPreset.FullRect);
            // Some transparency (owner): the game ghosts
            // through the panel; buttons stay solid.
            panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = PopupPanelColor, BorderColor = PopupBorderColor, BorderWidthLeft = PopupBorderWidth, BorderWidthRight = PopupBorderWidth, BorderWidthTop = PopupBorderWidth, BorderWidthBottom = PopupBorderWidth });
            root.AddChild(panel);

            var v = new VBoxContainer();
            v.SetAnchorsPreset(LayoutPreset.FullRect);
            v.OffsetLeft = PopupContentSideMargin; v.OffsetTop = ContentTop; v.OffsetRight = -PopupContentSideMargin; v.OffsetBottom = -ContentBottom;
            v.AddThemeConstantOverride("separation", PopupContentSeparation);
            root.AddChild(v);

            var title = new Label { Text = "Exit the game?", HorizontalAlignment = HorizontalAlignment.Center };
            // Cinzel for the popup's title (owner, 2026-11-13);
            // the rest of the game UI is Open Sans.
            title.AddThemeFontOverride("font", FlowUi.HeadFont(true));
            title.AddThemeFontSizeOverride("font_size", PopupTitleFontSize);
            title.AddThemeColorOverride("font_color", Colors.White);
            v.AddChild(title);
            var sub = new Label { Text = "Any unsaved progress will be lost.", HorizontalAlignment = HorizontalAlignment.Center };
            sub.AddThemeFontSizeOverride("font_size", PopupBodyFontSize);
            sub.AddThemeColorOverride("font_color", Colors.White);
            v.AddChild(sub);
            v.AddChild(new Control { CustomMinimumSize = new Vector2(0, SpacerHeight) });

            var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
            row.AddThemeConstantOverride("separation", ButtonRowSeparation);
            v.AddChild(row);
            var yes = MakeBtn("YES", dark: true);
            yes.Pressed += () => LeaveRequested?.Invoke();
            row.AddChild(yes);
            _noBtn = MakeBtn("NO", dark: false);
            _noBtn.Pressed += () => Visible = false;
            row.AddChild(_noBtn);

            var frame = new CornerFrame { MouseFilter = MouseFilterEnum.Ignore };
            frame.SetAnchorsPreset(LayoutPreset.FullRect);
            frame.OffsetLeft = -PopupFrameOutset; frame.OffsetTop = -PopupFrameOutset; frame.OffsetRight = PopupFrameOutset; frame.OffsetBottom = PopupFrameOutset;
            root.AddChild(frame);
        }

        internal static Button MakeBtn(string text, bool dark)
        {
            var b = new Button { Text = text, CustomMinimumSize = new Vector2(ButtonWidth, ButtonHeight) };
            b.AddThemeFontSizeOverride("font_size", ButtonFontSize);
            // The dark face was near-black (0.03) on the near-
            // black panel - once the popup backdrop deepened
            // to 0.93 it all but vanished (owner, 2026-11-24).
            // Lifted to a slate that reads against both.
            var bg = dark ? ButtonDarkFace : ButtonLightFace;
            var fg = dark ? Colors.White : ButtonDarkText;
            StyleBoxFlat S(Color c) => new() { BgColor = c };
            b.AddThemeStyleboxOverride("normal", S(bg));
            b.AddThemeStyleboxOverride("hover", S(dark ? ButtonDarkHover : Colors.White));
            b.AddThemeStyleboxOverride("pressed", S(dark ? ButtonDarkPressed : ButtonLightPressed));
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
            if (Time.GetTicksMsec() - _openedAt < OpeningKeyGuardMsec) return;
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
        private const float BaseHeight = 210f; // the team panel's height before its rows
        private const float RowPitch = 60f;    // one member row: 52 face + 8 separation
        // Team popup geometry, row faces and text sizes (exact values).
        private const float RootWidth = 520f;
        private const float RootInitialHeight = 300f;
        private const float ContentTop = 26f;
        private const float ContentBottom = 24f;
        private const int RowsSeparation = 8;
        private const float BottomSpacerHeight = 6f;
        private const int ButtonRowSeparation = 10;
        private const float RowHeight = 52f;
        private const int RowMarginX = 10;
        private const int RowMarginY = 6;
        private const int RowContentSeparation = 12;
        private const float TileSize = 40f;
        private const int GlyphFontSize = 14;
        private const int NameFontSize = 15;
        private const int StateFontSize = 12;
        private const int TextSeparation = 1;
        private static readonly Color SubTextColor = new(0.85f, 0.88f, 0.92f);
        private static readonly Color LeaderRowFace = new(0.16f, 0.13f, 0.04f, 0.95f);
        private static readonly Color DeadRowFace = new(0.05f, 0.05f, 0.06f, 0.9f);
        private static readonly Color RowHoverFace = new(0.14f, 0.18f, 0.26f, 0.98f);
        private static readonly Color RowPressedFace = new(0.20f, 0.17f, 0.08f, 0.98f);
        private static readonly Color RowTileFace = new(0.10f, 0.13f, 0.19f, 0.98f);
        private ulong _openedAt;

        public TeamOverlay()
        {
            ProcessMode = ProcessModeEnum.Always;
            Visible = false;
            SetAnchorsPreset(LayoutPreset.FullRect);
            MouseFilter = MouseFilterEnum.Stop;

            var dim = new ColorRect { Color = OverlayDimColor };
            dim.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(dim);

            var center = new CenterContainer();
            center.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(center);

            _root = new Control { CustomMinimumSize = new Vector2(RootWidth, RootInitialHeight) };
            center.AddChild(_root);

            var panel = new PanelContainer();
            panel.SetAnchorsPreset(LayoutPreset.FullRect);
            panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = PopupPanelColorDense, BorderColor = PopupBorderColor, BorderWidthLeft = PopupBorderWidth, BorderWidthRight = PopupBorderWidth, BorderWidthTop = PopupBorderWidth, BorderWidthBottom = PopupBorderWidth });
            _root.AddChild(panel);

            var v = new VBoxContainer();
            v.SetAnchorsPreset(LayoutPreset.FullRect);
            v.OffsetLeft = PopupContentSideMargin; v.OffsetTop = ContentTop; v.OffsetRight = -PopupContentSideMargin; v.OffsetBottom = -ContentBottom;
            v.AddThemeConstantOverride("separation", PopupContentSeparation);
            _root.AddChild(v);

            var title = new Label { Text = "TEAM", HorizontalAlignment = HorizontalAlignment.Center };
            title.AddThemeFontOverride("font", FlowUi.HeadFont(true));
            title.AddThemeFontSizeOverride("font_size", PopupTitleFontSize);
            title.AddThemeColorOverride("font_color", Colors.White);
            v.AddChild(title);
            var sub = new Label
            {
                Text = "Select a rider to take command of it, or leave the mission.",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            sub.AddThemeFontSizeOverride("font_size", PopupBodyFontSize);
            sub.AddThemeColorOverride("font_color", SubTextColor);
            v.AddChild(sub);

            _rows = new VBoxContainer();
            _rows.AddThemeConstantOverride("separation", RowsSeparation);
            v.AddChild(_rows);

            v.AddChild(new Control { CustomMinimumSize = new Vector2(0, BottomSpacerHeight) });
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", ButtonRowSeparation);
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
            frame.OffsetLeft = -PopupFrameOutset; frame.OffsetTop = -PopupFrameOutset; frame.OffsetRight = PopupFrameOutset; frame.OffsetBottom = PopupFrameOutset;
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
                var gold = PopupGoldColor;
                var steel = PopupSteelColor;
                var ink = PopupInkColor;
                var dim = PopupMutedTextColor;
                Color border = m.Leader ? gold : steel;
                Color nameCol = m.Dead ? dim : m.Leader ? gold : ink;
                string state = m.Dead ? "Destroyed"
                    : m.Leader ? $"Leader · HP {pct}"
                    : $"Take command · HP {pct}";
                var b = new Button { CustomMinimumSize = new Vector2(0, RowHeight) };
                StyleBoxFlat S(Color bgc, Color bdc)
                {
                    var sb = new StyleBoxFlat { BgColor = bgc, BorderColor = bdc };
                    sb.SetBorderWidthAll(PopupBorderWidth);
                    return sb;
                }
                var bg = m.Leader ? LeaderRowFace
                    : m.Dead ? DeadRowFace
                    : PopupFaceColor;
                if (m.Dead || m.Leader)
                {
                    b.Disabled = true;
                    b.AddThemeStyleboxOverride("disabled", S(bg, border));
                }
                else
                {
                    b.AddThemeStyleboxOverride("normal", S(bg, border));
                    b.AddThemeStyleboxOverride("hover", S(RowHoverFace, gold));
                    b.AddThemeStyleboxOverride("pressed", S(RowPressedFace, gold));
                    b.AddThemeStyleboxOverride("focus", S(bg, border));
                    b.Pressed += () => { Visible = false; LeaderChosen?.Invoke(slot); };
                }
                var margin = new MarginContainer();
                margin.SetAnchorsPreset(LayoutPreset.FullRect);
                margin.AddThemeConstantOverride("margin_left", RowMarginX);
                margin.AddThemeConstantOverride("margin_right", RowMarginX);
                margin.AddThemeConstantOverride("margin_top", RowMarginY);
                margin.AddThemeConstantOverride("margin_bottom", RowMarginY);
                b.AddChild(margin);
                var h = new HBoxContainer();
                h.AddThemeConstantOverride("separation", RowContentSeparation);
                margin.AddChild(h);
                var tile = new PanelContainer { CustomMinimumSize = new Vector2(TileSize, TileSize) };
                var tsb = new StyleBoxFlat { BgColor = RowTileFace, BorderColor = border };
                tsb.SetBorderWidthAll(PopupBorderWidth);
                tile.AddThemeStyleboxOverride("panel", tsb);
                var glyph = new Label
                {
                    Text = FlowUi.NameCase(m.Name.Substring(0, 2)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                glyph.AddThemeFontSizeOverride("font_size", GlyphFontSize);
                glyph.AddThemeColorOverride("font_color", nameCol);
                tile.AddChild(glyph);
                h.AddChild(tile);
                var texts = new VBoxContainer();
                texts.AddThemeConstantOverride("separation", TextSeparation);
                texts.SizeFlagsVertical = SizeFlags.ShrinkCenter;
                h.AddChild(texts);
                var nameL = new Label { Text = FlowUi.NameCase(m.Name) }; // no row numbers (owner, 2026-11-15)
                nameL.AddThemeFontSizeOverride("font_size", NameFontSize);
                nameL.AddThemeColorOverride("font_color", nameCol);
                texts.AddChild(nameL);
                var stateL = new Label { Text = state };
                stateL.AddThemeFontSizeOverride("font_size", StateFontSize);
                stateL.AddThemeColorOverride("font_color", m.Leader ? gold : dim);
                texts.AddChild(stateL);
                IgnoreMouse(b);
                _rows.AddChild(b);
            }
            _root.CustomMinimumSize = new Vector2(RootWidth, BaseHeight + members.Count * RowPitch);
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
            if (Time.GetTicksMsec() - _openedAt < OpeningKeyGuardMsec) return; // the opening key
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
        private const float PanelMaxWidth = 600f;   // the controls panel's design caps
        private const float PanelMaxHeight = 760f;
        private const float ViewportMarginX = 32f;  // closest the panel gets to the screen edges
        private const float ViewportMarginY = 48f;
        // Controls popup layout and colours (exact values).
        private const float RootInitialHeight = 700f;
        private const float ContentTop = 26f;
        private const float ContentBottom = 30f;
        private const int ContentSeparation = 7;
        private const int ListSeparation = 5;
        private const float SectionSpacerHeight = 10f;
        private const float BandSpacerHeight = 8f;
        private const float BottomSpacerHeight = 12f;
        private const int BandMarginRight = 14;
        private const int BandMarginX = 10;
        private const int BandMarginY = 4;
        private const int ControlsFontSize = 14; // the screen's one body text size
        private const float BulletWidth = 12f;
        private const float KeyColumnWidth = 205f;
        private const int RowSeparation = 10;
        private const float ResumeWidth = 120f;
        private const float ResumeHeight = 36f;
        private static readonly Color BandBackground = new(0.55f, 0.65f, 0.85f, 0.13f);
        private static readonly Color BandBorder = new(0.75f, 0.82f, 0.95f, 0.40f);
        private static readonly Color RowSeparatorColor = new(0.80f, 0.86f, 0.96f, 0.16f);
        private const float RowSeparatorHeight = 1f; // the hairline between binding rows
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
                ("Sniper view", "RMB"),
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

            var dim = new ColorRect { Color = OverlayDimColor };
            dim.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(dim);

            var center = new CenterContainer();
            center.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(center);

            _root = new Control { CustomMinimumSize = new Vector2(PanelMaxWidth, RootInitialHeight) };
            center.AddChild(_root);

            var panel = new PanelContainer();
            panel.SetAnchorsPreset(LayoutPreset.FullRect);
            panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = PopupPanelColorDense, BorderColor = PopupBorderColor, BorderWidthLeft = PopupBorderWidth, BorderWidthRight = PopupBorderWidth, BorderWidthTop = PopupBorderWidth, BorderWidthBottom = PopupBorderWidth });
            _root.AddChild(panel);

            var v = new VBoxContainer();
            v.SetAnchorsPreset(LayoutPreset.FullRect);
            v.OffsetLeft = PopupContentSideMargin; v.OffsetTop = ContentTop; v.OffsetRight = -PopupContentSideMargin; v.OffsetBottom = -ContentBottom;
            v.AddThemeConstantOverride("separation", ContentSeparation);
            _root.AddChild(v);

            var title = new Label { Text = "CONTROLS", HorizontalAlignment = HorizontalAlignment.Center };
            title.AddThemeFontOverride("font", FlowUi.HeadFont(true));
            title.AddThemeFontSizeOverride("font_size", PopupTitleFontSize);
            title.AddThemeColorOverride("font_color", PopupInkColor);
            v.AddChild(title);

            var gold = PopupGoldColor;
            var ink = PopupInkColor;

            var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
            scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            v.AddChild(scroll);
            var list = new VBoxContainer();
            list.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            list.AddThemeConstantOverride("separation", ListSeparation);
            scroll.AddChild(list);

            bool firstSection = true;
            foreach (var (secTitle, items) in Sections)
            {
                if (!firstSection)
                {
                    // Air between sections (owner, 2026-11-23:
                    // the hairline that stood here is removed -
                    // separators now live between the ROWS).
                    list.AddChild(new Control { CustomMinimumSize = new Vector2(0, SectionSpacerHeight) });
                }
                firstSection = false;
                // The subtitle wears a band (owner): body-size
                // text on a padded, semi-transparent panel
                // with a semi-transparent border. The band is
                // inset from the right edge and gets extra air
                // below it (owner, 2026-11-23).
                var headBand = new PanelContainer();
                var hsb = new StyleBoxFlat
                {
                    BgColor = BandBackground,
                    BorderColor = BandBorder,
                    ContentMarginLeft = BandMarginX, ContentMarginTop = BandMarginY,
                    ContentMarginRight = BandMarginX, ContentMarginBottom = BandMarginY
                };
                hsb.SetBorderWidthAll(PopupBorderWidth);
                headBand.AddThemeStyleboxOverride("panel", hsb);
                var head = new Label { Text = secTitle };
                head.AddThemeFontSizeOverride("font_size", ControlsFontSize);
                head.AddThemeColorOverride("font_color", gold);
                headBand.AddChild(head);
                var bandWrap = new MarginContainer();
                bandWrap.AddThemeConstantOverride("margin_right", BandMarginRight);
                bandWrap.AddChild(headBand);
                list.AddChild(bandWrap);
                list.AddChild(new Control { CustomMinimumSize = new Vector2(0, BandSpacerHeight) });
                bool firstRow = true;
                foreach (var (what, key) in items)
                {
                    if (!firstRow)
                    {
                        // A hairline between rows inside the
                        // section (owner, 2026-11-23).
                        var rsep = new ColorRect { Color = RowSeparatorColor, CustomMinimumSize = new Vector2(0, RowSeparatorHeight) };
                        rsep.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                        list.AddChild(rsep);
                    }
                    firstRow = false;
                    var h = new HBoxContainer();
                    h.AddThemeConstantOverride("separation", RowSeparation);
                    var bullet = new Label { Text = "\u2022", CustomMinimumSize = new Vector2(BulletWidth, 0) };
                    bullet.AddThemeFontSizeOverride("font_size", ControlsFontSize);
                    bullet.AddThemeColorOverride("font_color", gold);
                    h.AddChild(bullet);
                    var w = new Label { Text = what, HorizontalAlignment = HorizontalAlignment.Left };
                    w.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                    w.AddThemeFontSizeOverride("font_size", ControlsFontSize);
                    w.AddThemeColorOverride("font_color", ink);
                    h.AddChild(w);
                    var k = new Label { Text = key, CustomMinimumSize = new Vector2(KeyColumnWidth, 0) };
                    k.AddThemeFontSizeOverride("font_size", ControlsFontSize);
                    k.AddThemeColorOverride("font_color", gold);
                    h.AddChild(k);
                    list.AddChild(h);
                }
            }

            // Extra air above the button and the deeper bottom
            // inset (owner): Resume gets room at the bottom.
            v.AddChild(new Control { CustomMinimumSize = new Vector2(0, BottomSpacerHeight) });
            var resume = new Button { Text = "Resume", CustomMinimumSize = new Vector2(ResumeWidth, ResumeHeight) };
            resume.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
            var sb = new StyleBoxFlat { BgColor = PopupFaceColor, BorderColor = PopupSteelColor };
            sb.SetBorderWidthAll(PopupBorderWidth);
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
            frame.OffsetLeft = -PopupFrameOutset; frame.OffsetTop = -PopupFrameOutset; frame.OffsetRight = PopupFrameOutset; frame.OffsetBottom = PopupFrameOutset;
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
                Mathf.Min(PanelMaxWidth, vs.X - ViewportMarginX),
                Mathf.Min(PanelMaxHeight, vs.Y - ViewportMarginY));
            Visible = true;
        }

        public override void _Input(InputEvent e)
        {
            if (!Visible) return;
            if (Time.GetTicksMsec() - _openedAt < OpeningKeyGuardMsec) return; // the opening key
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
                fg.BgColor = Mathf.Sin((float)_simT * DenyFlashFrequency) > 0f ? CdDeny : CdDead;
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
            FlowMain.StartScreen = FlowMain.FlowScreen.None; // back to the front door
            GetTree().ChangeSceneToFile(Paths.FlowScene);
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
            PickTargetIdx(tlist);
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
        const float OffscreenMargin = 40f;
        if (maxX < -OffscreenMargin || minX > vp.X + OffscreenMargin || maxY < -OffscreenMargin || minY > vp.Y + OffscreenMargin)
        { _targetFrame.Show = false; _targetFrame.QueueRedraw(); return; }
        float pad = TargetFramePad;
        _targetFrame.Box = new Rect2(
            Mathf.Clamp(minX - pad, TargetFrameScreenMargin, vp.X - TargetFrameScreenMargin), Mathf.Clamp(minY - pad, TargetFrameTopMargin, vp.Y - TargetFrameScreenMargin),
            Mathf.Clamp(maxX + pad, TargetFrameScreenMargin, vp.X - TargetFrameScreenMargin) - Mathf.Clamp(minX - pad, TargetFrameScreenMargin, vp.X - TargetFrameScreenMargin),
            Mathf.Clamp(maxY + pad, TargetFrameScreenMargin, vp.Y - TargetFrameScreenMargin) - Mathf.Clamp(minY - pad, TargetFrameTopMargin, vp.Y - TargetFrameScreenMargin));
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
        _feed.Add((text, FeedLifetimeSeconds));
        while (_feed.Count > FeedMaxLines) _feed.RemoveAt(0);
        // Every order announces itself (owner, 2026-11-24):
        // posting a line is what makes the feed visible -
        // a reset used to hide the label for good, and no
        // live path ever showed it again, so orders after
        // an R went nowhere visible.
        if (_feedLabel != null) _feedLabel.Visible = true;
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
                a.Dead ? CdLabelDead : HealthTextColor);
            tbar.Value = a.Spec.MaxHp > 0f ? a.Hp / a.Spec.MaxHp : 0f;
            var tfill = tbar.GetThemeStylebox("fill") as StyleBoxFlat;
            if (tfill != null)
                tfill.BgColor = a.Dead ? CdDead : HealthFillColor;
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
        PickTargetIdx(foes); // a live pick stands; an empty/dead one becomes the nearest living foe
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
                    sel ? SelectedTextColor : GroupTextColor);
                _wpnLabels[wi].Text = gun.Def.Name;
                float wfrac = gun.Def.Cooldown > 0f ? 1f - gun.Cd / gun.Def.Cooldown : 1f;
                UpdateCdRow(_wpnBars[wi], _wpnLabels[wi], wfrac, dead, grp.DenyT);
                if (sel && !dead)
                    _wpnLabels[wi].AddThemeColorOverride("font_color", MarkerTextColor);
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
            // Thrusters line (owner, 2026-11-25): the label
            // reads exactly "Thrusters" - no dash, no state
            // symbols - and when thrusters are not fitted
            // the whole line (label + charge bar) is hidden.
            _lblThr.Visible = fitted;
            _cdBarThr.Visible = fitted;
            if (fitted)
            {
                _cdBarThr.Value = _player.JumpCharge;
                var tfg = _cdBarThr.GetThemeStylebox("fill") as StyleBoxFlat;
                if (tfg != null) tfg.BgColor = _player.JumpCharge >= ThrusterFullChargeFraction ? ThrusterReadyColor : CdNormal;
                _lblThr.AddThemeColorOverride("font_color", CdLabelNormal);
                _lblThr.Text = "Thrusters";
            }
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
                    : frac < PartLowHealthFraction ? PartWarningFillColor
                    : PartFillColor;
            cell.Lab.AddThemeColorOverride("font_color",
                part.Destroyed ? CdLabelDead : PartTextColor);
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
            var rHit = AimRay(_cam.GlobalPosition, AimDirWorld(), Aim.FarRange);
            _lblRange.Text = rHit.Hit
                ? $"TARGET RANGE: {Units.Km(_player.RootPos.DistanceTo(rHit.Point))}"
                : $"TARGET RANGE: {Units.KmVal(Aim.FarRange):0.##}+ km";
        }
        else
            _lblRange.Text = !liveTgt ? "TARGET: DESTROYED" : $"TARGET RANGE: {Units.Km(tgtRange)}";
        // RANGE (owner, 2026-11-23): its own line under
        // TARGET RANGE - the reticle rangefinder, the
        // distance from the rider to whatever the
        // crosshair is on (terrain included). AimRay's
        // team rule applies: a mate on the camera side
        // does not block it, one in the line of fire does.
        if (_cam != null)
        {
            var pHit = AimRay(_cam.GlobalPosition, AimDirWorld(), Aim.FarRange);
            _lblReticle.Text = pHit.Hit
                ? $"RANGE: {Units.Km(_player.RootPos.DistanceTo(pHit.Point))}"
                : $"RANGE: {Units.KmVal(Aim.FarRange):0.##}+ km";
        }
        else _lblReticle.Text = "RANGE: -";
        _lblSpeed.Text = $"SPEED: {Units.Kmh(_playerVel.Length())}" + (_running && _playerVel.Length() > RunIndicatorMinSpeed ? " - RUN" : "");

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
        bool hasPodL = _partsPlayer.Has(PartId.PodL), hasPodR = _partsPlayer.Has(PartId.PodR);
        int podRocketsL = 0, podRocketsR = 0;
        foreach (var g in _podGunsL) podRocketsL += g.Def.Rockets;
        foreach (var g in _podGunsR) podRocketsR += g.Def.Rockets;
        if (_defPodL != null && hasPodL && !_partsPlayer.Alive(PartId.PodL) && (podRocketsR > 0 || !hasPodR)) status.Add($"{podName} DEGRADED - LEFT POD LOST ({podRocketsL} ROCKETS)");
        else if (_defPodR != null && hasPodR && !_partsPlayer.Alive(PartId.PodR) && (podRocketsL > 0 || !hasPodL)) status.Add($"{podName} DEGRADED - RIGHT POD LOST ({podRocketsR} ROCKETS)");
        if (!_partsPlayer.Alive(PartId.Head)) status.Add("SENSORS DESTROYED");
        _lblWeaponStatus.Text = string.Join("\n", status);
        if (_feedLabel != null)
            _feedLabel.Text = string.Join("\n",
                System.Linq.Enumerable.Select(_feed, e => e.Text));

        // Head-loss HUD disturbance: while the flicker timer runs, the
        // whole HUD stutters (seeded, visual only).
        float alpha = 1f;
        if (_hudFlickerT > 0f)
            alpha = _rng.NextDouble() < HudFlickerChance ? HudFlickerAlpha : 1f;
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
        if (_feedLabel != null) _feedLabel.Visible = v;
        if (_lblWeaponStatus != null) _lblWeaponStatus.Visible = v;
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
        _panelWeapons.OffsetLeft = PanelScreenMarginX; _panelWeapons.OffsetTop = PanelScreenMarginY;
        _panelWeapons.OffsetRight = PanelScreenMarginX + w; _panelWeapons.OffsetBottom = PanelScreenMarginY + ws.Y;
        // Loss lines under the panel (owner, 2026-11-25), the
        // order feed under them; with no losses the feed sits
        // exactly where it always did.
        float feedTop = PanelScreenMarginY + ws.Y + FeedTopGap;
        if (_lblWeaponStatus != null)
        {
            _lblWeaponStatus.OffsetLeft = StatusLabelLeft;
            _lblWeaponStatus.OffsetRight = PanelScreenMarginX + w;
            _lblWeaponStatus.OffsetTop = PanelScreenMarginY + ws.Y + StatusTopGap;
            float lh = _lblWeaponStatus.GetCombinedMinimumSize().Y;
            if (lh > 0f) feedTop = PanelScreenMarginY + ws.Y + StatusTopGap + lh + FeedAfterStatusGap;
        }
        if (_feedLabel != null) _feedLabel.OffsetTop = feedTop;
        var hs = _panelHealth.GetCombinedMinimumSize();
        _panelHealth.OffsetLeft = PanelScreenMarginX; _panelHealth.OffsetBottom = -PanelScreenMarginY;
        _panelHealth.OffsetRight = PanelScreenMarginX + w; _panelHealth.OffsetTop = -PanelScreenMarginY - hs.Y;
        var ts = _panelTarget.GetCombinedMinimumSize();
        _panelTarget.OffsetRight = -PanelScreenMarginX; _panelTarget.OffsetBottom = -PanelScreenMarginY;
        _panelTarget.OffsetLeft = -PanelScreenMarginX - w; _panelTarget.OffsetTop = -PanelScreenMarginY - ts.Y;
        if (_panelRadar != null)
        {
            var rs = _panelRadar.GetCombinedMinimumSize();
            _panelRadar.OffsetRight = -PanelScreenMarginX; _panelRadar.OffsetTop = PanelScreenMarginY;
            _panelRadar.OffsetLeft = -PanelScreenMarginX - rs.X; _panelRadar.OffsetBottom = PanelScreenMarginY + rs.Y;
        }
    }

    private static float DroopCurve(float t) => t * t * (3f - 2f * t) * DroopScale;

    private void UpdateDroop(float dt)
    {
        if (_foe != null && _foe.Dead && _foe.DroopT < 1f)
        {
            _foe.DroopT = Mathf.Min(_foe.DroopT + dt / DroopDurationSeconds, 1f);
            float t1 = _foe.DroopT;
            _foe.Walker.Droop = DroopCurve(t1);
        }
        if (_enemyDead && _enemyDroopT < 1f)
        {
            _enemyDroopT = Mathf.Min(_enemyDroopT + dt / DroopDurationSeconds, 1f);
            float t = _enemyDroopT;
            _enemy.Droop = DroopCurve(t);
        }
        if (_playerDead && _playerDroopT < 1f)
        {
            _playerDroopT = Mathf.Min(_playerDroopT + dt / DroopDurationSeconds, 1f);
            float t = _playerDroopT;
            _player.Droop = DroopCurve(t);
        }
        foreach (var a in _allies)
        {
            if (!a.Dead || a.DroopT >= 1f) continue;
            a.DroopT = Mathf.Min(a.DroopT + dt / DroopDurationSeconds, 1f);
            float t = a.DroopT;
            a.Walker.Droop = DroopCurve(t);
        }
    }

    // Hector waist-gun gunnery: range + line-of-sight gated, leads the player's
    // velocity at time-of-flight, seeded ±1.2° inaccuracy cone.
}
