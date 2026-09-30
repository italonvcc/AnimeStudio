using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace AnimeStudio
{
    public static class GenshinVfxExporter
    {
        public static string Export(AssetsManager manager, IEnumerable<AssetEntry> entries, string requestFile, string destination, bool exportMaterials = true,
            CancellationToken cancellation = default, AssetDependencyIndex candidateIndex = null)
        {
            cancellation.ThrowIfCancellationRequested();
            if (!manager.Game.Type.IsGI()) throw new ArgumentException("Select Genshin Impact.");
            if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Choose a new output directory.");
            var request = JObject.Parse(File.ReadAllText(requestFile));
            var actions = request["actions"] as JArray ?? throw new InvalidDataException("Missing actions array.");
            if (actions.Count is < 1 or > 32) throw new InvalidDataException("Supply 1–32 actions.");
            var map = entries.ToArray();
            // JArray.CopyTo rejects an empty suffix when the destination index is
            // exactly its length. Enumerating child tokens also handles requests
            // with no optional evidence assets, as in a one-effect CLI export.
            var selectors = actions.SelectMany(a => a["effects"]?.Children() ?? Enumerable.Empty<JToken>())
                .Concat(request["evidenceAssets"]?.Children() ?? Enumerable.Empty<JToken>()).ToArray();
            var byName = map.ToLookup(e => (e.Name, e.Type));
            var chosen = new Dictionary<JToken, AssetEntry>();
            var missingSelections = new List<object>();
            foreach (var selector in selectors)
            {
                cancellation.ThrowIfCancellationRequested();
                string name = (string)selector["name"];
                ClassIDType type = selector["type"] == null ? ClassIDType.GameObject : Enum.Parse<ClassIDType>((string)selector["type"]);
                long? id = selector["pathID"] == null ? null : long.Parse((string)selector["pathID"]);
                string source = (string)selector["source"];
                long? offset = (long?)selector["offset"];
                var matches = byName[(name, type)].Where(e => (id == null || e.PathID == id) &&
                    (offset == null || e.Offset == offset) &&
                    (source == null || string.Equals(source, e.Source, StringComparison.OrdinalIgnoreCase))).ToArray();
                if (matches.Length != 1) { missingSelections.Add(new { selection = selector, count = matches.Length, reason = "Missing/ambiguous exact asset-map match" }); continue; }
                if (matches[0].Offset < 0) throw new InvalidDataException("Regenerate the map with bundle offsets.");
                chosen.Add(selector, matches[0]);
            }
            var oldFilter = manager.FilterData;
            try
            {
                manager.FilterData = new AssetsManager.AssetFilterData { Items = chosen.Values.Select(e => new AssetsManager.AssetFilterDataItem
                { Source = e.Source, Offset = e.Offset, PathID = e.PathID, Name = e.Name, Type = e.Type }).ToList() };
                manager.LoadFiles(chosen.Values.Select(e => e.Source).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), mergeSplitAssets: false);
                Object Get(AssetEntry e) => manager.FindAsset(e) ?? throw new InvalidDataException("VFX selection did not load: " + e.Name);
                var roots = chosen.ToDictionary(p => p.Key, p => Get(p.Value));
                if (roots.Count == 0) throw new InvalidDataException("No selected effect/evidence assets could be loaded.");
                Logger.Info($"Resolving {roots.Count} selected VFX roots/evidence assets");
                // Data-only effects retain geometry and raw components while the
                // unrequested renderer->material->texture graph stays unloaded.
                var resolver = new AssetDependencyResolver(manager, candidateIndex ?? new AssetDependencyIndex(map)) { IncludeMaterials = exportMaterials };
                var graph = resolver.Resolve(roots.Values, cancellation);
                Directory.CreateDirectory(destination);
                var settings = new JsonSerializerSettings { Converters = { new StringEnumConverter() } };
                var files = new string[graph.Objects.Count]; var errors = new object[graph.Objects.Count];
                var sourceGate = new object();
                int completed = 0, workers = GenshinExportWorkers.Count;
                var elapsed = Stopwatch.StartNew();
                void ReportCompleted()
                {
                    int done = Interlocked.Increment(ref completed);
                    if (done % 25 == 0 || done == graph.Objects.Count) Logger.Info($"Exporting VFX objects {done}/{graph.Objects.Count}");
                }
                Logger.Info($"Exporting VFX objects with {workers} workers");
                Parallel.For(0, graph.Objects.Count, new ParallelOptions { MaxDegreeOfParallelism = workers, CancellationToken = cancellation }, index =>
                {
                    var obj = graph.Objects[index];
                    if (!exportMaterials && (obj is Material or Texture2D or Cubemap or Shader))
                    { ReportCompleted(); return; }
                    object identity;
                    lock (sourceGate) identity = Identity(obj);
                    string folder = Path.Combine(destination, obj.type.ToString()); Directory.CreateDirectory(folder);
                    string file = Path.Combine(folder, Safe(obj.assetsFile.fileName) + "_" + obj.m_PathID);
                    try
                    {
                        if (obj is Cubemap cube && cube.ImageLayout != null)
                        {
                            file = GenshinCubemapPayload.Write(cube, file, identity, sourceGate).MetadataPath;
                        }
                        else if (obj is Texture2D texture)
                        {
                            byte[] source;
                            lock (sourceGate) source = texture.image_data.GetData();
                            GenshinVfxTexturePayload.Write(texture, source, file + ".astexture");
                            using var snapshot = new BinaryReader(new MemoryStream(source, writable: false));
                            using var data = texture.ConvertToStream(ImageFormat.Png, true, new ResourceReader(snapshot, 0, source.Length)) ?? throw new InvalidDataException("Texture decoder produced no image.");
                            if (data.Length == 0) throw new InvalidDataException("Texture encoder returned an empty image.");
                            data.Position = 0;
                            using var output = new FileStream(file + ".png", FileMode.CreateNew); data.CopyTo(output); file += ".png";
                        }
                        else if (obj is Material material)
                        {
                            string json;
                            byte[] raw;
                            lock (sourceGate)
                            {
                                json = MaterialJsonExporter.Serialize(material, settings);
                                raw = material.GetRawData();
                            }
                            File.WriteAllBytes(file + ".bin", raw);
                            var materialJson = JObject.Parse(json);
                            materialJson["SourceRaw"] = JObject.FromObject(new
                            {
                                assetPath = Path.GetFileName(file) + ".bin",
                                bytes = raw.Length,
                                sha256 = Convert.ToHexString(SHA256.HashData(raw))
                            });
                            file += ".json"; File.WriteAllText(file, materialJson.ToString(Formatting.Indented));
                        }
                        else if (obj is AnimationClip clip)
                        {
                            clip.PrepareForExport(sourceGate: sourceGate);
                            file += ".anim"; File.WriteAllText(file, AnimationClipExtensions.ConvertSerializedAnimationClip(clip));
                        }
                        else
                        {
                            byte[] bytes; object details, parsed = null;
                            bool snapshotType;
                            lock (sourceGate)
                            {
                                bytes = obj.GetRawData();
                                snapshotType = obj.reader.byteStart % 4 == 0;
                                if (!snapshotType) parsed = obj.ToType();
                                details = obj switch
                                {
                                    Transform t => new { position = t.m_LocalPosition, rotation = t.m_LocalRotation, scale = t.m_LocalScale,
                                        parent = Ref(t.m_Father), children = t.m_Children.Select(p => Ref(p)).ToArray() },
                                    GameObject go => new { components = go.m_Components.Select(p => Ref(p.Cast<Object>())).ToArray() },
                                    Renderer r => new { materials = r.m_Materials.Select(p => Ref(p)).ToArray(), particleSpecificFields = r is GenshinParticleSystemRenderer or GenshinTrailRenderer ? "Unsupported; raw bytes preserved" : null },
                                    Mesh m => new { coordinateSystem = "Source Unity local coordinates; index winding unchanged", vertexCount = m.m_VertexCount,
                                        vertices = m.m_Vertices, indices = m.m_Indices, normals = m.m_Normals, tangents = m.m_Tangents,
                                        colors = m.m_Colors, uv0 = m.m_UV0, uv1 = m.m_UV1, uv2 = m.m_UV2, uv3 = m.m_UV3,
                                        uv4 = m.m_UV4, uv5 = m.m_UV5, uv6 = m.m_UV6, uv7 = m.m_UV7,
                                        subMeshes = m.m_SubMeshes, bindPose = m.m_BindPose },
                                    MonoBehaviour mono => new { script = mono.m_Script.Name, strings = Regex.Matches(Encoding.ASCII.GetString(bytes), @"[\x20-\x7e]{6,}")
                                        .Select(m => new { offset = m.Index, text = m.Value }).ToArray() },
                                    Cubemap unsupportedCube => new { unsupportedCube.LayoutError, note = "Raw header retained; face/resource decoding unsupported for this layout." },
                                    _ => null
                                };
                            }
                            File.WriteAllBytes(file + ".bin", bytes);
                            if (snapshotType) parsed = ReadTypeSnapshot(obj, bytes);
                            file += ".json";
                            File.WriteAllText(file, JsonConvert.SerializeObject(new { identity, raw = Path.GetFileNameWithoutExtension(file) + ".bin",
                                sha256 = Convert.ToHexString(SHA256.HashData(bytes)), parsed, details }, Formatting.Indented, settings));
                        }
                        files[index] = Path.GetRelativePath(destination, file);
                    }
                    catch (Exception ex) { errors[index] = new { identity, error = ex.Message }; }
                    ReportCompleted();
                });
                var outputs = graph.Objects.Select((obj,index) => (obj,file: files[index])).Where(p => p.file != null).ToDictionary(p => p.obj,p => p.file);
                var failures = errors.Where(e => e != null).ToArray();
                File.WriteAllText(Path.Combine(destination, "export-performance.json"), JsonConvert.SerializeObject(new {
                    workers, objects = graph.Objects.Count, seconds = elapsed.Elapsed.TotalSeconds }, Formatting.Indented));
                var actionReports = new List<object>();
                foreach (var action in actions)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var effectReports = new List<object>();
                    foreach (var effect in action["effects"] ?? new JArray())
                    {
                        if (!roots.TryGetValue(effect, out var root)) { effectReports.Add(new { selection = effect, status = "Missing/ambiguous" }); continue; }
                        var subset = resolver.InspectLoaded(new[] { root });
                        string effectFile = root is GameObject effectRoot ? GenshinStructuredVfxExporter.Write(effectRoot, subset, outputs, destination) : null;
                        effectReports.Add(new { selection = effect, identity = Identity(root), effectFile, dependencies = subset.Objects.Select(o => new { identity = Identity(o), file = outputs.GetValueOrDefault(o) }), unresolved = subset.Missing,
                            unsupported = subset.Objects.Where(Unsupported).Select(Identity),
                            timing = "Runtime event timing/attachment schema is not decoded; source event data and local prefab transforms are retained." });
                    }
                    actionReports.Add(new { action = (string)action["name"], evidence = action["evidence"], effects = effectReports });
                }
                File.Copy(requestFile, Path.Combine(destination, "request.json"));
                string manifest = Path.Combine(destination, "manifest.json");
                File.WriteAllText(manifest, JsonConvert.SerializeObject(new { schemaVersion = 1, character = request["character"],
                    subject = request["subject"], exportMaterials,
                    declaredGameVersion = request["gameVersion"], gameVersion = GenshinSourceMetadata.GameVersion(roots.Values.First().assetsFile.originalPath),
                    toolRevision = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(GenshinVfxExporter).Assembly)?.InformationalVersion,
                    actions = actionReports, missingSelections, graph.LoadedBundles, unresolved = graph.Missing, failures,
                    evidenceAssets = roots.Where(p => p.Value is not GameObject).Select(p => new { identity = Identity(p.Value), file = outputs.GetValueOrDefault(p.Value) }),
                    scope = "Per-action asset/dependency export. Particle modules, trails, custom scripts, runtime attachments/conditions and simulation are not reconstructed; unsupported raw components are preserved. Discovery-only associations remain labeled by the input evidence." }, Formatting.Indented, settings));
                return manifest;
            }
            finally { manager.FilterData = oldFilter; }
        }
        private static object ReadTypeSnapshot(Object obj, byte[] bytes)
        {
            if (obj.serializedType?.m_Type == null) return null;
            // Type trees only align to four-byte boundaries. Keep unusual source
            // offsets on the serial reader path; aligned objects use private cursors.
            using var source = new EndianBinaryReader(new MemoryStream(bytes, writable: false), obj.reader.Endian);
            using var reader = new ObjectReader(source, obj.assetsFile, new ObjectInfo {
                byteStart = 0, byteSize = obj.byteSize, m_PathID = obj.m_PathID,
                classID = (int)obj.type, serializedType = obj.serializedType }, obj.reader.Game);
            return TypeTreeHelper.ReadType(obj.serializedType.m_Type, reader);
        }
        private static bool Unsupported(Object o) => o is GenshinParticleSystem or GenshinParticleSystemRenderer or GenshinTrailRenderer or MonoBehaviour or Animator || o is Cubemap c && c.ImageLayout == null || o.GetType() == typeof(Object);
        private static object Identity(Object o) => new { o.Name, type = o.type.ToString(), file = o.assetsFile.fileName,
            pathID = o.m_PathID.ToString(), source = o.assetsFile.originalPath, offset = o.assetsFile.offset };
        private static object Ref<T>(PPtr<T> p) where T : Object => new { file = ((IObjectReference)p).SerializedFileName, pathID = p.m_PathID.ToString(), p.IsNull, resolved = ((IObjectReference)p).TryGetObject(out _) };
        private static string Safe(string value) => string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
    }
}
