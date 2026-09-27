using System;
using System.IO;
using System.Linq;

namespace AnimeStudio
{
    internal static class GenshinSourceMetadata
    {
        public static string GameVersion(string source)
        {
            var directory = Directory.Exists(source) ? new DirectoryInfo(source) : new FileInfo(source).Directory;
            for (int depth = 0; directory != null && depth < 10; depth++, directory = directory.Parent)
            {
                string config = Path.Combine(directory.FullName, "config.ini");
                if (!File.Exists(config)) continue;
                string value = File.ReadLines(config).FirstOrDefault(s => s.StartsWith("game_version=", StringComparison.OrdinalIgnoreCase));
                if (value != null) return value[(value.IndexOf('=') + 1)..].Trim();
            }
            return null;
        }
    }
}
