using Godot;
using System.Collections.Generic;

// Probe 1 (Round 2): skinned glTF character (Khronos Fox) imported directly,
// AnimationPlayer clip switching: Survey (idle state) when stationary,
// Walk while auto-moving forward. Arg-gated screenshot proof:
//   --anim-test  captures proof/probe1_idle.png and proof/probe1_walk.png
public partial class AnimProbe : Node3D
{
    private const float WalkSpeed = 2.6f;
    private const string IdleClip = "Survey"; // Fox has no literal "Idle"; Survey is its stationary clip
    private const string WalkClip = "Walk";

    private Node3D _fox;
    private AnimationPlayer _anim;
    private Camera3D _cam;
    private bool _test;
    private double _t;
    private bool _walking;
    private bool _shotIdle, _shotWalk;

    public override void _Ready()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg == "--anim-test") _test = true;

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

        // ---- Direct glTF import: the .glb becomes a PackedScene via the importer.
        var packed = GD.Load<PackedScene>("res://assets/Fox.glb");
        if (packed == null) { GD.PrintErr("ANIMPROBE FAIL: Fox.glb did not load as PackedScene"); GetTree().Quit(1); return; }
        _fox = packed.Instantiate<Node3D>();

        // Measure bind-pose height from mesh AABBs, scale to ~1.5 world units.
        var aabb = MeasureAabb(_fox);
        GD.Print($"ANIMPROBE fox raw AABB size={aabb.Size} pos={aabb.Position}");
        float scale = 1.5f / aabb.Size.Y;
        _fox.Scale = Vector3.One * scale;
        var start = new Vector3(0f, Main.HeightAt(0f, 8f), 8f);
        _fox.Position = start;
        _fox.Rotation = new Vector3(0f, Mathf.Pi, 0f); // face -Z (away from camera)
        AddChild(_fox);

        _anim = FindAnimPlayer(_fox);
        if (_anim == null) { GD.PrintErr("ANIMPROBE FAIL: no AnimationPlayer in Fox scene"); GetTree().Quit(1); return; }
        var names = new List<string>();
        foreach (var n in _anim.GetAnimationList()) names.Add(n);
        GD.Print("ANIMPROBE animations: " + string.Join(", ", names));
        foreach (var n in names) _anim.GetAnimation(n).LoopMode = Animation.LoopModeEnum.Linear;

        _anim.Play(IdleClip);
        GD.Print($"ANIMPROBE state=idle clip={IdleClip} pos={_fox.GlobalPosition}");

        _cam = new Camera3D();
        AddChild(_cam);
    }

    private static Aabb MeasureAabb(Node node)
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
            var b = mi.GetAabb();
            if (first) { box = b; first = false; } else box = box.Merge(b);
        }
        foreach (var child in node.GetChildren()) Collect(child, ref box, ref first);
    }

    private static AnimationPlayer FindAnimPlayer(Node node)
    {
        if (node is AnimationPlayer ap) return ap;
        foreach (var child in node.GetChildren())
        {
            var found = FindAnimPlayer(child);
            if (found != null) return found;
        }
        return null;
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
        var meshInstance = new MeshInstance3D { Mesh = st.Commit() };
        AddChild(meshInstance);
    }

    public override async void _Process(double delta)
    {
        if (_fox == null || _anim == null) return;
        _t += delta;

        // Phase 1: idle (stationary, Survey). Phase 2 (t >= 1.5s): walk forward.
        if (!_walking && _t >= 1.5)
        {
            _walking = true;
            _anim.Play(WalkClip, 0.15);
            GD.Print($"ANIMPROBE state=walk clip={WalkClip} pos={_fox.GlobalPosition}");
        }
        if (_walking)
        {
            var p = _fox.Position;
            p.Z -= WalkSpeed * (float)delta;
            p.Y = Main.HeightAt(p.X, p.Z);
            _fox.Position = p;
        }

        // Close follow camera (fox always faces -Z in this probe).
        var fp = _fox.GlobalPosition;
        _cam.GlobalPosition = fp + new Vector3(0f, 1.9f, 4.0f);
        _cam.LookAt(fp + new Vector3(0f, 0.8f, 0f), Vector3.Up);

        if (!_test) return;
        if (!_shotIdle && _t >= 1.2)
        {
            _shotIdle = true;
            await Capture("res://proof/probe1_idle.png");
            GD.Print($"ANIMPROBE shot idle pos={_fox.GlobalPosition} clip={_anim.CurrentAnimation}");
        }
        if (!_shotWalk && _t >= 3.1)
        {
            _shotWalk = true;
            await Capture("res://proof/probe1_walk.png");
            GD.Print($"ANIMPROBE shot walk pos={_fox.GlobalPosition} clip={_anim.CurrentAnimation}");
            GD.Print("ANIMPROBE DONE");
            GetTree().Quit();
        }
    }

    private async System.Threading.Tasks.Task Capture(string path)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var img = GetViewport().GetTexture().GetImage();
        var err = img.SavePng(path);
        GD.Print($"ANIMPROBE captured {path} size={img.GetSize()} err={err}");
    }
}
