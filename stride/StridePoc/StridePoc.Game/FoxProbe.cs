using System;
using System.IO;
using System.Linq;
using Stride.Animations;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Rendering;

namespace StridePoc
{
    /// <summary>
    /// Round 2 / Probe 1: animated asset import + playback.
    /// Loads the Khronos glTF "Fox" (imported directly from .gltf via Stride's
    /// Assimp-based ThreeDAssetImporter — see stride/NOTES.md), wires its
    /// Survey clip as the stationary "idle" and its Walk clip to forward
    /// auto-movement, code-driven through AnimationComponent crossfades.
    /// Fox has no true idle clip; Survey (standing, looking around) is the
    /// stationary clip. The driver writes its phase to a phase file so an
    /// external harness can capture rendered frames of each phase.
    /// </summary>
    public class FoxDriver : SyncScript
    {
        public AnimationComponent Anim;
        public Entity CameraEntity;
        private float _elapsed;
        private float _lastWrite;
        private bool _walking;
        private string _phaseFile;
        private float _poseYaw = float.NaN; // diagnostic: hold a fixed yaw, never walk

        public override void Start()
        {
            var arg = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--phase-file="));
            _phaseFile = arg?.Substring("--phase-file=".Length);
            var pose = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--fox-pose="));
            if (pose != null)
            {
                _poseYaw = MathUtil.DegreesToRadians(float.Parse(pose.Substring("--fox-pose=".Length)));
                Entity.Transform.Rotation = Quaternion.RotationYawPitchRoll(_poseYaw, 0f, 0f);
            }
            Anim.Play("Survey");
            WritePhase("idle");
            Console.WriteLine("FOXPROBE phase=idle clip=Survey");
        }

        public override void Update()
        {
            float dt = (float)Game.UpdateTime.Elapsed.TotalSeconds;
            if (dt > 0.25f) dt = 0.25f; // llvmpipe warmup spikes
            _elapsed += dt;

            if (float.IsNaN(_poseYaw) && !_walking && _elapsed >= 3.0f)
            {
                _walking = true;
                Anim.Crossfade("Walk", TimeSpan.FromSeconds(0.25));
                WritePhase("walk");
                Console.WriteLine($"FOXPROBE phase=walk clip=Walk t={_elapsed:F2}");
            }

            if (_walking)
            {
                // Walk diagonally (-X,-Z) so the follow camera sees the fox's
                // lit flank in 3/4 view: the scene's single directional light
                // leaves the side facing away from it near-black (same as the
                // round-1 terrain's back slopes — verified via pose captures).
                var dir = new Vector3(-0.55f, 0f, -0.835f);
                var pos = Entity.Transform.Position;
                pos += dir * 2.0f * dt;
                pos.Y = WorldBuilder.HeightAt(pos.X, pos.Z);
                Entity.Transform.Position = pos;
                Entity.Transform.Rotation = Quaternion.RotationYawPitchRoll(MathF.Atan2(dir.X, dir.Z), 0f, 0f);
            }

            if (CameraEntity != null && float.IsNaN(_poseYaw))
            {
                // Closer 3/4 follow cam for the proof frames (CameraRig's
                // straight-behind framing made the fox ~60 px tall).
                var p = Entity.Transform.Position;
                CameraEntity.Transform.Position = new Vector3(p.X + 4.6f, p.Y + 2.1f, p.Z + 7.0f);
                CameraEntity.Transform.Rotation = Quaternion.RotationYawPitchRoll(0f, MathUtil.DegreesToRadians(-20f), 0f);
            }

            if (_elapsed - _lastWrite > 0.25f) { _lastWrite = _elapsed; WritePhase(_walking ? "walk" : "idle"); }

            if (_elapsed >= 10.0f)
            {
                WritePhase("done");
                Console.WriteLine($"FOXPROBE done pos={Entity.Transform.Position}");
                Environment.Exit(0);
            }
        }

        private void WritePhase(string phase)
        {
            if (_phaseFile == null) return;
            try { File.WriteAllText(_phaseFile, $"{phase} {_elapsed:F2}"); } catch { /* harness poll */ }
        }
    }

    public static class FoxProbe
    {
        public static Entity Build(StartupScript host, Scene scene, Entity cameraEntity)
        {
            var model = host.Content.Load<Model>("Fox");
            var survey = host.Content.Load<AnimationClip>("Fox_Survey");
            var walk = host.Content.Load<AnimationClip>("Fox_Walk");
            Console.WriteLine($"FOXPROBE clips: Survey dur={survey.Duration} Walk dur={walk.Duration} model meshes={model.Meshes.Count} skeletonNodes={model.Skeleton?.Nodes?.Length}");

            var fox = new Entity("Fox");
            fox.Transform.Position = new Vector3(0f, WorldBuilder.HeightAt(0f, 8f), 8f);
            fox.Transform.Scale = new Vector3(0.012f); // glTF Fox is ~79 units tall -> ~0.95 m
            // Face the walk direction from the start (3/4 view of the lit flank).
            fox.Transform.Rotation = Quaternion.RotationYawPitchRoll(MathF.Atan2(-0.55f, -0.835f), 0f, 0f);
            fox.Add(new ModelComponent { Model = model });

            var anim = new AnimationComponent();
            anim.Animations.Add("Survey", survey);
            anim.Animations.Add("Walk", walk);
            fox.Add(anim);
            fox.Add(new FoxDriver { Anim = anim, CameraEntity = cameraEntity });
            scene.Entities.Add(fox);
            return fox;
        }
    }
}
