using Godot;
using System;

namespace Titans;

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

  private static readonly Color SelectedTeamFill = new(0.14f, 0.13f, 0.05f, 0.95f);
  private static readonly Color TeamRowHoverFill = new(0.16f, 0.20f, 0.28f, 0.35f);

  private const float TeamsPanelStretchRatio = 0.85f;
  private const float RidersPanelStretchRatio = 1.15f;
  private const float TeamRowHeight = 46f;
  private const float TeamRowBorderWidth = 0.95f;
  private const int TeamRowMarginLeft = 14;
  private const int TeamRowMarginRight = 6;
  private const int TeamRowMarginVertical = 4;
  private const int TeamRowFontSize = 15;
  private const int TeamRemoveFontSize = 16;

  private VBoxContainer _teamRows, _riderRows;
  private Label _riderHead, _teamCount, _riderCount;
  private readonly Random _rng = new();

  public override void _Ready()
  {
    TeamRoster.EnsureTourney();
    this.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    var margin = new MarginContainer();
    margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    margin.AddThemeConstantOverride("margin_left", FlowUi.ScreenMarginHorizontal);
    margin.AddThemeConstantOverride("margin_right", FlowUi.ScreenMarginHorizontal);
    margin.AddThemeConstantOverride("margin_top", FlowUi.ScreenMarginVertical);
    margin.AddThemeConstantOverride("margin_bottom", FlowUi.ScreenMarginVertical);
    this.AddChild(margin);
    var root = new VBoxContainer();
    root.AddThemeConstantOverride("separation", FlowUi.ScreenSeparation);
    margin.AddChild(root);

    // ---- menu bar: Back left; Randomize, then Continue,
    // on the right (owner: Randomize deals both sections,
    // so it belongs to neither panel) ----
    var menu = new HBoxContainer();
    menu.AddThemeConstantOverride("separation", FlowUi.MenuSeparation);
    root.AddChild(menu);
    var back = FlowUi.MakeButton("Back", FlowUi.MenuButtonFontSize);
    back.Pressed += () => this.Flow.GoArena();
    menu.AddChild(back);
    menu.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
    var rand = FlowUi.MakeButton("Randomize", FlowUi.MenuButtonFontSize);
    rand.Pressed += this.RandomizeBracket;
    menu.AddChild(rand);
    var next = FlowUi.MakeButton("Continue", FlowUi.MenuButtonFontSize);
    next.Pressed += () => this.Flow.EnterLab();
    menu.AddChild(next);

    // ---- middle: teams | riders ----
    var mid = new HBoxContainer();
    mid.AddThemeConstantOverride("separation", FlowUi.MidSeparation);
    mid.SizeFlagsVertical = SizeFlags.ExpandFill;
    root.AddChild(mid);
    mid.AddChild(this.ListPanel(teams: true));
    mid.AddChild(this.ListPanel(teams: false));

    this.Refresh();
  }

  // One of the two list panels: heading, count line, the
  // scrollable rows. The add buttons live INSIDE the scroll,
  // right below the last row (owner) - no panel footer.
  private PanelContainer ListPanel(bool teams)
  {
    var panel = new PanelContainer
    {
      SizeFlagsHorizontal = SizeFlags.ExpandFill,
      SizeFlagsStretchRatio = teams ? TeamsPanelStretchRatio : RidersPanelStretchRatio
    };
    panel.AddThemeStyleboxOverride("panel", FlowUi.Panel(FlowUi.Navy));
    var pv = new VBoxContainer();
    pv.AddThemeConstantOverride("separation", FlowUi.PanelColumnSeparation);
    panel.AddChild(pv);

    if (teams)
    {
      pv.AddChild(FlowUi.NavHeading("Teams", FlowUi.SectionHeadingFontSize, FlowUi.Gold));
      this._teamCount = FlowUi.MakeLabel("", FlowUi.SmallTextFontSize, FlowUi.Dim);
      pv.AddChild(this._teamCount);
    }
    else
    {
      this._riderHead = FlowUi.NavHeading("", FlowUi.SectionHeadingFontSize, FlowUi.Gold);
      pv.AddChild(this._riderHead);
      this._riderCount = FlowUi.MakeLabel("", FlowUi.SmallTextFontSize, FlowUi.Dim);
      pv.AddChild(this._riderCount);
    }

    var scroll = new ScrollContainer
    {
      SizeFlagsVertical = SizeFlags.ExpandFill,
      HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
    };
    pv.AddChild(scroll);
    var rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    rows.AddThemeConstantOverride("separation", FlowUi.RowSeparation);
    scroll.AddChild(rows);
    if (teams)
    {
      this._teamRows = rows;
    }
    else
    {
      this._riderRows = rows;
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
    if (TeamRoster.Tourney.Count >= TeamRoster.MaxTeams)
    {
      return;
    }

    var spec = RiderSpecs.All[TeamRoster.Tourney.Count % RiderSpecs.All.Length];
    TeamRoster.Tourney.Add(new System.Collections.Generic.List<TeamMember> { TeamRoster.Make(spec) });
    TeamRoster.TourneyTeam = TeamRoster.Tourney.Count - 1;
    this.Refresh();
  }

  private void RemoveTeam(int idx)
  {
    if (idx <= 0 || TeamRoster.Tourney.Count <= TeamRoster.MinTeams)
    {
      return; // team 1 stands; two teams minimum
    }

    TeamRoster.Tourney.RemoveAt(idx);
    if (TeamRoster.TourneyTeam > idx)
    {
      TeamRoster.TourneyTeam--;
    }

    if (TeamRoster.TourneyTeam >= TeamRoster.Tourney.Count)
    {
      TeamRoster.TourneyTeam = TeamRoster.Tourney.Count - 1;
    }

    this.Refresh();
  }

  private void AddRider()
  {
    var team = TeamRoster.Tourney[TeamRoster.TourneyTeam];
    if (team.Count >= TeamRoster.MaxPerTeam)
    {
      return;
    }

    team.Add(TeamRoster.Make(NextSpecAfter(team[team.Count - 1].Spec)));
    this.Refresh();
  }

  private void RemoveRider(int idx)
  {
    var team = TeamRoster.Tourney[TeamRoster.TourneyTeam];
    if (team.Count <= 1 || idx < 0 || idx >= team.Count)
    {
      return;
    }

    if (TeamRoster.TourneyTeam == 0 && idx == 0)
    {
      return; // the player's own seat
    }

    team.RemoveAt(idx);
    this.Refresh();
  }

  private void SetRider(int idx, int specIndex)
  {
    var team = TeamRoster.Tourney[TeamRoster.TourneyTeam];
    var spec = RiderSpecs.All[specIndex];
    if (team[idx].Spec.Id == spec.Id)
    {
      return;
    }
    // A chassis change refits the slot with the new
    // chassis's default loadout - the lab's standing rule.
    team[idx] = TeamRoster.Make(spec);
    this.Refresh();
  }

  // A fresh bracket: 2-4 teams of 1-3 riders, dealt at
  // random - except the player's own seat (team 1, rider 1),
  // which keeps the rider he is about to prepare.
  private void RandomizeBracket()
  {
    var mine = TeamRoster.Tourney[0][0];
    int teamCount = this._rng.Next(TeamRoster.MinTeams, TeamRoster.MaxTeams + 1);
    TeamRoster.Tourney.Clear();
    for (int t = 0; t < teamCount; t++)
    {
      var team = new System.Collections.Generic.List<TeamMember>();
      int riders = this._rng.Next(1, 4);
      for (int r = 0; r < riders; r++)
      {
        team.Add(TeamRoster.Make(RiderSpecs.All[this._rng.Next(RiderSpecs.All.Length)]));
      }

      TeamRoster.Tourney.Add(team);
    }
    TeamRoster.Tourney[0][0] = mine;
    TeamRoster.TourneyTeam = 0;
    this.Refresh();
  }

  // The add row, row-button boxes and the rider slider
  // minimum are FlowUi's shared helpers (AddRow /
  // RowButtonBox / RiderSliderMin).

  private void Refresh()
  {
    if (this._teamRows == null)
    {
      return;
    }

    TeamRoster.EnsureTourney();
    int sel = TeamRoster.TourneyTeam;

    // ---- teams: uniform rows, each with its own '−' ----
    foreach (var ch in this._teamRows.GetChildren())
    {
      ch.QueueFree();
    }

    for (int t = 0; t < TeamRoster.Tourney.Count; t++)
    {
      int idx = t;
      var team = TeamRoster.Tourney[t];
      bool selected = t == sel;
      var rowPanel = new PanelContainer { CustomMinimumSize = new Vector2(0, TeamRowHeight) };
      var rowSb = FlowUi.Panel(
          selected ? SelectedTeamFill : FlowUi.NavySoft, TeamRowBorderWidth);
      // Left padding matches the row's effective
      // vertical padding (owner, 2026-11-15): the
      // 46px row centres its ~19px line, leaving
      // ~14px above and below the text, so the text
      // starts 14px in from the left border too.
      rowSb.ContentMarginLeft = TeamRowMarginLeft; rowSb.ContentMarginRight = TeamRowMarginRight;
      rowSb.ContentMarginTop = TeamRowMarginVertical; rowSb.ContentMarginBottom = TeamRowMarginVertical;
      rowPanel.AddThemeStyleboxOverride("panel", rowSb);
      var rowBox = new HBoxContainer();
      rowBox.AddThemeConstantOverride("separation", FlowUi.RowSeparation);
      rowPanel.AddChild(rowBox);
      string teamText = $"TEAM {t + 1} - {team.Count} RIDER{(team.Count == 1 ? "" : "S")}" + (t == 0 ? " · ACTIVE" : "");
      if (selected)
      {
        var lbl = FlowUi.MakeLabel(teamText, TeamRowFontSize, FlowUi.Gold);
        lbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        lbl.VerticalAlignment = VerticalAlignment.Center;
        lbl.MouseFilter = MouseFilterEnum.Ignore;
        rowBox.AddChild(lbl);
      }
      else
      {
        var cover = new Button { Text = teamText, Alignment = HorizontalAlignment.Left };
        cover.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        cover.AddThemeFontSizeOverride("font_size", TeamRowFontSize);
        // Empty boxes: a Panel stylebox here drew its
        // border as a stray pill inside the row.
        var clear = new StyleBoxEmpty();
        cover.AddThemeStyleboxOverride("normal", clear);
        cover.AddThemeStyleboxOverride("hover", new StyleBoxFlat
        {
          BgColor = TeamRowHoverFill,
          ContentMarginLeft = 0f,
          ContentMarginRight = 0f,
          ContentMarginTop = 0f,
          ContentMarginBottom = 0f
        });
        cover.AddThemeStyleboxOverride("pressed", clear);
        cover.AddThemeStyleboxOverride("focus", clear);
        cover.AddThemeColorOverride("font_color", FlowUi.Ink);
        cover.AddThemeColorOverride("font_hover_color", FlowUi.Ink);
        cover.Pressed += () => { TeamRoster.TourneyTeam = idx; this.Refresh(); };
        rowBox.AddChild(cover);
      }
      if (t > 0 && TeamRoster.Tourney.Count > 2)
      {
        var minus = new Button
        {
          Text = "−",
          CustomMinimumSize = new Vector2(FlowUi.RemoveButtonSize, FlowUi.RemoveButtonSize),
          SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        minus.AddThemeFontSizeOverride("font_size", TeamRemoveFontSize);
        minus.AddThemeStyleboxOverride("normal", FlowUi.RowButtonBox(FlowUi.PanelButtonNormal));
        minus.AddThemeStyleboxOverride("hover", FlowUi.RowButtonBox(FlowUi.PanelButtonHover));
        minus.AddThemeStyleboxOverride("pressed", FlowUi.RowButtonBox(FlowUi.PanelButtonPressed));
        minus.AddThemeColorOverride("font_color", FlowUi.Ink);
        minus.AddThemeColorOverride("font_hover_color", FlowUi.Gold);
        minus.Pressed += () => this.RemoveTeam(idx);
        rowBox.AddChild(minus);
      }
      this._teamRows.AddChild(rowPanel);
    }
    this._teamRows.AddChild(FlowUi.AddRow(this.AddTeam,
            TeamRoster.Tourney.Count >= TeamRoster.MaxTeams));
    this._teamCount.Text = $"{TeamRoster.Tourney.Count} OF {TeamRoster.MaxTeams} TEAMS - at least two teams always stand";

    // ---- riders of the open team: dropdown per slot ----
    var open = TeamRoster.Tourney[sel];
    this._riderHead.Text = $"Team {sel + 1} riders" + (sel == 0 ? " - Active" : "");
    this._riderCount.Text = $"{open.Count} OF {TeamRoster.MaxPerTeam} RIDERS" +
            (sel == 0 ? " - rider 1 is the active rider, prepared in the lab next" : "");
    foreach (var ch in this._riderRows.GetChildren())
    {
      ch.QueueFree();
    }

    float sliderMin = FlowUi.RiderSliderMin();
    for (int r = 0; r < open.Count; r++)
    {
      int idx = r;
      var member = open[r];
      bool protectedSeat = sel == 0 && idx == 0;
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
      // One line per rider (owner, 2026-11-15): name,
      // tonnage and class all live inside the selector -
      // '< Enyo - 75T - Heavy >'.
      var slider = FlowUi.SelectorRow(out var rname, FlowUi.RiderSelectorFontSize, step =>
      {
        int cur = Array.FindIndex(RiderSpecs.All, s => s.Id == member.Spec.Id);
        int n = RiderSpecs.All.Length;
        this.SetRider(idx, ((cur + step) % n + n) % n);
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
          Text = "−",
          CustomMinimumSize = new Vector2(FlowUi.RemoveButtonSize, FlowUi.RemoveButtonSize),
          SizeFlagsVertical = SizeFlags.ShrinkCenter
        };
        rm.AddThemeFontSizeOverride("font_size", FlowUi.RemoveButtonFontSize);
        rm.AddThemeStyleboxOverride("normal", FlowUi.RowButtonBox(FlowUi.PanelButtonNormal));
        rm.AddThemeStyleboxOverride("hover", FlowUi.RowButtonBox(FlowUi.PanelButtonHover));
        rm.AddThemeStyleboxOverride("pressed", FlowUi.RowButtonBox(FlowUi.PanelButtonPressed));
        rm.AddThemeColorOverride("font_color", FlowUi.Ink);
        rm.AddThemeColorOverride("font_hover_color", FlowUi.Gold);
        rm.Pressed += () => this.RemoveRider(idx);
        row.AddChild(rm);
      }
      else
      {
        // Reserve the remove button's slot so the
        // slider split is identical on every row.
        row.AddChild(new Control { CustomMinimumSize = new Vector2(FlowUi.RemoveButtonSize, FlowUi.RemoveButtonSize) });
      }
      this._riderRows.AddChild(card);
    }
    this._riderRows.AddChild(FlowUi.AddRow(this.AddRider,
            open.Count >= TeamRoster.MaxPerTeam));
  }
}
