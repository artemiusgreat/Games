using Godot;
using System.Collections.Generic;

// The universe map (owner, teams stage): a Freelancer-style star
// chart for the planet step — nebula wash, a faint grid, star
// dots, decorative neighbour systems joined by trade lanes, and
// the three playable planets as glowing nodes with route lines
// between them. Clicking a planet node selects it, in sync with
// the planet selector on the left. Pure presentation + picking;
// the planet data (names, colours, MapPos) is MissionCatalog's.
public partial class UniverseMap : Control
{
    public System.Action<int> OnSelect;
    public int Selected;

    private int _hover = -1;
    private readonly List<Vector2> _stars = new();
    private readonly List<float> _starA = new();
    private readonly List<(Vector2 P, float R, Color C)> _nebula = new();
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
    // 0 Chronos, 1 Meridian, 2 Orion): each planet ties to its
    // nearest deco systems so the playable nodes sit inside the
    // chart's web, not on top of it.
    private static readonly (int Planet, int Deco)[] PlanetLinks =
    {
        (0, 1), (0, 7), (1, 5), (1, 4), (2, 3), (2, 0)
    };

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        var rng = new RandomNumberGenerator { Seed = 20261113 };
        for (int i = 0; i < 170; i++)
        {
            _stars.Add(new Vector2(rng.Randf(), rng.Randf()));
            _starA.Add(rng.RandfRange(0.25f, 0.9f));
        }
        var nebCols = new[]
        {
            new Color(0.10f, 0.70f, 0.55f), new Color(0.42f, 0.30f, 0.85f),
            new Color(0.90f, 0.45f, 0.15f), new Color(0.20f, 0.45f, 0.95f)
        };
        for (int i = 0; i < 9; i++)
        {
            _nebula.Add((new Vector2(rng.Randf(), rng.Randf()),
                rng.RandfRange(0.16f, 0.34f), nebCols[i % nebCols.Length]));
        }
    }

    private Vector2 NodePos(int planet) =>
        MissionCatalog.Planets[planet].MapPos * Size;

    private int NodeAt(Vector2 p)
    {
        float hit = Mathf.Max(26f, Size.X * 0.035f);
        for (int i = 0; i < MissionCatalog.Planets.Count; i++)
            if (p.DistanceTo(NodePos(i)) < hit) return i;
        return -1;
    }

    public override void _GuiInput(InputEvent ev)
    {
        if (ev is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            int hit = NodeAt(mb.Position);
            if (hit >= 0) { OnSelect?.Invoke(hit); AcceptEvent(); }
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
        var rect = new Rect2(Vector2.Zero, Size);
        DrawRect(rect, new Color(0.010f, 0.018f, 0.042f, 1f));
        // Faint chart grid.
        var gridC = new Color(0.38f, 0.52f, 0.75f, 0.055f);
        for (int i = 1; i < 12; i++)
        {
            float x = Size.X * i / 12f;
            DrawLine(new Vector2(x, 0f), new Vector2(x, Size.Y), gridC, 1f);
            float y = Size.Y * i / 12f;
            DrawLine(new Vector2(0f, y), new Vector2(Size.X, y), gridC, 1f);
        }
        // Nebula wash: layered soft discs.
        foreach (var (p, r, c) in _nebula)
        {
            var ctr = p * Size;
            float rad = r * Size.X;
            DrawCircle(ctr, rad, new Color(c, 0.045f));
            DrawCircle(ctr, rad * 0.62f, new Color(c, 0.06f));
            DrawCircle(ctr, rad * 0.36f, new Color(c, 0.075f));
        }
        // Stars.
        for (int i = 0; i < _stars.Count; i++)
            DrawCircle(_stars[i] * Size, 1.3f, new Color(0.85f, 0.90f, 1f, _starA[i]));
        // Decorative systems + their lanes.
        var laneC = new Color(0.45f, 0.60f, 0.85f, 0.28f);
        foreach (var (a, b) in DecoLinks)
            DrawLine(DecoPos[a] * Size, DecoPos[b] * Size, laneC, 1f);
        foreach (var d in DecoPos)
            DrawCircle(d * Size, 3f, new Color(0.55f, 0.75f, 0.95f, 0.55f));
        // Routes between the playable planets.
        int n = MissionCatalog.Planets.Count;
        for (int i = 0; i < n; i++)
        for (int j = i + 1; j < n; j++)
        {
            bool hot = i == Selected || j == Selected;
            DrawLine(NodePos(i), NodePos(j),
                hot ? new Color(FlowUi.Gold, 0.75f) : new Color(0.45f, 0.60f, 0.85f, 0.40f),
                hot ? 2f : 1.5f, true);
        }
        foreach (var (pl, dc) in PlanetLinks)
            DrawLine(NodePos(pl), DecoPos[dc] * Size, laneC, 1f);
        // Planet nodes.
        var font = FlowUi.HeadFont(false);
        for (int i = 0; i < n; i++)
        {
            var p = MissionCatalog.Planets[i];
            var ctr = NodePos(i);
            bool sel = i == Selected;
            bool hov = i == _hover;
            if (sel)
            {
                DrawCircle(ctr, 25f, new Color(FlowUi.Gold, 0.16f));
                DrawArc(ctr, 21f, 0f, Mathf.Tau, 40, FlowUi.Gold, 2f, true);
            }
            else if (hov)
                DrawArc(ctr, 19f, 0f, Mathf.Tau, 40, new Color(FlowUi.Ink, 0.7f), 1.5f, true);
            var body = p.BaseColor.Lerp(p.LandColor, 0.55f);
            DrawCircle(ctr, 12f, body);
            DrawCircle(ctr + new Vector2(-4f, -4f), 5f, new Color(p.PolarColor, 0.5f));
            DrawArc(ctr, 14f, 0f, Mathf.Tau, 40, sel ? FlowUi.Gold : FlowUi.Steel, sel ? 2.5f : 1.5f, true);
            DrawString(font, ctr + new Vector2(-80f, 48f), p.Name.ToUpper(),
                HorizontalAlignment.Center, 160f, 16,
                sel ? FlowUi.Gold : FlowUi.Ink);
        }
    }
}
