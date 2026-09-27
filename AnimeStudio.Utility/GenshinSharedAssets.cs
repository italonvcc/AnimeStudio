using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AnimeStudio
{
    // Generic is a reusable dependency pool, not a claim that every object is
    // semantically universal. Character manifests retain ownership/assignment evidence.
    public static class GenshinSharedAssets
    {
        public static string Store(string characterDirectory, string source, string category)
        {
            string root = Path.GetFullPath(characterDirectory);
            source = Path.GetFullPath(source);
            if (!source.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Shared asset must belong to the new character package.");
            if (category.Split('/').Any(p => p.Length == 0 || p == "." || p == ".." || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
                throw new ArgumentException("Invalid shared asset category.");
            string hash;
            using (var input = File.OpenRead(source)) hash = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
            string target = Path.Combine(Path.GetDirectoryName(root), "Generic", category, hash, Path.GetFileName(source));
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            // Never overwrite a pooled resource or its Unity .meta. Atomic creation
            // also permits simultaneous exports to reuse the same content.
            string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.Copy(source, temporary);
                try { File.Move(temporary, target); }
                catch (IOException) when (File.Exists(target))
                {
                    using var existing = File.OpenRead(target);
                    if (Convert.ToHexString(SHA256.HashData(existing)).ToLowerInvariant() != hash)
                        throw new InvalidDataException("Shared resource was modified: " + target);
                }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return Path.GetRelativePath(root, target).Replace('\\', '/');
        }

        public static void Package(string directory, string character)
        {
            directory = Path.GetFullPath(directory);
            var links = new Dictionary<string, string>(StringComparer.Ordinal);
            var records = new List<object>();
            void ShareFolder(string relative, string category, string pattern, Func<string, bool> select = null)
            {
                string folder = Path.Combine(directory, relative);
                if (!Directory.Exists(folder)) return;
                var index = new List<object>();
                foreach (string source in Directory.GetFiles(folder, pattern).OrderBy(p => p, StringComparer.Ordinal))
                {
                    if (select != null && !select(Path.GetFileName(source))) continue;
                    string original = Path.GetRelativePath(directory, source).Replace('\\', '/');
                    string file = Store(directory, source, category);
                    links.Add(original, file);
                    var item = new { original, file, bytes = new FileInfo(source).Length };
                    records.Add(item); index.Add(item);
                }
                if (index.Count > 0) File.WriteAllText(Path.Combine(folder, "shared-index.json"), JsonConvert.SerializeObject(index, Formatting.Indented));
            }
            string body = character.Split('_')[1];
            ShareFolder("Animations", "Animations/" + body, "*.anim", name => name.StartsWith("Ani_Avatar_" + body + "_", StringComparison.Ordinal) && !name.StartsWith("Ani_" + character + "_", StringComparison.Ordinal));
            ShareFolder("Textures", "Textures", "*.png");
            ShareFolder("Materials", "Materials", "*.json", name => name != "shared-index.json");
            ShareFolder("VFX/Texture2D", "Textures", "*.png");
            ShareFolder("VFX/Material", "Materials", "*.json", name => name != "shared-index.json");

            string manifestPath = Path.Combine(directory, "manifest.json");
            var manifest = JObject.Parse(File.ReadAllText(manifestPath));
            foreach (var clip in manifest["sourceClips"] ?? new JArray())
                if (links.TryGetValue((string)clip["file"], out string shared)) clip["file"] = shared;
            manifest["sharedAssets"] = JArray.FromObject(records);
            File.WriteAllText(manifestPath, manifest.ToString(Formatting.Indented));

            string recipePath = Path.Combine(directory, character + ".character.json");
            var recipe = JObject.Parse(File.ReadAllText(recipePath));
            foreach (var clip in recipe["clips"])
                if (links.TryGetValue((string)clip["file"], out string shared)) clip["file"] = shared;
            recipe["textures"] = new JArray(links.Where(p => p.Key.StartsWith("Textures/", StringComparison.Ordinal))
                .Select(p => new JObject { ["name"] = Path.GetFileName(p.Key), ["file"] = p.Value }));
            recipe["previewMaterials"] = manifest["previewMaterials"]?.DeepClone() ?? new JArray();
            File.WriteAllText(recipePath, recipe.ToString(Formatting.Indented));

            string vfxPath = Path.Combine(directory, "VFX", "manifest.json");
            if (File.Exists(vfxPath))
            {
                var vfx = JObject.Parse(File.ReadAllText(vfxPath));
                foreach (var property in vfx.Descendants().OfType<JProperty>().Where(p => p.Name == "file" && p.Value.Type == JTokenType.String).ToArray())
                {
                    string old = ((string)property.Value).Replace('\\', '/');
                    if (links.TryGetValue("VFX/" + old, out string shared)) property.Value = "../" + shared;
                }
                File.WriteAllText(vfxPath, vfx.ToString(Formatting.Indented));
            }
            File.WriteAllText(Path.Combine(directory, "shared-assets.json"), JsonConvert.SerializeObject(new {
                schemaVersion = 1, identity = "SHA-256 of exact exported bytes plus original filename; textures are preview PNG data, materials retain full source JSON.",
                meaning = "Reusable dependency storage; placement does not assert universal character compatibility.", assets = records }, Formatting.Indented));
            // All references are written before removing the just-created local copies.
            foreach (string original in links.Keys) File.Delete(Path.Combine(directory, original));
        }
    }
}
