using Godot;

// Titans module: the Ascent campaign world's dressing
// (owner, 2026-11-20) - the villages, river bridges and the
// layered summit city for the Brisbane contract (the Ascent map). All positions
// come from Ascent.cs, the same table the heightfield and the
// guard spawner read. The higher the site, the taller and more
// advanced its buildings: mud-flat shanties at the valley
// mouth, towers and stacked platforms at the citadel.
public partial class Titans
{
    // Bridge pillar cross-section (the deck pillars are
    // square posts; owner, 2026-11-25: named constant).
    private const float BridgePillarWidth = 3f;

    // River-bridge deck and dressing conventions (Stage B
    // naming pass, 2026-11-27: names only, exact values).
    private const float RiverBridgeDeckThickness = 1.6f;
    private const float DressingRockTint = 0.5f;

    private void BuildAscentWorld()
    {
        // ---- Villages: a ring of buildings around each site's
        // muster green (kept clear for the guards), growing
        // taller and denser with altitude.
        var offs = new[]
        {
            new Vector2(30f, 10f), new Vector2(-28f, 24f),
            new Vector2(12f, -34f), new Vector2(-36f, -14f),
            new Vector2(42f, -28f), new Vector2(-52f, 42f),
            new Vector2(56f, 6f)
        };
        for (int s = 0; s < Ascent.Sites.Length; s++)
        {
            var site = Ascent.Sites[s];
            float rot = s * 1.3f;
            float hBase = 6.5f + s * 1.7f; // the climb gets more advanced
            for (int i = 0; i < offs.Length; i++)
            {
                var o = offs[i].Rotated(rot);
                var pos = new Vector3(site.C.X + o.X, 0f, site.C.Y + o.Y);
                float h = hBase * (1f + ((s * 3 + i) % 3 - 1) * 0.16f);
                float w = 9f + ((s + i * 2) % 3) * 2f;
                AddBuilding(pos, new Vector3(w, h, w * 0.9f));
            }
            if (s >= 2)
            {
                // Comms mast: the richer villages are wired.
                var m = new Vector2(22f, -26f).Rotated(rot);
                AddBuilding(new Vector3(site.C.X + m.X, 0f, site.C.Y + m.Y),
                    new Vector3(3f, 19f + s * 2f, 3f));
            }
            for (int r = 0; r < 3; r++)
            {
                float a = rot + r * 2.3f + 0.7f;
                var rp = new Vector2(site.C.X + Mathf.Cos(a) * (site.R + 14f),
                                     site.C.Y + Mathf.Sin(a) * (site.R + 14f));
                AddRock(new Vector3(rp.X, Main.HeightAt(rp.X, rp.Y), rp.Y),
                    new Vector3(4f + r, 3f + r * 0.7f, 4.5f), a, DressingRockTint);
            }
        }

        // ---- Road dressing (owner, 2026-11-20: more objects,
        // the statics cost almost nothing next to the riders):
        // a fortified checkpoint mid-valley and boulders
        // scattered along the run and up the slopes.
        {
            float cz = -60f, cx = Ascent.RoadX(cz);
            AddBuilding(new Vector3(cx + 26f, 0f, cz), new Vector3(6f, 24f, 6f)); // watchtower
            AddBuilding(new Vector3(cx - 14f, 0f, cz + 13f), new Vector3(11f, 3.5f, 2.5f)); // gate barriers
            AddBuilding(new Vector3(cx + 14f, 0f, cz - 13f), new Vector3(11f, 3.5f, 2.5f));
            AddBuilding(new Vector3(cx - 21f, 0f, cz - 9f), new Vector3(4.5f, 2.6f, 4.5f)); // crate stacks
            AddBuilding(new Vector3(cx + 21f, 0f, cz + 10f), new Vector3(4.5f, 2.6f, 4.5f));
            var rngR = new System.Random(0x90AD);
            for (float z = 770f; z > -700f; z -= 118f)
            {
                float side = (rngR.NextSingle() < 0.5f) ? -1f : 1f;
                float rx = Ascent.RoadX(z) + side * (30f + rngR.NextSingle() * 26f);
                AddRock(new Vector3(rx, Main.HeightAt(rx, z), z),
                    new Vector3(3f + rngR.NextSingle() * 3f, 2.4f + rngR.NextSingle() * 2f, 3.5f),
                    rngR.NextSingle() * 6.28f, DressingRockTint);
            }
            for (int i = 0; i < 26; i++)
            {
                float z = -720f + rngR.NextSingle() * 1520f;
                float side = (i % 2 == 0) ? -1f : 1f;
                float rx = Ascent.RoadX(z) + side * (150f + rngR.NextSingle() * 190f);
                AddRock(new Vector3(rx, Main.HeightAt(rx, z), z),
                    new Vector3(5f + rngR.NextSingle() * 6f, 4f + rngR.NextSingle() * 4f, 5.5f),
                    rngR.NextSingle() * 6.28f, DressingRockTint);
            }
        }

        // ---- Rivers: water in the channel and a deck bridge
        // where the road crosses.
        var waterMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.13f, 0.32f, 0.48f, 0.85f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 0.22f, Metallic = 0.15f
        };
        foreach (float rz in Ascent.Rivers)
        {
            float bankY = Ascent.Profile(rz);
            var water = new MeshInstance3D
            {
                Mesh = new PlaneMesh { Size = new Vector2(1900f, 46f) },
                MaterialOverride = waterMat,
                Position = new Vector3(0f, bankY - 5.0f, rz)
            };
            AddChild(water);
            float cx = Ascent.RoadX(rz);
            float top = bankY + 0.5f;
            Main.Roofs.Add(new Main.RoofRect
            {
                Cx = cx, Cz = rz, Hx = 17f, Hz = 34f, Top = top, Under = top - 2.1f
            });
            var deck = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(34f, RiverBridgeDeckThickness, 68f) },
                MaterialOverride = DeckMat(),
                Position = new Vector3(cx, top - RiverBridgeDeckThickness / 2f, rz)
            };
            AddChild(deck);
            var railMat = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.16f, 0.17f, 0.19f),
                Metallic = 0.55f, Roughness = 0.5f
            };
            foreach (float side in new[] { -16.4f, 16.4f })
            {
                var rail = new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(0.5f, 1.1f, 68f) },
                    MaterialOverride = railMat,
                    Position = new Vector3(cx + side, top + 0.55f, rz)
                };
                AddChild(rail);
            }
            foreach (float pz in new[] { -18f, 18f })
            {
                var pillar = new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(BridgePillarWidth, 8f, BridgePillarWidth) },
                    MaterialOverride = DeckMat(),
                    Position = new Vector3(cx, top - 4.8f, rz + pz)
                };
                AddChild(pillar);
            }
        }

        // ---- The summit city: two stacked platform levels
        // over the plateau, ramps between them, towers on every
        // level and the citadel keep on top.
        var sc = Ascent.Summit;
        float h0 = Ascent.SummitH;
        AddSummitDeck(sc + new Vector2(0f, 8f), 104f, 66f, h0 + 15f);
        AddSummitDeck(sc + new Vector2(0f, -30f), 58f, 40f, h0 + 30f);
        AddSummitRamp(new Vector2(sc.X, sc.Y + 89f), h0, h0 + 15f, 13f);
        AddSummitRamp(new Vector2(sc.X, sc.Y + 25f), h0 + 15f, h0 + 30f, 11f);
        // Plateau towers around the decks.
        var towerA = new[] { 0.5f, 1.25f, 2.0f, 2.7f, 3.6f, 4.3f, 5.1f, 5.8f };
        for (int i = 0; i < towerA.Length; i++)
        {
            float a = towerA[i];
            if (Mathf.Abs(Mathf.Sin(a)) < 0.30f && Mathf.Cos(a) > 0f) continue; // keep the south ramp corridor clear
            var tp = sc + new Vector2(Mathf.Cos(a) * 132f, Mathf.Sin(a) * 132f);
            float th = 26f + (i % 4) * 6f;
            AddBuilding(new Vector3(tp.X, 0f, tp.Y), new Vector3(14f + (i % 3) * 2f, th, 14f));
        }
        // Towers on the first platform.
        foreach (var o in new[] { new Vector2(-62f, -18f), new Vector2(64f, -24f), new Vector2(-30f, 44f) })
        {
            var tp = sc + new Vector2(0f, 8f) + o;
            AddBuilding(new Vector3(tp.X, 0f, tp.Y), new Vector3(13f, 20f + (o.X > 0f ? 6f : 0f), 13f), h0 + 15f);
        }
        // The citadel keep and its flanks on the top platform.
        var keep = sc + new Vector2(0f, -34f);
        AddBuilding(new Vector3(keep.X, 0f, keep.Y), new Vector3(22f, 40f, 22f), h0 + 30f);
        foreach (float side in new[] { -32f, 32f })
        {
            var fp = sc + new Vector2(side, -28f);
            AddBuilding(new Vector3(fp.X, 0f, fp.Y), new Vector3(12f, 24f, 12f), h0 + 30f);
        }
        Dbg.Print($"ASCENT world built: {Ascent.Sites.Length} villages, {Ascent.Rivers.Length} bridged rivers, summit city at h={h0:F0}");
    }

    // One stacked platform: the walkable RoofRect registration,
    // the slab visual and the columns that hold it up.
    private void AddSummitDeck(Vector2 c, float hx, float hz, float top)
    {
        Main.Roofs.Add(new Main.RoofRect
        {
            Cx = c.X, Cz = c.Y, Hx = hx, Hz = hz, Top = top, Under = top - 1.6f
        });
        var slab = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(hx * 2f, DeckThickness, hz * 2f) },
            MaterialOverride = DeckMat(),
            Position = new Vector3(c.X, top - DeckThickness / 2f, c.Y)
        };
        AddChild(slab);
        float ground = Main.HeightAt(c.X, c.Y);
        foreach (float ox in new[] { -hx + 8f, 0f, hx - 8f })
        foreach (float oz in new[] { -hz + 8f, hz - 8f })
        {
            float colH = top - ground;
            if (colH < 3f) continue;
            var col = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(5f, colH, 5f) },
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
