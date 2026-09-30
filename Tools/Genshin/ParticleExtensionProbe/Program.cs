// Read-only native-code evidence helper. File offsets are never serialized-field offsets.
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using Iced.Intel;

if (args.Length < 3) throw new ArgumentException("ParticleExtensionProbe <exe> <file-offset> <byte-count>");
byte[] image = File.ReadAllBytes(args[0]);
using var pe = new PEReader(new MemoryStream(image));
var headers = pe.PEHeaders;
ulong imageBase = headers.PEHeader!.ImageBase;
bool memoryFilter = args[1] == "--memory";
int start = args[1] == "--refs" ? headers.SectionHeaders.Single(s => s.Name == ".text").PointerToRawData : int.Parse(args[memoryFilter ? 3 : 1]);
int count = args[1] == "--refs" ? headers.SectionHeaders.Single(s => s.Name == ".text").SizeOfRawData : int.Parse(args[memoryFilter ? 4 : 2]);
var section = headers.SectionHeaders.Single(s => start >= s.PointerToRawData && start < s.PointerToRawData + s.SizeOfRawData);
ulong startVa = imageBase + (uint)(start - section.PointerToRawData + section.VirtualAddress);
int FileOffset(ulong va)
{
    if (va < imageBase || va - imageBase > int.MaxValue) return -1;
    int rva = (int)(va - imageBase);
    foreach (var s in headers.SectionHeaders)
        if (rva >= s.VirtualAddress && rva < s.VirtualAddress + s.SizeOfRawData)
            return rva - s.VirtualAddress + s.PointerToRawData;
    return -1;
}
Console.WriteLine($"sha256={Convert.ToHexString(SHA256.HashData(image))} imageBase={imageBase:X} fileStart={start} count={count}");
if (args[1] == "--refs")
{
    ulong target = Convert.ToUInt64(args[2], 16);
    // Candidate byte scan is followed by a real instruction decode. Callers must
    // still verify each hit belongs to code, not an embedded jump-table entry.
    for (int at = start; at + 7 <= start + count; at++)
    {
        ulong va = startVa + (uint)(at - start);
        bool call = image[at] == 0xE8 && (ulong)((long)va + 5 + BitConverter.ToInt32(image, at + 1)) == target;
        bool rip = image[at] is 0x48 or 0x4C && (image[at + 2] & 0xC7) == 0x05 &&
            (ulong)((long)va + 7 + BitConverter.ToInt32(image, at + 3)) == target;
        if (!call && !rip) continue;
        var d = Iced.Intel.Decoder.Create(64, new ByteArrayCodeReader(image, at, 15)); d.IP = va;
        d.Decode(out var ins);
        if (!(ins.IsIPRelativeMemoryOperand && ins.IPRelativeMemoryAddress == target) &&
            !(ins.FlowControl == FlowControl.Call && ins.NearBranch64 == target)) continue;
        var o = new TextOutput(); new IntelFormatter().Format(in ins, o);
        Console.WriteLine($"candidateFile={at} va={va:X} {o.Builder}");
    }
    return;
}
var exceptionDirectory = headers.PEHeader.ExceptionTableDirectory;
int exceptionOffset = FileOffset(imageBase + (uint)exceptionDirectory.RelativeVirtualAddress);
for (int i = exceptionOffset; i >= 0 && i + 12 <= exceptionOffset + exceptionDirectory.Size; i += 12)
{
    ulong a = imageBase + BitConverter.ToUInt32(image, i), b = imageBase + BitConverter.ToUInt32(image, i + 4);
    if (startVa >= a && startVa < b)
        Console.WriteLine($"runtimeFunction file={FileOffset(a)}..{FileOffset(b)} va={a:X}..{b:X}");
}
var reader = new ByteArrayCodeReader(image, start, count);
var decoder = Iced.Intel.Decoder.Create(64, reader);
decoder.IP = startVa;
var formatter = new IntelFormatter();
var output = new TextOutput();
while (reader.CanReadByte)
{
    decoder.Decode(out var instruction);
    if (memoryFilter && (instruction.MemoryDisplacement64 != Convert.ToUInt64(args[2], 16) ||
        instruction.MemoryBase is Register.None or Register.RIP or Register.RSP or Register.RBP)) continue;
    output.Builder.Clear(); formatter.Format(in instruction, output);
    string annotation = "";
    if (instruction.IsIPRelativeMemoryOperand)
    {
        int offset = FileOffset(instruction.IPRelativeMemoryAddress);
        if (offset >= 0)
        {
            int n = 0;
            while (n < 180 && offset + n < image.Length && image[offset + n] is >= 32 and <= 126) n++;
            if (n >= 3 && offset + n < image.Length && image[offset + n] == 0)
                annotation = $" ; string@{offset}={Encoding.ASCII.GetString(image, offset, n)}";
        }
    }
    if (instruction.FlowControl == FlowControl.Call && instruction.Op0Kind == OpKind.NearBranch64)
        annotation += $" ; callFile={FileOffset(instruction.NearBranch64)}";
    Console.WriteLine($"{FileOffset(instruction.IP),10} {instruction.IP:X} {output.Builder}{annotation}");
}
sealed class TextOutput : FormatterOutput
{
    public StringBuilder Builder { get; } = new();
    public override void Write(string text, FormatterTextKind kind) => Builder.Append(text);
}
