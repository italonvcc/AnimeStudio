using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AnimeStudio
{
    /// <summary>Preserve every action while respecting the shared exporter's bounded batch size.</summary>
    public static class GenshinWeaponVfxExport
    {
        public static IReadOnlyList<JObject> Partition(JObject request)
        {
            if (request?["actions"] is not JArray actions || actions.Count == 0)
                throw new InvalidDataException("At least one effect action is required.");
            var result = new List<JObject>();
            foreach (var chunk in actions.Chunk(32))
            {
                var batch = (JObject)request.DeepClone();
                batch["actions"] = new JArray(chunk.Select(a => a.DeepClone()));
                result.Add(batch);
            }
            return result;
        }

        public static string Export(IEnumerable<AssetEntry> entries, string requestFile, string destination,
            bool materials, CancellationToken cancellation = default, AssetDependencyIndex candidateIndex = null)
        {
            if (File.Exists(destination) || Directory.Exists(destination)) throw new IOException("Choose a new VFX output directory.");
            var request = JObject.Parse(File.ReadAllText(requestFile));
            var partitions = Partition(request);
            var map = entries.ToArray();
            candidateIndex ??= new AssetDependencyIndex(map);
            Directory.CreateDirectory(destination);
            var actions = new JArray();
            var failures = new JArray();
            var unresolved = new JArray();
            var missing = new JArray();
            var evidenceAssets = new JArray();
            var batches = new JArray();
            string manifest = Path.Combine(destination, "manifest.json");
            void Save(string status)
            {
                var report = new JObject { ["schemaVersion"] = 1, ["status"] = status,
                    ["subject"] = request["subject"]?.DeepClone(), ["character"] = request["character"]?.DeepClone(),
                    ["declaredGameVersion"] = request["gameVersion"]?.DeepClone(), ["exportMaterials"] = materials,
                    ["requestedActionCount"] = ((JArray)request["actions"]).Count, ["accountedActionCount"] = actions.Count,
                    ["actions"] = actions, ["batches"] = batches, ["failures"] = failures,
                    ["unresolved"] = unresolved, ["missingSelections"] = missing,
                    ["evidenceAssets"] = evidenceAssets,
                    ["playback"] = "NotTested" };
                string temporary = manifest + ".tmp";
                File.WriteAllText(temporary, report.ToString(Formatting.Indented));
                File.Move(temporary, manifest, true);
            }
            Save("Running");
            for (int i = 0; i < partitions.Count; i++)
            {
                string prefix = $"Batch-{i + 1:D4}";
                if (cancellation.IsCancellationRequested)
                {
                    foreach (var action in partitions[i]["actions"])
                        actions.Add(new JObject { ["action"] = action["name"]?.DeepClone(), ["status"] = "Cancelled", ["effects"] = new JArray() });
                    batches.Add(new JObject { ["batch"] = prefix, ["status"] = "Cancelled" });
                    Save("Cancelled");
                    continue;
                }
                string batchRequest = Path.Combine(destination, prefix + ".request.json");
                File.WriteAllText(batchRequest, partitions[i].ToString(Formatting.Indented));
                var manager = new AssetsManager { Game = GameManager.GetGameByType(GameType.GI), ResolveDependencies = false,
                    ObjectReadWorkers = GenshinExportWorkers.Count };
                try
                {
                    string batchManifest = GenshinVfxExporter.Export(manager, map, batchRequest,
                        Path.Combine(destination, prefix), materials, cancellation, candidateIndex);
                    var report = JObject.Parse(File.ReadAllText(batchManifest));
                    foreach (JObject action in report["actions"])
                    {
                        foreach (var effect in action["effects"] ?? new JArray())
                        {
                            if (effect["effectFile"]?.Type == JTokenType.String)
                                effect["effectFile"] = prefix + "/" + (string)effect["effectFile"];
                            foreach (var dependency in effect["dependencies"] ?? new JArray())
                                if (dependency["file"]?.Type == JTokenType.String)
                                    dependency["file"] = prefix + "/" + (string)dependency["file"];
                        }
                        action["batch"] = prefix;
                        actions.Add(action.DeepClone());
                    }
                    foreach (var failure in report["failures"] ?? new JArray()) failures.Add(failure.DeepClone());
                    foreach (var item in report["unresolved"] ?? new JArray()) unresolved.Add(item.DeepClone());
                    foreach (var item in report["missingSelections"] ?? new JArray()) missing.Add(item.DeepClone());
                    foreach (JObject item in report["evidenceAssets"] ?? new JArray())
                    {
                        var evidence = (JObject)item.DeepClone();
                        if (evidence["file"]?.Type == JTokenType.String) evidence["file"] = prefix + "/" + (string)evidence["file"];
                        evidence["batch"] = prefix;
                        evidenceAssets.Add(evidence);
                    }
                    batches.Add(new JObject { ["batch"] = prefix, ["status"] = "Exported",
                        ["manifest"] = prefix + "/manifest.json" });
                }
                catch (Exception ex)
                {
                    bool cancelled = ex is OperationCanceledException;
                    failures.Add(new JObject { ["batch"] = prefix, ["error"] = ex.Message });
                    foreach (var action in partitions[i]["actions"])
                        actions.Add(new JObject { ["action"] = action["name"]?.DeepClone(),
                            ["status"] = cancelled ? "Cancelled" : "Failed", ["effects"] = new JArray() });
                    batches.Add(new JObject { ["batch"] = prefix, ["status"] = cancelled ? "Cancelled" : "Failed" });
                }
                finally { manager.Clear(); }
                Save("Running");
            }
            Save(cancellation.IsCancellationRequested ? "Cancelled" : failures.Count + unresolved.Count + missing.Count == 0 ? "ExportedNotValidated" : "Partial");
            return manifest;
        }
    }
}
