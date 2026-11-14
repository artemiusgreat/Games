using Godot;

// Story campaign progression (owner, 2026-11-13, revised
// the same day to NON-LINEAR): all three planets are visible
// from the start, but each planet shows only its first
// mission. Completing a mission unlocks the next mission ON
// THAT PLANET — the planets progress independently, in any
// order the player chooses. Progress persists between
// sessions in user://story_progress.cfg (one unlocked-count
// per planet, comma-separated).
public static class StoryProgress
{
    private const string SavePath = "user://story_progress.cfg";
    private static int[] _counts; // per planet; null = not loaded

    private static int PlanetCount => MissionCatalog.Planets.Count;

    private static int[] Counts
    {
        get
        {
            if (_counts != null) return _counts;
            _counts = new int[PlanetCount];
            for (int p = 0; p < PlanetCount; p++) _counts[p] = 1;
            var f = FileAccess.Open(SavePath, FileAccess.ModeFlags.Read);
            if (f != null)
            {
                string text = f.GetAsText().Trim();
                f.Close();
                var parts = text.Split(',');
                if (parts.Length == PlanetCount)
                {
                    for (int p = 0; p < PlanetCount; p++)
                        if (int.TryParse(parts[p], out int v))
                            _counts[p] = Mathf.Clamp(v, 1, MissionCatalog.Planets[p].Missions.Count);
                }
                else if (int.TryParse(text, out int legacy))
                {
                    // Migration from the first build's single
                    // linear-chain count: unfold it back into
                    // per-planet counts so no progress is lost.
                    int left = legacy;
                    for (int p = 0; p < PlanetCount && left > 0; p++)
                    {
                        int take = Mathf.Min(left, MissionCatalog.Planets[p].Missions.Count);
                        _counts[p] = Mathf.Max(1, take);
                        left -= take;
                    }
                }
            }
            return _counts;
        }
    }

    private static void Save()
    {
        var f = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
        if (f != null)
        {
            f.StoreString(string.Join(",", Counts));
            f.Close();
        }
    }

    public static int UnlockedIn(int planet)
    {
        if (planet < 0 || planet >= PlanetCount) return 0;
        return Counts[planet];
    }

    public static bool IsUnlocked(int planet, int mission)
        => planet >= 0 && planet < PlanetCount
           && mission >= 0 && mission < Counts[planet];

    // The unlocked mission indices of a planet, in catalog
    // order (the front-end's selectors, offer lists and bar
    // patrons all cycle exactly this set).
    public static System.Collections.Generic.List<int> UnlockedList(int planet)
    {
        var list = new System.Collections.Generic.List<int>();
        if (planet < 0 || planet >= PlanetCount) return list;
        for (int m = 0; m < MissionCatalog.Planets[planet].Missions.Count; m++)
            if (IsUnlocked(planet, m)) list.Add(m);
        return list;
    }

    public static int FirstUnlocked(int planet)
    {
        var u = UnlockedList(planet);
        return u.Count > 0 ? u[0] : 0;
    }

    // Mark a mission complete: unlocks the NEXT mission on
    // the same planet. Replays never shrink the unlocked set,
    // and other planets are untouched.
    public static void Complete(int planet, int mission)
    {
        if (planet < 0 || planet >= PlanetCount) return;
        int cap = MissionCatalog.Planets[planet].Missions.Count;
        int want = Mathf.Min(mission + 2, cap);
        if (want > Counts[planet])
        {
            Counts[planet] = want;
            Save();
            GD.Print($"STORY progress: completed planet {planet} mission {mission}; that planet now has {want}/{cap} unlocked");
        }
    }

    public static void Reset()
    {
        for (int p = 0; p < PlanetCount; p++) Counts[p] = 1;
        Save();
        GD.Print("STORY progress reset: every planet back to its first mission");
    }
}
