using Godot;
using System;

// Weapon core: projectiles stepped with swept segment tests against the
// shared HitModel, plus cheap one-node FX factories (beams, flashes,
// tracers, rockets, trail puffs). MechDuel owns the instances and their
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

    public void Step(float dt, HitModel hm)
    {
        if (Dead) return;
        Life -= dt;
        Vel.Y -= Gravity * dt;
        var from = GlobalPosition;
        var to = from + Vel * dt;
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

    private static StandardMaterial3D _tracerMat;
    private static StandardMaterial3D _rocketBodyMat;
    private static StandardMaterial3D _rocketTipMat;

    // Laser beam: thin box stretched from a to b. Own material instance so
    // the owner can fade it over its short life.
    public static MeshInstance3D MakeBeam(Vector3 a, Vector3 b, Color core, float width)
    {
        var mi = new MeshInstance3D();
        float len = a.DistanceTo(b);
        mi.Mesh = new BoxMesh { Size = new Vector3(width, width, Mathf.Max(len, 0.1f)) };
        mi.MaterialOverride = Unshaded(core, alpha: core.A < 0.99f);
        var dir = (b - a).Normalized();
        var basis = Basis.LookingAt(dir, Vector3.Up);
        mi.GlobalTransform = new Transform3D(basis, (a + b) * 0.5f);
        return mi;
    }

    // Impact / muzzle flash: one unshaded sphere.
    public static MeshInstance3D MakeFlash(Vector3 point, Color col, float radius)
    {
        var mi = new MeshInstance3D();
        mi.Mesh = new SphereMesh { Radius = radius, Height = radius * 2f };
        mi.MaterialOverride = Unshaded(col);
        mi.GlobalTransform = new Transform3D(Basis.Identity, point);
        return mi;
    }

    // Autocannon tracer: small elongated glowing sphere (axis = -Z fwd,
    // the projectile node LookAts along its velocity).
    public static MeshInstance3D MakeTracer()
    {
        _tracerMat ??= Unshaded(new Color(1f, 0.62f, 0.15f));
        var mi = new MeshInstance3D();
        mi.Mesh = new SphereMesh { Radius = 0.16f, Height = 0.32f };
        mi.MaterialOverride = _tracerMat;
        mi.Scale = new Vector3(1f, 1f, 3.2f);
        return mi;
    }

    // Rocket: gray cylinder body + orange tip cone, nose toward -Z.
    public static Node3D MakeRocket()
    {
        _rocketBodyMat ??= new StandardMaterial3D { AlbedoColor = new Color(0.62f, 0.62f, 0.66f), Roughness = 0.5f };
        _rocketTipMat ??= Unshaded(new Color(1f, 0.45f, 0.1f));
        var root = new Node3D();
        var body = new MeshInstance3D();
        body.Mesh = new CylinderMesh { TopRadius = 0.09f, BottomRadius = 0.09f, Height = 0.72f };
        body.MaterialOverride = _rocketBodyMat;
        body.RotationDegrees = new Vector3(90f, 0f, 0f); // cylinder Y axis -> Z
        var tip = new MeshInstance3D();
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
        var mi = new MeshInstance3D();
        mi.Mesh = new BoxMesh { Size = new Vector3(0.14f, 0.14f, 0.14f) };
        mi.MaterialOverride = Unshaded(new Color(0.9f, 0.9f, 0.92f, 0.45f), alpha: true);
        mi.GlobalTransform = new Transform3D(Basis.Identity, pos);
        return mi;
    }
}
