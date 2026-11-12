using Godot;
using System.Collections.Generic;

// The terraced canyon city (owner, 2026-11-10): the city is
// no longer a disc in the middle of the map — the whole map
// is the city. The canyon terrain (Main.ValleyHeight) holds
// three valley basins, one at each base, joined by road
// passes with rock tunnel arches; every valley hosts its
// own multilevel city in the style of the owner's reference
// model: independent stone platforms at +22 / +44 / +66
// carrying the kit buildings, wide ramps and lift pads
// between levels, flat bridges between same-level
// platforms, a leadership landmark on the top platform,
// and — in the home valley — an island district across a
// water channel. Platforms and ramps wear a procedural
// stone texture; each level carries its own trim colour.
// Everything registers through the standing surface system
// (Main.Roofs / Walls / Ramps / DiscRoofs) with undersides,
// so decks collide both ways. Gated by
// WorldConfig.TerracedCity (the Harbor Stacks contract).
public partial class RiderDuel
{
    // Level heights (owner): decks high enough that a rider
    // walking UNDER a platform — chase camera included —
    // fits beneath it: the +22 underside is 19.8, above the
    // tallest rider (14) and its camera anchor (~16).
    private const float TerraceL1 = 22f, TerraceL2 = 44f, TerraceL3 = 66f;

    private sealed class Lift
    {
        public Vector2 C; public float Lo, Hi, Top, Target, Dwell, Empty;
        public int RoofIdx; public MeshInstance3D Pad; public int State; // 0 at Lo, 1 moving, 2 at Hi
    }
    private readonly List<Lift> _lifts = new();

    // Harness hooks: the first ramp and lift built, the
    // island's absolute centre, the under-deck bonk point and
    // valley 0's platform rects for the coverage scan.
    public Vector2 TestRampFoot, TestRampDir;
    public float TestRampLo, TestRampHi, TestRampRun;
    public Main.RampRect TestRampRect;
    public Vector2 TestLiftPad;
    public float TestLiftLo, TestLiftHi;
    public bool HasTestRamp, HasTestLift;
    public bool HasTerraceTest => HasTestRamp && HasTestLift;
    public Vector2 TestDeckPoint;
    public readonly List<(Vector2 C, Vector2 H, float R, float Top)> TestPlatforms = new();

    // Platform layout per valley, in plot indices (pitch 32)
    // — the reference model's arrangement: several INDEPENDENT
    // platforms per level, not one filled tier.
    //   L1 (+22): four 96x64 platforms in a loose ring
    //   L2 (+44): two 96x64 platforms, north and south
    //   L3 (+66): one 64x64 elite platform, landmark on top
    private static float PlatformLevel(int i, int j)
    {
        bool In(int i0, int i1, int j0, int j1) => i >= i0 && i <= i1 && j >= j0 && j <= j1;
        if (In(2, 3, -5, -4)) return TerraceL3;
        if (In(-3, -1, -5, -4) || In(1, 3, 4, 5)) return TerraceL2;
        if (In(-4, -2, -2, -1) || In(2, 4, -2, -1) ||
            In(-4, -2, 1, 2) || In(2, 4, 1, 2)) return TerraceL1;
        return 0f;
    }

    // Snow-dusted deck tone over the stone texture.
    private static readonly Color DeckCol = new(0.62f, 0.66f, 0.78f);

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
    private static ImageTexture StoneTexture()
    {
        if (_stoneTex != null) return _stoneTex;
        const int S = 256;
        var img = Image.CreateEmpty(S, S, false, Image.Format.Rgb8);
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float u = x / (float)S, v = y / (float)S;
            float blotch = PNoise(u * 5f, v * 5f, 5) * 0.55f
                         + PNoise(u * 13f + 7f, v * 13f + 3f, 13) * 0.30f
                         + PNoise(u * 34f + 13f, v * 34f + 29f, 34) * 0.15f;
            float grain = PNoise(u * 90f + 41f, v * 90f + 17f, 90);
            float spk = Hash2(x * 3 + 11, y * 5 + 7);
            float b = 0.52f + (blotch - 0.5f) * 0.22f + (grain - 0.5f) * 0.07f
                    + (spk - 0.5f) * 0.05f;
            img.SetPixel(x, y, new Color(b * 1.03f, b, b * 0.93f));
        }
        _stoneTex = ImageTexture.CreateFromImage(img);
        return _stoneTex;
    }

    private static StandardMaterial3D DeckMat() => new()
    {
        AlbedoColor = DeckCol, AlbedoTexture = StoneTexture(),
        NormalTexture = StoneNormalTexture(), NormalScale = 0.8f,
        Roughness = 0.95f, Metallic = 0.05f
    };

    // Bumpy stone (owner, 2026-11-11: 'apply bevel and
    // roughness to platforms... make them look bumpy'): a
    // tileable normal map derived from the same value-noise
    // field as the albedo, so the decks and columns read as
    // rough-hewn stone under raking light instead of flat
    // tinted slabs.
    private static ImageTexture _stoneNrm;
    private static ImageTexture StoneNormalTexture()
    {
        if (_stoneNrm != null) return _stoneNrm;
        const int S = 256;
        float H(int x, int y)
        {
            float u = ((x % S) + S) % S / (float)S, v = ((y % S) + S) % S / (float)S;
            return PNoise(u * 5f, v * 5f, 5) * 0.55f + PNoise(u * 13f + 7f, v * 13f + 3f, 13) * 0.30f
                 + PNoise(u * 34f + 13f, v * 34f + 29f, 34) * 0.15f;
        }
        var img = Image.CreateEmpty(S, S, false, Image.Format.Rgb8);
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float dx = (H(x + 1, y) - H(x - 1, y)) * 2.2f;
            float dy = (H(x, y + 1) - H(x, y - 1)) * 2.2f;
            var n = new Vector3(-dx, -dy, 1f).Normalized();
            img.SetPixel(x, y, new Color(n.X * 0.5f + 0.5f, n.Y * 0.5f + 0.5f, n.Z * 0.5f + 0.5f));
        }
        _stoneNrm = ImageTexture.CreateFromImage(img);
        return _stoneNrm;
    }

    // Bumpy metal plate for the ramp walking surface (owner:
    // 'the walking area is bumpy metal with some pattern
    // mounted inside the stone'): 2x2 tread plates per tile,
    // dark seams, rivets at the corners, per-plate tint —
    // albedo plus a matching normal map (seams recessed,
    // rivets raised). Generated once.
    private static ImageTexture _plateTex, _plateNrm;
    private static void PlateTextures()
    {
        if (_plateTex != null) return;
        const int S = 256;
        var alb = Image.CreateEmpty(S, S, false, Image.Format.Rgb8);
        var hgt = new float[S, S];
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            int px = x * 2 / S, py = y * 2 / S;              // plate indices (2x2 per tile)
            int lx = x % (S / 2), ly = y % (S / 2);          // local position in the plate
            float tint = 0.92f + Hash2(px * 7 + 3, py * 11 + 5) * 0.16f;
            float grain = PNoise(x / (float)S * 40f, y / (float)S * 40f, 40);
            bool seam = lx < 3 || ly < 3 || lx >= S / 2 - 3 || ly >= S / 2 - 3;
            bool rivet = (Mathf.Abs(lx - 14) < 3 || Mathf.Abs(lx - (S / 2 - 15)) < 3) &&
                         (Mathf.Abs(ly - 14) < 3 || Mathf.Abs(ly - (S / 2 - 15)) < 3);
            float b = (0.34f + (grain - 0.5f) * 0.06f) * tint;
            float h = seam ? 0.25f : 0.6f + (grain - 0.5f) * 0.08f;
            if (rivet) { b += 0.13f; h = 0.95f; }
            alb.SetPixel(x, y, new Color(b * 0.92f, b * 0.98f, b * 1.12f));
            hgt[x, y] = h;
        }
        _plateTex = ImageTexture.CreateFromImage(alb);
        var nrm = Image.CreateEmpty(S, S, false, Image.Format.Rgb8);
        float Hg(int x, int y) => hgt[((x % S) + S) % S, ((y % S) + S) % S];
        for (int y = 0; y < S; y++)
        for (int x = 0; x < S; x++)
        {
            float dx = (Hg(x + 1, y) - Hg(x - 1, y)) * 2.6f;
            float dy = (Hg(x, y + 1) - Hg(x, y - 1)) * 2.6f;
            var n = new Vector3(-dx, -dy, 1f).Normalized();
            nrm.SetPixel(x, y, new Color(n.X * 0.5f + 0.5f, n.Y * 0.5f + 0.5f, n.Z * 0.5f + 0.5f));
        }
        _plateNrm = ImageTexture.CreateFromImage(nrm);
    }
    private static StandardMaterial3D PlateMat()
    {
        PlateTextures();
        return new StandardMaterial3D
        {
            AlbedoTexture = _plateTex, NormalTexture = _plateNrm, NormalScale = 0.9f,
            Roughness = 0.5f, Metallic = 0.7f,
            Uv1Triplanar = true, // world-space tiling: the plate pattern repeats ~7u on every face
            Uv1Scale = new Vector3(0.14f, 0.14f, 0.14f)
        };
    }
    // Darker stone for the columns (owner: round, bevelled,
    // rough) — same rock, deeper tint, full roughness.
    private static StandardMaterial3D ColumnMat() => new()
    {
        AlbedoColor = new Color(0.45f, 0.48f, 0.58f), AlbedoTexture = StoneTexture(),
        NormalTexture = StoneNormalTexture(), NormalScale = 0.9f,
        Roughness = 1f, Metallic = 0.02f
    };
    // Blob-deck material: the generated cap/side mesh is
    // single-sided by construction order; cull off so the
    // underside reads from below regardless of winding.
    private static StandardMaterial3D BlobMat() => new()
    {
        AlbedoColor = DeckCol, AlbedoTexture = StoneTexture(),
        NormalTexture = StoneNormalTexture(), NormalScale = 0.8f,
        Roughness = 0.95f, Metallic = 0.05f,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled
    };
    // One trim colour per level (the reference model's
    // level coding): cyan, violet, gold.
    private static StandardMaterial3D TrimMat(int lvl = 1)
    {
        var em = lvl >= 3 ? new Color(1.0f, 0.75f, 0.30f)
               : lvl == 2 ? new Color(0.62f, 0.45f, 1.0f)
               : new Color(0.25f, 0.75f, 1.0f);
        return new StandardMaterial3D
        {
            AlbedoColor = new Color(0.05f, 0.08f, 0.10f),
            EmissionEnabled = true, Emission = em,
            EmissionEnergyMultiplier = 1.6f, Roughness = 0.6f
        };
    }
    private static int LevelIndex(float top) => top >= TerraceL3 - 1f ? 3 : top >= TerraceL2 - 1f ? 2 : 1;

    private void BuildTerracedCity(System.Random rng, ref int placed)
    {
        _skipRects.Clear();
        _lifts.Clear();
        TestPlatforms.Clear();
        foreach (var pad in Main.SpawnPads)
            BuildValleyCity(rng, ref placed, pad, pad == Main.SpawnPads[0]);
        BuildPassArches();
        GD.Print($"TERRACES built: {_lifts.Count} lifts in the 2 enemy valley cities (roofs={Main.Roofs.Count} walls={Main.Walls.Count} ramps={Main.Ramps.Count})");
    }

    private void BuildValleyCity(System.Random rng, ref int placed, Vector2 ctr, bool home)
    {
        float p = CityGrid.Pitch;
        Vector2 L(float x, float z) => ctr + new Vector2(x, z);
        // Multi-level terraces stand only around the ENEMY
        // bases (owner, 2026-11-11 round 3: 'multi-level
        // city is needed only around enemy bases, remove it
        // from the home base'): the home valley is a flat
        // ground-level city — no platforms, ramps, lifts,
        // bridges or island district. Connections first —
        // they register the skip rects the block placement
        // respects. Platform spans (plot centres ±16):
        // A x[-144,-48] z[-80,-16]; B mirrored +x; C/D
        // mirrored +z; E x[-112,-16] z[-176,-112];
        // F x[16,112] z[112,176]; G x[48,112] z[-176,-112].
        // Ground -> L1 ramps on the outer edges of all four
        // platforms; lifts on the plaza sides of A and D.
        if (!home)
        {
        BuildRamp(L(-144f, -48f), new Vector2(-1, 0), 0f, TerraceL1, TerraceL1 * 2f);
        BuildRamp(L(144f, -48f), new Vector2(1, 0), 0f, TerraceL1, TerraceL1 * 2f);
        BuildRamp(L(-144f, 48f), new Vector2(-1, 0), 0f, TerraceL1, TerraceL1 * 2f);
        BuildRamp(L(144f, 48f), new Vector2(1, 0), 0f, TerraceL1, TerraceL1 * 2f);
        BuildLift(L(-48f, -48f), new Vector2(1, 0), 0f, TerraceL1);
        BuildLift(L(48f, 48f), new Vector2(-1, 0), 0f, TerraceL1);
        // L1 -> L2 bridge-ramps over the ground between the
        // platforms (the wedge's underside stays walkable).
        BuildRamp(L(-80f, -112f), new Vector2(0, 1), TerraceL1, TerraceL2, 44f);
        BuildRamp(L(80f, 112f), new Vector2(0, -1), TerraceL1, TerraceL2, 44f);
        // L2 -> L3 ramp along the north band (long run: the
        // foot lands on E across the gap).
        BuildRamp(L(48f, -144f), new Vector2(-1, 0), TerraceL2, TerraceL3, 70f);
        // Same-level flat bridges across the plaza.
        BuildFlatBridge(L(-48f, -48f), L(48f, -48f), TerraceL1);
        BuildFlatBridge(L(-48f, 48f), L(48f, 48f), TerraceL1);
        // The landmark plot stays clear on the top platform.
        _skipRects.Add(new Main.RoofRect { Cx = ctr.X + 80f, Cz = ctr.Y - 144f, Hx = 17f, Hz = 17f, Top = 0f });
        }
        // Harness hooks anchor on the FIRST enemy valley
        // (they pointed at the home decks while those
        // existed).
        if (!home && TestPlatforms.Count == 0)
        {
            TestDeckPoint = L(-96f, -48f);
            TestPlatforms.Add((L(-96f, -48f), new Vector2(48f, 32f), 30f, TerraceL1));
            TestPlatforms.Add((L(96f, -48f), new Vector2(48f, 32f), 30f, TerraceL1));
            TestPlatforms.Add((L(-96f, 48f), new Vector2(48f, 32f), 30f, TerraceL1));
            TestPlatforms.Add((L(96f, 48f), new Vector2(48f, 32f), 30f, TerraceL1));
            TestPlatforms.Add((L(-64f, -144f), new Vector2(48f, 32f), 30f, TerraceL2));
            TestPlatforms.Add((L(64f, 144f), new Vector2(48f, 32f), 30f, TerraceL2));
            TestPlatforms.Add((L(80f, -144f), new Vector2(32f, 32f), 30f, TerraceL3));
        }
        // Platform clusters first (merged curvy decks over
        // each same-level plot group; the statics register
        // the roofs/columns/lights the blocks then respect).
        if (!home) BuildPlatformClusters(ctr);
        // Plots: exact grid (platforms must TILE — jittered
        // centres once left ground-data slits riders fell
        // through), platform membership by plot index.
        int hb = Mathf.CeilToInt(CityGrid.Radius / p);
        for (int i = -hb; i <= hb; i++)
        for (int j = -hb; j <= hb; j++)
        {
            var c = L(i * p, j * p);
            if ((c - ctr).Length() > CityGrid.Radius) continue;
            if (TerrainGrade(c.X, c.Y) > 0.4f) continue;
            float l = home ? 0f : PlatformLevel(i, j);
            // The base plaza at the valley centre stays clear
            // (spawn + base structures live there), wide
            // enough for the defenders' 55u patrol disc.
            if (l == 0f && (c - ctr).Length() < 62f) continue;
            // Home valley: no buildings at all (owner,
            // 2026-11-11 round 4: 'home base should be a
            // flat area, no buildings') — the whole valley
            // stays open ground around the base.
            if (!home) PlaceCityBlock(rng, c, ref placed, l);
        }
        if (!home) BuildLandmark(L(80f, -144f), TerraceL3, 26f, 46f, 200f, true);
        GD.Print($"VALLEY CITY at ({ctr.X:F0},{ctr.Y:F0}) home={home}");
    }

    // A flat same-level span between two platform edges:
    // deck slab on columns, light rails, registered with an
    // underside like every platform.
    private void BuildFlatBridge(Vector2 a, Vector2 b, float top)
    {
        var mid = (a + b) / 2f;
        var d = b - a;
        float len = d.Length();
        bool alongX = Mathf.Abs(d.X) > Mathf.Abs(d.Y);
        Main.Roofs.Add(new Main.RoofRect
        {
            Cx = mid.X, Cz = mid.Y,
            Hx = alongX ? len / 2f : 5.5f, Hz = alongX ? 5.5f : len / 2f,
            Top = top, Under = top - 1.2f
        });
        var mi = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(alongX ? len : 11f, 1.2f, alongX ? 11f : len) },
            MaterialOverride = DeckMat(),
            Position = new Vector3(mid.X, top - 0.6f, mid.Y)
        };
        AddChild(mi);
        foreach (float f in new[] { -len / 4f, 0f, len / 4f })
        {
            var cp = mid + d.Normalized() * f;
            AddColumn(cp, top - 1.2f, LevelIndex(top), b);
            _props.Add(new Circle { P = cp, R = 1.15f, RoofTop = top - 1.2f });
        }
        foreach (float side in new[] { -5.4f, 5.4f })
        {
            var rail = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(alongX ? len : 0.35f, 0.35f, alongX ? 0.35f : len) },
                MaterialOverride = TrimMat(LevelIndex(top)),
                Position = new Vector3(mid.X + (alongX ? 0f : side), top + 0.9f, mid.Y + (alongX ? side : 0f)),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            };
            AddChild(rail);
        }
        AddCeilingLight(new Vector3(mid.X, top - 1.35f, mid.Y), 17f);
        _skipRects.Add(new Main.RoofRect
        {
            Cx = mid.X, Cz = mid.Y,
            Hx = (alongX ? len / 2f : 5.5f) + 6f, Hz = (alongX ? 5.5f : len / 2f) + 6f, Top = 0f
        });
    }

    // Rock tunnel arches over the road passes (owner's
    // model has rock tunnels on the pathways): a stone
    // portal frame at each pass saddle.
    private static readonly Color RockCol = new(0.58f, 0.55f, 0.51f);
    private void BuildPassArches()
    {
        var rockMat = new StandardMaterial3D
        { AlbedoColor = RockCol, AlbedoTexture = StoneTexture(), Roughness = 1f };
        var pads = Main.SpawnPads;
        for (int a = 0; a < pads.Length; a++)
        for (int b = a + 1; b < pads.Length; b++)
        {
            var mid = (pads[a] + pads[b]) / 2f;
            var dir = (pads[b] - pads[a]).Normalized();
            var perp = new Vector2(-dir.Y, dir.X);
            float gy = Main.HeightAt(mid.X, mid.Y);
            float top = gy + 16f;
            var holder = new Node3D { Position = new Vector3(mid.X, 0f, mid.Y), Rotation = new Vector3(0f, Mathf.Atan2(dir.X, dir.Y), 0f) };
            foreach (float s in new[] { -10.5f, 10.5f })
            {
                var pp = mid + perp * s;
                float by = Main.HeightAt(pp.X, pp.Y);
                var pil = new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(3.2f, top - by + 2f, 4.5f) },
                    MaterialOverride = rockMat,
                    Position = new Vector3(s, (top + by) / 2f, 0f)
                };
                holder.AddChild(pil);
                _props.Add(new Circle { P = pp, R = 2.6f, RoofTop = top });
                _hit.Props.Add(new HitModel.PropCyl { P = pp, R = 2.6f, BaseY = by, Height = top - by + 2f });
            }
            // The lintel is an obstacle and a floor (owner,
            // round 7: 'the arc at the enemy base doesn't
            // have collider — everything meant to be an
            // obstacle should have collider and preferably
            // allow to walk on it'): a FLAT ramp rect across
            // the pass — same 3.5-thick wedge the ramps use —
            // gives it ground, ceiling, side push-out and
            // shot planes in one registration. Its top is
            // the mesh top: (top + 1.7) + 3.5/2 = top + 3.45.
            Main.Ramps.Add(new Main.RampRect
            {
                Cx = mid.X, Cz = mid.Y, DirX = perp.X, DirZ = perp.Y,
                HalfLen = 12.25f, HalfWid = 2.5f, H0 = top + 3.45f, H1 = top + 3.45f
            });
            var lin = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(24.5f, 3.5f, 5f) },
                MaterialOverride = rockMat,
                Position = new Vector3(0f, top + 1.7f, 0f)
            };
            holder.AddChild(lin);
            AddChild(holder);
            GD.Print($"PASS ARCH at ({mid.X:F0},{mid.Y:F0}) saddle h={gy:F1}");
        }
    }

    // Ceiling lights under a deck (owner, 2026-11-11: 'when
    // I am under the platform, I can't see rider — add some
    // lights at the ceiling'): a glowing fixture strip on the
    // underside plus ONE shadowless omni per deck/bridge,
    // ranged to its own footprint so the ground underneath is
    // lit without washing the level above. One light per deck
    // (25 decks/bridges across the three valleys + the island
    // span) keeps the forward+ cluster cheap.
    private void AddCeilingLight(Vector3 centre, float range)
    {
        var fix = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(6f, 0.3f, 1.2f) },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.10f, 0.13f, 0.14f),
                EmissionEnabled = true, Emission = new Color(0.55f, 0.80f, 0.95f),
                EmissionEnergyMultiplier = 2.2f, Roughness = 0.5f
            },
            Position = centre,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        AddChild(fix);
        var omni = new OmniLight3D
        {
            LightColor = new Color(0.55f, 0.75f, 0.95f),
            LightEnergy = 1.15f,
            OmniRange = range,
            ShadowEnabled = false,
            Position = centre + new Vector3(0f, -1.2f, 0f)
        };
        AddChild(omni);
    }

    // A round terrace column (owner, 2026-11-11: 'make
    // columns round, apply some bevel... add luminescent
    // lines along each column'): a rough stone cylinder the
    // size of its collision circle, a bevel ring at both
    // ends, and one emissive line running its height on the
    // side facing `face` (usually the deck's centre).
    private void AddColumn(Vector2 p, float topY, int lvl, Vector2 face)
    {
        float h = Mathf.Max(topY, 1f);
        // Shot collision (owner, 2026-11-11 round 7: 'I can
        // shoot through column even it is an obstacle'): the
        // movement circle existed, the hit cylinder did not.
        // Every column funnels through here, so one
        // registration covers decks and bridges alike.
        _hit.Props.Add(new HitModel.PropCyl { P = p, R = 1.15f, BaseY = 0f, Height = h });
        var col = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 1.15f, BottomRadius = 1.15f, Height = h, RadialSegments = 18 },
            MaterialOverride = ColumnMat(),
            Position = new Vector3(p.X, h / 2f, p.Y)
        };
        AddChild(col);
        foreach (float ry in new[] { 0.28f, h - 0.28f })
        {
            var ring = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 1.34f, BottomRadius = 1.34f, Height = 0.56f, RadialSegments = 18 },
                MaterialOverride = ColumnMat(),
                Position = new Vector3(p.X, ry, p.Y)
            };
            AddChild(ring);
        }
        var dir = face - p;
        if (dir.LengthSquared() > 0.01f)
        {
            dir = dir.Normalized();
            var strip = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.16f, h - 1.4f, 0.07f) },
                MaterialOverride = TrimMat(lvl),
                Position = new Vector3(p.X + dir.X * 1.17f, h / 2f, p.Y + dir.Y * 1.17f),
                Rotation = new Vector3(0f, Mathf.Atan2(dir.X, dir.Y), 0f),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            };
            AddChild(strip);
        }
    }

    // The non-visual half of a platform plot: its standable
    // roof rect, its four columns (the only obstacles under
    // the deck) and its ceiling light.
    private void PlatformStatics(Vector2 c, float lvlTop, bool roof = true)
    {
        foreach (var s2 in new[] { new Vector2(13.5f, 13.5f), new Vector2(-13.5f, 13.5f), new Vector2(13.5f, -13.5f), new Vector2(-13.5f, -13.5f) })
        {
            AddColumn(c + s2, lvlTop - 2.2f, LevelIndex(lvlTop), c);
            _props.Add(new Circle { P = c + s2, R = 1.15f, RoofTop = lvlTop });
        }
        if (roof) Main.Roofs.Add(new Main.RoofRect { Cx = c.X, Cz = c.Y, Hx = 16.05f, Hz = 16.05f, Top = lvlTop, Under = lvlTop - 2.2f });
        AddCeilingLight(new Vector3(c.X, lvlTop - 2.35f, c.Y), 21f);
    }

    // The platform outline (owner, 2026-11-11 round 5):
    // the platform ITSELF is round and curvy — a radial
    // amoeba around the cluster centre, not a rectangle
    // with smoothed edges. The radius in every direction
    // is the cluster rect's boundary distance times a
    // low-frequency lobe factor (>= 1.03), so the shape
    // bulges organically outward everywhere while always
    // CONTAINING the rect: ramps, lifts, bridges and the
    // landmark, which all land on the rect, keep working,
    // and the standable discs registered from the same
    // radii cover the rect with margin.
    // Connection sectors per cluster (centre offset, level):
    // the angular spans where a ramp, lift or bridge meets
    // the platform. Inside a sector the outline stays AT
    // the rect boundary (lobe excess scaled to 0, blended
    // over 0.65 rad) — a bulge over a ramp corridor would
    // roof the climbing rider (the deck underside at
    // top-2.2 vs the ramp surface a few units below it).
    // The radius function itself lives in Main (BlobRadius)
    // so the walker's ground query evaluates the exact
    // outline the mesh was built from.
    private static readonly Dictionary<(int, int, int), Main.Sector[]> ClusterSectors = new()
    {
        // A (-96,-48) L1: west ramp, east lift+bridge, NE wedge up to E
        [( -96, -48, 1)] = new[] { new Main.Sector(Mathf.Pi, 0.21f), new Main.Sector(0f, 0.21f), new Main.Sector(-1.107f, 0.26f) },
        // B (96,-48) L1: east ramp, west bridge
        [( 96, -48, 1)] = new[] { new Main.Sector(0f, 0.21f), new Main.Sector(Mathf.Pi, 0.21f) },
        // C (-96,48) L1: west ramp, east bridge
        [( -96, 48, 1)] = new[] { new Main.Sector(Mathf.Pi, 0.21f), new Main.Sector(0f, 0.21f) },
        // D (96,48) L1: east ramp, west lift+bridge, NW wedge up to F
        [( 96, 48, 1)] = new[] { new Main.Sector(0f, 0.21f), new Main.Sector(Mathf.Pi, 0.21f), new Main.Sector(2.199f, 0.26f) },
        // E (-64,-144) L2: bridge-ramp landing (S), L3-ramp foot wedge (E)
        [( -64, -144, 2)] = new[] { new Main.Sector(2.034f, 0.26f), new Main.Sector(0f, 0.24f) },
        // F (64,144) L2: bridge-ramp landing (N)
        [( 64, 144, 2)] = new[] { new Main.Sector(-1.107f, 0.26f) },
        // G (80,-144) L3: ramp landing (W)
        [( 80, -144, 3)] = new[] { new Main.Sector(Mathf.Pi, 0.24f) },
    };

    // The old per-plot slab, kept as the fallback for any
    // platform cluster that is not a clean rectangle.
    private void PlatformSlabFallback(Vector2 c, float lvlTop)
    {
        var mi = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(31.9f, 2.2f, 31.9f) },
            MaterialOverride = DeckMat(),
            Position = new Vector3(c.X, lvlTop - 1.1f, c.Y)
        };
        AddChild(mi);
        var trim = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(32.5f, 0.55f, 32.5f) },
            MaterialOverride = TrimMat(LevelIndex(lvlTop)),
            Position = new Vector3(c.X, lvlTop - 0.28f, c.Y),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        AddChild(trim);
    }

    // Platforms are built per CLUSTER (a flood-filled group
    // of same-level plots), not per plot (owner, 2026-11-11:
    // curvy platform shapes, visible borders, no more
    // per-plot trim slabs — whose coplanar overlaps also
    // z-fought into stray triangles under the decks).
    private void BuildPlatformClusters(Vector2 ctr)
    {
        float p = CityGrid.Pitch;
        int hb = Mathf.CeilToInt(CityGrid.Radius / p);
        var cells = new Dictionary<(int, int), float>();
        for (int i = -hb; i <= hb; i++)
        for (int j = -hb; j <= hb; j++)
        {
            var c = ctr + new Vector2(i * p, j * p);
            if ((c - ctr).Length() > CityGrid.Radius) continue;
            if (TerrainGrade(c.X, c.Y) > 0.4f) continue;
            float l = PlatformLevel(i, j);
            if (l > 0f) cells[(i, j)] = l;
        }
        var seen = new HashSet<(int, int)>();
        foreach (var kv in cells)
        {
            if (seen.Contains(kv.Key)) continue;
            var stack = new List<(int, int)> { kv.Key };
            var group = new List<(int, int)>();
            seen.Add(kv.Key);
            while (stack.Count > 0)
            {
                var cur = stack[stack.Count - 1]; stack.RemoveAt(stack.Count - 1);
                group.Add(cur);
                foreach (var d in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    var n = (cur.Item1 + d.Item1, cur.Item2 + d.Item2);
                    if (cells.TryGetValue(n, out float nl) && nl == kv.Value && !seen.Contains(n))
                    { seen.Add(n); stack.Add(n); }
                }
            }
            BuildPlatformCluster(group, kv.Value, ctr);
        }
    }

    private void BuildPlatformCluster(List<(int i, int j)> group, float lvlTop, Vector2 ctr)
    {
        float p = CityGrid.Pitch;
        int i0 = int.MaxValue, i1 = int.MinValue, j0 = int.MaxValue, j1 = int.MinValue;
        foreach (var (i, j) in group)
        {
            i0 = Mathf.Min(i0, i); i1 = Mathf.Max(i1, i); j0 = Mathf.Min(j0, j); j1 = Mathf.Max(j1, j);
        }
        bool rect = group.Count == (i1 - i0 + 1) * (j1 - j0 + 1);
        // Rectangular clusters are ROUND platforms now: their
        // standable area is a union of discs following the
        // amoeba outline (registered below), so the per-plot
        // rect roofs are skipped for them — columns and
        // ceiling lights stay per plot, inside the outline.
        foreach (var (i, j) in group)
            PlatformStatics(ctr + new Vector2(i * p, j * p), lvlTop, roof: !rect);
        if (!rect)
        {
            foreach (var (i, j) in group) PlatformSlabFallback(ctr + new Vector2(i * p, j * p), lvlTop);
            return;
        }
        float x0 = ctr.X + i0 * p - 16.05f, x1 = ctr.X + i1 * p + 16.05f;
        float z0 = ctr.Y + j0 * p - 16.05f, z1 = ctr.Y + j1 * p + 16.05f;
        var centre = new Vector2((x0 + x1) / 2f, (z0 + z1) / 2f);
        float hx = (x1 - x0) / 2f, hz = (z1 - z0) / 2f;
        float seed = Mathf.Abs(centre.X * 12.9898f + centre.Y * 78.233f) % 6.2831f;
        var key = ((int)Mathf.Round(centre.X - ctr.X), (int)Mathf.Round(centre.Y - ctr.Y), LevelIndex(lvlTop));
        ClusterSectors.TryGetValue(key, out var secs);
        // The walker grounds on the EXACT outline: one
        // BlobDeck registration (Main evaluates BlobRadius
        // per query — a disc union overhangs the ramp
        // corridors by metres and floated feet at deck
        // height over the ramp heads). The 32 discs remain
        // only as the shot-collision approximation.
        float rMax = 0f;
        for (int k = 0; k < 32; k++)
        {
            float th = k / 32f * Mathf.Tau;
            float r = Main.BlobRadius(hx, hz, th, seed, secs);
            rMax = Mathf.Max(rMax, r);
            var dc = centre + new Vector2(Mathf.Cos(th), Mathf.Sin(th)) * (r * 0.50f);
            Main.DiscRoofs.Add(new Main.DiscRoof { Cx = dc.X, Cz = dc.Y, R = r * 0.53f, Top = lvlTop, Under = lvlTop - 2.2f });
        }
        Main.BlobDecks.Add(new Main.BlobDeck
        {
            Cx = centre.X, Cz = centre.Y, Hx = hx, Hz = hz, Seed = seed,
            Top = lvlTop, Under = lvlTop - 2.2f, RMax = rMax, Sectors = secs
        });
        BuildBlobDeck(x0, x1, z0, z1, lvlTop, secs);
    }

    // One round, curvy deck over a rectangular plot cluster
    // (owner, 2026-11-11 round 5: 'make the platform itself
    // round and curvy' — NOT a rectangle with smoothed
    // edges): the outline is the radial amoeba of
    // Main.BlobRadius, sampled at 72 points around the
    // cluster centre. The shape is star-convex, so the top
    // is a clean fan; bevelled edge (a flare strip below
    // the cap) and the glowing border ribbon follow the
    // same outline; the standable discs registered in
    // BuildPlatformCluster follow the same radii.
    private void BuildBlobDeck(float x0, float x1, float z0, float z1, float top, Main.Sector[] secs = null)
    {
        var centre = new Vector2((x0 + x1) / 2f, (z0 + z1) / 2f);
        float hx = (x1 - x0) / 2f, hz = (z1 - z0) / 2f;
        float seed = Mathf.Abs(centre.X * 12.9898f + centre.Y * 78.233f) % 6.2831f;
        var loop = new List<Vector2>();
        var dir = new List<Vector2>();
        for (int k = 0; k < 72; k++)
        {
            float th = k / 72f * Mathf.Tau;
            var d = new Vector2(Mathf.Cos(th), Mathf.Sin(th));
            dir.Add(d);
            loop.Add(centre + d * Main.BlobRadius(hx, hz, th, seed, secs));
        }
        int n2 = loop.Count;
        var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>(); var idx = new List<int>();
        int V(Vector3 pos, Vector3 nrm)
        {
            verts.Add(pos); norms.Add(nrm); uvs.Add(new Vector2(pos.X / 14f, pos.Z / 14f));
            return verts.Count - 1;
        }
        void Tri(int a, int b, int c) { idx.Add(a); idx.Add(b); idx.Add(c); }
        void Quad(int a, int b, int c, int d) { Tri(a, b, c); Tri(a, c, d); }
        int cTop = V(new Vector3(centre.X, top, centre.Y), Vector3.Up);
        int cBot = V(new Vector3(centre.X, top - 2.2f, centre.Y), Vector3.Down);
        var T = new int[n2]; var O = new int[n2]; var B = new int[n2];
        for (int k = 0; k < n2; k++)
        {
            var d = dir[k];
            var outP = loop[k] + d * 0.55f;
            var bevN = new Vector3(d.X, 0.75f, d.Y).Normalized();
            var sideN = new Vector3(d.X, 0f, d.Y);
            T[k] = V(new Vector3(loop[k].X, top, loop[k].Y), Vector3.Up);
            O[k] = V(new Vector3(outP.X, top - 0.6f, outP.Y), bevN);
            B[k] = V(new Vector3(outP.X, top - 2.2f, outP.Y), sideN);
        }
        for (int k = 0; k < n2; k++)
        {
            int k2 = (k + 1) % n2;
            Tri(cTop, T[k], T[k2]);                    // top cap (fan)
            Quad(T[k], O[k], O[k2], T[k2]);            // bevel flare
            Quad(O[k], B[k], B[k2], O[k2]);            // side wall
            Tri(cBot, B[k2], B[k]);                    // underside (fan)
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = norms.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = idx.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = BlobMat() });
        // Glowing border ribbon along the wavy outline.
        var tv = new List<Vector3>(); var tn = new List<Vector3>(); var tuv = new List<Vector2>(); var tidx = new List<int>();
        for (int k = 0; k < n2; k++)
        {
            var d = dir[k];
            var inner = loop[k] - d * 1.1f;
            tv.Add(new Vector3(loop[k].X, top + 0.06f, loop[k].Y)); tn.Add(Vector3.Up); tuv.Add(new Vector2(loop[k].X / 14f, loop[k].Y / 14f));
            tv.Add(new Vector3(inner.X, top + 0.06f, inner.Y)); tn.Add(Vector3.Up); tuv.Add(new Vector2(inner.X / 14f, inner.Y / 14f));
        }
        for (int k = 0; k < n2; k++)
        {
            int k2 = (k + 1) % n2;
            tidx.Add(k * 2); tidx.Add(k2 * 2); tidx.Add(k2 * 2 + 1);
            tidx.Add(k * 2); tidx.Add(k2 * 2 + 1); tidx.Add(k * 2 + 1);
        }
        var ta = new Godot.Collections.Array();
        ta.Resize((int)Mesh.ArrayType.Max);
        ta[(int)Mesh.ArrayType.Vertex] = tv.ToArray();
        ta[(int)Mesh.ArrayType.Normal] = tn.ToArray();
        ta[(int)Mesh.ArrayType.TexUV] = tuv.ToArray();
        ta[(int)Mesh.ArrayType.Index] = tidx.ToArray();
        var tmesh = new ArrayMesh();
        tmesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, ta);
        var tmat = TrimMat(LevelIndex(top));
        tmat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        AddChild(new MeshInstance3D { Mesh = tmesh, MaterialOverride = tmat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
    }

    // A sloped deck wedge between two points at two heights:
    // a box sunk into the lower deck so its top face lies on
    // the slope line. Shared by terrace ramps and the bridge.
    private void AddRampWedge(Vector2 s0, float h0, Vector2 s1, float h1, float width)
    {
        var mid = (s0 + s1) / 2f;
        var d = s1 - s0;
        float run = d.Length();
        // The box's top face lies exactly ON the slope line
        // (holder at mid-height minus half the box) and extends
        // only 1u past each end — a longer nose poked ~1.1u
        // above the upper deck at the lip and read as the
        // rider sinking into the ramp (owner fall report).
        float len = Mathf.Sqrt(run * run + (h1 - h0) * (h1 - h0)) + 2f;
        var holder = new Node3D { Position = new Vector3(mid.X, (h0 + h1) / 2f - 1.75f, mid.Y) };
        // Local +Z runs up-slope: yaw maps +Z onto (s1 - s0),
        // pitch lifts it by the slope angle (negative X
        // rotation raises +Z in Godot's YXZ euler order).
        float yaw = Mathf.Atan2(d.X, d.Y);
        float pitch = -Mathf.Atan2(h1 - h0, run);
        holder.Rotation = new Vector3(pitch, yaw, 0f);
        // Walking surface: a bumpy metal plate inset in stone
        // (owner, 2026-11-11) — the plate's top lies on the
        // slope line, the stone curbs frame it as a visible
        // border standing slightly proud, each carrying the
        // level's glow line.
        var plate = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(width - 2.4f, 3.5f, len) },
            MaterialOverride = PlateMat()
        };
        holder.AddChild(plate);
        foreach (float sx in new[] { -1f, 1f })
        {
            var curb = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(1.2f, 3.75f, len) },
                MaterialOverride = DeckMat(),
                Position = new Vector3(sx * (width / 2f - 0.6f), 0.125f, 0f)
            };
            holder.AddChild(curb);
            var glow = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.34f, 0.1f, len) },
                MaterialOverride = TrimMat(LevelIndex(Mathf.Max(h0, h1))),
                Position = new Vector3(sx * (width / 2f - 0.6f), 2.03f, 0f),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            };
            holder.AddChild(glow);
        }
        AddChild(holder);
    }

    private void BuildRamp(Vector2 edgeMid, Vector2 dirOut, float lo, float hi, float run)
    {
        var s0 = edgeMid + dirOut * (run - 2f); // foot, on the lower surface
        var s1 = edgeMid - dirOut * 2f;         // lip, at the upper deck's edge
        // Corridor 13 wide (owner fell "through" the 9-wide
        // ramps: a turning rider's stepping foot left the
        // corridor and grounded on the surface far below,
        // dropping the root mid-ramp).
        var rect = new Main.RampRect
        {
            Cx = (s0.X + s1.X) / 2f, Cz = (s0.Y + s1.Y) / 2f,
            DirX = -dirOut.X, DirZ = -dirOut.Y,
            HalfLen = run / 2f, HalfWid = 6.5f, H0 = lo, H1 = hi
        };
        Main.Ramps.Add(rect);
        AddRampWedge(s0, lo, s1, hi, 13f);
        _skipRects.Add(new Main.RoofRect
        {
            Cx = (s0.X + s1.X) / 2f, Cz = (s0.Y + s1.Y) / 2f,
            Hx = Mathf.Abs(dirOut.X) > 0.5f ? run / 2f + 0.5f : 7.5f,
            Hz = Mathf.Abs(dirOut.Y) > 0.5f ? run / 2f + 0.5f : 7.5f, Top = 0f
        });
        if (!HasTestRamp)
        {
            TestRampFoot = s0; TestRampDir = -dirOut; TestRampLo = lo; TestRampHi = hi;
            TestRampRect = rect; TestRampRun = run;
            HasTestRamp = true;
        }
        GD.Print($"TERRACE RAMP {lo:F0}->{hi:F0} at ({edgeMid.X:F0},{edgeMid.Y:F0})");
    }

    private void BuildLift(Vector2 edgeMid, Vector2 dirOut, float lo, float hi)
    {
        var pc = edgeMid + dirOut * 4.6f;
        var pad = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(8.4f, 0.6f, 8.4f) },
            MaterialOverride = DeckMat(),
            Position = new Vector3(pc.X, lo - 0.3f, pc.Y)
        };
        AddChild(pad);
        var trim = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(8.9f, 0.3f, 8.9f) },
            MaterialOverride = TrimMat(LevelIndex(hi)),
            Position = new Vector3(pc.X, lo - 0.12f, pc.Y),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        pad.AddChild(trim); trim.Position = new Vector3(0f, 0.18f, 0f);
        // Guide posts on the pad's outer corners, full travel.
        foreach (var s in new[] { new Vector2(4f, 4f), new Vector2(-4f, 4f), new Vector2(4f, -4f), new Vector2(-4f, -4f) })
        {
            var post = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.35f, hi - lo + 2f, 0.35f) },
                MaterialOverride = DeckMat(),
                Position = new Vector3(pc.X + s.X, (lo + hi) / 2f + 1f, pc.Y + s.Y)
            };
            AddChild(post);
        }
        int roofIdx = Main.Roofs.Count;
        Main.Roofs.Add(new Main.RoofRect { Cx = pc.X, Cz = pc.Y, Hx = 4.2f, Hz = 4.2f, Top = lo, Under = lo - 0.6f });
        _skipRects.Add(new Main.RoofRect { Cx = pc.X, Cz = pc.Y, Hx = 5.4f, Hz = 5.4f, Top = 0f });
        _lifts.Add(new Lift
        {
            C = pc, Lo = lo, Hi = hi, Top = lo, Target = lo,
            RoofIdx = roofIdx, Pad = pad, State = 0
        });
        if (!HasTestLift)
        { TestLiftPad = pc; TestLiftLo = lo; TestLiftHi = hi; HasTestLift = true; }
        GD.Print($"TERRACE LIFT {lo:F0}->{hi:F0} pad at ({pc.X:F0},{pc.Y:F0})");
    }

    // Per-frame lift shuttles: a rider standing on a pad rides
    // it to the other deck; an empty pad at the top returns
    // down after a few seconds so it waits at the bottom.
    private void UpdateLifts(float dt)
    {
        if (_lifts.Count == 0) return;
        foreach (var lf in _lifts)
        {
            RiderWalker occ = null;
            foreach (var w in new[] { _player, _enemy, _foe?.Walker })
            {
                if (w == null || !w.IsReady) continue;
                if (Mathf.Abs(w.RootPos.X - lf.C.X) <= 4.6f &&
                    Mathf.Abs(w.RootPos.Z - lf.C.Y) <= 4.6f &&
                    Mathf.Abs(w.SoleLevel - lf.Top) <= 2.4f) { occ = w; break; }
            }
            if (lf.State == 1)
            {
                float dir = Mathf.Sign(lf.Target - lf.Top);
                lf.Top += dir * 7f * dt;
                if ((dir > 0 && lf.Top >= lf.Target) || (dir < 0 && lf.Top <= lf.Target))
                {
                    lf.Top = lf.Target;
                    lf.State = lf.Target == lf.Hi ? 2 : 0;
                    lf.Dwell = 0f; lf.Empty = 0f;
                }
                var r = Main.Roofs[lf.RoofIdx]; r.Top = lf.Top; r.Under = lf.Top - 0.6f; Main.Roofs[lf.RoofIdx] = r;
                lf.Pad.Position = new Vector3(lf.C.X, lf.Top - 0.3f, lf.C.Y);
            }
            else if (lf.State == 0)
            {
                if (occ != null) { lf.Dwell += dt; if (lf.Dwell > 0.5f) { lf.State = 1; lf.Target = lf.Hi; } }
                else lf.Dwell = 0f;
            }
            else
            {
                if (occ != null)
                {
                    lf.Empty = 0f; lf.Dwell += dt;
                    if (lf.Dwell > 1.0f) { lf.State = 1; lf.Target = lf.Lo; }
                }
                else
                {
                    lf.Dwell = 0f; lf.Empty += dt;
                    if (lf.Empty > 5f) { lf.State = 1; lf.Target = lf.Lo; }
                }
            }
        }
    }

    private Material TierMat(Vector3 size, int seq) => _photo
        ? PhotoEnv.BuildingMaterial(size, BuildingTints[seq % BuildingTints.Length], seq)
        : new StandardMaterial3D { AlbedoColor = new Color(0.4f, 0.42f, 0.48f), Roughness = 0.9f };

    // A composed landmark (owner: the seat of leadership):
    // stacked tiers with lit rings, corner spires and a
    // central spire, registered as one destructible building
    // through the same shell as every other building.
    private void BuildLandmark(Vector2 c, float deckTop, float w, float h, float hp, bool banners)
    {
        int pc0 = _props.Count, hc0 = _hit.Props.Count;
        const float Sink = 2.4f;
        var size = new Vector3(w, h, w);
        var pos = new Vector3(c.X, deckTop + h / 2f - Sink, c.Y);
        var mi = new MeshInstance3D { Position = pos };
        void Tier(float tw, float th, float yBase, int seq)
        {
            var t = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(tw, th, tw) },
                MaterialOverride = TierMat(new Vector3(tw, th, tw), seq),
                Position = new Vector3(0f, yBase + th / 2f - h / 2f, 0f)
            };
            mi.AddChild(t);
            var ring = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(tw + 1.2f, 0.5f, tw + 1.2f) },
                MaterialOverride = TrimMat(LevelIndex(deckTop)),
                Position = new Vector3(0f, yBase + th - h / 2f, 0f),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            };
            mi.AddChild(ring);
        }
        Tier(w, h * 0.24f, 0f, 7);
        Tier(w * 0.72f, h * 0.34f, h * 0.24f, 8);
        Tier(w * 0.48f, h * 0.26f, h * 0.58f, 9);
        var spire = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(w * 0.13f, h * 0.16f, w * 0.13f) },
            MaterialOverride = DeckMat(),
            Position = new Vector3(0f, h * 0.92f - h / 2f, 0f)
        };
        mi.AddChild(spire);
        foreach (var cs in new[] { new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1) })
        {
            var csp = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(w * 0.10f, h * 0.38f, w * 0.10f) },
                MaterialOverride = DeckMat(),
                Position = new Vector3(cs.X * (w / 2f - w * 0.07f), h * 0.19f - h / 2f, cs.Y * (w / 2f - w * 0.07f))
            };
            mi.AddChild(csp);
        }
        if (banners)
        {
            var bmat = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.08f, 0.10f), Roughness = 0.85f };
            foreach (float f in new[] { 1f, -1f })
            {
                var bp = new MeshInstance3D
                {
                    Mesh = new PlaneMesh { Size = new Vector2(w * 0.30f, h * 0.30f) },
                    MaterialOverride = bmat,
                    Position = new Vector3(0f, h * 0.38f - h / 2f, f * (w * 0.36f + 0.06f)),
                    Rotation = new Vector3(0f, f > 0f ? 0f : Mathf.Pi, 0f)
                };
                mi.AddChild(bp);
            }
        }
        AddChild(mi);
        _buildingPts.Add(c);
        RegisterBuildingShell(pos, size, deckTop, Sink, deckTop + h - Sink, pc0, hc0, mi);
        _destructibles[_destructibles.Count - 1].Hp = hp;
    }

}
