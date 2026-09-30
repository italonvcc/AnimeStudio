using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AnimeStudio
{
    // Adds only source-linked bones and their ancestors to a selected weapon
    // subtree. Other meshes under the common source ancestor are excluded.
    internal sealed class GenshinWeaponRigClosure
    {
        public GameObject ExportRoot { get; private init; }
        public HashSet<Transform> IncludedTransforms { get; private init; }
        public HashSet<Transform> SelectedTransforms { get; private init; }
        public HashSet<Renderer> IncludedRenderers { get; private init; }
        public Transform[] ReferencedBones { get; private init; }
        public string SelectedRootPath { get; private init; }
        public int ExcludedSiblingTransforms { get; private init; }

        public static GenshinWeaponRigClosure Inspect(Object selected)
        {
            GameObject selectedGo = selected switch
            {
                GameObject go => go,
                Animator animator when animator.m_GameObject.TryGet(out GameObject go) => go,
                _ => throw new InvalidDataException("Selected weapon has no source GameObject for rig closure.")
            };
            var selectedRoot = selectedGo.m_Transform
                ?? throw new InvalidDataException("Selected weapon has no source Transform for rig closure.");
            var selectedTransforms = new HashSet<Transform>();
            var renderers = new HashSet<Renderer>();
            void VisitSelected(Transform transform)
            {
                if (!selectedTransforms.Add(transform))
                    throw new InvalidDataException("Cycle or shared Transform in selected weapon hierarchy.");
                if (!transform.m_GameObject.TryGet(out GameObject gameObject))
                    throw new InvalidDataException("Selected weapon Transform has no source GameObject.");
                if (gameObject.m_MeshRenderer != null) renderers.Add(gameObject.m_MeshRenderer);
                if (gameObject.m_SkinnedMeshRenderer != null) renderers.Add(gameObject.m_SkinnedMeshRenderer);
                foreach (var child in transform.m_Children)
                {
                    if (!child.TryGet(out Transform next))
                        throw new InvalidDataException("Selected weapon child Transform pointer is unresolved.");
                    VisitSelected(next);
                }
            }
            VisitSelected(selectedRoot);
            var referenced = new HashSet<Transform>();
            var skins = renderers.OfType<SkinnedMeshRenderer>().ToArray();
            // Detect a detached reference before imposing the extra closure
            // proof. Ordinary weapon exports keep their existing converter path.
            foreach (var skin in skins)
            {
                foreach (var pointer in skin.m_Bones)
                    if (pointer.TryGet(out Transform bone)) referenced.Add(bone);
                if (skin.m_RootBone.TryGet(out Transform rootBone)) referenced.Add(rootBone);
            }
            if (referenced.Count == 0 || referenced.All(selectedTransforms.Contains)) return null;
            referenced.Clear();
            foreach (var skin in skins)
            {
                if (!skin.m_Mesh.TryGet(out Mesh mesh))
                    throw new InvalidDataException("Skinned weapon mesh pointer is unresolved before rig closure.");
                int count = mesh.m_BindPose?.Length ?? 0;
                if (count == 0 || skin.m_Bones.Count != count)
                    throw new InvalidDataException($"Source rig closure needs one qualified bone per bind pose: renderer {skin.m_PathID}, bones {skin.m_Bones.Count}, bind poses {count}.");
                var bones = new Transform[count];
                for (int index = 0; index < count; index++)
                {
                    if (skin.m_Bones[index].IsNull || !skin.m_Bones[index].TryGet(out bones[index]))
                        throw new InvalidDataException($"Source rig closure cannot resolve bone {index} of renderer {skin.m_PathID}.");
                    referenced.Add(bones[index]);
                }
                if (mesh.m_Skin == null || mesh.m_Skin.Count == 0)
                    throw new InvalidDataException("Source rig closure cannot verify weighted bone indices: " + mesh.Name);
                foreach (var vertex in mesh.m_Skin)
                for (int influence = 0; influence < Math.Min(vertex.weight.Length, vertex.boneIndex.Length); influence++)
                    if (vertex.weight[influence] > 0 &&
                        (vertex.boneIndex[influence] < 0 || vertex.boneIndex[influence] >= count))
                        throw new InvalidDataException("Source weighted bone index exceeds bind-pose/bone pointer count: " + mesh.Name);
                if (!skin.m_RootBone.IsNull)
                {
                    if (!skin.m_RootBone.TryGet(out Transform rootBone))
                        throw new InvalidDataException("Source root bone pointer is unresolved: " + skin.m_PathID);
                    referenced.Add(rootBone);
                }
            }
            // This bounded path intentionally accepts only the already loaded
            // serialized container. External parent traversal needs a separate
            // exact-container preflight, never a name-based approximation.
            var sourceFile = selectedRoot.assetsFile;
            Transform Parent(Transform transform)
            {
                if (transform.m_Father.IsNull) return null;
                if (!transform.m_Father.TryGet(out Transform parent) ||
                    !ReferenceEquals(parent.assetsFile, sourceFile))
                    throw new InvalidDataException("Detached rig parent is unresolved or outside the selected source container.");
                return parent;
            }
            List<Transform> Ancestors(Transform transform)
            {
                var chain = new List<Transform>();
                var seen = new HashSet<Transform>();
                for (var current = transform; current != null; current = Parent(current))
                {
                    if (!ReferenceEquals(current.assetsFile, sourceFile) || !seen.Add(current))
                        throw new InvalidDataException("Detached rig ancestry leaves source container or cycles.");
                    chain.Add(current);
                    if (chain.Count > 256) throw new InvalidDataException("Detached rig ancestry exceeds 256 source transforms.");
                }
                return chain;
            }
            var rootChain = Ancestors(selectedRoot);
            var boneChains = referenced.Select(Ancestors).ToArray();
            var common = rootChain.FirstOrDefault(candidate => boneChains.All(chain => chain.Contains(candidate)))
                ?? throw new InvalidDataException("Detached bones have no source-proven common ancestor with selected weapon.");
            if (!common.m_GameObject.TryGet(out GameObject exportRoot))
                throw new InvalidDataException("Detached rig common ancestor has no source GameObject.");
            var included = new HashSet<Transform>(selectedTransforms);
            void AddThrough(Transform start)
            {
                for (var current = start; current != null; current = Parent(current))
                {
                    included.Add(current);
                    if (ReferenceEquals(current, common)) return;
                }
                throw new InvalidDataException("Detached rig path did not reach its verified common ancestor.");
            }
            AddThrough(selectedRoot);
            foreach (var bone in referenced) AddThrough(bone);
            var path = new List<string>();
            for (var current = selectedRoot; !ReferenceEquals(current, common); current = Parent(current))
            {
                if (!current.m_GameObject.TryGet(out GameObject gameObject) ||
                    string.IsNullOrEmpty(gameObject.Name) || gameObject.Name.Contains('/'))
                    throw new InvalidDataException("Detached rig selected-root path cannot be represented unambiguously.");
                path.Add(gameObject.Name);
            }
            path.Reverse();
            int excluded = included.Sum(transform => transform.m_Children.Count(child =>
                child.TryGet(out Transform next) && !included.Contains(next)));
            return new GenshinWeaponRigClosure
            {
                ExportRoot = exportRoot, IncludedTransforms = included,
                SelectedTransforms = selectedTransforms, IncludedRenderers = renderers,
                ReferencedBones = referenced.OrderBy(t => t.m_PathID).ToArray(),
                SelectedRootPath = string.Join("/", path), ExcludedSiblingTransforms = excluded
            };
        }
    }
}
