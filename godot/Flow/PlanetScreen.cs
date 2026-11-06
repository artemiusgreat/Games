using Godot;

// Front-end step 2 (Mission path): the orbital contract board, in
// the spirit of MechWarrior 4's star-system screen. A slideshow
// picks the planet (top) and the contract (sidebar); the planet
// description fills a tall scrollable panel in the left sidebar,
// the Contract section lists label: value lines, and the contract
// description sits right below it. The disc still shows the drop
// sites — clicking one selects that contract too.
public partial class PlanetScreen : Control
{
    public FlowMain Flow;

    private Label _planetSlideName, _planetSub, _intelBody, _missionSlideName, _fieldsBody, _descBody;
    private TextureRect _disc;
    private Control _discHolder;
    private readonly System.Collections.Generic.List<Button> _markers = new();

    private PlanetDef Planet => MissionCatalog.Planets[Flow.PlanetIndex];
    private MissionDef Mission => Planet.Missions[Flow.MissionIndex];

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

        // ---- top bar: planet slideshow + date ----
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 10);
        root.AddChild(top);
        var pPrev = FlowUi.MakeButton("‹", 20);
        pPrev.CustomMinimumSize = new Vector2(48f, 0f);
        pPrev.Pressed += () => StepPlanet(-1);
        top.AddChild(pPrev);
        _planetSlideName = FlowUi.MakeLabel("", 20, FlowUi.Gold, HorizontalAlignment.Center);
        _planetSlideName.CustomMinimumSize = new Vector2(300f, 0f);
        top.AddChild(_planetSlideName);
        var pNext = FlowUi.MakeButton("›", 20);
        pNext.CustomMinimumSize = new Vector2(48f, 0f);
        pNext.Pressed += () => StepPlanet(1);
        top.AddChild(pNext);
        top.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.Expand });
        top.AddChild(FlowUi.MakeLabel("22 April 3067 · 12:48", 13, FlowUi.Dim));

        // ---- middle: sidebar | disc | nav ----
        var mid = new HBoxContainer();
        mid.AddThemeConstantOverride("separation", 12);
        mid.SizeFlagsVertical = SizeFlags.Expand;
        root.AddChild(mid);

        // Left sidebar: planet description (tall, scrollable),
        // then the Contract section and its description.
        var side = new PanelContainer { CustomMinimumSize = new Vector2(470f, 0f) };
        side.AddThemeStyleboxOverride("panel", FlowUi.Panel(FlowUi.Navy));
        var sv = new VBoxContainer();
        sv.AddThemeConstantOverride("separation", 7);
        side.AddChild(sv);
        _planetSub = FlowUi.MakeLabel("", 12, FlowUi.Dim);
        sv.AddChild(_planetSub);
        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0f, 240f),
            SizeFlagsVertical = SizeFlags.Expand,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        sv.AddChild(scroll);
        _intelBody = FlowUi.MakeLabel("", 13, FlowUi.Ink, HorizontalAlignment.Left, wrap: true);
        _intelBody.CustomMinimumSize = new Vector2(420f, 0f);
        scroll.AddChild(_intelBody);
        sv.AddChild(new HSeparator());
        sv.AddChild(FlowUi.MakeLabel("Contract", 15, FlowUi.Gold));
        var mslide = new HBoxContainer();
        mslide.AddThemeConstantOverride("separation", 8);
        sv.AddChild(mslide);
        var mPrev = FlowUi.MakeButton("‹", 17);
        mPrev.CustomMinimumSize = new Vector2(42f, 0f);
        mPrev.Pressed += () => StepMission(-1);
        mslide.AddChild(mPrev);
        _missionSlideName = FlowUi.MakeLabel("", 15, FlowUi.Gold, HorizontalAlignment.Center);
        _missionSlideName.SizeFlagsHorizontal = SizeFlags.Expand;
        mslide.AddChild(_missionSlideName);
        var mNext = FlowUi.MakeButton("›", 17);
        mNext.CustomMinimumSize = new Vector2(42f, 0f);
        mNext.Pressed += () => StepMission(1);
        mslide.AddChild(mNext);
        _fieldsBody = FlowUi.MakeLabel("", 13, FlowUi.Ink);
        sv.AddChild(_fieldsBody);
        _descBody = FlowUi.MakeLabel("", 12, FlowUi.Dim, HorizontalAlignment.Left, wrap: true);
        _descBody.CustomMinimumSize = new Vector2(434f, 0f);
        sv.AddChild(_descBody);
        mid.AddChild(side);

        // Center: the planet disc with its drop-site markers.
        var centerPanel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.Expand };
        centerPanel.AddThemeStyleboxOverride("panel", FlowUi.Panel(FlowUi.NavySoft, 0.75f));
        _discHolder = new Control { CustomMinimumSize = new Vector2(386f, 470f), SizeFlagsVertical = SizeFlags.Expand, SizeFlagsHorizontal = SizeFlags.Expand };
        centerPanel.AddChild(_discHolder);
        _disc = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _disc.SetAnchorsPreset(LayoutPreset.Center);
        _disc.CustomMinimumSize = new Vector2(380f, 380f);
        _disc.GrowHorizontal = GrowDirection.Both;
        _disc.GrowVertical = GrowDirection.Both;
        _discHolder.AddChild(_disc);
        _discHolder.Resized += PositionMarkers;
        mid.AddChild(centerPanel);

        // Right: navigation.
        var nav = new VBoxContainer { CustomMinimumSize = new Vector2(200f, 0f) };
        nav.AddThemeConstantOverride("separation", 10);
        mid.AddChild(nav);
        var toBar = FlowUi.MakeButton("Bar ›", 16);
        toBar.Pressed += () => Flow.GoBar();
        nav.AddChild(toBar);
        var toLab = FlowUi.MakeButton("Command center", 14);
        toLab.Pressed += () => Flow.EnterLab(fromPlanet: true);
        nav.AddChild(toLab);
        var back = FlowUi.MakeButton("‹ Back", 14);
        back.Pressed += () => Flow.GoMapType();
        nav.AddChild(back);
        nav.AddChild(FlowUi.MakeLabel("Sign the contract in the bar, or take the orbital offer straight to the command center.", 11, FlowUi.Dim, HorizontalAlignment.Left, wrap: true));

        Refresh();
        CallDeferred("Refresh"); // again once real layout sizes exist
    }

    private void StepPlanet(int d)
    {
        int n = MissionCatalog.Planets.Count;
        Flow.PlanetIndex = (Flow.PlanetIndex + d + n) % n;
        Flow.MissionIndex = 0;
        Refresh();
    }

    private void StepMission(int d)
    {
        int n = Planet.Missions.Count;
        Flow.MissionIndex = (Flow.MissionIndex + d + n) % n;
        Refresh();
    }

    private void Refresh()
    {
        var p = Planet; var m = Mission;
        _planetSlideName.Text = p.Name;
        _planetSub.Text = p.Subtitle;
        _intelBody.Text = p.Intel;
        _missionSlideName.Text = m.Name;
        _fieldsBody.Text =
            $"Mission: {m.Name}\nType: {m.Type}\nEmployer: {m.Employer}\n" +
            $"Payment: {m.Payment:N0} credits\nThreat: {FlowUi.ThreatPips(m.Threat)}";
        _descBody.Text = $"{m.Site} — {m.Briefing}";
        _disc.Texture = FlowUi.PlanetTexture(p, 512, MissionCatalog.Planets.IndexOf(p) * 1.618f + 1f);
        PositionMarkers();
    }

    private void PositionMarkers()
    {
        if (_discHolder == null) return;
        foreach (var old in _markers) old.QueueFree();
        _markers.Clear();
        var hs = _discHolder.Size.X > 10f ? _discHolder.Size : _discHolder.CustomMinimumSize;
        var center = hs / 2f;
        var p = Planet;
        for (int i = 0; i < p.Missions.Count; i++)
        {
            int idx = i;
            var mm = p.Missions[i];
            var b = new Button { Text = $"◆ {mm.Site}" };
            b.AddThemeFontSizeOverride("font_size", 12);
            b.AddThemeColorOverride("font_color", i == Flow.MissionIndex ? FlowUi.Gold : FlowUi.Ink);
            b.AddThemeColorOverride("font_focus_color", FlowUi.Gold);
            b.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
            b.AddThemeStyleboxOverride("hover", new StyleBoxEmpty());
            b.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
            var focus = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0), BorderColor = FlowUi.Gold, DrawCenter = false };
            focus.SetBorderWidthAll(1);
            b.AddThemeStyleboxOverride("focus", focus);
            var pos = center + new Vector2(mm.SitePos.X * 158f, mm.SitePos.Y * 158f);
            b.Position = pos - new Vector2(60f, 10f);
            b.Pressed += () => { Flow.MissionIndex = idx; Refresh(); };
            _discHolder.AddChild(b);
            _markers.Add(b);
        }
    }
}
