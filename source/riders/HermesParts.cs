using Godot;
using System.Collections.Generic;

// Hermes: breakable-part layout - which meshes form which
// part, the torso carve regions and the per-part HP. One file
// per rider (owner, 2026-11-27), structured like the maps;
// RiderParts keeps only the shared part machinery and calls
// this through the RiderLayouts factory.
public static class HermesParts
{
    public static void Build(RiderParts parts, Dictionary<string, MeshInstance3D> byName)
    {
        // Merged re-import (owner's part-merged FBX, 2026-11-21): 14 meshes instead of
        // 210, vertex-exact against the previous build. His export was only partly
        // merged (~115 tripo fragments still separate, some skinned to his own
        // armature, which was discarded); the transfer folded them into the named
        // parts by bone and part. Layout claims the merged names.
        // Owner's own grouping (Hermes-Clean.fbx, 2026-11-18,
        // replacing the Tripo island build): 12 named part
        // objects with the fragments parented under them; the
        // build binds every fragment rigidly to its root
        // object's bone on the file's own Mixamo skeleton
        // (renamed to the game bones), so the safety net below
        // files each unclaimed fragment into the part its bone
        // belongs to. Head and the LRM rack bind torso like
        // the rest of the chest, so they are claimed by name:
        // the rack is the honeycomb cells and housing over
        // the left shoulder, the head is the cockpit cap and
        // antennae on top of the torso.
        parts.AddPart(PartId.Torso, "TORSO", 0f, byName, "Pelvis", "Torso");
        parts.AddPart(PartId.PodL, "LRM RACK", 45f, byName, "tripo_part_34");
        parts.AddPart(PartId.Head, "HEAD", 50f, byName, "tripo_part_4");
        parts.AddPart(PartId.ArmL, "LEFT ARM", 90f, byName, "Left_Cannon", "Left_Shoulder");
        parts.AddPart(PartId.ArmR, "RIGHT ARM", 90f, byName, "Right_Cannon", "Right_Shoulder");
        parts.AddPart(PartId.LegL, "LEFT LEG", 90f, byName, "Left_Foot", "Left_Hip", "Left_Shin");
        parts.AddPart(PartId.LegR, "RIGHT LEG", 90f, byName, "Right_Foot", "Right_Hip", "Right_Shin");
        parts.AttachDebris(byName);
        // Fused-miniature build: parts render double-sided (open seams).
        parts.DoubleSidedMaterials = true;
    }
}
