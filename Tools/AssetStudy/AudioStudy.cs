using AnimeStudio;
using System.Text.Json;

internal static class AudioStudy
{
    public static int FindMedia(string[] args)
    {
        if (args.Length != 4 || File.Exists(args[3])) throw new ArgumentException("Usage: --audio-media <audio folder> <request.json> <new report.json>");
        using var request = JsonDocument.Parse(File.ReadAllText(args[2]));
        var ids = request.RootElement.GetProperty("entries").EnumerateArray().Select(e => ulong.Parse(e.GetProperty("id").GetString()!)).ToHashSet();
        var matches = new List<object>();
        foreach (var package in Directory.GetFiles(args[1], "*.pck", SearchOption.AllDirectories))
        foreach (var entry in WwisePackage.ReadIndex(package))
        {
            if (ids.Contains(entry.ID)) matches.Add(new { kind = "Package", entry });
            if (entry.Kind == "Bank")
                foreach (var media in WwiseBank.ReadMediaIndex(entry).Where(e => ids.Contains(e.ID)))
                    matches.Add(new { kind = "Embedded", bank = entry, media });
        }
        File.WriteAllText(args[3], JsonSerializer.Serialize(matches, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{matches.Count} media locations");
        return 0;
    }
    public static int Run(string[] args)
    {
        if (args.Length != 4) throw new ArgumentException("Usage: --audio-events <audio folder> <event names.txt> <new report.json>");
        if (File.Exists(args[3])) throw new IOException("Report exists.");
        var names = File.ReadAllLines(args[2]).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToArray();
        var ids = names.GroupBy(WwiseBank.EventID).ToDictionary(g => g.Key, g => g.ToArray());
        var matches = new List<object>(); var errors = new List<object>(); int bankCount = 0;
        var packages = Directory.GetFiles(args[1], "*.pck", SearchOption.AllDirectories);
        var counts = new Dictionary<string, int>();
        foreach (var package in packages)
        {
            try
            {
                var entries = WwisePackage.ReadIndex(package);
                foreach (var group in entries.GroupBy(e => e.Kind + "/" + e.Language))
                    counts[group.Key] = counts.GetValueOrDefault(group.Key) + group.Count();
                foreach (var entry in entries.Where(e => e.Kind == "Bank"))
                {
                    bankCount++;
                    var bank = WwiseBank.ReadIndex(entry);
                    foreach (var obj in bank.Objects.Where(o => o.Type == 4 && ids.ContainsKey(o.ID)))
                        matches.Add(new { names = ids[obj.ID], eventID = obj.ID, entry, bank.Version, bank.BankID, obj.Offset, obj.Size });
                }
            }
            catch (Exception ex) { errors.Add(new { package, error = ex.Message }); }
        }
        var report = new { packages = packages.Length, bankCount, counts, matches, errors,
            limitation = "Matches verify supplied names against typed event IDs. Event-to-action-to-media traversal, hash collision disambiguation and audio playback are not established." };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[3]))!);
        using var output = new FileStream(args[3], FileMode.CreateNew);
        JsonSerializer.Serialize(output, report, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine($"{matches.Count} event matches in {bankCount} banks; {errors.Count} package errors");
        return errors.Count == 0 ? 0 : 1;
    }
}
