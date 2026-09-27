using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AnimeStudio
{
    public static class WwiseBank
    {
        public sealed record HircObject(byte Type, uint ID, long Offset, uint Size);
        public sealed record Index(uint Version, uint BankID, List<HircObject> Objects);

        public static uint EventID(string name)
        {
            uint hash = 2166136261;
            foreach (char c in name)
            {
                if (c > 127) throw new ArgumentException("Only ASCII Wwise event names are supported.");
                hash = unchecked(hash * 16777619) ^ (byte)char.ToLowerInvariant(c);
            }
            return hash;
        }

        public static Index ReadIndex(WwisePackage.Entry entry)
        {
            if (entry.Kind != "Bank") throw new ArgumentException("Expected a bank entry.");
            using var stream = File.OpenRead(entry.Package);
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            long end = checked(entry.Offset + entry.Size);
            if (entry.Offset < 0 || end > stream.Length) throw new InvalidDataException("Bank outside the package.");
            stream.Position = entry.Offset;
            uint version = 0, bankID = 0;
            var objects = new List<HircObject>();
            while (stream.Position < end)
            {
                if (end - stream.Position < 8) throw new InvalidDataException("Truncated bank chunk.");
                uint tag = reader.ReadUInt32(); uint size = reader.ReadUInt32();
                long chunkEnd = stream.Position + size;
                if (chunkEnd > end) throw new InvalidDataException("Bank chunk outside entry.");
                if (tag == 0x44484B42) // BKHD
                {
                    if (size < 8) throw new InvalidDataException("Truncated BKHD.");
                    version = reader.ReadUInt32(); bankID = reader.ReadUInt32();
                }
                if (tag == 0x43524948) // HIRC
                {
                    if (size < 4) throw new InvalidDataException("Truncated HIRC.");
                    uint count = reader.ReadUInt32();
                    if (count > (size - 4) / 9) throw new InvalidDataException("Invalid HIRC object count.");
                    for (uint i = 0; i < count; i++)
                    {
                        if (chunkEnd - stream.Position < 9) throw new InvalidDataException("Truncated HIRC object.");
                        byte type = reader.ReadByte(); uint length = reader.ReadUInt32(); long offset = stream.Position;
                        if (length < 4 || offset + length > chunkEnd) throw new InvalidDataException("HIRC object outside chunk.");
                        uint id = reader.ReadUInt32();
                        objects.Add(new HircObject(type, id, offset, length));
                        stream.Position = offset + length;
                    }
                    if (stream.Position != chunkEnd) throw new InvalidDataException("HIRC count/length mismatch.");
                }
                stream.Position = chunkEnd;
            }
            if (version == 0) throw new InvalidDataException("Missing BKHD.");
            return new Index(version, bankID, objects);
        }
    }
}
