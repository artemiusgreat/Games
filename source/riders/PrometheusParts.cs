using Godot;
using System.Collections.Generic;
using Titans.Definitions;

namespace Titans;

// Prometheus: breakable-part layout - which meshes form which
// part, the torso carve regions and the per-part HP. One file
// per rider (owner, 2026-11-27), structured like the maps;
// RiderParts keeps only the shared part machinery and calls
// this through the RiderLayouts factory.
public static class PrometheusParts
{
  public static void Build(RiderParts parts, Dictionary<string, MeshInstance3D> byName)
  {
    // Hellbringer kit: the head is the cockpit hood region
    // split out of the Torso mesh (measured on the built
    // model: |x| <= 1.3, top front). The missile pod on
    // the LEFT shoulder (owner, 2026-11-12: 'obvious pods
    // for missiles on its left shoulder') is fused into
    // the same Torso mesh - the census puts it at
    // X 1.10..2.28, height 10.6..12.1 (610 verts, a
    // distinct block above the shoulder line; axis
    // mapping cross-checked against Enyo's pod census),
    // so SplitTorso carves it as a real pod part.
    parts.SplitTorso(byName, new (PartId, System.Func<Vector3, bool>)[]
    {
            (PartId.PodL, p => p.X >= 1.12f && p.Y >= 10.55f),
            // Classification space note: measured on the
            // built model - the cockpit is the top of the
            // torso's dense front slab, which lands at
            // Z >= -0.72 in this space (region census +
            // per-mesh AABB checks), not at the gait-front
            // sign one would guess from the other riders.
            (PartId.Head, p => Mathf.Abs(p.X) <= 1.3f && p.Y >= 11.0f && p.Z >= -0.72f),
    });
    parts.AddPart(PartId.Torso, "TORSO", 0f, byName, "TorsoCore", "TorsoPin", "Waist");
    parts.AddPart(PartId.PodL, "LEFT POD", 45f, byName, "TorsoPod_L");
    parts.AddPart(PartId.Head, "HEAD", 50f, byName, "TorsoHead");
    parts.AddPart(PartId.ArmL, "LEFT ARM", 90f, byName,
        "ShoulderAct_L", "UpperArm_L", "UpperArm2_L", "Elbow_L", "Forearm_L", "HandGun_L", "WeaponERPPC_L");
    parts.AddPart(PartId.ArmR, "RIGHT ARM", 90f, byName,
        "ShoulderAct_R", "UpperArm_R", "UpperArm2_R", "Elbow_R", "Forearm_R", "HandGun_R", "WeaponERPPC_R");
    parts.AddPart(PartId.LegL, "LEFT LEG", 90f, byName,
        "Thigh_L", "Knee_L", "Shin_L", "Foot_L");
    parts.AddPart(PartId.LegR, "RIGHT LEG", 90f, byName,
        "Thigh_R", "Knee_R", "Shin_R", "Foot_R");
  }
}
