using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace AnimeStudio
{
    public static class GenshinModelExporter
    {
        public static string Export(AssetsManager manager, IEnumerable<AssetEntry> map, Object root,
            string destination, AnimationClip[] clips = null, IReadOnlyList<GenshinPartReplacement> replacements = null, bool exportMaterials = true, bool compactAnimations = false,
            AssetDependencyIndex candidateIndex = null, bool tolerateAnimationFailures = false,
            bool allowPerMeshBindPoses = false, bool allowDetachedRigClosure = false)
        {
            if (!manager.Game.Type.IsGI()) throw new ArgumentException("Select Genshin Impact before exporting.");
            if (root is not (Animator or GameObject)) throw new ArgumentException("Select an Animator or GameObject root.");
            var animator = root as Animator;
            destination = Path.GetFullPath(destination);
            if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Choose a new export directory.");
            var roots = new List<Object> { root };
            if (replacements != null) roots.AddRange(replacements.Select(p => p.Root));
            if (clips != null) roots.AddRange(clips);
            var dependencies = candidateIndex == null
                ? new AssetDependencyResolver(manager, map) { IncludeMaterials = exportMaterials }.Resolve(roots)
                : new AssetDependencyResolver(manager, candidateIndex) { IncludeMaterials = exportMaterials }.Resolve(roots);
            var missingGeometry = dependencies.Missing.Where(m => m.ExpectedType is "Mesh" or "Transform" or "GameObject" or "Avatar" ||
                (exportMaterials && m.ExpectedType is "Material" or "Texture" or "Texture2D")).ToArray();
            if (missingGeometry.Length > 0)
                throw new InvalidOperationException($"Cannot export a complete model: {missingGeometry.Length} mesh, rig, material, or texture dependencies are unresolved. Resolve the source map/dependencies first.");
            var rigClosure = allowDetachedRigClosure ? GenshinWeaponRigClosure.Inspect(root) : null;
            if (rigClosure != null && replacements?.Count > 0)
                throw new InvalidDataException("Detached weapon rig closure cannot be combined with part replacement.");
            var options = new ModelConverter.Options
            {
                game = manager.Game, imageFormat = ImageFormat.Png, exportMaterials = exportMaterials,
                collectAnimations = false, exportAnimatorRootOnly = true, materials = new HashSet<Material>(),
                uvs = Enumerable.Range(0, 8).ToDictionary(i => $"UV{i}", _ => (true, 0)),
                texs = new Dictionary<string, int>(),
                includedTransforms = rigClosure?.IncludedTransforms,
                includedRenderers = rigClosure?.IncludedRenderers,
                includedAnimationTransforms = rigClosure?.SelectedTransforms
            };
            var converted = rigClosure != null
                ? new ModelConverter(rigClosure.ExportRoot, options, Array.Empty<AnimationClip>(), ignoreRootAnimator: true)
                : animator != null ? new ModelConverter(animator, options, Array.Empty<AnimationClip>())
                : new ModelConverter((GameObject)root, options, Array.Empty<AnimationClip>());
            if (converted.MeshList.Count == 0) throw new InvalidOperationException("No meshes were converted.");
            var assembly = new List<object>();
            foreach (var replacement in replacements ?? Array.Empty<GenshinPartReplacement>())
                assembly.Add(new { source = Identity(replacement.Root), result = GenshinPartAssembler.Replace(converted,
                    new ModelConverter(replacement.Root, options, Array.Empty<AnimationClip>()), replacement.Slot, replacement.RemoveMeshes) });
            if (exportMaterials && options.materials.GroupBy(m => SafeName(m.Name) + "_" + m.m_PathID + ".json",
                    StringComparer.OrdinalIgnoreCase).Any(g => g.Select(m => (m.assetsFile.originalPath, m.assetsFile.offset,
                        m.assetsFile.fileName, m.m_PathID)).Distinct().Count() > 1))
                throw new InvalidDataException("Distinct source materials would overwrite one material JSON path.");
            Logger.Info("Restoring model bind pose from mesh skin matrices");
            Avatar sourceAvatar = null;
            animator?.m_Avatar.TryGet(out sourceAvatar);
            var sourceFrames = new List<ImportedFrame>();
            void CaptureFrames(ImportedFrame frame)
            {
                sourceFrames.Add(frame);
                for (int index = 0; index < frame.Count; index++) CaptureFrames(frame[index]);
            }
            CaptureFrames(converted.RootFrame);
            var originalTransforms = sourceFrames.Select(frame => new
            { frame, frame.LocalPosition, frame.LocalRotation, frame.LocalScale }).ToArray();
            int bindPoseBones;
            string bindPoseStatus;
            object[] bindPoseConflicts = Array.Empty<object>();
            try
            {
                bindPoseBones = GenshinBindPose.Restore(converted, sourceAvatar);
                bindPoseStatus = "RestoredFromMeshBindPoses";
            }
            catch (GenshinBindPoseConflictException conflict) when (allowPerMeshBindPoses)
            {
                // The FBX writer stores the original inverse bind on each mesh's
                // cluster. A shared node cannot take two global rest transforms;
                // retain the exact source hierarchy rather than change weights.
                foreach (var original in originalTransforms)
                {
                    original.frame.LocalPosition = original.LocalPosition;
                    original.frame.LocalRotation = original.LocalRotation;
                    original.frame.LocalScale = original.LocalScale;
                }
                bindPoseBones = 0;
                bindPoseStatus = "PerMeshSourceBindPosesUnityImportUnvalidated";
                bindPoseConflicts = new object[] { new { conflict.FramePath, conflict.First, conflict.Second,
                    difference = "One source frame has incompatible desired bind worlds from the listed mesh constraints; source hierarchy and per-mesh inverse binds retained." } };
            }
            Directory.CreateDirectory(destination);
            var name = SafeName(root.Name);
            var fbx = Path.Combine(destination, name + ".fbx");
            Fbx.Exporter.Export(fbx, converted, new Fbx.ExportOptions
            {
                exportAllNodes = true, exportSkins = true, exportAnimations = false, exportBlendShape = true, continuousBoneHierarchy = true,
                boneSize = 10, scaleFactor = 100, fbxVersion = 3, fbxFormat = 0, eulerFilter = true, filterPrecision = 0.25f
            });
            var nativeTextureFailures = new List<object>();
            foreach (var texture in converted.TextureList)
            {
                if (texture.SourceTexture == null) continue;
                string relative = Path.ChangeExtension(texture.Name, ".astexture");
                string path = Path.GetFullPath(Path.Combine(destination, relative));
                if (!path.StartsWith(destination + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Native texture payload escapes export root: " + relative);
                try
                {
                    byte[] sourceBytes = texture.SourceTexture.image_data.GetData();
                    GenshinVfxTexturePayload.Write(texture.SourceTexture, sourceBytes, path);
                }
                catch (InvalidDataException ex)
                {
                    nativeTextureFailures.Add(new { texture = Identity(texture.SourceTexture), relative, reason = ex.Message });
                }
            }
            var settings = new JsonSerializerSettings { Converters = { new StringEnumConverter() } };
            var materials = Path.Combine(destination, "Materials");
            if (exportMaterials) Directory.CreateDirectory(materials);
            foreach (var material in options.materials)
                File.WriteAllText(Path.Combine(materials, SafeName(material.Name) + "_" + material.m_PathID + ".json"), MaterialJsonExporter.Serialize(material, settings));
            var cubemapBindings = exportMaterials ? DescribeCubemapBindings(destination, converted) : Array.Empty<object>();
            var materialBindings = exportMaterials ? converted.MeshList.SelectMany(mesh => mesh.SubmeshList.Select((submesh, index) =>
                DescribeMaterialBinding(destination, converted, mesh, submesh, index))).ToArray() : Array.Empty<object>();
            // Geometry-only exports still record original renderer slots, including
            // unresolved qualified pointers, without following appearance assets.
            var sourceRendererSlots = dependencies.Objects.OfType<Renderer>().Select(renderer => new
            {
                renderer = Identity(renderer),
                slots = renderer.m_Materials.Select((pointer, index) =>
                {
                    bool resolved = pointer.TryGet(out Material material);
                    return new { index, pointer.m_PathID, file = ((IObjectReference)pointer).SerializedFileName,
                        resolved, material = resolved ? Identity(material) : null };
                }).ToArray()
            }).ToArray();
            // Keep original GPU mip chains beside preview PNGs. The optional
            // AssetStudio native texture importer consumes these companions;
            // unsupported layouts remain explicitly listed, never mislabeled.
            var nativeTextures = options.materials.SelectMany(m => m.m_SavedProperties.m_TexEnvs)
                .Select(p => p.Value.m_Texture.TryGet(out var t) ? t as Texture : null)
                .Where(t => t != null).Distinct()
                .Select(t => UnityTexturePayload.Export(t,Path.Combine(destination,"NativeTextures"))).ToArray();
            File.WriteAllText(Path.Combine(destination,"native-texture-export.json"),JsonConvert.SerializeObject(nativeTextures,Formatting.Indented));
            File.WriteAllText(Path.Combine(destination,"NATIVE-TEXTURES.txt"),
                "NativeTextures/*.astexture preserves original Windows 2D/cubemap GPU bytes and every authored mip. " +
                "These optional companions require AssetStudioNativeTextureImporter from the com.caladan.stylizedgamekit Unity package. " +
                "Keep NativeTextures beside this model and its Materials JSON; the AssetStudio FBX material binder prefers them over PNG previews. " +
                "Without that importer, continue using the PNG files. Unsupported formats/layouts are listed in native-texture-export.json. " +
                "Native companions are not platform-transcoded; the destination GPU must support their format.\n");
            var sourceClips = new List<object>();
            var animationFailures = new List<object>();
            if (clips?.Length > 0)
            {
                clips = clips.Distinct().ToArray();
                if (clips.GroupBy(c => SafeName(c.Name) + "_" + c.m_PathID + ".anim", StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
                    throw new InvalidDataException("Selected animation sources would overwrite the same output filename.");
                var animationDirectory = Path.Combine(destination, "Animations");
                Directory.CreateDirectory(animationDirectory);
                var records = new object[clips.Length];
                var failures = new object[clips.Length];
                var prepareLock = new object();
                int completed = 0, workers = Math.Min(clips.Length, GenshinExportWorkers.Count);
                long prepareTicks = 0, writeTicks = 0;
                var elapsed = Stopwatch.StartNew();
                Logger.Info($"Exporting animations with {workers} workers; shared readers and native decoding remain serialized");
                Parallel.For(0, clips.Length, new ParallelOptions { MaxDegreeOfParallelism = workers }, index =>
                {
                    var clip = clips[index];
                    var file = SafeName(clip.Name) + "_" + clip.m_PathID + ".anim";
                    string target = Path.Combine(animationDirectory, file);
                    string temporary = target + ".tmp";
                    try
                    {
                        long start = Stopwatch.GetTimestamp();
                        clip.PrepareForExport(reduceConstantKeys: compactAnimations, sourceGate: prepareLock);
                        int resolvedBlendShapeBindings = GenshinBlendShapeCurveBindings.Resolve(
                            clip.m_FloatCurves, converted.MorphList, converted.RootFrame.Path);
                        Interlocked.Add(ref prepareTicks, Stopwatch.GetTimestamp() - start);
                        long writeStart = Stopwatch.GetTimestamp();
                        string yaml = AnimationClipExtensions.ConvertSerializedAnimationClip(clip);
                        File.WriteAllText(temporary, compactAnimations ? GenshinAnimationText.Compact(yaml) : yaml);
                        File.Move(temporary, target);
                        Interlocked.Add(ref writeTicks, Stopwatch.GetTimestamp() - writeStart);
                        var humanoid = clip.m_ClipBindingConstant?.genericBindings.Count(b => b.typeID == ClassIDType.Animator && b.customType == 8) ?? 0;
                        records[index] = new { source = Identity(clip), file = "Animations/" + file, humanoidBindings = humanoid,
                            resolvedBlendShapeBindings,
                            sampleRate = clip.m_SampleRate, startTime = clip.m_MuscleClip?.m_StartTime, stopTime = clip.m_MuscleClip?.m_StopTime,
                            loopTime = clip.m_MuscleClip?.m_LoopTime,
                            fbxStatus = "Rig/model only. Animation is exported as native Unity .anim." };
                    }
                    catch (Exception ex) when (tolerateAnimationFailures)
                    {
                        try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { }
                        string rawPath = Path.Combine(animationDirectory, SafeName(clip.Name) + "_" + clip.m_PathID + ".bin");
                        string rawError = null, rawSha256 = null;
                        try
                        {
                            byte[] raw;
                            lock (prepareLock) raw = clip.GetRawData();
                            File.WriteAllBytes(rawPath, raw);
                            rawSha256 = Convert.ToHexString(SHA256.HashData(raw));
                        }
                        catch (Exception rawEx) { rawError = rawEx.Message; }
                        object SaveAcl(byte[] data, string suffix)
                        {
                            if (data == null || data.Length == 0) return null;
                            string relative = "Animations/" + SafeName(clip.Name) + "_" + clip.m_PathID + suffix;
                            try
                            {
                                File.WriteAllBytes(Path.Combine(destination, relative.Replace('/', Path.DirectorySeparatorChar)), data);
                                return new { file = relative, bytes = data.Length,
                                    sha256 = Convert.ToHexString(SHA256.HashData(data)), error = (string)null };
                            }
                            catch (Exception saveError)
                            {
                                return new { file = (string)null, bytes = data.Length,
                                    sha256 = Convert.ToHexString(SHA256.HashData(data)), error = saveError.Message };
                            }
                        }
                        var acl = clip.m_MuscleClip?.m_Clip?.m_ACLClip as GIACLClip;
                        failures[index] = new { source = Identity(clip), reason = ex.GetBaseException().Message,
                            exception = ex.ToString(),
                            exceptionType = ex.GetType().FullName,
                            raw = rawError == null ? "Animations/" + Path.GetFileName(rawPath) : null,
                            rawSha256, rawError,
                            aclTracks = SaveAcl(acl?.m_ClipData, ".acl-tracks.bin"),
                            aclDatabase = SaveAcl(acl?.m_DatabaseData, ".acl-database.bin"),
                            streamData = clip.m_StreamData == null ? null : new
                            { clip.m_StreamData.path, clip.m_StreamData.offset, clip.m_StreamData.size },
                            status = "UnsupportedClipDecode" };
                    }
                    finally
                    {
                        Logger.Info($"Exporting animation {Interlocked.Increment(ref completed)}/{clips.Length}: {clip.Name}");
                    }
                });
                sourceClips.AddRange(records.Where(record => record != null));
                animationFailures.AddRange(failures.Where(failure => failure != null));
                File.WriteAllText(Path.Combine(destination, "animation-export-performance.json"), JsonConvert.SerializeObject(new {
                    workers, clips = clips.Length, seconds = elapsed.Elapsed.TotalSeconds,
                    prepareWorkerSeconds = prepareTicks / (double)Stopwatch.Frequency,
                    writeWorkerSeconds = writeTicks / (double)Stopwatch.Frequency }, Formatting.Indented));
            }
            var manifest = new
            {
                schemaVersion = 1, game = manager.Game.Name, gameVersion = GenshinSourceMetadata.GameVersion(root.assetsFile.originalPath), unityVersion = root.assetsFile.unityVersion,
                toolVersion = typeof(GenshinModelExporter).Assembly.GetName().Version.ToString(),
                toolRevision = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(GenshinModelExporter).Assembly)?.InformationalVersion,
                root = Identity(root), model = Path.GetFileName(fbx),
                rigClosureStatus = rigClosure == null ? "NotNeeded" : "SourceProvenDetachedRigUnityImportUnvalidated",
                selectedRootPath = rigClosure?.SelectedRootPath ?? "",
                rigClosure = rigClosure == null ? null : new
                {
                    exportRoot = Identity(rigClosure.ExportRoot),
                    selectedRoot = Identity(root),
                    rigClosure.SelectedRootPath,
                    includedTransformCount = rigClosure.IncludedTransforms.Count,
                    rigClosure.ExcludedSiblingTransforms,
                    referencedBones = rigClosure.ReferencedBones.Select(bone =>
                    {
                        bone.m_GameObject.TryGet(out GameObject gameObject);
                        return new { transform = Identity(bone), gameObject = gameObject == null ? null : Identity(gameObject) };
                    }).ToArray(),
                    note = "Only selected weapon renderers and source-proven referenced bone/ancestor transforms were converted; Unity skin import and animation path rebasing remain unvalidated."
                },
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
                meshes = converted.MeshList.Select(m => new { m.Path, sourceMesh = m.SourceMesh == null ? null : Identity(m.SourceMesh),
                    vertices = m.VertexList.Count, bones = m.BoneList?.Count,
                    m.hasNormal, m.hasTangent, m.hasColor, m.hasUV,
                    vertexSamples = DescribeVertexSamples(m),
                    submeshes = m.SubmeshList.Select((sub, index) => new { index, sub.Material, faces = sub.FaceList.Count,
                        sourceMaterial = sub.SourceMaterial == null ? null : Identity(sub.SourceMaterial) }),
                    bonePaths = m.BoneList?.Select(b => b.Path),
                    bindMatrices = m.BoneList?.Select(b => Enumerable.Range(0, 16).Select(i => b.Matrix[i]).ToArray()) }),
                materialBindings, cubemapBindings, sourceRendererSlots, appearanceExport = exportMaterials ? "Requested" : "SkippedByOption",
                nativeTextureFailures,
                clips = converted.AnimationList.Select(a => new { a.Name, a.SampleRate, tracks = a.TrackList.Count }),
                sourceClips, animationFailures, assembly, bindPoseBones, bindPoseStatus, bindPoseConflicts,
                animationStorage = compactAnimations ? "Wholly constant curves reduced to endpoints and compact YAML; moving curves and humanoid root/IK component keys preserved. No resampling or tolerance-based compression." : "Decoded source curves",
                previewMaterials = converted.MaterialList.Select(m => new { name = m.Name, textures = m.Textures.Select(t => new { name = t.Name, destination = t.Dest,
                    offset = new { x = t.Offset.X, y = t.Offset.Y }, scale = new { x = t.Scale.X, y = t.Scale.Y } }) }),
                objects = dependencies.Objects.Select(Identity)
            };
            File.WriteAllText(Path.Combine(destination, "manifest.json"), JsonConvert.SerializeObject(manifest, Formatting.Indented, settings));
            // Every model export is independently inspectable. Character and weapon
            // exporters replace this descriptor with their richer import recipe.
            GenshinUnityImportDescriptor.Write(destination, "genshin-model", "manifest.json", Path.GetFileName(fbx));
            return fbx;
        }

        private static object Identity(Object obj) => new { obj.Name, Type = obj.type.ToString(),
            SerializedFile = obj.assetsFile.fileName, PathID = obj.m_PathID.ToString(),
            Source = obj.assetsFile.originalPath, Offset = obj.assetsFile.offset };
        private static string SafeName(string value) => string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

        private static object[] DescribeCubemapBindings(string destination, ModelConverter model)
        {
            var results = new List<object>();
            var payloads = new Dictionary<Cubemap, (GenshinCubemapPayload.Result Result, string Error)>();
            string Relative(string path) => Path.GetRelativePath(destination, path).Replace('\\', '/');
            foreach (var mesh in model.MeshList)
            {
                string rendererPath = mesh.Path == model.RootFrame.Path ? "" :
                    mesh.Path.StartsWith(model.RootFrame.Path + "/", StringComparison.Ordinal)
                        ? mesh.Path[(model.RootFrame.Path.Length + 1)..] :
                        throw new InvalidDataException("Mesh outside exported root: " + mesh.Path);
                for (int index = 0; index < mesh.SubmeshList.Count; index++)
                {
                    var submesh = mesh.SubmeshList[index];
                    var material = submesh.SourceMaterial;
                    if (material == null) continue;
                    foreach (var pair in material.m_SavedProperties.m_TexEnvs)
                    {
                        if (!pair.Value.m_Texture.TryGet<Cubemap>(out var cube)) continue;
                        if (!payloads.TryGetValue(cube, out var payload))
                        {
                            try
                            {
                                string key = $"{cube.assetsFile.originalPath}|{cube.assetsFile.offset}|{cube.assetsFile.fileName}|{cube.m_PathID}";
                                string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..24];
                                string folder = Path.Combine(destination, "Cubemaps");
                                Directory.CreateDirectory(folder);
                                var written = GenshinCubemapPayload.Write(cube, Path.Combine(folder, "cube_" + hash), Identity(cube));
                                payload = (written, null);
                            }
                            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or NotSupportedException)
                            {
                                payload = (null, ex.Message);
                            }
                            payloads.Add(cube, payload);
                        }
                        var result = payload.Result;
                        results.Add(new
                        {
                            rendererPath, sourceRendererPath = mesh.Path, submeshIndex = index,
                            materialName = submesh.Material,
                            materialJsonPath = "Materials/" + SafeName(material.Name) + "_" + material.m_PathID + ".json",
                            property = pair.Key, sourceMaterial = Identity(material), sourceCubemap = Identity(cube),
                            metadata = result == null ? null : Relative(result.MetadataPath),
                            rawHeader = result == null ? null : Relative(result.RawPath),
                            imageData = result?.ImageDataPath == null ? null : Relative(result.ImageDataPath),
                            faces = result == null ? Array.Empty<object>() : result.FacePaths.Select((path, face) => (object)new
                            { face = result.FaceIndices[face], file = Relative(path) }).ToArray(),
                            payloadStatus = result?.Status, previewErrors = result?.PreviewErrors,
                            status = result == null ? "PayloadExportFailed" : "UnsupportedUnityBinding",
                            reason = result == null ? payload.Error : "Unity Cubemap binding is not implemented by the study importer. Source Cubemap payload is preserved."
                        });
                    }
                }
            }
            return results.ToArray();
        }

        private static object[] DescribeVertexSamples(ImportedMesh mesh)
        {
            if (mesh.SourceMesh == null || mesh.VertexList.Count == 0) return Array.Empty<object>();
            var source = mesh.SourceMesh;
            int count = mesh.VertexList.Count;
            int samples = Math.Min(16, count);
            return Enumerable.Range(0, samples).Select(sample =>
            {
                int index = samples == 1 ? 0 : (int)Math.Round(sample * (count - 1) / (double)(samples - 1));
                int vertexStride = source.m_Vertices.Length == count * 4 ? 4 : 3;
                int normalStride = source.m_Normals?.Length == count * 4 ? 4 : 3;
                int colorStride = source.m_Colors?.Length == count * 3 ? 3 : 4;
                float[] Slice(float[] values, int stride) => values == null || values.Length < (index + 1) * stride
                    ? null : Enumerable.Range(0, stride).Select(component => values[index * stride + component]).ToArray();
                object SourceUv(int channel)
                {
                    if (!mesh.hasUV[channel]) return null;
                    var values = source.GetUV(channel);
                    int stride = values.Length == count * 2 ? 2 : values.Length == count * 3 ? 3 : 4;
                    return Slice(values, stride);
                }
                var vertex = mesh.VertexList[index];
                return (object)new
                {
                    index,
                    source = new { position = Slice(source.m_Vertices, vertexStride),
                        normal = mesh.hasNormal ? Slice(source.m_Normals, normalStride) : null,
                        tangent = mesh.hasTangent ? Slice(source.m_Tangents, 4) : null,
                        color = mesh.hasColor ? Slice(source.m_Colors, colorStride) : null,
                        uv0 = SourceUv(0), uv1 = SourceUv(1), uv2 = SourceUv(2) },
                    converted = new { vertex.Vertex, vertex.Normal,
                        tangent = mesh.hasTangent ? (object)vertex.Tangent : null,
                        color = mesh.hasColor ? (object)vertex.Color : null,
                        uv0 = mesh.hasUV[0] ? vertex.UV[0] : null,
                        uv1 = mesh.hasUV[1] ? vertex.UV[1] : null,
                        uv2 = mesh.hasUV[2] ? vertex.UV[2] : null }
                };
            }).ToArray();
        }

        private static object DescribeMaterialBinding(string destination, ModelConverter model, ImportedMesh mesh,
            ImportedSubmesh submesh, int index)
        {
            string Relative(string path) => path == model.RootFrame.Path ? "" :
                path.StartsWith(model.RootFrame.Path + "/", StringComparison.Ordinal)
                    ? path[(model.RootFrame.Path.Length + 1)..] : throw new InvalidDataException("Mesh outside exported root: " + path);
            var source = submesh.SourceMaterial;
            var converted = source == null ? Array.Empty<ImportedMaterial>() :
                model.MaterialList.Where(m => m.Name == submesh.Material && ReferenceEquals(m.SourceMaterial, source)).ToArray();
            string materialJsonPath = source == null ? null :
                "Materials/" + SafeName(source.Name) + "_" + source.m_PathID + ".json";
            var textures = new Dictionary<string, string>(StringComparer.Ordinal);
            var nativeTextures = new Dictionary<string, string>(StringComparer.Ordinal);
            var textureImport = new Dictionary<string, object>(StringComparer.Ordinal);
            string reason = source == null ? "Source material pointer unresolved" : converted.Length != 1 ?
                "Source material identity is ambiguous after conversion" :
                !File.Exists(Path.Combine(destination, materialJsonPath)) ? "Material JSON missing" : null;
            if (reason == null)
            {
                int textureIndex = 0;
                foreach (var pair in source.m_SavedProperties.m_TexEnvs)
                {
                    if (pair.Value.m_Texture.IsNull) continue;
                    if (pair.Value.m_Texture.TryGet<Cubemap>(out _))
                    {
                        reason ??= "Cubemap property requires Unity Cubemap binding: " + pair.Key;
                        continue;
                    }
                    if (!pair.Value.m_Texture.TryGet<Texture2D>(out var sourceTexture))
                    {
                        reason ??= "Non-null texture pointer did not resolve as Texture2D or Cubemap: " + pair.Key;
                        continue;
                    }
                    if (textureIndex >= converted[0].Textures.Count)
                    {
                        reason = "Converted texture count does not match source material";
                        break;
                    }
                    string path = converted[0].Textures[textureIndex++].Name.Replace('\\', '/');
                    if (!File.Exists(Path.Combine(destination, path)))
                    {
                        reason = "Converted texture payload missing: " + path;
                        break;
                    }
                    textures.Add(pair.Key, path);
                    string nativePath = Path.ChangeExtension(path, ".astexture").Replace('\\', '/');
                    var exactTexture = model.TextureList.Where(t => t.Name.Replace('\\', '/') == path &&
                        ReferenceEquals(t.SourceTexture, sourceTexture)).ToArray();
                    if (exactTexture.Length != 1 || !File.Exists(Path.Combine(destination, nativePath)))
                    {
                        reason = "Exact native texture payload missing: " + pair.Key;
                        break;
                    }
                    nativeTextures.Add(pair.Key, nativePath);
                    var sourceSettings = sourceTexture.m_TextureSettings;
                    string filter = sourceSettings.m_FilterMode switch { 0 => "Point", 1 => "Bilinear", 2 => "Trilinear", _ => null };
                    string Wrap(int value) => value switch { 0 => "Repeat", 1 => "Clamp", 2 => "Mirror", 3 => "MirrorOnce", _ => null };
                    string wrapU = Wrap(sourceSettings.m_WrapMode), wrapV = Wrap(sourceSettings.m_WrapV), wrapW = Wrap(sourceSettings.m_WrapW);
                    if (sourceTexture.m_ColorSpace is < 0 or > 1 || filter == null || wrapU == null || wrapV == null || wrapW == null ||
                        wrapU != wrapV || wrapU != wrapW)
                    {
                        reason = "Source texture sampling mode unsupported: " + pair.Key;
                        break;
                    }
                    textureImport.Add(pair.Key, new { sRGB = sourceTexture.m_ColorSpace == 1,
                        mipmaps = sourceTexture.m_MipCount > 1, filter, wrap = wrapU,
                        wrapU, wrapV, wrapW, aniso = sourceSettings.m_Aniso,
                        sourceColorSpace = sourceTexture.m_ColorSpace, mipCount = sourceTexture.m_MipCount,
                        mipBias = sourceSettings.m_MipBias,
                        evidence = $"Serialized Texture2D {sourceTexture.assetsFile.fileName}@{sourceTexture.m_PathID}: m_ColorSpace, m_MipCount, m_TextureSettings; native ASTEX001" });
                }
                if (reason == null && textureIndex != converted[0].Textures.Count)
                    reason = "Converted texture count does not match source material";
            }
            return new { rendererPath = Relative(mesh.Path), sourceRendererPath = mesh.Path,
                submeshIndex = index, materialJsonPath, materialName = submesh.Material,
                sourceMaterial = source == null ? null : Identity(source), textures, nativeTextures, textureImport,
                status = reason == null ? "resolved" : "unsupported", reason };
        }
    }
}
