using Godot;

// The map screen, shared by Mission and Tournament — one screen,
// Tournament simply hides the Contract section (his direction).
//
// Anatomy, top to bottom:
//   - a solid menu bar (uniform buttons, no decorative arrows):
//     Mission [Back] [Command center]; Tournament [Back]
//     [Enter the arena]
//   - sidebar sections with one anatomy each: caption, a slideshow
//     selector carrying the name, a subtitle, and the description
//     on a desk monitor (bezel + scanlines, scrollable).
//     Tournament mirrors Mission exactly (owner): the top Map
//     section DISPLAYS the arena (name, subtitle, briefing) and
//     the bottom Arena section is the chooser, where Mission has
//     its Contract section.
//   - the visual panel: the planet disc with drop-site markers
//     (Mission) or the arena top-down (Tournament)
public partial class MapScreen : Control
{
    public FlowMain Flow;

    private Label _nameLabel, _subLabel, _descLabel, _contractName, _contractBody, _arenaName, _arenaBody;
    private ScrollContainer _descScroll, _contractScroll;
    private TextureRect _disc;
    private Control _discHolder;
    private readonly System.Collections.Generic.List<Button> _markers = new();

    private bool IsMission => WorldConfig.IsMission;
    private PlanetDef Planet => MissionCatalog.Planets[FlowMain.PlanetIndex];
    private MissionDef Mission => Planet.Missions[FlowMain.MissionIndex];

    // The map types (owner): every map is one of three biomes,
    // PREDEFINED per arena and per contract — descriptive text
    // only; the player never selects a biome. Order matches
    // WorldConfig.BiomeKind.
    private sealed class BiomeDef { public string Name, Sub, Desc; }
    private static readonly BiomeDef[] Biomes =
    {
        new()
        {
            Name = "Desert",
            Sub = "Open dunes · Rock cover · Long sightlines",
            Desc = "Sand and rock under a hard sun. Sightlines run to the horizon, " +
                   "rocks are the only hard cover, and the high ground stands bare."
        },
        new()
        {
            Name = "Forest",
            Sub = "Woodland hills · Tree cover · Broken sightlines",
            Desc = "Rolling woodland. Tree crowns stop shells and break every " +
                   "sightline — but a trunk falls to a touch, so cover is spent " +
                   "as the fight goes."
        },
        new()
        {
            Name = "City",
            Sub = "Ruined city grid · Building cover · Standable roofs",
            Desc = "A dead city of towers and blocks. Buildings block movement " +
                   "and shells, their roofs are fighting ground, and every " +
                   "street corner is close range."
        }
    };

    private static BiomeDef BiomeText(WorldConfig.BiomeKind b) => Biomes[(int)b];

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
            var back = FlowUi.MakeButton("Back", 14);
            back.Pressed += () => Flow.GoMapType();
            menu.AddChild(back);
            var toLab = FlowUi.MakeButton("Command center", 14);
            toLab.Pressed += () => Flow.EnterLab(fromPlanet: true);
            menu.AddChild(toLab);
            // No Bar button here (owner): the briefing pick IS
            // the contract — sending the player to sign another
            // one in the bar contradicted it. Bars return as a
            // game location in a later build.
        }
        else
        {
            // Back first, then the action (owner, swapped) — the
            // same order as the Mission menu.
            var back = FlowUi.MakeButton("Back", 14);
            back.Pressed += () => Flow.GoMapType();
            menu.AddChild(back);
            var enter = FlowUi.MakeButton("Enter the arena", 14);
            enter.Pressed += () => Flow.EnterLab();
            menu.AddChild(enter);
        }

        // ---- middle: sidebar | visual ----
        var mid = new HBoxContainer();
        mid.AddThemeConstantOverride("separation", 12);
        mid.SizeFlagsVertical = SizeFlags.ExpandFill;
        root.AddChild(mid);

        var side = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        side.AddThemeStyleboxOverride("panel", FlowUi.Panel(FlowUi.Navy));
        // The sections live in a scroll (owner's three-section
        // sidebar): at short window heights the stack keeps its
        // natural heights and the sidebar scrolls, instead of
        // the monitors squeezing below their minimums and their
        // text painting across the next section (measured at
        // 648px with the Map section added). When it all fits,
        // no scrollbar shows and nothing changes.
        var sideScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        side.AddChild(sideScroll);
        var sv = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        sv.AddThemeConstantOverride("separation", 7);
        sideScroll.AddChild(sv);
        mid.AddChild(side);

        // Planet / Map section. In Tournament the top section
        // only DISPLAYS the arena (same row geometry, no arrows)
        // — the choosing happens in the Arena section below,
        // exactly where Mission keeps its Contract chooser.
        sv.AddChild(FlowUi.MakeHeading(IsMission ? "Planet" : "Map", 13, FlowUi.Gold, bold: false));
        sv.AddChild(IsMission ? Selector(out _nameLabel, 16, StepPlace) : DisplayRow(out _nameLabel, 16));
        _subLabel = FlowUi.MakeLabel("", 11, FlowUi.Dim);
        sv.AddChild(_subLabel);
        var descScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0f, 150f),
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 1.25f,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        _descScroll = descScroll;
        _descLabel = FlowUi.MakeLabel("", 13, FlowUi.ScreenInk, HorizontalAlignment.Left, wrap: true);
        _descLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        descScroll.AddChild(_descLabel);
        descScroll.Resized += () => FitTextWidth(descScroll, _descLabel);
        var descMonitor = Monitor(descScroll);
        descMonitor.SizeFlagsVertical = SizeFlags.ExpandFill;
        sv.AddChild(descMonitor);

        // Contract section (Mission only).
        if (IsMission)
        {
            sv.AddChild(new HSeparator());
            sv.AddChild(FlowUi.MakeHeading("Contract", 13, FlowUi.Gold, bold: false));
            sv.AddChild(Selector(out _contractName, 15, StepMission));
            var contractScroll = new ScrollContainer
            {
                CustomMinimumSize = new Vector2(0f, 132f),
                SizeFlagsVertical = SizeFlags.ExpandFill,
                SizeFlagsStretchRatio = 0.8f,
                HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
            };
            _contractScroll = contractScroll;
            _contractBody = FlowUi.MakeLabel("", 13, FlowUi.ScreenInk, HorizontalAlignment.Left, wrap: true); // same size as the planet text (owner)
            _contractBody.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            contractScroll.AddChild(_contractBody);
            contractScroll.Resized += () => FitTextWidth(contractScroll, _contractBody);
            var contractMonitor = Monitor(contractScroll);
            contractMonitor.SizeFlagsVertical = SizeFlags.ExpandFill;
            sv.AddChild(contractMonitor);
        }
        else
        {
            // Arena section (Tournament): the chooser, built
            // exactly like the Contract section it replaces.
            sv.AddChild(new HSeparator());
            sv.AddChild(FlowUi.MakeHeading("Arena", 13, FlowUi.Gold, bold: false));
            sv.AddChild(Selector(out _arenaName, 15, StepPlace));
            var arenaScroll = new ScrollContainer
            {
                CustomMinimumSize = new Vector2(0f, 132f),
                SizeFlagsVertical = SizeFlags.ExpandFill,
                SizeFlagsStretchRatio = 0.8f,
                HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
            };
            _arenaBody = FlowUi.MakeLabel("", 13, FlowUi.ScreenInk, HorizontalAlignment.Left, wrap: true);
            _arenaBody.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            arenaScroll.AddChild(_arenaBody);
            arenaScroll.Resized += () => FitTextWidth(arenaScroll, _arenaBody);
            var arenaMonitor = Monitor(arenaScroll);
            arenaMonitor.SizeFlagsVertical = SizeFlags.ExpandFill;
            sv.AddChild(arenaMonitor);
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

    // Keep a monitor label exactly as wide as its scroll viewport
    // (minus the scrollbar), so the text reflows to the full screen
    // width whenever the window or the layout changes size.
    private static void FitTextWidth(ScrollContainer scroll, Label label)
    {
        if (scroll == null || label == null) return;
        float w = scroll.Size.X - 14f;
        if (w > 40f) label.CustomMinimumSize = new Vector2(w, label.CustomMinimumSize.Y);
    }

    // The selector's row without the arrows: same geometry, the
    // name centered between two button-width spacers.
    private HBoxContainer DisplayRow(out Label name, int fontSize)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        row.AddChild(new Control { CustomMinimumSize = new Vector2(42f, 36f) });
        name = FlowUi.MakeHeading("", fontSize, FlowUi.Gold, HorizontalAlignment.Center);
        name.SizeFlagsHorizontal = SizeFlags.Expand;
        row.AddChild(name);
        row.AddChild(new Control { CustomMinimumSize = new Vector2(42f, 36f) });
        return row;
    }

    private HBoxContainer Selector(out Label name, int fontSize, System.Action<int> step)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var prev = FlowUi.MakeButton("‹", 17);
        prev.CustomMinimumSize = new Vector2(42f, 0f);
        prev.Pressed += () => step(-1);
        row.AddChild(prev);
        name = FlowUi.MakeHeading("", fontSize, FlowUi.Gold, HorizontalAlignment.Center);
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
        // The bezel fits BOTH children to its rect directly (a
        // PanelContainer fits every child): the screen, then the
        // scanlines over it. An intermediate plain Control here
        // silently zeroed the chain's minimum size — the bezel
        // collapsed to its own insets (16px, measured) while the
        // screen kept its natural height and painted across the
        // next section whenever vertical space ran short.
        var bezel = new PanelContainer();
        bezel.AddThemeStyleboxOverride("panel", FlowUi.MonitorBezel());
        var screen = new PanelContainer();
        screen.AddThemeStyleboxOverride("panel", FlowUi.MonitorScreen());
        bezel.AddChild(screen);
        screen.AddChild(inner);
        var scan = new TextureRect
        {
            Texture = FlowUi.ScanlineTexture(),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = MouseFilterEnum.Ignore
        };
        bezel.AddChild(scan);
        return bezel;
    }

    private void StepPlace(int d)
    {
        if (IsMission)
        {
            int n = MissionCatalog.Planets.Count;
            FlowMain.PlanetIndex = (FlowMain.PlanetIndex + d + n) % n;
            FlowMain.MissionIndex = 0;
        }
        else
        {
            int n = ArenaCatalog.Arenas.Length;
            FlowMain.ArenaIndex = (FlowMain.ArenaIndex + d + n) % n;
        }
        Refresh();
    }

    private void StepMission(int d)
    {
        int n = Planet.Missions.Count;
        FlowMain.MissionIndex = (FlowMain.MissionIndex + d + n) % n;
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
                $"Mission: {m.Name}\nType: {m.Type}\nTerrain: {BiomeText(m.Biome).Name}\nEmployer: {m.Employer}\n" +
                $"Payment: {m.Payment:N0} credits\nThreat: {FlowUi.ThreatPips(m.Threat)}\n\n" +
                $"{m.Site} — {m.Briefing}";
            _disc.Texture = FlowUi.PlanetTexture(p, 512, MissionCatalog.Planets.IndexOf(p) * 1.618f + 1f);
        }
        else
        {
            var a = ArenaCatalog.Arenas[FlowMain.ArenaIndex];
            _nameLabel.Text = a.Name;
            _subLabel.Text = a.Sub;
            _descLabel.Text = a.Desc;
            _arenaName.Text = a.Name;
            var bt = BiomeText(a.Biome);
            _arenaBody.Text =
                $"Arena: {a.Name}\nTerrain: {bt.Name}\nSize: 260u\nRiders: Three — free-for-all\n" +
                $"Purse: Last machine standing\n\n{bt.Desc}";
            _disc.Texture = FlowUi.ArenaTexture(512, (int)a.Biome);
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
            var b = new Button { Text = mm.Site };
            b.AddThemeFontSizeOverride("font_size", 12);
            b.AddThemeColorOverride("font_color", i == FlowMain.MissionIndex ? FlowUi.Gold : FlowUi.Ink);
            b.AddThemeColorOverride("font_focus_color", FlowUi.Gold);
            b.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
            b.AddThemeStyleboxOverride("hover", new StyleBoxEmpty());
            b.AddThemeStyleboxOverride("pressed", new StyleBoxEmpty());
            var focus = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0), BorderColor = FlowUi.Gold, DrawCenter = false };
            focus.SetBorderWidthAll(1);
            b.AddThemeStyleboxOverride("focus", focus);
            var pos = center + new Vector2(mm.SitePos.X * 158f, mm.SitePos.Y * 158f);
            b.Position = pos - new Vector2(60f, 10f);
            b.Pressed += () => { FlowMain.MissionIndex = idx; Refresh(); };
            _discHolder.AddChild(b);
            _markers.Add(b);
        }
    }
}
