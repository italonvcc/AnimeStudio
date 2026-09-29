using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AnimeStudio
{
    // Index-only reader for the little-endian AKPK layout used by the inspected Windows client.
    public static class WwisePackage
    {
        public sealed record Entry(string Package, string Kind, ulong ID, uint LanguageID, string Language, long Offset, long Size);

        public static List<Entry> ReadIndex(string path)
        {
            path = Path.GetFullPath(path);
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            if (stream.Length < 28 || reader.ReadUInt32() != 0x4B504B41) throw new InvalidDataException("Expected AKPK package.");
            long headerEnd = 8L + reader.ReadUInt32();
            if (reader.ReadUInt32() != 1) throw new InvalidDataException("Unsupported AKPK byte order/version.");
            var languageSize = reader.ReadUInt32();
            var bankSize = reader.ReadUInt32();
            var mediaSize = reader.ReadUInt32();
            long externalSize = headerEnd > 24L + languageSize + bankSize + mediaSize ? reader.ReadUInt32() : 0;
            if (headerEnd != stream.Position + languageSize + bankSize + mediaSize + externalSize || headerEnd > stream.Length)
                throw new InvalidDataException("Invalid AKPK header sections.");
            long languageStart = stream.Position;
            long languageEnd = languageStart + languageSize;
            if (languageSize < 4) throw new InvalidDataException("Missing AKPK language table.");
            var languages = new Dictionary<uint, string>();
            var count = reader.ReadUInt32();
            if (count > (languageSize - 4) / 8) throw new InvalidDataException("Invalid language count.");
            for (uint i = 0; i < count; i++)
            {
                var offset = reader.ReadUInt32(); var id = reader.ReadUInt32(); var saved = stream.Position;
                if (offset < 4L + count * 8L || languageStart + offset >= languageEnd) throw new InvalidDataException("Invalid language offset.");
                stream.Position = languageStart + offset;
                var name = new StringBuilder(); bool terminated = false;
                while (stream.Position + 2 <= languageEnd)
                {
                    char c = (char)reader.ReadUInt16();
                    if (c == 0) { terminated = true; break; }
                    name.Append(c);
                }
                if (!terminated || !languages.TryAdd(id, name.ToString())) throw new InvalidDataException("Invalid language string/table.");
                stream.Position = saved;
            }
            stream.Position = languageEnd;
            var entries = new List<Entry>();
            void Table(long length, string kind)
            {
                if (length == 0) return;
                if (length < 4) throw new InvalidDataException("Invalid AKPK table size.");
                long end = stream.Position + length;
                uint files = reader.ReadUInt32();
                if (files == 0)
                {
                    if (length != 4) throw new InvalidDataException("Unexpected empty AKPK table payload.");
                    return;
                }
                long width = (length - 4) / files;
                if ((length - 4) % files != 0 || width is not (20 or 24)) throw new InvalidDataException("Unsupported AKPK entry width.");
                for (uint i = 0; i < files; i++)
                {
                    ulong id = kind == "External" && width == 24 ? reader.ReadUInt64() : reader.ReadUInt32();
                    uint block = reader.ReadUInt32();
                    ulong size = kind != "External" && width == 24 ? reader.ReadUInt64() : reader.ReadUInt32();
                    long offset = checked((long)reader.ReadUInt32() * Math.Max(1u, block));
                    uint language = reader.ReadUInt32();
                    if (size > long.MaxValue || offset < headerEnd || offset > stream.Length - (long)size)
                        throw new InvalidDataException("AKPK payload outside the package.");
                    if (!languages.TryGetValue(language, out var languageName)) throw new InvalidDataException("Unknown AKPK language ID.");
                    entries.Add(new Entry(path, kind, id, language, languageName, offset, (long)size));
                }
                if (stream.Position != end) throw new InvalidDataException("AKPK table length mismatch.");
            }
            Table(bankSize, "Bank"); Table(mediaSize, "Media"); Table(externalSize, "External");
            return entries;
        }
    }
}
