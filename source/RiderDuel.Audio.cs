using Godot;
using System.Collections.Generic;

// RiderDuel module: procedural sound: loading, the player pool, footsteps.
// Split from the single-file build (round 22 cleanup); the class
// stays one partial class - see RiderDuel.cs for the field block.
public partial class RiderDuel
{
    // ---- sound (round 21, owner): fully procedural WAVs in
    // assets/sfx (make_sfx.py - no external samples), played
    // through a small pool of 3D players so distance attenuates.
    private AudioStream _sfxLaser, _sfxCannon, _sfxRocket, _sfxHit, _sfxTree, _sfxExplosion, _sfxStepA, _sfxStepB, _sfxThruster;
    private readonly System.Collections.Generic.List<AudioStreamPlayer3D> _sfxPool = new();
    private int _sfxNext;
    private double _lastHitSfxT = -1;
    // Per-walker sound state (owner, 2026-11-17): EVERY rider
    // stomps, not just the duel pair, and every thrusting rider
    // gets its own looping thruster player.
    private readonly System.Collections.Generic.Dictionary<RiderWalker, int> _stepsSeen = new();
    private readonly System.Collections.Generic.Dictionary<RiderWalker, bool> _stepAltW = new();
    private readonly System.Collections.Generic.Dictionary<RiderWalker, AudioStreamPlayer3D> _thrusterPlayers = new();

    private void InitSfx()
    {
        _sfxLaser = GD.Load<AudioStream>("res://assets/sfx/laser.wav");
        _sfxCannon = GD.Load<AudioStream>("res://assets/sfx/cannon.wav");
        _sfxRocket = GD.Load<AudioStream>("res://assets/sfx/rocket.wav");
        _sfxHit = GD.Load<AudioStream>("res://assets/sfx/hit.wav");
        _sfxTree = GD.Load<AudioStream>("res://assets/sfx/tree.wav");
        _sfxExplosion = GD.Load<AudioStream>("res://assets/sfx/explosion.wav");
        _sfxStepA = GD.Load<AudioStream>("res://assets/sfx/step_a.wav");
        _sfxStepB = GD.Load<AudioStream>("res://assets/sfx/step_b.wav");
        _sfxThruster = GD.Load<AudioStream>("res://assets/sfx/thruster.wav");
        if (_sfxThruster is AudioStreamWav tw)
        {
            tw.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            tw.LoopBegin = 0;
            tw.LoopEnd = (int)tw.GetLength() * 22050;
        }
        for (int i = 0; i < 10; i++)
        {
            var p = new AudioStreamPlayer3D { MaxDistance = 220f, VolumeDb = -6f };
            AddChild(p);
            _sfxPool.Add(p);
        }
        Dbg.Print($"SFX loaded laser={_sfxLaser != null} cannon={_sfxCannon != null} rocket={_sfxRocket != null} hit={_sfxHit != null} tree={_sfxTree != null} explosion={_sfxExplosion != null} steps={_sfxStepA != null}/{_sfxStepB != null} thruster={_sfxThruster != null}");
    }

    private void PlaySfx(AudioStream stream, Vector3 pos, float volDb = 0f, float pitch = 1f)
    {
        if (stream == null || _sfxPool.Count == 0) return;
        var p = _sfxPool[_sfxNext++ % _sfxPool.Count];
        p.Stream = stream;
        p.GlobalPosition = pos;
        p.VolumeDb = -6f + volDb;
        p.PitchScale = pitch * _fxRng.RandfRange(0.94f, 1.06f);
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
            bool running = w.Speed > 7f;
            float vol = (w == _player ? -1f : -3f) + (running ? 2.5f : 0f);
            PlaySfx(alt ? _sfxStepA : _sfxStepB, w.RootPos, vol, running ? 0.9f : 1f);
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
                    p = new AudioStreamPlayer3D { Stream = _sfxThruster, MaxDistance = 260f, VolumeDb = -13f };  // owner, 2026-11-17: thrusters sit under the fight, not over it
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
