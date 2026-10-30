using Godot;
using System.Collections.Generic;

// MechDuel module: particles, lights, flashes, scorch, damage numbers, death feedback.
// Split from the single-file build (round 22 cleanup); the class
// stays one partial class — see MechDuel.cs for the field block.
public partial class MechDuel
{
    // ---------- GPU particles (round 9) ----------
    // The mesh-box puffs and short flashes did not read at cockpit
    // range — the owner reported seeing no fire, explosion or smoke
    // twice. These are real particle systems: one-shot bursts for
    // impacts and explosions, and burning emitters that keep a wreck
    // smoking for seconds. Verified by captured frames at burst
    // peak this time, not by counters alone.
    private int _fxParticles;

    private int _laserRescues; // test-fallback rescues inside FireLaser; the duel AIM phase asserts zero during stable shots
    private readonly System.Collections.Generic.List<(Node3D Node, float Ttl, bool StopFirst)> _timedFx = new();
    // Round 20 (owner: the fire looked like "a couple of yellow
    // bubbles" — it WAS bubbles: the draw pass was a low-poly
    // SPHERE mesh in flat orange). Fire, smoke and dust are now
    // billboarded quads with procedural sprites and lifetime color
    // ramps: a flame is a noisy teardrop that runs white-yellow ->
    // orange -> deep red -> gone, smoke/dust are eroded soft puffs
    // that swell and fade.
    private static ImageTexture FlameTexture()
    {
        const int N = 128;
        var img = Image.CreateEmpty(N, N, false, Image.Format.Rgba8);
        // A narrow, tall teardrop with a pointed tip — v1's fat
        // round blob mass read as a pale rounded SQUARE in the
        // proof frame (the burn emitter's flames), not as fire.
        var blobs = new (float X, float Y, float R, float W)[]
        {
            (0.50f, 0.80f, 0.27f, 1.00f), (0.47f, 0.64f, 0.21f, 0.90f),
            (0.53f, 0.49f, 0.155f, 0.82f), (0.49f, 0.34f, 0.11f, 0.72f),
            (0.51f, 0.21f, 0.07f, 0.60f),
        };
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            float u = x / (float)(N - 1), v = y / (float)(N - 1);
            float a = 0f;
            foreach (var (bx, by, br, bw) in blobs)
            {
                float d2 = ((u - bx) * (u - bx) + (v - by) * (v - by)) / (br * br);
                a += bw * Mathf.Exp(-d2 * 2.6f);
            }
            float n = Mathf.Sin(u * 39.7f + v * 17.3f) * Mathf.Sin(u * 11.9f - v * 29.1f);
            a *= 0.82f + 0.28f * n; // erode the edge so it is not a clean ellipse
            a *= Mathf.Clamp(1.25f - v * 0.45f, 0f, 1f); // base burns denser than the tip
            a = Mathf.Clamp(a, 0f, 1f);
            img.SetPixel(x, y, new Color(1f, 1f, 1f, a * a * (3f - 2f * a)));
        }
        return ImageTexture.CreateFromImage(img);
    }

    private static ImageTexture PuffTexture()
    {
        const int N = 128;
        var img = Image.CreateEmpty(N, N, false, Image.Format.Rgba8);
        var rng = new System.Random(777);
        var blobs = new (float X, float Y, float R)[9];
        for (int i = 0; i < blobs.Length; i++)
            blobs[i] = (0.5f + (float)(rng.NextDouble() - 0.5) * 0.20f,
                        0.5f + (float)(rng.NextDouble() - 0.5) * 0.20f,
                        0.17f + (float)rng.NextDouble() * 0.12f);
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            float u = x / (float)(N - 1), v = y / (float)(N - 1);
            float a = 0f;
            foreach (var (bx, by, br) in blobs)
            {
                float d2 = ((u - bx) * (u - bx) + (v - by) * (v - by)) / (br * br);
                a += Mathf.Exp(-d2 * 2.2f);
            }
            // A radial window guarantees ZERO alpha at the quad's
            // border — the first version let a blob reach the edge,
            // and the dust read as crisp white plaques in the proof
            // frame instead of a soft cloud.
            float r = new Vector2(u - 0.5f, v - 0.5f).Length() * 2f;
            float win = Mathf.Clamp((1f - r) / 0.45f, 0f, 1f);
            a = Mathf.Clamp(a * 0.55f, 0f, 1f) * win * win;
            img.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        return ImageTexture.CreateFromImage(img);
    }

    private static readonly StandardMaterial3D FireParticleMat = new StandardMaterial3D
    {
        AlbedoColor = new Color(1f, 1f, 1f, 0.95f),
        AlbedoTexture = FlameTexture(),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };

    private static readonly StandardMaterial3D DustParticleMat = new StandardMaterial3D
    {
        AlbedoColor = new Color(0.70f, 0.64f, 0.53f, 0.60f),
        AlbedoTexture = PuffTexture(),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };
    private static readonly StandardMaterial3D SparkParticleMat = new StandardMaterial3D
    {
        AlbedoColor = new Color(0.75f, 0.96f, 1f),
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };
    private static readonly StandardMaterial3D SmokeParticleMat = new StandardMaterial3D
    {
        AlbedoColor = new Color(0.52f, 0.52f, 0.55f, 0.42f),
        AlbedoTexture = PuffTexture(),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };

    private GpuParticles3D MakeParticles(Vector3 point, StandardMaterial3D mat, int amount,
        float lifetime, bool oneShot, float velMin, float velMax, Vector3 gravity,
        float meshRadius, float scaleMin, float scaleMax, float spread)
    {
        var pm = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = 0.45f,
            Direction = Vector3.Up,
            Spread = spread,
            InitialVelocityMin = velMin,
            InitialVelocityMax = velMax,
            Gravity = gravity,
            ScaleMin = scaleMin,
            ScaleMax = scaleMax,
            DampingMin = 0.4f,
            DampingMax = 1.2f,
        };
        if (mat == FireParticleMat)
        {
            pm.ColorRamp = new GradientTexture1D { Gradient = new Gradient {
                Offsets = new float[] { 0f, 0.30f, 0.62f, 1f },
                Colors = new Color[] { new Color(1f, 0.80f, 0.35f, 0.95f), new Color(1f, 0.50f, 0.10f, 0.9f),
                                       new Color(0.85f, 0.20f, 0.03f, 0.5f), new Color(0.30f, 0.05f, 0.01f, 0f) } } };
            var sc = new Curve(); sc.AddPoint(new Vector2(0f, 0.5f)); sc.AddPoint(new Vector2(0.35f, 0.9f)); sc.AddPoint(new Vector2(1f, 1.12f));
            pm.ScaleCurve = new CurveTexture { Curve = sc };
        }
        else if (mat == SmokeParticleMat || mat == DustParticleMat)
        {
            float a0 = mat == DustParticleMat ? 0.5f : 0.5f;
            pm.ColorRamp = new GradientTexture1D { Gradient = new Gradient {
                Offsets = new float[] { 0f, 1f },
                Colors = new Color[] { new Color(1f, 1f, 1f, a0), new Color(1f, 1f, 1f, 0f) } } };
            var sc = new Curve(); sc.AddPoint(new Vector2(0f, 0.7f)); sc.AddPoint(new Vector2(1f, 2.3f));
            pm.ScaleCurve = new CurveTexture { Curve = sc };
        }
        PrimitiveMesh mesh;
        if (mat.BillboardMode == BaseMaterial3D.BillboardModeEnum.Particles)
            // Flames stand taller than wide; puffs stay square.
            // Round 21 (owner): fire still read as bubbles — the
            // particles are notably SMALLER now, bursts denser.
            mesh = new QuadMesh { Size = mat == FireParticleMat
                ? new Vector2(meshRadius * 1.05f, meshRadius * 1.9f)
                : new Vector2(meshRadius * 1.7f, meshRadius * 1.7f) };
        else
            mesh = new SphereMesh { Radius = meshRadius, Height = meshRadius * 2f, RadialSegments = 6, Rings = 4 };
        mesh.Material = mat;
        var p = new GpuParticles3D
        {
            Amount = amount,
            Lifetime = lifetime,
            OneShot = oneShot,
            Explosiveness = oneShot ? 0.88f : 0f,
            ProcessMaterial = pm,
            DrawPass1 = mesh,
            VisibilityAabb = new Aabb(-45f, -45f, -45f, 90f, 90f, 90f),
        };
        AddChild(p);
        p.GlobalPosition = point;
        p.Emitting = true;
        _fxParticles++;
        return p;
    }

    // One-shot fire/smoke/spark burst at a point; freed after it ends.
    private void SpawnBurst(Vector3 point, StandardMaterial3D mat, int amount, float lifetime,
        float velMin, float velMax, Vector3 gravity, float meshRadius, float scaleMin, float scaleMax, float spread = 180f)
    {
        var p = MakeParticles(point, mat, amount, lifetime, true, velMin, velMax, gravity, meshRadius, scaleMin, scaleMax, spread);
        _timedFx.Add((p, lifetime + 0.5f, false));
    }

    // A wreck keeps burning: fire licking up + smoke rising for the
    // given seconds, then the emitters stop and are freed.
    private void SpawnBurnEmitter(Vector3 point, float seconds)
    {
        var fire = MakeParticles(point, FireParticleMat, 26, 0.9f, false, 2.4f, 4.4f,
            new Vector3(0f, 2.5f, 0f), 0.3f, 0.8f, 1.5f, 35f);
        _timedFx.Add((fire, seconds, true));
        var smoke = MakeParticles(point + Vector3.Up * 0.8f, SmokeParticleMat, 22, 2.8f, false, 1.1f, 2.1f,
            new Vector3(0f, 1.4f, 0f), 0.55f, 1.6f, 2.8f, 30f);
        _timedFx.Add((smoke, seconds, true));
    }

    private void StepTimedFx(float dt)
    {
        for (int i = _timedFx.Count - 1; i >= 0; i--)
        {
            var (n, ttl, stop) = _timedFx[i];
            ttl -= dt;
            if (ttl > 0f) { _timedFx[i] = (n, ttl, stop); continue; }
            if (stop && n is GpuParticles3D gp)
            {
                gp.Emitting = false;
                _timedFx[i] = (n, 1.8f, false);
                continue;
            }
            n.QueueFree();
            _timedFx.RemoveAt(i);
        }
    }

    // A brief real light at an impact or destruction — the one FX
    // that reads at any range (round 8: flashes and 0.14u puffs were
    // too small and short to see). Faded and freed by UpdateCombat.
    private readonly System.Collections.Generic.List<(OmniLight3D Light, float Life, float Max, float Energy0)> _lights = new();
    private int _fxLights;
    private void SpawnImpactLight(Vector3 point, Color col, float energy, float range, float life)
    {
        var l = new OmniLight3D
        {
            LightColor = col,
            LightEnergy = energy,
            OmniRange = range,
            ShadowEnabled = false,
        };
        AddChild(l);
        l.GlobalPosition = point;
        _lights.Add((l, life, life, energy));
        _fxLights++;
    }

    private void StepImpactLights(float dt)
    {
        for (int i = _lights.Count - 1; i >= 0; i--)
        {
            var (l, life, max, e0) = _lights[i];
            life -= dt;
            if (life <= 0f) { l.QueueFree(); _lights.RemoveAt(i); continue; }
            l.LightEnergy = e0 * (life / max);
            _lights[i] = (l, life, max, e0);
        }
    }

    private void SpawnFlash(Vector3 point, Color col, float radius, float life)
    {
        var f = WeaponFx.MakeFlash(point, col, radius);
        AddChild(f);
        _fx.Add((f, life, 0));
    }

    // Electric spark burst — the laser's impact signature: a spray of
    // bright cyan-white sparks with light gravity.
    private void SpawnSparks(Vector3 point, Color col, int count, float speed)
    {
        _fxSparkBursts++;
        var mat = new StandardMaterial3D
        {
            AlbedoColor = col,
            EmissionEnabled = true,
            Emission = col,
            EmissionEnergyMultiplier = 2.2f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
        };
        for (int i = 0; i < count; i++)
        {
            var mi = new MeshInstance3D { Mesh = SparkMesh, MaterialOverride = mat };
            AddChild(mi);
            mi.GlobalPosition = point;
            var dir = new Vector3(_fxRng.RandfRange(-1f, 1f), _fxRng.RandfRange(0.1f, 1f), _fxRng.RandfRange(-1f, 1f)).Normalized();
            _sparks.Add((mi, dir * speed * _fxRng.RandfRange(0.5f, 1.15f), _fxRng.RandfRange(0.3f, 0.55f)));
        }
    }

    // Rocket impact bundle: core flash, fireball and smoke — the
    // mini-explosion the owner asked for.
    private void SpawnExplosionFx(Vector3 point)
    {
        _fxBooms++;
        SpawnFlash(point, new Color(1f, 0.55f, 0.18f), 1.15f, 0.22f);
        SpawnFlash(point, new Color(1f, 0.9f, 0.65f), 0.5f, 0.12f);
        SpawnSparks(point, new Color(1f, 0.62f, 0.25f), 5, 3.8f);
        _fxSparkBursts--; // explosion sparks are not a laser burst; keep the counters honest
        for (int i = 0; i < 3; i++)
        {
            var puff = WeaponFx.MakeTrailPuff(point + new Vector3(_fxRng.RandfRange(-0.5f, 0.5f), _fxRng.RandfRange(0f, 0.6f), _fxRng.RandfRange(-0.5f, 0.5f)));
            AddChild(puff);
            _puffs.Add((puff, 0.7f));
        }
    }

    // Cannon scorch: a dark burn decal pasted on the struck surface. On
    // a mech it parents to the walker so it rides the hull; on terrain
    // or a prop it stays in the world. Fades out over ~7 seconds.
    private void SpawnScorch(Vector3 point, Vector3 normal, Node3D parent)
    {
        _fxScorches++;
        var mat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.03f, 0.03f, 0.03f, 0.82f),
            Roughness = 1f,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };
        var mi = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(1.05f, 1.05f) },
            MaterialOverride = mat
        };
        parent.AddChild(mi);
        mi.GlobalPosition = point + normal * 0.07f;
        if (normal.LengthSquared() > 0.01f && Mathf.Abs(normal.Dot(Vector3.Up)) < 0.98f)
            mi.LookAt(point + normal * 2f, Vector3.Up);
        mi.RotateObjectLocal(Vector3.Forward, _fxRng.RandfRange(0f, Mathf.Tau));
        _scorches.Add((mi, 0f));
        if (_scorches.Count > 40)
        {
            _scorches[0].Mi.QueueFree();
            _scorches.RemoveAt(0);
        }
    }

    // A fire attempt on a weapon whose mounting part is destroyed:
    // logged and flashed on its cooldown bar (HUD step), no cooldown spent.
    private void DenyWeapon(string weapon)
    {
        _lastDenied = weapon;
        GD.Print($"WPN DENIED {weapon} (mounting part destroyed)");
        if (weapon == "laser") _denyLaserT = 0.5f;
        else if (weapon == "ac") _denyAcT = 0.5f;
        else if (weapon == "lrm") _denyLrmT = 0.5f;
    }

    // Creates (or recreates, after a mech-selection change) the
    // MechLab against the current player spec + selections.
    // ---------- damage feedback / death / HUD ----------

    private void OnDamaged(MechWalker target, float dmg, Vector3 point, string source, string kind = "")
    {
        // Round 8: the reaction carries the weapon. Color + jolt per
        // kind — before this, every hit blinked the same orange and
        // the body never moved (owner's report).
        var (flashCol, flashEnergy, jolt) = kind switch
        {
            "laser" => (new Color(0.55f, 0.95f, 1f), 1.7f, 0.12f),
            "rocket" => (new Color(1f, 0.5f, 0.15f), 2.8f, 0.85f),
            "cannon" => (new Color(1f, 0.85f, 0.45f), 2.6f, 1.0f),
            "mg" => (new Color(1f, 0.75f, 0.3f), 1.3f, 0.3f),
            _ => (new Color(1f, 0.55f, 0.2f), 2.4f, 0.4f),
        };
        target.FlashPulse(flashCol, flashEnergy, point); // local to the hit (round 20)
        if (_simT - _lastHitSfxT > 0.06) { _lastHitSfxT = _simT; PlaySfx(_sfxHit, point, -2f); }
        if (jolt > 0f)
        {
            MechWalker shooter = source == "PLAYER" ? _player : source == "ENEMY" ? _enemy : null;
            if (shooter != null && shooter != target)
            {
                var push = target.RootPos - shooter.RootPos;
                push.Y = 0f;
                if (push.LengthSquared() > 0.01f) target.Jolt(push.Normalized(), jolt);
            }
        }
        bool onEnemy = target == _enemy;
        SpawnDmgNumber(point, dmg, onEnemy ? new Color(1f, 0.78f, 0.3f) : new Color(1f, 0.38f, 0.3f));
        if (onEnemy && source == "PLAYER") _cross.Hitmark = 1f;
        if (!onEnemy) _shake = Mathf.Min(_shake + 0.22f, 0.5f);
    }

    private void OnDeath(MechWalker w)
    {
        // Death is an explosion now, not a gray-out (owner): a flash
        // sequence at the torso, camera shake, and the whole mech comes
        // apart — every part, torso core included, becomes debris that
        // rains down and settles on the terrain.
        var parts = w == _player ? _partsPlayer : _partsEnemy;
        var center = w.RootPos + Vector3.Up * 6.5f;
        SpawnFlash(center, new Color(1f, 0.72f, 0.3f), 2.8f, 0.4f);
        SpawnFlash(center, new Color(1f, 0.95f, 0.72f), 1.5f, 0.25f);
        SpawnFlash(center + Vector3.Up * 2.2f, new Color(1f, 0.5f, 0.18f), 1.9f, 0.55f);
        SpawnExplosionFx(center + Vector3.Up * 1.5f);
        _fxBooms--; // the death bundle is not a rocket hit; keep counters honest
        SpawnBurst(center, FireParticleMat, 70, 1.6f, 5f, 12f, new Vector3(0f, -6f, 0f), 0.2f, 1f, 2.1f);
        SpawnBurst(center, SmokeParticleMat, 40, 3.6f, 1.6f, 3.6f, new Vector3(0f, 1.5f, 0f), 0.6f, 1.8f, 3.4f);
        SpawnBurnEmitter(w.RootPos + Vector3.Up * 1.2f, 8f);
        _exploding = true;
        parts.ExplodeAll();
        _exploding = false;
        float distToPlayer = w.RootPos.DistanceTo(_player.RootPos);
        if (w == _player || distToPlayer < 45f) _shake = 1.0f;
        GD.Print($"DEATHBLOW {(w == _player ? "PLAYER" : "ENEMY")} exploded into debris");
        w.SetDarkened(true);
        if (w == _enemy)
        {
            _banner.Text = "ATLAS DESTROYED";
            _banner.Modulate = new Color(1f, 0.85f, 0.35f);
            _bannerSub.Visible = true;
        }
        else
        {
            _banner.Text = "MECH DESTROYED — press R to reset";
            _banner.Modulate = new Color(1f, 0.35f, 0.28f);
            _bannerSub.Visible = false;
        }
        _banner.Visible = true;
    }

    private void SpawnDmgNumber(Vector3 point, float dmg, Color col)
    {
        var l = new Label3D
        {
            Text = $"{dmg:F0}",
            FontSize = 128,
            PixelSize = 0.016f,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Modulate = col,
            OutlineSize = 16,
            OutlineModulate = new Color(0f, 0f, 0f, 0.9f),
            NoDepthTest = true
        };
        AddChild(l);
        l.GlobalPosition = point + Vector3.Up * 0.7f
            + new Vector3((float)_rng.NextDouble() - 0.5f, 0f, (float)_rng.NextDouble() - 0.5f) * 0.9f;
        _dmgNums.Add((l, 0.9f));
    }

}
