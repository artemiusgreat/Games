using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Extensions;
using Stride.Graphics;
using Stride.Graphics.GeometricPrimitives;
using Stride.Rendering;
using Stride.Rendering.Materials;
using Stride.Rendering.Materials.ComputeColors;

namespace StridePoc
{
    /// <summary>
    /// Round 2 / Probe 3 player: auto-walks the big terrain (height sampling,
    /// same as round 1), logs per-second frame times, writes a phase file for
    /// the external frame-capture harness, exits with a stats summary.
    /// </summary>
    public class ScalePlayerController : SyncScript
    {
        private bool _started;
        private Vector3 _startPos;
        private float _elapsed;
        private float _lastWrite;
        private float _statWindow;
        private int _statFrames;
        private float _statMsSum;
        private float _statMsMax;
        private string _phaseFile;
        public static float LastAvgMs;
        public static int TotalFrames;
        public static float WorstMs;

        public override void Start()
        {
            var arg = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--phase-file="));
            _phaseFile = arg?.Substring("--phase-file=".Length);
        }

        public override void Update()
        {
            if (!_started) { _startPos = Entity.Transform.Position; _started = true; }
            float dtMs = (float)Game.UpdateTime.Elapsed.TotalMilliseconds;
            float dt = Math.Min(dtMs / 1000f, 0.25f);
            _elapsed += dt;
            TotalFrames++;
            _statFrames++; _statMsSum += dtMs; _statMsMax = Math.Max(_statMsMax, dtMs);
            _statWindow += dt;

            var pos = Entity.Transform.Position;
            pos.Z -= 9.5f * dt; // run forward (-Z), like round-1 walk test
            pos.Y = ScaleWorldBuilder.HeightBig(pos.X, pos.Z) + 0.9f;
            Entity.Transform.Position = pos;

            if (_elapsed - _lastWrite > 0.25f) { _lastWrite = _elapsed; WritePhase(); }
            if (_statWindow >= 1.0f)
            {
                float avg = _statMsSum / Math.Max(1, _statFrames);
                LastAvgMs = avg; WorstMs = Math.Max(WorstMs, _statMsMax);
                Console.WriteLine($"SCALE t={_elapsed:F1} avgMs={avg:F1} maxMs={_statMsMax:F1} fps={1000f / Math.Max(avg, 0.01f):F1} pos={pos}");
                _statWindow = 0; _statFrames = 0; _statMsSum = 0; _statMsMax = 0;
            }
            if (_elapsed >= 14f)
            {
                WritePhase(true);
                Console.WriteLine($"SCALE done moved={Vector3.Distance(_startPos, Entity.Transform.Position):F1} frames={TotalFrames} worstMs={WorstMs:F1}");
                Environment.Exit(0);
            }
        }

        private void WritePhase(bool done = false)
        {
            if (_phaseFile == null) return;
            try { File.WriteAllText(_phaseFile, done ? $"done {_elapsed:F2}" : $"walk {_elapsed:F2}"); } catch { }
        }
    }

    /// <summary>
    /// Round 2 / Probe 3: scale test. 1000x1000 terrain from the round-1 height
    /// function family (stretched + amplified), textured with a procedural
    /// 512x512 PBR ground material (texture baked in code from the height
    /// function + value noise: grass / dirt / rock by height and slope),
    /// 200 trees + 100 rocks as THREE instanced draws (InstancingComponent /
    /// InstancingUserArray — one ModelComponent per prop kind, 300 matrices
    /// total), the scene's single directional light already has shadows
    /// enabled (MainScene), player capsule walks by height sampling.
    /// Heightfield collider: still no code-first path (see NOTES) — sampling.
    /// </summary>
    public static class ScaleWorldBuilder
    {
        public static float HeightBig(float x, float z)
        {
            // Same family as round 1: same sum-of-sines, coordinates stretched
            // 1/0.15 (~6.7x broader hills) and amplitude x4 (peaks ~ +/-18).
            return WorldBuilder.HeightAt(x * 0.15f, z * 0.15f) * 4f;
        }

        public static void Build(StartupScript host, Scene scene)
        {
            var gd = host.GraphicsDevice;
            var rng = new Random(1234);

            var terrain = BuildTerrain(host, gd);
            scene.Entities.Add(terrain);

            // Scatter: deterministic RNG, keep the player's run lane (x ~ 0) clear-ish.
            var trunkMats = new List<Matrix>();
            var canopyMats = new List<Matrix>();
            var rockMats = new List<Matrix>();
            while (trunkMats.Count < 200)
            {
                float x = (float)(rng.NextDouble() * 960 - 480);
                float z = (float)(rng.NextDouble() * 960 - 480);
                if (Math.Abs(x) < 5f && z < 40f && z > -160f) continue;
                float y = HeightBig(x, z);
                float s = 0.8f + (float)rng.NextDouble() * 0.7f;
                float rot = (float)(rng.NextDouble() * Math.PI * 2);
                trunkMats.Add(Matrix.Scaling(s) * Matrix.RotationY(rot) * Matrix.Translation(x, y + 1.1f * s, z));
                canopyMats.Add(Matrix.Scaling(s) * Matrix.RotationY(rot) * Matrix.Translation(x, y + 3.6f * s, z));
            }
            while (rockMats.Count < 100)
            {
                float x = (float)(rng.NextDouble() * 960 - 480);
                float z = (float)(rng.NextDouble() * 960 - 480);
                if (Math.Abs(x) < 5f && z < 40f && z > -160f) continue;
                float y = HeightBig(x, z);
                float s = 0.7f + (float)rng.NextDouble() * 1.6f;
                float rot = (float)(rng.NextDouble() * Math.PI * 2);
                // Squashed spheres, sunk into the slope: read as low domes.
                rockMats.Add(Matrix.Scaling(s, s * 0.6f, s) * Matrix.RotationY(rot) * Matrix.Translation(x, y + 0.5f, z));
            }

            int trunkTris = AddInstanced(scene, gd, host,
                GeometricPrimitive.Cylinder.New(gd, 2.2f, 0.26f), new Color4(0.4f, 0.26f, 0.13f, 1f), trunkMats, "Trunks");
            int canopyTris = AddInstanced(scene, gd, host,
                GeometricPrimitive.Sphere.New(gd, 1.7f), new Color4(0.13f, 0.45f, 0.16f, 1f), canopyMats, "Canopies");
            var rockColor = Environment.GetCommandLineArgs().Contains("--rocks-red")
                ? new Color4(1f, 0f, 0f, 1f) : new Color4(0.45f, 0.45f, 0.48f, 1f);
            int rockTris = AddInstanced(scene, gd, host,
                GeometricPrimitive.Sphere.New(gd, 1.0f), rockColor, rockMats, "Rocks");

            // Player capsule + controller + camera (round-1 pattern).
            var playerModel = new Model();
            var capPrim = GeometricPrimitive.Capsule.New(gd, 1.0f, 0.4f);
            playerModel.Meshes.Add(new Mesh { Draw = capPrim.ToMeshDraw(), MaterialIndex = 0 });
            playerModel.Materials.Add(Colored(host, gd, new Color4(0.15f, 0.35f, 0.9f, 1f)));
            var player = new Entity("Player");
            player.Add(new ModelComponent { Model = playerModel });
            player.Transform.Position = new Vector3(0f, HeightBig(0f, 30f) + 0.9f, 30f);
            player.Add(new ScalePlayerController());
            scene.Entities.Add(player);

            var camera = scene.Entities.FirstOrDefault(e => e.Name == "Camera");
            camera?.Add(new CameraRig { Target = player });

            int totalTris = TerrainTris + trunkTris * 200 + canopyTris * 200 + rockTris * 100 + capPrim.ToMeshDraw().DrawCount / 3;
            Console.WriteLine($"SCALE stats terrainTris={TerrainTris} trunkTrisEach={trunkTris} canopyTrisEach={canopyTris} rockTrisEach={rockTris} totalSceneTris={totalTris} instancedDraws=3 instances={200 + 200 + 100} modelDrawsMainPass=5 (terrain+3 instanced+player)");
        }

        private static int TerrainTris;

        private static Material Colored(StartupScript host, GraphicsDevice gd, Color4 color)
        {
            return Material.New(gd, new MaterialDescriptor
            {
                Attributes = new MaterialAttributes
                {
                    Diffuse = new MaterialDiffuseMapFeature(new ComputeColor(color)),
                    DiffuseModel = new MaterialDiffuseLambertModelFeature()
                }
            }, host.Content);
        }

        private static int AddInstanced(Scene scene, GraphicsDevice gd, StartupScript host,
            GeometricPrimitive prim, Color4 color, List<Matrix> matrices, string name)
        {
            var draw = prim.ToMeshDraw();
            var model = new Model();
            model.Meshes.Add(new Mesh { Draw = draw, MaterialIndex = 0 });
            model.Materials.Add(Colored(host, gd, color));
            var e = new Entity(name);
            var mc = new ModelComponent { Model = model, IsShadowCaster = true };
            mc.BoundingBox = new BoundingBox(new Vector3(-600f, -60f, -600f), new Vector3(600f, 120f, 600f));
            e.Add(mc);
            var arr = new InstancingUserArray();
            // InstanceCount/BoundingBox are computed by UpdateWorldMatrices/Update.
            arr.UpdateWorldMatrices(matrices.ToArray(), matrices.Count);
            arr.Update();
            e.Add(new InstancingComponent { Type = arr });
            scene.Entities.Add(e);
            return draw.DrawCount / 3;
        }

        private static Entity BuildTerrain(StartupScript host, GraphicsDevice gd)
        {
            const int segs = 250;
            const float size = 1000f;
            float half = size / 2f, step = size / segs;
            var verts = new VertexPositionNormalTexture[(segs + 1) * (segs + 1)];
            for (int j = 0; j <= segs; j++)
            {
                for (int i = 0; i <= segs; i++)
                {
                    float x = -half + i * step, z = -half + j * step;
                    float hx = HeightBig(x + 0.5f, z) - HeightBig(x - 0.5f, z);
                    float hz = HeightBig(x, z + 0.5f) - HeightBig(x, z - 0.5f);
                    var n = new Vector3(-hx, 1f, -hz);
                    n.Normalize();
                    verts[j * (segs + 1) + i] = new VertexPositionNormalTexture(
                        new Vector3(x, HeightBig(x, z), z), n, new Vector2(i / (float)segs, j / (float)segs));
                }
            }
            var indices = new int[segs * segs * 6];
            int k = 0;
            for (int j = 0; j < segs; j++)
            {
                for (int i = 0; i < segs; i++)
                {
                    int a = j * (segs + 1) + i, b = a + 1, c = a + segs + 1, d = c + 1;
                    indices[k++] = a; indices[k++] = b; indices[k++] = c;
                    indices[k++] = b; indices[k++] = d; indices[k++] = c;
                }
            }
            TerrainTris = indices.Length / 3;
            var mesh = new Mesh
            {
                Draw = new MeshDraw
                {
                    PrimitiveType = PrimitiveType.TriangleList,
                    DrawCount = indices.Length,
                    VertexBuffers = new[]
                    {
                        new VertexBufferBinding(
                            Stride.Graphics.Buffer.Vertex.New(gd, verts, GraphicsResourceUsage.Default),
                            VertexPositionNormalTexture.Layout, verts.Length)
                    },
                    IndexBuffer = new IndexBufferBinding(
                        Stride.Graphics.Buffer.Index.New(gd, indices, GraphicsResourceUsage.Default), true, indices.Length)
                },
                MaterialIndex = 0
            };
            var model = new Model();
            model.Meshes.Add(mesh);
            model.Materials.Add(GroundMaterial(host, gd));
            var e = new Entity("TerrainBig");
            e.Add(new ModelComponent { Model = model });
            return e;
        }

        // Procedural PBR ground material: 512x512 texture baked from the height
        // function over the whole terrain extent (grass/dirt/rock by height and
        // slope) with value-noise detail, in a Stride material with a diffuse
        // texture map, Lambert diffuse and GGX microfacet specular.
        private static Material GroundMaterial(StartupScript host, GraphicsDevice gd)
        {
            const int T = 512;
            var data = new Color[T * T];
            bool diag = Environment.GetCommandLineArgs().Contains("--tex-diag");
            for (int j = 0; j < T; j++)
            {
                for (int i = 0; i < T; i++)
                {
                    if (diag)
                    {
                        data[j * T + i] = ((i / 32 + j / 32) % 2 == 0) ? new Color(0f, 0f, 0f, 1f) : new Color(1f, 1f, 1f, 1f);
                        continue;
                    }
                    float x = (i / (float)(T - 1) - 0.5f) * 1000f;
                    float z = (j / (float)(T - 1) - 0.5f) * 1000f;
                    float h = HeightBig(x, z);
                    float slope = Math.Abs(HeightBig(x + 2f, z) - HeightBig(x - 2f, z))
                                + Math.Abs(HeightBig(x, z + 2f) - HeightBig(x, z - 2f));
                    float noise = 0.5f + 0.5f * MathF.Sin(x * 1.31f + MathF.Sin(z * 1.73f) * 2.1f) * MathF.Sin(z * 1.13f + MathF.Sin(x * 0.71f) * 1.7f);
                    float detail = 0.85f + 0.3f * noise;
                    // NOTE: Stride Color.R/G/B are bytes — keep components as
                    // floats here or they clamp to white (found via --tex-diag).
                    float r, g, b;
                    if (slope > 2.6f || h > 11f) { r = 0.42f; g = 0.40f; b = 0.38f; }        // rock
                    else if (h > 5.5f) { r = 0.45f; g = 0.36f; b = 0.22f; }                  // dirt
                    else { r = 0.20f; g = 0.44f; b = 0.18f; }                              // grass
                    data[j * T + i] = new Color(r * detail, g * detail, b * detail, 1f);
                }
            }
            var tex = Texture.New2D(gd, T, T, PixelFormat.R8G8B8A8_UNorm, data, TextureFlags.ShaderResource, GraphicsResourceUsage.Default);
            return Material.New(gd, new MaterialDescriptor
            {
                Attributes = new MaterialAttributes
                {
                    Diffuse = new MaterialDiffuseMapFeature(new ComputeTextureColor(tex)
                    {
                        FallbackValue = new ComputeColor(new Color4(0.3f, 0.5f, 0.25f, 1f))
                    }),
                    DiffuseModel = new MaterialDiffuseLambertModelFeature(),
                    Specular = new MaterialSpecularMapFeature { SpecularMap = new ComputeColor(new Color4(0.04f, 0.04f, 0.04f, 1f)) },
                    SpecularModel = new MaterialSpecularMicrofacetModelFeature()
                }
            }, host.Content);
        }
    }
}
