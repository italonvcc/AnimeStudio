using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace AnimeStudio
{
    // The study package's ASTEX001 importer uploads these source bytes and mips
    // directly. The PNG beside it is a preview, not the authored GPU payload.
    internal static class GenshinVfxTexturePayload
    {
        public static string Write(Texture2D texture, byte[] data, string path)
        {
            if (texture.m_TextureDimension != 2 || texture.m_ImageCount != 1 ||
                texture.m_Width <= 0 || texture.m_Height <= 0 || texture.m_MipCount <= 0 ||
                texture.m_ColorSpace is < 0 or > 1 || data.Length == 0)
                throw new InvalidDataException("Unsupported native VFX Texture2D layout: " + texture.Name);

            var settings = texture.m_TextureSettings;
            var header = new
            {
                schemaVersion = 1,
                name = texture.Name,
                sourceId = texture.m_PathID.ToString(System.Globalization.CultureInfo.InvariantCulture),
                sourceFile = texture.assetsFile.fileName,
                sourcePlatform = texture.platform.ToString(),
                format = texture.m_TextureFormat.ToString(),
                dimension = "2D",
                width = texture.m_Width,
                height = texture.m_Height,
                mipCount = texture.m_MipCount,
                imageCount = texture.m_ImageCount,
                colorSpace = texture.m_ColorSpace,
                filterMode = settings.m_FilterMode,
                anisoLevel = settings.m_Aniso,
                wrapU = settings.m_WrapMode,
                wrapV = settings.m_WrapV,
                wrapW = settings.m_WrapW,
                mipBias = settings.m_MipBias,
                byteCount = data.Length
            };
            byte[] encoded = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(header));
            if (encoded.Length > 65536) throw new InvalidDataException("Native texture header is too long.");
            using var output = new BinaryWriter(new FileStream(path, FileMode.CreateNew));
            output.Write(Encoding.ASCII.GetBytes("ASTEX001"));
            output.Write(encoded.Length);
            output.Write(encoded);
            output.Write(data);
            return Convert.ToHexString(SHA256.HashData(data));
        }
    }
}
