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
        public string Target;   // rider id (Roles.Player/Roles.Enemy), Roles.Prop, Roles.Terrain, "" = nothing
        public string Zone;     // rider hits only: "legs" / "torso" / "head"
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
    // laser's hit thickness — the beam visual stays hair-thin).
    public Result Ray(Vector3 from, Vector3 dir, float maxDist, string ignoreId, float inflateProps = 0f)
    {
        dir = dir.Normalized();
        var best = new Result { Hit = false, T = maxDist, Target = "", Zone = "", ZoneFrac = 0f, Part = "" };
        float tt = RayTerrain(from, dir, maxDist);
        if (tt >= 0f) { best.Hit = true; best.T = tt; best.Target = Roles.Terrain; }
        // Built surfaces (owner: platforms must collide with
        // weapons — beams and shells were ending at the terrain
        // behind/under a deck while the reticle rested on it):
        // deck tops, undersides and side bands, island discs and
        // ramp slopes, competing by distance like everything else.
        float ts = RaySurfaces(from, dir, best.T);
        if (ts >= 0f) { best.Hit = true; best.T = ts; best.Target = Roles.Terrain; }
        foreach (var p in Props)
        {
            float t = RayCylinder(from, dir, p.P, p.R + inflateProps, p.BaseY, p.Height, best.T);
            if (t >= 0f) { best.Hit = true; best.T = t; best.Target = Roles.Prop; }
        }
        foreach (var m in Riders)
        {
            if (m.Id == ignoreId) continue;
            var root = m.Root();
            // The capsule stands on the rider's LIVE sole level
            // (its root), not the terrain: on a deck the root is
            // at deck height while HeightAt still says ground —
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
                best.Zone = best.ZoneFrac < 0.42f ? "legs" : best.ZoneFrac < 0.86f ? "torso" : "head";
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

    // Ray vs the built surfaces registered in Main: roof rects
    // (decks and building roofs — top plane, underside plane and
    // the side band between them), island discs (same three) and
    // ramp slopes (the inclined plane inside the ramp rect).
    // Returns the nearest t within maxT, or -1.
    private static float RaySurfaces(Vector3 from, Vector3 dir, float maxT)
    {
        float best = -1f;
        void Take(float t)
        {
            if (t >= 0f && t <= maxT && (best < 0f || t < best)) best = t;
        }
        foreach (var r in Main.Roofs)
        {
            // Top and underside planes.
            if (Mathf.Abs(dir.Y) > 1e-7f)
            {
                float tT = (r.Top - from.Y) / dir.Y;
                if (tT >= 0f && tT <= maxT)
                {
                    float px = from.X + dir.X * tT, pz = from.Z + dir.Z * tT;
                    if (Mathf.Abs(px - r.Cx) <= r.Hx && Mathf.Abs(pz - r.Cz) <= r.Hz) Take(tT);
                }
                float tU = (r.Under - from.Y) / dir.Y;
                if (tU >= 0f && tU <= maxT && tU != tT)
                {
                    float px = from.X + dir.X * tU, pz = from.Z + dir.Z * tU;
                    if (Mathf.Abs(px - r.Cx) <= r.Hx && Mathf.Abs(pz - r.Cz) <= r.Hz) Take(tU);
                }
            }
            // Side band: entry into the XZ rect at a height
            // between Under and Top.
            float tE = RectEntry(from, dir, r.Cx, r.Cz, r.Hx, r.Hz);
            if (tE >= 0f)
            {
                float y = from.Y + dir.Y * tE;
                if (y >= r.Under - 0.001f && y <= r.Top + 0.001f) Take(tE);
            }
        }
        foreach (var d in Main.DiscRoofs)
        {
            if (Mathf.Abs(dir.Y) > 1e-7f)
            {
                for (int k = 0; k < 2; k++)
                {
                    float lvl = k == 0 ? d.Top : d.Under;
                    float t = (lvl - from.Y) / dir.Y;
                    if (t < 0f || t > maxT) continue;
                    float px = from.X + dir.X * t - d.Cx, pz = from.Z + dir.Z * t - d.Cz;
                    if (px * px + pz * pz <= d.R * d.R) Take(t);
                }
            }
            float a = dir.X * dir.X + dir.Z * dir.Z;
            if (a > 1e-8f)
            {
                float ox = from.X - d.Cx, oz = from.Z - d.Cz;
                float b = ox * dir.X + oz * dir.Z;
                float cc = ox * ox + oz * oz - d.R * d.R;
                float disc = b * b - a * cc;
                if (disc >= 0f)
                {
                    float t = (-b - Mathf.Sqrt(disc)) / a;
                    if (t < 0f && cc < 0f) t = 0f;
                    if (t >= 0f)
                    {
                        float y = from.Y + dir.Y * t;
                        if (y >= d.Under - 0.001f && y <= d.Top + 0.001f) Take(t);
                    }
                }
            }
        }
        foreach (var r in Main.Ramps)
        {
            float slope = (r.H1 - r.H0) / (2f * r.HalfLen);
            float hMid = (r.H0 + r.H1) * 0.5f;
            float along0 = (from.X - r.Cx) * r.DirX + (from.Z - r.Cz) * r.DirZ;
            float f0 = from.Y - slope * along0 - hMid;
            float df = dir.Y - slope * (dir.X * r.DirX + dir.Z * r.DirZ);
            if (Mathf.Abs(df) >= 1e-7f)
            {
                float t = -f0 / df;
                if (t >= 0f && t <= maxT)
                {
                    var p = from + dir * t;
                    float along = (p.X - r.Cx) * r.DirX + (p.Z - r.Cz) * r.DirZ;
                    float perp = -(p.X - r.Cx) * r.DirZ + (p.Z - r.Cz) * r.DirX;
                    if (Mathf.Abs(along) <= r.HalfLen && Mathf.Abs(perp) <= r.HalfWid) Take(t);
                }
            }
            // Wedge band (owner round 7): the solid under the
            // slope plane is 3.5 thick — a level shot into the
            // wedge's side, or up into its underside, must
            // stop too, not just a shot landing on the plane.
            // Underside plane first (same math, hMid - 3.5):
            float f0b = from.Y - slope * along0 - (hMid - 3.5f);
            float tb = Mathf.Abs(df) >= 1e-7f ? -f0b / df : -1f;
            if (tb >= 0f && tb <= maxT)
            {
                var pb = from + dir * tb;
                float ab = (pb.X - r.Cx) * r.DirX + (pb.Z - r.Cz) * r.DirZ;
                float pp2 = -(pb.X - r.Cx) * r.DirZ + (pb.Z - r.Cz) * r.DirX;
                if (Mathf.Abs(ab) <= r.HalfLen && Mathf.Abs(pp2) <= r.HalfWid) Take(tb);
            }
            // Side entry: slab test in the ramp's own frame;
            // the hit counts when the entry height sits
            // inside [h(along) - 3.5, h(along)].
            float oP = -(from.X - r.Cx) * r.DirZ + (from.Z - r.Cz) * r.DirX;
            float dA = dir.X * r.DirX + dir.Z * r.DirZ;
            float dP = -dir.X * r.DirZ + dir.Z * r.DirX;
            float t0 = 0f, t1 = maxT; bool ok = true;
            if (Mathf.Abs(dA) < 1e-8f) { if (Mathf.Abs(along0) > r.HalfLen) ok = false; }
            else { float u1 = (-r.HalfLen - along0) / dA, u2 = (r.HalfLen - along0) / dA; if (u1 > u2) (u1, u2) = (u2, u1); t0 = Mathf.Max(t0, u1); t1 = Mathf.Min(t1, u2); }
            if (ok)
            {
                if (Mathf.Abs(dP) < 1e-8f) { if (Mathf.Abs(oP) > r.HalfWid) ok = false; }
                else { float u1 = (-r.HalfWid - oP) / dP, u2 = (r.HalfWid - oP) / dP; if (u1 > u2) (u1, u2) = (u2, u1); t0 = Mathf.Max(t0, u1); t1 = Mathf.Min(t1, u2); }
            }
            if (ok && t0 <= t1 && t0 <= maxT)
            {
                float aE = along0 + dA * t0;
                float hE = Mathf.Lerp(r.H0, r.H1, Mathf.Clamp((aE + r.HalfLen) / (2f * r.HalfLen), 0f, 1f));
                float yE = from.Y + dir.Y * t0;
                if (yE <= hE + 0.001f && yE >= hE - 3.5f - 0.001f) Take(t0);
            }
        }
        return best;
    }

    // Entry distance of a ray into an axis-aligned XZ rect
    // (0 when the origin is already inside), -1 when it misses.
    private static float RectEntry(Vector3 from, Vector3 dir, float cx, float cz, float hx, float hz)
    {
        float tmin = 0f, tmax = float.MaxValue;
        if (Mathf.Abs(dir.X) < 1e-8f) { if (Mathf.Abs(from.X - cx) > hx) return -1f; }
        else
        {
            float t1 = (cx - hx - from.X) / dir.X, t2 = (cx + hx - from.X) / dir.X;
            if (t1 > t2) (t1, t2) = (t2, t1);
            tmin = Mathf.Max(tmin, t1); tmax = Mathf.Min(tmax, t2);
            if (tmin > tmax) return -1f;
        }
        if (Mathf.Abs(dir.Z) < 1e-8f) { if (Mathf.Abs(from.Z - cz) > hz) return -1f; }
        else
        {
            float t1 = (cz - hz - from.Z) / dir.Z, t2 = (cz + hz - from.Z) / dir.Z;
            if (t1 > t2) (t1, t2) = (t2, t1);
            tmin = Mathf.Max(tmin, t1); tmax = Mathf.Min(tmax, t2);
            if (tmin > tmax) return -1f;
        }
        return tmin;
    }

    // Distance from a point to a rider's capsule surface (0 when touching):
    // point-to-axis-segment distance minus the radius. Used for splash.
    public float DistanceToRiderSurface(Vector3 point, string id)
    {
        foreach (var m in Riders)
        {
            if (m.Id != id) continue;
            var root = m.Root();
            float baseY = root.Y; // live sole level, as in Ray (decks)
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
