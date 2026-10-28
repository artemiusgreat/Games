using Godot;

public partial class Main : Node3D
{
    public const int TerrainSize = 170; // world units, square
    public const int TerrainSegs = 256; // mesh segments per side == heightfield cells

    // ---- Heightfield terrain (roadmap step 4) ----
    // The world is ONE heightfield grid, generated once from a seeded
    // value-noise stack. HeightAt is a bilinear sample of that grid, so
    // the terrain mesh, the gait/IK, the hit model, collisions and
    // debris settling all read the same ground by construction.
    // Layout: multi-octave rolling hills everywhere; a craggy ridge in
    // the NW; a shallow valley in the SE; mountain ring at the edges.
    // Amplitude is modulated by radius so the central arena (r < ~36)
    // keeps gentle duel-playable slopes while the rim rises properly.
    private const int GridN = TerrainSegs + 1; // 257 sample points per side
    private const uint NoiseSeed = 20261027u;
    private static readonly float[] Grid = BuildGrid();

    private static float LatticeHash(int ix, int iz)
    {
        uint h = (uint)(ix * 374761393) + (uint)(iz * 668265263) + unchecked(NoiseSeed * 2246822519u);
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return (h & 0x00FFFFFF) / 16777215f; // [0,1]
    }

    // Smooth value noise, output [0,1].
    private static float ValueNoise(float x, float z)
    {
        int ix = Mathf.FloorToInt(x), iz = Mathf.FloorToInt(z);
        float fx = x - ix, fz = z - iz;
        float sx = fx * fx * (3f - 2f * fx), sz = fz * fz * (3f - 2f * fz);
        float a = LatticeHash(ix, iz), b = LatticeHash(ix + 1, iz);
        float c = LatticeHash(ix, iz + 1), d = LatticeHash(ix + 1, iz + 1);
        return Mathf.Lerp(Mathf.Lerp(a, b, sx), Mathf.Lerp(c, d, sx), sz);
    }

    // Fractal value noise, output ~[-1,1].
    private static float Fbm(float x, float z, int octaves)
    {
        float sum = 0f, amp = 1f, tot = 0f;
        for (int i = 0; i < octaves; i++)
        {
            sum += amp * (ValueNoise(x, z) * 2f - 1f);
            tot += amp;
            amp *= 0.5f;
            x = x * 2.03f + 19.19f;
            z = z * 2.03f - 7.31f;
        }
        return sum / tot;
    }

    private static float DistToSegment(float px, float pz, float ax, float az, float bx, float bz)
    {
        float dx = bx - ax, dz = bz - az;
        float len2 = dx * dx + dz * dz;
        float t = len2 > 1e-6f ? Mathf.Clamp(((px - ax) * dx + (pz - az) * dz) / len2, 0f, 1f) : 0f;
        float ex = ax + dx * t - px, ez = az + dz * t - pz;
        return Mathf.Sqrt(ex * ex + ez * ez);
    }

    private static float Smooth(float a, float b, float x)
    {
        float t = Mathf.Clamp((x - a) / (b - a), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static float RawHeight(float x, float z)
    {
        float r = Mathf.Sqrt(x * x + z * z);
        // Rolling hills, the base relief everywhere.
        float hills = Fbm(x / 58f, z / 58f, 5) * 6.5f;
        // Craggy ridge line through the NW quadrant.
        float dRidge = DistToSegment(x, z, -88f, -28f, -24f, -88f);
        float crag = 1f - Mathf.Abs(Fbm(x / 21f + 40f, z / 21f - 17f, 3));
        float ridge = Mathf.Exp(-(dRidge * dRidge) / (2f * 15f * 15f)) * (5f + 7f * crag);
        // Shallow valley through the SE quadrant.
        float dVal = DistToSegment(x, z, 88f, 34f, 28f, -88f);
        float valley = -3.6f * Mathf.Exp(-(dVal * dVal) / (2f * 13f * 13f));
        // Mountain ring bounding the map (square falloff follows the rim).
        float sq = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
        float m = Smooth(56f, 84f, sq);
        float mtn = m * (9f + 7f * (Fbm(x / 37f - 9f, z / 37f + 23f, 4) * 0.5f + 0.5f));
        float full = hills + ridge + valley + mtn;
        // Arena modulation: 22% amplitude at the center, full by r=62.
        float t = Smooth(36f, 62f, r);
        return full * (0.22f + 0.78f * t);
    }

    private static float[] BuildGrid()
    {
        var g = new float[GridN * GridN];
        float half = TerrainSize / 2f;
        float step = (float)TerrainSize / TerrainSegs;
        for (int j = 0; j < GridN; j++)
            for (int i = 0; i < GridN; i++)
                g[j * GridN + i] = RawHeight(-half + i * step, -half + j * step);
        return g;
    }

    // Terrain height: bilinear sample of the heightfield grid (constant
    // outside the world, clamped to the rim). Called constantly by the
    // gait, the hit model and the debris sim — keep it allocation-free.
    public static float HeightAt(float x, float z)
    {
        float half = TerrainSize / 2f;
        float scale = TerrainSegs / (float)TerrainSize;
        float gx = Mathf.Clamp((x + half) * scale, 0f, TerrainSegs - 0.001f);
        float gz = Mathf.Clamp((z + half) * scale, 0f, TerrainSegs - 0.001f);
        int ix = (int)gx, iz = (int)gz;
        float fx = gx - ix, fz = gz - iz;
        int i0 = iz * GridN + ix;
        float a = Grid[i0], b = Grid[i0 + 1], c = Grid[i0 + GridN], d = Grid[i0 + GridN + 1];
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fz);
    }

    // Verification hook: the stored analytic height at a grid node.
    public static float GridHeightAt(int i, int j) => Grid[j * GridN + i];

    private static Vector3 GridNormal(int i, int j, float step)
    {
        int il = Mathf.Max(i - 1, 0), ir = Mathf.Min(i + 1, GridN - 1);
        int jd = Mathf.Max(j - 1, 0), ju = Mathf.Min(j + 1, GridN - 1);
        float dhdx = (Grid[j * GridN + ir] - Grid[j * GridN + il]) / ((ir - il) * step);
        float dhdz = (Grid[ju * GridN + i] - Grid[jd * GridN + i]) / ((ju - jd) * step);
        return new Vector3(-dhdx, 1f, -dhdz).Normalized();
    }

    // Roads (terrain-detail ask): two strips crossing at (-14, 6) — an
    // east-west road along z = 6 and a north-south road along x = -14.
    // Pure coloring: HeightAt is untouched, so gait, hit tests and
    // collisions never notice them. Distance is to the nearer centerline.
    public static float RoadDistance(float x, float z)
    {
        return Mathf.Min(Mathf.Abs(z - 6f), Mathf.Abs(x + 14f));
    }

    // Ground color without any texture image: grass tones varied by two
    // noise fields (patchiness + fine breakup), dirt on mid slopes, rock
    // on steep faces, pale summits up high.
    public static Color TerrainColor(float x, float z, float h, Vector3 n)
    {
        float slope = 1f - n.Y; // 0 flat, grows with gradient
        float patch = Fbm(x / 23f - 47f, z / 23f + 5f, 3) * 0.5f + 0.5f;
        float grain = Fbm(x / 6.5f + 31f, z / 6.5f - 11f, 3) * 0.5f + 0.5f;
        var col = new Color(0.185f, 0.36f, 0.145f).Lerp(new Color(0.36f, 0.485f, 0.21f), patch);
        col = col.Lerp(new Color(0.44f, 0.43f, 0.235f), Mathf.Clamp((patch - 0.60f) * 1.5f, 0f, 0.5f)); // dry patches
        float l = 0.88f + 0.24f * grain;
        col = new Color(col.R * l, col.G * l, col.B * l);
        float dirtW = Smooth(0.045f, 0.12f, slope + (grain - 0.5f) * 0.05f);
        col = col.Lerp(new Color(0.40f, 0.315f, 0.205f), dirtW * 0.8f);
        float rockW = Smooth(0.13f, 0.28f, slope + (grain - 0.5f) * 0.06f);
        var rock = new Color(0.395f, 0.395f, 0.415f);
        float rl = 0.82f + 0.36f * grain;
        col = col.Lerp(new Color(rock.R * rl, rock.G * rl, rock.B * rl), rockW);
        float peakW = Smooth(13.5f, 19f, h + (grain - 0.5f) * 3f);
        col = col.Lerp(new Color(0.72f, 0.71f, 0.685f), peakW * 0.85f);
        // Asphalt on the roads, blended over a soft shoulder.
        float roadD = RoadDistance(x, z);
        if (roadD < 3.1f)
        {
            float rw = 1f - Smooth(2.25f, 3.1f, roadD);
            float al = 0.85f + 0.3f * grain;
            var asphalt = new Color(0.16f * al, 0.16f * al, 0.175f * al);
            if (roadD > 1.85f && roadD < 2.25f) asphalt = asphalt.Lerp(new Color(0.5f, 0.48f, 0.4f), 0.35f); // worn edge line
            col = col.Lerp(asphalt, rw * 0.94f);
        }
        return col;
    }

    // Shared terrain mesh: an indexed grid whose vertices sit exactly on
    // heightfield nodes, so the rendered surface and HeightAt agree by
    // construction. One material, vertex-color albedo, no textures.
    public static Mesh CreateTerrainMesh()
    {
        int n = GridN;
        float half = TerrainSize / 2f;
        float step = (float)TerrainSize / TerrainSegs;
        var verts = new Vector3[n * n];
        var normals = new Vector3[n * n];
        var colors = new Color[n * n];
        for (int j = 0; j < n; j++)
        for (int i = 0; i < n; i++)
        {
            float x = -half + i * step, z = -half + j * step;
            float h = Grid[j * n + i];
            int k = j * n + i;
            verts[k] = new Vector3(x, h, z);
            var nrm = GridNormal(i, j, step);
            normals[k] = nrm;
            colors[k] = TerrainColor(x, z, h, nrm);
        }
        var idx = new int[TerrainSegs * TerrainSegs * 6];
        int t = 0;
        for (int j = 0; j < TerrainSegs; j++)
        for (int i = 0; i < TerrainSegs; i++)
        {
            int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
            idx[t++] = a; idx[t++] = b; idx[t++] = c;
            idx[t++] = b; idx[t++] = d; idx[t++] = c;
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.Index] = idx;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(0, new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            Roughness = 0.95f
        });
        return mesh;
    }

    public override void _Ready()
    {
        // Engine-default style sun + sky so the scene is visible when run windowed.
        var sun = new DirectionalLight3D();
        sun.RotationDegrees = new Vector3(-50f, -30f, 0f);
        AddChild(sun);
        var we = new WorldEnvironment();
        we.Environment = new Environment
        {
            BackgroundMode = Environment.BGMode.Color,
            BackgroundColor = new Color(0.35f, 0.5f, 0.7f)
        };
        AddChild(we);

        BuildTerrain();

        AddTree(new Vector3(-12f, 0f, -8f));
        AddTree(new Vector3(14f, 0f, -18f));
        AddTree(new Vector3(6f, 0f, 16f));

        AddBuilding(new Vector3(-20f, 0f, 14f), new Vector3(7f, 5f, 6f));
        AddBuilding(new Vector3(22f, 0f, 8f), new Vector3(6f, 7f, 6f));

        AddNpc(new Vector3(3f, 0f, -14f));

        var player = new Player();
        player.Position = new Vector3(0f, HeightAt(0f, 8f) + 1.5f, 8f);
        AddChild(player);

        // Third-person camera rig: follows the player, fixed yaw, behind + above.
        var rig = new CameraRig();
        rig.Target = player;
        AddChild(rig);
    }

    private void BuildTerrain()
    {
        // Visual mesh: the shared heightfield mesh (vertices on grid nodes).
        AddChild(new MeshInstance3D { Mesh = CreateTerrainMesh() });

        // Physics collision: heightmap shape sampled from the same function.
        float half = TerrainSize / 2f;
        float step = (float)TerrainSize / TerrainSegs;
        int n = TerrainSegs + 1;
        var data = new float[n * n];
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
                data[j * n + i] = HeightAt(-half + i * step, -half + j * step);
        var shape = new HeightMapShape3D { MapWidth = n, MapDepth = n, MapData = data };
        var body = new StaticBody3D();
        // The shape spans (n-1) unit cells natively; scale it onto the world.
        var col = new CollisionShape3D { Shape = shape, Scale = Vector3.One * step };
        body.AddChild(col);
        AddChild(body);
    }

    private static MeshInstance3D Box(Vector3 size, Color color, Vector3 pos)
    {
        var mi = new MeshInstance3D();
        mi.Mesh = new BoxMesh { Size = size };
        mi.MaterialOverride = new StandardMaterial3D { AlbedoColor = color };
        mi.Position = pos;
        return mi;
    }

    private void AddTree(Vector3 pos)
    {
        pos.Y = HeightAt(pos.X, pos.Z);
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

    private void AddBuilding(Vector3 pos, Vector3 size)
    {
        pos.Y = HeightAt(pos.X, pos.Z) + size.Y / 2f - 0.3f; // sink slightly into slope
        AddChild(Box(size, new Color(0.55f, 0.55f, 0.58f), pos));
    }

    private void AddNpc(Vector3 pos)
    {
        pos.Y = HeightAt(pos.X, pos.Z) + 0.9f;
        var npc = new MeshInstance3D();
        npc.Mesh = new CapsuleMesh { Radius = 0.4f, Height = 1.8f };
        npc.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.15f, 0.15f) };
        npc.Position = pos;
        AddChild(npc);
    }
}
