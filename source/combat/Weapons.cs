using Godot;
using System;

// Weapon core: projectiles stepped with swept segment tests against the
// shared HitModel, plus cheap one-node FX factories (beams, flashes,
// tracers, rockets, trail puffs). RiderDuel owns the instances and their
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

    public void Step(float dt, HitModel hm)
    {
        if (Dead) return;
        Life -= dt;
        Vector3 to;
        if (GuideActive)
        {
            // Gravity is inside the arc model here, so the plain
            // decrement below is skipped while guided; the swept
            // segment test below runs on the blend's step exactly
            // as it does for a ballistic step.
            GuideTime += dt;
            float u = Mathf.Clamp(GuideTime / GuideDur, 0f, 1f);
            float beta = u * u * (3f - 2f * u);
            float betaDeriv = GuideTime < GuideDur ? 6f * u * (1f - u) / GuideDur : 0f;
            var delta = GuideOrigin - GuideStart;
            to = GuideStart + GuideVel0 * GuideTime
                + Vector3.Down * (0.5f * Gravity * GuideTime * GuideTime)
                + delta * beta;
            Vel = GuideVel0 + Vector3.Down * (Gravity * GuideTime) + delta * betaDeriv;
            if (GuideTime >= GuideDur) GuideActive = false;
        }
        else
        {
            Vel.Y -= Gravity * dt;
            to = GlobalPosition + Vel * dt;
        }
        var from = GlobalPosition;
        float len = from.DistanceTo(to);
        if (len > 1e-5f)
        {
            var hit = hm.Ray(from, (to - from) / len, len, OwnerId);
            if (hit.Hit)
            {
                GlobalPosition = hit.Point;
                Dead = true;
                OnHit?.Invoke(hit, this);
                return;
            }
        }
        GlobalPosition = to;
        if (Vel.LengthSquared() > 0.01f)
        {
            var target = to + Vel;
            if (GlobalPosition.DistanceSquaredTo(target) > 1e-6f)
                LookAt(target, Vector3.Up);
        }
        OnStep?.Invoke(this);
        if (Life <= 0f) Dead = true;
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
        if (alpha) m.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        return m;
    }

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
        mi.Mesh = new BoxMesh { Size = new Vector3(width, width, Mathf.Max(len, 0.1f)) };
        mi.MaterialOverride = Unshaded(core, alpha: core.A < 0.99f);
        var dir = (b - a).Normalized();
        var basis = Basis.LookingAt(dir, Vector3.Up);
        mi.GlobalTransform = new Transform3D(basis, (a + b) * 0.5f);
        return mi;
    }

    // PPC bolt (owner, 2026-11-24): a lightning arc, not a
    // straight beam - a chain of thin boxes through jittered
    // midpoints plus a couple of short forks. The jitter is
    // seeded, so the core and its glow (same seed) trace the
    // identical path. Ends stay on the line; the deviation
    // peaks mid-flight.
    public static Node3D MakeBolt(Vector3 a, Vector3 b, Color core, float width, int seed)
    {
        var root = new Node3D();
        var rng = new System.Random(seed);
        float len = a.DistanceTo(b);
        var dir = (b - a).Normalized();
        var side = dir.Cross(Vector3.Up);
        if (side.Length() < 0.1f) side = dir.Cross(Vector3.Right);
        side = side.Normalized();
        var up2 = side.Cross(dir).Normalized();
        int n = Mathf.Clamp(Mathf.RoundToInt(len / 16f), 4, 16);
        var pts = new Vector3[n + 1];
        pts[0] = a; pts[n] = b;
        for (int i = 1; i < n; i++)
        {
            float t = (float)i / n;
            float amp = Mathf.Min(len * 0.03f, 3.2f) * Mathf.Pow(Mathf.Sin(Mathf.Pi * t), 0.7f);
            pts[i] = a + dir * (len * t)
                   + side * (float)(rng.NextDouble() * 2 - 1) * amp
                   + up2 * (float)(rng.NextDouble() * 2 - 1) * amp;
        }
        for (int i = 0; i < n; i++)
        {
            var seg = MakeBeam(pts[i], pts[i + 1], core, width);
            root.AddChild(seg);
        }
        int forks = rng.Next(2, 4);
        for (int f = 0; f < forks; f++)
        {
            int at = rng.Next(1, n);
            float fl = len * (0.07f + (float)rng.NextDouble() * 0.08f);
            var tip = pts[at] + dir * fl * 0.55f
                    + side * (float)(rng.NextDouble() * 2 - 1) * fl
                    + up2 * (float)(rng.NextDouble() * 2 - 1) * fl;
            root.AddChild(MakeBeam(pts[at], tip, core, width * 0.55f));
        }
        return root;
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
            AlbedoColor = new Color(0.23f, 0.16f, 0.07f),
            Metallic = 0.8f,
            Roughness = 0.32f,
            EmissionEnabled = true,
            Emission = new Color(1f, 0.38f, 0.06f),
            EmissionEnergyMultiplier = 0.35f
        };
        _tracerGlowMat ??= Unshaded(new Color(1f, 0.5f, 0.1f, 0.3f), alpha: true);
        var root = new Node3D();
        var body = new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        body.Mesh = new CylinderMesh { TopRadius = 0.075f, BottomRadius = 0.075f, Height = 0.62f, RadialSegments = 8 };
        body.MaterialOverride = _tracerCoreMat;
        body.RotationDegrees = new Vector3(90f, 0f, 0f); // cylinder Y axis -> Z
        root.AddChild(body);
        var tip = new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        tip.Mesh = new CylinderMesh { TopRadius = 0f, BottomRadius = 0.075f, Height = 0.3f, RadialSegments = 8 };
        tip.MaterialOverride = _tracerCoreMat;
        tip.RotationDegrees = new Vector3(-90f, 0f, 0f); // point toward -Z (rocket tip pattern)
        tip.Position = new Vector3(0f, 0f, -0.46f);
        root.AddChild(tip);
        var glow = new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        glow.Mesh = new SphereMesh { Radius = 0.09f, Height = 0.18f, RadialSegments = 8, Rings = 4 };
        glow.MaterialOverride = _tracerGlowMat;
        glow.Scale = new Vector3(1f, 1f, 8f); // slim streak: 0.18 wide, 1.44 long
        root.AddChild(glow);
        return root;
    }

    // Rocket: gray cylinder body + orange tip cone, nose toward -Z.
    public static Node3D MakeRocket()
    {
        _rocketBodyMat ??= new StandardMaterial3D { AlbedoColor = new Color(0.62f, 0.62f, 0.66f), Roughness = 0.5f };
        _rocketTipMat ??= Unshaded(new Color(1f, 0.45f, 0.1f));
        var root = new Node3D();
        var body = new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        body.Mesh = new CylinderMesh { TopRadius = 0.09f, BottomRadius = 0.09f, Height = 0.72f };
        body.MaterialOverride = _rocketBodyMat;
        body.RotationDegrees = new Vector3(90f, 0f, 0f); // cylinder Y axis -> Z
        var tip = new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        tip.Mesh = new CylinderMesh { TopRadius = 0f, BottomRadius = 0.09f, Height = 0.3f };
        tip.MaterialOverride = _rocketTipMat;
        tip.RotationDegrees = new Vector3(-90f, 0f, 0f);
        tip.Position = new Vector3(0f, 0f, -0.5f);
        root.AddChild(body);
        root.AddChild(tip);
        return root;
    }

    // Rocket trail puff: tiny translucent box with its OWN material so
    // the owner can fade each puff independently.
    public static MeshInstance3D MakeTrailPuff(Vector3 pos)
    {
        var mi = new MeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        mi.Mesh = new BoxMesh { Size = new Vector3(0.14f, 0.14f, 0.14f) };
        mi.MaterialOverride = Unshaded(new Color(0.9f, 0.9f, 0.92f, 0.45f), alpha: true);
        mi.GlobalTransform = new Transform3D(Basis.Identity, pos);
        return mi;
    }
}
