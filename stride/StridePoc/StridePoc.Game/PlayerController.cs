using System;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Input;

namespace StridePoc
{
    /// <summary>
    /// WASD + Shift-run movement, camera-relative. Terrain following is done
    /// by height sampling: Stride's heightfield collider is designed around
    /// editor-baked heightmap assets, so from pure code sampling the same
    /// height function is far less code (the spec allows either).
    /// </summary>
    public class PlayerController : SyncScript
    {
        public bool TestMode;
        public Entity CameraEntity;

        private bool _started;
        private Vector3 _startPos;
        private float _elapsed;

        public override void Update()
        {
            if (!_started) { _startPos = Entity.Transform.Position; _started = true; }
            float dt = (float)Game.UpdateTime.Elapsed.TotalSeconds;
            _elapsed += dt;

            var input = Vector2.Zero;
            if (TestMode)
            {
                input = new Vector2(0f, 1f); // hold "W"
            }
            else
            {
                if (Input.IsKeyDown(Keys.W)) input.Y += 1f;
                if (Input.IsKeyDown(Keys.S)) input.Y -= 1f;
                if (Input.IsKeyDown(Keys.A)) input.X -= 1f;
                if (Input.IsKeyDown(Keys.D)) input.X += 1f;
            }
            bool run = TestMode || Input.IsKeyDown(Keys.LeftShift) || Input.IsKeyDown(Keys.RightShift);
            float speed = run ? 9.5f : 5f;

            // Camera-relative direction (derive yaw from the camera's orientation).
            float yaw = 0f;
            if (CameraEntity != null)
            {
                var cf = Vector3.Transform(new Vector3(0f, 0f, -1f), CameraEntity.Transform.Rotation);
                yaw = MathF.Atan2(-cf.X, -cf.Z);
            }
            var fwd = new Vector3(-MathF.Sin(yaw), 0f, -MathF.Cos(yaw));
            var right = new Vector3(MathF.Cos(yaw), 0f, -MathF.Sin(yaw));
            var dir = right * input.X + fwd * input.Y;
            if (dir.LengthSquared() > 1f) dir.Normalize();

            var pos = Entity.Transform.Position;
            pos += dir * speed * dt;
            pos.Y = WorldBuilder.HeightAt(pos.X, pos.Z) + 0.9f; // capsule center
            Entity.Transform.Position = pos;

            if (dir.LengthSquared() > 0.001f)
                Entity.Transform.Rotation = Quaternion.RotationYawPitchRoll(MathF.Atan2(dir.X, dir.Z), 0f, 0f);

            if (TestMode && _elapsed >= 3f)
            {
                var end = Entity.Transform.Position;
                Console.WriteLine($"SMOKE start={_startPos} end={end} moved={Vector3.Distance(_startPos, end):F2}");
                Environment.Exit(0); // IGame has no Exit() in 4.4; smoke harness only
            }
        }
    }
}
