shader_type sky;

// Tiled sky (owner, 2026-11-25): his seamless star tile,
// repeated over the dome by triplanar projection - the
// view direction's dominant axis picks the sample plane
// and the three planes blend, so the tile keeps one
// uniform scale in every direction: no equirect stretch,
// no zenith pinch, no pole streaks. A second sample of
// the same tile, Y-rotated and at 1.7x scale, rides
// underneath at lower gain so the tile's few bright
// stars do not read as a repeating pattern.

uniform sampler2D tile : source_color, filter_linear_mipmap, repeat_enable;
uniform float repeats : hint_range(0.5, 10.0) = 3.0;
uniform float brightness : hint_range(0.0, 3.0) = 1.0;

vec3 proj(vec3 dir, float scale) {
	vec3 ad = abs(dir);
	vec3 w = pow(ad, vec3(6.0));
	w /= (w.x + w.y + w.z);
	vec3 cx = texture(tile, dir.zy * scale).rgb;
	vec3 cy = texture(tile, dir.xz * scale).rgb;
	vec3 cz = texture(tile, dir.xy * scale).rgb;
	return cx * w.x + cy * w.y + cz * w.z;
}

void sky() {
	vec3 d = normalize(EYEDIR);
	vec3 col = proj(d, repeats);
	float c = cos(1.1);
	float s = sin(1.1);
	vec3 d2 = vec3(c * d.x + s * d.z, d.y, -s * d.x + c * d.z);
	col += proj(d2, repeats * 1.7) * 0.55;
	COLOR = col * brightness;
}
