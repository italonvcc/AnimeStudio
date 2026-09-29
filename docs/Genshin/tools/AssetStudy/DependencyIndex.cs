using AnimeStudio;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

internal static class DependencyIndex
{
    public static int Run(string[] args)
    {
        if (args.Length != 4 || File.Exists(args[3])) throw new ArgumentException("--find-dependencies <manifest.json> <blocks folder> <new map.json>");
        var report = JObject.Parse(File.ReadAllText(args[1]));
        var remaining = report["unresolved"]!.Select(e => (File: ((string)e["TargetFile"]!).ToUpperInvariant(), PathID: long.Parse((string)e["TargetPathID"]!))).Distinct().ToHashSet();
        var found = new List<(AssetEntry Entry, string SerializedFile)>();
        var files = Directory.GetFiles(args[2], "*.blk", SearchOption.AllDirectories).OrderBy(p => p).ToArray();
        int scanned = 0;
        foreach (var batch in files.Chunk(48))
        {
            if (remaining.Count == 0) break;
            var matches = AssetsHelper.FindQualifiedReferences(batch, GameManager.GetGameByType(GameType.GI), remaining);
            found.AddRange(matches);
            foreach (var item in matches) remaining.Remove((item.SerializedFile.ToUpperInvariant(), item.Entry.PathID));
            scanned += batch.Length;
            Console.WriteLine($"{scanned}/{files.Length} source files; {found.Count} located, {remaining.Count} qualified references remaining");
        }
        var settings = new JsonSerializerSettings { Formatting = Formatting.Indented, Converters = { new StringEnumConverter() } };
        File.WriteAllText(args[3], JsonConvert.SerializeObject(new { GameType = "GI", AssetEntries = found.Select(p => p.Entry) }, settings));
        File.WriteAllText(args[3] + ".report.json", JsonConvert.SerializeObject(new { scanned, found, remaining }, settings));
        return remaining.Count == 0 ? 0 : 1;
    }
}
