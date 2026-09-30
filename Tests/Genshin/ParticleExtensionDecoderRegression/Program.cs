using AnimeStudio;
using Newtonsoft.Json.Linq;
using System.Security.Cryptography;

if (args.Length == 0) throw new ArgumentException("Pass one or more existing Mona Idle/InFloor effect JSON files.");
int particles = 0, controls = 0;
void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
void Reject(byte[] bytes, int offset, string label)
{
    var changed = (byte[])bytes.Clone(); changed[offset] = 2;
    try { GenshinParticleSystemDecoder.Decode(changed); }
    catch (InvalidDataException) { controls++; return; }
    throw new Exception("Accepted invalid bool: " + label);
}
foreach (string effectPath in args)
{
    var effect = JObject.Parse(File.ReadAllText(effectPath));
    foreach (var obj in effect["objects"])
    foreach (var component in obj["components"].Where(c => (string)c["type"] == "ParticleSystem"))
    {
        string name = (string)obj["Name"];
        byte[] bytes = File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(effectPath)!, (string)component["raw"]["assetPath"]));
        Assert(Convert.ToHexString(SHA256.HashData(bytes)) == (string)component["raw"]["sha256"], name + " source hash");
        var decoded = GenshinParticleSystemDecoder.Decode(bytes);
        var shape = decoded["ShapeModule"]; var emission = decoded["EmissionModule"];
        Assert((bool)shape["m_UseMeshScale"] && !(bool)shape["alignToDirection"], name + " corrected packed bools");
        Assert((float)shape["m_AABBSlice"]["x"] == 1 && (int)shape["m_MeshSpawn"]["mode"] == 0, name + " Shape extension");
        Assert((bool)decoded["useOptPrewarm"] && (bool)decoded["useCullingUpdate"] && (float)decoded["cullingUpdateDistance"] == 20, name + " header");
        Assert(!(bool)emission["m_PlacingOnGround"] && (bool)emission["m_KeepVisible"] &&
               (float)emission["m_DetectLen"] == 6 && (float)emission["m_YDelta"] == 3 &&
               (string)emission["m_LayerMask"] == "0" && (float)emission["m_EmissionLevel"]["x"] == .4f &&
               (float)emission["m_EmissionLevel"]["w"] == 1 && (float)emission["m_EmissionFalloffStart"] == 50 &&
               (float)emission["m_EmissionFalloffEnd"] == 150 && (bool)emission["m_EnableFallOff"] &&
               !(bool)emission["m_EnableSpecifyPostion"], name + " emission extension");
        Assert(!(bool)decoded["TextModule"]["enabled"] && (bool)decoded["ColorOverDayModule"]["enabled"], name + " tail activation");
        Assert((int)decoded["ColorOverDayModule"]["gradient"]["minMaxState"] == 1, name + " ColorOverDay gradient");
        Assert((int)decoded["sourceByteRanges"]["TextModule"][1] == bytes.Length-376 &&
               (int)decoded["sourceByteRanges"]["ColorOverDayModule"][1] == bytes.Length, name + " tail accounting");
        // Independent booleans must not alias: change each on its own while all
        // downstream curves/modules and the real align flag stay unchanged.
        var changed = (byte[])bytes.Clone(); changed[1018] = 0;
        var noMeshScale = GenshinParticleSystemDecoder.Decode(changed);
        Assert(!(bool)noMeshScale["ShapeModule"]["m_UseMeshScale"] && !(bool)noMeshScale["ShapeModule"]["alignToDirection"], name + " mesh bool isolation");
        Assert(JToken.DeepEquals(noMeshScale["EmissionModule"], emission), name + " changed downstream after mesh bool");
        changed = (byte[])bytes.Clone(); changed[1019] = 1;
        var withAlign = GenshinParticleSystemDecoder.Decode(changed);
        Assert((bool)withAlign["ShapeModule"]["m_UseMeshScale"] && (bool)withAlign["ShapeModule"]["alignToDirection"], name + " align bool isolation");
        Assert(JToken.DeepEquals(withAlign["ColorOverDayModule"], decoded["ColorOverDayModule"]), name + " changed tail after align bool");
        controls += 2;
        foreach (int offset in new[] {32,104,1018,1019,bytes.Length-440,bytes.Length-376}) Reject(bytes, offset, name);
        int extensionStart = (int)decoded["sourceByteRanges"]["EmissionModule"][1]-52;
        foreach (int relative in new[] {0,1,44,48}) Reject(bytes, extensionStart+relative, name);
        particles++;
        Console.WriteLine($"PASS {name}: {bytes.Length} bytes; all module boundaries, native field joins, isolated booleans and malformed bool controls");
    }
}
Console.WriteLine($"PASS particles={particles}, mutationControls={controls}");
