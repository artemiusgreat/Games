using UnityEngine;

// PROBE 2 (Round 2, Phase B) -- shared helpers. UNVERIFIED: written
// code-prep only; the Unity license on this VM no longer validates
// (machine-binding invalidated by a VM replacement), so none of the
// Probe 2 code has been compiled or run. See unity/NOTES.md.
public static class Probe2Util
{
    // Same shader fallback chain as WorldBuilder.Mat: "Standard" is
    // stripped from the player build when nothing references it, so
    // runtime materials resolve Standard -> Unlit/Color -> Sprites/Default.
    public static Material Mat(Color c)
    {
        var shader = Shader.Find("Standard")
                  ?? Shader.Find("Unlit/Color")
                  ?? Shader.Find("Sprites/Default");
        return new Material(shader) { color = c };
    }

    // Waits until WorldBuilder (round 1) has built its world, mirroring
    // the fixed WaitForSeconds(0.4) the Phase A probes used, but polled
    // so a slow first frame cannot race the retrofit.
    public static bool WorldReady()
    {
        return GameObject.Find("Terrain") != null
            && GameObject.Find("Player") != null
            && Camera.main != null;
    }
}
