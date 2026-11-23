// Winter dressing (owner, 2026-11-23): on snow worlds,
// walking leaves footprints in the snow and snow falls
// from the sky. Both effects gate on WorldConfig.SnowGround
// and stay completely inert elsewhere.
using Godot;
using System.Collections.Generic;

public partial class RiderDuel
{
    // ---- Footprints ----
    // A ring pool of soft dark prints stamped at each foot
    // plant (the walker counts plants in Steps, the same
    // signal the stomp sounds poll). Prints live ~75 s and
    // fade over the last 20 s. Only plants within 500u of the
    // player stamp - prints are a near-field detail.
    private const int PrintPool = 384;
    private const float PrintLife = 75f;
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

    private static Texture2D PrintTexture()
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
        var img = Image.CreateEmpty(96, 144, false, Image.Format.Rgba8);
        for (int y = 0; y < 144; y++)
        for (int x = 0; x < 96; x++)
        {
            var p = new Vector2((x + 0.5f) / 96f, (y + 0.5f) / 144f);
            float dToe = RRect(p, new Vector2(0.5f, 0.30f), new Vector2(0.31f, 0.235f), 0.10f);
            float dArch = RRect(p, new Vector2(0.5f, 0.545f), new Vector2(0.155f, 0.075f), 0.06f);
            float dHeel = RRect(p, new Vector2(0.5f, 0.775f), new Vector2(0.235f, 0.155f), 0.08f);
            float d = Mathf.Min(Mathf.Min(dToe, dHeel), dArch + 0.012f); // arch sits lighter (less contact)
            float a = Mathf.Clamp(0.5f - d / 0.055f, 0f, 1f);
            a = a * a * (3f - 2f * a);
            // Tread grooves across the toe pad.
            if (dToe < -0.015f)
            {
                float g1 = Mathf.Abs(p.Y - 0.185f), g2 = Mathf.Abs(p.Y - 0.415f);
                if (Mathf.Min(g1, g2) < 0.016f) a *= 0.45f;
            }
            img.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        return ImageTexture.CreateFromImage(img);
    }

    private void InitSnowFx()
    {
        var tex = PrintTexture();
        _prints = new MeshInstance3D[PrintPool];
        _printMats = new StandardMaterial3D[PrintPool];
        _printAge = new float[PrintPool];
        var mesh = new PlaneMesh { Size = new Vector2(1.8f, 2.7f) };
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
                AlbedoColor = new Color(0.40f, 0.48f, 0.68f, 0f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                NoDepthTest = false,
            };
            _printMats[i] = mat;
            _printAge[i] = PrintLife + 1f;
            _prints[i] = new MeshInstance3D
            {
                Mesh = mesh,
                MaterialOverride = mat,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Visible = false,
            };
            AddChild(_prints[i]);
        }

        // Falling snow: a world-space particle box that
        // follows the camera, so the fall reads everywhere
        // the player looks without snowing inside the lab.
        var pm = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(55f, 26f, 55f),
            Direction = new Vector3(0.25f, -1f, 0.1f),
            Spread = 10f,
            InitialVelocityMin = 2.2f,
            InitialVelocityMax = 3.4f,
            Gravity = new Vector3(0f, -0.55f, 0f),
            ScaleMin = 0.7f,
            ScaleMax = 1.4f,
        };
        var flake = new SphereMesh
        {
            Radius = 0.07f, Height = 0.14f,
            RadialSegments = 6, Rings = 3,
            Material = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.95f, 0.97f, 1f, 0.9f),
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            },
        };
        _snowfall = new GpuParticles3D
        {
            Amount = 1400,
            Lifetime = 7f,
            LocalCoords = false,
            ProcessMaterial = pm,
            DrawPass1 = flake,
            VisibilityAabb = new Aabb(-70f, -35f, -70f, 140f, 70f, 140f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_snowfall);
    }

    private void StampPrint(RiderWalker w)
    {
        Vector3 pos = w.LastPlantPos;
        if (_player != null && pos.DistanceTo(_player.RootPos) > 500f) return;
        // Terrain normal from the height gradient.
        float e = 1.5f;
        float hx = Main.HeightAt(pos.X + e, pos.Z) - Main.HeightAt(pos.X - e, pos.Z);
        float hz = Main.HeightAt(pos.X, pos.Z + e) - Main.HeightAt(pos.X, pos.Z - e);
        var n = new Vector3(-hx / (2f * e), 1f, -hz / (2f * e)).Normalized();
        var fwd = -w.LastPlantBasis.Z;
        fwd = (fwd - n * fwd.Dot(n)).Normalized();
        if (fwd.LengthSquared() < 0.01f) return; // foot pointing straight down - no sane print
        var xAxis = n.Cross(-fwd).Normalized();
        var zAxis = xAxis.Cross(n).Normalized();
        var basis = new Basis(xAxis, n, zAxis);
        int i = _printHead;
        _printHead = (_printHead + 1) % PrintPool;
        _printAge[i] = 0f;
        _prints[i].GlobalTransform = new Transform3D(basis, pos + n * 0.12f); // 0.05 clipped corners into slopes (owner, 2026-11-23)
        _prints[i].Visible = true;
    }

    // Per-frame snow update, beside StepSounds.
    private void StepSnowFx(float dt)
    {
        if (!WorldConfig.SnowGround) return;
        if (_prints == null) InitSnowFx();
        _snowClock += dt;

        foreach (var w in SfxWalkers())
        {
            if (w == null || !w.IsReady || IsDead(w)) continue;
            if (!_printStepsSeen.TryGetValue(w, out int seen)) { _printStepsSeen[w] = w.Steps; continue; }
            if (w.Steps == seen) continue;
            _printStepsSeen[w] = w.Steps;
            StampPrint(w);
        }

        for (int i = 0; i < PrintPool; i++)
        {
            if (_printAge[i] > PrintLife) { if (_prints[i].Visible) _prints[i].Visible = false; continue; }
            _printAge[i] += dt;
            float fade = Mathf.Clamp((PrintLife - _printAge[i]) / 20f, 0f, 1f);
            var c = _printMats[i].AlbedoColor;
            c.A = 0.55f * fade;
            _printMats[i].AlbedoColor = c;
        }

        if (_snowfall != null)
        {
            _snowfall.Emitting = !_labOpen;
            if (_cam != null) _snowfall.GlobalPosition = _cam.GlobalPosition + Vector3.Up * 4f;
        }
    }
}
