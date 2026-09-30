using AnimeStudio;
using AnimeStudio.CLI;
using Newtonsoft.Json.Linq;
using System.Reflection;

internal static class MaterialSmoke
{
    public static int Run(string[] args)
    {
        if (args.Length != 3 || args[0] != "--materials")
            throw new ArgumentException("Usage: RegressionChecks --materials <AssetStudy report.json> <new output directory>");
        var output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output)) throw new ArgumentException("Use a new output directory to preserve previous results.");
        var entries = JObject.Parse(File.ReadAllText(args[1]))["entries"].ToObject<List<AssetEntry>>();
        if (entries.Count == 0 || entries.Count > 32 || entries.Any(e => e.Offset < 0))
            throw new ArgumentException("Expected 1-32 entries with known bundle offsets.");
        var manager = new AssetsManager { Game = GameManager.GetGameByType(GameType.GI) };
        manager.FilterData.Items = entries.Select(e => new AssetsManager.AssetFilterDataItem
        { Source = e.Source, Offset = e.Offset, PathID = e.PathID, Name = e.Name, Type = e.Type }).ToList();
        var exportMethod = typeof(AnimeStudio.CLI.Program).Assembly.GetType("AnimeStudio.CLI.Exporter")
            .GetMethod("ExportJSONFile", BindingFlags.Public | BindingFlags.Static)!;
        try
        {
            manager.LoadFiles(entries.Select(e => e.Source).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), mergeSplitAssets: false);
            var count = 0;
            foreach (var file in manager.assetsFileList)
            foreach (var material in file.ObjectsDic.Values.OfType<Material>())
            {
                if (!entries.Any(e => e.Type == ClassIDType.Material && e.PathID == material.m_PathID && string.Equals(e.Source, file.originalPath, StringComparison.OrdinalIgnoreCase))) continue;
                var folder = Path.Combine(output, file.fileName);
                Directory.CreateDirectory(folder);
                if (!(bool)exportMethod.Invoke(null, new object[] { new AssetItem(material), folder }))
                    throw new Exception("Production exporter did not write the material.");
                var json = JObject.Parse(File.ReadAllText(Path.Combine(folder, material.Name + ".json")));
                if ((string)json["m_Shader"]["Name"] != "miHoYo/Character/Character_Base_Uber" ||
                    (string)json["ShaderReference"]["NameSource"] != "ValidatedGenshinFooter" ||
                    (bool)json["ShaderReference"]["ParsedShader"])
                    throw new Exception("Unexpected shader identity/provenance in " + material.Name);
                count++;
            }
            if (count != entries.Count(e => e.Type == ClassIDType.Material))
                throw new Exception("Some selected materials were not exported.");
            Console.WriteLine($"PASS {count} real Mona material exports through AnimeStudio.CLI.Exporter.ExportJSONFile");
            return 0;
        }
        finally { manager.Clear(); }
    }
}
