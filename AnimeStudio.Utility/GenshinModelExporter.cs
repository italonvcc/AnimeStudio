using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace AnimeStudio
{
    public static class GenshinModelExporter
    {
        public static string Export(AssetsManager manager, IEnumerable<AssetEntry> map, Object root,
            string destination, AnimationClip[] clips = null, IReadOnlyList<GenshinPartReplacement> replacements = null, bool exportMaterials = true)
        {
            if (!manager.Game.Type.IsGI()) throw new ArgumentException("Select Genshin Impact before exporting.");
            if (root is not (Animator or GameObject)) throw new ArgumentException("Select an Animator or GameObject root.");
            var animator = root as Animator;
            destination = Path.GetFullPath(destination);
            if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Choose a new export directory.");
            var roots = new List<Object> { root };
            if (replacements != null) roots.AddRange(replacements.Select(p => p.Root));
            if (clips != null) roots.AddRange(clips);
            var dependencies = new AssetDependencyResolver(manager, map).Resolve(roots);
            var missingGeometry = dependencies.Missing.Where(m => m.ExpectedType is "Mesh" or "Transform" or "GameObject" or "Avatar" or "Material" or "Texture" or "Texture2D").ToArray();
            if (missingGeometry.Length > 0)
                throw new InvalidOperationException($"Cannot export a complete model: {missingGeometry.Length} mesh, rig, material, or texture dependencies are unresolved. Resolve the source map/dependencies first.");
            var options = new ModelConverter.Options
            {
                game = manager.Game, imageFormat = ImageFormat.Png, exportMaterials = exportMaterials,
                collectAnimations = false, exportAnimatorRootOnly = true, materials = new HashSet<Material>(),
                uvs = Enumerable.Range(0, 8).ToDictionary(i => $"UV{i}", _ => (true, 0)),
                texs = new Dictionary<string, int>()
            };
            var converted = animator != null ? new ModelConverter(animator, options, Array.Empty<AnimationClip>())
                : new ModelConverter((GameObject)root, options, Array.Empty<AnimationClip>());
            if (converted.MeshList.Count == 0) throw new InvalidOperationException("No meshes were converted.");
            var assembly = new List<object>();
            foreach (var replacement in replacements ?? Array.Empty<GenshinPartReplacement>())
                assembly.Add(new { source = Identity(replacement.Root), result = GenshinPartAssembler.Replace(converted,
                    new ModelConverter(replacement.Root, options, Array.Empty<AnimationClip>()), replacement.Slot, replacement.RemoveMeshes) });
            Directory.CreateDirectory(destination);
            var name = SafeName(root.Name);
            var fbx = Path.Combine(destination, name + ".fbx");
            Fbx.Exporter.Export(fbx, converted, new Fbx.ExportOptions
            {
                exportAllNodes = true, exportSkins = true, exportAnimations = false, exportBlendShape = true, continuousBoneHierarchy = true,
                boneSize = 10, scaleFactor = 100, fbxVersion = 3, fbxFormat = 0, eulerFilter = true, filterPrecision = 0.25f
            });
            var settings = new JsonSerializerSettings { Converters = { new StringEnumConverter() } };
            var materials = Path.Combine(destination, "Materials");
            if (exportMaterials) Directory.CreateDirectory(materials);
            foreach (var material in options.materials)
                File.WriteAllText(Path.Combine(materials, SafeName(material.Name) + "_" + material.m_PathID + ".json"), MaterialJsonExporter.Serialize(material, settings));
            var sourceClips = new List<object>();
            if (clips?.Length > 0)
            {
                var animationDirectory = Path.Combine(destination, "Animations");
                Directory.CreateDirectory(animationDirectory);
                foreach (var clip in clips)
                {
                    var file = SafeName(clip.Name) + "_" + clip.m_PathID + ".anim";
                    File.WriteAllText(Path.Combine(animationDirectory, file), clip.Convert());
                    var humanoid = clip.m_ClipBindingConstant?.genericBindings.Count(b => b.typeID == ClassIDType.Animator && b.customType == 8) ?? 0;
                    sourceClips.Add(new { source = Identity(clip), file = "Animations/" + file, humanoidBindings = humanoid,
                        sampleRate = clip.m_SampleRate, startTime = clip.m_MuscleClip?.m_StartTime, stopTime = clip.m_MuscleClip?.m_StopTime,
                        loopTime = clip.m_MuscleClip?.m_LoopTime,
                        fbxStatus = "Rig/model only. Animation is exported as native Unity .anim." });
                }
            }
            var manifest = new
            {
                schemaVersion = 1, game = manager.Game.Name, gameVersion = GenshinSourceMetadata.GameVersion(root.assetsFile.originalPath), unityVersion = root.assetsFile.unityVersion,
                toolVersion = typeof(GenshinModelExporter).Assembly.GetName().Version.ToString(),
                toolRevision = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(GenshinModelExporter).Assembly)?.InformationalVersion,
                root = Identity(root), model = Path.GetFileName(fbx),
                units = "meters (FBX system unit: 100 centimeters)",
                scope = "Model hierarchy, skinning, materials, textures, and explicitly selected clips. Runtime scripts, controllers and effect simulation are not reconstructed.",
                dependencies.LoadedBundles, unresolved = dependencies.Missing,
                parts = dependencies.Objects.OfType<SkinnedMeshRenderer>().Select(skin => new
                {
                    name = skin.m_GameObject.Name, mesh = skin.m_Mesh.Name,
                    meshPathID = skin.m_Mesh.m_PathID.ToString(),
                    bones = skin.m_Bones.Select(p => new { p.Name, PathID = p.m_PathID.ToString() }).ToArray(),
                    materials = skin.m_Materials.Select(p => new { p.Name, PathID = p.m_PathID.ToString() }).ToArray()
                }),
                meshes = converted.MeshList.Select(m => new { m.Path, vertices = m.VertexList.Count, bones = m.BoneList?.Count }),
                clips = converted.AnimationList.Select(a => new { a.Name, a.SampleRate, tracks = a.TrackList.Count }),
                sourceClips, assembly,
                objects = dependencies.Objects.Select(Identity)
            };
            File.WriteAllText(Path.Combine(destination, "manifest.json"), JsonConvert.SerializeObject(manifest, Formatting.Indented, settings));
            return fbx;
        }

        private static object Identity(Object obj) => new { obj.Name, Type = obj.type.ToString(),
            SerializedFile = obj.assetsFile.fileName, PathID = obj.m_PathID.ToString(), Source = obj.assetsFile.originalPath };
        private static string SafeName(string value) => string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
    }
}
