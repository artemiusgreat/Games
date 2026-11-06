using Godot;

// Front-end step 2 (Mission path): the orbital contract board, in
// the spirit of MechWarrior 4's star-system screen. Pick a planet,
// pick a drop site on the disc, read the contract, then head to the
// bar to sign it — or go straight to the command center (the lab).
public partial class PlanetScreen : Control
{
    public FlowMain Flow;

    private Label _intelBody, _fieldsBody, _briefingBody, _planetName, _planetSub;
    private TextureRect _disc;
    private readonly System.Collections.Generic.List<Button> _markers = new();
    private readonly System.Collections.Generic.List<Button> _tabBtns = new();
    private Control _discHolder;

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
        root.AddThemeConstantOverride("separation", 12);
        margin.AddChild(root);

        // ---- top bar: planet tabs + date + exit ----
        var top = new HBoxContainer();
        top.AddThemeConstantOverride("separation", 10);
        root.AddChild(top);
        for (int i = 0; i < MissionCatalog.Planets.Count; i++)
        {
            int idx = i;
            var b = FlowUi.MakeButton(MissionCatalog.Planets[i].Name, 15);
            b.Pressed += () => { Flow.PlanetIndex = idx; Flow.MissionIndex = 0; Refresh(); };
            top.AddChild(b);
            _tabBtns.Add(b);
        }
        top.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.Expand });
        top.AddChild(FlowUi.MakeLabel("22 APRIL 3067 · 12:48", 13, FlowUi.Dim));

        // ---- middle: intel | disc | nav ----
        var mid = new HBoxContainer();
        mid.AddThemeConstantOverride("separation", 12);
        mid.SizeFlagsVertical = SizeFlags.Expand;
        root.AddChild(mid);

        var intel = new PanelContainer { CustomMinimumSize = new Vector2(320f, 0f) };
        intel.AddThemeStyleboxOverride("panel", FlowUi.Panel(FlowUi.Navy));
        var iv = new VBoxContainer();
        iv.AddThemeConstantOverride("separation", 8);
        intel.AddChild(iv);
        _planetName = FlowUi.MakeLabel("", 24, FlowUi.Gold);
        iv.AddChild(_planetName);
        _planetSub = FlowUi.MakeLabel("", 12, FlowUi.Dim);
        iv.AddChild(_planetSub);
        _intelBody = FlowUi.MakeLabel("", 13, FlowUi.Ink, HorizontalAlignment.Left, wrap: true);
        iv.AddChild(_intelBody);
        iv.AddChild(new HSeparator());
        iv.AddChild(FlowUi.MakeLabel("SELECTED CONTRACT", 13, FlowUi.Gold));
        _fieldsBody = FlowUi.MakeLabel("", 13, FlowUi.Ink);
        iv.AddChild(_fieldsBody);
        mid.AddChild(intel);

        var centerPanel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.Expand };
        centerPanel.AddThemeStyleboxOverride("panel", FlowUi.Panel(FlowUi.NavySoft, 0.75f));
        var cv = new VBoxContainer();
        cv.AddThemeConstantOverride("separation", 8);
        centerPanel.AddChild(cv);
        _discHolder = new Control { CustomMinimumSize = new Vector2(526f, 368f), SizeFlagsVertical = SizeFlags.Expand };
        cv.AddChild(_discHolder);
        _disc = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _disc.SetAnchorsPreset(LayoutPreset.Center);
        _disc.CustomMinimumSize = new Vector2(352f, 352f);
        _disc.GrowHorizontal = GrowDirection.Both;
        _disc.GrowVertical = GrowDirection.Both;
        _discHolder.AddChild(_disc);
        var listRow = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        listRow.AddThemeConstantOverride("separation", 8);
        cv.AddChild(listRow);
        for (int i = 0; i < 3; i++)
        {
            int idx = i;
            var b = FlowUi.MakeButton("", 12);
            b.Pressed += () => { Flow.MissionIndex = idx; Refresh(); };
            listRow.AddChild(b);
            _markers.Add(b); // reused below for list buttons too; markers on disc added in Refresh
        }
        _listBtns = _markers.ToArray();
        _markers.Clear();
        mid.AddChild(centerPanel);

        var nav = new VBoxContainer { CustomMinimumSize = new Vector2(210f, 0f) };
        nav.AddThemeConstantOverride("separation", 10);
        mid.AddChild(nav);
        var toBar = FlowUi.MakeButton("BAR  →", 16);
        toBar.Pressed += () => Flow.GoBar();
        nav.AddChild(toBar);
        var toLab = FlowUi.MakeButton("COMMAND CENTER", 14);
        toLab.Pressed += () => Flow.EnterLab(fromPlanet: true);
        nav.AddChild(toLab);
        var stars = FlowUi.MakeButton("STAR SYSTEMS", 14);
        stars.Pressed += () => Flow.GoMapType();
        nav.AddChild(stars);
        var menu = FlowUi.MakeButton("MAIN MENU", 14);
        menu.Pressed += () => Flow.GoMapType();
        nav.AddChild(menu);
        nav.AddChild(FlowUi.MakeLabel("Sign the contract in the bar, or take the orbital offer straight to the command center.", 11, FlowUi.Dim, HorizontalAlignment.Left, wrap: true));

        // ---- bottom: briefing ----
        var brief = new PanelContainer { CustomMinimumSize = new Vector2(0f, 96f) };
        brief.AddThemeStyleboxOverride("panel", FlowUi.Panel(FlowUi.Navy));
        _briefingBody = FlowUi.MakeLabel("", 13, FlowUi.Ink, HorizontalAlignment.Left, wrap: true);
        brief.AddChild(_briefingBody);
        root.AddChild(brief);

        Refresh();
    }

    private Button[] _listBtns;

    private void Refresh()
    {
        var p = Planet; var m = Mission;
        _planetName.Text = p.Name;
        _planetSub.Text = p.Subtitle;
        _intelBody.Text = p.Intel;
        _fieldsBody.Text =
            $"MISSION\n  {m.Name}\nTYPE\n  {m.Type}\nEMPLOYER\n  {m.Employer}\n" +
            $"PAYMENT\n  {m.Payment:N0} credits\nTHREAT\n  {FlowUi.ThreatPips(m.Threat)}";
        _briefingBody.Text = $"{m.Site} — {m.Briefing}";
        _disc.Texture = FlowUi.PlanetTexture(p, 512, MissionCatalog.Planets.IndexOf(p) * 1.618f + 1f);
        for (int i = 0; i < _tabBtns.Count; i++)
            _tabBtns[i].AddThemeColorOverride("font_color", i == Flow.PlanetIndex ? FlowUi.Gold : FlowUi.Ink);
        for (int i = 0; i < _listBtns.Length; i++)
        {
            _listBtns[i].Text = p.Missions[i].Name;
            _listBtns[i].AddThemeColorOverride("font_color", i == Flow.MissionIndex ? FlowUi.Gold : FlowUi.Ink);
        }
        // Disc site markers.
        foreach (var old in _markers) old.QueueFree();
        _markers.Clear();
        var hs = _discHolder.Size.X > 10f ? _discHolder.Size : _discHolder.CustomMinimumSize;
        var center = hs / 2f;
        for (int i = 0; i < p.Missions.Count; i++)
        {
            int idx = i;
            var mm = p.Missions[i];
            var b = new Button { Text = $"◆ {mm.Site}" };
            b.AddThemeFontSizeOverride("font_size", 12);
            b.AddThemeColorOverride("font_color", i == Flow.MissionIndex ? FlowUi.Gold : FlowUi.Ink);
            b.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
            b.AddThemeStyleboxOverride("hover", new StyleBoxEmpty());
            b.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
            var pos = center + new Vector2(mm.SitePos.X * 150f, mm.SitePos.Y * 150f);
            b.Position = pos - new Vector2(60f, 10f);
            b.Pressed += () => { Flow.MissionIndex = idx; Refresh(); };
            _discHolder.AddChild(b);
            _markers.Add(b);
        }
    }
}
