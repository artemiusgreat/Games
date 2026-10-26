using Godot;
using System.Collections.Generic;

// Probe 3 (Round 2): scale test.
// 1000x1000 terrain (extended round-1 height family), procedural PBR ground
// texture generated in code, ONE shadow-casting directional light, ~300
// scattered tree/rock props via MultiMesh instancing, player capsule walking
// on the terrain via height sampling. Arg-gated: --scale-test
// Captures proof/probe3_scale.png, prints frame-time / draw-call / triangle /
// RSS metrics.
public partial class ScaleProbe : Node3D
{
    public const int WorldSize = 1000;
    public const int Segs = 256;          // 256x256 quads = 131,072 terrain triangles
    public const int PropCount = 300;

    // Extended round-1 height family: same sine recipe, lower spatial
    // frequencies stretched for the 10x larger world, amplitude scaled up.
    public static float HeightAt(float x, float z)
    {
        return 13.0f * Mathf.Sin(x * 0.014f) * Mathf.Cos(z * 0.011f)
             + 7.0f * Mathf.Sin(x * 0.042f + 1.7f) * Mathf.Sin(z * 0.034f + 0.6f)
             + 2.5f * Mathf.Sin(x * 0.09f) * Mathf.Cos(z * 0.08f);
    }

    private CharacterBody3D _player;
    private Camera3D _cam;
    private bool _test;
    private double _t;
    private bool _shot;
    private readonly List<double> _frameMs = new List<double>();

    public override void _Ready()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg == "--scale-test") _test = true;

        // ONE directional light, shadows enabled.
        var sun = new DirectionalLight3D
        {
            RotationDegrees = new Vector3(-52f, -35f, 0f),
            ShadowEnabled = true,
            LightEnergy = 1.15f
        };
        sun.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits;
        sun.DirectionalShadowMaxDistance = 400f;
        AddChild(sun);
        var we = new WorldEnvironment
        {
            Environment = new Environment
            {
                BackgroundMode = Environment.BGMode.Color,
                BackgroundColor = new Color(0.35f, 0.5f, 0.7f),
                AmbientLightSource = Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.45f, 0.5f, 0.6f),
                AmbientLightEnergy = 0.5f
            }
        };
        AddChild(we);

        BuildTerrain();
        ScatterProps();
        SpawnPlayer();

        _cam = new Camera3D { Far = 2000f };
        AddChild(_cam);

        sw.Stop();
        GD.Print($"SCALEPROBE scene build ms={sw.ElapsedMilliseconds}");
    }

    // Procedural PBR ground: multi-octave value-noise albedo (grass/rock/dirt
    // blend by height+slope handled in albedo only) + matching roughness and
    // bump, generated as Images in code -> ImageTextures -> StandardMaterial3D.
    private static StandardMaterial3D GroundMaterial()
    {
        const int S = 512;
        var albedo = Image.CreateEmpty(S, S, false, Image.Format.Rgb8);
        var rough = Image.CreateEmpty(S, S, false, Image.Format.Rgb8);
        var rng = new RandomNumberGenerator { Seed = 12345 };
        // Pre-generate a tileable-ish random grid for value noise.
        const int G = 32;
        var grid = new float[G * G];
        for (int i = 0; i < grid.Length; i++) grid[i] = rng.Randf();
        float Noise(float u, float v) // u,v in grid cells, bilinear + wrap
        {
            int x0 = Mathf.PosMod(Mathf.FloorToInt(u), G), y0 = Mathf.PosMod(Mathf.FloorToInt(v), G);
            int x1 = (x0 + 1) % G, y1 = (y0 + 1) % G;
            float fx = u - Mathf.Floor(u), fy = v - Mathf.Floor(v);
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            float a = grid[y0 * G + x0], b = grid[y0 * G + x1], c = grid[y1 * G + x0], d = grid[y1 * G + x1];
            return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fy);
        }
        var grass = new Color(0.16f, 0.38f, 0.12f);
        var dry = new Color(0.45f, 0.38f, 0.18f);
        var dirt = new Color(0.32f, 0.24f, 0.15f);
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float u = (float)x / S * G, v = (float)y / S * G;
            float n = 0.55f * Noise(u, v) + 0.30f * Noise(u * 2f, v * 2f) + 0.15f * Noise(u * 4f, v * 4f);
            var col = grass.Lerp(dry, Mathf.Clamp((n - 0.35f) * 2.2f, 0f, 1f));
            if (n > 0.72f) col = col.Lerp(dirt, (n - 0.72f) * 2.5f);
            float grain = 0.92f + 0.16f * Noise(u * 8f, v * 8f);
            albedo.SetPixel(x, y, new Color(col.R * grain, col.G * grain, col.B * grain));
            float r = Mathf.Clamp(0.85f - (n - 0.5f) * 0.4f, 0.55f, 1f);
            rough.SetPixel(x, y, new Color(r, r, r));
        }
        var mat = new StandardMaterial3D
        {
            AlbedoTexture = ImageTexture.CreateFromImage(albedo),
            RoughnessTexture = ImageTexture.CreateFromImage(rough),
            Roughness = 1.0f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps
            // tiling comes from the per-vertex UVs (world/24) in BuildTerrain
        };
        return mat;
    }

    private void BuildTerrain()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetMaterial(GroundMaterial());
        float half = WorldSize / 2f;
        float step = (float)WorldSize / Segs;
        for (int j = 0; j < Segs; j++)
        for (int i = 0; i < Segs; i++)
        {
            float x0 = -half + i * step, x1 = x0 + step;
            float z0 = -half + j * step, z1 = z0 + step;
            var a = new Vector3(x0, HeightAt(x0, z0), z0);
            var b = new Vector3(x1, HeightAt(x1, z0), z0);
            var c = new Vector3(x0, HeightAt(x0, z1), z1);
            var d = new Vector3(x1, HeightAt(x1, z1), z1);
            // UVs so the ground texture tiles across the world.
            st.SetUV(new Vector2(x0 / 24f, z0 / 24f)); st.AddVertex(a);
            st.SetUV(new Vector2(x1 / 24f, z0 / 24f)); st.AddVertex(b);
            st.SetUV(new Vector2(x0 / 24f, z1 / 24f)); st.AddVertex(c);
            st.SetUV(new Vector2(x1 / 24f, z0 / 24f)); st.AddVertex(b);
            st.SetUV(new Vector2(x1 / 24f, z1 / 24f)); st.AddVertex(d);
            st.SetUV(new Vector2(x0 / 24f, z1 / 24f)); st.AddVertex(c);
        }
        st.GenerateNormals();
        var mi = new MeshInstance3D { Mesh = st.Commit() };
        AddChild(mi);
        long tris = (long)Segs * Segs * 2;
        GD.Print($"SCALEPROBE terrain tris={tris}");
    }

    private void ScatterProps()
    {
        // Two MultiMeshes: trees (trunk+canopy merged into one low-poly mesh)
        // and rocks (icosahedron-ish sphere blobs). ~300 instances total.
        var rng = new RandomNumberGenerator { Seed = 777 };
        var treeAt = new List<Vector3>();
        var rockAt = new List<Vector3>();
        for (int i = 0; i < PropCount; i++)
        {
            float x = rng.RandfRange(-half(), half());
            float z = rng.RandfRange(-half(), half());
            if (Mathf.Abs(x) < 12f && z > -40f && z < 20f) continue; // keep spawn corridor clear
            (i % 3 == 2 ? rockAt : treeAt).Add(new Vector3(x, HeightAt(x, z), z));
        }
        static float half() => WorldSize / 2f - 15f;

        AddMultiMesh(TreeMesh(), treeAt, rng, 0.85f, 1.5f);
        AddMultiMesh(RockMesh(), rockAt, rng, 0.7f, 2.2f);
        GD.Print($"SCALEPROBE props trees={treeAt.Count} rocks={rockAt.Count} total={treeAt.Count + rockAt.Count}");
    }

    private static ArrayMesh TreeMesh()
    {
        // Trunk cylinder + canopy cone merged via SurfaceTool into one mesh
        // with vertex colors (material uses vertex color as albedo).
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        AppendPrism(st, new Vector3(0f, 1.1f, 0f), 0.28f, 2.2f, 6, new Color(0.35f, 0.22f, 0.11f));
        AppendCone(st, new Vector3(0f, 3.6f, 0f), 1.9f, 3.6f, 8, new Color(0.10f, 0.38f, 0.13f));
        st.GenerateNormals();
        return (ArrayMesh)st.Commit();
    }

    private static ArrayMesh RockMesh()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        // Squashed octahedron blob.
        var top = new Vector3(0f, 1.0f, 0f); var bot = new Vector3(0f, -0.4f, 0f);
        var ring = new Vector3[5];
        for (int i = 0; i < 5; i++)
        {
            float a = i / 5f * Mathf.Tau;
            ring[i] = new Vector3(Mathf.Cos(a) * 1.3f, 0.15f, Mathf.Sin(a) * 1.3f);
        }
        var gray = new Color(0.45f, 0.45f, 0.48f);
        for (int i = 0; i < 5; i++)
        {
            var r0 = ring[i]; var r1 = ring[(i + 1) % 5];
            st.SetColor(gray); st.AddVertex(r0); st.AddVertex(r1); st.AddVertex(top);
            st.SetColor(gray.Darkened(0.25f)); st.AddVertex(r1); st.AddVertex(r0); st.AddVertex(bot);
        }
        st.GenerateNormals();
        return (ArrayMesh)st.Commit();
    }

    private static void AppendPrism(SurfaceTool st, Vector3 center, float radius, float height, int sides, Color color)
    {
        float y0 = center.Y - height / 2f, y1 = center.Y + height / 2f;
        for (int i = 0; i < sides; i++)
        {
            float a0 = i / (float)sides * Mathf.Tau, a1 = (i + 1) / (float)sides * Mathf.Tau;
            var b0 = new Vector3(center.X + Mathf.Cos(a0) * radius, y0, center.Z + Mathf.Sin(a0) * radius);
            var b1 = new Vector3(center.X + Mathf.Cos(a1) * radius, y0, center.Z + Mathf.Sin(a1) * radius);
            var t0 = new Vector3(b0.X, y1, b0.Z); var t1 = new Vector3(b1.X, y1, b1.Z);
            st.SetColor(color); st.AddVertex(b0); st.AddVertex(b1); st.AddVertex(t0);
            st.SetColor(color); st.AddVertex(b1); st.AddVertex(t1); st.AddVertex(t0);
        }
    }

    private static void AppendCone(SurfaceTool st, Vector3 center, float radius, float height, int sides, Color color)
    {
        float y0 = center.Y - height / 2f;
        var apex = new Vector3(center.X, center.Y + height / 2f, center.Z);
        for (int i = 0; i < sides; i++)
        {
            float a0 = i / (float)sides * Mathf.Tau, a1 = (i + 1) / (float)sides * Mathf.Tau;
            var p0 = new Vector3(center.X + Mathf.Cos(a0) * radius, y0, center.Z + Mathf.Sin(a0) * radius);
            var p1 = new Vector3(center.X + Mathf.Cos(a1) * radius, y0, center.Z + Mathf.Sin(a1) * radius);
            st.SetColor(color); st.AddVertex(p0); st.AddVertex(p1); st.AddVertex(apex);
        }
    }

    private void AddMultiMesh(ArrayMesh mesh, List<Vector3> at, RandomNumberGenerator rng, float sMin, float sMax)
    {
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = false,
            Mesh = mesh,
            InstanceCount = at.Count
        };
        for (int i = 0; i < at.Count; i++)
        {
            float s = rng.RandfRange(sMin, sMax);
            var basis = Basis.FromEuler(new Vector3(0f, rng.RandfRange(0f, Mathf.Tau), 0f)).Scaled(Vector3.One * s);
            mm.SetInstanceTransform(i, new Transform3D(basis, at[i]));
        }
        var mmi = new MultiMeshInstance3D
        {
            Multimesh = mm,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
            MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = 0.95f }
        };
        AddChild(mmi);
    }

    private void SpawnPlayer()
    {
        _player = new CharacterBody3D();
        var col = new CollisionShape3D { Shape = new CapsuleShape3D { Radius = 0.4f, Height = 1.8f } };
        _player.AddChild(col);
        var vis = new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = 0.4f, Height = 1.8f },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.15f, 0.35f, 0.9f) }
        };
        _player.AddChild(vis);
        _player.Position = new Vector3(0f, HeightAt(0f, 8f) + 1.0f, 8f);
        AddChild(_player);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_test || _player == null) return;
        // Walk forward (-Z) for the whole test; terrain following by height
        // sampling (no heightfield collider at this scale in the probe).
        var p = _player.Position;
        p.Z -= 6.0f * (float)delta;
        p.Y = HeightAt(p.X, p.Z) + 1.0f;
        _player.Position = p;
    }

    public override async void _Process(double delta)
    {
        _t += delta;
        if (_t > 0.5) _frameMs.Add(delta * 1000.0);

        if (_player != null && _cam != null)
        {
            var fp = _player.GlobalPosition;
            _cam.GlobalPosition = fp + new Vector3(0f, 4.5f, 11f);
            _cam.LookAt(fp + new Vector3(0f, 1.2f, 0f), Vector3.Up);
        }

        if (!_test || _shot || _t < 4.0) return;
        _shot = true;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var img = GetViewport().GetTexture().GetImage();
        var err = img.SavePng("res://proof/probe3_scale.png");
        ulong draws = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame);
        ulong prims = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame);
        double avg = 0; foreach (var f in _frameMs) avg += f; avg = _frameMs.Count > 0 ? avg / _frameMs.Count : 0;
        double worst = 0; foreach (var f in _frameMs) if (f > worst) worst = f;
        long rssKb = 0;
        try
        {
            foreach (var line in System.IO.File.ReadLines("/proc/self/status"))
                if (line.StartsWith("VmRSS:")) { rssKb = long.Parse(line.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries)[1]); break; }
        }
        catch { }
        GD.Print($"SCALEPROBE captured err={err} size={img.GetSize()}");
        GD.Print($"SCALEPROBE metrics frames={_frameMs.Count} avgFrameMs={avg:F2} worstFrameMs={worst:F2} drawCalls={draws} primitivesInFrame={prims} vmRssMB={rssKb / 1024.0:F0} playerPos={_player.GlobalPosition}");
        GD.Print("SCALEPROBE DONE");
        GetTree().Quit();
    }
}
