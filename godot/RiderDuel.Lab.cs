using Godot;
using System.Collections.Generic;

// RiderDuel module: RiderLab glue: construction from specs, selection, loadout application.
// Split from the single-file build (round 22 cleanup); the class
// stays one partial class - see RiderDuel.cs for the field block.
public partial class RiderDuel
{
    // Viewport position of a slot's part on the player's rider -
    // the lab's paperdoll dots are drawn there, so the mapping
    // survives any window size or aspect (owner). Null when the
    // chassis lacks the part or it is off-screen: the lab falls
    // back to its fractional anchors then.
    private Vector2? ProjectSlotAnchor(string slotId)
    {
        if (_partsPlayer == null || _cam == null || !_partsPlayer.Has(slotId)) return null;
        var w = _partsPlayer.PartCenterWorld(slotId);
        if (_cam.IsPositionBehind(w)) return null;
        var sp = _cam.UnprojectPosition(w);
        var vp = GetViewport().GetVisibleRect().Size;
        if (sp.X < -40f || sp.Y < -40f || sp.X > vp.X + 40f || sp.Y > vp.Y + 40f) return null;
        return sp;
    }

    // Viewport position of the rider's pelvis - the paperdoll's
    // radial center for panel placement. The slot anchors all
    // sit in the upper body, so their own centroid rides too
    // high and the panels bunch at the top; the pelvis is the
    // body's real middle.
    private Vector2? ProjectBodyCenter()
    {
        if (_player == null || _cam == null) return null;
        var w = _player.RootPos + Vector3.Up * (_playerSpec.Height * 0.45f);
        if (_cam.IsPositionBehind(w)) return null;
        var sp = _cam.UnprojectPosition(w);
        var vp = GetViewport().GetVisibleRect().Size;
        if (sp.X < -40f || sp.Y < -40f || sp.X > vp.X + 40f || sp.Y > vp.Y + 40f) return null;
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
        _labLoad = new CanvasLayer { Layer = 5, Visible = _photo };
        AddChild(_labLoad);
        var rect = new ColorRect { Color = FlowUi.LoadingBg };
        rect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _labLoad.AddChild(rect);
        // The loading art replaces the old text screen (owner),
        // same as the front door's cover.
        _labLoadArt = FlowUi.MakeLoadingArt();
        _labLoad.AddChild(_labLoadArt);
    }

    private void OpenLab()
    {
        if (_lab != null) { _lab.QueueFree(); _lab = null; }
        var lab = new RiderLab
        {
            Spec = _playerSpec,
            Loadout = _loadout,
            PlayerSpecId = _playerSpec.Id,
            EnemySpecId = _enemySpec.Id,
            AnchorProjector = ProjectSlotAnchor,
            CenterProjector = ProjectBodyCenter
        };
        if (WorldConfig.IsMission)
        {
            // Story team lab (owner, teams stage): the lab edits
            // one roster slot at a time; the slot chips switch,
            // add (up to 4) and remove slots.
            TeamRoster.EnsureStory();
            lab.TeamMode = true;
            lab.TeamSlot = TeamRoster.StorySlot;
            lab.TeamNames = new string[TeamRoster.Story.Count];
            lab.TeamMeta = new string[TeamRoster.Story.Count];
            for (int i = 0; i < TeamRoster.Story.Count; i++)
            {
                var sp = TeamRoster.Story[i].Spec;
                lab.TeamNames[i] = sp.Name;
                lab.TeamMeta[i] = $"{sp.Tonnage:F0}t · {char.ToUpper(sp.ClassLabel[0]) + sp.ClassLabel.Substring(1).ToLowerInvariant()}";
            }
            lab.OnTeamSlot = i => SetStorySlot(i);
            lab.OnTeamAdd = () => AddStoryMember();
            lab.OnTeamRemove = i => RemoveStoryMember(i);
        }
        _lab = lab;
        lab.OnStart = () =>
        {
            ShowWorld(); // the lab stand comes down, the fight world returns
            RebuildAlliesForDeploy(); // the roster may have changed in the lab (owner's report)
            ApplyLoadout();
            lab.QueueFree();
            if (_lab == lab) _lab = null;
            _labOpen = false;
            if (_labLoad != null) _labLoad.Visible = false;
            _cross.Visible = true; // the target cursor returns on deploy (owner: it must not show in the lab)
            if (_labLight != null) _labLight.Visible = false;
            SetHudPanelsVisible(true);
            _radar.Visible = true;
            _feedLabel.Visible = true;
            Input.MouseMode = Input.MouseModeEnum.Captured;
            GD.Print("LAB deploy: duel started with the lab loadout");
        };
        lab.OnSelectionChanged = () => SetRiderSelection(lab.PlayerSpecId, lab.EnemySpecId);
        BuildLabStand();
        _cross.Visible = false; // no target cursor in the lab view (owner)
        _uiLayer.AddChild(lab);
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
    private readonly System.Collections.Generic.List<Node3D> _labHidden = new();

    private void BuildLabStand()
    {
        if (_player == null) return;
        var c = _player.RootPos;
        _labStand = new Node3D { Name = "LabStand" };
        AddChild(_labStand);
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
            Mesh = new PlaneMesh { Size = new Vector2(160f, 160f) },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoTexture = PlateTexture(), Roughness = 0.75f, Metallic = 0.15f,
                DisableFog = true
            },
            Position = new Vector3(c.X, c.Y + 0.02f, c.Z)
        };
        _labStand.AddChild(disc);
        var dome = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 120f, Height = 240f, RadialSegments = 32, Rings = 16 },
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoTexture = SkyGradientTexture(), DisableFog = true,
                CullMode = BaseMaterial3D.CullModeEnum.Back
            },
            Position = new Vector3(c.X, c.Y + 2f, c.Z),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
        };
        _labStand.AddChild(dome);
        // Hide the fight world (terrain, props, other riders,
        // photo dressing); lights, the camera and the player
        // stay. The snapshot restores exactly what was hidden.
        foreach (var child in GetChildren())
        {
            if (child is Node3D n3 && n3 != _player && n3 != _labStand && n3 != _cam
                && n3 is not Light3D && n3.Visible)
            {
                n3.Visible = false;
                _labHidden.Add(n3);
            }
        }
    }

    private static ImageTexture _standPlateTex;
    // The deck's texture, drawn in code: one steel panel
    // (brushed noise, seam border, bevel line, four rivets)
    // blitted 16x16, so the 160u floor reads as 10u panels.
    private static ImageTexture PlateTexture()
    {
        if (_standPlateTex != null) return _standPlateTex;
        const int P = 128, N = 16;
        var panel = Image.CreateEmpty(P, P, false, Image.Format.Rgb8);
        var rng = new System.Random(0x57EE1);
        var baseC = new Color(0.25f, 0.27f, 0.31f);
        for (int y = 0; y < P; y++)
        for (int x = 0; x < P; x++)
        {
            float nse = (float)(rng.NextDouble() - 0.5) * 0.05f;
            float brush = Mathf.Sin(y * 0.9f) * 0.008f;
            panel.SetPixel(x, y, new Color(baseC.R + nse + brush, baseC.G + nse + brush, baseC.B + nse + brush));
        }
        var seam = new Color(0.10f, 0.11f, 0.13f);
        var bevel = new Color(0.36f, 0.39f, 0.44f);
        for (int i = 0; i < P; i++)
        {
            panel.SetPixel(i, 0, seam); panel.SetPixel(i, 1, seam);
            panel.SetPixel(i, P - 1, seam); panel.SetPixel(i, P - 2, seam);
            panel.SetPixel(0, i, seam); panel.SetPixel(1, i, seam);
            panel.SetPixel(P - 1, i, seam); panel.SetPixel(P - 2, i, seam);
            panel.SetPixel(i, 3, bevel); panel.SetPixel(i, P - 4, bevel);
            panel.SetPixel(3, i, bevel); panel.SetPixel(P - 4, i, bevel);
        }
        var rivet = new Color(0.42f, 0.46f, 0.52f);
        foreach (var (rx, ry) in new[] { (9, 9), (P - 10, 9), (9, P - 10), (P - 10, P - 10) })
            for (int y = -2; y <= 2; y++)
            for (int x = -2; x <= 2; x++)
                if (x * x + y * y <= 4) panel.SetPixel(rx + x, ry + y, x * x + y * y >= 3 ? seam : rivet);
        var img = Image.CreateEmpty(P * N, P * N, false, Image.Format.Rgb8);
        var full = new Rect2I(0, 0, P, P);
        for (int i = 0; i < N; i++)
        for (int j = 0; j < N; j++)
            img.BlitRect(panel, full, new Vector2I(i * P, j * P));
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
        if (_standSkyTex != null) return _standSkyTex;
        const int H = 256;
        var img = Image.CreateEmpty(2, H, false, Image.Format.Rgb8);
        var stops = new (float At, Color C)[]
        {
            (0.00f, new Color(0.030f, 0.038f, 0.052f)),
            (0.38f, new Color(0.075f, 0.092f, 0.120f)),
            (0.50f, new Color(0.135f, 0.160f, 0.200f)),
            (0.62f, new Color(0.075f, 0.092f, 0.120f)),
            (1.00f, new Color(0.035f, 0.045f, 0.062f))
        };
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
        foreach (var n in _labHidden) if (IsInstanceValid(n)) n.Visible = true;
        _labHidden.Clear();
        if (_labStand != null) { _labStand.QueueFree(); _labStand = null; }
    }

    // ---- rider construction from specs (round 22) ----
    // Everything model-bound comes from the RiderSpec, so the lab
    // can field either chassis on either side.
    private RiderWalker BuildRider(RiderSpec spec, string nodeName, Vector3 pos, float yaw)
    {
        var w = new RiderWalker { Name = nodeName };
        w.FootClamp = ClampFootTarget; // owner round 7: step targets clamp against obstacles (leg-collider option 1)
        AddChild(w);
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
            FlipKneePole = spec.FlipKneePole
        }, pos, yaw);
        return w;
    }

    private void RefreshHitCaps()
    {
        _hit.Riders.RemoveAll(m => m.Id == Roles.Player || m.Id == Roles.Enemy || m.Id == Roles.Foe
            || (m.Id != null && m.Id.StartsWith(Roles.AllyPrefix)));
        _hit.Riders.Add(new HitModel.RiderCap { Id = Roles.Player, Root = () => _player.RootPos, Radius = _playerSpec.Radius, Height = _playerSpec.Height });
        _hit.Riders.Add(new HitModel.RiderCap { Id = Roles.Enemy, Root = () => _enemy.RootPos, Radius = _enemySpec.Radius, Height = _enemySpec.Height });
        if (_foe?.Walker != null)
            _hit.Riders.Add(new HitModel.RiderCap { Id = Roles.Foe, Root = () => _foe.Walker.RootPos, Radius = _foe.Spec.Radius, Height = _foe.Spec.Height });
        foreach (var a in _allies)
            _hit.Riders.Add(new HitModel.RiderCap { Id = a.Role, Root = () => a.Walker.RootPos, Radius = a.Spec.Radius, Height = a.Spec.Height });
    }

    // The foe (free-for-all): the partner chassis - the first
    // entry on the roster's partner-priority list that the
    // enemy slot does not already hold. No chassis names in
    // engine code; the list is roster data.
    private static RiderSpec FfaPartner(RiderSpec enemy)
    {
        foreach (var s in RiderSpecs.FfaPartnerPriority) if (s.Id != enemy.Id) return s;
        foreach (var s in RiderSpecs.All) if (s.Id != enemy.Id) return s;
        return enemy;
    }

    private void BuildFoe()
    {
        var spec = _foeMember?.Spec ?? FfaPartner(_enemySpec);
        _foe = new Foe { Spec = spec, Loadout = _foeMember?.Loadout ?? LoadoutState.DefaultFor(spec) };
        var pad = Main.SpawnPads[2];
        _foe.Walker = BuildRider(spec, "Enemy2", PadPos(pad), SpawnYaw(pad));
        _foe.Yaw = SpawnYaw(pad);
        _foe.Parts = new RiderParts(_foe.Walker, spec.PartsId);
        _foe.Parts.OnBreak = (id, c) => OnPartBroken(_foe.Parts, id, c);
        _foe.Walker.RefreshMaterials();
        _foe.Hp = spec.MaxHp;
        _foe.PrevRoot = _foe.Walker.RootPos;
        string eb = SlotOf(spec, WeaponSlotType.Ballistic);
        _foe.BallisticSlot = eb;
        _foe.DefBallistic = eb == null ? null : WeaponCatalog.ById(_foe.Loadout.WeaponIn(eb));
        GD.Print($"FFA foe built: {spec.Id} at pad 2, ballistic={_foe.DefBallistic?.Id ?? "none"}");
    }

    private void RebuildFoe()
    {
        var want = _foeMember?.Spec ?? FfaPartner(_enemySpec);
        if (_foe != null && _foe.Spec.Id == want.Id) return;
        var pos = _foe?.Walker.RootPos ?? PadPos(Main.SpawnPads[2]);
        float yaw = _foe?.Walker.LegsYaw ?? SpawnYaw(Main.SpawnPads[2]);
        _foe?.Walker.QueueFree();
        var spec = want;
        _foe = new Foe { Spec = spec, Loadout = _foeMember?.Loadout ?? LoadoutState.DefaultFor(spec) };
        _foe.Walker = BuildRider(spec, "Enemy2", pos, yaw);
        _foe.Yaw = yaw;
        _foe.Parts = new RiderParts(_foe.Walker, spec.PartsId);
        _foe.Parts.OnBreak = (id, c) => OnPartBroken(_foe.Parts, id, c);
        _foe.Walker.RefreshMaterials();
        _foe.Hp = spec.MaxHp;
        _foe.PrevRoot = _foe.Walker.RootPos;
        string eb = SlotOf(spec, WeaponSlotType.Ballistic);
        _foe.BallisticSlot = eb;
        _foe.DefBallistic = eb == null ? null : WeaponCatalog.ById(_foe.Loadout.WeaponIn(eb));
        RefreshHitCaps();
    }

    private void RebuildRider(bool playerSide)
    {
        if (playerSide)
        {
            var pos = _player.RootPos; float yaw = _player.LegsYaw;
            _player.QueueFree();
            _player = BuildRider(_playerSpec, "Player", pos, yaw);
            _partsPlayer = new RiderParts(_player, _playerSpec.PartsId);
            _partsPlayer.OnBreak = (id, c) => OnPartBroken(_partsPlayer, id, c);
            _player.RefreshMaterials();
            _hpPlayer = _playerSpec.MaxHp; _playerDead = false;
            _prevPlayerRoot = _player.RootPos;
        }
        else
        {
            var pos = _enemy.RootPos; float yaw = _enemy.LegsYaw;
            _enemy.QueueFree();
            _enemy = BuildRider(_enemySpec, "Enemy", pos, yaw);
            _partsEnemy = new RiderParts(_enemy, _enemySpec.PartsId);
            _partsEnemy.OnBreak = (id, c) => OnPartBroken(_partsEnemy, id, c);
            _enemy.RefreshMaterials();
            _hpEnemy = _enemySpec.MaxHp; _enemyDead = false;
        }
        RefreshHitCaps();
        ApplyLoadout();
    }

    // Lab rider selection (round 22): swap either side's chassis.
    // The loadout resets to the new chassis's default fit; the lab
    // UI is recreated against the new spec.
    public void SetRiderSelection(string playerSpecId, string enemySpecId)
    {
        bool pChanged = playerSpecId != _playerSpec.Id;
        bool eChanged = enemySpecId != _enemySpec.Id;
        if (!pChanged && !eChanged) return;
        if (pChanged)
        {
            _playerSpec = RiderSpecs.ById(playerSpecId);
            _loadout = LoadoutState.DefaultFor(_playerSpec);
            RebuildRider(true);
            // In the Story team lab the change belongs to the
            // edited roster slot (the fresh default loadout
            // replaces the member's, as it replaces the lone
            // player's today).
            if (WorldConfig.IsMission && TeamRoster.Story.Count > 0)
            {
                var m = TeamRoster.Story[TeamRoster.StorySlot];
                m.Spec = _playerSpec;
                m.Loadout = _loadout;
            }
            else if (_bracketActive && TeamRoster.Tourney.Count > 0)
            {
                // Tournament: the lab prepares team 1's first
                // rider - a chassis change re-points its slot.
                var m = TeamRoster.Tourney[0][0];
                m.Spec = _playerSpec;
                m.Loadout = _loadout;
            }
        }
        if (eChanged)
        {
            _enemySpec = RiderSpecs.ById(enemySpecId);
            _enemyLoadout = LoadoutState.DefaultFor(_enemySpec);
            RebuildRider(false);
            if (_foe != null) RebuildFoe(); // the FFA partner re-picks from the priority data
        }
        GD.Print($"LAB selection player={_playerSpec.Id} enemy={_enemySpec.Id}");
        if (_lab != null) Callable.From(() => { if (_labOpen) OpenLab(); }).CallDeferred();
    }

    // ---- Story team slots (owner, teams stage) ----
    // Switching the edited slot points the lab (and its preview
    // rider) at that member's spec and loadout. The lab edits
    // the member's LoadoutState object directly, so nothing is
    // saved or lost on a switch.
    private void SetStorySlot(int i)
    {
        if (i < 0 || i >= TeamRoster.Story.Count || i == TeamRoster.StorySlot && _lab != null
            && _playerSpec == TeamRoster.Story[i].Spec) return;
        TeamRoster.StorySlot = i;
        var m = TeamRoster.Story[i];
        _playerSpec = m.Spec;
        _loadout = m.Loadout;
        RebuildRider(true);
        GD.Print($"LAB team slot {i}: {m.Spec.Id}");
        if (_labOpen) Callable.From(() => { if (_labOpen) OpenLab(); }).CallDeferred();
    }

    private void AddStoryMember()
    {
        if (TeamRoster.Story.Count >= 4) return;
        var last = TeamRoster.Story[TeamRoster.Story.Count - 1].Spec;
        int idx = 0;
        for (int i = 0; i < RiderSpecs.All.Length; i++)
            if (RiderSpecs.All[i].Id == last.Id) idx = i;
        var spec = RiderSpecs.All[(idx + 1) % RiderSpecs.All.Length];
        TeamRoster.Story.Add(TeamRoster.Make(spec));
        SetStorySlot(TeamRoster.Story.Count - 1);
    }

    private void RemoveStoryMember(int i)
    {
        if (i <= 0 || i >= TeamRoster.Story.Count) return; // slot 0 (the main player) stays
        TeamRoster.Story.RemoveAt(i);
        if (TeamRoster.StorySlot >= TeamRoster.Story.Count)
            TeamRoster.StorySlot = TeamRoster.Story.Count - 1;
        // Force the lab onto the (possibly shifted) current slot.
        var m = TeamRoster.Story[TeamRoster.StorySlot];
        _playerSpec = m.Spec;
        _loadout = m.Loadout;
        RebuildRider(true);
        if (_labOpen) Callable.From(() => { if (_labOpen) OpenLab(); }).CallDeferred();
    }

    private static string SlotOf(RiderSpec spec, WeaponSlotType type, int n = 0)
    {
        foreach (var s in spec.Slots) if (s.Type == type && n-- == 0) return s.Id;
        return null;
    }

    private void ApplyLoadout()
    {
        // Jump jets (owner): effective only when the chassis can
        // mount them AND the loadout has them fitted.
        _player.JumpJets = _playerSpec.JumpCapable && _loadout.JumpJets;
        _enemy.JumpJets = _enemySpec.JumpCapable;
        // Firing groups (multi-fit): every weapon fitted in a slot
        // of the matching type, in spec slot order. The single
        // _def* fields stay as each group's representative (the
        // slowest-cooldown weapon) for HUD bars and labels.
        _gunsEnergy.Clear(); _gunsBallistic.Clear(); _podGunsL.Clear(); _podGunsR.Clear();
        _groups.Clear();
        int missileIdx = 0;
        foreach (var slot in _playerSpec.Slots)
        {
            int path = slot.Type == WeaponSlotType.Energy ? 0
                : slot.Type == WeaponSlotType.Ballistic ? 1
                : missileIdx++ == 0 ? 2 : 3; // missile slots in order: left pod, right pod
            var bucket = path == 0 ? _gunsEnergy : path == 1 ? _gunsBallistic
                : path == 2 ? _podGunsL : _podGunsR;
            var fitted = _loadout.WeaponsIn(slot.Id);
            for (int fi = 0; fi < fitted.Count; fi++)
            {
                var def = WeaponCatalog.ById(fitted[fi]);
                if (def == null) continue;
                bucket.Add((slot.Id, def));
                // The player's lab-assigned firing group for this
                // exact fitted weapon (owner).
                int num = _loadout.GroupAt(slot.Id, fi);
                var grp = _groups.Find(g => g.Num == num);
                if (grp == null) { grp = new WpnGroup { Num = num }; _groups.Add(grp); }
                grp.Guns.Add(new WpnGun { Slot = slot.Id, Def = def, Path = path });
            }
        }
        _groups.Sort((x, y) => x.Num.CompareTo(y.Num));
        if (_weaponSel >= _groups.Count) _weaponSel = 0;
        _defEnergy = Slowest(_gunsEnergy);
        _defBallistic = Slowest(_gunsBallistic);
        _defPodL = Slowest(_podGunsL);
        _defPodR = Slowest(_podGunsR);
        string eb = SlotOf(_enemySpec, WeaponSlotType.Ballistic);
        _enemyBallisticSlot = eb;
        _defEnemyBallistic = eb == null ? null : WeaponCatalog.ById(_enemyLoadout.WeaponIn(eb));
        GD.Print($"LAB loadout energy={_defEnergy?.Id ?? "none"} ballistic={_defBallistic?.Id ?? "none"} podL={_defPodL?.Id ?? "none"} podR={_defPodR?.Id ?? "none"} total={_loadout.TotalWeaponWeight:F0}t");
    }

    private static WeaponDef Slowest(System.Collections.Generic.List<(string Slot, WeaponDef Def)> guns)
    {
        WeaponDef best = null;
        foreach (var g in guns) if (best == null || g.Def.Cooldown > best.Cooldown) best = g.Def;
        return best;
    }


}
