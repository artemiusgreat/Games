using Godot;

// Front-end step 1: choose the game mode. Tournament loads the
// arena duel; Mission continues to the orbital contract flow.
public partial class MapTypeScreen : Control
{
    public System.Action OnTournament;
    public System.Action OnMission;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        var root = new VBoxContainer();
        root.SetAnchorsPreset(LayoutPreset.Center);
        root.AddThemeConstantOverride("separation", 18);
        AddChild(root);
        // Center the column manually (anchors center + grow both ways).
        root.AnchorLeft = 0.5f; root.AnchorRight = 0.5f;
        root.AnchorTop = 0.5f; root.AnchorBottom = 0.5f;
        root.GrowHorizontal = GrowDirection.Both;
        root.GrowVertical = GrowDirection.Both;

        root.AddChild(FlowUi.MakeLabel("THE AGE OF TITANS", 52, FlowUi.Gold, HorizontalAlignment.Center));
        root.AddChild(FlowUi.MakeLabel("S E L E C T   G A M E   M O D E", 16, FlowUi.Dim, HorizontalAlignment.Center));

        var cards = new HBoxContainer();
        cards.AddThemeConstantOverride("separation", 22);
        cards.Alignment = BoxContainer.AlignmentMode.Center;
        root.AddChild(cards);
        cards.AddChild(Card(
            "TOURNAMENT",
            "Arena duel on a compact battlefield. Three riders enter — one leaves. " +
            "The classic test of machine and pilot, fought around the mesa under tournament rules.",
            "MAP 260u  ·  RIDERS 3  ·  FREE-FOR-ALL",
            "ENTER THE ARENA", () => OnTournament?.Invoke()));
        cards.AddChild(Card(
            "MISSION",
            "Planetary operations on a full-scale front. Pick a world in orbit, sign a " +
            "contract in the bar, then deploy from your base and march on the enemy.",
            "MAP 1900u  ·  BASES 3  ·  CONTRACTS 9",
            "FIND A CONTRACT", () => OnMission?.Invoke()));

        root.AddChild(FlowUi.MakeLabel("More modes will follow.", 12, FlowUi.Dim, HorizontalAlignment.Center));
    }

    private Control Card(string title, string desc, string stats, string cta, System.Action go)
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(430f, 300f) };
        panel.AddThemeStyleboxOverride("panel", FlowUi.Panel(FlowUi.Navy));
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 12);
        panel.AddChild(v);
        v.AddChild(FlowUi.MakeLabel(title, 30, FlowUi.Gold));
        var d = FlowUi.MakeLabel(desc, 14, FlowUi.Ink);
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
