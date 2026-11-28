using Godot;

namespace Titans;

// Titans module: player input: throttle/turn, torso bearing, thruster key.
// Split from the single-file build (round 22 cleanup); the class
// stays one partial class - see Titans.cs for the field block.
public partial class Titans
{
  // ---------- input ----------

  // Caps Lock state (owner, 2026-11-11 round 6: 'when
  // Capslock is on, switch to run mode'). Godot's input
  // events carry no lock state, so on Windows the toggle
  // is polled from the OS each frame (GetKeyState,
  // VK_CAPITAL); elsewhere it is tracked from the
  // CapsLock key's own presses.
  private bool _capsLockOn;
  private const int VkCapital = 0x14;              // Windows virtual-key code for Caps Lock
  private const float MouseYawSensitivity = 0.0030f;
  private const float MousePitchSensitivity = 0.0025f;
  private const float AimPitchCap = 1.35f;         // the mouse aim pitch clamp, radians
  private const float ReverseSpeedFactor = 0.55f;  // reverse pace vs walk speed
  private const float ThrusterForwardBonus = 3.0f; // jets burning + forward key: extra forward push

  [System.Runtime.InteropServices.DllImport("user32.dll")]
  private static extern short GetKeyState(int vKey);

  private void PollCapsLock()
  {
    if (OS.HasFeature("windows"))
    {
      this._capsLockOn = (GetKeyState(VkCapital) & 1) != 0; // VK_CAPITAL toggle bit
    }
  }

  public override void _Input(InputEvent @event)
  {
    if (!this._built)
    {
      return; // staged build in progress - the loading cover is up
    }

    if (!OS.HasFeature("windows") && @event is InputEventKey ck && ck.Pressed && !ck.Echo && ck.Keycode == Key.Capslock)
    {
      this._capsLockOn = !this._capsLockOn;
    }
    // While the RiderLab is open the mouse belongs to the GUI: the
    // click-to-capture below would hide the cursor the moment a
    // drag starts and kill drag-and-drop (owner's bug report).
    if (this._labOpen)
    {
      return;
    }

    if (@event is InputEventKey rk && rk.Pressed && !rk.Echo && rk.Keycode == Key.R)
    {
      this.ResetDuel();
      return;
    }
    if (@event is InputEventMouseButton zb && zb.Pressed && zb.ButtonIndex == MouseButton.Right)
    {
      // Sniper view toggle (owner, 2026-11-22; moved
      // from T to RMB at the owner's word, 2026-11-25):
      // the FOV ease and the mouse-look scaling ride on
      // _sniper. A press toggles, as T did.
      this._sniper = !this._sniper;
      if (Input.MouseMode != Input.MouseModeEnum.Captured)
      {
        Input.MouseMode = Input.MouseModeEnum.Captured;
      }

      this.GetViewport().SetInputAsHandled();
      return;
    }
    if (@event is InputEventKey tk && tk.Pressed && !tk.Echo && tk.Keycode == Key.Tab)
    {
      // The TAB family (owner; the two modifier forms
      // added 2026-11-23). Handled here so Tab never
      // moves UI focus.
      if (tk.CtrlPressed)
      {
        // CTRL+TAB: cycle the active companion -
        // SHIFT+5's protect order anchors on him.
        // The feed names the pick.
        this.CycleCompanion();
      }
      else if (tk.ShiftPressed)
      {
        // SHIFT+TAB: jump the target lock straight
        // to the CLOSEST living enemy.
        var cycS = this.OpposingTargets();
        int best = -1; float bd = float.MaxValue;
        for (int i = 0; i < cycS.Count; i++)
        {
          if (cycS[i].Dead || cycS[i].W == null)
          {
            continue;
          }

          float d = this._player.RootPos.DistanceSquaredTo(cycS[i].W.RootPos);
          if (d < bd) { bd = d; best = i; }
        }
        if (best >= 0)
        {
          this._targetIdx = best;
        }
      }
      else
      {
        // Plain TAB: cycle the ONE active enemy among
        // the living opponents. The cycle spans every
        // opposing rider on the field (Stage D3) - in
        // the classic arena that is the two AI seats,
        // as before.
        var cyc = this.OpposingTargets();
        int next = this._targetIdx;
        for (int i = 0; i < cyc.Count; i++)
        {
          next = (next + 1) % cyc.Count;
          if (!cyc[next].Dead)
          {
            break;
          }
        }
        this._targetIdx = next;
      }
      this.GetViewport().SetInputAsHandled();
      return;
    }
    if (!this._test && !this._weaponsTest && !this._breakTest && !this._terrainTest)
    {
      // Team orders (owner, teams stage step 5): SHIFT+1..5
      // command the NPC teammates. Intercepted before the
      // weapon-group keys below - a shifted number is an
      // order, not a group switch.
      if (@event is InputEventKey ok && ok.Pressed && !ok.Echo && ok.ShiftPressed
          && WorldConfig.IsMission && this._allies.Count > 0)
      {
        TeamOrder? ord = ok.Keycode switch
        {
          Key.Key1 => TeamOrder.Follow,
          Key.Key2 => TeamOrder.AttackTarget,
          Key.Key3 => TeamOrder.AttackNearby,
          Key.Key4 => TeamOrder.Hold,
          Key.Key5 => TeamOrder.ProtectCompanion,
          _ => null
        };
        if (ord.HasValue)
        {
          this.SetTeamOrder(ord.Value);
          this.AddFeed(ord.Value switch
          {
            TeamOrder.Follow => "ORDER: FOLLOW ME",
            TeamOrder.AttackTarget => "ORDER: ATTACK MY TARGET",
            TeamOrder.AttackNearby => "ORDER: ATTACK CLOSEST ENEMY",
            TeamOrder.ProtectCompanion => "ORDER: PROTECT MY COMPANION",
            _ => "ORDER: STOP"
          });
          this.GetViewport().SetInputAsHandled();
          return;
        }
      }
      // Weapon selection (owner's groups): keys 1-6 pick
      // the group by its lab number, the wheel cycles the
      // groups that exist.
      if (@event is InputEventKey sk && sk.Pressed && !sk.Echo)
      {
        int num = sk.Keycode switch
        {
          Key.Key1 => 1,
          Key.Key2 => 2,
          Key.Key3 => 3,
          Key.Key4 => 4,
          Key.Key5 => 5,
          Key.Key6 => 6,
          _ => 0
        };
        if (num > 0)
        {
          int gi = this._groups.FindIndex(g => g.Num == num);
          if (gi >= 0)
          {
            this._weaponSel = gi;
          }
        }
      }
      else if (@event is InputEventMouseButton wb && wb.Pressed && this._groups.Count > 0 &&
               (wb.ButtonIndex == MouseButton.WheelUp || wb.ButtonIndex == MouseButton.WheelDown))
      {
        int d = wb.ButtonIndex == MouseButton.WheelDown ? 1 : -1;
        this._weaponSel = (this._weaponSel + d + this._groups.Count) % this._groups.Count;
      }
    }
    if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
    {
      if (Input.MouseMode != Input.MouseModeEnum.Captured)
      {
        Input.MouseMode = Input.MouseModeEnum.Captured;
      }
    }
    else if (@event is InputEventKey k && k.Pressed && !k.Echo && k.Keycode == Key.Escape)
    {
      // Leave the game (owner): release the mouse, pause
      // the sim, and ask for confirmation. Esc is ONLY
      // the exit path (owner, 2026-11-17: Esc and the
      // team screen are separated - the team screen is
      // F2, the controls screen F1, branches below).
      Input.MouseMode = Input.MouseModeEnum.Visible;
      bool teamUp = (this._teamDialog != null && this._teamDialog.Visible)
          || (this._controlsDialog != null && this._controlsDialog.Visible);
      if (this._leaveDialog != null && !this._leaveDialog.Visible && !teamUp)
      {
        this._leaveDialog.Open();
        this.GetTree().Paused = true;
      }
    }
    else if (@event is InputEventKey kf && kf.Pressed && !kf.Echo && kf.Keycode == Key.F2)
    {
      // Team screen (owner, 2026-11-17; moved F1 -> F2,
      // 2026-11-23): F2 opens it in a mission - pick a
      // leader there, or exit from it. Pressing F2 again
      // closes it (the dialog's own input handler); Esc
      // closes it too.
      bool leaveUp = this._leaveDialog != null && this._leaveDialog.Visible;
      bool teamUp = this._teamDialog != null && this._teamDialog.Visible;
      if (!leaveUp && !teamUp && WorldConfig.IsMission && this._teamDialog != null)
      {
        Input.MouseMode = Input.MouseModeEnum.Visible;
        this._teamDialog.Open(this.TeamRows());
        this.GetTree().Paused = true;
      }
    }
    else if (@event is InputEventKey kc && kc.Pressed && !kc.Echo && kc.Keycode == Key.F1)
    {
      // Controls screen (owner, 2026-11-23): F1 lists
      // the game's key bindings. Same pause contract as
      // the other dialogs; F1 or Esc closes it (the
      // dialog's own input handler).
      bool leaveUp = this._leaveDialog != null && this._leaveDialog.Visible;
      bool teamUp = this._teamDialog != null && this._teamDialog.Visible;
      bool ctlUp = this._controlsDialog != null && this._controlsDialog.Visible;
      if (!leaveUp && !teamUp && !ctlUp && !this._labOpen && this._controlsDialog != null)
      {
        Input.MouseMode = Input.MouseModeEnum.Visible;
        this._controlsDialog.Open();
        this.GetTree().Paused = true;
      }
    }
    else if (@event is InputEventMouseMotion mm && Input.MouseMode == Input.MouseModeEnum.Captured && !this._test)
    {
      // The mouse owns the torso's WORLD bearing (round 7);
      // MaintainTorsoWorldYaw turns it into a twist each frame.
      // In sniper view the look slows by the zoom ratio, so
      // fine aim stays controllable at 16 degrees.
      float sensK = this._sniper ? SniperFov / BaseFov : 1f;
      this._torsoWorldYaw -= mm.Relative.X * MouseYawSensitivity * sensK;
      // The mouse target roams free (owner, round 13): the
      // wide aim pitch accumulates past the torso's mechanical
      // clamp, and the torso follows only as far as its stops
      // allow. Seeded from the torso on first motion.
      if (float.IsNaN(this._player.AimPitchWide))
      {
        this._player.AimPitchWide = this._player.TorsoPitch;
      }

      this._player.AimPitchWide = Mathf.Clamp(this._player.AimPitchWide - mm.Relative.Y * MousePitchSensitivity * sensK, -AimPitchCap, AimPitchCap);
      this._player.TorsoPitch = this._player.AimPitchWide;
    }
  }

  // Round 7: the mouse owns the torso's WORLD bearing. A/D turns
  // the legs underneath; each frame the twist is re-derived so the
  // torso (and the camera riding it) stays where the mouse put it
  // instead of being dragged around by leg rotation - the two
  // inputs no longer fight over the same yaw. The walker's clamp
  // still bounds the twist, and reading the clamped value back
  // re-anchors the bearing so the mouse never winds up against
  // the stop.
  private void MaintainTorsoWorldYaw()
  {
    this._player.TorsoTwist = RiderWalker.WrapAngle(this._torsoWorldYaw - this._playerYaw);
    this._torsoWorldYaw = this._playerYaw + this._player.TorsoTwist;
  }

  private void UpdatePlayerInput(float delta)
  {
    this.PollCapsLock();
    this.ApplyPlayerInput(delta,
            Input.IsPhysicalKeyPressed(Key.W), Input.IsPhysicalKeyPressed(Key.S),
            Input.IsPhysicalKeyPressed(Key.A), Input.IsPhysicalKeyPressed(Key.D),
            Input.IsPhysicalKeyPressed(Key.Shift));
    this._player.SetThrust(Input.IsPhysicalKeyPressed(Key.Space)); // thrusters: hold SPACE to fly (fitted riders only)
  }

  // The player's drive, with the keys passed in so the duel harness
  // can exercise the REAL input path. Leg damage does NOT gate this:
  // the round-1 "a lost leg immobilizes" rule was retired - a wrecked
  // leg only throttles the walker (SpeedFactor: limp 0.55 / crawl
  // 0.25) and turns slower by the same factor. (The retired gate
  // survived here after the walker-side rework and left the player
  // permanently stuck the moment a leg was wrecked: no forward, no
  // reverse, no turn - the owner's playtest report.)
  private void ApplyPlayerInput(float delta, bool w, bool s, bool a, bool d, bool shift)
  {
    float f = this._player.SpeedFactor;
    if (a)
    {
      this._playerYaw += TurnRate * f * delta;
    }

    if (d)
    {
      this._playerYaw -= TurnRate * f * delta;
    }
    // Run mode (RunFactor x walk speed): SHIFT held, or
    // Caps Lock on (owner round 6); W only - reverse
    // stays at walk pace.
    this._running = (shift || this._capsLockOn) && w;
    float spd = 0f;
    if (w)
    {
      spd = this._playerSpec.MaxSpeed * (this._running ? RunFactor : 1f);
    }

    if (w && this._player.Thrusting)
    {
      spd += ThrusterForwardBonus; // jets burning + forward key: the thrusters push the rider forward too (owner)
    }
    else if (s)
    {
      spd = -this._playerSpec.MaxSpeed * ReverseSpeedFactor;
    }

    this._player.SetDrive(this._playerYaw, spd);
  }

}
