using Godot;

namespace Titans;

// Center-screen crosshair for the duel scene (drawn, no textures),
// with a hitmarker X that the weapon code flashes on a registered hit.
// The reticle never wanders: there is no sway offset (the head-loss
// "sensor sway" was removed at the owner's word, 2026-11-15).
//
// Look-and-feel lives in the constants and static colours below
// (owner, 2026-11-25): every value that shapes the reticle is
// named once, here, instead of being hardcoded inside the draw
// calls. All values are this file's alone (checked: the amber
// and the fade time appear nowhere else), so they stay
// class-local rather than global.
public partial class Crosshair : Control
{
  // Reticle geometry, px from the centre.
  private const float ArmOuter = 26f;        // arms end this far out (the bottom arm ends at ArmOuterBottom)
  private const float ArmOuterBottom = 25f;
  private const float ArmInner = 8f;         // arms start this far out - the gap around the dot
  private const float ArmWidth = 3f;
  private const float OutlineWidth = 6.5f;   // the dark pass under the arms
  private const float DotRadius = 2.8f;
  private const float DotOutlineRadius = 4.6f;

  // Hitmarker X geometry and timing.
  private const float HitmarkOuter = 22f;    // corner strokes run from this offset
  private const float HitmarkInner = 12f;    // to this one
  private const float HitmarkWidth = 3.5f;
  private const float HitmarkFadeSeconds = 0.22f; // Hitmark 1 -> 0 over this long

  // Colours.
  private static readonly Color ReticleAmber = new(1f, 0.88f, 0.25f, 1f);
  private static readonly Color Outline = new(0.02f, 0.02f, 0.02f, 0.85f);
  private static readonly Color HitmarkWhite = new(1f, 1f, 1f); // alpha comes from Hitmark

  // ReticlePos sentinel: a coordinate this far off-screen
  // means "unset" (fall back to screen center).
  private const float UnsetCoord = -999f;
  private const float UnsetCutoff = -100f;

  public float Hitmark;
  // Screen position of the reticle. Since round 27 (center-
  // crosshair aiming, the owner's pick) the duel sets this to the
  // viewport center every frame.
  public Vector2 ReticlePos = new Vector2(UnsetCoord, UnsetCoord);
  // The centre the reticle was last drawn at (harness audit:
  // the breakable suite asserts it never leaves screen centre).
  public Vector2 LastCentre = new Vector2(UnsetCoord, UnsetCoord);

  public override void _Ready()
  {
    this.MouseFilter = MouseFilterEnum.Ignore;
  }

  public override void _Process(double delta)
  {
    if (this.Hitmark > 0f)
    {
      this.Hitmark = Mathf.Max(this.Hitmark - (float)delta / HitmarkFadeSeconds, 0f);
    }
    // Redraw every frame: the first _Draw can run before layout
    // gives this control its size, and a reticle drawn once at a
    // zero size would otherwise sit in the corner forever.
    this.QueueRedraw();
  }

  private void DrawArms(Vector2 c, Color colr, float wdt)
  {
    this.DrawLine(c + new Vector2(-ArmOuter, 0f), c + new Vector2(-ArmInner, 0f), colr, wdt);
    this.DrawLine(c + new Vector2(ArmInner, 0f), c + new Vector2(ArmOuter, 0f), colr, wdt);
    this.DrawLine(c + new Vector2(0f, -ArmOuter), c + new Vector2(0f, -ArmInner), colr, wdt);
    this.DrawLine(c + new Vector2(0f, ArmInner), c + new Vector2(0f, ArmOuterBottom), colr, wdt);
  }

  public override void _Draw()
  {
    var c = this.ReticlePos.X > UnsetCutoff ? this.ReticlePos : this.GetViewportRect().Size * 0.5f;
    this.LastCentre = c;
    // Owner's ask: the reticle must be unmissable - longer, thicker
    // arms in bright amber over a dark outline pass, so it reads
    // against sky, terrain and the rider's own hull alike.
    this.DrawArms(c, Outline, OutlineWidth);
    this.DrawArms(c, ReticleAmber, ArmWidth);
    this.DrawCircle(c, DotOutlineRadius, Outline);
    this.DrawCircle(c, DotRadius, ReticleAmber);
    if (this.Hitmark > 0f)
    {
      var hm = new Color(HitmarkWhite, this.Hitmark);
      this.DrawLine(c + new Vector2(-HitmarkOuter, -HitmarkOuter), c + new Vector2(-HitmarkInner, -HitmarkInner), hm, HitmarkWidth);
      this.DrawLine(c + new Vector2(HitmarkOuter, -HitmarkOuter), c + new Vector2(HitmarkInner, -HitmarkInner), hm, HitmarkWidth);
      this.DrawLine(c + new Vector2(-HitmarkOuter, HitmarkOuter), c + new Vector2(-HitmarkInner, HitmarkInner), hm, HitmarkWidth);
      this.DrawLine(c + new Vector2(HitmarkOuter, HitmarkOuter), c + new Vector2(HitmarkInner, HitmarkInner), hm, HitmarkWidth);
    }
  }
}
