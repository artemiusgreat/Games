using Godot;

// Front-end step 3 (Mission path): the port bar, in the spirit of
// Freelancer. A small 3D diorama (counter, stools, patrons, a holo
// of the planet) behind the contract UI: patrons offer the planet's
// missions, accepting one signs the contract, and the Rider Lab
// button deploys. Equipment is greyed — a later build's room.
public partial class BarScreen : Control
{
    public FlowMain Flow;

    private VBoxContainer _offers;
    private Label _status;
    private MissionDef _signedHere; // contract signed in this visit

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        BuildDiorama();

        var ui = new VBoxContainer();
        ui.AddThemeConstantOverride("separation", 10);
        var pad = new MarginContainer();
        pad.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        pad.AddThemeConstantOverride("margin_left", 18);
        pad.AddThemeConstantOverride("margin_right", 18);
        pad.AddThemeConstantOverride("margin_top", 12);
        pad.AddThemeConstantOverride("margin_bottom", 12);
        AddChild(pad);
        pad.AddChild(ui);

        // ---- Freelancer-style facility icon bar ----
        var icons = new HBoxContainer();
        icons.AddThemeConstantOverride("separation", 10);
        ui.AddChild(icons);
        var bar = FlowUi.MakeButton("◆ Bar", 15);
        bar.AddThemeColorOverride("font_color", FlowUi.Gold);
        icons.AddChild(bar);
        icons.AddChild(FlowUi.MakeButton("◆ Job offers", 15));
        var equip = FlowUi.MakeButton("◆ Equipment", 15);
        equip.Disabled = true;
        equip.TooltipText = "The equipment dealer opens in a later build.";
        icons.AddChild(equip);
        var backTop = FlowUi.MakeButton("Back to orbit", 15);
        backTop.Pressed += () => Flow.GoPlanet();
        icons.AddChild(backTop);
        icons.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.Expand });
        var lab = FlowUi.MakeButton("Rider lab", 16);
        lab.Pressed += () => Flow.EnterLab();
        icons.AddChild(lab);

        ui.AddChild(new Control { SizeFlagsVertical = SizeFlags.Expand });

        // ---- offers panel ----
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", FlowUi.Panel(FlowUi.Navy));
        ui.AddChild(panel);
        var pv = new VBoxContainer();
        pv.AddThemeConstantOverride("separation", 8);
        panel.AddChild(pv);
        pv.AddChild(FlowUi.MakeHeading($"Contract offers — {Planet.Name}", 17, FlowUi.Gold));
        _status = FlowUi.MakeLabel("", 12, FlowUi.Dim);
        pv.AddChild(_status);
        _offers = new VBoxContainer();
        _offers.AddThemeConstantOverride("separation", 8);
        pv.AddChild(_offers);

        ui.AddChild(FlowUi.MakeLabel("The bar keeps its own hours. Contracts signed here override the orbital selection.", 11, FlowUi.Dim, HorizontalAlignment.Left, wrap: true));

        RefreshOffers();
    }

    private PlanetDef Planet => MissionCatalog.Planets[Flow.PlanetIndex];

    private void RefreshOffers()
    {
        foreach (var c in _offers.GetChildren()) c.QueueFree();
        var current = WorldConfig.SelectedMission;
        _status.Text = _signedHere != null
            ? $"Contract signed: {_signedHere.Name} — {(_signedHere.Payment):N0} credits with {_signedHere.Employer}."
            : current != null
                ? $"Orbital selection on file: {current.Name}. A signature here replaces it."
                : "No contract on file yet.";
        foreach (var m in Planet.Missions)
        {
            var row = new PanelContainer();
            row.AddThemeStyleboxOverride("panel", FlowUi.Panel(FlowUi.NavySoft, 0.85f));
            var h = new HBoxContainer();
            h.AddThemeConstantOverride("separation", 12);
            row.AddChild(h);
            var tx = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.Expand };
            tx.AddThemeConstantOverride("separation", 2);
            h.AddChild(tx);
            tx.AddChild(FlowUi.MakeHeading(m.Patron, 14, FlowUi.Gold, bold: false));
            tx.AddChild(FlowUi.MakeLabel($"“{m.Pitch}”", 12, FlowUi.Ink, HorizontalAlignment.Left, wrap: true));
            tx.AddChild(FlowUi.MakeLabel($"{m.Name} · {m.Type} · {m.Payment:N0} credits · Threat {FlowUi.ThreatPips(m.Threat)}", 11, FlowUi.Dim));
            bool signed = _signedHere == m || (_signedHere == null && current == m);
            var b = FlowUi.MakeButton(signed ? "Signed ✓" : "Accept", 13);
            b.Disabled = signed;
            b.Pressed += () =>
            {
                _signedHere = m;
                WorldConfig.SelectedMission = m;
                RefreshOffers();
            };
            h.AddChild(b);
            _offers.AddChild(row);
        }
    }

    // ---- the diorama: a port bar from primitives in a SubViewport ----
    private void BuildDiorama()
    {
        var svc = new SubViewportContainer
        {
            Stretch = true,
            MouseFilter = MouseFilterEnum.Ignore
        };
        svc.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(svc);
        var sv = new SubViewport
        {
            Size = new Vector2I(1152, 648),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            OwnWorld3D = true
        };
        svc.AddChild(sv);

        var we = new WorldEnvironment
        {
            Environment = new Environment
            {
                BackgroundMode = Environment.BGMode.Color,
                BackgroundColor = new Color(0.015f, 0.02f, 0.03f),
                AmbientLightSource = Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.42f, 0.45f, 0.55f),
                AmbientLightEnergy = 0.85f,
                TonemapMode = Environment.ToneMapper.Aces
            }
        };
        sv.AddChild(we);

        var cam = new Camera3D { Position = new Vector3(0f, 2.3f, 6.2f), Fov = 62f };
        sv.AddChild(cam);
        cam.LookAt(new Vector3(0.3f, 1.0f, -2.6f), Vector3.Up);
        sv.AddChild(new DirectionalLight3D
        {
            LightEnergy = 0.55f, LightColor = new Color(1f, 0.9f, 0.78f),
            Rotation = new Vector3(-0.9f, 0.4f, 0f), ShadowEnabled = false
        });

        var root = new Node3D();
        sv.AddChild(root);
        StandardMaterial3D Mat(Color c, float rough = 0.9f, float emis = 0f) => new StandardMaterial3D
        {
            AlbedoColor = c, Roughness = rough,
            EmissionEnabled = emis > 0f, Emission = c, EmissionEnergyMultiplier = emis
        };
        MeshInstance3D Box(Vector3 pos, Vector3 size, Material m)
        {
            var mi = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = size },
                MaterialOverride = m, Position = pos
            };
            root.AddChild(mi); return mi;
        }
        var floorMat = Mat(new Color(0.16f, 0.11f, 0.07f), 0.95f);
        var wallMat = Mat(new Color(0.10f, 0.11f, 0.14f), 0.95f);
        var woodMat = Mat(new Color(0.30f, 0.19f, 0.10f), 0.7f);
        var brassMat = Mat(new Color(0.55f, 0.42f, 0.18f), 0.45f);

        Box(new Vector3(0f, -0.05f, 0f), new Vector3(16f, 0.1f, 14f), floorMat);            // floor
        Box(new Vector3(0f, 2.6f, -4.2f), new Vector3(16f, 5.2f, 0.3f), wallMat);          // back wall
        Box(new Vector3(-5.6f, 2.6f, 0f), new Vector3(0.3f, 5.2f, 12f), wallMat);          // left wall
        Box(new Vector3(0f, 4.4f, 0f), new Vector3(16f, 0.3f, 14f), wallMat);              // ceiling
        // Bar counter + glowing front strip + top slab.
        Box(new Vector3(0.7f, 0.55f, -2.5f), new Vector3(6.4f, 1.1f, 0.9f), woodMat);
        Box(new Vector3(0.7f, 1.14f, -2.5f), new Vector3(6.8f, 0.08f, 1.15f), brassMat);
        Box(new Vector3(0.7f, 0.72f, -2.02f), new Vector3(6.4f, 0.10f, 0.04f),
            Mat(new Color(0.25f, 0.85f, 0.95f), 0.4f, 1.6f));
        // Back-bar shelves + bottles.
        for (int sIdx = 0; sIdx < 2; sIdx++)
            Box(new Vector3(0.7f, 1.9f + sIdx * 0.75f, -3.95f), new Vector3(6.2f, 0.06f, 0.35f), woodMat);
        var rng = new RandomNumberGenerator { Seed = 777 };
        var bottleCols = new[]
        {
            new Color(0.2f, 0.75f, 0.35f), new Color(0.85f, 0.55f, 0.15f),
            new Color(0.3f, 0.5f, 0.95f), new Color(0.8f, 0.25f, 0.3f)
        };
        for (int i = 0; i < 26; i++)
        {
            float x = -2.2f + i * 0.235f;
            float y = 2.06f + (i % 2) * 0.75f;
            Box(new Vector3(x, y + 0.14f, -3.95f), new Vector3(0.09f, 0.28f, 0.09f),
                Mat(bottleCols[i % bottleCols.Length], 0.3f, 0.55f));
        }
        // Stools in front of the counter.
        for (int i = 0; i < 4; i++)
        {
            float x = -1.6f + i * 1.5f;
            Box(new Vector3(x, 0.28f, -1.55f), new Vector3(0.12f, 0.56f, 0.12f), brassMat);
            var seat = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.30f, BottomRadius = 0.30f, Height = 0.09f, RadialSegments = 12 },
                MaterialOverride = Mat(new Color(0.45f, 0.16f, 0.12f), 0.8f),
                Position = new Vector3(x, 0.60f, -1.55f)
            };
            root.AddChild(seat);
        }
        // Two side tables with chairs.
        foreach (var tx in new[] { -3.9f, 4.3f })
        {
            var top = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 0.62f, BottomRadius = 0.62f, Height = 0.06f, RadialSegments = 14 },
                MaterialOverride = woodMat,
                Position = new Vector3(tx, 0.78f, -0.6f)
            };
            root.AddChild(top);
            Box(new Vector3(tx, 0.39f, -0.6f), new Vector3(0.1f, 0.78f, 0.1f), brassMat);
            Box(new Vector3(tx - 0.95f, 0.45f, -0.6f), new Vector3(0.5f, 0.9f, 0.5f), Mat(new Color(0.13f, 0.15f, 0.19f)));
            // A drink on each table.
            Box(new Vector3(tx + 0.2f, 0.90f, -0.5f), new Vector3(0.08f, 0.18f, 0.08f),
                Mat(new Color(0.95f, 0.7f, 0.25f), 0.25f, 0.8f));
        }
        // Patrons: capsule bodies + heads, one bartender behind the bar.
        Patron(root, new Vector3(0.9f, 0f, -3.3f), new Color(0.55f, 0.48f, 0.40f), 0f);          // bartender
        Patron(root, new Vector3(-1.6f, -0.42f, -1.45f), new Color(0.20f, 0.30f, 0.55f), 3.14f); // seated at the bar
        Patron(root, new Vector3(0.9f, -0.42f, -1.5f), new Color(0.45f, 0.20f, 0.16f), 3.14f);   // seated
        Patron(root, new Vector3(-3.1f, 0f, -0.5f), new Color(0.25f, 0.38f, 0.28f), 1.35f);      // standing by a table
        Patron(root, new Vector3(4.6f, 0f, -2.9f), new Color(0.38f, 0.33f, 0.45f), -0.7f);       // standing at the counter end
        // Holo planet over the right end of the bar.
        var holo = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.42f, Height = 0.84f, RadialSegments = 24, Rings = 12 },
            Position = new Vector3(3.6f, 1.75f, -2.6f)
        };
        var holoMat = new StandardMaterial3D
        {
            AlbedoTexture = FlowUi.PlanetTexture(Planet, 256, 3.3f),
            EmissionEnabled = true,
            EmissionTexture = FlowUi.PlanetTexture(Planet, 256, 3.3f),
            Emission = new Color(0.6f, 0.75f, 1.0f),
            EmissionEnergyMultiplier = 0.7f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
        };
        holo.MaterialOverride = holoMat;
        root.AddChild(holo);
        _holo = holo;
        // Warm pendant lights.
        foreach (var lp in new[] { new Vector3(0.7f, 3.1f, -2.2f), new Vector3(-3.9f, 2.9f, -0.6f), new Vector3(4.3f, 2.9f, -0.6f) })
        {
            var omni = new OmniLight3D
            {
                Position = lp,
                LightColor = new Color(1f, 0.72f, 0.42f),
                LightEnergy = 2.4f,
                OmniRange = 7f
            };
            root.AddChild(omni);
            Box(lp + new Vector3(0f, 0.35f, 0f), new Vector3(0.3f, 0.12f, 0.3f), brassMat);
        }
    }

    private MeshInstance3D _holo;

    public override void _Process(double delta)
    {
        if (_holo != null) _holo.RotateY((float)delta * 0.35f);
    }

    private static void Patron(Node3D root, Vector3 pos, Color suit, float yaw)
    {
        var holder = new Node3D { Position = pos, Rotation = new Vector3(0f, yaw, 0f) };
        root.AddChild(holder);
        var body = new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = 0.30f, Height = 1.45f, RadialSegments = 10 },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = suit, Roughness = 0.9f },
            Position = new Vector3(0f, 0.78f, 0f)
        };
        holder.AddChild(body);
        var head = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.20f, Height = 0.40f, RadialSegments = 12, Rings = 8 },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.78f, 0.62f, 0.50f), Roughness = 0.7f },
            Position = new Vector3(0f, 1.68f, 0f)
        };
        holder.AddChild(head);
    }
}
