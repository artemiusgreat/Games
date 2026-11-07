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
            "Select map", () => OnTournament?.Invoke()));
        cards.AddChild(Card(
            "Mission",
            "Planetary operations on a full-scale front. Pick a world in orbit, sign a " +
            "contract in the bar, then deploy from your base and march on the enemy.",
            "Map 1900u · Bases 3 · Contracts 9",
            "Select map", () => OnMission?.Invoke()));

    }

    private Control Card(string title, string desc, string stats, string cta, System.Action go)
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(430f, 300f) };
        panel.AddThemeStyleboxOverride("panel", FlowUi.Panel(FlowUi.Navy));
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 12);
        panel.AddChild(v);
        v.AddChild(FlowUi.MakeHeading(title, 30, FlowUi.Gold));
        var d = FlowUi.MakeLabel(desc, 14, FlowUi.Ink, HorizontalAlignment.Left, wrap: true);
        d.CustomMinimumSize = new Vector2(394f, 92f);
        v.AddChild(d);
        v.AddChild(FlowUi.MakeLabel(stats, 13, FlowUi.Dim));
        var spacer = new Control { SizeFlagsVertical = SizeFlags.Expand };
        v.AddChild(spacer);
        var b = FlowUi.MakeButton(cta, 16);
        b.Pressed += () => go();
        v.AddChild(b);
        return panel;
    }
}
