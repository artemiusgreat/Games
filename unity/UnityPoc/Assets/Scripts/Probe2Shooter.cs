using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// PROBE 2 (Round 2, Phase B) -- aiming, shooting, and the probe's
// third-person camera. UNVERIFIED: code-prep only, never compiled or
// run (license gate; see NOTES.md).
//
// AIMING + SHOOTING: the shot is a raycast from the CAMERA CENTRE
// (Camera.ViewportPointToRay(0.5, 0.5)) -- what the HUD crosshair sits
// on is exactly what gets hit-tested. Interactive mode: left mouse
// button fires, the player aims with mouse yaw (Probe2Player). The
// scripted/automated mode (used by the verification in Probe2Game)
// calls AimAtNearestAndFire(): it yaws the player at the nearest living
// enemy and points this camera dead at the enemy's chest, then fires
// the same centre ray -- no separate cheat ray.
//
// The camera itself: Probe2Game disables round 1's CameraRig and this
// component owns the Main Camera in LateUpdate -- same follow offset as
// CameraRig by default, but when an aim target is set the camera looks
// AT the target instead of the player's chest, which is what lines the
// centre ray up with the enemy. Hits are filtered from RaycastAll
// sorted by distance, skipping the player's own colliders: the nearest
// remaining hit decides. An enemy hit increments Hits, spawns a hit
// flash + tracer, and destroys the enemy (Probe2Enemy.Die).
public class Probe2Shooter : MonoBehaviour
{
    public Camera Cam;
    public Probe2Player Player;
    public Probe2Hud Hud;
    public List<Probe2Enemy> Enemies = new List<Probe2Enemy>();

    public int Hits { get; private set; }
    public int Total => Enemies.Count;
    public Vector3? AimPoint { get; private set; }   // chest point currently aimed at, if any
    public float LastShotDistance { get; private set; } = -1f; // -1 = last shot hit nothing / no shot
    public string LastShotHitName { get; private set; } = "";

    private struct Fx { public GameObject Go; public float Until; }
    private readonly List<Fx> _fx = new List<Fx>();
    private Material _tracerMat, _flashMat;

    private void Start()
    {
        _tracerMat = Probe2Util.Mat(new Color(1f, 0.95f, 0.35f));
        _flashMat = Probe2Util.Mat(new Color(1f, 0.85f, 0.30f));
    }

    private void Update()
    {
        if (!Probe2Game.VerificationMode && Input.GetMouseButtonDown(0))
            Fire();

        // Age out tracers / hit flashes.
        float now = Time.time;
        for (int i = _fx.Count - 1; i >= 0; i--)
        {
            if (now >= _fx[i].Until)
            {
                Destroy(_fx[i].Go);
                _fx.RemoveAt(i);
            }
        }
    }

    // Third-person follow (CameraRig's offset), unless aiming.
    private void LateUpdate()
    {
        if (Cam == null || Player == null) return;
        var pt = Player.transform;
        var back = pt.rotation * Vector3.back;
        Cam.transform.position = pt.position + back * 7.5f + Vector3.up * 3.2f
                               - Vector3.up * 0.9f; // same framing as CameraRig
        Cam.transform.LookAt(AimPoint ?? (pt.position + Vector3.up * 0.7f));
    }

    public Probe2Enemy NearestLivingEnemy()
    {
        Probe2Enemy best = null;
        float bestDist = float.MaxValue;
        foreach (var e in Enemies)
        {
            if (e == null || !e.Alive) continue;
            float d = e.HorizontalDistanceTo(Player.transform.position);
            if (d < bestDist) { bestDist = d; best = e; }
        }
        return best;
    }

    // Aim the player + camera at an enemy's chest. One call per frame
    // for a few frames lets physics rotation and the camera settle
    // before Fire() (the verification does exactly that).
    public void AimAt(Probe2Enemy enemy)
    {
        if (enemy == null) { AimPoint = null; return; }
        var chest = enemy.transform.position + Vector3.up * 0.2f;
        AimPoint = chest;
        Player.ScriptYaw = Probe2Player.YawToward(Player.transform.position, chest);
    }

    public void ClearAim() => AimPoint = null;

    // Scripted/automated shot: aim at the nearest living enemy, then
    // fire the same camera-centre ray a player would. Returns the enemy
    // killed, or null (no living enemy, or the ray was blocked -- e.g.
    // by a building; the verification logs LastShotHitName either way).
    public Probe2Enemy AimAtNearestAndFire()
    {
        var enemy = NearestLivingEnemy();
        if (enemy == null) return null;
        AimAt(enemy);
        return Fire();
    }

    // Fires the camera-centre raycast. Returns the enemy destroyed, if
    // the first non-player hit was one.
    public Probe2Enemy Fire()
    {
        if (Cam == null || Player == null) return null;
        var ray = Cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        var hitsArr = Physics.RaycastAll(ray, 200f, ~0, QueryTriggerInteraction.Ignore);
        Array.Sort(hitsArr, (a, b) => a.distance.CompareTo(b.distance));

        var muzzle = Player.transform.position + Vector3.up * 0.2f;
        foreach (var hit in hitsArr)
        {
            // Never shoot ourselves: the ray starts behind the capsule.
            if (hit.collider.transform == Player.transform) continue;

            LastShotDistance = hit.distance;
            LastShotHitName = hit.collider.name;
            SpawnTracer(muzzle, hit.point);
            SpawnFlash(hit.point);

            var enemy = hit.collider.GetComponent<Probe2Enemy>();
            if (enemy != null && enemy.Alive)
            {
                enemy.Die();
                Hits++;
                if (Hud != null) Hud.SetHits(Hits, Total);
                Debug.Log($"PROBE2 FIRE hit={hit.collider.name} dist={hit.distance:F2} hits={Hits}/{Total}");
                return enemy;
            }
            Debug.Log($"PROBE2 FIRE blocked by={hit.collider.name} dist={hit.distance:F2}");
            return null; // nearest real surface wins; no piercing
        }
        LastShotDistance = -1f;
        LastShotHitName = "";
        SpawnTracer(muzzle, ray.GetPoint(60f));
        Debug.Log("PROBE2 FIRE miss (no hit within 200)");
        return null;
    }

    private void SpawnTracer(Vector3 from, Vector3 to)
    {
        var go = new GameObject("Probe2Tracer");
        var line = go.AddComponent<LineRenderer>();
        line.material = _tracerMat;
        line.positionCount = 2;
        line.SetPosition(0, from);
        line.SetPosition(1, to);
        line.startWidth = 0.03f;
        line.endWidth = 0.03f;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false; // Unity 6 name (was receiveShadow)
        _fx.Add(new Fx { Go = go, Until = Time.time + 0.09f });
    }

    private void SpawnFlash(Vector3 at)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Probe2HitFlash";
        Destroy(go.GetComponent<Collider>()); // purely visual; never blocks a ray
        go.transform.position = at;
        go.transform.localScale = Vector3.one * 0.45f;
        go.GetComponent<Renderer>().material = _flashMat;
        go.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        _fx.Add(new Fx { Go = go, Until = Time.time + 0.12f });
    }
}
