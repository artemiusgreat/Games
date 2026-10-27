using Godot;
using System.Collections.Generic;

// MechParts: per-mech breakable-part registry for the duel. Owns the part
// model (which meshes belong to which part, per-part HP), per-mesh hit
// volumes anchored to skeleton bones, and hit attribution.
//
// Part mapping (from the imported node names + per-vertex skin weights —
// every mesh in both FBXs is rigidly bound 100% to a single bone):
//   TW    torso = Torso core + TorsoPin + Waist (the main HP pool)
//         pod_L/pod_R = the LRM ear boxes, SPLIT out of the single Torso
//           mesh at runtime (the kit's torso STL merges ears+torso into
//           one mesh, so the ears are separated here by triangle region)
//         head  = canopy region split out of Torso + Antenna
//         arm_L/R = UpperArm + ShoulderPad + Elbow + Forearm + HandGun
//           (+ WeaponERPPC), all shoulder/elbow-bound
//         leg_L/R = Thigh + Shin + FootHub + all Toe meshes
//   ATLAS torso = Torso core + Pelvis + AC20 + SRM6 (per spec the guns
//           ride with the torso part)
//         head  = skull cap region split out of Torso
//         arm_L/R = Shoulder + UpperArm + Elbow + Forearm
//         leg_L/R = Thigh + Shin + Foot
//
// Volumes: each mesh contributes one box in its bone's frame, derived from
// the mesh AABB at bind pose. A world point is transformed into each
// bone's frame and the box signed-distance decides attribution, so the
// volumes follow articulation (leg bends, torso twist) exactly. Attribution
// always returns the nearest part (torso included), destroyed parts are
// skipped once they break off.
public sealed class MechParts
{
    public sealed class Volume
    {
        public int Bone;
        public Vector3 CenterB;
        public Vector3 HalfB;
    }

    public sealed class MeshRec
    {
        public MeshInstance3D Mi;
        public Node OrigParent;
        public Transform3D OrigLocal;
        public Skin OrigSkin;
        public NodePath OrigSkeleton;
        public int Bone;
        public string BoneName;
        public bool Detached;
    }

    public sealed class Part
    {
        public string Id;
        public string Display;
        public float MaxHp;
        public float Hp;
        public bool Destroyed;
        public readonly List<MeshRec> Meshes = new();
        public readonly List<Volume> Volumes = new();
    }

    private readonly MechWalker _walker;
    private readonly Skeleton3D _skel;
    private readonly string _mechId;
    private readonly Dictionary<string, Part> _parts = new();

    public string MechId => _mechId;
    public System.Action<string, Vector3> OnBreak; // (partId, worldCenter)

    public MechParts(MechWalker walker, string mechId)
    {
        _walker = walker;
        _mechId = mechId;
        _skel = walker.Skeleton;

        var byName = new Dictionary<string, MeshInstance3D>();
        CollectMeshes(walker.ModelRoot, byName);

        if (mechId == "TW")
        {
            SplitTorso(byName, new (string, System.Func<Vector3, bool>)[]
            {
                ("Pod_L", p => p.X >= 1.7f && p.Y >= 9.9f && p.Z <= -1.1f),
                ("Pod_R", p => p.X <= -1.7f && p.Y >= 9.9f && p.Z <= -1.1f),
                ("Head", p => Mathf.Abs(p.X) <= 1.4f && p.Y >= 10.1f && p.Z >= 1.0f),
            });
            AddPart("torso", "TORSO", 0f, byName, "TorsoCore", "TorsoPin", "Waist");
            AddPart("pod_L", "LEFT LRM POD", 45f, byName, "TorsoPod_L");
            AddPart("pod_R", "RIGHT LRM POD", 45f, byName, "TorsoPod_R");
            AddPart("head", "HEAD", 50f, byName, "TorsoHead", "Antenna");
            AddPart("arm_L", "LEFT ARM", 65f, byName,
                "UpperArm_L", "ShoulderPad_L", "Elbow_L", "Forearm_L", "HandGun_L", "WeaponERPPC_L");
            AddPart("arm_R", "RIGHT ARM", 65f, byName,
                "UpperArm_R", "ShoulderPad_R", "Elbow_R", "Forearm_R", "HandGun_R", "WeaponERPPC_R");
            AddPart("leg_L", "LEFT LEG", 90f, byName,
                "Thigh_L", "Shin_L", "FootHub_L", "ToeFL1_L", "ToeFL2_L", "ToeFR1_L", "ToeFR2_L", "ToeBack1_L", "ToeBack2_L");
            AddPart("leg_R", "RIGHT LEG", 90f, byName,
                "Thigh_R", "Shin_R", "FootHub_R", "ToeFL1_R", "ToeFL2_R", "ToeFR1_R", "ToeFR2_R", "ToeBack1_R", "ToeBack2_R");
        }
        else
        {
            SplitTorso(byName, new (string, System.Func<Vector3, bool>)[]
            {
                ("Head", p => Mathf.Abs(p.X) <= 1.3f && p.Y >= 12.45f),
            });
            AddPart("torso", "TORSO", 0f, byName, "TorsoCore", "Pelvis", "AC20", "SRM6");
            AddPart("head", "HEAD", 50f, byName, "TorsoHead");
            AddPart("arm_L", "LEFT ARM", 65f, byName, "Shoulder_L", "UpperArm_L", "Elbow_L", "Forearm_L");
            AddPart("arm_R", "RIGHT ARM", 65f, byName, "Shoulder_R", "UpperArm_R", "Elbow_R", "Forearm_R");
            AddPart("leg_L", "LEFT LEG", 90f, byName, "Thigh_L", "Shin_L", "Foot_L");
            AddPart("leg_R", "RIGHT LEG", 90f, byName, "Thigh_R", "Shin_R", "Foot_R");
        }

        foreach (var part in _parts.Values)
        {
            string line = $"{_mechId} PART {part.Id} hp={(part.MaxHp > 0 ? part.MaxHp.ToString("F0") : "pool")} meshes=[";
            foreach (var rec in part.Meshes)
            {
                line += $"{rec.Mi.Name}:{rec.BoneName} ";
                part.Volumes.Add(MakeVolume(rec));
            }
            GD.Print(line + "]");
        }
    }

    // ---------- registry construction ----------

    private static void CollectMeshes(Node n, Dictionary<string, MeshInstance3D> byName)
    {
        if (n is MeshInstance3D mi) byName[mi.Name] = mi;
        foreach (var c in n.GetChildren()) CollectMeshes(c, byName);
    }

    private void AddPart(string id, string display, float hp,
        Dictionary<string, MeshInstance3D> byName, params string[] meshNames)
    {
        var part = new Part { Id = id, Display = display, MaxHp = hp, Hp = hp };
        foreach (var name in meshNames)
        {
            if (!byName.TryGetValue(name, out var mi))
            {
                GD.PrintErr($"{_mechId} PARTS: mesh '{name}' not found for part {id}");
                continue;
            }
            int bone = DominantBone(mi, out string boneName);
            part.Meshes.Add(new MeshRec
            {
                Mi = mi,
                OrigParent = mi.GetParent(),
                OrigLocal = mi.Transform,
                OrigSkin = mi.Skin,
                OrigSkeleton = mi.Skeleton,
                Bone = bone,
                BoneName = boneName
            });
        }
        _parts[id] = part;
    }

    // Dominant skin bone of a mesh from its vertex weights (bind index ->
    // bind name -> skeleton bone index).
    private int DominantBone(MeshInstance3D mi, out string boneName)
    {
        boneName = "";
        if (mi.Skin == null || mi.Mesh == null) return -1;
        var tally = new Dictionary<int, double>();
        for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
        {
            var arrays = mi.Mesh.SurfaceGetArrays(s);
            var bones = arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
            var weights = arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
            int nv = weights.Length / 4;
            for (int v = 0; v < nv; v++)
                for (int k = 0; k < 4; k++)
                {
                    float w = weights[v * 4 + k];
                    if (w > 0.01f)
                    {
                        int b = bones[v * 4 + k];
                        tally[b] = (tally.TryGetValue(b, out var c) ? c : 0) + w;
                    }
                }
        }
        int best = -1; double bv = 0;
        foreach (var kv in tally) if (kv.Value > bv) { bv = kv.Value; best = kv.Key; }
        if (best < 0 || best >= mi.Skin.GetBindCount()) return -1;
        boneName = mi.Skin.GetBindName(best);
        return _skel.FindBone(boneName);
    }

    private Volume MakeVolume(MeshRec rec)
    {
        var aabb = rec.Mi.GetAabb();
        var inv = _skel.GetBoneGlobalRest(rec.Bone).AffineInverse();
        Vector3 mn = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
        Vector3 mx = new Vector3(float.MinValue, float.MinValue, float.MinValue);
        for (int i = 0; i < 8; i++)
        {
            var c = new Vector3(
                (i & 1) == 0 ? aabb.Position.X : aabb.End.X,
                (i & 2) == 0 ? aabb.Position.Y : aabb.End.Y,
                (i & 4) == 0 ? aabb.Position.Z : aabb.End.Z);
            var b = inv * c;
            mn = mn.Min(b); mx = mx.Max(b);
        }
        return new Volume { Bone = rec.Bone, CenterB = (mn + mx) * 0.5f, HalfB = (mx - mn) * 0.5f };
    }

    // ---------- torso split ----------

    // The kit FBXs merge several logical parts into the single Torso mesh.
    // Split it into region meshes (triangle-centroid classification in
    // walker-model space: feet at y=0, +x = model left, +z = model front).
    // Regions are tested in order; unmatched triangles keep the core piece.
    // New pieces share the original skin/skeleton and sit at the same tree
    // spot, so rendering is unchanged until a piece is detached.
    private void SplitTorso(Dictionary<string, MeshInstance3D> byName,
        (string Suffix, System.Func<Vector3, bool> Region)[] regions)
    {
        if (!byName.TryGetValue("Torso", out var orig) || orig.Mesh == null) return;
        var parent = orig.GetParent();
        var modelXf = _walker.GlobalTransform.AffineInverse() * orig.GlobalTransform;
        string baseName = orig.Name;
        var pieces = new Dictionary<string, MeshInstance3D>();
        var triCounts = new Dictionary<string, int>();

        MeshInstance3D Piece(string suffix)
        {
            if (!pieces.TryGetValue(suffix, out var mi))
            {
                mi = new MeshInstance3D
                {
                    Name = baseName + suffix,
                    Skin = orig.Skin,
                    Skeleton = orig.Skeleton,
                    Transform = orig.Transform
                };
                mi.Mesh = new ArrayMesh();
                pieces[suffix] = mi;
                triCounts[suffix] = 0;
            }
            return mi;
        }

        for (int s = 0; s < orig.Mesh.GetSurfaceCount(); s++)
        {
            var arrays = orig.Mesh.SurfaceGetArrays(s);
            var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var idxArr = arrays[(int)Mesh.ArrayType.Index];
            int[] idx;
            if (idxArr.VariantType == Variant.Type.PackedInt32Array)
                idx = idxArr.AsInt32Array();
            else if (idxArr.VariantType == Variant.Type.PackedInt64Array)
            {
                var l = idxArr.AsInt64Array();
                idx = new int[l.Length];
                for (int i = 0; i < l.Length; i++) idx[i] = (int)l[i];
            }
            else
            {
                idx = new int[verts.Length];
                for (int i = 0; i < idx.Length; i++) idx[i] = i;
            }

            // Classify triangles.
            var triRegion = new int[idx.Length / 3];
            for (int t = 0; t < triRegion.Length; t++)
            {
                var c = (verts[idx[t * 3]] + verts[idx[t * 3 + 1]] + verts[idx[t * 3 + 2]]) / 3f;
                var cm = modelXf * c;
                triRegion[t] = regions.Length; // core
                for (int r = 0; r < regions.Length; r++)
                    if (regions[r].Region(cm)) { triRegion[t] = r; break; }
            }

            // Emit one surface per (region) that has triangles.
            for (int r = 0; r <= regions.Length; r++)
            {
                var remap = new Dictionary<int, int>();
                var srcVerts = new List<int>();
                var newIdx = new List<int>();
                for (int t = 0; t < triRegion.Length; t++)
                {
                    if (triRegion[t] != r) continue;
                    for (int k = 0; k < 3; k++)
                    {
                        int sv = idx[t * 3 + k];
                        if (!remap.TryGetValue(sv, out int nv))
                        {
                            nv = srcVerts.Count;
                            remap[sv] = nv;
                            srcVerts.Add(sv);
                        }
                        newIdx.Add(nv);
                    }
                }
                if (newIdx.Count == 0) continue;
                string suffix = r < regions.Length ? regions[r].Suffix : "Core";
                var mi = Piece(suffix);
                var am = (ArrayMesh)mi.Mesh;
                var outArrays = SliceSurface(arrays, srcVerts, newIdx, verts.Length);
                int si = am.GetSurfaceCount();
                am.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, outArrays);
                var mat = orig.Mesh.SurfaceGetMaterial(s);
                if (mat != null) am.SurfaceSetMaterial(si, mat);
                triCounts[suffix] += newIdx.Count / 3;
            }
        }

        int insertAt = orig.GetIndex();
        foreach (var kv in pieces)
        {
            parent.AddChild(kv.Value);
            parent.MoveChild(kv.Value, insertAt++);
            byName[kv.Value.Name] = kv.Value;
            GD.Print($"{_mechId} SPLIT {kv.Value.Name} tris={triCounts[kv.Key]}");
        }
        byName.Remove(baseName);
        parent.RemoveChild(orig);
        orig.Free();
    }

    // Copy every attribute slot of a surface, keeping only srcVerts
    // (in order) and the given triangle index list.
    private static Godot.Collections.Array SliceSurface(Godot.Collections.Array arrays,
        List<int> srcVerts, List<int> newIdx, int srcVertCount)
    {
        var outArr = new Godot.Collections.Array();
        outArr.Resize((int)Mesh.ArrayType.Max);
        for (int slot = 0; slot < (int)Mesh.ArrayType.Max; slot++)
        {
            if (slot == (int)Mesh.ArrayType.Index)
            {
                outArr[slot] = newIdx.ToArray();
                continue;
            }
            var v = arrays[slot];
            if (v.VariantType == Variant.Type.Nil) continue;
            outArr[slot] = SliceVariant(v, srcVerts, srcVertCount);
        }
        return outArr;
    }

    // Note: Godot 4.7 C# surface slots carry plain System arrays
    // (Vector3[], float[], int[], ...) under the Packed* Variant types.
    private static Variant SliceVariant(Variant v, List<int> src, int srcCount)
    {
        switch (v.VariantType)
        {
            case Variant.Type.PackedVector3Array:
                {
                    var a = v.AsVector3Array();
                    var o = new Vector3[src.Count];
                    for (int i = 0; i < src.Count; i++) o[i] = a[src[i]];
                    return o;
                }
            case Variant.Type.PackedVector2Array:
                {
                    var a = v.AsVector2Array();
                    var o = new Vector2[src.Count];
                    for (int i = 0; i < src.Count; i++) o[i] = a[src[i]];
                    return o;
                }
            case Variant.Type.PackedColorArray:
                {
                    var a = v.AsColorArray();
                    var o = new Color[src.Count];
                    for (int i = 0; i < src.Count; i++) o[i] = a[src[i]];
                    return o;
                }
            case Variant.Type.PackedFloat32Array:
                {
                    var a = v.AsFloat32Array();
                    int per = srcCount > 0 ? a.Length / srcCount : 0;
                    var o = new float[src.Count * per];
                    for (int i = 0; i < src.Count; i++)
                        for (int k = 0; k < per; k++) o[i * per + k] = a[src[i] * per + k];
                    return o;
                }
            case Variant.Type.PackedInt32Array:
                {
                    var a = v.AsInt32Array();
                    int per = srcCount > 0 ? a.Length / srcCount : 0;
                    var o = new int[src.Count * per];
                    for (int i = 0; i < src.Count; i++)
                        for (int k = 0; k < per; k++) o[i * per + k] = a[src[i] * per + k];
                    return o;
                }
            case Variant.Type.PackedByteArray:
                {
                    var a = v.AsByteArray();
                    int per = srcCount > 0 ? a.Length / srcCount : 0;
                    var o = new byte[src.Count * per];
                    for (int i = 0; i < src.Count; i++)
                        for (int k = 0; k < per; k++) o[i * per + k] = a[src[i] * per + k];
                    return o;
                }
            default:
                return v;
        }
    }

    // ---------- queries ----------

    public Part GetPart(string id) => _parts[id];
    public bool Alive(string id) => !_parts[id].Destroyed;
    public float HpOf(string id) => _parts[id].Hp;
    public IEnumerable<Part> AllParts => _parts.Values;

    private static float BoxSdf(Vector3 p, Volume v)
    {
        var q = (p - v.CenterB).Abs() - v.HalfB;
        float ox = Mathf.Max(q.X, 0f), oy = Mathf.Max(q.Y, 0f), oz = Mathf.Max(q.Z, 0f);
        float outside = Mathf.Sqrt(ox * ox + oy * oy + oz * oz);
        float inside = Mathf.Min(Mathf.Max(q.X, Mathf.Max(q.Y, q.Z)), 0f);
        return outside + inside;
    }

    // Nearest part (by per-mesh bone-space box SDF) to a world point.
    // Destroyed parts no longer exist on the mech and are skipped.
    public string AttributePoint(Vector3 world)
    {
        string best = "torso";
        float bestSd = float.MaxValue;
        var skelInv = _skel.GlobalTransform.AffineInverse();
        var pSk = skelInv * world;
        var poseInv = new Dictionary<int, Transform3D>();
        foreach (var part in _parts.Values)
        {
            if (part.Destroyed) continue;
            foreach (var vol in part.Volumes)
            {
                if (!poseInv.TryGetValue(vol.Bone, out var inv))
                {
                    inv = _skel.GetBoneGlobalPose(vol.Bone).AffineInverse();
                    poseInv[vol.Bone] = inv;
                }
                float sd = BoxSdf(inv * pSk, vol);
                if (sd < bestSd) { bestSd = sd; best = part.Id; }
            }
        }
        return best;
    }

    // World-space center of a part (mean of its volume centers).
    public Vector3 PartCenterWorld(string partId)
    {
        var part = _parts[partId];
        if (part.Volumes.Count == 0) return _walker.RootPos;
        var sum = Vector3.Zero;
        var skelG = _skel.GlobalTransform;
        foreach (var vol in part.Volumes)
            sum += skelG * (_skel.GetBoneGlobalPose(vol.Bone) * vol.CenterB);
        return sum / part.Volumes.Count;
    }
}
