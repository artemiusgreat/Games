using Godot;
using System.Collections.Generic;

namespace Titans;

// Theseus: breakable-part layout - which meshes form which
// part, the torso carve regions and the per-part HP. One file
// per rider (owner, 2026-11-27), structured like the maps;
// RiderParts keeps only the shared part machinery and calls
// this through the RiderLayouts factory.
public static class TheseusParts
{
  public static void Build(RiderParts parts, Dictionary<string, MeshInstance3D> byName)
  {
    // Hauptmann upper body (owner's hand split) on the Enyo
    // leg rig. Head = cockpit prow split; the sculpt's LRM
    // rack sits on the RIGHT shoulder (bands measured).
    parts.SplitTorso(byName, new (PartId, System.Func<Vector3, bool>)[]
    {
            (PartId.Head, p => Mathf.Abs(p.X) <= 1.0f && p.Z <= -0.5f && p.Y >= 11.2f && p.Y <= 12.7f),
            (PartId.PodR, p => p.X <= -1.7f && p.Y >= 10.8f),
    });
    parts.AddPart(PartId.Torso, "TORSO", 0f, byName, "TorsoCore", "TorsoPin", "Waist");
    parts.AddPart(PartId.Head, "HEAD", 50f, byName, "TorsoHead");
    parts.AddPart(PartId.PodR, "RIGHT SHOULDER", 45f, byName, "TorsoPod_R");
    parts.AddPart(PartId.ArmL, "LEFT ARM", 95f, byName, "ShoulderPad_L", "GunArm_L", "Elbow_L");
    parts.AddPart(PartId.ArmR, "RIGHT ARM", 95f, byName, "ShoulderPad_R", "GunArm_R", "Elbow_R");
    parts.AddPart(PartId.LegL, "LEFT LEG", 95f, byName,
        "Thigh_L", "Shin_L", "FootHub_L", "ToeFL1_L", "ToeFL2_L", "ToeFR1_L", "ToeFR2_L", "ToeBack1_L", "ToeBack2_L");
    parts.AddPart(PartId.LegR, "RIGHT LEG", 95f, byName,
        "Thigh_R", "Shin_R", "FootHub_R", "ToeFL1_R", "ToeFL2_R", "ToeFR1_R", "ToeFR2_R", "ToeBack1_R", "ToeBack2_R");
    // Fused-miniature build: parts render double-sided (open seams).
    parts.DoubleSidedMaterials = true;
  }
}
