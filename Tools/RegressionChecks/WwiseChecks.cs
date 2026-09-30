using AnimeStudio;
using System.Text;

internal static class WwiseChecks
{
    public static void Run(Action<bool, string> check)
    {
        using var bytes = new MemoryStream();
        using var writer = new BinaryWriter(bytes, Encoding.UTF8, true);
        const uint eventID = 0x12345678;
        writer.Write(Encoding.ASCII.GetBytes("AKPK")); writer.Write(68u); writer.Write(1u);
        writer.Write(20u); writer.Write(20u + 4u); writer.Write(4u); writer.Write(0u);
        writer.Write(1u); writer.Write(12u); writer.Write(0u); writer.Write(Encoding.Unicode.GetBytes("sfx\0"));
        writer.Write(1u); writer.Write(99u); writer.Write(1u); writer.Write(41u); writer.Write(76u); writer.Write(0u);
        writer.Write(0u);
        writer.Write(Encoding.ASCII.GetBytes("BKHD")); writer.Write(8u); writer.Write(134u); writer.Write(99u);
        writer.Write(Encoding.ASCII.GetBytes("HIRC")); writer.Write(17u); writer.Write(1u);
        writer.Write((byte)4); writer.Write(8u); writer.Write(eventID); writer.Write(0u);
        var data = bytes.ToArray();
        var folder = Path.Combine(Path.GetTempPath(), "anime-wwise-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "fixture.pck");
        try
        {
            File.WriteAllBytes(path, data);
            var entry = WwisePackage.ReadIndex(path).Single();
            check(entry.ID == 99 && entry.Language == "sfx" && entry.Offset == 76 && entry.Size == 41, "AKPK bank identity, language, and range");
            var bank = WwiseBank.ReadIndex(entry);
            check(bank.Version == 134 && bank.BankID == 99 && bank.Objects.Single().ID == eventID && bank.Objects.Single().Type == 4, "typed HIRC event index");
            check(WwiseBank.EventID("Play_Test") == WwiseBank.EventID("PLAY_TEST"), "Wwise ASCII names hash case-insensitively");
            bool rejected = false;
            File.WriteAllBytes(path, data[..^1]);
            try { WwisePackage.ReadIndex(path); } catch (InvalidDataException) { rejected = true; }
            check(rejected, "AKPK payload truncation rejected");
            File.WriteAllBytes(path, data);
            data[101] = 0xff; data[102] = 0xff; data[103] = 0xff; data[104] = 0xff;
            File.WriteAllBytes(path, data);
            rejected = false;
            try { WwiseBank.ReadIndex(entry); } catch (InvalidDataException) { rejected = true; }
            check(rejected, "HIRC malformed object count/size rejected");
        }
        finally { File.Delete(path); Directory.Delete(folder); }
    }
}
