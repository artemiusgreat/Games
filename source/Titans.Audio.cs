using Godot;

namespace Titans;

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
  private const float ThrusterSoundHeight = 2f;        // thruster loop rides this far above the rider's root
                                                       // Servo-bed activity mix: ground speed and torso yaw rate,
                                                       // each normalized by its scale and weighted; at or below
                                                       // the idle threshold the bed drops to the silent floor.
  private const float ServoSpeedScale = 4.2f, ServoSpeedWeight = 0.75f;
  private const float ServoYawScale = 1.2f, ServoYawWeight = 0.6f;
  private const float ServoIdleThreshold = 0.02f;
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
    this._sfxLaser = GD.Load<AudioStream>(Paths.SfxLaser);
    this._sfxPpc = GD.Load<AudioStream>(Paths.SfxPpc);
    this._sfxCannon = GD.Load<AudioStream>(Paths.SfxCannon);
    this._sfxRocket = GD.Load<AudioStream>(Paths.SfxRocket);
    this._sfxHit = GD.Load<AudioStream>(Paths.SfxHit);
    this._sfxTree = GD.Load<AudioStream>(Paths.SfxTree);
    this._sfxExplosion = GD.Load<AudioStream>(Paths.SfxExplosion);
    this._sfxStepA = GD.Load<AudioStream>(Paths.SfxStepA);
    this._sfxStepB = GD.Load<AudioStream>(Paths.SfxStepB);
    this._sfxThruster = GD.Load<AudioStream>(Paths.SfxThruster);
    if (this._sfxThruster is AudioStreamWav tw)
    {
      tw.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
      tw.LoopBegin = 0;
      tw.LoopEnd = (int)tw.GetLength() * WavSampleRate;
    }
    this._sfxWind = GD.Load<AudioStream>(Paths.SfxWind);
    this._sfxServo = GD.Load<AudioStream>(Paths.SfxServo);
    foreach (var st in new[] { this._sfxWind, this._sfxServo })
    {
      if (st is AudioStreamWav lw)
      {
        lw.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        lw.LoopBegin = 0;
        lw.LoopEnd = (int)lw.GetLength() * WavSampleRate;
      }
    }

    this._windPlayer = new AudioStreamPlayer { Stream = this._sfxWind, VolumeDb = this._windDb };
    this.AddChild(this._windPlayer); this._windPlayer.Play();
    this._servoPlayer = new AudioStreamPlayer { Stream = this._sfxServo, VolumeDb = this._servoDb };
    this.AddChild(this._servoPlayer); this._servoPlayer.Play();
    this._voxFollow = GD.Load<AudioStream>(Paths.VoxFollow);
    this._voxAttackTarget = GD.Load<AudioStream>(Paths.VoxAttackTarget);
    this._voxAttackNearby = GD.Load<AudioStream>(Paths.VoxAttackNearby);
    this._voxProtect = GD.Load<AudioStream>(Paths.VoxProtect);
    this._voxStop = GD.Load<AudioStream>(Paths.VoxStop);
    this._voxPlayer = new AudioStreamPlayer { VolumeDb = VoxVolumeDb }; // announcement level; tune here
    this.AddChild(this._voxPlayer);
    for (int i = 0; i < SfxPoolSize; i++)
    {
      var p = new AudioStreamPlayer3D { MaxDistance = SfxPoolMaxDistance, VolumeDb = SfxBaseVolumeDb };
      this.AddChild(p);
      this._sfxPool.Add(p);
      this._sfxProtected.Add(false);
    }
    Dbg.Print($"SFX loaded laser={this._sfxLaser != null} ppc={this._sfxPpc != null} cannon={this._sfxCannon != null} rocket={this._sfxRocket != null} hit={this._sfxHit != null} tree={this._sfxTree != null} explosion={this._sfxExplosion != null} steps={this._sfxStepA != null}/{this._sfxStepB != null} thruster={this._sfxThruster != null}");
  }

  // Speak the order (owner, 2026-11-25): SetTeamOrder is
  // the only caller, so the voice clip and the feed line
  // always go together. A new order cuts the clip still
  // speaking - the latest command is the one that counts.
  private void AnnounceOrder(TeamOrder o)
  {
    if (this._voxPlayer == null)
    {
      return;
    }

    this._voxPlayer.Stream = o switch
    {
      TeamOrder.Follow => this._voxFollow,
      TeamOrder.AttackTarget => this._voxAttackTarget,
      TeamOrder.AttackNearby => this._voxAttackNearby,
      TeamOrder.ProtectCompanion => this._voxProtect,
      _ => this._voxStop,
    };
    if (this._voxPlayer.Stream == null)
    {
      return;
    }

    this._voxPlayer.Stop();
    this._voxPlayer.Play();
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
    if (this._windPlayer == null || dt <= 0f)
    {
      return;
    }

    float windTarget = this._labOpen ? AmbienceSilentDb : WindWorldDb; // owner, 2026-11-24: -26 buried the fight, -32 still too loud - the bed sits lower now
    this._windDb = Mathf.MoveToward(this._windDb, windTarget, dt * WindFadeRate);
    this._windPlayer.VolumeDb = this._windDb;

    float torso = this._player != null ? this._player.TorsoYawWorld : 0f;
    float yawRate = Mathf.Abs(Mathf.Wrap(torso - this._prevTorsoYaw, -Mathf.Pi, Mathf.Pi)) / dt;
    this._prevTorsoYaw = torso;
    float activity = 0f;
    if (!this._labOpen && this._player != null && !this._playerDead)
    {
      activity = Mathf.Clamp(this._playerVel.Length() / ServoSpeedScale, 0f, 1f) * ServoSpeedWeight
               + Mathf.Clamp(yawRate / ServoYawScale, 0f, 1f) * ServoYawWeight;
      activity = Mathf.Clamp(activity, 0f, 1f);
    }
    float servoTarget = activity <= ServoIdleThreshold ? AmbienceSilentDb : ServoBaseDb + activity * ServoActivityDb;
    this._servoDb = Mathf.MoveToward(this._servoDb, servoTarget, dt * ServoFadeRate);
    this._servoPlayer.VolumeDb = this._servoDb;
    this._servoPlayer.PitchScale = ServoPitchBase + ServoPitchRange * activity;
  }

  private void PlaySfx(AudioStream stream, Vector3 pos, float volDb = 0f, float pitch = 1f)
  {
    if (stream == null || this._sfxPool.Count == 0)
    {
      return;
    }
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
    bool prot = stream == this._sfxLaser || stream == this._sfxPpc || stream == this._sfxCannon
                    || stream == this._sfxRocket || stream == this._sfxExplosion || stream == this._sfxHit;
    AudioStreamPlayer3D p = null; int pi = -1;
    for (int k = 0; k < this._sfxPool.Count; k++)
    {
      int idx = (this._sfxNext + k) % this._sfxPool.Count;
      if (!this._sfxPool[idx].Playing) { p = this._sfxPool[idx]; pi = idx; this._sfxNext = (idx + 1) % this._sfxPool.Count; break; }
    }
    if (p == null)
    {
      for (int k = 0; k < this._sfxPool.Count; k++)
      {
        int idx = (this._sfxNext + k) % this._sfxPool.Count;
        if (!this._sfxProtected[idx]) { p = this._sfxPool[idx]; pi = idx; this._sfxNext = (idx + 1) % this._sfxPool.Count; break; }
      }
    }
    if (p == null) { pi = this._sfxNext++ % this._sfxPool.Count; p = this._sfxPool[pi]; }
    this._sfxProtected[pi] = prot;
    p.Stream = stream;
    p.GlobalPosition = pos;
    p.VolumeDb = SfxBaseVolumeDb + volDb;
    p.PitchScale = pitch * this._fxRng.RandfRange(PitchJitterMin, PitchJitterMax);
    p.Play();
  }

  // The full simulated field, the same set StompCheck walks:
  // player, primary enemy, second foe, NPC allies.
  private System.Collections.Generic.List<RiderWalker> SfxWalkers()
  {
    var walkers = new System.Collections.Generic.List<RiderWalker> { this._player, this._enemy };
    if (this._foe != null)
    {
      walkers.Add(this._foe.Walker);
    }

    foreach (var a in this._allies)
    {
      walkers.Add(a.Walker);
    }

    return walkers;
  }

  private void StepSounds()
  {
    // Stomps for every rider (owner, 2026-11-17): a foot
    // plant fires the walker's step counter; volume and
    // pitch follow the gait - a running rider stomps louder
    // and lower than a walking one. Distance attenuation is
    // the 3D player's job.
    foreach (var w in this.SfxWalkers())
    {
      if (w == null || !w.IsReady || this.IsDead(w))
      {
        continue;
      }

      if (!this._stepsSeen.TryGetValue(w, out int seen)) { this._stepsSeen[w] = w.Steps; continue; }
      if (w.Steps == seen)
      {
        continue;
      }

      this._stepsSeen[w] = w.Steps;
      bool alt = this._stepAltW.TryGetValue(w, out bool a) && a;
      this._stepAltW[w] = !alt;
      bool running = w.Speed > RunStepSpeedThreshold;
      float vol = (w == this._player ? PlayerStepDb : NpcStepDb) + (running ? RunStepDbBonus : 0f);
      this.PlaySfx(alt ? this._sfxStepA : this._sfxStepB, w.RootPos, vol, running ? RunStepPitch : 1f);
    }
  }

  private void ThrusterSounds()
  {
    // One looping player per thrusting rider (owner,
    // 2026-11-17): starts when the burn starts, stops when
    // it ends or the rider dies, positioned at the rider.
    if (this._sfxThruster == null)
    {
      return;
    }

    foreach (var w in this.SfxWalkers())
    {
      if (w == null || !w.IsReady)
      {
        continue;
      }

      bool on = w.Thrusting && !this.IsDead(w);
      if (on)
      {
        if (!this._thrusterPlayers.TryGetValue(w, out var p))
        {
          p = new AudioStreamPlayer3D { Stream = this._sfxThruster, MaxDistance = ThrusterMaxDistance, VolumeDb = ThrusterVolumeDb };  // owner, 2026-11-17: thrusters sit under the fight, not over it
          this.AddChild(p);
          this._thrusterPlayers[w] = p;
        }
        p.GlobalPosition = w.RootPos + Vector3.Up * ThrusterSoundHeight;
        if (!p.Playing)
        {
          p.Play();
        }
      }
      else if (this._thrusterPlayers.TryGetValue(w, out var p) && p.Playing)
      {
        p.Stop();
      }
    }
  }
}
