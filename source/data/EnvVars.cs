namespace Titans.Definitions;

// The AOT_* environment variables: harness seeds that force a
// selection the flow or the contract would otherwise make
// (planet, biome, sky, feature flags). The NAMES are constants
// (owner, 2026-11-27) so every read site is searchable and a
// typo is a compile error, not a silently-unset variable; the
// parse helpers below are the one place that knows how a
// variable's text becomes a value.
public static class EnvVars
{
  public const string Planet = "AOT_PLANET";
  public const string Mission = "AOT_MISSION";
  public const string Arena = "AOT_ARENA";
  public const string FromFlow = "AOT_FROMFLOW";
  public const string Mode = "AOT_MODE";
  // The AOT_MODE texts the harness boots with (external data,
  // set by the run scripts - the MapMode enum is what the
  // parse in WorldConfig produces from them).
  public const string ModeMission = "mission";
  public const string ModeTournament = "tournament";
  public const string Biome = "AOT_BIOME";
  public const string CityKit = "AOT_CITYKIT";
  public const string Sky = "AOT_SKY";
  public const string SkyTile = "AOT_SKYTILE";
  public const string Stars = "AOT_STARS";
  public const string Snow = "AOT_SNOW";
  public const string Ascent = "AOT_ASCENT";
  public const string Hills = "AOT_HILLS";
  public const string Photoreal = "AOT_PHOTOREAL";
  public const string Flat = "AOT_FLAT";
  public const string Sniper = "AOT_SNIPER";
  public const string TerraTest = "AOT_TERRATEST";
  public const string Hector = "AOT_HECTOR";
  public const string Slope = "AOT_SLOPE";

  // The variable's raw text, or null when unset.
  public static string Value(string name) =>
      System.Environment.GetEnvironmentVariable(name);

  // True when the variable is present at all (even empty).
  public static bool IsSet(string name) => !string.IsNullOrEmpty(Value(name));

  // The flag form: only "1" counts as on.
  public static bool Flag(string name) => Value(name) == "1";

  // The index form: the variable's integer, or the fallback
  // when it is unset or not a number. (This replaces the
  // per-variable parse snippets - including the old arena
  // switch that spelled "1" => 1, "2" => 2 out by hand.)
  public static int ParseIndex(string name, int fallback) =>
      int.TryParse(Value(name), out int v) ? v : fallback;
}
