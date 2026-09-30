using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace AnimeStudio
{
    // Source-keyed description for the package importer. It deliberately keeps
    // undecoded game components visible, rather than manufacturing Unity defaults.
    internal static class GenshinStructuredVfxExporter
    {
        public static string Write(GameObject root, AssetDependencyResolver.Result graph,
            IReadOnlyDictionary<Object, string> outputs, string destination)
        {
            string folder = Path.Combine(destination, "Effects");
            Directory.CreateDirectory(folder);
            string file = "Effects/" + Safe(root.assetsFile.fileName) + "_" + root.m_PathID + ".effect.json";
            string AssetPath(Object obj)
            {
                if (!outputs.TryGetValue(obj, out var relative)) return null;
                if (obj is Texture2D)
                {
                    string native = Path.ChangeExtension(relative, ".astexture");
                    if (File.Exists(Path.Combine(destination, native))) relative = native;
                }
                return "../" + relative.Replace('\\', '/');
            }
            var gameObjects = graph.Objects.OfType<GameObject>().ToArray();
            var nodes = gameObjects.Select(go =>
            {
                Component[] components = go.m_Components.Select(p => p.TryGet(out var component) ? component : null)
                    .Where(c => c != null).ToArray();
                var transform = components.OfType<Transform>().SingleOrDefault();
                GameObject parent = null;
                if (transform != null && transform.m_Father.TryGet(out var father))
                    father.m_GameObject.TryGet(out parent);
                return new
                {
                    id = Id(go), go.Name, active = go.m_IsActive,
                    parent = parent != null && gameObjects.Contains(parent) ? Id(parent) : null,
                    local = transform == null ? null : new
                    {
                        position = new { x = transform.m_LocalPosition.X, y = transform.m_LocalPosition.Y, z = transform.m_LocalPosition.Z },
                        rotation = new { x = transform.m_LocalRotation.X, y = transform.m_LocalRotation.Y,
                            z = transform.m_LocalRotation.Z, w = transform.m_LocalRotation.W },
                        scale = new { x = transform.m_LocalScale.X, y = transform.m_LocalScale.Y, z = transform.m_LocalScale.Z }
                    },
                    components = components.Select(component => Describe(component, AssetPath)).ToArray(),
                    coverage = new { status = transform != null && go.m_IsActive.HasValue ? "Complete" : "Partial",
                        unsupportedFields = transform == null ? new[] { "Missing resolved Transform" } :
                            go.m_IsActive.HasValue ? Array.Empty<string>() : new[] { "GameObject active flag layout" } }
                };
            }).ToArray();
            var settings = new JsonSerializerSettings { Converters = { new StringEnumConverter() } };
            File.WriteAllText(Path.Combine(destination, file.Replace('/', Path.DirectorySeparatorChar)),
                JsonConvert.SerializeObject(new
                {
                    schemaVersion = 1,
                    source = new
                    {
                        gameVersion = GenshinSourceMetadata.GameVersion(root.assetsFile.originalPath),
                        unityVersion = root.assetsFile.unityVersion,
                        serializedFormat = root.assetsFile.header.m_Version.ToString(),
                        root = Id(root),
                        rootSha256 = Convert.ToHexString(SHA256.HashData(root.GetRawData())),
                        sourceBundle = root.assetsFile.originalPath
                    },
                    root = Id(root),
                    objects = nodes,
                    dependencies = graph.Objects.Where(o => AssetPath(o) != null).Select(o => new
                    {
                        id = Id(o), kind = o.type.ToString(), assetPath = AssetPath(o)
                    }).ToArray(),
                    unresolved = graph.Missing,
                    coverage = new { status = "Partial",
                        note = "Only fields tied to a verified source layout are decoded. Unknown particle modules, game scripts and runtime spawning remain unsupported." }
                }, Formatting.Indented, settings));
            return file;
        }

        private static object Describe(Component component, Func<Object, string> assetPath)
        {
            byte[] bytes = component.GetRawData();
            string typeHash = Convert.ToHexString(component.serializedType?.m_OldTypeHash ?? Array.Empty<byte>());
            object modules = null;
            JObject rendererFields = null;
            JObject actionData = null;
            string[] decoded = Array.Empty<string>();
            string[] unsupported = Array.Empty<string>();
            object[] unresolvedRanges = Array.Empty<object>();
            string status = "Complete";
            bool verifiedNativeFieldsReady = false;
            bool? enabled = component switch
            {
                Behaviour b => b.m_Enabled != 0,
                Renderer sourceRenderer => sourceRenderer.m_Enabled,
                Transform => null,
                GenshinParticleSystem => null, // Component has no serialized enable flag.
                _ => null
            };
            if (component is GenshinParticleSystem)
            {
                status = "Unsupported";
                unsupported = new[] { "Particle module bytes and Genshin additions are not decoded" };
                if (component.assetsFile.unityVersion.StartsWith("2017.4.30f1", StringComparison.Ordinal) &&
                    typeHash == GenshinParticleSystemDecoder.TypeHash)
                {
                    try
                    {
                        modules = GenshinParticleSystemDecoder.Decode(bytes);
                        var typed = (JObject)modules;
                        int SourceOffset(string name) => (int)typed[name]["offset"];
                        int SourceEnd(string name) => SourceOffset(name) + (int)typed[name]["byteCount"];
                        int collisionWord = (int)typed["sourceByteRanges"]["CollisionModule"][0] + 240;
                        decoded = new[] { "2017.4.30f1 typed modules with per-module source byte boundary checks",
                            "Native-transfer-proven header, Shape, Emission, Text and ColorOverDay fields; corrected Shape alignment bool" };
                        status = "Partial";
                        verifiedNativeFieldsReady = true;
                        unsupported = new[] { "Collision/Trail additions remain in coverage.unresolvedRanges",
                            "Decoded game features require an explicit runtime adapter: ColorOverDay, emission level/falloff, culling update and game-specific Shape controls. Field decoding alone does not establish native playback parity." };
                        unresolvedRanges = new object[]
                        {
                            new { start = collisionWord, end = collisionWord + 4, context = "Collision extension", moduleEnabled = (bool?)typed["CollisionModule"]?["enabled"] },
                            new { start = SourceOffset("Genshin_Trail_extension"), end = SourceEnd("Genshin_Trail_extension"), context = "Trail extension", moduleEnabled = (bool?)typed["TrailModule"]?["enabled"] }
                        };
                    }
                    catch (InvalidDataException ex)
                    {
                        unsupported = new[] { "Strict particle layout validation failed: " + ex.Message };
                    }
                }
            }
            else if (component is GenshinParticleSystemRenderer or GenshinTrailRenderer)
            {
                status = "Partial";
                decoded = new[] { "Renderer base enabled flag and material references" };
                unsupported = new[] { "Renderer-specific geometry, vertex streams and game fields" };
                if (component is GenshinParticleSystemRenderer &&
                    typeHash == GenshinParticleRendererDecoder.TypeHash)
                {
                    try
                    {
                        rendererFields = GenshinParticleRendererDecoder.Decode(bytes);
                        decoded = new[] { "Renderer base, particle polygon, alignment, size controls, GPU flags, octagon state, parent transform controls, stream IDs, meshes, mask and flip" };
                        unsupported = new[] { "Inherited Renderer fields not individually surfaced in the structured particle renderer view; raw payload retained" };
                        unresolvedRanges = Array.Empty<object>();
                    }
                    catch (InvalidDataException ex)
                    {
                        unsupported = new[] { "Strict renderer layout validation failed: " + ex.Message };
                    }
                }
            }
            else if (component is MonoBehaviour monoBehaviour)
            {
                status = "Partial";
                decoded = new[] { "Unity MonoBehaviour header, enabled flag and script reference" };
                unsupported = new[] { "Game MonoBehaviour custom fields and runtime logic" };
                try
                {
                    actionData = GenshinEffectActionDecoder.Decode(monoBehaviour, bytes, typeHash);
                    int start = (int)actionData["unityHeaderBytes"];
                    unresolvedRanges = new object[] { new { start, end = bytes.Length,
                        context = "Game MonoBehaviour custom payload; observed strings do not establish field roles or timing",
                        moduleEnabled = enabled } };
                }
                catch (InvalidDataException ex)
                {
                    unsupported = new[] { "MonoBehaviour header validation failed: " + ex.Message,
                        "Game MonoBehaviour custom fields and runtime logic" };
                    unresolvedRanges = new object[] { new { start = 0, end = bytes.Length,
                        context = "Unverified MonoBehaviour payload", moduleEnabled = enabled } };
                }
            }
            else if (enabled == null && component is not Transform)
            {
                status = "Partial";
                unsupported = new[] { "Component enable state and fields" };
            }
            string relative = component.type + "/" + Safe(component.assetsFile.fileName) + "_" + component.m_PathID + ".bin";
            return new
            {
                id = Id(component), type = component.type.ToString(), enabled, modules,
                renderer = rendererFields,
                actionData,
                sourceUvOutline = rendererFields?["sourceUvOutline"],
                alignment = rendererFields?["alignment"],
                normalDirection = rendererFields?["normalDirection"],
                pivot = rendererFields?["pivot"],
                materials = component is Renderer r ? r.m_Materials.Select(p =>
                    p.TryGet(out var material) ? new { id = Id(material), assetPath = assetPath(material) } : null).ToArray() : null,
                meshes = component is GenshinParticleSystemRenderer particleRenderer && particleRenderer.m_Meshes != null
                    ? particleRenderer.m_Meshes.Select(p => p.TryGet(out var mesh)
                        ? new { id = Id(mesh), assetPath = assetPath(mesh) } : null).ToArray() : null,
                script = component is MonoBehaviour mono ? mono.m_Script.Name : null,
                scriptReference = component is MonoBehaviour scriptComponent ? ScriptReference(scriptComponent) : null,
                raw = new { assetPath = "../" + relative, bytes = bytes.Length,
                    sha256 = Convert.ToHexString(SHA256.HashData(bytes)), typeHash },
                coverage = new { status, enabledNotApplicable = component is Transform or GenshinParticleSystem,
                    verifiedNativeFieldsReady,
                    verifiedRendererFieldsReady = rendererFields != null,
                    nativePlaybackReady = false,
                    decodedFields = decoded, unsupportedFields = unsupported,
                    unresolvedRanges }
            };
        }

        private static object Id(Object obj) => new { file = obj.assetsFile.fileName,
            pathID = obj.m_PathID.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        private static object ScriptReference(MonoBehaviour component)
        {
            bool resolved = component.m_Script.TryGet(out var script);
            return new
            {
                fileID = component.m_Script.m_FileID,
                pathID = component.m_Script.m_PathID.ToString(System.Globalization.CultureInfo.InvariantCulture),
                file = ((IObjectReference)component.m_Script).SerializedFileName,
                resolved,
                className = resolved ? script.m_ClassName : null,
                @namespace = resolved ? script.m_Namespace : null,
                assemblyName = resolved ? script.m_AssemblyName : null
            };
        }
        private static string Safe(string value) => string.Concat(value.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
    }
}
