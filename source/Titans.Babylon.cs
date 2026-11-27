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
    private const float SummitDeckUnderClearance = 1.6f; // a summit deck's roof underside below its top
    private const float ColumnInset = 8f;            // deck columns inset this far from the slab edges
    private const float MinColumnHeight = 3f;        // shorter columns are skipped
    private const float ColumnFootprint = 5f;
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

        // ---- The summit city: two stacked platform levels
        // over the plateau, ramps between them, towers on every
        // level and the citadel keep on top.
        var sc = Babylon.Summit;
        float h0 = Babylon.SummitH;
        AddSummitDeck(sc + new Vector2(0f, 8f), 104f, 66f, h0 + 15f);
        AddSummitDeck(sc + new Vector2(0f, -30f), 58f, 40f, h0 + 30f);
        AddSummitRamp(new Vector2(sc.X, sc.Y + 89f), h0, h0 + 15f, 13f);
        AddSummitRamp(new Vector2(sc.X, sc.Y + 25f), h0 + 15f, h0 + 30f, 11f);
        Dbg.Print($"BABYLON world built: {Babylon.Sites.Length} villages, {Babylon.Rivers.Length} bridged rivers, summit city at h={h0:F0}");
    }

    // One stacked platform: the walkable RoofRect registration,
    // the slab visual and the columns that hold it up.
    private void AddSummitDeck(Vector2 c, float hx, float hz, float top)
    {
        Main.Roofs.Add(new Main.RoofRect
        {
            Cx = c.X, Cz = c.Y, Hx = hx, Hz = hz, Top = top, Under = top - SummitDeckUnderClearance
        });
        var slab = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(hx * 2f, DeckThickness, hz * 2f) },
            MaterialOverride = DeckMat(),
            Position = new Vector3(c.X, top - DeckThickness / 2f, c.Y)
        };
        AddChild(slab);
        float ground = Main.HeightAt(c.X, c.Y);
        foreach (float ox in new[] { -hx + ColumnInset, 0f, hx - ColumnInset })
        foreach (float oz in new[] { -hz + ColumnInset, hz - ColumnInset })
        {
            float colH = top - ground;
            if (colH < MinColumnHeight) continue;
            var col = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(ColumnFootprint, colH, ColumnFootprint) },
                MaterialOverride = DeckMat(),
                Position = new Vector3(c.X + ox, ground + colH / 2f, c.Y + oz)
            };
            AddChild(col);
        }
    }

    // A ramp up the levels: the RampRect registration plus a
    // sloped box visual (the Harbor wedge idiom). Dir is
    // (0, -1): the surface climbs toward the north (-z) end.
    private void AddSummitRamp(Vector2 c, float hFrom, float hTo, float halfWid)
    {
        Main.Ramps.Add(new Main.RampRect
        {
            Cx = c.X, Cz = c.Y, DirX = 0f, DirZ = -1f,
            HalfLen = 15f, HalfWid = halfWid, H0 = hFrom, H1 = hTo
        });
        float len = Mathf.Sqrt(30f * 30f + (hTo - hFrom) * (hTo - hFrom));
        var wedge = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(halfWid * 2f, DeckThickness, len) },
            MaterialOverride = DeckMat(),
            Position = new Vector3(c.X, (hFrom + hTo) / 2f - DeckThickness / 2f, c.Y),
            Rotation = new Vector3(Mathf.Atan2(hTo - hFrom, 30f), 0f, 0f)
        };
        AddChild(wedge);
    }
}
