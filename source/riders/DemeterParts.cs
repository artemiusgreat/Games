using Godot;
using System.Collections.Generic;

namespace Titans;

// Demeter: breakable-part layout - which meshes form which
// part, the torso carve regions and the per-part HP. One file
// per rider (owner, 2026-11-27), structured like the maps;
// RiderParts keeps only the shared part machinery and calls
// this through the RiderLayouts factory.
public static class DemeterParts
{
    public static void Build(RiderParts parts, Dictionary<string, MeshInstance3D> byName)
    {
        // Dire Wolf upper body (owner's hand split) on the Enyo
        // leg rig. Head = cockpit dome split, Pod_L = the left
        // shoulder rack split (bands measured on the mesh).
        parts.SplitTorso(byName, new (PartId, System.Func<Vector3, bool>)[]
        {
            (PartId.Head, p => Mathf.Abs(p.X) <= 1.1f && p.Z <= -2.4f && p.Y >= 9.2f && p.Y <= 11.6f),
            (PartId.PodL, p => p.X >= 1.6f && p.Y >= 12.0f),
        });
        parts.AddPart(PartId.Torso, "TORSO", 0f, byName, "TorsoCore", "TorsoPin", "Waist");
        parts.AddPart(PartId.Head, "HEAD", 50f, byName, "TorsoHead");
        parts.AddPart(PartId.PodL, "LEFT SHOULDER", 45f, byName, "TorsoPod_L");
        parts.AddPart(PartId.ArmL, "LEFT ARM", 95f, byName, "GunArm_L", "Elbow_L");
        parts.AddPart(PartId.ArmR, "RIGHT ARM", 95f, byName, "GunArm_R", "Elbow_R");
        parts.AddPart(PartId.LegL, "LEFT LEG", 95f, byName,
            "Thigh_L", "Shin_L", "FootHub_L", "ToeFL1_L", "ToeFL2_L", "ToeFR1_L", "ToeFR2_L", "ToeBack1_L", "ToeBack2_L");
        parts.AddPart(PartId.LegR, "RIGHT LEG", 95f, byName,
            "Thigh_R", "Shin_R", "FootHub_R", "ToeFL1_R", "ToeFL2_R", "ToeFR1_R", "ToeFR2_R", "ToeBack1_R", "ToeBack2_R");
        // Fused-miniature build: parts render double-sided (open seams).
        parts.DoubleSidedMaterials = true;
    }
}
