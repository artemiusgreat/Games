using Godot;
using System.Collections.Generic;

// Apollo: breakable-part layout - which meshes form which
// part, the torso carve regions and the per-part HP. One file
// per rider (owner, 2026-11-27), structured like the maps;
// RiderParts keeps only the shared part machinery and calls
// this through the RiderLayouts factory.
public static class ApolloParts
{
    public static void Build(RiderParts parts, Dictionary<string, MeshInstance3D> byName)
    {
        // Merged re-import (owner's part-merged FBX, 2026-11-21): 20 meshes instead of
        // 221. Binding and UVs transferred PER FACE from the previous build (each of
        // his faces takes the bone and UVs of the nearest previous-build triangle),
        // after his export arrived with its UV maps missing (the rider rendered
        // black) and a shell-level bind left joint-spanning geometry on one bone.
        // Layout claims the merged names; the head is still carved from Torso by
        // SplitTorso below.
        // Owner's own split (Apollo-Clean.fbx, 2026-11-17),
        // used AS IS at his instruction: no object moved,
        // renamed, joined or regrouped - the build only adds
        // the armature and a rigid bind per object, so the
        // meshes carry HIS object names and the parts claim
        // them by those names. The 216 ungrouped fragments
        // keep their own objects too; the safety net below
        // sweeps them into their nearest part at load. The
        // helmet is fused into his Torso object, so the head
        // is carved here (band = the build's head vertex
        // group: |x| <= 1.45, top of the torso). The
        // Shoulder_Cannon he split out is the missile pod.
        parts.SplitTorso(byName, new (PartId, System.Func<Vector3, bool>)[]
        {
            (PartId.Head, p => Mathf.Abs(p.X) <= 1.45f && p.Y >= 11.30f),
        });
        parts.AddPart(PartId.Torso, "TORSO", 0f, byName, "Pelvis", "Pelvis__torso", "TorsoCore");
        parts.AddPart(PartId.PodL, "SHOULDER CANNON", 45f, byName, "Shoulder_Cannon");
        parts.AddPart(PartId.Head, "HEAD", 50f, byName, "TorsoHead");
        parts.AddPart(PartId.ArmL, "LEFT ARM", 90f, byName, "Left_Elbow", "Left_Elbow__shoulder_L", "Left_Hand", "Left_Shoulder");
        parts.AddPart(PartId.ArmR, "RIGHT ARM", 90f, byName, "Right_Elbow", "Right_Shoulder");
        parts.AddPart(PartId.LegL, "LEFT LEG", 90f, byName, "Left_Foot", "Left_Hip", "Left_Hip__pelvis", "Left_Shin", "Left_Shin__hip_L");
        parts.AddPart(PartId.LegR, "RIGHT LEG", 90f, byName, "Right_Foot", "Right_Hip", "Right_Hip__pelvis", "Right_Shin", "Right_Shin__hip_R");
        parts.AttachDebris(byName);
        // Fused-miniature build: parts render double-sided (open seams).
        parts.DoubleSidedMaterials = true;
    }
}
