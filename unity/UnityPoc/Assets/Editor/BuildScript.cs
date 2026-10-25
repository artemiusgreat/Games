using System;
using UnityEditor;
using UnityEngine;

// CLI build for the smoke test:
//   Unity -batchmode -quit -projectPath . -executeMethod BuildScript.BuildLinux -logFile build.log
//   ./Build/UnityPoc --walk-test   (prints SMOKE start/end lines, then quits)
public static class BuildScript
{
    public static void BuildLinux()
    {
        EnsureStandardShaderIncluded();
        var options = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/Main.unity" },
            locationPathName = "Build/UnityPoc",
            target = BuildTarget.StandaloneLinux64,
            options = BuildOptions.None,
        };
        var report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            Debug.LogError($"Build failed: {report.summary.result}");
            EditorApplication.Exit(1);
        }
        Console.WriteLine($"Build OK: {report.summary.totalSize} bytes");
    }

    // Runtime code creates Materials via Shader.Find, but with no material or
    // scene in the build referencing a shader, it gets stripped and
    // Shader.Find returns null in the player. Pin the 1-variant "Unlit/Color"
    // (the runtime fallback in WorldBuilder.Mat) as always-included. Pinning
    // "Standard" instead compiles 16,384 variants (~30 min) -- not viable.
    private static void EnsureStandardShaderIncluded()
    {
        var shader = Shader.Find("Unlit/Color");
        if (shader == null) return;
        var gs = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.GraphicsSettings>(
            "ProjectSettings/GraphicsSettings.asset");
        if (gs == null) return;
        var so = new SerializedObject(gs);
        var arr = so.FindProperty("m_AlwaysIncludedShaders");
        bool present = false;
        for (int i = arr.arraySize - 1; i >= 0; i--)
        {
            var el = arr.GetArrayElementAtIndex(i).objectReferenceValue;
            if (el == shader) present = true;
            else if (el != null && el.name == "Standard") arr.DeleteArrayElementAtIndex(i); // undo the 16k-variant pin if a prior run set it
        }
        if (!present)
        {
            arr.InsertArrayElementAtIndex(arr.arraySize);
            arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = shader;
        }
        so.ApplyModifiedProperties();
        AssetDatabase.SaveAssets();
    }
}
