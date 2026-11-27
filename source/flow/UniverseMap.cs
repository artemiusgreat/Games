using Godot;
using System.Collections.Generic;

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
        MouseFilter = MouseFilterEnum.Stop;
    }

    private Vector2 NodePos(int planet) =>
        MissionCatalog.Planets[planet].MapPos * Size;

    private int NodeAt(Vector2 p)
    {
        float hit = Mathf.Max(HitMinDistance, Size.X * HitDistanceFraction);
        for (int i = 0; i < MissionCatalog.Planets.Count; i++)
            if (p.DistanceTo(NodePos(i)) < hit) return i;
        return -1;
    }

    public override void _GuiInput(InputEvent ev)
    {
        if (ev is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            int hit = NodeAt(mb.Position);
            if (hit >= 0) { UiSfx.Play(); OnSelect?.Invoke(hit); AcceptEvent(); }
        }
        else if (ev is InputEventMouseMotion mm)
        {
            int hit = NodeAt(mm.Position);
            if (hit != _hover)
            {
                _hover = hit;
                MouseDefaultCursorShape = hit >= 0 ? CursorShape.PointingHand : CursorShape.Arrow;
                QueueRedraw();
            }
        }
    }

    public void SetSelected(int i)
    {
        Selected = i;
        QueueRedraw();
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
            float x = Size.X * i / GridDivisions;
            DrawLine(new Vector2(x, 0f), new Vector2(x, Size.Y), gridC, 1f);
            float y = Size.Y * i / GridDivisions;
            DrawLine(new Vector2(0f, y), new Vector2(Size.X, y), gridC, 1f);
        }
        // Decorative systems + their lanes.
        var laneC = LaneColor;
        foreach (var (a, b) in DecoLinks)
            DrawLine(DecoPos[a] * Size, DecoPos[b] * Size, laneC, 1f);
        foreach (var d in DecoPos)
            DrawCircle(d * Size, DecoNodeRadius, DecoNodeColor);
        // Routes between the playable planets.
        int n = MissionCatalog.Planets.Count;
        for (int i = 0; i < n; i++)
        for (int j = i + 1; j < n; j++)
        {
            bool hot = i == Selected || j == Selected;
            DrawLine(NodePos(i), NodePos(j),
                hot ? new Color(FlowUi.Gold, 0.75f) : RouteColor,
                hot ? RouteHotWidth : RouteWidth, true);
        }
        foreach (var (pl, dc) in PlanetLinks)
            DrawLine(NodePos(pl), DecoPos[dc] * Size, laneC, 1f);
        // Planet nodes.
        var font = FlowUi.NavFont;
        for (int i = 0; i < n; i++)
        {
            var p = MissionCatalog.Planets[i];
            var ctr = NodePos(i);
            bool sel = i == Selected;
            bool hov = i == _hover;
            if (sel)
            {
                DrawCircle(ctr, SelectedHaloRadius, new Color(FlowUi.Gold, 0.16f));
                DrawArc(ctr, SelectedRingRadius, 0f, Mathf.Tau, 40, FlowUi.Gold, SelectedRingWidth, true);
            }
            else if (hov)
                DrawArc(ctr, HoverRingRadius, 0f, Mathf.Tau, 40, new Color(FlowUi.Ink, 0.7f), HoverRingWidth, true);
            var body = p.BaseColor.Lerp(p.LandColor, 0.55f);
            DrawCircle(ctr, NodeRadius, body);
            DrawCircle(ctr + new Vector2(-4f, -4f), NodeHighlightRadius, new Color(p.PolarColor, 0.5f));
            // Border ring (owner, 2026-11-25, screenshot of a
            // node): the old 1.5px Steel ring (0.5 grey at 0.6
            // alpha) washed out against the photograph - the
            // ring is now thicker and brighter on every planet:
            // near-white Ink at full alpha, Gold when selected.
            DrawArc(ctr, NodeBorderRadius, 0f, Mathf.Tau, 40, sel ? FlowUi.Gold : FlowUi.Ink, sel ? NodeSelectedBorderWidth : NodeBorderWidth, true);
            // Text shadow under the name lines (owner,
            // 2026-11-25): over the photograph's bright
            // nebulae the bare labels washed out - a soft
            // dark copy offset down-right keeps them legible.
            var shadowC = LabelShadowColor;
            var shadowOff = new Vector2(LabelShadowOffset, LabelShadowOffset);
            DrawString(font, ctr + new Vector2(-LabelWidth / 2f, NameLabelOffsetY) + shadowOff, FlowUi.NameCase(p.Name),
                HorizontalAlignment.Center, LabelWidth, NameFontSize, shadowC);
            DrawString(font, ctr + new Vector2(-LabelWidth / 2f, SystemLabelOffsetY) + shadowOff, FlowUi.NameCase(p.System),
                HorizontalAlignment.Center, LabelWidth, SystemFontSize, shadowC);
            DrawString(font, ctr + new Vector2(-LabelWidth / 2f, NameLabelOffsetY), FlowUi.NameCase(p.Name),
                HorizontalAlignment.Center, LabelWidth, NameFontSize,
                sel ? FlowUi.Gold : FlowUi.Ink);
            DrawString(font, ctr + new Vector2(-LabelWidth / 2f, SystemLabelOffsetY), FlowUi.NameCase(p.System),
                HorizontalAlignment.Center, LabelWidth, SystemFontSize,
                sel ? FlowUi.Gold : FlowUi.Steel);
        }
    }
}
