using Godot;
using System.Collections.Generic;

namespace Titans;

// Enyo: breakable-part layout - which meshes form which
// part, the torso carve regions and the per-part HP. One file
// per rider (owner, 2026-11-27), structured like the maps;
// RiderParts keeps only the shared part machinery and calls
// this through the RiderLayouts factory.
public static class EnyoParts
{
  public static void Build(RiderParts parts, Dictionary<string, MeshInstance3D> byName)
  {
    // Z thresholds carry a +0.09 frame shift (2026-11-15):
    // SplitTorso now classifies in the model's own frame;
    // these regions were measured in the old walker-relative
    // frame, whose origin sat 0.09 lower in Z for this model.
    parts.SplitTorso(byName, new (PartId, System.Func<Vector3, bool>)[]
    {
            (PartId.PodL, p => p.X >= 1.7f && p.Y >= 9.9f && p.Z <= -1.01f),
            (PartId.PodR, p => p.X <= -1.7f && p.Y >= 9.9f && p.Z <= -1.01f),
            (PartId.Head, p => Mathf.Abs(p.X) <= 1.4f && p.Y >= 10.1f && p.Z >= 1.09f),
    });
    parts.AddPart(PartId.Torso, "TORSO", 0f, byName, "TorsoCore", "TorsoPin", "Waist");
    parts.AddPart(PartId.PodL, "LEFT LRM POD", 45f, byName, "TorsoPod_L");
    parts.AddPart(PartId.PodR, "RIGHT LRM POD", 45f, byName, "TorsoPod_R");
    parts.AddPart(PartId.Head, "HEAD", 50f, byName, "TorsoHead", "Antenna");
    parts.AddPart(PartId.ArmL, "LEFT ARM", 90f, byName,
        "UpperArm_L", "ShoulderPad_L", "Elbow_L", "Forearm_L", "HandGun_L", "WeaponERPPC_L");
    parts.AddPart(PartId.ArmR, "RIGHT ARM", 90f, byName,
        "UpperArm_R", "ShoulderPad_R", "Elbow_R", "Forearm_R", "HandGun_R", "WeaponERPPC_R");
    parts.AddPart(PartId.LegL, "LEFT LEG", 90f, byName,
        "Thigh_L", "Shin_L", "FootHub_L", "ToeFL1_L", "ToeFL2_L", "ToeFR1_L", "ToeFR2_L", "ToeBack1_L", "ToeBack2_L");
    parts.AddPart(PartId.LegR, "RIGHT LEG", 90f, byName,
        "Thigh_R", "Shin_R", "FootHub_R", "ToeFL1_R", "ToeFL2_R", "ToeFR1_R", "ToeFR2_R", "ToeBack1_R", "ToeBack2_R");
  }
}
