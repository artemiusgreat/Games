// Round 2 code-first asset import tool (Probe 1).
// Uses Stride's OWN importer (ThreeDAssetImporter, Assimp-based) to import a
// source model file and writes the resulting .sdm3d/.sdskel/.sdanim/.sdmat/
// .sdtex YAML asset descriptors with Stride's AssetFileSerializer — the same
// code path Game Studio's import dialog uses, driven from a console app.
// Usage: ImportTool <source model file abs path> <output assets dir>
// NOTE: imported assets are NOT compiled into the game build until they are
// also listed in the package .sdpkg RootAssets (code-loaded assets have no
// reference closure from the scene) and the game project references the
// Stride.Assets.Models package. Source paths in the emitted YAML are
// absolute; rewrite them to "!file ../Resources/..." relative form before
// committing (see the Fox assets in StridePoc/Assets).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Stride.Core.Assets;
using Stride.Core.Diagnostics;
using Stride.Core.IO;
using Stride.Assets.Models;

class Program
{
    static int Main(string[] args)
    {
        var srcPath = args[0];
        var outDir = args[1];
        GlobalLogger.GlobalMessageLogged += (m) => { if (m.Type >= LogMessageType.Warning) Console.WriteLine($"[LOG {m.Type} {m.Module}] {m.Text}"); };
        var logger = GlobalLogger.GetLogger("ImportTool");

        // Force-load every Stride assembly so AssemblyScan module initializers
        // register asset factories/importers/serializers (a bare console app
        // otherwise has none registered and package/import calls fail).
        var root = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/.nuget/packages";
        int loaded = 0;
        foreach (var dll in Directory.GetFiles(root, "Stride.*.dll", SearchOption.AllDirectories).Where(f => f.Contains("lib/net10.0") || f.Contains("lib/netstandard")))
        {
            try { Assembly.LoadFrom(dll); loaded++; } catch { }
        }
        Console.WriteLine($"Force-loaded {loaded} Stride assemblies. Registered importers: {AssetRegistry.RegisteredImporters.Count()}");

        var importer = new ThreeDAssetImporter();
        var parameters = new AssetImporterParameters(importer.RootAssetTypes.Concat(importer.AdditionalAssetTypes).ToArray());
        parameters.Logger = logger;
        Console.WriteLine($"Importing {srcPath} ...");
        List<AssetItem> items = importer.Import(new UFile(srcPath), parameters).ToList();
        Console.WriteLine($"Imported {items.Count} items:");
        foreach (var it in items)
            Console.WriteLine($"  loc={it.Location} [{it.Asset.GetType().FullName}] id={it.Id}");

        Directory.CreateDirectory(outDir);
        foreach (var it in items)
        {
            var ext = AssetRegistry.GetDefaultExtension(it.Asset.GetType());
            var outPath = Path.Combine(outDir, it.Location.GetFileName() + ext);
            AssetFileSerializer.Save(outPath, it.Asset, null, logger);
            Console.WriteLine($"Saved {outPath}");
        }
        return 0;
    }
}
