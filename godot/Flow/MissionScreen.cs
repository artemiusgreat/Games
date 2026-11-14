using Godot;
using System.Collections.Generic;

// Wizard step 2 (Story, owner — teams stage): pick the mission.
// Left: the mission selector (slider) with the briefing monitor
// and the offer rows — every available mission with its own
// Accept button (the old "Command center" top button is gone).
// Right: the 3D port bar; the patrons standing in it are the
// people offering the missions on the left, name-labelled and
// CLICKABLE — clicking a patron selects their mission in the
// slider, exactly like clicking the offer row. Accept signs
// the mission and advances to the Rider Lab.
public partial class MissionScreen : Control
{
    public FlowMain Flow;

    private Label _selName, _briefBody, _status;
    private ScrollContainer _briefScroll;
    private VBoxContainer _offers;
    private MeshInstance3D _holo;
    private readonly List<Node3D> _figures = new();
    private readonly List<(Label3D Name, MeshInstance3D Ring, int Mission)> _patronUi = new();
    // Figure slots in assignment order: the two bar stools, the
    // table guest, the counter end, and the bartender last.
    private static readonly int[] FigureOrder = { 1, 2, 3, 4, 0 };

    private PlanetDef Planet => MissionCatalog.Planets[FlowMain.PlanetIndex];
    private MissionDef Mission => Planet.Missions[FlowMain.MissionIndex];

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

        // ---- menu bar: Back (+ Rider Lab once a mission is
        // signed on this planet) ----
        var menu = new HBoxContainer();
        menu.AddThemeConstantOverride("separation", 10);
        root.AddChild(menu);
        var back = FlowUi.MakeButton("Back", 14);
        back.Pressed += () => Flow.GoPlanet();
        menu.AddChild(back);
        var signed = WorldConfig.SelectedMission;
        if (signed != null && signed.Planet == Planet)
        {
            var toLab = FlowUi.MakeButton("Rider Lab", 14);
            toLab.Pressed += () => Flow.EnterLab();
            menu.AddChild(toLab);
        }

        // ---- middle: contracts | bar ----
        var mid = new HBoxContainer();
        mid.AddThemeConstantOverride("separation", 12);
        mid.SizeFlagsVertical = SizeFlags.ExpandFill;
        root.AddChild(mid);

        var side = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.0f };
        side.AddThemeStyleboxOverride("panel", FlowUi.Panel(FlowUi.Navy));
        mid.AddChild(side);
        var sv = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        sv.AddThemeConstantOverride("separation", 7);
        side.AddChild(sv);
        sv.AddChild(FlowUi.MakeHeading("Mission", 13, FlowUi.Gold, bold: false));
        sv.AddChild(FlowUi.SelectorRow(out _selName, 15, StepMission));
        var briefScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0f, 120f),
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 0.8f,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        _briefScroll = briefScroll;
        _briefBody = FlowUi.MakeLabel("", 13, FlowUi.ScreenInk, HorizontalAlignment.Left, wrap: true);
        _briefBody.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        briefScroll.AddChild(_briefBody);
        briefScroll.Resized += () => FlowUi.FitTextWidth(briefScroll, _briefBody);
        var briefMonitor = FlowUi.Monitor(briefScroll);
        briefMonitor.SizeFlagsVertical = SizeFlags.ExpandFill;
        sv.AddChild(briefMonitor);
        _status = FlowUi.MakeLabel("", 12, FlowUi.Dim, HorizontalAlignment.Left, wrap: true);
        sv.AddChild(_status);
        var offersScroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 1.2f,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        sv.AddChild(offersScroll);
        _offers = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _offers.AddThemeConstantOverride("separation", 8);
        offersScroll.AddChild(_offers);

        var barPanel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1.25f };
        barPanel.AddThemeStyleboxOverride("panel", FlowUi.Panel(FlowUi.NavySoft, 0.75f));
        mid.AddChild(barPanel);
        BuildDiorama(barPanel);

        ClampToUnlocked();
        Refresh();
        CallDeferred("Refresh");
    }

    private void ClampToUnlocked()
    {
        if (!StoryProgress.IsUnlocked(FlowMain.PlanetIndex, FlowMain.MissionIndex))
            FlowMain.MissionIndex = StoryProgress.FirstUnlocked(FlowMain.PlanetIndex);
    }

    private void StepMission(int d)
    {
        var un = StoryProgress.UnlockedList(FlowMain.PlanetIndex);
        if (un.Count == 0) return;
        int cur = un.IndexOf(FlowMain.MissionIndex);
        if (cur < 0) cur = 0;
        FlowMain.MissionIndex = un[(cur + d + un.Count) % un.Count];
        Refresh();
    }

    private void Select(int missionIdx)
    {
        FlowMain.MissionIndex = missionIdx;
        Refresh();
    }

    private void Accept(int missionIdx)
    {
        FlowMain.MissionIndex = missionIdx;
        WorldConfig.SelectedMission = Planet.Missions[missionIdx];
        Flow.EnterLab();
    }

    // Harness hook (--flowtest-nav): sign the first available
    // mission and deploy, as the player's clicks would.
    public void HarnessAcceptFirst()
    {
        var un = StoryProgress.UnlockedList(FlowMain.PlanetIndex);
        if (un.Count > 0) Accept(un[0]);
    }

    private void Refresh()
    {
        if (_selName == null) return;
        ClampToUnlocked();
        var m = Mission;
        _selName.Text = m.Name;
        _briefBody.Text =
            $"Mission: {m.Name}\nType: {m.Type}\nTerrain: {m.Biome}\nEmployer: {m.Employer}\n" +
            $"Payment: {m.Payment:N0} credits\nThreat: {FlowUi.ThreatPips(m.Threat)}\n\n" +
            $"{m.Site} — {m.Briefing}";
        var signed = WorldConfig.SelectedMission;
        _status.Text = signed != null && signed.Planet == Planet
            ? $"Mission signed: {signed.Name}. Accepting another replaces it."
            : "Click a patron in the bar, or a contract below — Accept signs and deploys.";

        foreach (var c in _offers.GetChildren()) c.QueueFree();
        foreach (int mi in StoryProgress.UnlockedList(FlowMain.PlanetIndex))
        {
            var mm = Planet.Missions[mi];
            int idx = mi;
            var row = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
            row.AddThemeStyleboxOverride("panel", FlowUi.Panel(
                mi == FlowMain.MissionIndex ? FlowUi.Navy : FlowUi.NavySoft, 0.9f));
            row.GuiInput += ev =>
            {
                if (ev is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                    Select(idx);
            };
            var h = new HBoxContainer();
            h.AddThemeConstantOverride("separation", 12);
            row.AddChild(h);
            var tx = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.Expand, MouseFilter = MouseFilterEnum.Ignore };
            tx.AddThemeConstantOverride("separation", 2);
            h.AddChild(tx);
            var patron = FlowUi.MakeHeading(mm.Patron, 14,
                mi == FlowMain.MissionIndex ? FlowUi.Gold : FlowUi.Ink, bold: false);
            patron.MouseFilter = MouseFilterEnum.Ignore;
            tx.AddChild(patron);
            var pitch = FlowUi.MakeLabel($"“{mm.Pitch}”", 12, FlowUi.Ink, HorizontalAlignment.Left, wrap: true);
            pitch.MouseFilter = MouseFilterEnum.Ignore;
            tx.AddChild(pitch);
            var meta = FlowUi.MakeLabel($"{mm.Name} · {mm.Type} · {mm.Payment:N0} credits · Threat {FlowUi.ThreatPips(mm.Threat)}", 11, FlowUi.Dim);
            meta.MouseFilter = MouseFilterEnum.Ignore;
            tx.AddChild(meta);
            bool isSigned = signed == mm;
            var b = FlowUi.MakeButton(isSigned ? "Signed" : "Accept", 13);
            b.CustomMinimumSize = new Vector2(124f, 0f);
            b.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            b.Disabled = isSigned;
            b.Pressed += () => Accept(idx);
            h.AddChild(b);
            _offers.AddChild(row);
        }
        RefreshPatrons();
    }

    // Map the unlocked missions onto the bar's figures (in
    // FigureOrder), (re)labelling each patron and showing the
    // selection ring under the selected mission's patron.
    private void RefreshPatrons()
    {
        var un = StoryProgress.UnlockedList(FlowMain.PlanetIndex);
        for (int s = 0; s < _patronUi.Count; s++)
        {
            var (label, ring, _) = _patronUi[s];
            if (s < un.Count)
            {
                var mm = Planet.Missions[un[s]];
                bool sel = un[s] == FlowMain.MissionIndex;
                label.Text = mm.Patron;
                label.Modulate = sel ? FlowUi.Gold : FlowUi.Ink;
                label.Visible = true;
                ring.Visible = sel;
                _patronUi[s] = (label, ring, un[s]);
            }
            else
            {
                label.Visible = false;
                ring.Visible = false;
                _patronUi[s] = (label, ring, -1);
            }
        }
    }

    // ---- the diorama: a port bar from primitives in a SubViewport ----
    private void BuildDiorama(Control hostPanel)
    {
        var svc = new SubViewportContainer
        {
            Stretch = true,
            MouseFilter = MouseFilterEnum.Stop
        };
        hostPanel.AddChild(svc);
        var sv = new SubViewport
        {
            Size = new Vector2I(1152, 648),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            OwnWorld3D = true,
            PhysicsObjectPicking = true
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
            Box(new Vector3(tx + 0.2f, 0.90f, -0.5f), new Vector3(0.08f, 0.18f, 0.08f),
                Mat(new Color(0.95f, 0.7f, 0.25f), 0.25f, 0.8f));
        }
        // Patrons: capsule bodies + heads, one bartender behind
        // the bar. Holders are recorded in creation order and
        // mapped to missions by FigureOrder in RefreshPatrons.
        Patron(root, new Vector3(0.9f, 0f, -3.3f), new Color(0.55f, 0.48f, 0.40f), 0f);          // 0 bartender
        Patron(root, new Vector3(-1.6f, -0.42f, -1.45f), new Color(0.20f, 0.30f, 0.55f), 3.14f); // 1 seated at the bar
        Patron(root, new Vector3(0.9f, -0.42f, -1.5f), new Color(0.45f, 0.20f, 0.16f), 3.14f);   // 2 seated
        Patron(root, new Vector3(-3.1f, 0f, -0.5f), new Color(0.25f, 0.38f, 0.28f), 1.35f);      // 3 standing by a table
        Patron(root, new Vector3(4.6f, 0f, -2.9f), new Color(0.38f, 0.33f, 0.45f), -0.7f);       // 4 standing at the counter end
        BuildPatronUi(root);
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

    // Name label, selection ring and click area for every
    // figure slot (in FigureOrder). Labels/rings start hidden;
    // RefreshPatrons assigns them to the unlocked missions.
    private void BuildPatronUi(Node3D root)
    {
        foreach (int fi in FigureOrder)
        {
            var holder = _figures[fi];
            var hp = holder.Position;
            var label = new Label3D
            {
                Text = "",
                PixelSize = 0.0042f,
                FontSize = 52,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                Shaded = false,
                Modulate = FlowUi.Ink,
                OutlineSize = 10,
                OutlineModulate = new Color(0, 0, 0, 0.85f),
                HorizontalAlignment = HorizontalAlignment.Center,
                Position = hp + new Vector3(0f, 2.12f, 0f),
                Visible = false
            };
            root.AddChild(label);
            var ring = new MeshInstance3D
            {
                Mesh = new TorusMesh { InnerRadius = 0.44f, OuterRadius = 0.56f, Rings = 24, RingSegments = 6 },
                Position = new Vector3(hp.X, 0.04f, hp.Z),
                Visible = false
            };
            ring.MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = FlowUi.Gold,
                EmissionEnabled = true,
                Emission = FlowUi.Gold,
                EmissionEnergyMultiplier = 1.4f,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
            };
            root.AddChild(ring);
            var area = new Area3D { Position = hp + new Vector3(0f, 1.15f, 0f) };
            var shape = new CollisionShape3D
            {
                Shape = new SphereShape3D { Radius = 1.0f }
            };
            area.AddChild(shape);
            int slot = _patronUi.Count;
            area.InputEvent += (cam, ev, pos, normal, shapeIdx) =>
            {
                if (ev is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                {
                    int mi = _patronUi[slot].Mission;
                    if (mi >= 0) Select(mi);
                }
            };
            area.MouseEntered += () => MouseDefaultCursorShape = CursorShape.PointingHand;
            area.MouseExited += () => MouseDefaultCursorShape = CursorShape.Arrow;
            root.AddChild(area);
            _patronUi.Add((label, ring, -1));
        }
    }

    public override void _Process(double delta)
    {
        if (_holo != null) _holo.RotateY((float)delta * 0.35f);
    }

    private void Patron(Node3D root, Vector3 pos, Color suit, float yaw)
    {
        var holder = new Node3D { Position = pos, Rotation = new Vector3(0f, yaw, 0f) };
        root.AddChild(holder);
        _figures.Add(holder);
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
