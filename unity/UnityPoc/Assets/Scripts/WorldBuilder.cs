using UnityEngine;

// Builds the whole slice in code at runtime: procedural terrain mesh, primitive
// trees/buildings, one static NPC. Same height function as the other engines.
public class WorldBuilder : MonoBehaviour
{
    public static float HeightAt(float x, float z)
    {
        return 2.6f * Mathf.Sin(0.07f * x) * Mathf.Cos(0.055f * z)
             + 1.4f * Mathf.Sin(0.21f * x + 1.7f) * Mathf.Sin(0.17f * z + 0.6f)
             + 0.5f * Mathf.Sin(0.45f * x) * Mathf.Cos(0.40f * z);
    }

    // Code-first bootstrap: the project runs from ANY scene (even an empty one).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (FindFirstObjectByType<WorldBuilder>() == null)
            new GameObject("Bootstrap").AddComponent<WorldBuilder>();
    }

    private static Material Mat(Color c)
    {
        return new Material(Shader.Find("Standard")) { color = c };
    }

    private void Awake()
    {
        // Default scene light + camera if the scene has none (scene setup only).
        if (FindFirstObjectByType<Light>() == null)
        {
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        BuildTerrain();
        var trunkMat = Mat(new Color(0.40f, 0.26f, 0.15f));
        var canopyMat = Mat(new Color(0.16f, 0.42f, 0.18f));
        var buildingMat = Mat(new Color(0.55f, 0.56f, 0.60f));

        foreach (var (tx, tz) in new[] { (-12f, -8f), (14f, -18f), (6f, 16f) })
        {
            Prop(PrimitiveType.Cylinder, trunkMat, tx, tz, 1.1f, new Vector3(0.45f, 1.1f, 0.45f));
            Prop(PrimitiveType.Sphere, canopyMat, tx, tz, 3.1f, new Vector3(3f, 2.6f, 3f));
        }
        Prop(PrimitiveType.Cube, buildingMat, -20, 14, 2.2f, new Vector3(7, 5, 6)); // centerY = h/2 - 0.3 sink
        Prop(PrimitiveType.Cube, buildingMat, 22, 8, 3.2f, new Vector3(6, 7, 6));

        // Static NPC enemy (no AI, no interaction).
        var npc = Prop(PrimitiveType.Capsule, Mat(new Color(0.85f, 0.15f, 0.15f)),
                       3, -14, 0.9f, new Vector3(0.7f, 0.9f, 0.7f));
        npc.name = "NPC";

        // Player: capsule + controller + camera rig.
        var player = Prop(PrimitiveType.Capsule, Mat(new Color(0.15f, 0.35f, 0.90f)),
                          0, 8, 0.9f, new Vector3(0.7f, 0.9f, 0.7f));
        player.name = "Player";
        player.AddComponent<PlayerController>();

        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        camGo.AddComponent<Camera>();
        camGo.AddComponent<CameraRig>().Target = player.transform;
    }

    private static GameObject Prop(PrimitiveType type, Material mat, float x, float z, float centerY, Vector3 scale)
    {
        var go = GameObject.CreatePrimitive(type);
        go.GetComponent<Renderer>().material = mat;
        go.transform.position = new Vector3(x, HeightAt(x, z) + centerY, z);
        go.transform.localScale = scale;
        return go;
    }

    private void BuildTerrain()
    {
        const int n = 64;
        const float half = 50f;
        var verts = new Vector3[(n + 1) * (n + 1)];
        var normals = new Vector3[verts.Length];
        var tris = new int[n * n * 6];
        for (int j = 0; j <= n; j++)
        for (int i = 0; i <= n; i++)
        {
            float x = -half + 100f * i / n, z = -half + 100f * j / n;
            float e = 1.0f;
            float dx = (HeightAt(x + e, z) - HeightAt(x - e, z)) / (2 * e);
            float dz = (HeightAt(x, z + e) - HeightAt(x, z - e)) / (2 * e);
            int k = i + j * (n + 1);
            verts[k] = new Vector3(x, HeightAt(x, z), z);
            normals[k] = new Vector3(-dx, 1, -dz).normalized;
        }
        int t = 0;
        for (int j = 0; j < n; j++)
        for (int i = 0; i < n; i++)
        {
            int a = i + j * (n + 1), b = a + 1, c = a + n + 1, d = c + 1;
            // Unity is left-handed, Y-up: (a, c, b) / (b, c, d) faces up.
            tris[t++] = a; tris[t++] = c; tris[t++] = b;
            tris[t++] = b; tris[t++] = c; tris[t++] = d;
        }
        var mesh = new Mesh { vertices = verts, normals = normals, triangles = tris };
        var go = new GameObject("Terrain");
        go.AddComponent<MeshFilter>().mesh = mesh;
        go.AddComponent<MeshRenderer>().material = Mat(new Color(0.32f, 0.55f, 0.28f));
    }
}
