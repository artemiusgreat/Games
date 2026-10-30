using Godot;
using System.Collections.Generic;

// Photoreal probe environment (owner ask, 2026-10-29): the duel's
// world rebuilt from CC0 photogrammetry — Poly Haven texture sets
// splat-blended over the SAME heightfield, scanned tree/rock models
// at the SAME prop positions, an instanced grass field, and a
// mountain backdrop. Everything here is visual only: HeightAt,
// colliders, hit cylinders and destructible registration in
// MechDuel are untouched, so the duel plays identically. Assets
// live in res://photo/ (see fetch_photo_assets.py and
// decimate_models.py there; Poly Haven content is CC0).
public static class PhotoEnv
{
    private const string Root = "res://photo/";
    private static readonly Dictionary<string, Texture2D> _tex = new();
    private static readonly Dictionary<string, PackedScene> _scenes = new();

    private static Texture2D Tex(string set, string kind)
    {
        string key = set + "/" + kind;
        if (_tex.TryGetValue(key, out var t)) return t;
        t = ResourceLoader.Load<Texture2D>($"{Root}textures/{set}/{set}_{kind}_2k.jpg");
        _tex[key] = t;
        return t;
    }

    private static PackedScene Scene(string id)
    {
        if (_scenes.TryGetValue(id, out var s)) return s;
        s = ResourceLoader.Load<PackedScene>($"{Root}models/{id}/{id}.gltf");
        _scenes[id] = s;
        return s;
    }

    // Merged world-space AABB of every mesh under a node, measured in
    // the node's own local space (before its own transform is set).
    private static Aabb LocalAabb(Node3D root)
    {
        var box = new Aabb();
        bool first = true;
        var stack = new Stack<(Node Node, Transform3D Xf)>();
        stack.Push((root, Transform3D.Identity));
        while (stack.Count > 0)
        {
            var (node, xf) = stack.Pop();
            if (node is MeshInstance3D mi && mi.Mesh != null)
            {
                var b = xf * mi.GetAabb();
                if (first) { box = b; first = false; } else box = box.Merge(b);
            }
            foreach (var child in node.GetChildren())
                if (child is Node3D n3)
                    stack.Push((n3, xf * n3.Transform));
        }
        return box;
    }

    // ---- Terrain: splat shader over the shared heightfield ----

    private const string TerrainShader = @"
shader_type spatial;
uniform sampler2D splat : filter_linear, repeat_disable;
uniform sampler2D grass_diff : source_color, repeat_enable;
uniform sampler2D grass_nor : hint_normal, repeat_enable;
uniform sampler2D grass_arm : repeat_enable;
uniform sampler2D dirt_diff : source_color, repeat_enable;
uniform sampler2D dirt_nor : hint_normal, repeat_enable;
uniform sampler2D dirt_arm : repeat_enable;
uniform sampler2D rock_diff : source_color, repeat_enable;
uniform sampler2D rock_nor : hint_normal, repeat_enable;
uniform sampler2D rock_arm : repeat_enable;
uniform sampler2D asph_diff : source_color, repeat_enable;
uniform sampler2D asph_nor : hint_normal, repeat_enable;
uniform sampler2D asph_arm : repeat_enable;
varying vec3 wpos;
void vertex() { wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
void fragment() {
    vec4 sp = texture(splat, wpos.xz / 170.0 + 0.5);
    float tot = max(sp.r + sp.g + sp.b + sp.a, 0.001);
    float wr = sp.r / tot, wg = sp.g / tot, wb = sp.b / tot, wa = sp.a / tot;
    vec2 uv1 = wpos.xz * 0.055;
    vec2 uv2 = wpos.xz * 0.019 + 0.37;
    vec3 alb = vec3(0.0);
    alb += wg * mix(texture(grass_diff, uv1).rgb, texture(grass_diff, uv2).rgb, 0.45);
    alb += wb * mix(texture(dirt_diff, uv1).rgb, texture(dirt_diff, uv2).rgb, 0.45);
    alb += wr * mix(texture(rock_diff, uv1).rgb, texture(rock_diff, uv2).rgb, 0.45);
    alb += wa * mix(texture(asph_diff, uv1).rgb, texture(asph_diff, uv2).rgb, 0.45);
    vec3 nrm = vec3(0.5, 0.5, 1.0);
    nrm += wg * (texture(grass_nor, uv1).rgb - vec3(0.5, 0.5, 1.0));
    nrm += wb * (texture(dirt_nor, uv1).rgb - vec3(0.5, 0.5, 1.0));
    nrm += wr * (texture(rock_nor, uv1).rgb - vec3(0.5, 0.5, 1.0));
    nrm += wa * (texture(asph_nor, uv1).rgb - vec3(0.5, 0.5, 1.0));
    vec3 arm = vec3(1.0, 0.9, 0.0);
    arm *= wg * texture(grass_arm, uv1).rgb + wb * texture(dirt_arm, uv1).rgb
         + wr * texture(rock_arm, uv1).rgb + wa * texture(asph_arm, uv1).rgb;
    ALBEDO = alb;
    NORMAL_MAP = nrm;
    NORMAL_MAP_DEPTH = 0.9;
    ROUGHNESS = clamp(arm.g, 0.05, 1.0);
    AO = arm.r;
}
";

    private static float HashNoise(float x, float z)
    {
        float s = Mathf.Sin(x * 127.1f + z * 311.7f) * 43758.5453f;
        return s - Mathf.Floor(s);
    }

    private static float ValueNoise(float x, float z)
    {
        float xi = Mathf.Floor(x), zi = Mathf.Floor(z);
        float xf = x - xi, zf = z - zi;
        float u = xf * xf * (3f - 2f * xf), v = zf * zf * (3f - 2f * zf);
        float a = HashNoise(xi, zi), b = HashNoise(xi + 1f, zi);
        float c = HashNoise(xi, zi + 1f), d = HashNoise(xi + 1f, zi + 1f);
        return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
    }

    // Splat weights (R rock, G grass, B dirt, A asphalt) from the same
    // heightfield the game logic reads: rock takes steep faces and
    // high ground, dirt breaks the grass in noise patches, asphalt
    // follows Main.RoadDistance exactly like the flat coloring did.
    private static ImageTexture BuildSplat()
    {
        const int N = 1024;
        var img = Image.CreateEmpty(N, N, false, Image.Format.Rgba8);
        for (int j = 0; j < N; j++)
        {
            for (int i = 0; i < N; i++)
            {
                float x = (i / (float)(N - 1) - 0.5f) * 170f;
                float z = (j / (float)(N - 1) - 0.5f) * 170f;
                float h = Main.HeightAt(x, z);
                float e = 0.35f;
                float slope = Mathf.Max(
                    Mathf.Abs(Main.HeightAt(x + e, z) - h),
                    Mathf.Abs(Main.HeightAt(x, z + e) - h)) / e; // rise per unit
                float patch = ValueNoise(x * 0.045f, z * 0.045f) * 0.65f
                            + ValueNoise(x * 0.23f, z * 0.23f) * 0.35f;
                float rock = Mathf.SmoothStep(0.30f, 0.62f, slope)
                           + Mathf.SmoothStep(13f, 20f, h) * 0.7f;
                float dirt = Mathf.SmoothStep(0.52f, 0.72f, patch) * (1f - rock);
                float grass = Mathf.Max(0f, 1f - rock - dirt);
                float road = 1f - Mathf.SmoothStep(2.25f, 3.1f, Main.RoadDistance(x, z));
                img.SetPixel(i, j, new Color(
                    Mathf.Clamp(rock, 0f, 1f), grass,
                    Mathf.Clamp(dirt, 0f, 1f), road));
            }
        }
        return ImageTexture.CreateFromImage(img);
    }

    public static Material TerrainMaterial()
    {
        var sh = new Shader { Code = TerrainShader };
        var m = new ShaderMaterial { Shader = sh };
        m.SetShaderParameter("splat", BuildSplat());
        m.SetShaderParameter("grass_diff", Tex("leafy_grass", "diff"));
        m.SetShaderParameter("grass_nor", Tex("leafy_grass", "nor_gl"));
        m.SetShaderParameter("grass_arm", Tex("leafy_grass", "arm"));
        m.SetShaderParameter("dirt_diff", Tex("forest_floor", "diff"));
        m.SetShaderParameter("dirt_nor", Tex("forest_floor", "nor_gl"));
        m.SetShaderParameter("dirt_arm", Tex("forest_floor", "arm"));
        m.SetShaderParameter("rock_diff", Tex("aerial_rocks_02", "diff"));
        m.SetShaderParameter("rock_nor", Tex("aerial_rocks_02", "nor_gl"));
        m.SetShaderParameter("rock_arm", Tex("aerial_rocks_02", "arm"));
        m.SetShaderParameter("asph_diff", Tex("aerial_asphalt_01", "diff"));
        m.SetShaderParameter("asph_nor", Tex("aerial_asphalt_01", "nor_gl"));
        m.SetShaderParameter("asph_arm", Tex("aerial_asphalt_01", "arm"));
        GD.Print("PHOTO terrain material ready (splat 1024, 4 scanned sets)");
        return m;
    }

    // ---- Scanned props at the duel's own positions ----

    public static void AddTreeModel(Node3D tree, int variety, RandomNumberGenerator rng)
    {
        // No scanned pine exists at usable size (Poly Haven's are
        // 0.5-0.9 GB photogrammetry), so the third island tree
        // stands in for the pine variety in this probe.
        string id = variety == 1 ? "island_tree_03"
            : (rng.RandiRange(0, 1) == 0 ? "island_tree_01" : "island_tree_02");
        float targetH = variety == 1 ? 7.6f : 5.9f;
        var inst = Scene(id).Instantiate<Node3D>();
        var box = LocalAabb(inst);
        float s = targetH / Mathf.Max(box.Size.Y, 0.01f);
        inst.Scale = Vector3.One * s;
        inst.Position = new Vector3(
            -box.GetCenter().X * s, -box.Position.Y * s, -box.GetCenter().Z * s);
        inst.Rotation = new Vector3(0f, rng.RandfRange(0f, Mathf.Tau), 0f);
        tree.AddChild(inst);
    }

    public static Node3D MakeRock(Vector3 scl)
    {
        var inst = Scene("coast_rocks_01").Instantiate<Node3D>();
        var box = LocalAabb(inst);
        var holder = new Node3D();
        inst.Scale = new Vector3(
            2f * scl.X / Mathf.Max(box.Size.X, 0.01f),
            2f * scl.Y / Mathf.Max(box.Size.Y, 0.01f),
            2f * scl.Z / Mathf.Max(box.Size.Z, 0.01f));
        inst.Position = new Vector3(
            -box.GetCenter().X * inst.Scale.X,
            -box.Position.Y * inst.Scale.Y,
            -box.GetCenter().Z * inst.Scale.Z);
        holder.AddChild(inst);
        return holder;
    }

    public static Material BuildingMaterial(Vector3 size)
    {
        return new StandardMaterial3D
        {
            AlbedoTexture = Tex("concrete_block_wall_02", "diff"),
            NormalEnabled = true,
            NormalScale = 0.9f,
            NormalTexture = Tex("concrete_block_wall_02", "nor_gl"),
            RoughnessTexture = Tex("concrete_block_wall_02", "arm"),
            RoughnessTextureChannel = BaseMaterial3D.TextureChannel.Green,
            AOTexture = Tex("concrete_block_wall_02", "arm"),
            Uv1Scale = new Vector3(Mathf.Max(size.X, size.Z) / 3.5f, size.Y / 3.5f, 1f)
        };
    }

    // ---- Instanced grass field (visual only) ----

    public static void AddGrassField(Node3D parent, List<Vector2> buildingPts,
        List<(Vector2 P, float R)> props)
    {
        // Two layers. (1) Blade cards: crossed quads textured with
        // the scanned grass photo — 6 triangles each, so 60k tufts
        // cost less than 0.4M triangles. (2) Hero clumps: the full
        // scanned patch (24.7k tris) used SPARINGLY — 320 instances
        // ≈ 8M tris. The naive version (42k scanned patches) was
        // 1.04 BILLION triangles/frame: that is the single most
        // important budget fact this probe produced.
        var spots = new List<(Vector3 Pos, float Yaw, float S)>();
        var rng = new RandomNumberGenerator { Seed = 424242 };
        int attempts = 0;
        while (spots.Count < 60000 && attempts++ < 400000)
        {
            float x = rng.RandfRange(-84f, 84f), z = rng.RandfRange(-84f, 84f);
            if (Main.RoadDistance(x, z) < 2.9f) continue;
            float h = Main.HeightAt(x, z);
            if (h > 10f) continue;
            float e = 0.4f;
            float slope = Mathf.Max(
                Mathf.Abs(Main.HeightAt(x + e, z) - h),
                Mathf.Abs(Main.HeightAt(x, z + e) - h)) / e;
            if (slope > 0.45f) continue;
            bool bad = false;
            foreach (var bp in buildingPts)
                if (new Vector2(x, z).DistanceTo(bp) < 7f) { bad = true; break; }
            if (bad) continue;
            foreach (var (cp, cr) in props)
                if (new Vector2(x, z).DistanceTo(cp) < cr + 0.5f) { bad = true; break; }
            if (bad) continue;
            spots.Add((new Vector3(x, h - 0.04f, z),
                rng.RandfRange(0f, Mathf.Tau), rng.RandfRange(0.75f, 1.5f)));
        }

        // Crossed-quad tuft mesh (3 quads, base at origin).
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var nrms = new List<Vector3>();
        var idx = new List<int>();
        for (int q = 0; q < 3; q++)
        {
            float a = q * Mathf.Pi / 3f;
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.45f;
            int b0 = verts.Count;
            verts.Add(-dir); uvs.Add(new Vector2(0f, 1f));
            verts.Add(dir); uvs.Add(new Vector2(1f, 1f));
            verts.Add(dir + Vector3.Up * 0.62f); uvs.Add(new Vector2(1f, 0f));
            verts.Add(-dir + Vector3.Up * 0.62f); uvs.Add(new Vector2(0f, 0f));
            // All normals point UP (probe run 3 lesson: the first
            // version shipped no normals at all and the cards lit
            // black). Up-normals are also the classic grass trick —
            // cards shade like the ground they grow from.
            for (int k = 0; k < 4; k++) nrms.Add(Vector3.Up);
            idx.AddRange(new[] { b0, b0 + 1, b0 + 2, b0, b0 + 2, b0 + 3 });
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = nrms.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = idx.ToArray();
        var tuft = new ArrayMesh();
        tuft.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        var tuftMat = new StandardMaterial3D
        {
            // The blade texture drawn for the purpose (the scanned
            // patch's diffuse is a JPG with no alpha — its silhouette
            // is real geometry, so it cannot drive cards).
            AlbedoTexture = BladeTexture(),
            // CRITICAL: the scissor only engages when the material's
            // transparency MODE is AlphaScissor. Setting the
            // threshold alone leaves the mode Disabled, the shader
            // ignores alpha, and the texture's transparent pixels
            // render as their raw RGB — black slabs (probe runs 2-4,
            // root-caused by a pink-debug bisection).
            Transparency = BaseMaterial3D.TransparencyEnum.AlphaScissor,
            AlphaScissorThreshold = 0.45f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            Roughness = 1f
        };
        tuft.SurfaceSetMaterial(0, tuftMat);

        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = tuft,
            InstanceCount = spots.Count
        };
        for (int i = 0; i < spots.Count; i++)
        {
            var (pos, yaw, sc) = spots[i];
            mm.SetInstanceTransform(i, new Transform3D(
                new Basis(Vector3.Up, yaw).Scaled(new Vector3(sc, sc, sc)), pos));
        }
        parent.AddChild(new MultiMeshInstance3D
        {
            Multimesh = mm,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        });

        // Hero clumps from the scanned patch.
        var probe = Scene("grass_medium_01").Instantiate<Node3D>();
        Mesh heroMesh = null;
        var stack = new Stack<Node>();
        stack.Push(probe);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n is MeshInstance3D mi && mi.Mesh != null) { heroMesh = mi.Mesh; break; }
            foreach (var c in n.GetChildren()) stack.Push(c);
        }
        probe.QueueFree();
        if (heroMesh != null)
        {
            int heroes = Mathf.Min(320, spots.Count);
            var hmm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = heroMesh,
                InstanceCount = heroes
            };
            for (int i = 0; i < heroes; i++)
            {
                var (pos, yaw, _) = spots[i * 37 % spots.Count];
                float sc = rng.RandfRange(1.1f, 1.9f);
                hmm.SetInstanceTransform(i, new Transform3D(
                    new Basis(Vector3.Up, yaw).Scaled(new Vector3(sc, sc, sc)), pos));
            }
            parent.AddChild(new MultiMeshInstance3D
            {
                Multimesh = hmm,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            });
            GD.Print($"PHOTO grass: {spots.Count} blade-card tufts + {heroes} scanned hero clumps");
        }
    }

    // Grass-blade card texture, drawn blade by blade: tapered,
    // curved strokes, dark roots to yellow-green tips, alpha
    // everywhere else. 256px, generated once.
    private static Texture2D BladeTexture()
    {
        const int N = 256;
        var img = Image.CreateEmpty(N, N, true, Image.Format.Rgba8);
        img.Fill(new Color(0f, 0f, 0f, 0f)); // explicit alpha 0: CreateEmpty's fill is opaque black, which defeated the alpha scissor in probe run 2 (black slabs)
        var rng = new RandomNumberGenerator { Seed = 1357 };
        for (int b = 0; b < 30; b++)
        {
            // Thin blades with air between them: the first texture
            // packed 52 wide blades (~63% coverage), so at any
            // distance the cards averaged into solid green boxes.
            float x0 = rng.RandfRange(8f, N - 8f);
            float hgt = rng.RandfRange(N * 0.5f, N * 0.97f);
            float lean = rng.RandfRange(-26f, 26f);
            float w0 = rng.RandfRange(2.2f, 4.2f);
            var root = new Color(rng.RandfRange(0.05f, 0.10f),
                rng.RandfRange(0.16f, 0.24f), rng.RandfRange(0.03f, 0.06f), 1f);
            var tip = new Color(rng.RandfRange(0.32f, 0.45f),
                rng.RandfRange(0.48f, 0.60f), rng.RandfRange(0.12f, 0.20f), 1f);
            int rows = (int)hgt;
            for (int r = 0; r < rows; r++)
            {
                float t = r / (float)rows;
                float cx = x0 + lean * t * t;
                float hw = w0 * (1f - t) * 0.5f + 0.5f;
                int y = N - 1 - r;
                var col = root.Lerp(tip, t);
                for (int px = (int)(cx - hw); px <= (int)(cx + hw); px++)
                    if (px >= 0 && px < N) img.SetPixel(px, y, col);
            }
        }
        img.GenerateMipmaps(); // without mips, distant cards sample base-level alpha and alias into solid blocks (probe run 5)
        return ImageTexture.CreateFromImage(img);
    }

    // ---- Backdrop: ground apron + mountain ring + forest edge ----

    public static void AddBackdrop(Node3D parent)
    {
        // Apron closing the horizon under the arena rim.
        var apronMat = new StandardMaterial3D
        {
            AlbedoTexture = Tex("leafy_grass", "diff"),
            AlbedoColor = new Color(0.52f, 0.55f, 0.5f),
            Roughness = 1f,
            Uv1Scale = new Vector3(220f, 220f, 1f)
        };
        var apron = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(1700f, 1700f) },
            MaterialOverride = apronMat,
            Position = new Vector3(0f, -3.2f, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        parent.AddChild(apron);

        // Craggy ring: low-segment cones with the scanned rock set.
        var rockMat = new StandardMaterial3D
        {
            AlbedoTexture = Tex("aerial_rocks_02", "diff"),
            NormalEnabled = true,
            NormalTexture = Tex("aerial_rocks_02", "nor_gl"),
            RoughnessTexture = Tex("aerial_rocks_02", "arm"),
            RoughnessTextureChannel = BaseMaterial3D.TextureChannel.Green,
            Uv1Triplanar = true,
            Uv1Scale = new Vector3(0.03f, 0.03f, 0.03f)
        };
        var rng = new RandomNumberGenerator { Seed = 90210 };
        for (int i = 0; i < 12; i++)
        {
            float a = i / 12f * Mathf.Tau + rng.RandfRange(-0.14f, 0.14f);
            float dist = rng.RandfRange(330f, 520f);
            float h = rng.RandfRange(38f, 95f);
            float r = h * rng.RandfRange(1.15f, 1.7f);
            var cone = new MeshInstance3D
            {
                Mesh = new CylinderMesh
                {
                    TopRadius = r * 0.10f, BottomRadius = r,
                    Height = h, RadialSegments = 7, Rings = 1
                },
                MaterialOverride = rockMat,
                Position = new Vector3(Mathf.Cos(a) * dist, h / 2f - 6f, Mathf.Sin(a) * dist),
                Rotation = new Vector3(0f, rng.RandfRange(0f, Mathf.Tau), 0f),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            };
            parent.AddChild(cone);
        }

        // Forest edge: scanned trees ringing the arena, visual only.
        for (int i = 0; i < 44; i++)
        {
            float a = rng.RandfRange(0f, Mathf.Tau);
            float dist = rng.RandfRange(98f, 165f);
            var pos = new Vector3(Mathf.Cos(a) * dist, 0f, Mathf.Sin(a) * dist);
            pos.Y = Main.HeightAt(Mathf.Clamp(pos.X, -84f, 84f), Mathf.Clamp(pos.Z, -84f, 84f)) - 0.3f;
            var holder = new Node3D { Position = pos };
            AddTreeModel(holder, rng.RandiRange(0, 2), rng);
            holder.Scale = Vector3.One * rng.RandfRange(1.15f, 1.6f);
            parent.AddChild(holder);
        }
        GD.Print("PHOTO backdrop: apron + 12 peaks + 44 ring trees");
    }

    // ---- Renderer / atmosphere tuning for the probe ----

    public static void Tune(Environment env, DirectionalLight3D sun)
    {
        env.SdfgiEnabled = true;
        // Optimization #2 (owner): fewer, cheaper cascades + probe
        // occlusion; convergence cost is spread thinner, which
        // flattens frame times. Quality at arena scale is unchanged
        // in practice — 4 cascades cover 170u comfortably.
        env.SdfgiCascades = 4;
        env.SdfgiUseOcclusion = true;
        env.SdfgiMinCellSize = 0.35f;
        env.SsaoEnabled = true;
        env.VolumetricFogEnabled = true;
        env.VolumetricFogDensity = 0.0022f; // probe run 1 white-out: volumetric density is its own dial, and the default buried the scene
        env.FogDensity = 0.0016f; // distance haze like the reference shot
        env.GlowEnabled = true;
        env.GlowIntensity = 0.15f;
        env.GlowStrength = 0.7f;
        // Look pass (owner: fog + bright light overilluminated the
        // scene). The wash came from stacked ambient terms — sky
        // ambient 0.42 + full-strength SDFGI bounce + bright fog —
        // so the fix is to let the SUN carry the image and deepen
        // everything else: lower exposure, weaker ambient and
        // bounce, darker fog, and a gentle filmic contrast/
        // saturation adjustment on top. The haze itself stays.
        env.TonemapExposure = 0.94f;
        env.AmbientLightEnergy = 0.30f;
        env.SdfgiEnergy = 0.85f;
        env.FogLightColor = new Color(0.55f, 0.65f, 0.78f);
        env.VolumetricFogAlbedo = new Color(0.55f, 0.65f, 0.78f);
        env.AdjustmentEnabled = true;
        env.AdjustmentBrightness = 1.0f;
        env.AdjustmentContrast = 1.08f;
        env.AdjustmentSaturation = 1.05f;
        sun.LightEnergy = 1.45f;
        sun.DirectionalShadowMaxDistance = 520f;
        GD.Print("PHOTO env tuned: SDFGI(4 cascades) + SSAO + volumetric fog + glow; darker contrast look");
    }
}
