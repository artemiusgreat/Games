using Godot;

namespace Titans;

// Titans module: the Babylon campaign world's dressing
// (owner, 2026-11-20) - the river water for the Babylon
// contract. All positions come from maps/Babylon.cs, the
// same table the heightfield and the guard spawner read.
// (The deck bridges and the summit decks are removed,
// owner, 2026-11-27: no constructions on any map.)
public partial class Titans
{
    // River water (kept from the bridge build: the water
    // itself is terrain dressing, not a construction).
    private static readonly Color RiverWaterColor = new(0.13f, 0.32f, 0.48f, 0.85f);
    private const float RiverRoughness = 0.22f, RiverMetallic = 0.15f; // water sheen
    private const float RiverPlaneLength = 1900f, RiverPlaneWidth = 46f; // the water strip spans the world, as wide as the carved channel
    private const float WaterDropBelowBank = 5.0f;   // the water plane sits this far under the bank profile

    internal void BuildBabylonWorld()
    {
        // ---- Rivers: water in the channel (owner,
        // 2026-11-27: the deck bridges are removed like
        // every other construction - the road fords the
        // channel at the bank profile).
        var waterMat = new StandardMaterial3D
        {
            AlbedoColor = RiverWaterColor,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = RiverRoughness, Metallic = RiverMetallic
        };
        foreach (float rz in Babylon.Rivers)
        {
            float bankY = Babylon.Profile(rz);
            var water = new MeshInstance3D
            {
                Mesh = new PlaneMesh { Size = new Vector2(RiverPlaneLength, RiverPlaneWidth) },
                MaterialOverride = waterMat,
                Position = new Vector3(0f, bankY - WaterDropBelowBank, rz)
            };
            AddChild(water);
        }

        // The summit camp sits on the bare plateau (owner,
        // 2026-11-27: the stacked summit decks and their ramps
        // were removed, like the city decks before them).
        Dbg.Print($"BABYLON world built: {Babylon.Sites.Length} villages, {Babylon.Rivers.Length} rivers, summit at h={Babylon.SummitH:F0}");
    }

}
