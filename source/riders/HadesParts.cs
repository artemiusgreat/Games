using Godot;
using System.Collections.Generic;

namespace Titans;

// Hades: breakable-part layout - which meshes form which
// part, the torso carve regions and the per-part HP. One file
// per rider (owner, 2026-11-27), structured like the maps;
// RiderParts keeps only the shared part machinery and calls
// this through the RiderLayouts factory.
public static class HadesParts
{
  public static void Build(RiderParts parts, Dictionary<string, MeshInstance3D> byName)
  {
    // Merged re-import (owner's part-merged FBX, 2026-11-21): 19 meshes instead of
    // 215, rig transferred from the previous build by vertex-position match (max
    // deviation 0.08u). Layout claims the merged names; the left-shoulder pod
    // cluster stays separate, as before.
    // Owner's own split (Hades.fbx, 2026-11-19), replacing
    // the Tripo island build: 12 named part groups with
    // every fragment parented under them; the build binds
    // each mesh rigidly to its group root's bone (no
    // skeleton in his file - the rig is authored at his
    // groups' measured joints). No arm adjustment: his
    // arm positioning ships as modelled. Two name calls:
    // the head is the cockpit assembly grouped under
    // Torso (its own mesh is the fin cap, tripo_part_16
    // the canopy block, 145/148 the cheek guards, 179
    // the top cap - isolation-verified), bound to the
    // head bone; the sculpt has no missile pod, so the
    // left shoulder's top cap cluster (142 + the small
    // plates above the pauldron) serves as pod_L - it
    // binds shoulder_L and rides the arm.
    parts.AddPart(PartId.Torso, "TORSO", 0f, byName, "Pelvis", "Torso");
    parts.AddPart(PartId.PodL, "LEFT SHOULDER", 45f, byName, "tripo_part_142", "tripo_part_4_001");
    parts.AddPart(PartId.Head, "HEAD", 50f, byName, "Torso_head", "tripo_part_145", "tripo_part_148", "tripo_part_16", "tripo_part_179");
    parts.AddPart(PartId.ArmL, "LEFT ARM", 90f, byName, "Left_Cannon", "Left_Shoulder");
    parts.AddPart(PartId.ArmR, "RIGHT ARM", 90f, byName, "Right_Cannon", "Right_Shoulder");
    parts.AddPart(PartId.LegL, "LEFT LEG", 90f, byName, "Left_Foot", "Left_Hip", "Left_Shin");
    parts.AddPart(PartId.LegR, "RIGHT LEG", 90f, byName, "Right_Foot", "Right_Hip", "Right_Shin");
    parts.AttachDebris(byName);
    // Fused-miniature build: parts render double-sided (open seams).
    parts.DoubleSidedMaterials = true;
  }
}
