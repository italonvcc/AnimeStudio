using System;
using System.IO;
using System.Linq;

namespace AnimeStudio.CLI
{
    internal static class GenshinCharacterCommand
    {
        public static int Run(string[] args)
        {
            try
            {
                if (args.Length < 4 || args.Skip(4).Any(a => a is not ("--animations" or "--voices" or "--vfx" or "--no-materials")))
                    throw new ArgumentException("--genshin-character <map> <AnimatorName[@PathID]> <new folder> [--animations] [--voices] [--vfx] [--no-materials]");
                if (ResourceMap.FromFile(args[1]) < 0 || !ResourceMap.GetGameType().IsGI()) throw new InvalidDataException("Load a GI asset map.");
                var entries = ResourceMap.GetEntries().ToList();
                string name = args[2]; long? id = null; int at = name.LastIndexOf('@');
                if (at >= 0) { id = long.Parse(name[(at + 1)..]); name = name[..at]; }
                var selected = entries.Where(e => e.Type == ClassIDType.Animator && e.Name == name && (id == null || e.PathID == id)).ToArray();
                if (selected.Length != 1) throw new ArgumentException("Select exactly one Animator; disambiguate with @PathID.");
                var references = GenshinCharacterReferences.Prepare(args[1], entries, Console.WriteLine);
                Console.WriteLine(GenshinCharacterExporter.Export(references, selected[0], args[3], new GenshinCharacterOptions(args.Contains("--voices"), args.Contains("--vfx"), args.Contains("--animations"), !args.Contains("--no-materials")), Console.WriteLine));
                return 0;
            }
            catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        }
    }
}
