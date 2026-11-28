using Godot;
using System.Collections.Generic;

namespace Titans;

// Dynamic loading for the Ascent campaign map (owner,
// 2026-11-20): the map is far too big to simulate and draw
// whole. Two mechanisms, both scoped to the ascent world
// (every other mode behaves exactly as before):
//
//  - Sector streaming: the terrain is built in 4x4 chunks
//    and every static world node (buildings, bridges,
//    decks, trees, rocks) is registered with its centre;
//    a node renders only while the player is within its
//    range. State is never touched - collision circles,
//    destructible hit points and damage all live outside
//    visibility, so a hidden building still blocks shots
//    and still burns.
//
//  - Rider dormancy, keyed to weapon reach as the owner
//    specified: a rider inside 500u simulates every frame;
//    between 500u and 1500u (the longest weapon range) it
//    ticks at 5 Hz with the accumulated time step, so it
//    still patrols and can still be fought at range;
//    beyond 1500u nothing can reach it, so it freezes and
//    hides until the player closes in.
public partial class Titans
{
    private readonly List<(Node3D Node, Vector2 Centre, float Range)> _streamed = new();
    private readonly List<MeshInstance3D> _terrainChunks = new();
    private readonly Dictionary<RiderWalker, float> _tickAcc = new();
    private readonly Dictionary<RiderWalker, float> _tickNow = new();
    private bool _streamingLive;
    private float _streamAcc;

    private const float DormantRange = 500f;   // full rate inside this
    private const float FreezeRange = 1500f;   // = the longest weapon range (LRM)
    private const float DormantTick = 0.2f;    // 5 Hz between the two
    private const float StaticStreamBaseRange = 750f;  // a static renders within this + its size margin
    private const float StaticStreamSizeFactor = 0.75f; // size margin per unit of half-diagonal
    private const float StreamPassInterval = 0.25f;    // visibility pass cadence

    // Called once at the end of the ascent build: register
    // every static world node for sector streaming.
    private void RegisterStreaming()
    {
        var riders = new HashSet<Node> { _player, _enemy };
        if (_foe != null) riders.Add(_foe.Walker);
        foreach (var a in _allies) riders.Add(a.Walker);
        int statics = 0;
        foreach (var child in GetChildren())
        {
            if (child is not Node3D n3 || riders.Contains(child)) continue;
            if (child is MeshInstance3D cmi0 && _terrainChunks.Contains(cmi0)) continue;
            var box = WorldAabb(child);
            if (box.Size == Vector3.Zero) continue;
            var centre = new Vector2(box.GetCenter().X, box.GetCenter().Z);
            float halfDiag = 0.5f * new Vector2(box.Size.X, box.Size.Z).Length();
            _streamed.Add((n3, centre, StaticStreamBaseRange + StaticStreamSizeFactor * halfDiag));
            statics++;
        }
        Dbg.Print($"STREAMING ascent: {_streamed.Count} streamed nodes " +
                 $"({_terrainChunks.Count} terrain chunks, {statics} statics), " +
                 $"dormancy {DormantRange}u full / {FreezeRange}u freeze");
    }

    private static Aabb WorldAabb(Node n)
    {
        var box = new Aabb();
        bool any = false;
        void Grow(Node m)
        {
            if (m is MeshInstance3D mi && mi.Mesh != null)
            {
                var b = mi.GlobalTransform * mi.GetAabb();
                box = any ? box.Merge(b) : b;
                any = true;
            }
            foreach (var c in m.GetChildren()) Grow(c);
        }
        Grow(n);
        return box;
    }

    // The per-frame streaming pass (live play only; harness
    // branches never call it, so their worlds stay whole).
    private void UpdateStreaming(float dt)
    {
        _streamingLive = true;
        _tickNow.Clear();
        var pp = new Vector2(_player.RootPos.X, _player.RootPos.Z);
        DormCheck(_enemy, pp, dt);
        if (_foe != null) DormCheck(_foe.Walker, pp, dt);
        foreach (var a in _allies) DormCheck(a.Walker, pp, dt);

        _streamAcc += dt;
        if (_streamAcc < StreamPassInterval) return;
        _streamAcc = 0f;
        // Destroyed props stay gone (owner, 2026-11-22): this pass
        // used to re-show a collapsed building within a quarter
        // second - StartCollapse hides the node and removes its
        // colliders for good, but the distance rule blindly turned
        // the mesh back on, leaving a standing ghost that riders
        // and shots passed through. A smoking-out tree (State 3)
        // is NOT in this set: its node must stay visible while it
        // shrinks, so only the finished states are pinned hidden.
        HashSet<Node3D> gone = null;
        foreach (var d in _destructibles)
            if (d.State == DestrState.Gone)
                (gone ??= new HashSet<Node3D>()).Add(d.Node);
        foreach (var (node, centre, range) in _streamed)
        {
            if (!GodotObject.IsInstanceValid(node)) continue;
            if (gone != null && gone.Contains(node)) { node.Visible = false; continue; }
            node.Visible = pp.DistanceTo(centre) <= range;
        }
    }

    private void DormCheck(RiderWalker w, Vector2 pp, float dt)
    {
        if (w == null) return;
        float d = pp.DistanceTo(new Vector2(w.RootPos.X, w.RootPos.Z));
        if (d > FreezeRange)
        {
            _tickAcc.Remove(w);
            w.Visible = false;
            return;
        }
        w.Visible = true;
        if (d < DormantRange || _sniper)
        {
            // Sniper view (owner, 2026-11-25): in the 5 Hz
            // band a far rider's walk reads as jitter under
            // the scope's magnification, so while the scope
            // is up every visible rider ticks at full frame
            // rate - the view is temporary and the rider
            // count is small.
            _tickNow[w] = dt;
            _tickAcc.Remove(w);
            return;
        }
        float acc = (_tickAcc.TryGetValue(w, out var v) ? v : 0f) + dt;
        if (acc >= DormantTick)
        {
            _tickNow[w] = acc;
            _tickAcc.Remove(w);
        }
        else _tickAcc[w] = acc;
    }

    // Whether this walker simulates on the current frame,
    // and the time step it should use when it does. Outside
    // the live ascent pass both are the plain frame values.
    private bool TickReady(RiderWalker w)
        => !_streamingLive || _tickNow.ContainsKey(w);

    private float TickDt(RiderWalker w, float frameDt)
        => _streamingLive && _tickNow.TryGetValue(w, out var v) ? v : frameDt;
}
