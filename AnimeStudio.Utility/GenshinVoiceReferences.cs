using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;

namespace AnimeStudio
{
    // External tools and naming data stay in the user's map cache, never bundled as fork code.
    public static class GenshinVoiceReferences
    {
        private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(5) };
        static GenshinVoiceReferences() { Client.DefaultRequestHeaders.UserAgent.ParseAdd("AnimeStudio-character-export/1.0"); }
        private static string Download(string url, string path)
        {
            if (!File.Exists(path))
            {
                byte[] data = Client.GetByteArrayAsync(url).GetAwaiter().GetResult();
                if (data.Length == 0 || data.Length > 256 * 1024 * 1024) throw new InvalidDataException("Invalid external reference download size.");
                Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path + ".tmp", data); File.Move(path + ".tmp", path);
            }
            return path;
        }
        public static void RefreshNames(string cache, Action<string> progress = null)
        {
            PrepareProvider(cache, progress);
        }
        private static (string Directory, JObject Metadata) PrepareProvider(string cache, Action<string> progress)
        {
            string external = Path.Combine(cache, "external-audio"); Directory.CreateDirectory(external);
            string metadata = Path.Combine(external, "provider.json");
            JObject provider;
            if (File.Exists(metadata)) provider = JObject.Parse(File.ReadAllText(metadata));
            else
            {
                progress?.Invoke("Refreshing voice names and WAV decoder from their upstream providers");
                var commit = JObject.Parse(Client.GetStringAsync("https://api.github.com/repos/Escartem/AnimeWwise/commits/master").GetAwaiter().GetResult());
                var release = JObject.Parse(Client.GetStringAsync("https://api.github.com/repos/vgmstream/vgmstream/releases/latest").GetAwaiter().GetResult());
                provider = new JObject { ["readerRevision"] = (string)commit["sha"], ["decoderRelease"] = (string)release["tag_name"],
                    ["decoderUrl"] = (string)release["assets"].Single(a => (string)a["name"] == "vgmstream-win64.zip")["browser_download_url"],
                    ["source"] = "https://github.com/Escartem/AnimeWwise", ["license"] = "CC-BY-NC-SA-4.0" };
                File.WriteAllText(metadata, provider.ToString());
            }
            string raw = "https://raw.githubusercontent.com/Escartem/AnimeWwise/" + (string)provider["readerRevision"] + "/";
            foreach (string file in new[] { "mapper.py", "filereader.py", "LICENCE.md", "maps/hk4e.map" }) Download(raw + file, Path.Combine(external, file));
            return (external, provider);
        }
        public static string Prepare(string cache, string character, Action<string> progress, out string decoder)
        {
            var (external, provider) = PrepareProvider(cache, progress);
            string zip = Download((string)provider["decoderUrl"], Path.Combine(external, "vgmstream.zip"));
            string binaries = Path.Combine(external, "vgmstream");
            if (!Directory.Exists(binaries)) ZipFile.ExtractToDirectory(zip, binaries);
            decoder = Directory.GetFiles(binaries, "vgmstream-cli.exe", SearchOption.AllDirectories).Single();
            string request = Path.Combine(external, character + ".voices.json");
            if (!File.Exists(request))
            {
                // Adapter calls upstream's public Mapper API; its implementation remains external.
                string script = "import sys,json,hashlib,re\nfrom pathlib import Path\nfrom mapper import Mapper\nm=Mapper(sys.argv[1])\np=re.compile(r'(^|[\\\\/])vo_'+re.escape(sys.argv[2])+r'([\\\\/_]|$)',re.I)\ne=[]\nfor k in m.keys:\n n,l=m.get_key(k,addLang=True)\n if p.search(n): e.append(dict(kind='External',id=str(int(k,16)),name=n,language=l))\ns=json.loads(Path('provider.json').read_text())\ns['mapSha256']=hashlib.sha256(Path(sys.argv[1]).read_bytes()).hexdigest()\ns['evidence']='External semantic voice-path map; installed package presence checked by AnimeStudio'\nPath(sys.argv[3]).write_text(json.dumps(dict(source=s,entries=e)),encoding='utf-8')\n";
                var start = new ProcessStartInfo("python") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = external, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (string argument in new[] { "-c", script, Path.Combine(external, "maps", "hk4e.map"), character, request }) start.ArgumentList.Add(argument);
                using var process = Process.Start(start) ?? throw new IOException("Python is required to read the external voice-name provider.");
                var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(180000)) { process.Kill(true); throw new TimeoutException("Voice naming provider timed out."); }
                File.WriteAllText(Path.Combine(external, "provider.log"), stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
                if (process.ExitCode != 0 || !File.Exists(request)) throw new IOException("Voice reference preparation failed. See " + Path.Combine(external, "provider.log"));
            }
            if (((JArray)JObject.Parse(File.ReadAllText(request))["entries"]).Count == 0) throw new InvalidDataException("The current voice-name provider has no evidenced entries for " + character + ". Model/animation exports remain available.");
            return request;
        }
    }
}
