using Godot;
using System;

// Wizard step 3 (Tournament, owner — teams stage): form the
// teams. Left: the teams, a scrollable list — at least two
// teams always stand, more can be added (to four). Right: the
// selected team's riders, a scrollable list — at least one
// rider per team, each slot cycles the roster, more can be
// added (to four). RANDOMIZE, in the control panel, deals a
// fresh bracket: a random number of teams with random riders.
// Team 1 is the player's own; its first rider is the one he
// prepares in the lab on the next step, so that seat can be
// re-chosen but never removed, and team 1 itself stands
// always. Menu: Back on the left, Continue on the right.
public partial class TeamSelectScreen : Control
{
    public FlowMain Flow;

    private VBoxContainer _teamRows, _riderRows;
    private Button _addTeam, _removeTeam, _addRider;
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

        // ---- menu bar: Back left, Continue right ----
        var menu = new HBoxContainer();
        menu.AddThemeConstantOverride("separation", 10);
        root.AddChild(menu);
        var back = FlowUi.MakeButton("Back", 14);
        back.Pressed += () => Flow.GoArena();
        menu.AddChild(back);
        menu.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
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

    // One of the two list panels. The teams panel (left) holds
    // the team rows + ADD/REMOVE TEAM; the riders panel (right)
    // holds the open team's rider rows + ADD RIDER and the
    // RANDOMIZE button (the control panel, per the spec).
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

        var btns = new HBoxContainer();
        btns.AddThemeConstantOverride("separation", 10);
        pv.AddChild(btns);
        if (teams)
        {
            _teamRows = rows;
            _addTeam = FlowUi.MakeButton("Add team", 13);
            _addTeam.Pressed += AddTeam;
            btns.AddChild(_addTeam);
            _removeTeam = FlowUi.MakeButton("Remove team", 13);
            _removeTeam.Pressed += RemoveTeam;
            btns.AddChild(_removeTeam);
        }
        else
        {
            _riderRows = rows;
            _addRider = FlowUi.MakeButton("Add rider", 13);
            _addRider.Pressed += AddRider;
            btns.AddChild(_addRider);
            btns.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
            var rand = FlowUi.MakeButton("Randomize", 13);
            rand.Pressed += RandomizeBracket;
            btns.AddChild(rand);
        }
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

    private void RemoveTeam()
    {
        int t = TeamRoster.TourneyTeam;
        if (t <= 0 || TeamRoster.Tourney.Count <= 2) return; // team 1 stands; two teams minimum
        TeamRoster.Tourney.RemoveAt(t);
        TeamRoster.TourneyTeam = Math.Min(t, TeamRoster.Tourney.Count - 1);
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

    private void CycleRider(int idx, int d)
    {
        var team = TeamRoster.Tourney[TeamRoster.TourneyTeam];
        int i = Array.FindIndex(RiderSpecs.All, s => s.Id == team[idx].Spec.Id);
        var spec = RiderSpecs.All[(i + d + RiderSpecs.All.Length) % RiderSpecs.All.Length];
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

    private void Refresh()
    {
        if (_teamRows == null) return;
        TeamRoster.EnsureTourney();
        int sel = TeamRoster.TourneyTeam;

        foreach (var ch in _teamRows.GetChildren()) ch.QueueFree();
        for (int t = 0; t < TeamRoster.Tourney.Count; t++)
        {
            int idx = t;
            var team = TeamRoster.Tourney[t];
            var b = new Button
            {
                Text = $"TEAM {t + 1} — {team.Count} RIDER{(team.Count == 1 ? "" : "S")}" + (t == 0 ? " · YOUR TEAM" : ""),
                CustomMinimumSize = new Vector2(0, 46),
                Alignment = HorizontalAlignment.Left
            };
            b.AddThemeFontSizeOverride("font_size", 15);
            if (t == sel)
            {
                b.Disabled = true;
                b.AddThemeColorOverride("font_disabled_color", FlowUi.Gold);
                b.AddThemeStyleboxOverride("disabled", FlowUi.Panel(new Color(0.14f, 0.13f, 0.05f, 0.95f)));
            }
            else b.Pressed += () => { TeamRoster.TourneyTeam = idx; Refresh(); };
            _teamRows.AddChild(b);
        }
        _teamCount.Text = $"{TeamRoster.Tourney.Count} OF {TeamRoster.MaxTeams} TEAMS — at least two teams always stand";
        _addTeam.Disabled = TeamRoster.Tourney.Count >= TeamRoster.MaxTeams;
        _removeTeam.Disabled = sel <= 0 || TeamRoster.Tourney.Count <= 2;
        _removeTeam.TooltipText = sel <= 0 ? "Your own team always stands" : "Remove the selected team";

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
            var selRow = FlowUi.SelectorRow(out var nameLabel, 16, d => CycleRider(idx, d));
            nameLabel.Text = member.Spec.Name;
            selRow.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            top.AddChild(selRow);
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
        _addRider.Disabled = open.Count >= TeamRoster.MaxPerTeam;
    }
}
