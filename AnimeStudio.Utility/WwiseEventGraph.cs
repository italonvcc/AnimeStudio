using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
using Newtonsoft.Json;

namespace AnimeStudio
{
    // Adapter for externally generated wwiser XML. No external parser implementation is bundled.
    public static class WwiseEventGraph
    {
        private sealed record Node(uint Bank, uint ID, string Kind, XElement Data, string Source);

        public static void WriteRequest(string namesFile, string[] xmlFiles, string destination)
        {
            if (File.Exists(destination)) throw new IOException("Choose a new request file.");
            if (xmlFiles.Length is < 1 or > 32) throw new ArgumentException("Supply 1–32 wwiser XML dumps.");
            var nodes = new List<Node>();
            var sources = new List<object>();
            foreach (string path in xmlFiles)
            {
                var info = new FileInfo(path);
                if (info.Length > 128 * 1024 * 1024) throw new InvalidDataException("XML dump exceeds 128 MiB.");
                using var input = new StringReader("<banks>" + File.ReadAllText(path) + "</banks>");
                using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 160 * 1024 * 1024 });
                var document = XDocument.Load(reader);
                foreach (var bank in document.Root.Elements("root"))
                {
                    if ((string)bank.Attribute("version") != "134") throw new InvalidDataException("This graph adapter currently validates Wwise bank version 134 only.");
                    if (bank.Descendants("error").Any()) throw new InvalidDataException("External parser reported errors: " + (string)bank.Attribute("filename"));
                    uint bankID = Value(bank, "dwSoundBankID");
                    foreach (var obj in bank.Descendants("list").Where(e => (string)e.Attribute("name") == "listLoadedItem").Elements("object"))
                    {
                        uint id = uint.Parse((string)obj.Elements("field").Single(e => (string)e.Attribute("type") == "sid").Attribute("value"));
                        nodes.Add(new Node(bankID, id, (string)obj.Attribute("name"), obj, (string)bank.Attribute("filename")));
                    }
                }
                using var bytes = File.OpenRead(path);
                sources.Add(new { file = Path.GetFileName(path), sha256 = Convert.ToHexString(SHA256.HashData(bytes)) });
            }
            nodes = nodes.GroupBy(n => (n.Bank, n.ID)).SelectMany(g => g.GroupBy(n => n.Data.ToString(SaveOptions.DisableFormatting)).Select(v => v.First())).ToList();
            var byBank = nodes.ToLookup(n => (n.Bank, n.ID));
            var byID = nodes.ToLookup(n => n.ID);
            var entries = new List<object>(); var graphs = new List<object>();
            foreach (var nameGroup in File.ReadAllLines(namesFile).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().GroupBy(WwiseBank.EventID))
            {
                var visited = new HashSet<(uint, uint)>(); var edges = new List<object>(); var missing = new List<object>();
                var names = nameGroup.ToArray();
                if (names.Length != 1) throw new InvalidDataException("Ambiguous event-name hash: " + nameGroup.Key);
                string name = names[0];
                var roots = byID[nameGroup.Key].Where(n => n.Kind == "CAkEvent").ToArray();
                if (roots.Length != 1) missing.Add(new { id = nameGroup.Key, reason = "Missing/ambiguous Event root", count = roots.Length });
                void Follow(Node owner, uint target, uint? bank = null)
                {
                    var matches = byBank[(bank ?? owner.Bank, target)].ToArray();
                    if (matches.Length == 0 && bank == null) matches = byID[target].ToArray();
                    edges.Add(new { ownerBank = owner.Bank, owner = owner.ID, targetBank = bank, target, matches = matches.Length });
                    if (matches.Length == 1) Visit(matches[0]);
                    else missing.Add(new { owner = owner.ID, target, bank, reason = "Missing/ambiguous typed reference" });
                }
                void Visit(Node node)
                {
                    if (!visited.Add((node.Bank, node.ID))) return;
                    if (visited.Count > 10000) throw new InvalidDataException("Event graph exceeds 10000 nodes.");
                    if (node.Kind == "CAkEvent")
                        foreach (uint action in Values(node.Data, "ulActionID")) Follow(node, action);
                    else if (node.Kind == "CAkActionPlay") Follow(node, Value(node.Data, "idExt"), Value(node.Data, "bankID"));
                    else if (node.Kind == "CAkActionPlayEvent") Follow(node, Value(node.Data, "idExt"));
                    else if (node.Kind is "CAkRanSeqCntr" or "CAkSwitchCntr" or "CAkLayerCntr" or "CAkActorMixer")
                    {
                        var children = Values(node.Data, "ulChildID").Distinct().ToArray();
                        foreach (uint child in children) Follow(node, child);
                        if (children.Length == 0) missing.Add(new { node = node.ID, reason = "Container has no supported child links" });
                    }
                    else if (node.Kind == "CAkSound")
                    {
                        uint media = Value(node.Data, "sourceID"), stream = Value(node.Data, "StreamType");
                        if (stream > 2) { missing.Add(new { node = node.ID, reason = "Unsupported stream type", stream }); return; }
                        entries.Add(new { kind = stream == 0 ? "SoundMedia" : "Media", id = media.ToString(), name = name + "__sound_" + node.ID,
                            language = "sfx", bankID = node.Bank.ToString(), evidence = new { eventID = nameGroup.Key, eventName = name,
                                soundID = node.ID, sourceBank = node.Source, streamType = stream,
                                source = Fields(node.Data, "AkBankSourceData"),
                                note = "All reachable variants exported; switches/randomization/RTPCs/effect mixing are retained in XML, not simulated." } });
                    }
                    else if (!node.Kind.StartsWith("CAkAction", StringComparison.Ordinal))
                        missing.Add(new { node = node.ID, node.Kind, reason = "Unsupported graph object" });
                    // Stop/parameter actions are preserved in the graph but produce no media.
                }
                if (roots.Length == 1) Visit(roots[0]);
                graphs.Add(new { name, eventID = nameGroup.Key, nodes = visited.Select(k => new { bankID = k.Item1, objectID = k.Item2, kind = byBank[k].Single().Kind }), edges, unresolved = missing });
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination)));
            File.WriteAllText(destination, JsonConvert.SerializeObject(new { schemaVersion = 1,
                source = new { parser = "wwiser XML", url = "https://github.com/bnnm/wwiser", dumps = sources, events = graphs,
                    evidence = "Names hashed to typed Event roots; explicit action, container-child and sound-source fields followed. No arbitrary numeric scanning." }, entries }, Newtonsoft.Json.Formatting.Indented));
        }
        private static IEnumerable<uint> Values(XElement node, string name) => node.Descendants("field")
            .Where(f => (string)f.Attribute("name") == name).Select(f => uint.Parse((string)f.Attribute("value")));
        private static uint Value(XElement node, string name) => Values(node, name).Single();
        private static object[] Fields(XElement node, string section) => node.Descendants("object").Where(o => (string)o.Attribute("name") == section)
            .Descendants("field").Select(f => (object)new { name = (string)f.Attribute("name"), value = (string)f.Attribute("value"), format = (string)f.Attribute("valuefmt") }).ToArray();
    }
}
