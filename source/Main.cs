using Godot;

public partial class Main : Node3D
{
	// World size comes from the front-end flow (WorldConfig): the
	// 260u Tournament arena, or the 1900u Mission front.
	public static int TerrainSize => WorldConfig.WorldSize;
	public static int TerrainSegs => WorldConfig.TerrainSegs;

	// The three spawn pads / Mission bases, 120 degrees apart.
	// Tournament: r=80 (pairwise ~139u - far beyond first-sight
	// range). Mission: the base triangle at r=620 (~1075u apart).
	// The active map's recipe flattens the ground at each.
	// Index 0 is the player's.
	public static Vector2[] SpawnPads => Maps.Active.Pads;

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
	// The standard terrain's noise seed (owner: one seed for
	// the whole seeded-noise family - Main's relief and the
	// map definitions built on the same fields, e.g. the
	// Harbor Stacks canyon country).
	public const uint NoiseSeed = 20261027u;
	// Built lazily on first use: the flow sets WorldConfig.Mode
	// before the duel scene loads, and whichever mode is current
	// at first access is the world this process runs.
	private static float[] _grid;
	private static float[] Grid => _grid ??= BuildGrid();

	private static float[] BuildGrid()
	{
		var g = new float[GridN * GridN];
		float half = TerrainSize / 2f;
		float step = (float)TerrainSize / TerrainSegs;
		for (int j = 0; j < GridN; j++)
			for (int i = 0; i < GridN; i++)
				g[j * GridN + i] = Maps.Active.RawHeight(-half + i * step, -half + j * step);
		return g;
	}

	// Terrain height: bilinear sample of the heightfield grid (constant
	// outside the world, clamped to the rim). Called constantly by the
	// gait, the hit model and the debris sim - keep it allocation-free.
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

	private static Vector3 GridNormal(int i, int j, float step)
	{
		int il = Mathf.Max(i - 1, 0), ir = Mathf.Min(i + 1, GridN - 1);
		int jd = Mathf.Max(j - 1, 0), ju = Mathf.Min(j + 1, GridN - 1);
		float dhdx = (Grid[j * GridN + ir] - Grid[j * GridN + il]) / ((ir - il) * step);
		float dhdz = (Grid[ju * GridN + i] - Grid[jd * GridN + i]) / ((ju - jd) * step);
		return new Vector3(-dhdx, 1f, -dhdz).Normalized();
	}

	// The terrain palette (owner: look-and-feel values live in
	// named fields, not inline at the lerp sites).
	// Terrain material factory values (the scanned rock
	// detail set's response on the standard terrain).
	private const float TerrainRoughness = 0.95f;
	private const float TerrainUvScale = 0.036f;
	private const float TerrainNormalScale = 0.7f;
	private static readonly Color DesertGroundLow = new(0.70f, 0.565f, 0.335f);
	private static readonly Color DesertGroundHigh = new(0.79f, 0.66f, 0.42f);
	private static readonly Color CityGroundLow = new(0.345f, 0.35f, 0.36f);
	private static readonly Color CityGroundHigh = new(0.41f, 0.405f, 0.395f);
	private static readonly Color ForestGroundLow = new(0.185f, 0.36f, 0.145f);
	private static readonly Color ForestGroundHigh = new(0.36f, 0.485f, 0.21f);
	private static readonly Color DryPatchColor = new(0.44f, 0.43f, 0.235f);
	private static readonly Color DesertDirtColor = new(0.60f, 0.46f, 0.29f);
	private static readonly Color CityDirtColor = new(0.42f, 0.385f, 0.335f); // rubble
	private static readonly Color ForestDirtColor = new(0.40f, 0.315f, 0.205f);
	private static readonly Color RockBaseColor = new(0.395f, 0.395f, 0.415f);
	private static readonly Color DesertPeakColor = new(0.84f, 0.75f, 0.57f);
	private static readonly Color PeakColor = new(0.72f, 0.71f, 0.685f);
	private static readonly Color DesertAsphaltColor = new(0.50f, 0.40f, 0.27f); // packed earth
	private static readonly Color CityAsphaltColor = new(0.235f, 0.235f, 0.25f);
	private static readonly Color ForestAsphaltColor = new(0.16f, 0.16f, 0.175f);
	private static readonly Color DesertRoadEdgeColor = new(0.72f, 0.62f, 0.45f); // worn edge line
	private static readonly Color RoadEdgeColor = new(0.5f, 0.48f, 0.4f);

	// Ground color without any texture image: grass tones varied by two
	// noise fields (patchiness + fine breakup), dirt on mid slopes, rock
	// on steep faces, pale summits up high.
	// The terrain colour recipe (owner, 2026-11-27: every
	// blend value named, and the recipe rounded to tidy steps
	// - the bands and gains below are the whole recipe; the
	// two noise fields' offsets are decorrelation shifts).
	private const float PatchFieldScale = 23f;  // patchiness field size (world units)
	private const float GrainFieldScale = 6.5f; // fine breakup field size
	private const int ColorFieldOctaves = 3;
	private const float DryPatchThreshold = 0.6f, DryPatchGain = 1.5f, DryPatchMax = 0.5f; // dry patches where the patch field runs high
	private const float GrainLightBase = 0.9f, GrainLightAmp = 0.25f;   // per-grain brightness swing
	private const float DirtSlopeStart = 0.05f, DirtSlopeEnd = 0.1f;    // dirt takes over over this slope band (slope = 1 - n.Y)
	private const float DirtGrainWobble = 0.05f, DirtBlend = 0.8f;      // the grain field wobbles the dirt line; dirt never fully covers
	private const float RockSlopeStart = 0.15f, RockSlopeEnd = 0.3f;    // rock takes the steeper band above dirt
	private const float RockGrainWobble = 0.05f;                        // same wobble as dirt's (one value, owner: collapse imperceptible differences)
	private const float RockLightBase = 0.8f, RockLightAmp = 0.35f;     // per-grain rock brightness
	private const float PeakHeightStart = 13.5f, PeakHeightEnd = 19f;   // pale summits over this height band
	private const float PeakGrainWobble = 3f, PeakBlend = 0.85f;
	private const float RoadPaintDistance = 3f;                         // asphalt paints inside this distance of a road centreline
	private const float RoadShoulderStart = 2.5f;                       // the asphalt fades over the outer half-unit shoulder
	private const float AsphaltLightBase = 0.85f, AsphaltLightAmp = 0.3f;
	private const float RoadEdgeStart = 2f, RoadEdgeEnd = 2.5f;         // the worn edge line band
	private const float RoadEdgeBlend = 0.35f, AsphaltBlend = 0.95f;

	public static Color TerrainColor(float x, float z, float h, Vector3 n)
	{
		float slope = 1f - n.Y; // 0 flat, grows with gradient
		float patch = TerrainMath.Fbm(x / PatchFieldScale - 47f, z / PatchFieldScale + 5f, ColorFieldOctaves, NoiseSeed) * 0.5f + 0.5f;
		float grain = TerrainMath.Fbm(x / GrainFieldScale + 31f, z / GrainFieldScale - 11f, ColorFieldOctaves, NoiseSeed) * 0.5f + 0.5f;
		// Biome palettes (owner's map types): Forest keeps the
		// original greens; Desert runs sand; City runs urban
		// concrete. The city tint varies SMOOTHLY (the patch
		// field): the old per-block quantised tint - Floor(x/26)
		// hashed per 26u block - read as a checkerboard over
		// every city floor, worst where no buildings cover it
		// (owner, 2026-11-11 round 5: 'checkerboard needs to
		// be removed everywhere').
		bool desert = WorldConfig.Biome == WorldConfig.BiomeKind.Desert;
		bool city = WorldConfig.Biome == WorldConfig.BiomeKind.City;
		var col = desert
			? DesertGroundLow.Lerp(DesertGroundHigh, patch)
			: city
				? CityGroundLow.Lerp(CityGroundHigh, patch)
				: ForestGroundLow.Lerp(ForestGroundHigh, patch);
		if (!desert && !city)
			col = col.Lerp(DryPatchColor, Mathf.Clamp((patch - DryPatchThreshold) * DryPatchGain, 0f, DryPatchMax)); // dry patches
		float l = GrainLightBase + GrainLightAmp * grain;
		col = new Color(col.R * l, col.G * l, col.B * l);
		float dirtW = TerrainMath.Smooth(DirtSlopeStart, DirtSlopeEnd, slope + (grain - 0.5f) * DirtGrainWobble);
		var dirtC = desert ? DesertDirtColor
			: city ? CityDirtColor
			: ForestDirtColor;
		col = col.Lerp(dirtC, dirtW * DirtBlend);
		float rockW = TerrainMath.Smooth(RockSlopeStart, RockSlopeEnd, slope + (grain - 0.5f) * RockGrainWobble);
		var rock = RockBaseColor;
		float rl = RockLightBase + RockLightAmp * grain;
		col = col.Lerp(new Color(rock.R * rl, rock.G * rl, rock.B * rl), rockW);
		float peakW = TerrainMath.Smooth(PeakHeightStart, PeakHeightEnd, h + (grain - 0.5f) * PeakGrainWobble);
		col = col.Lerp(desert ? DesertPeakColor : PeakColor, peakW * PeakBlend);
		// Asphalt on the roads, blended over a soft shoulder.
		float roadD = Maps.Active.RoadDistance(x, z);
		if (roadD < RoadPaintDistance)
		{
			float rw = 1f - TerrainMath.Smooth(RoadShoulderStart, RoadPaintDistance, roadD);
			float al = AsphaltLightBase + AsphaltLightAmp * grain;
			var asphaltBase = desert ? DesertAsphaltColor
				: city ? CityAsphaltColor
				: ForestAsphaltColor;
			var asphalt = new Color(asphaltBase.R * al, asphaltBase.G * al, asphaltBase.B * al);
			if (roadD > RoadEdgeStart && roadD < RoadEdgeEnd)
				asphalt = asphalt.Lerp(desert ? DesertRoadEdgeColor : RoadEdgeColor, RoadEdgeBlend); // worn edge line
			col = col.Lerp(asphalt, rw * AsphaltBlend);
		}
		// No district street darkening (owner, 2026-11-11
		// round 5: checkerboard removed everywhere): the grid
		// lanes used to darken against the plots, which read
		// as a checkerboard on open district ground. The
		// pad-to-pad roads above keep their asphalt.
		return col;
	}

	// Shared terrain mesh: an indexed grid whose vertices sit exactly on
	// heightfield nodes, so the rendered surface and HeightAt agree by
	// construction. One material, vertex-color albedo, no textures.
	public static Mesh CreateTerrainMesh()
		=> CreateTerrainMeshRange(0, TerrainSegs, 0, TerrainSegs);

	// One window of the heightfield as its own mesh (same
	// grid data, world-space vertices, vertex colours) -
	// a chunked map (MapDef.TerrainChunks) builds the ground
	// in windows so far chunks can be unloaded from the
	// renderer (owner, 2026-11-20: dynamic loading on the
	// big map).
	public static Mesh CreateTerrainMeshRange(int i0, int i1, int j0, int j1)
	{
		int n = GridN;
		float half = TerrainSize / 2f;
		float step = (float)TerrainSize / TerrainSegs;
		int w = i1 - i0 + 1, dpt = j1 - j0 + 1;
		var verts = new Vector3[w * dpt];
		var normals = new Vector3[w * dpt];
		var colors = new Color[w * dpt];
		for (int j = j0; j <= j1; j++)
		for (int i = i0; i <= i1; i++)
		{
			float x = -half + i * step, z = -half + j * step;
			float h = Grid[j * n + i];
			int k = (j - j0) * w + (i - i0);
			verts[k] = new Vector3(x, h, z);
			normals[k] = GridNormal(i, j, step);
			colors[k] = TerrainColor(x, z, h, normals[k]);
		}
		var idx = new int[(i1 - i0) * (j1 - j0) * 6];
		int t = 0;
		for (int j = 0; j < j1 - j0; j++)
		for (int i = 0; i < i1 - i0; i++)
		{
			int a = j * w + i, b = a + 1, c = a + w, dd = c + 1;
			idx[t++] = a; idx[t++] = b; idx[t++] = c;
			idx[t++] = b; idx[t++] = dd; idx[t++] = c;
		}
		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = verts;
		arrays[(int)Mesh.ArrayType.Normal] = normals;
		arrays[(int)Mesh.ArrayType.Color] = colors;
		arrays[(int)Mesh.ArrayType.Index] = idx;
		var mesh = new ArrayMesh { ResourceName = "Terrain" };
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		EnsureDetailMaps();
		var tmat = new StandardMaterial3D
		{
			VertexColorUseAsAlbedo = true,
			Roughness = TerrainRoughness,
			Uv1Triplanar = true,
			Uv1WorldTriplanar = true,
			Uv1Scale = new Vector3(TerrainUvScale, TerrainUvScale, TerrainUvScale),
		};
		if (_rockNor != null)
		{
			tmat.NormalTexture = _rockNor;
			tmat.NormalScale = TerrainNormalScale;
		}
		if (_rockArm != null)
		{
			tmat.RoughnessTexture = _rockArm;
			tmat.RoughnessTextureChannel = BaseMaterial3D.TextureChannel.Green;
			tmat.Roughness = 1.0f;
		}
		mesh.SurfaceSetMaterial(0, tmat);
		return mesh;
	}

	// PBR detail for the ground (owner, 2026-11-20): the
	// first attempt generated a synthetic noise normal +
	// roughness pair - it read flat, then tiled in a visible
	// grid, then (domain-warped) showed swirl and diamond-
	// quilt artifacts of its own; dumped and inspected, the
	// texture itself was the defect. Replaced with the real
	// scanned rock set the photo scene already ships
	// (aerial_rocks_02: normal + ARM), sampled world-
	// triplanar so the detail is continuous across the
	// terrain chunks. Vertex colours stay the albedo;
	// the maps only carry the light response.
	private static Texture2D _rockNor, _rockArm;

	private static void EnsureDetailMaps()
	{
		if (_rockNor != null) return;
		_rockNor = ResourceLoader.Load<Texture2D>(
			Paths.TerrainNormal);
		_rockArm = ResourceLoader.Load<Texture2D>(
			Paths.TerrainArm);
	}
}
