using System.IO;
using UnityEngine;

// Captures a real rendered frame from a camera into a PNG file.
// RenderTexture + ReadPixels: works in the standalone player under Xvfb,
// where a plain screenshot API would only grab the (uncomposited) back buffer.
public static class ShotUtil
{
    public static void Save(Camera cam, string path, int w = 800, int h = 600)
    {
        var rt = new RenderTexture(w, h, 24);
        var prevTarget = cam.targetTexture;
        var prevActive = RenderTexture.active;
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();
        cam.targetTexture = prevTarget;
        RenderTexture.active = prevActive;
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Debug.Log($"SHOT saved {path} ({w}x{h})");
        Object.Destroy(tex);
        Object.Destroy(rt);
    }

    public static string ArgValue(string name)
    {
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return null;
    }

    public static bool HasArg(string name)
    {
        foreach (var a in System.Environment.GetCommandLineArgs())
            if (a == name) return true;
        return false;
    }
}
