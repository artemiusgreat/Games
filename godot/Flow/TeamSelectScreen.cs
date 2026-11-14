using Godot;
using System;

// Wizard step 3 (Tournament, owner — teams stage): form the
// teams. Left: the teams, a scrollable list — at least two
// teams always stand; every row carries its own '−' (the
// player's team and the two-team minimum are protected),
// and ADD TEAM sits as a row right below the last team.
// Right: the selected team's riders, a scrollable list —
// each slot picks its rider from a DROPDOWN, at least one
// rider per team, ADD RIDER right below the last rider.
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
        var next = FlowUi.MakeButton("Rider Lab", 14);
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
    // right below the last row (owner) — no panel footer.
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
            pv.AddChild(FlowUi.MakeHeading("Teams", 13, FlowUi.Gold, bold: false));
            _teamCount = FlowUi.MakeLabel("", 12, FlowUi.Dim);
            pv.AddChild(_teamCount);
        }
        else
        {
            _riderHead = FlowUi.MakeHeading("", 13, FlowUi.Gold, bold: false);
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
        // chassis's default loadout — the lab's standing rule.
        team[idx] = TeamRoster.Make(spec);
        Refresh();
    }

    // A fresh bracket: 2-4 teams of 1-3 riders, dealt at
    // random — except the player's own seat (team 1, rider 1),
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

    // A full-width row button in the lists' own idiom (Add
    // team / Add rider): same box family as the rows, so its
    // padding matches theirs.
    private Button ListButton(string text)
    {
        var b = new Button { Text = text, CustomMinimumSize = new Vector2(0, 46) };
        b.AddThemeFontSizeOverride("font_size", 14);
        var bd = new Color(0.55f, 0.62f, 0.70f, 0.9f);
        b.AddThemeStyleboxOverride("normal", FlowUi.Panel(new Color(0.10f, 0.13f, 0.19f, 0.98f)));
        b.AddThemeStyleboxOverride("hover", FlowUi.Panel(new Color(0.16f, 0.20f, 0.28f, 0.98f)));
        b.AddThemeStyleboxOverride("pressed", FlowUi.Panel(new Color(0.20f, 0.17f, 0.08f, 0.98f)));
        b.AddThemeStyleboxOverride("disabled", FlowUi.Panel(new Color(0.06f, 0.07f, 0.09f, 0.9f)));
        b.AddThemeColorOverride("font_color", FlowUi.Ink);
        b.AddThemeColorOverride("font_hover_color", FlowUi.Gold);
        b.AddThemeColorOverride("font_disabled_color", new Color(0.45f, 0.48f, 0.52f));
        return b;
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
            rowPanel.AddThemeStyleboxOverride("panel", FlowUi.Panel(
                selected ? new Color(0.14f, 0.13f, 0.05f, 0.95f) : FlowUi.NavySoft, 0.95f));
            var host = new Control();
            rowPanel.AddChild(host);
            if (!selected)
            {
                var cover = new Button();
                cover.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
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
                cover.Pressed += () => { TeamRoster.TourneyTeam = idx; Refresh(); };
                host.AddChild(cover);
            }
            var lbl = FlowUi.MakeLabel(
                $"TEAM {t + 1} — {team.Count} RIDER{(team.Count == 1 ? "" : "S")}" + (t == 0 ? " · YOUR TEAM" : ""),
                15, selected ? FlowUi.Gold : FlowUi.Ink);
            lbl.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            lbl.OffsetLeft = 12f; lbl.OffsetRight = -44f;
            lbl.VerticalAlignment = VerticalAlignment.Center;
            lbl.MouseFilter = MouseFilterEnum.Ignore;
            host.AddChild(lbl);
            if (t > 0 && TeamRoster.Tourney.Count > 2)
            {
                var minus = new Button { Text = "−" };
                minus.AnchorLeft = 1f; minus.AnchorRight = 1f;
                minus.AnchorTop = 0.5f; minus.AnchorBottom = 0.5f;
                minus.OffsetLeft = -36f; minus.OffsetTop = -14f; minus.OffsetRight = -8f; minus.OffsetBottom = 14f;
                minus.AddThemeFontSizeOverride("font_size", 16);
                minus.AddThemeStyleboxOverride("normal", FlowUi.Panel(new Color(0.10f, 0.13f, 0.19f, 0.98f)));
                minus.AddThemeStyleboxOverride("hover", FlowUi.Panel(new Color(0.16f, 0.20f, 0.28f, 0.98f)));
                minus.AddThemeStyleboxOverride("pressed", FlowUi.Panel(new Color(0.20f, 0.17f, 0.08f, 0.98f)));
                minus.AddThemeColorOverride("font_color", FlowUi.Ink);
                minus.AddThemeColorOverride("font_hover_color", FlowUi.Gold);
                minus.TooltipText = "Remove this team";
                minus.Pressed += () => RemoveTeam(idx);
                host.AddChild(minus);
            }
            _teamRows.AddChild(rowPanel);
        }
        var addTeam = ListButton("Add team");
        addTeam.Disabled = TeamRoster.Tourney.Count >= TeamRoster.MaxTeams;
        addTeam.Pressed += AddTeam;
        _teamRows.AddChild(addTeam);
        _teamCount.Text = $"{TeamRoster.Tourney.Count} OF {TeamRoster.MaxTeams} TEAMS — at least two teams always stand";

        // ---- riders of the open team: dropdown per slot ----
        var open = TeamRoster.Tourney[sel];
        _riderHead.Text = $"Team {sel + 1} riders" + (sel == 0 ? " — your team" : "");
        _riderCount.Text = $"{open.Count} OF {TeamRoster.MaxPerTeam} RIDERS" +
            (sel == 0 ? " — rider 1 is yours, prepared in the lab next" : "");
        foreach (var ch in _riderRows.GetChildren()) ch.QueueFree();
        for (int r = 0; r < open.Count; r++)
        {
            int idx = r;
            var member = open[r];
            var card = new PanelContainer();
            card.AddThemeStyleboxOverride("panel", FlowUi.Panel(FlowUi.NavySoft, 0.9f));
            var cv = new VBoxContainer();
            cv.AddThemeConstantOverride("separation", 2);
            card.AddChild(cv);
            var top = new HBoxContainer();
            top.AddThemeConstantOverride("separation", 8);
            cv.AddChild(top);
            var dd = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            foreach (var sp in RiderSpecs.All) dd.AddItem(sp.Name);
            dd.Selected = Array.FindIndex(RiderSpecs.All, s => s.Id == member.Spec.Id);
            dd.AddThemeFontSizeOverride("font_size", 16);
            dd.AddThemeColorOverride("font_color", FlowUi.Gold);
            dd.AddThemeColorOverride("font_hover_color", FlowUi.Gold);
            dd.AddThemeStyleboxOverride("normal", FlowUi.Panel(new Color(0.10f, 0.13f, 0.19f, 0.98f)));
            dd.AddThemeStyleboxOverride("hover", FlowUi.Panel(new Color(0.16f, 0.20f, 0.28f, 0.98f)));
            dd.AddThemeStyleboxOverride("pressed", FlowUi.Panel(new Color(0.16f, 0.20f, 0.28f, 0.98f)));
            dd.AddThemeStyleboxOverride("focus", FlowUi.Panel(new Color(0, 0, 0, 0)));
            dd.ItemSelected += (long ix) => SetRider(idx, (int)ix);
            top.AddChild(dd);
            bool protectedSeat = sel == 0 && idx == 0;
            if (!protectedSeat && open.Count > 1)
            {
                var rm = FlowUi.MakeButton("×", 15);
                rm.TooltipText = "Remove this rider";
                rm.Pressed += () => RemoveRider(idx);
                top.AddChild(rm);
            }
            var meta = FlowUi.MakeLabel(
                $"{member.Spec.Tonnage:F0}T · {member.Spec.ClassLabel}" + (protectedSeat ? " · YOU" : ""),
                12, FlowUi.Dim);
            cv.AddChild(meta);
            _riderRows.AddChild(card);
        }
        var addRider = ListButton("Add rider");
        addRider.Disabled = open.Count >= TeamRoster.MaxPerTeam;
        addRider.Pressed += AddRider;
        _riderRows.AddChild(addRider);
    }
}
