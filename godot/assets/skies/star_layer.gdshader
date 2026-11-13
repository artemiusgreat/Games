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
		float halo = exp(-d * d * 36.0) * 0.22;
		float mag = 0.22 + pow(rnd.y, 6.0) * 1.7;
		vec3 tint = mix(vec3(0.72, 0.84, 1.25), vec3(1.25, 0.88, 0.66), rnd.z);
		if (rnd.z > 0.93) {
			tint = vec3(1.25, 0.62, 0.85); // rare pink, like the photo's
		}
		col += tint * (core + halo) * mag * gain;
	}
	return col;
}

void sky() {
	vec3 dir = EYEDIR;
	// Same equirect mapping the built-in panorama material uses,
	// so the photo lands exactly where it did without the layer.
	vec2 uv = vec2(atan(dir.x, -dir.z) / 6.2831853 + 0.5,
			acos(clamp(dir.y, -1.0, 1.0)) / 3.1415927);
	vec3 col = texture(panorama, uv).rgb;
	col += star_layer(dir, 120.0, 0.32, 0.30) * star_brightness; // dense faint field
	col += star_layer(dir, 46.0, 0.50, 0.60) * star_brightness;  // main field
	col += star_layer(dir, 21.0, 0.26, 1.10) * star_brightness;  // sparse bright stars
	COLOR = col;
}
