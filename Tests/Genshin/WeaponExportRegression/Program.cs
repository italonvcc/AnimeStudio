using AnimeStudio;
using Newtonsoft.Json.Linq;

if (args.Length > 0 && args[0] == "--native-load-failure")
{
    // Run as `dotnet WeaponExportRegression.dll --native-load-failure`: the
    // existing native loader looks beside dotnet.exe, deliberately unavailable.
    // A partial constructor must not later crash the finalizer thread.
    var contextType = typeof(Fbx).Assembly.GetType("AnimeStudio.FbxInterop.FbxExporterContext")!;
    bool rejected = false;
    try { Activator.CreateInstance(contextType, new object[] { new Fbx.ExportOptions(), Path.GetTempPath() }); }
    catch (System.Reflection.TargetInvocationException) { rejected = true; }
    if (!rejected) throw new Exception("Fixture did not trigger native initialization failure");
    GC.Collect();
    GC.WaitForPendingFinalizers();
    Console.WriteLine("PASS failed native initialization preserves the original error without a finalizer crash");
    return;
}

if (args.Length > 0 && args[0] == "--real-catalog-shard")
{
    if (args.Length != 5) throw new ArgumentException("--real-catalog-shard <map> <new-output> <zero-based-shard> <shard-count>");
    int shard = int.Parse(args[3]), count = int.Parse(args[4]);
    if (count < 1 || shard < 0 || shard >= count) throw new ArgumentException("Invalid shard");
    if (ResourceMap.FromFile(args[1]) < 0) throw new Exception("Invalid map");
    var references = GenshinWeaponReferences.Prepare(args[1], ResourceMap.GetEntries().ToArray(), Console.WriteLine);
    // Independent test processes own disjoint output roots. Production export
    // remains sequential within each process; no shared mutable readers/managers.
    var allKeys = references.Catalog.Families.Select(f => f.Key).OrderBy(k => k, StringComparer.Ordinal).ToArray();
    var keys = allKeys.Where((_, i) => i % count == shard).ToHashSet(StringComparer.Ordinal);
    references.Catalog.Families.RemoveAll(f => !keys.Contains(f.Key));
    string report = GenshinWeaponExporter.ExportCatalog(references, args[2], new(true, true, true, true), Console.WriteLine);
    var ledger = JObject.Parse(File.ReadAllText(report));
    if ((int)ledger["total"] != keys.Count || (int)ledger["accounted"] != keys.Count || (string)ledger["status"] == "Running")
        throw new Exception("Shard accounting incomplete");
    File.WriteAllText(Path.Combine(args[2], "validation-shard.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new {
        shard, count, fullCatalogCount = allKeys.Length, references.Fingerprint, keys = keys.OrderBy(k => k),
        assemblySha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(GenshinWeaponExporter).Assembly.Location)))
    }, Newtonsoft.Json.Formatting.Indented));
    Console.WriteLine($"PASS shard {shard}/{count}: {keys.Count} entries accounted; {ledger["entries"].Count(e => (string)e["status"] == "Failed")} failed exports require diagnosis; Unity not tested.");
    return;
}

if (args.Length > 0 && args[0] == "--real-selection")
{
    if (args.Length != 4) throw new ArgumentException("--real-selection <map> <new-output> <key-array.json>");
    var keys = JArray.Parse(File.ReadAllText(args[3])).Values<string>().ToHashSet(StringComparer.Ordinal);
    if (keys.Count == 0 || ResourceMap.FromFile(args[1]) < 0) throw new ArgumentException("Missing selection or map");
    var references = GenshinWeaponReferences.Prepare(args[1], ResourceMap.GetEntries().ToArray(), Console.WriteLine);
    references.Catalog.Families.RemoveAll(f => !keys.Contains(f.Key));
    if (references.Catalog.Families.Count != keys.Count) throw new Exception("Selected keys do not match current source catalog");
    string report = GenshinWeaponExporter.ExportCatalog(references, args[2], new(true, true, true, true), Console.WriteLine);
    var ledger = JObject.Parse(File.ReadAllText(report));
    if ((int)ledger["accounted"] != keys.Count) throw new Exception("Selection accounting incomplete");
    Console.WriteLine($"PASS selection accounting: {keys.Count} records; failed={ledger["entries"].Count(e => (string)e["status"] == "Failed")}; inspect source/model outcomes separately. Unity not tested.");
    return;
}

if (args.Length > 0 && args[0] == "--appearance-policy")
{
    if (args.Length < 4) throw new ArgumentException("--appearance-policy <map> <new-output> <source-file>...");
    if (Directory.Exists(args[2])) throw new IOException("Choose a new policy fixture output");
    if (ResourceMap.FromFile(args[1]) < 0) throw new Exception("Invalid map");
    var references = GenshinWeaponReferences.Prepare(args[1], ResourceMap.GetEntries().ToArray(), Console.WriteLine, sourceFiles: args.Skip(3));
    var family = references.Catalog.Families.Single(f => f.SourceToken == "Kasabouzu" && f.Roots.Count > 0 && f.Roots[0].Name == "Equip_Sword_Kasabouzu");
    // Remove the alternative exact-container evidence too: this fixture simulates
    // unavailable dependencies, not merely an incomplete object-level index.
    references.SerializedFiles.Clear();
    references.Entries.RemoveAll(e => e.Type is ClassIDType.Material or ClassIDType.Texture2D or ClassIDType.Cubemap);
    string geometry = GenshinWeaponExporter.Export(references, family, Path.Combine(args[2], "geometry"), new(false, false, false));
    var geometryReport = JObject.Parse(File.ReadAllText(geometry));
    if ((string)geometryReport["status"] == "Failed" || geometryReport["export"]["errors"].Any()) throw new Exception("Unrequested appearance prevented geometry export");
    string appearance = GenshinWeaponExporter.Export(references, family, Path.Combine(args[2], "appearance"), new(false, false, true));
    if ((string)JObject.Parse(File.ReadAllText(appearance))["status"] != "Failed") throw new Exception("Requested missing appearance was not rejected");
    references.Entries.RemoveAll(e => e.Type == ClassIDType.Mesh);
    string missingMesh = GenshinWeaponExporter.Export(references, family, Path.Combine(args[2], "missing-mesh"), new(false, false, false));
    if ((string)JObject.Parse(File.ReadAllText(missingMesh))["status"] != "Failed") throw new Exception("Missing mesh was not rejected in geometry-only mode");
    Console.WriteLine("PASS real option policy: omitted appearance succeeds; requested missing appearance fails; missing mesh always fails. Unity not tested.");
    return;
}

if (args.Length > 0 && args[0] == "--real-effects")
{
    if (args.Length is < 5 or > 6) throw new ArgumentException("--real-effects <map> <supplemental-map> <request> <new-output> [--no-materials]");
    var entries = new List<AssetEntry>();
    foreach (var mapPath in args.Skip(1).Take(2))
    {
        if (ResourceMap.FromFile(mapPath) < 0) throw new Exception("Invalid effect input map");
        entries.AddRange(ResourceMap.GetEntries());
    }
    var materials = args.Length == 5;
    var report = GenshinWeaponVfxExport.Export(entries.DistinctBy(e => (e.Source, e.Offset, e.PathID, e.Type)), args[3], args[4], materials);
    var result = JObject.Parse(File.ReadAllText(report));
    if ((int)result["requestedActionCount"] != (int)result["accountedActionCount"] || result["failures"].Any() || result["missingSelections"].Any())
        throw new Exception("VFX export has failed/missing selections or unaccounted actions; inspect report.");
    var effectFiles = result["actions"].SelectMany(a => a["effects"]).Select(e => (string)e["effectFile"]).ToArray();
    if (effectFiles.Length == 0 || effectFiles.Any(f => string.IsNullOrEmpty(f) || !File.Exists(Path.Combine(args[4], f))))
        throw new Exception("Structured effect output missing");
    foreach (var dependency in result["actions"].SelectMany(a => a["effects"]).SelectMany(e => e["dependencies"]))
        if (dependency["file"]?.Type == JTokenType.String && !File.Exists(Path.Combine(args[4], (string)dependency["file"])))
            throw new Exception("Reassembled dependency path is broken");
    if (!materials && Directory.GetFiles(args[4], "*", SearchOption.AllDirectories).Any(f =>
        new[] { ".png", ".astexture" }.Contains(Path.GetExtension(f)))) throw new Exception("Data-only VFX wrote textures");
    Console.WriteLine($"PASS real effects: {effectFiles.Length} structured effect records, accounted actions={(int)result["accountedActionCount"]}, materials={materials}; unresolved={result["unresolved"].Count()}; ownership/playback unverified.");
    return;
}

if (args.Length > 0 && args[0] == "--real-batch")
{
    if (args.Length < 4) throw new ArgumentException("--real-batch <map> <new-output> <source-file>...");
    if (File.Exists(args[2]) || Directory.Exists(args[2])) throw new IOException("Choose a new batch fixture output.");
    if (ResourceMap.FromFile(args[1]) < 0 || !ResourceMap.GetGameType().IsGI()) throw new Exception("Expected a Genshin map.");
    var references = GenshinWeaponReferences.Prepare(args[1], ResourceMap.GetEntries().ToArray(), Console.WriteLine,
        sourceFiles: args.Skip(3));
    // Test fixture only: preserve all dependency entries, narrow the catalog to the
    // two researched weapons before exercising the same production batch service.
    references.Catalog.Families.RemoveAll(f => f.SourceToken is not ("Kasabouzu" or "Amenoma"));
    var coveredMesh = references.Catalog.Families.First(f => f.Meshes.Count > 0).Meshes[0];
    // Force a real mesh-only record alongside its source owner to exercise the
    // cross-record coverage path that independent validation shards cannot share.
    references.Catalog.Families.Add(new GenshinWeaponFamily { Key = "Fixture-ExactMeshAlias", Name = "Fixture exact mesh alias",
        SourceToken = "Fixture", WeaponClass = "Sword", SourceName = coveredMesh.Name,
        DiscoveryStatus = "Test fixture: exact source dependency alias", Meshes = new() { coveredMesh } });
    int selectedCount = references.Catalog.Families.Count;
    if (selectedCount < 2) throw new Exception("Expected two representative source families.");
    int groupedAliases = references.Catalog.Families.Sum(f => f.SelectorAliases.Count(k => k != f.Key));
    string report = GenshinWeaponExporter.ExportCatalog(references, args[2], new GenshinWeaponOptions(true, true, true, true), Console.WriteLine);
    var batch = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(report));
    if ((int)batch["total"] != selectedCount || (int)batch["accounted"] != selectedCount)
        throw new Exception("Batch did not account for every selected source record.");
    if (batch["entries"].Any(e => (string)e["status"] == "Failed")) throw new Exception("Real batch has a failed entry; inspect report.");
    int aliases = 0;
    foreach (var entry in batch["entries"])
    {
        var family = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(args[2], (string)entry["manifest"])));
        if (family["export"]?["coveredBy"] is not Newtonsoft.Json.Linq.JArray coverage) continue;
        aliases++;
        foreach (var covered in coverage)
            if (!File.Exists(Path.Combine(args[2], (string)covered["originalReport"]))) throw new Exception("Broken alias report link.");
    }
    if (aliases != 1 || groupedAliases == 0) throw new Exception("Exact mesh coverage or grouped selectors were not consolidated.");
    Console.WriteLine($"PASS real batch: {selectedCount} groups accounted, {groupedAliases} source selector aliases, {aliases} export aliases, zero failed entries. Status={batch["status"]}; Unity not tested.");
    Console.WriteLine(report);
    return;
}

int passed = 0;
void Check(bool ok, string label)
{
    if (!ok) throw new Exception("FAIL " + label);
    Console.WriteLine("PASS " + label);
    passed++;
}
AssetEntry Entry(string name, ClassIDType type, long id, string source = "C:/fixtures/a.blk", long offset = 16) =>
    new() { Name = name, Type = type, Source = source, Offset = offset, PathID = id, Container = "", Hash = "" };

var inputs = new[]
{
    Entry("Equip_Sword_Kasabouzu_Model", ClassIDType.Animator, 10),
    Entry("Equip_Sword_Kasabouzu_Model", ClassIDType.GameObject, 11),
    Entry("Equip_Sword_Kasabouzu_Model", ClassIDType.Mesh, 12),
    Entry("Ani_Equip_Sword_KasabouzuLoop", ClassIDType.AnimationClip, 13),
    Entry("Eff_Ani_Weapon_Kasabouzu", ClassIDType.AnimationClip, 14),
    Entry("Eff_Weapon_Kasabouzu_Hand", ClassIDType.GameObject, 15),
    Entry("Ani_Avatar_Girl_Sword_Kasabouzu_WeaponStandby", ClassIDType.AnimationClip, 16),
    Entry("Eff_Weapon_NotKasabouzu_Hand", ClassIDType.GameObject, 17),
    Entry("Equip_Sword_Amenoma_Model", ClassIDType.Mesh, 18),
    Entry("MonEquip_Sword_Test_Model", ClassIDType.GameObject, 19),
    Entry("Equip_Gun_Test_Model", ClassIDType.GameObject, 20),
    Entry("Equip_Sword_Kasabouzu_Model", ClassIDType.Animator, 10, "C:/fixtures/b.blk"),
    Entry("Equip_Sword_Kasabouzu_Model", ClassIDType.Animator, 10, "C:/fixtures/a.blk", 32)
};
var catalog = GenshinWeaponCatalog.Build(inputs.Concat(new[] { inputs[0] }));
Check(catalog.Families.Count == 6, "qualified roots remain distinct; exact duplicate collapses; unlinked meshes remain visible");
Check(catalog.Families.Count(f => f.Roots.Count == 0) == 2, "name matches alone cannot promote an unlinked mesh to exportable");
Check(catalog.Families.Where(f => f.Roots.Count == 0).Any(f => f.Meshes.Single().PathID == 18), "missing-root source identity retained");
Check(catalog.Families.All(f => f.WeaponClass == "Sword"), "monster weapons and unrelated Equip class excluded");
var root = catalog.Families.First(f => f.Roots.Count > 0);
Check(root.Meshes.Count == 0 && root.MeshCandidates.Any(e => e.PathID == 12), "name match is candidate mesh evidence, not a verified renderer link");
Check(root.AnimationCandidates.Any(e => e.PathID == 13), "native weapon camel-case clip candidate found");
Check(root.AnimationCandidates.All(e => e.PathID != 16), "character wielding clip excluded from native weapon candidates");
Check(root.EffectCandidates.Any(e => e.PathID == 15) && root.EffectCandidates.All(e => e.PathID != 17), "effect token boundaries prevent substring false association");
Check(catalog.Families.Select(f => f.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() == catalog.Families.Count,
    "duplicate source names cannot collide in output keys");
Check(catalog.Families.Select(f => f.Key).SequenceEqual(GenshinWeaponCatalog.Build(inputs.Reverse()).Families.Select(f => f.Key)),
    "catalog identity and ordering independent of map order");
Check(GenshinWeaponCatalog.Build(Array.Empty<AssetEntry>()).Families.Count == 0, "empty map yields empty candidate catalog");
var topologyEntries = new[] { Entry("Equip_Sword_Parent", ClassIDType.GameObject, 100),
    Entry("Equip_Sword_Parent_Model", ClassIDType.GameObject, 101),
    Entry("Equip_Sword_Parent_Model", ClassIDType.Animator, 102),
    Entry("Equip_Sword_Parent_Model", ClassIDType.Mesh, 103) };
AssetsHelper.GenshinWeaponSourceLink Link(long owner, long target, string kind) => new() {
    Source = "C:/fixtures/a.blk", Offset = 16, OwnerFile = "CAB-a", OwnerPathID = owner,
    TargetFile = "CAB-a", TargetPathID = target, Kind = kind };
var topologyFiles = new[] { new AssetsHelper.GenshinWeaponSourceFile { Source = "C:/fixtures/a.blk", Offset = 16, SerializedFile = "CAB-a" } };
var grouped = GenshinWeaponCatalog.Build(topologyEntries, new[] { Link(100, 101, "child"), Link(101, 102, "animator"), Link(101, 103, "mesh") }, topologyFiles);
Check(grouped.Families.Count == 1 && grouped.Families[0].Roots.Single().PathID == 100, "Proven parent hierarchy promotes child selections to enclosing root");
Check(grouped.Families[0].Meshes.Single().PathID == 103 && grouped.Families[0].SelectorAliases.Count == 4, "Source grouping retains converted mesh identity and every former selector");
var ambiguousGroup = GenshinWeaponCatalog.Build(topologyEntries.Concat(new[] { Entry("Equip_Sword_Other", ClassIDType.GameObject, 104) }),
    new[] { Link(100, 103, "mesh"), Link(104, 103, "mesh") }, topologyFiles);
Check(ambiguousGroup.Families.Any(f => f.Roots.Count == 0 && f.Meshes.Any(e => e.PathID == 103)), "Shared mesh does not invent one unique owner");
var cyclic = GenshinWeaponCatalog.Build(topologyEntries.Take(2), new[] { Link(100, 101, "child"), Link(101, 100, "child") }, topologyFiles);
Check(cyclic.Families.Count == 2 && cyclic.Families.All(f => f.DiscoveryStatus.Contains("ambiguous", StringComparison.OrdinalIgnoreCase)), "Cyclic ancestry preserves every source record as ambiguous");
var commonMesh = GenshinWeaponCatalog.Build(topologyEntries, new[] { Link(100, 101, "child"), Link(101, 102, "animator"),
    Link(100, 103, "mesh"), Link(101, 103, "mesh") }, topologyFiles);
Check(commonMesh.Families.Count == 1 && commonMesh.Families[0].Meshes.Single().PathID == 103, "Multiple renderers under one proven root retain one mesh association");
var mutableEntry = Entry("snapshot", ClassIDType.Mesh, 333);
var frozenIndex = new AssetDependencyIndex(new[] { mutableEntry, Entry("other-source", ClassIDType.Mesh, 333, "C:/fixtures/b.blk") });
mutableEntry.Source = "C:/changed.blk";
bool foundCandidate = frozenIndex.TryGet(333, out var primaryCandidate, out var additionalCandidates);
Check(foundCandidate && additionalCandidates.Length == 1 && primaryCandidate.Source == "C:/fixtures/a.blk", "Shared dependency index snapshots qualified candidates without mutable load state");
var containerIndex = new AssetDependencyIndex(Array.Empty<AssetEntry>(), new[] {
    new AssetDependencyIndex.Container("CAB-known", "C:/fixtures/a.blk", 8),
    new AssetDependencyIndex.Container("cab-known", "C:/fixtures/a.blk", 8),
    new AssetDependencyIndex.Container("CAB-conflict", "C:/fixtures/a.blk", 8),
    new AssetDependencyIndex.Container("cab-conflict", "C:/fixtures/b.blk", 8) });
Check(containerIndex.TryGetContainer("cab-known", out var knownContainer) && knownContainer.Offset == 8,
    "Exact serialized-file lookup recovers containers absent from object index");
Check(!containerIndex.TryGetContainer("CAB-conflict", out _) && containerIndex.AmbiguousContainerCount == 1,
    "Conflicting CAB locations cannot silently choose a source");
var clipFixture = (AnimationClip)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(AnimationClip));
BindPoseRegression.Run(Check);
YamlScalarRegression.Run(Check);
clipFixture.m_ClipBindingConstant = new AnimationClipBindingConstant { genericBindings = new List<GenericBinding> {
    new() { path = 11 }, new() { path = 22 } } };
var tos = new Dictionary<uint, string> { [0] = "" };
var addTos = typeof(AnimationClipExtensions).GetMethod("AddTOS", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
bool completedEarly = (bool)addTos.Invoke(null, new object[] { clipFixture, new Dictionary<uint, string> { [11] = "first" }, tos })!;
Check(!completedEarly, "Seeded zero path cannot hide a missing animation binding hash");
bool completed = (bool)addTos.Invoke(null, new object[] { clipFixture, new Dictionary<uint, string> { [22] = "second" }, tos })!;
Check(completed && tos[22] == "second", "Later source hierarchy can fill the remaining animation binding");
var fiveClasses = GenshinWeaponCatalog.Build(new[] { "Sword", "Claymore", "Pole", "Bow", "Catalyst" }
    .Select((name, i) => Entry("Equip_" + name + "_Test_Model", ClassIDType.GameObject, i)));
Check(fiveClasses.Families.Select(f => f.WeaponClass).Distinct().Count() == 5, "all five intended source classes supported");
var hostile = GenshinWeaponCatalog.Build(new[]
{
    Entry("Equip_Bow_../bad:name_Model", ClassIDType.GameObject, 30),
    Entry("Equip_Bow_" + new string('X', 350) + "_Model", ClassIDType.GameObject, 31)
});
Check(hostile.Families.All(f => f.Key.Length <= 100 && f.Key.IndexOfAny(new[] { '/', '\\', ':', '.' }) < 0),
    "source labels cannot escape or create excessively long output segments");

// Fingerprints must invalidate on changed inputs and distinguish bounded scans.
string temp = Path.Combine(Path.GetTempPath(), "AnimeStudioWeaponRegression-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    var request = new JObject { ["subject"] = new JObject { ["kind"] = "weapon" }, ["actions"] = new JArray(
        Enumerable.Range(0, 65).Select(i => new JObject { ["name"] = "action-" + i, ["effects"] = new JArray() })) };
    var partitions = GenshinWeaponVfxExport.Partition(request);
    Check(partitions.Select(p => p["actions"].Count()).SequenceEqual(new[] { 32, 32, 1 }), "65 VFX action groups partition without truncation");
    Check(partitions.SelectMany(p => p["actions"]).Select(a => (string)a["name"]).SequenceEqual(request["actions"].Select(a => (string)a["name"])), "VFX partition preserves action order and subject metadata");
    partitions[0]["subject"]["kind"] = "changed";
    Check((string)request["subject"]["kind"] == "weapon", "VFX partitions do not mutate caller request");
    string canceledRequest = Path.Combine(temp, "effect-request.json");
    File.WriteAllText(canceledRequest, request.ToString());
    string canceledEffects = GenshinWeaponVfxExport.Export(Array.Empty<AssetEntry>(), canceledRequest, Path.Combine(temp, "cancel-effects"), true, new CancellationToken(true));
    var cancelledEffectReport = JObject.Parse(File.ReadAllText(canceledEffects));
    Check((int)cancelledEffectReport["accountedActionCount"] == 65 && (string)cancelledEffectReport["status"] == "Cancelled", "Cancelled VFX batches account for all 65 actions");

    // Character packaging must update preview paths after PNGs move to Textures.
    string characterFolder = Path.Combine(temp, "Avatar_Girl_Catalyst_Fixture");
    Directory.CreateDirectory(Path.Combine(characterFolder, "Textures"));
    File.WriteAllText(Path.Combine(characterFolder, "Textures", "Hair.png"), "preview");
    File.WriteAllText(Path.Combine(characterFolder, "Hair.astexture"), "native");
    File.WriteAllText(Path.Combine(characterFolder, "manifest.json"), new JObject {
        ["sourceClips"] = new JArray(), ["previewMaterials"] = new JArray(),
        ["materialBindings"] = new JArray(new JObject {
            ["textures"] = new JObject { ["_MainTex"] = "Hair.png" },
            ["nativeTextures"] = new JObject { ["_MainTex"] = "Hair.astexture" }
        })
    }.ToString());
    File.WriteAllText(Path.Combine(characterFolder, "Avatar_Girl_Catalyst_Fixture.character.json"),
        new JObject { ["clips"] = new JArray() }.ToString());
    GenshinSharedAssets.Package(characterFolder, "Avatar_Girl_Catalyst_Fixture");
    var packagedManifest = JObject.Parse(File.ReadAllText(Path.Combine(characterFolder, "manifest.json")));
    Check((string)packagedManifest["materialBindings"][0]["textures"]["_MainTex"] == "Textures/Hair.png" &&
        (string)packagedManifest["materialBindings"][0]["nativeTextures"]["_MainTex"] == "Hair.astexture",
        "Character packaging updates preview paths without changing native texture paths");

    // Offline import generation validates the file contract without a Unity Editor.
    string package = Path.Combine(temp, "Weapons", "package"), variant = Path.Combine(package, "Variants", "static");
    Directory.CreateDirectory(variant);
    File.WriteAllText(Path.Combine(variant, "Weapon.fbx"), "fixture");
    File.WriteAllText(Path.Combine(variant, "manifest.json"), new JObject { ["model"] = "Weapon.fbx",
        ["root"] = new JObject { ["Type"] = "GameObject" }, ["meshes"] = new JArray(new JObject { ["bones"] = 0 }),
        ["sourceClips"] = new JArray(), ["materialBindings"] = new JArray() }.ToString());
    var familyReport = new JObject { ["Key"] = "fixture", ["requestedOptions"] = new JObject { ["Materials"] = false },
        ["export"] = new JObject { ["variants"] = new JArray(new JObject { ["stages"] = new JObject {
            ["model"] = new JObject { ["path"] = "Variants/static/Weapon.fbx", ["manifest"] = "Variants/static/manifest.json" } } }) } };
    string familyReportPath = Path.Combine(package, "fixture.weapon.json");
    File.WriteAllText(familyReportPath, familyReport.ToString());
    GenshinWeaponUnityPackage.Write(package, familyReportPath);
    Check(!Directory.GetFiles(package, "*.cs", SearchOption.AllDirectories).Any(), "Offline Unity export contains no generated editor scripts");
    Check(!Directory.Exists(Path.Combine(temp, "Generic")), "Individual export under a Weapons folder cannot escape its output root");
    var recipe = JObject.Parse(File.ReadAllText(Path.Combine(variant, "weapon-import.json")));
    Check((string)recipe["rigMode"] == "Static" && !(bool)recipe["materialsRequested"], "Static/no-material import recipe keeps requested contract");
    var descriptor = JObject.Parse(File.ReadAllText(Path.Combine(variant, GenshinUnityImportDescriptor.FileName)));
    Check((int)descriptor["schemaVersion"] == 1 && (string)descriptor["kind"] == "genshin-weapon" &&
        (string)descriptor["recipe"] == "weapon-import.json" && (string)descriptor["model"] == "Weapon.fbx",
        "Weapon descriptor points at its same-folder model and rich recipe");
    GenshinWeaponUnityPackage.Write(package, familyReportPath, temp);
    Check(!Directory.GetFiles(temp, "*.cs", SearchOption.AllDirectories).Any(), "Batch output contains no generated editor scripts");
    foreach (string kind in new[] { "genshin-model", "genshin-character", "genshin-weapon" })
    {
        GenshinUnityImportDescriptor.Write(variant, kind, "manifest.json", "Weapon.fbx");
        var selected = JObject.Parse(File.ReadAllText(Path.Combine(variant, GenshinUnityImportDescriptor.FileName)));
        Check((string)selected["kind"] == kind && (string)selected["recipe"] == "manifest.json",
            "Descriptor supports " + kind + " without generating code");
    }
    bool escapedDescriptorRejected = false;
    try { GenshinUnityImportDescriptor.Write(variant, "genshin-model", "../manifest.json", "Weapon.fbx"); }
    catch (InvalidDataException) { escapedDescriptorRejected = true; }
    Check(escapedDescriptorRejected, "Descriptor rejects recipe traversal");
    familyReport["export"]["variants"][0]["stages"]["model"]["path"] = "../escape.fbx";
    File.WriteAllText(familyReportPath, familyReport.ToString());
    bool traversalRejected = false;
    try { GenshinWeaponUnityPackage.Write(package, familyReportPath); }
    catch (InvalidDataException) { traversalRejected = true; }
    Check(traversalRejected, "Offline importer packaging rejects model path traversal");

    string map = Path.Combine(temp, "map.bin"), source = Path.Combine(temp, "source.blk");
    File.WriteAllText(map, "map-v1"); File.WriteAllText(source, "source-v1");
    string Baseline(string version = "7.1", string scope = "all-map-sources") =>
        GenshinWeaponReferences.ComputeFingerprint(map, new[] { source }, version, scope);
    string initial = Baseline();
    Check(initial == Baseline(), "unchanged reference inputs reuse fingerprint");
    Check(initial != Baseline(scope: "selected-source-files"), "bounded and full discovery cannot share cache identity");
    Check(initial != Baseline(version: "7.2"), "game version invalidates reference cache");
    string beforeSameStamp = GenshinWeaponReferences.ComputeFingerprint(map, new[] { source }, "7.1", hashContents: true);
    DateTime originalStamp = File.GetLastWriteTimeUtc(source);
    File.WriteAllText(source, "source-v2");
    File.SetLastWriteTimeUtc(source, originalStamp);
    Check(beforeSameStamp != GenshinWeaponReferences.ComputeFingerprint(map, new[] { source }, "7.1", hashContents: true), "Content verification detects same-size same-timestamp source changes");
    File.AppendAllText(map, "changed");
    Check(initial != Baseline(), "map bytes invalidate reference cache");
    string afterMap = Baseline();
    File.AppendAllText(source, "changed");
    Check(afterMap != Baseline(), "source file changes invalidate reference cache");
    bool missingRejected = false;
    try { GenshinWeaponReferences.ComputeFingerprint(map, new[] { Path.Combine(temp, "absent.blk") }, "7.1"); }
    catch (FileNotFoundException) { missingRejected = true; }
    Check(missingRejected, "missing source cannot produce a ready reference fingerprint");
    bool cancelled = false;
    try { GenshinWeaponReferences.Prepare(map, new[] { inputs[0] }, cancellation: new CancellationToken(true)); }
    catch (OperationCanceledException) { cancelled = true; }
    Check(cancelled, "pre-cancelled preparation performs no client scan");

    // A synthetic catalog with no proven roots exercises the ledger/failure path
    // without loading game bundles or requiring Unity.
    var failedCatalog = GenshinWeaponCatalog.Build(new[]
    {
        Entry("Equip_Bow_Unlinked_Model", ClassIDType.Mesh, 900, source),
        Entry("Equip_Sword_Unlinked_Model", ClassIDType.Mesh, 901, source)
    });
    var refs = new GenshinWeaponReferences();
    void Set(string property, object value) => typeof(GenshinWeaponReferences).GetProperty(property).SetValue(refs, value);
    Set("Catalog", failedCatalog); Set("Entries", new List<AssetEntry>());
    Set("Fingerprint", "synthetic-regression"); Set("GameVersion", "fixture");
    Set("ScanScope", "fixture"); Set("ScanCompleteness", "fixture"); Set("Sources", new[] { source });
    var options = new GenshinWeaponOptions(false, false, false);
    string batch = Path.Combine(temp, "batch");
    var ledger = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(GenshinWeaponExporter.ExportCatalog(refs, batch, options)));
    Check((int)ledger["total"] == 2 && (int)ledger["accounted"] == 2, "batch accounts for every failed source record");
    Check(ledger["entries"].All(e => (string)e["status"] == "Failed"), "missing model roots cannot become successful exports");
    Check((string)ledger["status"] == "Failed", "all-failed catalog has failed aggregate status");
    var cancelledLedger = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(GenshinWeaponExporter.ExportCatalog(refs,
        Path.Combine(temp, "cancelled"), options, cancellation: new CancellationToken(true))));
    Check((int)cancelledLedger["accounted"] == 2 && cancelledLedger["entries"].All(e => (string)e["status"] == "Cancelled"),
        "batch cancellation explicitly accounts for all unstarted entries");
    bool existingRejected = false;
    File.WriteAllText(Path.Combine(batch, "preserve.txt"), "user data");
    try { GenshinWeaponExporter.ExportCatalog(refs, batch, options); }
    catch (IOException) { existingRejected = true; }
    Check(existingRejected && File.ReadAllText(Path.Combine(batch, "preserve.txt")) == "user data", "existing destinations and user files are preserved");
}
finally
{
    // Every output belongs to this uniquely created test directory. Remove files
    // and then empty directories; do not follow arbitrary supplied output paths.
    foreach (string file in Directory.GetFiles(temp, "*", SearchOption.AllDirectories)) File.Delete(file);
    foreach (string directory in Directory.GetDirectories(temp, "*", SearchOption.AllDirectories).OrderByDescending(p => p.Length))
        Directory.Delete(directory);
    Directory.Delete(temp);
}
Console.WriteLine($"Weapon export regression: {passed}/{passed} passed.");
