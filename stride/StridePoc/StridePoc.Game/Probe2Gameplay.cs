using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Extensions;
using Stride.Graphics;
using Stride.Graphics.Font;
using Stride.Graphics.GeometricPrimitives;
using Stride.Physics;
using Stride.Rendering;
using Stride.Rendering.Materials;
using Stride.Rendering.Materials.ComputeColors;
using Stride.Rendering.Sprites;
using Stride.UI;
using Stride.UI.Controls;
using Stride.UI.Panels;

namespace StridePoc
{
    /// <summary>
    /// Round 2 / Probe 2 (Stride): gameplay loop on REAL physics.
    /// World = round-1 terrain (WorldBuilder.HeightAt, 100x100) + the 2 round-1
    /// buildings, now with colliders. The terrain collider is the probe's core
    /// question: Stride's heightfield collider has NO code-first path (Phase A
    /// finding), so the collider is a StaticMeshColliderShape built in code
    /// from the height function at REDUCED resolution (50x50 segments = 5,000
    /// tris) vs the visual mesh (100x100 segments = 20,000 tris).
    /// Player + 3 enemies are CharacterComponents (Bullet kinematic character
    /// controllers) with manual gravity fed through Move(); enemies steer to
    /// the player and stop at a standoff; the player is script-driven
    /// (settle -> building-block test -> hunt), aims the follow camera at the
    /// current target and fires Simulation.Raycast from the camera centre.
    /// HUD is engine-native Stride UI created in code (UIPage + Canvas):
    /// crosshair = two Borders; counter = TextBlock on a runtime dynamic
    /// font (FontSystem + a system TTF), falling back to a texture-baked
    /// label (ImageElement + SpriteFromTexture) if the font route fails.
    /// </summary>
    public static class Probe2World
    {
        public const int VisualSegs = 100;
        public const int ColliderSegs = 50;
        public static long ColliderBuildMs;
        public static int ColliderTris;
        public static string HudRoute = "none";

        // Round-1 buildings: (x, z, size)
        public static readonly (float X, float Z, Vector3 Size)[] Buildings =
        {
            (-20f, 14f, new Vector3(7f, 5f, 6f)),
            (22f, 8f, new Vector3(6f, 7f, 6f)),
        };

        public static void Build(StartupScript host, Scene scene)
        {
            var gd = host.GraphicsDevice;

            // ---- Terrain: visual mesh (full res) + mesh collider (reduced res)
            var terrain = BuildTerrainEntity(host, gd, VisualSegs, out var visualTris);
            scene.Entities.Add(terrain);

            var sw = Stopwatch.StartNew();
            BuildHeightGrid(ColliderSegs, out var cVerts, out var cIndices);
            var meshShape = new StaticMeshColliderShape(cVerts, cIndices);
            sw.Stop();
            ColliderBuildMs = sw.ElapsedMilliseconds;
            ColliderTris = cIndices.Length / 3;
            var terrainCollider = new StaticColliderComponent { Tag = "terrain" };
            terrainCollider.ColliderShape = meshShape;
            terrain.Add(terrainCollider);
            Console.WriteLine($"PROBE2 terrain visualTris={visualTris} colliderTris={ColliderTris} colliderVerts={cVerts.Length} colliderBuildMs={ColliderBuildMs} (visual {VisualSegs}x{VisualSegs} segs vs collider {ColliderSegs}x{ColliderSegs} segs, 100x100 world)");

            // Scale reference: time a FULL Probe-3-resolution shape (1000x1000
            // HeightBig at 250 segs = 125,000 tris) without attaching it.
            sw.Restart();
            BuildHeightGrid(250, 1000f, (x, z) => ScaleWorldBuilder.HeightBig(x, z), out var bVerts, out var bIndices);
            var bigShape = new StaticMeshColliderShape(bVerts, bIndices);
            sw.Stop();
            Console.WriteLine($"PROBE2 collider-scale-test probe3Terrain(250segs,125000tris) buildMs={sw.ElapsedMilliseconds} verts={bVerts.Length}");
            bigShape.Dispose();

            // ---- Buildings: visual cube + box collider
            foreach (var (bx, bz, size) in Buildings)
            {
                float y = WorldBuilder.HeightAt(bx, bz) + size.Y / 2f - 0.3f; // round-1 sink
                var b = Prim(host, gd, GeometricPrimitive.Cube.New(gd, size),
                    new Color4(0.55f, 0.55f, 0.58f, 1f), new Vector3(bx, y, bz));
                b.Name = "Building";
                var sc = new StaticColliderComponent { Tag = "building" };
                sc.ColliderShapes.Add(new BoxColliderShapeDesc { Size = size });
                b.Add(sc);
                scene.Entities.Add(b);
            }

            // ---- HUD (built before actors so the player script can update it)
            var hud = Probe2Hud.Build(host, scene);

            // ---- Player
            var player = Prim(host, gd, GeometricPrimitive.Capsule.New(gd, 1.0f, 0.4f),
                new Color4(0.15f, 0.35f, 0.9f, 1f),
                new Vector3(0f, WorldBuilder.HeightAt(0f, 8f) + 0.9f + 0.6f, 8f)); // +0.6: drops in, gravity settle is logged
            player.Name = "Player";
            var playerChar = MakeCharacter();
            player.Add(playerChar);
            scene.Entities.Add(player);

            // ---- Enemies: ONE runtime template, instantiated x3
            var spawns = new[] { new Vector3(-26f, 0f, -2f), new Vector3(8f, 0f, -20f), new Vector3(28f, 0f, 2f) };
            var enemies = new List<Probe2Enemy>();
            for (int i = 0; i < spawns.Length; i++)
            {
                var e = CreateEnemy(host, gd, spawns[i], player, i + 1);
                scene.Entities.Add(e);
                enemies.Add(e.Get<Probe2Enemy>());
            }

            // ---- Camera + player driver
            var camera = scene.Entities.FirstOrDefault(e => e.Name == "Camera");
            var driver = new Probe2Player
            {
                Char = playerChar,
                CameraEntity = camera,
                Enemies = enemies,
                Hud = hud,
            };
            player.Add(driver);
            Console.WriteLine("PROBE2 world built: player + 3 enemies (template x3), HUD route=" + HudRoute);
        }

        public static CharacterComponent MakeCharacter()
        {
            var c = new CharacterComponent
            {
                Gravity = Vector3.Zero, // gravity is fed manually via Move() (see Probe2Actor)
                FallSpeed = 60f,
                JumpSpeed = 0f,
                StepHeight = 0.45f,
                MaxSlope = new AngleSingle(65f, AngleType.Degree),
            };
            c.ColliderShapes.Add(new CapsuleColliderShapeDesc { Radius = 0.4f, Length = 1.0f });
            return c;
        }

        /// <summary>The single enemy definition: red capsule + character + brain.</summary>
        private static Entity CreateEnemy(StartupScript host, GraphicsDevice gd, Vector3 xz, Entity player, int id)
        {
            float y = WorldBuilder.HeightAt(xz.X, xz.Z) + 0.9f + 0.4f;
            var e = Prim(host, gd, GeometricPrimitive.Capsule.New(gd, 1.0f, 0.4f),
                new Color4(0.85f, 0.15f, 0.15f, 1f), new Vector3(xz.X, y, xz.Z));
            e.Name = "Enemy" + id;
            var ch = MakeCharacter();
            e.Add(ch);
            e.Add(new Probe2Enemy { Id = id, Char = ch, Player = player });
            return e;
        }

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

        private static Entity Prim(StartupScript host, GraphicsDevice gd, GeometricPrimitive prim, Color4 color, Vector3 pos)
        {
            var model = new Model();
            model.Meshes.Add(new Mesh { Draw = prim.ToMeshDraw(), MaterialIndex = 0 });
            model.Materials.Add(Colored(host, gd, color));
            var e = new Entity();
            e.Add(new ModelComponent { Model = model });
            e.Transform.Position = pos;
            return e;
        }

        private static Entity BuildTerrainEntity(StartupScript host, GraphicsDevice gd, int segs, out int tris)
        {
            BuildHeightGrid(segs, 100f, (x, z) => WorldBuilder.HeightAt(x, z), out var pos, out var idx);
            var verts = new VertexPositionNormalColor[pos.Length];
            var green = new Color(0.25f, 0.5f, 0.22f, 1);
            for (int n = 0; n < pos.Length; n++)
            {
                float x = pos[n].X, z = pos[n].Z;
                float hx = WorldBuilder.HeightAt(x + 0.5f, z) - WorldBuilder.HeightAt(x - 0.5f, z);
                float hz = WorldBuilder.HeightAt(x, z + 0.5f) - WorldBuilder.HeightAt(x, z - 0.5f);
                var nrm = new Vector3(-hx, 1f, -hz);
                nrm.Normalize();
                verts[n] = new VertexPositionNormalColor(pos[n], nrm, green);
            }
            tris = idx.Length / 3;
            var mesh = new Mesh
            {
                Draw = new MeshDraw
                {
                    PrimitiveType = PrimitiveType.TriangleList,
                    DrawCount = idx.Length,
                    VertexBuffers = new[]
                    {
                        new VertexBufferBinding(
                            Stride.Graphics.Buffer.Vertex.New(gd, verts, GraphicsResourceUsage.Default),
                            VertexPositionNormalColor.Layout, verts.Length)
                    },
                    IndexBuffer = new IndexBufferBinding(
                        Stride.Graphics.Buffer.Index.New(gd, idx, GraphicsResourceUsage.Default), true, idx.Length)
                },
                MaterialIndex = 0
            };
            var model = new Model();
            model.Meshes.Add(mesh);
            model.Materials.Add(Colored(host, gd, new Color4(0.25f, 0.5f, 0.22f, 1f)));
            var e = new Entity("Terrain");
            e.Add(new ModelComponent { Model = model });
            return e;
        }

        /// <summary>Height grid over [-size/2, size/2]^2 sampled from a height function.</summary>
        public static void BuildHeightGrid(int segs, float size, Func<float, float, float> height,
            out Vector3[] verts, out int[] indices)
        {
            float half = size / 2f, step = size / segs;
            verts = new Vector3[(segs + 1) * (segs + 1)];
            for (int j = 0; j <= segs; j++)
                for (int i = 0; i <= segs; i++)
                {
                    float x = -half + i * step, z = -half + j * step;
                    verts[j * (segs + 1) + i] = new Vector3(x, height(x, z), z);
                }
            indices = new int[segs * segs * 6];
            int k = 0;
            for (int j = 0; j < segs; j++)
                for (int i = 0; i < segs; i++)
                {
                    int a = j * (segs + 1) + i, b = a + 1, c = a + segs + 1, d = c + 1;
                    indices[k++] = a; indices[k++] = b; indices[k++] = c;
                    indices[k++] = b; indices[k++] = d; indices[k++] = c;
                }
        }

        private static void BuildHeightGrid(int segs, out Vector3[] verts, out int[] indices)
            => BuildHeightGrid(segs, 100f, (x, z) => WorldBuilder.HeightAt(x, z), out verts, out indices);
    }

    /// <summary>Shared locomotion: manual gravity + sub-stepped CharacterComponent.Move.</summary>
    public abstract class Probe2Actor : SyncScript
    {
        public CharacterComponent Char;
        protected float Vy;
        protected const float GravityAccel = -30f;

        /// <summary>One frame of movement: horizontal wish velocity (units/s) + gravity.</summary>
        protected void ActorMove(Vector3 wishVel, float dt)
        {
            if (Char == null) return;
            Vy += GravityAccel * dt;
            if (Vy < -25f) Vy = -25f;
            if (Char.IsGrounded && Vy < -1f) Vy = -1f; // stick to ground
            var disp = new Vector3(wishVel.X * dt, Vy * dt, wishVel.Z * dt);
            int sub = Math.Max(1, (int)Math.Ceiling(dt / 0.025f));
            var step = disp / sub;
            for (int i = 0; i < sub; i++) Char.Move(step);
        }

        protected static float GroundY(float x, float z) => WorldBuilder.HeightAt(x, z) + 0.9f; // capsule centre when standing
    }

    /// <summary>Enemy brain: physics-driven (CharacterComponent) steering toward the player.</summary>
    public class Probe2Enemy : Probe2Actor
    {
        public int Id;
        public Entity Player;
        public bool Alive = true;
        public const float Speed = 3.4f;
        public const float Standoff = 3.5f;
        private float _logTimer;

        public float DistanceToPlayer()
        {
            if (Player == null) return float.MaxValue;
            var d = Player.Transform.Position - Entity.Transform.Position;
            d.Y = 0f;
            return d.Length();
        }

        public override void Update()
        {
            if (!Alive || Player == null) return;
            float dt = Math.Min((float)Game.UpdateTime.Elapsed.TotalSeconds, 0.5f);
            var to = Player.Transform.Position - Entity.Transform.Position;
            to.Y = 0f;
            float dist = to.Length();
            var wish = Vector3.Zero;
            if (dist > Standoff && dist > 0.001f)
            {
                to.Normalize();
                wish = to * Speed;
                Entity.Transform.Rotation = Quaternion.RotationYawPitchRoll(MathF.Atan2(to.X, to.Z), 0f, 0f);
            }
            ActorMove(wish, dt);
            _logTimer += dt;
            if (_logTimer >= 2f)
            {
                _logTimer = 0f;
                Console.WriteLine($"PROBE2 enemy{Id} pos={Entity.Transform.Position} distToPlayer={DistanceToPlayer():F1} grounded={Char?.IsGrounded}");
            }
        }

        public void Kill()
        {
            Alive = false;
            Entity.Scene?.Entities.Remove(Entity);
        }
    }

    /// <summary>Scripted player: settle -> building-block test -> hunt &amp; shoot -> done.</summary>
    public class Probe2Player : Probe2Actor
    {
        public Entity CameraEntity;
        public List<Probe2Enemy> Enemies;
        public Probe2Hud Hud;

        private enum Phase { Settle, Building, Hunt, Done }
        private Phase _phase = Phase.Settle;
        private float _t;            // total game time
        private float _phaseT;
        private float _logTimer;
        private float _fireTimer;
        private float _phaseFileTimer;
        private string _phaseFile;
        private int _missStreak;
        private float _strafeT;
        private float _minBuildingDist = float.MaxValue;
        private bool _buildingViolated;
        private bool _colliderLogged;
        private Vector3 _facing = new Vector3(0f, 0f, -1f);
        public int Hits;

        private const float WalkSpeed = 6.5f;
        private const float ShootRange = 10f;
        private const float GlobalTimeout = 100f;

        public override void Start()
        {
            var arg = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--phase-file="));
            _phaseFile = arg?.Substring("--phase-file=".Length);
            Console.WriteLine($"PROBE2 player start pos={Entity.Transform.Position} groundY={GroundY(0f, 8f):F2}");
        }

        public override void Update()
        {
            float dt = Math.Min((float)Game.UpdateTime.Elapsed.TotalSeconds, 0.5f);
            if (dt <= 0f) dt = 0.016f;
            _t += dt;
            _phaseT += dt;
            _logTimer += dt;
            _phaseFileTimer += dt;

            if (!_colliderLogged && _t > 0.3f)
            {
                _colliderLogged = true;
                LogColliderShapes();
            }

            var wish = Vector3.Zero;
            Probe2Enemy target = NearestAlive();

            switch (_phase)
            {
                case Phase.Settle:
                    if (_phaseT >= 1.2f) Goto(Phase.Building);
                    break;

                case Phase.Building:
                {
                    var (bx, bz, size) = Probe2World.Buildings[0];
                    float dAabb = DistToAabb(Entity.Transform.Position, bx, bz, size);
                    _minBuildingDist = Math.Min(_minBuildingDist, dAabb);
                    if (InsideAabb(Entity.Transform.Position, bx, bz, size, 0.30f)) _buildingViolated = true;
                    var to = new Vector3(bx - Entity.Transform.Position.X, 0f, bz - Entity.Transform.Position.Z);
                    if (to.LengthSquared() > 0.01f) { to.Normalize(); wish = to * WalkSpeed; Face(to); }
                    if ((dAabb <= 0.55f && _phaseT > 1.5f) || _phaseT >= 7f)
                    {
                        Console.WriteLine($"PROBE2 BUILDING-TEST result minDistToAabb={_minBuildingDist:F2} violated={_buildingViolated} phaseT={_phaseT:F1} pos={Entity.Transform.Position}");
                        Goto(Phase.Hunt);
                    }
                    break;
                }

                case Phase.Hunt:
                {
                    if (target == null) { Goto(Phase.Done); break; }
                    var tp = target.Entity.Transform.Position;
                    var to = new Vector3(tp.X - Entity.Transform.Position.X, 0f, tp.Z - Entity.Transform.Position.Z);
                    float dist = to.Length();
                    if (to.LengthSquared() > 0.01f) Face(to / Math.Max(dist, 0.001f));
                    if (_strafeT > 0f)
                    {
                        _strafeT -= dt;
                        var perp = new Vector3(-_facing.Z, 0f, _facing.X);
                        wish = perp * WalkSpeed * 0.7f;
                    }
                    else if (dist > ShootRange)
                    {
                        wish = (to / Math.Max(dist, 0.001f)) * WalkSpeed;
                    }
                    else
                    {
                        _fireTimer += dt;
                        if (_fireTimer >= 0.5f)
                        {
                            _fireTimer = 0f;
                            Fire(target);
                        }
                    }
                    break;
                }

                case Phase.Done:
                    break;
            }

            if (_phase != Phase.Done)
                ActorMove(wish, dt);

            UpdateCamera(target);
            if (target != null && _phase == Phase.Hunt) Face(target.Entity.Transform.Position - Entity.Transform.Position);

            if (_logTimer >= 1f)
            {
                _logTimer = 0f;
                var dists = string.Join(" ", Enemies.Select(e => $"E{e.Id}:{(e.Alive ? e.DistanceToPlayer().ToString("F1") : "dead")}"));
                var p = Entity.Transform.Position;
                Console.WriteLine($"PROBE2 t={_t:F1} phase={_phase} pos={p} grounded={Char?.IsGrounded} vy={Vy:F1} groundErr={p.Y - GroundY(p.X, p.Z):F2} hits={Hits} dists=[{dists}]");
            }
            if (_phaseFileTimer >= 0.2f)
            {
                _phaseFileTimer = 0f;
                // Keep reporting hits{N} for the first 1.2s of Done so the
                // capture harness can grab a settled final-counter frame
                // before the "done" marker (present-pipeline lag made an
                // instant hits3 capture show the stale 2/3 label).
                WritePhase(_phase == Phase.Done && _phaseT > 1.2f ? "done" : $"hits{Hits}");
            }
            if ((_phase == Phase.Done && _phaseT > 1.5f) || _t >= GlobalTimeout)
            {
                WritePhase("done");
                Console.WriteLine($"PROBE2 FINAL hits={Hits}/3 time={_t:F1} buildingViolated={_buildingViolated} minBuildingDist={_minBuildingDist:F2}");
                Environment.Exit(Hits == 3 ? 0 : 2);
            }
        }

        private void Goto(Phase p)
        {
            _phase = p;
            _phaseT = 0f;
            Console.WriteLine($"PROBE2 phase -> {p} at t={_t:F1}");
        }

        private Probe2Enemy NearestAlive()
        {
            Probe2Enemy best = null;
            float bd = float.MaxValue;
            foreach (var e in Enemies)
            {
                if (!e.Alive) continue;
                float d = e.DistanceToPlayer();
                if (d < bd) { bd = d; best = e; }
            }
            return best;
        }

        private void Face(Vector3 dir)
        {
            dir.Y = 0f;
            if (dir.LengthSquared() < 0.001f) return;
            dir.Normalize();
            _facing = dir;
            Entity.Transform.Rotation = Quaternion.RotationYawPitchRoll(MathF.Atan2(dir.X, dir.Z), 0f, 0f);
        }

        private void UpdateCamera(Probe2Enemy aimTarget)
        {
            if (CameraEntity == null) return;
            var p = Entity.Transform.Position;
            Vector3 camPos, lookAt;
            if (_phase == Phase.Hunt && aimTarget != null)
            {
                camPos = p - _facing * 7f + new Vector3(0f, 3.2f, 0f);
                var ep = aimTarget.Entity.Transform.Position;
                lookAt = new Vector3(ep.X, ep.Y + 0.1f, ep.Z); // enemy chest (centre +0.1)
            }
            else
            {
                camPos = p - _facing * 7.5f + new Vector3(0f, 2.6f, 0f);
                lookAt = p + _facing * 10f;
            }
            CameraEntity.Transform.Position = camPos;
            var f = lookAt - camPos;
            f.Normalize();
            float yaw = MathF.Atan2(-f.X, -f.Z);
            float pitch = MathF.Asin(Math.Clamp(f.Y, -1f, 1f));
            CameraEntity.Transform.Rotation = Quaternion.RotationYawPitchRoll(yaw, pitch, 0f);
        }

        private void Fire(Probe2Enemy target)
        {
            var sim = this.GetSimulation();
            if (sim == null || CameraEntity == null) { Console.WriteLine("PROBE2 FIRE failed: no simulation/camera"); return; }
            var camPos = CameraEntity.Transform.Position;
            var ep = target.Entity.Transform.Position + new Vector3(0f, 0.1f, 0f);
            var dir = ep - camPos;
            float dist = dir.Length();
            dir /= Math.Max(dist, 0.001f);
            // Ray from the camera centre along its forward (= dir by construction
            // of UpdateCamera). Skip past the player's own capsule if it intervenes.
            var from = camPos + dir * 0.3f;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                var hit = sim.Raycast(from, camPos + dir * (dist + 6f));
                if (!hit.Succeeded)
                {
                    Console.WriteLine($"PROBE2 FIRE target=E{target.Id} MISS (no hit) dist={dist:F1}");
                    RegisterMiss();
                    return;
                }
                var hitEntity = hit.Collider?.Entity;
                if (hitEntity == Entity) // own capsule: step past it and re-cast
                {
                    from = hit.Point + dir * 0.7f;
                    continue;
                }
                if (hitEntity == target.Entity)
                {
                    Hits++;
                    Hud?.SetHits(Hits);
                    Console.WriteLine($"PROBE2 HIT #{Hits} enemy=E{target.Id} dist={dist:F1} point={hit.Point} hitsNow={Hits}/3");
                    target.Kill();
                    _missStreak = 0;
                    return;
                }
                Console.WriteLine($"PROBE2 FIRE target=E{target.Id} BLOCKED by={(hitEntity?.Name ?? "?")} tag={hit.Collider?.Tag} dist={dist:F1} hitDist={(hit.Point - camPos).Length():F1}");
                RegisterMiss();
                return;
            }
            Console.WriteLine($"PROBE2 FIRE target=E{target.Id} MISS (self-skip exhausted)");
            RegisterMiss();
        }

        private void RegisterMiss()
        {
            _missStreak++;
            if (_missStreak >= 6)
            {
                _missStreak = 0;
                _strafeT = 1.2f; // sidestep to change the line of sight
                Console.WriteLine("PROBE2 strafe (repeated blocked shots)");
            }
        }

        private void LogColliderShapes()
        {
            try
            {
                var terrain = Entity.Scene.Entities.FirstOrDefault(e => e.Name == "Terrain");
                var tc = terrain?.Get<StaticColliderComponent>();
                var shape = tc?.ColliderShape;
                string info = shape == null ? "null" : shape.GetType().Name;
                if (shape is StaticMeshColliderShape sm)
                {
                    sm.GetMeshDataCopy(out var v, out var idx);
                    info += $" verts={v?.Length} tris={(idx == null ? 0 : idx.Length / 3)}";
                }
                var cc = Char?.ColliderShape;
                Console.WriteLine($"PROBE2 collider-check terrainShape={info} playerShape={(cc == null ? "null" : cc.GetType().Name)} grounded={Char?.IsGrounded} pos={Entity.Transform.Position}");
            }
            catch (Exception ex)
            {
                Console.WriteLine("PROBE2 collider-check failed: " + ex.Message);
            }
        }

        private static float DistToAabb(Vector3 p, float cx, float cz, Vector3 size)
        {
            float dx = Math.Max(Math.Abs(p.X - cx) - size.X / 2f, 0f);
            float dz = Math.Max(Math.Abs(p.Z - cz) - size.Z / 2f, 0f);
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        private static bool InsideAabb(Vector3 p, float cx, float cz, Vector3 size, float expand)
        {
            return Math.Abs(p.X - cx) < size.X / 2f + expand && Math.Abs(p.Z - cz) < size.Z / 2f + expand;
        }

        private void WritePhase(string phase)
        {
            if (_phaseFile == null) return;
            try { File.WriteAllText(_phaseFile, $"{phase} {_t:F2}"); } catch { /* harness poll */ }
        }
    }

    /// <summary>
    /// Code-created Stride UI: UIPage + Canvas root, crosshair from two
    /// Border elements, counter label. Font route: FontSystem runtime
    /// dynamic font from a system TTF; fallback: texture-baked label.
    /// </summary>
    public class Probe2Hud
    {
        private TextBlock _label;
        private ImageElement _labelImage;
        private GraphicsDevice _gd;
        private int _hits = -1;
        public const int Total = 3;

        public static Probe2Hud Build(StartupScript host, Scene scene)
        {
            var hud = new Probe2Hud { _gd = host.GraphicsDevice };
            var canvas = new Canvas();

            // Counter label (top-left) + crosshair (centre). Font first,
            // because the crosshair rides the same route as the label:
            // attempt 1 used two Border bars for the crosshair and they never
            // rendered (0 bright pixels outside the label in the proof frame —
            // a content-less Border in a Canvas measures 0 and its
            // BackgroundColor is not drawn), so the crosshair is a TextBlock
            // "+" glyph on the proven text path (texture image in fallback).
            SpriteFont font = null;
            string route = "texture-fallback";
            try
            {
                var fs = host.Game.Services.GetService<FontSystem>();
                if (fs != null)
                {
                    if (!fs.RuntimeFonts.IsRegistered("PocSans"))
                        fs.RuntimeFonts.RegisterFont("PocSans", "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", FontStyle.Regular);
                    font = fs.LoadRuntimeFont("PocSans", 40f, FontStyle.Regular);
                    var measured = font?.MeasureString("Hits: 0/3") ?? Vector2.Zero;
                    Console.WriteLine($"PROBE2 HUD font: LoadRuntimeFont -> {(font == null ? "null" : "ok")} measured={measured}");
                    if (font == null || measured.X <= 0f) { font = null; }
                    else route = "dynamic-font TextBlock";
                }
                else Console.WriteLine("PROBE2 HUD font: FontSystem service is null");
            }
            catch (Exception ex)
            {
                Console.WriteLine("PROBE2 HUD font route failed: " + ex.GetType().Name + ": " + ex.Message);
                font = null;
            }
            Probe2World.HudRoute = route;

            // Crosshair at canvas centre.
            if (font != null)
            {
                var cross = new TextBlock
                {
                    Text = "+",
                    Font = font,
                    TextSize = 44f,
                    TextColor = Color.White,
                    CanBeHitByUser = false,
                };
                cross.DependencyProperties.Set(Canvas.UseAbsolutePositionPropertyKey, true);
                cross.DependencyProperties.Set(Canvas.AbsolutePositionPropertyKey, new Vector3(640f, 360f, 0f));
                cross.DependencyProperties.Set(Canvas.PinOriginPropertyKey, new Vector3(0.5f, 0.5f, 0f));
                canvas.Children.Add(cross);
            }
            else
            {
                var crossImg = new ImageElement
                {
                    Width = 48f, Height = 48f,
                    StretchType = StretchType.Fill,
                    CanBeHitByUser = false,
                    Source = new SpriteFromTexture { Texture = MakeCrossTexture(host.GraphicsDevice) },
                };
                crossImg.DependencyProperties.Set(Canvas.UseAbsolutePositionPropertyKey, true);
                crossImg.DependencyProperties.Set(Canvas.AbsolutePositionPropertyKey, new Vector3(640f, 360f, 0f));
                crossImg.DependencyProperties.Set(Canvas.PinOriginPropertyKey, new Vector3(0.5f, 0.5f, 0f));
                canvas.Children.Add(crossImg);
            }

            if (font != null)
            {
                hud._label = new TextBlock
                {
                    Text = "Hits: 0/3",
                    Font = font,
                    TextSize = 40f,
                    TextColor = Color.White,
                    CanBeHitByUser = false,
                };
                hud._label.DependencyProperties.Set(Canvas.UseAbsolutePositionPropertyKey, true);
                hud._label.DependencyProperties.Set(Canvas.AbsolutePositionPropertyKey, new Vector3(24f, 14f, 0f));
                hud._label.DependencyProperties.Set(Canvas.PinOriginPropertyKey, new Vector3(0f, 0f, 0f));
                canvas.Children.Add(hud._label);
            }
            else
            {
                hud._labelImage = new ImageElement
                {
                    Width = 300f, Height = 64f,
                    StretchType = StretchType.Fill,
                    CanBeHitByUser = false,
                };
                hud._labelImage.DependencyProperties.Set(Canvas.UseAbsolutePositionPropertyKey, true);
                hud._labelImage.DependencyProperties.Set(Canvas.AbsolutePositionPropertyKey, new Vector3(24f, 14f, 0f));
                hud._labelImage.DependencyProperties.Set(Canvas.PinOriginPropertyKey, new Vector3(0f, 0f, 0f));
                canvas.Children.Add(hud._labelImage);
                hud.RedrawTextureLabel();
            }

            var uiEntity = new Entity("HUD");
            uiEntity.Add(new UIComponent
            {
                Page = new UIPage { RootElement = canvas },
                IsFullScreen = true,
                Resolution = new Vector3(1280f, 720f, 100f),
            });
            scene.Entities.Add(uiEntity);
            hud.SetHits(0);
            return hud;
        }

        public void SetHits(int hits)
        {
            if (hits == _hits) return;
            _hits = hits;
            if (_label != null) _label.Text = $"Hits: {hits}/{Total}";
            else RedrawTextureLabel();
        }

        private static Texture MakeCrossTexture(GraphicsDevice gd)
        {
            const int S = 48, C = S / 2;
            var data = new Color[S * S];
            for (int i = 0; i < data.Length; i++) data[i] = new Color(0, 0, 0, 0);
            for (int i = C - 13; i <= C + 13; i++)
            {
                data[C * S + i] = Color.White;
                data[C * S + i + S] = Color.White;
                data[i * S + C] = Color.White;
                data[i * S + C + 1] = Color.White;
            }
            return Texture.New2D(gd, S, S, PixelFormat.R8G8B8A8_UNorm, data, TextureFlags.ShaderResource, GraphicsResourceUsage.Default);
        }

        // --- Texture fallback: tiny 3x5 bitmap font baked into a texture. ---
        private static readonly Dictionary<char, string[]> Glyphs = new Dictionary<char, string[]>
        {
            ['H'] = new[] { "101", "101", "111", "101", "101" },
            ['I'] = new[] { "111", "010", "010", "010", "111" },
            ['T'] = new[] { "111", "010", "010", "010", "010" },
            ['S'] = new[] { "111", "100", "111", "001", "111" },
            [':'] = new[] { "000", "010", "000", "010", "000" },
            ['/'] = new[] { "001", "001", "010", "100", "100" },
            [' '] = new[] { "000", "000", "000", "000", "000" },
            ['0'] = new[] { "111", "101", "101", "101", "111" },
            ['1'] = new[] { "010", "010", "010", "010", "010" },
            ['2'] = new[] { "111", "001", "111", "100", "111" },
            ['3'] = new[] { "111", "001", "111", "001", "111" },
        };

        private void RedrawTextureLabel()
        {
            if (_labelImage == null || _gd == null) return;
            string text = $"HITS: {Math.Max(_hits, 0)}/{Total}";
            const int scale = 6, gw = 3, gh = 5, spacing = 1;
            int w = text.Length * (gw + spacing) * scale + 8;
            int h = gh * scale + 8;
            var data = new Color[w * h];
            for (int i = 0; i < data.Length; i++) data[i] = new Color(0, 0, 0, 160);
            for (int ci = 0; ci < text.Length; ci++)
            {
                if (!Glyphs.TryGetValue(text[ci], out var rows)) continue;
                for (int y = 0; y < gh; y++)
                    for (int x = 0; x < gw; x++)
                    {
                        if (rows[y][x] != '1') continue;
                        for (int sy = 0; sy < scale; sy++)
                            for (int sx = 0; sx < scale; sx++)
                            {
                                int px = 4 + ci * (gw + spacing) * scale + x * scale + sx;
                                int py = 4 + y * scale + sy;
                                data[py * w + px] = Color.White;
                            }
                    }
            }
            var tex = Texture.New2D(_gd, w, h, PixelFormat.R8G8B8A8_UNorm, data, TextureFlags.ShaderResource, GraphicsResourceUsage.Default);
            _labelImage.Source = new SpriteFromTexture { Texture = tex };
            _labelImage.Width = w;
            _labelImage.Height = h;
        }
    }
}
