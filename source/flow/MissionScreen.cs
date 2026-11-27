using Godot;
using System.Collections.Generic;

// Wizard step 2 (Story, owner - teams stage): pick the mission.
// Left: the mission selector (slider) with the briefing monitor
// and the offer rows - every available mission with its own
// Accept button (the old "Command center" top button is gone).
// Right: the 3D port bar; the patrons standing in it are the
// people offering the missions on the left, name-labelled and
// CLICKABLE - clicking a patron selects their mission in the
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

    private const float BriefScrollMinHeight = 120f;
    private const float BriefScrollStretchRatio = 0.8f;
    private const float OffersStretchRatio = 1.2f;
    private const float BarPanelStretchRatio = 1.25f;
    private const float AcceptButtonWidth = 124f;
    private const int OfferTitleFontSize = 14;
    private const int OfferRowSeparation = 12;
    private const int DioramaWidth = 1152;
    private const int DioramaHeight = 648;
    private const float DioramaCameraFov = 62f;
    // Diorama lighting: backdrop, ambient and the key light.
    private static readonly Color DioramaBgColor = new(0.015f, 0.02f, 0.03f);
    private static readonly Color DioramaAmbientColor = new(0.42f, 0.45f, 0.55f);
    private const float DioramaAmbientEnergy = 0.85f;
    private const float DioramaSunEnergy = 0.55f;
    private static readonly Color DioramaSunColor = new(1f, 0.9f, 0.78f);
    private const float DioramaDefaultRoughness = 0.9f; // the diorama Mat() helper's default
    // Bar-scene factory values (the furniture placement is
    // scene data; these are the shared piece recipes).
    private const float StoolSeatRadius = 0.30f, StoolSeatHeight = 0.09f;
    private const int StoolSeatSegments = 12;
    private const float TableTopRadius = 0.62f, TableTopHeight = 0.06f;
    private const int TableTopSegments = 14;
    private const float HoloRadius = 0.42f;
    private const int HoloSegments = 24, HoloRings = 12;
    private const int HoloTextureSize = 256;
    private const float HoloTextureSeed = 3.3f;
    private static readonly Color HoloEmissionColor = new(0.6f, 0.75f, 1.0f);
    private const float HoloEmissionEnergy = 0.7f;
    private const float HoloSpinRate = 0.35f; // rad/s, the holo planet's rotation
    private static readonly Color PendantLightColor = new(1f, 0.72f, 0.42f);
    private const float PendantLightEnergy = 2.4f, PendantLightRange = 7f;
    private const float PatronLabelPixelSize = 0.0042f;
    private const int PatronLabelFontSize = 52, PatronLabelOutlineSize = 10;
    private static readonly Color PatronLabelOutlineColor = new(0, 0, 0, 0.85f);
    private const float PatronRingInnerRadius = 0.44f, PatronRingOuterRadius = 0.56f;
    private const int PatronRingSegments = 24, PatronRingSegmentSides = 6;
    private const float PatronRingEmissionEnergy = 1.4f;
    private const float PatronClickRadius = 1.0f;
    private const float PatronBodyRadius = 0.30f, PatronBodyHeight = 1.45f;
    private const int PatronBodySegments = 10;
    private const float PatronSuitRoughness = 0.9f;
    private const float PatronHeadRadius = 0.20f;
    private const int PatronHeadSegments = 12, PatronHeadRings = 8;
    private static readonly Color PatronSkinColor = new(0.78f, 0.62f, 0.50f);
    private const float PatronSkinRoughness = 0.7f;
    // Diorama camera and sun (owner, 2026-11-27).
    private static readonly Vector3 DioramaCameraPosition = new(0f, 2.3f, 6.2f);
    private static readonly Vector3 DioramaCameraTarget = new(0.3f, 1.0f, -2.6f);
    private static readonly Vector3 DioramaSunRotation = new(-0.9f, 0.4f, 0f);
    // Diorama material palette (the Mat() recipes' colours
    // and roughnesses; furniture placement stays scene data
    // in the Box() calls).
    private static readonly Color FloorColor = new(0.16f, 0.11f, 0.07f);
    private const float FloorRoughness = 0.95f;
    private static readonly Color WallColor = new(0.10f, 0.11f, 0.14f);
    private const float WallRoughness = 0.95f;
    private static readonly Color WoodColor = new(0.30f, 0.19f, 0.10f);
    private const float WoodRoughness = 0.7f;
    private static readonly Color BrassColor = new(0.55f, 0.42f, 0.18f);
    private const float BrassRoughness = 0.45f;
    private static readonly Color StripLightColor = new(0.25f, 0.85f, 0.95f); // the counter's glowing front strip
    private const float StripLightRoughness = 0.4f, StripLightEmission = 1.6f;
    private static readonly Color SeatColor = new(0.45f, 0.16f, 0.12f);
    private const float SeatRoughness = 0.8f;
    private static readonly Color ChairColor = new(0.13f, 0.15f, 0.19f);
    private static readonly Color CandleColor = new(0.95f, 0.7f, 0.25f);
    private const float CandleRoughness = 0.25f, CandleEmission = 0.8f;
    // Back-bar bottle row: count, spacing and the bottle
    // piece recipe (the shelves share the row step).
    private const float ShelfRowStep = 0.75f;
    private static readonly Color[] BottleColors =
    {
        new(0.2f, 0.75f, 0.35f), new(0.85f, 0.55f, 0.15f),
        new(0.3f, 0.5f, 0.95f), new(0.8f, 0.25f, 0.3f)
    };
    private const int BottleCount = 26;
    private const float BottleStartX = -2.2f, BottleSpacing = 0.235f;
    private const float BottleBaseY = 2.06f, BottleYOffset = 0.14f;
    private const float ShelfZ = -3.95f; // the back-bar shelf plane (shelves and bottles share it)
    private static readonly Vector3 BottleSize = new(0.09f, 0.28f, 0.09f);
    private const float BottleRoughness = 0.3f, BottleEmission = 0.55f;
    // Screen-side values (owner, 2026-11-27): the contracts
    // column stretch, the selector font size, the offer rows'
    // panel alpha and inner text separation.
    private const float ContractsStretchRatio = 1.0f;
    private const int MissionSelectorFontSize = 15;
    private const float OfferRowPanelAlpha = 0.9f;
    private const int OfferTextSeparation = 2;
    // Patron UI placement heights.
    private const float PatronLabelHeight = 2.12f;
    private const float PatronRingHeight = 0.04f;
    private const float PatronClickHeight = 1.15f;
    private const float PatronBodyHeightOffset = 0.78f; // body centre over the holder origin
    private const float PatronHeadHeightOffset = 1.68f;

    private PlanetDef Planet => MissionCatalog.Planets[FlowMain.PlanetIndex];
    private MissionDef Mission => Planet.Missions[FlowMain.MissionIndex];

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", FlowUi.ScreenMarginHorizontal);
        margin.AddThemeConstantOverride("margin_right", FlowUi.ScreenMarginHorizontal);
        margin.AddThemeConstantOverride("margin_top", FlowUi.ScreenMarginVertical);
        margin.AddThemeConstantOverride("margin_bottom", FlowUi.ScreenMarginVertical);
        AddChild(margin);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", FlowUi.ScreenSeparation);
        margin.AddChild(root);

        // ---- menu bar: Back on the left; Rider Lab (once a
        // mission is signed on this planet) on the right -
        // the owner's standing button rule ----
        var menu = new HBoxContainer();
        menu.AddThemeConstantOverride("separation", FlowUi.MenuSeparation);
        root.AddChild(menu);
        var back = FlowUi.MakeButton("Back", FlowUi.MenuButtonFontSize);
        back.Pressed += () => Flow.GoPlanet();
        menu.AddChild(back);
        menu.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var signed = WorldConfig.SelectedMission;
        if (signed != null && signed.Planet == Planet)
        {
            var toLab = FlowUi.MakeButton("Continue", FlowUi.MenuButtonFontSize);
            toLab.Pressed += () => Flow.GoStoryTeam();
            menu.AddChild(toLab);
        }

        // ---- middle: contracts | bar ----
        var mid = new HBoxContainer();
        mid.AddThemeConstantOverride("separation", FlowUi.MidSeparation);
        mid.SizeFlagsVertical = SizeFlags.ExpandFill;
        root.AddChild(mid);

        var side = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = ContractsStretchRatio };
        side.AddThemeStyleboxOverride("panel", FlowUi.Panel(FlowUi.Navy));
        mid.AddChild(side);
        var sv = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        sv.AddThemeConstantOverride("separation", FlowUi.PanelColumnSeparation);
        side.AddChild(sv);
        sv.AddChild(FlowUi.NavHeading("Mission", FlowUi.SectionHeadingFontSize, FlowUi.Gold));
        sv.AddChild(FlowUi.SelectorRow(out _selName, MissionSelectorFontSize, StepMission));
        var briefScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0f, BriefScrollMinHeight),
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = BriefScrollStretchRatio,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        _briefScroll = briefScroll;
        _briefBody = FlowUi.MakeLabel("", FlowUi.BodyTextFontSize, FlowUi.ScreenInk, HorizontalAlignment.Left, wrap: true);
        _briefBody.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        briefScroll.AddChild(_briefBody);
        briefScroll.Resized += () => FlowUi.FitTextWidth(briefScroll, _briefBody);
        var briefMonitor = FlowUi.Monitor(briefScroll);
        briefMonitor.SizeFlagsVertical = SizeFlags.ExpandFill;
        sv.AddChild(briefMonitor);
        _status = FlowUi.MakeLabel("", FlowUi.SmallTextFontSize, FlowUi.Dim, HorizontalAlignment.Left, wrap: true);
        sv.AddChild(_status);
        var offersScroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = OffersStretchRatio,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        sv.AddChild(offersScroll);
        _offers = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _offers.AddThemeConstantOverride("separation", FlowUi.RowSeparation);
        offersScroll.AddChild(_offers);

        var barPanel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = BarPanelStretchRatio };
        barPanel.AddThemeStyleboxOverride("panel", FlowUi.Panel(FlowUi.NavySoft, FlowUi.VisualPanelAlpha));
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
        // Completed contracts are disabled (owner, 2026-11-16):
        // they can be viewed but never signed again.
        if (StoryProgress.IsCompleted(FlowMain.PlanetIndex, missionIdx))
        {
            Select(missionIdx);
            return;
        }
        FlowMain.MissionIndex = missionIdx;
        WorldConfig.SelectedMission = Planet.Missions[missionIdx];
        // The team for the contract is formed on the next
        // screen (owner, 2026-11-15); the lab follows it.
        Flow.GoStoryTeam();
    }

    // Harness hook (--flowtest-nav): sign the first available
    // mission and deploy, as the player's clicks would.
    public void HarnessAcceptFirst()
    {
        var un = StoryProgress.UnlockedList(FlowMain.PlanetIndex);
        foreach (int mi in un)
            if (!StoryProgress.IsCompleted(FlowMain.PlanetIndex, mi)) { Accept(mi); return; }
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
            $"{m.Site} - {m.Briefing}";
        var signed = WorldConfig.SelectedMission;
        _status.Text = signed != null && signed.Planet == Planet
            ? $"Mission signed: {signed.Name}. Accepting another replaces it."
            : "Click a patron in the bar, or a contract below - Accept signs and deploys.";

        foreach (var c in _offers.GetChildren()) c.QueueFree();
        foreach (int mi in StoryProgress.UnlockedList(FlowMain.PlanetIndex))
        {
            var mm = Planet.Missions[mi];
            int idx = mi;
            var row = new PanelContainer { MouseFilter = MouseFilterEnum.Stop };
            row.AddThemeStyleboxOverride("panel", FlowUi.Panel(
                mi == FlowMain.MissionIndex ? FlowUi.Navy : FlowUi.NavySoft, OfferRowPanelAlpha));
            row.GuiInput += ev =>
            {
                if (ev is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                { UiSfx.Play(); Select(idx); }
            };
            var h = new HBoxContainer();
            h.AddThemeConstantOverride("separation", OfferRowSeparation);
            row.AddChild(h);
            var tx = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.Expand, MouseFilter = MouseFilterEnum.Ignore };
            tx.AddThemeConstantOverride("separation", OfferTextSeparation);
            h.AddChild(tx);
            bool doneRow = StoryProgress.IsCompleted(FlowMain.PlanetIndex, idx);
            var patron = FlowUi.NavHeading(mm.Patron, OfferTitleFontSize,
                doneRow ? FlowUi.Dim : mi == FlowMain.MissionIndex ? FlowUi.Gold : FlowUi.Ink);
            patron.MouseFilter = MouseFilterEnum.Ignore;
            tx.AddChild(patron);
            var pitch = FlowUi.MakeLabel($"“{mm.Pitch}”", FlowUi.SmallTextFontSize, doneRow ? FlowUi.Dim : FlowUi.Ink, HorizontalAlignment.Left, wrap: true);
            pitch.MouseFilter = MouseFilterEnum.Ignore;
            tx.AddChild(pitch);
            var meta = FlowUi.MakeLabel($"{mm.Name} · {mm.Type} · {mm.Payment:N0} credits · Threat {FlowUi.ThreatPips(mm.Threat)}", FlowUi.SubTextFontSize, FlowUi.Dim);
            meta.MouseFilter = MouseFilterEnum.Ignore;
            tx.AddChild(meta);
            bool isSigned = signed == mm;
            bool isDone = StoryProgress.IsCompleted(FlowMain.PlanetIndex, idx);
            var b = FlowUi.MakeButton(isDone ? "Completed" : isSigned ? "Signed" : "Accept", FlowUi.BodyTextFontSize);
            b.CustomMinimumSize = new Vector2(AcceptButtonWidth, 0f);
            b.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            b.Disabled = isSigned || isDone;
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
            Size = new Vector2I(DioramaWidth, DioramaHeight),
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
                BackgroundColor = DioramaBgColor,
                AmbientLightSource = Environment.AmbientSource.Color,
                AmbientLightColor = DioramaAmbientColor,
                AmbientLightEnergy = DioramaAmbientEnergy,
                TonemapMode = Environment.ToneMapper.Aces
            }
        };
        sv.AddChild(we);

        var cam = new Camera3D { Position = DioramaCameraPosition, Fov = DioramaCameraFov };
        sv.AddChild(cam);
        cam.LookAt(DioramaCameraTarget, Vector3.Up);
        sv.AddChild(new DirectionalLight3D
        {
            LightEnergy = DioramaSunEnergy, LightColor = DioramaSunColor,
            Rotation = DioramaSunRotation, ShadowEnabled = false
        });

        var root = new Node3D();
        sv.AddChild(root);
        StandardMaterial3D Mat(Color c, float rough = DioramaDefaultRoughness, float emis = 0f) => new StandardMaterial3D
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
        var floorMat = Mat(FloorColor, FloorRoughness);
        var wallMat = Mat(WallColor, WallRoughness);
        var woodMat = Mat(WoodColor, WoodRoughness);
        var brassMat = Mat(BrassColor, BrassRoughness);

        Box(new Vector3(0f, -0.05f, 0f), new Vector3(16f, 0.1f, 14f), floorMat);            // floor
        Box(new Vector3(0f, 2.6f, -4.2f), new Vector3(16f, 5.2f, 0.3f), wallMat);          // back wall
        Box(new Vector3(-5.6f, 2.6f, 0f), new Vector3(0.3f, 5.2f, 12f), wallMat);          // left wall
        Box(new Vector3(0f, 4.4f, 0f), new Vector3(16f, 0.3f, 14f), wallMat);              // ceiling
        // Bar counter + glowing front strip + top slab.
        Box(new Vector3(0.7f, 0.55f, -2.5f), new Vector3(6.4f, 1.1f, 0.9f), woodMat);
        Box(new Vector3(0.7f, 1.14f, -2.5f), new Vector3(6.8f, 0.08f, 1.15f), brassMat);
        Box(new Vector3(0.7f, 0.72f, -2.02f), new Vector3(6.4f, 0.10f, 0.04f),
            Mat(StripLightColor, StripLightRoughness, StripLightEmission));
        // Back-bar shelves + bottles.
        for (int sIdx = 0; sIdx < 2; sIdx++)
            Box(new Vector3(0.7f, 1.9f + sIdx * ShelfRowStep, ShelfZ), new Vector3(6.2f, 0.06f, 0.35f), woodMat);
        for (int i = 0; i < BottleCount; i++)
        {
            float x = BottleStartX + i * BottleSpacing;
            float y = BottleBaseY + (i % 2) * ShelfRowStep;
            Box(new Vector3(x, y + BottleYOffset, ShelfZ), BottleSize,
                Mat(BottleColors[i % BottleColors.Length], BottleRoughness, BottleEmission));
        }
        // Stools in front of the counter.
        for (int i = 0; i < 4; i++)
        {
            float x = -1.6f + i * 1.5f;
            Box(new Vector3(x, 0.28f, -1.55f), new Vector3(0.12f, 0.56f, 0.12f), brassMat);
            var seat = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = StoolSeatRadius, BottomRadius = StoolSeatRadius, Height = StoolSeatHeight, RadialSegments = StoolSeatSegments },
                MaterialOverride = Mat(SeatColor, SeatRoughness),
                Position = new Vector3(x, 0.60f, -1.55f)
            };
            root.AddChild(seat);
        }
        // Two side tables with chairs.
        foreach (var tx in new[] { -3.9f, 4.3f })
        {
            var top = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = TableTopRadius, BottomRadius = TableTopRadius, Height = TableTopHeight, RadialSegments = TableTopSegments },
                MaterialOverride = woodMat,
                Position = new Vector3(tx, 0.78f, -0.6f)
            };
            root.AddChild(top);
            Box(new Vector3(tx, 0.39f, -0.6f), new Vector3(0.1f, 0.78f, 0.1f), brassMat);
            Box(new Vector3(tx - 0.95f, 0.45f, -0.6f), new Vector3(0.5f, 0.9f, 0.5f), Mat(ChairColor));
            Box(new Vector3(tx + 0.2f, 0.90f, -0.5f), new Vector3(0.08f, 0.18f, 0.08f),
                Mat(CandleColor, CandleRoughness, CandleEmission));
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
            Mesh = new SphereMesh { Radius = HoloRadius, Height = HoloRadius * 2f, RadialSegments = HoloSegments, Rings = HoloRings },
            Position = new Vector3(3.6f, 1.75f, -2.6f)
        };
        var holoMat = new StandardMaterial3D
        {
            AlbedoTexture = FlowUi.PlanetTexture(Planet, HoloTextureSize, HoloTextureSeed),
            EmissionEnabled = true,
            EmissionTexture = FlowUi.PlanetTexture(Planet, HoloTextureSize, HoloTextureSeed),
            Emission = HoloEmissionColor,
            EmissionEnergyMultiplier = HoloEmissionEnergy,
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
                LightColor = PendantLightColor,
                LightEnergy = PendantLightEnergy,
                OmniRange = PendantLightRange
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
                PixelSize = PatronLabelPixelSize,
                FontSize = PatronLabelFontSize,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                Shaded = false,
                Modulate = FlowUi.Ink,
                OutlineSize = PatronLabelOutlineSize,
                OutlineModulate = PatronLabelOutlineColor,
                HorizontalAlignment = HorizontalAlignment.Center,
                Position = hp + Vector3.Up * PatronLabelHeight,
                Visible = false
            };
            root.AddChild(label);
            var ring = new MeshInstance3D
            {
                Mesh = new TorusMesh { InnerRadius = PatronRingInnerRadius, OuterRadius = PatronRingOuterRadius, Rings = PatronRingSegments, RingSegments = PatronRingSegmentSides },
                Position = new Vector3(hp.X, PatronRingHeight, hp.Z),
                Visible = false
            };
            ring.MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = FlowUi.Gold,
                EmissionEnabled = true,
                Emission = FlowUi.Gold,
                EmissionEnergyMultiplier = PatronRingEmissionEnergy,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
            };
            root.AddChild(ring);
            var area = new Area3D { Position = hp + Vector3.Up * PatronClickHeight };
            var shape = new CollisionShape3D
            {
                Shape = new SphereShape3D { Radius = PatronClickRadius }
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
        if (_holo != null) _holo.RotateY((float)delta * HoloSpinRate);
    }

    private void Patron(Node3D root, Vector3 pos, Color suit, float yaw)
    {
        var holder = new Node3D { Position = pos, Rotation = new Vector3(0f, yaw, 0f) };
        root.AddChild(holder);
        _figures.Add(holder);
        var body = new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = PatronBodyRadius, Height = PatronBodyHeight, RadialSegments = PatronBodySegments },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = suit, Roughness = PatronSuitRoughness },
            Position = Vector3.Up * PatronBodyHeightOffset
        };
        holder.AddChild(body);
        var head = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = PatronHeadRadius, Height = PatronHeadRadius * 2f, RadialSegments = PatronHeadSegments, Rings = PatronHeadRings },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = PatronSkinColor, Roughness = PatronSkinRoughness },
            Position = Vector3.Up * PatronHeadHeightOffset
        };
        holder.AddChild(head);
    }
}
