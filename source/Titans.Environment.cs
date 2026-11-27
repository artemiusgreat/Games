using Godot;
using System.Collections.Generic;

// Titans module: terrain, props, destructibles, debris chunks, collisions.
// Split from the single-file build (round 22 cleanup); the class
// stays one partial class - see Titans.cs for the field block.
public partial class Titans
{
    private struct Circle { public Vector2 P; public float R; public float RoofTop; } // RoofTop > 0: a building circle whose push-out lifts at roof level.

    // Destructible environment (owner): buildings take weapon damage
    // and collapse into rubble; trees burn and smoke away.
    // (owner, 2026-11-27: the state is an enum, not magic
    // numbers. Value 1 was a retired separate burn phase -
    // a felled tree now goes straight to SmokingOut.)
    private enum DestrState { Intact = 0, Collapsing = 2, SmokingOut = 3, Gone = 4 }

    private sealed class Destructible
    {
        public string Kind;
        public Node3D Node;
        public Vector3 Center;      // base center (Y = ground)
        public float Radius;        // hit-attribution radius (XZ)
        public float Height;
        public float Hp;
        public DestrState State;
        public float Timer;
        public float PhaseLen;
        public float PuffAcc;
        public float FxAcc;
        public float StartScale = 1f;
        public readonly System.Collections.Generic.List<HitModel.PropCyl> Cyls = new();
        public readonly System.Collections.Generic.List<Vector2> CirclePts = new();
    }
    private readonly System.Collections.Generic.List<Destructible> _destructibles = new();

    // Rubble chunks thrown by a destruction (round 7): simple ballistic
    // boxes that tumble, bounce once and settle as permanent rubble.
    private sealed class EnvChunk
    {
        public MeshInstance3D Node;
        public Vector3 Vel;
        public Vector3 AngAxis = Vector3.Right;
        public float AngSpeed;
        public float Radius;
        public bool Settled;
        public float Life = -1f; // >0: seconds until the piece dissolves into dust (buildings); -1 = permanent (tree splinters)
    }
    private readonly System.Collections.Generic.List<EnvChunk> _envChunks = new();
    private int _envFxEvents; // destruction-FX firings (harness-visible)
    private static readonly StandardMaterial3D RubbleMat = new StandardMaterial3D
    { AlbedoColor = RubbleColor, Roughness = RubbleRoughness };
    private static readonly StandardMaterial3D SplinterMat = new StandardMaterial3D
    { AlbedoColor = SplinterColor, Roughness = SplinterRoughness };
    private static readonly Color RubbleColor = new(0.32f, 0.30f, 0.27f);
    private const float RubbleRoughness = 0.95f;
    private static readonly Color SplinterColor = new(0.23f, 0.16f, 0.09f);
    private const float SplinterRoughness = 1f;
    // Environment chunk spawn recipe and physics (the
    // destruction debris of trees and buildings).
    private const float ChunkSquashMin = 0.5f, ChunkSquashMax = 0.9f;   // chunk height as a fraction of its size
    private const float ChunkSpawnRadius = 1.2f;
    private const float ChunkSpawnHeightMin = 0.5f, ChunkSpawnHeightMax = 2.2f;
    private const float ChunkSpeedMin = 1.2f, ChunkSpeedMax = 3.8f;
    private const float ChunkAngSpeedMin = 2f, ChunkAngSpeedMax = 5.5f;
    private const float ChunkRadiusFactor = 0.55f;  // resting radius as a fraction of size
    private const float ChunkLifeJitterMin = 0.8f, ChunkLifeJitterMax = 1.25f;
    private const float ChunkGravity = 20f;
    private const float ChunkSubstepSeconds = 1f / 120f;
    private const float ChunkMinAngSpeed = 0.02f;
    private const float ChunkBounceMinSpeed = 1.6f;
    private const float ChunkRestitution = 0.22f;
    private const float ChunkBounceFriction = 0.55f, ChunkBounceAngDamp = 0.45f;
    private const float PropClearanceMargin = 2.0f; // placement keeps this much extra room around a prop
    private const float PitRiseMax = 4f;            // ground climbing more than this around a plot reads as a bowl
    private const float PitDropMax = 9f;            // ...and ground falling more than this reads as a cliff edge
    // Procedural tree materials.
    private static readonly Color TrunkColor = new(0.36f, 0.23f, 0.12f);
    private const float TrunkRoughness = 0.95f;
    private static readonly Color CrownColorA = new(0.13f, 0.42f, 0.14f);
    private static readonly Color CrownColorB = new(0.20f, 0.50f, 0.16f);
    private static readonly Color CrownColorC = new(0.09f, 0.34f, 0.11f);
    private const float CrownTintWeightA = 0.05f, CrownTintWeightB = 0.05f, CrownTintWeightC = 0.04f; // per-tree tint added to the red channel
    private const float CrownRoughness = 0.9f;
    // Building factory materials: glass panes, detail
    // trim, plain walls/roofs and signs.
    private const float WallRoughness = 0.92f;
    private const float CityLitRatio = 0.34f, TownLitRatio = 0.16f; // lit-pane share by biome

    private void SpawnChunks(Vector3 center, int count, float sizeMin, float sizeMax,
        Material mat, float upMin, float upMax, float life = -1f)
    {
        for (int i = 0; i < count; i++)
        {
            float size = _fxRng.RandfRange(sizeMin, sizeMax);
            var mi = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(size, size * _fxRng.RandfRange(ChunkSquashMin, ChunkSquashMax), size) },
                MaterialOverride = mat,
            };
            float ang = _fxRng.RandfRange(0f, Mathf.Tau);
            mi.GlobalPosition = center + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * _fxRng.RandfRange(0f, ChunkSpawnRadius)
                   + Vector3.Up * _fxRng.RandfRange(ChunkSpawnHeightMin, ChunkSpawnHeightMax);
            AddChild(mi);
            var dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
            _envChunks.Add(new EnvChunk
            {
                Node = mi,
                Vel = dir * _fxRng.RandfRange(ChunkSpeedMin, ChunkSpeedMax) + Vector3.Up * _fxRng.RandfRange(upMin, upMax),
                AngAxis = new Vector3(_fxRng.RandfRange(-1f, 1f), _fxRng.RandfRange(-1f, 1f), _fxRng.RandfRange(-1f, 1f)).Normalized(),
                AngSpeed = _fxRng.RandfRange(ChunkAngSpeedMin, ChunkAngSpeedMax),
                Radius = size * ChunkRadiusFactor,
                Life = life > 0f ? life * _fxRng.RandfRange(ChunkLifeJitterMin, ChunkLifeJitterMax) : -1f,
            });
        }
    }

    private void StepEnvChunks(float dt)
    {
        if (_envChunks.Count == 0 || dt <= 0f) return;
        // Timed pieces (building debris) are freed at end of life.
        for (int i = _envChunks.Count - 1; i >= 0; i--)
        {
            var c = _envChunks[i];
            if (c.Life <= 0f) continue;
            c.Life -= dt;
            if (c.Life > 0f) continue;
            c.Node.QueueFree(); // no dust dissolve (owner, 2026-11-20: no smoke bubbles)
            _envChunks.RemoveAt(i);
        }
        if (_envChunks.Count == 0) return;
        int n = Mathf.Max(1, Mathf.CeilToInt(dt / ChunkSubstepSeconds));
        float h = dt / n;
        for (int step = 0; step < n; step++)
            foreach (var c in _envChunks)
            {
                if (c.Settled) continue;
                c.Vel.Y -= ChunkGravity * h;
                c.Node.GlobalPosition += c.Vel * h;
                if (c.AngSpeed > ChunkMinAngSpeed)
                {
                    var gt = c.Node.GlobalTransform;
                    var nb = new Basis(new Quaternion(c.AngAxis, c.AngSpeed * h)) * gt.Basis;
                    c.Node.GlobalTransform = new Transform3D(nb, gt.Origin);
                }
                var pos = c.Node.GlobalPosition;
                float groundY = Main.HeightAt(pos.X, pos.Z) + c.Radius;
                if (pos.Y <= groundY)
                {
                    c.Node.GlobalPosition = new Vector3(pos.X, groundY, pos.Z);
                    if (c.Vel.Y < -ChunkBounceMinSpeed) { c.Vel.Y = -c.Vel.Y * ChunkRestitution; c.Vel.X *= ChunkBounceFriction; c.Vel.Z *= ChunkBounceFriction; c.AngSpeed *= ChunkBounceAngDamp; }
                    else { c.Settled = true; c.Vel = Vector3.Zero; c.AngSpeed = 0f; }
                }
            }
    }
    private readonly List<Circle> _props = new();
    private readonly System.Collections.Generic.List<Vector2> _buildingPts = new();

    // ---------- world ----------

    private MeshInstance3D BuildTerrainMesh()
    {
        if (WorldConfig.Ascent)
        {
            // The Ascent ground is built in 4x4 chunks over
            // the same grid, so the streaming pass can unload
            // the far end of the valley (owner, 2026-11-20).
            MeshInstance3D first = null;
            int chunk = Main.TerrainSegs / 4;
            for (int cj = 0; cj < 4; cj++)
            for (int ci = 0; ci < 4; ci++)
            {
                var cmi = new MeshInstance3D
                {
                    Mesh = Main.CreateTerrainMeshRange(
                        ci * chunk, (ci + 1) * chunk, cj * chunk, (cj + 1) * chunk)
                };
                AddChild(cmi);
                _terrainChunks.Add(cmi);
                float cx = -Main.TerrainSize / 2f + (ci + 0.5f) * (Main.TerrainSize / 4f);
                float cz = -Main.TerrainSize / 2f + (cj + 0.5f) * (Main.TerrainSize / 4f);
                _streamed.Add((cmi, new Vector2(cx, cz), TerrainChunkStreamRadius));
                if (first == null) first = cmi;
            }
            return first;
        }
        // Shared heightfield mesh (vertices on grid nodes = HeightAt).
        var mi = new MeshInstance3D { Mesh = Main.CreateTerrainMesh() };
        AddChild(mi);
        return mi;
    }

    // The photo skin is its own build step (the splat bake is
    // a 1024x1024 per-pixel pass): _Ready yields a frame
    // between the mesh and the skin so neither stall swallows
    // the loading ripple whole (owner, 2026-11-15).
    private void SkinTerrain(MeshInstance3D mi)
    {
        // A Photoreal contract gets the photo splat in the
        // standard scene too (owner, 2026-11-20, consol-
        // idated on Babylon) - scanned rock/dirt/grass sets
        // blended by the baked splat instead of the flat
        // vertex-colour ground.
        if (_photo || WorldConfig.Photoreal)
        {
            var mat = PhotoEnv.TerrainMaterial();
            mi.MaterialOverride = mat;
            foreach (var c in _terrainChunks) c.MaterialOverride = mat;
        }
    }

    // Mission scatter: groves and rock fields cluster around the
    // bases (cover where fights happen) with sparse wild growth
    // across the front, so the open country still reads empty.
    // Mission grove zones (owner, 2026-11-27: constants, not
    // literals at the Add call): a grove around each spawn
    // pad, plus the sparse wild growth across the front.
    private const float PadGroveRadius = 250f;
    private const int PadGroveTrees = 12;
    private const float WildGroveRadius = 700f;
    private const int WildGroveTrees = 26;

    private void ScatterNatureMission()
    {
        var rng = new System.Random(0x5EED);
        var zones = new System.Collections.Generic.List<(Vector2 C, float R, int Trees)>();
        foreach (var pad in Main.SpawnPads) zones.Add((pad, PadGroveRadius, PadGroveTrees));
        zones.Add((Vector2.Zero, WildGroveRadius, WildGroveTrees)); // wild, centered
        var ztMul = WorldConfig.Biome switch
        {
            WorldConfig.BiomeKind.Desert => DesertTreeMul,
            WorldConfig.BiomeKind.City => CityTreeMul,
            _ => 1f
        };
        foreach (var (c, rad, treeN0) in zones)
        {
            int treeN = (int)(treeN0 * ztMul);
            int trees = 0, tries = 0;
            while (trees < treeN && tries++ < ScatterMaxTriesMission)
            {
                float ang = (float)rng.NextDouble() * Mathf.Tau;
                float rr = ScatterTreeMinRadius + (rad - ScatterTreeMinRadius) * Mathf.Sqrt((float)rng.NextDouble());
                var p = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rr;
                if (Mathf.Abs(p.X) > ScatterBoundMission || Mathf.Abs(p.Y) > ScatterBoundMission) continue;
                float sc = ScatterTreeScaleBase + (float)rng.NextDouble() * ScatterTreeScaleRange;
                if (!ScatterFree(p, TreeRadiusFactor * sc + ScatterTreeClearance)) continue;
                if (TerrainGrade(p.X, p.Y) > ScatterTreeGradeMax) continue;
                if (Main.HeightAt(p.X, p.Y) > ScatterTreeHeightMax) continue;
                AddTreeV(new Vector3(p.X, 0f, p.Y), rng.Next(3), sc, (float)rng.NextDouble());
                trees++;
            }
        }
    }

    private void AddTree(Vector3 pos) => AddTreeV(pos, 0, 1f, 0f);

    // Sets a fade-out visibility range on every geometry
    // instance under a holder (see AddTreeV).
    private static void ApplyVisibilityRange(Node root, float end, float margin)
    {
        if (root is GeometryInstance3D gi)
        {
            gi.VisibilityRangeEnd = end;
            gi.VisibilityRangeEndMargin = margin;
            gi.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self;
        }
        foreach (var child in root.GetChildren()) ApplyVisibilityRange(child, end, margin);
    }

    // Tree varieties: 0 classic, 1 pine, 2 broadleaf. Detail pass
    // (owner): a tapered trunk, real branches reaching out of it, and a
    // canopy of separate leaf clusters in varied greens instead of one
    // smooth blob. Branch/blob placement is seeded from the tree's
    // position, so the forest is stable between runs. Collision circle
    // + hit-model cylinder are unchanged, and the whole tree still
    // burns and evaporates as one destructible node.
    private static readonly CylinderMesh BranchMesh = new()
    { TopRadius = BranchTopRadius, BottomRadius = BranchBottomRadius, Height = 1f, RadialSegments = BranchRadialSegments };
    private static readonly SphereMesh LeafMesh = new()
    { Radius = 1f, Height = 2f, RadialSegments = LeafRadialSegments, Rings = LeafRings };

    private static MeshInstance3D Branch(Vector3 from, Vector3 to, Material mat)
    {
        var dir = to - from;
        float len = dir.Length();
        var mi = new MeshInstance3D { Mesh = BranchMesh, MaterialOverride = mat };
        var q = new Quaternion(Vector3.Up, dir / len);
        mi.Transform = new Transform3D(new Basis(q).Scaled(new Vector3(1f, len, 1f)),
            (from + to) / 2f);
        return mi;
    }

    private static MeshInstance3D LeafBlob(Vector3 c, float r, Vector3 squash, Material mat)
    {
        return new MeshInstance3D
        {
            Mesh = LeafMesh, MaterialOverride = mat,
            Position = c, Scale = new Vector3(r, r * squash.Y, r)
        };
    }

    // World-build sizes and event light intensities (owner,
    // 2026-11-25: named constants for the bare values).
    private const float TrunkHeightLow = 3f;    // most varieties
    private const float TrunkHeightTall = 3.5f; // variety 1 (the pair is a range: round the range, not each number - owner)
    private const float CollapseLightIntensity = 3f;

    // Scatter and city conventions shared by the mission
    // and arena passes (Stage B naming pass, 2026-11-27:
    // names only - every value is EXACTLY what the bare
    // literal was, so the generated worlds do not change
    // by a single float).
    private const int ScatterMaxTriesMission = 600;
    private const int ScatterMaxTriesArena = 900;
    private const float ScatterBoundMission = 880f;
    private const float ScatterTreeScaleBase = 0.85f;
    private const float ScatterTreeScaleRange = 0.75f;
    private const float ScatterTreeGradeMax = 0.5f;
    private const float ScatterTreeHeightMax = 12.5f;
    private const float CityGradeMax = 0.4f;
    private const float BuildingSink = 2.4f;
    private const float BuildingFloorStep = 2.9f;
    private const float StepUpClearance = 1.5f;
    private const float SlopeTopTolerance = 2.5f;
    private const float PineTotalHeight = 7.6f;
    private static readonly Color NightHazeColor = new(0.10f, 0.14f, 0.24f);

    // Nature scatter, tree factory and mission-base values
    // (naming pass, 2026-11-27: names only - every value is
    // EXACTLY what the bare literal was).
    private const float TerrainChunkStreamRadius = 1150f; // streamed unload distance for Ascent terrain chunks
    private const float DesertTreeMul = 0.15f, DesertRockMul = 1.8f;   // biome scatter multipliers
    private const float CityTreeMul = 0.30f;
    private const float ForestTreeMul = 0.61f;
    private const float ScatterTreeMinRadius = 42f;  // mission scatter keeps this far from a zone centre
    private const float TreeRadiusFactor = 0.7f;     // trunk/hit radius as a fraction of tree scale
    private const float ScatterTreeClearance = 0.6f; // extra clear radius demanded around a scattered tree
    private const int ScatterTreeBaseCount = 33, ScatterRockBaseCount = 25; // arena scatter counts before biome multipliers
    private const float ScatterArenaTreeRadiusBase = 12f, ScatterArenaTreeRadiusRange = 68f;
    private const float TreeSeedWeightX = 131.7f, TreeSeedWeightZ = 57.3f, TreeSeedOffset = 9001f; // per-tree rng seed hash
    private const float PhotoTreeHeight = 5.9f;      // scanned-tree total height (non-pine varieties)
    private const float TrunkTopRadius = 0.16f, TrunkBottomRadius = 0.32f;
    private const int TrunkRadialSegments = 8;
    private const float BranchTopRadius = 0.045f, BranchBottomRadius = 0.085f;
    private const int BranchRadialSegments = 6;
    private const int LeafRadialSegments = 8, LeafRings = 5;
    private const float PineBranchBaseY = 1.6f, PineBranchStepY = 0.45f;
    private const float PineBranchReach = 0.95f, PineBranchRise = 0.34f;
    private const float ConeTopRadius = 0.04f, ConeSink = 0.4f;
    private const int ConeRadialSegments = 9;
    private const int ClassicBranchCount = 5, BroadleafBranchCount = 6;
    private const float ClassicReach = 1.55f, BroadleafReach = 1.9f;
    private const float BranchAngleJitter = 0.3f;
    private const float BranchReachFactorMin = 0.8f, BranchReachFactorMax = 1.1f;
    private const float BranchBaseFractionMin = 0.62f, BranchBaseFractionMax = 0.85f;
    private const float BranchRiseMin = 0.8f, BranchRiseMax = 1.5f;
    private const float LeafTipLift = 0.35f;
    private const float LeafBlobRadiusMin = 0.85f, LeafBlobRadiusMax = 1.25f;
    private const float LeafSquashMin = 0.72f, LeafSquashMax = 0.9f;
    private const float TreeCanopyExtra = 2.9f;      // classic/broadleaf height over the trunk (NOT BuildingFloorStep - different meaning, same value)
    private const float TreeFadeEnd = 1000f, TreeFadeMargin = 200f;
    private const float CanopyRadiusFactor = 2.0f;
    private const float CanopyBaseFraction = 0.38f, CanopyHeightFraction = 0.62f;
    private const float TreeHp = 15f;
    private void AddTreeV(Vector3 pos, int variety, float scale, float tint)
    {
        pos.Y = Main.HeightAt(pos.X, pos.Z);
        var tree = new Node3D { Position = pos, Scale = Vector3.One * scale };
        var rng = new RandomNumberGenerator
        { Seed = (ulong)(pos.X * TreeSeedWeightX + pos.Z * TreeSeedWeightZ + TreeSeedOffset) };
        var trunkMat = new StandardMaterial3D { AlbedoColor = TrunkColor, Roughness = TrunkRoughness };
        var leafMats = new[]
        {
            new StandardMaterial3D { AlbedoColor = new Color(CrownColorA.R + tint * CrownTintWeightA, CrownColorA.G, CrownColorA.B), Roughness = CrownRoughness },
            new StandardMaterial3D { AlbedoColor = new Color(CrownColorB.R + tint * CrownTintWeightB, CrownColorB.G, CrownColorB.B), Roughness = CrownRoughness },
            new StandardMaterial3D { AlbedoColor = new Color(CrownColorC.R + tint * CrownTintWeightC, CrownColorC.G, CrownColorC.B), Roughness = CrownRoughness }
        };
        float totalH;
        if (_photo || WorldConfig.Photoreal)
        {
            // Photoreal probe: a scanned tree model replaces the
            // procedural parts; cylinders + registration below are
            // unchanged, so the tree collides and burns as before.
            // Photoreal contracts share the treatment
            // (owner, 2026-11-20, consolidated on Babylon).
            PhotoEnv.AddTreeModel(tree, variety, rng);
            totalH = variety == 1 ? PineTotalHeight : PhotoTreeHeight;
        }
        else
        {
        // Tapered trunk, common to all varieties.
        float trunkH = variety == 1 ? TrunkHeightTall : TrunkHeightLow;
        var trunk = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = TrunkTopRadius, BottomRadius = TrunkBottomRadius, Height = trunkH, RadialSegments = TrunkRadialSegments },
            MaterialOverride = trunkMat,
            Position = new Vector3(0f, trunkH / 2f, 0f)
        };
        tree.AddChild(trunk);
        if (variety == 1)
        {
            // Pine: three shrinking cone tiers over short branch stubs.
            var coneMat = leafMats[2];
            for (int i = 0; i < 4; i++)
            {
                float a = rng.RandfRange(0f, Mathf.Tau);
                var from = new Vector3(0f, PineBranchBaseY + i * PineBranchStepY, 0f);
                tree.AddChild(Branch(from,
                    from + new Vector3(Mathf.Cos(a) * PineBranchReach, PineBranchRise, Mathf.Sin(a) * PineBranchReach), trunkMat));
            }
            foreach (var (ty, tr, th) in PineTiers)
            {
                var cone = new MeshInstance3D
                {
                    Mesh = new CylinderMesh { TopRadius = ConeTopRadius, BottomRadius = tr, Height = th, RadialSegments = ConeRadialSegments },
                    MaterialOverride = coneMat,
                    Position = new Vector3(0f, ty + th / 2f - ConeSink, 0f)
                };
                tree.AddChild(cone);
            }
            totalH = PineTotalHeight;
        }
        else
        {
            // Classic / broadleaf: 5-6 branches fanning up and out of the
            // trunk top, a leaf cluster at each branch tip, plus two
            // clusters crowning the center.
            int branches = variety == 2 ? BroadleafBranchCount : ClassicBranchCount;
            float reach = variety == 2 ? BroadleafReach : ClassicReach;
            var tips = new System.Collections.Generic.List<Vector3>();
            for (int i = 0; i < branches; i++)
            {
                float a = i * Mathf.Tau / branches + rng.RandfRange(-BranchAngleJitter, BranchAngleJitter);
                float rr = reach * rng.RandfRange(BranchReachFactorMin, BranchReachFactorMax);
                var from = new Vector3(0f, trunkH * rng.RandfRange(BranchBaseFractionMin, BranchBaseFractionMax), 0f);
                var to = new Vector3(Mathf.Cos(a) * rr, trunkH + rng.RandfRange(BranchRiseMin, BranchRiseMax), Mathf.Sin(a) * rr);
                tree.AddChild(Branch(from, to, trunkMat));
                tips.Add(to);
            }
            foreach (var tip in tips)
                tree.AddChild(LeafBlob(tip + new Vector3(0f, LeafTipLift, 0f),
                    rng.RandfRange(LeafBlobRadiusMin, LeafBlobRadiusMax), new Vector3(1f, rng.RandfRange(LeafSquashMin, LeafSquashMax), 1f),
                    leafMats[rng.RandiRange(0, 2)]));
            tree.AddChild(LeafBlob(new Vector3(0f, trunkH + 1.35f, 0f), 1.35f,
                new Vector3(1f, 0.8f, 1f), leafMats[rng.RandiRange(0, 2)]));
            tree.AddChild(LeafBlob(new Vector3(0.5f, trunkH + 0.9f, -0.4f), 1.0f,
                new Vector3(1f, 0.75f, 1f), leafMats[rng.RandiRange(0, 2)]));
            totalH = trunkH + TreeCanopyExtra;
        }
        }
        AddChild(tree);
        // Visibility range (owner, 2026-11-20, optimization
        // pass): a tree is clutter, not cover - fade it out
        // well before the horizon instead of drawing its
        // ~13 mesh nodes to the edge of the world. Covers
        // the procedural parts and the scanned models alike
        // (both hang under this holder). Collision and the
        // stomp/burn registration below are analytic and
        // unaffected.
        ApplyVisibilityRange(tree, TreeFadeEnd, TreeFadeMargin);
        float r = TreeRadiusFactor * scale;
        // Owner's rule: trees do NOT block movement - a rider that walks
        // into one stomps it flat (see StompCheck). Only buildings (and
        // rocks) keep movement circles. The hit cylinder stays, so
        // trees still block shots and can be shot/burned.
        var treeCyl = new HitModel.PropCyl { P = new Vector2(pos.X, pos.Z), R = r, BaseY = pos.Y, Height = totalH * scale };
        _hit.Props.Add(treeCyl);
        // The canopy is a collider too (owner, three reports): the
        // visible crown spans ~+-2u (branches + leaf blobs) but only
        // the 0.7u trunk collided, so shots through the leaves -
        // aimed at the tree's visible center - passed through
        // nothing. A tree now collides as it looks.
        var canopyCyl = new HitModel.PropCyl
        {
            P = new Vector2(pos.X, pos.Z), R = CanopyRadiusFactor * scale,
            BaseY = pos.Y + totalH * scale * CanopyBaseFraction, Height = totalH * scale * CanopyHeightFraction
        };
        _hit.Props.Add(canopyCyl);
        var td = new Destructible
        {
            Kind = "tree", Node = tree,
            Center = new Vector3(pos.X, pos.Y, pos.Z),
            Radius = r, Height = totalH * scale, Hp = TreeHp, StartScale = scale
        };
        td.CirclePts.Add(new Vector2(pos.X, pos.Z));
        td.Cyls.Add(treeCyl);
        td.Cyls.Add(canopyCyl);
        _destructibles.Add(td);
    }

    // Grass tufts REMOVED (round 28, owner report 2026-11-01:
    // "square grass wasn't removed"): the ~2,400 crossed-quad
    // tufts that used to be built here read as dark filled
    // squares from above - the same crossed-plane technique the
    // owner rejected in the photo scene in round 25 (that round
    // removed PhotoEnv's own blade cards; this base-scene system
    // ran in BOTH scenes and was missed). Measured by the
    // --grass-shot probe: the squares are these tufts (0.72u
    // crossed quads, solid vertex gradient); the photo scene's
    // scanned hero clumps are ~0.1u fragments and stay as the
    // photo scene's only grass.

    private bool ScatterFree(Vector2 p, float clearR)
    {
        // The lanes and pads (maps/Arena.cs) are TOURNAMENT test
        // choreography around the arena origin. In Mission mode
        // they are meaningless - the riders spawn 620u out - and
        // they sat exactly on the city district, silently eating
        // a third of its plots (measured: 13 plots + 46 block
        // slots rejected). Mission keeps only the prop spacing.
        if (!WorldConfig.IsMission)
        {
            // The lanes and pads are the arena map's own data
            // (maps/Arena.cs, owner, 2026-11-27).
            foreach (var lane in Arena.ScatterLanes)
                if (p.X > lane.X0 && p.X < lane.X1 && p.Y > lane.Z0 && p.Y < lane.Z1) return false;
            foreach (var pad in Arena.ScatterPads)
                if (p.DistanceTo(new Vector2(pad.X, pad.Z)) < pad.R + clearR) return false;
        }
        foreach (var c in _props)
            if (p.DistanceTo(c.P) < c.R + clearR + PropClearanceMargin) return false;
        return true;
    }

    // A plot can pass the grade check and still sit in a bowl:
    // the grade samples the center, but if the ground around
    // the footprint climbs well above the base, the building
    // reads as sunk into the terrain (owner's screenshot).
    // Reject bowls, and cliff edges where the far side falls
    // away beyond what the sink can cover.
    private static bool TerrainPit(Vector2 p, float r)
    {
        float h = Main.HeightAt(p.X, p.Y);
        for (int k = 0; k < 8; k++)
        {
            float a = k * Mathf.Pi / 4f;
            float rh = Main.HeightAt(p.X + Mathf.Cos(a) * r, p.Y + Mathf.Sin(a) * r);
            if (rh - h > PitRiseMax) return true;
            if (h - rh > PitDropMax) return true;
        }
        return false;
    }

    private static float TerrainGrade(float x, float z)
    {
        const float e = 0.7f;
        float gx = (Main.HeightAt(x + e, z) - Main.HeightAt(x - e, z)) / (2f * e);
        float gz = (Main.HeightAt(x, z + e) - Main.HeightAt(x, z - e)) / (2f * e);
        return Mathf.Sqrt(gx * gx + gz * gz);
    }

    // Seeded nature scatter: ~33 more trees (40 total) and 25
    // rocks. Placement respects the test choreography:
    // duel/weapons/breakable lanes and every spawn/teleport pad
    // stay clear, props keep spacing, trees stay off steep/high
    // ground.
    private void ScatterNature()
    {
        if (WorldConfig.IsMission) { ScatterNatureMission(); return; }
        var rng = new System.Random(0x5EED);
        var treeMul = WorldConfig.Biome switch
        {
            WorldConfig.BiomeKind.Desert => DesertTreeMul,
            WorldConfig.BiomeKind.City => CityTreeMul,
            // Forest thinned (owner, 2026-11-14: "too dense"):
            // 33 trees / 25 rocks in a 260u disc put a
            // shot-blocking crown on every line of fire.
            WorldConfig.BiomeKind.Forest => ForestTreeMul,
            _ => 1f
        };
        int treeTarget = (int)(ScatterTreeBaseCount * treeMul);
        int trees = 0, tries = 0;
        while (trees < treeTarget && tries++ < ScatterMaxTriesArena)
        {
            float ang = (float)rng.NextDouble() * Mathf.Tau;
            float rad = ScatterArenaTreeRadiusBase + ScatterArenaTreeRadiusRange * Mathf.Sqrt((float)rng.NextDouble());
            var p = new Vector2(Mathf.Cos(ang) * rad, Mathf.Sin(ang) * rad);
            float sc = ScatterTreeScaleBase + (float)rng.NextDouble() * ScatterTreeScaleRange;
            if (!ScatterFree(p, TreeRadiusFactor * sc + ScatterTreeClearance)) continue;
            if (TerrainGrade(p.X, p.Y) > ScatterTreeGradeMax) continue;
            if (Main.HeightAt(p.X, p.Y) > ScatterTreeHeightMax) continue;
            AddTreeV(new Vector3(p.X, 0f, p.Y), rng.Next(3), sc, (float)rng.NextDouble());
            trees++;
        }
        Dbg.Print($"DUEL scatter trees+={trees} propsNow={_props.Count}");
    }

    // Procedural facade (terrain-detail ask): a window grid with lit and
    // dark panes, a door at street level and a darker parapet band -
    // drawn once per building into a 256px image texture.
    // Facade texture, second detail pass (owner): real masonry instead
    // of flat paint - brick courses with mortar lines and per-brick
    // tone jitter, or ashlar stone blocks with bevel shading on the
    // alternate buildings. Windows are NOT painted here anymore: each
    // window is a dark frame quad with a real transparent glass pane in
    // front of it (see AddGlassPanes), so alignment is exact on every
    // face. Door + transom stay painted.
    private static int BrickHash(int seed, int a, int b)
    {
        int h = (seed * 73856093) ^ (a * 19349663) ^ (b * 83492791);
        h = (h ^ (h >> 13)) * 1274126177;
        return (h ^ (h >> 16)) & 0x3FF;
    }

    private static void AddQuad(System.Collections.Generic.List<Vector3> verts,
        System.Collections.Generic.List<Vector3> norms,
        System.Collections.Generic.List<int> idx,
        Vector3 center, Vector3 right, Vector3 normal, float w, float h)
    {
        int b0 = verts.Count;
        var up = Vector3.Up;
        verts.Add(center - right * (w / 2f) - up * (h / 2f)); norms.Add(normal);
        verts.Add(center + right * (w / 2f) - up * (h / 2f)); norms.Add(normal);
        verts.Add(center + right * (w / 2f) + up * (h / 2f)); norms.Add(normal);
        verts.Add(center - right * (w / 2f) + up * (h / 2f)); norms.Add(normal);
        // Godot front faces are clockwise from outside (the same
        // convention the terrain fix established): BL -> TR -> BR.
        idx.AddRange(new[] { b0, b0 + 2, b0 + 1, b0, b0 + 3, b0 + 2 });
    }

    // ---- Building detail (owner: buildings read as primitives;
    // they need roofs, doors, balconies, fire escapes) ----
    // One merged mesh per building, three materials - trim
    // (parapets, canopies, balcony slabs, cornices: the wall
    // concrete in the photo scene), metal (rails, fire escapes,
    // HVAC), dark (doors, storefront glass). Every building gets
    // a parapet, roof clutter and a front door with a canopy;
    // taller ones add one seeded character: balcony rows, a
    // zigzag fire escape, or cornice bands. Visual only - the
    // pieces hug the walls inside the collider margin, and they
    // ride the building node when it collapses.

    // ---- City dressing (owner's references): neon signs on the
    // gameplay buildings, a lit skyline beyond the boundary ----



    // District blocks pack tighter than the generic circle
    // spacing allows: a square's registered circle radius is
    // its half-diagonal, so the circle test demands ~40% more
    // room than the boxes need and killed half the block slots
    // (measured). Blocks test true box overlap against the
    // district's own placements instead, plus the nature-prop
    // circles (buildings register RoofTop circles; trees and
    // rocks don't).
    // District block spacing (naming pass, 2026-11-27).
    private const float BlockPadMargin = 0.15f;      // half-size padding on a block's test box
    private const float BlockGap = 0.1f;             // extra gap demanded between block boxes
    private const float BlockPropClearance = 2f;     // distance kept from nature-prop circles
    private const float SkipRectMargin = 6f;         // skip rects (ramps/lifts/bridges) repel blocks by this much

    // One district plot becomes a city block in the owner's
    // reference image: several buildings of MIXED heights on a
    // single block - low podiums beside a tower - with the gaps
    // between blocks reading as streets. 2x2 sub-plots, 2-4
    // filled; every block carries at least one low building
    // (9-16u) and one tall one (26-39u).
    // Rects where terrace works (ramps, lift pads) displace
    // buildings: block slots landing inside are skipped.

    // City block recipe (naming pass, 2026-11-27).
    private const float BlockExtraSlotChance = 0.55f; // chance the 4th sub-plot is filled too
    private const float BlockLowChance = 0.45f, BlockMidChance = 0.80f; // height-band rolls for extra buildings
    private const float BlockJitterRange = 1.6f, BlockJitterHalf = 0.8f; // sub-plot position jitter
    private const float BlockDepthMinFraction = 0.8f, BlockDepthRange = 0.2f; // depth as a fraction of width

    // Cluster city scatter (naming pass, 2026-11-27).
    private const float ClusterOffset = 48f;         // leg-cluster offset from the pad-pair midpoint
    private const float MesaClearRadius = 33f;       // tournament: no buildings inside this of the mesa centre
    private const float CityBuildingClearMargin = 2.5f; // ScatterFree margin over the footprint half-size (NOT SlopeTopTolerance - different test)
    private const float CityMaxGroundHeight = 9.5f;  // tournament: no buildings on ground higher than this

    // ---------- collisions ----------

    // Collision, steering and stomp tuning (naming pass,
    // 2026-11-27: names only, exact values).
    private const float FlyoverHeight = 12f;         // jump height at which riders pass over each other
    private const float SteeringBrakeMargin = 0.5f;  // extra separation the steering brake demands
    private const float SteeringBrakeDot = 0.3f;     // heading alignment above which a rider ahead brakes steering
    private const float StompMaxJumpHeight = 3f;     // above this a rider flies over the canopy instead of stomping
    private const float StompReachMargin = 0.9f;
    private const float StompSmokeSeconds = 0.8f;    // stomped trees take the fast evaporation path
    private const float StompShake = 0.18f;
    private const float PropHitReach = 0.35f;          // hit attribution slack around a prop cylinder
    private const float PropHitBandMargin = 0.6f;      // hit attribution slack above/below a prop cylinder
    private const float HitSmokeSeconds = 1.0f;        // shot trees smoke out over this
    private const float PropMatchEpsilonSq = 0.001f;   // squared-distance match when removing prop circles
    private const float RoofMatchEpsilon = 0.01f;      // centre match when removing a building's roof

    private static Vector2 PushOut(Vector2 p, float r, Circle c, out float sep)
    {
        var d = p - c.P;
        float dist = d.Length();
        float min = r + c.R;
        sep = dist;
        if (dist >= min) return p;
        if (dist < 1e-4f) d = new Vector2(1f, 0f);
        else d /= dist;
        return c.P + d * min;
    }

    private void ResolveCollisions()
    {
        ResolveOne(_player);
        ResolveOne(_enemy);
        if (_foe != null) ResolveOne(_foe.Walker);
        foreach (var a in _allies) if (a.Walker != null) ResolveOne(a.Walker);
        ResolvePair(_player, _enemy);
        if (_foe != null)
        {
            ResolvePair(_player, _foe.Walker);
            ResolvePair(_enemy, _foe.Walker);
        }
        // Teammates are bodies too (owner, 2026-11-15: the
        // allies were never resolved at all, so a follower
        // walked straight through the leader): they resolve
        // against every rider on the field, under the same
        // universal separation as every other pairing.
        foreach (var a in _allies)
        {
            if (a.Walker == null) continue;
            ResolvePair(a.Walker, _player);
            ResolvePair(a.Walker, _enemy);
            if (_foe != null) ResolvePair(a.Walker, _foe.Walker);
            foreach (var b in _allies)
                if (b != a && b.Walker != null) ResolvePair(a.Walker, b.Walker);
        }
    }

    // rider vs rider: hold the universal separation (the
    // two body radii + RiderBody.Gap) in every pairing and
    // every mode (owner, 2026-11-15). The push lands on the
    // NPC - the player is NEVER displaced by another rider,
    // which is what let a follower shove him across the
    // field in Follow mode; two NPCs split the push as
    // before.
    private void ResolvePair(RiderWalker wa, RiderWalker wb)
    {
        var a = new Vector2(wa.RootPos.X, wa.RootPos.Z);
        var b = new Vector2(wb.RootPos.X, wb.RootPos.Z);
        var d = b - a;
        float dist = d.Length();
        float min = wa.CollisionRadius + wb.CollisionRadius + RiderBody.Gap;
        // Flyover (owner): a jumping rider passes OVER the other -
        // no separation push while either is high above the ground.
        if (dist >= min || wa.JumpHeight >= FlyoverHeight || wb.JumpHeight >= FlyoverHeight) return;
        if (dist < 1e-4f) d = new Vector2(0f, 1f);
        else d /= dist;
        float push = min - dist;
        if (wa == _player) { b += d * push; wb.ApplyRootXZ(b); }
        else if (wb == _player) { a -= d * push; wa.ApplyRootXZ(a); }
        else
        {
            a -= d * push / 2f;
            b += d * push / 2f;
            wa.ApplyRootXZ(a);
            wb.ApplyRootXZ(b);
        }
    }

    // Steering brake (owner, 2026-11-15): true when rider w,
    // steering along yaw, would close inside the universal
    // separation of another living rider. NPC point-steering
    // (formation following, base patrols) checks it before
    // driving, so riders hold their distance by steering
    // instead of leaning on the collision pass. Moving AWAY
    // from a rider never brakes.
    private bool RiderAhead(RiderWalker w, float yaw)
    {
        var step = new Vector2(-Mathf.Sin(yaw), -Mathf.Cos(yaw));
        var p = new Vector2(w.RootPos.X, w.RootPos.Z);
        if (RiderBlocks(w, _player, p, step)) return true;
        if (RiderBlocks(w, _enemy, p, step)) return true;
        if (_foe != null && RiderBlocks(w, _foe.Walker, p, step)) return true;
        foreach (var a in _allies) if (RiderBlocks(w, a.Walker, p, step)) return true;
        return false;
    }

    private bool RiderBlocks(RiderWalker w, RiderWalker o, Vector2 p, Vector2 step)
    {
        if (o == null || o == w || IsDead(o)) return false;
        var to = new Vector2(o.RootPos.X, o.RootPos.Z) - p;
        float len = to.Length();
        float min = w.CollisionRadius + o.CollisionRadius + RiderBody.Gap + SteeringBrakeMargin;
        if (len >= min) return false;
        if (len < 1e-4f) return true;
        return to.Normalized().Dot(step) > SteeringBrakeDot;
    }

    // Any rider whose body is about to reach a standing tree crushes
    // it first (owner, round 29: the tree must explode BEFORE the
    // collision is noticeable - the old trigger fired only once the
    // rider's center was within CollisionRadius + 0.35 of the trunk,
    // i.e. after the body was already inside the trunk disc). The
    // trigger is the body's reach (collision radius + trunk radius)
    // plus a margin, so the tree drops into the fast evaporation
    // path (0.8s shrink with dust), its shot-blocking cylinder is
    // removed at once, and the rider walks on unimpeded. Buildings
    // are never stompable.
    private void StompCheck()
    {
        var walkers = new System.Collections.Generic.List<RiderWalker> { _player, _enemy };
        if (_foe != null) walkers.Add(_foe.Walker);
        // Team riders stomp too (owner's report, 2026-11-14:
        // trees survived being walked on - the NPC walkers of
        // story and bracket fights were never in this list).
        foreach (var a in _allies) walkers.Add(a.Walker);
        foreach (var w in walkers)
        {
            if (IsDead(w)) continue;
            if (w.JumpHeight > StompMaxJumpHeight) continue; // flying over the canopy is not a stomp
            var wp = new Vector2(w.RootPos.X, w.RootPos.Z);
            foreach (var d in _destructibles)
            {
                if (d.Kind != "tree" || d.State != DestrState.Intact) continue;
                var tp = new Vector2(d.Center.X, d.Center.Z);
                // Reach is the CROWN's width, not the trunk's
                // (owner, 2026-11-14): the visible canopy spans
                // +-2u, so a rider whose body reaches the crown
                // has reached the tree - measuring from the
                // 0.7u trunk let riders brush the leaves and
                // leave the tree standing.
                float reach = d.Radius;
                foreach (var cyl in d.Cyls) reach = Mathf.Max(reach, cyl.R);
                if (wp.DistanceTo(tp) > w.CollisionRadius + reach + StompReachMargin) continue;
                DestroyTree(d, StompSmokeSeconds);
                if (w == _player) _shake = Mathf.Max(_shake, StompShake);
                Dbg.Print($"DUEL STOMP {TagOf(w)} crushed the tree at ({d.Center.X:F0},{d.Center.Z:F0})");
            }
        }
    }

    // Foot-target clamp (owner, 2026-11-11: leg-collider
    // option 1). The root resolves against the obstacle
    // set in ResolveOne; a foot's step TARGET now clamps
    // against the SAME set - the prop circles - with the
    // same exemptions, judged at the foot's own level: a
    // building whose roof the foot is stepping onto is not
    // a wall to that foot, and stones never block. The pad
    // is the foot's
    // own clearance, much smaller than the body's: the
    // foot plants right at the wall base.
    private const float FootPad = 0.5f;

    private Vector3 ClampFootTarget(Vector3 t)
    {
        var p = new Vector2(t.X, t.Z);
        float lvl = t.Y;
        foreach (var c in _props)
        {
            if (c.RoofTop > 0f && lvl + StepUpClearance >= c.RoofTop) continue;
            p = PushOut(p, FootPad, c, out _);
        }
        return new Vector3(p.X, t.Y, p.Y);
    }

    private void ResolveOne(RiderWalker w)
    {
        var p = new Vector2(w.RootPos.X, w.RootPos.Z);
        for (int iter = 0; iter < 2; iter++)
        {
            foreach (var c in _props)
            {
                // A building's walls stop a rider on the ground -
                // but not one standing on its roof or flying over
                // it: at roof level the circle is not a wall.
                if (c.RoofTop > 0f && w.SoleLevel + StepUpClearance >= c.RoofTop) continue;
                p = PushOut(p, w.CollisionRadius, c, out _);
            }
        }
        float lim = WorldBound - w.CollisionRadius;
        p.X = Mathf.Clamp(p.X, -lim, lim);
        p.Y = Mathf.Clamp(p.Y, -lim, lim);
        w.ApplyRootXZ(p);
    }

    private Destructible PropAt(Vector3 point, float extraReach = 0f)
    {
        // Attribution by the destructible's OWN hit cylinders: a hit
        // point belongs to a prop when it lands on (or within reach
        // of) one of its cylinders, in that cylinder's height band.
        // (Center-distance attribution broke twice - the laser's
        // inflated entry point, then the tree canopy's edge.)
        Destructible best = null;
        float bestD = float.MaxValue;
        foreach (var d in _destructibles)
        {
            if (d.State != DestrState.Intact) continue;
            foreach (var c in d.Cyls)
            {
                float dxz = new Vector2(point.X - c.P.X, point.Z - c.P.Y).Length();
                float over = dxz - c.R;
                if (over > PropHitReach + extraReach) continue;
                if (point.Y < c.BaseY - PropHitBandMargin || point.Y > c.BaseY + c.Height + PropHitBandMargin) continue;
                if (over < bestD) { bestD = over; best = d; }
            }
        }
        return best;
    }

    private void DamageProp(Vector3 point, float dmg, float extraReach = 0f)
    {
        var d = PropAt(point, extraReach);
        if (d == null) return;
        if (d.Kind == "tree") { DestroyTree(d, HitSmokeSeconds); return; }
        d.Hp -= dmg;
        Dbg.Print($"ENVDMG building at ({d.Center.X:F0},{d.Center.Z:F0}) hp={d.Hp:F0}");
        if (d.Hp <= 0f) StartCollapse(d);
    }

    private void RemovePropColliders(Destructible d)
    {
        foreach (var c in d.Cyls) _hit.Props.Remove(c);
        foreach (var pt in d.CirclePts)
            for (int i = 0; i < _props.Count; i++)
                if (_props[i].P.DistanceSquaredTo(pt) < PropMatchEpsilonSq)
                    _props[i] = new Circle { P = pt, R = 0f, RoofTop = -1f };
        if (d.Kind == "building")
            Main.Roofs.RemoveAll(r => Mathf.Abs(r.Cx - d.Center.X) < RoofMatchEpsilon && Mathf.Abs(r.Cz - d.Center.Z) < RoofMatchEpsilon);
    }

    // Destruction FX recipes (naming pass, 2026-11-27:
    // names only, exact values) - the collapse and
    // tree-death bursts, flashes, lights and chunks.
    private const float CollapseSeconds = 3.4f;      // collapse state lifetime (pieces fly, settle, dissolve)
    private const float CollapseBurstHeightFraction = 0.35f;
    private const float CollapseExplosionVolDb = 2f, CollapseExplosionPitch = 0.95f;
    private static readonly Color CollapseFlashColor = new(1f, 0.8f, 0.5f);
    private const float CollapseFlashRadius = 2.6f, CollapseFlashLife = 0.5f;
    private static readonly Color CollapseDustFlashColor = new(0.75f, 0.72f, 0.66f);
    private const float CollapseDustFlashRadius = 1.9f, CollapseDustFlashLife = 0.35f;
    private const float CollapseDustFlashHeight = 1.5f;
    private static readonly Color CollapseLightColor = new(1f, 0.6f, 0.25f);
    private const float CollapseLightRange = 16f, CollapseLightLife = 0.8f; // energy is CollapseLightIntensity
    private const int CollapseChunkCount = 14;
    private const float CollapseChunkSizeMin = 0.7f, CollapseChunkSizeMax = 2.2f;
    private const float CollapseChunkUpMin = 4f, CollapseChunkUpMax = 8.5f;
    private const float CollapseChunkLife = 3.0f;
    private const int CollapseFireCount = 80;
    private const float CollapseFireLifetime = 1.5f;
    private const float CollapseFireVelMin = 5f, CollapseFireVelMax = 11f;
    private const float CollapseFireMeshRadius = 0.2f;
    private const float CollapseFireScaleMin = 1f, CollapseFireScaleMax = 2f;
    private static readonly Vector3 FireBurstGravity = new(0f, -6f, 0f); // shared by the collapse and tree fire bursts
    private const float CollapseBurnSeconds = 7f, CollapseBurnEmitterHeight = 0.6f;
    private const float TreeBurstHeightFraction = 0.7f;
    private static readonly Color TreeFlashColor = new(1f, 0.5f, 0.16f);
    private const float TreeFlashRadius = 1.1f, TreeFlashLife = 0.24f;
    private static readonly Color TreeSparkColor = new(1f, 0.6f, 0.22f);
    private const int TreeSparkCount = 9;
    private const float TreeSparkSpeed = 4.4f;
    private static readonly Color TreeLightColor = new(1f, 0.55f, 0.2f);
    private const float TreeLightEnergy = 2.6f, TreeLightRange = 11f, TreeLightLife = 0.55f;
    private const int SplinterCount = 5;
    private const float SplinterSizeMin = 0.45f, SplinterSizeMax = 0.95f;
    private const float SplinterUpMin = 2.5f, SplinterUpMax = 5f;
    private const int TreeFireCount = 48;
    private const float TreeFireLifetime = 1.25f;
    private const float TreeFireVelMin = 4f, TreeFireVelMax = 8.5f;
    private const float TreeFireMeshRadius = 0.22f;
    private const float TreeFireScaleMin = 0.9f, TreeFireScaleMax = 1.9f;
    private const float TreeBurnSeconds = 5f, TreeBurnEmitterHeight = 0.5f;
    private const int TreePuffCount = 3;
    private const float TreePuffBaseHeight = 0.8f, TreePuffStepHeight = 0.9f, TreePuffLife = 0.8f;
    private const float TreeSmokeMinScale = 0.15f, TreeSmokeScaleRange = 0.85f;
    private const float TreeSmokePuffInterval = 0.2f;
    private const float TreeSmokePuffBase = 1f, TreeSmokePuffHeightFraction = 0.4f;
    private const float TreeSmokePuffSizeMin = 6f, TreeSmokePuffSizeMax = 9f, TreeSmokePuffLife = 1.3f;
    // The pine's cone tiers (base height, radius, height) -
    // the tree builder's own recipe, shared by every map
    // that scatters pines (owner, 2026-11-27: named table).
    private static readonly (float Y, float R, float H)[] PineTiers = { (3.1f, 1.8f, 3.0f), (4.6f, 1.35f, 2.6f), (5.9f, 0.9f, 2.2f) };

    private const float TreeSmokeFlashInterval = 0.34f;
    private const float TreeSmokeFlashBase = 0.8f, TreeSmokeFlashHeightFraction = 0.3f;
    private static readonly Color TreeSmokeFlashColor = new(1f, 0.5f, 0.15f);
    private const float TreeSmokeFlashRadius = 0.9f, TreeSmokeFlashLife = 0.22f;

    private void StartCollapse(Destructible d)
    {
        d.State = DestrState.Collapsing;
        d.Timer = CollapseSeconds; // pieces fly, settle, then dissolve (~3s lives)
        d.PhaseLen = CollapseSeconds;
        RemovePropColliders(d); // falling masonry no longer blocks or stops riders
        // Round 21 (owner): the original building HIDES the moment
        // it is destroyed - no more sliding/squashing underground.
        // Only the pieces and the dust animate from here.
        if (d.Node is Node3D bn) bn.Visible = false;
        // Destruction is an EVENT now (round 7 - it used to sink
        // silently): an explosion burst at the base, a dust flash, and
        // the facade breaks into chunks that fly, tumble and settle
        // as rubble around the footprint.
        _envFxEvents++;
        var mid = d.Center + Vector3.Up * (d.Height * CollapseBurstHeightFraction);
        PlaySfx(_sfxExplosion, mid, CollapseExplosionVolDb, CollapseExplosionPitch);
        SpawnExplosionFx(mid);
        SpawnFlash(mid, CollapseFlashColor, CollapseFlashRadius, CollapseFlashLife);
        SpawnFlash(d.Center + Vector3.Up * CollapseDustFlashHeight, CollapseDustFlashColor, CollapseDustFlashRadius, CollapseDustFlashLife);
        SpawnImpactLight(mid, CollapseLightColor, CollapseLightIntensity, CollapseLightRange, CollapseLightLife);
        // Round 21 (owner, supersedes round 20's no-blocks rule):
        // the building explodes into PIECES (its own facade
        // material), the pieces settle, then each dissolves into
        // dust; a two-layer dust cloud billows over the whole event.
        Material facade = null;
        if (d.Node is MeshInstance3D fmi)
            facade = fmi.GetSurfaceOverrideMaterial(0) ?? fmi.Mesh?.SurfaceGetMaterial(0);
        SpawnChunks(mid, CollapseChunkCount, CollapseChunkSizeMin, CollapseChunkSizeMax, facade ?? RubbleMat, CollapseChunkUpMin, CollapseChunkUpMax, life: CollapseChunkLife);
        // NO dust cloud (owner, 2026-11-20, repeating his
        // 2026-11-16 rule): the round dust sprites read as
        // smoke bubbles. A collapse is the explosion, the
        // flashes, the fire burst and the flying pieces -
        // nothing soft and round.
        SpawnBurst(mid, FireParticleMat, CollapseFireCount, CollapseFireLifetime, CollapseFireVelMin, CollapseFireVelMax, FireBurstGravity, CollapseFireMeshRadius, CollapseFireScaleMin, CollapseFireScaleMax);
        SpawnBurnEmitter(d.Center + Vector3.Up * CollapseBurnEmitterHeight, CollapseBurnSeconds);
        Dbg.Print($"ENV building at ({d.Center.X:F0},{d.Center.Z:F0}) COLLAPSING (hidden, 14 pieces -> dust)");
    }

    // Owner's current rule: a tree is destroyed by ANY interaction -
    // a single weapon hit of any kind, or a stomp. Its colliders come
    // off immediately (shots and riders pass from that frame) and the
    // mesh smokes out over ~1s. This supersedes the round-2 spec
    // (ignite, burn 3s, then evaporate), under which a hit tree kept
    // standing and blocking shots for up to ~6 seconds.
    private void DestroyTree(Destructible d, float smokeLen)
    {
        if (d.State != DestrState.Intact) return;
        d.State = DestrState.SmokingOut;
        d.PhaseLen = smokeLen;
        d.Timer = smokeLen;
        RemovePropColliders(d);
        var top = d.Center + Vector3.Up * d.Height * TreeBurstHeightFraction;
        // Destruction FX (round 7): a fire flash, an ember burst and
        // wooden splinters that scatter and settle - a hit tree goes
        // out with a burst of sparks, then smokes out.
        _envFxEvents++;
        SpawnFlash(top, TreeFlashColor, TreeFlashRadius, TreeFlashLife);
        SpawnSparks(top, TreeSparkColor, TreeSparkCount, TreeSparkSpeed);
        _fxSparkBursts--; // tree embers are not a laser burst; keep the counters honest
        SpawnImpactLight(top, TreeLightColor, TreeLightEnergy, TreeLightRange, TreeLightLife);
        PlaySfx(_sfxTree, d.Center, 0f);
        SpawnChunks(top, SplinterCount, SplinterSizeMin, SplinterSizeMax, SplinterMat, SplinterUpMin, SplinterUpMax);
        SpawnBurst(top, FireParticleMat, TreeFireCount, TreeFireLifetime, TreeFireVelMin, TreeFireVelMax, FireBurstGravity, TreeFireMeshRadius, TreeFireScaleMin, TreeFireScaleMax);
        SpawnBurnEmitter(d.Center + Vector3.Up * TreeBurnEmitterHeight, TreeBurnSeconds);
        for (int i = 0; i < TreePuffCount; i++)
        {
            var puff = WeaponFx.MakeTrailPuff(d.Center + Vector3.Up * (TreePuffBaseHeight + i * TreePuffStepHeight));
            AddChild(puff);
            _puffs.Add((puff, TreePuffLife));
        }
        Dbg.Print($"ENV tree at ({d.Center.X:F0},{d.Center.Z:F0}) DESTROYED");
    }

    private void UpdateDestructibles(float dt)
    {
        foreach (var d in _destructibles)
        {
            if (d.State == DestrState.SmokingOut) // tree destroyed: smoking out
            {
                d.Timer -= dt;
                float u = Mathf.Clamp(d.Timer / d.PhaseLen, 0f, 1f);
                d.Node.Scale = Vector3.One * d.StartScale * (TreeSmokeMinScale + TreeSmokeScaleRange * u);
                d.PuffAcc += dt;
                if (d.PuffAcc > TreeSmokePuffInterval)
                {
                    d.PuffAcc = 0f;
                    var puff = WeaponFx.MakeTrailPuff(d.Center + Vector3.Up * (TreeSmokePuffBase + d.Height * TreeSmokePuffHeightFraction * u));
                    puff.Scale = Vector3.One * _fxRng.RandfRange(TreeSmokePuffSizeMin, TreeSmokePuffSizeMax);
                    AddChild(puff);
                    _puffs.Add((puff, TreeSmokePuffLife));
                }
                d.FxAcc += dt;
                if (d.FxAcc > TreeSmokeFlashInterval)
                {
                    d.FxAcc = 0f;
                    SpawnFlash(d.Center + Vector3.Up * (TreeSmokeFlashBase + d.Height * TreeSmokeFlashHeightFraction * u),
                        TreeSmokeFlashColor, TreeSmokeFlashRadius, TreeSmokeFlashLife); // the fire still burning as it smokes out
                }
                if (d.Timer <= 0f)
                {
                    d.State = DestrState.Gone;
                    RemovePropColliders(d);
                    d.Node.QueueFree();
                    Dbg.Print($"ENV tree at ({d.Center.X:F0},{d.Center.Z:F0}) GONE");
                }
            }
            else if (d.State == DestrState.Collapsing) // building gone: pieces + dust animate (round 21)
            {
                // The building node is hidden (StartCollapse); the
                // pieces fly and dissolve into dust under
                // StepEnvChunks. Nothing animates on the node itself.
                d.Timer -= dt;
                if (d.Timer <= 0f)
                {
                    d.State = DestrState.Gone;
                    Dbg.Print($"ENV building at ({d.Center.X:F0},{d.Center.Z:F0}) CLEARED (pieces turned to dust)");
                }
            }
        }
    }

    // A map's painted sky (owner: supplied images on City maps,
    // the darker/colder ones). WorldConfig.SkyTex names an
    // equirect texture; it replaces the procedural sky, and the
    // light follows the painting: a dim blue "sun" (moonlight
    // energy), sky-sourced ambient off the panorama itself, and
    // dark blue haze so the city sits IN the night instead of
    // under it. No SkyTex = the standing day setup, untouched.
    private void ApplyMapSky(Environment env, DirectionalLight3D sun)
    {
        // Tiled sky (owner, 2026-11-25): Babylon trials his
        // seamless star tile - the triplanar tiled-sky shader
        // repeats it over the dome at one uniform scale, so
        // nothing stretches and the zenith does not streak.
        // Takes precedence over the painted panorama; the
        // lighting tune below is shared by both paths.
        var tilePath = WorldConfig.SkyTile;
        if (!string.IsNullOrEmpty(tilePath)
            && ResourceLoader.Load<Texture2D>(tilePath) is Texture2D tileTex
            && ResourceLoader.Load<Shader>(Paths.TiledSkyShader) is Shader tileShader)
        {
            var tm = new ShaderMaterial { Shader = tileShader };
            tm.SetShaderParameter("tile", tileTex);
            env.Sky = new Sky { SkyMaterial = tm };
            TunePaintedSky(env, sun);
            Dbg.Print($"SKY tiled sky applied: {tilePath}");
            return;
        }
        var path = WorldConfig.SkyTex;
        if (string.IsNullOrEmpty(path)) return;
        var tex = ResourceLoader.Load<Texture2D>(path);
        if (tex == null) { Dbg.Err($"SKY texture missing: {path}"); return; }
        // Hybrid sky (owner's pick, 2026-11-13): where the
        // contract asks for it, the painted panorama is sampled
        // by a sky shader that adds a procedural star layer -
        // the photo keeps the nebula, the stars go sharp. Any
        // other painted sky keeps the plain panorama material.
        bool starsOn = false;
        Material skyMat;
        if (WorldConfig.SkyStars && ResourceLoader.Load<Shader>(Paths.StarLayerShader) is Shader starShader)
        {
            var sm = new ShaderMaterial { Shader = starShader };
            sm.SetShaderParameter("panorama", tex);
            skyMat = sm;
            starsOn = true;
        }
        else
            skyMat = new PanoramaSkyMaterial { Panorama = tex };
        env.Sky = new Sky { SkyMaterial = skyMat };
        TunePaintedSky(env, sun);
        Dbg.Print($"SKY map panorama applied: {path}");
        if (starsOn) Dbg.Print("SKY star layer: procedural stars over the photo (hybrid)");
    }

    // The lighting tune every painted/tiled sky shares:
    // darker sky than the lit scene, ambient decoupled from
    // the sky, moonlight sun, fog kept off the sky itself.
    // Painted-sky lighting tune (naming pass, 2026-11-27).
    private const float PaintedSkyEnergy = 0.48f;    // background energy: the painted sky darker than the lit scene
    private static readonly Color MoonAmbientColor = new(0.62f, 0.72f, 0.95f);
    private const float PaintedSkyExposure = 1.25f;
    private const float MoonAmbientEnergy = 0.9f;
    private const float MoonSunEnergy = 1.1f;
    private const float PaintedSkyShadowOpacity = 0.55f;
    private static readonly Color MoonSunColor = new(0.62f, 0.72f, 1.0f);

    private void TunePaintedSky(Environment env, DirectionalLight3D sun)
    {
        // Godot maps the panorama's center to +Z; the paintings
        // are composed to be looked AT, so turn the composition
        // to -Z (the direction of advance on most maps).
        env.SkyRotation = new Vector3(0f, 180f, 0f);
        env.BackgroundEnergyMultiplier = PaintedSkyEnergy; // painted sky darker than the lit scene (owner); 0.48 nets ~0.6 after the 1.25 exposure lift
        env.AmbientLightSource = Environment.AmbientSource.Color; // decouple ambient from the darkened sky, else the scene dims with it
        env.AmbientLightColor = MoonAmbientColor; // bright moonlight ambient (owner: game brighter)
        env.TonemapExposure = PaintedSkyExposure; // overall scene lift; the sky stays darker via BackgroundEnergyMultiplier 0.7 (net 0.88)
        env.AmbientLightEnergy = MoonAmbientEnergy;
        sun.LightEnergy = MoonSunEnergy;
        sun.ShadowOpacity = PaintedSkyShadowOpacity; // real shade depth (owner round 6: rider's shadow was too bright)
        sun.LightColor = MoonSunColor;
        env.FogLightColor = NightHazeColor;
        // The painting carries its own atmosphere: distance fog
        // must not wash the sky itself out (at mission far-plane
        // depths the exponential fog would otherwise replace
        // ~80% of the painting with flat fog color). Fog still
        // applies to terrain and buildings normally.
        env.FogSkyAffect = 0f;
        env.VolumetricFogAlbedo = NightHazeColor;
    }

}
