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
    // Torso aim (owner's model): the aim ray leaves the chest along
    // the TORSO's facing — mouse twist and pitch steer it — and the
    // weapons converge on whatever it lands on (max 200u). The reticle
    // is drawn at this point's screen projection (UpdateReticle), so
    // the cursor target visibly rides the torso instead of sitting on
    // the camera axis, which crossed the player's own back.
    private Vector3 _aimPoint;
    private Vector3 _aimFrom, _aimDir;
    private Vector3 TorsoAimPoint()
    {
        float yaw = _player.TorsoYawWorld;
        // Aim direction: the wide mouse pitch when the player has
        // taken it past the torso clamp, else the torso's own pitch
        // (every harness phase drives TorsoPitch directly).
        float p = float.IsNaN(_player.AimPitchWide) ? _player.TorsoPitch : _player.AimPitchWide;
        var h = RiderWalker.HeadingVec(yaw);
        var dir = new Vector3(h.X * Mathf.Cos(p), Mathf.Sin(p), h.Z * Mathf.Cos(p));
        var from = _player.BoneWorldPos("torso") + dir * 3.2f;
        _aimFrom = from;
        _aimDir = dir;
        _reticlePoint = from + dir * AimDepth;
        // Round 12: converge on the point UNDER THE CURSOR — a ray
        // from the CAMERA through the reticle. The high camera broke
        // the chest-ray convergence: the view line and the torso
        // line now diverge steeply, so shots converging on the chest
        // ray's hit landed visibly BELOW the cursor (reticle on the
        // Hector's chest, beam in its legs — the owner's screenshot).
        // The camera ray defines what the player is pointing at, at
        // any camera height: its first hit within 60u of the player
        // is where every weapon goes. Live play always runs the
        // chase camera; harness proof poses keep the chest logic.
        if (_camMode == "chase")
        {
            var camPos = _cam.GlobalPosition;
            var viewHit = _hit.Ray(camPos, (_reticlePoint - camPos).Normalized(), 220f, "PLAYER");
            // RIDER and PROP hits converge at ANY range (owner,
            // round 13): a 60u cap from the chest-ray era silently
            // rejected the cursor's hit on far targets and the shot
            // fell back to the free point — in the sky. TERRAIN
            // converges within 60u outright (the ground at your
            // feet, a near slope). Beyond 60u the hillside is a real
            // target too (owner: shooting at the hill shot the sky)
            // — but only when the chest's own line to that point is
            // clear of riders and props. If a rider stands in front of
            // the backdrop, the hillside stays backdrop: converging
            // on it would drag the muzzle's line down through the
            // rider (the AIM sweep's raised-cursor case), so the shot
            // follows the aim line over him instead.
            if (viewHit.Hit)
            {
                bool farTerrain = viewHit.Target == "TERRAIN" && viewHit.Point.DistanceTo(_player.RootPos) > 60f;
                if (!farTerrain)
                {
                    _aimTarget = viewHit.Target;
                    return viewHit.Point;
                }
                var toPt = viewHit.Point - from;
                var block = _hit.Ray(from, toPt.Normalized(), toPt.Length(), "PLAYER");
                if (!block.Hit || block.Target == "TERRAIN")
                {
                    _aimTarget = "TERRAIN";
                    return viewHit.Point;
                }
                // Blocked by a rider/prop: backdrop — fall through.
            }
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
        var pt = _reticlePoint;
        var vp = GetViewport().GetVisibleRect().Size;
        var sp = _cam.UnprojectPosition(pt);
        if (_cam.IsPositionBehind(pt)) sp = vp - sp;
        _cross.ReticlePos = new Vector2(
            Mathf.Clamp(sp.X, 28f, vp.X - 28f),
            Mathf.Clamp(sp.Y, 28f, vp.Y - 28f));
    }

}
