using Godot;

namespace Titans;

// Titans module: capture pipeline and the proof/live camera modes.
// Split from the single-file build (round 22 cleanup); the class
// stays one partial class - see Titans.cs for the field block.
public partial class Titans
{
  // Sniper view state (RMB toggles; see the chase branch of
  // UpdateCamera and the mouse-look scaling in Player input).
  private bool _sniper;
  private const float SniperFov = 16f;
  private const string DuelChaseCapture = "res://proof/duel_chase.png"; // harness capture output
  private const float BaseFov = 70f;               // the chase camera's normal field of view
  private const float HeroLightHeight = 15f;       // hero light offset above the player
  private const float BoomLength = 26f;            // chase boom at chassis scale 1 (x k in code)
  private const float BoomMinLength = 8f;          // steep-pitch boom floor (x k)
  private const float CameraFloorClearance = 1.3f; // the boom/floor clamps keep the camera this far off the ground
  private const float AnchorHeightFactor = 1.15f;  // boom anchor height, in chassis heights
  private const float AnchorPitchLiftFactor = 0.22f; // extra anchor lift at the pitch cap
  private const float AimPitchCapSin = 0.9757f;    // sin of the +-1.35 rad aim pitch cap
  private const float FovEaseRate = 11f;           // sniper zoom ease
  private const float ShakeAmplitude = 0.3f;       // camera shake offset scale
  private const float ReferenceChassisHeight = 13f; // boom distances scale with chassis height over this reference
  private const float BoomShortenPitchMin = 0.05f; // aim pitch (dir Y) past which the boom may shorten
  private const float ShakeMinAmplitude = 0.002f;  // shake below this is not worth jittering the camera
  private const float ShakeVerticalJitter = 0.6f;  // vertical shake offset range (+/- this)
  private const float MinLookStepSq = 0.0001f;     // squared-distance guard: the look target must differ from the camera

  // Proof-camera staging (owner, 2026-11-27): each capture
  // mode's offset from the rider/pad/point it frames and
  // its look offset, named per mode. Absolute world
  // positions in the vista modes stay inline - they are
  // placement coordinates, not offsets.
  private static readonly Vector3 MissionBaseCamOffset = new(46f, 17f, 46f);
  private static readonly Vector3 MissionBaseLookOffset = new(0f, 7f, 0f);
  private const float MissionVistaOffsetX = 30f, MissionVistaHeight = 60f, MissionVistaOffsetZ = 95f;
  private const float MissionVistaLookHeight = 16f; // look target's height over the far terrain sample
  private const float SlopeVistaCamHeight = 30f, SlopeVistaLookHeight = 4f; // heights over the sampled terrain
  private const float SlopeCloseCamHeight = 6f, SlopeCloseLookHeight = 16f;
  private const float CanyonVistaOffsetX = 150f, CanyonVistaCamHeight = 82f, CanyonVistaOffsetZ = -60f;
  private static readonly Vector3 BlockedCamOffset = new(-21f, 9f, 17f);
  private static readonly Vector3 BlockedCamLookOffset = new(0f, 6.5f, 0f);
  private const float HomecamOffsetX = -150f, HomecamHeight = 62f, HomecamOffsetZ = 150f;
  private const float HomecamLookOffsetX = 30f, HomecamLookHeight = 8f, HomecamLookOffsetZ = -10f;
  private const float PasscamBackDistance = 55f, PasscamEyeHeight = 9f;
  private const float PasscamLookDistance = 10f, PasscamLookHeight = 1.5f;
  private const float GateskyEyeHeight = 10f, GateskyLookDistance = 100f, GateskyLookHeight = 42f;
  private const float EnemyFrontDistance = 16f;
  private static readonly Vector3 EnemyFrontCamOffset = new(0f, 5.5f, 0f);
  private static readonly Vector3 EnemyFrontLookOffset = new(0f, 6.5f, 0f);
  private static readonly Vector3 EnemySideCamOffset = new(-17.5f, 5.5f, -3f);
  private static readonly Vector3 EnemySideLookOffset = new(0f, 6.8f, 0f);
  private static readonly Vector3 WideCamOffset = new(-27f, 15f, -27f);
  private static readonly Vector3 WideLookOffset = new(0f, 5f, 0f);
  private static readonly Vector3 TwistCamOffset = new(-11f, 7f, -11f);
  private static readonly Vector3 TwistLookOffset = new(0f, 8f, 0f);
  private static readonly Vector3 PropSpotOffset = new(6f, 0f, 16f); // the staged prop's offset from the player
  private static readonly Vector3 PropCamOffset = new(11f, 5f, 11f);
  private static readonly Vector3 PropLookOffset = new(0f, 3.5f, 0f);
  private const float WpnSideCamHeight = 6.0f, WpnRocketsCamHeight = 10.5f;
  private const float WpnSideCamOffsetX = -26f, WpnSideCamOffsetZ = 3f;
  private static readonly Vector3 WpnSideLookOffset = new(0f, 6.5f, 0f);
  private static readonly Vector3 WpnDeadCamOffset = new(16f, 7f, 13f);
  private static readonly Vector3 WpnDeadLookOffset = new(0f, 6.5f, 0f);
  private const float WpnHudBackDistance = 27f, WpnHudSideOffset = 8.5f, WpnHudCamHeight = 11f;
  private const float WpnHudLookHeight = 6f, WpnHudLookForward = 9f;
  private static readonly Vector3 RiderSideCamOffset = new(-18f, 6.5f, 0f);
  private static readonly Vector3 RiderSideLookOffset = new(0f, 6f, 0f);
  private static readonly Vector3 BrkPlayerCamOffset = new(-15f, 5.5f, 5f);
  private static readonly Vector3 BrkPlayerLookOffset = new(0f, 6.5f, 0f);
  private static readonly Vector3 BrkTargetCamOffset = new(14.5f, 6.5f, -5f);
  private static readonly Vector3 BrkTargetLookOffset = new(0f, 6.5f, 0f);
  private static readonly Vector3 BrkDebrisCamOffset = new(-8.5f, 3.4f, 6.5f);
  private const float BrkDebrisLookHeight = 0.4f;
  private static readonly Vector3 DetailBuildingCamOffset = new(-10.5f, 4.6f, 10.5f);
  private static readonly Vector3 DetailBuildingLookOffset = new(0f, 3.4f, 0f);
  private static readonly Vector3 DetailTreeCamOffset = new(5.2f, 3.1f, 5.2f);
  private static readonly Vector3 DetailTreeLookOffset = new(0f, 3.4f, 0f);
  private const float PhotoTuftCamHeight = 0.55f, PhotoTuftLookHeight = 0.30f; // heights over the sampled terrain
  private static readonly Vector3 GrassCloseCamOffset = new(2.4f, 1.5f, 2.4f);
  private const float GrassCloseLookHeight = 0.25f;
  private static readonly Vector3 GrassTopCamOffset = new(0.01f, 8f, 0.01f); // near-vertical: the 0.01 keeps the look off the singularity
  private static readonly Vector3 GrassFieldCamOffset = new(-17f, 14f, 17f);
  private const float PhotoGroundEyeHeight = 2.1f, PhotoGroundLookHeight = 5.5f;
  private const float SlopeSideDistance = 15f, SlopeSideCamHeight = 4.2f, SlopeSideLookHeight = 5.5f;
  private const float GroundDuelDistance = 11f, GroundDuelCamHeight = 3.4f, GroundDuelLookHeight = 6.5f;
  // Fixed world positions of the staged vista framings.
  private const float MissionVistaLookX = 0f, MissionVistaLookZ = 240f;
  private const float SlopeVistaCamX = 120f, SlopeVistaCamZ = -120f, SlopeVistaLookX = -40f, SlopeVistaLookZ = 40f;
  private const float SlopeCloseCamX = 58f, SlopeCloseCamZ = -14f, SlopeCloseLookX = 0f, SlopeCloseLookZ = 0f;
  private static readonly Vector3 CanyonVistaLookPos = new(-268f, 26f, 155f);
  private static readonly Vector3 WpnBlockedCamPos = new(43f, 9.5f, 17f), WpnBlockedLookPos = new(22f, 5.5f, 3f);
  private const float DetailBuildingX = 22f, DetailBuildingZ = 8f;   // the (22,8) building
  private const float DetailTreeX = -12f, DetailTreeZ = -8f;          // the hand-placed tree
  private static readonly Vector3 VistaCamPos = new(-88f, 44f, 90f), VistaLookPos = new(12f, 0f, -12f);
  private static readonly Vector3 PhotoOverCamPos = new(58f, 34f, 64f), PhotoOverLookPos = new(-6f, 0f, -6f);
  private const float PhotoTuftCamX = -18.6f, PhotoTuftCamZ = 2.2f, PhotoTuftLookX = -21.5f, PhotoTuftLookZ = -1.5f;
  private const float PhotoGroundX = -21f, PhotoGroundZ = -1f, PhotoGroundSampleZ = 0f; // the eye stands at Z, its ground is sampled at SampleZ

  // Capture tags (owner, 2026-11-27: the tag is a state
  // key - DoCapture compares it to decide when a suite
  // ends - so the literals live once, here, as constants,
  // never inline at a call site).
  private static class CaptureTags
  {
    public const string Chase = "chase";
    public const string SlopeVista = "slopeVista";
    public const string SlopeClose = "slopeClose";
    public const string Stride = "stride";
    public const string Main = "main";
    public const string Twist = "twist";
    public const string Prop = "prop";
    public const string Rider = "rider";
    public const string Aim = "aim";
    public const string Stone = "stone";
    public const string Debris = "debris";
    public const string Boom = "boom";
    public const string FlipMid = "flipmid";
    public const string FlipLate = "fliplate";
    public const string VBefore = "vbefore";
    public const string VAfter = "vafter";
    public const string TeamFight = "teamfight";
    public const string TeamScreen = "teamscreen";
    public const string WpnHit = "wpnHit";
    public const string WLaser = "wlaser";
    public const string WRockets = "wrockets";
    public const string WBlocked = "wblocked";
    public const string WHud = "whud";
    public const string WDead = "wdead";
    public const string EnvTreeDown = "envTreeDown";
    public const string BPodFall = "bpodfall";
    public const string BDebris = "bdebris";
    public const string BNoGun = "bnogun";
    public const string BHud = "bhud";
    public const string BLimp = "blimp";
    public const string BDisabled = "bdisabled";
    public const string BHead = "bhead";
    public const string BrkKnock = "brkKnock";
    public const string Gait = "gait";
    public const string GaitF = "gaitf";
    public const string GClose = "gclose";
    public const string GField = "gfield";
    public const string GSky = "gsky";
    public const string GTop = "gtop";
    public const string MBase = "mbase";
    public const string MVista = "mvista";
    public const string PGround = "pground";
    public const string POver = "pover";
    public const string PTuft = "ptuft";
    public const string TDetB = "tdetB";
    public const string TDetT = "tdetT";
    public const string TDuel = "tduel";
    public const string TStride = "tstride";
    public const string TVista = "tvista";
  }

  private void QueueCapture(string path, string tag, CamMode camMode, System.Action after = null)
  {
    if (this._pendingPath != null || this._capturing)
    {
      return;
    }

    this._pendingPath = path; this._pendingTag = tag; this._camMode = camMode;
    this._afterCapture = after;
  }

  // Captures write under res://proof/, which is not tracked in git;
  // create the folder on demand so a fresh clone can capture.
  private static void EnsureSaveDir(string path)
  {
    var dir = ProjectSettings.GlobalizePath(path.GetBaseDir());
    if (dir.Length > 0)
    {
      DirAccess.MakeDirRecursiveAbsolute(dir);
    }
  }

  private async void DoCapture()
  {
    this._capturing = true;
    this._frozen = true;
    string path = this._pendingPath, tag = this._pendingTag;
    this._pendingPath = null;
    this.UpdateCamera(0f);
    await this.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    var img = this.GetViewport().GetTexture().GetImage();
    EnsureSaveDir(path);
    var err = img.SavePng(path);
    Dbg.Print($"DUEL captured {path} ({tag}) size={img.GetSize()} err={err}");
    this._frozen = false;
    this._capturing = false;
    var after = this._afterCapture;
    this._afterCapture = null;
    after?.Invoke();
    if (this._phase == DuelPhase.Captures && tag == CaptureTags.Rider)
    {
      Dbg.Print($"DUEL DONE playerSteps={this._player.Steps} enemySteps={this._enemy.Steps} enemyMaxSlide={this._enemy.MaxSlide:F4} playerMaxSlide={this._player.MaxSlide:F4}");
      // One frame from the interactive chase camera (the player's
      // actual view) before quitting - framing proof for cam changes.
      this.QueueCapture(DuelChaseCapture, CaptureTags.Chase, CamMode.Chase);
    }
    else if (tag == CaptureTags.Chase)
    {
      this.GetTree().Quit();
    }
  }

  private void UpdateCamera(float delta)
  {
    // The hero light rides over the player rider so it
    // reads in any shadow (owner, 2026-11-11).
    if (this._heroLight != null && this._player != null)
    {
      this._heroLight.GlobalPosition = this._player.RootPos + new Vector3(0f, HeroLightHeight, 0f);
    }

    if (this._camMode == CamMode.MissionBase)
    {
      var pad = Main.SpawnPads[0];
      var c = new Vector3(pad.X, Main.HeightAt(pad.X, pad.Y), pad.Y);
      this._cam.GlobalPosition = c + MissionBaseCamOffset;
      this._cam.LookAt(c + MissionBaseLookOffset, Vector3.Up);
    }
    else if (this._camMode == CamMode.MissionVista)
    {
      var pad = Main.SpawnPads[0];
      float gy = Main.HeightAt(pad.X, pad.Y);
      this._cam.GlobalPosition = new Vector3(pad.X + MissionVistaOffsetX, gy + MissionVistaHeight, pad.Y + MissionVistaOffsetZ);
      this._cam.LookAt(new Vector3(MissionVistaLookX, Main.HeightAt(MissionVistaLookX, MissionVistaLookZ) + MissionVistaLookHeight, MissionVistaLookZ), Vector3.Up);
    }
    else if (this._camMode == CamMode.SlopeVista)
    {
      // Grazing view across the center relief (dome flank
      // + ridge line): the owner's streaked-mountains
      // screenshot staged head-on (--slope-shot).
      this._cam.GlobalPosition = new Vector3(SlopeVistaCamX, Main.HeightAt(SlopeVistaCamX, SlopeVistaCamZ) + SlopeVistaCamHeight, SlopeVistaCamZ);
      this._cam.LookAt(new Vector3(SlopeVistaLookX, Main.HeightAt(SlopeVistaLookX, SlopeVistaLookZ) + SlopeVistaLookHeight, SlopeVistaLookZ), Vector3.Up);
    }
    else if (this._camMode == CamMode.Stone)
    {
      // The mountain-stone check (--stone-shot): the
      // probe picks a broad steep face and parks the
      // camera off it, looking at the face.
      this._cam.GlobalPosition = this._stoneCamPos;
      this._cam.LookAt(this._stoneCamTarget, Vector3.Up);
    }
    else if (this._camMode == CamMode.SlopeClose)
    {
      // Low on the dome flank, looking up it: planar-UV
      // stretch and normal streaks show here if anywhere.
      this._cam.GlobalPosition = new Vector3(SlopeCloseCamX, Main.HeightAt(SlopeCloseCamX, SlopeCloseCamZ) + SlopeCloseCamHeight, SlopeCloseCamZ);
      this._cam.LookAt(new Vector3(SlopeCloseLookX, Main.HeightAt(SlopeCloseLookX, SlopeCloseLookZ) + SlopeCloseLookHeight, SlopeCloseLookZ), Vector3.Up);
    }
    else if (this._camMode == CamMode.CanyonVista)
    {
      // From above the home valley's east side, looking
      // northwest across the bowl to the pad-1 pass: the
      // rim ring and the road notch through it should
      // fill the middle distance (AOT_TERRATEST=10).
      var pad = Main.SpawnPads[0];
      this._cam.GlobalPosition = new Vector3(pad.X + CanyonVistaOffsetX, CanyonVistaCamHeight, pad.Y + CanyonVistaOffsetZ);
      this._cam.LookAt(CanyonVistaLookPos, Vector3.Up);
    }
    else if (this._camMode == CamMode.BlockedCam)
    {
      // Beside the blocked-walk test rider, at hip
      // height: the marching pose reads here - feet
      // under the body, normal cycle (AOT_TERRATEST=12).
      var p = this._player.RootPos;
      this._cam.GlobalPosition = p + BlockedCamOffset;
      this._cam.LookAt(p + BlockedCamLookOffset, Vector3.Up);
    }
    else if (this._camMode == CamMode.Homecam)
    {
      // High south-west of the HOME pad, looking across
      // its valley: since round 3 the home valley must
      // read as a FLAT ground-level city - no platforms,
      // ramps or island (AOT_TERRATEST=11).
      var hp = Main.SpawnPads[0];
      this._cam.GlobalPosition = new Vector3(hp.X + HomecamOffsetX, HomecamHeight, hp.Y + HomecamOffsetZ);
      this._cam.LookAt(new Vector3(hp.X + HomecamLookOffsetX, HomecamLookHeight, hp.Y + HomecamLookOffsetZ), Vector3.Up);
    }
    else if (this._camMode == CamMode.Passcam)
    {
      // On the road at the home->pad2 saddle, rider
      // height, looking along the road through the arch
      // (owner round 7: the pass floor must not wear
      // the mountain's grass/stone mix - the mix ends
      // where the slope meets the ground).
      var a = Main.SpawnPads[0]; var b = Main.SpawnPads[1];
      var mid = (a + b) * 0.5f;
      var dirv = new Vector3(b.X - a.X, 0f, b.Y - a.Y).Normalized();
      var eye = new Vector3(mid.X, Main.HeightAt(mid.X, mid.Y), mid.Y) - dirv * PasscamBackDistance + Vector3.Up * PasscamEyeHeight;
      this._cam.GlobalPosition = eye;
      this._cam.LookAt(eye + dirv * PasscamLookDistance + Vector3.Up * PasscamLookHeight, Vector3.Up);
    }
    else if (this._camMode == CamMode.Gatesky)
    {
      // Gate City sky check (owner: 'Saturn ring still
      // broken'): from the home base, pitched up at the
      // ringed planet's azimuth - the planet rides ~124
      // degrees clockwise from panorama centre; in the
      // mission world that bearing lands past pad 2, so
      // aim from pad 0 toward pad 2 and tilt up.
      var pad = Main.SpawnPads[0];
      var to = Main.SpawnPads[2] - pad;
      var dirv = new Vector3(to.X, 0f, to.Y).Normalized();
      var eye = new Vector3(pad.X, Main.HeightAt(pad.X, pad.Y) + GateskyEyeHeight, pad.Y);
      this._cam.GlobalPosition = eye;
      this._cam.LookAt(eye + dirv * GateskyLookDistance + Vector3.Up * GateskyLookHeight, Vector3.Up);
    }
    else if (this._camMode == CamMode.EnemyFront)
    {
      // In front of the enemy, on its line of advance: the
      // frontal view (knee stance reads here, not from the side).
      var p = this._enemy.RootPos;
      var toPlayer = new Vector3(this._player.RootPos.X - p.X, 0f, this._player.RootPos.Z - p.Z).Normalized();
      this._cam.GlobalPosition = p + toPlayer * EnemyFrontDistance + EnemyFrontCamOffset;
      this._cam.LookAt(p + EnemyFrontLookOffset, Vector3.Up);
    }
    else if (this._camMode == CamMode.EnemySide)
    {
      var p = this._enemy.RootPos;
      this._cam.GlobalPosition = p + EnemySideCamOffset;
      this._cam.LookAt(p + EnemySideLookOffset, Vector3.Up);
    }
    else if (this._camMode == CamMode.Wide)
    {
      var mid = (this._player.RootPos + this._enemy.RootPos) / 2f;
      this._cam.GlobalPosition = mid + WideCamOffset;
      this._cam.LookAt(mid + WideLookOffset, Vector3.Up);
    }
    else if (this._camMode == CamMode.Twist)
    {
      var p = this._player.RootPos;
      this._cam.GlobalPosition = p + TwistCamOffset;
      this._cam.LookAt(p + TwistLookOffset, Vector3.Up);
    }
    else if (this._camMode == CamMode.Prop)
    {
      var mid = (this._player.RootPos + PropSpotOffset) / 2f;
      this._cam.GlobalPosition = mid + PropCamOffset;
      this._cam.LookAt(mid + PropLookOffset, Vector3.Up);
    }
    else if (this._camMode == CamMode.WpnSide || this._camMode == CamMode.WpnRockets)
    {
      var mid = (this._player.RootPos + this._enemy.RootPos) / 2f;
      float h = this._camMode == CamMode.WpnRockets ? WpnRocketsCamHeight : WpnSideCamHeight;
      this._cam.GlobalPosition = mid + new Vector3(WpnSideCamOffsetX, h, WpnSideCamOffsetZ);
      this._cam.LookAt(mid + WpnSideLookOffset, Vector3.Up);
    }
    else if (this._camMode == CamMode.WpnBlocked)
    {
      this._cam.GlobalPosition = WpnBlockedCamPos;
      this._cam.LookAt(WpnBlockedLookPos, Vector3.Up);
    }
    else if (this._camMode == CamMode.WpnDead)
    {
      // From the player's side (sunlit front), pulled well back.
      var p = this._enemy.RootPos;
      this._cam.GlobalPosition = p + WpnDeadCamOffset;
      this._cam.LookAt(p + WpnDeadLookOffset, Vector3.Up);
    }
    else if (this._camMode == CamMode.WpnHud)
    {
      // Rear three-quarter: player rider in frame, Hector visible past it.
      var torsoYaw = this._player.TorsoYawWorld;
      var fwd = RiderWalker.HeadingVec(torsoYaw);
      var right = new Vector3(-fwd.Z, 0f, fwd.X);
      this._cam.GlobalPosition = this._player.RootPos - fwd * WpnHudBackDistance + right * WpnHudSideOffset + Vector3.Up * WpnHudCamHeight;
      this._cam.LookAt(this._player.RootPos + Vector3.Up * WpnHudLookHeight + fwd * WpnHudLookForward, Vector3.Up);
    }
    else if (this._camMode == CamMode.RiderSide)
    {
      var mid = (this._player.RootPos + this._enemy.RootPos) / 2f;
      this._cam.GlobalPosition = mid + RiderSideCamOffset;
      this._cam.LookAt(mid + RiderSideLookOffset, Vector3.Up);
    }
    else if (this._camMode == CamMode.BrkPlayer)
    {
      var p = this._player.RootPos;
      this._cam.GlobalPosition = p + BrkPlayerCamOffset;
      this._cam.LookAt(p + BrkPlayerLookOffset, Vector3.Up);
    }
    else if (this._camMode == CamMode.BrkTarget)
    {
      var p = this._enemy.RootPos;
      this._cam.GlobalPosition = p + BrkTargetCamOffset;
      this._cam.LookAt(p + BrkTargetLookOffset, Vector3.Up);
    }
    else if (this._camMode == CamMode.BrkDebris)
    {
      this._cam.GlobalPosition = this._camFocus + BrkDebrisCamOffset;
      this._cam.LookAt(this._camFocus + Vector3.Up * BrkDebrisLookHeight, Vector3.Up);
    }
    else if (this._camMode == CamMode.DetailBuilding)
    {
      // Close on the (22,8) building: brick courses + glass panes.
      var bp = new Vector3(DetailBuildingX, Main.HeightAt(DetailBuildingX, DetailBuildingZ), DetailBuildingZ);
      this._cam.GlobalPosition = bp + DetailBuildingCamOffset;
      this._cam.LookAt(bp + DetailBuildingLookOffset, Vector3.Up);
    }
    else if (this._camMode == CamMode.DetailTree)
    {
      // Close on the hand-placed tree at (-12,-8): branches + leaves.
      var tp = new Vector3(DetailTreeX, Main.HeightAt(DetailTreeX, DetailTreeZ), DetailTreeZ);
      this._cam.GlobalPosition = tp + DetailTreeCamOffset;
      this._cam.LookAt(tp + DetailTreeLookOffset, Vector3.Up);
    }
    else if (this._camMode == CamMode.Vista)
    {
      this._cam.GlobalPosition = VistaCamPos;
      this._cam.LookAt(VistaLookPos, Vector3.Up);
    }
    else if (this._camMode == CamMode.PhotoOver)
    {
      // Photoreal probe money shot: elevated over the arena,
      // both riders in frame, mountains behind.
      this._cam.GlobalPosition = PhotoOverCamPos;
      this._cam.LookAt(PhotoOverLookPos, Vector3.Up);
    }
    else if (this._camMode == CamMode.PhotoTuft)
    {
      // Debug: one tuft cluster at arm's length.
      this._cam.GlobalPosition = new Vector3(PhotoTuftCamX, Main.HeightAt(PhotoTuftCamX, PhotoTuftCamZ) + PhotoTuftCamHeight, PhotoTuftCamZ);
      this._cam.LookAt(new Vector3(PhotoTuftLookX, Main.HeightAt(PhotoTuftLookX, PhotoTuftLookZ) + PhotoTuftLookHeight, PhotoTuftLookZ), Vector3.Up);
    }
    else if (this._camMode == CamMode.GrassClose)
    {
      this._cam.GlobalPosition = this._grassPos + GrassCloseCamOffset;
      this._cam.LookAt(this._grassPos + Vector3.Up * GrassCloseLookHeight, Vector3.Up);
    }
    else if (this._camMode == CamMode.GrassTop)
    {
      this._cam.GlobalPosition = this._grassPos + GrassTopCamOffset;
      this._cam.LookAt(this._grassPos, Vector3.Up);
    }
    else if (this._camMode == CamMode.GrassField)
    {
      this._cam.GlobalPosition = this._grassPos + GrassFieldCamOffset;
      this._cam.LookAt(this._grassPos, Vector3.Up);
    }
    else if (this._camMode == CamMode.PhotoGround)
    {
      // Ground level by the hand-placed trees, looking across
      // the road at the Enyo.
      var tp = new Vector3(PhotoGroundX, Main.HeightAt(PhotoGroundX, PhotoGroundSampleZ) + PhotoGroundEyeHeight, PhotoGroundZ);
      this._cam.GlobalPosition = tp;
      this._cam.LookAt(this._player.RootPos + Vector3.Up * PhotoGroundLookHeight, Vector3.Up);
    }
    else if (this._camMode == CamMode.SlopeSide)
    {
      var p = this._player.RootPos;
      var fwd = RiderWalker.HeadingVec(this._player.LegsYaw);
      var right = new Vector3(-fwd.Z, 0f, fwd.X);
      this._cam.GlobalPosition = p - right * SlopeSideDistance + Vector3.Up * SlopeSideCamHeight;
      this._cam.LookAt(p + Vector3.Up * SlopeSideLookHeight, Vector3.Up);
    }
    else if (this._camMode == CamMode.GroundDuel)
    {
      var p = this._player.RootPos;
      var fwd = RiderWalker.HeadingVec(this._player.LegsYaw);
      this._cam.GlobalPosition = p - fwd * GroundDuelDistance + Vector3.Up * GroundDuelCamHeight;
      this._cam.LookAt(this._enemy.RootPos + Vector3.Up * GroundDuelLookHeight, Vector3.Up);
    }
    else // chase (interactive)
    {
      // Boom framing (round 28, owner: "the player's rider
      // should be always visible"): the camera rides a boom
      // ON the aim axis - anchored above the rider's head,
      // positioned behind the anchor along the aim direction.
      // The boom rotates rigidly with the aim pitch, so the
      // rider's framing is the same at EVERY pitch: it hangs
      // below the anchor in the lower-center of the frame,
      // head clear of the crosshair, and can never leave the
      // frame. (Round 27's fixed high perch pitched the view
      // along the mouse but left the camera in place, so
      // looking up dropped the rider out of the bottom of
      // the frame - the owner's report.) Centered, no
      // sideways offset (owner, round 12); distances scale
      // with the chassis height, as before.
      float k = this._playerSpec.Height / ReferenceChassisHeight;
      var aimDir = this.AimDirWorld();
      // The anchor rides higher as |pitch| grows (up to
      // +0.22 heights at the +-1.35 cap): the torso pitches
      // with the aim, and at the extremes its head would
      // otherwise crowd the crosshair the boom is centered
      // on. Level framing (ramp ~0) is untouched.
      float upRamp = Mathf.Abs(aimDir.Y) / AimPitchCapSin;
      var anchor = this._player.RootPos + Vector3.Up * (this._playerSpec.Height * (AnchorHeightFactor + AnchorPitchLiftFactor * upRamp));
      // Boom length: the full 26k while it fits. A steep
      // up-pitch swings the boom down behind the rider;
      // where the full boom would sink it into the ground,
      // shorten the boom instead of lifting the camera off
      // the axis - the camera stays ON the aim line (the
      // rider centered on it, seen from below) down to the
      // last length that keeps it above the terrain under
      // the anchor. The floor clamp below stays as a
      // backstop for sloping ground behind the rider.
      float boom = BoomLength * k;
      if (aimDir.Y > BoomShortenPitchMin)
      {
        float clearance = anchor.Y - (Main.HeightAt(anchor.X, anchor.Z) + CameraFloorClearance);
        boom = Mathf.Min(boom, Mathf.Max(BoomMinLength * k, clearance / aimDir.Y));
      }
      // NOTHING blocks the camera (owner, 2026-11-15, two
      // rulings the same evening): the camera tracks only
      // the player's rider on a fixed boom. First riders
      // stopped counting as blockers (a teammate crossing
      // behind collapsed the boom to first person); then
      // buildings too - walking the Atlantis pass arch
      // pulled the boom in the same way. The occlusion
      // pull-in/lift that used to live here is REMOVED, not
      // bypassed: no ray moves this camera. What remains is
      // pure geometry - the steep-pitch shortening above
      // and the floor clamp below, which only keeps the
      // camera out of the ground at its own position. A
      // building or arch on the boom line is simply passed
      // through; single-sided geometry renders see-through
      // from inside, so the rider stays in frame.
      var target = anchor - aimDir * boom;
      float floorY = Main.HeightAt(target.X, target.Z) + CameraFloorClearance;
      if (target.Y < floorY)
      {
        target.Y = floorY;
      }
      // No position smoothing (owner, round 29): the
      // camera sits exactly on the boom target every
      // frame. The exponential glide this replaces (rate
      // 6/s) trailed every mouse flick - measured by
      // --lag-check on the old code: one frame after a
      // hard flick the camera was still 24.6u short of
      // its target and needed 62 frames (~1s) to settle,
      // so the world kept panning after the mouse had
      // stopped, which read as the (fixed) crosshair
      // still moving - jittery aim. The aim state is set
      // directly by the input events; the camera now
      // is too.
      this._cam.GlobalPosition = target;
      // Center-crosshair aiming (owner's pick, round 27):
      // the camera looks along the aim direction itself
      // (AimDirWorld - the mouse's yaw/pitch), so the fixed
      // center crosshair, the rendered view and the weapons'
      // convergence ray are one and the same line at every
      // range and angle. This replaces the reticle servo of
      // rounds 25-26: with no free reticle point to chase,
      // nothing pins, saturates or inverts, and the
      // close-steep parallax dead zone measured after round
      // 26 (a top-edge target unpointable inside ~18u)
      // cannot occur. Pitch is capped at +-1.35 rad, well
      // clear of the vertical LookAt singularity.
      var lookTarget = this._cam.GlobalPosition + aimDir;
      if (this._cam.GlobalPosition.DistanceSquaredTo(lookTarget) > MinLookStepSq)
      {
        this._cam.LookAt(lookTarget, Vector3.Up);
      }
      // Sniper view (owner, 2026-11-22, MechWarrior-style):
      // RMB toggles a narrow 16-degree FOV on the same boom -
      // the aim line is FOV-independent, so the crosshair
      // stays true; the ease keeps the zoom from snapping.
      if (delta > 0f)
      {
        float fovT = this._sniper ? SniperFov : BaseFov;
        this._cam.Fov = Mathf.Lerp(this._cam.Fov, fovT, 1f - Mathf.Exp(-FovEaseRate * delta));
      }
      if (delta > 0f && this._shake > ShakeMinAmplitude)
      {
        this._cam.GlobalPosition += new Vector3(
                    (float)this._rng.NextDouble() * 2f - 1f,
                    (float)this._rng.NextDouble() * (ShakeVerticalJitter * 2f) - ShakeVerticalJitter,
                    (float)this._rng.NextDouble() * 2f - 1f) * (this._shake * ShakeAmplitude);
      }
    }
  }

  // Substep the walkers so their 0.1s accumulator clamp never drops
  // simulation time on slow frames (llvmpipe renders this scene at a
  // few FPS): gait, projectiles and cooldowns then share one clock at
  // any frame rate. At 60 FPS this is a single call, unchanged.
}
