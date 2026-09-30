using AnimeStudio;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static class SharedCharacterSmoke
{
    public static int Run(string[] args)
    {
        if (args.Length is < 4 or > 6) throw new ArgumentException("--shared-character <map> <character> <new output> [--vfx] [--full]");
        Logger.Default = new ConsoleLogger(); Logger.Flags = LoggerEvent.Info | LoggerEvent.Warning | LoggerEvent.Error;
        ResourceMap.FromFile(args[1]);
        var references = GenshinCharacterReferences.Prepare(args[1], ResourceMap.GetEntries(), Console.WriteLine);
        string[] selector = args[2].Split('@'); string character = selector[0];
        var names = new[] {"Standby","RunCycle","Attack_01","ElementalArt","ElementalBurst","Show_01"};
        string own = "Ani_" + character + "_", shared = "Ani_Avatar_" + character.Split('_')[1] + "_";
        // Narrow only the animation discovery set; retain dependency entries.
        if (!args.Contains("--full")) references.Entries.RemoveAll(e => e.Type == ClassIDType.AnimationClip && !names.Any(n => e.Name == own + n || e.Name == shared + n));
        var selected = references.Entries.Single(e => e.Type == ClassIDType.Animator && e.Name == character && (selector.Length == 1 || e.PathID == long.Parse(selector[1])));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var profile = new ExportProfile(); Logger.Default = profile;
        GenshinCharacterExporter.Export(references,selected,args[3],new(false,args.Contains("--vfx"),true,true),message => {profile.Stage(message); Console.WriteLine(message);});
        File.WriteAllText(Path.Combine(args[3],"export-profile.json"),JsonConvert.SerializeObject(profile.Finish(),Formatting.Indented));
        var recipe = JObject.Parse(File.ReadAllText(Path.Combine(args[3], character + ".character.json")));
        var clips = recipe["clips"]!.Select(c => new { name = (string)c["name"]!, file = (string)c["file"]!, bytes = new FileInfo(Path.Combine(args[3], (string)c["file"]!)).Length }).ToArray();
        File.WriteAllText(Path.Combine(args[3],"bounded-storage-check.json"),JsonConvert.SerializeObject(new {clips,bytes=clips.Sum(c=>c.bytes),seconds=watch.Elapsed.TotalSeconds},Formatting.Indented));
        Console.WriteLine($"PASS {(args.Contains("--full") ? "full-animation" : "bounded-animation")} package: {clips.Length} clips, {clips.Sum(c=>c.bytes)} bytes");
        return 0;
    }
}
