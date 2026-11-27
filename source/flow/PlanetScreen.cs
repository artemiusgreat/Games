using Godot;

// Wizard step 1 (both modes, owner - teams stage): pick the
// planet. The existing planet selector stays on the left; on
// the right, instead of the planet disc, a clickable Freelancer-
// style universe map - the player can select the planet with
// the slider or by clicking its node on the chart. The mission
// selector is GONE from this screen (missions are the next
// step, in the bar). Menu: Back on the left, Continue on the
// right (owner's standing button rule).
public partial class PlanetScreen : Control
{
    public FlowMain Flow;

    private const float MapMinSize = 420f;
    private const float MapPanelStretchRatio = 1.3f;
    private const int PlanetSelectorFontSize = 16;

    private Label _nameLabel, _subLabel, _descLabel;
    private UniverseMap _map;

    private static Texture2D _bgTex;
    private static Texture2D BgTex => _bgTex ??= GD.Load<Texture2D>("res://assets/ui/universe_bg.jpg");

    private bool IsMission => WorldConfig.IsMission;
    private PlanetDef Planet => MissionCatalog.Planets[FlowMain.PlanetIndex];

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        // Whole-screen backdrop (owner, 2026-11-25): his
        // universe photograph covers the full screen behind
        // the selector and the chart, replacing the flow's
        // shared panorama on this screen only.
        var bg = new TextureRect
        {
            Texture = BgTex,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = MouseFilterEnum.Ignore
        };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);
        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", FlowUi.ScreenMarginHorizontal);
        margin.AddThemeConstantOverride("margin_right", FlowUi.ScreenMarginHorizontal);
        margin.AddThemeConstantOverride("margin_top", FlowUi.ScreenMarginVertical);
        margin.AddThemeConstantOverride("margin_bottom", FlowUi.ScreenMarginVertical);
        AddChild(margin);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", FlowUi.ScreenSeparation);
        margin.AddChild(root);

        // ---- menu bar ----
        var menu = new HBoxContainer();
        menu.AddThemeConstantOverride("separation", FlowUi.MenuSeparation);
        root.AddChild(menu);
        var back = FlowUi.MakeButton("Back", FlowUi.MenuButtonFontSize);
        back.Pressed += () => Flow.GoMapType();
        menu.AddChild(back);
        // Back on the left, Continue on the right (owner's
        // standing button rule). The Reset story button is
        // removed at the owner's word - he sees no purpose
        // for it; StoryProgress.Reset stays as data API.
        menu.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var next = FlowUi.MakeButton("Continue", FlowUi.MenuButtonFontSize);
        next.Pressed += () => { if (IsMission) Flow.GoMissions(); else Flow.GoArena(); };
        menu.AddChild(next);

        // ---- middle: selector | universe map ----
        var mid = new HBoxContainer();
        mid.AddThemeConstantOverride("separation", FlowUi.MidSeparation);
        mid.SizeFlagsVertical = SizeFlags.ExpandFill;
        root.AddChild(mid);

        var side = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.0f };
        side.AddThemeStyleboxOverride("panel", FlowUi.Panel(FlowUi.Navy));
        mid.AddChild(side);
        var sv = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        sv.AddThemeConstantOverride("separation", FlowUi.PanelColumnSeparation);
        side.AddChild(sv);
        sv.AddChild(FlowUi.NavHeading("Planet", FlowUi.SectionHeadingFontSize, FlowUi.Gold));
        sv.AddChild(FlowUi.SelectorRow(out _nameLabel, PlanetSelectorFontSize, StepPlanet));
        _subLabel = FlowUi.MakeLabel("", FlowUi.SubTextFontSize, FlowUi.Dim);
        sv.AddChild(_subLabel);
        var descScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0f, FlowUi.DescriptionScrollMinHeight),
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        _descLabel = FlowUi.MakeLabel("", FlowUi.BodyTextFontSize, FlowUi.ScreenInk, HorizontalAlignment.Left, wrap: true);
        _descLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        descScroll.AddChild(_descLabel);
        descScroll.Resized += () => FlowUi.FitTextWidth(descScroll, _descLabel);
        var descMonitor = FlowUi.Monitor(descScroll);
        descMonitor.SizeFlagsVertical = SizeFlags.ExpandFill;
        sv.AddChild(descMonitor);
        sv.AddChild(FlowUi.MakeLabel("Select a planet with the selector, or click its node on the star chart.", FlowUi.SubTextFontSize, FlowUi.Dim, HorizontalAlignment.Left, wrap: true));

        var mapPanel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = MapPanelStretchRatio };
        // Frame only, no fill: the screen's photograph is the
        // chart's background too (owner, 2026-11-25).
        mapPanel.AddThemeStyleboxOverride("panel", FlowUi.Panel(FlowUi.NavySoft, 0f));
        mid.AddChild(mapPanel);
        _map = new UniverseMap
        {
            CustomMinimumSize = new Vector2(MapMinSize, MapMinSize),
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        _map.OnSelect = i =>
        {
            FlowMain.PlanetIndex = i;
            if (IsMission) FlowMain.MissionIndex = StoryProgress.FirstUnlocked(i);
            Refresh();
        };
        mapPanel.AddChild(_map);

        Refresh();
        CallDeferred("Refresh");
    }

    private void StepPlanet(int d)
    {
        int n = MissionCatalog.Planets.Count;
        FlowMain.PlanetIndex = (FlowMain.PlanetIndex + d + n) % n;
        if (IsMission) FlowMain.MissionIndex = StoryProgress.FirstUnlocked(FlowMain.PlanetIndex);
        Refresh();
    }

    private void Refresh()
    {
        var p = Planet;
        if (_nameLabel != null) _nameLabel.Text = p.Name;
        if (_subLabel != null) _subLabel.Text = $"{p.System} · {p.Subtitle}";
        if (_descLabel != null) _descLabel.Text = p.Intel;
        _map?.SetSelected(FlowMain.PlanetIndex);
    }
}
