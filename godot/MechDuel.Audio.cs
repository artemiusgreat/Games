using Godot;
using System.Collections.Generic;

// MechDuel module: procedural sound: loading, the player pool, footsteps.
// Split from the single-file build (round 22 cleanup); the class
// stays one partial class — see MechDuel.cs for the field block.
public partial class MechDuel
{
    // ---- sound (round 21, owner): fully procedural WAVs in
    // assets/sfx (make_sfx.py — no external samples), played
    // through a small pool of 3D players so distance attenuates.
    private AudioStream _sfxLaser, _sfxCannon, _sfxRocket, _sfxHit, _sfxTree, _sfxExplosion, _sfxStepA, _sfxStepB;
    private readonly System.Collections.Generic.List<AudioStreamPlayer3D> _sfxPool = new();
    private int _sfxNext;
    private double _lastHitSfxT = -1;
    private int _stepsPlayerSeen, _stepsEnemySeen;
    private bool _stepAlt;

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
        for (int i = 0; i < 10; i++)
        {
            var p = new AudioStreamPlayer3D { MaxDistance = 220f, VolumeDb = -6f };
            AddChild(p);
            _sfxPool.Add(p);
        }
        GD.Print($"SFX loaded laser={_sfxLaser != null} cannon={_sfxCannon != null} rocket={_sfxRocket != null} hit={_sfxHit != null} tree={_sfxTree != null} explosion={_sfxExplosion != null} steps={_sfxStepA != null}/{_sfxStepB != null}");
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

    private void StepSounds()
    {
        if (_player.Steps != _stepsPlayerSeen)
        {
            _stepsPlayerSeen = _player.Steps;
            _stepAlt = !_stepAlt;
            PlaySfx(_stepAlt ? _sfxStepA : _sfxStepB, _player.RootPos, -2f);
        }
        if (_enemy.Steps != _stepsEnemySeen)
        {
            _stepsEnemySeen = _enemy.Steps;
            PlaySfx(_stepAlt ? _sfxStepB : _sfxStepA, _enemy.RootPos, -4f);
        }
    }
}
