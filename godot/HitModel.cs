using Godot;
using System.Collections.Generic;

// Shared hit model for every weapon in the duel (hitscan and projectiles
// alike), so a laser, an autocannon shell and a rocket all agree on what
// they hit:
//   - mechs   = vertical cylinders (duel collision radius, full height
//               standing on the terrain under the root) with legs/torso/
//               head zones by height fraction -- the zone is what the
//               breakable-parts step will consume later.
//   - props   = vertical cylinders (circle + base height + height) for
//               trees and buildings; buildings block shots.
//   - terrain = segment sampling against Main.HeightAt every <= 0.4u,
//               bisected once for a decent contact point.
// Rays are unit-direction + max distance; projectiles call the same entry
// with their per-step segment length, which makes the test swept (no
// tunneling at 60 u/s).
public sealed class HitModel
{
    public struct Result
    {
        public bool Hit;
        public float T;
        public Vector3 Point;
        public string Target;   // mech id ("TW"/"ATLAS"), "PROP", "TERRAIN", "" = nothing
        public string Zone;     // mech hits only: "legs" / "torso" / "head"
        public float ZoneFrac;  // mech hits only: 0 at the sole, 1 at the top
        public string Part;     // mech hits only: breakable part id (via PartResolver), "" if unset
    }

    // Optional breakable-parts hook: (mech id, world hit point) -> part id.
    // Set by the duel once the part registries exist; null in other scenes.
    public System.Func<string, Vector3, string> PartResolver;

    public sealed class PropCyl
    {
        public Vector2 P;
        public float R;
        public float BaseY;
        public float Height;
    }

    public sealed class MechCap
    {
        public string Id;
        public System.Func<Vector3> Root; // live root position provider
        public float Radius;
        public float Height;
    }

    public readonly List<PropCyl> Props = new();
    public readonly List<MechCap> Mechs = new();

    public Result Ray(Vector3 from, Vector3 dir, float maxDist, string ignoreId)
    {
        dir = dir.Normalized();
        var best = new Result { Hit = false, T = maxDist, Target = "", Zone = "", ZoneFrac = 0f, Part = "" };
        float tt = RayTerrain(from, dir, maxDist);
        if (tt >= 0f) { best.Hit = true; best.T = tt; best.Target = "TERRAIN"; }
        foreach (var p in Props)
        {
            float t = RayCylinder(from, dir, p.P, p.R, p.BaseY, p.Height, best.T);
            if (t >= 0f) { best.Hit = true; best.T = t; best.Target = "PROP"; }
        }
        foreach (var m in Mechs)
        {
            if (m.Id == ignoreId) continue;
            var root = m.Root();
            float baseY = Main.HeightAt(root.X, root.Z);
            float t = RayCylinder(from, dir, new Vector2(root.X, root.Z), m.Radius, baseY, m.Height, best.T);
            if (t >= 0f)
            {
                best.Hit = true; best.T = t; best.Target = m.Id;
                float y = from.Y + dir.Y * t;
                best.ZoneFrac = Mathf.Clamp((y - baseY) / m.Height, 0f, 1f);
                best.Zone = best.ZoneFrac < 0.42f ? "legs" : best.ZoneFrac < 0.86f ? "torso" : "head";
                best.Part = PartResolver != null
                    ? PartResolver(m.Id, from + dir * t) : "";
            }
        }
        if (best.Hit) best.Point = from + dir * best.T;
        else best.Point = from + dir * maxDist;
        return best;
    }

    // Ray vs a vertical cylinder (flat top cap included). maxT rejects
    // anything farther than the current best hit. Returns t or -1.
    private static float RayCylinder(Vector3 from, Vector3 dir, Vector2 c, float r,
        float baseY, float height, float maxT)
    {
        float topY = baseY + height;
        float best = -1f;
        float ox = from.X - c.X, oz = from.Z - c.Y;
        float a = dir.X * dir.X + dir.Z * dir.Z;
        if (a > 1e-8f)
        {
            float b = ox * dir.X + oz * dir.Z;
            float cc = ox * ox + oz * oz - r * r;
            float disc = b * b - a * cc;
            if (disc >= 0f)
            {
                float t = (-b - Mathf.Sqrt(disc)) / a;
                if (t < 0f && cc < 0f) t = 0f; // origin inside the circle
                if (t >= 0f && t <= maxT)
                {
                    float y = from.Y + dir.Y * t;
                    if (y >= baseY - 0.001f && y <= topY + 0.001f) best = t;
                }
            }
        }
        // Descending onto the flat top disc.
        if (dir.Y < -1e-6f)
        {
            float t = (topY - from.Y) / dir.Y;
            if (t >= 0f && t <= maxT && (best < 0f || t < best))
            {
                float px = from.X + dir.X * t - c.X, pz = from.Z + dir.Z * t - c.Y;
                if (px * px + pz * pz <= r * r) best = t;
            }
        }
        return best;
    }

    private static float RayTerrain(Vector3 from, Vector3 dir, float maxDist)
    {
        const float step = 0.4f;
        float prevT = 0f;
        if (from.Y <= Main.HeightAt(from.X, from.Z)) return 0f;
        for (float t = step; t <= maxDist; t += step)
        {
            var p = from + dir * t;
            if (p.Y <= Main.HeightAt(p.X, p.Z))
            {
                float lo = prevT, hi = t;
                for (int i = 0; i < 7; i++)
                {
                    float mid = (lo + hi) * 0.5f;
                    var mp = from + dir * mid;
                    if (mp.Y <= Main.HeightAt(mp.X, mp.Z)) hi = mid; else lo = mid;
                }
                return hi;
            }
            prevT = t;
        }
        return -1f;
    }

    // Distance from a point to a mech's capsule surface (0 when touching):
    // point-to-axis-segment distance minus the radius. Used for splash.
    public float DistanceToMechSurface(Vector3 point, string id)
    {
        foreach (var m in Mechs)
        {
            if (m.Id != id) continue;
            var root = m.Root();
            float baseY = Main.HeightAt(root.X, root.Z);
            var a = new Vector3(root.X, baseY, root.Z);
            var b = new Vector3(root.X, baseY + m.Height, root.Z);
            var ab = b - a;
            float tt = ab.LengthSquared() > 1e-8f
                ? Mathf.Clamp((point - a).Dot(ab) / ab.LengthSquared(), 0f, 1f) : 0f;
            var closest = a + ab * tt;
            return Mathf.Max(point.DistanceTo(closest) - m.Radius, 0f);
        }
        return float.MaxValue;
    }
}
