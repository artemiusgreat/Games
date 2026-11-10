using Godot;

public partial class Main : Node3D
{
	// World size comes from the front-end flow (WorldConfig): the
	// 260u Tournament arena, or the 1900u Mission front.
	public static int TerrainSize => WorldConfig.WorldSize;
	public static int TerrainSegs => WorldConfig.TerrainSegs;

	// The three spawn pads / Mission bases, 120 degrees apart.
	// Tournament: r=80 (pairwise ~139u — far beyond first-sight
	// range). Mission: the base triangle at r=620 (~1075u apart).
	// RawHeight flattens the ground at each. Index 0 is the player's.
	public static Vector2[] SpawnPads => WorldConfig.Pads;

	// ---- Heightfield terrain (roadmap step 4) ----
	// The world is ONE heightfield grid, generated once from a seeded
	// value-noise stack. HeightAt is a bilinear sample of that grid, so
	// the terrain mesh, the gait/IK, the hit model, collisions and
	// debris settling all read the same ground by construction.
	// Layout: multi-octave rolling hills everywhere; a craggy ridge in
	// the NW; a shallow valley in the SE; mountain ring at the edges.
	// Amplitude is modulated by radius so the central arena (r < ~36)
	// keeps gentle duel-playable slopes while the rim rises properly.
	private static int GridN => TerrainSegs + 1; // sample points per side
	private const uint NoiseSeed = 20261027u;
	// Built lazily on first use: the flow sets WorldConfig.Mode
	// before the duel scene loads, and whichever mode is current
	// at first access is the world this process runs.
	private static float[] _grid;
	private static float[] Grid => _grid ??= BuildGrid();

	private static float LatticeHash(int ix, int iz)
	{
		uint h = (uint)(ix * 374761393) + (uint)(iz * 668265263) + unchecked(NoiseSeed * 2246822519u);
		h = (h ^ (h >> 13)) * 1274126177u;
		h ^= h >> 16;
		return (h & 0x00FFFFFF) / 16777215f; // [0,1]
	}

	// Smooth value noise, output [0,1].
	private static float ValueNoise(float x, float z)
	{
		int ix = Mathf.FloorToInt(x), iz = Mathf.FloorToInt(z);
		float fx = x - ix, fz = z - iz;
		float sx = fx * fx * (3f - 2f * fx), sz = fz * fz * (3f - 2f * fz);
		float a = LatticeHash(ix, iz), b = LatticeHash(ix + 1, iz);
		float c = LatticeHash(ix, iz + 1), d = LatticeHash(ix + 1, iz + 1);
		return Mathf.Lerp(Mathf.Lerp(a, b, sx), Mathf.Lerp(c, d, sx), sz);
	}

	// Fractal value noise, output ~[-1,1].
	private static float Fbm(float x, float z, int octaves)
	{
		float sum = 0f, amp = 1f, tot = 0f;
		for (int i = 0; i < octaves; i++)
		{
			sum += amp * (ValueNoise(x, z) * 2f - 1f);
			tot += amp;
			amp *= 0.5f;
			x = x * 2.03f + 19.19f;
			z = z * 2.03f - 7.31f;
		}
		return sum / tot;
	}

	private static float DistToSegment(float px, float pz, float ax, float az, float bx, float bz)
	{
		float dx = bx - ax, dz = bz - az;
		float len2 = dx * dx + dz * dz;
		float t = len2 > 1e-6f ? Mathf.Clamp(((px - ax) * dx + (pz - az) * dz) / len2, 0f, 1f) : 0f;
		float ex = ax + dx * t - px, ez = az + dz * t - pz;
		return Mathf.Sqrt(ex * ex + ez * ez);
	}

	private static float Smooth(float a, float b, float x)
	{
		float t = Mathf.Clamp((x - a) / (b - a), 0f, 1f);
		return t * t * (3f - 2f * t);
	}

	// The center dome (owner, 2026-10-31): a broad hill in the
	// middle of the map, ~34u over the arena floor, so the three
	// spawn pads have no sightline to each other. Measured
	// geometry: every pad pair's sightline passes r=40 from the
	// center, where the dome still stands ~23u — above the
	// highest pad eye line (pads flatten to at most 3u, +~11.5
	// eye height). Added AFTER the arena modulation so the
	// center damping cannot flatten it away. The top is a mesa
	// (flat inside r=14): the old pure Gaussian put the center
	// test marks on a 2.3u relative slope, which moved shot
	// attribution from torso to thigh — a fighting plateau is
	// also simply better ground to duel on.
	private static float DomeHeight(float x, float z)
	{
		float r = Mathf.Sqrt(x * x + z * z);
		float rr = Mathf.Max(0f, r - 14f);
		return 34f * Mathf.Exp(-(rr * rr) / (2f * 30f * 30f));
	}

	private static float RawHeight(float x, float z)
	{
		// Flat maps (owner: The Harbor Stacks) use no height map
		// at all — every street, road and base on one level plane.
		if (WorldConfig.FlatWorld) return 0f;
		float h = BaseHeight(x, z);
		// Flatten each spawn pad toward its own center height, so
		// a rider starts on level ground in its hollow.
		float flatR = WorldConfig.PadFlattenRadius;
		foreach (var pad in SpawnPads)
		{
			float d = new Vector2(x, z).DistanceTo(pad);
			if (d < flatR)
			{
				// Arena pads sit LOW (at most 3u) whatever the local
				// relief says — the hollows the dome math assumes.
				// Mission bases flatten to their own plateau height.
				float padH = WorldConfig.IsMission
					? BaseHeight(pad.X, pad.Y)
					: Mathf.Min(BaseHeight(pad.X, pad.Y), 3f);
				h = Mathf.Lerp(h, padH, Smooth(flatR, flatR * 0.44f, d));
			}
		}
		// City district plateau (owner: Harbor Stacks should
		// read as actual streets and blocks): on a kit contract
		// the map center levels into a building platform — the
		// dome and the relief blend out across the district, so
		// the street grid sits flat.
		if (WorldConfig.IsMission && WorldConfig.CityKit >= 0)
		{
			float r = Mathf.Sqrt(x * x + z * z);
			if (r < CityGrid.PlateauR)
			{
				float h0 = BaseHeight(0f, 0f) - DomeHeight(0f, 0f);
				h = Mathf.Lerp(h, h0, Smooth(CityGrid.PlateauR, CityGrid.PlateauR * 0.62f, r));
			}
		}
		return h;
	}

	private static float BaseHeight(float x, float z)
	{
		float r = Mathf.Sqrt(x * x + z * z);
		// Rolling hills, the base relief everywhere.
		float hills = Fbm(x / 58f, z / 58f, 5) * 6.5f;
		// Mission fronts get broad macro swells under the detail
		// noise, so a 1900u crossing has real country in it. Damped
		// near the center so the mesa keeps its clean plateau.
		if (WorldConfig.IsMission)
			hills += Fbm(x / 420f + 7f, z / 420f - 3f, 4) * 26f * Smooth(120f, 300f, r);
		// Craggy ridge line through the NW quadrant.
		float dRidge = DistToSegment(x, z, -88f, -28f, -24f, -88f);
		float crag = 1f - Mathf.Abs(Fbm(x / 21f + 40f, z / 21f - 17f, 3));
		float ridge = Mathf.Exp(-(dRidge * dRidge) / (2f * 15f * 15f)) * (5f + 7f * crag);
		// Shallow valley through the SE quadrant.
		float dVal = DistToSegment(x, z, 88f, 34f, 28f, -88f);
		float valley = -3.6f * Mathf.Exp(-(dVal * dVal) / (2f * 13f * 13f));
		// Mountain ring bounding the map (square falloff follows the rim).
		float sq = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
		float half = TerrainSize / 2f;
		float m = Smooth(half - 42f, half - 2f, sq);
		float mtn = m * (9f + 7f * (Fbm(x / 37f - 9f, z / 37f + 23f, 4) * 0.5f + 0.5f));
		float full = hills + ridge + valley + mtn;
		// Arena modulation: 22% amplitude at the center, full by r=94.
		// The Mission front keeps full amplitude everywhere.
		float t = WorldConfig.IsMission ? 1f : Smooth(54f, 94f, r);
		return full * (0.22f + 0.78f * t) + DomeHeight(x, z);
	}

	private static float[] BuildGrid()
	{
		var g = new float[GridN * GridN];
		float half = TerrainSize / 2f;
		float step = (float)TerrainSize / TerrainSegs;
		for (int j = 0; j < GridN; j++)
			for (int i = 0; i < GridN; i++)
				g[j * GridN + i] = RawHeight(-half + i * step, -half + j * step);
		return g;
	}

	// Terrain height: bilinear sample of the heightfield grid (constant
	// outside the world, clamped to the rim). Called constantly by the
	// gait, the hit model and the debris sim — keep it allocation-free.
	public static float HeightAt(float x, float z)
	{
		float half = TerrainSize / 2f;
		float scale = TerrainSegs / (float)TerrainSize;
		float gx = Mathf.Clamp((x + half) * scale, 0f, TerrainSegs - 0.001f);
		float gz = Mathf.Clamp((z + half) * scale, 0f, TerrainSegs - 0.001f);
		int ix = (int)gx, iz = (int)gz;
		float fx = gx - ix, fz = gz - iz;
		int i0 = iz * GridN + ix;
		float a = Grid[i0], b = Grid[i0 + 1], c = Grid[i0 + GridN], d = Grid[i0 + GridN + 1];
		return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fz);
	}

	// Verification hook: the stored analytic height at a grid node.
	public static float GridHeightAt(int i, int j) => Grid[j * GridN + i];

	// Building roofs a rider can stand on (owner, 2026-10-31):
	// registered by RiderDuel's AddBuilding, removed when the
	// building is destroyed. Axis-aligned rectangles with a top.
	public struct RoofRect { public float Cx, Cz, Hx, Hz, Top; }
	public static readonly System.Collections.Generic.List<RoofRect> Roofs = new();

	// Sloped walkable surfaces (owner: Harbor terraces): the
	// height varies linearly along (DirX, DirZ) from H0 at the
	// -Dir end to H1 at the +Dir end, across a HalfWid corridor.
	public struct RampRect { public float Cx, Cz, DirX, DirZ, HalfLen, HalfWid, H0, H1; }
	public static readonly System.Collections.Generic.List<RampRect> Ramps = new();

	// Round standable tops (the Harbor island deck).
	public struct DiscRoof { public float Cx, Cz, R, Top; }
	public static readonly System.Collections.Generic.List<DiscRoof> DiscRoofs = new();

	// Axis-aligned walls with a top (terrace platform sides,
	// the island bridge under-deck): RiderDuel's ResolveOne
	// pushes walkers out while their soles are below Top, in
	// the same spirit as the building circles' RoofTop gate.
	public static readonly System.Collections.Generic.List<RoofRect> Walls = new();

	public static float RampHeightAt(RampRect r, float x, float z, out bool inside)
	{
		float dx = x - r.Cx, dz = z - r.Cz;
		float along = dx * r.DirX + dz * r.DirZ;
		float perp = -dx * r.DirZ + dz * r.DirX;
		inside = Mathf.Abs(along) <= r.HalfLen && Mathf.Abs(perp) <= r.HalfWid;
		float t = Mathf.Clamp((along + r.HalfLen) / (2f * r.HalfLen), 0f, 1f);
		return Mathf.Lerp(r.H0, r.H1, t);
	}

	// Ground height for a walker at (x, z): the terrain, or a
	// roof's top when the walker's soles are already at that level
	// — refY is the sole level plus a small step tolerance, so a
	// walker standing ON the roof (or falling onto it) grounds on
	// it, while a walker on the ground beside the building, whose
	// refY sits far below the top, keeps the terrain and is still
	// stopped by the walls.
	public static float GroundAt(float x, float z, float refY)
	{
		float g = HeightAt(x, z);
		foreach (var r in Roofs)
			if (r.Top > g && refY >= r.Top && Mathf.Abs(x - r.Cx) <= r.Hx && Mathf.Abs(z - r.Cz) <= r.Hz)
				g = r.Top;
		foreach (var d in DiscRoofs)
			if (d.Top > g && refY >= d.Top &&
				(x - d.Cx) * (x - d.Cx) + (z - d.Cz) * (z - d.Cz) <= d.R * d.R)
				g = d.Top;
		// Ramps adopt with a step-up allowance: a walker
		// climbing plants its next foot up to ~2.5u above its
		// current sole (a stride's rise on a 28-degree ramp).
		foreach (var r in Ramps)
		{
			float h = RampHeightAt(r, x, z, out bool inside);
			if (inside && h > g && refY >= h - 2.5f) g = h;
		}
		return g;
	}

	private static Vector3 GridNormal(int i, int j, float step)
	{
		int il = Mathf.Max(i - 1, 0), ir = Mathf.Min(i + 1, GridN - 1);
		int jd = Mathf.Max(j - 1, 0), ju = Mathf.Min(j + 1, GridN - 1);
		float dhdx = (Grid[j * GridN + ir] - Grid[j * GridN + il]) / ((ir - il) * step);
		float dhdz = (Grid[ju * GridN + i] - Grid[jd * GridN + i]) / ((ju - jd) * step);
		return new Vector3(-dhdx, 1f, -dhdz).Normalized();
	}

	// Roads: pure coloring — HeightAt is untouched, so gait, hit
	// tests and collisions never notice them. Distance is to the
	// nearer centerline. The arena has two strips crossing at
	// (-14, 6); the Mission front gets dirt roads along the three
	// base-to-base legs, so the march between bases has a line to
	// follow.
	public static float RoadDistance(float x, float z)
	{
		if (WorldConfig.IsMission)
		{
			var p = WorldConfig.Pads;
			float d = DistToSegment(x, z, p[0].X, p[0].Y, p[1].X, p[1].Y);
			d = Mathf.Min(d, DistToSegment(x, z, p[1].X, p[1].Y, p[2].X, p[2].Y));
			d = Mathf.Min(d, DistToSegment(x, z, p[2].X, p[2].Y, p[0].X, p[0].Y));
			return d;
		}
		return Mathf.Min(Mathf.Abs(z - 6f), Mathf.Abs(x + 14f));
	}

	// Street grid of the city district (kit contracts): lanes
	// run between the building plots. Distance to the nearest
	// lane centerline; a large number outside the district or
	// off a kit contract.
	public static float DistrictStreetDistance(float x, float z)
	{
		if (!WorldConfig.IsMission || WorldConfig.CityKit < 0) return 1e6f;
		float p = CityGrid.Pitch;
		float ext = CityGrid.Radius + p;
		if (x * x + z * z > ext * ext) return 1e6f;
		float yx = x / p - 0.5f, yz = z / p - 0.5f;
		float dx = Mathf.Abs(yx - Mathf.Round(yx)) * p;
		float dz = Mathf.Abs(yz - Mathf.Round(yz)) * p;
		return Mathf.Min(dx, dz);
	}

	// Ground color without any texture image: grass tones varied by two
	// noise fields (patchiness + fine breakup), dirt on mid slopes, rock
	// on steep faces, pale summits up high.
	public static Color TerrainColor(float x, float z, float h, Vector3 n)
	{
		float slope = 1f - n.Y; // 0 flat, grows with gradient
		float patch = Fbm(x / 23f - 47f, z / 23f + 5f, 3) * 0.5f + 0.5f;
		float grain = Fbm(x / 6.5f + 31f, z / 6.5f - 11f, 3) * 0.5f + 0.5f;
		// Biome palettes (owner's map types): Forest keeps the
		// original greens; Desert runs sand; City runs urban
		// concrete with a per-block tint shift.
		bool desert = WorldConfig.Biome == WorldConfig.BiomeKind.Desert;
		bool city = WorldConfig.Biome == WorldConfig.BiomeKind.City;
		var col = desert
			? new Color(0.70f, 0.565f, 0.335f).Lerp(new Color(0.79f, 0.66f, 0.42f), patch)
			: city
				? new Color(0.345f, 0.35f, 0.36f).Lerp(new Color(0.41f, 0.405f, 0.395f),
					Fbm(Mathf.Floor(x / 26f) * 7.13f, Mathf.Floor(z / 26f) * 3.71f, 1) * 0.5f + 0.5f)
				: new Color(0.185f, 0.36f, 0.145f).Lerp(new Color(0.36f, 0.485f, 0.21f), patch);
		if (!desert && !city)
			col = col.Lerp(new Color(0.44f, 0.43f, 0.235f), Mathf.Clamp((patch - 0.60f) * 1.5f, 0f, 0.5f)); // dry patches
		float l = 0.88f + 0.24f * grain;
		col = new Color(col.R * l, col.G * l, col.B * l);
		float dirtW = Smooth(0.045f, 0.12f, slope + (grain - 0.5f) * 0.05f);
		var dirtC = desert ? new Color(0.60f, 0.46f, 0.29f)
			: city ? new Color(0.42f, 0.385f, 0.335f) // rubble
			: new Color(0.40f, 0.315f, 0.205f);
		col = col.Lerp(dirtC, dirtW * 0.8f);
		float rockW = Smooth(0.13f, 0.28f, slope + (grain - 0.5f) * 0.06f);
		var rock = new Color(0.395f, 0.395f, 0.415f);
		float rl = 0.82f + 0.36f * grain;
		col = col.Lerp(new Color(rock.R * rl, rock.G * rl, rock.B * rl), rockW);
		float peakW = Smooth(13.5f, 19f, h + (grain - 0.5f) * 3f);
		col = col.Lerp(desert ? new Color(0.84f, 0.75f, 0.57f) : new Color(0.72f, 0.71f, 0.685f), peakW * 0.85f);
		// Asphalt on the roads, blended over a soft shoulder.
		float roadD = RoadDistance(x, z);
		if (roadD < 3.1f)
		{
			float rw = 1f - Smooth(2.25f, 3.1f, roadD);
			float al = 0.85f + 0.3f * grain;
			var asphalt = desert ? new Color(0.50f * al, 0.40f * al, 0.27f * al) // packed earth
				: city ? new Color(0.235f * al, 0.235f * al, 0.25f * al)
				: new Color(0.16f * al, 0.16f * al, 0.175f * al);
			if (roadD > 1.85f && roadD < 2.25f)
				asphalt = asphalt.Lerp(desert ? new Color(0.72f, 0.62f, 0.45f) : new Color(0.5f, 0.48f, 0.4f), 0.35f); // worn edge line
			col = col.Lerp(asphalt, rw * 0.94f);
		}
		// District streets: the same dark asphalt on the grid
		// lanes between the building plots.
		float streetD = DistrictStreetDistance(x, z);
		if (streetD < 8f)
		{
			float sw = 1f - Smooth(5.5f, 8f, streetD);
			float al = 0.85f + 0.3f * grain;
			col = col.Lerp(new Color(0.235f * al, 0.235f * al, 0.25f * al), sw * 0.94f);
		}
		return col;
	}

	// Shared terrain mesh: an indexed grid whose vertices sit exactly on
	// heightfield nodes, so the rendered surface and HeightAt agree by
	// construction. One material, vertex-color albedo, no textures.
	public static Mesh CreateTerrainMesh()
	{
		int n = GridN;
		float half = TerrainSize / 2f;
		float step = (float)TerrainSize / TerrainSegs;
		var verts = new Vector3[n * n];
		var normals = new Vector3[n * n];
		var colors = new Color[n * n];
		for (int j = 0; j < n; j++)
		for (int i = 0; i < n; i++)
		{
			float x = -half + i * step, z = -half + j * step;
			float h = Grid[j * n + i];
			int k = j * n + i;
			verts[k] = new Vector3(x, h, z);
			var nrm = GridNormal(i, j, step);
			normals[k] = nrm;
			colors[k] = TerrainColor(x, z, h, nrm);
		}
		var idx = new int[TerrainSegs * TerrainSegs * 6];
		int t = 0;
		for (int j = 0; j < TerrainSegs; j++)
		for (int i = 0; i < TerrainSegs; i++)
		{
			int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
			idx[t++] = a; idx[t++] = b; idx[t++] = c;
			idx[t++] = b; idx[t++] = d; idx[t++] = c;
		}
		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = verts;
		arrays[(int)Mesh.ArrayType.Normal] = normals;
		arrays[(int)Mesh.ArrayType.Color] = colors;
		arrays[(int)Mesh.ArrayType.Index] = idx;
		var mesh = new ArrayMesh();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		mesh.SurfaceSetMaterial(0, new StandardMaterial3D
		{
			VertexColorUseAsAlbedo = true,
			Roughness = 0.95f
		});
		return mesh;
	}
}
