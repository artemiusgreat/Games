// Resource paths (owner, 2026-11-27): every res:// reference
// the code loads is a named constant here, so a moved asset is
// one edit and every load site is searchable. Data tables keep
// their own paths as content (RiderSpec's per-rider models,
// MissionDef's skies) - those are data, not references.
public static class Paths
{
    // Scenes
    public const string FlowScene = "res://flow/Flow.tscn";
    public const string DuelScene = "res://Duel.tscn";

    // Fonts
    public const string CinzelBold = "res://assets/fonts/Cinzel-Bold.ttf";
    public const string CinzelRegular = "res://assets/fonts/Cinzel-Regular.ttf";
    public const string OpenSansRegular = "res://assets/fonts/OpenSans-Regular.ttf";
    public const string OpenSansSemiBold = "res://assets/fonts/OpenSans-SemiBold.ttf";

    // Flow and menu art
    public const string SpaceBackground = "res://assets/flow/space_bg.png";
    public const string TitleCard = "res://assets/flow/title_card.png";
    public const string CardTournament = "res://assets/flow/card_tournament.jpg";
    public const string CardMission = "res://assets/flow/card_mission.jpg";
    public const string LoadingTarget = "res://assets/ui/loading_target.png";
    public const string UniverseBackground = "res://assets/ui/universe_bg.jpg";

    // Skies (a mission/arena names a FILE inside SkiesDir; the
    // full path is built from the directory constant)
    public const string SkiesDir = "res://assets/skies/";
    public const string SkyJungle = SkiesDir + "sky_jungle.jpg";
    public const string TiledSkyShader = SkiesDir + "tiled_sky.gdshader";
    public const string StarLayerShader = SkiesDir + "star_layer.gdshader";

    // Sound effects and order voices
    public const string SfxLaser = "res://assets/sfx/laser.mp3";
    public const string SfxPpc = "res://assets/sfx/ppc.mp3";
    public const string SfxCannon = "res://assets/sfx/cannon.mp3";
    public const string SfxRocket = "res://assets/sfx/rocket.mp3";
    public const string SfxHit = "res://assets/sfx/hit.mp3";
    public const string SfxTree = "res://assets/sfx/tree.wav";
    public const string SfxExplosion = "res://assets/sfx/explosion.wav";
    public const string SfxStepA = "res://assets/sfx/step_a.wav";
    public const string SfxStepB = "res://assets/sfx/step_b.wav";
    public const string SfxThruster = "res://assets/sfx/thruster.wav";
    public const string SfxWind = "res://assets/sfx/wind.wav";
    public const string SfxServo = "res://assets/sfx/servo.wav";
    public const string SfxClick = "res://assets/sfx/click.wav";
    public const string VoxFollow = "res://assets/sfx/vox_follow.mp3";
    public const string VoxAttackTarget = "res://assets/sfx/vox_attack_target.mp3";
    public const string VoxAttackNearby = "res://assets/sfx/vox_attack_nearby.mp3";
    public const string VoxProtect = "res://assets/sfx/vox_protect.mp3";
    public const string VoxStop = "res://assets/sfx/vox_stop.mp3";

    // Rider models (RiderSpec's ModelPath fields reference
    // these - the path is a reference, not spec data)
    public const string EnyoModel = "res://assets/enyo.fbx";
    public const string HectorModel = "res://assets/hector.fbx";
    public const string PrometheusModel = "res://assets/prometheus.fbx";
    public const string MedeaModel = "res://assets/medea.fbx";
    public const string DemeterModel = "res://assets/demeter.fbx";
    public const string TheseusModel = "res://assets/theseus.fbx";
    public const string HermesModel = "res://assets/hermes.fbx";
    public const string ApolloModel = "res://assets/apollo.fbx";
    public const string HadesModel = "res://assets/hades.fbx";
    public const string AresModel = "res://assets/ares.fbx";
    public const string AmadeusModel = "res://assets/amadeus.fbx";

    // Mission skies (MissionDef's sky/tile fields reference these)
    public const string SkyGalaxy = SkiesDir + "sky_galaxy.jpg";
    public const string SkyCoast = SkiesDir + "sky_coast.jpg";
    public const string SkyIce = SkiesDir + "sky_ice.jpg";
    public const string SkyTileStars = SkiesDir + "sky_tile_stars.png";

    // World assets
    public const string HillsHeightmap = "res://assets/hills/hills_height.bin";
    public const string TerrainNormal = "res://photo/textures/aerial_rocks_02/aerial_rocks_02_nor_gl_2k.jpg";
    public const string TerrainArm = "res://photo/textures/aerial_rocks_02/aerial_rocks_02_arm_2k.jpg";
    public const string ExplosionSheet = "res://assets/fx/explosion_fireball.png";
    // {0} = kit number, {1} = building index inside the kit.
    public const string CityKitBuildingFormat = "res://assets/citykits/kit{0}/b_{1}.glb";
}
