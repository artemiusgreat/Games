using Godot;

// The map screen, shared by Mission and Tournament — one screen,
// Tournament simply hides the Contract section (his direction).
//
// Anatomy, top to bottom:
//   - a solid menu bar (uniform buttons, no decorative arrows):
//     Mission [Bar] [Command center] [Back]; Tournament
//     [Enter the arena] [Back]
//   - sidebar sections with one anatomy each: caption, a slideshow
//     selector carrying the name, a subtitle, and the description
//     on a desk monitor (bezel + scanlines, scrollable)
//   - the visual panel: the planet disc with drop-site markers
//     (Mission) or the arena top-down (Tournament)
public partial class MapScreen : Control
{
    public FlowMain Flow;

    private Label _nameLabel, _subLabel, _descLabel, _contractName, _contractBody;
    private TextureRect _disc;
    private Control _discHolder;
    private readonly System.Collections.Generic.List<Button> _markers = new();
    private int _mapIndex; // tournament arena (one map today)

    private bool IsMission => WorldConfig.IsMission;
    private PlanetDef Planet => MissionCatalog.Planets[Flow.PlanetIndex];
    private MissionDef Mission => Planet.Missions[Flow.MissionIndex];

    private sealed class ArenaDef { public string Name, Sub, Desc; }
    private static readonly ArenaDef[] Arenas =
    {
        new()
        {
            Name = "The Proving Grounds",
            Sub = "Tournament arena · Map 260u · Riders 3 · Free-for-all",
            Desc = "A dry basin ringed by low hills, with a flat-topped mesa at its heart. " +
                   "Three riders drop on the pads around the mesa and fight until one remains.\n\n" +
                   "The mesa commands the center: its slopes are the only approach, its top the " +
                   "only roof. Buildings on the basin floor give hard cover, and the tree line " +
                   "breaks sight long before it stops fire.\n\n" +
                   "Tournament rules: free-for-all, no bases, no retreat past the boundary. " +
                   "Last machine standing takes the purse."
        }
    };

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

        // ---- solid menu bar ----
        var menu = new HBoxContainer();
        menu.AddThemeConstantOverride("separation", 10);
        root.AddChild(menu);
        if (IsMission)
        {
            var toBar = FlowUi.MakeButton("Bar", 14);
            toBar.Pressed += () => Flow.GoBar();
            menu.AddChild(toBar);
            var toLab = FlowUi.MakeButton("Command center", 14);
            toLab.Pressed += () => Flow.EnterLab(fromPlanet: true);
            menu.AddChild(toLab);
            var back = FlowUi.MakeButton("Back", 14);
            back.Pressed += () => Flow.GoMapType();
            menu.AddChild(back);
        }
        else
        {
            var enter = FlowUi.MakeButton("Enter the arena", 14);
            enter.Pressed += () => Flow.EnterLab();
            menu.AddChild(enter);
            var back = FlowUi.MakeButton("Back", 14);
            back.Pressed += () => Flow.GoMapType();
            menu.AddChild(back);
        }

        // ---- middle: sidebar | visual ----
        var mid = new HBoxContainer();
        mid.AddThemeConstantOverride("separation", 12);
        mid.SizeFlagsVertical = SizeFlags.ExpandFill;
        root.AddChild(mid);

        var side = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        side.AddThemeStyleboxOverride("panel", FlowUi.Panel(FlowUi.Navy));
        var sv = new VBoxContainer();
        sv.AddThemeConstantOverride("separation", 7);
        side.AddChild(sv);
        mid.AddChild(side);

        // Planet / Map section.
        sv.AddChild(FlowUi.MakeLabel(IsMission ? "Planet" : "Map", 13, FlowUi.Gold));
        sv.AddChild(Selector(out _nameLabel, 16, StepPlace));
        _subLabel = FlowUi.MakeLabel("", 11, FlowUi.Dim);
        sv.AddChild(_subLabel);
        var descScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0f, 110f),
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 1.25f,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        _descLabel = FlowUi.MakeLabel("", 13, FlowUi.ScreenInk, HorizontalAlignment.Left, wrap: true);
        _descLabel.CustomMinimumSize = new Vector2(392f, 0f);
        descScroll.AddChild(_descLabel);
        var descMonitor = Monitor(descScroll);
        descMonitor.SizeFlagsVertical = SizeFlags.ExpandFill;
        sv.AddChild(descMonitor);

        // Contract section (Mission only).
        if (IsMission)
        {
            sv.AddChild(new HSeparator());
            sv.AddChild(FlowUi.MakeLabel("Contract", 13, FlowUi.Gold));
            sv.AddChild(Selector(out _contractName, 15, StepMission));
            var contractScroll = new ScrollContainer
            {
                CustomMinimumSize = new Vector2(0f, 84f),
                SizeFlagsVertical = SizeFlags.ExpandFill,
                SizeFlagsStretchRatio = 0.8f,
                HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
            };
            _contractBody = FlowUi.MakeLabel("", 12, FlowUi.ScreenInk, HorizontalAlignment.Left, wrap: true);
            _contractBody.CustomMinimumSize = new Vector2(392f, 0f);
            contractScroll.AddChild(_contractBody);
            var contractMonitor = Monitor(contractScroll);
            contractMonitor.SizeFlagsVertical = SizeFlags.ExpandFill;
            sv.AddChild(contractMonitor);
        }

        // Visual panel.
        var centerPanel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        centerPanel.AddThemeStyleboxOverride("panel", FlowUi.Panel(FlowUi.NavySoft, 0.75f));
        _discHolder = new Control { CustomMinimumSize = new Vector2(386f, 420f), SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
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

        Refresh();
        CallDeferred("Refresh"); // again once real layout sizes exist
    }

    private HBoxContainer Selector(out Label name, int fontSize, System.Action<int> step)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var prev = FlowUi.MakeButton("‹", 17);
        prev.CustomMinimumSize = new Vector2(42f, 0f);
        prev.Pressed += () => step(-1);
        row.AddChild(prev);
        name = FlowUi.MakeLabel("", fontSize, FlowUi.Gold, HorizontalAlignment.Center);
        name.SizeFlagsHorizontal = SizeFlags.Expand;
        row.AddChild(name);
        var next = FlowUi.MakeButton("›", 17);
        next.CustomMinimumSize = new Vector2(42f, 0f);
        next.Pressed += () => step(1);
        row.AddChild(next);
        return row;
    }

    // A description on a desk monitor: bezel, dark screen, scanlines.
    private Control Monitor(Control inner)
    {
        var bezel = new PanelContainer();
        bezel.AddThemeStyleboxOverride("panel", FlowUi.MonitorBezel());
        var host = new Control();
        bezel.AddChild(host);
        var screen = new PanelContainer();
        screen.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        screen.AddThemeStyleboxOverride("panel", FlowUi.MonitorScreen());
        host.AddChild(screen);
        screen.AddChild(inner);
        var scan = new TextureRect
        {
            Texture = FlowUi.ScanlineTexture(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore
        };
        scan.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        host.AddChild(scan);
        return bezel;
    }

    private void StepPlace(int d)
    {
        if (IsMission)
        {
            int n = MissionCatalog.Planets.Count;
            Flow.PlanetIndex = (Flow.PlanetIndex + d + n) % n;
            Flow.MissionIndex = 0;
        }
        else
        {
            _mapIndex = (_mapIndex + d + Arenas.Length) % Arenas.Length;
        }
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
        if (IsMission)
        {
            var p = Planet; var m = Mission;
            _nameLabel.Text = p.Name;
            _subLabel.Text = p.Subtitle;
            _descLabel.Text = p.Intel;
            _contractName.Text = m.Name;
            _contractBody.Text =
                $"Mission: {m.Name}\nType: {m.Type}\nEmployer: {m.Employer}\n" +
                $"Payment: {m.Payment:N0} credits\nThreat: {FlowUi.ThreatPips(m.Threat)}\n\n" +
                $"{m.Site} — {m.Briefing}";
            _disc.Texture = FlowUi.PlanetTexture(p, 512, MissionCatalog.Planets.IndexOf(p) * 1.618f + 1f);
        }
        else
        {
            var a = Arenas[_mapIndex];
            _nameLabel.Text = a.Name;
            _subLabel.Text = a.Sub;
            _descLabel.Text = a.Desc;
            _disc.Texture = FlowUi.ArenaTexture(512);
        }
        PositionMarkers();
    }

    private void PositionMarkers()
    {
        if (_discHolder == null) return;
        foreach (var old in _markers) old.QueueFree();
        _markers.Clear();
        if (!IsMission) return;
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
