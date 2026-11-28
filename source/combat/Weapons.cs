using Godot;

namespace Titans;

// Weapon core: projectiles stepped with swept segment tests against the
// shared HitModel, plus cheap one-node FX factories (beams, flashes,
// tracers, rockets, trail puffs). Titans owns the instances and their
// lifetimes; this file only knows how a single shot moves and looks.

// A ballistic projectile (autocannon shell or rocket). The owner sets
// position/velocity, then calls Step every frame; on the first hit along
// the swept segment the projectile dies and OnHit fires exactly once.
public partial class Projectile : Node3D
{
  public Vector3 Vel;
  public float Gravity = 5f;
  public float Damage = 10f;
  public float SplashRadius = 0f; // 0 = direct damage only
  public string OwnerId = "";
  public float WeaponWeight; // firing weapon's tonnage (knockdown-by-weight)
  public float Life = 8f;
  public bool Dead;
  public System.Action<HitModel.Result, Projectile> OnHit;
  public System.Action<Projectile> OnStep; // per-step hook (rocket trail)

  // Opening guidance window (axis convergence, 2026-11-13): the
  // rocket was SOLVED from a virtual muzzle on the aim axis
  // (GuideOrigin, GuideVel0) but physically launched from a pod
  // off that axis (GuideStart). While guided, the position is an
  // explicit smoothstep blend between the pod-launched arc and
  // the axis arc - the two differ by the constant offset
  // GuideOrigin - GuideStart, so the blend is the pod arc shifted
  // by beta * offset - and the velocity is that blend's exact
  // derivative. At the end of the window the rocket sits ON the
  // solved arc with the arc's own velocity, so the ballistic
  // continuation is the exact solution (a velocity-chasing
  // controller was simulated first and rejected: its handoff
  // velocity error integrated into a ~4.4u miss at the target).
  // Rockets only; shells never set GuideActive.
  public bool GuideActive;
  public Vector3 GuideOrigin; // virtual muzzle on the aim axis
  public Vector3 GuideStart;  // physical launch point (the pod)
  public Vector3 GuideVel0;   // the solved arc's launch velocity
  public float GuideTime;
  public float GuideDur = 0.55f;
  // Homing (owner, 2026-11-25): an LRM salvo fired with
  // the reticle ON an enemy locks that rider. GuideTrack
  // returns its live chest point (null once it dies), and
  // the arc re-solves from the rocket's current position
  // whenever that point has moved - the salvo converges
  // on the ENEMY, not the spot the reticle rested on at
  // fire time. SRM never locks.
  // Guidance/step tuning (owner's rules: named, exact values).
  private const float GuideResolveDistSq = 0.0225f; // re-solve when the tracked point moved > 0.15u (squared)
  private const float GuideMinTof = 0.05f;          // below this time-of-flight the salvo flies out its arc
  private const float GuideMinDur = 0.3f;           // re-solved blend window floor
  private const float GuideDurFraction = 0.8f;      // blend window = this fraction of the remaining flight
  private const float StepMinLength = 1e-5f;        // shorter steps skip the swept test
  private const float OrientMinSpeedSq = 0.01f;     // below 0.1 u/s the node keeps its facing (squared)
  private const float OrientMinDistSq = 1e-6f;      // look-target must differ from the position (squared)

  public System.Func<Vector3?> GuideTrack;
  public Vector3 GuideEnd;    // the point the current solve runs through
  public float GuideBaseSpeed = 36f;
  public float GuideTofMax = 2.2f;

  public void Step(float dt, HitModel hm)
  {
    if (this.Dead)
    {
      return;
    }

    this.Life -= dt;
    Vector3 to;
    if (this.GuideActive)
    {
      // Gravity is inside the arc model here, so the plain
      // decrement below is skipped while guided; the swept
      // segment test below runs on the blend's step exactly
      // as it does for a ballistic step.
      // The track check runs BEFORE the clock advance
      // (owner's LRM report, 2026-11-25): a re-solve
      // re-anchors the arc at the rocket's position and
      // zeroes GuideTime - when the target moves more
      // than the 0.15u threshold in one step (a running
      // rider at frame rate, or any target at a low
      // frame rate), the re-solve fired EVERY step and
      // the increment was discarded before the position
      // formula ran, so the salvo hung frozen at the
      // pods for its whole Life. Measured by the
      // instrumented --lrm-probe: GuideTime 0.00 at
      // age 45, positions bit-identical to the muzzles.
      if (this.GuideTrack != null)
      {
        var tp = this.GuideTrack();
        if (tp == null)
        {
          this.GuideTrack = null; // target gone: the current arc finishes at the last solved point
        }
        else if (tp.Value.DistanceSquaredTo(this.GuideEnd) > GuideResolveDistSq) // moved > 0.15u: re-solve from here
        {
          this.GuideEnd = tp.Value;
          float distNow = this.GlobalPosition.DistanceTo(this.GuideEnd);
          float tofNow = Mathf.Min(distNow / this.GuideBaseSpeed, this.GuideTofMax);
          if (tofNow < GuideMinTof)
          {
            this.GuideTrack = null; // on top of it: fly out the arc
          }
          else
          {
            this.GuideStart = this.GlobalPosition;
            this.GuideOrigin = this.GlobalPosition;
            this.GuideTime = 0f;
            this.GuideVel0 = (this.GuideEnd - this.GlobalPosition) / tofNow + Vector3.Up * (0.5f * this.Gravity * tofNow);
            this.GuideDur = Mathf.Max(GuideMinDur, tofNow * GuideDurFraction);
          }
        }
      }
      this.GuideTime += dt;
      float u = Mathf.Clamp(this.GuideTime / this.GuideDur, 0f, 1f);
      float beta = u * u * (3f - 2f * u);
      float betaDeriv = this.GuideTime < this.GuideDur ? 6f * u * (1f - u) / this.GuideDur : 0f;
      var delta = this.GuideOrigin - this.GuideStart;
      to = this.GuideStart + this.GuideVel0 * this.GuideTime
          + Vector3.Down * (0.5f * this.Gravity * this.GuideTime * this.GuideTime)
          + delta * beta;
      this.Vel = this.GuideVel0 + Vector3.Down * (this.Gravity * this.GuideTime) + delta * betaDeriv;
      if (this.GuideTime >= this.GuideDur)
      {
        this.GuideActive = false;
      }
    }
    else
    {
      this.Vel.Y -= this.Gravity * dt;
      to = this.GlobalPosition + this.Vel * dt;
    }
    var from = this.GlobalPosition;
    float len = from.DistanceTo(to);
    if (len > StepMinLength)
    {
      var hit = hm.Ray(from, (to - from) / len, len, this.OwnerId);
      if (hit.Hit)
      {
        this.GlobalPosition = hit.Point;
        this.Dead = true;
        this.OnHit?.Invoke(hit, this);
        return;
      }
    }
    this.GlobalPosition = to;
    if (this.Vel.LengthSquared() > OrientMinSpeedSq)
    {
      var target = to + this.Vel;
      if (this.GlobalPosition.DistanceSquaredTo(target) > OrientMinDistSq)
      {
        this.LookAt(target, Vector3.Up);
      }
    }
    this.OnStep?.Invoke(this);
    if (this.Life <= 0f)
    {
      this.Dead = true;
    }
  }
}

// One-node visual factories. Unshaded materials read at llvmpipe/capture
// distances and keep the FX cost at one mesh each.
public static class WeaponFx
{
  private static StandardMaterial3D Unshaded(Color c, bool alpha = false)
  {
    var m = new StandardMaterial3D
    {
      ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
      AlbedoColor = c
    };
    if (alpha)
    {
      m.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
    }

    return m;
  }

  private static readonly Color TracerCoreColor = new(0.23f, 0.16f, 0.07f); // dark brass slug
  private static readonly Color TracerEmissionColor = new(1f, 0.38f, 0.06f);
  private static readonly Color TracerGlowColor = new(1f, 0.5f, 0.1f, 0.3f);
  private static readonly Color RocketBodyColor = new(0.62f, 0.62f, 0.66f);
  private static readonly Color RocketTipColor = new(1f, 0.45f, 0.1f);
  private static readonly Color TrailPuffColor = new(0.9f, 0.9f, 0.92f, 0.45f);

  // FX recipe values (owner's rules: named, exact values).
  private const float BeamMinLength = 0.1f;         // a beam never collapses below this
  private const float OpaqueAlpha = 0.99f;          // at/above this alpha the beam skips transparency
  private const float TracerMetallic = 0.8f;
  private const float TracerRoughness = 0.32f;
  private const float TracerEmissionEnergy = 0.35f;
  private const float TracerRadius = 0.075f;
  private const float TracerBodyLength = 0.62f;
  private const float TracerTipLength = 0.3f;
  private const float TracerTipOffsetZ = -0.46f;    // seats the tip against the body end
  private const int TracerRadialSegments = 8;
  private const float TracerGlowRadius = 0.09f;
  private const int TracerGlowRings = 4;
  private const float TracerGlowStretch = 8f;       // slim streak: 0.18 wide, 1.44 long
  private const float RocketBodyRoughness = 0.5f;
  private const float RocketRadius = 0.09f;
  private const float RocketBodyLength = 0.72f;
  private const float RocketTipLength = 0.3f;
  private const float RocketTipOffsetZ = -0.5f;
  private const float TrailPuffSize = 0.14f;

  private static StandardMaterial3D _tracerCoreMat;
  private static StandardMaterial3D _tracerGlowMat;
  private static StandardMaterial3D _rocketBodyMat;
  private static StandardMaterial3D _rocketTipMat;

  // Laser beam: thin box stretched from a to b. Own material instance so
  // the owner can fade it over its short life.
  public static MeshInstance3D MakeBeam(Vector3 a, Vector3 b, Color core, float width)
  {
    var mi = new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    float len = a.DistanceTo(b);
    mi.Mesh = new BoxMesh { Size = new Vector3(width, width, Mathf.Max(len, BeamMinLength)) };
    mi.MaterialOverride = Unshaded(core, alpha: core.A < OpaqueAlpha);
    var dir = (b - a).Normalized();
    var basis = Basis.LookingAt(dir, Vector3.Up);
    mi.GlobalTransform = new Transform3D(basis, (a + b) * 0.5f);
    return mi;
  }

  // Impact / muzzle flash: one unshaded sphere.
  public static MeshInstance3D MakeFlash(Vector3 point, Color col, float radius)
  {
    var mi = new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    mi.Mesh = new SphereMesh { Radius = radius, Height = radius * 2f };
    mi.MaterialOverride = Unshaded(col);
    mi.GlobalTransform = new Transform3D(Basis.Identity, point);
    return mi;
  }

  // Bullet (owner: the old tracer read as a yellow ball): a
  // slim metal slug - cylindrical body plus a conical nose,
  // dark brass so it silhouettes like the old core - wrapped
  // in a THIN tracer halo stretched along the flight line
  // instead of the fat glow ellipsoid. Axis = -Z fwd; the
  // projectile node LookAts along its velocity.
  public static Node3D MakeTracer()
  {
    _tracerCoreMat ??= new StandardMaterial3D
    {
      AlbedoColor = TracerCoreColor,
      Metallic = TracerMetallic,
      Roughness = TracerRoughness,
      EmissionEnabled = true,
      Emission = TracerEmissionColor,
      EmissionEnergyMultiplier = TracerEmissionEnergy
    };
    _tracerGlowMat ??= Unshaded(TracerGlowColor, alpha: true);
    var root = new Node3D();
    var body = new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    body.Mesh = new CylinderMesh { TopRadius = TracerRadius, BottomRadius = TracerRadius, Height = TracerBodyLength, RadialSegments = TracerRadialSegments };
    body.MaterialOverride = _tracerCoreMat;
    body.RotationDegrees = new Vector3(90f, 0f, 0f); // cylinder Y axis -> Z
    root.AddChild(body);
    var tip = new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    tip.Mesh = new CylinderMesh { TopRadius = 0f, BottomRadius = TracerRadius, Height = TracerTipLength, RadialSegments = TracerRadialSegments };
    tip.MaterialOverride = _tracerCoreMat;
    tip.RotationDegrees = new Vector3(-90f, 0f, 0f); // point toward -Z (rocket tip pattern)
    tip.Position = new Vector3(0f, 0f, TracerTipOffsetZ);
    root.AddChild(tip);
    var glow = new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    glow.Mesh = new SphereMesh { Radius = TracerGlowRadius, Height = TracerGlowRadius * 2f, RadialSegments = TracerRadialSegments, Rings = TracerGlowRings };
    glow.MaterialOverride = _tracerGlowMat;
    glow.Scale = new Vector3(1f, 1f, TracerGlowStretch);
    root.AddChild(glow);
    return root;
  }

  // Rocket: gray cylinder body + orange tip cone, nose toward -Z.
  public static Node3D MakeRocket()
  {
    _rocketBodyMat ??= new StandardMaterial3D { AlbedoColor = RocketBodyColor, Roughness = RocketBodyRoughness };
    _rocketTipMat ??= Unshaded(RocketTipColor);
    var root = new Node3D();
    var body = new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    body.Mesh = new CylinderMesh { TopRadius = RocketRadius, BottomRadius = RocketRadius, Height = RocketBodyLength };
    body.MaterialOverride = _rocketBodyMat;
    body.RotationDegrees = new Vector3(90f, 0f, 0f); // cylinder Y axis -> Z
    var tip = new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    tip.Mesh = new CylinderMesh { TopRadius = 0f, BottomRadius = RocketRadius, Height = RocketTipLength };
    tip.MaterialOverride = _rocketTipMat;
    tip.RotationDegrees = new Vector3(-90f, 0f, 0f);
    tip.Position = new Vector3(0f, 0f, RocketTipOffsetZ);
    root.AddChild(body);
    root.AddChild(tip);
    return root;
  }

  // Rocket trail puff: tiny translucent box with its OWN material so
  // the owner can fade each puff independently.
  public static MeshInstance3D MakeTrailPuff(Vector3 pos)
  {
    var mi = new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    mi.Mesh = new BoxMesh { Size = new Vector3(TrailPuffSize, TrailPuffSize, TrailPuffSize) };
    mi.MaterialOverride = Unshaded(TrailPuffColor, alpha: true);
    mi.GlobalTransform = new Transform3D(Basis.Identity, pos);
    return mi;
  }
}
