using Godot;
using System.Collections.Generic;

// The game's front door (project main scene). A quiet orbital
// backdrop - star panorama and a slowly turning planet - behind a
// stack of full-screen UI steps. Both modes are step wizards
// (owner, teams stage); every step has a Back button:
//
//   MapType ──Tournament──► Planet ──► Arena ──► Teams ──► lab ──► duel
//      │
//      └──────Story──────► Planet ──► Missions (bar) ──► Team ──► lab ──► duel
//
// The flow only CHOOSES; WorldConfig carries the choice into the
// duel scene, which builds the right world and opens the RiderLab
// as before. Harness: --flowshot=maptype|planet|missions|arena|bar
// renders one screen, saves /tmp/flow_<name>.png and quits;
// --flowfocus=N injects N ui_down presses before the capture;
// --flowtest-nav walks Story -> planet -> missions -> lab alone.
public partial class FlowMain : Node3D
{
    // Flow state is static so it survives the trip to the lab
    // and back (the lab's Back button reloads this scene).
    public static int PlanetIndex = InitPlanet();
    public static int MissionIndex = InitMission();
    public static int ArenaIndex = InitArena();

    // Harness seeds only: the starting planet / mission /
    // arena, parsed by the one helper that knows the format.
    private static int InitPlanet() => EnvVars.ParseIndex(EnvVars.Planet, 0);
    private static int InitMission() => EnvVars.ParseIndex(EnvVars.Mission, 0);
    private static int InitArena() => EnvVars.ParseIndex(EnvVars.Arena, 0);

    // The wizard screens _Ready can reopen on return from the
    // lab (owner, 2026-11-27: an enum, not string names).
    public enum FlowScreen { None, Planet, Missions, StoryTeam, Arena, Teams }

    // Set by EnterLab; the lab shows its Back button only when
    // the flow brought the player there. StartScreen is set by
    // that button to name the screen _Ready should reopen.
    public static bool FromFlow = EnvVars.Flag(EnvVars.FromFlow); // harness seed
    public static FlowScreen StartScreen;

    private const int BackdropLayer = -2;
    private const int LoadingLayer = 5;
    // Harness clocks (--flowtest-nav / --flowfocus / --flowshot).
    private const double NavPlanetTime = 1.0;
    private const double NavMissionsTime = 1.8;
    private const double NavAcceptTime = 2.6;
    private const double FocusStartDelay = 0.5;
    private const double FocusStepInterval = 0.3;
    private const double FlowshotCaptureDelay = 2.2;

    private CanvasLayer _layer;
    private Control _host;
    private Control _screen;
    private string _flowshot;
    private string _flowshotPath; // the capture's full file name, fixed in _Ready when the shot is armed
    private double _shotT;
    private CanvasLayer _loadLayer;
    private Control _loadArt;
    private bool _loading;
    private double _loadT;
    private int _focusTarget, _focusDone;
    private bool _navTest;
    private int _navStep;

    public override void _Ready()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--flowshot="))
            {
                _flowshot = arg.Substring("--flowshot=".Length);
                // The image's complete name is defined HERE, at
                // setup (owner, 2026-11-27) - never assembled in
                // _Process from the live screen's node name,
                // which only happened to match.
                _flowshotPath = $"/tmp/flow_{_flowshot}.png";
            }
            if (arg.StartsWith("--flowfocus=")) _focusTarget = int.Parse(arg.Substring("--flowfocus=".Length));
            if (arg == "--flowtest-nav") _navTest = true;
        }
        UiSfx.Install(this); // UI clicks across the whole wizard (owner, 2026-11-24)
        if (_flowshot != null || _navTest) Dbg.On = true; // harness runs may log (UISFX wiring)

        // Front-door boot (owner, 2026-11-15): arriving at
        // the home screen from a left match (StartScreen
        // unset, no harness entry) starts the wizard fresh -
        // the previous run's planet, mission, arena and teams
        // do not carry over. The lab's Back button names a
        // StartScreen and keeps its state intact, as before.
        if (_flowshot == null && StartScreen == FlowScreen.None && !_navTest)
        {
            PlanetIndex = InitPlanet();
            MissionIndex = InitMission();
            ArenaIndex = InitArena();
            WorldConfig.ResetSelections();
            TeamRoster.ResetSelections();
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
            case "loading":
                // Harness: hold the loading cover up for its portrait.
                _loading = true;
                _loadLayer.Visible = true;
                GoMapType();
                break;
            case "planet":
                WorldConfig.Mode = WorldConfig.MapMode.Mission;
                GoPlanet();
                break;
            case "tournament":
                GoTournamentMap();
                break;
            case "arena":
                WorldConfig.Mode = WorldConfig.MapMode.Tournament;
                GoArena();
                break;
            case "teams":
                WorldConfig.Mode = WorldConfig.MapMode.Tournament;
                GoTeams();
                break;
            case "teams3":
                // Harness: a 3-team bracket with team 3 open, so
                // the team rows carry their '-' buttons (owner's
                // 2026-11-15 screenshot state).
                WorldConfig.Mode = WorldConfig.MapMode.Tournament;
                TeamRoster.Tourney.Clear();
                TeamRoster.Tourney.Add(new List<TeamMember> { TeamRoster.Make(RiderSpecs.Enyo), TeamRoster.Make(RiderSpecs.Hector) });
                TeamRoster.Tourney.Add(new List<TeamMember> { TeamRoster.Make(RiderSpecs.Demeter) });
                TeamRoster.Tourney.Add(new List<TeamMember> { TeamRoster.Make(RiderSpecs.Prometheus) });
                TeamRoster.TourneyTeam = 2;
                GoTeams();
                break;
            case "storyteam":
                WorldConfig.Mode = WorldConfig.MapMode.Mission;
                GoStoryTeam();
                break;
            case "storyteam2": // harness seed: two riders (active + removable)
                WorldConfig.Mode = WorldConfig.MapMode.Mission;
                TeamRoster.EnsureStory();
                TeamRoster.Story.Add(TeamRoster.Make(RiderSpecs.Hector));
                GoStoryTeam();
                break;
            case "bar":
            case "missions":
                WorldConfig.Mode = WorldConfig.MapMode.Mission;
                GoMissions();
                break;
            default:
                switch (StartScreen)
                {
                    case FlowScreen.Planet: GoPlanet(); break;
                    case FlowScreen.Missions: GoMissions(); break;
                    case FlowScreen.StoryTeam: GoStoryTeam(); break;
                    case FlowScreen.Arena: GoArena(); break;
                    case FlowScreen.Teams: GoTeams(); break;
                    default: GoMapType(); break;
                }
                StartScreen = FlowScreen.None;
                break;
        }
    }

    public override void _Process(double delta)
    {
        if (_loading) _loadT += delta;
        if (_navTest)
        {
            _shotT += delta;
            if (_navStep == 0 && _shotT > NavPlanetTime)
            {
                _navStep = 1;
                WorldConfig.Mode = WorldConfig.MapMode.Mission;
                GoPlanet();
            }
            else if (_navStep == 1 && _shotT > NavMissionsTime)
            {
                _navStep = 2;
                GoMissions();
            }
            else if (_navStep == 2 && _shotT > NavAcceptTime)
            {
                _navStep = 3;
                (_screen as MissionScreen)?.HarnessAcceptFirst();
            }
        }
        if (_focusTarget > 0)
        {
            _shotT += 0; // (timing shares _shotT below via flowshot clock)
            if (_focusDone < _focusTarget && _shotT > FocusStartDelay + _focusDone * FocusStepInterval)
            {
                Input.ParseInputEvent(new InputEventAction { Action = "ui_down", Pressed = true });
                Input.ParseInputEvent(new InputEventAction { Action = "ui_down", Pressed = false });
                _focusDone++;
            }
        }
        if (_flowshot != null)
        {
            _shotT += delta;
            if (_shotT > FlowshotCaptureDelay)
            {
                _flowshot = null;
                var img = GetViewport().GetTexture().GetImage();
                img.SavePng(_flowshotPath);
                GD.Print($"FLOW captured {_flowshotPath} size={img.GetSize()}");
                GetTree().Quit(0);
            }
        }
    }

    private void Show(Control screen)
    {
        foreach (var c in _host.GetChildren()) { _host.RemoveChild(c); c.QueueFree(); }
        _host.AddChild(screen);
        _screen = screen;
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
        _loadLayer = new CanvasLayer { Layer = LoadingLayer, Visible = false };
        AddChild(_loadLayer);
        var rect = new ColorRect { Color = FlowUi.LoadingBg };
        rect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _loadLayer.AddChild(rect);
        // The loading art (owner): the targeting-reticle
        // rider, plain, over the shared loading background.
        _loadArt = FlowUi.MakeLoadingArt();
        _loadLayer.AddChild(_loadArt);
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
        GoPlanet();
    }

    // Wizard step 1 (both modes): the planet screen - selector
    // on the left, the clickable universe map on the right.
    public void GoPlanet()
    {
        Show(new PlanetScreen { Name = "planet", Flow = this });
    }

    // Wizard step 2 (Story): the bar - mission slider on the
    // left, clickable patrons on the right.
    public void GoMissions()
    {
        Show(new MissionScreen { Name = "missions", Flow = this });
    }

    // Wizard step 3 (Story): the mission team selector -
    // the team for the signed contract is formed here, not
    // in the lab (owner, 2026-11-15). Continue goes to the
    // lab to prepare rider 1, the active rider.
    public void GoStoryTeam()
    {
        Show(new StoryTeamScreen { Name = "storyteam", Flow = this });
    }

    // Wizard step 2 (Tournament): the arena screen.
    public void GoArena()
    {
        Show(new ArenaScreen { Name = "arena", Flow = this });
    }

    // Wizard step 3 (Tournament): the team selector - teams
    // on the left, the open team's riders on the right,
    // Randomize in the control panel. From there, Continue
    // goes to the lab to prepare the player's own rider.
    public void GoTeams()
    {
        Show(new TeamSelectScreen { Name = "teams", Flow = this });
    }

    // Command Center / Rider Lab: load the duel scene, which opens
    // the RiderLab. From the orbital screen the current pick becomes
    // the contract; from the bar, the signed contract stands.
    public void EnterLab(bool fromPlanet = false)
    {
        // The orbital pick becomes the contract FIRST - reading
        // its biome before this assignment threw on a null
        // SelectedMission and the Command center button read as
        // dead (owner's report).
        if (WorldConfig.IsMission && (fromPlanet || WorldConfig.SelectedMission == null))
            WorldConfig.SelectedMission = MissionCatalog.Planets[PlanetIndex].Missions[MissionIndex];
        // The lab prepares rider 1, the active rider (owner,
        // 2026-11-15): the team itself is formed on the team
        // screen before the lab, so the edited slot is 0.
        if (WorldConfig.IsMission) { TeamRoster.EnsureStory(); TeamRoster.StorySlot = 0; }
        // The selected map's PREDEFINED biome (owner: the
        // player never picks a map type): the contract's ground
        // on the mission path, the arena's on the tournament
        // path. AOT_BIOME still overrides for harnesses.
        if (string.IsNullOrEmpty(EnvVars.Value(EnvVars.Biome)))
            WorldConfig.Biome = WorldConfig.IsMission
                ? WorldConfig.SelectedMission.Biome
                : ArenaCatalog.Arenas[ArenaIndex].Biome;
        // The contract's building kit (City contracts only;
        // tournament arenas keep the procedural buildings).
        // AOT_CITYKIT, when set, was already applied at startup.
        if (string.IsNullOrEmpty(EnvVars.Value(EnvVars.CityKit)))
            WorldConfig.CityKit = WorldConfig.IsMission
                ? WorldConfig.SelectedMission.CityKit : -1;
        if (string.IsNullOrEmpty(EnvVars.Value(EnvVars.Flat)))
            WorldConfig.FlatWorld = WorldConfig.IsMission
                && WorldConfig.SelectedMission.FlatTerrain;
        if (string.IsNullOrEmpty(EnvVars.Value(EnvVars.Snow)))
            WorldConfig.SnowGround = WorldConfig.IsMission
                && WorldConfig.SelectedMission.SnowGround;
        if (string.IsNullOrEmpty(EnvVars.Value(EnvVars.Terraces)))
            WorldConfig.TerracedCity = WorldConfig.IsMission
                && WorldConfig.SelectedMission.Terraces;
        if (string.IsNullOrEmpty(EnvVars.Value(EnvVars.Ascent)))
            WorldConfig.Ascent = WorldConfig.IsMission
                && WorldConfig.SelectedMission.Ascent;
        if (string.IsNullOrEmpty(EnvVars.Value(EnvVars.Photoreal)))
            WorldConfig.Photoreal = WorldConfig.IsMission
                && WorldConfig.SelectedMission.Photoreal;
        if (string.IsNullOrEmpty(EnvVars.Value(EnvVars.Hills)))
            WorldConfig.Hills = WorldConfig.IsMission
                && WorldConfig.SelectedMission.Hills;
        if (string.IsNullOrEmpty(EnvVars.Value(EnvVars.Sky)))
            WorldConfig.SkyTex = WorldConfig.IsMission
                ? WorldConfig.SelectedMission.SkyTex
                : ArenaCatalog.Arenas[ArenaIndex].SkyTex;
        if (string.IsNullOrEmpty(EnvVars.Value(EnvVars.SkyTile)))
            WorldConfig.SkyTile = WorldConfig.IsMission
                ? WorldConfig.SelectedMission.SkyTile
                : "";
        if (string.IsNullOrEmpty(EnvVars.Value(EnvVars.Stars)))
            WorldConfig.SkyStars = WorldConfig.IsMission
                && WorldConfig.SelectedMission.ProceduralStars;
        FromFlow = true;
        EnterLabScene();
    }

    // The duel scene takes seconds to build (terrain, textures,
    // riders), which used to read as a freeze. Show the loading
    // overlay, let it paint, then load the scene on a thread and
    // swap when it is ready - the overlay's last frame stays up
    // through the brief instantiation hitch.
    private async void EnterLabScene()
    {
        if (_loading) return;
        _loading = true;
        _loadLayer.Visible = true;
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        const string path = Paths.DuelScene;
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
        var layer = new CanvasLayer { Layer = BackdropLayer };
        AddChild(layer);
        var bg = new TextureRect
        {
            Texture = GD.Load<Texture2D>(Paths.SpaceBackground),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        bg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(bg);
    }
}
