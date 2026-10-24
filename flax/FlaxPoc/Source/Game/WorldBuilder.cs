using System;
using System.Collections.Generic;
using FlaxEngine;

// Flax units are CENTIMETERS. The slice is authored in meters (same numbers as
// the Godot/Stride versions) and scaled by U at placement time.
// Terrain following = height sampling (same call as Stride: Flax has a
// CharacterController, but it needs collider surfaces; sampling is less code).
public class WorldBuilder : Script
{
    private const float U = 100f; // cm per meter

    private Material _baseMaterial;
    private Actor _player;
    private Camera _camera;
    private float _yaw;       // degrees
    private float _vy;        // m/s
    private bool _walkTest;
    private float _testTime;
    private Vector3 _startPos; // meters

    private static float HeightAt(float x, float z)
    {
        return 2.6f * Mathf.Sin(0.07f * x) * Mathf.Cos(0.055f * z)
             + 1.4f * Mathf.Sin(0.21f * x + 1.7f) * Mathf.Sin(0.17f * z + 0.6f)
             + 0.5f * Mathf.Sin(0.45f * x) * Mathf.Cos(0.40f * z);
    }

    // ---- materials: one parameterized base material, tinted per instance ----
    private MaterialInstance Tint(Color c)
    {
        var inst = Content.CreateVirtualAsset<MaterialInstance>();
        inst.BaseMaterial = _baseMaterial;
        if (inst.Parameters != null)
        {
            foreach (var p in inst.Parameters)
            {
                if (p.Name == "Color" || p.Name == "Base Color" || p.Name == "Albedo")
                    p.Value = c;
            }
        }
        return inst;
    }

    private StaticModel AddMesh(Model model, MaterialBase material, Vector3 posMeters, Vector3 scaleMeters)
    {
        var sm = new StaticModel();
        sm.Parent = Actor;
        sm.Model = model;
        if (sm.Entries != null && sm.Entries.Length > 0)
            sm.Entries[0].Material = material;
        sm.Position = posMeters * U;
        sm.Scale = scaleMeters;
        return sm;
    }

    private static Model MakeModel(List<Vector3> verts, List<int> tris, List<Vector3> normals)
    {
        var model = Content.CreateVirtualAsset<Model>();
        model.LODs[0].Meshes[0].UpdateMesh(verts.ToArray(), tris.ToArray(), normals.ToArray());
        return model;
    }

    // Unit box spanning -0.5..0.5 on every axis (scale = size in meters).
    private static Model BoxModel()
    {
        var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
        var faces = new[]
        {
            new Vector3(0, 0, 1), new Vector3(0, 0, -1), new Vector3(0, 1, 0),
            new Vector3(0, -1, 0), new Vector3(1, 0, 0), new Vector3(-1, 0, 0),
        };
        foreach (var f in faces)
        {
            var u = Math.Abs(f.Y) > 0.5f ? Vector3.UnitX : Vector3.UnitY;
            var r = Vector3.Cross(u, f);
            int b = v.Count;
            v.Add(f * 0.5f + (r - u) * 0.5f); v.Add(f * 0.5f + (r + u) * 0.5f);
            v.Add(f * 0.5f + (-r + u) * 0.5f); v.Add(f * 0.5f + (-r - u) * 0.5f);
            for (int i = 0; i < 4; i++) n.Add(f);
            t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b); t.Add(b + 2); t.Add(b + 3);
        }
        return MakeModel(v, t, n);
    }

    // Lathe around Y: profile of (radius, y) pairs, y in -0.5..0.5 (unit height).
    private static Model LatheModel((float r, float y)[] profile, int segments = 12)
    {
        var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
        for (int s = 0; s < segments; s++)
        {
            float a0 = s * 360f / segments, a1 = (s + 1) * 360f / segments;
            Vector3 D(float a, float r) => new Vector3(Mathf.Sin(a * Mathf.DegreesToRadians) * r, 0, Mathf.Cos(a * Mathf.DegreesToRadians) * r);
            for (int i = 0; i < profile.Length - 1; i++)
            {
                var (r0, y0) = profile[i]; var (r1, y1) = profile[i + 1];
                int b = v.Count;
                var p00 = D(a0, r0); p00.Y = y0; var p01 = D(a1, r0); p01.Y = y0;
                var p10 = D(a0, r1); p10.Y = y1; var p11 = D(a1, r1); p11.Y = y1;
                v.Add(p00); v.Add(p01); v.Add(p11); v.Add(p10);
                var nrm = new Vector3(p00.X, 0, p00.Z); nrm.Normalize();
                for (int k = 0; k < 4; k++) n.Add(nrm);
                t.Add(b); t.Add(b + 1); t.Add(b + 2); t.Add(b); t.Add(b + 2); t.Add(b + 3);
            }
        }
        return MakeModel(v, t, n);
    }

    private Model TerrainModel()
    {
        const int n = 64; const float half = 50f;
        var v = new List<Vector3>(); var nr = new List<Vector3>(); var t = new List<int>();
        for (int j = 0; j <= n; j++)
        for (int i = 0; i <= n; i++)
        {
            float x = -half + 100f * i / n, z = -half + 100f * j / n;
            float e = 1.0f;
            float dx = (HeightAt(x + e, z) - HeightAt(x - e, z)) / (2 * e);
            float dz = (HeightAt(x, z + e) - HeightAt(x, z - e)) / (2 * e);
            var nrm = new Vector3(-dx, 1, -dz); nrm.Normalize();
            v.Add(new Vector3(x * U, HeightAt(x, z) * U, z * U));
            nr.Add(nrm);
        }
        for (int j = 0; j < n; j++)
        for (int i = 0; i < n; i++)
        {
            int a = i + j * (n + 1), b = a + 1, c = a + n + 1, d = c + 1;
            // Flax is left-handed; docs specify clockwise triangles.
            t.Add(a); t.Add(b); t.Add(c); t.Add(b); t.Add(d); t.Add(c);
        }
        return MakeModel(v, t, nr);
    }

    private void Prop(Model model, MaterialBase mat, float x, float z, float centerY, Vector3 scale)
    {
        AddMesh(model, mat, new Vector3(x, HeightAt(x, z) + centerY, z), scale);
    }

    /// <inheritdoc />
    public override void OnStart()
    {
        _baseMaterial = Content.Load<Material>("Materials/Base Material.flax");
        var terrainMat = Tint(new Color(0.32f, 0.55f, 0.28f, 1f));
        var trunkMat = Tint(new Color(0.40f, 0.26f, 0.15f, 1f));
        var canopyMat = Tint(new Color(0.16f, 0.42f, 0.18f, 1f));
        var buildingMat = Tint(new Color(0.55f, 0.56f, 0.60f, 1f));
        var npcMat = Tint(new Color(0.85f, 0.15f, 0.15f, 1f));
        var playerMat = Tint(new Color(0.15f, 0.35f, 0.90f, 1f));

        AddMesh(TerrainModel(), terrainMat, Vector3.Zero, Vector3.One);

        var box = BoxModel();
        var trunk = LatheModel(new[] { (0.22f, -0.5f), (0.22f, 0.5f) });
        var cone = LatheModel(new[] { (1.0f, -0.5f), (0.0f, 0.5f) });
        foreach (var (tx, tz) in new[] { (-12f, -8f), (14f, -18f), (6f, 16f) })
        {
            Prop(trunk, trunkMat, tx, tz, 1.1f, new Vector3(1, 2.2f, 1));
            Prop(cone, canopyMat, tx, tz, 3.4f, new Vector3(3, 2.6f, 3));
        }
        Prop(box, buildingMat, -20, 14, 2.2f, new Vector3(7, 5, 6)); // centerY = h/2 - 0.3 sink
        Prop(box, buildingMat, 22, 8, 3.2f, new Vector3(6, 7, 6));

        // Static NPC enemy (no AI, no interaction).
        Prop(box, npcMat, 3, -14, 0.9f, new Vector3(0.7f, 1.8f, 0.7f));

        // Player root at feet + box visual.
        _player = new EmptyActor();
        _player.Parent = Actor;
        _player.Position = new Vector3(0, HeightAt(0, 8) * U, 8 * U);
        var visual = new StaticModel();
        visual.Parent = _player;
        visual.Model = box;
        if (visual.Entries != null && visual.Entries.Length > 0)
            visual.Entries[0].Material = playerMat;
        visual.Scale = new Vector3(0.6f, 1.8f, 0.6f);
        visual.Position = new Vector3(0, 0.9f * U, 0);

        _camera = new Camera();
        _camera.Parent = Actor;

        _walkTest = Engine.CommandLine.Contains("--walk-test");
        _startPos = _player.Position / U;
        if (_walkTest)
            Debug.Log($"SMOKE start=({_startPos.X:F2}, {_startPos.Y:F2}, {_startPos.Z:F2})");
    }

    /// <inheritdoc />
    public override void OnUpdate()
    {
        float dt = Time.DeltaTime;
        if (dt <= 0) return;

        float iv, ih;
        if (_walkTest)
        {
            _testTime += dt;
            iv = 1; ih = 0;
            if (_testTime >= 3.5f)
            {
                var p = _player.Position / U;
                Debug.Log($"SMOKE end=({p.X:F2}, {p.Y:F2}, {p.Z:F2}) moved={Vector3.Distance(_startPos, p):F2}");
                Engine.RequestExit();
                return;
            }
        }
        else
        {
            _yaw += Input.GetAxis("Mouse X") * 3.0f;
            iv = Input.GetAxis("Vertical");
            ih = Input.GetAxis("Horizontal");
        }

        bool run = Input.GetKey(KeyboardKeys.Shift);
        float speed = run ? 9.5f : 5.0f;

        var basis = new Transform(Vector3.Zero, Quaternion.Euler(0, _yaw, 0), Vector3.One);
        var wish = basis.TransformDirection(new Vector3(ih, 0, iv));
        wish.Y = 0;
        if (wish.Length > 1) wish.Normalize();

        var pos = _player.Position / U; // work in meters
        pos += wish * (speed * dt);
        pos.X = Mathf.Clamp(pos.X, -48, 48);
        pos.Z = Mathf.Clamp(pos.Z, -48, 48);
        _vy -= 22f * dt;
        pos.Y += _vy * dt;
        float ground = HeightAt(pos.X, pos.Z);
        if (pos.Y <= ground) { pos.Y = ground; _vy = 0; }
        _player.Position = pos * U;
        _player.Orientation = Quaternion.Euler(0, _yaw, 0);

        // Third-person camera: behind + above, looking at the player's chest.
        var back = basis.TransformDirection(new Vector3(0, 0, -1));
        _camera.Position = (pos + back * 7.5f + new Vector3(0, 3.2f, 0)) * U;
        _camera.LookAt((pos + new Vector3(0, 1.6f, 0)) * U);
    }
}
