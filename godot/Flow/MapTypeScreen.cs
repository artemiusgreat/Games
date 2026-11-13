using Godot;

// Front-end step 1: choose the game mode, laid out after the
// owner's menu concept (2026-11-13): the title art top-left,
// the EXPLORE · FIGHT · SURVIVE tagline top-right, and two
// tall illustrated cards over the orbital backdrop — no
// rewards block on the cards, no bottom status bar.
// Tournament loads the arena duel; Mission continues to the
// orbital contract flow. The whole card is the button, as
// before; the action bar at its foot is part of the card.
public partial class MapTypeScreen : Control
{
    public System.Action OnTournament;
    public System.Action OnMission;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 40);
        margin.AddThemeConstantOverride("margin_right", 40);
        margin.AddThemeConstantOverride("margin_top", 26);
        margin.AddThemeConstantOverride("margin_bottom", 30);
        AddChild(margin);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 10);
        margin.AddChild(root);

        // Top bar: the owner's title art (transparent letters)
        // on the left, the tagline on the right.
        var top = new HBoxContainer();
        root.AddChild(top);
        top.AddChild(new TextureRect
        {
            Texture = GD.Load<Texture2D>("res://assets/flow/title_card.png"),
            CustomMinimumSize = new Vector2(300f, 96f),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin
        });
        var tag = FlowUi.MakeHeading("EXPLORE  ·  FIGHT  ·  SURVIVE", 17, FlowUi.GoldDim,
            HorizontalAlignment.Right);
        tag.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        tag.VerticalAlignment = VerticalAlignment.Center;
        top.AddChild(tag);

        // The two mode cards, centred as a pair.
        var centre = new CenterContainer();
        centre.SizeFlagsVertical = SizeFlags.ExpandFill;
        root.AddChild(centre);
        var cards = new HBoxContainer();
        cards.AddThemeConstantOverride("separation", 30);
        centre.AddChild(cards);
        cards.AddChild(Card(
            "TOURNAMENT", "SELECT YOUR CHAMPION",
            "res://assets/flow/card_tournament.jpg",
            "Arena duel on a compact battlefield. Three riders enter — one leaves. " +
            "The classic test of machine and pilot, fought around the mesa under tournament rules.",
            "ENTER TOURNAMENT  >", featured: true, icon: CardIcon.Kind.Compass,
            go: () => OnTournament?.Invoke()));
        cards.AddChild(Card(
            "MISSION", "TAKE ON A CONTRACT",
            "res://assets/flow/card_mission.jpg",
            "Planetary operations on a full-scale front. Pick a world in orbit, sign a " +
            "contract, then deploy from your base and march on the enemy.",
            "VIEW MISSIONS  >", featured: false, icon: CardIcon.Kind.Target,
            go: () => OnMission?.Invoke()));
    }

    private Control Card(string title, string sub, string artPath, string desc,
        string action, bool featured, CardIcon.Kind icon, System.Action go)
    {
        var card = new Button { CustomMinimumSize = new Vector2(600f, 620f) };
        var normal = FlowUi.Panel(FlowUi.Navy);
        normal.BorderColor = featured ? FlowUi.Gold : FlowUi.Steel;
        card.AddThemeStyleboxOverride("normal", normal);
        var hover = FlowUi.Panel(FlowUi.NavySoft);
        hover.BorderColor = FlowUi.Gold;
        card.AddThemeStyleboxOverride("hover", hover);
        card.AddThemeStyleboxOverride("pressed",
            FlowUi.Panel(new Color(0.23f, 0.19f, 0.08f, 0.98f)));
        var focus = FlowUi.ButtonBox(new Color(0, 0, 0, 0));
        focus.DrawCenter = false;
        focus.BorderColor = FlowUi.Gold;
        focus.SetBorderWidthAll(3);
        card.AddThemeStyleboxOverride("focus", focus);
        card.Pressed += () => go();

        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 24);
        margin.AddThemeConstantOverride("margin_right", 24);
        margin.AddThemeConstantOverride("margin_top", 20);
        margin.AddThemeConstantOverride("margin_bottom", 20);
        card.AddChild(margin);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 14);
        margin.AddChild(v);

        // Header: line icon + title / subtitle stack.
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 16);
        v.AddChild(head);
        head.AddChild(new CardIcon
        {
            IconKind = icon,
            Tint = featured ? FlowUi.Gold : FlowUi.Dim,
            CustomMinimumSize = new Vector2(56f, 56f)
        });
        var titles = new VBoxContainer();
        titles.AddThemeConstantOverride("separation", 2);
        head.AddChild(titles);
        titles.AddChild(FlowUi.MakeHeading(title, 32,
            featured ? FlowUi.Gold : FlowUi.Ink));
        titles.AddChild(FlowUi.MakeLabel(sub, 14,
            featured ? FlowUi.GoldDim : FlowUi.Dim));

        // Illustration.
        v.AddChild(new TextureRect
        {
            Texture = GD.Load<Texture2D>(artPath),
            CustomMinimumSize = new Vector2(0f, 250f),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered
        });

        var d = FlowUi.MakeLabel(desc, 15, FlowUi.Ink, HorizontalAlignment.Left, wrap: true);
        d.CustomMinimumSize = new Vector2(552f, 72f);
        v.AddChild(d);

        var spacer = new Control { SizeFlagsVertical = SizeFlags.ExpandFill };
        v.AddChild(spacer);

        // Action bar (visual; the whole card clicks). The
        // featured card's bar is solid gold with dark text,
        // the other's an outline bar.
        var bar = new PanelContainer { CustomMinimumSize = new Vector2(0f, 58f) };
        if (featured)
        {
            var gold = new StyleBoxFlat
            {
                BgColor = new Color(0.83f, 0.64f, 0.22f, 1f),
                ContentMarginTop = 14, ContentMarginBottom = 14
            };
            gold.SetCornerRadiusAll(6);
            bar.AddThemeStyleboxOverride("panel", gold);
            bar.AddChild(FlowUi.MakeLabel(action, 19, new Color(0.09f, 0.07f, 0.02f),
                HorizontalAlignment.Center));
        }
        else
        {
            bar.AddThemeStyleboxOverride("panel",
                FlowUi.ButtonBox(new Color(0.08f, 0.11f, 0.16f, 0.95f)));
            bar.AddChild(FlowUi.MakeLabel(action, 19, FlowUi.Ink,
                HorizontalAlignment.Center));
        }
        v.AddChild(bar);

        // Labels and layout containers must not eat the click:
        // the whole subtree passes input through to the button.
        SetIgnoreMouse(card);
        return card;
    }

    private static void SetIgnoreMouse(Control c)
    {
        foreach (var child in c.GetChildren())
            if (child is Control cc)
            {
                cc.MouseFilter = MouseFilterEnum.Ignore;
                SetIgnoreMouse(cc);
            }
    }
}

// The cards' line icons, drawn as vector strokes (no glyph
// fonts): a compass rose for Tournament, a targeting reticle
// for Mission — the concept's two marks.
public partial class CardIcon : Control
{
    public enum Kind { Compass, Target }
    public Kind IconKind;
    public Color Tint = Colors.White;

    public override void _Draw()
    {
        var c = Size * 0.5f;
        float r = Mathf.Min(Size.X, Size.Y) * 0.46f;
        if (IconKind == Kind.Compass)
        {
            DrawArc(c, r, 0f, Mathf.Tau, 48, Tint, 2.5f, true);
            // Four long points, four short diagonals.
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.Pi * 0.5f;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                DrawLine(c - dir * r * 1.02f, c + dir * r * 1.02f, Tint, 2.5f, true);
                float b = a + Mathf.Pi * 0.25f;
                var d2 = new Vector2(Mathf.Cos(b), Mathf.Sin(b));
                DrawLine(c, c + d2 * r * 0.55f, Tint, 2f, true);
            }
            DrawCircle(c, 3.5f, Tint);
        }
        else
        {
            DrawArc(c, r * 0.78f, 0f, Mathf.Tau, 48, Tint, 2.5f, true);
            DrawArc(c, r * 0.30f, 0f, Mathf.Tau, 32, Tint, 2f, true);
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.Pi * 0.5f;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                DrawLine(c + dir * r * 0.52f, c + dir * r * 1.05f, Tint, 2.5f, true);
            }
            DrawCircle(c, 3f, Tint);
        }
    }
}
