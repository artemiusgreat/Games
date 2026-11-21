using Godot;
using System;

// Wizard step 3 (Tournament, owner - teams stage): form the
// teams. Left: the teams, a scrollable list - at least two
// teams always stand; every row carries its own '−' (the
// player's team and the two-team minimum are protected),
// and a compact '+' button, right-aligned and never
// stretched, sits right below the last team.
// Right: the selected team's riders, a scrollable list -
// each slot picks its rider with a SLIDER (the front-end's
// selector row), at least one rider per team, every row
// carries its own '−', and the same '+' below the last.
// RANDOMIZE sits in the top menu, before the Continue
// button, because it deals BOTH lists: a random number of
// teams with random riders. Team 1 is the player's own; its
// first rider is the one he prepares in the lab on the next
// step, so that seat can be re-chosen but never removed.
public partial class TeamSelectScreen : Control
{
    public FlowMain Flow;

    private VBoxContainer _teamRows, _riderRows;
    private Label _riderHead, _teamCount, _riderCount;
    private readonly Random _rng = new();

    public override void _Ready()
    {
        TeamRoster.EnsureTourney();
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 18);
        margin.AddThemeConstantOverride("margin_right", 18);
        margin.AddThemeConstantOverride("margin_top", 14);
        margin.AddThemeConstantOverride("margin_bottom", 14);
        AddChild(margin);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 10);
        margin.AddChild(root);

        // ---- menu bar: Back left; Randomize, then Continue,
        // on the right (owner: Randomize deals both sections,
        // so it belongs to neither panel) ----
        var menu = new HBoxContainer();
        menu.AddThemeConstantOverride("separation", 10);
        root.AddChild(menu);
        var back = FlowUi.MakeButton("Back", 14);
        back.Pressed += () => Flow.GoArena();
        menu.AddChild(back);
        menu.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var rand = FlowUi.MakeButton("Randomize", 14);
        rand.Pressed += RandomizeBracket;
        menu.AddChild(rand);
        var next = FlowUi.MakeButton("Continue", 14);
        next.Pressed += () => Flow.EnterLab();
        menu.AddChild(next);

        // ---- middle: teams | riders ----
        var mid = new HBoxContainer();
        mid.AddThemeConstantOverride("separation", 12);
        mid.SizeFlagsVertical = SizeFlags.ExpandFill;
        root.AddChild(mid);
        mid.AddChild(ListPanel(teams: true));
        mid.AddChild(ListPanel(teams: false));

        Refresh();
    }

    // One of the two list panels: heading, count line, the
    // scrollable rows. The add buttons live INSIDE the scroll,
    // right below the last row (owner) - no panel footer.
    private Control ListPanel(bool teams)
    {
        var panel = new PanelContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = teams ? 0.85f : 1.15f
        };
        panel.AddThemeStyleboxOverride("panel", FlowUi.Panel(FlowUi.Navy));
        var pv = new VBoxContainer();
        pv.AddThemeConstantOverride("separation", 7);
        panel.AddChild(pv);

        if (teams)
        {
            pv.AddChild(FlowUi.NavHeading("Teams", 13, FlowUi.Gold));
            _teamCount = FlowUi.MakeLabel("", 12, FlowUi.Dim);
            pv.AddChild(_teamCount);
        }
        else
        {
            _riderHead = FlowUi.NavHeading("", 13, FlowUi.Gold);
            pv.AddChild(_riderHead);
            _riderCount = FlowUi.MakeLabel("", 12, FlowUi.Dim);
            pv.AddChild(_riderCount);
        }

        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        pv.AddChild(scroll);
        var rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        rows.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(rows);
        if (teams) _teamRows = rows; else _riderRows = rows;
        return panel;
    }

    private static RiderSpec NextSpecAfter(RiderSpec spec)
    {
        int i = Array.FindIndex(RiderSpecs.All, s => s.Id == spec.Id);
        return RiderSpecs.All[(i + 1 + RiderSpecs.All.Length) % RiderSpecs.All.Length];
    }

    private void AddTeam()
    {
        if (TeamRoster.Tourney.Count >= TeamRoster.MaxTeams) return;
        var spec = RiderSpecs.All[TeamRoster.Tourney.Count % RiderSpecs.All.Length];
        TeamRoster.Tourney.Add(new System.Collections.Generic.List<TeamMember> { TeamRoster.Make(spec) });
        TeamRoster.TourneyTeam = TeamRoster.Tourney.Count - 1;
        Refresh();
    }

    private void RemoveTeam(int idx)
    {
        if (idx <= 0 || TeamRoster.Tourney.Count <= 2) return; // team 1 stands; two teams minimum
        TeamRoster.Tourney.RemoveAt(idx);
        if (TeamRoster.TourneyTeam > idx) TeamRoster.TourneyTeam--;
        if (TeamRoster.TourneyTeam >= TeamRoster.Tourney.Count)
            TeamRoster.TourneyTeam = TeamRoster.Tourney.Count - 1;
        Refresh();
    }

    private void AddRider()
    {
        var team = TeamRoster.Tourney[TeamRoster.TourneyTeam];
        if (team.Count >= TeamRoster.MaxPerTeam) return;
        team.Add(TeamRoster.Make(NextSpecAfter(team[team.Count - 1].Spec)));
        Refresh();
    }

    private void RemoveRider(int idx)
    {
        var team = TeamRoster.Tourney[TeamRoster.TourneyTeam];
        if (team.Count <= 1 || idx < 0 || idx >= team.Count) return;
        if (TeamRoster.TourneyTeam == 0 && idx == 0) return; // the player's own seat
        team.RemoveAt(idx);
        Refresh();
    }

    private void SetRider(int idx, int specIndex)
    {
        var team = TeamRoster.Tourney[TeamRoster.TourneyTeam];
        var spec = RiderSpecs.All[specIndex];
        if (team[idx].Spec.Id == spec.Id) return;
        // A chassis change refits the slot with the new
        // chassis's default loadout - the lab's standing rule.
        team[idx] = TeamRoster.Make(spec);
        Refresh();
    }

    // A fresh bracket: 2-4 teams of 1-3 riders, dealt at
    // random - except the player's own seat (team 1, rider 1),
    // which keeps the rider he is about to prepare.
    private void RandomizeBracket()
    {
        var mine = TeamRoster.Tourney[0][0];
        int teamCount = _rng.Next(2, TeamRoster.MaxTeams + 1);
        TeamRoster.Tourney.Clear();
        for (int t = 0; t < teamCount; t++)
        {
            var team = new System.Collections.Generic.List<TeamMember>();
            int riders = _rng.Next(1, 4);
            for (int r = 0; r < riders; r++)
                team.Add(TeamRoster.Make(RiderSpecs.All[_rng.Next(RiderSpecs.All.Length)]));
            TeamRoster.Tourney.Add(team);
        }
        TeamRoster.Tourney[0][0] = mine;
        TeamRoster.TourneyTeam = 0;
        Refresh();
    }

    // The add control (owner, 2026-11-14): a compact '+'
    // button in the lists' own box family, right-aligned
    // below the last row - never stretched across the list.
    private Control AddRow(System.Action add, bool disabled)
    {
        var row = new HBoxContainer();
        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var b = new Button { Text = "+", CustomMinimumSize = new Vector2(46, 46) };
        b.AddThemeFontSizeOverride("font_size", 18);
        var bd = new Color(0.55f, 0.62f, 0.70f, 0.9f);
        b.AddThemeStyleboxOverride("normal", FlowUi.Panel(new Color(0.10f, 0.13f, 0.19f, 0.98f)));
        b.AddThemeStyleboxOverride("hover", FlowUi.Panel(new Color(0.16f, 0.20f, 0.28f, 0.98f)));
        b.AddThemeStyleboxOverride("pressed", FlowUi.Panel(new Color(0.20f, 0.17f, 0.08f, 0.98f)));
        b.AddThemeStyleboxOverride("disabled", FlowUi.Panel(new Color(0.06f, 0.07f, 0.09f, 0.9f)));
        b.AddThemeColorOverride("font_color", FlowUi.Ink);
        b.AddThemeColorOverride("font_hover_color", FlowUi.Gold);
        b.AddThemeColorOverride("font_disabled_color", new Color(0.45f, 0.48f, 0.52f));
        b.Disabled = disabled;
        b.Pressed += () => add();
        row.AddChild(b);
        return row;
    }

    // FlowUi.Panel boxes carry 12px vertical content margins -
    // a button wearing one has a ~44px minimum height whatever
    // its rect says, which is how the team rows' '-' spilled
    // out of its row (owner, 2026-11-15). Boxes for buttons
    // that must fit inside a row wear near-zero margins.
    private static StyleBoxFlat RowBtnBox(Color bg)
    {
        var sb = FlowUi.Panel(bg);
        sb.ContentMarginLeft = 2; sb.ContentMarginRight = 2;
        sb.ContentMarginTop = 2; sb.ContentMarginBottom = 2;
        return sb;
    }

    // One shared slider minimum for every rider row (owner,
    // 2026-11-15): the widest label the selector can show -
    // the longest rider name with the active suffix - plus
    // the two 42px arrows and separations. With one minimum
    // (and the remove slot reserved on rows without a
    // button), the half-width split lands identical on
    // every row instead of drifting with the name length.
    private static float RiderSliderMin()
    {
        var probe = FlowUi.NavHeading("", 16, FlowUi.Gold, HorizontalAlignment.Center);
        var font = probe.GetThemeFont("font");
        float w = 0f;
        foreach (var s in RiderSpecs.All)
        {
            var t = $"{FlowUi.NameCase(s.Name)} - {s.Tonnage:F0}T - {FlowUi.NameCase(s.ClassLabel)} - Active";
            w = Mathf.Max(w, font.GetStringSize(t, HorizontalAlignment.Left, -1f, 16).X);
        }
        return w + 100f;
    }

    private void Refresh()
    {
        if (_teamRows == null) return;
        TeamRoster.EnsureTourney();
        int sel = TeamRoster.TourneyTeam;

        // ---- teams: uniform rows, each with its own '−' ----
        foreach (var ch in _teamRows.GetChildren()) ch.QueueFree();
        for (int t = 0; t < TeamRoster.Tourney.Count; t++)
        {
            int idx = t;
            var team = TeamRoster.Tourney[t];
            bool selected = t == sel;
            var rowPanel = new PanelContainer { CustomMinimumSize = new Vector2(0, 46) };
            var rowSb = FlowUi.Panel(
                selected ? new Color(0.14f, 0.13f, 0.05f, 0.95f) : FlowUi.NavySoft, 0.95f);
            // Left padding matches the row's effective
            // vertical padding (owner, 2026-11-15): the
            // 46px row centres its ~19px line, leaving
            // ~14px above and below the text, so the text
            // starts 14px in from the left border too.
            rowSb.ContentMarginLeft = 14; rowSb.ContentMarginRight = 6;
            rowSb.ContentMarginTop = 4; rowSb.ContentMarginBottom = 4;
            rowPanel.AddThemeStyleboxOverride("panel", rowSb);
            var rowBox = new HBoxContainer();
            rowBox.AddThemeConstantOverride("separation", 8);
            rowPanel.AddChild(rowBox);
            string teamText = $"TEAM {t + 1} - {team.Count} RIDER{(team.Count == 1 ? "" : "S")}" + (t == 0 ? " · ACTIVE" : "");
            if (selected)
            {
                var lbl = FlowUi.MakeLabel(teamText, 15, FlowUi.Gold);
                lbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                lbl.VerticalAlignment = VerticalAlignment.Center;
                lbl.MouseFilter = MouseFilterEnum.Ignore;
                rowBox.AddChild(lbl);
            }
            else
            {
                var cover = new Button { Text = teamText, Alignment = HorizontalAlignment.Left };
                cover.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                cover.AddThemeFontSizeOverride("font_size", 15);
                // Empty boxes: a Panel stylebox here drew its
                // border as a stray pill inside the row.
                var clear = new StyleBoxEmpty();
                cover.AddThemeStyleboxOverride("normal", clear);
                cover.AddThemeStyleboxOverride("hover", new StyleBoxFlat
                {
                    BgColor = new Color(0.16f, 0.20f, 0.28f, 0.35f),
                    ContentMarginLeft = 0f, ContentMarginRight = 0f,
                    ContentMarginTop = 0f, ContentMarginBottom = 0f
                });
                cover.AddThemeStyleboxOverride("pressed", clear);
                cover.AddThemeStyleboxOverride("focus", clear);
                cover.AddThemeColorOverride("font_color", FlowUi.Ink);
                cover.AddThemeColorOverride("font_hover_color", FlowUi.Ink);
                cover.Pressed += () => { TeamRoster.TourneyTeam = idx; Refresh(); };
                rowBox.AddChild(cover);
            }
            if (t > 0 && TeamRoster.Tourney.Count > 2)
            {
                var minus = new Button
                {
                    Text = "−", CustomMinimumSize = new Vector2(30, 30),
                    SizeFlagsVertical = SizeFlags.ShrinkCenter
                };
                minus.AddThemeFontSizeOverride("font_size", 16);
                minus.AddThemeStyleboxOverride("normal", RowBtnBox(new Color(0.10f, 0.13f, 0.19f, 0.98f)));
                minus.AddThemeStyleboxOverride("hover", RowBtnBox(new Color(0.16f, 0.20f, 0.28f, 0.98f)));
                minus.AddThemeStyleboxOverride("pressed", RowBtnBox(new Color(0.20f, 0.17f, 0.08f, 0.98f)));
                minus.AddThemeColorOverride("font_color", FlowUi.Ink);
                minus.AddThemeColorOverride("font_hover_color", FlowUi.Gold);
                minus.Pressed += () => RemoveTeam(idx);
                rowBox.AddChild(minus);
            }
            _teamRows.AddChild(rowPanel);
        }
        _teamRows.AddChild(AddRow(AddTeam,
            TeamRoster.Tourney.Count >= TeamRoster.MaxTeams));
        _teamCount.Text = $"{TeamRoster.Tourney.Count} OF {TeamRoster.MaxTeams} TEAMS - at least two teams always stand";

        // ---- riders of the open team: dropdown per slot ----
        var open = TeamRoster.Tourney[sel];
        _riderHead.Text = $"Team {sel + 1} riders" + (sel == 0 ? " - Active" : "");
        _riderCount.Text = $"{open.Count} OF {TeamRoster.MaxPerTeam} RIDERS" +
            (sel == 0 ? " - rider 1 is the active rider, prepared in the lab next" : "");
        foreach (var ch in _riderRows.GetChildren()) ch.QueueFree();
        float sliderMin = RiderSliderMin();
        for (int r = 0; r < open.Count; r++)
        {
            int idx = r;
            var member = open[r];
            bool protectedSeat = sel == 0 && idx == 0;
            var card = new PanelContainer();
            var cardSb = FlowUi.Panel(FlowUi.NavySoft, 0.9f);
            // Vertical padding matches the horizontal 10
            // (owner, 2026-11-15).
            cardSb.ContentMarginLeft = 10; cardSb.ContentMarginRight = 10;
            cardSb.ContentMarginTop = 10; cardSb.ContentMarginBottom = 10;
            card.AddThemeStyleboxOverride("panel", cardSb);
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            card.AddChild(row);
            // One line per rider (owner, 2026-11-15): name,
            // tonnage and class all live inside the selector -
            // '< Enyo - 75T - Heavy >'.
            var slider = FlowUi.SelectorRow(out var rname, 16, step =>
            {
                int cur = Array.FindIndex(RiderSpecs.All, s => s.Id == member.Spec.Id);
                int n = RiderSpecs.All.Length;
                SetRider(idx, ((cur + step) % n + n) % n);
            });
            slider.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            slider.CustomMinimumSize = new Vector2(sliderMin, 0f);
            rname.Text = $"{FlowUi.NameCase(member.Spec.Name)} - {member.Spec.Tonnage:F0}T - {FlowUi.NameCase(member.Spec.ClassLabel)}"
                + (protectedSeat ? " - Active" : "");
            row.AddChild(slider);
            // Half-width slider (owner, 2026-11-15): an equal
            // expanding spacer takes the row's other half; the
            // remove button keeps the right edge.
            row.AddChild(new Control
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(sliderMin, 0f)
            });
            if (!protectedSeat && open.Count > 1)
            {
                var rm = new Button
                {
                    Text = "−", CustomMinimumSize = new Vector2(30, 30),
                    SizeFlagsVertical = SizeFlags.ShrinkCenter
                };
                rm.AddThemeFontSizeOverride("font_size", 15);
                rm.AddThemeStyleboxOverride("normal", RowBtnBox(new Color(0.10f, 0.13f, 0.19f, 0.98f)));
                rm.AddThemeStyleboxOverride("hover", RowBtnBox(new Color(0.16f, 0.20f, 0.28f, 0.98f)));
                rm.AddThemeStyleboxOverride("pressed", RowBtnBox(new Color(0.20f, 0.17f, 0.08f, 0.98f)));
                rm.AddThemeColorOverride("font_color", FlowUi.Ink);
                rm.AddThemeColorOverride("font_hover_color", FlowUi.Gold);
                rm.Pressed += () => RemoveRider(idx);
                row.AddChild(rm);
            }
            else
            {
                // Reserve the remove button's slot so the
                // slider split is identical on every row.
                row.AddChild(new Control { CustomMinimumSize = new Vector2(30, 30) });
            }
            _riderRows.AddChild(card);
        }
        _riderRows.AddChild(AddRow(AddRider,
            open.Count >= TeamRoster.MaxPerTeam));
    }
}
