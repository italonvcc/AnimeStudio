using AnimeStudio;
using Newtonsoft.Json.Linq;
using System.Text;

internal static class AudioExportChecks
{
    public static void Run(Action<bool, string> check)
    {
        string dir = Path.Combine(Path.GetTempPath(), "anime-audio-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string bankPath = Path.Combine(dir, "synthetic.bnk");
            void WriteBank(uint offset, uint length)
            {
                using var writer = new BinaryWriter(File.Create(bankPath));
                writer.Write(Encoding.ASCII.GetBytes("DIDX")); writer.Write(12u);
                writer.Write(41u); writer.Write(offset); writer.Write(length);
                writer.Write(Encoding.ASCII.GetBytes("DATA")); writer.Write(4u); writer.Write(new byte[] { 1, 2, 3, 4 });
            }
            WwisePackage.Entry Bank() => new(bankPath, "Bank", 10, 0, "sfx", 0, new FileInfo(bankPath).Length);
            WriteBank(1, 2);
            var media = WwiseBank.ReadMediaIndex(Bank()).Single();
            check(media.ID == 41 && media.Offset == 29 && media.Size == 2, "DIDX resolves media inside DATA with absolute package offsets");
            string copied = Path.Combine(dir, "copied.wem");
            GenshinAudioExporter.CopyEntry(media, copied);
            check(File.ReadAllBytes(copied).SequenceEqual(new byte[] { 2, 3 }), "audio copy is limited to the indexed payload");
            bool rejected = false;
            try { GenshinAudioExporter.CopyEntry(media, copied); } catch (IOException) { rejected = true; }
            check(rejected, "audio extraction refuses overwriting existing output");
            WriteBank(3, 2); rejected = false;
            try { WwiseBank.ReadMediaIndex(Bank()); } catch (InvalidDataException) { rejected = true; }
            check(rejected, "out-of-bounds embedded media is rejected");
            string names = Path.Combine(dir, "names.txt"), xml = Path.Combine(dir, "graph.xml"), request = Path.Combine(dir, "request.json");
            File.WriteAllText(names, "Play_Synthetic");
            uint evt = WwiseBank.EventID("Play_Synthetic");
            string Field(string name, uint value, string type = "tid") => $"<field name='{name}' type='{type}' value='{value}'/>";
            string Node(string name, uint id, string fields) => $"<object name='{name}'>{Field("ulID", id, "sid")}{fields}</object>";
            string header = "<root filename='synthetic.bnk' version='134'>" + Field("dwSoundBankID", 10) + "<list name='listLoadedItem'>";
            string valid = header + Node("CAkEvent", evt, Field("ulActionID", 1)) + Node("CAkActionPlay", 1, Field("idExt", 2) + Field("bankID", 10))
                + Node("CAkRanSeqCntr", 2, Field("ulChildID", 3) + Field("DirectParentID", 99))
                + Node("CAkSound", 3, Field("sourceID", 41) + Field("StreamType", 0)) + "</list></root>";
            File.WriteAllText(xml, valid);
            WwiseEventGraph.WriteRequest(names, new[] { xml }, request);
            var result = JObject.Parse(File.ReadAllText(request));
            check(result["entries"]!.Count() == 1 && (string)result["entries"]![0]!["id"]! == "41", "event/action/container/source chain resolves one sound");
            check(!result["source"]!["events"]![0]!["unresolved"]!.Any(), "unrelated parent/bus IDs are not treated as media dependencies");
            File.WriteAllText(xml, valid.Replace("version='134'", "version='135'")); rejected = false;
            try { WwiseEventGraph.WriteRequest(names, new[] { xml }, Path.Combine(dir, "unsupported.json")); } catch (InvalidDataException) { rejected = true; }
            check(rejected, "untested Wwise graph versions fail explicitly");
            File.WriteAllText(xml, valid.Replace(Field("ulChildID", 3), Field("ulChildID", 99)));
            string missing = Path.Combine(dir, "missing.json");
            WwiseEventGraph.WriteRequest(names, new[] { xml }, missing);
            result = JObject.Parse(File.ReadAllText(missing));
            check(!result["entries"]!.Any() && result["source"]!["events"]![0]!["unresolved"]!.Any(), "missing typed child is reported without guessing another sound");
        }
        finally
        {
            foreach (string file in Directory.GetFiles(dir)) File.Delete(file);
            Directory.Delete(dir); // Empty test directory only; no recursive deletion.
        }
    }
}
