using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AnimeStudio
{
    public static class GenshinAssemblyExporter
    {
        public static string Export(AssetsManager manager, IEnumerable<AssetEntry> entries, string requestFile, string destination)
        {
            var map = entries.ToArray();
            var request = JObject.Parse(File.ReadAllText(requestFile));
            var parts = request["parts"] as JArray ?? throw new InvalidDataException("Missing parts array.");
            if (parts.Count is < 1 or > 16) throw new InvalidDataException("Select 1–16 explicit part replacements.");
            AssetEntry Select(JToken selector)
            {
                if (selector == null) throw new InvalidDataException("Missing root selector.");
                var type = Enum.Parse<ClassIDType>((string)selector["type"]);
                if (type is not (ClassIDType.Animator or ClassIDType.GameObject)) throw new InvalidDataException("Root must be Animator/GameObject.");
                string name = (string)selector["name"];
                long id = long.Parse((string)selector["pathID"]);
                string source = (string)selector["source"];
                var matches = map.Where(e => e.Type == type && e.Name == name && e.PathID == id &&
                    (source == null || string.Equals(e.Source, source, StringComparison.OrdinalIgnoreCase))).ToArray();
                if (matches.Length != 1)
                    throw new InvalidDataException($"Expected one {type} {name}@{id} source record; found {matches.Length}. Supply the exact source path when repeated across bundles.");
                return matches[0];
            }
            var rootEntry = Select(request["base"]);
            var partEntries = parts.Select(p => Select(p["root"])).ToArray();
            if (partEntries.Any(e => e.Type != ClassIDType.GameObject)) throw new InvalidDataException("Replacement roots must be GameObjects.");
            var selected = partEntries.Prepend(rootEntry).ToArray();
            if (selected.Any(e => e.Offset < 0)) throw new InvalidDataException("Regenerate a map with bundle offsets.");
            var oldFilter = manager.FilterData;
            try
            {
                manager.FilterData = new AssetsManager.AssetFilterData { Items = selected.Select(e => new AssetsManager.AssetFilterDataItem
                { Source = e.Source, Offset = e.Offset, PathID = e.PathID, Name = e.Name, Type = e.Type }).ToList() };
                manager.LoadFiles(selected.Select(e => e.Source).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), mergeSplitAssets: false);
                Object Get(AssetEntry entry) => manager.assetsFileList.SelectMany(f => f.ObjectsDic.Values).Single(o => o.type == entry.Type && o.m_PathID == entry.PathID && string.Equals(o.assetsFile.originalPath, entry.Source, StringComparison.OrdinalIgnoreCase));
                var replacements = parts.Select((p, i) => new GenshinPartReplacement((GameObject)Get(partEntries[i]), (string)p["slot"],
                    p["removeMeshes"]?.Values<string>().ToArray() ?? throw new InvalidDataException("Specify relative mesh paths to replace."))).ToArray();
                string result = GenshinModelExporter.Export(manager, map, Get(rootEntry), destination, replacements: replacements);
                File.Copy(requestFile, Path.Combine(destination, "assembly-request.json"));
                return result;
            }
            finally { manager.FilterData = oldFilter; }
        }
    }
}
