using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace AnimeStudio.CLI
{
    // GUI and CLI share catalog identity and export policy. A name is accepted only
    // when unique; stable catalog keys remain usable for duplicate source names.
    internal static class GenshinWeaponCommand
    {
        private const string Usage =
            "--genshin-weapon-catalog <map> <new-report.json> [--map-only] [--scan-source <source-file>]\n" +
            "--genshin-weapon <map> <catalog-key-or-exact-name> <new-output-directory> [--animations] [--vfx] [--no-materials] [--unity-import] [--scan-source <source-file>]\n" +
            "--genshin-weapons <map> <new-output-directory> [--animations] [--vfx] [--no-materials] [--unity-import] [--scan-source <source-file>]\n" +
            "--scan-source may repeat and makes supplemental discovery explicitly partial. --verify-source-content hashes every selected bundle (including full-client scans). No Unity import or playback is performed.";

        public static int Run(string[] args)
        {
            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
            Console.CancelKeyPress += cancel;
            try
            {
                bool catalogOnly = args[0] == "--genshin-weapon-catalog";
                bool single = args[0] == "--genshin-weapon";
                int required = single ? 4 : 3;
                if (args.Length < required) throw new ArgumentException(Usage);
                var flags = new HashSet<string>(StringComparer.Ordinal);
                var sources = new List<string>();
                for (int i = required; i < args.Length; i++)
                {
                    if (args[i] == "--scan-source")
                    {
                        if (++i >= args.Length || args[i].StartsWith("--", StringComparison.Ordinal))
                            throw new ArgumentException("--scan-source requires an existing source file.\n" + Usage);
                        sources.Add(Path.GetFullPath(args[i]));
                    }
                    else if (args[i] == "--verify-source-content" || (catalogOnly && args[i] == "--map-only") ||
                        (!catalogOnly && args[i] is "--animations" or "--vfx" or "--no-materials" or "--unity-import"))
                    {
                        if (!flags.Add(args[i])) throw new ArgumentException("Duplicate option: " + args[i]);
                    }
                    else throw new ArgumentException("Unknown option: " + args[i] + "\n" + Usage);
                }
                if (flags.Contains("--map-only") && (sources.Count != 0 || flags.Contains("--verify-source-content")))
                    throw new ArgumentException("--map-only cannot scan supplemental source files.");
                string output = Path.GetFullPath(args[single ? 3 : 2]);
                if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Choose a new output path: " + output);
                if (!File.Exists(args[1])) throw new FileNotFoundException("Asset map not found.", args[1]);
                if (ResourceMap.FromFile(args[1]) < 0 || !ResourceMap.GetGameType().IsGI())
                    throw new InvalidDataException("Load a Genshin asset map.");
                var entries = ResourceMap.GetEntries().ToArray();
                GenshinWeaponReferences references = null;
                GenshinWeaponCatalog catalog;
                if (flags.Contains("--map-only")) catalog = GenshinWeaponCatalog.Build(entries);
                else
                {
                    references = GenshinWeaponReferences.Prepare(args[1], entries, Console.WriteLine,
                        cancellation.Token, sources.Count == 0 ? null : sources, flags.Contains("--verify-source-content"));
                    catalog = references.Catalog;
                }
                cancellation.Token.ThrowIfCancellationRequested();
                if (catalogOnly)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(output));
                    var report = new
                    {
                        schemaVersion = 1, sourceMap = Path.GetFullPath(args[1]),
                        scope = references?.ScanScope ?? "map-only",
                        sourceFingerprint = references?.Fingerprint,
                        gameVersion = references?.GameVersion,
                        sourceHashMode = references?.SourceHashMode,
                        versionCoverage = references?.VersionCoverage,
                        scanCompleteness = references?.ScanCompleteness,
                        scanErrors = references?.ScanErrors,
                        sourceFailures = references?.SourceFailures,
                        sourcesWithoutSerializedFiles = references?.SourcesWithoutSerializedFiles,
                        scannedSources = references?.Sources ?? Array.Empty<string>(),
                        note = "Indexed source candidates; not an authoritative equippable registry or a playback validation.",
                        catalog
                    };
                    using var file = new FileStream(output, FileMode.CreateNew, FileAccess.Write);
                    using var writer = new StreamWriter(file);
                    writer.Write(JsonConvert.SerializeObject(report, Formatting.Indented, new StringEnumConverter()));
                    Console.WriteLine(output);
                    return 0;
                }
                var options = new GenshinWeaponOptions(flags.Contains("--animations"), flags.Contains("--vfx"), !flags.Contains("--no-materials"), flags.Contains("--unity-import"));
                string result;
                if (single)
                {
                    var matches = catalog.Families.Where(f => f.Key == args[2] || f.SelectorAliases.Contains(args[2], StringComparer.Ordinal)).ToArray();
                    if (matches.Length == 0) matches = catalog.Families.Where(f => f.Name == args[2]).ToArray();
                    if (matches.Length != 1)
                        throw new ArgumentException($"Expected one weapon, found {matches.Length}. Generate --genshin-weapon-catalog and select its exact Key.");
                    result = GenshinWeaponExporter.Export(references, matches[0], output, options, Console.WriteLine, cancellation.Token);
                }
                else result = GenshinWeaponExporter.ExportCatalog(references, output, options, Console.WriteLine, cancellation.Token);
                Console.WriteLine(result);
                if (cancellation.IsCancellationRequested) return 130;
                // A report, not absence of an exception, determines export success.
                var summary = JObject.Parse(File.ReadAllText(result));
                string status = (string)(summary["status"] ?? summary["Status"]);
                return string.Equals(status, "Complete", StringComparison.OrdinalIgnoreCase) ? 0 : 3;
            }
            catch (OperationCanceledException) { Console.Error.WriteLine("Weapon operation cancelled; inspect any saved run report."); return 130; }
            catch (ArgumentException ex) { Console.Error.WriteLine(ex.Message); return 2; }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            finally { Console.CancelKeyPress -= cancel; ResourceMap.Clear(); }
        }
    }
}
