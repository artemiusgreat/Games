using Godot;

// Diagnostic log gate (owner, 2026-11-22): gameplay diagnostics
// (per-shot, per-hit and per-event lines) flooded the editor
// console to overflow in normal play. They now print only in a
// test/shot mode or with AOT_VERBOSE set; errors (GD.PrintErr)
// always print. Harness test output does not go through here.
public static class Dbg
{
    public static bool On;

    public static void Print(object message)
    {
        if (On) GD.Print(message);
    }
}
