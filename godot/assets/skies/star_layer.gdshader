shader_type sky;

// Hybrid sky (owner's pick, 2026-11-13): the painted panorama
// carries the nebula and the galactic band; a procedural star
// layer computed from the 3D view direction rides on top, so the
// stars are single sharp points at any window size and stay
// round at the zenith, where the equirect photo pinches into
// radial streaks. No time input anywhere: the sky is static,
// nothing twinkles or shimmers.

uniform sampler2D panorama : source_color, filter_linear_mipmap, repeat_disable;
uniform float star_brightness : hint_range(0.0, 3.0) = 1.0;

vec3 hash33(vec3 p) {
	p = fract(p * vec3(0.1031, 0.1030, 0.0973));
	p += dot(p, p.yxz + 33.33);
	return fract((p.xxy + p.yxx) * p.zyx);
}

// One star field at one grid scale: each cell holds at most one
// star, placed off-centre by a hash; brightness follows a steep
// power curve so most stars are faint and a few are bright.
// Stars are tight points (owner, 2026-11-13: the first cut's wide
// halo read as soft blue dots) — a star is its core plus a whisper
// of tight glow, nothing disc-like.
vec3 star_layer(vec3 dir, float scale, float density, float gain) {
	vec3 p = dir * scale;
	vec3 base = floor(p);
	vec3 col = vec3(0.0);
	for (int i = -1; i <= 1; i++)
	for (int j = -1; j <= 1; j++)
	for (int k = -1; k <= 1; k++) {
		vec3 cell = base + vec3(float(i), float(j), float(k));
		vec3 rnd = hash33(cell);
		if (rnd.x > density) {
			continue;
		}
		vec3 center = cell + 0.5 + (hash33(cell + 19.19) - 0.5) * 0.75;
		float d = length(p - center);
		float core = exp(-d * d * 300.0);
		float glow = exp(-d * d * 140.0) * 0.10;
		float mag = 0.22 + pow(rnd.y, 6.0) * 1.7;
		// Mostly neutral-to-warm whites; blue is the rare tail of
		// the mix, not the default (owner: no blue dots).
		vec3 tint = mix(vec3(1.18, 1.05, 0.90), vec3(0.88, 0.95, 1.15), rnd.z * rnd.z);
		if (rnd.z > 0.95) {
			tint = vec3(1.25, 0.62, 0.85); // rare pink, like the photo's
		}
		col += tint * (core + glow) * mag * gain;
	}
	return col;
}

void sky() {
	vec3 dir = EYEDIR;
	// Same equirect mapping the built-in panorama material uses,
	// so the photo lands exactly where it did without the layer.
	vec2 uv = vec2(atan(dir.x, -dir.z) / 6.2831853 + 0.5,
			acos(clamp(dir.y, -1.0, 1.0)) / 3.1415927);
	// The photo's own saturated-blue stars are cleaned in the
	// texture itself (owner, 2026-11-13: baked de-blue pass —
	// blue-excess pixels neutralized to their luminance; the
	// shader's earlier mute block is retired with it).
	vec3 col = texture(panorama, uv).rgb;
	// Sparser fields (owner, 2026-11-13): roughly half the first
	// cut's densities at every scale.
	col += star_layer(dir, 120.0, 0.14, 0.30) * star_brightness; // faint background field
	col += star_layer(dir, 46.0, 0.24, 0.60) * star_brightness;  // main field
	col += star_layer(dir, 21.0, 0.10, 1.10) * star_brightness;  // sparse bright stars
	COLOR = col;
}
