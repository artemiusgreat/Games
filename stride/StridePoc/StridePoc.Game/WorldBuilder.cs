using System;
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
    /// Builds the whole slice in code at startup: procedural terrain, trees,
    /// buildings, a static NPC, and the player (with camera rig attached to
    /// the scene's Camera entity). The scene file itself contains only a
    /// directional light, a camera, and this script's host entity.
    /// </summary>
    public class WorldBuilder : StartupScript
    {
        // Same procedural height function as the other engines' slices.
        public static float HeightAt(float x, float z)
        {
            return 2.6f * MathF.Sin(x * 0.07f) * MathF.Cos(z * 0.055f)
                 + 1.4f * MathF.Sin(x * 0.21f + 1.7f) * MathF.Sin(z * 0.17f + 0.6f)
                 + 0.5f * MathF.Sin(x * 0.45f) * MathF.Cos(z * 0.40f);
        }

        public override void Start()
        {
            var scene = Entity.Scene;

            scene.Entities.Add(BuildTerrain());

            AddTree(scene, -12f, -8f);
            AddTree(scene, 14f, -18f);
            AddTree(scene, 6f, 16f);

            AddBuilding(scene, -20f, 14f, new Vector3(7f, 5f, 6f));
            AddBuilding(scene, 22f, 8f, new Vector3(6f, 7f, 6f));

            // Static NPC "enemy": red capsule standing on the terrain.
            var npc = Prim(GeometricPrimitive.Capsule.New(GraphicsDevice, 1.0f, 0.4f),
                new Color4(0.85f, 0.15f, 0.15f, 1f), new Vector3(3f, HeightAt(3f, -14f) + 0.9f, -14f));
            npc.Name = "NPC";
            scene.Entities.Add(npc);

            // Player: blue capsule + controller script.
            var player = Prim(GeometricPrimitive.Capsule.New(GraphicsDevice, 1.0f, 0.4f),
                new Color4(0.15f, 0.35f, 0.9f, 1f), new Vector3(0f, HeightAt(0f, 8f) + 0.9f, 8f));
            player.Name = "Player";
            var controller = new PlayerController
            {
                TestMode = Environment.GetCommandLineArgs().Contains("--walk-test")
            };
            player.Add(controller);
            scene.Entities.Add(player);
            controller.CameraEntity = scene.Entities.FirstOrDefault(e => e.Name == "Camera");

            controller.CameraEntity?.Add(new CameraRig { Target = player });
        }

        private Material Colored(Color4 color)
        {
            return Material.New(GraphicsDevice, new MaterialDescriptor
            {
                Attributes = new MaterialAttributes
                {
                    Diffuse = new MaterialDiffuseMapFeature(new ComputeColor(color)),
                    DiffuseModel = new MaterialDiffuseLambertModelFeature()
                }
            }, Content);
        }

        private Entity Prim(GeometricPrimitive prim, Color4 color, Vector3 pos)
        {
            var model = new Model();
            model.Meshes.Add(new Mesh { Draw = prim.ToMeshDraw(), MaterialIndex = 0 });
            model.Materials.Add(Colored(color));
            var e = new Entity();
            e.Add(new ModelComponent { Model = model });
            e.Transform.Position = pos;
            return e;
        }

        private Entity BuildTerrain()
        {
            const int segs = 100;
            const float size = 100f;
            float half = size / 2f, step = size / segs;
            var verts = new VertexPositionNormalColor[(segs + 1) * (segs + 1)];
            var green = new Color(0.25f, 0.5f, 0.22f, 1);
            for (int j = 0; j <= segs; j++)
            {
                for (int i = 0; i <= segs; i++)
                {
                    float x = -half + i * step, z = -half + j * step;
                    float hx = HeightAt(x + 0.5f, z) - HeightAt(x - 0.5f, z);
                    float hz = HeightAt(x, z + 0.5f) - HeightAt(x, z - 0.5f);
                    var n = new Vector3(-hx, 1f, -hz);
                    n.Normalize();
                    verts[j * (segs + 1) + i] = new VertexPositionNormalColor(
                        new Vector3(x, HeightAt(x, z), z), n, green);
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
            var mesh = new Mesh
            {
                Draw = new MeshDraw
                {
                    PrimitiveType = PrimitiveType.TriangleList,
                    DrawCount = indices.Length,
                    VertexBuffers = new[]
                    {
                        new VertexBufferBinding(
                            Stride.Graphics.Buffer.Vertex.New(GraphicsDevice, verts, GraphicsResourceUsage.Default),
                            VertexPositionNormalColor.Layout, verts.Length)
                    },
                    IndexBuffer = new IndexBufferBinding(
                        Stride.Graphics.Buffer.Index.New(GraphicsDevice, indices, GraphicsResourceUsage.Default), true, indices.Length)
                },
                MaterialIndex = 0
            };
            var model = new Model();
            model.Meshes.Add(mesh);
            model.Materials.Add(Colored(new Color4(0.25f, 0.5f, 0.22f, 1f)));
            var e = new Entity("Terrain");
            e.Add(new ModelComponent { Model = model });
            return e;
        }

        private void AddTree(Scene scene, float x, float z)
        {
            float y = HeightAt(x, z);
            var trunk = Prim(GeometricPrimitive.Cylinder.New(GraphicsDevice, 2.2f, 0.26f),
                new Color4(0.4f, 0.26f, 0.13f, 1f), new Vector3(x, y + 1.1f, z));
            var canopy = Prim(GeometricPrimitive.Sphere.New(GraphicsDevice, 1.7f),
                new Color4(0.13f, 0.45f, 0.16f, 1f), new Vector3(x, y + 3.6f, z));
            scene.Entities.Add(trunk);
            scene.Entities.Add(canopy);
        }

        private void AddBuilding(Scene scene, float x, float z, Vector3 size)
        {
            float y = HeightAt(x, z) + size.Y / 2f - 0.3f; // sink slightly into slope
            var b = Prim(GeometricPrimitive.Cube.New(GraphicsDevice, size),
                new Color4(0.55f, 0.55f, 0.58f, 1f), new Vector3(x, y, z));
            scene.Entities.Add(b);
        }
    }
}
