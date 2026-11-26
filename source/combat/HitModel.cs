using Godot;
using System.Collections.Generic;

// Shared hit model for every weapon in the duel (hitscan and projectiles
// alike), so a laser, an autocannon shell and a rocket all agree on what
// they hit:
//   - riders   = vertical cylinders (duel collision radius, full height
//               standing on the terrain under the root) with legs/torso/
//               head zones by height fraction -- the zone is what the
//               breakable-parts step will consume later.
//   - props   = vertical cylinders (circle + base height + height) for
//               trees.
//   - terrain = segment sampling against Main.HeightAt every
//               TerrainStep units, bisected for a decent contact point.
// Built surfaces (roofs, ramps, discs) are NOT hit-tested here any
// more (owner, 2026-11-25: buildings and platforms left the maps;
// Main still keeps those registries for walker ground height, but
// weapons no longer collide with them).
// Rays are unit-direction + max distance; projectiles call the same entry
// with their per-step segment length, which makes the test swept (no
// tunneling at 60 u/s).
public sealed class HitModel
{
    // Shared zero-guard for the quadratic / squared-length
    // tests (owner, 2026-11-25: one named constant for the
    // value, not a literal per site).
    private const float Epsilon = 1e-8f;
    // The ray must be genuinely descending before the flat
    // top-disc test applies.
    private const float DescentEpsilon = 1e-6f;
    // Slack on a cylinder's height band, so a hit exactly at
    // the sole or the cap still counts.
    private const float HeightTolerance = 0.001f;
    // Terrain sampling step and bisection refinement count.
    private const float TerrainStep = 0.4f;
    private const int TerrainRefineSteps = 7;
    // Rider hit zones by height fraction: below LegsZoneTop
    // is legs, below TorsoZoneTop is torso, above is head.
    private const float LegsZoneTop = 0.42f;
    private const float TorsoZoneTop = 0.86f;
    public struct Result
    {
        public bool Hit;
        public float T;
        public Vector3 Point;
        public string Target;   // rider id (Roles.Player/Roles.Enemy), Roles.Prop, Roles.Terrain, "" = nothing
        public HitZone Zone;    // rider hits only: Legs / Torso / Head (Splash stamped by damage)
        public float ZoneFrac;  // rider hits only: 0 at the sole, 1 at the top
        public string Part;     // rider hits only: breakable part id (via PartResolver), "" if unset
    }

    // Optional breakable-parts hook: (rider id, world hit point) -> part id.
    // Set by the duel once the part registries exist; null in other scenes.
    public System.Func<string, Vector3, string> PartResolver;

    // Optional breakable-parts ray hook: (rider id, origin, unit dir,
    // maxDist) -> (t, partId) of the nearest part-volume box hit along
    // the ray, t < 0 when none. Parts protrude outside the rider capsule
    // (arms, LRM pods), so aimed fire at them can only register through
    // the boxes; the capsule alone would sail past. Null in other scenes.
    public System.Func<string, Vector3, Vector3, float, (float T, string Part)> PartRayCast;

    public sealed class PropCyl
    {
        public Vector2 P;
        public float R;
        public float BaseY;
        public float Height;
    }

    public sealed class RiderCap
    {
        public string Id;
        public System.Func<Vector3> Root; // live root position provider
        public float Radius;
        public float Height;
    }

    public readonly List<PropCyl> Props = new();
    public readonly List<RiderCap> Riders = new();

    // inflateProps widens PROP cylinders for this cast only (the
    // laser's hit thickness - the beam visual stays hair-thin).
    public Result Ray(Vector3 from, Vector3 dir, float maxDist, string ignoreId, float inflateProps = 0f,
        System.Collections.Generic.HashSet<string> ignoreIds = null)
    {
        dir = dir.Normalized();
        var best = new Result { Hit = false, T = maxDist, Target = "", Zone = HitZone.None, ZoneFrac = 0f, Part = "" };
        float tt = RayTerrain(from, dir, maxDist);
        if (tt >= 0f) { best.Hit = true; best.T = tt; best.Target = Roles.Terrain; }
        foreach (var p in Props)
        {
            float t = RayCylinder(from, dir, p.P, p.R + inflateProps, p.BaseY, p.Height, best.T);
            if (t >= 0f) { best.Hit = true; best.T = t; best.Target = Roles.Prop; }
        }
        foreach (var m in Riders)
        {
            if (m.Id == ignoreId || (ignoreIds != null && ignoreIds.Contains(m.Id))) continue;
            var root = m.Root();
            // The capsule stands on the rider's LIVE sole level
            // (its root), not the terrain: on a deck the root is
            // at deck height while HeightAt still says ground -
            // the capsule used to stay buried under the platform
            // and shots at the visible rider sailed over it.
            float baseY = root.Y;
            float t = RayCylinder(from, dir, new Vector2(root.X, root.Z), m.Radius, baseY, m.Height, best.T);
            string boxPart = null;
            if (PartRayCast != null)
            {
                var (tb, pb) = PartRayCast(m.Id, from, dir, best.T);
                if (tb >= 0f && (t < 0f || tb < t)) { t = tb; boxPart = pb; }
            }
            if (t >= 0f)
            {
                best.Hit = true; best.T = t; best.Target = m.Id;
                float y = from.Y + dir.Y * t;
                best.ZoneFrac = Mathf.Clamp((y - baseY) / m.Height, 0f, 1f);
                best.Zone = best.ZoneFrac < LegsZoneTop ? HitZone.Legs : best.ZoneFrac < TorsoZoneTop ? HitZone.Torso : HitZone.Head;
                best.Part = boxPart ?? (PartResolver != null
                    ? PartResolver(m.Id, from + dir * t) : "");
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
        if (a > Epsilon)
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
                    if (y >= baseY - HeightTolerance && y <= topY + HeightTolerance) best = t;
                }
            }
        }
        // Descending onto the flat top disc.
        if (dir.Y < -DescentEpsilon)
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
        float prevT = 0f;
        if (from.Y <= Main.HeightAt(from.X, from.Z)) return 0f;
        for (float t = TerrainStep; t <= maxDist; t += TerrainStep)
        {
            var p = from + dir * t;
            if (p.Y <= Main.HeightAt(p.X, p.Z))
            {
                float lo = prevT, hi = t;
                for (int i = 0; i < TerrainRefineSteps; i++)
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

    // Distance from a point to a rider's capsule surface (0 when touching):
    // point-to-axis-segment distance minus the radius. Used for splash.
    public float DistanceToRiderSurface(Vector3 point, string id)
    {
        foreach (var m in Riders)
        {
            if (m.Id != id) continue;
            var root = m.Root();
            float baseY = root.Y; // live sole level, as in Ray
            var a = new Vector3(root.X, baseY, root.Z);
            var b = new Vector3(root.X, baseY + m.Height, root.Z);
            var ab = b - a;
            float tt = ab.LengthSquared() > Epsilon
                ? Mathf.Clamp((point - a).Dot(ab) / ab.LengthSquared(), 0f, 1f) : 0f;
            var closest = a + ab * tt;
            return Mathf.Max(point.DistanceTo(closest) - m.Radius, 0f);
        }
        return float.MaxValue;
    }
}
