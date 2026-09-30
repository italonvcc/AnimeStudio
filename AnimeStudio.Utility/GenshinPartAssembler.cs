using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AnimeStudio
{
    public sealed record GenshinPartReplacement(GameObject Root, string Slot, string[] RemoveMeshes);

    public static class GenshinPartAssembler
    {
        public static object Replace(ModelConverter target, ModelConverter part, string slot, string[] removeMeshes)
        {
            if (string.IsNullOrWhiteSpace(slot) || slot.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException("Supply a simple part-slot name.");
            string Relative(string path, ImportedFrame root) => path == root.Path ? "" : path.StartsWith(root.Path + "/", StringComparison.Ordinal)
                ? path[(root.Path.Length + 1)..] : throw new InvalidDataException("Part path outside root: " + path);
            string TargetPath(string path) => target.RootFrame.Path + "/" + Relative(path, part.RootFrame);
            var remove = removeMeshes.Select(p => target.MeshList.SingleOrDefault(m => Relative(m.Path, target.RootFrame) == p)
                ?? throw new InvalidDataException("Replacement mesh missing/ambiguous: " + p)).ToArray();
            var removedPaths = remove.Select(m => m.Path).ToHashSet();
            // An explicitly replaced renderer no longer constrains the incoming
            // branch. Every other renderer and morph still vetoes rest changes.
            var retainedMeshes = target.MeshList.Where(m => !removedPaths.Contains(m.Path)).ToArray();
            // A hairless source base can accept an add-only part. The same bone,
            // rest-transform, bind-matrix and mesh-path checks below still apply.
            if (part.MeshList.Count == 0) throw new InvalidDataException("Replacement requires a nonempty part.");
            bool FramesMatch(ImportedFrame a, ImportedFrame b)
            {
                float position = Math.Max(Math.Abs(a.LocalPosition.X - b.LocalPosition.X), Math.Max(Math.Abs(a.LocalPosition.Y - b.LocalPosition.Y), Math.Abs(a.LocalPosition.Z - b.LocalPosition.Z)));
                float scale = Math.Max(Math.Abs(a.LocalScale.X - b.LocalScale.X), Math.Max(Math.Abs(a.LocalScale.Y - b.LocalScale.Y), Math.Abs(a.LocalScale.Z - b.LocalScale.Z)));
                float dot = Math.Abs(a.LocalRotation.X * b.LocalRotation.X + a.LocalRotation.Y * b.LocalRotation.Y + a.LocalRotation.Z * b.LocalRotation.Z + a.LocalRotation.W * b.LocalRotation.W);
                float Norm(Quaternion q) => q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W;
                return float.IsFinite(position) && float.IsFinite(scale) && float.IsFinite(dot) &&
                    position <= 0.0001f && scale <= 0.0001f && dot >= 0.999999f &&
                    Math.Abs(Norm(a.LocalRotation)-1) <= 0.0001f && Math.Abs(Norm(b.LocalRotation)-1) <= 0.0001f;
            }
            void CheckFrame(ImportedFrame a, ImportedFrame b)
            {
                if (!FramesMatch(a, b))
                    throw new InvalidDataException("Incompatible rest transform: " + a.Path);
            }
            CheckFrame(part.RootFrame, target.RootFrame);
            var targetBones = retainedMeshes.SelectMany(m => m.BoneList ?? new List<ImportedBone>()).ToLookup(b => b.Path);
            var exclusiveRestOverrides = new List<object>();
            bool Under(string path, string root) => path == root || path.StartsWith(root + "/", StringComparison.Ordinal);
            bool ExclusiveTransformSubtree(ImportedFrame branch)
            {
                // A source-local rest override is safe for this geometry export
                // only if nothing already rendered, weighted or attached in the
                // target depends on the branch. A synthetic/unresolved GameObject
                // or any non-Transform component vetoes the override.
                if (retainedMeshes.Any(m => Under(m.Path, branch.Path) ||
                    (m.BoneList?.Any(b => Under(b.Path, branch.Path)) ?? false)) ||
                    target.MorphList.Any(m => !removedPaths.Contains(m.Path) && Under(m.Path, branch.Path))) return false;
                var pending = new Stack<ImportedFrame>();
                pending.Push(branch);
                while (pending.Count > 0)
                {
                    var frame = pending.Pop();
                    var go = frame.SourceGameObject;
                    if (go?.m_Components?.Count != 1 ||
                        !go.m_Components[0].TryGet(out var component) || component is not Transform)
                        return false;
                    for (int i = 0; i < frame.Count; i++) pending.Push(frame[i]);
                }
                return true;
            }
            int checkedBones = 0;
            int bonesWithoutBaseBind = 0;
            var addedBoneFrames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var bone in part.MeshList.SelectMany(m => m.BoneList ?? new List<ImportedBone>()))
            {
                if (Enumerable.Range(0, 16).Any(i => !float.IsFinite(bone.Matrix[i])))
                    throw new InvalidDataException("Non-finite part bind matrix: " + bone.Path);
                var frame = part.RootFrame.FindFrameByPath(bone.Path) ?? throw new InvalidDataException("Missing part bone: " + bone.Path);
                while (frame != part.RootFrame)
                {
                    var matching = target.RootFrame.FindFrameByPath(TargetPath(frame.Path));
                    if (matching != null && !FramesMatch(frame, matching))
                    {
                        if (frame.SourceGameObject == null || !ExclusiveTransformSubtree(matching)) CheckFrame(frame, matching);
                        exclusiveRestOverrides.Add(new { targetPath = matching.Path, sourcePath = frame.Path,
                            targetSourceGameObject = new { matching.SourceGameObject.Name,
                                PathID = matching.SourceGameObject.m_PathID.ToString(),
                                Source = matching.SourceGameObject.assetsFile.originalPath },
                            incomingSourceGameObject = new { frame.SourceGameObject.Name,
                                PathID = frame.SourceGameObject.m_PathID.ToString(),
                                Source = frame.SourceGameObject.assetsFile.originalPath },
                            previous = new { matching.LocalPosition, matching.LocalRotation, matching.LocalScale },
                            replacement = new { frame.LocalPosition, frame.LocalRotation, frame.LocalScale },
                            proof = "No retained mesh, morph, weighted bone or non-Transform GameObject component in the target subtree." });
                        matching.LocalPosition = frame.LocalPosition;
                        matching.LocalRotation = frame.LocalRotation;
                        matching.LocalScale = frame.LocalScale;
                    }
                    else if (matching != null) CheckFrame(frame, matching);
                    else addedBoneFrames.Add(frame.Path);
                    frame = frame.Parent;
                }
                var baseBinds = targetBones[TargetPath(bone.Path)].ToArray();
                // A valid target bone need not carry a skin weight in the base
                // meshes. Its exact rest path is checked above; the incoming
                // part supplies its own bind and Restore validates that bind
                // against the assembled skeleton before export.
                if (baseBinds.Length == 0) bonesWithoutBaseBind++;
                foreach (var other in baseBinds)
                    if (Enumerable.Range(0, 16).Any(i => !float.IsFinite(bone.Matrix[i]) || !float.IsFinite(other.Matrix[i]) || Math.Abs(bone.Matrix[i] - other.Matrix[i]) > 0.001f))
                        throw new InvalidDataException("Incompatible bind matrix: " + bone.Path);
                checkedBones++;
            }
            foreach (var mesh in part.MeshList)
                if (target.MeshList.Any(m => m.Path == TargetPath(mesh.Path) && !removedPaths.Contains(m.Path)))
                    throw new InvalidDataException("Part collides with a retained mesh: " + mesh.Path);
            ImportedFrame EnsureFrame(ImportedFrame source)
            {
                if (source == part.RootFrame) return target.RootFrame;
                var existing = target.RootFrame.FindFrameByPath(TargetPath(source.Path));
                if (existing != null) { CheckFrame(source, existing); return existing; }
                var result = new ImportedFrame { Name = source.Name, SourceGameObject = source.SourceGameObject,
                    LocalPosition = source.LocalPosition, LocalRotation = source.LocalRotation, LocalScale = source.LocalScale };
                EnsureFrame(source.Parent).AddChild(result); return result;
            }
            foreach (var mesh in part.MeshList) EnsureFrame(part.RootFrame.FindFrameByPath(mesh.Path) ?? throw new InvalidDataException("Missing mesh transform"));
            foreach (var bone in part.MeshList.SelectMany(m => m.BoneList ?? new List<ImportedBone>()))
                EnsureFrame(part.RootFrame.FindFrameByPath(bone.Path) ?? throw new InvalidDataException("Missing part bone transform"));
            string prefix = slot + "__";
            foreach (var material in part.MaterialList)
            {
                string original = material.Name;
                material.Name = prefix + original;
                if (target.MaterialList.Any(m => m.Name == material.Name)) throw new InvalidDataException("Duplicate replacement slot/material: " + material.Name);
                foreach (var sub in part.MeshList.SelectMany(m => m.SubmeshList).Where(s => s.Material == original)) sub.Material = material.Name;
                foreach (var texture in material.Textures) texture.Name = prefix + texture.Name;
            }
            foreach (var texture in part.TextureList) texture.Name = prefix + texture.Name;
            target.MeshList.RemoveAll(m => removedPaths.Contains(m.Path));
            target.MorphList.RemoveAll(m => removedPaths.Contains(m.Path));
            foreach (var mesh in part.MeshList)
            {
                mesh.Path = TargetPath(mesh.Path);
                foreach (var bone in mesh.BoneList ?? new List<ImportedBone>()) bone.Path = TargetPath(bone.Path);
                target.MeshList.Add(mesh);
            }
            foreach (var morph in part.MorphList) { morph.Path = TargetPath(morph.Path); target.MorphList.Add(morph); }
            target.MaterialList.AddRange(part.MaterialList); target.TextureList.AddRange(part.TextureList);
            return new { slot, removedMeshes = removeMeshes, addedMeshes = part.MeshList.Select(m => m.Path).ToArray(), checkedBones,
                bonesWithoutBaseBind, addedBoneFrames = addedBoneFrames.Count,
                exclusiveRestOverrides,
                compatibility = exclusiveRestOverrides.Count > 0
                    ? "Shared weighted bones and retained geometry checked. Exclusive Transform-only, unweighted target rest branches copied from source part and recorded; incoming finite binds checked by Restore. Animation behavior of overridden branches is not certified."
                    : addedBoneFrames.Count == 0
                    ? "Bone paths and ancestor local rest transforms validated. Available base bind matrices matched; new unweighted bones use source part binds checked by Restore. Original skin weights retained."
                    : "Shared bone paths and ancestor local rest transforms validated. Missing bone frames copied from the source part; incoming finite binds checked by Restore. Available base bind matrices matched; original skin weights retained." };
        }
    }
}
