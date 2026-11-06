using Godot;

// Front-end step 2 (Tournament path): pick the arena map, then
// enter the lab — the same two-step shape as the Mission path.
// One map exists today (the 260u proving grounds); the card list
// is where future arenas slot in.
public partial class TournamentMapScreen : Control
{
    public System.Action OnEnter;
    public System.Action OnBack;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var center = new CenterContainer();
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(center);
        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 16);
        center.AddChild(root);

        root.AddChild(FlowUi.MakeLabel("Tournament", 40, FlowUi.Gold, HorizontalAlignment.Center));
        root.AddChild(FlowUi.MakeLabel("Select map", 15, FlowUi.Dim, HorizontalAlignment.Center));

        var panel = new PanelContainer { CustomMinimumSize = new Vector2(600f, 0f) };
        panel.AddThemeStyleboxOverride("panel", FlowUi.Panel(FlowUi.Navy));
        root.AddChild(panel);
        var v = new VBoxContainer();
        v.AddThemeConstantOverride("separation", 10);
        panel.AddChild(v);
        var mapImg = new TextureRect
        {
            Texture = FlowUi.ArenaTexture(512),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            CustomMinimumSize = new Vector2(560f, 240f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        v.AddChild(mapImg);
        v.AddChild(FlowUi.MakeLabel("The Proving Grounds", 22, FlowUi.Gold));
        var d = FlowUi.MakeLabel(
            "A dry basin ringed by hills, with a flat-topped mesa at its heart. " +
            "Three riders drop on the pads around it and fight until one remains.",
            13, FlowUi.Ink, HorizontalAlignment.Left, wrap: true);
        d.CustomMinimumSize = new Vector2(560f, 0f);
        v.AddChild(d);
        v.AddChild(FlowUi.MakeLabel("Map 260u · Riders 3 · Free-for-all", 12, FlowUi.Dim));
        var enter = FlowUi.MakeButton("Enter the arena", 16);
        enter.Pressed += () => OnEnter?.Invoke();
        v.AddChild(enter);

        var backRow = new HBoxContainer();
        root.AddChild(backRow);
        var back = FlowUi.MakeButton("‹ Back", 14);
        back.Pressed += () => OnBack?.Invoke();
        backRow.AddChild(back);
    }
}
