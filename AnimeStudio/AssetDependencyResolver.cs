using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace AnimeStudio
{
    public sealed class AssetDependencyResolver
    {
        private readonly AssetsManager manager;
        private readonly Dictionary<long, List<AssetEntry>> candidates;
        private readonly HashSet<(string, long)> attempted = new();
        public int MaximumBundles { get; set; } = 512;
        public int MaximumPasses { get; set; } = 16;

        public AssetDependencyResolver(AssetsManager manager, IEnumerable<AssetEntry> map)
        {
            this.manager = manager;
            candidates = map.GroupBy(e => e.PathID).ToDictionary(g => g.Key, g => g.ToList());
        }

        public sealed record MissingReference(string OwnerFile, string OwnerPathID, string OwnerName,
            string Field, string TargetFile, string TargetPathID, string ExpectedType, string Reason);
        public sealed record Result(List<Object> Objects, List<MissingReference> Missing, int LoadedBundles);

        public Result Resolve(IEnumerable<Object> roots, CancellationToken cancellation = default)
        {
            var rootList = roots.ToArray();
            var oldFilter = manager.FilterData;
            var oldResolve = manager.ResolveDependencies;
            int loaded = 0;
            try
            {
                // Candidate discovery uses the map; PPtr remains the authority for CAB + path identity.
                manager.ResolveDependencies = false;
                for (int pass = 0; pass < MaximumPasses; pass++)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var graph = Walk(rootList);
                    var pending = new List<AssetEntry>();
                    foreach (var edge in graph.Edges.Where(e => !e.Reference.IsNull && !e.Reference.TryGetObject(out _)))
                    {
                        if (!candidates.TryGetValue(edge.Reference.PathID, out var matches)) continue;
                        foreach (var entry in matches)
                        {
                            if (entry.Offset < 0 || !File.Exists(entry.Source)) continue;
                            var type = typeof(Object).Assembly.GetType("AnimeStudio." + entry.Type);
                            if (type == null || !edge.Reference.ObjectType.IsAssignableFrom(type)) continue;
                            var key = (Path.GetFullPath(entry.Source).ToUpperInvariant(), entry.Offset);
                            if (attempted.Contains(key)) continue;
                            if (attempted.Count >= MaximumBundles) break;
                            attempted.Add(key);
                            pending.Add(entry);
                        }
                    }
                    if (pending.Count == 0) return Finish(graph, loaded);
                    manager.FilterData = new AssetsManager.AssetFilterData
                    {
                        Items = pending.Select(e => new AssetsManager.AssetFilterDataItem
                        { Source = e.Source, Offset = e.Offset, PathID = e.PathID, Name = e.Name, Type = e.Type }).ToList()
                    };
                    manager.LoadFiles(pending.Select(e => e.Source).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), mergeSplitAssets: false);
                    loaded += pending.Count;
                }
                return Finish(Walk(rootList), loaded);
            }
            finally { manager.FilterData = oldFilter; manager.ResolveDependencies = oldResolve; }
        }

        private Result Finish(Graph graph, int loaded) => new(graph.Objects, graph.Edges
            .Where(e => !e.Reference.IsNull && !e.Reference.TryGetObject(out _))
            .Select(e => new MissingReference(e.Owner.assetsFile.fileName, e.Owner.m_PathID.ToString(), e.Owner.Name,
                e.Field, e.Reference.SerializedFileName, e.Reference.PathID.ToString(), e.Reference.ObjectType.Name,
                candidates.ContainsKey(e.Reference.PathID) ? "Candidate did not resolve the qualified reference, was unavailable, or a limit was reached" : "No candidate in asset map"))
            .ToList(), loaded);

        private sealed record Edge(Object Owner, string Field, IObjectReference Reference);
        private sealed record Graph(List<Object> Objects, List<Edge> Edges);
        private static Graph Walk(IEnumerable<Object> roots)
        {
            var objects = new List<Object>();
            var edges = new List<Edge>();
            var visited = new HashSet<Object>();
            var pending = new Queue<Object>(roots);
            while (pending.TryDequeue(out var obj))
            {
                if (obj == null || !visited.Add(obj)) continue;
                objects.Add(obj);
                void Add(string field, IObjectReference pointer)
                {
                    if (pointer == null) return;
                    edges.Add(new Edge(obj, field, pointer));
                    if (!pointer.IsNull && pointer.TryGetObject(out var target)) pending.Enqueue(target);
                }
                if (obj is Component component) Add("m_GameObject", component.m_GameObject);
                switch (obj)
                {
                    case GameObject gameObject:
                        for (int i = 0; i < gameObject.m_Components.Count; i++) Add($"m_Components[{i}]", gameObject.m_Components[i]);
                        break;
                    case Transform transform:
                        for (int i = 0; i < transform.m_Children.Count; i++) Add($"m_Children[{i}]", transform.m_Children[i]);
                        break;
                    case Animator animator:
                        Add("m_Avatar", animator.m_Avatar);
                        // Controllers can pull in an entire game's clip library. Clips are explicit roots.
                        break;
                    case MeshFilter filter:
                        Add("m_Mesh", filter.m_Mesh);
                        break;
                    case Renderer renderer:
                        for (int i = 0; i < renderer.m_Materials.Count; i++) Add($"m_Materials[{i}]", renderer.m_Materials[i]);
                        if (renderer is SkinnedMeshRenderer skin)
                        {
                            Add("m_Mesh", skin.m_Mesh); Add("m_RootBone", skin.m_RootBone);
                            for (int i = 0; i < skin.m_Bones.Count; i++) Add($"m_Bones[{i}]", skin.m_Bones[i]);
                        }
                        break;
                    case Material material:
                        Add("m_Shader", material.m_Shader);
                        foreach (var texture in material.m_SavedProperties.m_TexEnvs) Add("m_TexEnvs." + texture.Key, texture.Value.m_Texture);
                        break;
                }
            }
            return new Graph(objects, edges);
        }
    }
}
