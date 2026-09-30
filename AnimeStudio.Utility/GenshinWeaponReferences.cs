using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Newtonsoft.Json;

namespace AnimeStudio
{
    /// <summary>
    /// A source identity index for weapon catalog discovery. Index entries are
    /// candidates; a name match is never an asset dependency or variant proof.
    /// Callers must serialize Prepare against other AssetsHelper operations because
    /// AssetsHelper owns a single static AssetsManager.
    /// </summary>
    public sealed class GenshinWeaponReferences
    {
        private const string Schema = "genshin-weapon-references-v3";
        private static readonly object ScanLock = new();

        public List<AssetEntry> Entries { get; private set; }
        public GenshinWeaponCatalog Catalog { get; private set; }
        public string Fingerprint { get; private set; }
        public string GameVersion { get; private set; }
        public string CacheDirectory { get; private set; }
        public string[] Sources { get; private set; }
        public string ScanScope { get; private set; }
        // The fingerprint covers the map bytes and the selected scan sources.
        // For a bounded fixture it is not a preflight of every client file in the map.
        public string FingerprintScope => ScanScope;
        public string ScanCompleteness { get; private set; }
        public string SourceHashMode { get; private set; }
        public string VersionCoverage { get; private set; }
        public List<string> SourcesWithoutSerializedFiles { get; private set; } = new();
        public List<string> ScanErrors { get; private set; } = new();
        public List<string> SourceFailures { get; private set; } = new();
        public List<AssetsHelper.GenshinWeaponSourceLink> SourceLinks { get; private set; } = new();
        public List<AssetsHelper.GenshinWeaponSourceFile> SerializedFiles { get; private set; } = new();

        public static GenshinWeaponReferences Prepare(string mapPath, IEnumerable<AssetEntry> entries,
            Action<string> progress = null, CancellationToken cancellation = default,
            IEnumerable<string> sourceFiles = null, bool verifyAllSourceContent = false)
        {
            ArgumentNullException.ThrowIfNull(mapPath);
            ArgumentNullException.ThrowIfNull(entries);
            cancellation.ThrowIfCancellationRequested();
            var map = entries.ToList();
            if (map.Count == 0) throw new InvalidDataException("Load a Genshin asset map first.");
            if (map.Any(e => e == null || string.IsNullOrWhiteSpace(e.Source)))
                throw new InvalidDataException("The map contains an entry without a source file path.");
            var mapSources = map.Where(e => !string.IsNullOrWhiteSpace(e.Source))
                .Select(e => Path.GetFullPath(e.Source)).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray();
            if (mapSources.Length == 0) throw new InvalidDataException("The map has no source file paths.");
            var selected = sourceFiles == null ? mapSources : sourceFiles.Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray();
            if (selected.Length == 0) throw new ArgumentException("At least one source file is required.", nameof(sourceFiles));
            var known = mapSources.ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (selected.Any(s => !known.Contains(s)))
                throw new ArgumentException("A selected source file is absent from the map.", nameof(sourceFiles));
            string scope = sourceFiles == null ? "all-map-sources" : "selected-source-files";
            var versions = selected.Select(s => Path.GetDirectoryName(s)).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(d => GenshinSourceMetadata.GameVersion(d)).Distinct(StringComparer.Ordinal).ToArray();
            if (versions.Where(v => !string.IsNullOrEmpty(v)).Distinct(StringComparer.Ordinal).Count() > 1)
                throw new InvalidDataException("Selected source files belong to different declared game versions.");
            string version = versions.FirstOrDefault(v => !string.IsNullOrEmpty(v));
            string versionCoverage = versions.All(v => !string.IsNullOrEmpty(v))
                ? "all selected source directories declare " + version
                : "some selected source directories have no declared game version";
            bool hashContents = sourceFiles != null || verifyAllSourceContent;
            string fingerprint = ComputeFingerprint(mapPath, selected, version, scope, hashContents,
                cancellation, progress);
            string cache = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(mapPath)),
                ".genshin-weapon-references", fingerprint);
            string index = Path.Combine(cache, "references.json");
            AssetsHelper.GenshinWeaponSourceScan scan;
            // AssetsManager catches some parse failures internally. Its public API
            // cannot certify every serialized object was readable, even if a scan
            // returns without throwing. Never label this cache as source-complete.
            lock (ScanLock)
            {
                cancellation.ThrowIfCancellationRequested();
                if (File.Exists(index))
                {
                    scan = JsonConvert.DeserializeObject<AssetsHelper.GenshinWeaponSourceScan>(File.ReadAllText(index))
                        ?? throw new InvalidDataException("The weapon reference cache is empty: " + index);
                }
                else
                {
                    scan = new AssetsHelper.GenshinWeaponSourceScan();
                    for (int start = 0; start < selected.Length; start += 8)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        progress?.Invoke($"Indexing weapon references: {start}/{selected.Length} source files");
                        var sources = selected.Skip(start).Take(8).ToArray();
                        void Append(AssetsHelper.GenshinWeaponSourceScan batch)
                        {
                            scan.Entries.AddRange(batch.Entries);
                            scan.Links.AddRange(batch.Links);
                            scan.Files.AddRange(batch.Files);
                            scan.ParseErrors.AddRange(batch.ParseErrors);
                        }
                        try { Append(AssetsHelper.IndexGenshinWeaponSource(sources)); }
                        catch (Exception batchError) when (batchError is not OperationCanceledException)
                        {
                            // Retry separately so one unreadable source cannot erase
                            // seven other source inventories in this batch.
                            foreach (var source in sources)
                            {
                                cancellation.ThrowIfCancellationRequested();
                                try { Append(AssetsHelper.IndexGenshinWeaponSource(new[] { source })); }
                                catch (Exception error) when (error is not OperationCanceledException)
                                {
                                    scan.SourceFailures.Add(source + ": " + error.Message);
                                }
                            }
                        }
                    }
                    cancellation.ThrowIfCancellationRequested();
                    Directory.CreateDirectory(cache);
                    string temp = index + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        File.WriteAllText(temp, JsonConvert.SerializeObject(scan));
                        File.Move(temp, index, true);
                        File.WriteAllLines(Path.Combine(cache, "sources.txt"),
                            new[] { "Schema: " + Schema, "Game version: " + version,
                                "Fingerprint: " + fingerprint, "Scan scope: " + scope,
                                "Source hash mode: " + (hashContents ? "sha256-content" : "length-and-mtime"),
                                "Version coverage: " + versionCoverage }.Concat(selected));
                    }
                    finally { if (File.Exists(temp)) File.Delete(temp); }
                }
            }
            cancellation.ThrowIfCancellationRequested();
            var observed = scan.Files.Select(f => f.Source).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = selected.Where(s => !observed.Contains(s)).ToList();
            string completeness = $"{selected.Length - missing.Count}/{selected.Length} sources yielded serialized files; "
                + $"{scan.ParseErrors.Count} recorded object parse errors; {scan.SourceFailures.Count} source failures; "
                + "internal loader errors may remain unobservable";
            var all = map.Concat(scan.Entries)
                .Where(e => e != null && !string.IsNullOrWhiteSpace(e.Source))
                .GroupBy(e => (Source: Path.GetFullPath(e.Source).ToUpperInvariant(), e.Offset, e.PathID, e.Type))
                .Select(g => g.OrderByDescending(e => !string.IsNullOrEmpty(e.Name)).First())
                .OrderBy(e => e.Source, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Offset)
                .ThenBy(e => e.PathID).ThenBy(e => e.Type).ToList();
            var catalog = GenshinWeaponCatalog.Build(all, scan.Links, scan.Files);
            progress?.Invoke($"Weapon references ready: {catalog.Families.Count} candidate families ({scope})");
            return new GenshinWeaponReferences { Entries = all, Catalog = catalog,
                Fingerprint = fingerprint, GameVersion = version, CacheDirectory = cache,
                Sources = selected, ScanScope = scope, ScanCompleteness = completeness,
                SourceHashMode = hashContents ? "sha256-content" : "length-and-mtime",
                VersionCoverage = versionCoverage, SourcesWithoutSerializedFiles = missing,
                ScanErrors = scan.ParseErrors, SourceFailures = scan.SourceFailures,
                SourceLinks = scan.Links, SerializedFiles = scan.Files };
        }

        public static string ComputeFingerprint(string mapPath, IEnumerable<string> sources,
            string version, string scope = "all-map-sources", bool hashContents = false,
            CancellationToken cancellation = default, Action<string> progress = null)
        {
            var stamp = new StringBuilder(Schema).Append('\n').Append(scope).Append('\n');
            using (var file = File.OpenRead(mapPath))
                stamp.AppendLine(Convert.ToHexString(SHA256.HashData(file)));
            var files = sources.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray();
            for (int index = 0; index < files.Length; index++)
            {
                cancellation.ThrowIfCancellationRequested();
                if (hashContents && index % 8 == 0)
                    progress?.Invoke($"Hashing weapon source files: {index}/{files.Length}");
                var source = files[index];
                var file = new FileInfo(source);
                if (!file.Exists) throw new FileNotFoundException("The map references missing client files. Rebuild or relocate the map.", source);
                stamp.Append(source.ToUpperInvariant()).Append('|').Append(file.Length).Append('|')
                    .Append(file.LastWriteTimeUtc.Ticks);
                if (hashContents)
                    using (var input = file.OpenRead()) stamp.Append('|').Append(Convert.ToHexString(SHA256.HashData(input)));
                stamp.AppendLine();
            }
            stamp.AppendLine(version ?? string.Empty);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stamp.ToString())));
        }
    }
}
