using Godot;
using System.Collections.Generic;

// Titans module: particles, lights, flashes, damage numbers, death feedback.
// Split from the single-file build (round 22 cleanup); the class
// stays one partial class - see Titans.cs for the field block.
public partial class Titans
{
    // ---------- GPU particles (round 9) ----------
    // The mesh-box puffs and short flashes did not read at cockpit
    // range - the owner reported seeing no fire, explosion or smoke
    // twice. These are real particle systems: one-shot bursts for
    // impacts and explosions, and burning emitters that keep a wreck
    // smoking for seconds. Verified by captured frames at burst
    // peak this time, not by counters alone.
    private int _fxParticles;

    // FX palette and tuning (owner's refactor rules: look
    // values named once, feel values named at their exact
    // current settings - never retuned by a refactor).
    private static readonly Color FireColor = new(1f, 1f, 1f, 0.95f);
    private static readonly Color SparkColor = new(0.75f, 0.96f, 1f);
    private static readonly Color[] FireRampColors =
    {
        new Color(1f, 0.80f, 0.35f, 0.95f), new Color(1f, 0.50f, 0.10f, 0.9f),
        new Color(0.85f, 0.20f, 0.03f, 0.5f), new Color(0.30f, 0.05f, 0.01f, 0f),
    };
    private static readonly Color FireballModulate = new(1.5f, 1.5f, 1.5f, 1f);
    private static readonly Color ExplosionFlashColor = new(1f, 0.55f, 0.18f);
    private static readonly Color ExplosionCoreColor = new(1f, 0.9f, 0.65f);
    private static readonly Color ExplosionSparkColor = new(1f, 0.62f, 0.25f);
    private static readonly Color DeathFlashColor = new(1f, 0.72f, 0.3f);
    private static readonly Color DeathCoreColor = new(1f, 0.95f, 0.72f);
    private static readonly Color DeathLowColor = new(1f, 0.5f, 0.18f);
    private static readonly Color DmgNumberEnemyColor = new(1f, 0.78f, 0.3f);
    private static readonly Color DmgNumberPlayerColor = new(1f, 0.38f, 0.3f);
    private static readonly Color DmgNumberOutlineColor = new(0f, 0f, 0f, 0.9f);
    private const float DenyFlashSeconds = 0.5f;         // denied-shot cooldown-bar flash
    private const float HitSfxThrottleSeconds = 0.06f;   // ordinary hit-sound floor
    private const float RocketHitSfxThrottleSeconds = 1.1f; // one boom per salvo (see OnDamaged)
    private const float JoltLaser = 0.12f, JoltRocket = 0.85f, JoltCannon = 1.0f, JoltMg = 0.3f, JoltDefault = 0.4f;
    private const float PlayerHitShake = 0.22f, PlayerHitShakeMax = 0.5f;
    private const float DeathShake = 1.0f, DeathShakeRange = 45f;
    private const float SparkEmissionEnergy = 2.2f;
    private const float BurnStopFadeSeconds = 1.8f;      // emitter tail after a burn stops
    private const float ParticleAabbHalf = 45f;          // particle visibility box half-extent
    private const int FlipbookCell = 128, FlipbookFrameCount = 64, FlipbookColumns = 8;
    private const int DmgNumberFontSize = 128, DmgNumberOutlineSize = 16;
    private const float DmgNumberPixelSize = 0.016f;
    private const float DmgNumberRise = 0.7f;            // damage number spawns this far above the hit point
    private const float DmgNumberJitter = 0.9f;          // horizontal spawn scatter around the hit point
    private const float DmgNumberLifeSeconds = 0.9f;
    private const float DmgNumberRiseSpeed = 1.7f;       // damage numbers drift upward at this rate
    private const float SparkGravity = 3.4f;             // spark sprays fall at this rate
    private const float PuffFadePeakAlpha = 0.45f;       // a trail puff's alpha at spawn (its colour's alpha), fading with life
    // Particle factory (MakeParticles): emission/damping and
    // the draw-mesh proportions shared by every burst.
    private const float ParticleEmissionRadius = 0.45f;
    private const float ParticleDampingMin = 0.4f, ParticleDampingMax = 1.2f;
    private const float BurstExplosiveness = 0.88f;      // one-shot bursts only
    private const float BurstTailSeconds = 0.5f;         // a burst node is freed this long after its lifetime
    private const float DefaultBurstSpreadDeg = 180f;    // a burst's default emission spread
    private const float FlameQuadWidthScale = 1.05f, FlameQuadHeightScale = 1.9f; // flames stand taller than wide
    private const float PuffQuadScale = 1.7f;            // puffs stay square
    private const int SparkMeshSegments = 6, SparkMeshRings = 4;
    // Spark spray (SpawnSparks): direction/speed/life jitter.
    private const float SparkDirYMin = 0.1f;             // sparks spray upward-biased
    private const float SparkSpeedJitterMin = 0.5f, SparkSpeedJitterMax = 1.15f;
    private const float SparkLifeMin = 0.3f, SparkLifeMax = 0.55f;
    // Burn emitter recipe (a wreck keeps burning).
    private const int BurnAmount = 26;
    private const float BurnLifetime = 0.9f, BurnVelMin = 2.4f, BurnVelMax = 4.4f;
    private const float BurnRiseY = 2.5f, BurnMeshRadius = 0.3f;
    private const float BurnScaleMin = 0.8f, BurnScaleMax = 1.5f, BurnSpread = 35f;
    // Flipbook fireball: random start frame range and free tail.
    private const int FlipbookStartFrameMax = 6;
    private const float FlipbookTailSeconds = 0.1f;
    // Rocket-impact explosion bundle (SpawnExplosionFx).
    private const float ExplosionFireballSize = 3.4f, ExplosionFireballSeconds = 0.9f;
    private const float ExplosionFlashSize = 1.15f, ExplosionFlashLife = 0.22f;
    private const float ExplosionCoreSize = 0.5f, ExplosionCoreLife = 0.12f;
    private const int ExplosionSparkCount = 5;
    private const float ExplosionSparkSpeed = 3.8f;
    // Hit sound level and the jolt direction guard.
    private const float HitSfxVolumeDb = -2f;
    private const float MinJoltPushSq = 0.01f;           // squared push length below which no jolt direction exists
    // Death bundle (OnDeath): flash sequence heights/sizes,
    // the big fireball, the fire burst and the burn time.
    private const float DeathFxHeight = 6.5f;
    private const float DeathFlashSize = 2.8f, DeathFlashLife = 0.4f;
    private const float DeathCoreSize = 1.5f, DeathCoreLife = 0.25f;
    private const float DeathLowHeight = 2.2f, DeathLowSize = 1.9f, DeathLowLife = 0.55f;
    private const float DeathExplosionHeight = 1.5f;
    private const float DeathFireballSize = 10f, DeathFireballSeconds = 1.2f;
    private const float DeathBurnHeight = 1.2f, DeathBurnSeconds = 8f;
    private const int DeathBurstAmount = 70;
    private const float DeathBurstLife = 1.6f, DeathBurstVelMin = 5f, DeathBurstVelMax = 12f;
    private const float DeathBurstGravityY = -6f, DeathBurstMeshRadius = 0.2f;
    private const float DeathBurstScaleMin = 1f, DeathBurstScaleMax = 2.1f;

    private readonly System.Collections.Generic.List<(Node3D Node, float Ttl, bool StopFirst)> _timedFx = new();
    // Round 20 (owner: the fire looked like "a couple of yellow
    // bubbles" - it WAS bubbles: the draw pass was a low-poly
    // SPHERE mesh in flat orange). Fire, smoke and dust are now
    // billboarded quads with procedural sprites and lifetime color
    // ramps: a flame is a noisy teardrop that runs white-yellow ->
    // orange -> deep red -> gone, smoke/dust are eroded soft puffs
    // that swell and fade.
    private static ImageTexture FlameTexture()
    {
        const int N = 128;
        var img = Image.CreateEmpty(N, N, false, Image.Format.Rgba8);
        // A narrow, tall teardrop with a pointed tip - v1's fat
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

    private static readonly StandardMaterial3D FireParticleMat = new StandardMaterial3D
    {
        AlbedoColor = FireColor,
        AlbedoTexture = FlameTexture(),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };

    private static readonly StandardMaterial3D SparkParticleMat = new StandardMaterial3D
    {
        AlbedoColor = SparkColor,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    };
    private GpuParticles3D MakeParticles(Vector3 point, StandardMaterial3D mat, int amount,
        float lifetime, bool oneShot, float velMin, float velMax, Vector3 gravity,
        float meshRadius, float scaleMin, float scaleMax, float spread)
    {
        var pm = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = ParticleEmissionRadius,
            Direction = Vector3.Up,
            Spread = spread,
            InitialVelocityMin = velMin,
            InitialVelocityMax = velMax,
            Gravity = gravity,
            ScaleMin = scaleMin,
            ScaleMax = scaleMax,
            DampingMin = ParticleDampingMin,
            DampingMax = ParticleDampingMax,
        };
        if (mat == FireParticleMat)
        {
            pm.ColorRamp = new GradientTexture1D { Gradient = new Gradient {
                Offsets = new float[] { 0f, 0.30f, 0.62f, 1f },
                Colors = FireRampColors } };
            var sc = new Curve(); sc.AddPoint(new Vector2(0f, 0.5f)); sc.AddPoint(new Vector2(0.35f, 0.9f)); sc.AddPoint(new Vector2(1f, 1.12f));
            pm.ScaleCurve = new CurveTexture { Curve = sc };
        }
        PrimitiveMesh mesh;
        if (mat.BillboardMode == BaseMaterial3D.BillboardModeEnum.Particles)
            // Flames stand taller than wide; puffs stay square.
            // Round 21 (owner): fire still read as bubbles - the
            // particles are notably SMALLER now, bursts denser.
            mesh = new QuadMesh { Size = mat == FireParticleMat
                ? new Vector2(meshRadius * FlameQuadWidthScale, meshRadius * FlameQuadHeightScale)
                : new Vector2(meshRadius * PuffQuadScale, meshRadius * PuffQuadScale) };
        else
            mesh = new SphereMesh { Radius = meshRadius, Height = meshRadius * 2f, RadialSegments = SparkMeshSegments, Rings = SparkMeshRings };
        mesh.Material = mat;
        var p = new GpuParticles3D
        {
            Amount = amount,
            Lifetime = lifetime,
            OneShot = oneShot,
            Explosiveness = oneShot ? BurstExplosiveness : 0f,
            ProcessMaterial = pm,
            DrawPass1 = mesh,
            VisibilityAabb = new Aabb(-ParticleAabbHalf, -ParticleAabbHalf, -ParticleAabbHalf, ParticleAabbHalf * 2f, ParticleAabbHalf * 2f, ParticleAabbHalf * 2f),
        };
        AddChild(p);
        p.GlobalPosition = point;
        p.Emitting = true;
        _fxParticles++;
        return p;
    }

    // One-shot fire/smoke/spark burst at a point; freed after it ends.
    private void SpawnBurst(Vector3 point, StandardMaterial3D mat, int amount, float lifetime,
        float velMin, float velMax, Vector3 gravity, float meshRadius, float scaleMin, float scaleMax, float spread = DefaultBurstSpreadDeg)
    {
        var p = MakeParticles(point, mat, amount, lifetime, true, velMin, velMax, gravity, meshRadius, scaleMin, scaleMax, spread);
        _timedFx.Add((p, lifetime + BurstTailSeconds, false));
    }

    // A wreck keeps burning: fire licking up for the given
    // seconds, then the emitter stops and is freed. (Owner,
    // 2026-11-16: the smoke-puff emitter that rode along here
    // is REMOVED - the round sprites read as bubbles; fire
    // only. Smoke bursts are gone game-wide for the same
    // reason; explosions keep fire, flash, sparks and debris.)
    private void SpawnBurnEmitter(Vector3 point, float seconds)
    {
        var fire = MakeParticles(point, FireParticleMat, BurnAmount, BurnLifetime, false, BurnVelMin, BurnVelMax,
            new Vector3(0f, BurnRiseY, 0f), BurnMeshRadius, BurnScaleMin, BurnScaleMax, BurnSpread);
        _timedFx.Add((fire, seconds, true));
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
                _timedFx[i] = (n, BurnStopFadeSeconds, false);
                continue;
            }
            n.QueueFree();
            _timedFx.RemoveAt(i);
        }
    }

    // A brief real light at an impact or destruction - the one FX
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

    // Electric spark burst - the laser's impact signature: a spray of
    // bright cyan-white sparks with light gravity.
    private void SpawnSparks(Vector3 point, Color col, int count, float speed)
    {
        _fxSparkBursts++;
        var mat = new StandardMaterial3D
        {
            AlbedoColor = col,
            EmissionEnabled = true,
            Emission = col,
            EmissionEnergyMultiplier = SparkEmissionEnergy,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
        };
        for (int i = 0; i < count; i++)
        {
            var mi = new MeshInstance3D { Mesh = SparkMesh, MaterialOverride = mat };
            AddChild(mi);
            mi.GlobalPosition = point;
            var dir = new Vector3(_fxRng.RandfRange(-1f, 1f), _fxRng.RandfRange(SparkDirYMin, 1f), _fxRng.RandfRange(-1f, 1f)).Normalized();
            _sparks.Add((mi, dir * speed * _fxRng.RandfRange(SparkSpeedJitterMin, SparkSpeedJitterMax), _fxRng.RandfRange(SparkLifeMin, SparkLifeMax)));
        }
    }

    // Flipbook fireball (Freelancer-style prototype): one camera-facing
    // quad playing a pre-rendered 8x8 frame sheet (white-hot -> yellow
    // -> orange -> deep red -> smoke), layered over the existing
    // flash/sparks/puffs. The sheet is baked by
    // tools/make_explosion_flipbook.py; the frames carry the boiling
    // motion the static flame sprite cannot. Per instance: random
    // start frame, flip and scale, so repeats do not read as canned.
    private static SpriteFrames _flipbookFrames;
    private static SpriteFrames FlipbookFrames()
    {
        if (_flipbookFrames != null) return _flipbookFrames;
        var sheet = GD.Load<Texture2D>(Paths.ExplosionSheet);
        if (sheet == null) { Dbg.Print($"FLIPBOOK sheet missing: {Paths.ExplosionSheet}"); return null; }
        var sf = new SpriteFrames();
        sf.AddAnimation("boom");
        sf.SetAnimationSpeed("boom", FlipbookFrameCount); // 64 frames = 1.0s at speed 1
        sf.SetAnimationLoopMode("boom", SpriteFrames.LoopMode.None);
        for (int f = 0; f < FlipbookFrameCount; f++)
        {
            int row = f / FlipbookColumns, col = f % FlipbookColumns;
            sf.AddFrame("boom", new AtlasTexture { Atlas = sheet, Region = new Rect2(col * FlipbookCell, row * FlipbookCell, FlipbookCell, FlipbookCell) });
        }
        _flipbookFrames = sf;
        return sf;
    }

    private void SpawnFlipbookFireball(Vector3 point, float size, float seconds)
    {
        var frames = FlipbookFrames();
        if (frames == null) return;
        var s = new AnimatedSprite3D
        {
            SpriteFrames = frames,
            Animation = "boom",
            PixelSize = size / FlipbookCell,
            Shaded = false,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Modulate = FireballModulate,
            SpeedScale = 1.0f / seconds,
            FlipH = _fxRng.Randf() < 0.5f,
            FlipV = _fxRng.Randf() < 0.5f,
        };
        AddChild(s);
        s.GlobalPosition = point;
        s.Play();
        s.SetFrameAndProgress(_fxRng.RandiRange(0, FlipbookStartFrameMax), 0f);
        _timedFx.Add((s, seconds + FlipbookTailSeconds, false));
    }

    // Rocket impact bundle: fireball, core flash and sparks -
    // the mini-explosion. NO smoke puffs (owner's standing
    // rule, repeated 2026-11-20): the puffs read as bubbles;
    // an explosion is fire, flash and sparks only.
    private void SpawnExplosionFx(Vector3 point)
    {
        _fxBooms++;
        SpawnFlipbookFireball(point, ExplosionFireballSize, ExplosionFireballSeconds);
        SpawnFlash(point, ExplosionFlashColor, ExplosionFlashSize, ExplosionFlashLife);
        SpawnFlash(point, ExplosionCoreColor, ExplosionCoreSize, ExplosionCoreLife);
        SpawnSparks(point, ExplosionSparkColor, ExplosionSparkCount, ExplosionSparkSpeed);
        _fxSparkBursts--; // explosion sparks are not a laser burst; keep the counters honest
    }

    // A fire attempt on a weapon whose mounting part is destroyed:
    // logged and flashed on its cooldown bar (HUD step), no cooldown spent.
    private void DenyWeapon(DenyKind kind)
    {
        _lastDenied = kind;
        Dbg.Print($"WPN DENIED {kind} (mounting part destroyed)");
        if (kind == DenyKind.Laser) _denyLaserT = DenyFlashSeconds;
        else if (kind == DenyKind.Autocannon) _denyAcT = DenyFlashSeconds;
        else if (kind == DenyKind.Lrm) _denyLrmT = DenyFlashSeconds;
    }

    // Creates (or recreates, after a rider-selection change) the
    // RiderLab against the current player spec + selections.
    // ---------- damage feedback / death / HUD ----------

    private void OnDamaged(RiderWalker target, float dmg, Vector3 point, string source, string kind = "")
    {
        // The reaction carries the weapon: jolt per kind. (The hit
        // flash that used to blink the struck rider is REMOVED -
        // owner, 2026-11-19: keep only the explosion and the sparks
        // at the hit point, no blinking or shining of the rider.)
        float jolt = kind switch
        {
            HitKinds.Laser => JoltLaser,
            HitKinds.Rocket => JoltRocket,
            HitKinds.Cannon => JoltCannon,
            HitKinds.Mg => JoltMg,
            _ => JoltDefault,
        };
        // Hit-sound throttle: 60ms for ordinary fire, but a
        // whole second-plus for rockets (owner, 2026-11-24).
        // A salvo's rockets arrive spread over ~0.9s (staged
        // probe: 6-8 triggers per 16-rocket salvo at ~130ms
        // spacing), and every trigger restarts the explosion
        // clip - a rattle of attack transients he heard as a
        // 'plastic bottle', NOT a pool steal (the batch-16
        // protection was not the cause; it stays). One boom
        // per salvo: the window sits safely under the SRM
        // (2.0s) and LRM (3.5s) cooldowns, so two distinct
        // salvos never merge.
        if (kind == "rocket")
        {
            if (_simT - _lastRocketHitSfxT > RocketHitSfxThrottleSeconds) { _lastRocketHitSfxT = _simT; PlaySfx(_sfxHit, point, HitSfxVolumeDb); Dbg.Print($"HITSFX t={_simT:F3} kind={kind}"); }
        }
        else if (_simT - _lastHitSfxT > HitSfxThrottleSeconds) { _lastHitSfxT = _simT; PlaySfx(_sfxHit, point, HitSfxVolumeDb); Dbg.Print($"HITSFX t={_simT:F3} kind={kind}"); }
        if (jolt > 0f)
        {
            RiderWalker shooter = source == Roles.Player ? _player : source == Roles.Enemy ? _enemy : null;
            if (shooter != null && shooter != target)
            {
                var push = target.RootPos - shooter.RootPos;
                push.Y = 0f;
                if (push.LengthSquared() > MinJoltPushSq) target.Jolt(push.Normalized(), jolt);
            }
        }
        bool onEnemy = target == _enemy;
        SpawnDmgNumber(point, dmg, onEnemy ? DmgNumberEnemyColor : DmgNumberPlayerColor);
        if (onEnemy && source == Roles.Player) _cross.Hitmark = 1f;
        // Camera shake is the PLAYER's hit feedback only
        // (owner, 2026-11-20: NPCs shooting each other across
        // the map shook his screen - external fire is not
        // his feedback). The death-explosion shake below
        // stays proximity-gated as it was.
        if (target == _player) _shake = Mathf.Min(_shake + PlayerHitShake, PlayerHitShakeMax);
    }

    private void OnDeath(RiderWalker w)
    {
        // Death is an explosion now, not a gray-out (owner): a flash
        // sequence at the torso, camera shake, and the whole rider comes
        // apart - every part, torso core included, becomes debris that
        // rains down and settles on the terrain.
        var parts = PartsOf(w);
        var center = w.RootPos + Vector3.Up * DeathFxHeight;
        SpawnFlash(center, DeathFlashColor, DeathFlashSize, DeathFlashLife);
        SpawnFlash(center, DeathCoreColor, DeathCoreSize, DeathCoreLife);
        SpawnFlash(center + Vector3.Up * DeathLowHeight, DeathLowColor, DeathLowSize, DeathLowLife);
        SpawnExplosionFx(center + Vector3.Up * DeathExplosionHeight);
        SpawnFlipbookFireball(center + Vector3.Up * DeathExplosionHeight, DeathFireballSize, DeathFireballSeconds);
        _fxBooms--; // the death bundle is not a rocket hit; keep counters honest
        SpawnBurst(center, FireParticleMat, DeathBurstAmount, DeathBurstLife, DeathBurstVelMin, DeathBurstVelMax, new Vector3(0f, DeathBurstGravityY, 0f), DeathBurstMeshRadius, DeathBurstScaleMin, DeathBurstScaleMax);
        SpawnBurnEmitter(w.RootPos + Vector3.Up * DeathBurnHeight, DeathBurnSeconds);
        _exploding = true;
        parts.ExplodeAll();
        _exploding = false;
        float distToPlayer = w.RootPos.DistanceTo(_player.RootPos);
        if (w == _player || distToPlayer < DeathShakeRange) _shake = DeathShake;
        Dbg.Print($"DEATHBLOW {TagOf(w)} exploded into debris");
        w.SetDarkened(true);
        // No centre banner for a destruction (owner: the
        // "NAME DESTROYED / press R to reset" text overlapped
        // the exit confirmation popup) - the wreck, the feed
        // and the target readout carry the news; R still
        // resets. The match-end VICTORY banner is removed
        // too (owner, 2026-11-11 round 4) - nothing raises
        // the centre banner any more.
    }

    private void SpawnDmgNumber(Vector3 point, float dmg, Color col)
    {
        var l = new Label3D
        {
            Text = $"{dmg:F0}",
            FontSize = DmgNumberFontSize,
            PixelSize = DmgNumberPixelSize,
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            Modulate = col,
            OutlineSize = DmgNumberOutlineSize,
            OutlineModulate = DmgNumberOutlineColor,
            NoDepthTest = true
        };
        AddChild(l);
        l.GlobalPosition = point + Vector3.Up * DmgNumberRise
            + new Vector3((float)_rng.NextDouble() - 0.5f, 0f, (float)_rng.NextDouble() - 0.5f) * DmgNumberJitter;
        _dmgNums.Add((l, DmgNumberLifeSeconds));
    }

}
