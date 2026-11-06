using Godot;
using System.Collections.Generic;

// RiderDuel module: capture pipeline and the proof/live camera modes.
// Split from the single-file build (round 22 cleanup); the class
// stays one partial class — see RiderDuel.cs for the field block.
public partial class RiderDuel
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
        if (_phase == 7 && tag == "rider")
        {
            GD.Print($"DUEL DONE playerSteps={_player.Steps} hectorSteps={_enemy.Steps} hectorMaxSlide={_enemy.MaxSlide:F4} twMaxSlide={_player.MaxSlide:F4}");
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
        if (_camMode == "missionBase")
        {
            var pad = Main.SpawnPads[0];
            var c = new Vector3(pad.X, Main.HeightAt(pad.X, pad.Y), pad.Y);
            _cam.GlobalPosition = c + new Vector3(46f, 17f, 46f);
            _cam.LookAt(c + new Vector3(0f, 7f, 0f), Vector3.Up);
        }
        else if (_camMode == "missionVista")
        {
            var pad = Main.SpawnPads[0];
            float gy = Main.HeightAt(pad.X, pad.Y);
            _cam.GlobalPosition = new Vector3(pad.X + 30f, gy + 60f, pad.Y + 95f);
            _cam.LookAt(new Vector3(0f, Main.HeightAt(0f, 240f) + 16f, 240f), Vector3.Up);
        }
        else if (_camMode == "enemyFront")
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
            // Rear three-quarter: player rider in frame, Hector visible past it.
            var torsoYaw = _player.TorsoYawWorld;
            var fwd = RiderWalker.HeadingVec(torsoYaw);
            var right = new Vector3(-fwd.Z, 0f, fwd.X);
            _cam.GlobalPosition = _player.RootPos - fwd * 27f + right * 8.5f + Vector3.Up * 11f;
            _cam.LookAt(_player.RootPos + Vector3.Up * 6f + fwd * 9f, Vector3.Up);
        }
        else if (_camMode == "riderSide")
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
        else if (_camMode == "brkHector")
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
            // both riders in frame, mountains behind.
            _cam.GlobalPosition = new Vector3(58f, 34f, 64f);
            _cam.LookAt(new Vector3(-6f, 0f, -6f), Vector3.Up);
        }
        else if (_camMode == "photoTuft")
        {
            // Debug: one tuft cluster at arm's length.
            _cam.GlobalPosition = new Vector3(-18.6f, Main.HeightAt(-18.6f, 2.2f) + 0.55f, 2.2f);
            _cam.LookAt(new Vector3(-21.5f, Main.HeightAt(-21.5f, -1.5f) + 0.30f, -1.5f), Vector3.Up);
        }
        else if (_camMode == "grassClose")
        {
            _cam.GlobalPosition = _grassPos + new Vector3(2.4f, 1.5f, 2.4f);
            _cam.LookAt(_grassPos + Vector3.Up * 0.25f, Vector3.Up);
        }
        else if (_camMode == "grassTop")
        {
            _cam.GlobalPosition = _grassPos + new Vector3(0.01f, 8f, 0.01f);
            _cam.LookAt(_grassPos, Vector3.Up);
        }
        else if (_camMode == "grassField")
        {
            _cam.GlobalPosition = _grassPos + new Vector3(-17f, 14f, 17f);
            _cam.LookAt(_grassPos, Vector3.Up);
        }
        else if (_camMode == "photoGround")
        {
            // Ground level by the hand-placed trees, looking across
            // the road at the Enyo.
            var tp = new Vector3(-21f, Main.HeightAt(-21f, 0f) + 2.1f, -1f);
            _cam.GlobalPosition = tp;
            _cam.LookAt(_player.RootPos + Vector3.Up * 5.5f, Vector3.Up);
        }
        else if (_camMode == "slopeSide")
        {
            var p = _player.RootPos;
            var fwd = RiderWalker.HeadingVec(_player.LegsYaw);
            var right = new Vector3(-fwd.Z, 0f, fwd.X);
            _cam.GlobalPosition = p - right * 15f + Vector3.Up * 4.2f;
            _cam.LookAt(p + Vector3.Up * 5.5f, Vector3.Up);
        }
        else if (_camMode == "groundDuel")
        {
            var p = _player.RootPos;
            var fwd = RiderWalker.HeadingVec(_player.LegsYaw);
            _cam.GlobalPosition = p - fwd * 11f + Vector3.Up * 3.4f;
            _cam.LookAt(_enemy.RootPos + Vector3.Up * 6.5f, Vector3.Up);
        }
        else // chase (interactive)
        {
            // Boom framing (round 28, owner: "the player's rider
            // should be always visible"): the camera rides a boom
            // ON the aim axis — anchored above the rider's head,
            // positioned behind the anchor along the aim direction.
            // The boom rotates rigidly with the aim pitch, so the
            // rider's framing is the same at EVERY pitch: it hangs
            // below the anchor in the lower-center of the frame,
            // head clear of the crosshair, and can never leave the
            // frame. (Round 27's fixed high perch pitched the view
            // along the mouse but left the camera in place, so
            // looking up dropped the rider out of the bottom of
            // the frame — the owner's report.) Centered, no
            // sideways offset (owner, round 12); distances scale
            // with the chassis height, as before.
            float k = _playerSpec.Height / 13f;
            var aimDir = AimDirWorld();
            // The anchor rides higher as |pitch| grows (up to
            // +0.22 heights at the +-1.35 cap): the torso pitches
            // with the aim, and at the extremes its head would
            // otherwise crowd the crosshair the boom is centered
            // on. Level framing (ramp ~0) is untouched.
            float upRamp = Mathf.Abs(aimDir.Y) / 0.9757f;
            var anchor = _player.RootPos + Vector3.Up * (_playerSpec.Height * (1.15f + 0.22f * upRamp));
            // Boom length: the full 26k while it fits. A steep
            // up-pitch swings the boom down behind the rider;
            // where the full boom would sink it into the ground,
            // shorten the boom instead of lifting the camera off
            // the axis — the camera stays ON the aim line (the
            // rider centered on it, seen from below) down to the
            // last length that keeps it above the terrain under
            // the anchor. The floor clamp below stays as a
            // backstop for sloping ground behind the rider.
            float boom = 26f * k;
            if (aimDir.Y > 0.05f)
            {
                float clearance = anchor.Y - (Main.HeightAt(anchor.X, anchor.Z) + 1.3f);
                boom = Mathf.Min(boom, Mathf.Max(8f * k, clearance / aimDir.Y));
            }
            // Occlusion pull-in: never let an obstacle behind
            // the rider hide it — backed against a building and
            // pitching up, the boom left the camera just behind
            // the roofline, and the rider vanished behind the
            // facade (owner's repeated report; reproduced by
            // --boom-check at pitch +0.35, only the pod tops
            // peeking over the roof). The axis ray alone misses
            // this: it clears the roof by a hair while the sight-
            // lines into the rider's mass cross the wall. So the
            // boom is tested twice — on the axis and 0.35 heights
            // below it, bracketing the sight-line bundle into the
            // rider — and pulls in to the nearest blocker. (A
            // ground-level third ray was tried and dropped: it
            // false-fired on terrain undulation and collapsed
            // the boom onto the rider.) The pull-in slides along
            // the same axis line, so the crosshair's line is
            // unchanged.
            var target = anchor - aimDir * boom;
            float floorY = Main.HeightAt(target.X, target.Z) + 1.3f;
            if (target.Y < floorY) target.Y = floorY;
            float blockT = boom;
            foreach (float drop in new[] { 0f, 0.35f * _playerSpec.Height })
            {
                var hit = _hit.Ray(anchor - Vector3.Up * drop, -aimDir, boom, "PLAYER");
                if (hit.Hit && hit.T < blockT) blockT = hit.T;
            }
            if (blockT < boom)
            {
                // Blocked. First try lifting the camera over the
                // obstacle: the framing survives, the whole rider
                // stays in view over the roofline, and the
                // crosshair's line is unaffected — the convergence
                // cast always starts at the real camera position.
                // (Pulling straight in was tried first: at the
                // resulting ~9u boom the rider jammed against the
                // frame's bottom edge, mostly behind the panel.)
                bool cleared = false;
                // Probes: head and chest. The feet are not a
                // probe — a roofline between the camera and the
                // rider legitimately hides the legs (the same
                // compromise as the steep-pitch envelope), while
                // head + torso in view is the rider being visible.
                var probes = new[]
                {
                    _player.RootPos + Vector3.Up * _playerSpec.Height,
                    _player.RootPos + Vector3.Up * (_playerSpec.Height * 0.5f)
                };
                for (float lift = 2f; lift <= 18f && !cleared; lift += 2f)
                {
                    var cand = target + Vector3.Up * lift;
                    cleared = true;
                    foreach (var pp in probes)
                    {
                        var to = pp - cand;
                        if (_hit.Ray(cand, to, to.Length() - 0.3f, "PLAYER").Hit) { cleared = false; break; }
                    }
                    if (cleared) target = cand;
                }
                if (!cleared)
                {
                    // No lift clears it (a tall wall at the rider's
                    // back): pull in along the axis instead.
                    boom = Mathf.Max(5f, blockT - 0.6f);
                    target = anchor - aimDir * boom;
                    float fy2 = Main.HeightAt(target.X, target.Z) + 1.3f;
                    if (target.Y < fy2) target.Y = fy2;
                }
            }
            // No position smoothing (owner, round 29): the
            // camera sits exactly on the boom target every
            // frame. The exponential glide this replaces (rate
            // 6/s) trailed every mouse flick — measured by
            // --lag-check on the old code: one frame after a
            // hard flick the camera was still 24.6u short of
            // its target and needed 62 frames (~1s) to settle,
            // so the world kept panning after the mouse had
            // stopped, which read as the (fixed) crosshair
            // still moving — jittery aim. The aim state is set
            // directly by the input events; the camera now
            // is too.
            _cam.GlobalPosition = target;
            // Center-crosshair aiming (owner's pick, round 27):
            // the camera looks along the aim direction itself
            // (AimDirWorld — the mouse's yaw/pitch), so the fixed
            // center crosshair, the rendered view and the weapons'
            // convergence ray are one and the same line at every
            // range and angle. This replaces the reticle servo of
            // rounds 25-26: with no free reticle point to chase,
            // nothing pins, saturates or inverts, and the
            // close-steep parallax dead zone measured after round
            // 26 (a top-edge target unpointable inside ~18u)
            // cannot occur. Pitch is capped at +-1.35 rad, well
            // clear of the vertical LookAt singularity.
            var lookTarget = _cam.GlobalPosition + aimDir;
            if (_cam.GlobalPosition.DistanceSquaredTo(lookTarget) > 0.0001f)
                _cam.LookAt(lookTarget, Vector3.Up);
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
