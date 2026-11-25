using Godot;

// UI select sounds (owner, 2026-11-24; the tick became
// a laser-like whoosh at his word, 2026-11-25): every
// selector, button and stepper on every screen sounds -
// the flow wizard, the lab, the in-game popups. One
// Install() per
// scene root (FlowMain for the wizard, RiderDuel for the
// lab and the match): the tree's NodeAdded signal wires
// every BaseButton as it appears (screens rebuild their
// rows on the fly), a meta flag keeps a button from
// wiring twice, and a small pool of plain 2D players on
// the root carries the sound - no 3D attenuation on UI.
// Click surfaces that are not buttons (mission rows, lab
// weapon tiles and hardpoint panels, the universe map)
// call Play() from their own press handlers.
public partial class UiSfx : Node
{
    private static UiSfx _current;
    private static AudioStream _stream;
    private readonly AudioStreamPlayer[] _pool = new AudioStreamPlayer[3];
    private int _next;
    public static int WiredCount; // harness visibility

    public static void Install(Node root)
    {
        _stream ??= GD.Load<AudioStream>("res://assets/sfx/uiwhoosh.wav");
        var sfx = new UiSfx();
        root.AddChild(sfx);
        for (int i = 0; i < sfx._pool.Length; i++)
        {
            var p = new AudioStreamPlayer { Stream = _stream, VolumeDb = -20f }; // owner: the first cut (-11) was too loud; tune here
            sfx.AddChild(p);
            sfx._pool[i] = p;
        }
        _current = sfx;
        root.GetTree().NodeAdded += sfx.OnNodeAdded;
        sfx.WireDeep(root);
    }

    public override void _ExitTree()
    {
        if (_current == this) _current = null;
    }

    public static void Play()
    {
        var s = _current;
        if (s == null) return;
        var p = s._pool[s._next];
        s._next = (s._next + 1) % s._pool.Length;
        p.Play();
    }

    private void OnNodeAdded(Node node)
    {
        if (node is BaseButton b) Wire(b);
    }

    private void WireDeep(Node node)
    {
        if (node is BaseButton b) Wire(b);
        foreach (var child in node.GetChildren()) WireDeep(child);
    }

    private void Wire(BaseButton b)
    {
        if (b.HasMeta("uisfx")) return;
        b.SetMeta("uisfx", true);
        WiredCount++;
        Dbg.Print($"UISFX wired '{b.Name}' total={WiredCount}");
        b.Pressed += Play;
    }
}
