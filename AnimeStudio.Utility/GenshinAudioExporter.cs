using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AnimeStudio
{
    // Names are explicit evidence inputs; package IDs alone never imply character ownership.
    public static class GenshinAudioExporter
    {
        public static void CopyEntry(WwisePackage.Entry entry, string destination)
        {
            using var input = File.OpenRead(entry.Package);
            if (entry.Offset < 0 || entry.Size < 0 || entry.Offset > input.Length - entry.Size)
                throw new InvalidDataException("Audio entry outside source package.");
            using var output = new FileStream(destination, FileMode.CreateNew);
            input.Position = entry.Offset;
            var buffer = new byte[65536];
            long remaining = entry.Size;
            while (remaining > 0)
            {
                int read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (read == 0) throw new EndOfStreamException();
                output.Write(buffer, 0, read); remaining -= read;
            }
        }

        public static void ExportNames(string folder, string requestFile, string destination, string decoder = null)
        {
            var request = JObject.Parse(File.ReadAllText(requestFile));
            var entries = request["entries"] as JArray ?? throw new InvalidDataException("Missing entries.");
            if (request["source"] == null || entries.Count is < 1 or > 20000)
                throw new InvalidDataException("Supply source evidence and 1–20000 named entries.");
            if (decoder != null && !File.Exists(decoder)) throw new FileNotFoundException("Decoder not found.", decoder);
            var packageEntries = Directory.GetFiles(folder, "*.pck", SearchOption.AllDirectories)
                .SelectMany(WwisePackage.ReadIndex).ToArray();
            var index = packageEntries.ToLookup(e => (e.Kind, e.ID));
            var bankIDs = entries.Where(e => (string)e["kind"] is "EmbeddedMedia" or "SoundMedia").Select(e => ulong.Parse((string)e["bankID"], CultureInfo.InvariantCulture)).ToHashSet();
            var soundIDs = entries.Where(e => (string)e["kind"] == "SoundMedia").Select(e => ulong.Parse((string)e["id"], CultureInfo.InvariantCulture)).ToHashSet();
            var mediaLocations = packageEntries.Where(e => e.Kind == "Bank" && (soundIDs.Count > 0 || bankIDs.Contains(e.ID)))
                .SelectMany(bank => WwiseBank.ReadMediaIndex(bank).Where(media => soundIDs.Contains(media.ID) || bankIDs.Contains(bank.ID)).Select(media => (bank, media))).ToArray();
            var embedded = mediaLocations.ToLookup(e => (e.bank.ID, e.media.ID));
            Prepare(destination);
            var results = new List<object>();
            foreach (var item in entries)
            {
                string kind = (string)item["kind"], name = (string)item["name"], language = (string)item["language"];
                if (kind is not ("External" or "Media" or "EmbeddedMedia" or "SoundMedia") || string.IsNullOrWhiteSpace(name))
                    throw new InvalidDataException("Each entry needs Media/External/EmbeddedMedia kind and an evidenced name.");
                ulong id = ulong.Parse((string)item["id"], CultureInfo.InvariantCulture);
                var available = kind is "EmbeddedMedia" or "SoundMedia" ? embedded[(ulong.Parse((string)item["bankID"], CultureInfo.InvariantCulture), id)].Select(e => e.media) : index[(kind, id)];
                // GI separates HIRC content banks from DATA-only media banks. Search typed DIDX entries,
                // retaining ambiguity when multiple different payloads share a media ID.
                if (kind == "SoundMedia" && !available.Any()) available = mediaLocations.Where(e => e.media.ID == id).Select(e => e.media).Concat(index[("Media", id)]);
                var candidates = available.Where(e => string.IsNullOrEmpty(language) ||
                    string.Equals(e.Language, language, StringComparison.OrdinalIgnoreCase) ||
                    Path.GetDirectoryName(e.Package).Split(Path.DirectorySeparatorChar).Any(p => string.Equals(p, language, StringComparison.OrdinalIgnoreCase))).ToArray();
                bool identicalCopies = candidates.Length > 1 && candidates.Select(HashEntry).Distinct().Count() == 1;
                if (candidates.Length != 1 && !identicalCopies)
                {
                    results.Add(new { request = item, status = candidates.Length == 0 ? "Missing installed media/language" : "Ambiguous package entries", candidates });
                    continue;
                }
                var entry = candidates[0];
                string baseName = SafeName(name.Replace('\\', '/').Split('/').Last()) + "_" + id + "_" + SafeName(language ?? entry.Language);
                string wem = Path.Combine(destination, baseName + ".wem");
                CopyEntry(entry, wem);
                string decoded = null, decodeError = null;
                if (decoder != null)
                {
                    string wav = Path.Combine(destination, baseName + ".wav");
                    var start = new ProcessStartInfo(Path.GetFullPath(decoder)) { UseShellExecute = false, CreateNoWindow = true };
                    foreach (var arg in new[] { "-o", Path.GetFullPath(wav), Path.GetFullPath(wem) }) start.ArgumentList.Add(arg);
                    using var process = Process.Start(start);
                    if (!process.WaitForExit(120000)) { process.Kill(true); decodeError = "Decoder timed out"; }
                    else if (process.ExitCode != 0 || !File.Exists(wav)) decodeError = "Decoder failed: " + process.ExitCode;
                    else decoded = Path.GetFileName(wav);
                }
                using var source = File.OpenRead(wem);
                results.Add(new { request = item, status = "Exported", entry, file = Path.GetFileName(wem),
                    sha256 = Convert.ToHexString(SHA256.HashData(source)), decoded, decodeError, identicalCopies,
                    locations = candidates.Select(media => new { media, banks = mediaLocations.Where(p => p.media == media).Select(p => p.bank) }) });
            }
            File.WriteAllText(Path.Combine(destination, "manifest.json"), JsonConvert.SerializeObject(new {
                schemaVersion = 1, gameVersion = GenshinSourceMetadata.GameVersion(folder), source = request["source"], results,
                toolRevision = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(GenshinAudioExporter).Assembly)?.InformationalVersion,
                limitation = "Names come from supplied evidence. Missing or ambiguous package/language matches are not guessed; waveform playback does not establish event mixing or runtime timing."
            }, Formatting.Indented));
        }

        public static void ExportEventBanks(string folder, string namesFile, string destination)
        {
            var names = File.ReadAllLines(namesFile).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToArray();
            if (names.Length is < 1 or > 4096) throw new ArgumentException("Supply 1–4096 source-observed event names.");
            var ids = names.GroupBy(WwiseBank.EventID).ToDictionary(g => g.Key, g => g.ToArray());
            Prepare(destination);
            var results = new List<object>();
            foreach (string package in Directory.GetFiles(folder, "*.pck", SearchOption.AllDirectories))
            foreach (var entry in WwisePackage.ReadIndex(package).Where(e => e.Kind == "Bank"))
            {
                var bank = WwiseBank.ReadIndex(entry);
                var events = bank.Objects.Where(o => o.Type == 4 && ids.ContainsKey(o.ID)).Select(o => new { o.ID, names = ids[o.ID], ambiguousName = ids[o.ID].Length != 1 }).ToArray();
                if (events.Length == 0) continue;
                string file = SafeName(Path.GetFileNameWithoutExtension(package)) + "_" + entry.LanguageID + "_" + entry.ID + "_" + entry.Offset + ".bnk";
                CopyEntry(entry, Path.Combine(destination, file));
                results.Add(new { file, entry, bank.Version, bank.BankID, events });
            }
            File.Copy(namesFile, Path.Combine(destination, "source-event-names.txt"));
            File.WriteAllText(Path.Combine(destination, "banks.json"), JsonConvert.SerializeObject(new { schemaVersion = 1, gameVersion = GenshinSourceMetadata.GameVersion(folder), results,
                limitation = "Typed HIRC Event matches only. Traverse bank actions/containers before assigning names to media." }, Formatting.Indented));
        }

        public static void ExportBankIDs(string folder, string idsFile, string destination)
        {
            var ids = JsonConvert.DeserializeObject<ulong[]>(File.ReadAllText(idsFile)).ToHashSet();
            if (ids.Count is < 1 or > 4096) throw new ArgumentException("Supply 1–4096 explicit bank IDs.");
            Prepare(destination);
            var results = new List<object>();
            foreach (string package in Directory.GetFiles(folder, "*.pck", SearchOption.AllDirectories))
            foreach (var entry in WwisePackage.ReadIndex(package).Where(e => e.Kind == "Bank" && ids.Contains(e.ID)))
            {
                var bank = WwiseBank.ReadIndex(entry);
                string file = SafeName(Path.GetFileNameWithoutExtension(package)) + "_" + entry.LanguageID + "_" + entry.ID + "_" + entry.Offset + ".bnk";
                CopyEntry(entry, Path.Combine(destination, file));
                results.Add(new { file, entry, bank.Version, bank.BankID });
            }
            File.WriteAllText(Path.Combine(destination, "banks.json"), JsonConvert.SerializeObject(new { schemaVersion = 1, requested = ids, results }, Formatting.Indented));
        }

        private static void Prepare(string destination)
        {
            if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Choose a new output directory.");
            Directory.CreateDirectory(destination);
        }
        private static string HashEntry(WwisePackage.Entry entry)
        {
            using var source = File.OpenRead(entry.Package);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            source.Position = entry.Offset;
            long remaining = entry.Size;
            var buffer = new byte[65536];
            while (remaining > 0)
            {
                int size = source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (size == 0) throw new EndOfStreamException();
                hash.AppendData(buffer, 0, size); remaining -= size;
            }
            return Convert.ToHexString(hash.GetHashAndReset());
        }
        private static string SafeName(string name) => string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
    }
}
