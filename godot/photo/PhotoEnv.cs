using Godot;
using System.Collections.Generic;

// Photoreal probe environment (owner ask, 2026-10-29): the duel's
// world rebuilt from CC0 photogrammetry — Poly Haven texture sets
// splat-blended over the SAME heightfield, scanned tree/rock models
// at the SAME prop positions, an instanced grass field, and a
// mountain backdrop. Everything here is visual only: HeightAt,
// colliders, hit cylinders and destructible registration in
// RiderDuel are untouched, so the duel plays identically. Assets
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
uniform vec4 grass_tint : source_color = vec4(1.0);
uniform vec4 dirt_tint : source_color = vec4(1.0);
uniform vec4 rock_tint : source_color = vec4(1.0);
uniform vec4 asph_tint : source_color = vec4(1.0);
// Solid ground zones (owner, 2026-11-11 round 6: 'replace
// checkerboard with solid color'): inside a zone (xy =
// centre XZ, z = radius, w = strength) the ground renders
// as ONE flat colour — no splat patches, no texture grain.
uniform vec4 solid_zones[4];
uniform vec4 solid_col : source_color = vec4(0.0);
varying vec3 wpos;
void vertex() { wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
void fragment() {
    vec4 sp = texture(splat, wpos.xz / WORLD_SIZE + 0.5);
    float tot = max(sp.r + sp.g + sp.b + sp.a, 0.001);
    float wr = sp.r / tot, wg = sp.g / tot, wb = sp.b / tot, wa = sp.a / tot;
    vec2 uv1 = wpos.xz * 0.055;
    vec2 uv2 = wpos.xz * 0.019 + 0.37;
    vec3 alb = vec3(0.0);
    alb += wg * grass_tint.rgb * mix(texture(grass_diff, uv1).rgb, texture(grass_diff, uv2).rgb, 0.45);
    alb += wb * dirt_tint.rgb * mix(texture(dirt_diff, uv1).rgb, texture(dirt_diff, uv2).rgb, 0.45);
    alb += wr * rock_tint.rgb * mix(texture(rock_diff, uv1).rgb, texture(rock_diff, uv2).rgb, 0.45);
    alb += wa * asph_tint.rgb * mix(texture(asph_diff, uv1).rgb, texture(asph_diff, uv2).rgb, 0.45);
    vec3 nrm = vec3(0.5, 0.5, 1.0);
    nrm += wg * (texture(grass_nor, uv1).rgb - vec3(0.5, 0.5, 1.0));
    nrm += wb * (texture(dirt_nor, uv1).rgb - vec3(0.5, 0.5, 1.0));
    nrm += wr * (texture(rock_nor, uv1).rgb - vec3(0.5, 0.5, 1.0));
    nrm += wa * (texture(asph_nor, uv1).rgb - vec3(0.5, 0.5, 1.0));
    vec3 arm = vec3(1.0, 0.9, 0.0);
    arm *= wg * texture(grass_arm, uv1).rgb + wb * texture(dirt_arm, uv1).rgb
         + wr * texture(rock_arm, uv1).rgb + wa * texture(asph_arm, uv1).rgb;
    float sf = 0.0;
    for (int zi = 0; zi < 4; zi++) {
        vec4 zn = solid_zones[zi];
        if (zn.w > 0.001) {
            float dd = distance(wpos.xz, zn.xy);
            sf = max(sf, zn.w * (1.0 - smoothstep(zn.z - 14.0, zn.z, dd)));
        }
    }
    alb = mix(alb, solid_col.rgb, sf);
    nrm = mix(nrm, vec3(0.5, 0.5, 1.0), sf);
    ALBEDO = alb;
    NORMAL_MAP = nrm;
    NORMAL_MAP_DEPTH = 0.9;
    ROUGHNESS = mix(clamp(arm.g, 0.05, 1.0), 0.95, sf);
    AO = mix(arm.r, 1.0, sf);
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
                float x = (i / (float)(N - 1) - 0.5f) * Main.TerrainSize;
                float z = (j / (float)(N - 1) - 0.5f) * Main.TerrainSize;
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
                float street = 1f - Mathf.SmoothStep(5.5f, 8f, Main.DistrictStreetDistance(x, z));
                road = Mathf.Max(road, street);
                // Biome weights (owner's map types): the desert
                // runs sand (dirt channel, tinted) with only a
                // scrub of grass; the city paves its blocks —
                // asphalt spreads by coarse blocks over flat
                // ground, grass all but vanishes.
                if (WorldConfig.Biome == WorldConfig.BiomeKind.Desert)
                {
                    dirt = Mathf.SmoothStep(0.30f, 0.52f, patch) * (1f - rock);
                    grass = Mathf.Max(0f, 1f - rock - dirt) * 0.35f;
                }
                else if (WorldConfig.Biome == WorldConfig.BiomeKind.City)
                {
                    float blocks = ValueNoise(x * 0.021f + 9f, z * 0.021f + 3f);
                    float paved = Mathf.SmoothStep(0.34f, 0.52f, blocks) * (1f - Mathf.SmoothStep(0.5f, 0.8f, slope));
                    road = Mathf.Max(road, paved * 0.9f);
                    grass *= 0.12f;
                    dirt *= 0.5f;
                }
                // City district ground stays UNIFORM (owner,
                // 2026-11-11 round 5: the checkerboard comes
                // out everywhere): the old plot/lane split —
                // dirt plots against asphalt lanes on the 32u
                // grid — read as a crisp checkerboard wherever
                // no buildings covered it, worst across the
                // emptied home valley. No district term: the
                // ground flows on the smooth patch field like
                // everywhere else; the buildings themselves
                // define the lanes.
                img.SetPixel(i, j, new Color(
                    Mathf.Clamp(rock, 0f, 1f), grass,
                    Mathf.Clamp(dirt, 0f, 1f), road));
            }
        }
        return ImageTexture.CreateFromImage(img);
    }

    public static Material TerrainMaterial()
    {
        var sh = new Shader { Code = TerrainShader.Replace("WORLD_SIZE",
            Main.TerrainSize.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)) };
        var m = new ShaderMaterial { Shader = sh };
        m.SetShaderParameter("splat", BuildSplat());
        // Biome tints over the scanned sets: sand over the dirt
        // and grass channels in the desert; moss-dimmed grass and
        // lighter concrete in the city; Forest stays neutral.
        var (gt, dt, rt, at) = WorldConfig.Biome switch
        {
            WorldConfig.BiomeKind.Desert => (new Color(1.30f, 1.10f, 0.62f), new Color(1.22f, 1.02f, 0.66f), new Color(1.05f, 0.98f, 0.90f), new Color(1.05f, 0.98f, 0.85f)),
            WorldConfig.BiomeKind.City => (new Color(0.55f, 0.58f, 0.55f), new Color(0.80f, 0.78f, 0.75f), new Color(1.0f, 1.0f, 1.0f), new Color(0.78f, 0.78f, 0.82f)),
            _ => (new Color(1f, 1f, 1f), new Color(1f, 1f, 1f), new Color(1f, 1f, 1f), new Color(1f, 1f, 1f))
        };
        if (WorldConfig.SnowGround)
        {
            // Snow cover (owner): the ground matches a snowy
            // sky — every channel lifts to blue-white snow,
            // asphalt stays a paler plowed grey.
            gt = new Color(1.55f, 1.65f, 1.95f); dt = new Color(1.45f, 1.55f, 1.85f);
            rt = new Color(1.35f, 1.45f, 1.75f); at = new Color(1.05f, 1.12f, 1.30f);
        }
        // Solid ground zones (owner round 6): on mission
        // city maps the valley floors (and the Gate City
        // disc) render as one flat colour — the splat patch
        // field read as a checkerboard there no matter how
        // smooth the weights got. Snow city: pale blue-grey
        // concrete; plain city: mid concrete.
        var zones = new Vector4[4];
        if (WorldConfig.IsMission && WorldConfig.Biome == WorldConfig.BiomeKind.City)
        {
            float padR = WorldConfig.TerracedCity ? 300f : 120f;
            for (int i = 0; i < 3 && i < Main.SpawnPads.Length; i++)
                zones[i] = new Vector4(Main.SpawnPads[i].X, Main.SpawnPads[i].Y, padR, 1f);
            if (!WorldConfig.TerracedCity)
                zones[3] = new Vector4(0f, 0f, CityGrid.Radius, 1f);
            m.SetShaderParameter("solid_col", WorldConfig.SnowGround
                ? new Color(0.58f, 0.64f, 0.74f) : new Color(0.33f, 0.34f, 0.35f));
        }
        m.SetShaderParameter("solid_zones", zones);
        m.SetShaderParameter("grass_tint", gt);
        m.SetShaderParameter("dirt_tint", dt);
        m.SetShaderParameter("rock_tint", rt);
        m.SetShaderParameter("asph_tint", at);
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

    // Photographic wall sets (Poly Haven, CC0): one per
    // building variant — the photoreal pass (owner, 2026-11-07)
    // so buildings stop sharing a single concrete block.
    private static readonly string[] WallSets =
    {
        "concrete_block_wall_02", "brick_wall_04", "plastered_wall",
        "concrete_panels", "concrete_tile_facade", "painted_plaster_wall"
    };

    public static Material BuildingMaterial(Vector3 size, Color tint, int variant = 0)
    {
        string set = WallSets[((variant % WallSets.Length) + WallSets.Length) % WallSets.Length];
        return new StandardMaterial3D
        {
            AlbedoColor = tint, // per-building shade (owner: buildings must not all look the same)
            AlbedoTexture = Tex(set, "diff"),
            NormalEnabled = true,
            NormalScale = 0.9f,
            NormalTexture = Tex(set, "nor_gl"),
            RoughnessTexture = Tex(set, "arm"),
            RoughnessTextureChannel = BaseMaterial3D.TextureChannel.Green,
            AOTexture = Tex(set, "arm"),
            Uv1Scale = new Vector3(Mathf.Max(size.X, size.Z) / 3.5f, size.Y / 3.5f, 1f)
        };
    }

    // Roof faces wear gravel concrete, not the wall skin.
    public static Material RoofMaterial(Vector3 size)
    {
        return new StandardMaterial3D
        {
            AlbedoTexture = Tex("gravel_concrete_02", "diff"),
            NormalEnabled = true,
            NormalScale = 0.7f,
            NormalTexture = Tex("gravel_concrete_02", "nor_gl"),
            RoughnessTexture = Tex("gravel_concrete_02", "arm"),
            RoughnessTextureChannel = BaseMaterial3D.TextureChannel.Green,
            AOTexture = Tex("gravel_concrete_02", "arm"),
            Uv1Scale = new Vector3(size.X / 3f, size.Z / 3f, 1f)
        };
    }

    // ---- Instanced grass field (visual only) ----

    public static void AddGrassField(Node3D parent, List<Vector2> buildingPts,
        List<(Vector2 P, float R)> props)
    {
        // Scanned hero clumps ONLY (the full grass patch, 24.7k
        // tris, used sparingly — 320 instances ≈ 8M tris). The
        // blade-card layer (60k crossed-quad tufts) was REMOVED
        // at the owner's word (2026-10-31): the crossed quads
        // read as intersecting flat planes, worst of all where
        // they stood in the water. The naive all-scanned version
        // (42k patches) was 1.04 BILLION triangles/frame: that is
        // the single most important budget fact this probe
        // produced.
        var spots = new List<(Vector3 Pos, float Yaw, float S)>();
        var rng = new RandomNumberGenerator { Seed = 424242 };
        int attempts = 0;
        while (spots.Count < 6000 && attempts++ < 120000)
        {
            float x = rng.RandfRange(-84f, 84f), z = rng.RandfRange(-84f, 84f);
            if (Main.RoadDistance(x, z) < 2.9f) continue;
            float h = Main.HeightAt(x, z);
            if (h > 10f) continue;
            if (h < 0.3f) continue; // dry land only: scatter stood in the sea/lake shallows (owner)
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
            GD.Print($"PHOTO grass: {heroes} scanned hero clumps over {spots.Count} dry-land spots (blade cards removed — owner)");
        }
    }

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
        bool mission = WorldConfig.IsMission;
        float bs = Main.TerrainSize / 260f; // backdrop scale vs the arena
        // Apron closing the horizon under the rim.
        var apronMat = new StandardMaterial3D
        {
            AlbedoTexture = Tex("leafy_grass", "diff"),
            AlbedoColor = WorldConfig.SnowGround ? new Color(1.35f, 1.45f, 1.75f) : new Color(0.52f, 0.55f, 0.5f),
            Roughness = 1f,
            Uv1Scale = new Vector3(220f, 220f, 1f)
        };
        var apron = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(1700f * bs, 1700f * bs) },
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
        for (int i = 0; i < (WorldConfig.BackdropPeaks ? 12 : 0); i++)
        {
            float a = i / 12f * Mathf.Tau + rng.RandfRange(-0.14f, 0.14f);
            float dist = rng.RandfRange(330f, 520f) * (mission ? bs * 0.55f : 1f);
            float h = rng.RandfRange(38f, 95f) * (mission ? 2.1f : 1f);
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

        // Backdrop ring by biome: forest keeps its tree ring
        // (visual only; on the Mission front it rings the rim),
        // the desert gets bare rock outcrops; the city gets no
        // ring (owner: no procedural buildings outside the map —
        // every city building is a gameplay building inside it).
        if (WorldConfig.Biome == WorldConfig.BiomeKind.City) return;
        if (WorldConfig.Biome == WorldConfig.BiomeKind.Desert)
        {
            for (int i = 0; i < 18; i++)
            {
                float a2 = rng.RandfRange(0f, Mathf.Tau);
                float half2 = Main.TerrainSize / 2f;
                float dist2 = mission ? rng.RandfRange(half2 + 18f, half2 + 95f) : rng.RandfRange(98f, 165f);
                var pos2 = new Vector3(Mathf.Cos(a2) * dist2, 0f, Mathf.Sin(a2) * dist2);
                float cl2 = mission ? half2 - 4f : 84f;
                pos2.Y = Main.HeightAt(Mathf.Clamp(pos2.X, -cl2, cl2), Mathf.Clamp(pos2.Z, -cl2, cl2)) - 0.5f;
                var rock = MakeRock(new Vector3(rng.RandfRange(3.5f, 6.5f), rng.RandfRange(2.2f, 4.2f), rng.RandfRange(3.5f, 6.5f)));
                rock.Position = pos2;
                parent.AddChild(rock);
            }
            GD.Print("PHOTO backdrop: apron + peaks + desert rock ring");
            return;
        }
        for (int i = 0; i < 44; i++)
        {
            float a = rng.RandfRange(0f, Mathf.Tau);
            float half = Main.TerrainSize / 2f;
            float dist = mission ? rng.RandfRange(half + 18f, half + 95f) : rng.RandfRange(98f, 165f);
            var pos = new Vector3(Mathf.Cos(a) * dist, 0f, Mathf.Sin(a) * dist);
            float cl = mission ? half - 4f : 84f;
            pos.Y = Main.HeightAt(Mathf.Clamp(pos.X, -cl, cl), Mathf.Clamp(pos.Z, -cl, cl)) - 0.3f;
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
        env.SdfgiCascades = WorldConfig.IsMission ? 6 : 4;
        env.SdfgiUseOcclusion = true;
        env.SdfgiMinCellSize = 0.35f;
        env.SsaoEnabled = true;
        env.VolumetricFogEnabled = true;
        // Fog by biome: warm haze in the desert, a duskier
        // teal-grey in the city (lit windows and signs carry),
        // the standing blue-grey in the forest.
        var (fogC, fogD, volD, sunE) = WorldConfig.Biome switch
        {
            WorldConfig.BiomeKind.Desert => (new Color(0.80f, 0.70f, 0.53f), WorldConfig.IsMission ? 0.00050f : 0.0019f, WorldConfig.IsMission ? 0.0009f : 0.0026f, 1.55f),
            WorldConfig.BiomeKind.City => (new Color(0.42f, 0.52f, 0.60f), WorldConfig.IsMission ? 0.00055f : 0.0021f, WorldConfig.IsMission ? 0.0010f : 0.0028f, 1.15f),
            _ => (new Color(0.55f, 0.65f, 0.78f), WorldConfig.IsMission ? 0.00042f : 0.0016f, WorldConfig.IsMission ? 0.00075f : 0.0022f, 1.45f)
        };
        env.VolumetricFogDensity = volD;
        env.FogDensity = fogD; // distance haze; the Mission front hazes out near ~1200u
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
        env.AmbientLightEnergy = 0.50f; // raised 0.30 -> 0.50 (owner, 2026-11-11: shadows must stay readable)
        env.SdfgiEnergy = 0.85f;
        env.FogLightColor = fogC;
        env.VolumetricFogAlbedo = fogC;
        env.AdjustmentEnabled = true;
        env.AdjustmentBrightness = 1.0f;
        env.AdjustmentContrast = 1.08f;
        env.AdjustmentSaturation = 1.05f;
        sun.LightEnergy = sunE;
        sun.ShadowOpacity = 0.28f; // shadows barely noticeable (owner, 2026-11-11)
        sun.DirectionalShadowMaxDistance = WorldConfig.IsMission ? 900f : 520f;
        GD.Print("PHOTO env tuned: SDFGI(4 cascades) + SSAO + volumetric fog + glow; darker contrast look");
    }
}
