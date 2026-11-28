using Godot;

namespace Titans;

// Titans module: RiderLab glue: construction from specs, selection, loadout application.
// Split from the single-file build (round 22 cleanup); the class
// stays one partial class - see Titans.cs for the field block.
public partial class Titans
{
  // Viewport position of a slot's part on the player's rider -
  // the lab's paperdoll dots are drawn there, so the mapping
  // survives any window size or aspect (owner). Null when the
  // chassis lacks the part or it is off-screen: the lab falls
  // back to its fractional anchors then.
  private const float LabAnchorSlack = 40f; // px past the viewport edge an anchor may sit and still anchor a panel
  private const float PelvisHeightFraction = 0.45f; // the paperdoll anchors at this fraction of the rider's height (the pelvis)
                                                    // Lab stand factory: floor plate and sky dome.
  private const float LabFloorSize = 160f;
  private const float LabPlateRoughness = 0.75f, LabPlateMetallic = 0.15f;
  private const float LabPlateUvScale = 4f, LabPlateLift = 0.02f;
  private const float LabDomeRadius = 120f, LabDomeLift = 2f;
  private const int LabDomeSegments = 32, LabDomeRings = 16;
  private const int LabLoadLayer = 5; // the lab loading cover's canvas layer (the flow's loading layer, same value)
  private const int StoryRosterMaxMembers = 4; // AddStoryMember's roster cap
                                               // Deck plate painting recipe (PlateTexture).
  private static readonly Color PlateBaseColor = new(0.25f, 0.27f, 0.31f);
  private static readonly Color PlateSeamColor = new(0.10f, 0.11f, 0.13f);
  private static readonly Color PlateBevelColor = new(0.36f, 0.39f, 0.44f);
  private static readonly Color PlateRivetColor = new(0.42f, 0.46f, 0.52f);
  private const float PlateNoiseAmplitude = 0.05f;   // per-pixel brushed-noise swing
  private const float PlateBrushFrequency = 0.9f;    // the horizontal brush sine's frequency
  private const float PlateBrushAmplitude = 0.008f;  // the brush sine's swing
  private const int RivetRadiusSquared = 4;          // a rivet's disc, in squared pixel distance
  private const int RivetEdgeSquared = 3;            // squared distance where a rivet's rim turns to seam colour
                                                     // Lab dome gradient (SkyGradientTexture): the horizon band
                                                     // is shared by both stops either side of the peak (symmetric
                                                     // by design, so the sphere's V direction cannot flip it).
  private static readonly Color LabSkyHorizonBand = new(0.075f, 0.092f, 0.120f);
  private static readonly (float At, Color C)[] LabSkyGradientStops =
  {
        (0.00f, new Color(0.030f, 0.038f, 0.052f)),
        (0.38f, LabSkyHorizonBand),
        (0.50f, new Color(0.135f, 0.160f, 0.200f)),
        (0.62f, LabSkyHorizonBand),
        (1.00f, new Color(0.035f, 0.045f, 0.062f))
    };

  private Vector2? ProjectSlotAnchor(PartId slotId)
  {
    if (this._partsPlayer == null || this._cam == null || !this._partsPlayer.Has(slotId))
    {
      return null;
    }

    var w = this._partsPlayer.PartCenterWorld(slotId);
    if (this._cam.IsPositionBehind(w))
    {
      return null;
    }

    var sp = this._cam.UnprojectPosition(w);
    var vp = this.GetViewport().GetVisibleRect().Size;
    if (sp.X < -LabAnchorSlack || sp.Y < -LabAnchorSlack || sp.X > vp.X + LabAnchorSlack || sp.Y > vp.Y + LabAnchorSlack)
    {
      return null;
    }

    return sp;
  }

  // Viewport position of the rider's pelvis - the paperdoll's
  // radial center for panel placement. The slot anchors all
  // sit in the upper body, so their own centroid rides too
  // high and the panels bunch at the top; the pelvis is the
  // body's real middle.
  private Vector2? ProjectBodyCenter()
  {
    if (this._player == null || this._cam == null)
    {
      return null;
    }

    var w = this._player.RootPos + Vector3.Up * (this._playerSpec.Height * PelvisHeightFraction);
    if (this._cam.IsPositionBehind(w))
    {
      return null;
    }

    var sp = this._cam.UnprojectPosition(w);
    var vp = this.GetViewport().GetVisibleRect().Size;
    if (sp.X < -LabAnchorSlack || sp.Y < -LabAnchorSlack || sp.X > vp.X + LabAnchorSlack || sp.Y > vp.Y + LabAnchorSlack)
    {
      return null;
    }

    return sp;
  }

  // The lab's loading cover (owner): styled like the front
  // door's loading screen, it stays over the lab while the
  // photo-mode warmup sweep compiles shaders and settles the
  // world behind it, so the lab never flickers through its
  // translucent panels mid-load. _Process lifts it when the
  // sweep completes (_warmPose reaches 4).
  private void BuildLabLoadOverlay()
  {
    this._labLoad = new CanvasLayer { Layer = LabLoadLayer, Visible = this._photo };
    this.AddChild(this._labLoad);
    var rect = new ColorRect { Color = FlowUi.LoadingBg };
    rect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    this._labLoad.AddChild(rect);
    // The loading art replaces the old text screen (owner),
    // same as the front door's cover.
    this._labLoadArt = FlowUi.MakeLoadingArt();
    this._labLoad.AddChild(this._labLoadArt);
  }

  private void OpenLab()
  {
    if (this._lab != null) { this._lab.QueueFree(); this._lab = null; }
    var lab = new RiderLab
    {
      Spec = this._playerSpec,
      Loadout = this._loadout,
      PlayerSpecId = this._playerSpec.Id,
      EnemySpecId = this._enemySpec.Id,
      AnchorProjector = this.ProjectSlotAnchor,
      CenterProjector = this.ProjectBodyCenter
    };
    // Story (owner, 2026-11-15): the lab no longer hosts
    // a team selector - the mission team is formed on the
    // flow's team screen before the lab, and the lab
    // prepares rider 1 (the roster's slot 0, pinned by
    // EnterLab) exactly as the Tournament lab prepares
    // team 1's first rider.
    if (WorldConfig.IsMission)
    {
      TeamRoster.EnsureStory();
    }

    this._lab = lab;
    lab.OnStart = () =>
    {
      this.ShowWorld(); // the lab stand comes down, the fight world returns
      this.RebuildAlliesForDeploy(); // the roster may have changed in the lab (owner's report)
      this.ApplyLoadout();
      lab.QueueFree();
      if (this._lab == lab)
      {
        this._lab = null;
      }

      this._labOpen = false;
      if (this._labLoad != null)
      {
        this._labLoad.Visible = false;
      }

      this._cross.Visible = true; // the target cursor returns on deploy (owner: it must not show in the lab)
      if (this._labLight != null)
      {
        this._labLight.Visible = false;
      }

      this.SetHudPanelsVisible(true);
      this._radar.Visible = true;
      Input.MouseMode = Input.MouseModeEnum.Captured;
      Dbg.Print("LAB deploy: duel started with the lab loadout");
    };
    lab.OnSelectionChanged = () => this.SetRiderSelection(lab.PlayerSpecId, lab.EnemySpecId);
    // Showroom stance (owner, 2026-11-25): the lab rider
    // does not turn and stands straight. Whatever state
    // the walker arrived in - a torso twist left over
    // from aiming, a staggered gait pose - the lab pins
    // the neutral stance: torso untwisted, feet
    // re-planted at their home slots, facing unchanged.
    // OpenLab re-runs on every rider cycle, so the pin
    // covers swaps too.
    this._player.TorsoTwist = 0f;
    this._player.Teleport(this._player.RootPos, this._player.LegsYaw);
    this.BuildLabStand();
    // Deck alignment lives in the lab camera block
    // (Titans.cs): the deck's local X is kept parallel
    // to the camera's right vector every frame, so the
    // transverse seams are level with the horizon by
    // construction on every entry path. (An OpenLab-time
    // yaw computed from the rider's facing still read
    // rotated in the owner's play - his entry path
    // differs from the harness's, so the deck now
    // follows the camera itself, not the rider.)
    this._cross.Visible = false; // no target cursor in the lab view (owner)
    this._uiLayer.AddChild(lab);
  }

  // The lab's own view (owner, 2026-11-14: "It is better
  // if Lab screen will have its own view / stand"): the
  // fight world is hidden for the duration of the lab and
  // the rider presents on a showroom stand - a wide
  // textured steel floor under his feet inside a gradient
  // dome - the same on every map, no terrain behind him.
  // (Square, not an oval plate: owner, 2026-11-15; then
  // stretched past the frame and given a basic texture
  // and light, so the floor reads as a horizon line.)
  // The rider never moves or turns; Deploy restores the
  // world's visibility and frees the stand. (This
  // supersedes the same-day lab facing turn, which still
  // left the arena rim in frame on Hyle.)
  private Node3D _labStand;
  private MeshInstance3D _labDeck;
  private readonly System.Collections.Generic.List<Node3D> _labHidden = new();

  private void BuildLabStand()
  {
    if (this._player == null)
    {
      return;
    }
    // One stand per lab session (owner's report, 2026-11-15:
    // the stand leaked into the game world): OpenLab runs
    // again on every rider cycle and team-slot switch while
    // the lab is open, and each rebuild used to stack a
    // fresh stand over the last - the deploy then freed
    // only the newest and restored the older ones' visibility
    // with the rest of the world, leaving their deck and dome
    // standing on the map. The stand is already up and the
    // world already hidden on a re-entry: reuse it.
    if (this._labStand != null)
    {
      return;
    }

    var c = this._player.RootPos;
    this._labStand = new Node3D { Name = "LabStand" };
    this.AddChild(this._labStand);
    // The floor: a 160u steel deck, far wider than the
    // frame, so its far edge is the horizon. A PlaneMesh,
    // not a hand-built ArrayMesh: in this scene a built
    // mesh's lit face never lit (three captures, with and
    // without UVs, either culling), while primitive
    // meshes take the sun and shadow normally. Fog-exempt
    // like the dome: the scene's warm fog washed the
    // first plates brown; a studio stand keeps its own
    // colour.
    var disc = new MeshInstance3D
    {
      Mesh = new PlaneMesh { Size = new Vector2(LabFloorSize, LabFloorSize) },
      MaterialOverride = new StandardMaterial3D
      {
        AlbedoTexture = PlateTexture(),
        Roughness = LabPlateRoughness,
        Metallic = LabPlateMetallic,
        DisableFog = true,
        // Tiled 4x (owner, 2026-11-25): one stretch of
        // the plate sheet over 160u read overstretched
        // and cheap - tiled, the plates are 2.5u and
        // the texel density is 4x (Uv1Scale alone
        // tiles here, as on the PhotoEnv materials).
        Uv1Scale = new Vector3(LabPlateUvScale, LabPlateUvScale, 1f)
      },
      Position = new Vector3(c.X, c.Y + LabPlateLift, c.Z)
    };
    this._labDeck = disc;
    this._labStand.AddChild(disc);
    var dome = new MeshInstance3D
    {
      Mesh = new SphereMesh { Radius = LabDomeRadius, Height = LabDomeRadius * 2f, RadialSegments = LabDomeSegments, Rings = LabDomeRings },
      MaterialOverride = new StandardMaterial3D
      {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        AlbedoTexture = SkyGradientTexture(),
        DisableFog = true,
        CullMode = BaseMaterial3D.CullModeEnum.Back
      },
      Position = new Vector3(c.X, c.Y + LabDomeLift, c.Z),
      CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
    };
    this._labStand.AddChild(dome);
    // Hide the fight world (terrain, props, other riders,
    // photo dressing); lights, the camera and the player
    // stay. The snapshot restores exactly what was hidden.
    foreach (var child in this.GetChildren())
    {
      if (child is Node3D n3 && n3 != this._player && n3 != this._labStand && n3 != this._cam
          && n3.Name != "LabStand" && n3 is not Light3D && n3.Visible)
      {
        n3.Visible = false;
        this._labHidden.Add(n3);
      }
    }
  }

  private static ImageTexture _standPlateTex;
  // The deck's texture, drawn in code: one steel panel
  // (brushed noise, seam border, bevel line, four rivets)
  // blitted 16x16, so the 160u floor reads as 10u panels.
  private static ImageTexture PlateTexture()
  {
    if (_standPlateTex != null)
    {
      return _standPlateTex;
    }

    const int P = 128, N = 16;
    var panel = Image.CreateEmpty(P, P, false, Image.Format.Rgb8);
    var rng = new System.Random(0x57EE1);
    var baseC = PlateBaseColor;
    for (int y = 0; y < P; y++)
    {
      for (int x = 0; x < P; x++)
      {
        float nse = (float)(rng.NextDouble() - 0.5) * PlateNoiseAmplitude;
        float brush = Mathf.Sin(y * PlateBrushFrequency) * PlateBrushAmplitude;
        panel.SetPixel(x, y, new Color(baseC.R + nse + brush, baseC.G + nse + brush, baseC.B + nse + brush));
      }
    }

    var seam = PlateSeamColor;
    var bevel = PlateBevelColor;
    for (int i = 0; i < P; i++)
    {
      panel.SetPixel(i, 0, seam); panel.SetPixel(i, 1, seam);
      panel.SetPixel(i, P - 1, seam); panel.SetPixel(i, P - 2, seam);
      panel.SetPixel(0, i, seam); panel.SetPixel(1, i, seam);
      panel.SetPixel(P - 1, i, seam); panel.SetPixel(P - 2, i, seam);
      panel.SetPixel(i, 3, bevel); panel.SetPixel(i, P - 4, bevel);
      panel.SetPixel(3, i, bevel); panel.SetPixel(P - 4, i, bevel);
    }
    var rivet = PlateRivetColor;
    foreach (var (rx, ry) in new[] { (9, 9), (P - 10, 9), (9, P - 10), (P - 10, P - 10) })
    {
      for (int y = -2; y <= 2; y++)
      {
        for (int x = -2; x <= 2; x++)
        {
          if (x * x + y * y <= RivetRadiusSquared)
          {
            panel.SetPixel(rx + x, ry + y, x * x + y * y >= RivetEdgeSquared ? seam : rivet);
          }
        }
      }
    }

    var img = Image.CreateEmpty(P * N, P * N, false, Image.Format.Rgb8);
    var full = new Rect2I(0, 0, P, P);
    for (int i = 0; i < N; i++)
    {
      for (int j = 0; j < N; j++)
      {
        img.BlitRect(panel, full, new Vector2I(i * P, j * P));
      }
    }

    img.GenerateMipmaps();
    _standPlateTex = ImageTexture.CreateFromImage(img);
    return _standPlateTex;
  }

  private static ImageTexture _standSkyTex;
  // The backdrop's vertical gradient: dark floor haze, a
  // lighter horizon band, a darkening top. Symmetric
  // enough that the sphere's V direction cannot flip it
  // into nonsense.
  private static ImageTexture SkyGradientTexture()
  {
    if (_standSkyTex != null)
    {
      return _standSkyTex;
    }

    const int H = 256;
    var img = Image.CreateEmpty(2, H, false, Image.Format.Rgb8);
    var stops = LabSkyGradientStops;
    for (int y = 0; y < H; y++)
    {
      float v = (float)y / (H - 1);
      var col = stops[stops.Length - 1].C;
      for (int k = 0; k + 1 < stops.Length; k++)
      {
        if (v >= stops[k].At && v <= stops[k + 1].At)
        {
          float t = (v - stops[k].At) / (stops[k + 1].At - stops[k].At);
          col = stops[k].C.Lerp(stops[k + 1].C, t);
          break;
        }
      }
      img.SetPixel(0, y, col);
      img.SetPixel(1, y, col);
    }
    _standSkyTex = ImageTexture.CreateFromImage(img);
    return _standSkyTex;
  }

  private void ShowWorld()
  {
    foreach (var n in this._labHidden)
    {
      if (IsInstanceValid(n))
      {
        n.Visible = true;
      }
    }

    this._labHidden.Clear();
    // Every stand comes down, not just the tracked one:
    // the lab's view and the game world stay separated
    // (owner, 2026-11-15) even if a rebuild ever stacks
    // a second stand again.
    foreach (var child in this.GetChildren())
    {
      if (child is Node3D sn && sn.Name == "LabStand")
      {
        sn.QueueFree();
      }
    }

    this._labStand = null;
  }

  // ---- rider construction from specs (round 22) ----
  // Everything model-bound comes from the RiderSpec, so the lab
  // can field either chassis on either side.
  private RiderWalker BuildRider(RiderSpec spec, string nodeName, Vector3 pos, float yaw)
  {
    var w = new RiderWalker { Name = nodeName };
    this.AddChild(w);
    w.Init(new RiderWalker.Config
    {
      Label = nodeName.ToUpperInvariant(),
      ModelPath = spec.ModelPath,
      TargetHeight = spec.Height,
      StepTime = spec.StepTime,
      StepThreshold = spec.StepThreshold,
      StepLift = spec.StepLift,
      Accel = spec.Accel,
      CollisionRadius = spec.Radius,
      FlipKneePole = spec.FlipKneePole,
      SagittalKneePole = spec.SagittalKneePole
    }, pos, yaw);
    return w;
  }

  private void RefreshHitCaps()
  {
    this._hit.Riders.RemoveAll(m => m.Id == Roles.Player || m.Id == Roles.Enemy || m.Id == Roles.Foe
            || (m.Id != null && m.Id.StartsWith(Roles.AllyPrefix)));
    this._hit.Riders.Add(new HitModel.RiderCap { Id = Roles.Player, Root = () => this._player.RootPos, Radius = this._playerSpec.Radius, Height = this._playerSpec.Height });
    this._hit.Riders.Add(new HitModel.RiderCap { Id = Roles.Enemy, Root = () => this._enemy.RootPos, Radius = this._enemySpec.Radius, Height = this._enemySpec.Height });
    if (this._foe?.Walker != null)
    {
      this._hit.Riders.Add(new HitModel.RiderCap { Id = Roles.Foe, Root = () => this._foe.Walker.RootPos, Radius = this._foe.Spec.Radius, Height = this._foe.Spec.Height });
    }

    foreach (var a in this._allies)
    {
      this._hit.Riders.Add(new HitModel.RiderCap { Id = a.Role, Root = () => a.Walker.RootPos, Radius = a.Spec.Radius, Height = a.Spec.Height });
    }
  }

  // The foe (free-for-all): the partner chassis - the first
  // entry on the roster's partner-priority list that the
  // enemy slot does not already hold. No chassis names in
  // engine code; the list is roster data.
  private static RiderSpec FfaPartner(RiderSpec enemy)
  {
    foreach (var s in RiderSpecs.FfaPartnerPriority)
    {
      if (s.Id != enemy.Id)
      {
        return s;
      }
    }

    foreach (var s in RiderSpecs.All)
    {
      if (s.Id != enemy.Id)
      {
        return s;
      }
    }

    return enemy;
  }

  private void BuildFoe()
  {
    var spec = this._foeMember?.Spec ?? FfaPartner(this._enemySpec);
    this._foe = new Foe { Spec = spec, Loadout = this._foeMember?.Loadout ?? LoadoutState.DefaultFor(spec) };
    var pad = Main.SpawnPads[2];
    this._foe.Walker = this.BuildRider(spec, "Enemy2", PadPos(pad), SpawnYaw(pad));
    this._foe.Yaw = SpawnYaw(pad);
    this._foe.Parts = new RiderParts(this._foe.Walker, spec.Id);
    this._foe.Parts.OnBreak = (id, c) => this.OnPartBroken(this._foe.Parts, id, c);
    this._foe.Walker.RefreshMaterials();
    this._foe.Hp = spec.MaxHp;
    this._foe.PrevRoot = this._foe.Walker.RootPos;
    var eb = SlotOf(spec, WeaponSlotType.Ballistic);
    this._foe.BallisticSlot = eb;
    this._foe.DefBallistic = eb == null ? null : this._foe.Loadout.FirstDef(eb.Value);
    Dbg.Print($"FFA foe built: {spec.Id} at pad 2, ballistic={this._foe.DefBallistic?.Id}");
  }

  private void RebuildFoe()
  {
    var want = this._foeMember?.Spec ?? FfaPartner(this._enemySpec);
    if (this._foe != null && this._foe.Spec.Id == want.Id)
    {
      return;
    }

    var pos = this._foe?.Walker.RootPos ?? PadPos(Main.SpawnPads[2]);
    float yaw = this._foe?.Walker.LegsYaw ?? SpawnYaw(Main.SpawnPads[2]);
    this._foe?.Walker.QueueFree();
    var spec = want;
    this._foe = new Foe { Spec = spec, Loadout = this._foeMember?.Loadout ?? LoadoutState.DefaultFor(spec) };
    this._foe.Walker = this.BuildRider(spec, "Enemy2", pos, yaw);
    this._foe.Yaw = yaw;
    this._foe.Parts = new RiderParts(this._foe.Walker, spec.Id);
    this._foe.Parts.OnBreak = (id, c) => this.OnPartBroken(this._foe.Parts, id, c);
    this._foe.Walker.RefreshMaterials();
    this._foe.Hp = spec.MaxHp;
    this._foe.PrevRoot = this._foe.Walker.RootPos;
    var eb = SlotOf(spec, WeaponSlotType.Ballistic);
    this._foe.BallisticSlot = eb;
    this._foe.DefBallistic = eb == null ? null : this._foe.Loadout.FirstDef(eb.Value);
    this.RefreshHitCaps();
  }

  private void RebuildRider(bool playerSide)
  {
    if (playerSide)
    {
      var pos = this._player.RootPos; float yaw = this._player.LegsYaw;
      this._player.QueueFree();
      this._player = this.BuildRider(this._playerSpec, "Player", pos, yaw);
      this._partsPlayer = new RiderParts(this._player, this._playerSpec.Id);
      this._partsPlayer.OnBreak = (id, c) => this.OnPartBroken(this._partsPlayer, id, c);
      this._player.RefreshMaterials();
      this._hpPlayer = this._playerSpec.MaxHp; this._playerDead = false;
      this._prevPlayerRoot = this._player.RootPos;
    }
    else
    {
      var pos = this._enemy.RootPos; float yaw = this._enemy.LegsYaw;
      this._enemy.QueueFree();
      this._enemy = this.BuildRider(this._enemySpec, "Enemy", pos, yaw);
      this._partsEnemy = new RiderParts(this._enemy, this._enemySpec.Id);
      this._partsEnemy.OnBreak = (id, c) => this.OnPartBroken(this._partsEnemy, id, c);
      this._enemy.RefreshMaterials();
      this._hpEnemy = this._enemySpec.MaxHp; this._enemyDead = false;
    }
    this.RefreshHitCaps();
    this.ApplyLoadout();
  }

  // Lab rider selection (round 22): swap either side's chassis.
  // The loadout resets to the new chassis's default fit; the lab
  // UI is recreated against the new spec.
  public void SetRiderSelection(RiderId playerSpecId, RiderId enemySpecId)
  {
    bool pChanged = playerSpecId != this._playerSpec.Id;
    bool eChanged = enemySpecId != this._enemySpec.Id;
    if (!pChanged && !eChanged)
    {
      return;
    }

    if (pChanged)
    {
      this._playerSpec = RiderSpecs.ById(playerSpecId);
      this._loadout = LoadoutState.DefaultFor(this._playerSpec);
      this.RebuildRider(true);
      // In the Story team lab the change belongs to the
      // edited roster slot (the fresh default loadout
      // replaces the member's, as it replaces the lone
      // player's today).
      if (WorldConfig.IsMission && TeamRoster.Story.Count > 0)
      {
        var m = TeamRoster.Story[TeamRoster.StorySlot];
        m.Spec = this._playerSpec;
        m.Loadout = this._loadout;
      }
      else if (this._bracketActive && TeamRoster.Tourney.Count > 0)
      {
        // Tournament: the lab prepares team 1's first
        // rider - a chassis change re-points its slot.
        var m = TeamRoster.Tourney[0][0];
        m.Spec = this._playerSpec;
        m.Loadout = this._loadout;
      }
    }
    if (eChanged)
    {
      this._enemySpec = RiderSpecs.ById(enemySpecId);
      this._enemyLoadout = LoadoutState.DefaultFor(this._enemySpec);
      this.RebuildRider(false);
      if (this._foe != null)
      {
        this.RebuildFoe(); // the FFA partner re-picks from the priority data
      }
    }
    Dbg.Print($"LAB selection player={this._playerSpec.Id} enemy={this._enemySpec.Id}");
    if (this._lab != null)
    {
      Callable.From(() => { if (this._labOpen) { this.OpenLab(); } }).CallDeferred();
    }
  }

  // ---- Story roster growth (deploy-test path) ----
  // The mission team itself is formed on the flow's team
  // screen (owner, 2026-11-15); this stays as the roster
  // operation the deploy test drives to prove lab-era
  // roster changes still take the field at deploy.
  private void AddStoryMember()
  {
    if (TeamRoster.Story.Count >= StoryRosterMaxMembers)
    {
      return;
    }

    var last = TeamRoster.Story[TeamRoster.Story.Count - 1].Spec;
    int idx = 0;
    for (int i = 0; i < RiderSpecs.All.Length; i++)
    {
      if (RiderSpecs.All[i].Id == last.Id)
      {
        idx = i;
      }
    }

    var spec = RiderSpecs.All[(idx + 1) % RiderSpecs.All.Length];
    TeamRoster.Story.Add(TeamRoster.Make(spec));
  }

  private static PartId? SlotOf(RiderSpec spec, WeaponSlotType type, int n = 0)
  {
    foreach (var s in spec.Slots)
    {
      if (s.Type == type && n-- == 0)
      {
        return s.Id;
      }
    }

    return null;
  }

  private void ApplyLoadout()
  {
    // Jump jets (owner): effective only when the chassis can
    // mount them AND the loadout has them fitted.
    this._player.JumpJets = this._playerSpec.JumpCapable && this._loadout.JumpJets;
    this._enemy.JumpJets = this._enemySpec.JumpCapable;
    // Firing groups (multi-fit): every weapon fitted in a slot
    // of the matching type, in spec slot order. The single
    // _def* fields stay as each group's representative (the
    // slowest-cooldown weapon) for HUD bars and labels.
    this._gunsEnergy.Clear(); this._gunsBallistic.Clear(); this._podGunsL.Clear(); this._podGunsR.Clear();
    this._groups.Clear();
    int missileIdx = 0;
    foreach (var slot in this._playerSpec.Slots)
    {
      var path = slot.Type == WeaponSlotType.Energy ? FirePath.Energy
          : slot.Type == WeaponSlotType.Ballistic ? FirePath.Cannon
          : missileIdx++ == 0 ? FirePath.PodL : FirePath.PodR; // missile slots in order: left pod, right pod
      var bucket = path == FirePath.Energy ? this._gunsEnergy : path == FirePath.Cannon ? this._gunsBallistic
          : path == FirePath.PodL ? this._podGunsL : this._podGunsR;
      var fitted = this._loadout.WeaponsIn(slot.Id);
      for (int fi = 0; fi < fitted.Count; fi++)
      {
        var def = WeaponCatalog.ById(fitted[fi]);
        if (def == null)
        {
          continue;
        }

        bucket.Add((slot.Id, def));
        // The player's lab-assigned firing group for this
        // exact fitted weapon (owner).
        int num = this._loadout.GroupAt(slot.Id, fi);
        var grp = this._groups.Find(g => g.Num == num);
        if (grp == null) { grp = new WpnGroup { Num = num }; this._groups.Add(grp); }
        grp.Guns.Add(new WpnGun { Slot = slot.Id, Def = def, Path = path });
      }
    }
    this._groups.Sort((x, y) => x.Num.CompareTo(y.Num));
    if (this._weaponSel >= this._groups.Count)
    {
      this._weaponSel = 0;
    }

    this._defEnergy = Slowest(this._gunsEnergy);
    this._defBallistic = Slowest(this._gunsBallistic);
    this._defPodL = Slowest(this._podGunsL);
    this._defPodR = Slowest(this._podGunsR);
    var eb = SlotOf(this._enemySpec, WeaponSlotType.Ballistic);
    this._enemyBallisticSlot = eb;
    this._defEnemyBallistic = eb == null ? null : this._enemyLoadout.FirstDef(eb.Value);
    Dbg.Print($"LAB loadout energy={this._defEnergy?.Id} ballistic={this._defBallistic?.Id} podL={this._defPodL?.Id} podR={this._defPodR?.Id} total={this._loadout.TotalWeaponWeight:F0}t");
  }

  private static WeaponDef Slowest(System.Collections.Generic.List<(PartId Slot, WeaponDef Def)> guns)
  {
    WeaponDef best = null;
    foreach (var g in guns)
    {
      if (best == null || g.Def.Cooldown > best.Cooldown)
      {
        best = g.Def;
      }
    }

    return best;
  }


}
