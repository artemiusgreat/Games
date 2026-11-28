using Godot;
using System.Collections.Generic;

namespace Titans;

// Factory for the per-rider breakable-part layouts (owner,
// 2026-11-27: the layouts live one per file in this folder,
// structured like the maps in source/maps/). RiderParts owns
// the shared part machinery and asks here for the layout of
// the rider being built.
public static class RiderLayouts
{
    private static readonly Dictionary<RiderId, System.Action<RiderParts, Dictionary<string, MeshInstance3D>>> All = new()
    {
        { RiderId.Enyo, EnyoParts.Build },
        { RiderId.Hector, HectorParts.Build },
        { RiderId.Prometheus, PrometheusParts.Build },
        { RiderId.Medea, MedeaParts.Build },
        { RiderId.Demeter, DemeterParts.Build },
        { RiderId.Theseus, TheseusParts.Build },
        { RiderId.Hermes, HermesParts.Build },
        { RiderId.Apollo, ApolloParts.Build },
        { RiderId.Hades, HadesParts.Build },
        { RiderId.Ares, AresParts.Build },
        { RiderId.Amadeus, AmadeusParts.Build },
    };

    public static bool TryBuild(RiderId id, RiderParts parts, Dictionary<string, MeshInstance3D> byName)
    {
        if (!All.TryGetValue(id, out var build)) return false;
        build(parts, byName);
        return true;
    }
}
