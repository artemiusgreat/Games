using Godot;
using System.Collections.Generic;
using System.Diagnostics;

// Probe 2 (Round 2, Phase B): gameplay loop on REAL physics, built on the
// round-1 / Phase A terrain (same Main.HeightAt, same 100x100 world, same 2
// buildings — now WITH StaticBody3D colliders). Player capsule vs
// HeightMapShape3D terrain + building boxes, 3 physics-steered enemies from
// ONE PackedScene, camera-center raycast shooting, code-built HUD
// (crosshair + "Hits: n/3"). Arg-gated verification: --probe2-test
// Proof frames: probe2-frames/frame_early.png (Hits 0/3, enemies visible)
// and probe2-frames/frame_late.png (final counter, enemies gone), with an
// in-engine pixel diff of the counter region between the two frames.
public partial class GameProbe2 : Node3D
{
    private Probe2Player _player;
    private Label _hitsLabel;
    private bool _test;
    private double _sinceHunt = -1;
    private double _sinceFinish = -1;
    private bool _earlyShot, _lateShot;
    private Image _earlyImg;
    private readonly List<(MeshInstance3D Node, double Ttl)> _puffs = new List<(MeshInstance3D, double)>();

    public override void _Ready()
    {
        var buildSw = Stopwatch.StartNew();
        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg == "--probe2-test") _test = true;

        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://probe2-frames"));

        var sun = new DirectionalLight3D { RotationDegrees = new Vector3(-50f, -30f, 0f) };
        AddChild(sun);
        var we = new WorldEnvironment
        {
            Environment = new Environment
            {
                BackgroundMode = Environment.BGMode.Color,
                BackgroundColor = new Color(0.35f, 0.5f, 0.7f)
            }
        };
        AddChild(we);

        BuildTerrainWithCollider();
        AddTree(new Vector3(-12f, 0f, -8f));
        AddTree(new Vector3(14f, 0f, -18f));
        AddTree(new Vector3(6f, 0f, 16f));
        AddBuilding(new Vector3(-20f, 0f, 14f), new Vector3(7f, 5f, 6f));
        AddBuilding(new Vector3(22f, 0f, 8f), new Vector3(6f, 7f, 6f));

        // Player: in test mode it spawns west of building 2 for the collision
        // phase; otherwise at the round-1 start.
        _player = new Probe2Player();
        _player.Position = _test
            ? new Vector3(14f, Main.HeightAt(14f, 8f) + 1.5f, 8f)
            : new Vector3(0f, Main.HeightAt(0f, 8f) + 1.5f, 8f);
        AddChild(_player);
        _player.Hit += OnHit;
        _player.EnemyDied += OnEnemyDied;

        // Enemies: ONE PackedScene definition, instantiated x3, spread out.
        var packed = GD.Load<PackedScene>("res://Enemy.tscn");
        if (packed == null) { GD.PrintErr("PROBE2 FAIL: Enemy.tscn did not load"); GetTree().Quit(1); return; }
        var spawns = new[]
        {
            new Vector3(4f, 0f, -14f),
            new Vector3(-9f, 0f, -28f),
            new Vector3(9f, 0f, -42f)
        };
        foreach (var s in spawns)
        {
            var e = packed.Instantiate<Enemy>();
            e.Position = new Vector3(s.X, Main.HeightAt(s.X, s.Z) + 1.5f, s.Z);
            e.Target = _player;
            AddChild(e);
            _player.Enemies.Add(e);
            GD.Print($"PROBE2 enemy spawned at {e.Position}");
        }
        _player.TotalEnemies = _player.Enemies.Count;

        BuildHud();

        buildSw.Stop();
        GD.Print($"PROBE2 scene build ms={buildSw.ElapsedMilliseconds} testMode={_test}");
    }

    private void BuildTerrainWithCollider()
    {
        // Visual mesh: identical construction to Main.BuildTerrain.
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetMaterial(new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.5f, 0.22f) });
        float half = Main.TerrainSize / 2f;
        float step = (float)Main.TerrainSize / Main.TerrainSegs;
        for (int j = 0; j < Main.TerrainSegs; j++)
        for (int i = 0; i < Main.TerrainSegs; i++)
        {
            float x0 = -half + i * step, x1 = x0 + step;
            float z0 = -half + j * step, z1 = z0 + step;
            var a = new Vector3(x0, Main.HeightAt(x0, z0), z0);
            var b = new Vector3(x1, Main.HeightAt(x1, z0), z0);
            var c = new Vector3(x0, Main.HeightAt(x0, z1), z1);
            var d = new Vector3(x1, Main.HeightAt(x1, z1), z1);
            st.AddVertex(a); st.AddVertex(b); st.AddVertex(c);
            st.AddVertex(b); st.AddVertex(d); st.AddVertex(c);
        }
        st.GenerateNormals();
        var meshInstance = new MeshInstance3D { Mesh = st.Commit() };
        AddChild(meshInstance);

        // Physics: HeightMapShape3D sampled from the same function. Grid
        // spacing is 1.0 world unit per cell, exactly the shape's native
        // spacing, so no scaling is needed. Setup cost is timed and printed.
        var sw = Stopwatch.StartNew();
        int n = Main.TerrainSegs + 1;
        var data = new float[n * n];
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
                data[j * n + i] = Main.HeightAt(-half + i * step, -half + j * step);
        var shape = new HeightMapShape3D { MapWidth = n, MapDepth = n, MapData = data };
        var body = new StaticBody3D { Name = "TerrainCollider" };
        body.AddChild(new CollisionShape3D { Shape = shape });
        AddChild(body);
        sw.Stop();
        GD.Print($"PROBE2 heightmap collider cells={n * n} ({n}x{n}) setupMs={sw.Elapsed.TotalMilliseconds:F2}");
    }

    private void AddBuilding(Vector3 pos, Vector3 size)
    {
        pos.Y = Main.HeightAt(pos.X, pos.Z) + size.Y / 2f - 0.3f; // same placement as round 1
        var mi = new MeshInstance3D();
        mi.Mesh = new BoxMesh { Size = size };
        mi.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.55f, 0.58f) };
        mi.Position = pos;
        AddChild(mi);

        // NEW vs round 1: a real collider. The box shape is extended 2 units
        // downward so the sloped terrain can never open a gap under a wall.
        var body = new StaticBody3D { Name = "BuildingCollider", Position = pos };
        var col = new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(size.X, size.Y + 2f, size.Z) },
            Position = new Vector3(0f, -1f, 0f)
        };
        body.AddChild(col);
        AddChild(body);
        GD.Print($"PROBE2 building collider at {pos} size={size}");
    }

    private void AddTree(Vector3 pos)
    {
        pos.Y = Main.HeightAt(pos.X, pos.Z);
        var tree = new Node3D { Position = pos };
        var trunk = new MeshInstance3D();
        trunk.Mesh = new CylinderMesh { TopRadius = 0.22f, BottomRadius = 0.3f, Height = 2.2f };
        trunk.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.4f, 0.26f, 0.13f) };
        trunk.Position = new Vector3(0f, 1.1f, 0f);
        var canopy = new MeshInstance3D();
        canopy.Mesh = new SphereMesh { Radius = 1.7f, Height = 3.4f };
        canopy.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.13f, 0.45f, 0.16f) };
        canopy.Position = new Vector3(0f, 3.6f, 0f);
        tree.AddChild(trunk);
        tree.AddChild(canopy);
        AddChild(tree);
    }

    private void BuildHud()
    {
        var layer = new CanvasLayer();
        AddChild(layer);

        var panel = new ColorRect
        {
            Color = new Color(0f, 0f, 0f, 0.55f),
            Position = new Vector2(12f, 12f),
            Size = new Vector2(260f, 64f)
        };
        layer.AddChild(panel);

        _hitsLabel = new Label
        {
            Text = "Hits: 0/3",
            Position = new Vector2(26f, 20f)
        };
        _hitsLabel.AddThemeFontSizeOverride("font_size", 40);
        _hitsLabel.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));
        layer.AddChild(_hitsLabel);

        // Crosshair: a "+" centered by a full-rect CenterContainer.
        var center = new CenterContainer();
        center.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var plus = new Label { Text = "+" };
        plus.AddThemeFontSizeOverride("font_size", 36);
        plus.AddThemeColorOverride("font_color", new Color(1f, 1f, 0.4f));
        center.AddChild(plus);
        layer.AddChild(center);
    }

    private void OnHit(int hits, int total)
    {
        _hitsLabel.Text = $"Hits: {hits}/{total}";
        GD.Print($"PROBE2 HUD counter -> Hits: {hits}/{total}");
    }

    private void OnEnemyDied(Vector3 at)
    {
        // Simple death effect: an orange puff sphere that expands and fades.
        var puff = new MeshInstance3D();
        puff.Mesh = new SphereMesh { Radius = 0.9f, Height = 1.8f };
        puff.MaterialOverride = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(1f, 0.55f, 0.15f, 0.85f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
        };
        AddChild(puff);
        puff.GlobalPosition = at + new Vector3(0f, 0.4f, 0f);
        _puffs.Add((puff, 0.35));
    }

    public override async void _Process(double delta)
    {
        for (int i = _puffs.Count - 1; i >= 0; i--)
        {
            var (node, ttl) = _puffs[i];
            ttl -= delta;
            if (ttl <= 0.0) { node.QueueFree(); _puffs.RemoveAt(i); continue; }
            float k = 1f - (float)(ttl / 0.35);
            node.Scale = Vector3.One * (1f + k * 2.2f);
            if (node.MaterialOverride is StandardMaterial3D m)
            {
                var c = m.AlbedoColor; c.A = 0.85f * (1f - k); m.AlbedoColor = c;
            }
            _puffs[i] = (node, ttl);
        }

        if (!_test || _player == null) return;

        if (_player.HuntStarted && _sinceHunt < 0) _sinceHunt = 0;
        if (_sinceHunt >= 0 && !_earlyShot)
        {
            _sinceHunt += delta;
            if (_sinceHunt >= 0.6)
            {
                _earlyShot = true;
                _earlyImg = await Capture("res://probe2-frames/frame_early.png");
                GD.Print($"PROBE2 early frame: hits={_player.Hits}/{_player.TotalEnemies} enemiesAlive={_player.Enemies.Count}");
            }
        }

        if (_player.Finished && _sinceFinish < 0) _sinceFinish = 0;
        if (_sinceFinish >= 0 && !_lateShot)
        {
            _sinceFinish += delta;
            if (_sinceFinish >= 0.6)
            {
                _lateShot = true;
                var lateImg = await Capture("res://probe2-frames/frame_late.png");
                FinishReport(lateImg);
            }
        }
    }

    private async System.Threading.Tasks.Task<Image> Capture(string path)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var img = GetViewport().GetTexture().GetImage();
        var err = img.SavePng(path);
        GD.Print($"PROBE2 captured {path} size={img.GetSize()} err={err}");
        return img;
    }

    private void FinishReport(Image lateImg)
    {
        // Pixel-verify: the HUD counter region (top-left panel) must differ
        // between the early (Hits: 0/3) and late (final) frames.
        if (_earlyImg != null && lateImg != null)
        {
            var size = lateImg.GetSize();
            int w = (int)(size.X * 0.24f), h = (int)(size.Y * 0.13f);
            long diff = 0, totalPx = 0;
            for (int y = 0; y < h; y += 2)
            for (int x = 0; x < w; x += 2)
            {
                var a = _earlyImg.GetPixel(x, y);
                var b = lateImg.GetPixel(x, y);
                totalPx++;
                if (Mathf.Abs(a.R - b.R) > 0.08f || Mathf.Abs(a.G - b.G) > 0.08f || Mathf.Abs(a.B - b.B) > 0.08f) diff++;
            }
            GD.Print($"PROBE2 pixelDiff counterRegion differing={diff}/{totalPx} sampled pixels");
        }

        long rssKb = 0;
        try
        {
            foreach (var line in System.IO.File.ReadLines("/proc/self/status"))
                if (line.StartsWith("VmRSS:")) { rssKb = long.Parse(line.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries)[1]); break; }
        }
        catch { }

        bool pass = _player.BuildingTestPassed && _player.Hits == _player.TotalEnemies && !_player.TimedOut;
        GD.Print($"PROBE2 final counter Hits: {_player.Hits}/{_player.TotalEnemies} buildingTest={(_player.BuildingTestPassed ? "PASS" : "FAIL")} timedOut={_player.TimedOut} vmRssMB={rssKb / 1024.0:F0}");
        GD.Print($"PROBE2 RESULT {(pass ? "PASS" : "FAIL")}");
        GetTree().Quit(pass ? 0 : 1);
    }
}
