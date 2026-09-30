using AnimeStudio;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Runtime.CompilerServices;
using System.Text;

if (args.FirstOrDefault() == "--animations") return AnimationSmoke.Run(args);
if (args.FirstOrDefault() == "--shared-character") return SharedCharacterSmoke.Run(args);
if (args.FirstOrDefault() == "--character-source") return CharacterSourceCheck.Run(args);
if (args.FirstOrDefault() == "--humanoid-rotations") return HumanoidChecks.Reference(args);
if (args.Length != 0) return MaterialSmoke.Run(args);
int checks = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new Exception(description);
    checks++;
    Console.WriteLine($"PASS {description}");
}
byte[] Footer(bool sentinel = true, uint compressedSize = 8, string editor = "MoleMole.SyntheticShaderEditor")
{
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream);
    void Text(string value) { var bytes = Encoding.ASCII.GetBytes(value); writer.Write(bytes.Length); writer.Write(bytes); while (stream.Position % 4 != 0) writer.Write((byte)0); }
    Text("miHoYo/Character/Synthetic"); Text(editor); Text("");
    writer.Write(0); writer.Write(0); // dependency count, aligned boolean
    writer.Write(1); writer.Write(4); // one platform: D3D11
    writer.Write(1); writer.Write(0); // offset
    writer.Write(1); writer.Write(compressedSize);
    writer.Write(1); writer.Write(8); // decompressed length
    if (sentinel) { writer.Write(4); writer.Write(uint.MaxValue); }
    writer.Write(8); writer.Write(new byte[8]);
    return stream.ToArray();
}
var valid = Footer();
CharacterWorkflowChecks.Run(Check);
ExportPerformanceChecks.Run(Check);
SharedAssetChecks.Run(Check);
BindPoseChecks.Run(Check);
Check(GenshinShaderNameReader.TryRead(Footer(editor: "MoleMole.RevertableEditor")) != null, "observed FX revertable editor footer");
Check(GenshinShaderNameReader.TryRead(Footer(editor: "MiHoYoASEMaterialInspector")) != null, "observed FX ASE editor footer");
Check(GenshinShaderNameReader.TryRead(Footer(editor: "MoleMole.ASECharacterShaderEditorBase")) != null, "observed character base editor footer");
Check(GenshinShaderNameReader.TryRead(Footer(editor: "MiHoYoParticleCommonEditor")) != null, "observed particle common editor footer");
Check(GenshinShaderNameReader.TryRead(Footer(editor: "Unknown.MaterialInspector")) == null, "unknown editor rejected");
Check(GenshinShaderNameReader.TryRead(valid) == "miHoYo/Character/Synthetic", "sentinel blob footer");
Check(GenshinShaderNameReader.TryRead(Footer(false)) == "miHoYo/Character/Synthetic", "ordinary blob footer");
Check(GenshinShaderNameReader.TryRead(Footer(compressedSize: 9)) == null, "out-of-bounds blob table rejected");
Check(GenshinShaderNameReader.TryRead(Encoding.ASCII.GetBytes("miHoYo/Character/FalsePositive")) == null, "unstructured name rejected");
Check(GenshinShaderNameReader.TryRead(valid.Concat(valid).ToArray()) == null, "ambiguous records rejected");
for (int size = 0; size < valid.Length; size++)
    Check(GenshinShaderNameReader.TryRead(valid[..size]) == null, $"truncation at {size}");
T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
var manager = new AssetsManager();
var source = Empty<SerializedFile>();
source.assetsManager = manager; source.fileName = "source"; source.ObjectsDic = new();
source.m_Externals = new() { new FileIdentifier { fileName = "shader-file" } };
source.game = GameManager.GetGameByType(GameType.GI);
manager.assetsFileList.Add(source);
var pointer = new PPtr<Shader>(1, 42, source);
Check(!pointer.TryGet(out _), "missing dependency initially unresolved");
Check(!pointer.TryGet(out _), "repeated missing lookup remains unresolved");
var target = Empty<SerializedFile>();
target.assetsManager = manager; target.fileName = "SHADER-FILE"; target.ObjectsDic = new();
var shader = Empty<Shader>(); shader.m_Name = "Synthetic/Resolved"; shader.m_PathID = 42; shader.assetsFile = target;
target.ObjectsDic.Add(42, shader); manager.assetsFileList.Add(target);
Check(pointer.TryGet(out var found) && found == shader, "late dependency load retries cached miss case-insensitively");
Check(!new PPtr<Shader>(0, 42, null).TryGet(out _), "unbound pointer safely unresolved");
Check(!new PPtr<Shader>(-1, 42, source).TryGet(out _), "null file pointer safely unresolved");
var mat = Empty<Material>(); mat.assetsFile = source; mat.m_Name = "Synthetic material"; mat.m_Shader = pointer;
JObject MaterialJson() => JObject.Parse(MaterialJsonExporter.Serialize(mat, new JsonSerializerSettings()));
var json = MaterialJson();
Check((string)json["m_Shader"]["Name"] == "Synthetic/Resolved", "material preserves resolved pointer name");
Check((string)json["ShaderReference"]["Status"] == "Resolved", "resolved status");
Check((string)json["ShaderReference"]["SerializedFile"] == "shader-file", "qualified serialized-file identity");
Check((string)json["ShaderReference"]["PathID"] == "42", "lossless textual path ID");
shader.m_Name = "";
Check((string)MaterialJson()["ShaderReference"]["Status"] == "Unnamed", "loaded unnamed shader distinguished");
mat.m_Shader = new PPtr<Shader>(1, 43, source);
Check((string)MaterialJson()["ShaderReference"]["Status"] == "Unresolved", "missing object distinguished");
mat.m_Shader = new PPtr<Shader>(0, 0, source);
Check((string)MaterialJson()["ShaderReference"]["Status"] == "Null", "null shader distinguished");
var bulkMethod = typeof(ACLLibs.DBACL).GetMethod("GenshinBulkOffset", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
var database = new byte[100];
var standaloneMethod = typeof(ACLLibs.DBACL).GetMethod("ValidateStandaloneGenshinTracks", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
var standalone = new byte[64];
for(int offset=0;offset<64;offset+=32) {
    BitConverter.GetBytes(32u).CopyTo(standalone,offset);
    BitConverter.GetBytes(0xAC11AC11u).CopyTo(standalone,offset+8);
}
standalone[15]=12;
standaloneMethod.Invoke(null,new object[]{standalone});
Check(new GIACLClip {m_ClipData=standalone}.IsSet,"standalone Genshin ACL tracks are decoded without a database");
foreach(int offset in new[]{0,32}) {
    var missingDatabase=(byte[])standalone.Clone(); missingDatabase[offset+29]=1;
    bool rejected=false;
    try {standaloneMethod.Invoke(null,new object[]{missingDatabase});}
    catch(System.Reflection.TargetInvocationException e) when(e.InnerException is InvalidDataException) {rejected=true;}
    Check(rejected,"database-dependent tracks are rejected before native standalone decoding");
}
void U32(int at, uint value) => BitConverter.GetBytes(value).CopyTo(database, at);
U32(0, 88); U32(8, 0xAC11DB01); database[12] = 100;
U32(40, 3); U32(44, 8);
Check((int)bulkMethod.Invoke(null, new object[] { database })! == 88, "ACL bulk tiers start after the database header, including medium-tier padding");
foreach (var malformed in new[] { database[..99], database[..63], new byte[100] })
{
    bool rejected = false;
    try { bulkMethod.Invoke(null, new object[] { malformed }); }
    catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is InvalidDataException) { rejected = true; }
    Check(rejected, "truncated or invalid ACL database rejected before native decoding");
}
mat.m_Shader = pointer; shader.m_Name = "Synthetic/Resolved";
mat.m_SavedProperties = Empty<UnityPropertySheet>(); mat.m_SavedProperties.m_TexEnvs = new();
var oldFilter = manager.FilterData;
var oldResolve = manager.ResolveDependencies;
var resolved = new AssetDependencyResolver(manager, Array.Empty<AssetEntry>()).Resolve(new[] { mat });
Check(resolved.Objects.Contains(shader) && resolved.Missing.Count == 0, "dependency graph follows the qualified shader reference");
Check(ReferenceEquals(manager.FilterData, oldFilter) && manager.ResolveDependencies == oldResolve, "dependency resolver restores loading settings");
var cancelled = new CancellationToken(true);
try { new AssetDependencyResolver(manager, Array.Empty<AssetEntry>()).Resolve(new[] { mat }, cancelled); }
catch (OperationCanceledException) { }
Check(ReferenceEquals(manager.FilterData, oldFilter) && manager.ResolveDependencies == oldResolve, "dependency resolver restores settings after cancellation");
WwiseChecks.Run(Check);
AudioExportChecks.Run(Check);
AssemblyChecks.Run(Check);
HumanoidChecks.Run(Check);
Console.WriteLine($"{checks} checks passed.");

return 0;
