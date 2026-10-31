using Godot;
using System.Collections.Generic;

// MechDuel module: player input: throttle/turn, torso bearing, thruster key.
// Split from the single-file build (round 22 cleanup); the class
// stays one partial class — see MechDuel.cs for the field block.
public partial class MechDuel
{
    // ---------- input ----------

    public override void _Input(InputEvent e)
    {
        // While the MechLab is open the mouse belongs to the GUI: the
        // click-to-capture below would hide the cursor the moment a
        // drag starts and kill drag-and-drop (owner's bug report).
        if (_labOpen) return;
        if (e is InputEventKey rk && rk.Pressed && !rk.Echo && rk.Keycode == Key.R)
        {
            ResetDuel();
            return;
        }
        if (!_test && !_weaponsTest && !_breakTest && !_terrainTest)
        {
            // Weapon selection: keys 1/2/3, mouse wheel cycles.
            if (e is InputEventKey sk && sk.Pressed && !sk.Echo)
            {
                if (sk.Keycode == Key.Key1) _weaponSel = 0;
                else if (sk.Keycode == Key.Key2) _weaponSel = 1;
                else if (sk.Keycode == Key.Key3) _weaponSel = 2;
            }
            else if (e is InputEventMouseButton wb && wb.Pressed &&
                     (wb.ButtonIndex == MouseButton.WheelUp || wb.ButtonIndex == MouseButton.WheelDown))
            {
                int d = wb.ButtonIndex == MouseButton.WheelDown ? 1 : -1;
                _weaponSel = (_weaponSel + d + 3) % 3;
            }
        }
        if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            if (Input.MouseMode != Input.MouseModeEnum.Captured)
                Input.MouseMode = Input.MouseModeEnum.Captured;
        }
        else if (e is InputEventKey k && k.Pressed && k.Keycode == Key.Escape)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
        }
        else if (e is InputEventMouseMotion mm && Input.MouseMode == Input.MouseModeEnum.Captured && !_test)
        {
            // The mouse owns the torso's WORLD bearing (round 7);
            // MaintainTorsoWorldYaw turns it into a twist each frame.
            _torsoWorldYaw -= mm.Relative.X * 0.0030f;
            // The mouse target roams free (owner, round 13): the
            // wide aim pitch accumulates past the torso's mechanical
            // clamp, and the torso follows only as far as its stops
            // allow. Seeded from the torso on first motion.
            if (float.IsNaN(_player.AimPitchWide)) _player.AimPitchWide = _player.TorsoPitch;
            _player.AimPitchWide = Mathf.Clamp(_player.AimPitchWide - mm.Relative.Y * 0.0025f, -1.35f, 1.35f);
            _player.TorsoPitch = _player.AimPitchWide;
        }
    }

    // Round 7: the mouse owns the torso's WORLD bearing. A/D turns
    // the legs underneath; each frame the twist is re-derived so the
    // torso (and the camera riding it) stays where the mouse put it
    // instead of being dragged around by leg rotation — the two
    // inputs no longer fight over the same yaw. The walker's clamp
    // still bounds the twist, and reading the clamped value back
    // re-anchors the bearing so the mouse never winds up against
    // the stop.
    private void MaintainTorsoWorldYaw()
    {
        _player.TorsoTwist = MechWalker.WrapAngle(_torsoWorldYaw - _playerYaw);
        _torsoWorldYaw = _playerYaw + _player.TorsoTwist;
    }

    private void UpdatePlayerInput(float delta)
    {
        ApplyPlayerInput(delta,
            Input.IsPhysicalKeyPressed(Key.W), Input.IsPhysicalKeyPressed(Key.S),
            Input.IsPhysicalKeyPressed(Key.A), Input.IsPhysicalKeyPressed(Key.D),
            Input.IsPhysicalKeyPressed(Key.Shift));
        _player.SetThrust(Input.IsPhysicalKeyPressed(Key.Space)); // thrusters: hold SPACE to fly (fitted mechs only)
    }

    // The player's drive, with the keys passed in so the duel harness
    // can exercise the REAL input path. Leg damage does NOT gate this:
    // the round-1 "a lost leg immobilizes" rule was retired — a wrecked
    // leg only throttles the walker (SpeedFactor: limp 0.55 / crawl
    // 0.25) and turns slower by the same factor. (The retired gate
    // survived here after the walker-side rework and left the player
    // permanently stuck the moment a leg was wrecked: no forward, no
    // reverse, no turn — the owner's playtest report.)
    private void ApplyPlayerInput(float delta, bool w, bool s, bool a, bool d, bool shift)
    {
        float f = _player.SpeedFactor;
        if (a) _playerYaw += TurnRate * f * delta;
        if (d) _playerYaw -= TurnRate * f * delta;
        // SHIFT + W = run mode (RunFactor x walk speed); reverse stays at walk pace.
        _running = shift && w;
        float spd = 0f;
        if (w) spd = _playerSpec.MaxSpeed * (_running ? RunFactor : 1f);
        if (w && _player.Thrusting) spd += 3.0f; // jets burning + forward key: the thrusters push the mech forward too (owner)
        else if (s) spd = -_playerSpec.MaxSpeed * 0.55f;
        _player.SetDrive(_playerYaw, spd);
    }

}
