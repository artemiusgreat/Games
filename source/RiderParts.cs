using Godot;
using System.Collections.Generic;
using Titans.Definitions;

namespace Titans;

// RiderParts: per-rider breakable-part registry for the duel. Owns the part
// model (which meshes belong to which part, per-part HP), per-mesh hit
// volumes anchored to skeleton bones, and hit attribution.
//
// The per-rider part mapping itself (which meshes form which
// part, the torso carve regions, per-part HP) lives one file
// per rider under source/riders/, reached through the
// RiderLayouts factory - this class is the shared machinery
// the layouts drive.
//
// Volumes: each mesh contributes one box in its bone's frame, derived from
// the mesh AABB at bind pose. A world point is transformed into each
// bone's frame and the box signed-distance decides attribution, so the
// volumes follow articulation (leg bends, torso twist) exactly. Attribution
// always returns the nearest part (torso included), destroyed parts are
// skipped once they break off.
public sealed class RiderParts
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
    public int BindIndex;
    public string BoneName;
    public bool Detached;
    // Saved overlay for the wreck state (restored on unwreck/reset;
    // the wreck transform itself is rebuilt from OrigLocal).
    public Material SavedOverlay;
  }

  public sealed class Debris
  {
    public Part Part;
    public Node3D Node;
    public Vector3 Vel;
    public Vector3 AngAxisW = Vector3.Up;
    public float AngSpeed;
    public bool Settled;
    public float RestMinY;
    public float RestGroundY;
  }

  public sealed class Part
  {
    public PartId Id;
    public string Display;
    public float MaxHp;
    public float Hp;
    public bool Destroyed;
    // Legs only: destroyed but still attached (darkened + deformed),
    // per the owner's rule that legs never fall off.
    public bool Wrecked;
    public readonly List<MeshRec> Meshes = new();
    public readonly List<Volume> Volumes = new();
  }

  // Set by a rider's layout: the fused-miniature builds are
  // cut from one shell, so their parts have open seams; with
  // backface culling on, a seam reads as a see-through hole
  // (owner report, 2026-11-01). Those riders' materials
  // render double-sided so a seam shows the part's dark
  // interior instead of the sky.
  internal bool DoubleSidedMaterials;
  private readonly RiderWalker _walker;
  private readonly Skeleton3D _skel;
  private readonly RiderId _riderId;
  private readonly Dictionary<PartId, Part> _parts = new();

  public RiderId RiderId => this._riderId;
  public System.Action<PartId, Vector3> OnBreak; // (partId, worldCenter)

  public RiderParts(RiderWalker walker, RiderId riderId)
  {
    this._walker = walker;
    this._riderId = riderId;
    this._skel = walker.Skeleton;

    var byName = new Dictionary<string, MeshInstance3D>();
    CollectMeshes(walker.ModelRoot, byName);

    // Rider paint lives in the textures, never in vertex colours.
    // An export that carries a stray colour layer (the owner's
    // 2026-11-21 Hector Torso: near-black values, the only mesh in
    // the file with a colour slot) turns the importer's
    // vertex-colour-as-albedo flag into a black part in-game while
    // Blender shows the texture fine. Neutralise the flag on every
    // rider material before the torso carve copies materials.
    StripVertexColorAlbedo(byName);

    // The per-rider layout comes from the factory (owner,
    // 2026-11-27: one file per rider under source/riders/,
    // like the maps) - this class keeps only the shared
    // machinery.
    if (!RiderLayouts.TryBuild(riderId, this, byName))
    {
      // Unknown chassis: no hand-authored layout. Fail LOUD
      // and fall back to a single torso part (the whole
      // model) so the duel still runs - author a real
      // layout in source/riders/ before shipping the rider.
      Dbg.Err($"PARTS: no layout for '{riderId}' - torso-only fallback; author a layout in source/riders/");
      var all = new string[byName.Count];
      byName.Keys.CopyTo(all, 0);
      this.AddPart(PartId.Torso, "TORSO", 0f, byName, all);
    }
    // Safety net: EVERY mesh must belong to a part. A mesh
    // no layout claimed never detaches - it stays skinned to
    // the dead rider's skeleton and floats at the death
    // position while every real part lies on the ground
    // (owner report, 2026-11-17). Attach any leftover to the
    // part whose claimed meshes sit nearest to it.
    {
      var claimed = new System.Collections.Generic.HashSet<MeshInstance3D>();
      var partCenters = new System.Collections.Generic.Dictionary<PartId, Vector3>();
      foreach (var part in this._parts.Values)
      {
        var acc = Vector3.Zero; int n = 0;
        foreach (var rec in part.Meshes)
        {
          claimed.Add(rec.Mi);
          acc += rec.Mi.GetAabb().GetCenter(); n++;
        }
        if (n > 0)
        {
          partCenters[part.Id] = acc / n;
        }
      }
      foreach (var kv in byName)
      {
        if (claimed.Contains(kv.Value))
        {
          continue;
        }
        // Prefer the part the mesh's own bone belongs to
        // (a fragment bound to an elbow should fall with
        // the arm, wherever its centre sits); nearest
        // centre is the fallback.
        Part best = null;
        this.DominantBone(kv.Value, out string bn, out int _);
        PartId? want = null;
        if (bn == Bones.Head)
        {
          want = PartId.Head;
        }
        else if (bn == Bones.Torso || bn == Bones.Pelvis)
        {
          want = PartId.Torso;
        }
        else if (bn == Bones.ShoulderL || bn == Bones.ElbowL)
        {
          want = PartId.ArmL;
        }
        else if (bn == Bones.ShoulderR || bn == Bones.ElbowR)
        {
          want = PartId.ArmR;
        }
        else if (bn == Bones.HipL || bn == Bones.KneeL || bn == Bones.AnkleL)
        {
          want = PartId.LegL;
        }
        else if (bn == Bones.HipR || bn == Bones.KneeR || bn == Bones.AnkleR)
        {
          want = PartId.LegR;
        }

        if (want != null)
        {
          this._parts.TryGetValue(want.Value, out best);
        }

        if (best == null)
        {
          var c = kv.Value.GetAabb().GetCenter();
          float bd = float.MaxValue;
          foreach (var part in this._parts.Values)
          {
            if (!partCenters.TryGetValue(part.Id, out var pc))
            {
              continue;
            }

            float d = c.DistanceSquaredTo(pc);
            if (d < bd) { bd = d; best = part; }
          }
        }
        if (best != null)
        {
          Dbg.Err($"PARTS {this._riderId}: mesh '{kv.Key}' claimed by no layout - attached to {best.Id} by the safety net");
          this.AttachMesh(best, kv.Value);
        }
      }
    }
    foreach (var part in this._parts.Values)
    {
      string line = $"{this._riderId} PART {part.Id} hp={(part.MaxHp > 0 ? part.MaxHp.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) : "pool")} meshes=[";
      foreach (var rec in part.Meshes)
      {
        line += $"{rec.Mi.Name}:{rec.BoneName} ";
        part.Volumes.Add(this.MakeVolume(rec));
      }
      Dbg.Print(line + "]");
    }
    string centers = $"{this._riderId} PARTCENTERS root={this._walker.RootPos}";
    foreach (var part in this._parts.Values)
    {
      centers += $" {part.Id}={this.PartCenterWorld(part.Id)}";
    }

    Dbg.Print(centers);

    if (this.DoubleSidedMaterials)
    {
      var dupCache = new Dictionary<Material, Material>();
      foreach (var part in this._parts.Values)
      {
        foreach (var rec in part.Meshes)
        {
          var mesh = rec.Mi.Mesh;
          if (mesh == null)
          {
            continue;
          }

          for (int si = 0; si < mesh.GetSurfaceCount(); si++)
          {
            var mat = rec.Mi.GetSurfaceOverrideMaterial(si) ?? mesh.SurfaceGetMaterial(si);
            if (mat is not StandardMaterial3D sm)
            {
              continue;
            }

            if (!dupCache.TryGetValue(sm, out var dup))
            {
              dup = (StandardMaterial3D)sm.Duplicate();
              ((StandardMaterial3D)dup).CullMode = BaseMaterial3D.CullModeEnum.Disabled;
              dupCache[sm] = dup;
            }
            rec.Mi.SetSurfaceOverrideMaterial(si, dup);
          }
        }
      }
    }
  }

  // ---------- registry construction ----------

  // See the call in the constructor: duplicate any rider material
  // that multiplies its texture by vertex colours, with the flag
  // off. Materials are shared between meshes, so dedupe by instance.
  private static void StripVertexColorAlbedo(Dictionary<string, MeshInstance3D> byName)
  {
    var done = new Dictionary<Material, Material>();
    foreach (var mi in byName.Values)
    {
      var mesh = mi.Mesh;
      if (mesh == null)
      {
        continue;
      }

      for (int s = 0; s < mesh.GetSurfaceCount(); s++)
      {
        var mat = mesh.SurfaceGetMaterial(s);
        if (mat is not StandardMaterial3D sm || !sm.VertexColorUseAsAlbedo)
        {
          continue;
        }

        if (!done.TryGetValue(mat, out var repl))
        {
          repl = (StandardMaterial3D)sm.Duplicate();
          ((StandardMaterial3D)repl).VertexColorUseAsAlbedo = false;
          done[mat] = repl;
        }
        mesh.SurfaceSetMaterial(s, repl);
      }
    }
  }

  private static void CollectMeshes(Node n, Dictionary<string, MeshInstance3D> byName)
  {
    if (n is MeshInstance3D mi)
    {
      byName[mi.Name] = mi;
    }

    foreach (var c in n.GetChildren())
    {
      CollectMeshes(c, byName);
    }
  }

  internal void AddPart(PartId id, string display, float hp,
      Dictionary<string, MeshInstance3D> byName, params string[] meshNames)
  {
    var part = new Part { Id = id, Display = display, MaxHp = hp, Hp = hp };
    foreach (var name in meshNames)
    {
      if (!byName.TryGetValue(name, out var mi))
      {
        Dbg.Err($"{this._riderId} PARTS: mesh '{name}' not found for part {id}");
        continue;
      }
      this.AttachMesh(part, mi);
    }
    this._parts[id] = part;
  }

  private void AttachMesh(Part part, MeshInstance3D mi)
  {
    int bone = this.DominantBone(mi, out string boneName, out int bindIndex);
    part.Meshes.Add(new MeshRec
    {
      Mi = mi,
      OrigParent = mi.GetParent(),
      OrigLocal = mi.Transform,
      OrigSkin = mi.Skin,
      OrigSkeleton = mi.Skeleton,
      Bone = bone,
      BindIndex = bindIndex,
      BoneName = boneName
    });
  }

  // Segmented builds ship their small fragments as separate
  // meshes named Debris_<partId>_N: sweep them into the named
  // part so each fragment breaks and flies on its own. (Only
  // the segmented Hector build uses the convention today.)
  // Debris mesh names embed the part's string name
  // (Debris_arm_L_37_small) - model-file data, resolved
  // through this map, the same exception bone names get.
  private static readonly System.Collections.Generic.Dictionary<string, PartId> PartNameMap = new()
  {
    ["arm_L"] = PartId.ArmL,
    ["arm_R"] = PartId.ArmR,
    ["gun"] = PartId.Gun,
    ["torso"] = PartId.Torso,
    ["pod_L"] = PartId.PodL,
    ["pod_R"] = PartId.PodR,
    ["head"] = PartId.Head,
    ["leg_L"] = PartId.LegL,
    ["leg_R"] = PartId.LegR,
  };

  internal void AttachDebris(Dictionary<string, MeshInstance3D> byName)
  {
    foreach (var kv in byName)
    {
      if (!kv.Key.StartsWith("Debris_", System.StringComparison.Ordinal))
      {
        continue;
      }

      var rest = kv.Key.Substring("Debris_".Length);
      // The part id is the longest leading segment chain
      // that names a real part: Debris_arm_L_37_small must
      // land on arm_L - stripping only the last segment
      // parsed it to a non-existent "arm_L_37", the bit
      // stayed unclaimed, and since an unclaimed mesh never
      // detaches it hung in the air at the death position
      // after its rider was destroyed (owner, 2026-11-17).
      string cand = rest;
      PartId hit = PartId.None;
      while (cand.Length > 0)
      {
        if (PartNameMap.TryGetValue(cand, out var pid) && this._parts.ContainsKey(pid)) { hit = pid; break; }
        int cut = cand.LastIndexOf('_');
        if (cut <= 0)
        {
          break;
        }

        cand = cand.Substring(0, cut);
      }
      if (hit != PartId.None)
      {
        this.AttachMesh(this._parts[hit], kv.Value);
      }
    }
  }

  // Dominant skin bone of a mesh from its vertex weights (bind index ->
  // bind name -> skeleton bone index).
  private int DominantBone(MeshInstance3D mi, out string boneName, out int bindIndex)
  {
    boneName = "";
    bindIndex = -1;
    if (mi.Skin == null || mi.Mesh == null)
    {
      return -1;
    }

    var tally = new Dictionary<int, double>();
    for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
    {
      var arrays = mi.Mesh.SurfaceGetArrays(s);
      var bones = arrays[(int)Mesh.ArrayType.Bones].AsInt32Array();
      var weights = arrays[(int)Mesh.ArrayType.Weights].AsFloat32Array();
      int nv = weights.Length / 4;
      for (int v = 0; v < nv; v++)
      {
        for (int k = 0; k < 4; k++)
        {
          float w = weights[v * 4 + k];
          if (w > MinBindWeight)
          {
            int b = bones[v * 4 + k];
            tally[b] = (tally.TryGetValue(b, out var c) ? c : 0) + w;
          }
        }
      }
    }
    int best = -1; double bv = 0;
    foreach (var kv in tally)
    {
      if (kv.Value > bv) { bv = kv.Value; best = kv.Key; }
    }

    if (best < 0 || best >= mi.Skin.GetBindCount())
    {
      return -1;
    }

    bindIndex = best;
    boneName = mi.Skin.GetBindName(best);
    return this._skel.FindBone(boneName);
  }

  private Volume MakeVolume(MeshRec rec)
  {
    var aabb = rec.Mi.GetAabb();
    // A mesh whose dominant bind names a bone the skeleton
    // does not carry (Hermes's skin binds do this) resolves
    // Bone to -1: the volume then comes from the skin's own
    // bind pose - the same transform the detach path below
    // uses - instead of querying the skeleton with -1,
    // which logged an engine error per mesh and left the
    // volume in identity space.
    var inv = rec.Bone >= 0
        ? this._skel.GetBoneGlobalRest(rec.Bone).AffineInverse()
        : rec.BindIndex >= 0 && rec.OrigSkin != null
            ? rec.OrigSkin.GetBindPose(rec.BindIndex)
            : Transform3D.Identity;
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
  // the model's own frame: feet at y=0, +x = model left, +z = model front).
  // Regions are tested in order; unmatched triangles keep the core piece.
  // New pieces share the original skin/skeleton and sit at the same tree
  // spot, so rendering is unchanged until a piece is detached.
  // The carved piece's mesh name is the torso mesh's name
  // plus a suffix (TorsoPod_L, TorsoHead) - model-file data,
  // derived from the part here, the one place that knows it;
  // the layouts name the part, never the string.
  private static string CarveSuffix(PartId part) => part switch
  {
    PartId.PodL => "Pod_L",
    PartId.PodR => "Pod_R",
    PartId.Head => "Head",
    _ => "Core",
  };

  internal void SplitTorso(Dictionary<string, MeshInstance3D> byName,
      (PartId Part, System.Func<Vector3, bool> Region)[] regions)
  {
    if (!byName.TryGetValue("Torso", out var orig) || orig.Mesh == null)
    {
      return;
    }

    var parent = orig.GetParent();
    // Classify against the model root, not the walker: both nodes
    // live in the same subtree, so the walker's placement, scale
    // and AABB centring cancel exactly. The walker-relative form
    // drifted whenever parts were rebuilt after the walker had
    // been placed or teleported, and the carve changed with the
    // build timing (Hector's head split, 2026-11-15).
    Node3D modelRoot = null;
    for (Node n = orig; n != null; n = n.GetParent())
    {
      if (n.GetParent() == this._walker) { modelRoot = n as Node3D; break; }
    }

    var modelXf = modelRoot != null
            ? modelRoot.GlobalTransform.AffineInverse() * orig.GlobalTransform
            : this._walker.GlobalTransform.AffineInverse() * orig.GlobalTransform;
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
      {
        idx = idxArr.AsInt32Array();
      }
      else if (idxArr.VariantType == Variant.Type.PackedInt64Array)
      {
        var l = idxArr.AsInt64Array();
        idx = new int[l.Length];
        for (int i = 0; i < l.Length; i++)
        {
          idx[i] = (int)l[i];
        }
      }
      else
      {
        idx = new int[verts.Length];
        for (int i = 0; i < idx.Length; i++)
        {
          idx[i] = i;
        }
      }

      // Classify triangles.
      var triRegion = new int[idx.Length / 3];
      for (int t = 0; t < triRegion.Length; t++)
      {
        var c = (verts[idx[t * 3]] + verts[idx[t * 3 + 1]] + verts[idx[t * 3 + 2]]) / 3f;
        var cm = modelXf * c;
        triRegion[t] = regions.Length; // core
        for (int r = 0; r < regions.Length; r++)
        {
          if (regions[r].Region(cm)) { triRegion[t] = r; break; }
        }
      }

      // Emit one surface per (region) that has triangles.
      var emitted = new Dictionary<int, (Godot.Collections.Array Arr, int[] Idx)>();
      for (int r = 0; r <= regions.Length; r++)
      {
        var remap = new Dictionary<int, int>();
        var srcVerts = new List<int>();
        var newIdx = new List<int>();
        for (int t = 0; t < triRegion.Length; t++)
        {
          if (triRegion[t] != r)
          {
            continue;
          }

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
        if (newIdx.Count == 0)
        {
          continue;
        }

        string suffix = r < regions.Length ? CarveSuffix(regions[r].Part) : "Core";
        var mi = Piece(suffix);
        var am = (ArrayMesh)mi.Mesh;
        var outArrays = SliceSurface(arrays, srcVerts, newIdx, verts.Length);
        emitted[r] = (outArrays, newIdx.ToArray());
        int si = am.GetSurfaceCount();
        am.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, outArrays);
        var mat = orig.Mesh.SurfaceGetMaterial(s);
        if (mat != null)
        {
          am.SurfaceSetMaterial(si, mat);
        }

        triCounts[suffix] += newIdx.Count / 3;
      }

      // Socket plugs (owner, 2026-11-22): a carved pod leaves
      // an open wound in the core - the shell is hollow and
      // single-sided, so once the pod breaks off the player
      // sees straight through the rider. Cap the wound on the
      // CORE piece with a dark plate (see AddSocketPlug).
      for (int r = 0; r < regions.Length; r++)
      {
        if (regions[r].Part is not (PartId.PodL or PartId.PodR))
        {
          continue;
        }

        if (!emitted.TryGetValue(r, out var podE))
        {
          continue;
        }

        if (!emitted.TryGetValue(regions.Length, out var coreE))
        {
          continue;
        }

        this.AddSocketPlug((ArrayMesh)Piece("Core").Mesh, coreE.Arr, coreE.Idx, podE.Arr, podE.Idx);
      }
    }

    int insertAt = orig.GetIndex();
    foreach (var kv in pieces)
    {
      parent.AddChild(kv.Value);
      parent.MoveChild(kv.Value, insertAt++);
      byName[kv.Value.Name] = kv.Value;
      Dbg.Print($"{this._riderId} SPLIT {kv.Value.Name} tris={triCounts[kv.Key]}");
    }
    byName.Remove(baseName);
    parent.RemoveChild(orig);
    orig.Free();
  }

  private static readonly Color SocketPlateColor = new(0.05f, 0.05f, 0.06f); // the dark socket plate
  private const float SocketPlateRoughness = 0.92f, SocketPlateMetallic = 0.25f;
  // Socket-plug geometry guards and tolerances.
  private const float RimWeldFraction = 0.002f;    // rim welding quantum, as a fraction of the pod radius
  private const float RimWeldQuantumMin = 1e-6f;   // ...and its absolute floor
  private const float RimDedupFraction = 0.015f;   // rim points closer than this fraction of the pod radius merge
  private const int MinSocketRimPoints = 3;        // a fan needs at least this many rim points
  private const int MinFanIndices = 3;             // ...and at least one whole triangle
  private const float SocketAxisEpsilon = 1e-5f;   // plug axis degeneracy guard
  private const float SocketUpDotMax = 0.9f;       // |axis.Y| below this keeps world up as the fan reference
  private const float SocketFanEpsilon = 1e-8f;    // fan area-vector degeneracy guard
  private const float SocketTriEpsilon = 1e-9f;    // single fan-triangle degeneracy guard
  private const float MinBindWeight = 0.01f;       // skin weights below this don't count toward a mesh's dominant bone
                                                   // Ray-vs-mesh guards.
  private const float RayMinT = 0.001f;            // hits at or below this distance are the ray's own origin
  private const float RayParallelEpsilon = 1e-8f;  // ray/plane parallelism guard
                                                   // The wreck deformation recipe (crush + tilt, applied
                                                   // in each mesh's own space) and its audit bound.
  private const float WreckTiltRad = 0.085f, WreckPitchRad = 0.045f;
  private const float WreckScaleXZ = 1.07f, WreckScaleY = 0.92f;
  private const float WreckAuditMaxDisp = 0.3f;    // a wreck may move a mesh's centre at most this far
                                                   // Debris launch and physics.
  private const float DebrisMinAwaySq = 0.25f;     // squared distance below which the break direction falls back to facing
  private const float DebrisLaunchAway = 2.0f, DebrisLaunchUp = 2.8f;
  private const float DebrisLaunchAngSpeed = 1.6f;
  private const float DebrisGravity = 20f;
  private const float DebrisSubstepSeconds = 1f / 120f; // debris integrates at the gait's rate
  private const float DebrisMinAngSpeed = 0.02f;   // tumble below this is not integrated
  private const float DebrisContactSkin = 0.002f;  // lowest-corner contact tolerance
  private const float DebrisBounceMinSpeed = 1.6f; // impacts slower than this settle instead of bouncing
  private const float DebrisRestitution = 0.22f;
  private const float DebrisBounceFriction = 0.55f; // horizontal velocity kept through a bounce
  private const float DebrisBounceAngDamp = 0.45f;  // tumble kept through a bounce
  private const float FootPitchBand = 0.5f;        // foot verts within this of the ankle plane are neither toe nor heel
  private const float SlopeSampleStep = 2.0f;      // terrain-normal sampling distance for the slope conform
  private const float SlopeConformMinNormalY = 0.94f; // normals above this (~20 deg or gentler) keep the corner rest
  private const float ConformAxisEpsilon = 1e-8f;  // conform rotation-axis degeneracy guard
  private const float ConformDropEpsilon = 0.001f; // re-seat drops at or below this are skipped
  private static StandardMaterial3D _socketMat;

  // Cap the wound a carved pod leaves in the core piece. The cut
  // classifies triangles by centroid, so the core's boundary and
  // the chunk's boundary are two different polylines a triangle
  // apart, sharing almost no vertices - the plate therefore fans
  // over the UNION of both rims (per slice, the boundary loop
  // nearest the chunk; the sculpt's other openings - the neck,
  // the waist - are separate loops and stay out). Each fan vert
  // keeps its own slice's bone data. The plate is an extra
  // surface of the core mesh: hidden inside the pod while it is
  // attached, it stays with the torso when the pod tears away
  // and reads as the empty socket (owner, 2026-11-22).
  private void AddSocketPlug(ArrayMesh coreMesh, Godot.Collections.Array coreArr, int[] coreIdx, Godot.Collections.Array podArr, int[] podIdx)
  {
    var pv = podArr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
    if (pv.Length == 0) { Dbg.Print($"{this._riderId} SOCKET skip: empty pod slice"); return; }
    var podC = Vector3.Zero;
    foreach (var p in pv)
    {
      podC += p;
    }

    podC /= pv.Length;
    float podR = 0f;
    foreach (var p in pv)
    {
      podR = Mathf.Max(podR, (p - podC).Length());
    }

    // Rim = the boundary loop of each slice that belongs to
    // THIS wound: walk each slice's boundary edges into
    // connected loops, then take the MATING pair - the core
    // loop and the pod loop whose centroids coincide (the
    // two rims of one wound sit on top of each other; the
    // neck, the waist, Enyo's missile-tube rings and stray
    // 2-vert debris loops do not mate with anything).
    // (The old rule - every boundary vert within podR + 0.35
    // of the centre, deduped at an absolute 0.02 - was
    // written in model units but runs on raw mesh-local
    // verts, whose scale differs per kit: on Prometheus it
    // merged the whole rim into 12 clusters and the fan
    // left see-through crescents along the wound, owner
    // report 2026-11-24. Loop pairing + radii relative to
    // podR are scale-free.)
    var rim = new List<(Vector3 Pos, Godot.Collections.Array Arr, int Src, int VertCount)>();
    // Loops are traced on POSITION keys, not vertex indices:
    // the kits carry split verts along the carve seam (same
    // position, several indices), so an index graph shatters
    // the rim into ~14-vert fragments - the fan then caps a
    // fragment and leaves the rest of the wound open (the
    // first loop-pairing build still read verts=14 in-game).
    // Welding by position (quantum 0.2% of podR) restores
    // the true loops; each key's first index stands as its
    // representative for bone data.
    List<(List<int> Comp, Vector3 Cen)> LoopsOf(Godot.Collections.Array arr, int[] idx)
    {
      var vs = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
      float q = Mathf.Max(podR * RimWeldFraction, RimWeldQuantumMin);
      var keyOf = new (int, int, int)[vs.Length];
      var repOf = new Dictionary<(int, int, int), int>();
      for (int i = 0; i < vs.Length; i++)
      {
        var key = ((int)Mathf.Round(vs[i].X / q), (int)Mathf.Round(vs[i].Y / q), (int)Mathf.Round(vs[i].Z / q));
        keyOf[i] = key;
        if (!repOf.ContainsKey(key))
        {
          repOf[key] = i;
        }
      }
      var use = new Dictionary<((int, int, int), (int, int, int)), int>();
      for (int t = 0; t + 2 < idx.Length; t += 3)
      {
        for (int k = 0; k < 3; k++)
        {
          var a = keyOf[idx[t + k]]; var b = keyOf[idx[t + (k + 1) % 3]];
          if (a.Equals(b))
          {
            continue;
          }

          var e = a.CompareTo(b) < 0 ? (a, b) : (b, a);
          use[e] = use.TryGetValue(e, out int cnt) ? cnt + 1 : 1;
        }
      }

      var adj = new Dictionary<(int, int, int), List<(int, int, int)>>();
      foreach (var kv in use)
      {
        if (kv.Value != 1)
        {
          continue;
        }

        if (!adj.TryGetValue(kv.Key.Item1, out var l1))
        {
          adj[kv.Key.Item1] = l1 = new List<(int, int, int)>();
        }

        if (!adj.TryGetValue(kv.Key.Item2, out var l2))
        {
          adj[kv.Key.Item2] = l2 = new List<(int, int, int)>();
        }

        l1.Add(kv.Key.Item2);
        l2.Add(kv.Key.Item1);
      }
      var loops = new List<(List<int>, Vector3)>();
      var seen = new HashSet<(int, int, int)>();
      foreach (var start in adj.Keys)
      {
        if (!seen.Add(start))
        {
          continue;
        }

        var comp = new List<int> { repOf[start] };
        var stack = new Stack<(int, int, int)>();
        stack.Push(start);
        var cc = vs[repOf[start]];
        while (stack.Count > 0)
        {
          var x = stack.Pop();
          foreach (var y in adj[x])
          {
            if (seen.Add(y)) { comp.Add(repOf[y]); cc += vs[repOf[y]]; stack.Push(y); }
          }
        }
        cc /= comp.Count;
        loops.Add((comp, cc));
      }
      return loops;
    }
    var coreLoops = LoopsOf(coreArr, coreIdx);
    var podLoops = LoopsOf(podArr, podIdx);
    (List<int> Comp, Vector3 Cen) coreBest = (null, Vector3.Zero), podBest = (null, Vector3.Zero);
    float pairD = float.MaxValue;
    foreach (var cl in coreLoops)
    {
      if (cl.Comp.Count < 8)
      {
        continue;
      }

      foreach (var pl in podLoops)
      {
        if (pl.Comp.Count < 8)
        {
          continue;
        }

        float d = (cl.Cen - pl.Cen).Length();
        if (d < pairD) { pairD = d; coreBest = cl; podBest = pl; }
      }
    }
    if (coreBest.Comp == null)
    {
      // No sane pair - fall back to each slice's loop
      // nearest the pod centre.
      float bd = float.MaxValue;
      foreach (var cl in coreLoops) { float d = (cl.Cen - podC).Length(); if (d < bd) { bd = d; coreBest = cl; } }
      bd = float.MaxValue;
      foreach (var pl in podLoops) { float d = (pl.Cen - podC).Length(); if (d < bd) { bd = d; podBest = pl; } }
    }
    if (coreBest.Comp != null)
    {
      var vs = coreArr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
      foreach (int vi in coreBest.Comp)
      {
        rim.Add((vs[vi], coreArr, vi, vs.Length));
      }
    }
    if (podBest.Comp != null)
    {
      var vs = podArr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
      foreach (int vi in podBest.Comp)
      {
        rim.Add((vs[vi], podArr, vi, vs.Length));
      }
    }
    if (rim.Count < 3) { Dbg.Print($"{this._riderId} SOCKET skip: rim {rim.Count} verts"); return; }

    // The two rims pass through nearly the same points; drop
    // near-duplicates or the fan fills with sliver triangles.
    // Threshold relative to the pod's own radius (scale-free).
    float dd = podR * RimDedupFraction;
    var dedup = new List<(Vector3 Pos, Godot.Collections.Array Arr, int Src, int VertCount)>();
    foreach (var r in rim)
    {
      bool dup = false;
      foreach (var q in dedup)
      {
        if (q.Pos.DistanceSquaredTo(r.Pos) < dd * dd) { dup = true; break; }
      }

      if (!dup)
      {
        dedup.Add(r);
      }
    }
    rim = dedup;
    if (rim.Count < MinSocketRimPoints) { Dbg.Print($"{this._riderId} SOCKET skip: rim {rim.Count} after dedupe"); return; }

    var cen = Vector3.Zero;
    foreach (var r in rim)
    {
      cen += r.Pos;
    }

    cen /= rim.Count;
    var axis = podC - cen;
    if (axis.Length() < SocketAxisEpsilon) { Dbg.Print($"{this._riderId} SOCKET skip: degenerate axis"); return; }
    axis = axis.Normalized();
    var u = (Mathf.Abs(axis.Y) < SocketUpDotMax ? Vector3.Up : Vector3.Right).Cross(axis).Normalized();
    var w = axis.Cross(u);
    rim.Sort((a, b) =>
    {
      var pa = a.Pos - cen; var pb = b.Pos - cen;
      return Mathf.Atan2(pa.Dot(w), pa.Dot(u)).CompareTo(Mathf.Atan2(pb.Dot(w), pb.Dot(u)));
    });

    int n = rim.Count;
    var verts = new Vector3[n + 1];
    for (int i = 0; i < n; i++)
    {
      verts[i] = rim[i].Pos;
    }

    verts[n] = cen;
    // Normal from the fan's total area vector (robust when
    // individual fan triangles are slivers); zero-area
    // triangles are dropped from the index list.
    var area = Vector3.Zero;
    for (int i = 0; i < n; i++)
    {
      area += (verts[i] - cen).Cross(verts[(i + 1) % n] - cen);
    }

    if (area.Length() < SocketFanEpsilon) { Dbg.Print($"{this._riderId} SOCKET skip: degenerate fan"); return; }
    var nrm = area.Normalized();
    bool flip = nrm.Dot(axis) < 0f;
    if (flip)
    {
      nrm = -nrm;
    }

    var idxL = new List<int>();
    for (int i = 0; i < n; i++)
    {
      int j = (i + 1) % n;
      var cr = (verts[i] - cen).Cross(verts[j] - cen);
      if (cr.Length() < SocketTriEpsilon)
      {
        continue;
      }

      idxL.Add(n);
      if (flip) { idxL.Add(j); idxL.Add(i); } else { idxL.Add(i); idxL.Add(j); }
    }
    if (idxL.Count < MinFanIndices) { Dbg.Print($"{this._riderId} SOCKET skip: all fan tris degenerate"); return; }
    var idx = idxL.ToArray();
    var nrms = new Vector3[n + 1];
    for (int i = 0; i <= n; i++)
    {
      nrms[i] = nrm;
    }

    var arr = new Godot.Collections.Array();
    arr.Resize((int)Mesh.ArrayType.Max);
    arr[(int)Mesh.ArrayType.Vertex] = verts;
    arr[(int)Mesh.ArrayType.Normal] = nrms;
    arr[(int)Mesh.ArrayType.Index] = idx;
    // Skin each fan vert with its own source vert's bone chunk;
    // the centroid takes the first rim vert's.
    foreach (int slot in new[] { (int)Mesh.ArrayType.Bones, (int)Mesh.ArrayType.Weights })
    {
      var o = new float[(n + 1) * 8];
      bool any = false;
      for (int i = 0; i <= n; i++)
      {
        var src = i < n ? rim[i] : rim[0];
        var sv = src.Arr[slot];
        if (sv.VariantType != Variant.Type.PackedFloat32Array)
        {
          continue;
        }

        var sf = sv.AsFloat32Array();
        int per = sf.Length / src.VertCount;
        if (per <= 0 || per > 8)
        {
          continue;
        }

        for (int k = 0; k < per; k++)
        {
          o[i * 8 + k] = sf[src.Src * per + k];
        }

        any = true;
      }
      if (any)
      {
        arr[slot] = o;
      }
    }
    _socketMat ??= new StandardMaterial3D
    {
      AlbedoColor = SocketPlateColor,
      Roughness = SocketPlateRoughness,
      Metallic = SocketPlateMetallic,
      // The plate is read at glancing angles from either
      // side of the wound - never let it cull away.
      CullMode = BaseMaterial3D.CullModeEnum.Disabled
    };
    int si = coreMesh.GetSurfaceCount();
    coreMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);
    coreMesh.SurfaceSetMaterial(si, _socketMat);
    Dbg.Print($"{this._riderId} SOCKET plug verts={n}");
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
      if (v.VariantType == Variant.Type.Nil)
      {
        continue;
      }

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
          for (int i = 0; i < src.Count; i++)
          {
            o[i] = a[src[i]];
          }

          return o;
        }
      case Variant.Type.PackedVector2Array:
        {
          var a = v.AsVector2Array();
          var o = new Vector2[src.Count];
          for (int i = 0; i < src.Count; i++)
          {
            o[i] = a[src[i]];
          }

          return o;
        }
      case Variant.Type.PackedColorArray:
        {
          var a = v.AsColorArray();
          var o = new Color[src.Count];
          for (int i = 0; i < src.Count; i++)
          {
            o[i] = a[src[i]];
          }

          return o;
        }
      case Variant.Type.PackedFloat32Array:
        {
          var a = v.AsFloat32Array();
          int per = srcCount > 0 ? a.Length / srcCount : 0;
          var o = new float[src.Count * per];
          for (int i = 0; i < src.Count; i++)
          {
            for (int k = 0; k < per; k++)
            {
              o[i * per + k] = a[src[i] * per + k];
            }
          }

          return o;
        }
      case Variant.Type.PackedInt32Array:
        {
          var a = v.AsInt32Array();
          int per = srcCount > 0 ? a.Length / srcCount : 0;
          var o = new int[src.Count * per];
          for (int i = 0; i < src.Count; i++)
          {
            for (int k = 0; k < per; k++)
            {
              o[i * per + k] = a[src[i] * per + k];
            }
          }

          return o;
        }
      case Variant.Type.PackedByteArray:
        {
          var a = v.AsByteArray();
          int per = srcCount > 0 ? a.Length / srcCount : 0;
          var o = new byte[src.Count * per];
          for (int i = 0; i < src.Count; i++)
          {
            for (int k = 0; k < per; k++)
            {
              o[i * per + k] = a[src[i] * per + k];
            }
          }

          return o;
        }
      default:
        return v;
    }
  }

  // ---------- queries ----------

  public bool Has(PartId id) => this._parts.ContainsKey(id);
  public bool PartHasVolumes(PartId id) => this._parts.TryGetValue(id, out var p) && p.Volumes.Count > 0;
  // Tolerant lookups: a chassis may simply not HAVE a part (the
  // Hector has no missile pods). Absent reads as destroyed / 0 HP /
  // null rather than throwing - a hard dictionary lookup here
  // crashed every frame for an Hector player and froze the camera
  // mid-duel (owner's report, 2026-10-30).
  public Part GetPart(PartId id) => this._parts.TryGetValue(id, out var p) ? p : null;
  public bool Alive(PartId id) => this._parts.TryGetValue(id, out var p) && !p.Destroyed;
  public float HpOf(PartId id) => this._parts.TryGetValue(id, out var p) ? p.Hp : 0f;
  public IEnumerable<Part> AllParts => this._parts.Values;

  private static float BoxSdf(Vector3 p, Volume v)
  {
    var q = (p - v.CenterB).Abs() - v.HalfB;
    float ox = Mathf.Max(q.X, 0f), oy = Mathf.Max(q.Y, 0f), oz = Mathf.Max(q.Z, 0f);
    float outside = Mathf.Sqrt(ox * ox + oy * oy + oz * oz);
    float inside = Mathf.Min(Mathf.Max(q.X, Mathf.Max(q.Y, q.Z)), 0f);
    return outside + inside;
  }

  // Nearest part (by per-mesh bone-space box SDF) to a world point.
  // Destroyed parts no longer exist on the rider and are skipped.
  public PartId AttributePoint(Vector3 world)
  {
    PartId best = PartId.Torso;
    float bestSd = float.MaxValue;
    var skelInv = this._skel.GlobalTransform.AffineInverse();
    var pSk = skelInv * world;
    var poseInv = new Dictionary<int, Transform3D>();
    foreach (var part in this._parts.Values)
    {
      if (part.Destroyed)
      {
        continue;
      }

      foreach (var vol in part.Volumes)
      {
        if (!poseInv.TryGetValue(vol.Bone, out var inv))
        {
          inv = this._skel.GetBoneGlobalPose(vol.Bone).AffineInverse();
          poseInv[vol.Bone] = inv;
        }
        float sd = BoxSdf(inv * pSk, vol);
        if (sd < bestSd) { bestSd = sd; best = part.Id; }
      }
    }
    return best;
  }

  // Ray vs the per-part bone-space boxes (slab test in each bone's
  // frame; the affine map preserves the ray parameter). Returns the
  // nearest (t, partId) or t < 0. Destroyed parts are gone and skipped.
  // The TORSO is excluded on purpose: the rider capsule is its hit
  // volume, and its core AABB is a wide slab that would otherwise
  // shadow the shoulder/arm boxes behind its corners.
  public (float T, PartId Part) RaycastParts(Vector3 origin, Vector3 dir, float maxDist)
  {
    float bestT = -1f;
    PartId bestPart = PartId.None;
    var skelInv = this._skel.GlobalTransform.AffineInverse();
    var oSk = skelInv * origin;
    var dSk = skelInv.Basis * dir;
    var poseInv = new Dictionary<int, Transform3D>();
    foreach (var part in this._parts.Values)
    {
      if (part.Destroyed || part.Id == PartId.Torso)
      {
        continue;
      }

      foreach (var vol in part.Volumes)
      {
        if (!poseInv.TryGetValue(vol.Bone, out var inv))
        {
          inv = this._skel.GetBoneGlobalPose(vol.Bone).AffineInverse();
          poseInv[vol.Bone] = inv;
        }
        float t = RayBox(inv * oSk, inv.Basis * dSk, vol);
        if (t > RayMinT && t <= maxDist && (bestT < 0f || t < bestT))
        {
          bestT = t;
          bestPart = part.Id;
        }
      }
    }
    return (bestT, bestPart);
  }

  private static float RayBox(Vector3 o, Vector3 d, Volume v)
  {
    float tmin = 0f, tmax = float.MaxValue;
    for (int i = 0; i < 3; i++)
    {
      float lo = v.CenterB[i] - v.HalfB[i];
      float hi = v.CenterB[i] + v.HalfB[i];
      if (Mathf.Abs(d[i]) < RayParallelEpsilon)
      {
        if (o[i] < lo || o[i] > hi)
        {
          return -1f;
        }
      }
      else
      {
        float t1 = (lo - o[i]) / d[i];
        float t2 = (hi - o[i]) / d[i];
        if (t1 > t2)
        {
          (t1, t2) = (t2, t1);
        }

        if (t1 > tmin)
        {
          tmin = t1;
        }

        if (t2 < tmax)
        {
          tmax = t2;
        }

        if (tmax < tmin)
        {
          return -1f;
        }
      }
    }
    return tmin;
  }

  // Aim point for tests and targeting: the world center of the part's
  // LARGEST volume. The mean of all volume centers can land in empty
  // space for multi-piece parts (head + a thin antenna spike), where
  // attribution would rightly name a different part.
  public Vector3 PartAimPoint(PartId partId)
  {
    var part = this._parts[partId];
    Volume best = null;
    float bestSize = -1f;
    foreach (var vol in part.Volumes)
    {
      float s = vol.HalfB.X * vol.HalfB.Y * vol.HalfB.Z;
      if (s > bestSize) { bestSize = s; best = vol; }
    }
    if (best == null)
    {
      return this._walker.RootPos;
    }
    // Volumes on skeleton-less binds (Bone -1, see
    // MakeVolume) cannot be posed; their centre is the
    // walker's root rather than an identity-pose guess.
    if (best.Bone < 0)
    {
      return this._walker.RootPos;
    }

    return this._skel.GlobalTransform * (this._skel.GetBoneGlobalPose(best.Bone) * best.CenterB);
  }

  // World-space center of a part (mean of its volume centers).
  public Vector3 PartCenterWorld(PartId partId)
  {
    var part = this._parts[partId];
    if (part.Volumes.Count == 0)
    {
      return this._walker.RootPos;
    }

    var sum = Vector3.Zero;
    var skelG = this._skel.GlobalTransform;
    int n = 0;
    foreach (var vol in part.Volumes)
    {
      // Skip skeleton-less binds (Bone -1): posing them
      // logs an engine error and contributes an
      // identity-space centre - worse than leaving the
      // volume out of the mean.
      if (vol.Bone < 0)
      {
        continue;
      }

      sum += skelG * (this._skel.GetBoneGlobalPose(vol.Bone) * vol.CenterB);
      n++;
    }
    return n > 0 ? sum / n : this._walker.RootPos;
  }

  // ---------- damage / break-off / debris ----------

  private readonly List<Debris> _debris = new();
  public IReadOnlyList<Debris> DebrisList => this._debris;

  // Apply damage to a part's own HP. Returns true when this hit broke
  // the part off. Torso is pool-owned (MaxHp 0) and never breaks here.
  public bool DamagePart(PartId partId, float dmg)
  {
    var part = this._parts[partId];
    if (part.Destroyed || part.MaxHp <= 0f)
    {
      return false;
    }

    part.Hp = Mathf.Max(part.Hp - dmg, 0f);
    Dbg.Print($"BRK DMG {this._riderId} part={partId} dmg={dmg:F1} partHp={part.Hp:F1}/{part.MaxHp:F0}");
    if (part.Hp > 0f)
    {
      return false;
    }

    var center = this.PartCenterWorld(partId);
    if (partId == PartId.LegL || partId == PartId.LegR || partId == PartId.Head)
    {
      // Owner's rule: legs NEVER fall off. A destroyed leg stays
      // attached - darkened and deformed - and the rider limps.
      // The head stays on too (owner, 2026-10-31): a shot-off
      // head left the torso a hollow shell you could see
      // through from behind. A wrecked head stays attached,
      // charred and lolling, like the legs.
      this.WreckPart(part);
      Dbg.Print($"BRK WRECK {this._riderId} part={partId} '{part.Display}' (stays attached, rider limps)");
    }
    else
    {
      this.BreakPart(part, center);
      Dbg.Print($"BRK BREAK {this._riderId} part={partId} '{part.Display}' at {center}");
    }
    this.OnBreak?.Invoke(partId, center);
    return true;
  }

  private static readonly StandardMaterial3D WreckOverlay = new()
  {
    AlbedoColor = new Color(0.05f, 0.04f, 0.03f, 0.62f),
    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
    Roughness = 1f
  };

  // The leg-wreck look: a charred translucent overlay darkens the
  // leg's own texture, and the meshes are crushed slightly (shorter,
  // wider) and tilted - deformation without detachment.
  // The wreck deformation (a slight crush and tilt under the
  // char overlay) is applied in each mesh's own space, pivoting
  // on the mesh's own centre. It used to ride on the instance's
  // Scale/Rotation, which pivot on the MODEL ORIGIN at ground
  // level: a wrecked head (centre ~13u up) was scaled ~1u down
  // into the torso and swung ~1u sideways off the neck - "half
  // the head gone, the rest floating" (owner, 2026-11-15) - and
  // wrecked legs sheared away from the pelvis the same way.
  private void WreckPart(Part part)
  {
    part.Destroyed = true;
    part.Wrecked = true;
    float tilt = part.Id == PartId.LegL ? WreckTiltRad : -WreckTiltRad;
    var rs = Basis.FromEuler(new Vector3(WreckPitchRad, 0f, tilt))
        .Scaled(new Vector3(WreckScaleXZ, WreckScaleY, WreckScaleXZ));
    foreach (var rec in part.Meshes)
    {
      rec.SavedOverlay = rec.Mi.MaterialOverlay;
      var pivot = rec.Mi.Mesh.GetAabb().GetCenter();
      var d = new Transform3D(rs, pivot - rs * pivot);
      // Audit (owner report, 2026-11-23: a chunk of a
      // wrecked Hector leg hung in the air beside it):
      // the deformation must move a mesh's skinned centre
      // by centimetres, not metres - measure it in the
      // same skinning chain BreakPart uses and log any
      // mesh the wreck throws off its part.
      var bind = rec.OrigSkin != null && rec.BindIndex >= 0
          ? rec.OrigSkin.GetBindPose(rec.BindIndex)
          : this._skel.GetBoneGlobalRest(rec.Bone).AffineInverse();
      var skin = this._skel.GetBoneGlobalPose(rec.Bone) * bind;
      var cBefore = (rec.Mi.GlobalTransform * skin) * pivot;
      var after = new Transform3D(rec.Mi.GlobalTransform.Basis * (rec.OrigLocal * d).Basis,
          rec.Mi.GlobalTransform.Origin + rec.Mi.GlobalTransform.Basis * (rec.OrigLocal * d).Origin);
      var cAfter = (after * skin) * pivot;
      float disp = cBefore.DistanceTo(cAfter);
      if (disp > WreckAuditMaxDisp)
      {
        Dbg.Print($"BRK WRECKAUDIT {this._riderId} {part.Id} mesh={rec.Mi.Name} disp={disp:F2}u bone={rec.BoneName} origLocalOrigin={rec.OrigLocal.Origin} aabbC={pivot}");
      }

      rec.Mi.Transform = rec.OrigLocal * d;
      rec.Mi.MaterialOverlay = WreckOverlay;
    }
  }

  private void UnWreck(Part part)
  {
    if (!part.Wrecked)
    {
      return;
    }

    part.Wrecked = false;
    foreach (var rec in part.Meshes)
    {
      rec.Mi.MaterialOverlay = rec.SavedOverlay;
      rec.Mi.Transform = rec.OrigLocal;
    }
  }

  // Death explosion: EVERY part detaches into debris at once -
  // including the torso core, which normal damage can never break
  // (its MaxHp 0 marks it as the pool itself). OnBreak still fires
  // per part; the owner suppresses the usual consequences while a
  // whole rider is coming apart.
  public void ExplodeAll()
  {
    foreach (var part in this._parts.Values)
    {
      if (part.Destroyed && !part.Wrecked)
      {
        continue;
      }

      if (part.Wrecked)
      {
        this.UnWreck(part); // debris piece starts clean
      }

      part.Hp = 0f;
      var center = this.PartCenterWorld(part.Id);
      this.BreakPart(part, center);
      this.OnBreak?.Invoke(part.Id, center);
    }
  }

  // Detach the part's meshes from the skeleton and turn them into
  // falling debris. The skinless-equivalent transform
  // (meshGlobal * bonePose * bindPose) reproduces the skinned vertex
  // positions at the detach instant, so nothing visually jumps; after
  // that the piece is plain scenery under simple gravity.
  private void BreakPart(Part part, Vector3 center)
  {
    part.Destroyed = true;
    var container = new Node3D { Name = $"{this._riderId}_Debris_{part.Id}" };
    this._walker.GetParent().AddChild(container);
    container.GlobalTransform = new Transform3D(Basis.Identity, center);
    foreach (var rec in part.Meshes)
    {
      var mi = rec.Mi;
      var bind = rec.OrigSkin != null && rec.BindIndex >= 0
          ? rec.OrigSkin.GetBindPose(rec.BindIndex)
          : this._skel.GetBoneGlobalRest(rec.Bone).AffineInverse();
      var eff = mi.GlobalTransform * (this._skel.GetBoneGlobalPose(rec.Bone) * bind);
      mi.GetParent().RemoveChild(mi);
      container.AddChild(mi);
      mi.GlobalTransform = eff;
      mi.Skin = null;
      mi.Skeleton = new NodePath("");
      rec.Detached = true;
    }
    var away = center - this._walker.RootPos;
    away.Y = 0f;
    if (away.LengthSquared() < DebrisMinAwaySq)
    {
      away = RiderWalker.HeadingVec(this._walker.TorsoYawWorld);
    }

    away = away.Normalized();
    var d = new Debris
    {
      Part = part,
      Node = container,
      Vel = away * DebrisLaunchAway + Vector3.Up * DebrisLaunchUp,
      AngAxisW = Vector3.Up.Cross(away).Normalized(),
      AngSpeed = DebrisLaunchAngSpeed
    };
    this._debris.Add(d);
  }

  // Probe dump: every debris piece's rest state against the
  // terrain under its own box (owner report, 2026-11-23: a
  // chunk hanging in the air beside a walking Hector).
  public void DumpDebris(string tag)
  {
    foreach (var d in this._debris)
    {
      var box = WorldAabb(d.Node);
      var low = LowestCorner(box);
      Dbg.Print($"DEBRISDUMP {tag} {this._riderId} part={d.Part.Id} settled={d.Settled} pos={d.Node.GlobalPosition} aabbMinY={box.Position.Y:F2} lowCornerY={low.Y:F2} groundAtLow={Main.HeightAt(low.X, low.Z):F2} groundAtCenter={Main.HeightAt(box.GetCenter().X, box.GetCenter().Z):F2}");
    }
    if (this._debris.Count == 0)
    {
      Dbg.Print($"DEBRISDUMP {tag} {this._riderId} (no debris)");
    }
  }

  public void StepDebris(float dt)
  {
    if (dt <= 0f)
    {
      return;
    }
    // Substep at 120 Hz like the gait: with a raw frame dt the
    // gravity increment per step (20*dt) can exceed the settle
    // threshold outright - at dt 0.2 a resting piece gains
    // -4 u/s every frame, bounces at 0.22 restitution forever,
    // and never settles (measured: breakable POD phase timing
    // out at slow render rates; the same class of bug as the
    // gait dt clamp found in the terrain stage).
    int n = Mathf.Max(1, Mathf.CeilToInt(dt / DebrisSubstepSeconds));
    float h = dt / n;
    for (int i = 0; i < n; i++)
    {
      this.StepDebrisStep(h);
    }
  }

  private void StepDebrisStep(float dt)
  {
    foreach (var d in this._debris)
    {
      if (d.Settled)
      {
        continue;
      }

      d.Vel.Y -= DebrisGravity * dt;
      d.Node.GlobalPosition += d.Vel * dt;
      if (d.AngSpeed > DebrisMinAngSpeed)
      {
        var gt = d.Node.GlobalTransform;
        var nb = new Basis(new Quaternion(d.AngAxisW, d.AngSpeed * dt)) * gt.Basis;
        d.Node.GlobalTransform = new Transform3D(nb, gt.Origin);
      }
      var box = WorldAabb(d.Node);
      // Contact is judged at the AABB's lowest corner: on a slope the
      // downhill corner touches first and is where the piece rests.
      Vector3 low = LowestCorner(box);
      float groundY = Main.HeightAt(low.X, low.Z);
      d.RestMinY = box.Position.Y;
      d.RestGroundY = Main.HeightAt(box.GetCenter().X, box.GetCenter().Z);
      if (low.Y <= groundY + DebrisContactSkin)
      {
        d.Node.GlobalPosition += Vector3.Up * (groundY - low.Y);
        d.RestMinY = box.Position.Y + (groundY - low.Y);
        if (d.Vel.Y < -DebrisBounceMinSpeed)
        {
          d.Vel.Y = -d.Vel.Y * DebrisRestitution; // one bounce
          d.Vel.X *= DebrisBounceFriction;
          d.Vel.Z *= DebrisBounceFriction;
          d.AngSpeed *= DebrisBounceAngDamp;
        }
        else
        {
          this.ConformDebrisToSlope(d);
          d.Settled = true;
          d.Vel = Vector3.Zero;
          d.AngSpeed = 0f;
          Dbg.Print($"BRK DEBRIS {this._riderId} {d.Part.Id} settled pos={d.Node.GlobalPosition} minY={d.RestMinY:F3} groundAtCenter={d.RestGroundY:F3}");
        }
      }
    }
  }

  // Dev audit (gait-shot): the VISUAL clearance of a leg's foot -
  // the lowest skinned vertex of the ankle-bound meshes in the
  // leg part, measured above the terrain under that vertex. The
  // walker's sole point is derived from the ankle's bind-pose
  // projection; this measures what is actually rendered, so a
  // bind/geometry mismatch shows up here even when the sole
  // solver itself is exact.
  public float FootVisualGap(PartId partId)
  {
    var part = this._parts[partId];
    string ankle = partId == PartId.LegL ? Bones.AnkleL : Bones.AnkleR;
    float best = float.MaxValue;
    foreach (var rec in part.Meshes)
    {
      if (rec.Detached || rec.BoneName != ankle || rec.Mi.Mesh == null)
      {
        continue;
      }

      var bind = rec.OrigSkin != null && rec.BindIndex >= 0
                ? rec.OrigSkin.GetBindPose(rec.BindIndex)
                : this._skel.GetBoneGlobalRest(rec.Bone).AffineInverse();
      var eff = rec.Mi.GlobalTransform * (this._skel.GetBoneGlobalPose(rec.Bone) * bind);
      for (int s = 0; s < rec.Mi.Mesh.GetSurfaceCount(); s++)
      {
        var verts = rec.Mi.Mesh.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        foreach (var v in verts)
        {
          var w = eff * v;
          float gap = w.Y - Main.HeightAt(w.X, w.Z);
          if (gap < best)
          {
            best = gap;
          }
        }
      }
    }
    return best;
  }

  // Dev audit (gait-shot): the same visual clearance split by
  // foot end - the lowest toe-side vertex (forward of the ankle)
  // vs the lowest heel-side vertex. A foot can touch at the toe
  // (gap 0) while standing pitched on its toes with the heel
  // high; the split reads that pitch as two numbers.
  public (float Toe, float Heel) FootPitchGaps(PartId partId, Vector3 ankleW, Vector3 fwdW)
  {
    var part = this._parts[partId];
    string ankle = partId == PartId.LegL ? Bones.AnkleL : Bones.AnkleR;
    float toe = float.MaxValue, heel = float.MaxValue;
    foreach (var rec in part.Meshes)
    {
      if (rec.Detached || rec.BoneName != ankle || rec.Mi.Mesh == null)
      {
        continue;
      }

      var bind = rec.OrigSkin != null && rec.BindIndex >= 0
                ? rec.OrigSkin.GetBindPose(rec.BindIndex)
                : this._skel.GetBoneGlobalRest(rec.Bone).AffineInverse();
      var eff = rec.Mi.GlobalTransform * (this._skel.GetBoneGlobalPose(rec.Bone) * bind);
      for (int s = 0; s < rec.Mi.Mesh.GetSurfaceCount(); s++)
      {
        var verts = rec.Mi.Mesh.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        foreach (var v in verts)
        {
          var w = eff * v;
          float fwd = (w - ankleW).Dot(fwdW);
          if (Mathf.Abs(fwd) < FootPitchBand)
          {
            continue;
          }

          float gap = w.Y - Main.HeightAt(w.X, w.Z);
          if (fwd > 0f)
          {
            if (gap < toe)
            {
              toe = gap;
            }
          }
          else if (gap < heel)
          {
            heel = gap;
          }
        }
      }
    }
    return (toe, heel);
  }

  public Debris DebrisOf(PartId partId)
  {
    foreach (var d in this._debris)
    {
      if (d.Part.Id == partId)
      {
        return d;
      }
    }

    return null;
  }

  // Resting check for the harness: the part's debris is settled and its
  // world AABB's lowest corner sits on the terrain sampled at that
  // corner (the physically meaningful contact on a slope).
  public bool DebrisRestOk(PartId partId, float tol, out float minY, out float groundY)
  {
    minY = 0f; groundY = 0f;
    var d = this.DebrisOf(partId);
    if (d == null || !d.Settled)
    {
      return false;
    }

    var box = WorldAabb(d.Node);
    var low = LowestCorner(box);
    minY = low.Y;
    groundY = Main.HeightAt(low.X, low.Z);
    return Mathf.Abs(low.Y - groundY) <= tol;
  }

  // Rebind every detached mesh to the skeleton and refill all part HP.
  // Safe to call repeatedly and mid-flight.
  public void RestoreAll()
  {
    foreach (var d in this._debris)
    {
      foreach (var rec in d.Part.Meshes)
      {
        var mi = rec.Mi;
        mi.GetParent()?.RemoveChild(mi);
        rec.OrigParent.AddChild(mi);
        mi.Transform = rec.OrigLocal;
        mi.Skin = rec.OrigSkin;
        mi.Skeleton = rec.OrigSkeleton;
        rec.Detached = false;
      }
      d.Node.QueueFree();
    }
    this._debris.Clear();
    foreach (var part in this._parts.Values)
    {
      if (part.Wrecked)
      {
        this.UnWreck(part);
      }

      part.Hp = part.MaxHp;
      part.Destroyed = false;
    }
  }

  private static Vector3 CornerOf(Aabb box, int i) => new Vector3(
      (i & 1) == 0 ? box.Position.X : box.End.X,
      (i & 2) == 0 ? box.Position.Y : box.End.Y,
      (i & 4) == 0 ? box.Position.Z : box.End.Z);

  private static Vector3 LowestCorner(Aabb box)
  {
    Vector3 best = box.Position;
    float bestY = float.MaxValue;
    for (int i = 0; i < 8; i++)
    {
      var c = CornerOf(box, i);
      if (c.Y < bestY) { bestY = c.Y; best = c; }
    }
    return best;
  }

  // A piece settles at its first AABB-corner touch, frozen in
  // whatever tumble orientation it had: on a steep slope the
  // rest of the piece hangs in the air over the downhill side
  // (owner, 2026-11-22: a destroyed enemy floated above the
  // snow). On steep ground only, rotate the settled piece
  // about its contact corner until world down meets the
  // terrain normal, then re-seat it: lift out of any corner
  // penetration, drop the lowest corner back onto its own
  // ground. Gentle ground keeps the original rest untouched.
  private void ConformDebrisToSlope(Debris d)
  {
    var box = WorldAabb(d.Node);
    var centre = box.GetCenter();
    float e = SlopeSampleStep;
    float hx = (Main.HeightAt(centre.X + e, centre.Z) - Main.HeightAt(centre.X - e, centre.Z)) / (2f * e);
    float hz = (Main.HeightAt(centre.X, centre.Z + e) - Main.HeightAt(centre.X, centre.Z - e)) / (2f * e);
    var n = new Vector3(-hx, 1f, -hz).Normalized();
    if (n.Y > SlopeConformMinNormalY)
    {
      return; // under ~20 degrees the corner rest reads fine
    }

    var low = LowestCorner(box);
    var axis = Vector3.Up.Cross(n);
    if (axis.LengthSquared() < ConformAxisEpsilon)
    {
      return;
    }

    float ang = Mathf.Acos(Mathf.Clamp(Vector3.Up.Dot(n), -1f, 1f));
    var q = new Quaternion(axis.Normalized(), ang);
    var gt = d.Node.GlobalTransform;
    d.Node.GlobalTransform = new Transform3D(new Basis(q) * gt.Basis, low + q * (gt.Origin - low));
    box = WorldAabb(d.Node);
    float lift = 0f;
    for (int i = 0; i < 8; i++)
    {
      var c = CornerOf(box, i);
      lift = Mathf.Max(lift, Main.HeightAt(c.X, c.Z) - c.Y);
    }
    if (lift > 0f) { d.Node.GlobalPosition += Vector3.Up * lift; box = WorldAabb(d.Node); }
    low = LowestCorner(box);
    float drop = low.Y - Main.HeightAt(low.X, low.Z);
    if (Mathf.Abs(drop) > ConformDropEpsilon)
    {
      d.Node.GlobalPosition -= Vector3.Up * drop;
    }

    var fb = WorldAabb(d.Node);
    d.RestMinY = fb.Position.Y;
    d.RestGroundY = Main.HeightAt(fb.GetCenter().X, fb.GetCenter().Z);
  }

  private static Aabb WorldAabb(Node node)
  {
    var box = new Aabb();
    bool first = true;
    CollectAabb(node, ref box, ref first);
    return box;
  }

  private static void CollectAabb(Node node, ref Aabb box, ref bool first)
  {
    if (node is MeshInstance3D mi)
    {
      var local = mi.GetAabb();
      var gt = mi.GlobalTransform;
      for (int i = 0; i < 8; i++)
      {
        var corner = new Vector3(
            (i & 1) == 0 ? local.Position.X : local.End.X,
            (i & 2) == 0 ? local.Position.Y : local.End.Y,
            (i & 4) == 0 ? local.Position.Z : local.End.Z);
        var w = gt * corner;
        if (first) { box = new Aabb(w, Vector3.Zero); first = false; }
        else
        {
          box = box.Expand(w);
        }
      }
    }
    foreach (var child in node.GetChildren())
    {
      CollectAabb(child, ref box, ref first);
    }
  }
}
