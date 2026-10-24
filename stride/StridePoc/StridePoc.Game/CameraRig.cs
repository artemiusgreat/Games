using Stride.Core.Mathematics;
using Stride.Engine;

namespace StridePoc
{
    /// <summary>Third-person rig: sits behind (+Z) and above the target, fixed yaw, pitched down.</summary>
    public class CameraRig : SyncScript
    {
        public Entity Target;

        public override void Update()
        {
            if (Target == null) return;
            var p = Target.Transform.Position;
            Entity.Transform.Position = new Vector3(p.X, p.Y + 2.3f, p.Z + 7.5f);
            Entity.Transform.Rotation = Quaternion.RotationYawPitchRoll(0f, MathUtil.DegreesToRadians(-22f), 0f);
        }
    }
}
