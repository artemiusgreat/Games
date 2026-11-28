using Godot;

namespace Titans;

// The universe map (owner, teams stage): a Freelancer-style star
// chart for the planet step - the owner's universe photograph
// fills the whole screen behind it (PlanetScreen draws it), a
// faint grid, decorative neighbour systems joined by trade
// lanes, and the three playable planets as glowing nodes with
// route lines between them. Clicking a planet node selects it,
// in sync with the planet selector on the left. Pure
// presentation + picking; the planet data (names, colours,
// MapPos) is MissionCatalog's.
public partial class UniverseMap : Control
{
  public System.Action<int> OnSelect;
  public int Selected;

  private static readonly Color GridColor = new(0.38f, 0.52f, 0.75f, 0.055f);
  private static readonly Color LaneColor = new(0.45f, 0.60f, 0.85f, 0.28f);
  private static readonly Color DecoNodeColor = new(0.55f, 0.75f, 0.95f, 0.55f);
  private static readonly Color RouteColor = new(0.45f, 0.60f, 0.85f, 0.40f);
  private static readonly Color LabelShadowColor = new(0f, 0f, 0f, 0.55f);

  private const int GridDivisions = 12;
  private const float HitMinDistance = 26f;
  private const float HitDistanceFraction = 0.035f;
  private const float DecoNodeRadius = 3f;
  private const float RouteWidth = 1.5f;
  private const float RouteHotAlpha = 0.75f;       // a route touching the selected planet brightens to this
  private const float SelectedHaloAlpha = 0.16f;
  private const int RingSegments = 40;             // arc smoothness for every node ring
  private const float HoverRingAlpha = 0.7f;
  private const float NodeBodyLerp = 0.55f;        // node body: base colour lerped this far toward the land colour
  private const float NodeHighlightOffset = 4f;    // the polar highlight sits this far up-left of the centre
  private const float NodeHighlightAlpha = 0.5f;
  private const float RouteHotWidth = 2f;
  private const float NodeRadius = 12f;
  private const float NodeHighlightRadius = 5f;
  private const float NodeBorderRadius = 14f;
  // Owner's tuned node borders (2026-11-25): named, not rounded.
  private const float NodeBorderWidth = 3.5f;
  private const float NodeSelectedBorderWidth = 4.5f;
  private const float SelectedHaloRadius = 25f;
  private const float SelectedRingRadius = 21f;
  private const float SelectedRingWidth = 2f;
  private const float HoverRingRadius = 19f;
  private const float HoverRingWidth = 1.5f;
  private const float LabelWidth = 160f;
  private const float NameLabelOffsetY = 48f;
  private const float SystemLabelOffsetY = 64f;
  private const int NameFontSize = 16;
  private const int SystemFontSize = 11;
  private const float LabelShadowOffset = 1.5f;

  private int _hover = -1;
  private static readonly Vector2[] DecoPos =
  {
        new(0.08f, 0.16f), new(0.16f, 0.44f), new(0.36f, 0.50f), new(0.58f, 0.14f),
        new(0.84f, 0.22f), new(0.90f, 0.72f), new(0.60f, 0.84f), new(0.12f, 0.88f)
    };
  private static readonly (int, int)[] DecoLinks =
  {
        (0, 2), (1, 0), (1, 2), (3, 4), (4, 5), (6, 7), (2, 6)
    };
  // Planet-index links into the decorative web (planet order:
  // 0 Yorkshire, 1 New York, 2 Bavaria): each planet ties to its
  // nearest deco systems so the playable nodes sit inside the
  // chart's web, not on top of it.
  private static readonly (int Planet, int Deco)[] PlanetLinks =
  {
        (0, 1), (0, 7), (1, 5), (1, 4), (2, 3), (2, 0)
    };

  public override void _Ready()
  {
    this.MouseFilter = MouseFilterEnum.Stop;
  }

  private Vector2 NodePos(int planet) =>
      MissionCatalog.Planets[planet].MapPos * this.Size;

  private int NodeAt(Vector2 p)
  {
    float hit = Mathf.Max(HitMinDistance, this.Size.X * HitDistanceFraction);
    for (int i = 0; i < MissionCatalog.Planets.Count; i++)
    {
      if (p.DistanceTo(this.NodePos(i)) < hit)
      {
        return i;
      }
    }

    return -1;
  }

  public override void _GuiInput(InputEvent ev)
  {
    if (ev is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
    {
      int hit = this.NodeAt(mb.Position);
      if (hit >= 0) { UiSfx.Play(); this.OnSelect?.Invoke(hit); this.AcceptEvent(); }
    }
    else if (ev is InputEventMouseMotion mm)
    {
      int hit = this.NodeAt(mm.Position);
      if (hit != this._hover)
      {
        this._hover = hit;
        this.MouseDefaultCursorShape = hit >= 0 ? CursorShape.PointingHand : CursorShape.Arrow;
        this.QueueRedraw();
      }
    }
  }

  public void SetSelected(int i)
  {
    this.Selected = i;
    this.QueueRedraw();
  }

  public override void _Draw()
  {
    // No backdrop of its own (owner, 2026-11-25): the
    // screen's universe photograph shows through; the
    // chart draws only its grid and nodes over it.
    // Faint chart grid (kept at the owner's word).
    var gridC = GridColor;
    for (int i = 1; i < GridDivisions; i++)
    {
      float x = this.Size.X * i / GridDivisions;
      this.DrawLine(new Vector2(x, 0f), new Vector2(x, this.Size.Y), gridC, 1f);
      float y = this.Size.Y * i / GridDivisions;
      this.DrawLine(new Vector2(0f, y), new Vector2(this.Size.X, y), gridC, 1f);
    }
    // Decorative systems + their lanes.
    var laneC = LaneColor;
    foreach (var (a, b) in DecoLinks)
    {
      this.DrawLine(DecoPos[a] * this.Size, DecoPos[b] * this.Size, laneC, 1f);
    }

    foreach (var d in DecoPos)
    {
      this.DrawCircle(d * this.Size, DecoNodeRadius, DecoNodeColor);
    }
    // Routes between the playable planets.
    int n = MissionCatalog.Planets.Count;
    for (int i = 0; i < n; i++)
    {
      for (int j = i + 1; j < n; j++)
      {
        bool hot = i == this.Selected || j == this.Selected;
        this.DrawLine(this.NodePos(i), this.NodePos(j),
                hot ? new Color(FlowUi.Gold, RouteHotAlpha) : RouteColor,
                hot ? RouteHotWidth : RouteWidth, true);
      }
    }

    foreach (var (pl, dc) in PlanetLinks)
    {
      this.DrawLine(this.NodePos(pl), DecoPos[dc] * this.Size, laneC, 1f);
    }
    // Planet nodes.
    var font = FlowUi.NavFont;
    for (int i = 0; i < n; i++)
    {
      var p = MissionCatalog.Planets[i];
      var ctr = this.NodePos(i);
      bool sel = i == this.Selected;
      bool hov = i == this._hover;
      if (sel)
      {
        this.DrawCircle(ctr, SelectedHaloRadius, new Color(FlowUi.Gold, SelectedHaloAlpha));
        this.DrawArc(ctr, SelectedRingRadius, 0f, Mathf.Tau, RingSegments, FlowUi.Gold, SelectedRingWidth, true);
      }
      else if (hov)
      {
        this.DrawArc(ctr, HoverRingRadius, 0f, Mathf.Tau, RingSegments, new Color(FlowUi.Ink, HoverRingAlpha), HoverRingWidth, true);
      }

      var body = p.BaseColor.Lerp(p.LandColor, NodeBodyLerp);
      this.DrawCircle(ctr, NodeRadius, body);
      this.DrawCircle(ctr + new Vector2(-NodeHighlightOffset, -NodeHighlightOffset), NodeHighlightRadius, new Color(p.PolarColor, NodeHighlightAlpha));
      // Border ring (owner, 2026-11-25, screenshot of a
      // node): the old 1.5px Steel ring (0.5 grey at 0.6
      // alpha) washed out against the photograph - the
      // ring is now thicker and brighter on every planet:
      // near-white Ink at full alpha, Gold when selected.
      this.DrawArc(ctr, NodeBorderRadius, 0f, Mathf.Tau, RingSegments, sel ? FlowUi.Gold : FlowUi.Ink, sel ? NodeSelectedBorderWidth : NodeBorderWidth, true);
      // Text shadow under the name lines (owner,
      // 2026-11-25): over the photograph's bright
      // nebulae the bare labels washed out - a soft
      // dark copy offset down-right keeps them legible.
      var shadowC = LabelShadowColor;
      var shadowOff = new Vector2(LabelShadowOffset, LabelShadowOffset);
      this.DrawString(font, ctr + new Vector2(-LabelWidth / 2f, NameLabelOffsetY) + shadowOff, FlowUi.NameCase(p.Name),
                HorizontalAlignment.Center, LabelWidth, NameFontSize, shadowC);
      this.DrawString(font, ctr + new Vector2(-LabelWidth / 2f, SystemLabelOffsetY) + shadowOff, FlowUi.NameCase(p.System),
                HorizontalAlignment.Center, LabelWidth, SystemFontSize, shadowC);
      this.DrawString(font, ctr + new Vector2(-LabelWidth / 2f, NameLabelOffsetY), FlowUi.NameCase(p.Name),
                HorizontalAlignment.Center, LabelWidth, NameFontSize,
                sel ? FlowUi.Gold : FlowUi.Ink);
      this.DrawString(font, ctr + new Vector2(-LabelWidth / 2f, SystemLabelOffsetY), FlowUi.NameCase(p.System),
                HorizontalAlignment.Center, LabelWidth, SystemFontSize,
                sel ? FlowUi.Gold : FlowUi.Steel);
    }
  }
}
