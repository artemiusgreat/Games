using Godot;
using System.Collections.Generic;

// RiderDuel module: terrain, props, destructibles, debris chunks, collisions.
// Split from the single-file build (round 22 cleanup); the class
// stays one partial class — see RiderDuel.cs for the field block.
public partial class RiderDuel
{
    private struct Circle { public Vector2 P; public float R; public float RoofTop; public bool Pass; } // RoofTop > 0: a building circle whose push-out lifts at roof level. Pass: the circle never blocks movement (stones — owner, round 29: only TALL objects block a rider)

    // Destructible environment (owner): buildings take weapon damage
    // and collapse into rubble; trees ignite, burn for 3s, then smoke
    // away over 1-3s. State: 0 intact, 1 burning, 2 collapsing,
    // 3 evaporating, 4 gone/rubble (colliders removed).
    private sealed class Destructible
    {
        public string Kind;
        public Node3D Node;
        public Vector3 Center;      // base center (Y = ground)
        public float Radius;        // hit-attribution radius (XZ)
        public float Height;
        public float Hp;
        public int State;
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
    { AlbedoColor = new Color(0.32f, 0.30f, 0.27f), Roughness = 0.95f };
    private static readonly StandardMaterial3D SplinterMat = new StandardMaterial3D
    { AlbedoColor = new Color(0.23f, 0.16f, 0.09f), Roughness = 1f };

    private void SpawnChunks(Vector3 center, int count, float sizeMin, float sizeMax,
        Material mat, float upMin, float upMax, float life = -1f)
    {
        for (int i = 0; i < count; i++)
        {
            float size = _fxRng.RandfRange(sizeMin, sizeMax);
            var mi = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(size, size * _fxRng.RandfRange(0.5f, 0.9f), size) },
                MaterialOverride = mat,
            };
            float ang = _fxRng.RandfRange(0f, Mathf.Tau);
            mi.GlobalPosition = center + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * _fxRng.RandfRange(0f, 1.2f)
                   + Vector3.Up * _fxRng.RandfRange(0.5f, 2.2f);
            AddChild(mi);
            var dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
            _envChunks.Add(new EnvChunk
            {
                Node = mi,
                Vel = dir * _fxRng.RandfRange(1.2f, 3.8f) + Vector3.Up * _fxRng.RandfRange(upMin, upMax),
                AngAxis = new Vector3(_fxRng.RandfRange(-1f, 1f), _fxRng.RandfRange(-1f, 1f), _fxRng.RandfRange(-1f, 1f)).Normalized(),
                AngSpeed = _fxRng.RandfRange(2f, 5.5f),
                Radius = size * 0.55f,
                Life = life > 0f ? life * _fxRng.RandfRange(0.8f, 1.25f) : -1f,
            });
        }
    }

    private void StepEnvChunks(float dt)
    {
        if (_envChunks.Count == 0 || dt <= 0f) return;
        // Timed pieces (building debris) dissolve into dust.
        for (int i = _envChunks.Count - 1; i >= 0; i--)
        {
            var c = _envChunks[i];
            if (c.Life <= 0f) continue;
            c.Life -= dt;
            if (c.Life > 0f) continue;
            SpawnBurst(c.Node.GlobalPosition, DustParticleMat, 5, 1.5f, 0.6f, 1.8f, new Vector3(0f, 0.8f, 0f), 0.55f, 0.9f, 1.6f);
            c.Node.QueueFree();
            _envChunks.RemoveAt(i);
        }
        if (_envChunks.Count == 0) return;
        int n = Mathf.Max(1, Mathf.CeilToInt(dt / (1f / 120f)));
        float h = dt / n;
        for (int step = 0; step < n; step++)
            foreach (var c in _envChunks)
            {
                if (c.Settled) continue;
                c.Vel.Y -= 20f * h;
                c.Node.GlobalPosition += c.Vel * h;
                if (c.AngSpeed > 0.02f)
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
                    if (c.Vel.Y < -1.6f) { c.Vel.Y = -c.Vel.Y * 0.22f; c.Vel.X *= 0.55f; c.Vel.Z *= 0.55f; c.AngSpeed *= 0.45f; }
                    else { c.Settled = true; c.Vel = Vector3.Zero; c.AngSpeed = 0f; }
                }
            }
    }
    private readonly List<Circle> _props = new();
    private readonly System.Collections.Generic.List<Vector2> _buildingPts = new();

    // ---------- world ----------

    private void BuildTerrain()
    {
        // Shared heightfield mesh (vertices on grid nodes = HeightAt).
        var mi = new MeshInstance3D { Mesh = Main.CreateTerrainMesh() };
        if (_photo) mi.MaterialOverride = PhotoEnv.TerrainMaterial();
        AddChild(mi);
    }

    // Mission scatter: groves and rock fields cluster around the
    // bases (cover where fights happen) with sparse wild growth
    // across the front, so the open country still reads empty.
    private void ScatterNatureMission()
    {
        var rng = new System.Random(0x5EED);
        var zones = new System.Collections.Generic.List<(Vector2 C, float R, int Trees, int Rocks)>();
        foreach (var pad in Main.SpawnPads) zones.Add((pad, 250f, 12, 7));
        zones.Add((Vector2.Zero, 700f, 26, 18)); // wild, centered
        var (ztMul, zrMul) = WorldConfig.Biome switch
        {
            WorldConfig.BiomeKind.Desert => (0.15f, 1.8f),
            WorldConfig.BiomeKind.City => (0.30f, 0.15f),
            _ => (1f, 1f)
        };
        foreach (var (c, rad, treeN0, rockN0) in zones)
        {
            int treeN = (int)(treeN0 * ztMul), rockN = (int)(rockN0 * zrMul);
            int trees = 0, tries = 0;
            while (trees < treeN && tries++ < 600)
            {
                float ang = (float)rng.NextDouble() * Mathf.Tau;
                float rr = 42f + (rad - 42f) * Mathf.Sqrt((float)rng.NextDouble());
                var p = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rr;
                if (Mathf.Abs(p.X) > 880f || Mathf.Abs(p.Y) > 880f) continue;
                float sc = 0.85f + (float)rng.NextDouble() * 0.75f;
                if (!ScatterFree(p, 0.7f * sc + 0.6f)) continue;
                if (TerrainGrade(p.X, p.Y) > 0.5f) continue;
                if (Main.HeightAt(p.X, p.Y) > 12.5f) continue;
                AddTreeV(new Vector3(p.X, 0f, p.Y), rng.Next(3), sc, (float)rng.NextDouble());
                trees++;
            }
            int rocks = 0; tries = 0;
            while (rocks < rockN && tries++ < 600)
            {
                float ang = (float)rng.NextDouble() * Mathf.Tau;
                float rr = 40f + (rad - 40f) * Mathf.Sqrt((float)rng.NextDouble());
                var p = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rr;
                if (Mathf.Abs(p.X) > 880f || Mathf.Abs(p.Y) > 880f) continue;
                var scl = new Vector3(
                    1.2f + (float)rng.NextDouble() * 2.0f,
                    0.8f + (float)rng.NextDouble() * 1.3f,
                    1.2f + (float)rng.NextDouble() * 2.0f);
                scl.Y *= 0.62f;
                if (!ScatterFree(p, Mathf.Max(scl.X, scl.Z))) continue;
                if (Main.HeightAt(p.X, p.Y) > 21f) continue;
                AddRock(new Vector3(p.X, 0f, p.Y), scl, (float)rng.NextDouble() * Mathf.Tau, (float)rng.NextDouble());
                rocks++;
            }
        }
    }

    private void AddTree(Vector3 pos) => AddTreeV(pos, 0, 1f, 0f);

    // Tree varieties: 0 classic, 1 pine, 2 broadleaf. Detail pass
    // (owner): a tapered trunk, real branches reaching out of it, and a
    // canopy of separate leaf clusters in varied greens instead of one
    // smooth blob. Branch/blob placement is seeded from the tree's
    // position, so the forest is stable between runs. Collision circle
    // + hit-model cylinder are unchanged, and the whole tree still
    // burns and evaporates as one destructible node.
    private static readonly CylinderMesh BranchMesh = new()
    { TopRadius = 0.045f, BottomRadius = 0.085f, Height = 1f, RadialSegments = 6 };
    private static readonly SphereMesh LeafMesh = new()
    { Radius = 1f, Height = 2f, RadialSegments = 8, Rings = 5 };

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

    private void AddTreeV(Vector3 pos, int variety, float scale, float tint)
    {
        pos.Y = Main.HeightAt(pos.X, pos.Z);
        var tree = new Node3D { Position = pos, Scale = Vector3.One * scale };
        var rng = new RandomNumberGenerator
        { Seed = (ulong)(pos.X * 131.7f + pos.Z * 57.3f + 9001f) };
        var trunkMat = new StandardMaterial3D { AlbedoColor = new Color(0.36f, 0.23f, 0.12f), Roughness = 0.95f };
        var leafMats = new[]
        {
            new StandardMaterial3D { AlbedoColor = new Color(0.13f + tint * 0.05f, 0.42f, 0.14f), Roughness = 0.9f },
            new StandardMaterial3D { AlbedoColor = new Color(0.20f + tint * 0.05f, 0.50f, 0.16f), Roughness = 0.9f },
            new StandardMaterial3D { AlbedoColor = new Color(0.09f + tint * 0.04f, 0.34f, 0.11f), Roughness = 0.9f }
        };
        float totalH;
        if (_photo)
        {
            // Photoreal probe: a scanned tree model replaces the
            // procedural parts; cylinders + registration below are
            // unchanged, so the tree collides and burns as before.
            PhotoEnv.AddTreeModel(tree, variety, rng);
            totalH = variety == 1 ? 7.6f : 5.9f;
        }
        else
        {
        // Tapered trunk, common to all varieties.
        float trunkH = variety == 1 ? 3.2f : 2.8f;
        var trunk = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.16f, BottomRadius = 0.32f, Height = trunkH, RadialSegments = 8 },
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
                var from = new Vector3(0f, 1.6f + i * 0.45f, 0f);
                tree.AddChild(Branch(from,
                    from + new Vector3(Mathf.Cos(a) * 0.95f, 0.34f, Mathf.Sin(a) * 0.95f), trunkMat));
            }
            var tiers = new (float Y, float R, float H)[] { (3.1f, 1.8f, 3.0f), (4.6f, 1.35f, 2.6f), (5.9f, 0.9f, 2.2f) };
            foreach (var (ty, tr, th) in tiers)
            {
                var cone = new MeshInstance3D
                {
                    Mesh = new CylinderMesh { TopRadius = 0.04f, BottomRadius = tr, Height = th, RadialSegments = 9 },
                    MaterialOverride = coneMat,
                    Position = new Vector3(0f, ty + th / 2f - 0.4f, 0f)
                };
                tree.AddChild(cone);
            }
            totalH = 7.6f;
        }
        else
        {
            // Classic / broadleaf: 5-6 branches fanning up and out of the
            // trunk top, a leaf cluster at each branch tip, plus two
            // clusters crowning the center.
            int branches = variety == 2 ? 6 : 5;
            float reach = variety == 2 ? 1.9f : 1.55f;
            var tips = new System.Collections.Generic.List<Vector3>();
            for (int i = 0; i < branches; i++)
            {
                float a = i * Mathf.Tau / branches + rng.RandfRange(-0.3f, 0.3f);
                float rr = reach * rng.RandfRange(0.8f, 1.1f);
                var from = new Vector3(0f, trunkH * rng.RandfRange(0.62f, 0.85f), 0f);
                var to = new Vector3(Mathf.Cos(a) * rr, trunkH + rng.RandfRange(0.8f, 1.5f), Mathf.Sin(a) * rr);
                tree.AddChild(Branch(from, to, trunkMat));
                tips.Add(to);
            }
            foreach (var tip in tips)
                tree.AddChild(LeafBlob(tip + new Vector3(0f, 0.35f, 0f),
                    rng.RandfRange(0.85f, 1.25f), new Vector3(1f, rng.RandfRange(0.72f, 0.9f), 1f),
                    leafMats[rng.RandiRange(0, 2)]));
            tree.AddChild(LeafBlob(new Vector3(0f, trunkH + 1.35f, 0f), 1.35f,
                new Vector3(1f, 0.8f, 1f), leafMats[rng.RandiRange(0, 2)]));
            tree.AddChild(LeafBlob(new Vector3(0.5f, trunkH + 0.9f, -0.4f), 1.0f,
                new Vector3(1f, 0.75f, 1f), leafMats[rng.RandiRange(0, 2)]));
            totalH = trunkH + 2.9f;
        }
        }
        AddChild(tree);
        float r = 0.7f * scale;
        // Owner's rule: trees do NOT block movement — a rider that walks
        // into one stomps it flat (see StompCheck). Only buildings (and
        // rocks) keep movement circles. The hit cylinder stays, so
        // trees still block shots and can be shot/burned.
        var treeCyl = new HitModel.PropCyl { P = new Vector2(pos.X, pos.Z), R = r, BaseY = pos.Y, Height = totalH * scale };
        _hit.Props.Add(treeCyl);
        // The canopy is a collider too (owner, three reports): the
        // visible crown spans ~+-2u (branches + leaf blobs) but only
        // the 0.7u trunk collided, so shots through the leaves —
        // aimed at the tree's visible center — passed through
        // nothing. A tree now collides as it looks.
        var canopyCyl = new HitModel.PropCyl
        {
            P = new Vector2(pos.X, pos.Z), R = 2.0f * scale,
            BaseY = pos.Y + totalH * scale * 0.38f, Height = totalH * scale * 0.62f
        };
        _hit.Props.Add(canopyCyl);
        var td = new Destructible
        {
            Kind = "tree", Node = tree,
            Center = new Vector3(pos.X, pos.Y, pos.Z),
            Radius = r, Height = totalH * scale, Hp = 15f, StartScale = scale
        };
        td.CirclePts.Add(new Vector2(pos.X, pos.Z));
        td.Cyls.Add(treeCyl);
        td.Cyls.Add(canopyCyl);
        _destructibles.Add(td);
    }

    // Low-poly rock: squashed, randomly tilted icosphere-ish blob. Gets a
    // hit cylinder (shots still collide) and a movement circle marked
    // Pass: stones do NOT block riders (owner, round 29 — a rider got
    // stuck in a tree-and-stone patch; only tall objects, buildings
    // and other riders, may block movement).
    private void AddRock(Vector3 pos, Vector3 scl, float yaw, float tint)
    {
        float groundY = Main.HeightAt(pos.X, pos.Z);
        if (_photo)
        {
            // Photoreal probe: scanned rock at the same spot, scale
            // and collider; only the visual changes.
            var rock = PhotoEnv.MakeRock(scl);
            rock.Position = new Vector3(pos.X, groundY - 0.30f * scl.Y, pos.Z);
            rock.Rotation = new Vector3(tint * 0.22f - 0.11f, yaw, tint * 0.15f - 0.07f);
            AddChild(rock);
        }
        else
        {
        var mi = new MeshInstance3D();
        mi.Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 7, Rings = 3 };
        float g = 0.36f + tint * 0.14f;
        mi.MaterialOverride = new StandardMaterial3D
        {
            AlbedoColor = new Color(g, g * (0.99f + tint * 0.03f), g * (1.02f - tint * 0.02f)),
            Roughness = 0.97f
        };
        mi.Position = new Vector3(pos.X, groundY - 0.30f * scl.Y, pos.Z);
        mi.Scale = scl;
        mi.Rotation = new Vector3(tint * 0.22f - 0.11f, yaw, tint * 0.15f - 0.07f);
        AddChild(mi);
        }
        float r = Mathf.Max(scl.X, scl.Z) * 0.92f;
        _props.Add(new Circle { P = new Vector2(pos.X, pos.Z), R = r, Pass = true });
        _hit.Props.Add(new HitModel.PropCyl
        {
            P = new Vector2(pos.X, pos.Z), R = r,
            BaseY = groundY - 0.35f * scl.Y, Height = 1.05f * scl.Y
        });
    }

    // Grass tufts REMOVED (round 28, owner report 2026-11-01:
    // "square grass wasn't removed"): the ~2,400 crossed-quad
    // tufts that used to be built here read as dark filled
    // squares from above — the same crossed-plane technique the
    // owner rejected in the photo scene in round 25 (that round
    // removed PhotoEnv's own blade cards; this base-scene system
    // ran in BOTH scenes and was missed). Measured by the
    // --grass-shot probe: the squares are these tufts (0.72u
    // crossed quads, solid vertex gradient); the photo scene's
    // scanned hero clumps are ~0.1u fragments and stay as the
    // photo scene's only grass.

    private bool ScatterFree(Vector2 p, float clearR)
    {
        // The lanes and pads below are TOURNAMENT test
        // choreography around the arena origin. In Mission mode
        // they are meaningless — the riders spawn 620u out — and
        // they sat exactly on the city district, silently eating
        // a third of its plots (measured: 13 plots + 46 block
        // slots rejected). Mission keeps only the prop spacing.
        if (!WorldConfig.IsMission)
        {
            // lanes (x0, x1, z0, z1)
            if (p.X > -5.5f && p.X < 5.5f && p.Y > -23f && p.Y < 37f) return false;
            if (p.X > 16f && p.X < 28f && p.Y > -23f && p.Y < 31f) return false;
            if (p.X > 2f && p.X < 10.5f && p.Y > 9f && p.Y < 33f) return false;
            var pads = new (float X, float Z, float R)[]
            {
                (0f, 30f, 7f), (0f, -16f, 7f), (0f, 18f, 7f), (0f, -12f, 7f),
                (0f, 20f, 7f), (0f, -10f, 7f), (22f, 24f, 7f), (22f, -16f, 7f),
                (-38f, 38f, 7.5f), (0f, 24f, 7f), (20f, 2f, 6f), (6f, 27f, 5.5f),
                (0f, 10f, 6f), (0f, -2f, 6f)
            };
            foreach (var pad in pads)
                if (p.DistanceTo(new Vector2(pad.X, pad.Z)) < pad.R + clearR) return false;
        }
        foreach (var c in _props)
            if (p.DistanceTo(c.P) < c.R + clearR + 2.0f) return false;
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
            if (rh - h > 4f) return true;
            if (h - rh > 9f) return true;
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
        var (treeMul, rockMul) = WorldConfig.Biome switch
        {
            WorldConfig.BiomeKind.Desert => (0.15f, 1.8f),
            WorldConfig.BiomeKind.City => (0.30f, 0.15f),
            _ => (1f, 1f) // Forest: the standing scatter, untouched
        };
        int treeTarget = (int)(33 * treeMul), rockTarget = (int)(25 * rockMul);
        int trees = 0, tries = 0;
        while (trees < treeTarget && tries++ < 900)
        {
            float ang = (float)rng.NextDouble() * Mathf.Tau;
            float rad = 12f + 68f * Mathf.Sqrt((float)rng.NextDouble());
            var p = new Vector2(Mathf.Cos(ang) * rad, Mathf.Sin(ang) * rad);
            float sc = 0.85f + (float)rng.NextDouble() * 0.75f;
            if (!ScatterFree(p, 0.7f * sc + 0.6f)) continue;
            if (TerrainGrade(p.X, p.Y) > 0.5f) continue;
            if (Main.HeightAt(p.X, p.Y) > 12.5f) continue;
            AddTreeV(new Vector3(p.X, 0f, p.Y), rng.Next(3), sc, (float)rng.NextDouble());
            trees++;
        }
        int rocks = 0;
        tries = 0;
        while (rocks < rockTarget && tries++ < 900)
        {
            float ang = (float)rng.NextDouble() * Mathf.Tau;
            float rad = 10f + 70f * Mathf.Sqrt((float)rng.NextDouble());
            var p = new Vector2(Mathf.Cos(ang) * rad, Mathf.Sin(ang) * rad);
            var scl = new Vector3(
                1.2f + (float)rng.NextDouble() * 2.0f,
                0.8f + (float)rng.NextDouble() * 1.3f,
                1.2f + (float)rng.NextDouble() * 2.0f);
            scl.Y *= 0.62f; // squash
            if (!ScatterFree(p, Mathf.Max(scl.X, scl.Z))) continue;
            if (Main.HeightAt(p.X, p.Y) > 21f) continue;
            AddRock(new Vector3(p.X, 0f, p.Y), scl, (float)rng.NextDouble() * Mathf.Tau, (float)rng.NextDouble());
            rocks++;
        }
        GD.Print($"DUEL scatter trees+={trees} rocks={rocks} propsNow={_props.Count}");
    }

    private static int _facadeSeq;
    private static int _kitBuilt, _procBuilt; // placement audit (kit contracts must end procedural-free)
    private static readonly Color[] FacadeBases =
    {
        new(0.62f, 0.60f, 0.58f), new(0.66f, 0.60f, 0.52f),
        new(0.55f, 0.58f, 0.62f), new(0.60f, 0.55f, 0.50f),
        new(0.50f, 0.58f, 0.56f), new(0.63f, 0.52f, 0.44f)
    };

    // Photo-scene shades over the shared concrete set (owner:
    // buildings must not all look the same) — blue-grey, teal,
    // warm sand, pale, brick-warm, slate, cycled per building
    // with a small hash jitter on top.
    private static readonly Color[] BuildingTints =
    {
        new(0.92f, 0.95f, 1.00f), new(0.85f, 0.95f, 0.95f),
        new(1.03f, 0.97f, 0.88f), new(0.88f, 0.90f, 0.94f),
        new(1.02f, 0.90f, 0.84f), new(0.80f, 0.86f, 0.90f)
    };

    // Procedural facade (terrain-detail ask): a window grid with lit and
    // dark panes, a door at street level and a darker parapet band —
    // drawn once per building into a 256px image texture.
    // Facade texture, second detail pass (owner): real masonry instead
    // of flat paint — brick courses with mortar lines and per-brick
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

    private static ImageTexture MakeFacadeTexture(int seed, Color baseCol)
    {
        var img = Image.Create(512, 512, false, Image.Format.Rgba8);
        bool stone = seed % 2 == 1;
        var mortar = baseCol.Darkened(0.42f);
        img.Fill(mortar);
        if (stone)
        {
            // Ashlar blocks 124x58 with a light top edge and dark foot.
            for (int ry = 0; ry < 9; ry++)
            for (int cx = -1; cx < 5; cx++)
            {
                int x = cx * 128 + (ry % 2 == 0 ? 0 : 64), y = ry * 58;
                int j = BrickHash(seed, cx + 3, ry);
                var c = baseCol.Lightened((j % 13 - 6) * 0.008f);
                img.FillRect(new Rect2I(x + 3, y + 3, 122, 52), c);
                img.FillRect(new Rect2I(x + 3, y + 3, 122, 4), c.Lightened(0.13f));
                img.FillRect(new Rect2I(x + 3, y + 51, 122, 4), c.Darkened(0.16f));
            }
        }
        else
        {
            // Brick courses: 62x26 bricks, 3px mortar, staggered rows.
            for (int ry = 0; ry < 19; ry++)
            for (int cx = -1; cx < 9; cx++)
            {
                int x = cx * 64 + (ry % 2 == 0 ? 0 : 32), y = ry * 27;
                int j = BrickHash(seed, cx + 3, ry);
                var c = baseCol.Lightened((j % 17 - 8) * 0.010f)
                    .Lerp(new Color(0.45f, 0.16f, 0.10f), (j % 5) * 0.03f);
                img.FillRect(new Rect2I(x + 2, y + 2, 60, 23), c);
                img.FillRect(new Rect2I(x + 2, y + 2, 60, 3), c.Lightened(0.10f));
            }
        }
        img.FillRect(new Rect2I(0, 0, 512, 14), baseCol.Darkened(0.3f)); // parapet
        img.FillRect(new Rect2I(216, 416, 80, 96), new Color(0.10f, 0.08f, 0.06f)); // door
        img.FillRect(new Rect2I(216, 416, 80, 12), new Color(0.95f, 0.8f, 0.5f));    // transom light
        return ImageTexture.CreateFromImage(img);
    }

    // Window glass for one building: for each of the four walls, a grid
    // of dark frame quads with a slightly proud transparent pane over
    // each. All quads merge into ONE ArrayMesh with an alpha glass
    // material; frames use a second surface of the same mesh with a
    // dark opaque material. Local space: the building box is centered
    // at the mesh origin, so panes ride the collapse scale for free.
    private static MeshInstance3D MakeGlassPanes(Vector3 size, int seed, float litRatio)
    {
        var verts = new System.Collections.Generic.List<Vector3>();
        var norms = new System.Collections.Generic.List<Vector3>();
        var idx = new System.Collections.Generic.List<int>();
        var frameMat = new StandardMaterial3D { AlbedoColor = new Color(0.07f, 0.08f, 0.10f), Roughness = 0.9f };
        var glassMat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.55f, 0.72f, 0.85f, 0.38f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            Roughness = 0.06f, Metallic = 0.35f
        };
        // Lit panes (owner's city references: no two facades
        // alike) — a seeded share of the panes glow warm or cool
        // on a third, emissive surface of the same mesh.
        var litWarm = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.45f, 0.30f, 0.13f),
            EmissionEnabled = true, Emission = new Color(1.0f, 0.72f, 0.38f), EmissionEnergyMultiplier = 1.3f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled, Roughness = 0.4f
        };
        var litCool = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.16f, 0.24f, 0.32f),
            EmissionEnabled = true, Emission = new Color(0.62f, 0.82f, 1.0f), EmissionEnergyMultiplier = 1.2f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled, Roughness = 0.4f
        };
        // face: outward normal, right axis, half extents (w = face width, h = height)
        var faces = new (Vector3 N, Vector3 R, float W)[]
        {
            (new Vector3(0, 0, 1), new Vector3(1, 0, 0), size.X),
            (new Vector3(0, 0, -1), new Vector3(-1, 0, 0), size.X),
            (new Vector3(1, 0, 0), new Vector3(0, 0, -1), size.Z),
            (new Vector3(-1, 0, 0), new Vector3(0, 0, 1), size.Z)
        };
        var frameIdx = new System.Collections.Generic.List<int>();
        var glassIdx = new System.Collections.Generic.List<int>();
        var litWarmIdx = new System.Collections.Generic.List<int>();
        var litCoolIdx = new System.Collections.Generic.List<int>();
        // The grid itself varies per building (seeded cell size),
        // a few panes are missing outright, and the lit share is
        // the caller's (higher in the city, where dusk facades
        // glow — the owner's screenshots).
        float cellW = 2.1f + (BrickHash(seed, 7, 1) % 100) / 100f * 0.9f;
        float cellH = 2.5f + (BrickHash(seed, 3, 11) % 100) / 100f * 0.8f;
        int faceI = 0;
        foreach (var (n, rgt, w) in faces)
        {
            float half = (Mathf.Abs(n.Z) > 0.5f ? size.Z : size.X) / 2f + 0.04f;
            int cols = Mathf.Clamp(Mathf.RoundToInt(w / cellW), 3, 7);
            int rows = Mathf.Clamp(Mathf.RoundToInt(size.Y / cellH), 2, 6);
            for (int ri = 0; ri < rows; ri++)
            for (int ci = 0; ci < cols; ci++)
            {
                int hsh = BrickHash(seed, ci * 3 + faceI * 31, ri * 7 + faceI);
                if (hsh % 23 == 0) continue; // a missing pane
                float u = (ci + 0.5f) / cols - 0.5f;         // across the face
                // The grid starts HIGH (owner: the lowest
                // windows sat at ground level and buried on
                // slopes): a windowless ground band takes the
                // bottom third of the wall.
                float v = 0.34f + (ri + 0.5f) / rows * 0.58f; // 0 at sill band..1 top
                var center = n * half + rgt * (u * w)
                    + Vector3.Up * ((v - 0.5f) * size.Y);
                float pw = w / cols * 0.52f, ph = size.Y / rows * 0.5f;
                AddQuad(verts, norms, frameIdx, center - n * 0.02f, rgt, n, pw * 1.18f, ph * 1.18f);
                var dst = (hsh % 100) < (int)(litRatio * 100f)
                    ? (hsh % 3 == 0 ? litCoolIdx : litWarmIdx) : glassIdx;
                AddQuad(verts, norms, dst, center, rgt, n, pw, ph);
            }
            faceI++;
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = norms.ToArray();
        var mesh = new ArrayMesh();
        var fArrays = new Godot.Collections.Array(); fArrays.Resize((int)Mesh.ArrayType.Max);
        fArrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        fArrays[(int)Mesh.ArrayType.Normal] = norms.ToArray();
        fArrays[(int)Mesh.ArrayType.Index] = frameIdx.ToArray();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, fArrays);
        mesh.SurfaceSetMaterial(0, frameMat);
        var gArrays = new Godot.Collections.Array(); gArrays.Resize((int)Mesh.ArrayType.Max);
        gArrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        gArrays[(int)Mesh.ArrayType.Normal] = norms.ToArray();
        gArrays[(int)Mesh.ArrayType.Index] = glassIdx.ToArray();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, gArrays);
        mesh.SurfaceSetMaterial(1, glassMat);
        if (litWarmIdx.Count > 0)
        {
            var wArrays = new Godot.Collections.Array(); wArrays.Resize((int)Mesh.ArrayType.Max);
            wArrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
            wArrays[(int)Mesh.ArrayType.Normal] = norms.ToArray();
            wArrays[(int)Mesh.ArrayType.Index] = litWarmIdx.ToArray();
            int si = mesh.GetSurfaceCount();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, wArrays);
            mesh.SurfaceSetMaterial(si, litWarm);
        }
        if (litCoolIdx.Count > 0)
        {
            var cArrays = new Godot.Collections.Array(); cArrays.Resize((int)Mesh.ArrayType.Max);
            cArrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
            cArrays[(int)Mesh.ArrayType.Normal] = norms.ToArray();
            cArrays[(int)Mesh.ArrayType.Index] = litCoolIdx.ToArray();
            int si = mesh.GetSurfaceCount();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, cArrays);
            mesh.SurfaceSetMaterial(si, litCool);
        }
        return new MeshInstance3D { Mesh = mesh };
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

    // Mission fronts: a base cluster on each flattened plateau —
    // four structures and a comms mast around the spawn court, so
    // every base reads as a place from across the map and fights
    // like the arena's building blocks (same HP, roofs, colliders).
    private void BuildBases()
    {
        var offs = new[]
        {
            new Vector2(34f, 12f), new Vector2(-30f, 26f),
            new Vector2(14f, -36f), new Vector2(-34f, -20f)
        };
        var sizes = new[]
        {
            new Vector3(14f, 12f, 12f), new Vector3(11f, 15f, 11f),
            new Vector3(15f, 11f, 10f), new Vector3(10f, 16f, 10f)
        };
        for (int b = 0; b < Main.SpawnPads.Length; b++)
        {
            // The HOME base stands empty (owner, 2026-11-11
            // round 6: 'remove buildings in the home base') —
            // the player spawns on open ground; the two enemy
            // bases keep their structures.
            if (b == 0) continue;
            var pad = Main.SpawnPads[b];
            float rot = b * 2.1f; // vary the layout per base
            for (int i = 0; i < offs.Length; i++)
            {
                var o = offs[i].Rotated(rot);
                // Per-base size jitter (owner: buildings must not
                // all look the same) — deterministic per base and
                // structure, footprints ±10%, heights ±13%.
                float sx = 1f + ((b * 3 + i) % 4 - 1.5f) * 0.07f;
                float sy = 1f + ((b + i * 2) % 3 - 1) * 0.13f;
                var sz = sizes[i];
                AddBuilding(new Vector3(pad.X + o.X, 0f, pad.Y + o.Y),
                    new Vector3(sz.X * sx, sz.Y * sy, sz.Z * sx));
            }
            var m = new Vector2(22f, -26f).Rotated(rot);
            // Comms mast: on a kit contract it is a kit building
            // too (owner: no procedural buildings on kit maps),
            // so the footprint widens past the kit threshold.
            AddBuilding(new Vector3(pad.X + m.X, 0f, pad.Y + m.Y),
                WorldConfig.CityKit >= 0 ? new Vector3(5f, 27f, 5f) : new Vector3(3f, 27f, 3f));
        }
        GD.Print("MISSION bases built at the three plateaus");
    }

    // ---- Building detail (owner: buildings read as primitives;
    // they need roofs, doors, balconies, fire escapes) ----
    // One merged mesh per building, three materials — trim
    // (parapets, canopies, balcony slabs, cornices: the wall
    // concrete in the photo scene), metal (rails, fire escapes,
    // HVAC), dark (doors, storefront glass). Every building gets
    // a parapet, roof clutter and a front door with a canopy;
    // taller ones add one seeded character: balcony rows, a
    // zigzag fire escape, or cornice bands. Visual only — the
    // pieces hug the walls inside the collider margin, and they
    // ride the building node when it collapses.
    private static readonly Dictionary<bool, Material[]> _detailMats = new();

    private static Material[] DetailMats(bool photo)
    {
        if (_detailMats.TryGetValue(photo, out var m)) return m;
        var trim = photo
            ? PhotoEnv.BuildingMaterial(new Vector3(3f, 3f, 3f), new Color(0.88f, 0.86f, 0.82f))
            : (Material)new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.53f, 0.50f), Roughness = 0.9f };
        var metal = new StandardMaterial3D { AlbedoColor = new Color(0.15f, 0.16f, 0.18f), Metallic = 0.55f, Roughness = 0.5f };
        var dark = new StandardMaterial3D { AlbedoColor = new Color(0.05f, 0.06f, 0.07f), Metallic = 0.25f, Roughness = 0.35f };
        m = new[] { trim, metal, dark };
        _detailMats[photo] = m;
        return m;
    }

    private static void AddQuadB(List<Vector3> verts, List<Vector3> norms, List<int> idx,
        Vector3 center, Vector3 right, Vector3 up, Vector3 normal, float w, float h)
    {
        int b0 = verts.Count;
        verts.Add(center - right * (w / 2f) - up * (h / 2f)); norms.Add(normal);
        verts.Add(center + right * (w / 2f) - up * (h / 2f)); norms.Add(normal);
        verts.Add(center + right * (w / 2f) + up * (h / 2f)); norms.Add(normal);
        verts.Add(center - right * (w / 2f) + up * (h / 2f)); norms.Add(normal);
        idx.AddRange(new[] { b0, b0 + 2, b0 + 1, b0, b0 + 3, b0 + 2 });
    }

    // One box (24 verts, flat faces) into a surface list; the
    // optional basis rotates it (fire-escape stairs tilt).
    private static void AddBox(List<Vector3> verts, List<Vector3> norms, List<int> idx,
        Vector3 center, Vector3 size, Basis basis)
    {
        var faces = new (Vector3 N, Vector3 R, Vector3 U)[]
        {
            (new Vector3(0, 0, 1), new Vector3(1, 0, 0), new Vector3(0, 1, 0)),
            (new Vector3(0, 0, -1), new Vector3(-1, 0, 0), new Vector3(0, 1, 0)),
            (new Vector3(1, 0, 0), new Vector3(0, 0, -1), new Vector3(0, 1, 0)),
            (new Vector3(-1, 0, 0), new Vector3(0, 0, 1), new Vector3(0, 1, 0)),
            (new Vector3(0, 1, 0), new Vector3(1, 0, 0), new Vector3(0, 0, -1)),
            (new Vector3(0, -1, 0), new Vector3(1, 0, 0), new Vector3(0, 0, 1))
        };
        foreach (var (n, r, u) in faces)
        {
            var fc = center + basis * (new Vector3(n.X * size.X, n.Y * size.Y, n.Z * size.Z) / 2f);
            float fw = Mathf.Abs(r.X) > 0.5f ? size.X : Mathf.Abs(r.Y) > 0.5f ? size.Y : size.Z;
            float fh = Mathf.Abs(u.X) > 0.5f ? size.X : Mathf.Abs(u.Y) > 0.5f ? size.Y : size.Z;
            AddQuadB(verts, norms, idx, fc, basis * r, basis * u, basis * n, fw, fh);
        }
    }

    private static MeshInstance3D MakeBuildingDetail(Vector3 size, int seq, bool photo)
    {
        if (Mathf.Min(size.X, size.Z) < 5f) return null; // the comms mast stays clean
        int face = BrickHash(seq, 5, 2) % 4;
        float fw = face % 2 == 0 ? size.X : size.Z;   // front width
        float fd = face % 2 == 0 ? size.Z : size.X;   // front depth
        float top = size.Y / 2f;
        float g = -size.Y / 2f + 2.4f;                // terrain line (AddBuilding's sink)
        var verts = new List<Vector3>(); var norms = new List<Vector3>();
        var trimI = new List<int>(); var metalI = new List<int>(); var darkI = new List<int>();
        var I = Basis.Identity;

        // Parapet rim (trim).
        AddBox(verts, norms, trimI, new Vector3(0, top - 0.21f, fd / 2f - 0.03f), new Vector3(fw + 0.12f, 0.42f, 0.2f), I);
        AddBox(verts, norms, trimI, new Vector3(0, top - 0.21f, -fd / 2f + 0.03f), new Vector3(fw + 0.12f, 0.42f, 0.2f), I);
        AddBox(verts, norms, trimI, new Vector3(fw / 2f - 0.03f, top - 0.21f, 0), new Vector3(0.2f, 0.42f, fd + 0.12f), I);
        AddBox(verts, norms, trimI, new Vector3(-fw / 2f + 0.03f, top - 0.21f, 0), new Vector3(0.2f, 0.42f, fd + 0.12f), I);

        // Roof clutter: HVAC boxes (metal), a roof hut on some,
        // an antenna on others.
        int hvac = 1 + BrickHash(seq, 8, 3) % 2;
        for (int i = 0; i < hvac; i++)
        {
            float ox = (BrickHash(seq, i, 21) % 61 - 30) / 100f * fw * 0.55f;
            float oz = (BrickHash(seq, i, 33) % 61 - 30) / 100f * fd * 0.55f;
            AddBox(verts, norms, metalI, new Vector3(ox, top + 0.38f, oz),
                new Vector3(1.3f + (BrickHash(seq, i, 7) % 10) * 0.13f, 0.76f, 1.0f + (BrickHash(seq, i, 13) % 10) * 0.09f), I);
        }
        if (seq % 3 == 0)
        {
            var hut = new Vector3(fw * 0.22f, top + 1.0f, -fd * 0.16f);
            AddBox(verts, norms, trimI, hut, new Vector3(2.1f, 2.0f, 1.9f), I);
            AddBox(verts, norms, darkI, hut + new Vector3(0, -0.25f, 0.98f), new Vector3(0.9f, 1.5f, 0.1f), I);
        }
        if (seq % 4 == 1)
        {
            var ant = new Vector3(-fw * 0.30f, top + 1.35f, fd * 0.22f);
            AddBox(verts, norms, metalI, ant, new Vector3(0.1f, 2.7f, 0.1f), I);
            AddBox(verts, norms, metalI, ant + new Vector3(0, 1.05f, 0), new Vector3(1.0f, 0.08f, 0.08f), I);
        }

        // Front door, canopy, step (dark + trim) on the front face.
        float doorX = (BrickHash(seq, 4, 17) % 51 - 25) / 100f * fw * 0.6f;
        AddBox(verts, norms, darkI, new Vector3(doorX, g + 1.35f, fd / 2f + 0.04f), new Vector3(1.7f, 2.7f, 0.14f), I);
        AddBox(verts, norms, trimI, new Vector3(doorX, g + 3.02f, fd / 2f + 0.48f), new Vector3(2.6f, 0.16f, 1.05f), I);
        AddBox(verts, norms, trimI, new Vector3(doorX, g + 0.11f, fd / 2f + 0.30f), new Vector3(2.4f, 0.22f, 0.66f), I);

        // City storefront: a dark glass band + header at the base.
        if (WorldConfig.Biome == WorldConfig.BiomeKind.City)
        {
            float sw = Mathf.Min(fw * 0.86f, 12f);
            AddBox(verts, norms, darkI, new Vector3(0, g + 1.5f, fd / 2f + 0.03f), new Vector3(sw, 3.0f, 0.12f), I);
            AddBox(verts, norms, trimI, new Vector3(0, g + 3.18f, fd / 2f + 0.05f), new Vector3(sw, 0.3f, 0.2f), I);
        }

        int style = seq % 3;
        if (style == 0 && size.Y >= 12f && fw >= 8f)
        {
            // Balcony rows on the front (trim slabs, metal rails).
            int cols = Mathf.Clamp(Mathf.FloorToInt(fw / 4.2f), 2, 3);
            for (float fy = g + 4.4f; fy < top - 1.7f; fy += 2.9f)
            for (int c = 0; c < cols; c++)
            {
                if (BrickHash(seq, c * 7, (int)(fy * 3f)) % 4 == 0) continue;
                float cx = (c - (cols - 1) / 2f) * (fw / cols) * 0.92f;
                AddBox(verts, norms, trimI, new Vector3(cx, fy, fd / 2f + 0.40f), new Vector3(2.3f, 0.15f, 0.8f), I);
                AddBox(verts, norms, metalI, new Vector3(cx, fy + 0.36f, fd / 2f + 0.78f), new Vector3(2.3f, 0.57f, 0.05f), I);
                AddBox(verts, norms, metalI, new Vector3(cx - 1.13f, fy + 0.36f, fd / 2f + 0.40f), new Vector3(0.05f, 0.57f, 0.8f), I);
                AddBox(verts, norms, metalI, new Vector3(cx + 1.13f, fy + 0.36f, fd / 2f + 0.40f), new Vector3(0.05f, 0.57f, 0.8f), I);
            }
        }
        else if (style == 1 && size.Y >= 13f)
        {
            // Zigzag fire escape on the front (metal).
            float prevX = 0f, prevY = 0f; bool first = true;
            int lvl = 0;
            for (float ly = g + 3.4f; ly < top - 1.3f; ly += 2.9f, lvl++)
            {
                float px = lvl % 2 == 0 ? -1.0f : 1.0f;
                AddBox(verts, norms, metalI, new Vector3(px, ly, fd / 2f + 0.42f), new Vector3(3.6f, 0.13f, 0.85f), I);
                AddBox(verts, norms, metalI, new Vector3(px, ly + 0.38f, fd / 2f + 0.82f), new Vector3(3.6f, 0.62f, 0.05f), I);
                AddBox(verts, norms, metalI, new Vector3(px - 1.78f, ly + 0.38f, fd / 2f + 0.42f), new Vector3(0.05f, 0.62f, 0.85f), I);
                AddBox(verts, norms, metalI, new Vector3(px + 1.78f, ly + 0.38f, fd / 2f + 0.42f), new Vector3(0.05f, 0.62f, 0.85f), I);
                if (!first)
                {
                    float dx = px - prevX, dy = ly - prevY;
                    float len = Mathf.Sqrt(dx * dx + dy * dy);
                    var tilt = new Basis(new Vector3(0, 0, 1), Mathf.Atan2(dy, dx));
                    AddBox(verts, norms, metalI,
                        new Vector3((px + prevX) / 2f, (ly + prevY) / 2f, fd / 2f + 0.42f),
                        new Vector3(len, 0.1f, 0.7f), tilt);
                }
                if (first)
                {
                    // The drop ladder to the ground.
                    AddBox(verts, norms, metalI, new Vector3(px - 0.35f, g + 1.6f, fd / 2f + 0.5f), new Vector3(0.05f, 3.2f, 0.05f), I);
                    AddBox(verts, norms, metalI, new Vector3(px + 0.35f, g + 1.6f, fd / 2f + 0.5f), new Vector3(0.05f, 3.2f, 0.05f), I);
                    for (int r2 = 0; r2 < 4; r2++)
                        AddBox(verts, norms, metalI, new Vector3(px, g + 0.5f + r2 * 0.75f, fd / 2f + 0.5f), new Vector3(0.7f, 0.05f, 0.05f), I);
                }
                prevX = px; prevY = ly; first = false;
            }
        }
        else if (style == 2)
        {
            // Cornice bands at every floor line (trim).
            for (float fy = g + 2.9f; fy < top - 0.9f; fy += 2.9f)
            {
                AddBox(verts, norms, trimI, new Vector3(0, fy, fd / 2f + 0.03f), new Vector3(fw + 0.26f, 0.2f, 0.16f), I);
                AddBox(verts, norms, trimI, new Vector3(0, fy, -fd / 2f - 0.03f), new Vector3(fw + 0.26f, 0.2f, 0.16f), I);
                AddBox(verts, norms, trimI, new Vector3(fw / 2f + 0.03f, fy, 0), new Vector3(0.16f, 0.2f, fd + 0.26f), I);
                AddBox(verts, norms, trimI, new Vector3(-fw / 2f - 0.03f, fy, 0), new Vector3(0.16f, 0.2f, fd + 0.26f), I);
            }
        }

        var mesh = new ArrayMesh();
        var mats = DetailMats(photo);
        var lists = new[] { trimI, metalI, darkI };
        for (int si = 0; si < 3; si++)
        {
            if (lists[si].Count == 0) continue;
            var arr = new Godot.Collections.Array(); arr.Resize((int)Mesh.ArrayType.Max);
            arr[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
            arr[(int)Mesh.ArrayType.Normal] = norms.ToArray();
            arr[(int)Mesh.ArrayType.Index] = lists[si].ToArray();
            int at = mesh.GetSurfaceCount();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);
            mesh.SurfaceSetMaterial(at, mats[si]);
        }
        var node = new MeshInstance3D { Mesh = mesh };
        node.Rotation = new Vector3(0, face * Mathf.Pi / 2f, 0);
        return node;
    }

    // The building box as a two-surface ArrayMesh (a BoxMesh
    // carries one material only): walls on surface 0, the roof
    // face on surface 1. UVs are 0..1 per face — the mapping
    // BoxMesh used — so each material's Uv1Scale keeps its
    // meaning. Faces are (origin, right, up) triples with
    // right x up = the outward normal, wound like the panes.
    private static ArrayMesh BuildingBoxMesh(Vector3 size, Material wallMat, Material roofMat)
    {
        float hx = size.X / 2f, hy = size.Y / 2f, hz = size.Z / 2f;
        var verts = new System.Collections.Generic.List<Vector3>();
        var norms = new System.Collections.Generic.List<Vector3>();
        var uvs = new System.Collections.Generic.List<Vector2>();
        var wallI = new System.Collections.Generic.List<int>();
        var roofI = new System.Collections.Generic.List<int>();
        void Face(Vector3 o, Vector3 r, Vector3 u, System.Collections.Generic.List<int> idx)
        {
            int b = verts.Count;
            var n = r.Cross(u).Normalized();
            verts.Add(o); verts.Add(o + r); verts.Add(o + r + u); verts.Add(o + u);
            for (int i = 0; i < 4; i++) norms.Add(n);
            uvs.Add(new Vector2(0, 1)); uvs.Add(new Vector2(1, 1));
            uvs.Add(new Vector2(1, 0)); uvs.Add(new Vector2(0, 0));
            idx.Add(b); idx.Add(b + 2); idx.Add(b + 1);
            idx.Add(b); idx.Add(b + 3); idx.Add(b + 2);
        }
        var X = new Vector3(size.X, 0, 0); var Y = new Vector3(0, size.Y, 0); var Z = new Vector3(0, 0, size.Z);
        Face(new Vector3(-hx, -hy, hz), X, Y, wallI);   // +Z
        Face(new Vector3(hx, -hy, -hz), -X, Y, wallI);  // -Z
        Face(new Vector3(hx, -hy, hz), -Z, Y, wallI);   // +X
        Face(new Vector3(-hx, -hy, -hz), Z, Y, wallI);  // -X
        Face(new Vector3(-hx, -hy, -hz), X, Z, wallI);  // -Y (buried)
        Face(new Vector3(-hx, hy, hz), X, -Z, roofI);   // +Y roof
        var mesh = new ArrayMesh();
        void Surf(System.Collections.Generic.List<int> idx, Material mat)
        {
            var arr = new Godot.Collections.Array(); arr.Resize((int)Mesh.ArrayType.Max);
            arr[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
            arr[(int)Mesh.ArrayType.Normal] = norms.ToArray();
            arr[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
            arr[(int)Mesh.ArrayType.Index] = idx.ToArray();
            int at = mesh.GetSurfaceCount();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);
            mesh.SurfaceSetMaterial(at, mat);
        }
        Surf(wallI, wallMat);
        Surf(roofI, roofMat);
        return mesh;
    }

    private void AddBuilding(Vector3 pos, Vector3 size, float baseY = float.NaN)
    {
        int pc0 = _props.Count, hc0 = _hit.Props.Count;
        // Terrace decks (owner: Harbor levels): buildings can
        // stand on a platform top instead of the terrain.
        float groundY = float.IsNaN(baseY) ? Main.HeightAt(pos.X, pos.Z) : baseY;
        // City kits (owner): when the active contract carries a
        // building kit, the placement instances a real building
        // model from the kit, uniformly scaled to the requested
        // height — and `size` becomes the model's scaled AABB,
        // so the colliders, roof floor and destruction below all
        // match the model. Any placement the kit can't serve
        // (it returns null) stays procedural.
        Node3D kitHolder = null;
        if (WorldConfig.CityKit >= 0 && size.X >= 4f && size.Z >= 4f)
        {
            var req = size;
            kitHolder = CityKits.Take(WorldConfig.CityKit, _facadeSeq, size.Y, out Vector3 eff);
            if (kitHolder != null)
            {
                size = eff;
                // Small requests are dense-city slots: a wide
                // model scaled to the slot's height can sprawl
                // far past the slot and swallow its neighbours.
                // Clamp the footprint to the request (+5%),
                // scaling the model down uniformly with it.
                if (Mathf.Max(req.X, req.Z) <= 14f)
                {
                    float capF = Mathf.Max(req.X, req.Z) * 1.05f;
                    float m = Mathf.Max(size.X, size.Z);
                    if (m > capF)
                    {
                        float k = capF / m;
                        size *= k;
                        kitHolder.Scale *= k;
                    }
                }
            }
        }
        // The big footprints (owner, 2026-10-31: larger buildings)
        // sit on slopes — sink the base well into the grade so no
        // corner hovers; the roof top is the walkable surface.
        const float Sink = 2.4f;
        float roofTop = groundY + size.Y - Sink;
        pos.Y = groundY + size.Y / 2f - Sink;
        int seq = _facadeSeq;
        var mi = new MeshInstance3D();
        if (kitHolder != null)
        {
            // The container stays a MeshInstance3D (destruction
            // types it as one) with no mesh of its own; the model
            // sits with its base at the sunk ground line — the
            // container's origin is the building's center, so the
            // holder goes half the height down. A seeded quarter
            // turn varies the street faces (colliders are
            // circles, so facing is purely visual).
            kitHolder.Position = new Vector3(0f, -size.Y / 2f, 0f);
            kitHolder.Rotation = new Vector3(0f, (seq % 4) * Mathf.Pi / 2f, 0f);
            mi.AddChild(kitHolder);
            _kitBuilt++;
            _facadeSeq++;
            mi.Position = pos;
            AddChild(mi);
            _buildingPts.Add(new Vector2(pos.X, pos.Z));
            RegisterBuildingShell(pos, size, groundY, Sink, roofTop, pc0, hc0, mi);
            return;
        }
        float tintJ = 0.94f + (BrickHash(seq, 3, 9) % 13) * 0.01f;
        // Two-surface box (walls / roof): in the photo scene the
        // walls take one of six photographic sets by sequence and
        // the roof takes gravel; the plain scene keeps its
        // authored facade and a flat dark roof.
        var wallMat = _photo
            ? PhotoEnv.BuildingMaterial(size, BuildingTints[seq % BuildingTints.Length] * tintJ, seq)
            : new StandardMaterial3D
            {
                AlbedoTexture = MakeFacadeTexture(seq, FacadeBases[seq % FacadeBases.Length]),
                Roughness = 0.92f
            };
        var roofMat = _photo
            ? PhotoEnv.RoofMaterial(size)
            : new StandardMaterial3D { AlbedoColor = new Color(0.13f, 0.12f, 0.11f), Roughness = 1f };
        mi.Mesh = BuildingBoxMesh(size, wallMat, roofMat);
        _procBuilt++;
        _facadeSeq++;
        mi.Position = pos;
        AddChild(mi);
        mi.AddChild(MakeGlassPanes(size, seq,
            WorldConfig.Biome == WorldConfig.BiomeKind.City ? 0.34f : 0.16f));
        var detailNode = MakeBuildingDetail(size, seq, _photo);
        if (detailNode != null) mi.AddChild(detailNode);
        if (WorldConfig.Biome == WorldConfig.BiomeKind.City && seq % 3 == 1 && size.Y >= 12f && size.X >= 8f)
            AddSign(mi, size, seq);
        _buildingPts.Add(new Vector2(pos.X, pos.Z));
        RegisterBuildingShell(pos, size, groundY, Sink, roofTop, pc0, hc0, mi);
    }

    // Shared by the procedural and kit paths: collision
    // circles, hit cylinders, the destructible record and
    // the walkable roof, all from the effective size.
    private void RegisterBuildingShell(Vector3 pos, Vector3 size, float groundY,
        float sink, float roofTop, int pc0, int hc0, MeshInstance3D mi)
    {
        // Circle colliders: one for near-square footprints, two along the
        // long axis for elongated ones. Radius covers the short side plus a
        // margin; extreme corners can poke slightly past a circle (POC).
        // The same circles register as hit-model cylinders (base sunk
        // with the mesh, top at the roof) so buildings block shots too.
        float sx = size.X, sz = size.Z;
        if (Mathf.Max(sx, sz) / Mathf.Min(sx, sz) > 1.45f)
        {
            float off = (Mathf.Max(sx, sz) - Mathf.Min(sx, sz)) / 4f;
            float r = Mathf.Min(sx, sz) / 2f + 0.35f;
            var a = new Vector2(pos.X, pos.Z);
            var b = new Vector2(pos.X, pos.Z);
            if (sx > sz) { a.X -= off; b.X += off; } else { a.Y -= off; b.Y += off; }
            _props.Add(new Circle { P = a, R = r, RoofTop = roofTop });
            _props.Add(new Circle { P = b, R = r, RoofTop = roofTop });
            _hit.Props.Add(new HitModel.PropCyl { P = a, R = r, BaseY = groundY - sink, Height = size.Y });
            _hit.Props.Add(new HitModel.PropCyl { P = b, R = r, BaseY = groundY - sink, Height = size.Y });
        }
        else
        {
            float r = Mathf.Max(sx, sz) / 2f + 0.3f;
            _props.Add(new Circle { P = new Vector2(pos.X, pos.Z), R = r, RoofTop = roofTop });
            _hit.Props.Add(new HitModel.PropCyl { P = new Vector2(pos.X, pos.Z), R = r, BaseY = groundY - sink, Height = size.Y });
        }
        var bd = new Destructible
        {
            Kind = "building", Node = mi,
            Center = new Vector3(pos.X, groundY, pos.Z),
            Radius = Mathf.Max(size.X, size.Z) / 2f + 0.3f,
            Height = size.Y, Hp = 40f
        };
        for (int i = pc0; i < _props.Count; i++) bd.CirclePts.Add(_props[i].P);
        for (int i = hc0; i < _hit.Props.Count; i++) bd.Cyls.Add(_hit.Props[i]);
        _destructibles.Add(bd);
        // The roof is a floor (owner, 2026-10-31): a rider with
        // thrusters can land and stand on it. Removed again when
        // the building is destroyed (RemovePropColliders).
        Main.Roofs.Add(new Main.RoofRect { Cx = pos.X, Cz = pos.Z, Hx = size.X / 2f, Hz = size.Z / 2f, Top = roofTop, Under = groundY - sink });
    }


    // ---- City dressing (owner's references): neon signs on the
    // gameplay buildings, a lit skyline beyond the boundary ----

    private static readonly Color[] SignNeons =
    {
        new(0.20f, 0.90f, 1.00f), new(1.00f, 0.35f, 0.80f),
        new(1.00f, 0.70f, 0.25f), new(0.40f, 1.00f, 0.60f)
    };
    private static readonly System.Collections.Generic.Dictionary<int, ImageTexture> _signTex = new();

    private static ImageTexture SignTexture(int variant)
    {
        if (_signTex.TryGetValue(variant, out var t)) return t;
        var neon = SignNeons[variant % SignNeons.Length];
        var img = Image.Create(256, 128, false, Image.Format.Rgba8);
        img.Fill(new Color(0.03f, 0.03f, 0.045f));
        img.FillRect(new Rect2I(4, 4, 248, 120), neon);
        img.FillRect(new Rect2I(10, 10, 236, 108), new Color(0.03f, 0.03f, 0.045f));
        // Abstract ad: bright bars of seeded widths + one block.
        for (int i = 0; i < 4; i++)
        {
            int bw = 60 + BrickHash(variant, i, 5) % 150;
            var c = i % 2 == 0 ? neon : neon.Lightened(0.35f);
            img.FillRect(new Rect2I(22, 24 + i * 20, bw, 9), c);
        }
        img.FillRect(new Rect2I(196, 78, 36, 36), neon.Lightened(0.2f));
        t = ImageTexture.CreateFromImage(img);
        _signTex[variant] = t;
        return t;
    }

    private static void AddSign(MeshInstance3D building, Vector3 size, int seq)
    {
        int variant = seq % SignNeons.Length;
        var tex = SignTexture(variant);
        var mat = new StandardMaterial3D
        {
            AlbedoTexture = tex,
            EmissionEnabled = true, EmissionTexture = tex,
            Emission = new Color(1f, 1f, 1f), EmissionEnergyMultiplier = 1.5f,
            Roughness = 0.6f
        };
        float w = Mathf.Min(size.X * 0.72f, 9f);
        var sign = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(w, w * 0.5f) },
            MaterialOverride = mat
        };
        // One face per building, seeded; the sign rides the
        // building node, so it collapses with it when destroyed.
        switch (seq % 4)
        {
            case 0: sign.Position = new Vector3(0f, size.Y * 0.16f, size.Z / 2f + 0.07f); break;
            case 1: sign.Position = new Vector3(0f, size.Y * 0.16f, -size.Z / 2f - 0.07f); sign.Rotation = new Vector3(0f, Mathf.Pi, 0f); break;
            case 2: sign.Position = new Vector3(size.X / 2f + 0.07f, size.Y * 0.16f, 0f); sign.Rotation = new Vector3(0f, Mathf.Pi / 2f, 0f); break;
            default: sign.Position = new Vector3(-size.X / 2f - 0.07f, size.Y * 0.16f, 0f); sign.Rotation = new Vector3(0f, -Mathf.Pi / 2f, 0f); break;
        }
        building.AddChild(sign);
    }

    // City biome: extra gameplay buildings on the same rules as
    // the arena's and the bases' (destructible, blocking,
    // standable) — a town center and street clusters in Mission,
    // blocks between the mesa and the pads in Tournament. Sizes
    // vary widely (owner: no two alike). Pads, roads and the
    // mesa stay clear.
    // Kit contracts: the town is a real street grid — blocks of
    // kit buildings on the level district plateau (Main), with
    // the ground between the plots colored as streets. One
    // central plot stays empty as a plaza; a few plots skip at
    // random so the blocks are not a perfect lattice.
    private void PlaceCityGrid(System.Random rng, ref int placed)
    {
        // Terraced city (owner: Harbor's three levels) builds
        // its own plots, platforms, ramps, lifts and island.
        if (WorldConfig.TerracedCity) { BuildTerracedCity(rng, ref placed); return; }
        // The dense city (owner: "so dense the rider can have
        // space to walk only between the houses or jump on their
        // roofs — no open space"): every plot in the disc is
        // built, no plaza, no gap plots; the lanes between the
        // blocks are the only ground left open. No ScatterFree —
        // the pads and bases sit 600u+ away, and the pad roads
        // ARE these streets now.
        float p = CityGrid.Pitch;
        int hb = Mathf.CeilToInt(CityGrid.Radius / p);
        for (int i = -hb; i <= hb; i++)
        for (int j = -hb; j <= hb; j++)
        {
            var c = new Vector2(
                i * p + (float)(rng.NextDouble() - 0.5) * 2f,
                j * p + (float)(rng.NextDouble() - 0.5) * 2f);
            if (c.Length() > CityGrid.Radius) continue;
            if (TerrainGrade(c.X, c.Y) > 0.4f) continue;
            PlaceCityBlock(rng, c, ref placed);
        }
    }

    private readonly System.Collections.Generic.List<(Vector2 C, Vector2 H)> _districtBoxes = new();

    // District blocks pack tighter than the generic circle
    // spacing allows: a square's registered circle radius is
    // its half-diagonal, so the circle test demands ~40% more
    // room than the boxes need and killed half the block slots
    // (measured). Blocks test true box overlap against the
    // district's own placements instead, plus the nature-prop
    // circles (buildings register RoofTop circles; trees and
    // rocks don't).
    private bool BlockFree(Vector2 pt, Vector3 size)
    {
        var h = new Vector2(size.X, size.Z) / 2f + new Vector2(0.15f, 0.15f);
        foreach (var b in _districtBoxes)
            if (Mathf.Abs(pt.X - b.C.X) < h.X + b.H.X + 0.1f &&
                Mathf.Abs(pt.Y - b.C.Y) < h.Y + b.H.Y + 0.1f)
                return false;
        foreach (var c in _props)
        {
            if (c.RoofTop > 0f) continue; // building circles are not nature props
            if (pt.DistanceTo(c.P) < c.R + Mathf.Max(h.X, h.Y) + 2f) return false;
        }
        _districtBoxes.Add((pt, h));
        return true;
    }

    // One district plot becomes a city block in the owner's
    // reference image: several buildings of MIXED heights on a
    // single block — low podiums beside a tower — with the gaps
    // between blocks reading as streets. 2x2 sub-plots, 2-4
    // filled; every block carries at least one low building
    // (9-16u) and one tall one (26-39u).
    // Rects where terrace works (ramps, lift pads) displace
    // buildings: block slots landing inside are skipped.
    private readonly System.Collections.Generic.List<Main.RoofRect> _skipRects = new();

    private bool InSkipRect(Vector2 pt)
    {
        foreach (var r in _skipRects)
            if (Mathf.Abs(pt.X - r.Cx) <= r.Hx + 6f && Mathf.Abs(pt.Y - r.Cz) <= r.Hz + 6f)
                return true;
        return false;
    }

    private void PlaceCityBlock(System.Random rng, Vector2 c, ref int placed, float baseY = float.NaN)
    {
        var slots = new System.Collections.Generic.List<Vector2>
        {
            new(-5.5f, -5.5f), new(5.5f, -5.5f), new(-5.5f, 5.5f), new(5.5f, 5.5f)
        };
        for (int i = slots.Count - 1; i > 0; i--)
        { int j = rng.Next(i + 1); (slots[i], slots[j]) = (slots[j], slots[i]); }
        int n = 3 + (rng.NextDouble() < 0.55 ? 1 : 0);
        var heights = new System.Collections.Generic.List<float>
        {
            9f + (float)rng.NextDouble() * 7f,
            26f + (float)rng.NextDouble() * 13f
        };
        for (int i = 2; i < n; i++)
        {
            double hr = rng.NextDouble();
            heights.Add(hr < 0.45 ? 9f + (float)rng.NextDouble() * 8f
                : hr < 0.80 ? 17f + (float)rng.NextDouble() * 10f
                : 27f + (float)rng.NextDouble() * 12f);
        }
        for (int i = heights.Count - 1; i > 0; i--)
        { int j = rng.Next(i + 1); (heights[i], heights[j]) = (heights[j], heights[i]); }
        for (int k = 0; k < n; k++)
        {
            var pt = c + slots[k] + new Vector2(
                (float)(rng.NextDouble() * 1.6 - 0.8),
                (float)(rng.NextDouble() * 1.6 - 0.8));
            // Footprints ~10u on 11u slot spacing: the block
            // fills its plot and the lane left between blocks is
            // ~10u wall to wall — a rider fits between the
            // houses, or goes over the roofs.
            float fw = 9.8f + (float)rng.NextDouble() * 0.8f;
            var size = new Vector3(fw, heights[k],
                fw * (0.8f + (float)rng.NextDouble() * 0.2f));
            if (TerrainPit(pt, Mathf.Max(size.X, size.Z) / 2f + 6f)) continue;
            if (InSkipRect(pt)) continue;
            if (!BlockFree(pt, size)) continue;
            AddBuilding(new Vector3(pt.X, 0f, pt.Y), size, baseY);
            placed++;
        }
    }

    private void ScatterCity()
    {
        var rng = new System.Random(0xC17E11);
        var clusters = new System.Collections.Generic.List<(Vector2 C, float R, int N)>();
        bool grid = false;
        if (WorldConfig.IsMission)
        {
            if (WorldConfig.CityKit >= 0) grid = true;
            else clusters.Add((Vector2.Zero, 120f, 7));
            var p = Main.SpawnPads;
            for (int leg = 0; leg < (grid ? 0 : 3); leg++)
            {
                var a = p[leg]; var b = p[(leg + 1) % 3];
                var mid = (a + b) / 2f;
                var perp = new Vector2(b.Y - a.Y, -(b.X - a.X)).Normalized()
                    * (leg % 2 == 0 ? 48f : -48f);
                clusters.Add((mid + perp, 65f, 5));
            }
        }
        else clusters.Add((Vector2.Zero, 108f, 12));
        int placed = 0;
        if (grid) PlaceCityGrid(rng, ref placed);
        foreach (var (cc, cr, cn) in clusters)
        {
            int got = 0, tries = 0;
            while (got < cn && tries++ < 500)
            {
                float ang = (float)rng.NextDouble() * Mathf.Tau;
                float rr = cr * Mathf.Sqrt((float)rng.NextDouble());
                var pt = cc + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * rr;
                if (!WorldConfig.IsMission && pt.Length() < 33f) continue; // the mesa
                var size = new Vector3(
                    9f + (float)rng.NextDouble() * 8f,
                    11f + (float)rng.NextDouble() * 17f,
                    9f + (float)rng.NextDouble() * 8f);
                if (!ScatterFree(pt, Mathf.Max(size.X, size.Z) / 2f + 2.5f)) continue;
                if (Main.RoadDistance(pt.X, pt.Y) < 8f) continue;
                if (TerrainGrade(pt.X, pt.Y) > 0.4f) continue;
                if (TerrainPit(pt, Mathf.Max(size.X, size.Z) / 2f + 6f)) continue;
                if (!WorldConfig.IsMission && Main.HeightAt(pt.X, pt.Y) > 9.5f) continue;
                bool nearPad = false;
                foreach (var pad in Main.SpawnPads)
                    if (pt.DistanceTo(pad) < (WorldConfig.IsMission ? 80f : 25f)) { nearPad = true; break; }
                if (nearPad) continue;
                AddBuilding(new Vector3(pt.X, 0f, pt.Y), size);
                got++; placed++;
            }
        }
        GD.Print($"CITY scatter: {placed} buildings placed (world totals: kit {_kitBuilt}, procedural {_procBuilt})");
    }

    // ---------- collisions ----------

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
        ResolvePair(_player, _enemy);
        if (_foe != null)
        {
            ResolvePair(_player, _foe.Walker);
            ResolvePair(_enemy, _foe.Walker);
        }
    }

    // rider vs rider: split the overlap
    private static void ResolvePair(RiderWalker wa, RiderWalker wb)
    {
        var a = new Vector2(wa.RootPos.X, wa.RootPos.Z);
        var b = new Vector2(wb.RootPos.X, wb.RootPos.Z);
        var d = b - a;
        float dist = d.Length();
        float min = wa.CollisionRadius + wb.CollisionRadius;
        // Flyover (owner): a jumping rider passes OVER the other —
        // no separation push while either is high above the ground.
        if (dist < min && wa.JumpHeight < 12f && wb.JumpHeight < 12f)
        {
            if (dist < 1e-4f) d = new Vector2(0f, 1f);
            else d /= dist;
            float push = (min - dist) / 2f;
            a -= d * push;
            b += d * push;
            wa.ApplyRootXZ(a);
            wb.ApplyRootXZ(b);
        }
    }

    // Any rider whose body is about to reach a standing tree crushes
    // it first (owner, round 29: the tree must explode BEFORE the
    // collision is noticeable — the old trigger fired only once the
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
        foreach (var w in walkers)
        {
            if (IsDead(w)) continue;
            if (w.JumpHeight > 3f) continue; // flying over the canopy is not a stomp
            var wp = new Vector2(w.RootPos.X, w.RootPos.Z);
            foreach (var d in _destructibles)
            {
                if (d.Kind != "tree" || d.State != 0) continue;
                var tp = new Vector2(d.Center.X, d.Center.Z);
                if (wp.DistanceTo(tp) > w.CollisionRadius + d.Radius + 0.9f) continue;
                DestroyTree(d, 0.8f);
                if (w == _player) _shake = Mathf.Max(_shake, 0.18f);
                GD.Print($"DUEL STOMP {TagOf(w)} crushed the tree at ({d.Center.X:F0},{d.Center.Z:F0})");
            }
        }
    }

    // Push a point out of an axis-aligned rect expanded by r:
    // along the separating axis when outside, along the least
    // penetration axis when the point is already inside.
    private static Vector2 PushOutRect(Vector2 p, float r, Main.RoofRect rc)
    {
        float dx = p.X - Mathf.Clamp(p.X, rc.Cx - rc.Hx, rc.Cx + rc.Hx);
        float dz = p.Y - Mathf.Clamp(p.Y, rc.Cz - rc.Hz, rc.Cz + rc.Hz);
        float d2 = dx * dx + dz * dz;
        if (d2 > 1e-9f)
        {
            float d = Mathf.Sqrt(d2);
            if (d >= r) return p;
            return p + new Vector2(dx, dz) / d * (r - d);
        }
        float px = rc.Hx - Mathf.Abs(p.X - rc.Cx);
        float pz = rc.Hz - Mathf.Abs(p.Y - rc.Cz);
        if (px < pz) p.X = rc.Cx + (p.X >= rc.Cx ? rc.Hx + r : -rc.Hx - r);
        else p.Y = rc.Cz + (p.Y >= rc.Cz ? rc.Hz + r : -rc.Hz - r);
        return p;
    }

    private void ResolveOne(RiderWalker w)
    {
        var p = new Vector2(w.RootPos.X, w.RootPos.Z);
        for (int iter = 0; iter < 2; iter++)
        {
            // Terrace walls (owner: Harbor levels): rect sides
            // with a top — a walker below the top is pushed out,
            // a walker at deck level walks on freely.
            foreach (var wl in Main.Walls)
            {
                if (w.SoleLevel + 1.5f >= wl.Top) continue;
                p = PushOutRect(p, w.CollisionRadius, wl);
            }
            foreach (var c in _props)
            {
                // A building's walls stop a rider on the ground —
                // but not one standing on its roof or flying over
                // it: at roof level the circle is not a wall.
                if (c.RoofTop > 0f && w.SoleLevel + 1.5f >= c.RoofTop) continue;
                if (c.Pass) continue; // stones: kept for scatter spacing and shot hits, never a wall
                p = PushOut(p, w.CollisionRadius, c, out _);
            }
            // Ramp wedges are solid from the side (owner,
            // 2026-11-11: 'I can walk through the ramp if I
            // come from the side'). GroundAt alone only refuses
            // the FOOT — the root walked on under the wedge and
            // out the other side. A walker whose soles are
            // below the slope at its position (beyond the
            // step-up it could legitimately climb) and whose
            // head does not fit under the wedge's underside is
            // pushed out by the shortest exit. On the slope
            // (soles at its height) or fully under a bridge
            // span, no push.
            foreach (var rp in Main.Ramps)
            {
                float dx = p.X - rp.Cx, dz = p.Y - rp.Cz;
                float along = dx * rp.DirX + dz * rp.DirZ;
                float perp = -dx * rp.DirZ + dz * rp.DirX;
                float rr = w.CollisionRadius;
                if (Mathf.Abs(along) > rp.HalfLen + rr || Mathf.Abs(perp) > rp.HalfWid + rr) continue;
                float h = Mathf.Lerp(rp.H0, rp.H1, Mathf.Clamp((along + rp.HalfLen) / (2f * rp.HalfLen), 0f, 1f));
                if (w.SoleLevel >= h - 2.5f) continue;                 // standing on the slope
                if (w.SoleLevel + w.Height <= h - 3.3f) continue;     // fits under the wedge
                // Shortest exit in ramp space (perp sides or
                // either end), then back to world XZ.
                float exP = rp.HalfWid + rr - Mathf.Abs(perp);
                float exA = rp.HalfLen + rr - Mathf.Abs(along);
                if (exP <= exA)
                    perp = (perp >= 0f ? 1f : -1f) * (rp.HalfWid + rr);
                else
                    along = (along >= 0f ? 1f : -1f) * (rp.HalfLen + rr);
                p = new Vector2(rp.Cx, rp.Cz) + new Vector2(
                    rp.DirX * along - rp.DirZ * perp,
                    rp.DirZ * along + rp.DirX * perp);
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
        // (Center-distance attribution broke twice — the laser's
        // inflated entry point, then the tree canopy's edge.)
        Destructible best = null;
        float bestD = float.MaxValue;
        foreach (var d in _destructibles)
        {
            if (d.State != 0) continue;
            foreach (var c in d.Cyls)
            {
                float dxz = new Vector2(point.X - c.P.X, point.Z - c.P.Y).Length();
                float over = dxz - c.R;
                if (over > 0.35f + extraReach) continue;
                if (point.Y < c.BaseY - 0.6f || point.Y > c.BaseY + c.Height + 0.6f) continue;
                if (over < bestD) { bestD = over; best = d; }
            }
        }
        return best;
    }

    private void DamageProp(Vector3 point, float dmg, float extraReach = 0f)
    {
        var d = PropAt(point, extraReach);
        if (d == null) return;
        if (d.Kind == "tree") { DestroyTree(d, 1.0f); return; }
        d.Hp -= dmg;
        GD.Print($"ENVDMG building at ({d.Center.X:F0},{d.Center.Z:F0}) hp={d.Hp:F0}");
        if (d.Hp <= 0f) StartCollapse(d);
    }

    private void RemovePropColliders(Destructible d)
    {
        foreach (var c in d.Cyls) _hit.Props.Remove(c);
        foreach (var pt in d.CirclePts)
            for (int i = 0; i < _props.Count; i++)
                if (_props[i].P.DistanceSquaredTo(pt) < 0.001f)
                    _props[i] = new Circle { P = pt, R = 0f, RoofTop = -1f };
        if (d.Kind == "building")
            Main.Roofs.RemoveAll(r => Mathf.Abs(r.Cx - d.Center.X) < 0.01f && Mathf.Abs(r.Cz - d.Center.Z) < 0.01f);
    }

    private void StartCollapse(Destructible d)
    {
        d.State = 2;
        d.Timer = 3.4f; // pieces fly, settle, then dissolve (~3s lives)
        d.PhaseLen = 3.4f;
        RemovePropColliders(d); // falling masonry no longer blocks or stops riders
        // Round 21 (owner): the original building HIDES the moment
        // it is destroyed — no more sliding/squashing underground.
        // Only the pieces and the dust animate from here.
        if (d.Node is Node3D bn) bn.Visible = false;
        // Destruction is an EVENT now (round 7 — it used to sink
        // silently): an explosion burst at the base, a dust flash, and
        // the facade breaks into chunks that fly, tumble and settle
        // as rubble around the footprint.
        _envFxEvents++;
        var mid = d.Center + Vector3.Up * (d.Height * 0.35f);
        PlaySfx(_sfxExplosion, mid, 2f, 0.95f);
        SpawnExplosionFx(mid);
        SpawnFlash(mid, new Color(1f, 0.8f, 0.5f), 2.6f, 0.5f);
        SpawnFlash(d.Center + Vector3.Up * 1.5f, new Color(0.75f, 0.72f, 0.66f), 1.9f, 0.35f);
        SpawnImpactLight(mid, new Color(1f, 0.6f, 0.25f), 3.2f, 16f, 0.8f);
        // Round 21 (owner, supersedes round 20's no-blocks rule):
        // the building explodes into PIECES (its own facade
        // material), the pieces settle, then each dissolves into
        // dust; a two-layer dust cloud billows over the whole event.
        Material facade = null;
        if (d.Node is MeshInstance3D fmi)
            facade = fmi.GetSurfaceOverrideMaterial(0) ?? fmi.Mesh?.SurfaceGetMaterial(0);
        SpawnChunks(mid, 14, 0.7f, 2.2f, facade ?? RubbleMat, 4f, 8.5f, life: 3.0f);
        SpawnBurst(d.Center + Vector3.Up * 1.2f, DustParticleMat, 46, 3.4f, 2.5f, 7f, new Vector3(0f, 0.9f, 0f), 1.1f, 1.6f, 3.2f);
        SpawnBurst(mid, DustParticleMat, 26, 2.6f, 1.5f, 4.5f, new Vector3(0f, 0.6f, 0f), 0.9f, 1.4f, 2.6f);
        SpawnBurst(mid, FireParticleMat, 80, 1.5f, 5f, 11f, new Vector3(0f, -6f, 0f), 0.2f, 1f, 2f);
        SpawnBurst(mid, SmokeParticleMat, 36, 3.4f, 1.6f, 3.4f, new Vector3(0f, 1.5f, 0f), 0.6f, 1.8f, 3.2f);
        SpawnBurnEmitter(d.Center + Vector3.Up * 0.6f, 7f);
        GD.Print($"ENV building at ({d.Center.X:F0},{d.Center.Z:F0}) COLLAPSING (hidden, 14 pieces -> dust)");
    }

    // Owner's current rule: a tree is destroyed by ANY interaction —
    // a single weapon hit of any kind, or a stomp. Its colliders come
    // off immediately (shots and riders pass from that frame) and the
    // mesh smokes out over ~1s. This supersedes the round-2 spec
    // (ignite, burn 3s, then evaporate), under which a hit tree kept
    // standing and blocking shots for up to ~6 seconds.
    private void DestroyTree(Destructible d, float smokeLen)
    {
        if (d.State != 0) return;
        d.State = 3;
        d.PhaseLen = smokeLen;
        d.Timer = smokeLen;
        RemovePropColliders(d);
        var top = d.Center + Vector3.Up * d.Height * 0.7f;
        // Destruction FX (round 7): a fire flash, an ember burst and
        // wooden splinters that scatter and settle — a hit tree goes
        // out with a burst of sparks, then smokes out.
        _envFxEvents++;
        SpawnFlash(top, new Color(1f, 0.5f, 0.16f), 1.1f, 0.24f);
        SpawnSparks(top, new Color(1f, 0.6f, 0.22f), 9, 4.4f);
        _fxSparkBursts--; // tree embers are not a laser burst; keep the counters honest
        SpawnImpactLight(top, new Color(1f, 0.55f, 0.2f), 2.6f, 11f, 0.55f);
        PlaySfx(_sfxTree, d.Center, 0f);
        SpawnChunks(top, 5, 0.45f, 0.95f, SplinterMat, 2.5f, 5f);
        SpawnBurst(top, FireParticleMat, 48, 1.25f, 4f, 8.5f, new Vector3(0f, -6f, 0f), 0.22f, 0.9f, 1.9f);
        SpawnBurst(top + Vector3.Up * 0.6f, SmokeParticleMat, 14, 2.8f, 1.2f, 2.6f, new Vector3(0f, 1.5f, 0f), 0.6f, 1.5f, 2.6f);
        SpawnBurnEmitter(d.Center + Vector3.Up * 0.5f, 5f);
        for (int i = 0; i < 3; i++)
        {
            var puff = WeaponFx.MakeTrailPuff(d.Center + Vector3.Up * (0.8f + i * 0.9f));
            AddChild(puff);
            _puffs.Add((puff, 0.8f));
        }
        GD.Print($"ENV tree at ({d.Center.X:F0},{d.Center.Z:F0}) DESTROYED");
    }

    private void UpdateDestructibles(float dt)
    {
        foreach (var d in _destructibles)
        {
            if (d.State == 3) // tree destroyed: smoking out
            {
                d.Timer -= dt;
                float u = Mathf.Clamp(d.Timer / d.PhaseLen, 0f, 1f);
                d.Node.Scale = Vector3.One * d.StartScale * (0.15f + 0.85f * u);
                d.PuffAcc += dt;
                if (d.PuffAcc > 0.2f)
                {
                    d.PuffAcc = 0f;
                    var puff = WeaponFx.MakeTrailPuff(d.Center + Vector3.Up * (1f + d.Height * 0.4f * u));
                    puff.Scale = Vector3.One * _fxRng.RandfRange(6f, 9f);
                    AddChild(puff);
                    _puffs.Add((puff, 1.3f));
                }
                d.FxAcc += dt;
                if (d.FxAcc > 0.34f)
                {
                    d.FxAcc = 0f;
                    SpawnFlash(d.Center + Vector3.Up * (0.8f + d.Height * 0.3f * u),
                        new Color(1f, 0.5f, 0.15f), 0.9f, 0.22f); // the fire still burning as it smokes out
                }
                if (d.Timer <= 0f)
                {
                    d.State = 4;
                    RemovePropColliders(d);
                    d.Node.QueueFree();
                    GD.Print($"ENV tree at ({d.Center.X:F0},{d.Center.Z:F0}) GONE");
                }
            }
            else if (d.State == 2) // building gone: pieces + dust animate (round 21)
            {
                // The building node is hidden (StartCollapse); the
                // pieces fly and dissolve into dust under
                // StepEnvChunks. Nothing animates on the node itself.
                d.Timer -= dt;
                if (d.Timer <= 0f)
                {
                    d.State = 4;
                    GD.Print($"ENV building at ({d.Center.X:F0},{d.Center.Z:F0}) CLEARED (pieces turned to dust)");
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
        var path = WorldConfig.SkyTex;
        if (string.IsNullOrEmpty(path)) return;
        var tex = ResourceLoader.Load<Texture2D>(path);
        if (tex == null) { GD.PrintErr($"SKY texture missing: {path}"); return; }
        env.Sky = new Sky { SkyMaterial = new PanoramaSkyMaterial { Panorama = tex } };
        // Godot maps the panorama's center to +Z; the paintings
        // are composed to be looked AT, so turn the composition
        // to -Z (the direction of advance on most maps).
        env.SkyRotation = new Vector3(0f, 180f, 0f);
        env.BackgroundEnergyMultiplier = 0.48f; // painted sky darker than the lit scene (owner); 0.48 nets ~0.6 after the 1.25 exposure lift
        env.AmbientLightSource = Environment.AmbientSource.Color; // decouple ambient from the darkened sky, else the scene dims with it
        env.AmbientLightColor = new Color(0.62f, 0.72f, 0.95f); // bright moonlight ambient (owner: game brighter)
        env.TonemapExposure = 1.25f; // overall scene lift; the sky stays darker via BackgroundEnergyMultiplier 0.7 (net 0.88)
        env.AmbientLightEnergy = 0.9f;
        sun.LightEnergy = 1.1f;
        sun.ShadowOpacity = 0.28f; // shadows barely noticeable (owner, 2026-11-11)
        sun.LightColor = new Color(0.62f, 0.72f, 1.0f);
        env.FogLightColor = new Color(0.10f, 0.14f, 0.24f);
        // The painting carries its own atmosphere: distance fog
        // must not wash the sky itself out (at mission far-plane
        // depths the exponential fog would otherwise replace
        // ~80% of the painting with flat fog color). Fog still
        // applies to terrain and buildings normally.
        env.FogSkyAffect = 0f;
        env.VolumetricFogAlbedo = new Color(0.10f, 0.14f, 0.24f);
        GD.Print($"SKY map panorama applied: {path}");
    }

}
