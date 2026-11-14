using Godot;

// Wizard step 2 (Tournament, owner — teams stage): pick the
// arena. Left: the info slider (the arena selector with its
// briefing on a monitor); right: the arena visualized top-down.
// The three arenas are predefined maps — desert, forest, city —
// with Greek names (owner). Menu: [Back] [Continue].
public partial class ArenaScreen : Control
{
    public FlowMain Flow;

    private Label _nameLabel, _subLabel, _descLabel, _arenaName, _arenaBody;
    private TextureRect _view;

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

        // ---- menu bar ----
        var menu = new HBoxContainer();
        menu.AddThemeConstantOverride("separation", 10);
        root.AddChild(menu);
        var back = FlowUi.MakeButton("Back", 14);
        back.Pressed += () => Flow.GoPlanet();
        menu.AddChild(back);
        menu.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var next = FlowUi.MakeButton("To teams", 14);
        next.Pressed += () => Flow.GoTeams();
        menu.AddChild(next);

        // ---- middle: info slider | visualized map ----
        var mid = new HBoxContainer();
        mid.AddThemeConstantOverride("separation", 12);
        mid.SizeFlagsVertical = SizeFlags.ExpandFill;
        root.AddChild(mid);

        var side = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        side.AddThemeStyleboxOverride("panel", FlowUi.Panel(FlowUi.Navy));
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

        // Map section: displays the arena.
        sv.AddChild(FlowUi.MakeHeading("Map", 13, FlowUi.Gold, bold: false));
        _nameLabel = FlowUi.MakeHeading("", 16, FlowUi.Gold);
        sv.AddChild(_nameLabel);
        _subLabel = FlowUi.MakeLabel("", 11, FlowUi.Dim);
        sv.AddChild(_subLabel);
        var descScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0f, 150f),
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 1.25f,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        _descLabel = FlowUi.MakeLabel("", 13, FlowUi.ScreenInk, HorizontalAlignment.Left, wrap: true);
        _descLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        descScroll.AddChild(_descLabel);
        descScroll.Resized += () => FlowUi.FitTextWidth(descScroll, _descLabel);
        var descMonitor = FlowUi.Monitor(descScroll);
        descMonitor.SizeFlagsVertical = SizeFlags.ExpandFill;
        sv.AddChild(descMonitor);

        // Arena section: the chooser.
        sv.AddChild(FlowUi.MakeHeading("Arena", 13, FlowUi.Gold, bold: false));
        sv.AddChild(FlowUi.SelectorRow(out _arenaName, 15, StepArena));
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
        arenaScroll.Resized += () => FlowUi.FitTextWidth(arenaScroll, _arenaBody);
        var arenaMonitor = FlowUi.Monitor(arenaScroll);
        arenaMonitor.SizeFlagsVertical = SizeFlags.ExpandFill;
        sv.AddChild(arenaMonitor);

        // Visual panel: the arena top-down.
        var centerPanel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        centerPanel.AddThemeStyleboxOverride("panel", FlowUi.Panel(FlowUi.NavySoft, 0.75f));
        mid.AddChild(centerPanel);
        var holder = new Control
        {
            CustomMinimumSize = new Vector2(386f, 420f),
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        centerPanel.AddChild(holder);
        _view = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _view.SetAnchorsPreset(LayoutPreset.Center);
        _view.CustomMinimumSize = new Vector2(380f, 380f);
        _view.GrowHorizontal = GrowDirection.Both;
        _view.GrowVertical = GrowDirection.Both;
        holder.AddChild(_view);

        Refresh();
        CallDeferred("Refresh");
    }

    private void StepArena(int d)
    {
        int n = ArenaCatalog.Arenas.Length;
        FlowMain.ArenaIndex = (FlowMain.ArenaIndex + d + n) % n;
        Refresh();
    }

    private void Refresh()
    {
        if (_nameLabel == null) return;
        var a = ArenaCatalog.Arenas[FlowMain.ArenaIndex];
        _nameLabel.Text = a.Name;
        _subLabel.Text = a.Sub;
        _descLabel.Text = a.Desc;
        _arenaName.Text = a.Name;
        var bt = BiomeText(a.Biome);
        _arenaBody.Text =
            $"Arena: {a.Name}\nTerrain: {bt.Name}\nSize: 260u\nFormat: Team battle — teams are formed on the next step\n" +
            $"Purse: Last team standing\n\n{bt.Desc}";
        _view.Texture = FlowUi.ArenaTexture(512, (int)a.Biome);
    }
}
