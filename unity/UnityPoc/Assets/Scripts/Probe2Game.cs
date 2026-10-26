using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// PROBE 2 (Round 2, Phase B): gameplay loop on REAL physics, on the
// round-1 world. Gated by player args:
//   --probe2                    interactive: physics player, mouse aim,
//                               click to shoot, enemies hunt you.
//   --probe2 --probe2-verify    scripted verification (below), then quit.
//   --shotdir <dir>             where the verification saves its frames.
//
// *** UNVERIFIED *** This whole probe is code-prep only: the Unity
// license on this VM no longer validates (machine-binding invalidated
// by a VM replacement), so NONE of it has been compiled or run. It is
// written to be executed unchanged by the first licensed run:
//   ProbeBuild.BuildAll  (batch editor script; also preps probes 1+3)
//   ./Build/UnityPoc --probe2 --probe2-verify --shotdir <dir>
// Expected log prefix throughout: PROBE2.
//
// TERRAIN COLLIDER APPROACH (stated choice): a MeshCollider on the
// round-1 terrain's OWN Mesh -- the same vertices WorldBuilder generated
// from WorldBuilder.HeightAt. Chosen over TerrainCollider because Unity
// Terrain needs a TerrainData heightmap: a second, resampled
// representation of the height function that can drift from the visible
// mesh (and from HeightAt, which the enemies height-follow). The mesh is
// only 65x65 verts / 8,192 tris, static and non-convex -- MeshCollider
// handles that exactly and cheaply, and what you see is bit-for-bit what
// you collide with. The buildings need nothing added: round 1 built
// them with GameObject.CreatePrimitive(Cube), which already carries a
// BoxCollider; this probe collects and names them (see RetrofitWorld).
public class Probe2Game : MonoBehaviour
{
    public static bool VerificationMode { get; private set; }

    private Probe2Player _player;
    private Probe2Shooter _shooter;
    private Probe2Hud _hud;
    private readonly List<Probe2Enemy> _enemies = new List<Probe2Enemy>();
    private readonly List<Collider> _buildings = new List<Collider>();
    private string _shotDir = ".";
    private bool _failed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (ShotUtil.HasArg("--probe2"))
            new GameObject("Probe2Game").AddComponent<Probe2Game>();
    }

    private IEnumerator Start()
    {
        VerificationMode = ShotUtil.HasArg("--probe2-verify");
        _shotDir = ShotUtil.ArgValue("--shotdir") ?? ".";

        // Let WorldBuilder build the round-1 world (Phase A pattern).
        float waited = 0f;
        while (!Probe2Util.WorldReady() && waited < 10f)
        {
            yield return null;
            waited += Time.deltaTime;
        }
        if (!Probe2Util.WorldReady())
        {
            Debug.LogError("PROBE2 FAIL: round-1 world never appeared");
            Application.Quit(2);
            yield break;
        }
        yield return new WaitForSeconds(0.2f);

        RetrofitWorld();
        SetupPlayer();
        SpawnEnemies();

        _hud = Probe2Hud.Create(_enemies.Count);
        _shooter.Hud = _hud;
        _shooter.Enemies = _enemies;
        Debug.Log($"PROBE2 ready: buildings={_buildings.Count} enemies={_enemies.Count} " +
                  $"verify={VerificationMode} playerPos={_player.transform.position}");

        if (VerificationMode)
            StartCoroutine(VerifyRoutine());
        // Interactive mode: nothing else to drive -- Probe2Player reads
        // the keyboard, Probe2Shooter fires on click, enemies hunt.
    }

    // Adds the physics the round-1 slice deliberately lacked.
    private void RetrofitWorld()
    {
        // Terrain: MeshCollider on the exact procedural mesh.
        var terrain = GameObject.Find("Terrain");
        var mc = terrain.AddComponent<MeshCollider>();
        mc.sharedMesh = terrain.GetComponent<MeshFilter>().sharedMesh;
        mc.convex = false; // concave heightfield; static body, exact surface
        Debug.Log($"PROBE2 terrain collider: MeshCollider tris={mc.sharedMesh.triangles.Length / 3}");

        // Buildings: round 1's only BoxColliders are the two
        // CreatePrimitive cubes (trees = capsule/sphere colliders, NPC /
        // player = capsule colliders, terrain = the MeshCollider above).
        foreach (var bc in FindObjectsByType<BoxCollider>(FindObjectsSortMode.None))
        {
            bc.gameObject.name = $"Building{_buildings.Count}";
            _buildings.Add(bc);
            Debug.Log($"PROBE2 building[{_buildings.Count - 1}] pos={bc.transform.position} size={bc.size}");
        }
        if (_buildings.Count != 2)
            Debug.LogWarning($"PROBE2 expected 2 buildings, found {_buildings.Count} (continuing)");

        // Round-1 NPC: static decoration; leave it (and its collider) be.
    }

    private void SetupPlayer()
    {
        var go = GameObject.Find("Player");

        // Round-1 kinematic controller OFF (kept, not destroyed).
        var old = go.GetComponent<PlayerController>();
        if (old != null) old.enabled = false;

        _player = go.AddComponent<Probe2Player>();
        _player.ConfigurePhysics();

        // Drop test: start 1.5 m above the ground so gravity + contact
        // have something to prove (verification phase 0 watches it land).
        var p = go.transform.position;
        p.y = WorldBuilder.HeightAt(p.x, p.z) + 0.9f + 1.5f;
        go.transform.position = p;

        // Camera: this probe's shooter owns it (aim-aware follow).
        var rig = FindFirstObjectByType<CameraRig>();
        if (rig != null) rig.enabled = false;
        _shooter = gameObject.AddComponent<Probe2Shooter>();
        _shooter.Cam = Camera.main;
        _shooter.Player = _player;

        if (VerificationMode) _player.Scripted = true;
    }

    private void SpawnEnemies()
    {
        // ONE definition (Probe2Enemy.Create), x3 at spread positions
        // around the player's start (0, 8): different approach bearings,
        // so the verification's aim actually has to rotate.
        var spawns = new[]
        {
            new Vector2(14f, -12f),
            new Vector2(-18f, -6f),
            new Vector2(8f, 26f),
        };
        for (int i = 0; i < spawns.Length; i++)
        {
            float x = spawns[i].x, z = spawns[i].y;
            var e = Probe2Enemy.Create(
                new Vector3(x, WorldBuilder.HeightAt(x, z) + 0.9f, z), $"Enemy{i}");
            e.Target = _player.transform;
            _enemies.Add(e);
            Debug.Log($"PROBE2 enemy[{i}] spawn={e.transform.position} " +
                      $"distToPlayer={e.HorizontalDistanceTo(_player.transform.position):F2}");
        }
    }

    // ----------------------------------------------------------------
    // SCRIPTED VERIFICATION -- UNVERIFIED, written to run unchanged on
    // the first licensed build. Mirrors the Phase A harness pattern
    // (arg-gated driver + ShotUtil frame captures + Debug.Log verdicts
    // + Application.Quit codes: 0 = PASS, 2 = FAIL).
    //   Phase 0: gravity settle -> ground contact must become true and
    //            the capsule must rest at HeightAt + ~0.9.
    //   Phase 1: building collision -> walk into the nearest building
    //            for 2.5 s; the player must end OUTSIDE its collider.
    //   Phase 2: combat -> walk forward while enemies close to their
    //            standoff, then acquire + shoot them one by one via
    //            AimAtNearestAndFire; final counter must read 3/3.
    //   Frame captures: verify_settled.png, verify_combat.png,
    //   verify_final.png (same ShotUtil harness as probes 1 and 3).
    // ----------------------------------------------------------------
    private IEnumerator VerifyRoutine()
    {
        // ---- Phase 0: gravity + ground contact ----
        float t = 0f;
        while (!_player.IsGrounded && t < 5f)
        {
            yield return null;
            t += Time.deltaTime;
        }
        var pp = _player.transform.position;
        float restErr = Mathf.Abs(pp.y - (WorldBuilder.HeightAt(pp.x, pp.z) + 0.9f));
        Debug.Log($"PROBE2 VERIFY phase0 grounded={_player.IsGrounded} pos={pp} restErr={restErr:F3}");
        if (!_player.IsGrounded || restErr > 0.25f) { Fail("phase0: no clean ground contact"); yield break; }
        ShotUtil.Save(_shooter.Cam, $"{_shotDir}/verify_settled.png");

        // ---- Phase 1: building collision ----
        if (_buildings.Count > 0)
        {
            var b = NearestBuilding();
            _player.ScriptYaw = Probe2Player.YawToward(pp, b.transform.position);
            _player.ScriptMove = new Vector2(0f, 1f);
            Debug.Log($"PROBE2 VERIFY phase1 walking into {b.name} at {b.transform.position} for 2.5s");
            float w = 0f;
            while (w < 2.5f)
            {
                yield return null;
                w += Time.deltaTime;
                if (Mathf.RoundToInt(w * 2f) != Mathf.RoundToInt((w - Time.deltaTime) * 2f))
                {
                    var q = _player.transform.position;
                    Debug.Log($"PROBE2 VERIFY phase1 t={w:F1} pos={q} grounded={_player.IsGrounded} " +
                              $"insideBuilding={b.bounds.Contains(q)}");
                }
            }
            _player.ScriptMove = Vector2.zero;
            var end = _player.transform.position;
            // Capsule centre must stay outside the box (0.1 m slack for
            // the solver's contact skin): walked INTO it, never through.
            bool inside = b.bounds.Contains(end);
            Debug.Log($"PROBE2 VERIFY phase1 end={end} insideBuilding={inside} grounded={_player.IsGrounded}");
            if (inside) { Fail("phase1: player ended inside a building"); yield break; }
        }

        // ---- Phase 2: walk forward, enemies approach, shoot x3 ----
        _player.ScriptYaw = 0f; // face +Z, away from the buildings
        _player.ScriptMove = new Vector2(0f, 1f);
        Debug.Log("PROBE2 VERIFY phase2 walking forward 2s, enemies closing");
        float f = 0f;
        while (f < 2f)
        {
            yield return null;
            f += Time.deltaTime;
        }
        _player.ScriptMove = Vector2.zero;

        int kills = 0;
        float combatDeadline = Time.time + 45f;
        bool shotTaken = false;
        while (kills < _enemies.Count && Time.time < combatDeadline && !_failed)
        {
            var enemy = _shooter.NearestLivingEnemy();
            if (enemy == null) break;
            float dist = enemy.HorizontalDistanceTo(_player.transform.position);
            Debug.Log($"PROBE2 VERIFY phase2 target={enemy.name} dist={dist:F2} grounded={_player.IsGrounded}");
            if (dist > Probe2Enemy.Standoff + 0.6f)
            {
                yield return new WaitForSeconds(0.5f); // still closing in
                continue;
            }
            // In range: aim for a few frames (player yaw + camera
            // settle through physics + LateUpdate), then fire.
            for (int i = 0; i < 4; i++)
            {
                _shooter.AimAt(enemy);
                yield return null;
            }
            var killed = _shooter.AimAtNearestAndFire();
            shotTaken = true;
            if (killed != null)
            {
                kills++;
                Debug.Log($"PROBE2 VERIFY phase2 kill {kills}/{_enemies.Count} counter=Hits: {_shooter.Hits}/{_shooter.Total}");
                if (kills == 1)
                    ShotUtil.Save(_shooter.Cam, $"{_shotDir}/verify_combat.png");
            }
            else
            {
                Debug.LogWarning($"PROBE2 VERIFY phase2 shot did not kill (hit={_shooter.LastShotHitName}); retrying");
            }
            yield return new WaitForSeconds(0.4f);
        }
        _shooter.ClearAim();

        ShotUtil.Save(_shooter.Cam, $"{_shotDir}/verify_final.png");
        if (!shotTaken || kills < _enemies.Count || _shooter.Hits != _enemies.Count)
        {
            Fail($"phase2: kills={kills} hits={_shooter.Hits}/{_shooter.Total}");
            yield break;
        }
        Debug.Log($"PROBE2 VERIFY PASS hits={_shooter.Hits}/{_shooter.Total} finalCounter=\"Hits: {_shooter.Hits}/{_shooter.Total}\"");
        Application.Quit(0);
    }

    private Collider NearestBuilding()
    {
        Collider best = null;
        float bestDist = float.MaxValue;
        foreach (var b in _buildings)
        {
            float d = Vector3.Distance(_player.transform.position, b.transform.position);
            if (d < bestDist) { bestDist = d; best = b; }
        }
        return best;
    }

    private void Fail(string why)
    {
        _failed = true;
        Debug.LogError($"PROBE2 VERIFY FAIL: {why}");
        Application.Quit(2);
    }
}
