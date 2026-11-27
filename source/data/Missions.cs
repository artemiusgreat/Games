using Godot;
using System.Collections.Generic;

// Contract data for the Mission path (front-end flow). Planets are
// picked on the orbital screen, contracts are signed in the bar.
// Pure data - the duel reads WorldConfig.SelectedMission.
public sealed class PlanetDef
{
    public string System;       // "Union" - the country layer (owner, 2026-11-20 naming convention)
    public string Name;         // "New York" - a state/province of the system
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
    // A seamless sky TILE (owner, 2026-11-25) repeated over
    // the dome by the triplanar tiled-sky shader; when set
    // it takes precedence over SkyTex. "" = no tile.
    public string SkyTile = "";
    // Procedural star layer over the painted sky (owner:
    // hybrid sky - the photo carries the nebula, a sky shader
    // adds resolution-independent stars). Atlantis only.
    public bool ProceduralStars;
    // Flat maps (owner): the contract's world builds with no
    // height map at all - every street level. The Harbor Stacks.
    public bool FlatTerrain;
    // Snow-covered ground (owner): the map's ground matches
    // a snowy painted sky (Gate City under the ice panorama).
    public bool SnowGround;
    // Three terrace levels with ramps and lifts, plus an
    // island district on a bridge (owner: Harbor rebuild).
    public bool Terraces;
    // The Ascent campaign map (owner, 2026-11-20): the
    // valley-to-summit world with village guard groups (maps/Babylon.cs).
    public bool Ascent;
    // Always on the offers list, regardless of story
    // progression (owner, 2026-11-20: Anabasis, so he can
    // test the new map without playing through to it).
    public bool AlwaysAvailable;
    // The photoreal treatment (owner, 2026-11-20): the
    // photo scene's splat terrain, scanned trees and
    // environment tune applied in the standard scene.
    // Consolidated on ONE contract - Babylon - so the
    // improvements are judged in a single place.
    public bool Photoreal;
    // Hills terrain (owner, 2026-11-22): the map's heightfield
    // comes from the grid baked out of his Maps-Hills OBJ
    // (assets/hills/) instead of the analytic valley - the
    // winter dressing (snow, galaxy sky) is unchanged.
    public bool Hills;
    public PlanetDef Planet;
}

public static class MissionCatalog
{
    public static readonly List<PlanetDef> Planets = Build();

    private static MissionDef M(PlanetDef p, string name, string site, float sx, float sy,
        string type, string employer, long pay, int threat, string briefing,
        string patron, string pitch, WorldConfig.BiomeKind biome, int cityKit = -1,
        bool flat = false, string sky = "", bool snow = false, bool terraces = false,
        bool stars = false, bool ascent = false, bool alwaysAvailable = false,
        bool photoreal = false, bool hills = false, string tile = "")
    {
        var m = new MissionDef
        {
            Name = name, Site = site, SitePos = new Vector2(sx, sy), Type = type,
            Employer = employer, Payment = pay, Threat = threat, Briefing = briefing,
            Patron = patron, Pitch = pitch, Biome = biome, CityKit = cityKit,
            FlatTerrain = flat, SkyTex = sky, SkyTile = tile, SnowGround = snow, Terraces = terraces, Planet = p,
            ProceduralStars = stars, Ascent = ascent, AlwaysAvailable = alwaysAvailable,
            Photoreal = photoreal, Hills = hills
        };
        p.Missions.Add(m);
        return m;
    }

    private static List<PlanetDef> Build()
    {
        var ares = new PlanetDef
        {
            System = "Kingdom",
            Name = "Yorkshire",
            MapPos = new Vector2(0.24f, 0.70f),
            Subtitle = "Desert war world · The Brass Marches",
            Intel = "Yorkshire changed hands four times in the last decade and the maps never caught up. " +
                    "House Asterion holds the northern forts; House Corvane raids out of the southern flats. " +
                    "Every garrison on the planet is hiring, and none of them ask questions.",
            BaseColor = new Color(0.36f, 0.16f, 0.08f),
            LandColor = new Color(0.72f, 0.42f, 0.20f),
            PolarColor = new Color(0.85f, 0.66f, 0.42f)
        };
        M(ares, "York", "North Yorkshire", -0.38f, 0.30f,
            "Base Assault", "House Asterion", 480000, 4,
            "Move overland to the York depot and destroy the Corvane garrison before their relief column arrives. " +
            "Rally in the depot courtyard. Ensure the safety of Liaison Officer Petra Davion - the new mission depends on her signature.",
            "Colonel Mara Voss", "Asterion needs that depot rubble by dawn. You look like dawn to me.", WorldConfig.BiomeKind.Desert);
        M(ares, "Bath", "Somerset", 0.34f, -0.18f,
            "Convoy Escort", "Asterion Logistics", 210000, 2,
            "A supply convoy crawls from the Somerset depot to the front at first light. Raiders have hit the last two. " +
            "Walk with the trucks, keep the machines off them, and the fort pays a clean bonus per truck that arrives.",
            "Quartermaster Ilya Brandt", "Simple work: walk next to trucks, shoot what I point at. Mostly trucks arrive.", WorldConfig.BiomeKind.Desert);
        M(ares, "London", "Westminster", 0.52f, -0.52f,
            "Base Assault", "House Asterion", 480000, 5,
            "A Corvane garrison holds the Westminster depot and the road junction it guards. Cross the " +
            "flats, destroy the garrison riders, and hold the junction until Asterion's relief column " +
            "arrives. The depot's fuel pays for the next campaign - Asterion wants it intact.",
            "Colonel Mara Voss", "Westminster by dawn, rider. The depot stands - the garrison doesn't.", WorldConfig.BiomeKind.Desert);
        M(ares, "Oxford", "Oxfordshire", 0.28f, 0.40f,
            "Recon Sweep", "Boreas Research Trust", 180000, 2,
            "A Trust survey team went silent near Oxford six days ago. Cross the shelf, find their " +
            "beacon, and bring back the data core - or word of what found them first.",
            "Dr. Ingrid Halvorsen", "Six days of silence. The ice keeps secrets; bring me back one of them.", WorldConfig.BiomeKind.Desert);
        var thalassa = new PlanetDef
        {
            System = "Union",
            Name = "New York",
            MapPos = new Vector2(0.68f, 0.52f),
            Subtitle = "Harbor world · Rimward Trade Lane",
            Intel = "Nine-tenths ocean, and the tenth that matters is harbor steel. New York's platforms pump the " +
                    "fuel every House in the sector burns, which makes them worth raiding weekly. The Combine pays " +
                    "well and lies about the weather.",
            BaseColor = new Color(0.05f, 0.20f, 0.30f),
            LandColor = new Color(0.16f, 0.48f, 0.44f),
            PolarColor = new Color(0.75f, 0.88f, 0.90f)
        };
        // Babylon leads New York's list (owner, 2026-11-13):
        // it is the mission under active testing, so it is the
        // planet's first available mission in Story mode.
        M(thalassa, "Babylon", "Suffolk", -0.60f, 0.28f,
            "Valley Campaign", "Meridian Combine", 900000, 4,
            "The Suffolk valley climbs from the lowland mouth to a fortified camp on the summit " +
            "plateau - six villages on the way up, each with its own riders, two rivers to cross, " +
            "and ten defenders waiting on top. Take the villages one by one, slip past them, or " +
            "drive straight up the road: the contract pays when the summit falls and your team " +
            "still stands.",
            "Portmaster Selene Kallis", "The whole valley, rider, from the storm water to the summit camp. Bring me the top.", WorldConfig.BiomeKind.City, 4, flat: false, sky: Paths.SkyGalaxy, snow: true, stars: true, photoreal: true, ascent: true, hills: true, tile: Paths.SkyTileStars);
        M(thalassa, "Boston", "Middlesex", -0.30f, -0.34f,
            "Convoy Escort", "Meridian Combine", 275000, 3,
            "The Boston light is the only thing the fuel barges steer by on the night run, and raiders " +
            "have started shooting at the lamp house and picking the dark water for stragglers. Walk the " +
            "causeway with the convoy and keep the light burning until the last barge is in.",
            "Keeper Ana Serra", "Barges steer by my light, rider. Keep it lit - and keep me alive to tend it.", WorldConfig.BiomeKind.City, sky: Paths.SkyCoast);
        M(thalassa, "Albany", "Rensselaer", 0.42f, 0.22f,
            "Raid", "House Corvane", 520000, 5,
            "The Albany platforms refine for Asterion now. Corvane wants it dark: cross the flats at low tide, " +
            "drop the platform's defense riders, and be gone before the response fleet clears the horizon.",
            "A Corvane spymaster", "Names are a luxury neither of us can afford. The credits, however, are real.", WorldConfig.BiomeKind.City, sky: Paths.SkyCoast);
        M(thalassa, "Rochester", "Monroe", 0.10f, 0.52f,
            "Duel Challenge", "Rochester Yards", 350000, 3,
            "The Combine settles salvage claims the old way: one rider against theirs, in the yards, at noon. " +
            "Their champion rides a Hector-class frame and has never lost a claim. The purse says that changes today.",
            "Fixer known as 'Ledger'", "No armies, no ambush. One machine against one machine. My money is flexible.", WorldConfig.BiomeKind.City, sky: Paths.SkyCoast);
        M(thalassa, "Buffalo", "Erie", 0.58f, -0.42f,
            "Base Assault", "House Asterion", 560000, 5,
            "A machine nest has taken Buffalo, a walled port town on the trade lane, and grows " +
            "bolder every week. Asterion wants the city cleared and the nest burned out, whatever it " +
            "costs in frames. The largest purse on the planet.",
            "Colonel Mara Voss", "This one is not business, rider. It is a warning we are sending uphill.", WorldConfig.BiomeKind.City, 8, sky: Paths.SkyIce, snow: true);
        var kryos = new PlanetDef
        {
            System = "Teutonia",
            Name = "Bavaria",
            MapPos = new Vector2(0.44f, 0.20f),
            Subtitle = "Mountain ice world · The Far Reaches",
            Intel = "Bavaria keeps what it kills: riders frozen mid-stride still stand in the passes from the first " +
                    "landings. The research stations here pay in hard currency and salvage rights, and the machine " +
                    "remnants in the deep ice are waking up more often than the brochures admit.",
            BaseColor = new Color(0.55f, 0.65f, 0.75f),
            LandColor = new Color(0.82f, 0.88f, 0.94f),
            PolarColor = new Color(0.97f, 0.99f, 1.0f)
        };
        M(kryos, "Munich", "Upper Bavaria", -0.44f, -0.10f,
            "Base Defense", "Boreas Research Trust", 430000, 4,
            "Something is coming down out of the northern ice in numbers the Trust won't say out loud. Hold " +
            "the Munich perimeter for one night cycle. Salvage rights on everything you drop are yours.",
            "Dr. Ingrid Halvorsen", "We are scientists, not soldiers. Whatever walks out of that ice, please stop it.", WorldConfig.BiomeKind.Forest);
        M(kryos, "Nuremberg", "Middle Franconia", 0.48f, -0.42f,
            "Base Assault", "House Asterion", 540000, 5,
            "The Nuremberg pass is the only road out of the northern ice, and a machine nest has sealed it. " +
            "Asterion wants it open before the thaw strands three garrisons on the wrong side: go up the " +
            "mountain, clear the nest out of the narrows, and hold the road until their engineers arrive.",
            "Colonel Mara Voss", "Three garrisons are counting on that road, rider. Open it, and Asterion remembers its friends.", WorldConfig.BiomeKind.Forest);
        M(kryos, "Augsburg", "Swabia", 0.02f, -0.55f,
            "Convoy Intercept", "Free Captains' League", 260000, 3,
            "A Combine fuel convoy leaves the Augsburg refinery under light escort. The League wants the fuel, not a " +
            "war: disable the escorts, take the tankers intact, and nobody's House colors get involved.",
            "Captain Rurik Hale", "Fuel is money that burns, friend. Help me carry some of it away.", WorldConfig.BiomeKind.Forest);

        return new List<PlanetDef> { ares, thalassa, kryos };
    }
}
