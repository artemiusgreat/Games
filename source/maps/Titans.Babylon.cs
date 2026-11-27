using Godot;

// Titans module: the Babylon campaign world's dressing
// (owner, 2026-11-20) - the river bridges and the layered
// summit city for the Babylon contract. All positions come
// from maps/Babylon.cs, the same table the heightfield and
// the guard spawner read.
public partial class Titans
{
    // Bridge pillar cross-section (the deck pillars are
    // square posts; owner, 2026-11-25: named constant).
    private const float BridgePillarWidth = 3f;

    // River-bridge deck and dressing conventions (Stage B
    // naming pass, 2026-11-27: names only, exact values).
    private const float RiverBridgeDeckThickness = 1.6f;
    // River and bridge factory values.
    private static readonly Color RiverWaterColor = new(0.13f, 0.32f, 0.48f, 0.85f);
    private const float RiverRoughness = 0.22f, RiverMetallic = 0.15f;
    private const float RiverPlaneLength = 1900f, RiverPlaneWidth = 46f;
    private const float WaterDropBelowBank = 5.0f;   // the water plane sits this far under the bank profile
    private const float BridgeDeckWidth = 34f, BridgeDeckLength = 68f;
    private const float BridgeTopAboveBank = 0.5f;   // the deck top rides this far above the bank
    private const float BridgeUnderClearance = 2.1f; // the roof rect's underside below its top
    private static readonly Color RailColor = new(0.16f, 0.17f, 0.19f);
    private const float RailMetallic = 0.55f, RailRoughness = 0.5f;
    private const float RailOffsetX = 16.4f;         // rails sit this far either side of the deck centre
    private const float RailThickness = 0.5f, RailHeight = 1.1f;
    private const float BridgePillarHeight = 8f, PillarOffsetZ = 18f, PillarCenterDrop = 4.8f;
    private const float RampCorridorSinMax = 0.30f;  // plateau towers skip bearings this close to the south ramp corridor

    private void BuildBabylonWorld()
    {
        // ---- Rivers: water in the channel and a deck bridge
        // where the road crosses.
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
            float cx = Babylon.RoadX(rz);
            float top = bankY + BridgeTopAboveBank;
            Main.Roofs.Add(new Main.RoofRect
            {
                Cx = cx, Cz = rz, Hx = BridgeDeckWidth / 2f, Hz = BridgeDeckLength / 2f, Top = top, Under = top - BridgeUnderClearance
            });
            var deck = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(BridgeDeckWidth, RiverBridgeDeckThickness, BridgeDeckLength) },
                MaterialOverride = DeckMat(),
                Position = new Vector3(cx, top - RiverBridgeDeckThickness / 2f, rz)
            };
            AddChild(deck);
            var railMat = new StandardMaterial3D
            {
                AlbedoColor = RailColor,
                Metallic = RailMetallic, Roughness = RailRoughness
            };
            foreach (float side in new[] { -RailOffsetX, RailOffsetX })
            {
                var rail = new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(RailThickness, RailHeight, BridgeDeckLength) },
                    MaterialOverride = railMat,
                    Position = new Vector3(cx + side, top + RailHeight / 2f, rz)
                };
                AddChild(rail);
            }
            foreach (float pz in new[] { -PillarOffsetZ, PillarOffsetZ })
            {
                var pillar = new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(BridgePillarWidth, BridgePillarHeight, BridgePillarWidth) },
                    MaterialOverride = DeckMat(),
                    Position = new Vector3(cx, top - PillarCenterDrop, rz + pz)
                };
                AddChild(pillar);
            }
        }

        // The summit camp sits on the bare plateau (owner,
        // 2026-11-27: the stacked summit decks and their ramps
        // were removed, like the city decks before them).
        Dbg.Print($"BABYLON world built: {Babylon.Sites.Length} villages, {Babylon.Rivers.Length} bridged rivers, summit at h={Babylon.SummitH:F0}");
    }

}
