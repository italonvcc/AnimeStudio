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
            if (remove.Length == 0 || part.MeshList.Count == 0) throw new InvalidDataException("Replacement requires existing meshes and a nonempty part.");
            void CheckFrame(ImportedFrame a, ImportedFrame b)
            {
                float position = Math.Max(Math.Abs(a.LocalPosition.X - b.LocalPosition.X), Math.Max(Math.Abs(a.LocalPosition.Y - b.LocalPosition.Y), Math.Abs(a.LocalPosition.Z - b.LocalPosition.Z)));
                float scale = Math.Max(Math.Abs(a.LocalScale.X - b.LocalScale.X), Math.Max(Math.Abs(a.LocalScale.Y - b.LocalScale.Y), Math.Abs(a.LocalScale.Z - b.LocalScale.Z)));
                float dot = Math.Abs(a.LocalRotation.X * b.LocalRotation.X + a.LocalRotation.Y * b.LocalRotation.Y + a.LocalRotation.Z * b.LocalRotation.Z + a.LocalRotation.W * b.LocalRotation.W);
                float Norm(Quaternion q) => q.X * q.X + q.Y * q.Y + q.Z * q.Z + q.W * q.W;
                if (!float.IsFinite(position) || !float.IsFinite(scale) || !float.IsFinite(dot) || position > 0.0001f || scale > 0.0001f || dot < 0.999999f || Math.Abs(Norm(a.LocalRotation)-1) > 0.0001f || Math.Abs(Norm(b.LocalRotation)-1) > 0.0001f)
                    throw new InvalidDataException("Incompatible rest transform: " + a.Path);
            }
            CheckFrame(part.RootFrame, target.RootFrame);
            var targetBones = target.MeshList.SelectMany(m => m.BoneList ?? new List<ImportedBone>()).ToLookup(b => b.Path);
            int checkedBones = 0;
            foreach (var bone in part.MeshList.SelectMany(m => m.BoneList ?? new List<ImportedBone>()))
            {
                var frame = part.RootFrame.FindFrameByPath(bone.Path) ?? throw new InvalidDataException("Missing part bone: " + bone.Path);
                while (frame != part.RootFrame)
                {
                    var matching = target.RootFrame.FindFrameByPath(TargetPath(frame.Path)) ?? throw new InvalidDataException("Part requires a bone absent from the base: " + frame.Path);
                    CheckFrame(frame, matching); frame = frame.Parent;
                }
                var baseBinds = targetBones[TargetPath(bone.Path)].ToArray();
                if (baseBinds.Length == 0) throw new InvalidDataException("No base bind-pose evidence for " + bone.Path);
                foreach (var other in baseBinds)
                    if (Enumerable.Range(0, 16).Any(i => !float.IsFinite(bone.Matrix[i]) || !float.IsFinite(other.Matrix[i]) || Math.Abs(bone.Matrix[i] - other.Matrix[i]) > 0.001f))
                        throw new InvalidDataException("Incompatible bind matrix: " + bone.Path);
                checkedBones++;
            }
            var removedPaths = remove.Select(m => m.Path).ToHashSet();
            foreach (var mesh in part.MeshList)
                if (target.MeshList.Any(m => m.Path == TargetPath(mesh.Path) && !removedPaths.Contains(m.Path)))
                    throw new InvalidDataException("Part collides with a retained mesh: " + mesh.Path);
            ImportedFrame EnsureFrame(ImportedFrame source)
            {
                if (source == part.RootFrame) return target.RootFrame;
                var existing = target.RootFrame.FindFrameByPath(TargetPath(source.Path));
                if (existing != null) { CheckFrame(source, existing); return existing; }
                var result = new ImportedFrame { Name = source.Name, LocalPosition = source.LocalPosition, LocalRotation = source.LocalRotation, LocalScale = source.LocalScale };
                EnsureFrame(source.Parent).AddChild(result); return result;
            }
            foreach (var mesh in part.MeshList) EnsureFrame(part.RootFrame.FindFrameByPath(mesh.Path) ?? throw new InvalidDataException("Missing mesh transform"));
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
                compatibility = "Shared bone paths, ancestor local rest transforms and all base bind matrices validated; original skin weights retained." };
        }
    }
}
