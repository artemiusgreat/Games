using System.Collections.Generic;

// Team state for the wizard flow (owner, teams stage). Static,
// like FlowMain's flow state, so it survives the trips between
// the flow scene and the duel scene (the lab's Back button
// reloads the flow; DEPLOY loads the duel).
//
// Story: the player's team — 1 to 4 riders formed in the Rider
// Lab (step 3). Slot 0 is the main player's rider; the leader
// is chosen in the mission (step 4) and can change there, so
// the roster itself carries no leader flag.
public sealed class TeamMember
{
    public RiderSpec Spec;
    public LoadoutState Loadout;
}

public static class TeamRoster
{
    public static readonly List<TeamMember> Story = new();
    public static int StorySlot; // the lab's currently edited slot

    public static TeamMember Make(RiderSpec spec) =>
        new() { Spec = spec, Loadout = LoadoutState.DefaultFor(spec) };

    // A fresh story starts with a single rider for the main
    // player (owner: the lab shows one slot and lets the
    // player add up to four).
    public static void EnsureStory()
    {
        if (Story.Count == 0) Story.Add(Make(RiderSpecs.Enyo));
        if (StorySlot < 0 || StorySlot >= Story.Count) StorySlot = 0;
    }

    // Tournament: the bracket formed on the team selector —
    // a list of teams, each a list of riders. Team 0 is the
    // player's own; its slot 0 is the rider he prepares in the
    // lab (step 4). At least two teams always stand and every
    // team fields at least one rider (the selector's rules).
    public const int MaxTeams = 4;
    public const int MaxPerTeam = 4;
    public static readonly List<List<TeamMember>> Tourney = new();
    public static int TourneyTeam; // the selector's open team

    public static void EnsureTourney()
    {
        if (Tourney.Count == 0)
        {
            Tourney.Add(new List<TeamMember> { Make(RiderSpecs.Enyo) });
            Tourney.Add(new List<TeamMember> { Make(RiderSpecs.Hector) });
        }
        foreach (var t in Tourney)
            if (t.Count == 0) t.Add(Make(RiderSpecs.Enyo));
        if (TourneyTeam < 0 || TourneyTeam >= Tourney.Count) TourneyTeam = 0;
    }
}
