using Godot;
using System.Collections.Generic;

// RiderDuel module: AIMING — the torso aim ray, the camera-ray
// convergence that defines the aim point every weapon uses,
// and the reticle that rides that point on screen. Part of
// the Combat folder: everything that aims or shoots lives
// here (firing itself is in Combat/RiderDuel.Weapons.cs).
// The class stays one partial class — see RiderDuel.cs.
public partial class RiderDuel
{
    // Aim (owner's model, round 27 — center crosshair, his pick
    // over the free reticle point): the crosshair is fixed at the
    // screen center and the CAMERA's axis is the aim. The mouse
    // steers yaw/pitch (AimDirWorld); the chase camera looks along
    // exactly that direction, and this method casts that same axis
    // from the camera — the weapons converge on its first hit, at
    // any range or angle. The torso still twists/pitches after the
    // mouse to its mechanical stops, as a visual. This retires the
    // free 40u reticle point (rounds 9-26): its projection against
    // the high chase camera left a parallax dead zone close-in and
    // steep (measured round 26 follow-up: a top-edge target could
    // not be pointed at inside ~18u), which a center crosshair
    // cannot have by construction.
    private Vector3 _aimPoint;
    private Vector3 _aimFrom, _aimDir;
    // The one aim direction: the mouse's world yaw and the
    // effective pitch (the wide mouse pitch when the player has
    // taken it past the torso clamp, else the torso's own pitch —
    // every harness phase drives TorsoPitch directly). The chase
    // camera, the convergence cast and the laser's truth ray all
    // use this, so they can never disagree about where "aim" is.
    private Vector3 AimDirWorld()
    {
        float yaw = _player.TorsoYawWorld;
        float p = float.IsNaN(_player.AimPitchWide) ? _player.TorsoPitch : _player.AimPitchWide;
        var h = RiderWalker.HeadingVec(yaw);
        return new Vector3(h.X * Mathf.Cos(p), Mathf.Sin(p), h.Z * Mathf.Cos(p));
    }

    private Vector3 TorsoAimPoint()
    {
        var dir = AimDirWorld();
        var from = _player.BoneWorldPos("torso") + dir * 3.2f;
        _aimFrom = from;
        _aimDir = dir;
        _reticlePoint = from + dir * AimDepth;
        // Round 27: converge on the point UNDER THE CROSSHAIR —
        // a ray from the CAMERA along the aim axis (the direction
        // the chase camera is looking). Its first hit of ANY kind
        // is where every weapon goes: what the center crosshair
        // rests on is what gets hit, at any range; a clean sky
        // shot converges on the far point of the axis itself.
        // Live play always runs the chase camera; harness proof
        // poses keep the chest logic below.
        if (_camMode == "chase")
        {
            var camPos = _cam.GlobalPosition;
            var viewHit = _hit.Ray(camPos, dir, 220f, "PLAYER");
            _aimTarget = viewHit.Hit ? viewHit.Target : "";
            _reticlePoint = viewHit.Hit ? viewHit.Point : camPos + dir * 220f;
            return _reticlePoint;
        }
        // Fallback (round 11): the chest ray's RIDER/PROP hit within
        // weapon reach, else the free 40u point.
        var hit = _hit.Ray(from, dir, 200f, "PLAYER");
        _aimTarget = hit.Hit ? hit.Target : "";
        if (hit.Hit && hit.T <= 140f && (hit.Target == "ENEMY" || hit.Target == "FOE1" || hit.Target == "PROP"))
            return hit.Point;
        return _reticlePoint;
    }

    // The reticle rides a fixed-depth point on the aim ray — AND
    // (round 9) the weapons converge on that same point, which is
    // what makes both of the owner's demands true at once:
    // round 6: the target must move FREELY (projecting the ray's
    // hit point made it stick to surfaces and jump between boxes —
    // round 8's model, and he reported it as grid-snapped within
    // the day). Round 8: shots must land where the cursor points
    // (a free cursor the weapons ignored was a lie — cannons hit
    // under a high cursor because the chest ray still crossed the
    // enemy). Now the cursor IS the convergence point: every
    // weapon's line is drawn to it, and the first thing on that
    // line is what gets hit. Cursor above him = the lines pass
    // above him. When the torso points behind the camera (deep
    // twist), the projection mirrors — flip through the center and
    // clamp to the screen edge.
    private string _aimTarget = "";
    private Vector3 _reticlePoint;
    private const float AimDepth = 40f;
    private void UpdateReticle()
    {
        // Center crosshair (round 27): the reticle is the viewport
        // center, always — the camera axis is the aim, so there is
        // no world point left to project. (_reticlePoint still
        // tracks the convergence point for the weapon code.)
        var vp = GetViewport().GetVisibleRect().Size;
        _cross.ReticlePos = vp * 0.5f;
    }

}
