using Godot;
using System.Collections.Generic;
using System.Linq;

// RiderDuel module: the Story team in the duel (owner, teams
// stage, step 4-5). The roster formed in the lab (TeamRoster.
// Story) takes the field in missions: the leader is the rider
// the player drives (the plain _player state, exactly as
// before teams existed); every other member is an NPC teammate
// — an Ally record below, driven by the shared foe brain under
// the current team order (RiderDuel.TeamAi.cs).
//
// Solo missions (a one-rider roster) build no allies and play
// exactly as missions always have; every harness mode is
// excluded, so the scripted suites keep their calibrated
// two/three-rider fields.
public partial class RiderDuel
{
    // One NPC teammate. Mirrors the Foe record's shape — the
    // shared AI/gunnery/damage code dispatches on walker
    // identity, and allies join those dispatches through the
    // AllyOf* helpers below.
    private sealed class Ally
    {
        public RiderWalker Walker;
        public RiderParts Parts;
        public RiderSpec Spec;
        public LoadoutState Loadout;
        public float Hp;
        public bool Dead;
        public string Role;   // Roles.AllyPrefix + roster slot
        public int Slot;      // TeamRoster.Story index
        public WeaponDef DefBallistic;
        public string BallisticSlot;
        public float Cd;
        public float Yaw = Mathf.Pi;
        public float OrbitDir = 1f;
        public float OrbitTimer;
        public float ThinkT;
        public RiderWalker TargetW;
        public Vector3 PrevRoot;
        public Vector3 Vel;
        public float DroopT;
        public Vector3 SpawnPos;
        public float SpawnYaw;
    }

    private readonly List<Ally> _allies = new();
    private int _leaderSlot = -1; // roster slot the player drives
    private bool _missionDone;    // complete or failed, announced once
    private bool _pendingLeaderPass; // the driven rider died; command passes next frame

    // The harness modes stage their own calibrated fields; the
    // team exists only in the live game.
    private bool AnyHarness => _test || _weaponsTest || _breakTest || _terrainTest
        || _photoTest || _missionTest || _ffaTest || _lrmProbe || _viewShot || _pointTest
        || _brakeTest || _roofTest || _longshotTest || _rangeTest || _grassShot
        || _boomShot || _boomCheck || _lagCheck || _flipbookTest || _gaitShot || _leaveShot;

    private static string RoleForSlot(int slot) => Roles.AllyPrefix + slot;

    private Ally AllyOf(RiderWalker w) => _allies.Find(a => a.Walker == w);
    private Ally AllyOfParts(RiderParts p) => _allies.Find(a => a.Parts == p);
    private Ally AllyByRole(string role) =>
        role != null && role.StartsWith(Roles.AllyPrefix) ? _allies.Find(a => a.Role == role) : null;
    private Ally AllyBySlot(int slot) => _allies.Find(a => a.Slot == slot);

    // The whole player team, leader first: the driven rider
    // plus every NPC teammate record (dead or alive).
    private IEnumerable<(RiderWalker W, RiderSpec Spec, float Hp, bool Dead, int Slot)> TeamMembers()
    {
        yield return (_player, _playerSpec, _hpPlayer, _playerDead, _leaderSlot);
        foreach (var a in _allies.OrderBy(a => a.Slot))
            yield return (a.Walker, a.Spec, a.Hp, a.Dead, a.Slot);
    }

    private bool TeamAlive => !_playerDead || _allies.Any(a => !a.Dead);

    // The living team rider nearest a point — the defenders'
    // engagement reference (a solo team resolves to the player,
    // the pre-teams behaviour exactly).
    private RiderWalker NearestTeamWalker(Vector3 from)
    {
        RiderWalker best = null;
        float bd = float.MaxValue;
        if (!_playerDead)
        {
            best = _player;
            bd = from.DistanceSquaredTo(_player.RootPos);
        }
        foreach (var a in _allies)
        {
            if (a.Dead) continue;
            float d = from.DistanceSquaredTo(a.Walker.RootPos);
            if (d < bd) { bd = d; best = a.Walker; }
        }
        return best;
    }

    // Parts registry by combat role — the hit model's resolver
    // delegates read through here, so a leader swap only has to
    // move state between the player fields and one Ally record;
    // the roles follow the seats automatically.
    private RiderParts PartsForRole(string id)
    {
        if (id == Roles.Player) return _partsPlayer;
        if (id == Roles.Foe && _foe != null) return _foe.Parts;
        var a = AllyByRole(id);
        if (a != null) return a.Parts;
        return _partsEnemy;
    }

    private RiderWalker WalkerForRole(string id)
    {
        if (id == Roles.Player) return _player;
        if (id == Roles.Foe && _foe != null) return _foe.Walker;
        var a = AllyByRole(id);
        return a != null ? a.Walker : _enemy;
    }

    // Build the NPC teammates from the roster: everyone except
    // the slot the player drives. They muster around the home
    // pad in a loose ring, facing the map centre like the
    // leader. Their fitted ballistic weapon (the lab loadout's)
    // is what the shared AI gunnery fires — the same rule the
    // foe fights by.
    private void BuildAllies()
    {
        _allies.Clear();
        if (!WorldConfig.IsMission || AnyHarness) return;
        TeamRoster.EnsureStory();
        _leaderSlot = TeamRoster.StorySlot;
        var pad = Main.SpawnPads[0];
        int k = 0;
        for (int slot = 0; slot < TeamRoster.Story.Count; slot++)
        {
            if (slot == _leaderSlot) continue;
            var member = TeamRoster.Story[slot];
            var spec = member.Spec;
            float ang = (k + 1) * 2.1f;
            var pos = PadPos(pad) + new Vector3(Mathf.Sin(ang) * 13f, 0f, Mathf.Cos(ang) * 13f);
            pos.Y = Main.HeightAt(pos.X, pos.Z);
            float yaw = SpawnYaw(pad);
            var a = new Ally
            {
                Spec = spec,
                Loadout = member.Loadout,
                Role = RoleForSlot(slot),
                Slot = slot,
                Yaw = yaw,
                SpawnPos = pos,
                SpawnYaw = yaw
            };
            a.Walker = BuildRider(spec, $"Ally{slot}", pos, yaw);
            a.Parts = new RiderParts(a.Walker, spec.PartsId);
            a.Parts.OnBreak = (id, c) => OnPartBroken(a.Parts, id, c);
            a.Walker.RefreshMaterials();
            a.Hp = spec.MaxHp;
            a.PrevRoot = a.Walker.RootPos;
            string eb = SlotOf(spec, WeaponSlotType.Ballistic);
            a.BallisticSlot = eb;
            a.DefBallistic = eb == null ? null : WeaponCatalog.ById(a.Loadout.WeaponIn(eb));
            _allies.Add(a);
            k++;
            GD.Print($"TEAM ally built: slot {slot} {spec.Id} role={a.Role} ballistic={a.DefBallistic?.Id ?? "none"}");
        }
        if (_allies.Count > 0) GD.Print($"TEAM {_allies.Count + 1} riders take the field; leader slot {_leaderSlot}");
    }

    // Take command of a teammate (owner, teams stage step 4):
    // the player drives the chosen rider; the previously driven
    // rider becomes an NPC in the freed seat. State moves
    // between the player fields and the Ally record — walkers
    // and parts objects never move, so every closure and
    // registry keyed on them (part-break handlers, hit
    // resolvers) stays correct; the roles follow the seats.
    private bool SetLeader(int slot)
    {
        if (slot == _leaderSlot) return false;
        var ally = AllyBySlot(slot);
        if (ally == null || ally.Dead) return false;
        // Pack the current driven rider into the freed seat.
        string eb = SlotOf(_playerSpec, WeaponSlotType.Ballistic);
        var old = new Ally
        {
            Walker = _player, Parts = _partsPlayer, Spec = _playerSpec, Loadout = _loadout,
            Hp = _hpPlayer, Dead = _playerDead, Role = ally.Role, Slot = _leaderSlot,
            Yaw = _playerYaw, PrevRoot = _player.RootPos, Vel = _playerVel,
            DroopT = _playerDroopT,
            SpawnPos = PadPos(Main.SpawnPads[0]), SpawnYaw = SpawnYaw(Main.SpawnPads[0]),
            BallisticSlot = eb,
            DefBallistic = eb == null ? null : WeaponCatalog.ById(_loadout.WeaponIn(eb))
        };
        // The packed rider's parts were bound to the _partsPlayer
        // FIELD in their OnBreak closure (the lab rebuild idiom);
        // rebind them to the record so a break on the NPC seat
        // reports on the right rider.
        old.Parts.OnBreak = (id, c) => OnPartBroken(old.Parts, id, c);
        // Promote the chosen teammate into the player fields.
        _player = ally.Walker; _partsPlayer = ally.Parts; _playerSpec = ally.Spec;
        _loadout = ally.Loadout; _hpPlayer = ally.Hp; _playerDead = false;
        _playerDroopT = ally.DroopT;
        _playerYaw = ally.Yaw;
        _torsoWorldYaw = _playerYaw + _player.TorsoTwist;
        _prevPlayerRoot = _player.RootPos; _playerVel = ally.Vel;
        _leaderSlot = slot;
        _allies.Remove(ally);
        _allies.Add(old);
        _allies.Sort((x, y) => x.Slot.CompareTo(y.Slot));
        _weaponSel = 0;
        RefreshHitCaps();
        ApplyLoadout();
        GD.Print($"TEAM leader now slot {slot} ({_playerSpec.Id})");
        return true;
    }

    // The driven rider died with teammates standing: command
    // passes to the first living teammate in slot order. Runs
    // at the top of a frame, never inside damage resolution.
    private void PassCommandIfNeeded()
    {
        if (!_pendingLeaderPass) return;
        _pendingLeaderPass = false;
        var next = _allies.Where(a => !a.Dead).OrderBy(a => a.Slot).FirstOrDefault();
        if (next == null) return;
        string fell = _playerSpec.Name;
        if (SetLeader(next.Slot))
            AddFeed($"{fell} DESTROYED — {next.Spec.Name} TAKES COMMAND");
    }

    // R reset: teammates restored with everyone else — full
    // pools, parts restored, back on their muster points.
    private void ResetAllies(bool live)
    {
        foreach (var a in _allies)
        {
            a.Hp = a.Spec.MaxHp;
            a.Dead = false;
            a.Cd = 0f;
            a.DroopT = 0f;
            a.TargetW = null;
            a.Parts.RestoreAll();
            SyncLegConsequences(a.Walker, a.Parts);
            a.Walker.Halted = false;
            a.Walker.Droop = 0f;
            a.Walker.SetDarkened(false);
            if (live)
            {
                a.Walker.Teleport(a.SpawnPos, a.SpawnYaw);
                a.Yaw = a.SpawnYaw;
                a.Walker.TorsoTwist = 0f;
                a.PrevRoot = a.Walker.RootPos;
                a.Vel = Vector3.Zero;
            }
        }
    }
}
