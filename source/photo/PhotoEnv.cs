using Godot;
using System.Collections.Generic;

// Photoreal probe environment (owner ask, 2026-10-29): the duel's
// world rebuilt from CC0 photogrammetry - Poly Haven texture sets
// splat-blended over the SAME heightfield, scanned tree/rock models
// at the SAME prop positions, an instanced grass field, and a
// mountain backdrop. Everything here is visual only: HeightAt,
// colliders, hit cylinders and destructible registration in
// Titans are untouched, so the duel plays identically. Assets
// live in res://photo/ (see fetch_photo_assets.py and
// decimate_models.py there; Poly Haven content is CC0).
public static class PhotoEnv
{
    private const string Root = "res://photo/";
    private static readonly Dictionary<string, Texture2D> _tex = new();
    private static readonly Dictionary<string, PackedScene> _scenes = new();

    // The ground apron plane sits this far below the origin,
    // under every heightfield, so its edge never z-fights the
    // terrain it extends.
    private const float ApronDrop = 3f;

    // Biome tint palette (C# side only - the terrain
    // shader's own literals are NOT touched by this pass:
    // a silent shader compile failure once hid for weeks).
    private static readonly Color DesertGrassTint = new(1.30f, 1.10f, 0.62f);
    private static readonly Color DesertDirtTint = new(1.22f, 1.02f, 0.66f);
    private static readonly Color DesertRockTint = new(1.05f, 0.98f, 0.90f);
    private static readonly Color DesertAsphTint = new(1.05f, 0.98f, 0.85f);
    private static readonly Color CityGrassTint = new(0.55f, 0.58f, 0.55f);
    private static readonly Color CityDirtTint = new(0.80f, 0.78f, 0.75f);
    private static readonly Color CityRockTint = new(1.0f, 1.0f, 1.0f);
    private static readonly Color CityAsphTint = new(0.78f, 0.78f, 0.82f);
    private static readonly Color SnowGroundTint = new(1.35f, 1.45f, 1.75f); // grass/dirt lift under snow; also the apron's snow colour
    private static readonly Color SnowRockTint = new(0.88f, 0.89f, 0.94f);
    private static readonly Color SnowAsphTint = new(1.05f, 1.12f, 1.30f);
    private static readonly Color ApronFieldTint = new(0.52f, 0.55f, 0.5f);
    private static readonly Color DesertFogColor = new(0.80f, 0.70f, 0.53f);
    private static readonly Color CityFogColor = new(0.42f, 0.52f, 0.60f);
    private static readonly Color ForestFogColor = new(0.55f, 0.65f, 0.78f);
    // Renderer tuning for Tune() - exact current values.
    private const float SdfgiCellSize = 0.35f;
    private const float GlowIntensityValue = 0.15f, GlowStrengthValue = 0.7f;
    private const float TonemapExposureValue = 0.94f, AmbientEnergyValue = 0.50f, SdfgiEnergyValue = 0.85f;
    private const float AdjustmentContrastValue = 1.08f, AdjustmentSaturationValue = 1.05f;
    private const float SunShadowOpacity = 0.55f;
    private const float MissionShadowDistance = 900f, ArenaShadowDistance = 520f;
    private const float WallTileSize = 3.5f, RoofTileSize = 3f;   // UV tiling divisors for the wall/roof sets
    private const float WallNormalScale = 0.9f, RoofNormalScale = 0.7f;
    private const float PineStandInHeight = 7.6f, IslandTreeHeight = 5.9f;
    private const int BackdropTreeCount = 44;
    private const int HeroClumpCap = 320;
    // Grass scatter: spot acceptance rules and placement.
    private const int GrassSpotTarget = 6000;
    private const int GrassScatterMaxAttempts = 120000;
    private const float GrassScatterBound = 84f;   // spots sample within +/- this
    private const float GrassRoadClearance = 2.9f;
    private const float GrassMaxHeight = 10f, GrassMinHeight = 0.3f;
    private const float GrassSlopeSampleStep = 0.4f, GrassMaxSlope = 0.45f;
    private const float GrassPropClearanceMargin = 0.5f; // added to a prop's own radius
    private const float GrassSpotSink = 0.04f;       // spots sit this far into the ground
    private const float GrassSpotScaleMin = 0.75f, GrassSpotScaleMax = 1.5f;
    private const int HeroSpotStride = 37;         // hero clumps pick spots at this stride through the list
    private const float HeroClumpScaleMin = 1.1f, HeroClumpScaleMax = 1.9f;
    // Backdrop: apron and ring placement.
    private const float BackdropArenaReference = 260f; // backdrop scale is relative to the arena's size
    private const float ApronUvScale = 220f;
    private const float BackdropPlaneSize = 1700f;
    private const float RingMissionInnerMargin = 18f, RingMissionOuterMargin = 95f; // past the terrain half-size
    private const float RingArenaDistMin = 124f, RingArenaDistMax = 175f;
    private const float RingClampInset = 4f, RingArenaClamp = 84f; // height-sample clamp bounds
    private const float RingTreeSink = 0.3f;
    private const float BackdropTreeScaleMin = 1.15f, BackdropTreeScaleMax = 1.6f;

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
uniform sampler2D splat : filter_linear_mipmap, repeat_disable;
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
uniform float snow_mode = 0.0;
uniform vec4 dirt_tint : source_color = vec4(1.0);
uniform vec4 rock_tint : source_color = vec4(1.0);
uniform vec4 asph_tint : source_color = vec4(1.0);
// Uniform ground zones (owner, 2026-11-11 round 6,
// corrected round 7: 'when I said solid color I meant it
// should look the same as the ground in other places'):
// inside a zone (xy = centre XZ, z = radius, w = strength)
// the splat's rock/grass/dirt weights freeze to the zone
// centre's own mix, so the patch and paving noise that
// read as blotches/checkerboard cannot vary the surface -
// while the detail textures, normals and road weight stay
// fully live. The ground is the same material as anywhere
// else; it just stops changing its recipe mid-valley.
uniform vec4 solid_zones[4];
varying vec3 wpos;
varying vec3 wnorm;
// The coarse detail scale is sampled through a 38-degree
// rotation (owner, 2026-11-22: the mountain texture read as
// one repeating tile): with the two scales sharing no axis,
// their composite has no short period left to recognise, and
// the blend between them also drifts per 160u cell below, so
// no two stretches of slope wear the same pattern.
vec2 rot38(vec2 p) { return vec2(0.788 * p.x + 0.616 * p.y, -0.616 * p.x + 0.788 * p.y); }
// (Was mat2(0.788, -0.616, 0.616, 0.788) * p - Godot's
// shader compiler rejects the 4-scalar mat2 constructor,
// and the failure was SILENT in play: the whole terrain
// shader fell back to a default material, which is why
// the snow material and the slope stones never showed
// from 2026-11-23 until the --stone-shot log audit.)
void vertex() {
    wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    wnorm = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}
float vhash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float vnoise(vec2 p) {
    vec2 i = floor(p); vec2 f = fract(p); f = f * f * (3.0 - 2.0 * f);
    return mix(mix(vhash(i), vhash(i + vec2(1.0, 0.0)), f.x),
               mix(vhash(i + vec2(0.0, 1.0)), vhash(i + vec2(1.0, 1.0)), f.x), f.y);
}
void fragment() {
    vec4 sp = texture(splat, wpos.xz / WORLD_SIZE + 0.5);
    float zf = 0.0; vec3 zrgb = vec3(0.0);
    for (int zi = 0; zi < 4; zi++) {
        vec4 zn = solid_zones[zi];
        if (zn.w > 0.001) {
            float dd = distance(wpos.xz, zn.xy);
            float f = zn.w * (1.0 - smoothstep(zn.z - 34.0, zn.z, dd));
            if (f > zf) { zf = f; zrgb = texture(splat, zn.xy / WORLD_SIZE + 0.5).rgb; }
        }
    }
    sp = vec4(mix(sp.rgb, zrgb, zf), sp.a);
    float tot = max(sp.r + sp.g + sp.b + sp.a, 0.001);
    float wr = sp.r / tot, wg = sp.g / tot, wb = sp.b / tot, wa = sp.a / tot;
    // Detail fades with distance: past ~150u the fine scale
    // and the normal relief dissolve into per-pixel dither on
    // open slopes (owner's streaked-mountains screenshot,
    // 2026-11-15), so the mix leans on the coarse scale and
    // the normal depth eases off as the ground recedes. The
    // fade was too strong (owner, 2026-11-23: at gameplay
    // distance the pattern 'looks flat') - the far lean and
    // the depth ease below are softened; the dither guard
    // stays, just shallower.
    float camd = distance(wpos, CAMERA_POSITION_WORLD);
    float far_k = smoothstep(150.0, 450.0, camd);
    float detmix = mix(0.45, 0.58, far_k);
    float cellh = fract(sin(dot(floor(wpos.xz / 160.0), vec2(127.1, 311.7))) * 43758.5453);
    detmix = clamp(detmix + (cellh - 0.5) * 0.35, 0.05, 0.95);
    vec2 uv1 = wpos.xz * 0.055;
    vec2 uv2 = rot38(wpos.xz * 0.019) + 0.37;
    // Rock is TRIPLANAR (owner, 2026-11-15): the scanned rock
    // set carries strong sedimentary strata, and planar XZ
    // sampling wrapped those strata around every slope as
    // contour bands, squashed into vertical streaks on steep
    // faces. Blending the three world projections by the
    // surface normal breaks the contour wrap - the strata run
    // with the projection, not with the hillside.
    vec3 tw = pow(abs(wnorm), vec3(4.0));
    tw /= max(tw.x + tw.y + tw.z, 0.001);
    vec3 rkAlb = vec3(0.0);
    rkAlb += tw.y * mix(texture(rock_diff, uv1).rgb, texture(rock_diff, uv2).rgb, detmix);
    rkAlb += tw.x * mix(texture(rock_diff, wpos.zy * 0.055).rgb, texture(rock_diff, rot38(wpos.zy * 0.019) + 0.37).rgb, detmix);
    rkAlb += tw.z * mix(texture(rock_diff, wpos.xy * 0.055).rgb, texture(rock_diff, rot38(wpos.xy * 0.019) + 0.37).rgb, detmix);
    // Winter rule (owner, 2026-11-23): under snow the stone
    // is GREY or the field is WHITE - the scanned set's warm
    // brown chroma reads as mossy green in the winter light.
    // The rock albedo goes value-only under snow_mode, like
    // the snowfield itself; the relief and roughness maps
    // keep all the detail. (Both the splat rock weight and
    // the slope mix drink from rkAlb, so this covers both.)
    if (snow_mode > 0.5) rkAlb = vec3(dot(rkAlb, vec3(0.299, 0.587, 0.114)));
    vec3 rkNor = vec3(0.0);
    rkNor += tw.y * texture(rock_nor, uv1).rgb;
    rkNor += tw.x * texture(rock_nor, wpos.zy * 0.055).rgb;
    rkNor += tw.z * texture(rock_nor, wpos.xy * 0.055).rgb;
    vec3 rkArm = vec3(0.0);
    rkArm += tw.y * texture(rock_arm, uv1).rgb;
    rkArm += tw.x * texture(rock_arm, wpos.zy * 0.055).rgb;
    rkArm += tw.z * texture(rock_arm, wpos.xy * 0.055).rgb;
    vec3 alb = vec3(0.0);
    alb += wg * grass_tint.rgb * mix(texture(grass_diff, uv1).rgb, texture(grass_diff, uv2).rgb, detmix);
    alb += wb * dirt_tint.rgb * mix(texture(dirt_diff, uv1).rgb, texture(dirt_diff, uv2).rgb, detmix);
    alb += wr * rock_tint.rgb * rkAlb;
    alb += wa * asph_tint.rgb * mix(texture(asph_diff, uv1).rgb, texture(asph_diff, uv2).rgb, detmix);
    vec3 nrm = vec3(0.5, 0.5, 1.0);
    nrm += wg * (texture(grass_nor, uv1).rgb - vec3(0.5, 0.5, 1.0));
    nrm += wb * (texture(dirt_nor, uv1).rgb - vec3(0.5, 0.5, 1.0));
    nrm += wr * (rkNor - vec3(0.5, 0.5, 1.0));
    nrm += wa * (texture(asph_nor, uv1).rgb - vec3(0.5, 0.5, 1.0));
    vec3 arm = vec3(1.0, 0.9, 0.0);
    arm *= wg * texture(grass_arm, uv1).rgb + wb * texture(dirt_arm, uv1).rgb
         + wr * rkArm + wa * texture(asph_arm, uv1).rgb;
    // Snow's own micro-surface (owner, 2026-11-23): under snow
    // cover the grass/dirt weights ARE the snowfield, and they
    // were sampling the rock set - sedimentary strata normals
    // and rock roughness tinted white, which reads as painted
    // rock. Snow gets near-flat normals (a whisper of grain),
    // its own fine-grained roughness, and full AO; the rock
    // weight keeps the stone's maps, so steeps stay exposed.
    if (snow_mode > 0.5) {
        float ws = clamp(wg + wb, 0.0, 1.0);
        float grain = fract(sin(dot(floor(wpos.xz * 9.0), vec2(127.1, 311.7))) * 43758.5453);
        float grain2 = fract(sin(dot(floor(wpos.xz * 2.3 + 40.0), vec2(269.5, 183.3))) * 28001.8384);
        // The snowfield's albedo: the underlying texture kept
        // as VALUE only - its luminance becomes soft shading
        // (dips and drifts), its chroma and contrast go.
        // (v1 flattened the normals over the tinted rock
        // albedo and the field blew out to a featureless
        // white void in the ground capture.)
        float lum = clamp(dot(alb, vec3(0.3333)), 0.0, 1.3);
        vec3 snowCol = vec3(0.88, 0.925, 1.05) * (0.84 + 0.34 * lum);
        // Soft value patches at gameplay scales - v2's grain
        // cells were sub-metre and the field still read as a
        // flat white void in the ground capture.
        snowCol *= 0.895 + 0.21 * vnoise(wpos.xz * 0.021); // ~50u drifts
        snowCol *= 0.945 + 0.11 * vnoise(wpos.xz * 0.33);  // ~3u soft mottle
        alb = mix(alb, snowCol, ws);
        nrm = mix(nrm, vec3(0.5, 0.5, 1.0), ws * 0.50);
        arm.g = mix(arm.g, 0.42 + 0.26 * grain, ws);
        arm.r = mix(arm.r, 1.0, ws);
        // Stones show on the steeps (owner, 2026-11-23: he
        // asked for stone on the mountains and the snow had
        // buried every face). The splat was painted from the
        // old analytic valley, so on the hills mesh its rock
        // weight is meaningless; slope is the truth. Past
        // ~30 degrees the triplanar rock takes over, its
        // edge feathered by noise so the snow line is not a
        // clean contour.
        // (Reversed-edge smoothstep was UB and returned 0 on
        // real drivers - the mix NEVER fired and the owner
        // saw no stones at all; written in the well-defined
        // direction now, proven by the --stone-shot frame.)
        float steep = 1.0 - smoothstep(0.66, 0.88, wnorm.y + (vnoise(wpos.xz * 0.05) - 0.5) * 0.10);
        alb = mix(alb, rock_tint.rgb * rkAlb, steep);
        nrm = mix(nrm, rkNor, steep);
        arm.g = mix(arm.g, rkArm.g, steep);
        arm.r = mix(arm.r, rkArm.r, steep);
    }
    ALBEDO = alb;
    NORMAL_MAP = nrm;
    NORMAL_MAP_DEPTH = 1.05 * (1.0 - far_k * 0.35);
    ROUGHNESS = clamp(arm.g, 0.05, 1.0);
    AO = arm.r;
}
";


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
                // Blurry-cloud variation (owner, 2026-11-12):
                // the ground must carry NO obvious shapes - no
                // blotches, no stripes, no rims. Every weight
                // below therefore crosses a WIDE smoothstep at
                // reduced strength, so the recipe drifts like
                // cloud shadows instead of swapping at an edge.
                // (The old patch field's fine 0.23 octave and
                // the narrow dirt/street/road bands drew the
                // shapes in his screenshot.)
                float patch = TerrainMath.SinValueNoise(x * 0.032f, z * 0.032f) * 0.85f
                            + TerrainMath.SinValueNoise(x * 0.09f, z * 0.09f) * 0.15f;
                // Rock is a SLOPE material only (owner,
                // 2026-11-11 round 7: 'ground should not
                // look the same as mountains - the grass/
                // stone mix should end where the mountain
                // goes into the ground'). The old height
                // term (+0.7 above h=13) dragged stone onto
                // every elevated flat - the pass floors wore
                // the mountain's face. Steep faces keep it.
                float rock = Mathf.SmoothStep(0.30f, 0.62f, slope);
                float dirt = Mathf.SmoothStep(0.30f, 0.85f, patch) * 0.6f * (1f - rock);
                float grass = Mathf.Max(0f, 1f - rock - dirt);
                // No full-strength core on the road either
                // (splat audit, 2026-11-12: the +-1.4u plateau
                // still drew a bright stripe down its middle) -
                // a pure gradient from a 0.65 peak, gone by
                // 7.5u. The district streets drop to a faint
                // wash: at full strength their grid lattice IS
                // the checkerboard the owner keeps reporting.
                float road = (1f - Mathf.SmoothStep(0f, 7.5f, Main.RoadDistance(x, z))) * 0.65f;
                float street = (1f - Mathf.SmoothStep(2f, 16f, Main.DistrictStreetDistance(x, z))) * 0.3f;
                // Plaza carve (splat audit round 2): the open
                // ground around a pad - the patrol plaza where
                // fights happen - carries no street wash at
                // all; the lattice there was the last crisp
                // pattern left in the weights. Streets fade
                // back in between 58u and 92u from a pad.
                float padD = float.MaxValue;
                foreach (var pad in Main.SpawnPads)
                {
                    float pdx = x - pad.X, pdz = z - pad.Y;
                    padD = Mathf.Min(padD, Mathf.Sqrt(pdx * pdx + pdz * pdz));
                }
                street *= Mathf.SmoothStep(58f, 92f, padD);
                road = Mathf.Max(road, street);
                // Biome weights (owner's map types): the desert
                // runs sand (dirt channel, tinted) with only a
                // scrub of grass; the city paves its blocks -
                // asphalt spreads by coarse blocks over flat
                // ground, grass all but vanishes.
                if (WorldConfig.Biome == WorldConfig.BiomeKind.Desert)
                {
                    dirt = Mathf.SmoothStep(0.15f, 0.80f, patch) * (1f - rock);
                    grass = Mathf.Max(0f, 1f - rock - dirt) * 0.35f;
                }
                else if (WorldConfig.Biome == WorldConfig.BiomeKind.City)
                {
                    float blocks = TerrainMath.SinValueNoise(x * 0.021f + 9f, z * 0.021f + 3f);
                    float paved = Mathf.SmoothStep(0.05f, 0.95f, blocks) * (1f - Mathf.SmoothStep(0.5f, 0.8f, slope));
                    road = Mathf.Max(road, paved * 0.45f);
                    grass *= 0.12f;
                    dirt *= 0.5f;
                }
                // City district ground stays UNIFORM (owner,
                // 2026-11-11 round 5: the checkerboard comes
                // out everywhere): the old plot/lane split -
                // dirt plots against asphalt lanes on the 32u
                // grid - read as a crisp checkerboard wherever
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
        // Mipmaps (owner, 2026-11-13): without them the splat and
        // the detail textures minify by picking single texels, so
        // distant slopes render as per-pixel dither. The four
        // scanned sets' .import files generate mipmaps too now.
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }

    // Harness hook (phase 18): the baked splat weights as
    // an image, so ground-material complaints can be
    // diagnosed channel by channel instead of by eye.
    internal static Image SplatImage() => BuildSplat().GetImage();

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
            WorldConfig.BiomeKind.Desert => (DesertGrassTint, DesertDirtTint, DesertRockTint, DesertAsphTint),
            WorldConfig.BiomeKind.City => (CityGrassTint, CityDirtTint, CityRockTint, CityAsphTint),
            _ => (Colors.White, Colors.White, Colors.White, Colors.White)
        };
        string grassSet = "leafy_grass", dirtSet = "forest_floor";
        if (WorldConfig.SnowGround)
        {
            // Snow cover (owner, corrected 2026-11-23): snow
            // replaces the GREEN only - grass and dirt lift to
            // blue-white snow - while the mountains stay grey
            // stone: the rock channel keeps a neutral stone
            // tint. (The first pass lifted every channel to
            // snow white and the whole range washed out.)
            gt = SnowGroundTint; dt = gt;
            rt = SnowRockTint;
            at = SnowAsphTint;
            // No green under snow (owner, 2026-11-22): the
            // grass and dirt channels sample the rock set
            // itself, so no green can show through; under the
            // snow tint it reads as snow-covered ground.
            grassSet = "aerial_rocks_02"; dirtSet = "aerial_rocks_02";
        }
        // Uniform ground zones (owner round 6, corrected
        // round 7): on mission city maps the valley floors
        // (and the Gate City disc) freeze their splat
        // recipe to the pad centre's mix in the shader -
        // same ground as everywhere else, minus the patch
        // blotches. (Round 6 first rendered them as one
        // flat colour: a blue plastic sheet. Replaced.)
        var zones = new Vector4[4];
        if (WorldConfig.IsMission && WorldConfig.Biome == WorldConfig.BiomeKind.City)
        {
            float padR = 120f;
            for (int i = 0; i < 3 && i < Main.SpawnPads.Length; i++)
                zones[i] = new Vector4(Main.SpawnPads[i].X, Main.SpawnPads[i].Y, padR, 1f);
            zones[3] = new Vector4(0f, 0f, CityGrid.Radius, 1f);
        }
        m.SetShaderParameter("solid_zones", zones);
        m.SetShaderParameter("snow_mode", WorldConfig.SnowGround ? 1f : 0f);
        m.SetShaderParameter("grass_tint", gt);
        m.SetShaderParameter("dirt_tint", dt);
        m.SetShaderParameter("rock_tint", rt);
        m.SetShaderParameter("asph_tint", at);
        m.SetShaderParameter("grass_diff", Tex(grassSet, "diff"));
        m.SetShaderParameter("grass_nor", Tex(grassSet, "nor_gl"));
        m.SetShaderParameter("grass_arm", Tex(grassSet, "arm"));
        m.SetShaderParameter("dirt_diff", Tex(dirtSet, "diff"));
        m.SetShaderParameter("dirt_nor", Tex(dirtSet, "nor_gl"));
        m.SetShaderParameter("dirt_arm", Tex(dirtSet, "arm"));
        m.SetShaderParameter("rock_diff", Tex("aerial_rocks_02", "diff"));
        m.SetShaderParameter("rock_nor", Tex("aerial_rocks_02", "nor_gl"));
        m.SetShaderParameter("rock_arm", Tex("aerial_rocks_02", "arm"));
        m.SetShaderParameter("asph_diff", Tex("aerial_asphalt_01", "diff"));
        m.SetShaderParameter("asph_nor", Tex("aerial_asphalt_01", "nor_gl"));
        m.SetShaderParameter("asph_arm", Tex("aerial_asphalt_01", "arm"));
        Dbg.Print("PHOTO terrain material ready (splat 1024, 4 scanned sets)");
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
        float targetH = variety == 1 ? PineStandInHeight : IslandTreeHeight;
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
    public static void AddGrassField(Node3D parent,
        List<(Vector2 P, float R)> props)
    {
        // Scanned hero clumps ONLY (the full grass patch, 24.7k
        // tris, used sparingly - 320 instances ≈ 8M tris). The
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
        while (spots.Count < GrassSpotTarget && attempts++ < GrassScatterMaxAttempts)
        {
            float x = rng.RandfRange(-GrassScatterBound, GrassScatterBound), z = rng.RandfRange(-GrassScatterBound, GrassScatterBound);
            if (Main.RoadDistance(x, z) < GrassRoadClearance) continue;
            float h = Main.HeightAt(x, z);
            if (h > GrassMaxHeight) continue;
            if (h < GrassMinHeight) continue; // dry land only: scatter stood in the sea/lake shallows (owner)
            float e = GrassSlopeSampleStep;
            float slope = Mathf.Max(
                Mathf.Abs(Main.HeightAt(x + e, z) - h),
                Mathf.Abs(Main.HeightAt(x, z + e) - h)) / e;
            if (slope > GrassMaxSlope) continue;
            bool bad = false;
            foreach (var (cp, cr) in props)
                if (new Vector2(x, z).DistanceTo(cp) < cr + GrassPropClearanceMargin) { bad = true; break; }
            if (bad) continue;
            spots.Add((new Vector3(x, h - GrassSpotSink, z),
                rng.RandfRange(0f, Mathf.Tau), rng.RandfRange(GrassSpotScaleMin, GrassSpotScaleMax)));
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
            int heroes = Mathf.Min(HeroClumpCap, spots.Count);
            var hmm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = heroMesh,
                InstanceCount = heroes
            };
            for (int i = 0; i < heroes; i++)
            {
                var (pos, yaw, _) = spots[i * HeroSpotStride % spots.Count];
                float sc = rng.RandfRange(HeroClumpScaleMin, HeroClumpScaleMax);
                hmm.SetInstanceTransform(i, new Transform3D(
                    new Basis(Vector3.Up, yaw).Scaled(new Vector3(sc, sc, sc)), pos));
            }
            parent.AddChild(new MultiMeshInstance3D
            {
                Multimesh = hmm,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            });
            Dbg.Print($"PHOTO grass: {heroes} scanned hero clumps over {spots.Count} dry-land spots (blade cards removed - owner)");
        }
    }

    // ---- Backdrop: ground apron + biome rim rings (forest trees,
    // desert rocks). The procedural peak ring was removed entirely
    // (owner, 2026-11-19: the cone mountains read fake on every map).

    public static void AddBackdrop(Node3D parent)
    {
        bool mission = WorldConfig.IsMission;
        float bs = Main.TerrainSize / BackdropArenaReference; // backdrop scale vs the arena
        // Apron closing the horizon under the rim.
        var apronMat = new StandardMaterial3D
        {
            AlbedoTexture = Tex("leafy_grass", "diff"),
            AlbedoColor = WorldConfig.SnowGround ? SnowGroundTint : ApronFieldTint,
            Roughness = 1f,
            Uv1Scale = new Vector3(ApronUvScale, ApronUvScale, 1f)
        };
        var apron = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(BackdropPlaneSize * bs, BackdropPlaneSize * bs) },
            MaterialOverride = apronMat,
            Position = new Vector3(0f, -ApronDrop, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        parent.AddChild(apron);

        var rng = new RandomNumberGenerator { Seed = 90210 };

        // Backdrop ring by biome: forest keeps its tree ring
        // (visual only; on the Mission front it rings the rim).
        // The arena ring starts past the movement bound (115u)
        // plus a margin (owner, 2026-11-15): at 98u the inner
        // ring trees and rocks stood INSIDE the reachable
        // ground as ghosts - riders walked straight through
        // them while the planted trees stomp and burn.
        // the desert gets bare rock outcrops; the city gets no
        // ring (owner: no procedural buildings outside the map -
        // every city building is a gameplay building inside it).
        if (WorldConfig.Biome == WorldConfig.BiomeKind.City)
        {
            Dbg.Print("PHOTO backdrop: apron only (city - no ring)");
            return;
        }
        if (WorldConfig.Biome == WorldConfig.BiomeKind.Desert)
        {
            // The desert rock ring is gone with the rest of
            // the rocks (owner, 2026-11-20: removed
            // completely) - the apron stands bare.
            Dbg.Print("PHOTO backdrop: apron only (desert - rock ring removed)");
            return;
        }
        for (int i = 0; i < BackdropTreeCount; i++)
        {
            float a = rng.RandfRange(0f, Mathf.Tau);
            float half = Main.TerrainSize / 2f;
            float dist = mission ? rng.RandfRange(half + RingMissionInnerMargin, half + RingMissionOuterMargin) : rng.RandfRange(RingArenaDistMin, RingArenaDistMax);
            var pos = new Vector3(Mathf.Cos(a) * dist, 0f, Mathf.Sin(a) * dist);
            float cl = mission ? half - RingClampInset : RingArenaClamp;
            pos.Y = Main.HeightAt(Mathf.Clamp(pos.X, -cl, cl), Mathf.Clamp(pos.Z, -cl, cl)) - RingTreeSink;
            var holder = new Node3D { Position = pos };
            AddTreeModel(holder, rng.RandiRange(0, 2), rng);
            holder.Scale = Vector3.One * rng.RandfRange(BackdropTreeScaleMin, BackdropTreeScaleMax);
            parent.AddChild(holder);
        }
        Dbg.Print($"PHOTO backdrop: apron + {BackdropTreeCount} ring trees");
    }

    // ---- Renderer / atmosphere tuning for the probe ----

    public static void Tune(Environment env, DirectionalLight3D sun)
    {
        env.SdfgiEnabled = true;
        // Optimization #2 (owner): fewer, cheaper cascades + probe
        // occlusion; convergence cost is spread thinner, which
        // flattens frame times. Quality at arena scale is unchanged
        // in practice - 4 cascades cover 170u comfortably.
        env.SdfgiCascades = WorldConfig.IsMission ? 6 : 4;
        env.SdfgiUseOcclusion = true;
        env.SdfgiMinCellSize = SdfgiCellSize;
        env.SsaoEnabled = true;
        env.VolumetricFogEnabled = true;
        // Fog by biome: warm haze in the desert, a duskier
        // teal-grey in the city (lit windows and signs carry),
        // the standing blue-grey in the forest.
        var (fogC, fogD, volD, sunE) = WorldConfig.Biome switch
        {
            WorldConfig.BiomeKind.Desert => (DesertFogColor, WorldConfig.IsMission ? 0.00050f : 0.0019f, WorldConfig.IsMission ? 0.0009f : 0.0026f, 1.55f),
            WorldConfig.BiomeKind.City => (CityFogColor, WorldConfig.IsMission ? 0.00055f : 0.0021f, WorldConfig.IsMission ? 0.0010f : 0.0028f, 1.15f),
            _ => (ForestFogColor, WorldConfig.IsMission ? 0.00042f : 0.0016f, WorldConfig.IsMission ? 0.00075f : 0.0022f, 1.45f)
        };
        env.VolumetricFogDensity = volD;
        env.FogDensity = fogD; // distance haze; the Mission front hazes out near ~1200u
        env.GlowEnabled = true;
        env.GlowIntensity = GlowIntensityValue;
        env.GlowStrength = GlowStrengthValue;
        // Look pass (owner: fog + bright light overilluminated the
        // scene). The wash came from stacked ambient terms - sky
        // ambient 0.42 + full-strength SDFGI bounce + bright fog -
        // so the fix is to let the SUN carry the image and deepen
        // everything else: lower exposure, weaker ambient and
        // bounce, darker fog, and a gentle filmic contrast/
        // saturation adjustment on top. The haze itself stays.
        env.TonemapExposure = TonemapExposureValue;
        env.AmbientLightEnergy = AmbientEnergyValue; // raised 0.30 -> 0.50 (owner, 2026-11-11: shadows must stay readable)
        env.SdfgiEnergy = SdfgiEnergyValue;
        env.FogLightColor = fogC;
        env.VolumetricFogAlbedo = fogC;
        env.AdjustmentEnabled = true;
        env.AdjustmentBrightness = 1.0f;
        env.AdjustmentContrast = AdjustmentContrastValue;
        env.AdjustmentSaturation = AdjustmentSaturationValue;
        sun.LightEnergy = sunE;
        sun.ShadowOpacity = SunShadowOpacity; // real shade depth (owner round 6: rider's shadow was too bright)
        sun.DirectionalShadowMaxDistance = WorldConfig.IsMission ? MissionShadowDistance : ArenaShadowDistance;
        Dbg.Print("PHOTO env tuned: SDFGI(4 cascades) + SSAO + volumetric fog + glow; darker contrast look");
    }
}
