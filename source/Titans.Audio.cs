using Godot;
using System.Collections.Generic;

// Titans module: procedural sound: loading, the player pool, footsteps.
// Split from the single-file build (round 22 cleanup); the class
// stays one partial class - see Titans.cs for the field block.
public partial class Titans
{
    // ---- sound (round 21, owner): fully procedural WAVs in
    // assets/sfx (make_sfx.py - no external samples), played
    // through a small pool of 3D players so distance attenuates.
    private AudioStream _sfxLaser, _sfxPpc, _sfxCannon, _sfxRocket, _sfxHit, _sfxTree, _sfxExplosion, _sfxStepA, _sfxStepB, _sfxThruster;
    // Ambience (owner, 2026-11-24): a wind bed for the world
    // and a servo bed for the player's own rider - two
    // dedicated 2D players, always running, faded by state.
    private AudioStream _sfxWind, _sfxServo;
    private AudioStreamPlayer _windPlayer, _servoPlayer;
    // Order announcements (owner, 2026-11-25): recorded
    // voice clips for the five team orders - his pick over
    // live text-to-speech. One dedicated 2D player; a new
    // order cuts the clip still speaking.
    private AudioStream _voxFollow, _voxAttackTarget, _voxAttackNearby, _voxProtect, _voxStop;
    private AudioStreamPlayer _voxPlayer;
    // Audio levels and pool tuning (named at their exact
    // current values - a refactor never retunes sound).
    private const int WavSampleRate = 22050;             // loop-end math for the baked WAVs
    private const int SfxPoolSize = 20;
    private const float SfxPoolMaxDistance = 220f;
    private const float SfxBaseVolumeDb = -6f;           // pool baseline; callers offset from it
    private const float VoxVolumeDb = -4f;               // order-announcement level
    private const float AmbienceSilentDb = -60f;         // the ambience floor (effectively off)
    private const float WindWorldDb = -38f;              // wind bed level in the world (silent in the lab)
    private const float WindFadeRate = 9f, ServoFadeRate = 26f;
    private const float ServoBaseDb = -30f, ServoActivityDb = 14f;
    private const float ServoPitchBase = 0.92f, ServoPitchRange = 0.16f;
    private const float PitchJitterMin = 0.94f, PitchJitterMax = 1.06f;
    private const float RunStepSpeedThreshold = 7f;      // gait speed that counts as running, for step sounds
    private const float PlayerStepDb = -1f, NpcStepDb = -3f, RunStepDbBonus = 2.5f, RunStepPitch = 0.9f;
    private const float ThrusterMaxDistance = 260f, ThrusterVolumeDb = -13f;
    private float _windDb = AmbienceSilentDb, _servoDb = AmbienceSilentDb, _prevTorsoYaw;
    private readonly System.Collections.Generic.List<AudioStreamPlayer3D> _sfxPool = new();
    private readonly System.Collections.Generic.List<bool> _sfxProtected = new();
    private int _sfxNext;
    private double _lastHitSfxT = -1;
    private double _lastRocketHitSfxT = -1;
    // Per-walker sound state (owner, 2026-11-17): EVERY rider
    // stomps, not just the duel pair, and every thrusting rider
    // gets its own looping thruster player.
    private readonly System.Collections.Generic.Dictionary<RiderWalker, int> _stepsSeen = new();
    private readonly System.Collections.Generic.Dictionary<RiderWalker, bool> _stepAltW = new();
    private readonly System.Collections.Generic.Dictionary<RiderWalker, AudioStreamPlayer3D> _thrusterPlayers = new();

    private void InitSfx()
    {
        // Fire and hit sounds are the owner's supplied recordings
        // (owner, 2026-11-22): laser, PPC (its own recording - it
        // no longer shares the laser's), cannon, missile launch,
        // and the hit, which is his explosion recording - the old
        // synthetic hit read as a plastic bottle dropping.
        _sfxLaser = GD.Load<AudioStream>("res://assets/sfx/laser.mp3");
        _sfxPpc = GD.Load<AudioStream>("res://assets/sfx/ppc.mp3");
        _sfxCannon = GD.Load<AudioStream>("res://assets/sfx/cannon.mp3");
        _sfxRocket = GD.Load<AudioStream>("res://assets/sfx/rocket.mp3");
        _sfxHit = GD.Load<AudioStream>("res://assets/sfx/hit.mp3");
        _sfxTree = GD.Load<AudioStream>("res://assets/sfx/tree.wav");
        _sfxExplosion = GD.Load<AudioStream>("res://assets/sfx/explosion.wav");
        _sfxStepA = GD.Load<AudioStream>("res://assets/sfx/step_a.wav");
        _sfxStepB = GD.Load<AudioStream>("res://assets/sfx/step_b.wav");
        _sfxThruster = GD.Load<AudioStream>("res://assets/sfx/thruster.wav");
        if (_sfxThruster is AudioStreamWav tw)
        {
            tw.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            tw.LoopBegin = 0;
            tw.LoopEnd = (int)tw.GetLength() * WavSampleRate;
        }
        _sfxWind = GD.Load<AudioStream>("res://assets/sfx/wind.wav");
        _sfxServo = GD.Load<AudioStream>("res://assets/sfx/servo.wav");
        foreach (var st in new[] { _sfxWind, _sfxServo })
            if (st is AudioStreamWav lw)
            {
                lw.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
                lw.LoopBegin = 0;
                lw.LoopEnd = (int)lw.GetLength() * WavSampleRate;
            }
        _windPlayer = new AudioStreamPlayer { Stream = _sfxWind, VolumeDb = _windDb };
        AddChild(_windPlayer); _windPlayer.Play();
        _servoPlayer = new AudioStreamPlayer { Stream = _sfxServo, VolumeDb = _servoDb };
        AddChild(_servoPlayer); _servoPlayer.Play();
        _voxFollow = GD.Load<AudioStream>("res://assets/sfx/vox_follow.mp3");
        _voxAttackTarget = GD.Load<AudioStream>("res://assets/sfx/vox_attack_target.mp3");
        _voxAttackNearby = GD.Load<AudioStream>("res://assets/sfx/vox_attack_nearby.mp3");
        _voxProtect = GD.Load<AudioStream>("res://assets/sfx/vox_protect.mp3");
        _voxStop = GD.Load<AudioStream>("res://assets/sfx/vox_stop.mp3");
        _voxPlayer = new AudioStreamPlayer { VolumeDb = VoxVolumeDb }; // announcement level; tune here
        AddChild(_voxPlayer);
        for (int i = 0; i < SfxPoolSize; i++)
        {
            var p = new AudioStreamPlayer3D { MaxDistance = SfxPoolMaxDistance, VolumeDb = SfxBaseVolumeDb };
            AddChild(p);
            _sfxPool.Add(p);
            _sfxProtected.Add(false);
        }
        Dbg.Print($"SFX loaded laser={_sfxLaser != null} ppc={_sfxPpc != null} cannon={_sfxCannon != null} rocket={_sfxRocket != null} hit={_sfxHit != null} tree={_sfxTree != null} explosion={_sfxExplosion != null} steps={_sfxStepA != null}/{_sfxStepB != null} thruster={_sfxThruster != null}");
    }

    // Speak the order (owner, 2026-11-25): SetTeamOrder is
    // the only caller, so the voice clip and the feed line
    // always go together. A new order cuts the clip still
    // speaking - the latest command is the one that counts.
    private void AnnounceOrder(TeamOrder o)
    {
        if (_voxPlayer == null) return;
        _voxPlayer.Stream = o switch
        {
            TeamOrder.Follow => _voxFollow,
            TeamOrder.AttackTarget => _voxAttackTarget,
            TeamOrder.AttackNearby => _voxAttackNearby,
            TeamOrder.ProtectCompanion => _voxProtect,
            _ => _voxStop,
        };
        if (_voxPlayer.Stream == null) return;
        _voxPlayer.Stop();
        _voxPlayer.Play();
        Dbg.Print($"VOX order={o}");
    }

    // Ambience step (owner, 2026-11-24): fades only - the two
    // loops run continuously, so state changes never click.
    // Wind belongs to the world (silent in the lab); the
    // servo bed follows the player's OWN activity - ground
    // speed plus torso twist rate - and sits at the floor
    // when he stands still.
    private void StepAmbient(float dt)
    {
        if (_windPlayer == null || dt <= 0f) return;
        float windTarget = _labOpen ? AmbienceSilentDb : WindWorldDb; // owner, 2026-11-24: -26 buried the fight, -32 still too loud - the bed sits lower now
        _windDb = Mathf.MoveToward(_windDb, windTarget, dt * WindFadeRate);
        _windPlayer.VolumeDb = _windDb;

        float torso = _player != null ? _player.TorsoYawWorld : 0f;
        float yawRate = Mathf.Abs(Mathf.Wrap(torso - _prevTorsoYaw, -Mathf.Pi, Mathf.Pi)) / dt;
        _prevTorsoYaw = torso;
        float activity = 0f;
        if (!_labOpen && _player != null && !_playerDead)
        {
            activity = Mathf.Clamp(_playerVel.Length() / 4.2f, 0f, 1f) * 0.75f
                     + Mathf.Clamp(yawRate / 1.2f, 0f, 1f) * 0.6f;
            activity = Mathf.Clamp(activity, 0f, 1f);
        }
        float servoTarget = activity <= 0.02f ? AmbienceSilentDb : ServoBaseDb + activity * ServoActivityDb;
        _servoDb = Mathf.MoveToward(_servoDb, servoTarget, dt * ServoFadeRate);
        _servoPlayer.VolumeDb = _servoDb;
        _servoPlayer.PitchScale = ServoPitchBase + ServoPitchRange * activity;
    }

    private void PlaySfx(AudioStream stream, Vector3 pos, float volDb = 0f, float pitch = 1f)
    {
        if (stream == null || _sfxPool.Count == 0) return;
        // Idle-first (owner, 2026-11-23): the old round-robin
        // reassigned players that were still playing, cutting
        // sounds mid-play. His laser recording runs 1.1s and
        // every system shares this pool - footsteps, hits,
        // NPC fire - so under quick intermittent fire the
        // wrap landed inside the clip and the laser audibly
        // disappeared. Take the next FREE player; steal the
        // round-robin slot only when every player is busy.
        // Weapon reports and the death explosion are
        // PROTECTED (owner, 2026-11-24): his cannon clip
        // runs 1.92s, and in a busy fight the expendable
        // traffic (steps, hits) fills every voice inside
        // that window - the steal then landed on the cannon
        // mid-report. A protected sound is never the steal
        // victim while any expendable voice is playing.
        // The HIT joins the protected set (owner, 2026-11-24):
        // it is his 1.5s explosion recording, it was the one
        // fight sound left expendable, and in a missile salvo
        // (impacts every 60ms, steps and NPC fire crowding
        // the pool) the steal kept cutting it to its attack
        // transient - the 'plastic bottle' pop he reported.
        bool prot = stream == _sfxLaser || stream == _sfxPpc || stream == _sfxCannon
                    || stream == _sfxRocket || stream == _sfxExplosion || stream == _sfxHit;
        AudioStreamPlayer3D p = null; int pi = -1;
        for (int k = 0; k < _sfxPool.Count; k++)
        {
            int idx = (_sfxNext + k) % _sfxPool.Count;
            if (!_sfxPool[idx].Playing) { p = _sfxPool[idx]; pi = idx; _sfxNext = (idx + 1) % _sfxPool.Count; break; }
        }
        if (p == null)
        {
            for (int k = 0; k < _sfxPool.Count; k++)
            {
                int idx = (_sfxNext + k) % _sfxPool.Count;
                if (!_sfxProtected[idx]) { p = _sfxPool[idx]; pi = idx; _sfxNext = (idx + 1) % _sfxPool.Count; break; }
            }
        }
        if (p == null) { pi = _sfxNext++ % _sfxPool.Count; p = _sfxPool[pi]; }
        _sfxProtected[pi] = prot;
        p.Stream = stream;
        p.GlobalPosition = pos;
        p.VolumeDb = SfxBaseVolumeDb + volDb;
        p.PitchScale = pitch * _fxRng.RandfRange(PitchJitterMin, PitchJitterMax);
        p.Play();
    }

    // The full simulated field, the same set StompCheck walks:
    // player, primary enemy, second foe, NPC allies.
    private System.Collections.Generic.List<RiderWalker> SfxWalkers()
    {
        var walkers = new System.Collections.Generic.List<RiderWalker> { _player, _enemy };
        if (_foe != null) walkers.Add(_foe.Walker);
        foreach (var a in _allies) walkers.Add(a.Walker);
        return walkers;
    }

    private void StepSounds()
    {
        // Stomps for every rider (owner, 2026-11-17): a foot
        // plant fires the walker's step counter; volume and
        // pitch follow the gait - a running rider stomps louder
        // and lower than a walking one. Distance attenuation is
        // the 3D player's job.
        foreach (var w in SfxWalkers())
        {
            if (w == null || !w.IsReady || IsDead(w)) continue;
            if (!_stepsSeen.TryGetValue(w, out int seen)) { _stepsSeen[w] = w.Steps; continue; }
            if (w.Steps == seen) continue;
            _stepsSeen[w] = w.Steps;
            bool alt = _stepAltW.TryGetValue(w, out bool a) && a;
            _stepAltW[w] = !alt;
            bool running = w.Speed > RunStepSpeedThreshold;
            float vol = (w == _player ? PlayerStepDb : NpcStepDb) + (running ? RunStepDbBonus : 0f);
            PlaySfx(alt ? _sfxStepA : _sfxStepB, w.RootPos, vol, running ? RunStepPitch : 1f);
        }
    }

    private void ThrusterSounds()
    {
        // One looping player per thrusting rider (owner,
        // 2026-11-17): starts when the burn starts, stops when
        // it ends or the rider dies, positioned at the rider.
        if (_sfxThruster == null) return;
        foreach (var w in SfxWalkers())
        {
            if (w == null || !w.IsReady) continue;
            bool on = w.Thrusting && !IsDead(w);
            if (on)
            {
                if (!_thrusterPlayers.TryGetValue(w, out var p))
                {
                    p = new AudioStreamPlayer3D { Stream = _sfxThruster, MaxDistance = ThrusterMaxDistance, VolumeDb = ThrusterVolumeDb };  // owner, 2026-11-17: thrusters sit under the fight, not over it
                    AddChild(p);
                    _thrusterPlayers[w] = p;
                }
                p.GlobalPosition = w.RootPos + Vector3.Up * 2f;
                if (!p.Playing) p.Play();
            }
            else if (_thrusterPlayers.TryGetValue(w, out var p) && p.Playing)
                p.Stop();
        }
    }
}
