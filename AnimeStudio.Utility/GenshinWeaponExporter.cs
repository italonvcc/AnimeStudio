using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AnimeStudio
{
    public sealed record GenshinWeaponOptions(bool Animations, bool Vfx, bool Materials, bool UnityImport = false);

    /// <summary>Source-bound weapon export. Candidate names remain evidence, never automatic bindings.</summary>
    public static class GenshinWeaponExporter
    {
        public static string Export(GenshinWeaponReferences references, GenshinWeaponFamily family,
            string destination, GenshinWeaponOptions options, Action<string> progress = null,
            CancellationToken cancellation = default)
        {
            if (references == null || family == null || options == null) throw new ArgumentNullException("Weapon export inputs are required.");
            destination = NewDestination(destination);
            family = JsonConvert.DeserializeObject<GenshinWeaponFamily>(JsonConvert.SerializeObject(family));
            Directory.CreateDirectory(destination);
            return ExportInto(references, family, destination, options, progress, cancellation,
                candidateIndex: new AssetDependencyIndex(references.Entries, references.SerializedFiles.Select(f => new AssetDependencyIndex.Container(f.SerializedFile, f.Source, f.Offset))));
        }

        public static string ExportCatalog(GenshinWeaponReferences references, string destination,
            GenshinWeaponOptions options, Action<string> progress = null, CancellationToken cancellation = default)
        {
            if (references == null || options == null) throw new ArgumentNullException("Weapon export inputs are required.");
            destination = NewDestination(destination);
            // Export broad GameObject roots first so a later child/Animator/mesh
            // record can be covered only after exact source graph proof exists.
            var families = JsonConvert.DeserializeObject<List<GenshinWeaponFamily>>(JsonConvert.SerializeObject(references.Catalog.Families)).OrderBy(f => f.Roots.Count == 0 ? 3 :
                    f.Roots[0].Type == ClassIDType.GameObject && !f.Roots[0].Name.EndsWith("_Model", StringComparison.Ordinal) ? 0 :
                    f.Roots[0].Type == ClassIDType.GameObject ? 1 : 2)
                .ThenBy(f => f.Key, StringComparer.Ordinal).ToArray();
            if (families.Select(f => FolderKey(f.Key)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != families.Length)
                throw new InvalidDataException("Catalog family keys collide after path normalization.");
            Directory.CreateDirectory(destination);
            AtomicJson(Path.Combine(destination, "preflight.json"), Preflight(families, destination));
            var weapons = Path.Combine(destination, "Weapons");
            Directory.CreateDirectory(weapons);
            AtomicJson(Path.Combine(destination, "catalog.json"), new
            {
                schemaVersion = 1, references.GameVersion, references.Fingerprint,
                references.ScanScope, references.ScanCompleteness, references.SourceHashMode,
                references.VersionCoverage, references.ScanErrors, references.SourceFailures,
                references.SourcesWithoutSerializedFiles, families
            });
            var entries = new List<object>();
            var candidateIndex = new AssetDependencyIndex(references.Entries, references.SerializedFiles.Select(f => new AssetDependencyIndex.Container(f.SerializedFile, f.Source, f.Offset)));
            var covered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string ledger = Path.Combine(destination, "weapon-export.json");
            void WriteLedger(string status) => AtomicJson(ledger, new
            {
                schemaVersion = 1, status, references.GameVersion, references.Fingerprint,
                references.ScanScope, references.ScanCompleteness, references.SourceHashMode,
                references.VersionCoverage, tool = ToolIdentity(), options,
                sharedFiles = SharedFiles(destination),
                total = families.Length, accounted = entries.Count, entries
            });
            WriteLedger("Running");
            foreach (var family in families)
            {
                if (cancellation.IsCancellationRequested)
                {
                    entries.Add(new { family.Key, family.Name, status = "Cancelled", manifest = (string)null, error = (string)null });
                    WriteLedger("Cancelled");
                    continue;
                }
                string folder = Path.Combine(weapons, FolderKey(family.Key));
                try
                {
                    Directory.CreateDirectory(folder);
                    var requested = family.Roots.Count > 0 ? family.Roots : family.Meshes;
                    var matched = requested.Where(e => covered.ContainsKey(EntryKey(e))).ToArray();
                    string manifest = requested.Count > 0 && matched.Length == requested.Count
                        ? WriteCoveredReport(references, family, folder, options, matched.Select(e => new
                            { entry = e, originalReport = covered[EntryKey(e)] }).ToArray())
                        : ExportInto(references, family, folder, options, progress, cancellation, destination, candidateIndex);
                    string status = (string)JObject.Parse(File.ReadAllText(manifest))["export"]["status"];
                    if (matched.Length != requested.Count)
                    {
                        var written = JObject.Parse(File.ReadAllText(manifest));
                        foreach (var variant in written["export"]?["variants"] ?? new JArray())
                        {
                            foreach (var node in variant["sourceGraph"]?["nodes"] ?? new JArray())
                            {
                                if ((bool?)node["inModelHierarchy"] != true) continue;
                                string key = IdentityKey(node["source"]);
                                if (key != null) covered.TryAdd(key, Path.GetRelativePath(destination, manifest).Replace('\\', '/'));
                                foreach (var component in node["components"] ?? new JArray())
                                {
                                    if (component is not JObject componentObject || (string)componentObject["type"] != "Animator") continue;
                                    key = IdentityKey(component);
                                    if (key != null) covered.TryAdd(key, Path.GetRelativePath(destination, manifest).Replace('\\', '/'));
                                }
                            }
                            string modelManifest = (string)variant["stages"]?["model"]?["manifest"];
                            if (modelManifest == null) continue;
                            string absolute = Path.GetFullPath(Path.Combine(folder, modelManifest));
                            if (!File.Exists(absolute)) continue;
                            var model = JObject.Parse(File.ReadAllText(absolute));
                            foreach (var mesh in model["meshes"] ?? new JArray())
                            {
                                string key = IdentityKey(mesh["sourceMesh"]);
                                if (key != null) covered.TryAdd(key, Path.GetRelativePath(destination, manifest).Replace('\\', '/'));
                            }
                        }
                    }
                    entries.Add(new { family.Key, family.Name, status, manifest = Path.GetRelativePath(destination, manifest).Replace('\\', '/'), error = (string)null });
                }
                catch (OperationCanceledException)
                {
                    entries.Add(new { family.Key, family.Name, status = "Cancelled", manifest = (string)null, error = (string)null });
                    WriteLedger("Cancelled");
                    continue;
                }
                catch (Exception ex)
                {
                    Directory.CreateDirectory(folder);
                    File.WriteAllText(Path.Combine(folder, "EXPORT-INCOMPLETE.txt"), ex.ToString());
                    entries.Add(new { family.Key, family.Name, status = "Failed", manifest = (string)null, error = ex.Message });
                }
                WriteLedger("Running");
            }
            bool cancelled = cancellation.IsCancellationRequested;
            var finalStatuses = entries.Select(e => (string)JObject.FromObject(e)["status"]).ToArray();
            WriteLedger(cancelled ? "Cancelled" : finalStatuses.All(s => s == "Complete") ? "Complete" :
                finalStatuses.All(s => s == "Failed") ? "Failed" : "Partial");
            return ledger;
        }

        private static string ExportInto(GenshinWeaponReferences references, GenshinWeaponFamily family,
            string destination, GenshinWeaponOptions options, Action<string> progress, CancellationToken cancellation,
            string sharedPackageRoot = null, AssetDependencyIndex candidateIndex = null)
        {
            string report = Path.Combine(destination, FolderKey(family.Key) + ".weapon.json");
            var variants = new List<object>();
            var errors = new List<object>();
            var candidates = family.AnimationCandidates.Select(e => new { entry = e, reason = "Name/index candidate; no attached source reference proved ownership" }).ToArray();
            var effectCandidates = family.EffectCandidates.Select(e => new { entry = e, reason = "Name/index candidate; no spawn or hierarchy link proved ownership" }).ToArray();
            var roots = family.Roots.DistinctBy(EntryKey).ToArray();
            if (roots.Length == 0) errors.Add(new { stage = "Model", reason = "No source GameObject/Animator root; mesh candidates cannot form a verified hierarchy" });
            var seenGameObjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int visitedRoots = 0;
            foreach (var entry in roots)
            {
                if (cancellation.IsCancellationRequested) break;
                visitedRoots++;
                string variantKey = FolderKey(entry.Name + "-" + ShortHash(EntryKey(entry)));
                string variantFolder = Path.Combine(destination, "Variants", variantKey);
                var stage = new Dictionary<string, object>();
                AssetsManager manager = new() { Game = GameManager.GetGameByType(GameType.GI), ResolveDependencies = false,
                    ObjectReadWorkers = GenshinExportWorkers.Count };
                try
                {
                    progress?.Invoke("Weapon " + family.Name + ": " + entry.Name);
                    Load(manager, entry);
                    Object selected = manager.FindAsset(entry) ?? throw new InvalidDataException("Selected source root did not load; rebuild the map.");
                    if (selected is not (Animator or GameObject)) throw new InvalidDataException("Catalog root is not an Animator or GameObject.");
                    var discovery = new AssetDependencyResolver(manager, candidateIndex ?? new AssetDependencyIndex(references.Entries, references.SerializedFiles.Select(f => new AssetDependencyIndex.Container(f.SerializedFile, f.Source, f.Offset))))
                    {
                        IncludeMaterials = options.Materials,
                        IncludeAnimatorControllers = options.Animations,
                        IncludeAnimationObjectReferences = options.Animations,
                        MaximumBundles = 256, MaximumPasses = 12, MaximumControllerClips = 256
                    }.Resolve(new[] { selected }, cancellation);
                    GameObject gameObject = selected is Animator a
                        ? a.m_GameObject.TryGet(out var go) ? go : throw new InvalidDataException("Animator GameObject did not resolve")
                        : (GameObject)selected;
                    string rootId = SourceId(gameObject);
                    if (!seenGameObjects.Add(rootId))
                    {
                        variants.Add(new { source = entry, status = "DuplicateSourceRoot", aliasOf = rootId });
                        continue;
                    }
                    Animator animator = selected as Animator ?? discovery.Objects.OfType<Animator>()
                        .FirstOrDefault(a => a.m_GameObject.TryGet(out var attached) && ReferenceEquals(attached, gameObject));
                    Object modelRoot = (Object)animator ?? gameObject;
                    var controllers = discovery.Objects.Where(o => o is AnimatorController or AnimatorOverrideController).ToArray();
                    var clips = options.Animations ? discovery.Objects.OfType<AnimationClip>().DistinctBy(SourceId).ToArray() : Array.Empty<AnimationClip>();
                    var clipTargets = clips.ToDictionary(clip => clip, clip => clip.FindRoots().Where(go => IsDescendant(go, gameObject))
                        .DistinctBy(SourceId).Select(go => new { path = RelativeHierarchyPath(go, gameObject),
                            sourceRoot = SourceIdentity(go), componentKinds = go.m_Components
                                .Select(p => p.TryGet(out Component component) ? component : null)
                                .Where(c => c is Animator or Animation).Select(c => c.type.ToString()).Distinct().ToArray() })
                        .Where(target => target.path != null).ToArray());
                    var sourceGraph = new
                    {
                        root = SourceIdentity(gameObject),
                        dependencies = discovery.Objects.Select(SourceIdentity).ToArray(),
                        nodes = discovery.Objects.OfType<GameObject>().Select(node => new
                        {
                            source = SourceIdentity(node),
                            inModelHierarchy = IsDescendant(node, gameObject),
                            components = node.m_Components.Select(p => p.TryGet(out Component component)
                                ? SourceIdentity(component) : null).ToArray()
                        }).ToArray(),
                        controllers = controllers.Select(controller => new
                        {
                            source = SourceIdentity(controller),
                            clips = ControllerClips(controller)
                        }).ToArray(),
                        renderers = discovery.Objects.OfType<Renderer>().Select(renderer => new
                        {
                            source = SourceIdentity(renderer),
                            mesh = renderer is SkinnedMeshRenderer skin ? PointerEvidence(skin.m_Mesh) : null,
                            rootBone = renderer is SkinnedMeshRenderer skinned ? PointerEvidence(skinned.m_RootBone) : null,
                            bones = renderer is SkinnedMeshRenderer rig ? rig.m_Bones.Select(PointerEvidence).ToArray() : Array.Empty<object>(),
                            materials = renderer.m_Materials.Select(PointerEvidence).ToArray()
                        }).ToArray(),
                        legacyAnimations = discovery.Objects.OfType<Animation>().Select(animation => new
                        {
                            source = SourceIdentity(animation),
                            clips = animation.m_Animations.Select(p => new { p.m_PathID, file = ((IObjectReference)p).SerializedFileName }).ToArray()
                        }).ToArray()
                    };
                    string sourceGraphPath = Path.Combine(destination, "Source", variantKey + ".graph.json");
                    Directory.CreateDirectory(Path.GetDirectoryName(sourceGraphPath));
                    AtomicJson(sourceGraphPath, sourceGraph);
                    stage["sourceDiscovery"] = new { graph = Path.GetRelativePath(destination, sourceGraphPath).Replace('\\', '/'),
                        unresolved = discovery.Missing };
                    // Custom components can carry physics/visibility/attachment
                    // settings that FBX cannot represent. Preserve their bytes and
                    // any available type tree without asserting runtime support.
                    var sourceComponents = new List<object>();
                    foreach (var component in discovery.Objects.Where(o => o is MonoBehaviour || o.GetType() == typeof(Object)))
                    {
                        string evidenceDirectory = Path.Combine(destination, "Source", variantKey);
                        Directory.CreateDirectory(evidenceDirectory);
                        string name = ShortHash(SourceId(component));
                        byte[] bytes = component.GetRawData();
                        string rawPath = Path.Combine(evidenceDirectory, name + ".bin");
                        File.WriteAllBytes(rawPath, bytes);
                        object parsed = null;
                        string decodeError = null;
                        try { parsed = component.ToType(); }
                        catch (Exception ex) { decodeError = ex.Message; }
                        string metadata = Path.Combine(evidenceDirectory, name + ".json");
                        AtomicJson(metadata, new { source = SourceIdentity(component), raw = name + ".bin", parsed, decodeError,
                            status = "SourceEvidenceOnly", note = "Runtime behavior is not reconstructed; unknown bytes are not inferred references" });
                        sourceComponents.Add(new { source = SourceIdentity(component),
                            metadata = Path.GetRelativePath(destination, metadata).Replace('\\', '/'),
                            raw = Path.GetRelativePath(destination, rawPath).Replace('\\', '/'), decodeError });
                    }
                    string animationDiscovery = options.Animations
                        ? discovery.Missing.Any(m => m.ExpectedType is "AnimationClip" or "RuntimeAnimatorController" or "AnimatorController" or "AnimatorOverrideController") ? "Partial" : "AttachedLinksOnly"
                        : "SkippedByOption";
                    Directory.CreateDirectory(Path.Combine(destination, "Source"));
                    var controllerEvidence = new List<object>();
                    foreach (var controller in controllers)
                    {
                        string file = "Source/" + variantKey + "-" + ShortHash(SourceId(controller)) + ".controller.bin";
                        byte[] raw = controller.GetRawData();
                        File.WriteAllBytes(Path.Combine(destination, file.Replace('/', Path.DirectorySeparatorChar)), raw);
                        string decodedFile = file.Replace(".controller.bin", ".controller.json", StringComparison.Ordinal);
                        AtomicJson(Path.Combine(destination, decodedFile.Replace('/', Path.DirectorySeparatorChar)), ControllerData(controller));
                        controllerEvidence.Add(new { source = SourceIdentity(controller), raw = file,
                            decoded = decodedFile,
                            sha256 = Convert.ToHexString(SHA256.HashData(raw)),
                            interpretation = "Original bytes and parsed state/layer/override data retained; a playable Unity graph is not reconstructed" });
                    }
                    cancellation.ThrowIfCancellationRequested();
                    string fbx = GenshinModelExporter.Export(manager, references.Entries, modelRoot, variantFolder,
                        clips, exportMaterials: options.Materials, compactAnimations: false, candidateIndex: candidateIndex,
                        tolerateAnimationFailures: true, allowPerMeshBindPoses: true, allowDetachedRigClosure: true);
                    JObject model = JObject.Parse(File.ReadAllText(Path.Combine(variantFolder, "manifest.json")));
                    bool bindingsOkay = !options.Materials || model["materialBindings"] is JArray bindings &&
                        bindings.All(b => (string)b["status"] == "resolved");
                    bool texturesOkay = !options.Materials || model["nativeTextureFailures"] is JArray native && native.Count == 0;
                    bool unresolved = model["unresolved"] is JArray missing && missing.Count > 0;
                    bool unknownBindings = model["animationFailures"] is JArray failedClips && failedClips.Count > 0 ||
                        clipTargets.Any(pair => pair.Value.Length == 0) ||
                        discovery.Missing.Any(m => m.Field.StartsWith("m_PPtrCurves", StringComparison.Ordinal) ||
                        m.Field.StartsWith("m_Events", StringComparison.Ordinal)) ||
                        Directory.Exists(Path.Combine(variantFolder, "Animations")) &&
                        Directory.GetFiles(Path.Combine(variantFolder, "Animations"), "*.anim")
                            .Any(file => System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(file),
                                @"\b(path_[0-9]+|missed_[0-9]+|script_[0-9]+|typetree_[0-9]+)\b",
                                System.Text.RegularExpressions.RegexOptions.CultureInvariant));
                    bool perMeshBindPoses = (string)model["bindPoseStatus"] == "PerMeshSourceBindPosesUnityImportUnvalidated";
                    bool rigClosure = (string)model["rigClosureStatus"] == "SourceProvenDetachedRigUnityImportUnvalidated";
                    string selectedRootPath = (string)model["selectedRootPath"] ?? "";
                    string ClipTargetPath(string path) => selectedRootPath.Length == 0 ? path :
                        path.Length == 0 ? selectedRootPath : selectedRootPath + "/" + path;
                    string modelStatus = unresolved || !bindingsOkay || !texturesOkay || perMeshBindPoses || rigClosure ? "Partial" : "Complete";
                    stage["model"] = new { status = modelStatus, path = Path.GetRelativePath(destination, fbx).Replace('\\', '/'),
                        manifest = Path.GetRelativePath(destination, Path.Combine(variantFolder, "manifest.json")).Replace('\\', '/'),
                        unresolved = model["unresolved"], materialBindings = model["materialBindings"], nativeTextureFailures = model["nativeTextureFailures"],
                        cubemapBindings = model["cubemapBindings"], bindPoseStatus = model["bindPoseStatus"], bindPoseConflicts = model["bindPoseConflicts"],
                        rigClosureStatus = model["rigClosureStatus"], selectedRootPath, rigClosure = model["rigClosure"] };
                    stage["animations"] = new { status = !options.Animations ? "SkippedByOption" :
                        clips.Length == 0 || animationDiscovery == "Partial" || unknownBindings ? "Partial" : "Complete",
                        discovery = animationDiscovery, clipCount = clips.Length,
                        exportedClipCount = model["sourceClips"]?.Count() ?? 0, failures = model["animationFailures"],
                        unknownBindings, note = "Only attached Animator/controller/override and legacy Animation links are source-proven. Object-reference/event binding and playback still require Unity validation.",
                        source = clips.Select(SourceIdentity).ToArray(), controllers = controllerEvidence,
                        bindings = clipTargets.Select(pair => new { source = SourceIdentity(pair.Key),
                            targetPaths = pair.Value.Select(target => ClipTargetPath(target.path)).ToArray(),
                            targets = pair.Value.Select(target => new { path = ClipTargetPath(target.path), target.sourceRoot, target.componentKinds }).ToArray() }).ToArray() };
                    if (options.Vfx)
                    {
                        // A child effect GameObject is structurally attached. External
                        // name candidates stay in the report and are never auto-bound.
                        bool HasEffectComponent(GameObject go) => go.m_Components.Any(p =>
                            p.TryGet(out Component component) && component is GenshinParticleSystem or GenshinParticleSystemRenderer or GenshinTrailRenderer);
                        var embedded = discovery.Objects.OfType<GameObject>()
                            .Where(go => HasEffectComponent(go) && IsDescendant(go, gameObject))
                            .DistinctBy(SourceId).ToArray();
                        var externalLinked = discovery.Objects.OfType<GameObject>()
                            .Where(go => !ReferenceEquals(go, gameObject) && HasEffectComponent(go) && !IsDescendant(go, gameObject))
                            .DistinctBy(SourceId).Select(SourceIdentity).ToArray();
                        if (embedded.Length > 0)
                        {
                            string vfxFolder = Path.Combine(destination, "VFX", variantKey);
                            string requestPath = Path.Combine(destination, "Source", variantKey + ".vfx-request.json");
                            JObject Selector(GameObject go) => new() { ["name"] = go.Name, ["type"] = "GameObject",
                                ["pathID"] = go.m_PathID.ToString(), ["source"] = go.assetsFile.originalPath,
                                ["offset"] = go.assetsFile.offset,
                                ["evidence"] = "Child in resolved weapon hierarchy" };
                            var request = new JObject { ["character"] = family.Name,
                                ["subject"] = new JObject { ["kind"] = "weapon", ["key"] = family.Key, ["name"] = family.Name },
                                ["gameVersion"] = references.GameVersion,
                                ["actions"] = new JArray(new JObject { ["name"] = "Embedded weapon effects",
                                    ["evidence"] = "Resolved source hierarchy; runtime trigger remains unknown",
                                    ["effects"] = new JArray(embedded.Select(Selector)) }) };
                            File.WriteAllText(requestPath, request.ToString());
                            try
                            {
                                var effectEntries = references.Entries.Concat(embedded.Select(go => new AssetEntry
                                {
                                    Name = go.Name, Type = go.type, PathID = go.m_PathID,
                                    Source = go.assetsFile.originalPath, Offset = go.assetsFile.offset
                                })).DistinctBy(EntryKey).ToArray();
                                JObject vfx = JObject.Parse(File.ReadAllText(GenshinWeaponVfxExport.Export(effectEntries,
                                    requestPath, vfxFolder, options.Materials, cancellation, candidateIndex)));
                                stage["vfx"] = new { status = "Partial", embedded = embedded.Select(SourceIdentity).ToArray(), externalLinked,
                                    manifest = Path.GetRelativePath(destination, Path.Combine(vfxFolder, "manifest.json")).Replace('\\', '/'),
                                    failures = vfx["failures"], unresolved = vfx["unresolved"],
                                    note = "Source effect assets retained; runtime triggering, unknown modules and playback are unverified" };
                            }
                            catch (Exception ex)
                            {
                                errors.Add(new { source = entry, stage = "VFX", reason = ex.Message });
                                stage["vfx"] = new { status = "Failed", reason = ex.Message };
                            }
                        }
                        else stage["vfx"] = new { status = "Partial", externalLinked,
                            note = "No embedded effect root found; external graph links and name candidates have no verified spawn/ownership contract" };
                    }
                    else stage["vfx"] = new { status = "SkippedByOption" };
                    string variantStatus = modelStatus == "Complete" &&
                        (!options.Animations || clips.Length > 0 && animationDiscovery != "Partial" && !unknownBindings) &&
                        !options.Vfx && !errors.Any(e => (string)JObject.FromObject(e)["stage"] == "VFX") ? "Complete" : "Partial";
                    variants.Add(new { source = entry, sourceRoot = SourceIdentity(gameObject), status = variantStatus,
                        stages = stage, sourceGraph, sourceComponents, discoveryUnresolved = discovery.Missing });
                }
                catch (OperationCanceledException) { variants.Add(new { source = entry, status = "Cancelled", stages = stage }); break; }
                catch (Exception ex)
                {
                    errors.Add(new { source = entry, stage = "Variant", reason = ex.Message });
                    variants.Add(new { source = entry, status = "Failed", stages = stage });
                }
                finally { manager.Clear(); }
            }
            if (cancellation.IsCancellationRequested)
                foreach (var entry in roots.Skip(visitedRoots))
                    variants.Add(new { source = entry, status = "Cancelled", reason = "Not started after cancellation" });
            string status = cancellation.IsCancellationRequested ? "Cancelled" : variants.Count == 0 || variants.All(v =>
                (string)JObject.FromObject(v)["status"] == "Failed") ? "Failed" :
                errors.Count == 0 && variants.All(v => (string)JObject.FromObject(v)["status"] is "Complete" or "DuplicateSourceRoot")
                ? "Complete" : "Partial";
            if (status == "Complete" && (family.DiscoveryStatus != "Verified" || candidates.Length != 0 || effectCandidates.Length != 0))
                status = "Partial";
            // Discovery and Unity playback are open even if all file stages succeeded.
            if (status != "Complete") File.WriteAllText(Path.Combine(destination, "EXPORT-INCOMPLETE.txt"),
                "See the weapon report. Source discovery, export coverage, and Unity playback are reported separately.\n");
            var reportData = JObject.FromObject(new
            {
                schemaVersion = 1, status, family.Key, family.Name, family.WeaponClass,
                source = new { references.GameVersion, references.Fingerprint, references.ScanScope,
                    references.ScanCompleteness, roots, family.Meshes, family.MeshCandidates },
                tool = ToolIdentity(),
                requestedOptions = options, effectiveOptions = options,
                discovery = new { status = family.DiscoveryStatus, reason = "Catalog root and source-attached links are bounded; external configs/effects and closed absence search are unverified" },
                export = new { status, variants, errors },
                playback = new { status = "NotTested", reason = "Unity import and animation/effect playback have not been validated" },
                candidateAssociations = new { animations = candidates, effects = effectCandidates },
                files = Array.Empty<object>()
            });
            if (options.UnityImport && !cancellation.IsCancellationRequested && variants.Count != 0)
            {
                // The package reads this provisional report. Its files are then
                // inventoried with the export, avoiding a report hash cycle.
                AtomicJson(report, reportData);
                try
                {
                    GenshinWeaponUnityPackage.Write(destination, report, sharedPackageRoot);
                    reportData["unityImport"] = new JObject { ["status"] = "GeneratedNotValidated",
                        ["note"] = "Offline import recipe only; no Unity Editor operation was performed" };
                }
                catch (Exception ex)
                {
                    errors.Add(new { stage = "UnityImportPackage", reason = ex.Message });
                    reportData["export"]["errors"] = JArray.FromObject(errors);
                    if (status != "Failed") reportData["status"] = reportData["export"]["status"] = "Partial";
                    reportData["unityImport"] = new JObject { ["status"] = "Failed", ["reason"] = ex.Message };
                }
            }
            reportData["files"] = JArray.FromObject(Directory.GetFiles(destination, "*", SearchOption.AllDirectories)
                .Where(p => !string.Equals(p, report, StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p, StringComparer.Ordinal)
                .Select(p => new { path = Path.GetRelativePath(destination, p).Replace('\\', '/'), sha256 = Sha256(p) }));
            AtomicJson(report, reportData);
            return report;
        }

        private static void Load(AssetsManager manager, AssetEntry entry)
        {
            manager.FilterData.Items = new List<AssetsManager.AssetFilterDataItem> { new()
            { Source = entry.Source, Offset = entry.Offset, PathID = entry.PathID, Name = entry.Name, Type = entry.Type } };
            manager.LoadFiles(new[] { entry.Source }, mergeSplitAssets: false);
        }
        private static string NewDestination(string path)
        {
            string full = Path.GetFullPath(path);
            if (Directory.Exists(full) || File.Exists(full)) throw new IOException("Choose a new weapon export directory.");
            return full;
        }
        private static object Preflight(IEnumerable<GenshinWeaponFamily> families, string destination)
        {
            var files = families.SelectMany(f => f.Roots.Concat(f.Meshes)).Select(e => Path.GetFullPath(e.Source))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .Select(p => new FileInfo(p)).ToArray();
            long? available = null;
            try { available = new DriveInfo(Path.GetPathRoot(destination)).AvailableFreeSpace; }
            catch (IOException) { } // Network filesystems may not report capacity.
            if (available == 0) throw new IOException("The export volume has no available space.");
            return new { rootSourceFileCount = files.Length, missingRootSources = files.Where(f => !f.Exists).Select(f => f.FullName).ToArray(),
                availableDiskBytes = available, inputContainerBytes = files.Where(f => f.Exists).Sum(f => f.Length),
                estimatedOutputBytes = (long?)null,
                note = "Output size is unknown until compressed textures/animations are converted; input container bytes are not an output estimate. Missing individual sources are accounted per family." };
        }
        private static object[] SharedFiles(string destination)
        {
            string generic = Path.Combine(destination, "Generic");
            var files = Directory.Exists(generic) ? Directory.GetFiles(generic, "*", SearchOption.AllDirectories).ToList() : new List<string>();
            string instructions = Path.Combine(destination, "UNITY-IMPORT.txt");
            if (File.Exists(instructions)) files.Add(instructions);
            return files.OrderBy(p => p, StringComparer.Ordinal).Select(p => (object)new
                { path = Path.GetRelativePath(destination, p).Replace('\\', '/'), sha256 = Sha256(p) }).ToArray();
        }
        private static string EntryKey(AssetEntry e) => $"{Path.GetFullPath(e.Source).ToUpperInvariant()}|{e.Offset}|{e.PathID}|{e.Type}";
        private static bool IsDescendant(GameObject candidate, GameObject root)
        {
            var transform = candidate.m_Transform;
            var seen = new HashSet<Transform>();
            while (transform != null && seen.Add(transform))
            {
                if (transform.m_GameObject.TryGet(out var current) && ReferenceEquals(current, root)) return true;
                transform = transform.m_Father.TryGet(out var parent) ? parent : null;
            }
            return false;
        }
        private static string RelativeHierarchyPath(GameObject candidate, GameObject root)
        {
            var parts = new List<string>();
            var seen = new HashSet<Transform>();
            var transform = candidate.m_Transform;
            while (transform != null && seen.Add(transform))
            {
                if (!transform.m_GameObject.TryGet(out var current)) return null;
                if (ReferenceEquals(current, root)) return string.Join("/", parts.AsEnumerable().Reverse());
                parts.Add(current.Name);
                transform = transform.m_Father.TryGet(out var parent) ? parent : null;
            }
            return null;
        }
        private static string IdentityKey(JToken identity)
        {
            if (identity is not JObject objectIdentity) return null;
            string source = (string)(objectIdentity["source"] ?? objectIdentity["Source"]);
            string offset = (string)(objectIdentity["offset"] ?? objectIdentity["Offset"]);
            string pathID = (string)(objectIdentity["pathID"] ?? objectIdentity["PathID"]);
            string type = (string)(objectIdentity["type"] ?? objectIdentity["Type"]);
            return source == null || offset == null || pathID == null || type == null ? null :
                $"{Path.GetFullPath(source).ToUpperInvariant()}|{offset}|{pathID}|{type}";
        }
        private static string WriteCoveredReport(GenshinWeaponReferences references, GenshinWeaponFamily family,
            string folder, GenshinWeaponOptions options, object[] coverage)
        {
            string report = Path.Combine(folder, FolderKey(family.Key) + ".weapon.json");
            AtomicJson(report, new
            {
                schemaVersion = 1, status = "Partial", family.Key, family.Name, family.WeaponClass,
                source = new { references.GameVersion, references.Fingerprint, references.ScanScope,
                    references.ScanCompleteness, family.Roots, family.MeshCandidates },
                tool = ToolIdentity(), requestedOptions = options, effectiveOptions = options,
                discovery = new { status = "Candidate", reason = "Exact qualified object was found in another exported source graph; catalog family/variant relation is unverified" },
                export = new { status = "Partial", coveredBy = coverage,
                    reason = "This source object is included as a dependency of the referenced root export. No duplicate model or separate weapon identity is asserted." },
                playback = new { status = "NotTested" },
                candidateAssociations = new { animations = family.AnimationCandidates, effects = family.EffectCandidates }
            });
            File.WriteAllText(Path.Combine(folder, "EXPORT-INCOMPLETE.txt"),
                "Exact source coverage is linked in the weapon report; family/variant identity and Unity playback remain unverified.\n");
            return report;
        }
        private static object[] ControllerClips(Object controller)
        {
            if (controller is AnimatorController standard)
                return standard.m_AnimationClips.Select(p => (object)new { p.m_PathID, file = ((IObjectReference)p).SerializedFileName }).ToArray();
            if (controller is AnimatorOverrideController overrides)
                return overrides.m_Clips.SelectMany(c => new[] { c.m_OriginalClip, c.m_OverrideClip })
                    .Select(p => (object)new { p.m_PathID, file = ((IObjectReference)p).SerializedFileName }).ToArray();
            return Array.Empty<object>();
        }
        private static object PointerEvidence(IObjectReference pointer)
        {
            if (pointer == null) return null;
            bool resolved = pointer.TryGetObject(out var target);
            return new { file = pointer.SerializedFileName, pathID = pointer.PathID.ToString(),
                isNull = pointer.IsNull, expectedType = pointer.ObjectType.Name, resolved,
                source = resolved ? SourceIdentity(target) : null };
        }

        private static object ControllerData(Object controller)
        {
            object Pointer<T>(PPtr<T> pointer) where T : Object => new
            { file = ((IObjectReference)pointer).SerializedFileName, pathID = pointer.m_PathID.ToString() };
            return controller switch
            {
                AnimatorController standard => new { schemaVersion = 1, source = SourceIdentity(standard),
                    serializedSize = standard.m_ControllerSize, state = standard.m_Controller, strings = standard.m_TOS,
                    clips = standard.m_AnimationClips.Select(p => Pointer(p)).ToArray(),
                    interpretation = "Parsed source constants and clip index order; not a generated Unity controller" },
                AnimatorOverrideController overrides => (object)new { schemaVersion = 1, source = SourceIdentity(overrides),
                    controller = Pointer(overrides.m_Controller),
                    overrides = overrides.m_Clips.Select(c => new { original = Pointer(c.m_OriginalClip), replacement = Pointer(c.m_OverrideClip) }).ToArray(),
                    interpretation = "Ordered source overrides retained, including null replacements" },
                _ => throw new InvalidDataException("Unsupported controller type")
            };
        }
        private static string SourceId(Object o) => $"{o.assetsFile.originalPath}|{o.assetsFile.offset}|{o.assetsFile.fileName}|{o.m_PathID}|{o.type}";
        private static object SourceIdentity(Object o) => new { o.Name, type = o.type.ToString(), file = o.assetsFile.fileName,
            pathID = o.m_PathID.ToString(), source = o.assetsFile.originalPath, offset = o.assetsFile.offset,
            sha256 = Convert.ToHexString(SHA256.HashData(o.GetRawData())) };
        private static string ShortHash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..12];
        private static string FolderKey(string value)
        {
            var safe = new string((value ?? "weapon").Where(c => char.IsLetterOrDigit(c) && c < 128 || c is '-' or '_').Take(64).ToArray()).Trim('.', ' ');
            if (safe.Length == 0) safe = "weapon";
            return safe + "-" + ShortHash(value ?? "weapon");
        }
        private static string Sha256(string file) { using var stream = File.OpenRead(file); return Convert.ToHexString(SHA256.HashData(stream)); }
        private static object ToolIdentity()
        {
            var assembly = typeof(GenshinWeaponExporter).Assembly;
            string location = assembly.Location;
            return new
            {
                version = assembly.GetName().Version?.ToString(),
                revision = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(assembly)?.InformationalVersion,
                assemblySha256 = File.Exists(location) ? Sha256(location) : null
            };
        }
        private static void AtomicJson(string path, object value)
        {
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonConvert.SerializeObject(value, Formatting.Indented));
            File.Move(temporary, path, true);
        }
    }
}
