using System;
using System.Linq;

namespace AnimeStudio.CLI
{
    internal static class GenshinAudioCommand
    {
        public static int Run(string[] args)
        {
            try
            {
                if (args[0] == "--genshin-event-graph" && args.Length >= 4)
                    WwiseEventGraph.WriteRequest(args[1], args.Skip(3).ToArray(), args[2]);
                else if (args[0] == "--genshin-banks" && args.Length == 4)
                    GenshinAudioExporter.ExportEventBanks(args[1], args[2], args[3]);
                else if (args[0] == "--genshin-bank-ids" && args.Length == 4)
                    GenshinAudioExporter.ExportBankIDs(args[1], args[2], args[3]);
                else if (args[0] == "--genshin-audio" && (args.Length == 4 || args.Length == 6 && args[4] == "--decoder"))
                    GenshinAudioExporter.ExportNames(args[1], args[2], args[3], args.Length == 6 ? args[5] : null);
                else throw new ArgumentException("Usage: --genshin-banks <audio folder> <event names.txt> <new directory> OR --genshin-bank-ids <audio folder> <ids.json> <new directory> OR --genshin-event-graph <event names.txt> <new request.json> <wwiser.xml> [...] OR --genshin-audio <audio folder> <names.json> <new directory> [--decoder <vgmstream-cli.exe>]");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
    }
}
