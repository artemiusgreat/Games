using Godot;

// Shared procedural-terrain math: deterministic value noise
// and the smoothstep remap. No map data lives here - each
// map's own definition file (source/maps/) feeds its layout
// numbers through these helpers, so two maps can never
// disagree with themselves about the ground they stand on.
public static class TerrainMath
{
    // Integer lattice hash (the xxhash finalizer family): the
    // multipliers are large odd primes chosen for bit
    // avalanche - a one-cell step in ix or iz scrambles every
    // output bit - not tuned values. The 13/16 shifts are the
    // standard xor-shift folds that finish the mixing, and
    // the result keeps the low 16 bits as a 0..1 fraction.
    private static float Hash2(int ix, int iz)
    {
        int h = ix * 374761393 + iz * 668265263;
        h = (h ^ (h >> 13)) * 1274126177;
        return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
    }

    // Value noise: the four lattice corners around (x, z),
    // blended with the cubic smoothstep fade t*t*(3-2t) -
    // its slope is zero at both ends, so cell borders show
    // no crease.
    public static float Noise2(float x, float z)
    {
        int ix = Mathf.FloorToInt(x), iz = Mathf.FloorToInt(z);
        float fx = x - ix, fz = z - iz;
        float sx = fx * fx * (3f - 2f * fx), sz = fz * fz * (3f - 2f * fz);
        float a = Hash2(ix, iz), b = Hash2(ix + 1, iz);
        float c = Hash2(ix, iz + 1), d = Hash2(ix + 1, iz + 1);
        return a + (b - a) * sx + (c - a) * sz + (a - b - c + d) * sx * sz;
    }

    // Three noise octaves: weights 0.55 / 0.30 / 0.15 (they
    // sum to 1, so the output stays in the noise's own 0..1
    // range), each octave about 2.13x finer than the last,
    // with irrational offsets so the octaves never align
    // into a visible grid.
    public static float Fbm(float x, float z)
        => Noise2(x, z) * 0.55f
         + Noise2(x * 2.13f + 7.3f, z * 2.13f + 3.1f) * 0.30f
         + Noise2(x * 4.31f + 13.7f, z * 4.31f + 9.2f) * 0.15f;

    // One-dimensional noise: a slice of Noise2 at a fixed,
    // non-integer z, so a value that varies along one axis
    // (wall height along a valley, say) never correlates
    // with the 2-D detail built from the same field.
    public static float Noise1(float t) => Noise2(t, 3.77f);

    // Smoothstep remap of v from [a, b] onto [0, 1] (clamped),
    // with the same cubic fade as Noise2.
    public static float Smooth(float a, float b, float v)
    {
        float t = Mathf.Clamp((v - a) / (b - a), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
