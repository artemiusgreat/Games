// Winter dressing (owner, 2026-11-23): on snow worlds,
// walking leaves footprints in the snow and snow falls
// from the sky. Both effects gate on WorldConfig.SnowGround
// and stay completely inert elsewhere.
using Godot;
using System.Collections.Generic;

namespace Titans;

public partial class Titans
{
  // ---- Footprints ----
  // A ring pool of soft dark prints stamped at each foot
  // plant (the walker counts plants in Steps, the same
  // signal the stomp sounds poll). Prints live ~75 s and
  // fade over the last 20 s. Only plants within 500u of the
  // player stamp - prints are a near-field detail.
  private const int PrintPool = 384;
  private const float PrintLife = 75f;
  private const float PrintFadeSeconds = 20f;      // prints fade over their last 20 s
  private const float PrintMaxAlpha = 0.55f;
  private const float PrintStampRange = 500f;      // only plants within this of the player stamp
  private const float PrintLift = 0.12f;           // stamp offset along the terrain normal
  private const float SnowfallFollowHeight = 4f;   // snowfall box rides this far above the camera
                                                   // Snowfall particle recipe (the falling-snow box).
  private const float SnowEmissionHalfWidth = 55f, SnowEmissionHalfHeight = 26f;
  private const float SnowDriftX = 0.25f, SnowDriftZ = 0.1f; // fall direction tilt
  private const float SnowSpreadDeg = 10f;
  private const float SnowFallSpeedMin = 2.2f, SnowFallSpeedMax = 3.4f;
  private const float SnowGravityY = -0.55f;
  private const float SnowFlakeScaleMin = 0.7f, SnowFlakeScaleMax = 1.4f;
  private const float FlakeQuadSize = 0.11f;
  private const int SnowAmount = 1400;
  private const float SnowLifetime = 7f;
  private const float SnowVisHalfExtent = 70f, SnowVisBelow = 35f; // visibility box around the box
  private const float PrintGradientStep = 1.5f;    // terrain-normal sampling distance for a print
  private const float MinPrintForwardSq = 0.01f;   // squared forward length below which a print has no direction
  private static readonly Vector2 PrintPlaneSize = new(1.8f, 2.7f);
  // Footprint stamp canvas and shape recipe (owner,
  // 2026-11-27): the toe/arch/heel rounded-rect shapes in
  // UV space (centre, half-size, corner radius), the
  // arch's lighter contact, the soft rim, and the toe's
  // tread grooves.
  private const int PrintTextureWidth = 96, PrintTextureHeight = 144;
  private static readonly Vector2 PrintToeCenter = new(0.5f, 0.30f);
  private static readonly Vector2 PrintToeHalfSize = new(0.31f, 0.235f);
  private const float PrintToeRadius = 0.10f;
  private static readonly Vector2 PrintArchCenter = new(0.5f, 0.545f);
  private static readonly Vector2 PrintArchHalfSize = new(0.155f, 0.075f);
  private const float PrintArchRadius = 0.06f;
  private static readonly Vector2 PrintHeelCenter = new(0.5f, 0.775f);
  private static readonly Vector2 PrintHeelHalfSize = new(0.235f, 0.155f);
  private const float PrintHeelRadius = 0.08f;
  private const float PrintArchLighten = 0.012f;   // SDF offset: the arch sits lighter (less contact)
  private const float PrintEdgeSoftness = 0.055f;  // SDF range the stamp's rim fades over
  private const float PrintGrooveInset = -0.015f;  // grooves only well inside the toe pad
  private const float PrintGrooveY1 = 0.185f, PrintGrooveY2 = 0.415f; // groove rows across the toe
  private const float PrintGrooveHalfWidth = 0.016f;
  private const float PrintGrooveDim = 0.45f;      // alpha factor inside a groove
                                                   // Snowflake recipe (owner, 2026-11-27): arm length and
                                                   // width with their jitter ranges, the branch geometry,
                                                   // the broken arm's gap, the melt specks, and the alpha
                                                   // construction. The LCG constants in FlakeTexture stay
                                                   // inline - they ARE the generator algorithm.
  private const float FlakeArmLength = 0.92f;
  private const float FlakeArmLengthMin = 0.74f, FlakeArmLengthRange = 0.26f; // per-arm length jitter
  private const float FlakeArmAngleJitter = 0.24f;
  private const float FlakeArmBaseWidth = 0.050f;
  private const float FlakeArmWidthMin = 0.8f, FlakeArmWidthRange = 0.4f; // per-arm width jitter (fractions)
  private const float FlakeArmTipWidth = 0.016f;
  private const float FlakeBrokenTaperStart = 0.8f, FlakeBrokenTaperEnd = 0.62f; // broken-arm width, fractions of the base width
  private const float FlakeBrokenGapStart = 0.34f, FlakeBrokenGapEnd = 0.58f; // the melted gap, fractions of the arm length
  private const float FlakeBrokenTipWidth = 0.014f;
  private const float FlakeBranchT = 0.52f, FlakeBranchTJitter = 0.1f;   // lone branch pair's position along the arm
  private const float FlakeBranchTStart = 0.40f, FlakeBranchTStep = 0.24f, FlakeBranchTJitter2 = 0.08f; // two-pair positions
  private const float FlakeBranchLengthGain = 0.52f, FlakeBranchLengthRange = 0.22f;
  private const float FlakeBranchAngle = 0.96f, FlakeBranchAngleJitter = 0.3f;
  private const float FlakeBranchBaseWidth = 0.030f, FlakeBranchTipWidth = 0.007f;
  private const float FlakeSnappedBranchFactor = 0.4f; // the one snapped branch's length factor
  private const int FlakeSpeckCount = 4;
  private const float FlakeSpeckRadiusMin = 0.016f, FlakeSpeckRadiusRange = 0.02f;
  private const float FlakeEdgeSoftness = 0.6f;    // alpha fade range, as a fraction of the local thickness
  private const float FlakePeakAlpha = 0.30f;
  private const float FlakeCoreRadius = 0.16f, FlakeCoreAlpha = 0.18f; // the centre knot
  private static readonly Color PrintColor = new(0.40f, 0.48f, 0.68f, 0f); // alpha driven per-frame by the fade
  private static readonly Color FlakeColor = new(1f, 1f, 1f, 0.95f);
  private static readonly Color FlakeTint = new(0.96f, 0.98f, 1f); // flake RGB; alpha comes from the distance field
  private MeshInstance3D[] _prints;
  private StandardMaterial3D[] _printMats;
  private float[] _printAge;
  private int _printHead;
  private readonly Dictionary<RiderWalker, int> _printStepsSeen = new();
  private float _snowClock;

  // ---- Falling snow ----
  private GpuParticles3D _snowfall;

  // Signed-distance rounded rect (p, centre, half-size,
  // corner radius) for the stamp shapes below.
  private static float RRect(Vector2 p, Vector2 c, Vector2 h, float r)
  {
    var q = new Vector2(Mathf.Abs(p.X - c.X), Mathf.Abs(p.Y - c.Y)) - (h - new Vector2(r, r));
    float ax = Mathf.Max(q.X, 0f), ay = Mathf.Max(q.Y, 0f);
    return Mathf.Sqrt(ax * ax + ay * ay) + Mathf.Min(Mathf.Max(q.X, q.Y), 0f) - r;
  }

  private static ImageTexture PrintTexture()
  {
    // Abstract mech-foot stamp (owner, 2026-11-23: the
    // plain oval read as a smudge and clipped on slopes):
    // a wide toe pad with two tread grooves, a narrow
    // arch, and a heel block - one stylised shape that
    // suits every chassis's foot. Canvas top (v=0) is the
    // toe: the print basis puts v=0 toward the foot's
    // forward. Alpha comes from the shapes' signed
    // distance, so the rim is soft and a corner sunk into
    // a slope fades instead of slicing the shape.
    var img = Image.CreateEmpty(PrintTextureWidth, PrintTextureHeight, false, Image.Format.Rgba8);
    for (int y = 0; y < PrintTextureHeight; y++)
    {
      for (int x = 0; x < PrintTextureWidth; x++)
      {
        var p = new Vector2((x + 0.5f) / PrintTextureWidth, (y + 0.5f) / PrintTextureHeight);
        float dToe = RRect(p, PrintToeCenter, PrintToeHalfSize, PrintToeRadius);
        float dArch = RRect(p, PrintArchCenter, PrintArchHalfSize, PrintArchRadius);
        float dHeel = RRect(p, PrintHeelCenter, PrintHeelHalfSize, PrintHeelRadius);
        float d = Mathf.Min(Mathf.Min(dToe, dHeel), dArch + PrintArchLighten); // arch sits lighter (less contact)
        float a = Mathf.Clamp(0.5f - d / PrintEdgeSoftness, 0f, 1f);
        a = a * a * (3f - 2f * a);
        // Tread grooves across the toe pad.
        if (dToe < PrintGrooveInset)
        {
          float g1 = Mathf.Abs(p.Y - PrintGrooveY1), g2 = Mathf.Abs(p.Y - PrintGrooveY2);
          if (Mathf.Min(g1, g2) < PrintGrooveHalfWidth)
          {
            a *= PrintGrooveDim;
          }
        }
        img.SetPixel(x, y, new Color(1f, 1f, 1f, a));
      }
    }

    return ImageTexture.CreateFromImage(img);
  }

  private void InitSnowFx()
  {
    var tex = PrintTexture();
    this._prints = new MeshInstance3D[PrintPool];
    this._printMats = new StandardMaterial3D[PrintPool];
    this._printAge = new float[PrintPool];
    var mesh = new PlaneMesh { Size = PrintPlaneSize };
    for (int i = 0; i < PrintPool; i++)
    {
      var mat = new StandardMaterial3D
      {
        AlbedoTexture = tex,
        // Shaded, a compressed-snow blue-grey darker
        // than the field: the unshaded pale oval read
        // as a blob pasted ON the snow (owner,
        // 2026-11-23); a depression reads darker and
        // takes the scene's light.
        AlbedoColor = PrintColor,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        NoDepthTest = false,
      };
      this._printMats[i] = mat;
      this._printAge[i] = PrintLife + 1f;
      this._prints[i] = new MeshInstance3D
      {
        Mesh = mesh,
        MaterialOverride = mat,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        Visible = false,
      };
      this.AddChild(this._prints[i]);
    }

    // Falling snow: a world-space particle box that
    // follows the camera, so the fall reads everywhere
    // the player looks without snowing inside the lab.
    var pm = new ParticleProcessMaterial
    {
      EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
      EmissionBoxExtents = new Vector3(SnowEmissionHalfWidth, SnowEmissionHalfHeight, SnowEmissionHalfWidth),
      Direction = new Vector3(SnowDriftX, -1f, SnowDriftZ),
      Spread = SnowSpreadDeg,
      InitialVelocityMin = SnowFallSpeedMin,
      InitialVelocityMax = SnowFallSpeedMax,
      Gravity = new Vector3(0f, SnowGravityY, 0f),
      ScaleMin = SnowFlakeScaleMin,
      ScaleMax = SnowFlakeScaleMax,
    };
    var flake = new QuadMesh
    {
      Size = new Vector2(FlakeQuadSize, FlakeQuadSize),
      Material = new StandardMaterial3D
      {
        AlbedoTexture = FlakeTexture(),
        AlbedoColor = FlakeColor,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
      },
    };
    this._snowfall = new GpuParticles3D
    {
      Amount = SnowAmount,
      Lifetime = SnowLifetime,
      LocalCoords = false,
      ProcessMaterial = pm,
      DrawPass1 = flake,
      VisibilityAabb = new Aabb(-SnowVisHalfExtent, -SnowVisBelow, -SnowVisHalfExtent, SnowVisHalfExtent * 2f, SnowVisBelow * 2f, SnowVisHalfExtent * 2f),
      CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };
    this.AddChild(this._snowfall);
  }

  private void StampPrint(RiderWalker w)
  {
    Vector3 pos = w.LastPlantPos;
    if (this._player != null && pos.DistanceTo(this._player.RootPos) > PrintStampRange)
    {
      return;
    }
    // Terrain normal from the height gradient.
    float e = PrintGradientStep;
    float hx = Main.HeightAt(pos.X + e, pos.Z) - Main.HeightAt(pos.X - e, pos.Z);
    float hz = Main.HeightAt(pos.X, pos.Z + e) - Main.HeightAt(pos.X, pos.Z - e);
    var n = new Vector3(-hx / (2f * e), 1f, -hz / (2f * e)).Normalized();
    var fwd = -w.LastPlantBasis.Z;
    fwd = (fwd - n * fwd.Dot(n)).Normalized();
    if (fwd.LengthSquared() < MinPrintForwardSq)
    {
      return; // foot pointing straight down - no sane print
    }

    var xAxis = n.Cross(-fwd).Normalized();
    var zAxis = xAxis.Cross(n).Normalized();
    var basis = new Basis(xAxis, n, zAxis);
    int i = this._printHead;
    this._printHead = (this._printHead + 1) % PrintPool;
    this._printAge[i] = 0f;
    this._prints[i].GlobalTransform = new Transform3D(basis, pos + n * PrintLift); // 0.05 clipped corners into slopes (owner, 2026-11-23)
    this._prints[i].Visible = true;
  }

  // Per-frame snow update, beside StepSounds.
  private void StepSnowFx(float dt)
  {
    if (!WorldConfig.SnowGround)
    {
      return;
    }

    if (this._prints == null)
    {
      this.InitSnowFx();
    }

    this._snowClock += dt;

    foreach (var w in this.SfxWalkers())
    {
      if (w == null || !w.IsReady || this.IsDead(w))
      {
        continue;
      }

      if (!this._printStepsSeen.TryGetValue(w, out int seen)) { this._printStepsSeen[w] = w.Steps; continue; }
      if (w.Steps == seen)
      {
        continue;
      }

      this._printStepsSeen[w] = w.Steps;
      this.StampPrint(w);
    }

    for (int i = 0; i < PrintPool; i++)
    {
      if (this._printAge[i] > PrintLife) { if (this._prints[i].Visible) { this._prints[i].Visible = false; } continue; }
      this._printAge[i] += dt;
      float fade = Mathf.Clamp((PrintLife - this._printAge[i]) / PrintFadeSeconds, 0f, 1f);
      var c = this._printMats[i].AlbedoColor;
      c.A = PrintMaxAlpha * fade;
      this._printMats[i].AlbedoColor = c;
    }

    if (this._snowfall != null)
    {
      this._snowfall.Emitting = !this._labOpen;
      if (this._cam != null)
      {
        this._snowfall.GlobalPosition = this._cam.GlobalPosition + Vector3.Up * SnowfallFollowHeight;
      }
    }
  }
  // A six-armed dendrite snowflake, drawn as a distance
  // field like the footprint stamp: 6 arms, two branch
  // pairs per arm at ~55 degrees, thickness tapering
  // toward every tip, a small centre knot. Semi-
  // transparent by construction (peak alpha ~0.30, soft
  // edges). The old draw pass was a tiny sphere, which
  // rendered as a plain grey disc (owner, 2026-11-23:
  // "just a plain circle with no shape"). Third pass the
  // same day: smaller and fainter still - at 0.11u across
  // and a third of the alpha, a flake is a drifting speck
  // whose shape cannot be made out, as it should be.
  private static ImageTexture FlakeTexture()
  {
    const int S = 96;
    var img = Image.CreateEmpty(S, S, false, Image.Format.Rgba8);
    var segs = new System.Collections.Generic.List<(Vector2 A, Vector2 B, float T0, float T1)>();
    // Owner (2026-11-23, second pass): the perfect sixfold
    // dendrite read as a printed icon - too regular to be
    // weather. A real flake is ALMOST regular: arms of
    // uneven length, branch pairs that come and go, one arm
    // broken by a melted gap, specks that came off it. All
    // jitter comes from a fixed-seed LCG, so it is the same
    // imperfect flake every run. Peak alpha 0.55 (was 0.92):
    // more transparent, per the same note.
    uint rng = 20261123u;
    float Rand() { rng = rng * 1664525u + 1013904223u; return (rng >> 8) / 16777216f; }
    int[] pairs = { 2, 1, 2, 0, 2, 1 };
    for (int k = 0; k < 6; k++)
    {
      float a = k * Mathf.Pi / 3f + (Rand() - 0.5f) * FlakeArmAngleJitter;
      var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
      var side = new Vector2(-dir.Y, dir.X);
      float len = FlakeArmLength * (FlakeArmLengthMin + FlakeArmLengthRange * Rand());
      float w0 = FlakeArmBaseWidth * (FlakeArmWidthMin + FlakeArmWidthRange * Rand());
      if (k == 3)
      {
        // the broken arm: a melted gap mid-shaft
        segs.Add((Vector2.Zero, dir * (len * FlakeBrokenGapStart), w0, w0 * FlakeBrokenTaperStart));
        segs.Add((dir * (len * FlakeBrokenGapEnd), dir * len, w0 * FlakeBrokenTaperEnd, FlakeBrokenTipWidth));
      }
      else
      {
        segs.Add((Vector2.Zero, dir * len, w0, FlakeArmTipWidth));
      }

      for (int b = 0; b < pairs[k]; b++)
      {
        float t = pairs[k] == 1 ? FlakeBranchT + (Rand() - 0.5f) * FlakeBranchTJitter
                                : FlakeBranchTStart + FlakeBranchTStep * b + (Rand() - 0.5f) * FlakeBranchTJitter2;
        var bas = dir * (len * t);
        float bl = len * (1f - t) * (FlakeBranchLengthGain + FlakeBranchLengthRange * Rand()) / FlakeArmLength;
        foreach (float sgn in new[] { -1f, 1f })
        {
          float ba = FlakeBranchAngle + (Rand() - 0.5f) * FlakeBranchAngleJitter;
          var bd = (dir * Mathf.Cos(ba) + side * (sgn * Mathf.Sin(ba))).Normalized();
          float l2 = (k == 1 && b == 0 && sgn > 0f) ? bl * FlakeSnappedBranchFactor : bl; // one snapped branch
          segs.Add((bas, bas + bd * l2, FlakeBranchBaseWidth, FlakeBranchTipWidth));
        }
      }
    }
    // melt specks drifting off the flake
    for (int i = 0; i < FlakeSpeckCount; i++)
    {
      float a = Rand() * Mathf.Tau, d = FlakeArmLength * (0.5f + 0.5f * Rand());
      var c = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d;
      float r = FlakeSpeckRadiusMin + FlakeSpeckRadiusRange * Rand();
      segs.Add((c, c, r, r));
    }
    for (int y = 0; y < S; y++)
    {
      for (int x = 0; x < S; x++)
      {
        var p = new Vector2((x + 0.5f) / S * 2f - 1f, (y + 0.5f) / S * 2f - 1f);
        float best = 1e9f, thick = 0.01f;
        foreach (var (A, B, T0, T1) in segs)
        {
          var ab = B - A;
          float ls = ab.LengthSquared();
          float t = ls > 1e-6f ? Mathf.Clamp((p - A).Dot(ab) / ls, 0f, 1f) : 0f;
          float d = (p - (A + ab * t)).Length();
          if (d < best) { best = d; thick = Mathf.Lerp(T0, T1, t); }
        }
        float core = Mathf.Clamp(1f - p.Length() / FlakeCoreRadius, 0f, 1f);
        float alpha = Mathf.Clamp((thick - best) / (thick * FlakeEdgeSoftness), 0f, 1f) * FlakePeakAlpha;
        alpha = Mathf.Max(alpha, core * FlakeCoreAlpha);
        img.SetPixel(x, y, new Color(FlakeTint, alpha));
      }
    }

    return ImageTexture.CreateFromImage(img);
  }

}
