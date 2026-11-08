using Godot;
using System.Collections.Generic;

// Contract data for the Mission path (front-end flow). Planets are
// picked on the orbital screen, contracts are signed in the bar.
// Pure data — the duel reads WorldConfig.SelectedMission.
public sealed class PlanetDef
{
    public string Name;         // "Ares Prime"
    public string Subtitle;     // "Desert war world · Lyran Marches"
    public string Intel;        // orbital-screen info column
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
    public PlanetDef Planet;
}

public static class MissionCatalog
{
    public static readonly List<PlanetDef> Planets = Build();

    private static MissionDef M(PlanetDef p, string name, string site, float sx, float sy,
        string type, string employer, long pay, int threat, string briefing,
        string patron, string pitch, WorldConfig.BiomeKind biome, int cityKit = -1)
    {
        var m = new MissionDef
        {
            Name = name, Site = site, SitePos = new Vector2(sx, sy), Type = type,
            Employer = employer, Payment = pay, Threat = threat, Briefing = briefing,
            Patron = patron, Pitch = pitch, Biome = biome, CityKit = cityKit, Planet = p
        };
        p.Missions.Add(m);
        return m;
    }

    private static List<PlanetDef> Build()
    {
        var ares = new PlanetDef
        {
            Name = "Ares Prime",
            Subtitle = "Desert war world · Inner Marches",
            Intel = "Ares Prime changed hands four times in the last decade and the maps never caught up. " +
                    "House Asterion holds the northern forts; House Corvane raids out of the southern flats. " +
                    "Every garrison on the planet is hiring, and none of them ask questions.",
            BaseColor = new Color(0.36f, 0.16f, 0.08f),
            LandColor = new Color(0.72f, 0.42f, 0.20f),
            PolarColor = new Color(0.85f, 0.66f, 0.42f)
        };
        M(ares, "Checkmate at Kestrel Flats", "Kestrel Flats", -0.38f, 0.30f,
            "Base Assault", "House Asterion", 480000, 4,
            "Move overland to the Kestrel Flats depot and destroy the Corvane garrison before their relief column arrives. " +
            "Rally in the depot courtyard. Ensure the safety of Liaison Officer Petra Davion — the new contract depends on her signature.",
            "Colonel Mara Voss", "Asterion needs that depot rubble by dawn. You look like dawn to me.", WorldConfig.BiomeKind.Desert);
        M(ares, "Resupply Run", "Fort Asterion", 0.34f, -0.18f,
            "Convoy Escort", "Asterion Logistics", 210000, 2,
            "A supply convoy crawls from Fort Asterion to the front at first light. Raiders have hit the last two. " +
            "Walk with the trucks, keep the machines off them, and the fort pays a clean bonus per truck that arrives.",
            "Quartermaster Ilya Brandt", "Simple work: walk next to trucks, shoot what I point at. Mostly trucks arrive.", WorldConfig.BiomeKind.Desert);
        M(ares, "The Shattered Shelf", "Shattered Shelf", 0.28f, 0.40f,
            "Recon Sweep", "Boreas Research Trust", 180000, 2,
            "A Trust survey team went silent on the Shattered Shelf six days ago. Cross the shelf, find their " +
            "beacon, and bring back the data core — or word of what found them first.",
            "Dr. Ingrid Halvorsen", "Six days of silence. The ice keeps secrets; bring me back one of them.", WorldConfig.BiomeKind.Desert);
        var thalassa = new PlanetDef
        {
            Name = "Thalassa",
            Subtitle = "Oceanic industrial world · Rimward Trade Lane",
            Intel = "Nine-tenths ocean, and the tenth that matters is refinery steel. Thalassa's platforms pump the " +
                    "fuel every House in the sector burns, which makes them worth raiding weekly. The Combine pays " +
                    "well and lies about the weather.",
            BaseColor = new Color(0.05f, 0.20f, 0.30f),
            LandColor = new Color(0.16f, 0.48f, 0.44f),
            PolarColor = new Color(0.75f, 0.88f, 0.90f)
        };
        M(thalassa, "Hold Port Nereid", "Port Nereid", -0.30f, -0.34f,
            "Base Defense", "Thalassa Combine", 390000, 4,
            "Pirates riding out of the storm belt hit Port Nereid at every low tide. Hold the port until the " +
            "Combine's own lance lands — three days of raids, paid per day held, and the port bar tab is on them.",
            "Portmaster Selene Kallis", "They come with the tide, rider. Be the rock they break on.", WorldConfig.BiomeKind.City);
        M(thalassa, "Platform Nine", "Helix Platforms", 0.42f, 0.22f,
            "Raid", "House Corvane", 520000, 5,
            "Helix Platform Nine refines for Asterion now. Corvane wants it dark: cross the flats at low tide, " +
            "drop the platform's defense riders, and be gone before the response fleet clears the horizon.",
            "A Corvane spymaster", "Names are a luxury neither of us can afford. The credits, however, are real.", WorldConfig.BiomeKind.City);
        M(thalassa, "The Vostok Claim", "Vostok Yards", 0.10f, 0.52f,
            "Duel Challenge", "Vostok Yards Combine", 350000, 3,
            "The Combine settles salvage claims the old way: one rider against theirs, in the yards, at noon. " +
            "Their champion rides a Hector-class frame and has never lost a claim. The purse says that changes today.",
            "Fixer known as 'Ledger'", "No armies, no ambush. One machine against one machine. My money is flexible.", WorldConfig.BiomeKind.City);
        M(thalassa, "The Harbor Stacks", "Port Nereid Stacks", -0.60f, 0.28f,
            "Base Defense", "Thalassa Combine", 390000, 4,
            "Port Nereid's harbor stacks rise straight out of the water — ten thousand windows over the " +
            "docks, and pirates riding the storm belt hit them at every low tide. Hold the stacks until " +
            "the Combine's own lance lands. The port bar tab is on them.",
            "Portmaster Selene Kallis", "They come with the tide, rider. Be the rock they break on.", WorldConfig.BiomeKind.City, 4);
        M(thalassa, "Gate City", "Glacier Gate City", 0.58f, -0.42f,
            "Base Assault", "House Asterion", 560000, 5,
            "A machine nest has taken Gate City, a walled port town on the trade lane, and grows " +
            "bolder every week. Asterion wants the city cleared and the nest burned out, whatever it " +
            "costs in frames. The largest purse on the planet.",
            "Colonel Mara Voss", "This one is not business, rider. It is a warning we are sending uphill.", WorldConfig.BiomeKind.City, 8);
        var kryos = new PlanetDef
        {
            Name = "Kryos",
            Subtitle = "Ice world · The Far Reaches",
            Intel = "Kryos keeps what it kills: riders frozen mid-stride still stand in the passes from the first " +
                    "landings. The research stations here pay in hard currency and salvage rights, and the machine " +
                    "remnants in the deep ice are waking up more often than the brochures admit.",
            BaseColor = new Color(0.55f, 0.65f, 0.75f),
            LandColor = new Color(0.82f, 0.88f, 0.94f),
            PolarColor = new Color(0.97f, 0.99f, 1.0f)
        };
        M(kryos, "Boreas Must Not Fall", "Boreas Station", -0.44f, -0.10f,
            "Base Defense", "Boreas Research Trust", 430000, 4,
            "Something is coming down out of the northern ice in numbers the Trust won't say out loud. Hold " +
            "Boreas Station's perimeter for one night cycle. Salvage rights on everything you drop are yours.",
            "Dr. Ingrid Halvorsen", "We are scientists, not soldiers. Whatever walks out of that ice, please stop it.", WorldConfig.BiomeKind.Forest);
        M(kryos, "Glacier Gate", "Glacier Gate", 0.48f, -0.42f,
            "Base Assault", "House Asterion", 560000, 5,
            "A machine nest has taken the Glacier Gate pass and grows bolder every week. Asterion wants the pass " +
            "cleared and the nest burned out, whatever it costs in frames. The largest purse on the planet.",
            "Colonel Mara Voss", "This one is not business, rider. It is a warning we are sending uphill.", WorldConfig.BiomeKind.Forest);
        M(kryos, "Ceto Intercept", "Ceto Refinery", 0.02f, -0.55f,
            "Convoy Intercept", "Free Captains' League", 260000, 3,
            "A Combine fuel convoy leaves the Ceto refinery under light escort. The League wants the fuel, not a " +
            "war: disable the escorts, take the tankers intact, and nobody's House colors get involved.",
            "Captain Rurik Hale", "Fuel is money that burns, friend. Help me carry some of it away.", WorldConfig.BiomeKind.Forest);

        return new List<PlanetDef> { ares, thalassa, kryos };
    }
}
