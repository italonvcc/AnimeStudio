using System;
using System.Buffers.Binary;
using System.Text;

namespace AnimeStudio
{
    // Narrow fallback for GI's Unity 2017 shader layout when subshader parsing fails.
    // Require a unique length-prefixed name/editor/footer and consistent blob tables.
    public static class GenshinShaderNameReader
    {
        public static string TryRead(byte[] data)
        {
            string result = null;
            ReadOnlySpan<byte> prefix = "miHoYo/"u8;
            for (int start = 4; start < data.Length - prefix.Length; start++)
            {
                if ((start & 3) != 0 || !data.AsSpan(start).StartsWith(prefix)) continue;
                try
                {
                    int position = start - 4;
                    string name = ReadString(data, ref position);
                    string editor = ReadString(data, ref position);
                    ReadString(data, ref position); // fallback name
                    if (!name.StartsWith("miHoYo/", StringComparison.Ordinal) ||
                        !IsKnownEditor(editor)) continue;
                    int dependencies = ReadCount(data, ref position, 32);
                    for (int i = 0; i < dependencies; i++)
                    {
                        ReadString(data, ref position);
                        ReadString(data, ref position);
                    }
                    if (!ReadBoolean(data, ref position)) continue;
                    var platforms = ReadArray(data, ref position, 32);
                    var offsets = ReadArray(data, ref position, 32);
                    var compressed = ReadArray(data, ref position, 32);
                    var decompressed = ReadArray(data, ref position, 32);
                    if (platforms.Length == 0 || platforms.Length != offsets.Length ||
                        platforms.Length != compressed.Length || platforms.Length != decompressed.Length) continue;
                    bool valid = true;
                    foreach (var platform in platforms)
                        valid &= Enum.IsDefined(typeof(ShaderCompilerPlatform), (int)platform) && platform != uint.MaxValue;
                    int blobLength = ReadCount(data, ref position, data.Length);
                    if (blobLength == 4 && ReadUInt(data, position) == uint.MaxValue)
                    {
                        position += 4;
                        blobLength = ReadCount(data, ref position, data.Length);
                    }
                    if (blobLength == 0 || blobLength > data.Length - position) continue;
                    for (int i = 0; i < offsets.Length; i++)
                        valid &= (ulong)offsets[i] + compressed[i] <= (ulong)blobLength && decompressed[i] > 0;
                    if (!valid) continue;
                    // Ambiguous records must never silently pick the first string.
                    if (result != null) return null;
                    result = name;
                }
                catch (ArgumentException) { }
                catch (OverflowException) { }
            }
            return result;
        }

        private static bool IsKnownEditor(string editor) =>
            editor == "MoleMole.RevertableEditor" || editor == "MiHoYoASEMaterialInspector" || editor == "MoleMole.ASECharacterShaderEditorBase" ||
            editor.StartsWith("MoleMole.", StringComparison.Ordinal) && editor.EndsWith("ShaderEditor", StringComparison.Ordinal);

        private static uint ReadUInt(byte[] data, int position) => BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(position, 4));
        private static int ReadCount(byte[] data, ref int position, int maximum)
        {
            uint count = ReadUInt(data, position);
            position += 4;
            if (count > maximum) throw new ArgumentException("Invalid count");
            return (int)count;
        }
        private static string ReadString(byte[] data, ref int position)
        {
            int length = ReadCount(data, ref position, 512);
            var bytes = data.AsSpan(position, length);
            foreach (byte value in bytes)
                if (value < 32 || value > 126) throw new ArgumentException("Not an ASCII metadata string");
            var text = Encoding.ASCII.GetString(bytes);
            position = checked((position + length + 3) & ~3);
            if (position > data.Length) throw new ArgumentException("Truncated string");
            return text;
        }
        private static bool ReadBoolean(byte[] data, ref int position)
        {
            if (position > data.Length - 4 || data[position] > 1) return false;
            for (int i = 1; i < 4; i++) if (data[position + i] != 0) return false;
            position += 4;
            return true;
        }
        private static uint[] ReadArray(byte[] data, ref int position, int maximum)
        {
            int count = ReadCount(data, ref position, maximum);
            var values = new uint[count];
            for (int i = 0; i < count; i++, position += 4) values[i] = ReadUInt(data, position);
            return values;
        }
    }
}
