using System;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace AnimeStudio
{
    // This is a byte-level inventory, not a reconstruction of the game's scripts.
    // The source MonoBehaviour type trees are stripped. Only the shared Unity
    // header and bounded, length-prefixed Audio strings are interpreted.
    internal static class GenshinEffectActionDecoder
    {
        private const string AudioTypeHash = "3328A57742DE2EE75925DEA077773399";

        public static JObject Decode(MonoBehaviour script, byte[] bytes, string typeHash)
        {
            if (!script.assetsFile.unityVersion.StartsWith("2017.4.30f1", StringComparison.Ordinal))
                throw new InvalidDataException("MonoBehaviour header layout is not verified for this source Unity version.");
            if (bytes.Length < 32) throw new InvalidDataException("MonoBehaviour payload is shorter than its Unity header.");
            long gameObjectPath = BitConverter.ToInt64(bytes, 4);
            long scriptPath = BitConverter.ToInt64(bytes, 20);
            if (BitConverter.ToInt32(bytes, 0) != script.m_GameObject.m_FileID ||
                gameObjectPath != script.m_GameObject.m_PathID ||
                bytes[12] != script.m_Enabled || bytes[13] != 0 || bytes[14] != 0 || bytes[15] != 0 ||
                BitConverter.ToInt32(bytes, 16) != script.m_Script.m_FileID ||
                scriptPath != script.m_Script.m_PathID)
                throw new InvalidDataException("MonoBehaviour header disagrees with parsed Unity references.");

            int nameBytes = BitConverter.ToInt32(bytes, 28);
            if (nameBytes < 0 || nameBytes > bytes.Length - 32)
                throw new InvalidDataException("MonoBehaviour name string crosses the source payload.");
            int customStart = (32 + nameBytes + 3) & ~3;
            if (customStart > bytes.Length) throw new InvalidDataException("MonoBehaviour name alignment crosses the source payload.");
            if (!bytes.AsSpan(32, nameBytes).SequenceEqual(Encoding.UTF8.GetBytes(script.m_Name ?? "")))
                throw new InvalidDataException("MonoBehaviour name disagrees with parsed Unity string.");
            var strings = new JArray();
            if (typeHash == AudioTypeHash)
            {
                // Scan aligned length-prefix candidates rather than assuming that
                // every Audio component has Mona Idle's two string offsets.
                // Candidates are literal byte interpretations, not field names.
                for (int offset = customStart; offset <= bytes.Length - 4; offset += 4)
                {
                    var candidate = TryReadAlignedString(bytes, offset);
                    if (candidate == null) continue;
                    strings.Add(candidate);
                    offset = (((int)candidate["dataOffset"] + (int)candidate["byteCount"] + 3) & ~3) - 4;
                }
            }
            return new JObject
            {
                ["unityHeaderBytes"] = customStart,
                ["customPayloadBytes"] = bytes.Length - customStart,
                ["observedStrings"] = strings,
                ["interpretation"] = "Literal UTF-8 strings only; game script fields, execution, attachment, spawn and timing are not decoded."
            };
        }

        private static JObject TryReadAlignedString(byte[] bytes, int lengthOffset)
        {
            int length = BitConverter.ToInt32(bytes, lengthOffset);
            int dataOffset = lengthOffset + 4;
            if (length is < 6 or > 256 || length > bytes.Length - dataOffset)
                return null;
            string value;
            try { value = new UTF8Encoding(false, true).GetString(bytes, dataOffset, length); }
            catch (DecoderFallbackException) { return null; }
            if (value.IndexOf('\0') >= 0 || Array.Exists(value.ToCharArray(), c => char.IsControl(c)))
                return null;
            return new JObject { ["lengthOffset"] = lengthOffset, ["dataOffset"] = dataOffset,
                ["byteCount"] = length, ["text"] = value };
        }
    }
}
