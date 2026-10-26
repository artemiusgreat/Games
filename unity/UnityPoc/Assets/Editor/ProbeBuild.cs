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
// 3. Delegates to the round-1 BuildScript for the actual player build.
public static class ProbeBuild
{
    public static void BuildAll()
    {
        PrepFox();
        PrepScaleGround();
        BuildScript.BuildLinux();
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
