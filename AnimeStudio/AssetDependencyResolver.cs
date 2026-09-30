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
        private readonly AssetDependencyIndex candidates;
        private readonly HashSet<(string, long)> attempted = new();
        public int MaximumBundles { get; set; } = 512;
        public int MaximumPasses { get; set; } = 16;
        // Opt-in policies keep ordinary model/VFX walks unchanged. A weapon can
        // inspect its attached controller without fetching an entire shared library.
        public bool IncludeMaterials { get; set; } = true;
        public bool IncludeAnimatorControllers { get; set; }
        public bool IncludeAnimationObjectReferences { get; set; }
        public int MaximumControllerClips { get; set; } = 256;

        // The complete graph has already been resolved; inspect subsets without
        // rebuilding the map index or repeating dependency loads per effect.
        public Result InspectLoaded(IEnumerable<Object> roots) => Finish(Walk(roots), 0);

        public AssetDependencyResolver(AssetsManager manager, IEnumerable<AssetEntry> map)
            : this(manager, new AssetDependencyIndex(map)) { }

        public AssetDependencyResolver(AssetsManager manager, AssetDependencyIndex index)
        {
            this.manager = manager ?? throw new ArgumentNullException(nameof(manager));
            candidates = index ?? throw new ArgumentNullException(nameof(index));
        }

        public sealed record MissingReference(string OwnerFile, string OwnerPathID, string OwnerName,
            string Field, string TargetFile, string TargetPathID, string ExpectedType, string Reason);
        public sealed record Result(List<Object> Objects, List<MissingReference> Missing, int LoadedBundles);

        public Result Resolve(IEnumerable<Object> roots, CancellationToken cancellation = default)
        {
            var rootList = roots.ToArray();
            var oldFilter = manager.FilterData;
            var oldResolve = manager.ResolveDependencies;
            var oldSelectedOffsets = manager.UseSelectedGenshinOffsets;
            int loaded = 0;
            try
            {
                // Candidate discovery uses the map; PPtr remains the authority for CAB + path identity.
                manager.ResolveDependencies = false;
                for (int pass = 0; pass < MaximumPasses; pass++)
                {
                    Logger.Info($"Resolving dependencies: pass {pass + 1}, {loaded} bundles loaded");
                    cancellation.ThrowIfCancellationRequested();
                    var graph = Walk(rootList);
                    var pending = new List<AssetEntry>();
                    bool exactContainerPending = false;
                    foreach (var edge in graph.Edges.Where(e => !e.Reference.IsNull && !e.Reference.TryGetObject(out _)))
                    {
                        // The source index gives a qualified CAB location even
                        // when the minimal map omits this object type (Cubemap is
                        // one example). Load that exact container first; only the
                        // original PPtr can prove its PathID and expected type.
                        if (candidates.TryGetContainer(edge.Reference.SerializedFileName, out var container))
                        {
                            if (File.Exists(container.Source))
                            {
                                var key = (Path.GetFullPath(container.Source).ToUpperInvariant(), container.Offset);
                                if (!attempted.Contains(key) && attempted.Count < MaximumBundles)
                                {
                                    attempted.Add(key);
                                    pending.Add(new AssetEntry { Source = container.Source, Offset = container.Offset,
                                        PathID = edge.Reference.PathID, Type = ClassIDType.UnknownType,
                                        Name = "Qualified container " + container.SerializedFile, Container = "", Hash = "" });
                                    exactContainerPending = true;
                                }
                            }
                            continue;
                        }
                        if (!candidates.TryGet(edge.Reference.PathID, out var first, out var additional)) continue;
                        void Consider(AssetDependencyIndex.Candidate entry)
                        {
                            if (entry.Offset < 0 || !File.Exists(entry.Source)) return;
                            var type = typeof(Object).Assembly.GetType("AnimeStudio." + entry.Type);
                            if (type == null || !edge.Reference.ObjectType.IsAssignableFrom(type)) return;
                            var key = (Path.GetFullPath(entry.Source).ToUpperInvariant(), entry.Offset);
                            if (attempted.Contains(key) || attempted.Count >= MaximumBundles) return;
                            attempted.Add(key);
                            pending.Add(entry.ToAssetEntry());
                        }
                        Consider(first);
                        foreach (var entry in additional) Consider(entry);
                    }
                    if (pending.Count == 0) return Finish(graph, loaded);
                    manager.FilterData = new AssetsManager.AssetFilterData
                    {
                        Items = pending.Select(e => new AssetsManager.AssetFilterDataItem
                        { Source = e.Source, Offset = e.Offset, PathID = e.PathID, Name = e.Name, Type = e.Type }).ToList()
                    };
                    if (exactContainerPending && manager.Game.Type.IsGI())
                        manager.UseSelectedGenshinOffsets = true;
                    manager.LoadFiles(pending.Select(e => e.Source).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), mergeSplitAssets: false);
                    manager.UseSelectedGenshinOffsets = oldSelectedOffsets;
                    loaded += pending.Count;
                }
                return Finish(Walk(rootList), loaded);
            }
            finally { manager.FilterData = oldFilter; manager.ResolveDependencies = oldResolve;
                manager.UseSelectedGenshinOffsets = oldSelectedOffsets; }
        }

        private Result Finish(Graph graph, int loaded) => new(graph.Objects, graph.Edges
            .Where(e => !e.Reference.IsNull && !e.Reference.TryGetObject(out _))
            .Select(e => new MissingReference(e.Owner.assetsFile.fileName, e.Owner.m_PathID.ToString(), e.Owner.Name,
                e.Field, e.Reference.SerializedFileName, e.Reference.PathID.ToString(), e.Reference.ObjectType.Name,
                candidates.TryGetContainer(e.Reference.SerializedFileName, out _)
                    ? "Exact indexed container did not resolve the qualified PathID/type, was unavailable, or a limit was reached"
                    : candidates.ContainsPathID(e.Reference.PathID)
                        ? "Candidate did not resolve the qualified reference, was unavailable, or a limit was reached"
                        : "No candidate in asset map or indexed container"))
            .ToList(), loaded);

        private sealed record Edge(Object Owner, string Field, IObjectReference Reference);
        private sealed record Graph(List<Object> Objects, List<Edge> Edges);
        private Graph Walk(IEnumerable<Object> roots)
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
                        for (int i = 0; i < gameObject.m_Components.Count; i++) Add($"m_Components[{i}]", gameObject.m_Components[i].Cast<Object>());
                        break;
                    case Transform transform:
                        for (int i = 0; i < transform.m_Children.Count; i++) Add($"m_Children[{i}]", transform.m_Children[i]);
                        break;
                    case Animator animator:
                        Add("m_Avatar", animator.m_Avatar);
                        if (IncludeAnimatorControllers) Add("m_Controller", animator.m_Controller);
                        // The default policy leaves controllers out of ordinary model walks.
                        break;
                    case AnimatorOverrideController overrides when IncludeAnimatorControllers:
                        Add("m_Controller", overrides.m_Controller);
                        if (overrides.m_Clips.Count > MaximumControllerClips)
                            throw new InvalidDataException("Attached override controller exceeds the bounded clip limit.");
                        for (int i = 0; i < overrides.m_Clips.Count; i++)
                        {
                            Add($"m_Clips[{i}].m_OriginalClip", overrides.m_Clips[i].m_OriginalClip);
                            Add($"m_Clips[{i}].m_OverrideClip", overrides.m_Clips[i].m_OverrideClip);
                        }
                        break;
                    case AnimatorController controller when IncludeAnimatorControllers:
                        if (controller.m_AnimationClips.Count > MaximumControllerClips)
                            throw new InvalidDataException("Attached Animator controller exceeds the bounded clip limit.");
                        for (int i = 0; i < controller.m_AnimationClips.Count; i++)
                            Add($"m_AnimationClips[{i}]", controller.m_AnimationClips[i]);
                        break;
                    case Animation animation:
                        Add("m_Animation", animation.m_Animation);
                        for (int i = 0; i < animation.m_Animations.Count; i++) Add($"m_Animations[{i}]", animation.m_Animations[i]);
                        break;
                    case AnimationClip clip when IncludeAnimationObjectReferences:
                        for (int i = 0; i < clip.m_PPtrCurves.Count; i++)
                        {
                            Add($"m_PPtrCurves[{i}].script", clip.m_PPtrCurves[i].script);
                            for (int key = 0; key < clip.m_PPtrCurves[i].curve.Count; key++)
                                Add($"m_PPtrCurves[{i}].curve[{key}].value", clip.m_PPtrCurves[i].curve[key].value);
                        }
                        for (int i = 0; i < clip.m_Events.Count; i++)
                            Add($"m_Events[{i}].objectReferenceParameter", clip.m_Events[i].objectReferenceParameter);
                        break;
                    case MeshFilter filter:
                        Add("m_Mesh", filter.m_Mesh);
                        break;
                    case Renderer renderer:
                        if (IncludeMaterials)
                            for (int i = 0; i < renderer.m_Materials.Count; i++) Add($"m_Materials[{i}]", renderer.m_Materials[i]);
                        if (renderer is GenshinParticleSystemRenderer particleRenderer && particleRenderer.m_Meshes != null)
                            for (int i = 0; i < particleRenderer.m_Meshes.Length; i++)
                                Add($"m_Meshes[{i}]", particleRenderer.m_Meshes[i]);
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
