using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AnimeStudio
{
    public sealed record GenshinCharacterOptions(bool Voices, bool Vfx, bool Animations, bool Materials);
    public static class GenshinCharacterExporter
    {
        public static string CharacterToken(string name)
        {
            var parts = name.Split('_');
            if (parts.Length < 4 || parts[0] != "Avatar") throw new ArgumentException("Select a character Animator named Avatar_<body>_<weapon>_<character>.");
            return Regex.Replace(parts[3], "Costume.*$", "", RegexOptions.CultureInvariant);
        }
        public static List<AssetEntry> SelectClips(IEnumerable<AssetEntry> map, string character)
        {
            string prefix = "Ani_" + character + "_";
            var own = map.Where(e => e.Type == ClassIDType.AnimationClip && e.Name.StartsWith(prefix, StringComparison.Ordinal)).ToList();
            string shared = "Ani_Avatar_" + character.Split('_')[1] + "_";
            var suffixes = own.Select(e => e.Name[prefix.Length..]).ToHashSet(StringComparer.Ordinal);
            return own.Concat(map.Where(e => e.Type == ClassIDType.AnimationClip && e.Name.StartsWith(shared, StringComparison.Ordinal) && suffixes.Contains(e.Name[shared.Length..])))
                .DistinctBy(e => (e.Source, e.Offset, e.PathID, e.Type)).ToList();
        }
        public static string Export(GenshinCharacterReferences references, AssetEntry selected, string destination, GenshinCharacterOptions options, Action<string> progress = null)
        {
            if (selected.Type != ClassIDType.Animator) throw new ArgumentException("Select one character Animator.");
            if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("An export already exists at this location. Choose another parent folder.");
            string token = CharacterToken(selected.Name);
            var map = references.Entries;
            var clips = options.Animations ? SelectClips(map, selected.Name) : new List<AssetEntry>();
            if (options.Animations && clips.Count == 0) throw new InvalidDataException("No character animation clips were found in the loaded map.");
            string voiceRequest = null, decoder = null;
            if (options.Voices)
            {
                if (references.AudioDirectory == null) throw new DirectoryNotFoundException("Could not locate AudioAssets beside the selected client's AssetBundles.");
                voiceRequest = GenshinVoiceReferences.Prepare(references.CacheDirectory, token, progress, out decoder);
            }
            var manager = new AssetsManager { Game = GameManager.GetGameByType(GameType.GI), ResolveDependencies = false };
            try
            {
                progress?.Invoke("Loading selected character and Unity animations");
                Load(manager, clips.Prepend(selected));
                var animator = Get(manager, selected) as Animator ?? throw new InvalidDataException("Selected Animator no longer matches the client. Rebuild the asset map.");
                var animations = clips.Select(e => Get(manager, e) as AnimationClip ?? throw new InvalidDataException("Animation did not load: " + e.Name))
                    .DistinctBy(c => (c.Name, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(c.GetRawData())))).ToArray();
                progress?.Invoke("Exporting character model, textures and source rig");
                string fbx = GenshinModelExporter.Export(manager, map, animator, destination, animations, exportMaterials: options.Materials, compactAnimations: true);
                if (!animator.m_Avatar.TryGet(out var avatar)) throw new InvalidDataException("Selected Animator has no usable humanoid Avatar.");
                GenshinUnityPackage.Write(avatar, selected.Name, fbx, animations, destination);
                // Unity resolves textures from a child Textures directory during FBX import.
                var textures = Directory.GetFiles(destination, "*.png");
                if (textures.Length > 0)
                {
                    string folder = Path.Combine(destination, "Textures"); Directory.CreateDirectory(folder);
                    foreach (var file in textures) File.Move(file, Path.Combine(folder, Path.GetFileName(file)));
                }
                var components = new List<object>();
                // Release model/clip source buffers before traversing the independent VFX graph.
                manager.Clear(); animations = null; animator = null;
                GC.Collect();
                if (options.Vfx)
                {
                    progress?.Invoke("Discovering and exporting character VFX dependencies");
                    var effects = map.Where(e => e.Type == ClassIDType.GameObject &&
                        (e.Name.StartsWith("Eff_" + selected.Name + "_", StringComparison.Ordinal) || e.Name.StartsWith("SkillObj_" + token + "_", StringComparison.Ordinal))).ToArray();
                    var evidence = map.Where(e => e.Type == ClassIDType.MonoBehaviour && e.Name.StartsWith("EventPattern_", StringComparison.Ordinal) &&
                        Regex.IsMatch(e.Name, "(^|_)" + Regex.Escape(token) + "(_|$)")).ToArray();
                    if (effects.Length == 0) throw new InvalidDataException("No VFX roots were found for the selected character in the current client references.");
                    Load(manager, evidence);
                    var observed = evidence.SelectMany(e => Regex.Matches(System.Text.Encoding.ASCII.GetString(Get(manager, e).GetRawData()), @"[\x20-\x7e]{6,}").Select(m => m.Value)).ToHashSet();
                    JObject Selector(AssetEntry e, string why) => new() { ["name"] = e.Name, ["type"] = e.Type.ToString(), ["pathID"] = e.PathID.ToString(), ["source"] = e.Source, ["evidence"] = why };
                    var request = new JObject { ["character"] = selected.Name, ["gameVersion"] = references.GameVersion,
                        ["actions"] = new JArray(new JObject { ["name"] = "Character effects", ["evidence"] = "Refreshed current-client prefab names and source event strings; runtime timing is preserved as raw data.",
                            ["effects"] = new JArray(effects.Select(e => Selector(e, observed.Contains(e.Name) ? "Exact source event string" : "Character-prefixed asset name; runtime association unverified"))) }),
                        ["evidenceAssets"] = new JArray(evidence.Select(e => Selector(e, "Character event configuration"))) };
                    string requestPath = Path.Combine(destination, "vfx-discovery.json"); File.WriteAllText(requestPath, request.ToString());
                    string manifest = GenshinVfxExporter.Export(manager, map, requestPath, Path.Combine(destination, "VFX"), options.Materials);
                    var result = JObject.Parse(File.ReadAllText(manifest));
                    if (((JArray)result["failures"]).Count > 0 || ((JArray)result["missingSelections"]).Count > 0) throw new InvalidDataException("Some VFX assets failed to export. See VFX/manifest.json.");
                    components.Add(new { kind = "VFX", roots = effects.Length, unresolved = ((JArray)result["unresolved"]).Count });
                }
                if (options.Voices)
                {
                    progress?.Invoke("Exporting named voice clips as WAV");
                    string folder = Path.Combine(destination, "Voices");
                    GenshinAudioExporter.ExportNames(references.AudioDirectory, voiceRequest, folder, decoder);
                    var result = JObject.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json")));
                    var exported = result["results"].Where(r => (string)r["status"] == "Exported").ToArray();
                    if (exported.Length == 0 || exported.Any(r => r["decodeError"]?.Type != JTokenType.Null && r["decodeError"] != null)) throw new InvalidDataException("Voice decoding was incomplete. See Voices/manifest.json.");
                    components.Add(new { kind = "Voices", exported = exported.Length, missing = result["results"].Count() - exported.Length });
                }
                progress?.Invoke("Organizing shared body animations; retaining character materials and textures");
                GenshinSharedAssets.Package(destination, selected.Name);
                File.WriteAllText(Path.Combine(destination, "character-export.json"), JsonConvert.SerializeObject(new { schemaVersion = 2, references.GameVersion, references.Fingerprint,
                    character = selected, options, animationSelection = "Character name prefix plus matching shared body action suffixes. Layer pairs are validated on Unity import.",
                    components, status = "Exported; copy this character folder AND its sibling Generic folder under the same Unity Assets parent." }, Formatting.Indented));
                return destination;
            }
            catch (Exception e)
            {
                if (Directory.Exists(destination)) File.WriteAllText(Path.Combine(destination, "EXPORT-INCOMPLETE.txt"), e.ToString());
                throw;
            }
            finally { manager.Clear(); }
        }
        private static void Load(AssetsManager manager, IEnumerable<AssetEntry> entries)
        {
            var list = entries.ToArray(); if (list.Length == 0) return;
            manager.FilterData.Items = list.Select(e => new AssetsManager.AssetFilterDataItem { Source = e.Source, Offset = e.Offset, Type = e.Type, PathID = e.PathID, Name = e.Name }).ToList();
            manager.LoadFiles(list.Select(e => e.Source).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), mergeSplitAssets: false);
        }
        private static Object Get(AssetsManager manager, AssetEntry entry) => manager.FindAsset(entry)
            ?? throw new InvalidDataException("Map selection did not load: " + entry.Name + ". Rebuild the map for the installed client.");
    }
}
