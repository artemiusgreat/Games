using Godot;
using System.Collections.Generic;

// Story campaign progression (owner, 2026-11-13): a new
// Story shows ONE planet and ONE mission. Completing a
// mission unlocks the next in the catalog's linear chain
// (planets in catalog order, missions in planet order), so
// finishing a planet's last mission opens the next planet.
// Progress persists between sessions in
// user://story_progress.cfg (a single integer: how many
// chain entries are unlocked).
public static class StoryProgress
{
    private const string SavePath = "user://story_progress.cfg";
    private static int _unlocked = -1; // -1 = not loaded yet

    private static List<(int Planet, int Mission)> Chain()
    {
        var list = new List<(int, int)>();
        for (int p = 0; p < MissionCatalog.Planets.Count; p++)
            for (int m = 0; m < MissionCatalog.Planets[p].Missions.Count; m++)
                list.Add((p, m));
        return list;
    }

    public static int Total => Chain().Count;

    public static int UnlockedCount
    {
        get
        {
            if (_unlocked < 0)
            {
                _unlocked = 1;
                var f = FileAccess.Open(SavePath, FileAccess.ModeFlags.Read);
                if (f != null)
                {
                    if (int.TryParse(f.GetAsText().Trim(), out int v))
                        _unlocked = Mathf.Clamp(v, 1, Total);
                    f.Close();
                }
            }
            return _unlocked;
        }
    }

    private static void Save()
    {
        var f = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
        if (f != null)
        {
            f.StoreString(UnlockedCount.ToString());
            f.Close();
        }
    }

    public static bool IsUnlocked(int planet, int mission)
    {
        var chain = Chain();
        int i = chain.FindIndex(e => e.Planet == planet && e.Mission == mission);
        return i >= 0 && i < UnlockedCount;
    }

    // Mark a mission complete: unlocks everything up to the
    // NEXT chain entry. Replays of older missions never
    // shrink the unlocked set.
    public static void Complete(int planet, int mission)
    {
        var chain = Chain();
        int i = chain.FindIndex(e => e.Planet == planet && e.Mission == mission);
        if (i < 0) return;
        int want = Mathf.Min(i + 2, chain.Count);
        if (want > UnlockedCount)
        {
            _unlocked = want;
            Save();
            GD.Print($"STORY progress: completed planet {planet} mission {mission}; unlocked {want}/{chain.Count}");
        }
    }

    public static void Reset()
    {
        _unlocked = 1;
        Save();
        GD.Print("STORY progress reset: one planet, one mission");
    }
}
