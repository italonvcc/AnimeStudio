using AnimeStudio;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static class DiscoveryIndex
{
    public static async Task<int> Run(string[] args)
    {
        if (args.Length != 4) throw new ArgumentException("--reindex <seed-report.json> <new-map-directory> <name-regex>");
        using var report = JsonDocument.Parse(File.ReadAllText(args[1]));
        var files = report.RootElement.GetProperty("entries").EnumerateArray().Select(e => e.GetProperty("Source").GetString()!).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (files.Length is < 1 or > 64 || files.Any(f => !File.Exists(f))) throw new ArgumentException("Use a seed report with 1-64 existing source files.");
        string destination = Path.GetFullPath(args[2]);
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Choose a new map directory.");
        var regex = new Regex(args[3], RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
        Directory.CreateDirectory(destination);
        // The fork owns all bundle and object parsing. Read names/headers only and
        // include GameObject/Mesh types omitted from the original export-oriented map.
        await AssetsHelper.BuildAssetMap(files, "scene-index", GameManager.GetGameByType(GameType.GI), destination, ExportListType.JSON,
            new[] { ClassIDType.GameObject, ClassIDType.Mesh, ClassIDType.Animator }, new[] { regex }, mergeSplitAssets: false);
        var outputs = Directory.GetFiles(destination, "*.json");
        if (outputs.Length != 1 || ResourceMap.FromFile(outputs[0]) < 0) throw new IOException("Scene index was not produced successfully.");
        Console.WriteLine($"Indexed {ResourceMap.GetEntries().Count()} scene/mesh objects from {files.Length} source files -> {outputs[0]}");
        return 0;
    }
}
