// Read-only search for direct native field uses in the installed game's code.
// A matching displacement is a candidate, not proof of the containing type.
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using Iced.Intel;

if (args.Length < 3 || args[1] is not ("--field" or "--constant" or "--string" or "--emission-cluster" or "--falloff-pairs" or "--read-va" or "--rip-refs"))
    throw new ArgumentException("ParticleDayColorProbe <exe> (--field|--constant|--string|--read-va|--rip-refs) <value> | <exe> --emission-cluster <start-file-decimal> <end-file-decimal>");

byte[] image = File.ReadAllBytes(args[0]);
using var pe = new PEReader(new MemoryStream(image));
var headers = pe.PEHeaders;
ulong imageBase = headers.PEHeader!.ImageBase;
ulong searchValue = args[1] is "--string" or "--emission-cluster" or "--falloff-pairs" ? 0 : Convert.ToUInt64(args[2], 16);
var text = headers.SectionHeaders.Single(section => section.Name == ".text");
int textStart = text.PointerToRawData;
int textLength = text.SizeOfRawData;
ulong textVa = imageBase + (uint)text.VirtualAddress;

int FileOffset(ulong va)
{
    if (va < imageBase || va - imageBase > int.MaxValue) return -1;
    int rva = (int)(va - imageBase);
    foreach (var section in headers.SectionHeaders)
        if (rva >= section.VirtualAddress && rva < section.VirtualAddress + section.SizeOfRawData)
            return rva - section.VirtualAddress + section.PointerToRawData;
    return -1;
}

if (args[1] == "--string")
{
    byte[] needle = System.Text.Encoding.ASCII.GetBytes(args[2]);
    Console.WriteLine($"sha256={Convert.ToHexString(SHA256.HashData(image))} text={args[2]}");
    foreach (var section in headers.SectionHeaders)
    {
        int begin = section.PointerToRawData;
        int end = Math.Min(image.Length, begin + section.SizeOfRawData);
        for (int at = begin; at + needle.Length < end; at++)
        {
            if (!image.AsSpan(at, needle.Length).SequenceEqual(needle)) continue;
            int length = 0;
            while (at + length < end && length < 120 && image[at + length] is >= 32 and <= 126)
                length++;
            ulong va = imageBase + (uint)(section.VirtualAddress + at - begin);
            Console.WriteLine($"file={at} va={va:X} section={section.Name} text={System.Text.Encoding.ASCII.GetString(image, at, length)}");
        }
    }
    return;
}

if (args[1] == "--read-va")
{
    int offset = FileOffset(searchValue);
    int length = args.Length > 3 ? int.Parse(args[3]) : 16;
    if (offset < 0 || offset + length > image.Length || length < 1 || length > 128)
        throw new ArgumentOutOfRangeException(nameof(searchValue), "VA does not map to bounded PE bytes");
    Console.WriteLine($"sha256={Convert.ToHexString(SHA256.HashData(image))} va={searchValue:X} file={offset} bytes={Convert.ToHexString(image.AsSpan(offset, length))}");
    for (int at = offset; at + 4 <= offset + length; at += 4)
        Console.WriteLine($"+{at - offset:X}: uint={BitConverter.ToUInt32(image, at)} float={BitConverter.ToSingle(image, at):R}");
    return;
}

var exception = headers.PEHeader.ExceptionTableDirectory;
int exceptionStart = FileOffset(imageBase + (uint)exception.RelativeVirtualAddress);
var functions = new List<(ulong start, ulong end)>();
for (int at = exceptionStart; at >= 0 && at + 12 <= exceptionStart + exception.Size; at += 12)
    functions.Add((imageBase + BitConverter.ToUInt32(image, at), imageBase + BitConverter.ToUInt32(image, at + 4)));
functions.Sort((left, right) => left.start.CompareTo(right.start));

(ulong start, ulong end) FunctionAt(ulong va)
{
    int lo = 0, hi = functions.Count - 1;
    while (lo <= hi)
    {
        int middle = lo + ((hi - lo) / 2);
        var function = functions[middle];
        if (va < function.start) hi = middle - 1;
        else if (va >= function.end) lo = middle + 1;
        else return function;
    }
    return (0, 0);
}

Console.WriteLine($"sha256={Convert.ToHexString(SHA256.HashData(image))} {args[1]}=0x{searchValue:X}");
bool pairMode = args[1] == "--falloff-pairs";
bool cluster = args[1] == "--emission-cluster" || pairMode;
int clusterStart = cluster ? int.Parse(args[2]) : 0;
int clusterEnd = cluster ? int.Parse(args[3]) : image.Length;
var emissionFields = new HashSet<ulong> { 0x1D8, 0x1D9, 0x1DC, 0x1E0, 0x1E8, 0x1F0, 0x1F4, 0x1F8, 0x1FC, 0x200, 0x204, 0x208, 0x20C };
var clusters = new Dictionary<(ulong start, ulong end), List<(ulong field, string line)>>();
var reader = new ByteArrayCodeReader(image, textStart, textLength);
var decoder = Decoder.Create(64, reader);
decoder.IP = textVa;
var formatter = new IntelFormatter();
var output = new TextOutput();
int matches = 0;
while (reader.CanReadByte)
{
    decoder.Decode(out var instruction);
    output.Builder.Clear();
    formatter.Format(in instruction, output);
    if (args[1] == "--rip-refs")
    {
        if (!instruction.IsIPRelativeMemoryOperand || instruction.IPRelativeMemoryAddress != searchValue) continue;
        Console.WriteLine($"file={FileOffset(instruction.IP)} va={instruction.IP:X} {output.Builder}");
        matches++;
        continue;
    }
    if (cluster)
    {
        int file = FileOffset(instruction.IP);
        ulong displacement = instruction.MemoryDisplacement64;
        if (file < clusterStart || file >= clusterEnd
            || instruction.MemoryBase is Register.RIP or Register.None or Register.RSP or Register.RBP
            || !emissionFields.Contains(displacement)) continue;
        var candidateFunction = FunctionAt(instruction.IP);
        if (candidateFunction.start == 0) continue;
        if (!clusters.TryGetValue(candidateFunction, out var lines))
            clusters[candidateFunction] = lines = new();
        lines.Add((displacement, $"  file={file} va={instruction.IP:X} field=0x{displacement:X} {output.Builder}"));
        continue;
    }
    bool match = args[1] == "--field"
        ? instruction.MemoryBase is not (Register.RIP or Register.None)
            && instruction.MemoryDisplacement64 == searchValue
        : output.Builder.ToString().Contains(searchValue.ToString("X") + "h", StringComparison.OrdinalIgnoreCase);
    if (!match) continue;
    var function = FunctionAt(instruction.IP);
    Console.WriteLine($"file={FileOffset(instruction.IP)} va={instruction.IP:X} "
        + $"function={FileOffset(function.start)}..{FileOffset(function.end)} {output.Builder}");
    matches++;
}
if (cluster)
{
    foreach (var (function, lines) in clusters.OrderBy(item => item.Key.start))
    {
        int distinct = lines.Select(line => line.field).Distinct().Count();
        if (pairMode ? !lines.Any(line => line.field == 0x204) || !lines.Any(line => line.field == 0x208) : distinct < 3) continue;
        Console.WriteLine($"function={FileOffset(function.start)}..{FileOffset(function.end)} fields={distinct} refs={lines.Count}");
        foreach (var line in lines) Console.WriteLine(line.line);
    }
    return;
}
Console.WriteLine($"matches={matches}");

sealed class TextOutput : FormatterOutput
{
    public System.Text.StringBuilder Builder { get; } = new();
    public override void Write(string text, FormatterTextKind kind) => Builder.Append(text);
}
