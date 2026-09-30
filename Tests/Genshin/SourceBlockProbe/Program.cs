using AnimeStudio;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

if (args.Length != 2 || (args[1] is not ("--particles" or "--renderers" or "--scan-type-hashes" or "--pe-debug" or "--pe-sections" or "--pe-string-xrefs" or "--pe-renderer-bindings" or "--pe-particle-bindings") && !long.TryParse(args[1], out _)))
    throw new ArgumentException("Usage: SourceBlockProbe <source block> <GameObject PathID|--particles|--renderers> or <player executable> <--scan-type-hashes|--pe-debug|--pe-sections|--pe-string-xrefs|--pe-renderer-bindings|--pe-particle-bindings>");

if (args[1] is "--pe-renderer-bindings" or "--pe-particle-bindings")
{
    using var stream = File.OpenRead(args[0]);
    using var pe = new PEReader(stream);
    var code = pe.PEHeaders.SectionHeaders.Single(s => s.Name == ".text");
    var strings = pe.PEHeaders.SectionHeaders.Single(s => s.Name == ".rdata");
    byte[] bytes = new byte[code.SizeOfRawData];
    stream.Position = code.PointerToRawData;
    stream.ReadExactly(bytes);
    byte[] stringBytes = new byte[strings.SizeOfRawData];
    stream.Position = strings.PointerToRawData;
    stream.ReadExactly(stringBytes);
    var ranges = args[1] == "--pe-renderer-bindings"
        ? new[] { (28623900, 28626000), (28630700, 28633000) }
        : new[] { (20412000, 20417000), (20723000, 20728000) };
    foreach (var (start, end) in ranges)
    for (int fileOffset = start; fileOffset < end; fileOffset++)
    {
        int i = fileOffset - code.PointerToRawData;
        if (bytes[i] is not (0x48 or 0x4C) || bytes[i + 1] != 0x8D ||
            (bytes[i + 2] & 0xC7) != 0x05) continue;
        int target = code.VirtualAddress + i + 7 + BitConverter.ToInt32(bytes, i + 3);
        int stringIndex = target - strings.VirtualAddress;
        if (stringIndex < 0 || stringIndex >= stringBytes.Length) continue;
        int count = 0;
        while (count < 100 && stringIndex + count < stringBytes.Length &&
               stringBytes[stringIndex + count] is >= 32 and <= 126) count++;
        if (count >= 3 && stringIndex + count < stringBytes.Length && stringBytes[stringIndex + count] == 0)
            Console.WriteLine($"{fileOffset}\t{System.Text.Encoding.ASCII.GetString(stringBytes, stringIndex, count)}");
    }
    return;
}

if (args[1] == "--pe-string-xrefs")
{
    using var stream = File.OpenRead(args[0]);
    using var pe = new PEReader(stream);
    var code = pe.PEHeaders.SectionHeaders.Single(s => s.Name == ".text");
    var strings = pe.PEHeaders.SectionHeaders.Single(s => s.Name == ".rdata");
    var targets = new (string Name, int FileOffset)[]
    {
        ("CurveSpawnScalar", 61473512), ("CurveSpawnName", 61473524),
        ("DefaultCurveNormal", 61321160), ("OriginalPoint", 61321008),
        ("EmissionRate", 61473400), ("UseCustomStreams", 61411632),
        ("EnableGPUInstancingA", 61553784), ("EnableGPUInstancingB", 61750216),
        ("RenderAlignment", 61369792), ("FlipA", 61308972),
        ("FlipB", 61683544), ("FlipC", 61685536),
        ("UseParticleDistanceLOD", 61712320)
    }.ToDictionary(x => strings.VirtualAddress + x.FileOffset - strings.PointerToRawData, x => x.Name);
    byte[] bytes = new byte[code.SizeOfRawData];
    stream.Position = code.PointerToRawData;
    stream.ReadExactly(bytes);
    for (int i = 0; i + 7 < bytes.Length; i++)
    {
        // x64 RIP-relative LEA for a string or nearby static property binding.
        if (bytes[i] is not (0x48 or 0x4C) || bytes[i + 1] != 0x8D ||
            (bytes[i + 2] & 0xC7) != 0x05) continue;
        int displacement = BitConverter.ToInt32(bytes, i + 3);
        int target = code.VirtualAddress + i + 7 + displacement;
        if (targets.TryGetValue(target, out string name))
            Console.WriteLine($"{name}\tcodeFile={code.PointerToRawData + i}\trva={code.VirtualAddress + i:X}");
    }
    return;
}

if (args[1] == "--pe-sections")
{
    using var stream = File.OpenRead(args[0]);
    using var pe = new PEReader(stream);
    foreach (var section in pe.PEHeaders.SectionHeaders)
        Console.WriteLine($"{section.Name}\tfile={section.PointerToRawData}..{section.PointerToRawData + section.SizeOfRawData}\trva={section.VirtualAddress:X}..{section.VirtualAddress + section.VirtualSize:X}");
    return;
}

if (args[1] == "--pe-debug")
{
    using var stream = File.OpenRead(args[0]);
    using var pe = new PEReader(stream);
    foreach (var entry in pe.ReadDebugDirectory())
    {
        Console.WriteLine($"{entry.Type}\tdataSize={entry.DataSize}");
        if (entry.Type == DebugDirectoryEntryType.CodeView)
            Console.WriteLine($"CodeViewPath={pe.ReadCodeViewDebugDirectoryData(entry).Path}");
    }
    return;
}

if (args[1] == "--scan-type-hashes")
{
    var hashes = new[]
    {
        "A854C19E25E0A7F85417CC1807AA87CA", // ParticleSystem
        "EC270FAF17AE20EA92C009F528E47167"  // ParticleSystemRenderer
    };
    using var source = File.OpenRead(args[0]);
    byte[] chunk = new byte[4 * 1024 * 1024 + 15];
    long consumed = 0;
    int carry = 0;
    while (true)
    {
        int read = source.Read(chunk, carry, chunk.Length - carry);
        int count = carry + read;
        if (read == 0) break;
        foreach (string hash in hashes)
        foreach (bool reversed in new[] { false, true })
        {
            byte[] needle = Convert.FromHexString(hash);
            if (reversed) Array.Reverse(needle);
            int from = 0;
            while (from < count)
            {
                int found = chunk.AsSpan(from, count - from).IndexOf(needle);
                if (found < 0) break;
                int index = from + found;
                Console.WriteLine($"{hash}\t{(reversed ? "reversed" : "direct")}\toffset={consumed - carry + index}");
                from = index + 1;
            }
        }
        carry = Math.Min(15, count);
        Array.Copy(chunk, count - carry, chunk, 0, carry);
        consumed += read;
    }
    return;
}

var manager = new AssetsManager { Game = GameManager.GetGameByType(GameType.GI), Silent = true };
try
{
    manager.LoadFiles(new[] { args[0] }, mergeSplitAssets: false);
    if (args[1] == "--renderers")
    {
        foreach (var file in manager.assetsFileList)
        foreach (var renderer in file.ObjectsDic.Values.OfType<GenshinParticleSystemRenderer>())
        {
            byte[] bytes = renderer.GetRawData();
            if (bytes.Length != 396) continue;
            string Words(int offset, int count) => string.Join(',', Enumerable.Range(0, count / 4)
                .Select(i => BitConverter.ToUInt32(bytes, offset + i * 4).ToString("X8")));
            Console.WriteLine($"{file.fileName}\t{renderer.m_PathID}\tmode={BitConverter.ToUInt16(bytes, 164)}\tflags={Words(212, 12)}\tafterOutline={Words(288, 32)}\ttail={Words(380, 16)}");
        }
        return;
    }
    if (args[1] == "--particles")
    {
        foreach (var file in manager.assetsFileList)
        foreach (var particle in file.ObjectsDic.Values.OfType<GenshinParticleSystem>())
        {
            byte[] bytes = particle.GetRawData();
            if (bytes.Length is not (7868 or 8060 or 8116)) continue;
            int afterEmission = bytes.Length is 7868 or 8060 ? 1312 : 1368;
            string ShortHash(int offset, int count) => Convert.ToHexString(SHA256.HashData(bytes.AsSpan(offset, count)))[..16];
            string Words(int offset, int count) => string.Join(',', Enumerable.Range(0, count / 4)
                .Select(i => BitConverter.ToUInt32(bytes, offset + i * 4).ToString("X8")));
            Console.WriteLine($"{file.fileName}\t{particle.m_PathID}\tbytes={bytes.Length}\theader={Words(32, 4)},{Words(104, 8)}\tshapeType={BitConverter.ToInt32(bytes, 900)}\tshape={ShortHash(1144, 68)}\temission={ShortHash(afterEmission, 52)}\ttail={ShortHash(bytes.Length - 440, 440)}\tshapeWords={Words(1144, 68)}\temissionWords={Words(afterEmission, 52)}");
        }
        return;
    }
    long pathId = long.Parse(args[1]);
    foreach (var file in manager.assetsFileList)
    {
        if (file.ObjectsDic.TryGetValue(pathId, out var root))
            Console.WriteLine($"{file.fileName}\toffset={file.offset}\t{root.type}\t{root.m_PathID}\t{root.Name}");
    }
}
finally { manager.Clear(); }
