using Godot;
using System.Collections.Generic;

// City building kits (owner): real building models converted
// from the city-map archives the owner supplied, one kit per
// mission contract (assets/citykits/kit<N>/b_<i>.glb). When a
// contract's kit is active, building placements instance kit
// models instead of the procedural boxes: the model is scaled
// UNIFORMLY to the requested height (architecture keeps its
// proportions) and the caller registers collision, the roof
// floor and destruction from the model's scaled AABB, so a kit
// building blocks, stands and falls exactly like a built one.
//
// Kits load lazily and cache their PackedScenes + native AABBs;
// a missing or empty kit yields null and the caller falls back
// to the procedural building.
public static class CityKits
{
    private sealed class Entry
    {
        public PackedScene Scene;
        public Aabb Box; // native AABB, base at y=0, centered on x/z
    }

    private static readonly Dictionary<int, List<Entry>> _kits = new();

    private static List<Entry> Load(int kit)
    {
        if (_kits.TryGetValue(kit, out var cached)) return cached;
        var list = new List<Entry>();
        // Scan a bounded index range and SKIP missing
        // models: the kit's roster is whatever files are
        // present. (Owner, 2026-11-12: the seven heavy
        // towers were deleted from kit4 outright — the old
        // stop-at-first-gap scan would have emptied the
        // whole kit and fallen back to procedural boxes.)
        for (int i = 0; i < 64; i++)
        {
            string path = $"res://assets/citykits/kit{kit}/b_{i}.glb";
            if (!ResourceLoader.Exists(path)) continue;
            var scene = ResourceLoader.Load<PackedScene>(path);
            if (scene == null) continue;
            var probe = scene.Instantiate<Node3D>();
            var box = Measure(probe);
            probe.Free();
            if (box.Size.Y < 0.5f) continue; // not a building
            list.Add(new Entry { Scene = scene, Box = box });
        }
        _kits[kit] = list;
        GD.Print($"CITYKIT {kit}: {list.Count} buildings loaded");
        return list;
    }

    // Union of every MeshInstance3D's AABB under the instance,
    // in the instance root's local space.
    private static Aabb Measure(Node3D root)
    {
        var acc = new Aabb();
        bool first = true;
        void Walk(Node node, Transform3D xf)
        {
            var t = node is Node3D n3 ? xf * n3.Transform : xf;
            if (node is MeshInstance3D mi && mi.Mesh != null)
            {
                var b = t * mi.GetAabb();
                if (first) { acc = b; first = false; }
                else acc = acc.Merge(b);
            }
            foreach (var c in node.GetChildren()) Walk(c, t);
        }
        Walk(root, Transform3D.Identity);
        return acc;
    }

    /// <summary>
    /// Instance a building from the kit, uniformly scaled so its
    /// height matches wantHeight (clamped so no model is blown
    /// up or shrunk absurdly). effSize receives the scaled AABB
    /// size to register collision/roof/destruction with. The
    /// returned node's origin is the footprint center at the
    /// model's base. Null when the kit has no buildings.
    /// </summary>
    public static Node3D Take(int kit, int seq, float wantHeight, out Vector3 effSize)
    {
        effSize = Vector3.Zero;
        var list = Load(kit);
        if (list.Count == 0) return null;
        var e = list[Mathf.Abs(seq) % list.Count];
        float s = Mathf.Clamp(wantHeight / e.Box.Size.Y, 0.3f, 3.5f);
        var node = e.Scene.Instantiate<Node3D>();
        node.Scale = Vector3.One * s;
        effSize = e.Box.Size * s;
        // The exported models sit with their base at y=0 and the
        // footprint centered; the source AABB may be offset, so
        // shift the instance so its base-center is the origin.
        node.Position = new Vector3(
            -(e.Box.Position.X + e.Box.Size.X / 2f) * s, -e.Box.Position.Y * s,
            -(e.Box.Position.Z + e.Box.Size.Z / 2f) * s);
        var holder = new Node3D();
        holder.AddChild(node);
        return holder;
    }
}
