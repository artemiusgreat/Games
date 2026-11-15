using Godot;
using System;

// Wizard step 3 (Story, owner, 2026-11-15): form the team
// for the signed mission. Until now the team was formed on
// a selector strip inside the Rider Lab; the owner moved
// it out of the lab onto its own screen, mirroring the
// Tournament team selector: the contract is accepted on
// the mission screen, the team is formed here, and only
// then does the lab open - where rider 1 (the active
// rider) is prepared. One team of 1-4 riders; rider 1 can
// be re-chosen but never removed, and every other slot
// carries its own '−'. A chassis change refits the slot
// with the new chassis's default loadout, the lab's rule.
public partial class StoryTeamScreen : Control
{
    public FlowMain Flow;

    private VBoxContainer _riderRows;
    private Label _riderCount;

    public override void _Ready()
    {
        TeamRoster.EnsureStory();
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

        // ---- menu bar: Back on the left, Continue on the
        // right - the owner's standing button rule ----
        var menu = new HBoxContainer();
        menu.AddThemeConstantOverride("separation", 10);
        root.AddChild(menu);
        var back = FlowUi.MakeButton("Back", 14);
        back.Pressed += () => Flow.GoMissions();
        menu.AddChild(back);
        menu.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var next = FlowUi.MakeButton("Continue", 14);
        next.Pressed += () => Flow.EnterLab();
        menu.AddChild(next);

        // ---- the mission team panel ----
        var panel = new PanelContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        panel.AddThemeStyleboxOverride("panel", FlowUi.Panel(FlowUi.Navy));
        root.AddChild(panel);
        var pv = new VBoxContainer();
        pv.AddThemeConstantOverride("separation", 7);
        panel.AddChild(pv);
        pv.AddChild(FlowUi.NavHeading("Mission team", 13, FlowUi.Gold));
        _riderCount = FlowUi.MakeLabel("", 12, FlowUi.Dim);
        pv.AddChild(_riderCount);
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        pv.AddChild(scroll);
        _riderRows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _riderRows.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(_riderRows);

        Refresh();
    }

    private static RiderSpec NextSpecAfter(RiderSpec spec)
    {
        int i = Array.FindIndex(RiderSpecs.All, s => s.Id == spec.Id);
        return RiderSpecs.All[(i + 1 + RiderSpecs.All.Length) % RiderSpecs.All.Length];
    }

    private void AddRider()
    {
        var team = TeamRoster.Story;
        if (team.Count >= TeamRoster.MaxPerTeam) return;
        team.Add(TeamRoster.Make(NextSpecAfter(team[team.Count - 1].Spec)));
        Refresh();
    }

    private void RemoveRider(int idx)
    {
        var team = TeamRoster.Story;
        if (idx <= 0 || idx >= team.Count) return; // rider 1, the active rider, stands
        team.RemoveAt(idx);
        Refresh();
    }

    private void SetRider(int idx, int specIndex)
    {
        var team = TeamRoster.Story;
        var spec = RiderSpecs.All[specIndex];
        if (team[idx].Spec.Id == spec.Id) return;
        team[idx] = TeamRoster.Make(spec);
        Refresh();
    }

    // The add control: a compact '+' button, right-aligned
    // below the last row - the team selector's idiom.
    private Control AddRow(System.Action add, bool disabled, string tip)
    {
        var row = new HBoxContainer();
        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var b = new Button { Text = "+", CustomMinimumSize = new Vector2(46, 46) };
        b.AddThemeFontSizeOverride("font_size", 18);
        b.AddThemeStyleboxOverride("normal", FlowUi.Panel(new Color(0.10f, 0.13f, 0.19f, 0.98f)));
        b.AddThemeStyleboxOverride("hover", FlowUi.Panel(new Color(0.16f, 0.20f, 0.28f, 0.98f)));
        b.AddThemeStyleboxOverride("pressed", FlowUi.Panel(new Color(0.20f, 0.17f, 0.08f, 0.98f)));
        b.AddThemeStyleboxOverride("disabled", FlowUi.Panel(new Color(0.06f, 0.07f, 0.09f, 0.9f)));
        b.AddThemeColorOverride("font_color", FlowUi.Ink);
        b.AddThemeColorOverride("font_hover_color", FlowUi.Gold);
        b.AddThemeColorOverride("font_disabled_color", new Color(0.45f, 0.48f, 0.52f));
        b.Disabled = disabled;
        b.TooltipText = tip;
        b.Pressed += () => add();
        row.AddChild(b);
        return row;
    }

    // FlowUi.Panel boxes carry 12px vertical content margins;
    // boxes for buttons that must fit inside a row wear
    // near-zero margins (the team selector's lesson).
    private static StyleBoxFlat RowBtnBox(Color bg)
    {
        var sb = FlowUi.Panel(bg);
        sb.ContentMarginLeft = 2; sb.ContentMarginRight = 2;
        sb.ContentMarginTop = 2; sb.ContentMarginBottom = 2;
        return sb;
    }

    private void Refresh()
    {
        if (_riderRows == null) return;
        TeamRoster.EnsureStory();
        var team = TeamRoster.Story;
        _riderCount.Text = $"{team.Count} OF {TeamRoster.MaxPerTeam} RIDERS" +
            " - rider 1 is the active rider, prepared in the lab next";
        foreach (var ch in _riderRows.GetChildren()) ch.QueueFree();
        for (int r = 0; r < team.Count; r++)
        {
            int idx = r;
            var member = team[r];
            bool activeSeat = idx == 0;
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
            // One line per rider: name, tonnage and class all
            // live inside the selector - '< Enyo - 75T - Heavy >'.
            var slider = FlowUi.SelectorRow(out var rname, 16, step =>
            {
                int cur = Array.FindIndex(RiderSpecs.All, s => s.Id == member.Spec.Id);
                int n = RiderSpecs.All.Length;
                SetRider(idx, ((cur + step) % n + n) % n);
            });
            slider.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            rname.Text = $"{FlowUi.NameCase(member.Spec.Name)} - {member.Spec.Tonnage:F0}T - {FlowUi.NameCase(member.Spec.ClassLabel)}"
                + (activeSeat ? " (Active)" : "");
            row.AddChild(slider);
            // Half-width slider (owner, 2026-11-15): an equal
            // expanding spacer takes the row's other half; the
            // remove button keeps the right edge.
            row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
            if (!activeSeat && team.Count > 1)
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
                rm.TooltipText = "Remove this rider";
                rm.Pressed += () => RemoveRider(idx);
                row.AddChild(rm);
            }
            _riderRows.AddChild(card);
        }
        _riderRows.AddChild(AddRow(AddRider,
            team.Count >= TeamRoster.MaxPerTeam, "Add a rider"));
    }
}
