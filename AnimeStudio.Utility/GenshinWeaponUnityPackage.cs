using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace AnimeStudio
{
    /// <summary>
    /// Writes an offline Unity import recipe from already exported, source-backed
    /// model manifests. It does not import into Unity or certify playback.
    /// </summary>
    public static class GenshinWeaponUnityPackage
    {
        private static readonly Regex LegacyFlag = new(@"(?m)^\s*m_Legacy:\s*([01])\s*$", RegexOptions.CultureInvariant);
        // ModelExporter historically calls every Animator/customType 8 binding
        // "humanoidBindings". Attributes 0..6 are Motion T/Q, 7..13 are Root
        // T/Q; neither group proves human muscles or a Humanoid Avatar.
        private static readonly Regex AnimatorChannel = new(@"(?m)^\s*- path:[^\r\n]*\r?\n\s*attribute:\s*(\d+)\r?\n\s*script:[^\r\n]*\r?\n\s*classID:\s*95\r?\n\s*customType:\s*8\s*$", RegexOptions.CultureInvariant);

        public static string[] Write(string outputRoot, string weaponManifestPath, string sharedPackageRoot = null)
        {
            string root = Path.GetFullPath(outputRoot);
            string reportPath = Contained(root, weaponManifestPath);
            var report = JObject.Parse(File.ReadAllText(reportPath));
            string familyFolder = Path.GetDirectoryName(reportPath)
                ?? throw new InvalidDataException("Weapon report has no parent directory.");
            string packageRoot = sharedPackageRoot == null ? root : Path.GetFullPath(sharedPackageRoot);
            if (sharedPackageRoot != null && !root.StartsWith(packageRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The shared weapon package root must contain the family output folder.");
            var variants = report["export"]?["variants"] as JArray
                ?? throw new InvalidDataException("Weapon report has no variant records.");
            var authored = new List<string>();
            foreach (var variant in variants)
            {
                var modelStage = variant["stages"]?["model"];
                string modelRelative = (string)modelStage?["path"];
                string manifestRelative = (string)modelStage?["manifest"];
                if (string.IsNullOrWhiteSpace(modelRelative) || string.IsNullOrWhiteSpace(manifestRelative)) continue;
                string modelPath = Contained(familyFolder, Path.Combine(familyFolder, modelRelative));
                string modelManifestPath = Contained(familyFolder, Path.Combine(familyFolder, manifestRelative));
                if (!File.Exists(modelPath) || !File.Exists(modelManifestPath))
                    throw new InvalidDataException("A reported model or model manifest is missing: " + modelRelative);
                string variantFolder = Path.GetDirectoryName(modelPath);
                if (!string.Equals(variantFolder, Path.GetDirectoryName(modelManifestPath), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Model and manifest must share a variant folder.");
                var model = JObject.Parse(File.ReadAllText(modelManifestPath));
                var clipRecords = new List<object>();
                bool unknownLegacy = false, hasLegacy = false, hasGeneric = false, hasBodyOrUnknown = false;
                var clipBindings = (variant["stages"]?["animations"]?["bindings"] as JArray) ?? new JArray();
                foreach (var sourceClip in (model["sourceClips"] as JArray) ?? new JArray())
                {
                    string relative = (string)sourceClip["file"];
                    if (string.IsNullOrWhiteSpace(relative)) throw new InvalidDataException("A source clip has no exported path.");
                    string clipFile = Contained(variantFolder, Path.Combine(variantFolder, relative));
                    if (!File.Exists(clipFile)) throw new InvalidDataException("An exported clip is missing: " + relative);
                    string clipText = File.ReadAllText(clipFile);
                    var legacy = LegacyFlag.Match(clipText);
                    bool? isLegacy = legacy.Success ? legacy.Groups[1].Value == "1" : null;
                    unknownLegacy |= isLegacy == null;
                    hasLegacy |= isLegacy == true;
                    hasGeneric |= isLegacy == false;
                    int reportedAnimatorChannels = (int?)sourceClip["humanoidBindings"] ?? 0;
                    var attributes = AnimatorChannel.Matches(clipText).Select(match => int.Parse(match.Groups[1].Value)).ToArray();
                    int rootMotionChannels = attributes.Count(attribute => attribute < 14);
                    int bodyChannels = attributes.Count(attribute => attribute >= 14);
                    int unknownChannels = Math.Max(0, reportedAnimatorChannels - attributes.Length);
                    hasBodyOrUnknown |= bodyChannels > 0 || unknownChannels > 0;
                    string sourceKey = IdentityKey(sourceClip["source"]);
                    var matching = sourceKey == null ? Array.Empty<JToken>() :
                        clipBindings.Where(binding => IdentityKey(binding["source"]) == sourceKey).ToArray();
                    var targets = matching.Length == 1 ? ((matching[0]["targets"] as JArray) ?? new JArray())
                        .Select(target => (object)new
                        {
                            path = (string)target["path"],
                            componentKind = isLegacy == true ?
                                ((target["componentKinds"] as JArray)?.Any(kind => (string)kind == "Animation") == true ? "Animation" : "Unsupported") :
                                isLegacy == false ?
                                ((target["componentKinds"] as JArray)?.Any(kind => (string)kind == "Animator") == true ? "Animator" : "Unsupported") : "Unsupported",
                            sourceRoot = target["sourceRoot"]
                        }).ToArray() : Array.Empty<object>();
                    clipRecords.Add(new
                    {
                        name = (string)sourceClip["source"]?["Name"] ?? Path.GetFileNameWithoutExtension(clipFile),
                        file = Forward(Path.GetRelativePath(variantFolder, clipFile)),
                        legacy = isLegacy,
                        animatorCustomBindings = reportedAnimatorChannels,
                        rootMotionChannels,
                        bodyChannels,
                        unknownChannels,
                        targetPaths = matching.Length == 1 ? matching[0]["targetPaths"] : new JArray(),
                        targets,
                        targetStatus = matching.Length != 1 ? "UnprovenOrAmbiguousClipOwner" : targets.Length == 0 ? "NoProvenInModelTarget" :
                            targets.Any(target => (string)JObject.FromObject(target)["componentKind"] == "Unsupported") ? "UnsupportedTargetComponent" : "ProvenSourceLink",
                        rootMotionValidation = rootMotionChannels > 0 ? "Motion/Root T/Q present; Unity playback and root motion behavior unvalidated" : "No Motion/Root T/Q channel identified",
                        source = sourceClip["source"]
                    });
                }
                bool skinned = ((model["meshes"] as JArray) ?? new JArray()).Any(mesh => (int?)mesh["bones"] > 0);
                bool sourceAnimator = string.Equals((string)model["root"]?["Type"], "Animator", StringComparison.Ordinal);
                string rigMode = hasBodyOrUnknown || unknownLegacy || hasLegacy && hasGeneric ? "Unsupported" :
                    hasLegacy ? "Legacy" : hasGeneric || skinned || sourceAnimator ? "Generic" : "Static";
                var bindings = (model["materialBindings"] as JArray) ?? new JArray();
                bool materialsRequested = (bool?)report["requestedOptions"]?["Materials"] ?? false;
                var materialRecords = bindings.Select(binding => new
                {
                    name = (string)binding["materialName"],
                    status = (string)binding["status"],
                    reason = (string)binding["reason"],
                    sourceJson = (string)binding["materialJsonPath"],
                    texturePaths = (binding["textures"] as JObject)?.Properties().Select(p => (object)new { property = p.Name, file = (string)p.Value }).ToArray()
                        ?? Array.Empty<object>()
                }).ToArray();
                var recipe = new
                {
                    schemaVersion = 1,
                    familyKey = (string)report["Key"],
                    familyName = (string)report["Name"],
                    weaponClass = (string)report["WeaponClass"],
                    gameVersion = (string)report["source"]?["GameVersion"],
                    sourceFingerprint = (string)report["source"]?["Fingerprint"],
                    model = Path.GetFileName(modelPath),
                    prefab = "Weapon.prefab",
                    rigMode,
                    rigClosureStatus = (string)model["rigClosureStatus"] ?? "Unknown",
                    selectedRootPath = (string)model["selectedRootPath"],
                    // Preserve source-qualified exported root, selected root,
                    // referenced bones and excluded siblings as proof context.
                    rigClosure = model["rigClosure"],
                    bindPoseStatus = (string)model["bindPoseStatus"] ?? "Unknown",
                    // Retain both source mesh constraints and their matrices;
                    // a global hierarchy rest cannot represent this conflict.
                    bindPoseConflicts = (model["bindPoseConflicts"] as JArray) ?? new JArray(),
                    clips = clipRecords,
                    materials = materialRecords,
                    // Preserve the model exporter's complete source-qualified
                    // Cubemap slot records. The Unity template reports these
                    // slots unsupported; it never treats faces as Texture2D
                    // replacements for a Cubemap property.
                    cubemapBindings = (model["cubemapBindings"] as JArray) ?? new JArray(),
                    materialsRequested,
                    sourceRoot = variant["sourceRoot"],
                    sourceReport = Forward(Path.GetRelativePath(variantFolder, reportPath)),
                    animationBindingUnknown = (bool?)variant["stages"]?["animations"]?["unknownBindings"] ?? false,
                    attachment = new { status = "Unknown", reason = "No source-proven socket or character attachment transform is available" },
                    externalCharacterRequirements = Array.Empty<string>(),
                    effectPlayback = "NotTested; no automatic trigger or timing is inferred from exported effect data",
                    validation = "Offline recipe only; Unity import, clip binding and playback have not been checked"
                };
                string recipePath = Path.Combine(variantFolder, "weapon-import.json");
                File.WriteAllText(recipePath, JsonConvert.SerializeObject(recipe, Formatting.Indented));
                authored.Add(GenshinUnityImportDescriptor.Write(variantFolder, "genshin-weapon", "weapon-import.json", Path.GetFileName(modelPath)));
                authored.Add(recipePath);
            }
            if (authored.Count == 0)
                throw new InvalidDataException("No exported model variant is available for a Unity import recipe.");
            string instructions = Path.Combine(packageRoot, "UNITY-IMPORT.txt");
            const string guidance = "Install com.caladan.shaders, then copy this export run folder under Unity Assets while preserving existing .meta files. Each model variant has anime-studio-import.json and weapon-import.json; the package importer creates a prefab and optional preview controller from this data. Existing prefab, controller, material assets and FBX material remaps are preserved for review on reimport. Cubemap source payload and face previews remain evidence; Cubemap binding is unsupported. Conflicting mesh bind poses and detached rig closure require live Unity validation. Ambiguous body channels remain unsupported. No weapon effect is auto-triggered. Inspect each variant's unity-import-report.json. Unity playback and game visual fidelity remain unvalidated by this offline export.\n";
            if (File.Exists(instructions) && File.ReadAllText(instructions) != guidance)
                throw new IOException("The output contains different Unity import instructions. Choose a new run folder.");
            if (!File.Exists(instructions)) File.WriteAllText(instructions, guidance);
            authored.Add(instructions);
            return authored.ToArray();
        }

        private static string Contained(string root, string candidate)
        {
            string absolute = Path.GetFullPath(candidate);
            string parent = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!absolute.StartsWith(parent, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Weapon import input escapes the export run: " + candidate);
            return absolute;
        }

        private static string IdentityKey(JToken source)
        {
            string path = (string)(source?["Source"] ?? source?["source"]);
            string offset = (string)(source?["Offset"] ?? source?["offset"]);
            string id = (string)(source?["PathID"] ?? source?["pathID"]);
            string type = (string)(source?["Type"] ?? source?["type"]);
            return path == null || offset == null || id == null || type == null ? null :
                path.Replace('\\', '/').ToUpperInvariant() + "|" + offset + "|" + id + "|" + type;
        }

        private static string Forward(string path) => path.Replace('\\', '/');
    }
}
