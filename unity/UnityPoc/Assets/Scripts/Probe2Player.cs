using UnityEngine;

// PROBE 2 (Round 2, Phase B) -- physics-driven player. UNVERIFIED:
// code-prep only, never compiled or run (license gate; see NOTES.md).
//
// Replaces round 1's height-sampling PlayerController FOR THIS PROBE
// ONLY (Probe2Game disables that component; deleting --probe2 from the
// command line restores round 1 untouched). This component drives the
// round-1 player capsule with a real Rigidbody:
//   * Gravity: Rigidbody.useGravity (project gravity -9.81). The body is
//     dropped 1.5 m above the terrain at setup and SETTLES onto the
//     MeshCollider -- verification phase 0 logs that settle.
//   * Ground contact: IsGrounded is rebuilt every FixedUpdate from
//     OnCollisionStay contact normals (any contact with normal.y > 0.65),
//     so it is true on terrain AND on building roofs, false in the air.
//   * No falling through terrain: the terrain carries a MeshCollider on
//     the exact procedural mesh (Probe2Game), the capsule is a dynamic
//     body with ContinuousDynamic collision detection, and speeds are
//     low (<= 9.5 m/s = < 0.2 m per physics step).
//   * No walking through buildings: horizontal velocity is set on the
//     body, never the transform, so the solver stops the capsule at the
//     buildings' BoxColliders. Only the vertical component is physics'.
// Movement input comes from the keyboard in interactive mode, or from
// ScriptMove/ScriptYaw in the scripted verification (--probe2-verify),
// which Probe2Game drives. Yaw is applied via MoveRotation with
// freezeRotation on, so the capsule never tips over.
[RequireComponent(typeof(Rigidbody))]
public class Probe2Player : MonoBehaviour
{
    public float WalkSpeed = 5.0f;   // same speeds as round 1
    public float RunSpeed = 9.5f;

    // Scripted input (verification mode). When Scripted is true the
    // keyboard/mouse are ignored and Probe2Game sets these each frame.
    public bool Scripted;
    public Vector2 ScriptMove;       // x = strafe, y = forward, -1..1
    public float ScriptYaw;          // degrees

    public bool IsGrounded { get; private set; }
    public Rigidbody Body { get; private set; }

    private float _yaw;
    private bool _groundContact;

    // Called by Probe2Game right after AddComponent, before the first
    // physics step: converts the round-1 capsule into a physics body.
    public void ConfigurePhysics()
    {
        Body = GetComponent<Rigidbody>();
        Body.useGravity = true;
        Body.freezeRotation = true; // yaw is ours; never tip
        Body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        Body.interpolation = RigidbodyInterpolation.Interpolate;

        // Zero-friction material: the capsule slides along building walls
        // instead of sticking or climbing on friction.
        var col = GetComponent<Collider>();
        if (col != null)
        {
            col.sharedMaterial = new PhysicsMaterial("Probe2PlayerMat")
            {
                dynamicFriction = 0f,
                staticFriction = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounciness = 0f,
                bounceCombine = PhysicsMaterialCombine.Minimum,
            };
        }
        _yaw = transform.eulerAngles.y;
    }

    private void Update()
    {
        if (Scripted) { _yaw = ScriptYaw; return; }
        _yaw += Input.GetAxis("Mouse X") * 3.0f; // same feel as round 1
    }

    private void FixedUpdate()
    {
        if (Body == null) return;

        Vector2 move;
        bool run;
        if (Scripted)
        {
            move = ScriptMove;
            run = false; // verification always walks: deterministic timing
        }
        else
        {
            move = new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
            run = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        }
        if (move.magnitude > 1f) move.Normalize();
        float speed = run ? RunSpeed : WalkSpeed;

        var wish = Quaternion.Euler(0f, _yaw, 0f) * new Vector3(move.x, 0f, move.y);
        var v = Body.linearVelocity;
        // Horizontal velocity is set directly (arcade control, same as
        // round 1's kinematic speed); the vertical component is left to
        // gravity and the solver -- that is what makes ground contact,
        // falling, and building collision REAL instead of sampled.
        Body.linearVelocity = new Vector3(wish.x * speed, v.y, wish.z * speed);
        Body.MoveRotation(Quaternion.Euler(0f, _yaw, 0f));

        IsGrounded = _groundContact;
        _groundContact = false; // re-proven by OnCollisionStay each step
    }

    private void OnCollisionStay(Collision collision)
    {
        for (int i = 0; i < collision.contactCount; i++)
        {
            if (collision.GetContact(i).normal.y > 0.65f)
            {
                _groundContact = true;
                return;
            }
        }
    }

    // Yaw that faces a world point from the player (used by the shooter /
    // verification to aim; degrees, Unity convention).
    public static float YawToward(Vector3 from, Vector3 to)
    {
        var d = to - from;
        return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
    }
}
