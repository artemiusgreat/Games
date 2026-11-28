using Godot;

namespace Titans;

// Shared procedural-terrain math - THE one home for the
// noise stack (owner, 2026-11-27: several files each carried
// their own ValueNoise; they are all here now). No map data
// lives in this file: each map's definition (source/maps/)
// and each terrain painter feeds its own numbers through
// these helpers.
//
// Two hash families, one algorithm each:
// - the INTEGER lattice hash (xxhash-style primes) in two
//   quantizations: LatticeHash (16-bit, unseeded) and
//   SeededHash (24-bit, with a world seed mixed in);
// - the SIN hash (SinHash), the classic fract(sin(dot))
//   painter's hash over float inputs, for texture work.
// Value noise is the same construction over either: lattice
// corners blended with the cubic smoothstep fade.
public static class TerrainMath
{
    // Integer lattice hash (the xxhash finalizer family): the
    // multipliers are large odd primes chosen for bit
    // avalanche - a one-cell step in ix or iz scrambles every
    // output bit - not tuned values. The 13/16 shifts are the
    // standard xor-shift folds that finish the mixing, and
    // the result keeps the low 16 bits as a 0..1 fraction.
    public static float LatticeHash(int ix, int iz)
    {
        int h = ix * 374761393 + iz * 668265263;
        h = (h ^ (h >> 13)) * 1274126177;
        return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
    }

    // The seeded variant (Main's world noise): the seed is
    // mixed in through another large prime before the same
    // finalizer, and the output keeps 24 bits, so a seeded
    // world has finer value steps than the 16-bit form.
    public static float SeededHash(int ix, int iz, uint seed)
    {
        uint h = (uint)(ix * 374761393) + (uint)(iz * 668265263) + unchecked(seed * 2246822519u);
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return (h & 0x00FFFFFF) / 16777215f; // [0,1]
    }

    // Value noise over the unseeded lattice hash: the four
    // lattice corners around (x, z), blended with the cubic
    // smoothstep fade t*t*(3-2t) - its slope is zero at both
    // ends, so cell borders show no crease. Output [0,1].
    public static float ValueNoise(float x, float z)
    {
        int ix = Mathf.FloorToInt(x), iz = Mathf.FloorToInt(z);
        float fx = x - ix, fz = z - iz;
        float sx = fx * fx * (3f - 2f * fx), sz = fz * fz * (3f - 2f * fz);
        float a = LatticeHash(ix, iz), b = LatticeHash(ix + 1, iz);
        float c = LatticeHash(ix, iz + 1), d = LatticeHash(ix + 1, iz + 1);
        return Mathf.Lerp(Mathf.Lerp(a, b, sx), Mathf.Lerp(c, d, sx), sz);
    }

    // Value noise over the seeded hash - the same
    // construction, for worlds that carry a seed.
    public static float ValueNoise(float x, float z, uint seed)
    {
        int ix = Mathf.FloorToInt(x), iz = Mathf.FloorToInt(z);
        float fx = x - ix, fz = z - iz;
        float sx = fx * fx * (3f - 2f * fx), sz = fz * fz * (3f - 2f * fz);
        float a = SeededHash(ix, iz, seed), b = SeededHash(ix + 1, iz, seed);
        float c = SeededHash(ix, iz + 1, seed), d = SeededHash(ix + 1, iz + 1, seed);
        return Mathf.Lerp(Mathf.Lerp(a, b, sx), Mathf.Lerp(c, d, sx), sz);
    }

    // The one FBM octave walk (owner, 2026-11-27: every
    // consumer shares this walk - the base noise is the only
    // difference): each octave halves its amplitude, grows
    // its frequency about 2.03x and shifts the sample point
    // by an irrational offset, so the octaves never align
    // into a visible grid; the sum is normalized by the
    // total amplitude.
    private const float FbmAmplitudeFalloff = 0.5f;
    private const float FbmFrequencyGrowth = 2.03f;
    private const float FbmShiftX = 19.19f;
    private const float FbmShiftZ = -7.31f;

    private static float FbmWalk(float x, float z, int octaves, System.Func<float, float, float> noise)
    {
        float sum = 0f, amp = 1f, tot = 0f;
        for (int i = 0; i < octaves; i++)
        {
            sum += amp * noise(x, z);
            tot += amp;
            amp *= FbmAmplitudeFalloff;
            x = x * FbmFrequencyGrowth + FbmShiftX;
            z = z * FbmFrequencyGrowth + FbmShiftZ;
        }
        return sum / tot;
    }

    // Fractal value noise over the seeded hash, output ~[-1,1].
    public static float Fbm(float x, float z, int octaves, uint seed)
        => FbmWalk(x, z, octaves, (a, b) => ValueNoise(a, b, seed) * 2f - 1f);

    // The same walk over the sin value noise, output 0..1 -
    // the flow backdrop painter's field.
    public static float FbmSin(float x, float z, int octaves)
        => FbmWalk(x, z, octaves, SinValueNoise);

    // The sin hash (the classic painter's hash): a sine of a
    // dot product with irrational-ish weights, scaled up and
    // kept fractional. Cheap and stateless over float inputs,
    // which is why the texture painters use it; its lattice
    // cousin above is the better-behaved field for heights.
    private const float SinHashSeedX = 127.1f;
    private const float SinHashSeedZ = 311.7f;
    private const float SinHashScale = 43758.5453f;
    public static float SinHash(float x, float z)
    {
        float s = Mathf.Sin(x * SinHashSeedX + z * SinHashSeedZ) * SinHashScale;
        return s - Mathf.Floor(s);
    }

    // Value noise over the sin hash - the same smoothstep
    // blend of the four lattice corners. Output [0,1].
    public static float SinValueNoise(float x, float z)
    {
        float xi = Mathf.Floor(x), zi = Mathf.Floor(z);
        float xf = x - xi, zf = z - zi;
        float u = xf * xf * (3f - 2f * xf), v = zf * zf * (3f - 2f * zf);
        float a = SinHash(xi, zi), b = SinHash(xi + 1f, zi);
        float c = SinHash(xi, zi + 1f), d = SinHash(xi + 1f, zi + 1f);
        return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
    }

    // Smoothstep remap of v from [a, b] onto [0, 1] (clamped),
    // with the same cubic fade as the value noise.
    public static float Smooth(float a, float b, float v)
    {
        float t = Mathf.Clamp((v - a) / (b - a), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    // Distance from a point to a segment (the map recipes'
    // ridge/valley lines and road centrelines all read it).
    public static float DistToSegment(float px, float pz, float ax, float az, float bx, float bz)
    {
        float dx = bx - ax, dz = bz - az;
        float len2 = dx * dx + dz * dz;
        float t = len2 > 1e-6f ? Mathf.Clamp(((px - ax) * dx + (pz - az) * dz) / len2, 0f, 1f) : 0f;
        float ex = ax + dx * t - px, ez = az + dz * t - pz;
        return Mathf.Sqrt(ex * ex + ez * ez);
    }
}
