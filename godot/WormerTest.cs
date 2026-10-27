using Godot;

// WormerMech quick test: the user's wormermech.max, converted by the user
// himself to wormermech.fbx (online converter), used as the player character
// on the round-1 terrain.
// Rig verification of the FBX (Blender import): NOT RIGGED - 0 armatures,
// 0 bones, 0 vertex groups/weights, 0 animation actions across 310 meshes;
// hierarchy is EMPTY transform nodes with child meshes. So articulation proof
// does not apply; verification is (a) rendered frame standing ON terrain,
// (b) rendered frame after auto-walk with feet-tracking log
// (holder Y == HeightAt, AABB min Y == terrain height).
//   --wormer-test  captures proof/wormer_stand.png and proof/wormer_walk.png
public partial class WormerTest : Node3D
{
    private const float WalkSpeed = 2.4f;
    private const float TargetHeight = 11.5f; // world units after normalization

    private Node3D _holder;   // scaled + walked; its origin is the feet center
    private Node3D _inner;    // instantiated glb scene, offset-corrected inside holder
    private Camera3D _cam;
    private bool _test;
    private double _t;
    private bool _walking;
    private bool _shotStand, _shotWalk;
    private Vector3 _startPos;

    public override void _Ready()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg == "--wormer-test") _test = true;

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

        var packed = GD.Load<PackedScene>("res://assets/wormermech.fbx");
        if (packed == null) { GD.PrintErr("WORMER FAIL: wormermech.fbx did not load as PackedScene"); GetTree().Quit(1); return; }
        _inner = packed.Instantiate<Node3D>();

        _holder = new Node3D { Name = "MechHolder" };
        AddChild(_holder);
        _holder.AddChild(_inner);

        // Normalize: uniform scale so AABB height == TargetHeight, then shift the
        // inner scene so the AABB min sits at holder origin (feet at y=0, centered x/z).
        var raw = MeasureWorldAabb(_inner);
        GD.Print($"WORMER raw AABB pos={raw.Position} size={raw.Size}");
        float s = TargetHeight / raw.Size.Y;
        _holder.Scale = Vector3.One * s;
        var a = MeasureWorldAabb(_inner);
        var desiredMin = new Vector3(-a.Size.X / 2f, 0f, -a.Size.Z / 2f);
        _inner.Position += (desiredMin - a.Position) / s;
        var chk = MeasureWorldAabb(_inner);
        GD.Print($"WORMER normalized AABB pos={chk.Position} size={chk.Size} scale={s}");

        // glTF front of this model faces +Z; rotate so it faces -Z (walk direction).
        _holder.Rotation = new Vector3(0f, Mathf.Pi, 0f);

        _startPos = new Vector3(0f, Main.HeightAt(0f, 16f), 16f);
        _holder.Position = _startPos;
        GD.Print($"WORMER spawn pos={_holder.Position} terrainH={Main.HeightAt(0f, 16f)}");

        _cam = new Camera3D();
        AddChild(_cam);
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
        // Shared heightfield mesh (vertices on grid nodes = HeightAt).
        AddChild(new MeshInstance3D { Mesh = Main.CreateTerrainMesh() });
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
        GD.Print($"WORMER {tag} holderPos={_holder.Position} aabbMinY={a.Position.Y:F3} terrainH={terrain:F3} diff={a.Position.Y - terrain:F3}");
    }

    public override async void _Process(double delta)
    {
        if (_holder == null) return;
        _t += delta;

        if (!_walking && _t >= 1.5)
        {
            _walking = true;
            GD.Print($"WORMER state=walk pos={_holder.Position}");
        }
        if (_walking)
        {
            var p = _holder.Position;
            p.Z -= WalkSpeed * (float)delta;
            p.Y = Main.HeightAt(p.X, p.Z);
            _holder.Position = p;
        }

        // Follow camera: front-left 3/4, framing the whole 11.5u mech.
        var mp = _holder.GlobalPosition;
        _cam.GlobalPosition = mp + new Vector3(-10.5f, 6.0f, -14.5f);
        _cam.LookAt(mp + new Vector3(0f, 5.0f, 0f), Vector3.Up);

        if (!_test) return;
        if (!_shotStand && _t >= 1.2)
        {
            _shotStand = true;
            LogFeet("stand");
            await Capture("res://proof/wormer_stand.png");
        }
        if (!_shotWalk && _t >= 4.6)
        {
            _shotWalk = true;
            LogFeet("walk");
            await Capture("res://proof/wormer_walk.png");
            float moved = _startPos.DistanceTo(_holder.Position);
            GD.Print($"WORMER DONE moved={moved:F2} start={_startPos} end={_holder.Position}");
            GetTree().Quit();
        }
    }

    private async System.Threading.Tasks.Task Capture(string path)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var img = GetViewport().GetTexture().GetImage();
        var err = img.SavePng(path);
        GD.Print($"WORMER captured {path} size={img.GetSize()} err={err}");
    }
}
