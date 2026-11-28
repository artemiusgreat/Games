using Godot;
using Titans.Definitions;

namespace Titans;

// Titans module: terrain, props, destructibles, debris chunks, collisions.
// Split from the single-file build (round 22 cleanup); the class
// stays one partial class - see Titans.cs for the field block.
public partial class Titans
{
  // Destructible environment (owner): buildings take weapon damage
  // and collapse into rubble; trees burn and smoke away.
  // (owner, 2026-11-27: the state is an enum, not magic
  // numbers. Value 1 was a retired separate burn phase -
  // a felled tree now goes straight to SmokingOut.)
  private enum DestrState { Intact = 0, Collapsing = 2, SmokingOut = 3, Gone = 4 }

  // What a destructible IS (owner, 2026-11-27: an enum,
  // not a string). Trees are the only kind left since the
  // buildings were removed; the seam stays for the day a
  // new kind is added.
  private enum DestrKind { Tree }

  private sealed class Destructible
  {
    public DestrKind Kind;
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
      float size = this._fxRng.RandfRange(sizeMin, sizeMax);
      var mi = new MeshInstance3D
      {
        Mesh = new BoxMesh { Size = new Vector3(size, size * this._fxRng.RandfRange(ChunkSquashMin, ChunkSquashMax), size) },
        MaterialOverride = mat,
      };
      float ang = this._fxRng.RandfRange(0f, Mathf.Tau);
      mi.GlobalPosition = center + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * this._fxRng.RandfRange(0f, ChunkSpawnRadius)
             + Vector3.Up * this._fxRng.RandfRange(ChunkSpawnHeightMin, ChunkSpawnHeightMax);
      this.AddChild(mi);
      var dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
      this._envChunks.Add(new EnvChunk
      {
        Node = mi,
        Vel = dir * this._fxRng.RandfRange(ChunkSpeedMin, ChunkSpeedMax) + Vector3.Up * this._fxRng.RandfRange(upMin, upMax),
        AngAxis = new Vector3(this._fxRng.RandfRange(-1f, 1f), this._fxRng.RandfRange(-1f, 1f), this._fxRng.RandfRange(-1f, 1f)).Normalized(),
        AngSpeed = this._fxRng.RandfRange(ChunkAngSpeedMin, ChunkAngSpeedMax),
        Radius = size * ChunkRadiusFactor,
        Life = life > 0f ? life * this._fxRng.RandfRange(ChunkLifeJitterMin, ChunkLifeJitterMax) : -1f,
      });
    }
  }

  private void StepEnvChunks(float dt)
  {
    if (this._envChunks.Count == 0 || dt <= 0f)
    {
      return;
    }
    // Timed pieces (building debris) are freed at end of life.
    for (int i = this._envChunks.Count - 1; i >= 0; i--)
    {
      var c = this._envChunks[i];
      if (c.Life <= 0f)
      {
        continue;
      }

      c.Life -= dt;
      if (c.Life > 0f)
      {
        continue;
      }

      c.Node.QueueFree(); // no dust dissolve (owner, 2026-11-20: no smoke bubbles)
      this._envChunks.RemoveAt(i);
    }
    if (this._envChunks.Count == 0)
    {
      return;
    }

    int n = Mathf.Max(1, Mathf.CeilToInt(dt / ChunkSubstepSeconds));
    float h = dt / n;
    for (int step = 0; step < n; step++)
    {
      foreach (var c in this._envChunks)
      {
        if (c.Settled)
        {
          continue;
        }

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
  }

  // ---------- world ----------

  private MeshInstance3D BuildTerrainMesh()
  {
    int chunks = Maps.Active.TerrainChunks;
    if (chunks > 1)
    {
      // A chunked map's ground is built in windows over
      // the same grid, so the streaming pass can unload
      // the far end of the world (owner, 2026-11-20).
      MeshInstance3D first = null;
      int chunk = Main.TerrainSegs / chunks;
      for (int cj = 0; cj < chunks; cj++)
      {
        for (int ci = 0; ci < chunks; ci++)
        {
          var cmi = new MeshInstance3D
          {
            Mesh = Main.CreateTerrainMeshRange(
                  ci * chunk, (ci + 1) * chunk, cj * chunk, (cj + 1) * chunk)
          };
          this.AddChild(cmi);
          this._terrainChunks.Add(cmi);
          float cx = -Main.TerrainSize / 2f + (ci + 0.5f) * (Main.TerrainSize / chunks);
          float cz = -Main.TerrainSize / 2f + (cj + 0.5f) * (Main.TerrainSize / chunks);
          this._streamed.Add((cmi, new Vector2(cx, cz), TerrainChunkStreamRadius));
          if (first == null)
          {
            first = cmi;
          }
        }
      }

      return first;
    }
    // Shared heightfield mesh (vertices on grid nodes = HeightAt).
    var mi = new MeshInstance3D { Mesh = Main.CreateTerrainMesh() };
    this.AddChild(mi);
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
    if (this._photo || WorldConfig.Photoreal)
    {
      var mat = PhotoEnv.TerrainMaterial();
      mi.MaterialOverride = mat;
      foreach (var c in this._terrainChunks)
      {
        c.MaterialOverride = mat;
      }
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

  // One seed for every nature scatter (mission and arena
  // alike): the layouts are deterministic, and both passes
  // draw from the same stream start, so a seed edit moves
  // both instead of silently drifting apart.
  private const int ScatterSeed = 0x5EED;

  private void ScatterNatureMission()
  {
    var rng = new System.Random(ScatterSeed);
    var zones = new System.Collections.Generic.List<(Vector2 C, float R, int Trees)>();
    foreach (var pad in Main.SpawnPads)
    {
      zones.Add((pad, PadGroveRadius, PadGroveTrees));
    }

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
        if (Mathf.Abs(p.X) > ScatterBoundMission || Mathf.Abs(p.Y) > ScatterBoundMission)
        {
          continue;
        }

        float sc = ScatterTreeScaleBase + (float)rng.NextDouble() * ScatterTreeScaleRange;
        if (!this.ScatterFree(p, TreeRadiusFactor * sc + ScatterTreeClearance))
        {
          continue;
        }

        if (TerrainGrade(p.X, p.Y) > ScatterTreeGradeMax)
        {
          continue;
        }

        if (Main.HeightAt(p.X, p.Y) > ScatterTreeHeightMax)
        {
          continue;
        }

        this.AddTreeV(new Vector3(p.X, 0f, p.Y), rng.Next(3), sc, (float)rng.NextDouble());
        trees++;
      }
    }
  }

  private void AddTree(Vector3 pos) => this.AddTreeV(pos, 0, 1f, 0f);

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
    foreach (var child in root.GetChildren())
    {
      ApplyVisibilityRange(child, end, margin);
    }
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
      Mesh = LeafMesh,
      MaterialOverride = mat,
      Position = c,
      Scale = new Vector3(r, r * squash.Y, r)
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
    if (this._photo || WorldConfig.Photoreal)
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
        {
          tree.AddChild(LeafBlob(tip + new Vector3(0f, LeafTipLift, 0f),
                    rng.RandfRange(LeafBlobRadiusMin, LeafBlobRadiusMax), new Vector3(1f, rng.RandfRange(LeafSquashMin, LeafSquashMax), 1f),
                    leafMats[rng.RandiRange(0, 2)]));
        }

        tree.AddChild(LeafBlob(new Vector3(0f, trunkH + 1.35f, 0f), 1.35f,
                new Vector3(1f, 0.8f, 1f), leafMats[rng.RandiRange(0, 2)]));
        tree.AddChild(LeafBlob(new Vector3(0.5f, trunkH + 0.9f, -0.4f), 1.0f,
            new Vector3(1f, 0.75f, 1f), leafMats[rng.RandiRange(0, 2)]));
        totalH = trunkH + TreeCanopyExtra;
      }
    }
    this.AddChild(tree);
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
    // into one stomps it flat (see StompCheck). Nothing keeps
    // movement circles any more (the buildings and rocks are
    // gone). The hit cylinder stays, so trees still block
    // shots and can be shot/burned.
    var treeCyl = new HitModel.PropCyl { P = new Vector2(pos.X, pos.Z), R = r, BaseY = pos.Y, Height = totalH * scale };
    this._hit.Props.Add(treeCyl);
    // The canopy is a collider too (owner, three reports): the
    // visible crown spans ~+-2u (branches + leaf blobs) but only
    // the 0.7u trunk collided, so shots through the leaves -
    // aimed at the tree's visible center - passed through
    // nothing. A tree now collides as it looks.
    var canopyCyl = new HitModel.PropCyl
    {
      P = new Vector2(pos.X, pos.Z),
      R = CanopyRadiusFactor * scale,
      BaseY = pos.Y + totalH * scale * CanopyBaseFraction,
      Height = totalH * scale * CanopyHeightFraction
    };
    this._hit.Props.Add(canopyCyl);
    var td = new Destructible
    {
      Kind = DestrKind.Tree,
      Node = tree,
      Center = new Vector3(pos.X, pos.Y, pos.Z),
      Radius = r,
      Height = totalH * scale,
      Hp = TreeHp,
      StartScale = scale
    };
    td.Cyls.Add(treeCyl);
    td.Cyls.Add(canopyCyl);
    this._destructibles.Add(td);
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
      {
        if (p.X > lane.X0 && p.X < lane.X1 && p.Y > lane.Z0 && p.Y < lane.Z1)
        {
          return false;
        }
      }

      foreach (var pad in Arena.ScatterPads)
      {
        if (p.DistanceTo(new Vector2(pad.X, pad.Z)) < pad.R + clearR)
        {
          return false;
        }
      }
    }
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
      if (rh - h > PitRiseMax)
      {
        return true;
      }

      if (h - rh > PitDropMax)
      {
        return true;
      }
    }
    return false;
  }

  // Central-difference half-step for the slope estimate
  // below: HeightAt is sampled this far either side of the
  // point. Wide enough to average out the heightfield's
  // grid steps, narrow enough to follow a real grade.
  private const float GradeStep = 0.7f;

  private static float TerrainGrade(float x, float z)
  {
    float gx = (Main.HeightAt(x + GradeStep, z) - Main.HeightAt(x - GradeStep, z)) / (2f * GradeStep);
    float gz = (Main.HeightAt(x, z + GradeStep) - Main.HeightAt(x, z - GradeStep)) / (2f * GradeStep);
    return Mathf.Sqrt(gx * gx + gz * gz);
  }

  // Seeded nature scatter: ~33 more trees (40 total) and 25
  // rocks. Placement respects the test choreography:
  // duel/weapons/breakable lanes and every spawn/teleport pad
  // stay clear, props keep spacing, trees stay off steep/high
  // ground.
  private void ScatterNature()
  {
    if (WorldConfig.IsMission) { this.ScatterNatureMission(); return; }
    var rng = new System.Random(ScatterSeed);
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
      if (!this.ScatterFree(p, TreeRadiusFactor * sc + ScatterTreeClearance))
      {
        continue;
      }

      if (TerrainGrade(p.X, p.Y) > ScatterTreeGradeMax)
      {
        continue;
      }

      if (Main.HeightAt(p.X, p.Y) > ScatterTreeHeightMax)
      {
        continue;
      }

      this.AddTreeV(new Vector3(p.X, 0f, p.Y), rng.Next(3), sc, (float)rng.NextDouble());
      trees++;
    }
    Dbg.Print($"DUEL scatter trees+={trees}");
  }

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
                                                     // Two centres this close count as coincident: the
                                                     // separation direction between them is undefined, so the
                                                     // guards in ResolvePair / RiderBlocks substitute a fixed
                                                     // one instead of dividing by ~0. The value is NOT a
                                                     // precision epsilon: a float near 100u of world scale
                                                     // resolves only ~1e-5, so a tighter guard (1e-8) could
                                                     // never be trusted to trigger - 1e-4 sits safely above
                                                     // that noise and far below anything visible (0.1mm).
  private const float CoincidentDist = 1e-4f;

  // Every rider on the field, whatever roster it came
  // from (owner, 2026-11-27: one term for all of them in
  // the collision code - the foe/ally split lives in the
  // rosters that own those riders, not in the physics).
  // Rebuilt by the collision pass and the steering brake,
  // the two consumers; walkers only, in roster order.
  private readonly System.Collections.Generic.List<RiderWalker> _fieldRiders = new();

  private void CollectFieldRiders()
  {
    this._fieldRiders.Clear();
    this._fieldRiders.Add(this._player);
    this._fieldRiders.Add(this._enemy);
    if (this._foe != null)
    {
      this._fieldRiders.Add(this._foe.Walker);
    }

    foreach (var a in this._allies)
    {
      if (a.Walker != null)
      {
        this._fieldRiders.Add(a.Walker);
      }
    }
  }

  private void ResolveCollisions()
  {
    this.CollectFieldRiders();
    // Teammates are bodies too (owner, 2026-11-15: the
    // allies were never resolved at all, so a follower
    // walked straight through the leader): every rider
    // resolves against the world, then every pair against
    // each other, under the same universal separation.
    foreach (var w in this._fieldRiders)
    {
      this.ResolveOne(w);
    }

    for (int i = 0; i < this._fieldRiders.Count; i++)
    {
      for (int j = i + 1; j < this._fieldRiders.Count; j++)
      {
        this.ResolvePair(this._fieldRiders[i], this._fieldRiders[j]);
      }
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
    if (dist >= min || wa.JumpHeight >= FlyoverHeight || wb.JumpHeight >= FlyoverHeight)
    {
      return;
    }

    if (dist < CoincidentDist)
    {
      d = new Vector2(0f, 1f);
    }
    else
    {
      d /= dist;
    }

    float push = min - dist;
    if (wa == this._player) { b += d * push; wb.ApplyRootXZ(b); }
    else if (wb == this._player) { a -= d * push; wa.ApplyRootXZ(a); }
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
    this.CollectFieldRiders();
    foreach (var o in this._fieldRiders)
    {
      if (this.RiderBlocks(w, o, p, step))
      {
        return true;
      }
    }

    return false;
  }

  private bool RiderBlocks(RiderWalker w, RiderWalker o, Vector2 p, Vector2 step)
  {
    if (o == null || o == w || this.IsDead(o))
    {
      return false;
    }

    var to = new Vector2(o.RootPos.X, o.RootPos.Z) - p;
    float len = to.Length();
    float min = w.CollisionRadius + o.CollisionRadius + RiderBody.Gap + SteeringBrakeMargin;
    if (len >= min)
    {
      return false;
    }

    if (len < CoincidentDist)
    {
      return true;
    }

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
  // removed at once, and the rider walks on unimpeded.
  private void StompCheck()
  {
    // Team riders stomp too (owner's report, 2026-11-14:
    // trees survived being walked on - the NPC walkers of
    // story and bracket fights were never in this list).
    this.CollectFieldRiders();
    foreach (var w in this._fieldRiders)
    {
      if (this.IsDead(w))
      {
        continue;
      }

      if (w.JumpHeight > StompMaxJumpHeight)
      {
        continue; // flying over the canopy is not a stomp
      }

      var wp = new Vector2(w.RootPos.X, w.RootPos.Z);
      foreach (var d in this._destructibles)
      {
        if (d.Kind != DestrKind.Tree || d.State != DestrState.Intact)
        {
          continue;
        }

        var tp = new Vector2(d.Center.X, d.Center.Z);
        // Reach is the CROWN's width, not the trunk's
        // (owner, 2026-11-14): the visible canopy spans
        // +-2u, so a rider whose body reaches the crown
        // has reached the tree - measuring from the
        // 0.7u trunk let riders brush the leaves and
        // leave the tree standing.
        float reach = d.Radius;
        foreach (var cyl in d.Cyls)
        {
          reach = Mathf.Max(reach, cyl.R);
        }

        if (wp.DistanceTo(tp) > w.CollisionRadius + reach + StompReachMargin)
        {
          continue;
        }

        this.DestroyTree(d, StompSmokeSeconds);
        if (w == this._player)
        {
          this._shake = Mathf.Max(this._shake, StompShake);
        }

        Dbg.Print($"DUEL STOMP {this.TagOf(w)} crushed the tree at ({d.Center.X:F0},{d.Center.Z:F0})");
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

  private void ResolveOne(RiderWalker w)
  {
    // Movement collision is the world bound alone (owner,
    // 2026-11-27: trees are stomped, never walls, and the
    // building circles are gone with the buildings).
    var p = new Vector2(w.RootPos.X, w.RootPos.Z);
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
    foreach (var d in this._destructibles)
    {
      if (d.State != DestrState.Intact)
      {
        continue;
      }

      foreach (var c in d.Cyls)
      {
        float dxz = new Vector2(point.X - c.P.X, point.Z - c.P.Y).Length();
        float over = dxz - c.R;
        if (over > PropHitReach + extraReach)
        {
          continue;
        }

        if (point.Y < c.BaseY - PropHitBandMargin || point.Y > c.BaseY + c.Height + PropHitBandMargin)
        {
          continue;
        }

        if (over < bestD) { bestD = over; best = d; }
      }
    }
    return best;
  }

  private void DamageProp(Vector3 point, float dmg, float extraReach = 0f)
  {
    var d = this.PropAt(point, extraReach);
    if (d == null)
    {
      return;
    }

    if (d.Kind == DestrKind.Tree) { this.DestroyTree(d, HitSmokeSeconds); return; }
  }

  private void RemovePropColliders(Destructible d)
  {
    foreach (var c in d.Cyls)
    {
      this._hit.Props.Remove(c);
    }
  }

  // Destruction FX recipes (naming pass, 2026-11-27:
  // names only, exact values) - the tree-death bursts,
  // flashes, lights and chunks.
  private static readonly Vector3 FireBurstGravity = new(0f, -6f, 0f); // shared by the collapse and tree fire bursts
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

  private void DestroyTree(Destructible d, float smokeLen)
  {
    if (d.State != DestrState.Intact)
    {
      return;
    }

    d.State = DestrState.SmokingOut;
    d.PhaseLen = smokeLen;
    d.Timer = smokeLen;
    this.RemovePropColliders(d);
    var top = d.Center + Vector3.Up * d.Height * TreeBurstHeightFraction;
    // Destruction FX (round 7): a fire flash, an ember burst and
    // wooden splinters that scatter and settle - a hit tree goes
    // out with a burst of sparks, then smokes out.
    this._envFxEvents++;
    this.SpawnFlash(top, TreeFlashColor, TreeFlashRadius, TreeFlashLife);
    this.SpawnSparks(top, TreeSparkColor, TreeSparkCount, TreeSparkSpeed);
    this._fxSparkBursts--; // tree embers are not a laser burst; keep the counters honest
    this.SpawnImpactLight(top, TreeLightColor, TreeLightEnergy, TreeLightRange, TreeLightLife);
    this.PlaySfx(this._sfxTree, d.Center, 0f);
    this.SpawnChunks(top, SplinterCount, SplinterSizeMin, SplinterSizeMax, SplinterMat, SplinterUpMin, SplinterUpMax);
    this.SpawnBurst(top, FireParticleMat, TreeFireCount, TreeFireLifetime, TreeFireVelMin, TreeFireVelMax, FireBurstGravity, TreeFireMeshRadius, TreeFireScaleMin, TreeFireScaleMax);
    this.SpawnBurnEmitter(d.Center + Vector3.Up * TreeBurnEmitterHeight, TreeBurnSeconds);
    for (int i = 0; i < TreePuffCount; i++)
    {
      var puff = WeaponFx.MakeTrailPuff(d.Center + Vector3.Up * (TreePuffBaseHeight + i * TreePuffStepHeight));
      this.AddChild(puff);
      this._puffs.Add((puff, TreePuffLife));
    }
    Dbg.Print($"ENV tree at ({d.Center.X:F0},{d.Center.Z:F0}) DESTROYED");
  }

  private void UpdateDestructibles(float dt)
  {
    foreach (var d in this._destructibles)
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
          puff.Scale = Vector3.One * this._fxRng.RandfRange(TreeSmokePuffSizeMin, TreeSmokePuffSizeMax);
          this.AddChild(puff);
          this._puffs.Add((puff, TreeSmokePuffLife));
        }
        d.FxAcc += dt;
        if (d.FxAcc > TreeSmokeFlashInterval)
        {
          d.FxAcc = 0f;
          this.SpawnFlash(d.Center + Vector3.Up * (TreeSmokeFlashBase + d.Height * TreeSmokeFlashHeightFraction * u),
                        TreeSmokeFlashColor, TreeSmokeFlashRadius, TreeSmokeFlashLife); // the fire still burning as it smokes out
        }
        if (d.Timer <= 0f)
        {
          d.State = DestrState.Gone;
          this.RemovePropColliders(d);
          d.Node.QueueFree();
          Dbg.Print($"ENV tree at ({d.Center.X:F0},{d.Center.Z:F0}) GONE");
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
      this.TunePaintedSky(env, sun);
      Dbg.Print($"SKY tiled sky applied: {tilePath}");
      return;
    }
    var path = WorldConfig.SkyTex;
    if (string.IsNullOrEmpty(path))
    {
      return;
    }

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
    {
      skyMat = new PanoramaSkyMaterial { Panorama = tex };
    }

    env.Sky = new Sky { SkyMaterial = skyMat };
    this.TunePaintedSky(env, sun);
    Dbg.Print($"SKY map panorama applied: {path}");
    if (starsOn)
    {
      Dbg.Print("SKY star layer: procedural stars over the photo (hybrid)");
    }
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
