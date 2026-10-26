using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;

// PROBE 3 (Round 2): scale test, all code-built, gated by player arg --probe3.
//   * 1000x1000 terrain: same height-function family as round 1, spatial
//     frequency scaled down 10x and amplitude up 6x (stated method).
//   * Ground: Standard-shader PBR material asset (Assets/Resources/ScaleGround)
//     whose albedo is a procedural texture generated in code at runtime.
//   * ONE directional light (the round-1 sun, reused) with shadows ON.
//   * 300 scattered props via Graphics.DrawMeshInstanced: 200 trees
//     (instanced trunk mesh + instanced canopy mesh) + 100 rocks.
//   * Player capsule height-sampling the terrain (same method as round 1).
// The world sits at +2000 x/z so the round-1 slice is untouched and
// frustum-culled out of this test's view.
// Logs: PROBE3 stats (triangles, instances) + frame times; shot -> scale.png.
public class ScaleWorld : MonoBehaviour
{
    private static readonly Vector3 C = new Vector3(2000f, 0f, 2000f); // world center offset
    private const float Size = 1000f;

    private Mesh _trunkMesh, _canopyMesh, _rockMesh;
    private Material _trunkMat, _canopyMat, _rockMat;
    private Matrix4x4[] _trunks, _canopies, _rocks;
    private Transform _player, _camT;
    private float _t, _frameAcc;
    private int _frames, _statFrames;
    private bool _shot;
    private string _shotDir = ".";
    private float _px; // player local x

    public static float ScaleHeight(float lx, float lz)
    {
        return WorldBuilder.HeightAt(lx * 0.10f, lz * 0.10f) * 6f;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (ShotUtil.HasArg("--probe3"))
            new GameObject("ScaleWorld").AddComponent<ScaleWorld>();
    }

    private IEnumerator Start()
    {
        _shotDir = ShotUtil.ArgValue("--shotdir") ?? ".";
        yield return new WaitForSeconds(0.4f); // round-1 world (and its sun) exist

        // The ONE directional light: reuse round-1's sun, turn shadows ON.
        var sun = FindFirstObjectByType<Light>();
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.9f;
        sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);

        BuildTerrain();
        BuildProps();
        BuildPlayerAndCamera();

        Debug.Log($"PROBE3 stats: terrainTris={_terrainTris} propTris={_propTris} " +
                  $"totalTris={_terrainTris + _propTris} instances={_trunks.Length + _rocks.Length} " +
                  $"(trees={_trunks.Length} rocks={_rocks.Length}) instancedDrawCalls=3 (+terrain 1 +player 1, x2 with shadow pass)");
    }

    private long _terrainTris, _propTris;

    private void BuildTerrain()
    {
        const int n = 256;
        var verts = new Vector3[(n + 1) * (n + 1)];
        var uvs = new Vector2[verts.Length];
        var normals = new Vector3[verts.Length];
        var tris = new int[n * n * 6];
        for (int j = 0; j <= n; j++)
        for (int i = 0; i <= n; i++)
        {
            float lx = -Size / 2 + Size * i / n, lz = -Size / 2 + Size * j / n;
            float e = 1.5f;
            float dx = (ScaleHeight(lx + e, lz) - ScaleHeight(lx - e, lz)) / (2 * e);
            float dz = (ScaleHeight(lx, lz + e) - ScaleHeight(lx, lz - e)) / (2 * e);
            int k = i + j * (n + 1);
            verts[k] = new Vector3(C.x + lx, ScaleHeight(lx, lz), C.z + lz);
            normals[k] = new Vector3(-dx, 1, -dz).normalized;
            uvs[k] = new Vector2(lx / 8f, lz / 8f); // texture tile = 8 world units
        }
        int t = 0;
        for (int j = 0; j < n; j++)
        for (int i = 0; i < n; i++)
        {
            int a = i + j * (n + 1), b = a + 1, c = a + n + 1, d = c + 1;
            tris[t++] = a; tris[t++] = c; tris[t++] = b;   // round-1 up-facing winding
            tris[t++] = b; tris[t++] = c; tris[t++] = d;
        }
        var mesh = new Mesh
        {
            indexFormat = IndexFormat.UInt32,
            vertices = verts, normals = normals, uv = uvs, triangles = tris,
        };
        _terrainTris = tris.Length / 3;

        // PBR ground: Standard material ASSET (referencing it from Resources is
        // what keeps Standard + its shadow variants from being stripped --
        // the round-1 trap). Albedo is procedural, generated here in code.
        var mat = Resources.Load<Material>("ScaleGround");
        if (mat == null) { Debug.LogError("PROBE3 FAIL: ScaleGround material missing"); Application.Quit(2); return; }
        mat.mainTexture = MakeGroundTexture(512);
        mat.SetFloat("_Roughness", 0.95f);
        mat.SetFloat("_Metallic", 0f);
        Debug.Log($"PROBE3 ground shader={mat.shader.name}");

        var go = new GameObject("ScaleTerrain");
        go.AddComponent<MeshFilter>().mesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.material = mat;
        mr.receiveShadow = true;
        mr.shadowCastingMode = ShadowCastingMode.Off;
    }

    // Deterministic multi-octave value noise -> grass/dirt albedo.
    private static Texture2D MakeGroundTexture(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGB24, true)
        { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
        var px = new Color[size * size];
        var grass = new Color(0.30f, 0.52f, 0.24f);
        var dry = new Color(0.55f, 0.50f, 0.30f);
        var dirt = new Color(0.42f, 0.33f, 0.22f);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float n = 0f, amp = 0.55f, freq = 1f / 64f;
            for (int o = 0; o < 4; o++)
            {
                n += amp * Noise(x * freq, y * freq);
                amp *= 0.5f; freq *= 2f;
            }
            float m = Mathf.Clamp01((x % 8) / 8f * 0.05f + (y % 8) / 8f * 0.05f); // faint micro-grid variation
            var c = n < 0.48f ? Color.Lerp(grass, dry, n / 0.48f) : Color.Lerp(dry, dirt, (n - 0.48f) / 0.52f);
            px[x + y * size] = c * (0.92f + m);
        }
        tex.SetPixels(px);
        tex.Apply(true);
        return tex;
    }

    private static float Noise(float x, float y)
    {
        int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
        float xf = x - xi, yf = y - yi;
        System.Func<float, float, float> s = (a, b) => Mathf.Sin(a * 127.1f + b * 311.7f) * 43758.5453f;
        float h00 = s(xi, yi) - Mathf.Floor(s(xi, yi));
        float h10 = s(xi + 1, yi) - Mathf.Floor(s(xi + 1, yi));
        float h01 = s(xi, yi + 1) - Mathf.Floor(s(xi, yi + 1));
        float h11 = s(xi + 1, yi + 1) - Mathf.Floor(s(xi + 1, yi + 1));
        float u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf);
        return Mathf.Lerp(Mathf.Lerp(h00, h10, u), Mathf.Lerp(h01, h11, u), v);
    }

    private static Material InstMat(Color c)
    {
        var sh = Shader.Find("Standard") ?? Shader.Find("Unlit/Color");
        var m = new Material(sh) { color = c };
        m.enableInstancing = true;
        m.SetFloat("_Roughness", 0.9f);
        return m;
    }

    private static Mesh PrimitiveMesh(PrimitiveType t)
    {
        var go = GameObject.CreatePrimitive(t);
        var mesh = go.GetComponent<MeshFilter>().sharedMesh;
        Destroy(go);
        return mesh;
    }

    private void BuildProps()
    {
        _trunkMesh = PrimitiveMesh(PrimitiveType.Cylinder);
        _canopyMesh = PrimitiveMesh(PrimitiveType.Sphere);
        _rockMesh = PrimitiveMesh(PrimitiveType.Cube);
        _trunkMat = InstMat(new Color(0.40f, 0.26f, 0.15f));
        _canopyMat = InstMat(new Color(0.15f, 0.40f, 0.17f));
        _rockMat = InstMat(new Color(0.52f, 0.52f, 0.55f));

        var rng = new System.Random(12345);
        const int trees = 200, rocks = 100;
        _trunks = new Matrix4x4[trees];
        _canopies = new Matrix4x4[trees];
        _rocks = new Matrix4x4[rocks];
        int placed = 0;
        while (placed < trees)
        {
            float lx = (float)(rng.NextDouble() * 840 - 420), lz = (float)(rng.NextDouble() * 840 - 420);
            if (Mathf.Abs(lz) < 7f && lx > -120f && lx < 120f) continue; // keep the walk corridor clear
            float y = ScaleHeight(lx, lz);
            float s = 0.8f + (float)rng.NextDouble() * 0.7f;
            var rot = Quaternion.Euler(0, (float)rng.NextDouble() * 360f, 0);
            var pos = new Vector3(C.x + lx, y, C.z + lz);
            _trunks[placed] = Matrix4x4.TRS(pos + Vector3.up * (1.6f * s - 0.2f), rot, new Vector3(0.55f * s, 1.6f * s, 0.55f * s));
            _canopies[placed] = Matrix4x4.TRS(pos + Vector3.up * (3.6f * s), rot, new Vector3(3.4f * s, 3.0f * s, 3.4f * s));
            placed++;
        }
        for (int i = 0; i < rocks; i++)
        {
            float lx = (float)(rng.NextDouble() * 840 - 420), lz = (float)(rng.NextDouble() * 840 - 420);
            if (Mathf.Abs(lz) < 6f && lx > -120f && lx < 120f) { lz += 14f; }
            float y = ScaleHeight(lx, lz);
            float s = 0.7f + (float)rng.NextDouble() * 1.8f;
            var rot = Quaternion.Euler((float)rng.NextDouble() * 40f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 40f);
            _rocks[i] = Matrix4x4.TRS(new Vector3(C.x + lx, y + 0.2f * s, C.z + lz), rot, new Vector3(s, s * 0.7f, s));
        }
        _propTris = (_trunkMesh.triangles.Length / 3 + _canopyMesh.triangles.Length / 3) * trees
                  + (_rockMesh.triangles.Length / 3) * rocks;
    }

    private void BuildPlayerAndCamera()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = "ScalePlayer";
        var sh = Shader.Find("Standard") ?? Shader.Find("Unlit/Color");
        go.GetComponent<Renderer>().material = new Material(sh) { color = new Color(0.15f, 0.35f, 0.90f) };
        go.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.On;
        go.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
        _player = go.transform;
        _px = -60f;
        _player.position = new Vector3(C.x + _px, ScaleHeight(_px, 0) + 0.9f, C.z);

        var rig = FindFirstObjectByType<CameraRig>();
        if (rig != null) rig.enabled = false;
        var old = GameObject.Find("Player");
        if (old != null) old.SetActive(false);
        var cam = Camera.main ?? FindFirstObjectByType<Camera>();
        cam.farClipPlane = 1500f;
        _camT = cam.transform;
    }

    private void Update()
    {
        if (_player == null) return;
        _t += Time.deltaTime;
        if (_t > 1f) { _frameAcc += Time.deltaTime; _statFrames++; }
        _frames++;

        // Auto-walk +X on the terrain (height sampling, round-1 method).
        _px += 4.0f * Time.deltaTime;
        _player.position = new Vector3(C.x + _px, ScaleHeight(_px, 0) + 0.9f, C.z);
        _player.rotation = Quaternion.Euler(0, 90, 0);

        // Instanced props: 3 draw calls for all 300 props (+shadow pass).
        Graphics.DrawMeshInstanced(_trunkMesh, 0, _trunkMat, _trunks, _trunks.Length, null, ShadowCastingMode.On, true);
        Graphics.DrawMeshInstanced(_canopyMesh, 0, _canopyMat, _canopies, _canopies.Length, null, ShadowCastingMode.On, true);
        Graphics.DrawMeshInstanced(_rockMesh, 0, _rockMat, _rocks, _rocks.Length, null, ShadowCastingMode.On, true);

        // Camera: third-person follow behind the walker.
        var cam = Camera.main;
        if (cam != null)
        {
            cam.transform.position = _player.position + new Vector3(-8.5f, 3.4f, -5.5f);
            cam.transform.LookAt(_player.position + Vector3.up * 1.2f + Vector3.right * 6f);
        }

        if (!_shot && _t >= 3.0f)
        {
            ShotUtil.Save(cam, $"{_shotDir}/scale.png", 1024, 640);
            _shot = true;
        }
        if (_t >= 9f)
        {
            float avgMs = _statFrames > 0 ? _frameAcc / _statFrames * 1000f : 0;
            Debug.Log($"PROBE3 frametime avgMs={avgMs:F2} fps={1000f / Mathf.Max(0.01f, avgMs):F1} frames={_frames} (llvmpipe, relative-only)");
            Debug.Log("PROBE3 DONE");
            Application.Quit(0);
        }
    }
}
