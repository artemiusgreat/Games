using UnityEngine;
using UnityEngine.UI;

// PROBE 2 (Round 2, Phase B) -- engine-native HUD, uGUI built entirely
// in code (no editor, no Canvas prefab in the scene). UNVERIFIED:
// code-prep only, never compiled or run (license gate; see NOTES.md).
//
// Contents: a centre crosshair (two thin bars + a dot, exactly on the
// point the shooter's ray is cast from) and a "Hits: n/3" counter in
// the top-left that Probe2Shooter updates on every kill.
// Dependency: uGUI is the built-in package com.unity.ugui 2.0.0 -- it
// ships inside the editor install and resolves offline. This project's
// hand-authored Packages/manifest.json originally had NO dependencies,
// so Probe 2 adds com.unity.ugui there (committed with this probe); a
// future editor open resolves it before compiling. If that resolution
// ever fails, THIS file is the one that will not compile -- the fix is
// the manifest entry, nothing else.
public class Probe2Hud : MonoBehaviour
{
    private Text _hitsLabel;
    private int _total = 3;

    public static Probe2Hud Create(int total)
    {
        var go = new GameObject("Probe2Hud");
        var hud = go.AddComponent<Probe2Hud>();
        hud._total = total;
        hud.Build();
        return hud;
    }

    private void Build()
    {
        // Canvas hierarchy, all code: Canvas + scaler + raycaster (the
        // raycaster is unused -- no clickable UI -- but is part of a
        // standard uGUI canvas and harmless).
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(800f, 600f);
        gameObject.AddComponent<GraphicRaycaster>();

        // --- Crosshair: centred bars + dot ---
        MakeBar(new Vector2(26f, 3f), Vector2.zero);   // horizontal
        MakeBar(new Vector2(3f, 26f), Vector2.zero);   // vertical
        MakeBar(new Vector2(5f, 5f), Vector2.zero);    // centre dot

        // --- Hits counter: top-left ---
        var labelGo = new GameObject("HitsLabel");
        labelGo.transform.SetParent(transform, false);
        _hitsLabel = labelGo.AddComponent<Text>();
        _hitsLabel.font = BuiltinFont();
        _hitsLabel.fontSize = 28;
        _hitsLabel.color = Color.white;
        _hitsLabel.alignment = TextAnchor.UpperLeft;
        var rt = _hitsLabel.rectTransform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(16f, -12f);
        rt.sizeDelta = new Vector2(320f, 44f);
        SetHits(0, _total);
    }

    private void MakeBar(Vector2 size, Vector2 offset)
    {
        var go = new GameObject("Crosshair");
        go.transform.SetParent(transform, false);
        var img = go.AddComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0.9f);
        img.raycastTarget = false;
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = offset;
        rt.sizeDelta = size;
    }

    private static Font BuiltinFont()
    {
        // Unity 6 renamed the builtin font resource Arial.ttf ->
        // LegacyRuntime.ttf; try the current name first, fall back.
        Font f = null;
        try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
        catch { /* fall through to the legacy name */ }
        if (f == null)
        {
            try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); }
            catch { /* no font: label renders nothing, counter logic unaffected */ }
        }
        return f;
    }

    public void SetHits(int hits, int total)
    {
        _total = total;
        if (_hitsLabel != null) _hitsLabel.text = $"Hits: {hits}/{total}";
    }
}
