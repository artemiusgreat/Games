using Godot;
using System;

namespace Titans;

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
        margin.AddThemeConstantOverride("margin_left", FlowUi.ScreenMarginHorizontal);
        margin.AddThemeConstantOverride("margin_right", FlowUi.ScreenMarginHorizontal);
        margin.AddThemeConstantOverride("margin_top", FlowUi.ScreenMarginVertical);
        margin.AddThemeConstantOverride("margin_bottom", FlowUi.ScreenMarginVertical);
        AddChild(margin);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", FlowUi.ScreenSeparation);
        margin.AddChild(root);

        // ---- menu bar: Back on the left, Continue on the
        // right - the owner's standing button rule ----
        var menu = new HBoxContainer();
        menu.AddThemeConstantOverride("separation", FlowUi.MenuSeparation);
        root.AddChild(menu);
        var back = FlowUi.MakeButton("Back", FlowUi.MenuButtonFontSize);
        back.Pressed += () => Flow.GoMissions();
        menu.AddChild(back);
        menu.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        var next = FlowUi.MakeButton("Continue", FlowUi.MenuButtonFontSize);
        next.Pressed += () => Flow.EnterLab();
        menu.AddChild(next);

        // ---- the mission team panel ----
        var panel = new PanelContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        panel.AddThemeStyleboxOverride("panel", FlowUi.Panel(FlowUi.Navy));
        root.AddChild(panel);
        var pv = new VBoxContainer();
        pv.AddThemeConstantOverride("separation", FlowUi.PanelColumnSeparation);
        panel.AddChild(pv);
        pv.AddChild(FlowUi.NavHeading("Mission team", FlowUi.SectionHeadingFontSize, FlowUi.Gold));
        _riderCount = FlowUi.MakeLabel("", FlowUi.SmallTextFontSize, FlowUi.Dim);
        pv.AddChild(_riderCount);
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        pv.AddChild(scroll);
        _riderRows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _riderRows.AddThemeConstantOverride("separation", FlowUi.RowSeparation);
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

    // The add row, row-button boxes and the rider slider
    // minimum are FlowUi's shared helpers (AddRow /
    // RowButtonBox / RiderSliderMin).

    private void Refresh()
    {
        if (_riderRows == null) return;
        TeamRoster.EnsureStory();
        var team = TeamRoster.Story;
        _riderCount.Text = $"{team.Count} OF {TeamRoster.MaxPerTeam} RIDERS" +
            " - rider 1 is the active rider, prepared in the lab next";
        foreach (var ch in _riderRows.GetChildren()) ch.QueueFree();
        float sliderMin = FlowUi.RiderSliderMin();
        for (int r = 0; r < team.Count; r++)
        {
            int idx = r;
            var member = team[r];
            bool activeSeat = idx == 0;
            var card = new PanelContainer();
            var cardSb = FlowUi.Panel(FlowUi.NavySoft, FlowUi.RiderCardAlpha);
            // Vertical padding matches the horizontal 10
            // (owner, 2026-11-15).
            cardSb.ContentMarginLeft = FlowUi.RiderCardPadding; cardSb.ContentMarginRight = FlowUi.RiderCardPadding;
            cardSb.ContentMarginTop = FlowUi.RiderCardPadding; cardSb.ContentMarginBottom = FlowUi.RiderCardPadding;
            card.AddThemeStyleboxOverride("panel", cardSb);
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", FlowUi.RowSeparation);
            card.AddChild(row);
            // One line per rider: name, tonnage and class all
            // live inside the selector - '< Enyo - 75T - Heavy >'.
            var slider = FlowUi.SelectorRow(out var rname, FlowUi.RiderSelectorFontSize, step =>
            {
                int cur = Array.FindIndex(RiderSpecs.All, s => s.Id == member.Spec.Id);
                int n = RiderSpecs.All.Length;
                SetRider(idx, ((cur + step) % n + n) % n);
            });
            slider.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            slider.CustomMinimumSize = new Vector2(sliderMin, 0f);
            rname.Text = $"{FlowUi.NameCase(member.Spec.Name)} - {member.Spec.Tonnage:F0}T - {FlowUi.NameCase(member.Spec.ClassLabel)}"
                + (activeSeat ? " - Active" : "");
            row.AddChild(slider);
            // Half-width slider (owner, 2026-11-15): an equal
            // expanding spacer takes the row's other half; the
            // remove button keeps the right edge.
            row.AddChild(new Control
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(sliderMin, 0f)
            });
            if (!activeSeat && team.Count > 1)
            {
                var rm = new Button
                {
                    Text = "−", CustomMinimumSize = new Vector2(FlowUi.RemoveButtonSize, FlowUi.RemoveButtonSize),
                    SizeFlagsVertical = SizeFlags.ShrinkCenter
                };
                rm.AddThemeFontSizeOverride("font_size", FlowUi.RemoveButtonFontSize);
                rm.AddThemeStyleboxOverride("normal", FlowUi.RowButtonBox(FlowUi.PanelButtonNormal));
                rm.AddThemeStyleboxOverride("hover", FlowUi.RowButtonBox(FlowUi.PanelButtonHover));
                rm.AddThemeStyleboxOverride("pressed", FlowUi.RowButtonBox(FlowUi.PanelButtonPressed));
                rm.AddThemeColorOverride("font_color", FlowUi.Ink);
                rm.AddThemeColorOverride("font_hover_color", FlowUi.Gold);
                rm.Pressed += () => RemoveRider(idx);
                row.AddChild(rm);
            }
            else
            {
                // Reserve the remove button's slot so the
                // slider split is identical on every row.
                row.AddChild(new Control { CustomMinimumSize = new Vector2(FlowUi.RemoveButtonSize, FlowUi.RemoveButtonSize) });
            }
            _riderRows.AddChild(card);
        }
        _riderRows.AddChild(FlowUi.AddRow(AddRider,
            team.Count >= TeamRoster.MaxPerTeam));
    }
}
