using Godot;

namespace Titans;

// The plain mission front (owner, 2026-11-27: map-specific
// code lives in the map's own file): the shared relief
// recipe at full amplitude with mission macro swells, the
// pads flattened to their own plateau heights, and - on a
// city-kit contract - the district plateau at the centre
// with its street grid. The pads are the arena triangle
// scaled x7.75: r=620, 120 degrees apart.
public sealed class MissionMap : MapDef
{
  public static readonly Vector2[] PadsTable =
  {
        new(0f, 620f), new(-537f, -310f), new(537f, -310f)
    };
  public override Vector2[] Pads => PadsTable;

  private const float CityPlateauInnerFraction = 0.62f; // the district plateau is fully level inside this fraction of its radius

  public override float RawHeight(float x, float z)
  {
    float h = BaseRelief(x, z, true);
    // Flatten each spawn pad toward its own center height, so
    // a rider starts on level ground in its hollow.
    h = this.FlattenPads(h, x, z, true, 0f);
    // City district plateau (owner: Harbor Stacks should
    // read as actual streets and blocks): on a kit contract
    // the map center levels into a building platform - the
    // dome and the relief blend out across the district, so
    // the street grid sits flat.
    if (WorldConfig.CityKit >= 0)
    {
      float r = Mathf.Sqrt(x * x + z * z);
      if (r < CityGrid.PlateauR)
      {
        float h0 = BaseRelief(0f, 0f, true) - DomeHeight(0f, 0f);
        h = Mathf.Lerp(h, h0, TerrainMath.Smooth(CityGrid.PlateauR, CityGrid.PlateauR * CityPlateauInnerFraction, r));
      }
    }
    return h;
  }

  // Street grid of the city district (kit contracts): lanes
  // run between the building plots. Distance to the nearest
  // lane centerline; the sentinel outside the district or
  // off a kit contract.
  public override float StreetDistance(float x, float z)
  {
    if (WorldConfig.CityKit < 0)
    {
      return NoStreetDistance;
    }

    float p = CityGrid.Pitch;
    float ext = CityGrid.Radius + p;
    if (x * x + z * z > ext * ext)
    {
      return NoStreetDistance;
    }

    float yx = x / p - 0.5f, yz = z / p - 0.5f;
    float dx = Mathf.Abs(yx - Mathf.Round(yx)) * p;
    float dz = Mathf.Abs(yz - Mathf.Round(yz)) * p;
    return Mathf.Min(dx, dz);
  }
}
