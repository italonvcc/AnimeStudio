using System.Buffers.Binary;
using System.Text;
using AnimeStudio;

const string name = "miHoYo/Character/Character_Ugc";
var cases = new (string Label, byte[] Bytes, string? Expected)[]
{
    ("tagged serialized name", Tagged(name), name),
    ("ambiguous tagged names", DoubleTagged(), null),
    ("wrong tag", Tagged(name, tag: "RenderTypo"), null),
    ("wrong header", Tagged(name, headerValue: 9), null),
    ("truncated string", Tagged(name, truncate: true), null),
    ("excessive name length", Tagged(name, lengthOverride: 257), null),
    ("legacy parsed footer", Footer(name), name)
};
foreach (var test in cases)
{
    string? actual = GenshinShaderNameReader.TryRead(test.Bytes);
    if (actual != test.Expected)
        throw new Exception($"{test.Label}: expected '{test.Expected ?? "<null>"}', got '{actual ?? "<null>"}'");
    Console.WriteLine($"PASS {test.Label}");
}

static byte[] Tagged(string shaderName, string tag = "RenderType", uint headerValue = 2,
    bool truncate = false, int? lengthOverride = null)
{
    var bytes = new byte[256];
    const int start = 64;
    PutUInt(bytes, start - 36, 10);
    Encoding.ASCII.GetBytes(tag, bytes.AsSpan(start - 32));
    PutUInt(bytes, start - 20, 6);
    Encoding.ASCII.GetBytes("Opaque", bytes.AsSpan(start - 16));
    PutUInt(bytes, start - 8, 0);
    PutUInt(bytes, start - 4, (uint)(lengthOverride ?? shaderName.Length));
    Encoding.ASCII.GetBytes(shaderName, bytes.AsSpan(start));
    int after = (start + shaderName.Length + 3) & ~3;
    PutUInt(bytes, after + 16, headerValue);
    if (truncate) Array.Resize(ref bytes, start + shaderName.Length - 1);
    return bytes;
}

static byte[] DoubleTagged()
{
    var first = Tagged("miHoYo/Character/Character_Ugc");
    var second = Tagged("miHoYo/Character/Character_Ugc_Face");
    var combined = new byte[600];
    first.CopyTo(combined, 0);
    second.CopyTo(combined, 320);
    return combined;
}

static byte[] Footer(string shaderName)
{
    var bytes = new byte[512];
    int at = 4;
    WriteString(bytes, ref at, shaderName);
    WriteString(bytes, ref at, "MoleMole.RevertableEditor");
    WriteString(bytes, ref at, "");
    PutUInt(bytes, at, 0); at += 4; // no dependencies
    PutUInt(bytes, at, 1); at += 4; // valid boolean
    PutUInt(bytes, at, 1); at += 4; // one platform
    PutUInt(bytes, at, 4); at += 4; // D3D11
    foreach (uint value in new uint[] { 0, 4, 4 })
    {
        PutUInt(bytes, at, 1); at += 4;
        PutUInt(bytes, at, value); at += 4;
    }
    PutUInt(bytes, at, 4); at += 4;
    PutUInt(bytes, at, 123);
    return bytes;
}

static void WriteString(byte[] bytes, ref int at, string value)
{
    PutUInt(bytes, at, (uint)value.Length);
    at += 4;
    Encoding.ASCII.GetBytes(value, bytes.AsSpan(at));
    at = (at + value.Length + 3) & ~3;
}

static void PutUInt(byte[] bytes, int at, uint value) =>
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at, 4), value);
