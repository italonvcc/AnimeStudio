using System;
using System.Collections.Generic;

namespace AnimeStudio
{
    /// <summary>
    /// Snapshot of dependency candidate identity from one frozen asset map.
    /// Resolver instances share this read-only lookup but keep load state and
    /// AssetsManager access private to each export operation.
    /// </summary>
    public sealed class AssetDependencyIndex
    {
        public readonly record struct Container(string SerializedFile, string Source, long Offset);

        public readonly record struct Candidate(string Source, long Offset, long PathID,
            ClassIDType Type, string Name)
        {
            public AssetEntry ToAssetEntry() => new()
            {
                Source = Source, Offset = Offset, PathID = PathID, Type = Type, Name = Name,
                Container = "", Hash = ""
            };
        }

        private readonly Dictionary<long, Candidate> primary;
        private readonly Dictionary<long, Candidate[]> additional;
        private readonly Dictionary<string, Container> containersByFile;
        public int EntryCount { get; }
        public int DistinctPathIDCount => primary.Count;
        public int ContainerCount => containersByFile.Count;
        public int AmbiguousContainerCount { get; }

        public AssetDependencyIndex(IEnumerable<AssetEntry> entries, IEnumerable<Container> containers = null)
        {
            ArgumentNullException.ThrowIfNull(entries);
            primary = new Dictionary<long, Candidate>();
            var duplicates = new Dictionary<long, List<Candidate>>();
            int count = 0;
            foreach (var entry in entries)
            {
                if (entry == null) continue;
                var candidate = new Candidate(entry.Source, entry.Offset, entry.PathID, entry.Type, entry.Name);
                if (!primary.TryAdd(entry.PathID, candidate))
                {
                    if (!duplicates.TryGetValue(entry.PathID, out var list))
                        duplicates.Add(entry.PathID, list = new List<Candidate>());
                    list.Add(candidate);
                }
                count++;
            }
            additional = new Dictionary<long, Candidate[]>(duplicates.Count);
            foreach (var group in duplicates) additional.Add(group.Key, group.Value.ToArray());
            containersByFile = new Dictionary<string, Container>(StringComparer.OrdinalIgnoreCase);
            var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (containers != null)
            foreach (var container in containers)
            {
                if (string.IsNullOrWhiteSpace(container.SerializedFile)
                    || string.IsNullOrWhiteSpace(container.Source) || container.Offset < 0) continue;
                if (containersByFile.TryGetValue(container.SerializedFile, out var prior)
                    && (!prior.Source.Equals(container.Source, StringComparison.OrdinalIgnoreCase)
                        || prior.Offset != container.Offset)) ambiguous.Add(container.SerializedFile);
                else containersByFile.TryAdd(container.SerializedFile, container);
            }
            foreach (var name in ambiguous) containersByFile.Remove(name);
            AmbiguousContainerCount = ambiguous.Count;
            EntryCount = count;
        }

        public bool TryGet(long pathID, out Candidate first, out ReadOnlySpan<Candidate> other)
        {
            if (!primary.TryGetValue(pathID, out first))
            {
                other = ReadOnlySpan<Candidate>.Empty;
                return false;
            }
            other = additional.TryGetValue(pathID, out var entries) ? entries : ReadOnlySpan<Candidate>.Empty;
            return true;
        }

        public bool ContainsPathID(long pathID) => primary.ContainsKey(pathID);

        // A duplicate CAB name at different source offsets is not a qualified
        // location. Those names are excluded rather than choosing one silently.
        public bool TryGetContainer(string serializedFile, out Container container)
        {
            container = default;
            return serializedFile != null && containersByFile.TryGetValue(serializedFile, out container);
        }
    }
}
