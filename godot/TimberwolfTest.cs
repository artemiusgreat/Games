using Godot;

// TimberwolfTest: the user's STL print kit (53 STLs, variant kit by BobertmaX)
// assembled + rigged in Blender (~/workspace/timberwolf/build_timberwolf.py) and
// imported here as the player character on the round-1 terrain.
// Config: Torso_Opt2 (LRM-20 missile ears, matches the user's printed photo),
// Hand_Opt5 forearm cannons + ERPPCShort barrels, Leftleg_Lower_Opt2; the kit
// ships left side only, right side is mirrored in Blender.
// Verification (--timberwolf-test):
//   (a) proof/timberwolf_stand.png  - standing, feet on terrain (AABB log)
//   (b) proof/timberwolf_posed.png  - same spot, left leg posed via Skeleton3D
//       bone rotations (hip/knee/ankle) -> pixel-diff vs (a) proves articulation
//   (c) proof/timberwolf_walk.png   - after auto-walk, distance logged
public partial class TimberwolfTest : Node3D
{
    private const float WalkSpeed = 2.4f;
    private const float TargetHeight = 13.0f;

    private Node3D _holder;
    private Node3D _inner;
    private Skeleton3D _skel;
    private Camera3D _cam;
    private bool _test;
    private double _t;
    private int _phase; // 0 stand, 1 posed, 2 walk
    private bool _shotStand, _shotPosed, _shotWalk;
    private Vector3 _startPos;
    private int _hipL, _kneeL, _ankleL;

    public override void _Ready()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg == "--timberwolf-test") _test = true;

        var sun = new DirectionalLight3D { RotationDegrees = new Vector3(-50f, -30f, 0f) };
        AddChild(sun);
        var we = new WorldEnvironment
        {
            Environment = new Environment
            {
                BackgroundMode = Environment.BGMode.Color,
                BackgroundColor = new Color(0.35f, 0.5f, 0.7f)
            }
        };
        AddChild(we);

        BuildTerrain();
        AddTree(new Vector3(-13f, 0f, 4f));
        AddTree(new Vector3(11f, 0f, -2f));
        AddBuilding(new Vector3(18f, 0f, 18f), new Vector3(6f, 7f, 6f));

        var packed = GD.Load<PackedScene>("res://assets/timberwolf.fbx");
        if (packed == null) { GD.PrintErr("TW FAIL: timberwolf.fbx did not load as PackedScene"); GetTree().Quit(1); return; }
        _inner = packed.Instantiate<Node3D>();

        _holder = new Node3D { Name = "MechHolder" };
        AddChild(_holder);
        _holder.AddChild(_inner);

        var raw = MeasureWorldAabb(_inner);
        GD.Print($"TW raw AABB pos={raw.Position} size={raw.Size}");
        float s = TargetHeight / raw.Size.Y;
        _holder.Scale = Vector3.One * s;
        var a = MeasureWorldAabb(_inner);
        var desiredMin = new Vector3(-a.Size.X / 2f, 0f, -a.Size.Z / 2f);
        _inner.Position += (desiredMin - a.Position) / s;
        var chk = MeasureWorldAabb(_inner);
        GD.Print($"TW normalized AABB pos={chk.Position} size={chk.Size} scale={s}");

        // Blender FBX: model front faces +Z after import; face -Z (walk dir).
        _holder.Rotation = new Vector3(0f, Mathf.Pi, 0f);

        _skel = FindSkeleton(_inner);
        if (_skel == null) { GD.PrintErr("TW FAIL: no Skeleton3D in timberwolf.fbx"); GetTree().Quit(1); return; }
        var names = new string[_skel.GetBoneCount()];
        for (int i = 0; i < names.Length; i++) names[i] = _skel.GetBoneName(i);
        GD.Print($"TW skeleton bones={names.Length}: {string.Join(",", names)}");
        _hipL = _skel.FindBone("hip_L");
        _kneeL = _skel.FindBone("knee_L");
        _ankleL = _skel.FindBone("ankle_L");
        GD.Print($"TW bone idx hip_L={_hipL} knee_L={_kneeL} ankle_L={_ankleL}");

        _startPos = new Vector3(0f, Main.HeightAt(0f, 16f), 16f);
        _holder.Position = _startPos;
        GD.Print($"TW spawn pos={_holder.Position} terrainH={Main.HeightAt(0f, 16f)}");

        _cam = new Camera3D();
        AddChild(_cam);
    }

    private static Skeleton3D FindSkeleton(Node n)
    {
        if (n is Skeleton3D s) return s;
        foreach (var c in n.GetChildren())
        {
            var r = FindSkeleton(c);
            if (r != null) return r;
        }
        return null;
    }

    private static Aabb MeasureWorldAabb(Node node)
    {
        var box = new Aabb();
        bool first = true;
        Collect(node, ref box, ref first);
        return box;
    }

    private static void Collect(Node node, ref Aabb box, ref bool first)
    {
        if (node is MeshInstance3D mi)
        {
            var local = mi.GetAabb();
            var gt = mi.GlobalTransform;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? local.Position.X : local.End.X,
                    (i & 2) == 0 ? local.Position.Y : local.End.Y,
                    (i & 4) == 0 ? local.Position.Z : local.End.Z);
                var w = gt * corner;
                if (first) { box = new Aabb(w, Vector3.Zero); first = false; }
                else box = box.Expand(w);
            }
        }
        foreach (var child in node.GetChildren()) Collect(child, ref box, ref first);
    }

    private void BuildTerrain()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetMaterial(new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.5f, 0.22f) });
        float half = Main.TerrainSize / 2f;
        float step = (float)Main.TerrainSize / Main.TerrainSegs;
        for (int j = 0; j < Main.TerrainSegs; j++)
        for (int i = 0; i < Main.TerrainSegs; i++)
        {
            float x0 = -half + i * step, x1 = x0 + step;
            float z0 = -half + j * step, z1 = z0 + step;
            var a = new Vector3(x0, Main.HeightAt(x0, z0), z0);
            var b = new Vector3(x1, Main.HeightAt(x1, z0), z0);
            var c = new Vector3(x0, Main.HeightAt(x0, z1), z1);
            var d = new Vector3(x1, Main.HeightAt(x1, z1), z1);
            st.AddVertex(a); st.AddVertex(b); st.AddVertex(c);
            st.AddVertex(b); st.AddVertex(d); st.AddVertex(c);
        }
        st.GenerateNormals();
        AddChild(new MeshInstance3D { Mesh = st.Commit() });
    }

    private void AddTree(Vector3 pos)
    {
        pos.Y = Main.HeightAt(pos.X, pos.Z);
        var tree = new Node3D { Position = pos };
        var trunk = new MeshInstance3D();
        trunk.Mesh = new CylinderMesh { TopRadius = 0.22f, BottomRadius = 0.3f, Height = 2.2f };
        trunk.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.4f, 0.26f, 0.13f) };
        trunk.Position = new Vector3(0f, 1.1f, 0f);
        var canopy = new MeshInstance3D();
        canopy.Mesh = new SphereMesh { Radius = 1.7f, Height = 3.4f };
        canopy.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.13f, 0.45f, 0.16f) };
        canopy.Position = new Vector3(0f, 3.6f, 0f);
        tree.AddChild(trunk);
        tree.AddChild(canopy);
        AddChild(tree);
    }

    private void AddBuilding(Vector3 pos, Vector3 size)
    {
        pos.Y = Main.HeightAt(pos.X, pos.Z) + size.Y / 2f - 0.3f;
        var mi = new MeshInstance3D();
        mi.Mesh = new BoxMesh { Size = size };
        mi.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.55f, 0.58f) };
        mi.Position = pos;
        AddChild(mi);
    }

    private void LogFeet(string tag)
    {
        var a = MeasureWorldAabb(_inner);
        float terrain = Main.HeightAt(_holder.Position.X, _holder.Position.Z);
        GD.Print($"TW {tag} holderPos={_holder.Position} aabbMinY={a.Position.Y:F3} terrainH={terrain:F3} diff={a.Position.Y - terrain:F3}");
    }

    // Pose in skeleton-global space: SetBonePoseRotation is bone-local, and the
    // FBX bones' local axes are arbitrary (hip bone points down the leg), which
    // flung the leg sideways. Global pose overrides rotate about the model X
    // axis through each joint's current position, chained hip -> knee -> ankle.
    private Transform3D AroundAxis(Vector3 origin, float angle)
    {
        var rot = new Basis(Vector3.Right, angle);
        return new Transform3D(rot, origin) * new Transform3D(Basis.Identity, -origin);
    }

    private void PoseLeftLeg(float hip, float knee, float ankle)
    {
        if (_hipL < 0) return;
        var restHip = _skel.GetBoneGlobalRest(_hipL);
        var ah = AroundAxis(restHip.Origin, hip);
        _skel.SetBoneGlobalPoseOverride(_hipL, ah * restHip, 1.0f, true);
        if (_kneeL >= 0)
        {
            var restKnee = _skel.GetBoneGlobalRest(_kneeL);
            var ak = AroundAxis(ah * restKnee.Origin, knee);
            _skel.SetBoneGlobalPoseOverride(_kneeL, ak * ah * restKnee, 1.0f, true);
            if (_ankleL >= 0)
            {
                var restAnkle = _skel.GetBoneGlobalRest(_ankleL);
                var aa = AroundAxis(ak * ah * restAnkle.Origin, ankle);
                _skel.SetBoneGlobalPoseOverride(_ankleL, aa * ak * ah * restAnkle, 1.0f, true);
            }
        }
    }

    private void ClearPose()
    {
        _skel.ClearBonesGlobalPoseOverride();
    }

    public override async void _Process(double delta)
    {
        if (_holder == null || _skel == null) return;
        _t += delta;

        if (_phase == 0 && _t >= 2.0)
        {
            _phase = 1;
            PoseLeftLeg(-0.5f, 0.65f, -0.15f); // swing left leg forward, knee bent
            GD.Print("TW state=posed (left leg forward)");
        }
        if (_phase == 1 && _t >= 3.6)
        {
            _phase = 2;
            ClearPose();
            GD.Print($"TW state=walk pos={_holder.Position}");
        }
        if (_phase == 2)
        {
            var p = _holder.Position;
            p.Z -= WalkSpeed * (float)delta;
            p.Y = Main.HeightAt(p.X, p.Z);
            _holder.Position = p;
        }

        var mp = _holder.GlobalPosition;
        _cam.GlobalPosition = mp + new Vector3(-11.5f, 6.5f, -15.5f);
        _cam.LookAt(mp + new Vector3(0f, 5.5f, 0f), Vector3.Up);

        if (!_test) return;
        if (!_shotStand && _t >= 1.4)
        {
            _shotStand = true;
            LogFeet("stand");
            await Capture("res://proof/timberwolf_stand.png");
        }
        if (!_shotPosed && _phase == 1 && _t >= 3.0)
        {
            _shotPosed = true;
            LogFeet("posed");
            await Capture("res://proof/timberwolf_posed.png");
        }
        if (!_shotWalk && _phase == 2 && _t >= 6.8)
        {
            _shotWalk = true;
            LogFeet("walk");
            await Capture("res://proof/timberwolf_walk.png");
            float moved = _startPos.DistanceTo(_holder.Position);
            GD.Print($"TW DONE moved={moved:F2} start={_startPos} end={_holder.Position}");
            GetTree().Quit();
        }
    }

    private async System.Threading.Tasks.Task Capture(string path)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var img = GetViewport().GetTexture().GetImage();
        var err = img.SavePng(path);
        GD.Print($"TW captured {path} size={img.GetSize()} err={err}");
    }
}
