using Godot;

public partial class Main : Node3D
{
    public const int TerrainSize = 100; // world units, square
    public const int TerrainSegs = 100; // grid resolution

    // Procedural terrain height: a few sine waves -> hills and valleys.
    public static float HeightAt(float x, float z)
    {
        return 2.6f * Mathf.Sin(x * 0.07f) * Mathf.Cos(z * 0.055f)
             + 1.4f * Mathf.Sin(x * 0.21f + 1.7f) * Mathf.Sin(z * 0.17f + 0.6f)
             + 0.5f * Mathf.Sin(x * 0.45f) * Mathf.Cos(z * 0.40f);
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
        // Visual mesh from the same height function.
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetMaterial(new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.5f, 0.22f) });
        float half = TerrainSize / 2f;
        float step = (float)TerrainSize / TerrainSegs;
        for (int j = 0; j < TerrainSegs; j++)
        {
            for (int i = 0; i < TerrainSegs; i++)
            {
                float x0 = -half + i * step, x1 = x0 + step;
                float z0 = -half + j * step, z1 = z0 + step;
                var a = new Vector3(x0, HeightAt(x0, z0), z0);
                var b = new Vector3(x1, HeightAt(x1, z0), z0);
                var c = new Vector3(x0, HeightAt(x0, z1), z1);
                var d = new Vector3(x1, HeightAt(x1, z1), z1);
                st.AddVertex(a); st.AddVertex(c); st.AddVertex(b);
                st.AddVertex(b); st.AddVertex(c); st.AddVertex(d);
            }
        }
        st.GenerateNormals();
        var meshInstance = new MeshInstance3D();
        meshInstance.Mesh = st.Commit();
        AddChild(meshInstance);

        // Physics collision: heightmap shape sampled from the same function.
        int n = TerrainSegs + 1;
        var data = new float[n * n];
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
                data[j * n + i] = HeightAt(-half + i * step, -half + j * step);
        var shape = new HeightMapShape3D { MapWidth = n, MapDepth = n, MapData = data };
        var body = new StaticBody3D();
        var col = new CollisionShape3D { Shape = shape };
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
