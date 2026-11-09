using Godot;

// The game's front door (project main scene). A quiet orbital
// backdrop — star panorama and a slowly turning planet — behind a
// stack of full-screen UI steps:
//
//   MapType ──Tournament──► Map select ──► PhotoDuel (arena) ──► lab ──► duel
//      │
//      └────Mission────► Planet ──► Bar ──► PhotoDuel (front) ──► lab ──► duel
//                          │
//                          └──── Command Center ──► lab (skips the bar)
//
// The flow only CHOOSES; WorldConfig carries the choice into the
// duel scene, which builds the right world and opens the RiderLab
// as before. Harness: --flowshot=maptype|planet|tournament|bar
// renders one screen, saves /tmp/flow_<name>.png and quits;
// --flowfocus=N injects N ui_down presses before the capture;
// --flowtest-nav walks Mission -> planet -> lab by itself.
public partial class FlowMain : Node3D
{
    // Flow state is static so it survives the trip to the lab
    // and back (the lab's Back button reloads this scene).
    public static int PlanetIndex =
        int.TryParse(System.Environment.GetEnvironmentVariable("AOT_PLANET"), out int pl) ? pl : 0; // harness seed only
    public static int MissionIndex =
        int.TryParse(System.Environment.GetEnvironmentVariable("AOT_MISSION"), out int mi) ? mi : 0; // harness seed only
    public static int ArenaIndex = System.Environment.GetEnvironmentVariable("AOT_ARENA") switch
    {
        "1" => 1, "2" => 2, _ => 0 // harness seed only; arenas carry their own biomes
    };

    // Set by EnterLab; the lab shows its Back button only when
    // the flow brought the player there. StartScreen is set by
    // that button to name the screen _Ready should reopen.
    public static bool FromFlow =
        System.Environment.GetEnvironmentVariable("AOT_FROMFLOW") == "1"; // harness seed
    public static string StartScreen;

    private CanvasLayer _layer;
    private Control _host;
    private string _flowshot;
    private double _shotT;
    private CanvasLayer _loadLayer;
    private Label _loadLabel, _loadPct;
    private bool _loading;
    private double _loadT;
    private int _focusTarget, _focusDone;
    private bool _navTest;
    private int _navStep;

    public override void _Ready()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--flowshot=")) _flowshot = arg.Substring("--flowshot=".Length);
            if (arg.StartsWith("--flowfocus=")) _focusTarget = int.Parse(arg.Substring("--flowfocus=".Length));
            if (arg == "--flowtest-nav") _navTest = true;
        }

        BuildBackdrop();
        _layer = new CanvasLayer();
        AddChild(_layer);
        _host = new Control();
        _host.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(_host);
        BuildLoadingOverlay();

        switch (_flowshot)
        {
            case "planet":
                WorldConfig.Mode = WorldConfig.MapMode.Mission;
                WorldConfig.SelectedMission = MissionCatalog.Planets[0].Missions[0];
                GoPlanet();
                break;
            case "tournament":
                GoTournamentMap();
                break;
            case "bar":
                WorldConfig.Mode = WorldConfig.MapMode.Mission;
                WorldConfig.SelectedMission = MissionCatalog.Planets[0].Missions[0];
                GoBar();
                break;
            default:
                if (StartScreen == "planet") GoPlanet();
                else if (StartScreen == "tournament") GoTournamentMap();
                else GoMapType();
                StartScreen = null;
                break;
        }
    }

    public override void _Process(double delta)
    {
        if (_loading)
        {
            _loadT += delta;
            _loadLabel.Text = "Loading" + new string('.', 1 + (int)(_loadT * 3.0) % 3);
        }
        if (_navTest)
        {
            _shotT += delta;
            if (_navStep == 0 && _shotT > 1.0)
            {
                _navStep = 1;
                WorldConfig.Mode = WorldConfig.MapMode.Mission;
                GoPlanet();
            }
            else if (_navStep == 1 && _shotT > 1.8)
            {
                _navStep = 2;
                EnterLab(fromPlanet: true);
            }
        }
        if (_focusTarget > 0)
        {
            _shotT += 0; // (timing shares _shotT below via flowshot clock)
            if (_focusDone < _focusTarget && _shotT > 0.5 + _focusDone * 0.3)
            {
                Input.ParseInputEvent(new InputEventAction { Action = "ui_down", Pressed = true });
                Input.ParseInputEvent(new InputEventAction { Action = "ui_down", Pressed = false });
                _focusDone++;
            }
        }
        if (_flowshot != null)
        {
            _shotT += delta;
            if (_shotT > 2.2)
            {
                _flowshot = null;
                var img = GetViewport().GetTexture().GetImage();
                var path = $"/tmp/flow_{(_host.GetChildCount() > 0 ? _host.GetChild(0).Name : "screen")}.png";
                img.SavePng(path);
                GD.Print($"FLOW captured {path} size={img.GetSize()}");
                GetTree().Quit(0);
            }
        }
    }

    private void Show(Control screen)
    {
        foreach (var c in _host.GetChildren()) { _host.RemoveChild(c); c.QueueFree(); }
        _host.AddChild(screen);
        CallDeferred("GrabFirstFocus");
    }

    // Keyboard entry point: every screen opens with focus on its
    // first button, so Tab / arrow keys work from the first press.
    private void GrabFirstFocus()
    {
        FindFirstButton(_host)?.GrabFocus();
    }

    private static Button FindFirstButton(Node n)
    {
        if (n is Button b && !b.Disabled && b.IsInsideTree()) return b;
        foreach (var c in n.GetChildren())
        {
            var r = FindFirstButton(c);
            if (r != null) return r;
        }
        return null;
    }

    private void BuildLoadingOverlay()
    {
        _loadLayer = new CanvasLayer { Layer = 5, Visible = false };
        AddChild(_loadLayer);
        var rect = new ColorRect { Color = new Color(0.015f, 0.02f, 0.035f, 0.98f) };
        rect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _loadLayer.AddChild(rect);
        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _loadLayer.AddChild(center);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 10);
        center.AddChild(v);
        _loadLabel = FlowUi.MakeHeading("Loading", 30, FlowUi.Gold, HorizontalAlignment.Center);
        v.AddChild(_loadLabel);
        _loadPct = FlowUi.MakeLabel("Loading the front…", 14, FlowUi.Dim, HorizontalAlignment.Center);
        v.AddChild(_loadPct);
    }

    public void GoMapType()
    {
        var s = new MapTypeScreen { Name = "maptype" };
        s.OnTournament = () => GoTournamentMap();
        s.OnMission = () =>
        {
            WorldConfig.Mode = WorldConfig.MapMode.Mission;
            GoPlanet();
        };
        Show(s);
    }

    public void GoTournamentMap()
    {
        WorldConfig.Mode = WorldConfig.MapMode.Tournament;
        WorldConfig.SelectedMission = null;
        Show(new MapScreen { Name = "tournament", Flow = this });
    }

    public void GoPlanet()
    {
        Show(new MapScreen { Name = "planet", Flow = this });
    }

    public void GoBar()
    {
        // Entering the bar keeps the orbital selection as the
        // contract on file until a patron's offer replaces it.
        WorldConfig.SelectedMission ??= MissionCatalog.Planets[PlanetIndex].Missions[MissionIndex];
        Show(new BarScreen { Name = "bar", Flow = this });
    }

    // Command Center / Rider Lab: load the duel scene, which opens
    // the RiderLab. From the orbital screen the current pick becomes
    // the contract; from the bar, the signed contract stands.
    public void EnterLab(bool fromPlanet = false)
    {
        // The orbital pick becomes the contract FIRST — reading
        // its biome before this assignment threw on a null
        // SelectedMission and the Command center button read as
        // dead (owner's report).
        if (WorldConfig.IsMission && (fromPlanet || WorldConfig.SelectedMission == null))
            WorldConfig.SelectedMission = MissionCatalog.Planets[PlanetIndex].Missions[MissionIndex];
        // The selected map's PREDEFINED biome (owner: the
        // player never picks a map type): the contract's ground
        // on the mission path, the arena's on the tournament
        // path. AOT_BIOME still overrides for harnesses.
        if (string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("AOT_BIOME")))
            WorldConfig.Biome = WorldConfig.IsMission
                ? WorldConfig.SelectedMission.Biome
                : ArenaCatalog.Arenas[ArenaIndex].Biome;
        // The contract's building kit (City contracts only;
        // tournament arenas keep the procedural buildings).
        // AOT_CITYKIT, when set, was already applied at startup.
        if (string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("AOT_CITYKIT")))
            WorldConfig.CityKit = WorldConfig.IsMission
                ? WorldConfig.SelectedMission.CityKit : -1;
        if (string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("AOT_FLAT")))
            WorldConfig.FlatWorld = WorldConfig.IsMission
                && WorldConfig.SelectedMission.FlatTerrain;
        if (string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("AOT_SNOW")))
            WorldConfig.SnowGround = WorldConfig.IsMission
                && WorldConfig.SelectedMission.SnowGround;
        if (string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("AOT_SKY")))
            WorldConfig.SkyTex = WorldConfig.IsMission
                ? WorldConfig.SelectedMission.SkyTex
                : ArenaCatalog.Arenas[ArenaIndex].SkyTex;
        FromFlow = true;
        EnterLabScene();
    }

    // The duel scene takes seconds to build (terrain, textures,
    // riders), which used to read as a freeze. Show the loading
    // overlay, let it paint, then load the scene on a thread and
    // swap when it is ready — the overlay's last frame stays up
    // through the brief instantiation hitch.
    private async void EnterLabScene()
    {
        if (_loading) return;
        _loading = true;
        _loadLayer.Visible = true;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        const string path = "res://photo/PhotoDuel.tscn";
        if (ResourceLoader.LoadThreadedRequest(path) != Error.Ok)
        {
            GetTree().ChangeSceneToFile(path);
            return;
        }
        var progress = new Godot.Collections.Array(); // status only; no % shown (owner: it was wrong anyway)
        while (true)
        {
            var st = ResourceLoader.LoadThreadedGetStatus(path, progress);
            if (st == ResourceLoader.ThreadLoadStatus.Loaded)
            {
                var packed = (PackedScene)ResourceLoader.LoadThreadedGet(path);
                GetTree().ChangeSceneToPacked(packed);
                return;
            }
            if (st == ResourceLoader.ThreadLoadStatus.Failed ||
                st == ResourceLoader.ThreadLoadStatus.InvalidResource)
            {
                GetTree().ChangeSceneToFile(path);
                return;
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }

    private void BuildBackdrop()
    {
        // A real orbital photograph behind every menu screen (the
        // owner's reference shot), cover-fitted to the window. The
        // screens' translucent panels let it breathe through.
        var layer = new CanvasLayer { Layer = -2 };
        AddChild(layer);
        var bg = new TextureRect
        {
            Texture = GD.Load<Texture2D>("res://assets/flow/space_bg.png"),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        bg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(bg);
    }
}
