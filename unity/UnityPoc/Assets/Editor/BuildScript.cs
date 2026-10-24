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
}
