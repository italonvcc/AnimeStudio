using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace AnimeStudio.CLI
{
    internal static class GenshinModelCommand
    {
        public static int Run(string[] args)
        {
            if (args.Length is < 4 or > 5)
            {
                Console.Error.WriteLine("Usage: --genshin-model <map> <AnimatorName[@PathID]> <new-output-directory> [clip-name-regex]. FBX animation baking has been removed; animations export as .anim.");
                return 2;
            }
            var manager = new AssetsManager { Game = GameManager.GetGameByType(GameType.GI) };
            try
            {
                var map = new List<AssetEntry>();
                foreach (var mapPath in args[1].Split('|'))
                {
                    if (Path.GetExtension(mapPath).ToLowerInvariant() is not (".map" or ".json") || ResourceMap.FromFile(mapPath) < 0)
                        throw new ArgumentException("Could not read the asset map.");
                    if (!ResourceMap.GetGameType().IsGI()) throw new ArgumentException("The map must be for GI.");
                    map.AddRange(ResourceMap.GetEntries());
                }
                map = map.DistinctBy(e => (e.Source, e.Offset, e.PathID, e.Type)).ToList();
                if (args[0] == "--genshin-vfx")
                {
                    if (args.Length != 4) throw new ArgumentException("--genshin-vfx <maps joined by |> <vfx-request.json> <new output directory>");
                    Console.WriteLine(GenshinVfxExporter.Export(manager, map, args[2], args[3]));
                    return 0;
                }
                if (args[0] == "--genshin-assemble")
                {
                    if (args.Length != 4) throw new ArgumentException("--genshin-assemble <maps joined by |> <assembly.json> <new output directory>");
                    Console.WriteLine(GenshinAssemblyExporter.Export(manager, map, args[2], args[3]));
                    return 0;
                }
                var rootType = args[0] == "--genshin-prefab" ? ClassIDType.GameObject : ClassIDType.Animator;
                var name = args[2];
                long? pathID = null;
                int at = name.LastIndexOf('@');
                if (at >= 0) { pathID = long.Parse(name[(at + 1)..]); name = name[..at]; }
                var roots = map.Where(e => e.Type == rootType && e.Name == name &&
                    (pathID == null || pathID == e.PathID)).DistinctBy(e => (e.Source, e.Offset, e.PathID)).ToArray();
                if (roots.Length != 1) throw new ArgumentException($"Expected one {rootType}, found {roots.Length}. Use the exact name and @PathID to disambiguate.");
                var selected = roots.ToList();
                if (args.Length >= 5)
                {
                    var regex = new Regex(args[4], RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
                    var clips = map.Where(e => e.Type == ClassIDType.AnimationClip && regex.IsMatch(e.Name)).ToList();
                    if (clips.Count == 0 || clips.Count > 128) throw new ArgumentException("Clip selection must match 1-128 AnimationClip entries.");
                    selected.AddRange(clips);
                }
                if (selected.Any(e => e.Offset < 0)) throw new ArgumentException("Selected entries require bundle offsets; regenerate the map.");
                manager.FilterData.Items = selected.Select(e => new AssetsManager.AssetFilterDataItem
                { Source = e.Source, Offset = e.Offset, PathID = e.PathID, Name = e.Name, Type = e.Type }).ToList();
                manager.LoadFiles(selected.Select(e => e.Source).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), mergeSplitAssets: false);
                var objects = manager.assetsFileList.SelectMany(f => f.ObjectsDic.Values).ToArray();
                var root = objects.Single(a => a.type == rootType && a.m_PathID == roots[0].PathID && string.Equals(a.assetsFile.originalPath, roots[0].Source, StringComparison.OrdinalIgnoreCase));
                var animations = objects.OfType<AnimationClip>().Where(c => selected.Any(e => e.Type == ClassIDType.AnimationClip &&
                    e.PathID == c.m_PathID && string.Equals(e.Source, c.assetsFile.originalPath, StringComparison.OrdinalIgnoreCase))).ToArray();
                if (animations.Length != selected.Count - 1) throw new InvalidDataException("Some selected clips did not load.");
                var result = GenshinModelExporter.Export(manager, map, root, args[3], animations);
                Console.WriteLine(result);
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            finally { manager.Clear(); }
        }
    }
}
