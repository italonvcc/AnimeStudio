using System;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AnimeStudio.CLI
{
    // Bounded source inspection for records whose game selection rules are not yet known.
    internal static class GenshinSourceProbeCommand
    {
        public static int RunAvatarPackage(string[] args)
        {
            if (args.Length != 4)
            {
                Console.Error.WriteLine("Usage: --genshin-avatar-package <map> <exact-request.json> <new-output-directory>");
                return 2;
            }
            var manager = new AssetsManager { Game = GameManager.GetGameByType(GameType.GI) };
            try
            {
                if (ResourceMap.FromFile(args[1]) < 0 || !ResourceMap.GetGameType().IsGI())
                    throw new InvalidDataException("Expected a Genshin asset map.");
                var entries = ResourceMap.GetEntries().ToArray();
                var request = JObject.Parse(File.ReadAllText(args[2]));
                var sourceAnimator = request["sourceAnimator"] ?? throw new InvalidDataException("Missing sourceAnimator.");
                var clips = (JArray)(request["clips"] ?? throw new InvalidDataException("Missing exact clips."));
                if (clips.Count != 2) throw new InvalidDataException("Select exactly two diagnostic clips.");
                AssetEntry Exact(JToken selector, ClassIDType type)
                {
                    var name = (string)selector["name"];
                    var source = (string)selector["source"];
                    long pathId = long.Parse((string)selector["pathId"]);
                    return entries.Single(e => e.Type == type && e.Name == name && e.PathID == pathId &&
                        string.Equals(e.Source, source, StringComparison.OrdinalIgnoreCase));
                }
                var animatorEntry = Exact(sourceAnimator, ClassIDType.Animator);
                var clipEntries = clips.Select(c => Exact(c, ClassIDType.AnimationClip)).ToArray();
                if (animatorEntry.Offset < 0 || clipEntries.Any(c => c.Offset < 0))
                    throw new InvalidDataException("Selected source objects need bundle offsets.");
                var selected = clipEntries.Prepend(animatorEntry).ToArray();
                manager.FilterData.Items = selected.Select(e => new AssetsManager.AssetFilterDataItem
                { Source = e.Source, Offset = e.Offset, PathID = e.PathID, Name = e.Name, Type = e.Type }).ToList();
                manager.LoadFiles(selected.Select(e => e.Source).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    mergeSplitAssets: false);
                var objects = manager.assetsFileList.SelectMany(f => f.ObjectsDic.Values).ToArray();
                var animator = objects.OfType<Animator>().Single(a => a.m_PathID == animatorEntry.PathID &&
                    string.Equals(a.assetsFile.originalPath, animatorEntry.Source, StringComparison.OrdinalIgnoreCase));
                var sourceClips = clipEntries.Select(e => objects.OfType<AnimationClip>().Single(c => c.m_PathID == e.PathID &&
                    string.Equals(c.assetsFile.originalPath, e.Source, StringComparison.OrdinalIgnoreCase))).ToArray();
                if (animator.m_Avatar.m_FileID <= 0 || animator.m_Avatar.m_FileID > animator.assetsFile.m_Externals.Count)
                    throw new InvalidDataException("Source Animator has no external Avatar pointer.");
                var cabMapPath = Path.GetFullPath((string)request["cabMapPath"]);
                if (!AssetsHelper.LoadCABMap(cabMapPath)) throw new InvalidDataException("Could not read source CAB map.");
                string cab = animator.assetsFile.m_Externals[animator.m_Avatar.m_FileID - 1].fileName;
                if (!AssetsHelper.TryResolveCAB(cab, out var avatarSource, out var avatarOffset))
                    throw new InvalidDataException("Source Avatar CAB missing: " + cab);
                manager.FilterData.Items.Add(new AssetsManager.AssetFilterDataItem
                { Source = avatarSource, Offset = avatarOffset, PathID = animator.m_Avatar.m_PathID,
                    Name = "Avatar", Type = ClassIDType.Avatar });
                manager.LoadFiles(new[] { avatarSource }, mergeSplitAssets: false);
                if (!animator.m_Avatar.TryGet(out var avatar))
                    throw new InvalidDataException("Source Avatar did not resolve after exact CAB load.");
                var modelManifestPath = Path.GetFullPath((string)request["modelManifestPath"]);
                var modelPath = Path.GetFullPath((string)request["modelPath"]);
                var animationRoot = Path.GetFullPath((string)request["animationExportRoot"]);
                var modelManifest = JObject.Parse(File.ReadAllText(modelManifestPath));
                string character = (string)modelManifest["root"]?["Name"];
                if (string.IsNullOrWhiteSpace(character) ||
                    Path.GetFileName(modelPath) != character + ".fbx")
                    throw new InvalidDataException("Base model filename must match its exact source root name.");
                var animationManifest = JObject.Parse(File.ReadAllText(Path.Combine(animationRoot, "manifest.json")));
                foreach (var clip in sourceClips)
                {
                    var sourceRecord = ((JArray)animationManifest["sourceClips"]).Single(r =>
                        (string)r["source"]?["PathID"] == clip.m_PathID.ToString() &&
                        (string)r["source"]?["Source"] == clip.assetsFile.originalPath);
                    if ((string)sourceRecord["source"]?["Name"] != clip.Name ||
                        !File.Exists(Path.Combine(animationRoot, (string)sourceRecord["file"])))
                        throw new InvalidDataException("Selected native .anim does not match its source clip.");
                }
                string output = Path.GetFullPath(args[3]);
                if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Choose a new package directory.");
                Directory.CreateDirectory(output);
                File.Copy(args[2], Path.Combine(output, "source-request.json"));
                File.Copy(modelPath, Path.Combine(output, Path.GetFileName(modelPath)));
                Directory.CreateDirectory(Path.Combine(output, "Animations"));
                var clipPayloads = sourceClips.Select(clip =>
                {
                    string file = "Animations/" + clip.Name + "_" + clip.m_PathID + ".anim";
                    File.Copy(Path.Combine(animationRoot, file.Replace('/', Path.DirectorySeparatorChar)),
                        Path.Combine(output, file.Replace('/', Path.DirectorySeparatorChar)));
                    return new { clip.Name, PathID = clip.m_PathID.ToString(), Source = clip.assetsFile.originalPath,
                        file, body = GenshinUnityPackage.HasBody(clip),
                        sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(output, file)))) };
                }).ToArray();
                if (clipPayloads.Any(c => !c.body)) throw new InvalidDataException("Selected diagnostic clip lacks source body muscle bindings.");
                GenshinUnityPackage.Write(avatar, character, modelPath, sourceClips, output);
                string Hash(string path)
                {
                    using var stream = File.OpenRead(path);
                    return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
                }
                File.WriteAllText(Path.Combine(output, "avatar-source-provenance.json"),
                    JsonConvert.SerializeObject(new { schemaVersion = 1,
                        status = "diagnostic source Avatar package; target rig compatibility and playback require Unity checks",
                        sourceAnimator = new { animatorEntry.Name, animatorEntry.Source,
                            PathID = animatorEntry.PathID.ToString(), animatorEntry.Offset },
                        avatarPointer = new { cab, PathID = animator.m_Avatar.m_PathID.ToString() },
                        avatar = new { avatar.Name, Source = avatar.assetsFile.originalPath,
                            PathID = avatar.m_PathID.ToString(), rawSha256 = HashAvatar(avatar) },
                        cabMapSha256 = Hash(cabMapPath), modelManifestSha256 = Hash(modelManifestPath),
                        modelSha256 = Hash(modelPath), clips = clipPayloads }, Formatting.Indented));
                Console.WriteLine(output);
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            finally { manager.Clear(); AssetsHelper.Clear(); ResourceMap.Clear(); }
        }

        private static string HashAvatar(Avatar avatar) =>
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(avatar.GetRawData()));

        public static int RunAvatarRecipe(string[] args)
        {
            if (args.Length is < 4 or > 5)
            {
                Console.Error.WriteLine("Usage: --genshin-avatar-recipe <maps joined by |> <AnimatorName@PathID[@Offset]> <new report.json> [CAB-map.bin]");
                return 2;
            }
            var manager = new AssetsManager { Game = GameManager.GetGameByType(GameType.GI) };
            try
            {
                var entries = args[1].Split('|').SelectMany(mapPath =>
                {
                    if (ResourceMap.FromFile(mapPath) < 0 || !ResourceMap.GetGameType().IsGI())
                        throw new InvalidDataException("Expected a Genshin asset map: " + mapPath);
                    return ResourceMap.GetEntries().ToArray();
                }).DistinctBy(e => (e.Source, e.Offset, e.PathID, e.Type)).ToArray();
                var selector = args[2].Split('@');
                if (selector.Length is < 2 or > 3 || string.IsNullOrWhiteSpace(selector[0]) ||
                    !long.TryParse(selector[1], out long pathId) ||
                    selector.Length == 3 && !long.TryParse(selector[2], out _))
                    throw new ArgumentException("Select AnimatorName@PathID[@Offset].");
                string name = selector[0];
                long? offset = selector.Length == 3 ? long.Parse(selector[2]) : null;
                var candidateEntries = entries.Where(e => e.Type == ClassIDType.Animator && e.Name == name &&
                    e.PathID == pathId && (offset == null || e.Offset == offset)).ToArray();
                if (candidateEntries.Length != 1)
                    throw new InvalidDataException($"Expected one source Animator, found {candidateEntries.Length}; offsets " +
                        string.Join(", ", candidateEntries.Select(e => e.Offset)));
                var entry = candidateEntries[0];
                if (entry.Offset < 0) throw new InvalidDataException("Selected Animator requires a bundle offset.");
                manager.FilterData.Items = new[] { new AssetsManager.AssetFilterDataItem
                { Source = entry.Source, Offset = entry.Offset, PathID = entry.PathID,
                    Name = entry.Name, Type = entry.Type } }.ToList();
                manager.LoadFiles(new[] { entry.Source }, mergeSplitAssets: false);
                var matches = manager.assetsFileList.SelectMany(f => f.ObjectsDic.Values).OfType<Animator>()
                    .Where(a => a.m_PathID == entry.PathID &&
                        string.Equals(a.assetsFile.originalPath, entry.Source, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                if (matches.Length != 1) throw new InvalidDataException($"Expected one loaded Animator, found {matches.Length}: " +
                    string.Join(", ", matches.Select(a => a.assetsFile.fileName)));
                var animator = matches[0];
                if (!animator.m_Avatar.TryGet(out _) && args.Length == 5 && animator.m_Avatar.m_FileID > 0)
                {
                    if (!AssetsHelper.LoadCABMap(args[4]))
                        throw new InvalidDataException("Could not read the source CAB map.");
                    string cab = animator.assetsFile.m_Externals[animator.m_Avatar.m_FileID - 1].fileName;
                    if (!AssetsHelper.TryResolveCAB(cab, out var avatarSource, out var avatarOffset))
                        throw new InvalidDataException("Referenced Avatar CAB is absent from the source CAB map: " + cab);
                    manager.FilterData.Items.Add(new AssetsManager.AssetFilterDataItem
                    { Source = avatarSource, Offset = avatarOffset, PathID = animator.m_Avatar.m_PathID,
                        Name = "Avatar", Type = ClassIDType.Avatar });
                    manager.LoadFiles(new[] { avatarSource }, mergeSplitAssets: false);
                }
                if (!animator.m_Avatar.TryGet(out var avatar))
                    throw new InvalidDataException($"Source Animator has no resolvable Avatar: fileID {animator.m_Avatar.m_FileID}, pathID {animator.m_Avatar.m_PathID}; map Avatar count {entries.Count(e => e.Type == ClassIDType.Avatar)}; map candidates " +
                        string.Join(", ", entries.Where(e => e.Type == ClassIDType.Avatar && e.PathID == animator.m_Avatar.m_PathID)
                            .Select(e => e.Source + " (" + e.Container + ")")) + "; external " +
                        (animator.m_Avatar.m_FileID > 0 && animator.m_Avatar.m_FileID <= animator.assetsFile.m_Externals.Count
                            ? animator.assetsFile.m_Externals[animator.m_Avatar.m_FileID - 1].fileName : "none"));
                var output = Path.GetFullPath(args[3]);
                if (File.Exists(output)) throw new IOException("Choose a new report path.");
                string cabMapSha256 = null;
                if (args.Length == 5)
                {
                    using var cabMapStream = File.OpenRead(args[4]);
                    cabMapSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(cabMapStream));
                }
                var report = new { schemaVersion = 1, status = "source Avatar calibration recipe; model compatibility unverified",
                    cabMapSha256,
                    animator = new { entry.Name, entry.Source, PathID = entry.PathID.ToString(),
                        entry.Container, entry.Offset, serializedFile = animator.assetsFile.fileName },
                    avatarPointer = new { animator.m_Avatar.m_FileID,
                        PathID = animator.m_Avatar.m_PathID.ToString(),
                        externalCab = animator.m_Avatar.m_FileID > 0
                            ? animator.assetsFile.m_Externals[animator.m_Avatar.m_FileID - 1].fileName : null },
                    avatar = new { avatar.Name, Source = avatar.assetsFile.originalPath,
                        PathID = avatar.m_PathID.ToString(), serializedFile = avatar.assetsFile.fileName,
                        rawSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(avatar.GetRawData())) },
                    recipe = GenshinUnityPackage.DescribeAvatar(avatar, name, string.Empty, Array.Empty<AnimationClip>()) };
                File.WriteAllText(output, JsonConvert.SerializeObject(report, Formatting.Indented));
                Console.WriteLine(output);
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            finally { manager.Clear(); AssetsHelper.Clear(); }
        }

        public static int RunAnimationIndex(string[] args)
        {
            if (args.Length != 3)
            {
                Console.Error.WriteLine("Usage: --genshin-animation-index <map> <new report.json>");
                return 2;
            }
            try
            {
                if (ResourceMap.FromFile(args[1]) < 0 || !ResourceMap.GetGameType().IsGI())
                    throw new InvalidDataException("Expected a Genshin asset map: " + args[1]);
                var clips = ResourceMap.GetEntries().Where(e => e.Type == ClassIDType.AnimationClip &&
                    e.Name?.Contains("Beyd", StringComparison.OrdinalIgnoreCase) == true)
                    .DistinctBy(e => (e.Source, e.Offset, e.PathID)).Take(10001).ToArray();
                if (clips.Length > 10000) throw new InvalidDataException("Over 10,000 Beyd animation entries; narrow the index.");
                var output = Path.GetFullPath(args[2]);
                if (File.Exists(output)) throw new IOException("Choose a new report path.");
                File.WriteAllText(output, JsonConvert.SerializeObject(new { schemaVersion = 1,
                    sourceMap = Path.GetFullPath(args[1]), nameFilter = "Beyd", type = "AnimationClip",
                    count = clips.Length, entries = clips.Select(e => new { e.Name,
                        Type = e.Type.ToString(), e.Source, PathID = e.PathID.ToString(),
                        e.Container, e.Offset, e.Hash }) }, Formatting.Indented));
                Console.WriteLine(output);
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            finally { ResourceMap.Clear(); }
        }

        public static int RunMapPaths(string[] args)
        {
            if (args.Length != 4)
            {
                Console.Error.WriteLine("Usage: --genshin-map-paths <map> <path-ids.json> <new report.json>");
                return 2;
            }
            try
            {
                var ids = JArray.Parse(File.ReadAllText(args[2])).Select(token => long.Parse((string)token)).ToHashSet();
                if (ids.Count is < 1 or > 64) throw new InvalidDataException("Select 1–64 path IDs.");
                if (ResourceMap.FromFile(args[1]) < 0 || !ResourceMap.GetGameType().IsGI())
                    throw new InvalidDataException("Expected a Genshin asset map: " + args[1]);
                var matches = ResourceMap.GetEntries().Where(e => ids.Contains(e.PathID)).Take(257).ToArray();
                if (matches.Length > 256) throw new InvalidDataException("Too many path-ID matches; narrow the request.");
                var output = Path.GetFullPath(args[3]);
                if (File.Exists(output)) throw new IOException("Choose a new report path.");
                File.WriteAllText(output, JsonConvert.SerializeObject(new { schemaVersion = 1,
                    sourceMap = Path.GetFullPath(args[1]), selectedPathIds = ids.Select(id => id.ToString()).ToArray(),
                    count = matches.Length, entries = matches.Select(e => new { e.Name,
                        Type = e.Type.ToString(), e.Source, PathID = e.PathID.ToString(),
                        e.Container, e.Offset, e.Hash }) }, Formatting.Indented));
                Console.WriteLine(output);
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            finally { ResourceMap.Clear(); }
        }

        public static int RunHierarchyComponents(string[] args)
        {
            if (args.Length != 5 || string.IsNullOrWhiteSpace(args[3]) || args[3].Length > 128)
            {
                Console.Error.WriteLine("Usage: --genshin-hierarchy-components <maps joined by |> <assembly-request.json> <name-substring> <new report.json>");
                return 2;
            }
            var manager = new AssetsManager { Game = GameManager.GetGameByType(GameType.GI) };
            try
            {
                var entries = args[1].Split('|').SelectMany(mapPath =>
                {
                    if (ResourceMap.FromFile(mapPath) < 0 || !ResourceMap.GetGameType().IsGI())
                        throw new InvalidDataException("Expected a Genshin asset map: " + mapPath);
                    return ResourceMap.GetEntries().ToArray();
                }).DistinctBy(e => (e.Source, e.Offset, e.PathID, e.Type)).ToArray();
                var request = JObject.Parse(File.ReadAllText(args[2]));
                var selector = request["base"] ?? throw new InvalidDataException("Assembly request has no exact base root.");
                var type = Enum.Parse<ClassIDType>((string)selector["type"]);
                var pathId = long.Parse((string)selector["pathID"]);
                var source = (string)selector["source"];
                var entry = entries.Single(e => e.Type == type && e.PathID == pathId &&
                    string.Equals(e.Source, source, StringComparison.OrdinalIgnoreCase));
                manager.FilterData.Items = new System.Collections.Generic.List<AssetsManager.AssetFilterDataItem>
                { new() { Source = entry.Source, Offset = entry.Offset, PathID = entry.PathID,
                    Name = entry.Name, Type = entry.Type } };
                manager.LoadFiles(new[] { entry.Source }, mergeSplitAssets: false);
                var root = manager.assetsFileList.SelectMany(f => f.ObjectsDic.Values).Single(o =>
                    o.type == type && o.m_PathID == pathId &&
                    string.Equals(o.assetsFile.originalPath, source, StringComparison.OrdinalIgnoreCase));
                var resolved = new AssetDependencyResolver(manager, entries).Resolve(new[] { root });
                var gameObjects = resolved.Objects.OfType<GameObject>().Where(go =>
                    go.Name.Contains(args[3], StringComparison.OrdinalIgnoreCase)).ToArray();
                if (gameObjects.Length > 100) throw new InvalidDataException("Over 100 matching GameObjects; narrow the substring.");
                (object[] chain, bool complete) Ancestors(GameObject go)
                {
                    var chain = new System.Collections.Generic.List<object>();
                    var seen = new System.Collections.Generic.HashSet<long>();
                    var current = go.m_Transform;
                    for (int depth = 0; depth < 128; depth++)
                    {
                        if (current == null) return (chain.ToArray(), false);
                        if (current.m_Father.m_PathID == 0) return (chain.ToArray(), true);
                        if (!current.m_Father.TryGet(out var parent) || !seen.Add(parent.m_PathID) ||
                            !parent.m_GameObject.TryGet(out var parentGo)) return (chain.ToArray(), false);
                        chain.Add(new { parentGo.Name, source = parentGo.assetsFile.originalPath,
                            pathId = parentGo.m_PathID.ToString(), active = parentGo.m_IsActive });
                        current = parent;
                    }
                    return (chain.ToArray(), false);
                }
                var output = Path.GetFullPath(args[4]);
                if (File.Exists(output)) throw new IOException("Choose a new report path.");
                File.WriteAllText(output, JsonConvert.SerializeObject(new { schemaVersion = 1,
                    root = new { root.Name, rootType = root.type.ToString(), source, pathId = pathId.ToString() },
                    substring = args[3], count = gameObjects.Length,
                    objects = gameObjects.Select(go => {
                        var ancestors = Ancestors(go);
                        return new { go.Name, source = go.assetsFile.originalPath,
                            pathId = go.m_PathID.ToString(), active = go.m_IsActive, tag = go.m_Tag,
                            ancestors = ancestors.chain, ancestorChainComplete = ancestors.complete,
                            components = go.m_Components.Select(p => {
                                p.TryGet(out var component);
                                return new { p.m_FileID, pathId = p.m_PathID.ToString(),
                                    type = component?.type.ToString(), name = component?.Name,
                                    enabled = component is Renderer renderer ? renderer.m_Enabled : (bool?)null };
                            }) };
                    }) }, Formatting.Indented));
                Console.WriteLine(output);
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            finally { manager.Clear(); ResourceMap.Clear(); }
        }

        // The full map is compressed; a bounded exact substring query helps
        // distinguish an absent source object from a narrow historical inventory.
        public static int RunMapQuery(string[] args)
        {
            if (args.Length != 4 || string.IsNullOrWhiteSpace(args[2]) || args[2].Length > 128)
            {
                Console.Error.WriteLine("Usage: --genshin-map-query <map> <name-substring> <new report.json>");
                return 2;
            }
            try
            {
                if (ResourceMap.FromFile(args[1]) < 0 || !ResourceMap.GetGameType().IsGI())
                    throw new InvalidDataException("Expected a Genshin asset map: " + args[1]);
                var matches = ResourceMap.GetEntries().Where(e => e.Name?.Contains(args[2], StringComparison.OrdinalIgnoreCase) == true)
                    .Take(101).ToArray();
                if (matches.Length > 100) throw new InvalidDataException("Query exceeds 100 results; narrow the substring.");
                var output = Path.GetFullPath(args[3]);
                if (File.Exists(output)) throw new IOException("Choose a new report path.");
                File.WriteAllText(output, JsonConvert.SerializeObject(new { schemaVersion = 1,
                    sourceMap = Path.GetFullPath(args[1]), substring = args[2], count = matches.Length,
                    entries = matches.Select(e => new { e.Name, Type = e.Type.ToString(), e.Source,
                        PathID = e.PathID.ToString(), e.Container, e.Offset, e.Hash }) }, Formatting.Indented));
                Console.WriteLine(output);
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            finally { ResourceMap.Clear(); }
        }

        public static int Run(string[] args)
        {
            if (args.Length != 4)
            {
                Console.Error.WriteLine("Usage: --genshin-source-probe <maps joined by |> <selectors.json> <new report.json>");
                return 2;
            }
            var manager = new AssetsManager { Game = GameManager.GetGameByType(GameType.GI) };
            try
            {
                var entries = args[1].Split('|').SelectMany(mapPath =>
                {
                    if (ResourceMap.FromFile(mapPath) < 0 || !ResourceMap.GetGameType().IsGI())
                        throw new InvalidDataException("Expected a Genshin asset map: " + mapPath);
                    return ResourceMap.GetEntries().ToArray();
                }).DistinctBy(e => (e.Source, e.Offset, e.PathID, e.Type)).ToArray();
                var selectors = JArray.Parse(File.ReadAllText(args[2]));
                if (selectors.Count is < 1 or > 32) throw new InvalidDataException("Select 1-32 exact source objects.");
                var selected = selectors.Select(selector =>
                {
                    var type = Enum.Parse<ClassIDType>((string)selector["type"]);
                    var pathId = long.Parse((string)selector["pathId"]);
                    var source = (string)selector["source"];
                    return entries.Single(entry => entry.Type == type && entry.PathID == pathId &&
                        string.Equals(entry.Source, source, StringComparison.OrdinalIgnoreCase));
                }).ToArray();
                if (selected.Any(e => e.Offset < 0)) throw new InvalidDataException("Map needs bundle offsets.");
                manager.FilterData.Items = selected.Select(e => new AssetsManager.AssetFilterDataItem
                { Source = e.Source, Offset = e.Offset, PathID = e.PathID, Name = e.Name, Type = e.Type }).ToList();
                manager.LoadFiles(selected.Select(e => e.Source).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), mergeSplitAssets: false);
                var objects = manager.assetsFileList.SelectMany(file => file.ObjectsDic.Values).ToArray();
                var results = selected.Select(entry =>
                {
                    var obj = objects.Single(o => o.type == entry.Type && o.m_PathID == entry.PathID &&
                        string.Equals(o.assetsFile.originalPath, entry.Source, StringComparison.OrdinalIgnoreCase));
                    var raw = obj.GetRawData();
                    var strings = new System.Collections.Generic.List<object>();
                    for (int i = 0; i < raw.Length;)
                    {
                        if (raw[i] < 32 || raw[i] > 126) { i++; continue; }
                        int start = i;
                        while (i < raw.Length && raw[i] >= 32 && raw[i] <= 126) i++;
                        if (i - start >= 4 && i - start <= 256 &&
                            (strings.Count < 160 || obj is Shader &&
                             (raw.AsSpan(start, i - start).IndexOf("miHoYo/"u8) >= 0 ||
                              raw.AsSpan(start, i - start).IndexOf("MoleMole."u8) >= 0 ||
                              raw.AsSpan(start, i - start).IndexOf("Shader"u8) >= 0)))
                            strings.Add(new { offset = start, value = Encoding.ASCII.GetString(raw, start, i - start) });
                    }
                    var mono = obj as MonoBehaviour;
                    var shaderContexts = new System.Collections.Generic.List<object>();
                    if (obj is Shader)
                    {
                        ReadOnlySpan<byte> marker = "miHoYo/Character/Character_Ugc"u8;
                        for (int at = 0; at < raw.Length - marker.Length; at++)
                        {
                            if (!raw.AsSpan(at).StartsWith(marker)) continue;
                            int start = Math.Max(0, at - 64);
                            int count = Math.Min(raw.Length - start, 384);
                            shaderContexts.Add(new { offset = at, contextStart = start,
                                contextBase64 = Convert.ToBase64String(raw, start, count) });
                        }
                    }
                    return new
                    {
                        entry.Name, type = entry.Type.ToString(), entry.Source, pathId = entry.PathID.ToString(),
                        serializedFile = obj.assetsFile.fileName, bytes = raw.Length,
                        shaderName = obj is Shader shader ? shader.Name : null,
                        shaderNameSource = obj is Shader named ? named.NameSource : null,
                        shaderFooterName = obj is Shader ? GenshinShaderNameReader.TryRead(raw) : null,
                        shaderSubshaders = (obj as Shader)?.m_ParsedForm?.m_SubShaders?.Count,
                        shaderPasses = (obj as Shader)?.m_ParsedForm?.m_SubShaders?.Sum(sub => sub.m_Passes?.Count ?? 0),
                        shaderPlatforms = (obj as Shader)?.platforms?.Select(p => p.ToString()).ToArray(),
                        shaderCompressedBlobBytes = (obj as Shader)?.compressedBlob?.Length,
                        shaderContexts,
                        script = mono?.m_Script.Name,
                        typed = mono?.ToType(),
                        gameObjectComponents = obj is GameObject gameObject ?
                            gameObject.m_Components.Select(p => new { fileID = p.m_FileID,
                                pathID = p.m_PathID.ToString() }).ToArray() : null,
                        rawBase64 = mono != null && raw.Length <= 4096 || obj is Shader && raw.Length <= 2 * 1024 * 1024
                            ? Convert.ToBase64String(raw) : null,
                        strings
                    };
                }).ToArray();
                var output = Path.GetFullPath(args[3]);
                if (File.Exists(output)) throw new IOException("Choose a new report path.");
                File.WriteAllText(output, JsonConvert.SerializeObject(new { schemaVersion = 1, results }, Formatting.Indented));
                Console.WriteLine(output);
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
            finally { manager.Clear(); }
        }
    }
}
