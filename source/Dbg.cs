using Godot;

namespace Titans;

// The one logging helper for the whole codebase (owner,
// 2026-11-22 gate; unified 2026-11-27: every class logs through
// here, no direct GD.Print/GD.PrintErr anywhere else).
// Diagnostics (per-shot, per-hit and per-event lines) flooded
// the editor console to overflow in normal play, so Print runs
// only in a test/shot mode or with AOT_VERBOSE set - production
// play stays quiet. Errors are the exception: Err always
// prints, in production too.
public static class Dbg
{
  public static bool On;

  public static void Print(object message)
  {
    if (On)
    {
      GD.Print(message);
    }
  }

  public static void Err(object message) => GD.PrintErr(message);
}
