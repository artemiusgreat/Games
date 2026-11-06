using Godot;

// The game's front door (project main scene). A quiet orbital
// backdrop — star panorama and a slowly turning planet — behind a
// stack of full-screen UI steps:
//
//   MapType ──Tournament──► PhotoDuel (arena) ──► lab ──► duel
//      │
//      └────Mission────► Planet ──► Bar ──► PhotoDuel (front) ──► lab ──► duel
//                          │
//                          └──── Command Center ──► lab (skips the bar)
//
// The flow only CHOOSES; WorldConfig carries the choice into the
// duel scene, which builds the right world and opens the RiderLab
// as before. Harness: --flowshot=maptype|planet|bar renders one
// screen, saves /tmp/flow_<name>.png and quits.
public partial class FlowMain : Node3D
{
    public int PlanetIndex = 0;
    public int MissionIndex = 0;

    private CanvasLayer _layer;
    private Control _host;
    private MeshInstance3D _planet;
    private string _flowshot;
    private double _shotT;

    public override void _Ready()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--flowshot=")) _flowshot = arg.Substring("--flowshot=".Length);

        BuildBackdrop();
        _layer = new CanvasLayer();
        AddChild(_layer);
        _host = new Control();
        _host.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _layer.AddChild(_host);

        switch (_flowshot)
        {
            case "planet":
                WorldConfig.Mode = WorldConfig.MapMode.Mission;
                WorldConfig.SelectedMission = MissionCatalog.Planets[0].Missions[0];
                GoPlanet();
                break;
            case "bar":
                WorldConfig.Mode = WorldConfig.MapMode.Mission;
                WorldConfig.SelectedMission = MissionCatalog.Planets[0].Missions[0];
                GoBar();
                break;
            default:
                GoMapType();
                break;
        }
    }

    public override void _Process(double delta)
    {
        if (_planet != null) _planet.RotateY((float)delta * 0.05f);
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
        foreach (var c in _host.GetChildren()) c.QueueFree();
        _host.AddChild(screen);
    }

    public void GoMapType()
    {
        var s = new MapTypeScreen { Name = "maptype" };
        s.OnTournament = () =>
        {
            WorldConfig.Mode = WorldConfig.MapMode.Tournament;
            WorldConfig.SelectedMission = null;
            EnterLabScene();
        };
        s.OnMission = () =>
        {
            WorldConfig.Mode = WorldConfig.MapMode.Mission;
            GoPlanet();
        };
        Show(s);
    }

    public void GoPlanet()
    {
        Show(new PlanetScreen { Name = "planet", Flow = this });
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
        if (WorldConfig.IsMission && (fromPlanet || WorldConfig.SelectedMission == null))
            WorldConfig.SelectedMission = MissionCatalog.Planets[PlanetIndex].Missions[MissionIndex];
        EnterLabScene();
    }

    private void EnterLabScene() => GetTree().ChangeSceneToFile("res://photo/PhotoDuel.tscn");

    private void BuildBackdrop()
    {
        var we = new WorldEnvironment
        {
            Environment = new Environment
            {
                BackgroundMode = Environment.BGMode.Sky,
                Sky = new Sky
                {
                    SkyMaterial = new PanoramaSkyMaterial { Panorama = FlowUi.StarTexture() }
                },
                AmbientLightSource = Environment.AmbientSource.Sky,
                AmbientLightEnergy = 0.35f,
                TonemapMode = Environment.ToneMapper.Aces
            }
        };
        AddChild(we);
        AddChild(new Camera3D { Position = new Vector3(0f, 0.6f, 9.5f), Fov = 55f });
        var sun = new DirectionalLight3D
        {
            LightEnergy = 1.1f,
            LightColor = new Color(1f, 0.95f, 0.88f),
            Rotation = new Vector3(-0.5f, -0.7f, 0f),
            ShadowEnabled = false
        };
        AddChild(sun);
        var tex = FlowUi.PlanetTexture(MissionCatalog.Planets[0], 512, 1.0f);
        _planet = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 3.1f, Height = 6.2f, RadialSegments = 48, Rings = 24 },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoTexture = tex, Roughness = 0.85f,
                EmissionEnabled = true, EmissionTexture = tex,
                Emission = new Color(0.35f, 0.38f, 0.45f), EmissionEnergyMultiplier = 0.25f
            },
            Position = new Vector3(3.4f, 0.9f, -1.5f)
        };
        AddChild(_planet);
    }
}
