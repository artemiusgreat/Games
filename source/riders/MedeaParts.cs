using Godot;
using System.Collections.Generic;

namespace Titans;

// Medea: breakable-part layout - which meshes form which
// part, the torso carve regions and the per-part HP. One file
// per rider (owner, 2026-11-27), structured like the maps;
// RiderParts keeps only the shared part machinery and calls
// this through the RiderLayouts factory.
public static class MedeaParts
{
    public static void Build(RiderParts parts, Dictionary<string, MeshInstance3D> byName)
    {
        // Shield mech, owner's clean split (2026-11-18):
        // his named objects ARE the parts - the turret is
        // its own object (the head), the left-shoulder
        // block is the missile pod, the shield and its pin
        // ride the left arm, the twin-barrel Cannon is the
        // right arm, and the legs are Enyo's (hub + toes).
        parts.AddPart(PartId.Torso, "TORSO", 0f, byName, "Torso", "Pelvis");
        parts.AddPart(PartId.Head, "HEAD", 45f, byName, "Tower");
        parts.AddPart(PartId.ArmL, "LEFT ARM", 85f, byName, "Shield", "Cylinder");
        parts.AddPart(PartId.ArmR, "RIGHT ARM", 85f, byName, "Cannon");
        parts.AddPart(PartId.LegL, "LEFT LEG", 80f, byName, "Thigh_L", "Shin_L", "FootHub_L",
            "ToeFL1_L", "ToeFL2_L", "ToeFR1_L", "ToeFR2_L", "ToeBack1_L", "ToeBack2_L");
        parts.AddPart(PartId.LegR, "RIGHT LEG", 80f, byName, "Thigh_R", "Shin_R", "FootHub_R",
            "ToeFL1_R", "ToeFL2_R", "ToeFR1_R", "ToeFR2_R", "ToeBack1_R", "ToeBack2_R");
        parts.AddPart(PartId.PodL, "LEFT SHOULDER", 40f, byName, "Cabin");
        // Fused-miniature build: parts render double-sided (open seams).
        parts.DoubleSidedMaterials = true;
    }
}
