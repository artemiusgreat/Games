using Godot;
using System.Collections.Generic;

// Ares: breakable-part layout - which meshes form which
// part, the torso carve regions and the per-part HP. One file
// per rider (owner, 2026-11-27), structured like the maps;
// RiderParts keeps only the shared part machinery and calls
// this through the RiderLayouts factory.
public static class AresParts
{
    public static void Build(RiderParts parts, Dictionary<string, MeshInstance3D> byName)
    {
        // Merged re-import (owner's part-merged FBX, 2026-11-21): 18 meshes instead of
        // 265, vertex-exact against the previous build. Layout claims the merged
        // names; the head is still carved from Torso by SplitTorso below.
        // Owner's own split (Ares.fbx, 2026-11-18), replacing
        // the Tripo island build: 12 named part objects with
        // every fragment parented under them; the build binds
        // each mesh rigidly to its root object's bone on the
        // file's own Mixamo skeleton, so the safety net files
        // unclaimed fragments by bone. No arm adjustment: the
        // sculpt's arms are modelled bent at 90 degrees, guns
        // forward (owner's rule for replaced models). Two
        // name claims: his "Torso" object IS the head assembly
        // (dome, face guard, chin) riding on a small sternum
        // tab - SplitTorso carves the head off the tab (clean
        // triangle gap at file z 0.834-0.855; SplitTorso
        // classifies in the file's own frame, and this file is
        // 1.0 tall, so the band is 0.845), and the face
        // plates plus the two back-pylon panels the previous
        // build counted as head are claimed by name. The pod
        // is the left shoulder drum group (the previous
        // build's pod was the left shoulder cap assembly):
        // the drum and its cradle children bind shoulder_L
        // but are claimed here by name.
        parts.SplitTorso(byName, new (PartId, System.Func<Vector3, bool>)[]
        {
            (PartId.Head, p => p.Y >= 0.845f),
        });
        parts.AddPart(PartId.Torso, "TORSO", 0f, byName, "Pelvis", "TorsoCore");
        parts.AddPart(PartId.PodL, "LEFT SHOULDER", 45f, byName, "Left_Shoulder");
        parts.AddPart(PartId.Head, "HEAD", 50f, byName, "TorsoHead", "tripo_part_132", "tripo_part_136", "tripo_part_143", "tripo_part_153", "tripo_part_154", "tripo_part_181");
        parts.AddPart(PartId.ArmL, "LEFT ARM", 90f, byName, "Left_Cannon");
        parts.AddPart(PartId.ArmR, "RIGHT ARM", 90f, byName, "Right_Cannon", "Right_Shoulder");
        parts.AddPart(PartId.LegL, "LEFT LEG", 90f, byName, "Left_Foot", "Left_Hip", "Left_Shin");
        parts.AddPart(PartId.LegR, "RIGHT LEG", 90f, byName, "Right_Foot", "Right_Hip", "Right_Shin");
        parts.AttachDebris(byName);
        // Fused-miniature build: parts render double-sided (open seams).
        parts.DoubleSidedMaterials = true;
    }
}
