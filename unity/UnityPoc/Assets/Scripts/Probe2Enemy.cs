using UnityEngine;

// PROBE 2 (Round 2, Phase B) -- the ONE enemy definition. UNVERIFIED:
// code-prep only, never compiled or run (license gate; see NOTES.md).
//
// Prefab-equivalent, Unity-convention: there is no editor here to author
// a .prefab asset, so the definition is this component plus the static
// factory Create() below -- it builds the whole enemy from code
// (primitive capsule + collider + kinematic Rigidbody + this behaviour,
// one shared material), and Probe2Game instantiates it x3. Dropping this
// component on a capsule in the editor would behave identically.
//
// Movement: HEIGHT-FOLLOWING steering on a kinematic Rigidbody (stated
// choice). Each FixedUpdate the enemy MovePosition()s toward the player
// on the XZ plane and pins Y to WorldBuilder.HeightAt + 0.9 -- the same
// height function the terrain mesh is built from, so it can neither sink
// nor float, and it never tips, snags on MeshCollider seams, or needs a
// step offset on the procedural slopes. It stops at Standoff metres
// (horizontal) from the player. The kinematic body still collides with
// the dynamic player (an enemy that reaches the standoff ring is a real
// obstacle) and its CapsuleCollider is what the shooter's raycast hits.
[RequireComponent(typeof(Rigidbody))]
public class Probe2Enemy : MonoBehaviour
{
    public const float Standoff = 3.0f;  // metres, horizontal
    public const float Speed = 3.2f;     // m/s, slower than player walk (5.0)

    public Transform Target;             // the player, set by Probe2Game
    public bool Alive { get; private set; } = true;

    private Rigidbody _rb;
    private static Material _sharedMat;

    public static Probe2Enemy Create(Vector3 pos, string enemyName)
    {
        if (_sharedMat == null)
            _sharedMat = Probe2Util.Mat(new Color(0.90f, 0.45f, 0.10f)); // orange; round-1 NPC red stays distinct

        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule); // brings a CapsuleCollider
        go.name = enemyName;
        go.transform.position = pos;
        go.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f); // same proportions as the player capsule

        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        var enemy = go.AddComponent<Probe2Enemy>();
        enemy._rb = rb;
        return enemy;
    }

    private void Awake()
    {
        if (_rb == null) _rb = GetComponent<Rigidbody>();
    }

    private void FixedUpdate()
    {
        if (!Alive || Target == null || _rb == null) return;

        var p = _rb.position;
        var to = Target.position - p;
        to.y = 0f;
        float dist = to.magnitude;

        Vector3 next = p;
        if (dist > 0.01f)
        {
            var dir = to / dist;
            // Face the player whether advancing or holding the ring.
            _rb.MoveRotation(Quaternion.LookRotation(dir, Vector3.up));
            if (dist > Standoff)
            {
                float step = Mathf.Min(Speed * Time.fixedDeltaTime, dist - Standoff);
                next += dir * step;
            }
        }
        // Height-following: Y always from the terrain height function.
        next.y = WorldBuilder.HeightAt(next.x, next.z) + 0.9f;
        _rb.MovePosition(next);
    }

    // Horizontal distance to a point (used by the verification logging).
    public float HorizontalDistanceTo(Vector3 other)
    {
        var d = transform.position - other;
        d.y = 0f;
        return d.magnitude;
    }

    // Killed by Probe2Shooter on a confirmed raycast hit. Immediate
    // removal (the hit flash + tracer are the shooter's job).
    public void Die()
    {
        if (!Alive) return;
        Alive = false;
        Debug.Log($"PROBE2 HIT enemy={name} pos={transform.position}");
        Destroy(gameObject);
    }
}
