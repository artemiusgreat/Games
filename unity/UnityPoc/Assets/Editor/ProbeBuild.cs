using UnityEditor;
using UnityEngine;

// Round-2 batch prep + build:
//   Unity -batchmode -quit -noUpm -projectPath . -executeMethod ProbeBuild.BuildAll
// 1. Fox.fbx (copied into Assets/Resources by the build driver shell step):
//    force Generic rig + animation import so its takes become AnimationClips.
// 2. ScaleGround.mat: a Standard-shader material ASSET. Referencing Standard
//    from an asset in Resources is what keeps the shader (and its shadow
//    variants) in the player build -- pinning it as always-included would
//    compile ~16k variants (round-1 finding).
// 3. Probe 2: verifies the uGUI package Probe2Hud needs is resolved
//    (com.unity.ugui 2.0.0, a built-in package -- it is committed in
//    Packages/manifest.json; this only reports, it cannot fix a failed
//    resolution, and script compilation would already have failed).
// 4. Delegates to the round-1 BuildScript for the actual player build.
public static class ProbeBuild
{
    public static void BuildAll()
    {
        PrepFox();
        PrepScaleGround();
        PrepProbe2();
        BuildScript.BuildLinux();
    }

    private static void PrepProbe2()
    {
        // UNVERIFIED (Probe 2 code-prep, license gate -- see NOTES.md):
        // Probe2Hud.cs uses UnityEngine.UI (uGUI). If the assembly is
        // absent, compilation fails before this method can run, so this
        // is a belt-and-braces confirmation for the build log.
        var ui = System.Type.GetType("UnityEngine.UI.CanvasScaler, UnityEngine.UI");
        if (ui == null)
            Debug.LogError("ProbeBuild: UnityEngine.UI (com.unity.ugui) NOT resolved -- " +
                           "check Packages/manifest.json carries com.unity.ugui 2.0.0 (built-in, resolves offline).");
        else
            Debug.Log("ProbeBuild: Probe2 OK -- UnityEngine.UI resolved (uGUI present).");
    }

    private static void PrepFox()
    {
        const string path = "Assets/Resources/Fox.fbx";
        var imp = AssetImporter.GetAtPath(path) as ModelImporter;
        if (imp == null) { Debug.LogError($"ProbeBuild: no ModelImporter at {path}"); return; }
        imp.animationType = ModelImporterAnimationType.Generic;
        imp.importAnimation = true;
        imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        var clips = imp.defaultClipAnimations;
        foreach (var c in clips) c.loopTime = true;
        imp.clipAnimations = clips;
        imp.SaveAndReimport();
        Debug.Log($"ProbeBuild: Fox importer set (Generic, clips={clips.Length})");
    }

    private static void PrepScaleGround()
    {
        const string path = "Assets/Resources/ScaleGround.mat";
        if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;
        var mat = new Material(Shader.Find("Standard"));
        mat.name = "ScaleGround";
        mat.color = Color.white;
        mat.SetFloat("_Roughness", 0.95f);
        mat.SetFloat("_Metallic", 0f);
        AssetDatabase.CreateAsset(mat, path);
        AssetDatabase.SaveAssets();
        Debug.Log("ProbeBuild: created ScaleGround.mat (Standard)");
    }
}
