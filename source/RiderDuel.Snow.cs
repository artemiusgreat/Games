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

    private static Texture2D PrintTexture()
    {
        // Soft rounded blob: a compressed-snow footprint.
        var img = Image.CreateEmpty(64, 96, false, Image.Format.Rgba8);
        for (int y = 0; y < 96; y++)
        for (int x = 0; x < 64; x++)
        {
            float nx = (x - 31.5f) / 30f, ny = (y - 47.5f) / 46f;
            float d = nx * nx + ny * ny;
            float a = Mathf.Clamp(1f - d, 0f, 1f);
            a = a * a * (3f - 2f * a);
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
        var mesh = new PlaneMesh { Size = new Vector2(1.7f, 2.7f) };
        for (int i = 0; i < PrintPool; i++)
        {
            var mat = new StandardMaterial3D
            {
                AlbedoTexture = tex,
                AlbedoColor = new Color(0.52f, 0.60f, 0.78f, 0f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
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
        _prints[i].GlobalTransform = new Transform3D(basis, pos + n * 0.05f);
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
            c.A = 0.5f * fade;
            _printMats[i].AlbedoColor = c;
        }

        if (_snowfall != null)
        {
            _snowfall.Emitting = !_labOpen;
            if (_cam != null) _snowfall.GlobalPosition = _cam.GlobalPosition + Vector3.Up * 4f;
        }
    }
}
