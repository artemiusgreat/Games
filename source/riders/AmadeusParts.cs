using Godot;
using System.Collections.Generic;
using Titans.Definitions;

namespace Titans;

// Amadeus: breakable-part layout - which meshes form which
// part, the torso carve regions and the per-part HP. One file
// per rider (owner, 2026-11-27), structured like the maps;
// RiderParts keeps only the shared part machinery and calls
// this through the RiderLayouts factory.
public static class AmadeusParts
{
  public static void Build(RiderParts parts, Dictionary<string, MeshInstance3D> byName)
  {
    // Owner's merged re-split (Amadeus_0_dz4x, 2026-11-21):
    // the same geometry as the 2026-11-19 split (vertex-exact,
    // 39,444 tris) with each part group's fragments merged
    // into a single mesh per body part - 14 meshes instead of
    // 244. The rig is the shipped build's armature, weights
    // transferred by exact vertex position; the head cluster
    // (ex tripo_part_10/11/124/169) and the right-shoulder
    // launcher cluster (ex tripo_part_88 + the 33-38 tube row
    // + 118/159/202/202 fittings) stay separate meshes so
    // both parts still break and move as before. His right
    // leg groups now carry proper Right_* names.
    parts.AddPart(PartId.Torso, "TORSO", 0f, byName, "Pelvis", "Torso");
    parts.AddPart(PartId.PodR, "RIGHT SHOULDER", 45f, byName, "Pod_R");
    parts.AddPart(PartId.Head, "HEAD", 50f, byName, "Head");
    parts.AddPart(PartId.ArmL, "LEFT ARM", 90f, byName, "Left_Cannon", "Left_Shoulder");
    parts.AddPart(PartId.ArmR, "RIGHT ARM", 90f, byName, "Right_Cannon", "Right_Shoulder");
    parts.AddPart(PartId.LegL, "LEFT LEG", 90f, byName, "Left_Foot", "Left_Hip", "Left_Shin");
    parts.AddPart(PartId.LegR, "RIGHT LEG", 90f, byName, "Right_Foot", "Right_Hip", "Right_Shin");
    parts.AttachDebris(byName);
    // Fused-miniature build: parts render double-sided (open seams).
    parts.DoubleSidedMaterials = true;
  }
}
