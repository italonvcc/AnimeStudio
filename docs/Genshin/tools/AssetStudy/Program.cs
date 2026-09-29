using AnimeStudio;
using System.Text.RegularExpressions;
using System.Text.Json;

if (args.FirstOrDefault() == "--prepare-character")
{
    if (args.Length != 2 || ResourceMap.FromFile(args[1]) < 0) return 2;
    var references = GenshinCharacterReferences.Prepare(args[1], ResourceMap.GetEntries(), Console.WriteLine);
    Console.WriteLine($"{references.GameVersion}: {references.Entries.Count} entries; {references.CacheDirectory}");
    return 0;
}

if (args.FirstOrDefault() == "--audio-events") return AudioStudy.Run(args);
if (args.FirstOrDefault() == "--audio-media") return AudioStudy.FindMedia(args);
if (args.FirstOrDefault() == "--find-dependencies") return DependencyIndex.Run(args);
if (args.FirstOrDefault() == "--reindex") return await DiscoveryIndex.Run(args);
if (args.Length != 3 && !(args.Length == 4 && (args[3] == "--inspect" || args[3] == "--resolve")))
{
    Console.Error.WriteLine("Usage: AssetStudy <asset-map.map|json> <name/container/path-ID regex> <report.json> [--inspect|--resolve]");
    return 2;
}
try
{
    var input = Path.GetFullPath(args[0]);
    if (Path.GetExtension(input).ToLowerInvariant() is not (".map" or ".json")) throw new ArgumentException("Expected a .map or .json asset map.");
    var output = Path.GetFullPath(args[2]);
    if (string.Equals(input, output, StringComparison.OrdinalIgnoreCase))
        throw new ArgumentException("Report must not overwrite its input map.");
    if (File.Exists(output)) throw new ArgumentException("Report already exists; choose a new output path.");
    var pattern = new Regex(args[1], RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
    if (ResourceMap.FromFile(input) < 0)
        throw new InvalidDataException("AnimeStudio could not read the map.");
    var entries = ResourceMap.GetEntries().ToList();
    var matches = entries.Where(e => pattern.IsMatch(e.Name ?? "") || pattern.IsMatch(e.Container ?? "") || pattern.IsMatch(e.PathID.ToString(System.Globalization.CultureInfo.InvariantCulture))).ToList();
    var inspections = args.Length == 4 ? Inspect.Run(matches, args[3] == "--resolve") : null;
    var report = new
    {
        schemaVersion = 1,
        inspections,
        dependencyResolution = Inspect.LastResolution,
        map = Path.GetFileName(input),
        query = args[1],
        game = ResourceMap.GetGameType().ToString(),
        totalEntries = entries.Count,
        matchedEntries = matches.Count,
        warning = "Name/container/path-ID matches are candidates, not proof of character ownership or complete dependencies.",
        entries = matches.Select(e => new { e.Name, e.Container, e.Source, PathID = e.PathID.ToString(System.Globalization.CultureInfo.InvariantCulture), Type = e.Type.ToString(), e.Offset, e.Hash })
    };
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    // Fail rather than silently overwrite a previous investigation.
    using var destination = new FileStream(output, FileMode.CreateNew);
    JsonSerializer.Serialize(destination, report, new JsonSerializerOptions { WriteIndented = true, IncludeFields = true });
    Console.WriteLine($"{matches.Count} candidates from {entries.Count} entries -> {output}");
    if (inspections != null && inspections.Count != matches.Count)
    {
        Console.Error.WriteLine($"Inspected {inspections.Count} objects for {matches.Count} map entries; inspect the report for missing or ambiguous matches.");
        return 1;
    }
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
