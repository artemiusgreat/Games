using Godot;

namespace Titans;

// Wizard step 2 (Tournament, owner - teams stage): pick the
// arena. Left: the info slider (the arena selector with its
// briefing on a monitor); right: the arena visualized top-down.
// The three arenas are predefined maps - desert, forest, city -
// with Greek names (owner). Menu: [Back] [Continue].
public partial class ArenaScreen : Control
{
  public FlowMain Flow;

  private const float ArenaScrollMinHeight = 132f;
  private const int ArenaNameFontSize = 16;
  private const int ArenaSelectorFontSize = 15;
  private const float DescScrollStretchRatio = 1.25f;
  private const float ArenaScrollStretchRatio = 0.8f;
  private const float ViewHolderMinWidth = 386f;
  private const float ViewHolderMinHeight = 420f;
  private const float ViewMinSize = 380f;
  private const float ArenaSize = 260f;

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
                   "sightline - but a trunk falls to a touch, so cover is spent " +
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
    this.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    var margin = new MarginContainer();
    margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    margin.AddThemeConstantOverride("margin_left", FlowUi.ScreenMarginHorizontal);
    margin.AddThemeConstantOverride("margin_right", FlowUi.ScreenMarginHorizontal);
    margin.AddThemeConstantOverride("margin_top", FlowUi.ScreenMarginVertical);
    margin.AddThemeConstantOverride("margin_bottom", FlowUi.ScreenMarginVertical);
    this.AddChild(margin);
    var root = new VBoxContainer();
    root.AddThemeConstantOverride("separation", FlowUi.ScreenSeparation);
    margin.AddChild(root);

    // ---- menu bar ----
    var menu = new HBoxContainer();
    menu.AddThemeConstantOverride("separation", FlowUi.MenuSeparation);
    root.AddChild(menu);
    var back = FlowUi.MakeButton("Back", FlowUi.MenuButtonFontSize);
    back.Pressed += () => this.Flow.GoPlanet();
    menu.AddChild(back);
    menu.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
    var next = FlowUi.MakeButton("Continue", FlowUi.MenuButtonFontSize);
    next.Pressed += () => this.Flow.GoTeams();
    menu.AddChild(next);

    // ---- middle: info slider | visualized map ----
    var mid = new HBoxContainer();
    mid.AddThemeConstantOverride("separation", FlowUi.MidSeparation);
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
    sv.AddThemeConstantOverride("separation", FlowUi.PanelColumnSeparation);
    sideScroll.AddChild(sv);
    mid.AddChild(side);

    // Map section: displays the arena.
    sv.AddChild(FlowUi.NavHeading("Map", FlowUi.SectionHeadingFontSize, FlowUi.Gold));
    this._nameLabel = FlowUi.NavHeading("", ArenaNameFontSize, FlowUi.Gold);
    sv.AddChild(this._nameLabel);
    this._subLabel = FlowUi.MakeLabel("", FlowUi.SubTextFontSize, FlowUi.Dim);
    sv.AddChild(this._subLabel);
    var descScroll = new ScrollContainer
    {
      CustomMinimumSize = new Vector2(0f, FlowUi.DescriptionScrollMinHeight),
      SizeFlagsVertical = SizeFlags.ExpandFill,
      SizeFlagsStretchRatio = DescScrollStretchRatio,
      HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
    };
    this._descLabel = FlowUi.MakeLabel("", FlowUi.BodyTextFontSize, FlowUi.ScreenInk, HorizontalAlignment.Left, wrap: true);
    this._descLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
    descScroll.AddChild(this._descLabel);
    descScroll.Resized += () => FlowUi.FitTextWidth(descScroll, this._descLabel);
    var descMonitor = FlowUi.Monitor(descScroll);
    descMonitor.SizeFlagsVertical = SizeFlags.ExpandFill;
    sv.AddChild(descMonitor);

    // Arena section: the chooser.
    sv.AddChild(FlowUi.NavHeading("Arena", FlowUi.SectionHeadingFontSize, FlowUi.Gold));
    sv.AddChild(FlowUi.SelectorRow(out this._arenaName, ArenaSelectorFontSize, this.StepArena));
    var arenaScroll = new ScrollContainer
    {
      CustomMinimumSize = new Vector2(0f, ArenaScrollMinHeight),
      SizeFlagsVertical = SizeFlags.ExpandFill,
      SizeFlagsStretchRatio = ArenaScrollStretchRatio,
      HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
    };
    this._arenaBody = FlowUi.MakeLabel("", FlowUi.BodyTextFontSize, FlowUi.ScreenInk, HorizontalAlignment.Left, wrap: true);
    this._arenaBody.SizeFlagsHorizontal = SizeFlags.ExpandFill;
    arenaScroll.AddChild(this._arenaBody);
    arenaScroll.Resized += () => FlowUi.FitTextWidth(arenaScroll, this._arenaBody);
    var arenaMonitor = FlowUi.Monitor(arenaScroll);
    arenaMonitor.SizeFlagsVertical = SizeFlags.ExpandFill;
    sv.AddChild(arenaMonitor);

    // Visual panel: the arena top-down.
    var centerPanel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    centerPanel.AddThemeStyleboxOverride("panel", FlowUi.Panel(FlowUi.NavySoft, FlowUi.VisualPanelAlpha));
    mid.AddChild(centerPanel);
    var holder = new Control
    {
      CustomMinimumSize = new Vector2(ViewHolderMinWidth, ViewHolderMinHeight),
      SizeFlagsVertical = SizeFlags.ExpandFill,
      SizeFlagsHorizontal = SizeFlags.ExpandFill
    };
    centerPanel.AddChild(holder);
    this._view = new TextureRect
    {
      ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
      StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
      MouseFilter = MouseFilterEnum.Ignore
    };
    this._view.SetAnchorsPreset(LayoutPreset.Center);
    this._view.CustomMinimumSize = new Vector2(ViewMinSize, ViewMinSize);
    this._view.GrowHorizontal = GrowDirection.Both;
    this._view.GrowVertical = GrowDirection.Both;
    holder.AddChild(this._view);

    this.Refresh();
    this.CallDeferred("Refresh");
  }

  private void StepArena(int d)
  {
    int n = ArenaCatalog.Arenas.Length;
    FlowMain.ArenaIndex = (FlowMain.ArenaIndex + d + n) % n;
    this.Refresh();
  }

  private void Refresh()
  {
    if (this._nameLabel == null)
    {
      return;
    }

    var a = ArenaCatalog.Arenas[FlowMain.ArenaIndex];
    this._nameLabel.Text = a.Name;
    this._subLabel.Text = a.Sub;
    this._descLabel.Text = a.Desc;
    this._arenaName.Text = a.Name;
    var bt = BiomeText(a.Biome);
    this._arenaBody.Text =
            $"Arena: {a.Name}\nTerrain: {bt.Name}\nSize: {Units.Km(ArenaSize)}\nFormat: Team battle - teams are formed on the next step\n" +
            $"Purse: Last team standing\n\n{bt.Desc}";
    this._view.Texture = FlowUi.ArenaTexture(FlowUi.ArenaTextureSize, (int)a.Biome);
  }
}
