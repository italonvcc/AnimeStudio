using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace AnimeStudio
{
    public sealed class GenshinCharacterReferences
    {
        public string Fingerprint { get; private set; }
        public string CacheDirectory { get; private set; }
        public string GameVersion { get; private set; }
        public string AudioDirectory { get; private set; }
        public List<AssetEntry> Entries { get; private set; }
        public string[] Sources { get; private set; }
        private static readonly object ScanLock = new();

        public static string ComputeFingerprint(string mapPath, IEnumerable<string> sources, string version)
        {
            var stamp = new StringBuilder("character-references-v1\n");
            using (var file = File.OpenRead(mapPath)) stamp.AppendLine(Convert.ToHexString(SHA256.HashData(file)));
            foreach (var source in sources.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
            {
                var file = new FileInfo(source);
                if (!file.Exists) throw new FileNotFoundException("The map references missing client files. Rebuild or relocate the map.", source);
                stamp.Append(source).Append('|').Append(file.Length).Append('|').Append(file.LastWriteTimeUtc.Ticks).AppendLine();
            }
            stamp.AppendLine(version);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stamp.ToString())));
        }
        public static GenshinCharacterReferences Prepare(string mapPath, IEnumerable<AssetEntry> entries, Action<string> progress = null)
        {
            var map = entries.ToList();
            if (map.Count == 0) throw new InvalidDataException("Load a Genshin asset map first.");
            var sources = map.Select(e => Path.GetFullPath(e.Source)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray();
            string version = GenshinSourceMetadata.GameVersion(sources[0]);
            string fingerprint = ComputeFingerprint(mapPath, sources, version);
            string cache = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(mapPath)), ".genshin-references", fingerprint);
            string index = Path.Combine(cache, "references.json");
            List<AssetEntry> supplemental;
            lock (ScanLock)
            {
                if (File.Exists(index)) supplemental = JsonConvert.DeserializeObject<List<AssetEntry>>(File.ReadAllText(index));
                else
                {
                    supplemental = new();
                    for (int start = 0; start < sources.Length; start += 48)
                    {
                        progress?.Invoke($"Refreshing Genshin references: {start}/{sources.Length} files");
                        supplemental.AddRange(AssetsHelper.IndexGenshinReferences(sources.Skip(start).Take(48).ToArray()));
                    }
                    Directory.CreateDirectory(cache);
                    File.WriteAllText(index + ".tmp", JsonConvert.SerializeObject(supplemental));
                    File.Move(index + ".tmp", index, true);
                    File.WriteAllLines(Path.Combine(cache, "sources.txt"), new[] { "Game version: " + version, "Fingerprint: " + fingerprint }.Concat(sources));
                }
            }
            string audio = null;
            for (var directory = new FileInfo(sources[0]).Directory; directory != null; directory = directory.Parent)
            {
                audio = new[] { Path.Combine(directory.FullName, "AudioAssets"), Path.Combine(directory.FullName, "StreamingAssets", "AudioAssets") }.FirstOrDefault(Directory.Exists);
                if (audio != null) break;
            }
            // Voice naming references share the map/client fingerprint. Offline failures
            // must not prevent exporting the model; a voice export retries preparation.
            try { GenshinVoiceReferences.RefreshNames(cache, progress); }
            catch (Exception e) when (e is System.Net.Http.HttpRequestException || e is System.Threading.Tasks.TaskCanceledException || e is IOException)
            {
                Logger.Warning("Voice reference refresh unavailable; voice export will retry: " + e.Message);
            }
            progress?.Invoke("Genshin references ready");
            return new GenshinCharacterReferences { Fingerprint = fingerprint, CacheDirectory = cache, GameVersion = version, AudioDirectory = audio,
                Entries = map.Concat(supplemental).DistinctBy(e => (e.Source.ToUpperInvariant(), e.Offset, e.PathID, e.Type)).ToList(), Sources = sources };
        }
    }
}
