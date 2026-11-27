using Godot;
using System.Collections.Generic;

// Hector: breakable-part layout - which meshes form which
// part, the torso carve regions and the per-part HP. One file
// per rider (owner, 2026-11-27), structured like the maps;
// RiderParts keeps only the shared part machinery and calls
// this through the RiderLayouts factory.
public static class HectorParts
{
    public static void Build(RiderParts parts, Dictionary<string, MeshInstance3D> byName)
    {
        // Merged re-import (owner's part-merged FBX, 2026-11-21): 33 meshes instead of
        // 208, rig transferred from the previous build by vertex-position match (his
        // re-export is re-decimated, -3,584 tris, positions within 0.13u in file
        // units). The two cannons stay whole as the waist gun; the head is still
        // carved from Torso by SplitTorso below. (2026-11-21 evening: the owner's
        // own re-export from the reverted history - 16 objects with the clean part
        // names, his rig and UVs, textures as Hector.fbm sidecars like Apollo's.
        // Used AS IS. His merge folded the SRM pod into Left_Shoulder, so pod_L
        // claims no mesh until the pod is its own object again.)
        // Owner's clean split (2026-11-17): his 16 named
        // objects plus the Tripo fragments, rigged as-is -
        // nothing moved or regrouped. Head = the skull
        // band of his Torso object (re-measured on his
        // 2026-11-21 re-export: his head-group verts span
        // |X| <= 0.146, Y >= 0.848, Z >= -0.108 in file
        // units; the band below covers them - the shipped
        // band caught only 341 of his 614 head verts and
        // the brk rays attributed the skull to torso). Gun = his cannon group under
        // Torso (Left/Right_Cannon.001 housings + the
        // Cannon barrels), torso-bound per his grouping
        // (2026-11-18 restructure: Ares legs + pelvis
        // replaced the native bottom half; the old
        // tripo_part_33 island name is gone). Pod = the connected armour cluster
        // over the left shoulder, contact-chained from
        // tripo_part_53 - the same pieces the previous
        // build joined as SRM6.
        parts.SplitTorso(byName, new (PartId, System.Func<Vector3, bool>)[]
        {
            (PartId.Head, p => Mathf.Abs(p.X) <= 0.15f && p.Y >= 0.84f && p.Z >= -0.14f),
        });
        parts.AddPart(PartId.Torso, "TORSO", 0f, byName, "Pelvis", "TorsoCore");
        parts.AddPart(PartId.Gun, "WAIST GUN", 65f, byName, "Left_Cannon", "Right_Cannon");
        parts.AddPart(PartId.PodL, "SRM POD", 45f, byName);
        parts.AddPart(PartId.Head, "HEAD", 50f, byName, "TorsoHead");
        parts.AddPart(PartId.ArmL, "LEFT ARM", 90f, byName, "Left_Elbow", "Left_Hand", "Left_Shoulder");
        parts.AddPart(PartId.ArmR, "RIGHT ARM", 90f, byName, "Right_Elbow", "Right_Hand", "Right_Shoulder");
        parts.AddPart(PartId.LegL, "LEFT LEG", 90f, byName, "Left_Foot", "Left_Hip", "Left_Shin");
        parts.AddPart(PartId.LegR, "RIGHT LEG", 90f, byName, "Right_Foot", "Right_Hip", "Right_Shin");
        parts.AttachDebris(byName);
        // Fused-miniature build: parts render double-sided (open seams).
        parts.DoubleSidedMaterials = true;
    }
}
