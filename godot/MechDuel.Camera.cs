using Godot;
using System.Collections.Generic;

// MechDuel module: capture pipeline and the proof/live camera modes.
// Split from the single-file build (round 22 cleanup); the class
// stays one partial class — see MechDuel.cs for the field block.
public partial class MechDuel
{
    private void QueueCapture(string path, string tag, string camMode, System.Action after = null)
    {
        if (_pendingPath != null || _capturing) return;
        _pendingPath = path; _pendingTag = tag; _camMode = camMode;
        _afterCapture = after;
    }

    private async void DoCapture()
    {
        _capturing = true;
        _frozen = true;
        string path = _pendingPath, tag = _pendingTag;
        _pendingPath = null;
        UpdateCamera(0f);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var img = GetViewport().GetTexture().GetImage();
        var err = img.SavePng(path);
        GD.Print($"DUEL captured {path} ({tag}) size={img.GetSize()} err={err}");
        _frozen = false;
        _capturing = false;
        var after = _afterCapture;
        _afterCapture = null;
        after?.Invoke();
        if (_phase == 7 && tag == "mech")
        {
            GD.Print($"DUEL DONE playerSteps={_player.Steps} atlasSteps={_enemy.Steps} atlasMaxSlide={_enemy.MaxSlide:F4} twMaxSlide={_player.MaxSlide:F4}");
            // One frame from the interactive chase camera (the player's
            // actual view) before quitting — framing proof for cam changes.
            QueueCapture("res://proof/duel_chase.png", "chase", "chase");
        }
        else if (tag == "chase")
        {
            GetTree().Quit();
        }
    }

    private void UpdateCamera(float delta)
    {
        if (_camMode == "enemyFront")
        {
            // In front of the enemy, on its line of advance: the
            // frontal view (knee stance reads here, not from the side).
            var p = _enemy.RootPos;
            var toPlayer = new Vector3(_player.RootPos.X - p.X, 0f, _player.RootPos.Z - p.Z).Normalized();
            _cam.GlobalPosition = p + toPlayer * 16f + new Vector3(0f, 5.5f, 0f);
            _cam.LookAt(p + new Vector3(0f, 6.5f, 0f), Vector3.Up);
        }
        else if (_camMode == "enemySide")
        {
            var p = _enemy.RootPos;
            _cam.GlobalPosition = p + new Vector3(-17.5f, 5.5f, -3f);
            _cam.LookAt(p + new Vector3(0f, 6.8f, 0f), Vector3.Up);
        }
        else if (_camMode == "wide")
        {
            var mid = (_player.RootPos + _enemy.RootPos) / 2f;
            _cam.GlobalPosition = mid + new Vector3(-27f, 15f, -27f);
            _cam.LookAt(mid + new Vector3(0f, 5f, 0f), Vector3.Up);
        }
        else if (_camMode == "twist")
        {
            var p = _player.RootPos;
            _cam.GlobalPosition = p + new Vector3(-11f, 7f, -11f);
            _cam.LookAt(p + new Vector3(0f, 8f, 0f), Vector3.Up);
        }
        else if (_camMode == "prop")
        {
            var mid = (_player.RootPos + new Vector3(6f, 0f, 16f)) / 2f;
            _cam.GlobalPosition = mid + new Vector3(11f, 5f, 11f);
            _cam.LookAt(mid + new Vector3(0f, 3.5f, 0f), Vector3.Up);
        }
        else if (_camMode == "wpnSide" || _camMode == "wpnRockets")
        {
            var mid = (_player.RootPos + _enemy.RootPos) / 2f;
            float h = _camMode == "wpnRockets" ? 10.5f : 6.0f;
            _cam.GlobalPosition = mid + new Vector3(-26f, h, 3f);
            _cam.LookAt(mid + new Vector3(0f, 6.5f, 0f), Vector3.Up);
        }
        else if (_camMode == "wpnBlocked")
        {
            _cam.GlobalPosition = new Vector3(43f, 9.5f, 17f);
            _cam.LookAt(new Vector3(22f, 5.5f, 3f), Vector3.Up);
        }
        else if (_camMode == "wpnDead")
        {
            // From the player's side (sunlit front), pulled well back.
            var p = _enemy.RootPos;
            _cam.GlobalPosition = p + new Vector3(16f, 7f, 13f);
            _cam.LookAt(p + new Vector3(0f, 6.5f, 0f), Vector3.Up);
        }
        else if (_camMode == "wpnHud")
        {
            // Rear three-quarter: player mech in frame, Atlas visible past it.
            var torsoYaw = _player.TorsoYawWorld;
            var fwd = MechWalker.HeadingVec(torsoYaw);
            var right = new Vector3(-fwd.Z, 0f, fwd.X);
            _cam.GlobalPosition = _player.RootPos - fwd * 27f + right * 8.5f + Vector3.Up * 11f;
            _cam.LookAt(_player.RootPos + Vector3.Up * 6f + fwd * 9f, Vector3.Up);
        }
        else if (_camMode == "mechSide")
        {
            var mid = (_player.RootPos + _enemy.RootPos) / 2f;
            _cam.GlobalPosition = mid + new Vector3(-18f, 6.5f, 0f);
            _cam.LookAt(mid + new Vector3(0f, 6f, 0f), Vector3.Up);
        }
        else if (_camMode == "brkPlayer")
        {
            var p = _player.RootPos;
            _cam.GlobalPosition = p + new Vector3(-15f, 5.5f, 5f);
            _cam.LookAt(p + new Vector3(0f, 6.5f, 0f), Vector3.Up);
        }
        else if (_camMode == "brkAtlas")
        {
            var p = _enemy.RootPos;
            _cam.GlobalPosition = p + new Vector3(14.5f, 6.5f, -5f);
            _cam.LookAt(p + new Vector3(0f, 6.5f, 0f), Vector3.Up);
        }
        else if (_camMode == "brkDebris")
        {
            _cam.GlobalPosition = _camFocus + new Vector3(-8.5f, 3.4f, 6.5f);
            _cam.LookAt(_camFocus + Vector3.Up * 0.4f, Vector3.Up);
        }
        else if (_camMode == "detailBuilding")
        {
            // Close on the (22,8) building: brick courses + glass panes.
            var bp = new Vector3(22f, Main.HeightAt(22f, 8f), 8f);
            _cam.GlobalPosition = bp + new Vector3(-10.5f, 4.6f, 10.5f);
            _cam.LookAt(bp + new Vector3(0f, 3.4f, 0f), Vector3.Up);
        }
        else if (_camMode == "detailTree")
        {
            // Close on the hand-placed tree at (-12,-8): branches + leaves.
            var tp = new Vector3(-12f, Main.HeightAt(-12f, -8f), -8f);
            _cam.GlobalPosition = tp + new Vector3(5.2f, 3.1f, 5.2f);
            _cam.LookAt(tp + new Vector3(0f, 3.4f, 0f), Vector3.Up);
        }
        else if (_camMode == "vista")
        {
            _cam.GlobalPosition = new Vector3(-88f, 44f, 90f);
            _cam.LookAt(new Vector3(12f, 0f, -12f), Vector3.Up);
        }
        else if (_camMode == "photoOver")
        {
            // Photoreal probe money shot: elevated over the arena,
            // both mechs in frame, mountains behind.
            _cam.GlobalPosition = new Vector3(58f, 34f, 64f);
            _cam.LookAt(new Vector3(-6f, 0f, -6f), Vector3.Up);
        }
        else if (_camMode == "photoTuft")
        {
            // Debug: one tuft cluster at arm's length.
            _cam.GlobalPosition = new Vector3(-18.6f, Main.HeightAt(-18.6f, 2.2f) + 0.55f, 2.2f);
            _cam.LookAt(new Vector3(-21.5f, Main.HeightAt(-21.5f, -1.5f) + 0.30f, -1.5f), Vector3.Up);
        }
        else if (_camMode == "photoGround")
        {
            // Ground level by the hand-placed trees, looking across
            // the road at the Timber Wolf.
            var tp = new Vector3(-21f, Main.HeightAt(-21f, 0f) + 2.1f, -1f);
            _cam.GlobalPosition = tp;
            _cam.LookAt(_player.RootPos + Vector3.Up * 5.5f, Vector3.Up);
        }
        else if (_camMode == "slopeSide")
        {
            var p = _player.RootPos;
            var fwd = MechWalker.HeadingVec(_player.LegsYaw);
            var right = new Vector3(-fwd.Z, 0f, fwd.X);
            _cam.GlobalPosition = p - right * 15f + Vector3.Up * 4.2f;
            _cam.LookAt(p + Vector3.Up * 5.5f, Vector3.Up);
        }
        else if (_camMode == "groundDuel")
        {
            var p = _player.RootPos;
            var fwd = MechWalker.HeadingVec(_player.LegsYaw);
            _cam.GlobalPosition = p - fwd * 11f + Vector3.Up * 3.4f;
            _cam.LookAt(_enemy.RootPos + Vector3.Up * 6.5f, Vector3.Up);
        }
        else // chase (interactive)
        {
            var torsoYaw = _player.TorsoYawWorld;
            var back = -MechWalker.HeadingVec(torsoYaw);
            // Centered and HIGH (owner, round 12): no sideways
            // offset — the camera sits on the torso axis, lifted
            // well above the mech so the field in front of it is
            // visible over the hull.
            var target = _player.RootPos + back * 16.0f + Vector3.Up * 23.0f;
            if (delta > 0f)
                _cam.GlobalPosition = _cam.GlobalPosition.Lerp(target, 1f - Mathf.Exp(-6f * delta));
            else
                _cam.GlobalPosition = target;
            var look = _player.RootPos + Vector3.Up * 4.5f + MechWalker.HeadingVec(torsoYaw) * 14f;
            if (_cam.GlobalPosition.DistanceSquaredTo(look) > 0.01f)
                _cam.LookAt(look, Vector3.Up);
            if (delta > 0f && _shake > 0.002f)
            {
                _cam.GlobalPosition += new Vector3(
                    (float)_rng.NextDouble() * 2f - 1f,
                    (float)_rng.NextDouble() * 1.2f - 0.6f,
                    (float)_rng.NextDouble() * 2f - 1f) * (_shake * 0.3f);
            }
        }
    }

    // Substep the walkers so their 0.1s accumulator clamp never drops
    // simulation time on slow frames (llvmpipe renders this scene at a
    // few FPS): gait, projectiles and cooldowns then share one clock at
    // any frame rate. At 60 FPS this is a single call, unchanged.
}
