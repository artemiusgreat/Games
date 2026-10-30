using Godot;
using System.Collections.Generic;

// MechDuel module: terrain, props, destructibles, debris chunks, collisions.
// Split from the single-file build (round 22 cleanup); the class
// stays one partial class — see MechDuel.cs for the field block.
public partial class MechDuel
{
    private struct Circle { public Vector2 P; public float R; }

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
        public OmniLight3D Light;
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
        // Owner's rule: trees do NOT block movement — a mech that walks
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
    // collision circle + hit cylinder like every other prop.
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
        _props.Add(new Circle { P = new Vector2(pos.X, pos.Z), R = r });
        _hit.Props.Add(new HitModel.PropCyl
        {
            P = new Vector2(pos.X, pos.Z), R = r,
            BaseY = groundY - 0.35f * scl.Y, Height = 1.05f * scl.Y
        });
    }

    // Seeded nature scatter: ~33 more trees (40 total) and 25 rocks.
    // Placement respects the test choreography: duel/weapons/breakable
    // lanes and every spawn/teleport pad stay clear, props keep spacing,
    // trees stay off steep/high ground.
    // Grass (terrain-detail ask): ~2400 instanced crossed-quad tufts in
    // two color populations (deep green and yellow-green) on low,
    // gentle, off-road ground. Two MultiMesh draw calls total, no
    // colliders — purely cosmetic ground cover.
    private void ScatterGrass()
    {
        var rng = new RandomNumberGenerator { Seed = 777 };
        var spots = new System.Collections.Generic.List<(Vector3 Pos, float Yaw, float S)>();
        int attempts = 0;
        while (spots.Count < 2400 && attempts++ < 16000)
        {
            float x = rng.RandfRange(-78f, 78f), z = rng.RandfRange(-78f, 78f);
            float h = Main.HeightAt(x, z);
            if (h > 9f) continue;
            if (TerrainGrade(x, z) > 0.3f) continue;
            if (Main.RoadDistance(x, z) < 3.0f) continue;
            bool bad = false;
            foreach (var bp in _buildingPts) if (new Vector2(x, z).DistanceTo(bp) < 7f) { bad = true; break; }
            if (bad) continue;
            foreach (var c in _props) if (new Vector2(x, z).DistanceTo(c.P) < c.R + 0.6f) { bad = true; break; }
            if (bad) continue;
            spots.Add((new Vector3(x, h - 0.03f, z), rng.RandfRange(0f, Mathf.Tau), rng.RandfRange(0.5f, 1.6f)));
        }
        // Crossed-quad tuft with a base-to-tip color gradient.
        var verts = new System.Collections.Generic.List<Vector3>();
        var cols = new System.Collections.Generic.List<Color>();
        var idx = new System.Collections.Generic.List<int>();
        var dark = new Color(0.10f, 0.22f, 0.07f);
        var tip = new Color(0.38f, 0.55f, 0.20f);
        for (int q = 0; q < 2; q++)
        {
            float a = q * Mathf.Pi / 2f;
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.36f;
            int b0 = verts.Count;
            verts.Add(-dir); cols.Add(dark);
            verts.Add(dir); cols.Add(dark);
            verts.Add(dir + Vector3.Up * 0.55f); cols.Add(tip);
            verts.Add(-dir + Vector3.Up * 0.55f); cols.Add(tip);
            idx.AddRange(new[] { b0, b0 + 1, b0 + 2, b0, b0 + 2, b0 + 3 });
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = cols.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = idx.ToArray();
        var tuft = new ArrayMesh();
        tuft.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        tuft.SurfaceSetMaterial(0, new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            Roughness = 1f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        });
        // Two populations share the tuft mesh: deep green, and a
        // yellow-green variant mesh (same geometry, different gradient).
        var tuft2 = new ArrayMesh();
        var cols2 = new System.Collections.Generic.List<Color>();
        var dark2 = new Color(0.16f, 0.26f, 0.06f);
        var tip2 = new Color(0.55f, 0.60f, 0.22f);
        foreach (var c in cols) cols2.Add(c == dark ? dark2 : tip2);
        var arrays2 = new Godot.Collections.Array();
        arrays2.Resize((int)Mesh.ArrayType.Max);
        arrays2[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays2[(int)Mesh.ArrayType.Color] = cols2.ToArray();
        arrays2[(int)Mesh.ArrayType.Index] = idx.ToArray();
        tuft2.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays2);
        tuft2.SurfaceSetMaterial(0, tuft.SurfaceGetMaterial(0));
        var halves = new[] { new System.Collections.Generic.List<int>(), new System.Collections.Generic.List<int>() };
        for (int i = 0; i < spots.Count; i++) halves[rng.RandiRange(0, 1)].Add(i);
        var meshes = new[] { tuft, tuft2 };
        for (int h2 = 0; h2 < 2; h2++)
        {
            var mm = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = meshes[h2],
                InstanceCount = halves[h2].Count
            };
            for (int j = 0; j < halves[h2].Count; j++)
            {
                var (pos, yaw, sc) = spots[halves[h2][j]];
                mm.SetInstanceTransform(j, new Transform3D(
                    Basis.Identity.Rotated(Vector3.Up, yaw).Scaled(Vector3.One * sc), pos));
            }
            AddChild(new MultiMeshInstance3D { Multimesh = mm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
        GD.Print($"DUEL grass tufts={spots.Count}");
    }

    private bool ScatterFree(Vector2 p, float clearR)
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
        foreach (var c in _props)
            if (p.DistanceTo(c.P) < c.R + clearR + 2.0f) return false;
        return true;
    }

    private static float TerrainGrade(float x, float z)
    {
        const float e = 0.7f;
        float gx = (Main.HeightAt(x + e, z) - Main.HeightAt(x - e, z)) / (2f * e);
        float gz = (Main.HeightAt(x, z + e) - Main.HeightAt(x, z - e)) / (2f * e);
        return Mathf.Sqrt(gx * gx + gz * gz);
    }

    private void ScatterNature()
    {
        var rng = new System.Random(0x5EED);
        int trees = 0, tries = 0;
        while (trees < 33 && tries++ < 900)
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
        while (rocks < 25 && tries++ < 900)
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
        ScatterGrass();
    }

    private static int _facadeSeq;
    private static readonly Color[] FacadeBases =
    {
        new(0.62f, 0.60f, 0.58f), new(0.66f, 0.60f, 0.52f),
        new(0.55f, 0.58f, 0.62f), new(0.60f, 0.55f, 0.50f)
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
    private static MeshInstance3D MakeGlassPanes(Vector3 size)
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
        foreach (var (n, rgt, w) in faces)
        {
            float half = (Mathf.Abs(n.Z) > 0.5f ? size.Z : size.X) / 2f + 0.04f;
            int cols = Mathf.Clamp(Mathf.RoundToInt(w / 2.4f), 3, 6);
            int rows = Mathf.Clamp(Mathf.RoundToInt(size.Y / 2.8f), 2, 4);
            for (int ri = 0; ri < rows; ri++)
            for (int ci = 0; ci < cols; ci++)
            {
                float u = (ci + 0.5f) / cols - 0.5f;         // across the face
                float v = 0.18f + (ri + 0.5f) / rows * 0.72f; // 0 at sill band..1 top
                var center = n * half + rgt * (u * w)
                    + Vector3.Up * ((v - 0.5f) * size.Y);
                float pw = w / cols * 0.52f, ph = size.Y / rows * 0.5f;
                AddQuad(verts, norms, frameIdx, center - n * 0.02f, rgt, n, pw * 1.18f, ph * 1.18f);
                AddQuad(verts, norms, glassIdx, center, rgt, n, pw, ph);
            }
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

    private void AddBuilding(Vector3 pos, Vector3 size)
    {
        int pc0 = _props.Count, hc0 = _hit.Props.Count;
        float groundY = Main.HeightAt(pos.X, pos.Z);
        pos.Y = groundY + size.Y / 2f - 0.3f;
        var mi = new MeshInstance3D();
        mi.Mesh = new BoxMesh { Size = size };
        mi.MaterialOverride = _photo
            ? PhotoEnv.BuildingMaterial(size)
            : new StandardMaterial3D
            {
                AlbedoTexture = MakeFacadeTexture(_facadeSeq, FacadeBases[_facadeSeq % FacadeBases.Length]),
                Roughness = 0.92f
            };
        _facadeSeq++;
        mi.Position = pos;
        AddChild(mi);
        mi.AddChild(MakeGlassPanes(size));
        _buildingPts.Add(new Vector2(pos.X, pos.Z));
        // Circle colliders: one for near-square footprints, two along the
        // long axis for elongated ones. Radius covers the short side plus a
        // margin; extreme corners can poke slightly past a circle (POC).
        // The same circles register as hit-model cylinders (base sunk 0.3
        // with the mesh, full height) so buildings block shots too.
        float sx = size.X, sz = size.Z;
        if (Mathf.Max(sx, sz) / Mathf.Min(sx, sz) > 1.45f)
        {
            float off = (Mathf.Max(sx, sz) - Mathf.Min(sx, sz)) / 4f;
            float r = Mathf.Min(sx, sz) / 2f + 0.35f;
            var a = new Vector2(pos.X, pos.Z);
            var b = new Vector2(pos.X, pos.Z);
            if (sx > sz) { a.X -= off; b.X += off; } else { a.Y -= off; b.Y += off; }
            _props.Add(new Circle { P = a, R = r });
            _props.Add(new Circle { P = b, R = r });
            _hit.Props.Add(new HitModel.PropCyl { P = a, R = r, BaseY = groundY - 0.3f, Height = size.Y });
            _hit.Props.Add(new HitModel.PropCyl { P = b, R = r, BaseY = groundY - 0.3f, Height = size.Y });
        }
        else
        {
            float r = Mathf.Max(sx, sz) / 2f + 0.3f;
            _props.Add(new Circle { P = new Vector2(pos.X, pos.Z), R = r });
            _hit.Props.Add(new HitModel.PropCyl { P = new Vector2(pos.X, pos.Z), R = r, BaseY = groundY - 0.3f, Height = size.Y });
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
        // mech vs mech: split the overlap
        var a = new Vector2(_player.RootPos.X, _player.RootPos.Z);
        var b = new Vector2(_enemy.RootPos.X, _enemy.RootPos.Z);
        var d = b - a;
        float dist = d.Length();
        float min = _player.CollisionRadius + _enemy.CollisionRadius;
        // Flyover (owner): a jumping mech passes OVER the other —
        // no separation push while either is high above the ground.
        if (dist < min && _player.JumpHeight < 12f && _enemy.JumpHeight < 12f)
        {
            if (dist < 1e-4f) d = new Vector2(0f, 1f);
            else d /= dist;
            float push = (min - dist) / 2f;
            a -= d * push;
            b += d * push;
            _player.ApplyRootXZ(a);
            _enemy.ApplyRootXZ(b);
        }
    }

    // Any mech whose footprint reaches a standing tree crushes it:
    // the tree drops into the fast evaporation path (0.8s shrink with
    // dust), its shot-blocking cylinder is removed at once, and the
    // mech walks on. Buildings are never stompable.
    private void StompCheck()
    {
        foreach (var w in new[] { _player, _enemy })
        {
            if (w == _player ? _playerDead : _enemyDead) continue;
            if (w.JumpHeight > 3f) continue; // flying over the canopy is not a stomp
            var wp = new Vector2(w.RootPos.X, w.RootPos.Z);
            foreach (var d in _destructibles)
            {
                if (d.Kind != "tree" || d.State != 0) continue;
                var tp = new Vector2(d.Center.X, d.Center.Z);
                if (wp.DistanceTo(tp) > w.CollisionRadius + 0.35f) continue;
                DestroyTree(d, 0.8f);
                if (w == _player) _shake = Mathf.Max(_shake, 0.18f);
                GD.Print($"DUEL STOMP {(w == _player ? "PLAYER" : "ENEMY")} crushed the tree at ({d.Center.X:F0},{d.Center.Z:F0})");
            }
        }
    }

    private void ResolveOne(MechWalker w)
    {
        var p = new Vector2(w.RootPos.X, w.RootPos.Z);
        for (int iter = 0; iter < 2; iter++)
        {
            foreach (var c in _props)
            {
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
                    _props[i] = new Circle { P = pt, R = 0f };
    }

    private void StartCollapse(Destructible d)
    {
        d.State = 2;
        d.Timer = 3.4f; // pieces fly, settle, then dissolve (~3s lives)
        d.PhaseLen = 3.4f;
        RemovePropColliders(d); // falling masonry no longer blocks or stops mechs
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
    // off immediately (shots and mechs pass from that frame) and the
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

}
