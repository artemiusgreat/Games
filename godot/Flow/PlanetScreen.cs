using Godot;

// Wizard step 1 (both modes, owner — teams stage): pick the
// planet. The existing planet selector stays on the left; on
// the right, instead of the planet disc, a clickable Freelancer-
// style universe map — the player can select the planet with
// the slider or by clicking its node on the chart. The mission
// selector is GONE from this screen (missions are the next
// step, in the bar). Menu: [Back] [Continue] (+ Reset story on
// the Story path).
public partial class PlanetScreen : Control
{
    public FlowMain Flow;

    private Label _nameLabel, _subLabel, _descLabel;
    private UniverseMap _map;

    private bool IsMission => WorldConfig.IsMission;
    private PlanetDef Planet => MissionCatalog.Planets[FlowMain.PlanetIndex];

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 18);
        margin.AddThemeConstantOverride("margin_right", 18);
        margin.AddThemeConstantOverride("margin_top", 14);
        margin.AddThemeConstantOverride("margin_bottom", 14);
        AddChild(margin);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 10);
        margin.AddChild(root);

        // ---- menu bar ----
        var menu = new HBoxContainer();
        menu.AddThemeConstantOverride("separation", 10);
        root.AddChild(menu);
        var back = FlowUi.MakeButton("Back", 14);
        back.Pressed += () => Flow.GoMapType();
        menu.AddChild(back);
        var next = FlowUi.MakeButton(IsMission ? "To missions" : "To arenas", 14);
        next.Pressed += () => { if (IsMission) Flow.GoMissions(); else Flow.GoArena(); };
        menu.AddChild(next);
        if (IsMission)
        {
            var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            menu.AddChild(spacer);
            var resetStory = FlowUi.MakeButton("Reset story", 11);
            resetStory.Pressed += () => { StoryProgress.Reset(); Flow.GoPlanet(); };
            menu.AddChild(resetStory);
        }

        // ---- middle: selector | universe map ----
        var mid = new HBoxContainer();
        mid.AddThemeConstantOverride("separation", 12);
        mid.SizeFlagsVertical = SizeFlags.ExpandFill;
        root.AddChild(mid);

        var side = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.0f };
        side.AddThemeStyleboxOverride("panel", FlowUi.Panel(FlowUi.Navy));
        mid.AddChild(side);
        var sv = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        sv.AddThemeConstantOverride("separation", 7);
        side.AddChild(sv);
        sv.AddChild(FlowUi.MakeHeading("Planet", 13, FlowUi.Gold, bold: false));
        sv.AddChild(FlowUi.SelectorRow(out _nameLabel, 16, StepPlanet));
        _subLabel = FlowUi.MakeLabel("", 11, FlowUi.Dim);
        sv.AddChild(_subLabel);
        var descScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0f, 150f),
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        _descLabel = FlowUi.MakeLabel("", 13, FlowUi.ScreenInk, HorizontalAlignment.Left, wrap: true);
        _descLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        descScroll.AddChild(_descLabel);
        descScroll.Resized += () => FlowUi.FitTextWidth(descScroll, _descLabel);
        var descMonitor = FlowUi.Monitor(descScroll);
        descMonitor.SizeFlagsVertical = SizeFlags.ExpandFill;
        sv.AddChild(descMonitor);
        sv.AddChild(FlowUi.MakeLabel("Select a planet with the selector, or click its node on the star chart.", 11, FlowUi.Dim, HorizontalAlignment.Left, wrap: true));

        var mapPanel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.3f };
        mapPanel.AddThemeStyleboxOverride("panel", FlowUi.Panel(FlowUi.NavySoft, 0.75f));
        mid.AddChild(mapPanel);
        _map = new UniverseMap
        {
            CustomMinimumSize = new Vector2(420f, 420f),
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
        if (_subLabel != null) _subLabel.Text = p.Subtitle;
        if (_descLabel != null) _descLabel.Text = p.Intel;
        _map?.SetSelected(FlowMain.PlanetIndex);
    }
}
