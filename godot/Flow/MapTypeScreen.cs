using Godot;

// Front-end step 1: choose the game mode. Tournament loads the
// arena duel; Mission continues to the orbital contract flow.
public partial class MapTypeScreen : Control
{
    public System.Action OnTournament;
    public System.Action OnMission;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 18);
        center.AddChild(root);

        // The title is the owner's promo art itself (the stone
        // and ember card from the film), luminance-keyed so the
        // letters sit directly on the orbital backdrop.
        root.AddChild(new TextureRect
        {
            Texture = GD.Load<Texture2D>("res://assets/flow/title_card.png"),
            CustomMinimumSize = new Vector2(0f, 205f),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
        });

        var cards = new HBoxContainer();
        cards.AddThemeConstantOverride("separation", 22);
        cards.Alignment = BoxContainer.AlignmentMode.Center;
        root.AddChild(cards);
        cards.AddChild(Card(
            "Tournament",
            "Arena duel on a compact battlefield. Three riders enter — one leaves. " +
            "The classic test of machine and pilot, fought around the mesa under tournament rules.",
            "Map 260u · Riders 3 · Free-for-all",
            () => OnTournament?.Invoke()));
        cards.AddChild(Card(
            "Mission",
            "Planetary operations on a full-scale front. Pick a world in orbit, sign a " +
            "contract in the bar, then deploy from your base and march on the enemy.",
            "Map 1900u · Bases 3 · Contracts 9",
            () => OnMission?.Invoke()));

    }

    // The whole card is the button (owner): no separate
    // "Select map" control — the square itself clicks, hovers
    // and takes the gold focus frame like any other button.
    private Control Card(string title, string desc, string stats, System.Action go)
    {
        var card = new Button { CustomMinimumSize = new Vector2(430f, 300f) };
        card.AddThemeStyleboxOverride("normal", FlowUi.Panel(FlowUi.Navy));
        var hover = FlowUi.Panel(FlowUi.NavySoft);
        hover.BorderColor = FlowUi.GoldDim;
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
        margin.AddThemeConstantOverride("margin_left", 18);
        margin.AddThemeConstantOverride("margin_right", 18);
        margin.AddThemeConstantOverride("margin_top", 12);
        margin.AddThemeConstantOverride("margin_bottom", 12);
        card.AddChild(margin);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 12);
        margin.AddChild(v);
        v.AddChild(FlowUi.MakeHeading(title, 30, FlowUi.Gold));
        var d = FlowUi.MakeLabel(desc, 14, FlowUi.Ink, HorizontalAlignment.Left, wrap: true);
        d.CustomMinimumSize = new Vector2(394f, 92f);
        v.AddChild(d);
        var spacer = new Control { SizeFlagsVertical = SizeFlags.Expand };
        v.AddChild(spacer);
        v.AddChild(FlowUi.MakeLabel(stats, 13, FlowUi.Dim));
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
