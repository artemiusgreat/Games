using Godot;
using System.Collections.Generic;

// Harbor terraces (owner): the flat kit city rebuilt as two
// wide deck RINGS — +22 and +44 — around an open centre
// plaza, split by a ground-level moat ring, as standable
// mega-platforms carrying the kit buildings, with futuristic
// edge-light trim. Levels connect by wide ramps (sloped
// surfaces in Main.Ramps), lift pads at the ring edges and
// bridge-ramps across the moat; an island district floats across a water
// channel east of the city, reached by a single bridge.
// Everything registers through the standing surface system —
// Main.Roofs / Walls / Ramps / DiscRoofs — so grounding,
// wall blocking and roof standing treat decks exactly like
// building roofs. Gated by WorldConfig.TerracedCity (the
// Harbor Stacks contract); every other map is untouched.
public partial class RiderDuel
{
    // Deck heights (owner, 2026-11-10): tall enough that a
    // rider walking UNDER a platform — chase camera included —
    // fits beneath it: the +22 deck's underside is 19.8, above
    // the tallest rider (14) and its camera anchor (~16).
    private const float TerraceL1 = 22f, TerraceL2 = 44f;
    private const float IslandTop = 6f;
    private static readonly Vector2 IslandC = new(430f, 0f);
    private const float IslandR = 64f;

    private sealed class Lift
    {
        public Vector2 C; public float Lo, Hi, Top, Target, Dwell, Empty;
        public int RoofIdx; public MeshInstance3D Pad; public int State; // 0 at Lo, 1 moving, 2 at Hi
    }
    private readonly List<Lift> _lifts = new();

    // Harness hooks: the first ramp and lift built.
    public Vector2 TestRampFoot, TestRampDir;
    public float TestRampLo, TestRampHi, TestRampRun;
    public Main.RampRect TestRampRect;
    public Vector2 TestLiftPad;
    public float TestLiftLo, TestLiftHi;
    public bool HasTestRamp, HasTestLift;
    public bool HasTerraceTest => HasTestRamp && HasTestLift;

    // The decks form RINGS, not a solid filled disc (owner):
    // a wide +44 ring around a small open plaza at the centre
    // (the citadel stands over it), a ground-level moat ring,
    // then the wide +22 ring, then ordinary ground city.
    private static float TerraceLevel(float r) =>
        r < 26f ? 0f : r < 88f ? TerraceL2 : r < 120f ? 0f : r < 200f ? TerraceL1 : 0f;

    // Snow-dusted deck tone (proof frame: the first tone
    // read near-black against the snowfields).
    private static readonly Color DeckCol = new(0.50f, 0.56f, 0.70f);
    private static StandardMaterial3D DeckMat() => new()
    { AlbedoColor = DeckCol, Roughness = 0.85f, Metallic = 0.25f };
    private static StandardMaterial3D TrimMat() => new()
    {
        AlbedoColor = new Color(0.05f, 0.08f, 0.10f),
        EmissionEnabled = true, Emission = new Color(0.25f, 0.75f, 1.0f),
        EmissionEnergyMultiplier = 1.6f, Roughness = 0.6f
    };

    private void BuildTerracedCity(System.Random rng, ref int placed)
    {
        _skipRects.Clear();
        _lifts.Clear();
        float p = CityGrid.Pitch;
        int hb = Mathf.CeilToInt(CityGrid.Radius / p);
        var plots = new List<(Vector2 C, float Lvl)>();
        var ctr = new Dictionary<(int, int), Vector2>();
        var lvl = new Dictionary<(int, int), float>();
        for (int i = -hb; i <= hb; i++)
        for (int j = -hb; j <= hb; j++)
        {
            // NO position jitter here (owner: riders fell
            // through the decks): platforms must TILE — jittered
            // centres left 1-2u slits between deck rects that the
            // visuals bridged but the ground data did not.
            var c = new Vector2(i * p, j * p);
            if (c.Length() > CityGrid.Radius) continue;
            if (TerrainGrade(c.X, c.Y) > 0.4f) continue;
            float l = TerraceLevel(c.Length());
            plots.Add((c, l)); ctr[(i, j)] = c; lvl[(i, j)] = l;
        }
        // Crossings. Ground <-> +22 ring: for each target
        // azimuth, the level-straddling plot pair whose shared
        // edge sits closest to it — OUTER edges (ground side)
        // take ramps at 45/225 and lifts at 135/315; the INNER
        // edges (moat side) take the complementary set.
        var used = new HashSet<(int, int, int, int)>();
        void Crossing(float azDeg, bool isRamp, bool outer)
        {
            float az = Mathf.DegToRad(azDeg);
            (int, int, int, int) bestKey = default; Vector2 bestEdge = default, bestDir = default;
            float bestScore = float.MaxValue;
            foreach (var ((i, j), c) in ctr)
            {
                foreach (var (di, dj) in new[] { (1, 0), (0, 1) })
                {
                    var k2 = (i + di, j + dj);
                    if (!lvl.TryGetValue(k2, out float l2)) continue;
                    float l1 = lvl[(i, j)];
                    if (Mathf.Abs(l1 - l2) != TerraceL1 || Mathf.Min(l1, l2) != 0f) continue;
                    var edge = (c + ctr[k2]) / 2f;
                    if ((edge.Length() > 155f) != outer) continue;
                    float ea = Mathf.Atan2(edge.Y, edge.X);
                    float da = Mathf.Abs(Mathf.Wrap(ea - az, -Mathf.Pi, Mathf.Pi));
                    if (da > 0.6f || da >= bestScore) continue;
                    bestScore = da; bestKey = (i, j, k2.Item1, k2.Item2);
                    bestEdge = edge;
                    bestDir = (l1 < l2 ? c - ctr[k2] : ctr[k2] - c).Normalized();
                }
            }
            if (bestScore == float.MaxValue || used.Contains(bestKey)) return;
            used.Add(bestKey);
            if (isRamp) BuildRamp(bestEdge, bestDir, 0f, TerraceL1, TerraceL1 * 2f);
            else BuildLift(bestEdge, bestDir, 0f, TerraceL1);
        }
        Crossing(45f, true, true); Crossing(225f, true, true);
        Crossing(135f, false, true); Crossing(315f, false, true);
        Crossing(135f, true, false); Crossing(315f, true, false);
        Crossing(45f, false, false); Crossing(225f, false, false);
        // Ring <-> ring: BRIDGE-RAMPS at the four cardinal
        // azimuths (owner: bridges / ramps connecting the
        // rings) — a long ramp from the +22 deck across the
        // moat up to the +44 ring's edge.
        foreach (var dir in new[] { new Vector2(1, 0), new Vector2(-1, 0), new Vector2(0, 1), new Vector2(0, -1) })
        {
            Vector2 l2c = default; float l2p = -1f;
            Vector2 l1c = default; float l1p = float.MaxValue;
            foreach (var ((i, j), c) in ctr)
            {
                if (Mathf.Abs(c.X * dir.Y - c.Y * dir.X) > 1f) continue; // on the axis row
                float proj = c.X * dir.X + c.Y * dir.Y;
                if (lvl[(i, j)] == TerraceL2 && proj > l2p) { l2p = proj; l2c = c; }
            }
            foreach (var ((i, j), c) in ctr)
            {
                if (Mathf.Abs(c.X * dir.Y - c.Y * dir.X) > 1f) continue;
                float proj = c.X * dir.X + c.Y * dir.Y;
                if (lvl[(i, j)] == TerraceL1 && proj > l2p && proj < l1p) { l1p = proj; l1c = c; }
            }
            if (l2p < 0f || l1p == float.MaxValue) continue;
            var edge = l2c + dir * (CityGrid.Pitch / 2f);
            BuildRamp(edge, dir, TerraceL1, TerraceL2, (TerraceL2 - TerraceL1) * 2f);
            GD.Print($"TERRACE BRIDGE {TerraceL1:F0}->{TerraceL2:F0} at ({edge.X:F0},{edge.Y:F0})");
        }
        // The citadel clears the center: no buildings on the
        // plots under its deck or in its ramp strip.
        _skipRects.Add(new Main.RoofRect { Cx = 0f, Cz = 0f, Hx = 40f, Hz = 40f, Top = 0f });
        _skipRects.Add(new Main.RoofRect { Cx = 0f, Cz = 44.5f, Hx = 5.5f, Hz = 15f, Top = 0f });
        foreach (var (c, l) in plots)
        {
            if (l > 0f) BuildPlatform(c, l);
            PlaceCityBlock(rng, c, ref placed, l);
        }
        BuildCitadel();
        BuildIsland(rng, ref placed);
        GD.Print($"TERRACES built: {_lifts.Count} lifts, platforms+ramps+island registered (roofs={Main.Roofs.Count} walls={Main.Walls.Count} ramps={Main.Ramps.Count})");
    }

    private void BuildPlatform(Vector2 c, float lvlTop)
    {
        // A deck slab on columns (owner: platforms stand on
        // pillars, not solid blocks) — the space under a deck
        // is open ground a rider can walk through; the corner
        // columns are the only obstacles down there.
        var mi = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(31.9f, 2.2f, 31.9f) },
            MaterialOverride = DeckMat(),
            Position = new Vector3(c.X, lvlTop - 1.1f, c.Y)
        };
        AddChild(mi);
        foreach (var s2 in new[] { new Vector2(13.5f, 13.5f), new Vector2(-13.5f, 13.5f), new Vector2(13.5f, -13.5f), new Vector2(-13.5f, -13.5f) })
        {
            var col = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(1.7f, lvlTop - 2.2f, 1.7f) },
                MaterialOverride = DeckMat(),
                Position = new Vector3(c.X + s2.X, (lvlTop - 2.2f) / 2f, c.Y + s2.Y)
            };
            AddChild(col);
            _props.Add(new Circle { P = c + s2, R = 1.15f, RoofTop = lvlTop });
        }
        var trim = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(32.5f, 0.55f, 32.5f) },
            MaterialOverride = TrimMat(),
            Position = new Vector3(c.X, lvlTop - 0.28f, c.Y),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        AddChild(trim);
        Main.Roofs.Add(new Main.RoofRect { Cx = c.X, Cz = c.Y, Hx = 16.05f, Hz = 16.05f, Top = lvlTop, Under = lvlTop - 2.2f });
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
        var mi = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(width, 3.5f, len) },
            MaterialOverride = DeckMat()
        };
        holder.AddChild(mi);
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
            MaterialOverride = TrimMat(),
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
                MaterialOverride = TrimMat(),
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

    // The central citadel deck (owner): one platform at the
    // center above the top deck, carrying the leadership
    // landmark, reached by a single ramp from the +32 deck.
    private void BuildCitadel()
    {
        const float top = TerraceL2 + 8f; // 40
        var slab = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 33f, BottomRadius = 34f, Height = 2.2f, RadialSegments = 36 },
            MaterialOverride = DeckMat(),
            Position = new Vector3(0f, top - 1.1f, 0f)
        };
        AddChild(slab);
        var rim = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 32.5f, OuterRadius = 33.9f, Rings = 36, RingSegments = 6 },
            MaterialOverride = TrimMat(),
            Position = new Vector3(0f, top - 0.1f, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        AddChild(rim);
        for (int k = 0; k < 6; k++)
        {
            float a2 = k / 6f * Mathf.Tau;
            var col = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 1.1f, BottomRadius = 1.1f, Height = top - TerraceL2 - 1.2f, RadialSegments = 10 },
                MaterialOverride = DeckMat(),
                Position = new Vector3(Mathf.Cos(a2) * 29f, TerraceL2 - 1.0f + (top - TerraceL2 - 1.2f) / 2f, Mathf.Sin(a2) * 29f)
            };
            AddChild(col);
        }
        Main.DiscRoofs.Add(new Main.DiscRoof { Cx = 0f, Cz = 0f, R = 33f, Top = top, Under = top - 2.2f });
        _props.Add(new Circle { P = Vector2.Zero, R = 32.6f, RoofTop = top });
        var s0 = new Vector2(0f, 58f); var s1 = new Vector2(0f, 31f);
        Main.Ramps.Add(new Main.RampRect
        {
            Cx = 0f, Cz = 44.5f, DirX = 0f, DirZ = -1f,
            HalfLen = 13.5f, HalfWid = 6.5f, H0 = TerraceL2, H1 = top
        });
        AddRampWedge(s0, TerraceL2, s1, top, 13f);
        BuildLandmark(Vector2.Zero, top, 30f, 52f, 200f, true);
        GD.Print($"TERRACE CITADEL deck top={top:F0} at the center, landmark 30x52");
    }

    private void BuildIsland(System.Random rng, ref int placed)
    {
        // Island deck: a round terrace across the water.
        var deck = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = IslandR, BottomRadius = IslandR + 2f, Height = IslandTop + 2f, RadialSegments = 36 },
            MaterialOverride = DeckMat(),
            Position = new Vector3(IslandC.X, IslandTop / 2f - 1f, IslandC.Y)
        };
        AddChild(deck);
        // A torus, not a cylinder: a cylinder's top cap is a
        // full disc and painted the whole deck emissive.
        var rim = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = IslandR - 0.5f, OuterRadius = IslandR + 0.9f, Rings = 36, RingSegments = 6 },
            MaterialOverride = TrimMat(),
            Position = new Vector3(IslandC.X, IslandTop - 0.1f, IslandC.Y),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        AddChild(rim);
        // The island deck stands on columns in the water.
        for (int k = 0; k < 8; k++)
        {
            float a3 = k / 8f * Mathf.Tau + 0.2f;
            var col = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = 1.3f, BottomRadius = 1.3f, Height = IslandTop + 1f, RadialSegments = 10 },
                MaterialOverride = DeckMat(),
                Position = new Vector3(IslandC.X + Mathf.Cos(a3) * 56f, (IslandTop - 2f) / 2f, IslandC.Y + Mathf.Sin(a3) * 56f)
            };
            AddChild(col);
        }
        Main.DiscRoofs.Add(new Main.DiscRoof { Cx = IslandC.X, Cz = IslandC.Y, R = IslandR, Top = IslandTop });
        _props.Add(new Circle { P = IslandC, R = IslandR + 0.4f, RoofTop = IslandTop });
        // The water: a still, dark sheet around the island.
        var water = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(250f, 250f) },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.10f, 0.22f, 0.38f, 0.85f),
                Roughness = 0.15f, Metallic = 0.35f,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha
            },
            Position = new Vector3(IslandC.X, 0.4f, IslandC.Y),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        AddChild(water);
        // The bridge: a sloped span up from the city rim, then a
        // flat deck to the island edge — the only crossing.
        Main.Ramps.Add(new Main.RampRect
        {
            Cx = 293f, Cz = 0f, DirX = 1f, DirZ = 0f,
            HalfLen = 13f, HalfWid = 5.5f, H0 = 0f, H1 = IslandTop
        });
        AddRampWedge(new Vector2(280f, 0f), 0f, new Vector2(306f, 0f), IslandTop, 11f);
        Main.Roofs.Add(new Main.RoofRect { Cx = 336f, Cz = 0f, Hx = 30f, Hz = 5.5f, Top = IslandTop, Under = IslandTop - 1.2f });
        var deckFlat = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(60f, 1.2f, 11f) },
            MaterialOverride = DeckMat(),
            Position = new Vector3(336f, IslandTop - 0.6f, 0f)
        };
        AddChild(deckFlat);
        foreach (float side in new[] { -5.4f, 5.4f })
        {
            Main.Walls.Add(new Main.RoofRect { Cx = 323f, Cz = side, Hx = 43f, Hz = 0.7f, Top = IslandTop });
            var rail = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(86f, 0.35f, 0.35f) },
                MaterialOverride = TrimMat(),
                Position = new Vector3(323f, IslandTop + 0.9f, side),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            };
            AddChild(rail);
        }
        // The island's own dais (owner: the island was too
        // primitive): a second, smaller deck at +12 carrying
        // a landmark spire, up a ramp from the island deck.
        const float daisTop = IslandTop + 6f;
        var dais = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 30f, BottomRadius = 31f, Height = daisTop - IslandTop + 1f, RadialSegments = 30 },
            MaterialOverride = DeckMat(),
            Position = new Vector3(IslandC.X, (IslandTop + daisTop) / 2f - 0.5f, IslandC.Y)
        };
        AddChild(dais);
        var daisRim = new MeshInstance3D
        {
            Mesh = new TorusMesh { InnerRadius = 29.5f, OuterRadius = 30.9f, Rings = 30, RingSegments = 6 },
            MaterialOverride = TrimMat(),
            Position = new Vector3(IslandC.X, daisTop - 0.1f, IslandC.Y),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        AddChild(daisRim);
        Main.DiscRoofs.Add(new Main.DiscRoof { Cx = IslandC.X, Cz = IslandC.Y, R = 30f, Top = daisTop });
        _props.Add(new Circle { P = IslandC, R = 29.6f, RoofTop = daisTop });
        Main.Ramps.Add(new Main.RampRect
        {
            Cx = IslandC.X - 43f, Cz = 0f, DirX = 1f, DirZ = 0f,
            HalfLen = 13f, HalfWid = 5.5f, H0 = IslandTop, H1 = daisTop
        });
        AddRampWedge(new Vector2(IslandC.X - 56f, 0f), IslandTop, new Vector2(IslandC.X - 30f, 0f), daisTop, 11f);
        BuildLandmark(IslandC, daisTop, 20f, 34f, 120f, false);
        // Island blocks on the deck, clear of the bridge
        // landing (west) and the dais.
        for (int gx = -1; gx <= 1; gx++)
        for (int gz = -1; gz <= 1; gz++)
        {
            var c = IslandC + new Vector2(gx * CityGrid.Pitch, gz * CityGrid.Pitch);
            if (c.DistanceTo(IslandC) > 46f) continue;
            if (c.DistanceTo(IslandC) < 34f) continue;
            if (c.X > 384f && Mathf.Abs(c.Y) < 20f) continue;
            PlaceCityBlock(rng, c, ref placed, IslandTop);
        }
        GD.Print($"TERRACE ISLAND at ({IslandC.X:F0},{IslandC.Y:F0}) r={IslandR:F0} top={IslandTop:F0}, bridge from the city rim");
    }
}
