using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// PROBE 1 (Round 2): skinned Fox (Khronos glTF sample, converted to FBX --
// Unity has no native glTF importer), animated purely from code via the
// Playables API. No Animator Controller asset exists in this project.
// Gated by the player arg:  --probe1 --shotdir <dir>
// Timeline: idle (Survey) -> walk (Walk) while auto-moving forward on the
// round-1 terrain -> captured frames idle.png / walk_a.png / walk_b.png.
public class FoxDriver : MonoBehaviour
{
    private Animator _animator;
    private PlayableGraph _graph;
    private AnimationClipPlayable _current;
    private AnimationClip _curClip;
    private AnimationClip _idleClip, _walkClip;
    private GameObject _fox;
    private Camera _cam;
    private float _t;
    private bool _moving;
    private bool _shotIdle, _shotWalkA, _shotWalkB;
    private string _shotDir = ".";
    private const float WalkSpeed = 1.7f;
    private const float StartX = -4.5f, StartZ = 10f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (ShotUtil.HasArg("--probe1"))
            new GameObject("FoxDriver").AddComponent<FoxDriver>();
    }

    private IEnumerator Start()
    {
        _shotDir = ShotUtil.ArgValue("--shotdir") ?? ".";
        yield return new WaitForSeconds(0.4f); // let WorldBuilder build round-1 world

        // Load the FBX from Resources: prefab + its AnimationClip sub-assets.
        GameObject prefab = null;
        var clips = new Dictionary<string, AnimationClip>();
        foreach (var o in Resources.LoadAll<Object>("Fox"))
        {
            if (o is GameObject g) prefab = g;
            else if (o is AnimationClip c && !c.name.StartsWith("__preview"))
            {
                clips[c.name] = c;
                Debug.Log($"PROBE1 clip: '{c.name}' length={c.length:F2}s loop={c.isLooping}");
            }
        }
        if (prefab == null) { Debug.LogError("PROBE1 FAIL: Fox prefab not in Resources"); Application.Quit(2); yield break; }
        _idleClip = Find(clips, "Survey"); _walkClip = Find(clips, "Walk");
        if (_idleClip == null || _walkClip == null) { Debug.LogError("PROBE1 FAIL: Survey/Walk clips missing"); Application.Quit(2); yield break; }
        AnimationClip idle = _idleClip, walk = _walkClip;

        _fox = Instantiate(prefab);
        _fox.name = "Fox";
        // Auto-scale to ~1.5 world units tall from the renderers' bounds.
        var b = BoundsOf(_fox);
        float s = 1.5f / Mathf.Max(0.01f, b.size.y);
        _fox.transform.localScale = Vector3.one * s;
        _fox.transform.rotation = Quaternion.Euler(0, 90, 0); // model faces +Z; face +X (walk dir)
        _fox.transform.position = new Vector3(StartX, WorldBuilder.HeightAt(StartX, StartZ), StartZ);
        Debug.Log($"PROBE1 fox bounds={b.size} scale={s:F3} clips idle={idle.name} walk={walk.name}");

        _animator = _fox.GetComponentInChildren<Animator>() ?? _fox.AddComponent<Animator>();
        _animator.applyRootMotion = false;

        // Round-1 camera rig + capsule player would fight us: park them.
        var rig = FindFirstObjectByType<CameraRig>();
        if (rig != null) rig.enabled = false;
        var player = GameObject.Find("Player");
        if (player != null) player.SetActive(false);
        _cam = Camera.main ?? FindFirstObjectByType<Camera>();
        // Fixed side-on camera: same framing for idle and walk shots, so the
        // pixel diff isolates the character's pose change.
        _cam.transform.position = new Vector3(StartX + 2.5f, WorldBuilder.HeightAt(StartX, StartZ) + 1.6f, StartZ - 7.5f);
        _cam.transform.LookAt(new Vector3(StartX + 2.5f, WorldBuilder.HeightAt(StartX, StartZ) + 0.8f, StartZ));

        Play(idle);
        Debug.Log("PROBE1 idle (Survey) playing");
    }

    private static AnimationClip Find(Dictionary<string, AnimationClip> clips, string key)
    {
        foreach (var kv in clips)
            if (kv.Key.IndexOf(key, System.StringComparison.OrdinalIgnoreCase) >= 0) return kv.Value;
        return null;
    }

    private static Bounds BoundsOf(GameObject root)
    {
        var rs = root.GetComponentsInChildren<Renderer>();
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    // Code-only animation switching: rebuild a 1-clip PlayableGraph per state.
    private void Play(AnimationClip clip)
    {
        if (_graph.IsValid()) _graph.Destroy();
        _curClip = clip;
        _graph = PlayableGraph.Create("FoxGraph");
        _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        var output = AnimationPlayableOutput.Create(_graph, "Anim", _animator);
        _current = AnimationClipPlayable.Create(_graph, clip);
        _current.SetApplyFootIK(false);
        output.SetSourcePlayable(_current);
        _graph.Play();
    }

    private void Update()
    {
        if (_fox == null || _cam == null) return;
        _t += Time.deltaTime;

        // Manual loop guard (imported takes are not guaranteed loopTime).
        if (_current.IsValid() && _curClip != null && _current.GetTime() > _curClip.length)
            _current.SetTime(0);

        if (!_moving && _t >= 1.8f)
        {
            Play(_walkClip);
            _moving = true;
            Debug.Log("PROBE1 switched to Walk, moving");
        }
        if (_moving)
        {
            var p = _fox.transform.position;
            p.x += WalkSpeed * Time.deltaTime;
            p.y = WorldBuilder.HeightAt(p.x, p.z);
            _fox.transform.position = p;
        }

        if (!_shotIdle && _t >= 1.5f) { ShotUtil.Save(_cam, $"{_shotDir}/idle.png"); _shotIdle = true; }
        if (!_shotWalkA && _t >= 2.45f) { ShotUtil.Save(_cam, $"{_shotDir}/walk_a.png"); _shotWalkA = true; }
        if (!_shotWalkB && _t >= 2.85f) { ShotUtil.Save(_cam, $"{_shotDir}/walk_b.png"); _shotWalkB = true; }
        if (_t >= 3.6f)
        {
            Debug.Log($"PROBE1 DONE foxPos={_fox.transform.position}");
            Application.Quit(0);
        }
    }

}
