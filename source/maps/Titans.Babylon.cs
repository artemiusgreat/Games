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

    // Bridge deck stone (moved here from the deleted
    // terraced-city builder, 2026-11-27: the helpers outlived
    // it because Babylon's bridge decks and pillars wear this
    // material). Snow-dusted deck tone over the stone texture.
    private static readonly Color DeckCol = new(0.62f, 0.66f, 0.78f);
    // Deck material factory values.
    private const float DeckNormalScale = 0.8f, DeckRoughness = 0.95f, DeckMetallic = 0.05f;

    // Procedural stone (owner: platforms and ramps should
    // look like stone): tileable value-noise blotches and
    // grain with a speckle pass, generated once.
    private static ImageTexture _stoneTex;
    private static float Hash2(int x, int y)
    {
        int h = x * 374761393 + y * 668265263;
        h = (h ^ (h >> 13)) * 1274126177;
        return ((h ^ (h >> 16)) & 0xffff) / 65535f;
    }
    private static float PNoise(float x, float y, int period)
    {
        int xi = (int)Mathf.Floor(x), yi = (int)Mathf.Floor(y);
        float xf = x - xi, yf = y - yi;
        float u = xf * xf * (3f - 2f * xf), v = yf * yf * (3f - 2f * yf);
        int x0 = ((xi % period) + period) % period, y0 = ((yi % period) + period) % period;
        int x1 = (x0 + 1) % period, y1 = (y0 + 1) % period;
        return Mathf.Lerp(
            Mathf.Lerp(Hash2(x0, y0), Hash2(x1, y0), u),
            Mathf.Lerp(Hash2(x0, y1), Hash2(x1, y1), u), v);
    }
    // Stone texture recipe (naming pass, 2026-11-27: names
    // only, exact values). For each noise layer the tiling
    // period must equal its scale, so scale and period are
    // separate constants carrying the same number.
    private const float StoneBlotchScale1 = 5f, StoneBlotchScale2 = 13f, StoneBlotchScale3 = 34f;
    private const int StoneBlotchPeriod1 = 5, StoneBlotchPeriod2 = 13, StoneBlotchPeriod3 = 34;
    private const float StoneBlotchOffset2X = 7f, StoneBlotchOffset2Y = 3f;
    private const float StoneBlotchOffset3X = 13f, StoneBlotchOffset3Y = 29f;
    private const float StoneBlotchWeight1 = 0.55f, StoneBlotchWeight2 = 0.30f, StoneBlotchWeight3 = 0.15f;
    private const float StoneGrainScale = 90f;
    private const int StoneGrainPeriod = 90;
    private const float StoneGrainOffsetX = 41f, StoneGrainOffsetY = 17f;
    private const float StoneBaseBrightness = 0.52f;
    private const float StoneBlotchGain = 0.22f, StoneGrainGain = 0.07f, StoneSpeckleGain = 0.05f;
    private const float StoneTintR = 1.03f, StoneTintB = 0.93f;
    private const float StoneNormalStrength = 2.2f;

    private static ImageTexture StoneTexture()
    {
        if (_stoneTex != null) return _stoneTex;
        const int S = 256;
        var img = Image.CreateEmpty(S, S, false, Image.Format.Rgb8);
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float u = x / (float)S, v = y / (float)S;
            float blotch = PNoise(u * StoneBlotchScale1, v * StoneBlotchScale1, StoneBlotchPeriod1) * StoneBlotchWeight1
                         + PNoise(u * StoneBlotchScale2 + StoneBlotchOffset2X, v * StoneBlotchScale2 + StoneBlotchOffset2Y, StoneBlotchPeriod2) * StoneBlotchWeight2
                         + PNoise(u * StoneBlotchScale3 + StoneBlotchOffset3X, v * StoneBlotchScale3 + StoneBlotchOffset3Y, StoneBlotchPeriod3) * StoneBlotchWeight3;
            float grain = PNoise(u * StoneGrainScale + StoneGrainOffsetX, v * StoneGrainScale + StoneGrainOffsetY, StoneGrainPeriod);
            float spk = Hash2(x * 3 + 11, y * 5 + 7);
            float b = StoneBaseBrightness + (blotch - 0.5f) * StoneBlotchGain + (grain - 0.5f) * StoneGrainGain
                    + (spk - 0.5f) * StoneSpeckleGain;
            img.SetPixel(x, y, new Color(b * StoneTintR, b, b * StoneTintB));
        }
        _stoneTex = ImageTexture.CreateFromImage(img);
        return _stoneTex;
    }

    private static StandardMaterial3D DeckMat() => new()
    {
        AlbedoColor = DeckCol, AlbedoTexture = StoneTexture(),
        NormalTexture = StoneNormalTexture(), NormalScale = DeckNormalScale,
        Roughness = DeckRoughness, Metallic = DeckMetallic
    };

    // Bumpy stone (owner, 2026-11-11: 'apply bevel and
    // roughness to platforms... make them look bumpy'): a
    // tileable normal map derived from the same value-noise
    // field as the albedo, so the decks read as rough-hewn
    // stone under raking light instead of flat tinted slabs.
    private static ImageTexture _stoneNrm;
    private static ImageTexture StoneNormalTexture()
    {
        if (_stoneNrm != null) return _stoneNrm;
        const int S = 256;
        float H(int x, int y)
        {
            float u = ((x % S) + S) % S / (float)S, v = ((y % S) + S) % S / (float)S;
            return PNoise(u * StoneBlotchScale1, v * StoneBlotchScale1, StoneBlotchPeriod1) * StoneBlotchWeight1 + PNoise(u * StoneBlotchScale2 + StoneBlotchOffset2X, v * StoneBlotchScale2 + StoneBlotchOffset2Y, StoneBlotchPeriod2) * StoneBlotchWeight2
                 + PNoise(u * StoneBlotchScale3 + StoneBlotchOffset3X, v * StoneBlotchScale3 + StoneBlotchOffset3Y, StoneBlotchPeriod3) * StoneBlotchWeight3;
        }
        var img = Image.CreateEmpty(S, S, false, Image.Format.Rgb8);
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float dx = (H(x + 1, y) - H(x - 1, y)) * StoneNormalStrength;
            float dy = (H(x, y + 1) - H(x, y - 1)) * StoneNormalStrength;
            var n = new Vector3(-dx, -dy, 1f).Normalized();
            img.SetPixel(x, y, new Color(n.X * 0.5f + 0.5f, n.Y * 0.5f + 0.5f, n.Z * 0.5f + 0.5f));
        }
        _stoneNrm = ImageTexture.CreateFromImage(img);
        return _stoneNrm;
    }

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
