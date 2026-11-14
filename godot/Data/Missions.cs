using Godot;
using System.Collections.Generic;

// Contract data for the Mission path (front-end flow). Planets are
// picked on the orbital screen, contracts are signed in the bar.
// Pure data — the duel reads WorldConfig.SelectedMission.
public sealed class PlanetDef
{
    public string Name;         // "Chronos"
    public string Subtitle;     // "Desert war world · Lyran Marches"
    public string Intel;        // orbital-screen info column
    public Vector2 MapPos;      // position on the universe map (0..1, y down)
    public Color BaseColor;     // planet texture: deep ground
    public Color LandColor;     // planet texture: continents
    public Color PolarColor;    // planet texture: caps / highlands
    public List<MissionDef> Missions = new();
}

public sealed class MissionDef
{
    public string Name;         // "Checkmate at Kestrel Flats"
    public string Site;         // marker label on the planet disc
    public Vector2 SitePos;     // disc-relative marker position (-1..1)
    public string Type;         // "Base Assault"
    public string Employer;     // "House Asterion"
    public long Payment;        // credits
    public int Threat;          // 1..5
    public string Briefing;     // two or three sentences
    public string Patron;       // who offers it in the bar
    public string Pitch;        // the patron's one-line offer
    // The ground the contract is fought on (owner: predefined
    // per contract, never a player choice).
    public WorldConfig.BiomeKind Biome;
    // The building kit the contract's city is built from
    // (owner: one of the supplied city-map archives, converted
    // to assets/citykits/kit<N>/). -1 = procedural buildings.
    // City contracts only.
    public int CityKit = -1;
    // A painted sky for the map (owner: supplied images, the
    // darker/colder ones) as a res:// path to an equirect
    // texture in assets/skies/. "" = the procedural sky.
    public string SkyTex = "";
    // Procedural star layer over the painted sky (owner:
    // hybrid sky — the photo carries the nebula, a sky shader
    // adds resolution-independent stars). Atlantis only.
    public bool ProceduralStars;
    // Flat maps (owner): the contract's world builds with no
    // height map at all — every street level. The Harbor Stacks.
    public bool FlatTerrain;
    // Snow-covered ground (owner): the map's ground matches
    // a snowy painted sky (Gate City under the ice panorama).
    public bool SnowGround;
    // Procedural backdrop peaks (owner): off where a
    // painted horizon replaces them (Harbor under its sky).
    public bool BackdropPeaks = true;
    // Three terrace levels with ramps and lifts, plus an
    // island district on a bridge (owner: Harbor rebuild).
    public bool Terraces;
    public PlanetDef Planet;
}

public static class MissionCatalog
{
    public static readonly List<PlanetDef> Planets = Build();

    private static MissionDef M(PlanetDef p, string name, string site, float sx, float sy,
        string type, string employer, long pay, int threat, string briefing,
        string patron, string pitch, WorldConfig.BiomeKind biome, int cityKit = -1,
        bool flat = false, string sky = "", bool snow = false, bool peaks = true, bool terraces = false,
        bool stars = false)
    {
        var m = new MissionDef
        {
            Name = name, Site = site, SitePos = new Vector2(sx, sy), Type = type,
            Employer = employer, Payment = pay, Threat = threat, Briefing = briefing,
            Patron = patron, Pitch = pitch, Biome = biome, CityKit = cityKit,
            FlatTerrain = flat, SkyTex = sky, SnowGround = snow, BackdropPeaks = peaks, Terraces = terraces, Planet = p,
            ProceduralStars = stars
        };
        p.Missions.Add(m);
        return m;
    }

    private static List<PlanetDef> Build()
    {
        var ares = new PlanetDef
        {
            Name = "Chronos",
            MapPos = new Vector2(0.24f, 0.70f),
            Subtitle = "Desert war world · The Brass Marches",
            Intel = "Chronos changed hands four times in the last decade and the maps never caught up. " +
                    "House Asterion holds the northern forts; House Corvane raids out of the southern flats. " +
                    "Every garrison on the planet is hiring, and none of them ask questions.",
            BaseColor = new Color(0.36f, 0.16f, 0.08f),
            LandColor = new Color(0.72f, 0.42f, 0.20f),
            PolarColor = new Color(0.85f, 0.66f, 0.42f)
        };
        M(ares, "Troy", "Troy", -0.38f, 0.30f,
            "Base Assault", "House Asterion", 480000, 4,
            "Move overland to the Troy depot and destroy the Corvane garrison before their relief column arrives. " +
            "Rally in the depot courtyard. Ensure the safety of Liaison Officer Petra Davion — the new mission depends on her signature.",
            "Colonel Mara Voss", "Asterion needs that depot rubble by dawn. You look like dawn to me.", WorldConfig.BiomeKind.Desert);
        M(ares, "Syracuse", "Athens", 0.34f, -0.18f,
            "Convoy Escort", "Asterion Logistics", 210000, 2,
            "A supply convoy crawls from Athens to the front at first light. Raiders have hit the last two. " +
            "Walk with the trucks, keep the machines off them, and the fort pays a clean bonus per truck that arrives.",
            "Quartermaster Ilya Brandt", "Simple work: walk next to trucks, shoot what I point at. Mostly trucks arrive.", WorldConfig.BiomeKind.Desert);
        M(ares, "Erebus", "Erebus", 0.28f, 0.40f,
            "Recon Sweep", "Boreas Research Trust", 180000, 2,
            "A Trust survey team went silent on Erebus six days ago. Cross the shelf, find their " +
            "beacon, and bring back the data core — or word of what found them first.",
            "Dr. Ingrid Halvorsen", "Six days of silence. The ice keeps secrets; bring me back one of them.", WorldConfig.BiomeKind.Desert);
        var thalassa = new PlanetDef
        {
            Name = "Meridian",
            MapPos = new Vector2(0.68f, 0.52f),
            Subtitle = "Harbor world · Rimward Trade Lane",
            Intel = "Nine-tenths ocean, and the tenth that matters is harbor steel. Meridian's platforms pump the " +
                    "fuel every House in the sector burns, which makes them worth raiding weekly. The Combine pays " +
                    "well and lies about the weather.",
            BaseColor = new Color(0.05f, 0.20f, 0.30f),
            LandColor = new Color(0.16f, 0.48f, 0.44f),
            PolarColor = new Color(0.75f, 0.88f, 0.90f)
        };
        // Atlantis leads Meridian's list (owner, 2026-11-13):
        // it is the mission under active testing, so it is the
        // planet's first available mission in Story mode.
        M(thalassa, "Atlantis", "Atlantis", -0.60f, 0.28f,
            "Base Defense", "Meridian Combine", 390000, 4,
            "The Atlantis stacks rise straight out of the storm water — ten thousand windows over the " +
            "docks, and pirates riding the storm belt hit them at every low tide. Hold the stacks until " +
            "the Combine's own lance lands. The port bar tab is on them.",
            "Portmaster Selene Kallis", "They come with the tide, rider. Be the rock they break on.", WorldConfig.BiomeKind.City, 4, flat: false, sky: "res://assets/skies/sky_galaxy.jpg", snow: true, peaks: false, terraces: true, stars: true);
        M(thalassa, "Pharos", "Pharos", -0.30f, -0.34f,
            "Base Defense", "Meridian Combine", 390000, 4,
            "Pirates riding out of the storm belt hit Pharos at every low tide. Hold the port until the " +
            "Combine's own lance lands — three days of raids, paid per day held, and the port bar tab is on them.",
            "Portmaster Selene Kallis", "They come with the tide, rider. Be the rock they break on.", WorldConfig.BiomeKind.City, sky: "res://assets/skies/sky_coast.jpg");
        M(thalassa, "Helios", "Helios", 0.42f, 0.22f,
            "Raid", "House Corvane", 520000, 5,
            "The Helios platforms refine for Asterion now. Corvane wants it dark: cross the flats at low tide, " +
            "drop the platform's defense riders, and be gone before the response fleet clears the horizon.",
            "A Corvane spymaster", "Names are a luxury neither of us can afford. The credits, however, are real.", WorldConfig.BiomeKind.City, sky: "res://assets/skies/sky_coast.jpg");
        M(thalassa, "Argos", "Argos", 0.10f, 0.52f,
            "Duel Challenge", "Argos Yards", 350000, 3,
            "The Combine settles salvage claims the old way: one rider against theirs, in the yards, at noon. " +
            "Their champion rides a Hector-class frame and has never lost a claim. The purse says that changes today.",
            "Fixer known as 'Ledger'", "No armies, no ambush. One machine against one machine. My money is flexible.", WorldConfig.BiomeKind.City, sky: "res://assets/skies/sky_coast.jpg");
        M(thalassa, "Elysium", "Elysium", 0.58f, -0.42f,
            "Base Assault", "House Asterion", 560000, 5,
            "A machine nest has taken Elysium, a walled port town on the trade lane, and grows " +
            "bolder every week. Asterion wants the city cleared and the nest burned out, whatever it " +
            "costs in frames. The largest purse on the planet.",
            "Colonel Mara Voss", "This one is not business, rider. It is a warning we are sending uphill.", WorldConfig.BiomeKind.City, 8, sky: "res://assets/skies/sky_ice.jpg", snow: true);
        var kryos = new PlanetDef
        {
            Name = "Orion",
            MapPos = new Vector2(0.44f, 0.20f),
            Subtitle = "Mountain ice world · The Far Reaches",
            Intel = "Orion keeps what it kills: riders frozen mid-stride still stand in the passes from the first " +
                    "landings. The research stations here pay in hard currency and salvage rights, and the machine " +
                    "remnants in the deep ice are waking up more often than the brochures admit.",
            BaseColor = new Color(0.55f, 0.65f, 0.75f),
            LandColor = new Color(0.82f, 0.88f, 0.94f),
            PolarColor = new Color(0.97f, 0.99f, 1.0f)
        };
        M(kryos, "Olympus", "Olympus", -0.44f, -0.10f,
            "Base Defense", "Boreas Research Trust", 430000, 4,
            "Something is coming down out of the northern ice in numbers the Trust won't say out loud. Hold " +
            "the Olympus perimeter for one night cycle. Salvage rights on everything you drop are yours.",
            "Dr. Ingrid Halvorsen", "We are scientists, not soldiers. Whatever walks out of that ice, please stop it.", WorldConfig.BiomeKind.Forest);
        M(kryos, "Tartarus", "Tartarus", 0.48f, -0.42f,
            "Base Assault", "House Asterion", 560000, 5,
            "A machine nest has taken the Tartarus pass and grows bolder every week. Asterion wants the pass " +
            "cleared and the nest burned out, whatever it costs in frames. The largest purse on the planet.",
            "Colonel Mara Voss", "This one is not business, rider. It is a warning we are sending uphill.", WorldConfig.BiomeKind.Forest);
        M(kryos, "Arcadia", "Arcadia", 0.02f, -0.55f,
            "Convoy Intercept", "Free Captains' League", 260000, 3,
            "A Combine fuel convoy leaves the Arcadia refinery under light escort. The League wants the fuel, not a " +
            "war: disable the escorts, take the tankers intact, and nobody's House colors get involved.",
            "Captain Rurik Hale", "Fuel is money that burns, friend. Help me carry some of it away.", WorldConfig.BiomeKind.Forest);

        return new List<PlanetDef> { ares, thalassa, kryos };
    }
}
