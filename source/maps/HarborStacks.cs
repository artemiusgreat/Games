using Godot;

// The terraced city's terrain recipe (owner, 2026-11-27:
// map data lives in per-map files - this is the Harbor
// Stacks stepped-valley heightfield, moved out of Main).
// Canyon country for the terraced city (owner: the map's
// edge reads as canyon / mountains, with several valleys
// connected by pathways - each valley hosts its multilevel
// city): ridged mountains everywhere, a walkable pass
// carved along each base-to-base road, and a flat basin
// floor at each base for its city.
public static class HarborStacks
{
    // Height recipe (naming pass, 2026-11-27: names only,
    // exact values).
    private const float RollingFieldScale = 170f;
    private const float RollingOffsetX = 3.1f, RollingOffsetZ = -8.2f;
    private const int RollingOctaves = 4;
    private const float RidgeFieldScale = 67f;
    private const float RidgeOffsetX = -5.3f, RidgeOffsetZ = 11.1f;
    private const int RidgeOctaves = 4;
    private const float ValleyBaseHeight = 12f, ValleyNoiseGain = 34f, RidgeGain = 62f;
    private const float RimOnset = 640f, RimFull = 920f, RimGain = 85f;
    private const float RimNoiseScale = 90f;
    private const float RimNoiseOffsetX = 17.7f, RimNoiseOffsetZ = -4.2f;
    private const int RimOctaves = 3;
    private const float RimStart = 452f, RimPeakEnd = 565f, RimFadeStart = 720f, RimFadeEnd = 880f;
    private const float RimBaseHeight = 22f, RimNoiseGain = 16f;
    private const float PassHalfWidth = 64f, PassFullCarve = 22f, PassBaseHeight = 3f, PassGain = 23f, PassCurveExponent = 0.8f;
    private const float BasinRadius = 440f, BasinFullFlat = 305f;

    public static float Height(float x, float z)
    {
        float n = TerrainMath.Fbm(x / RollingFieldScale + RollingOffsetX, z / RollingFieldScale + RollingOffsetZ, RollingOctaves, Main.NoiseSeed) * 0.5f + 0.5f;
        float rg = 1f - Mathf.Abs(TerrainMath.Fbm(x / RidgeFieldScale + RidgeOffsetX, z / RidgeFieldScale + RidgeOffsetZ, RidgeOctaves, Main.NoiseSeed));
        float sq = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
        float h = ValleyBaseHeight + n * ValleyNoiseGain + rg * rg * RidgeGain + TerrainMath.Smooth(RimOnset, RimFull, sq) * RimGain;
        var p = new Vector2(x, z);
        // Valley rims (owner: "looks like you removed canyon" -
        // the basins were flat discs in low rolling ground, so
        // from inside a valley nothing read as canyon): a ridge
        // ring just outside each basin puts every valley in a
        // visible bowl, 22-38u of relief starting beyond the
        // city (r 452) and fading by r 880. The pass carve
        // below cuts the road notches through it; the basin
        // flatten (r <= 440) is untouched.
        float rim = 0f;
        foreach (var pad in Main.SpawnPads)
        {
            float d = p.DistanceTo(pad);
            float rimN = TerrainMath.Fbm(x / RimNoiseScale + RimNoiseOffsetX, z / RimNoiseScale + RimNoiseOffsetZ, RimOctaves, Main.NoiseSeed) * 0.5f + 0.5f;
            rim = Mathf.Max(rim, TerrainMath.Smooth(RimStart, RimPeakEnd, d)
                * (1f - TerrainMath.Smooth(RimFadeStart, RimFadeEnd, d)) * (RimBaseHeight + rimN * RimNoiseGain));
        }
        h += rim;
        for (int a = 0; a < Main.SpawnPads.Length; a++)
        for (int b = a + 1; b < Main.SpawnPads.Length; b++)
        {
            var pa = Main.SpawnPads[a]; var pb = Main.SpawnPads[b];
            var ab = pb - pa;
            float t = Mathf.Clamp((p - pa).Dot(ab) / ab.LengthSquared(), 0f, 1f);
            float d = (pa + ab * t).DistanceTo(p);
            if (d < PassHalfWidth)
            {
                // Max(0, ...): Sin(Pi * 1) in floats is a tiny
                // NEGATIVE, and Pow(neg, 0.8) is NaN - that
                // poisoned the heightfield (and both AI spawns)
                // beyond the pb endpoints before this guard.
                float passH = PassBaseHeight + PassGain * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.Pi * t)), PassCurveExponent);
                h = Mathf.Lerp(h, Mathf.Min(h, passH), TerrainMath.Smooth(PassHalfWidth, PassFullCarve, d));
            }
        }
        foreach (var pad in Main.SpawnPads)
        {
            float d = p.DistanceTo(pad);
            if (d < BasinRadius) h = Mathf.Lerp(h, 0f, TerrainMath.Smooth(BasinRadius, BasinFullFlat, d));
        }
        return h;
    }
}
