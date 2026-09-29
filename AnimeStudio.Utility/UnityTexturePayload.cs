using System;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace AnimeStudio
{
    // Lossless companion to the preview PNG. No decoder, row flip, mip
    // generation or recompression participates in this export. Consumers must
    // support the declared Unity texture format and the source color space.
    public static class UnityTexturePayload
    {
        public sealed class Result
        {
            public string name, sourceId, file, status, reason;
        }

        public static Result Export(Texture source, string directory)
        {
            if (source is Texture2D image) return ExportImage(image,directory,false);
            if (source is Cubemap cube && cube.ImageLayout != null) return ExportImage(cube.ImageLayout,directory,true);
            return new Result {name=source.Name,sourceId=source.m_PathID.ToString(),status="unsupported",
                reason=source is Cubemap failed ? failed.LayoutError : "Unsupported texture dimension"};
        }

        static Result ExportImage(Texture2D texture, string directory, bool cube)
        {
            var result = new Result { name = texture.Name, sourceId = texture.m_PathID.ToString() };
            string Unsupported(string reason) { result.status = "unsupported"; result.reason = reason; return reason; }
            if (texture.platform != BuildTarget.StandaloneWindows && texture.platform != BuildTarget.StandaloneWindows64)
            { Unsupported("Native layout verified only for Windows Unity textures"); return result; }
            if (cube ? texture.m_ImageCount != 6 || texture.m_Width != texture.m_Height : texture.m_TextureDimension != 2 || texture.m_ImageCount != 1)
            { Unsupported("Expected a single 2D image or a validated six-face cubemap"); return result; }
            if (texture.m_ColorSpace is not (0 or 1))
            { Unsupported("Source color-space metadata is unavailable"); return result; }
            string format = texture.m_TextureFormat.ToString();
            int blockBytes = format switch { "DXT1" or "BC4" => 8, "DXT5" or "BC5" or "BC6H" or "BC7" => 16, _ => 0 };
            int pixelBytes = format switch { "Alpha8" or "R8" => 1, "RGB565" or "ARGB4444" or "RGBA4444" or "RG16" or "RHalf" => 2,
                "RGB24" => 3, "RGBA32" or "ARGB32" or "BGRA32" or "RFloat" or "RGHalf" => 4,
                "RGFloat" or "RGBAHalf" => 8, "RGBAFloat" => 16, _ => 0 };
            if (blockBytes == 0 && pixelBytes == 0)
            { Unsupported("Unsupported native texture format: " + format); return result; }
            int width = texture.m_Width, height = texture.m_Height;
            if (width <= 0 || height <= 0) throw new InvalidDataException("Invalid source texture dimensions");
            int fullMips = 1;
            for (int size = Math.Max(width,height); size > 1; size >>= 1) fullMips++;
            int mips = texture.m_MipCount > 0 ? texture.m_MipCount : texture.m_MipMap ? fullMips : 1;
            if (mips > fullMips) throw new InvalidDataException("Invalid source mip count");
            long expected = 0;
            for (int mip = 0; mip < mips; mip++)
            {
                int w = Math.Max(1,width >> mip), h = Math.Max(1,height >> mip);
                expected += blockBytes > 0 ? (long)((w+3)/4)*((h+3)/4)*blockBytes : (long)w*h*pixelBytes;
            }
            if (cube) expected *= 6;
            byte[] data = texture.image_data.GetData();
            if (data.LongLength != expected)
            { Unsupported($"Native layout size {data.LongLength} differs from expected mip payload {expected}"); return result; }
            var sampler = texture.m_TextureSettings;
            var metadata = new { schemaVersion = cube ? 2 : 1, dimension = cube ? "Cube" : "2D", imageCount = cube ? 6 : 1, name = texture.Name, sourceId = result.sourceId,
                sourceFile = texture.assetsFile.fileName, sourcePlatform = texture.platform.ToString(),
                width, height, mipCount = mips, format, colorSpace = texture.m_ColorSpace,
                filterMode = sampler.m_FilterMode, anisoLevel = sampler.m_Aniso, mipBias = sampler.m_MipBias,
                wrapU = sampler.m_WrapMode, wrapV = sampler.m_WrapV, wrapW = sampler.m_WrapW, byteCount = data.Length };
            byte[] header = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(metadata));
            string safeName = string.Concat(texture.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            result.file = safeName + "_" + texture.m_PathID + ".astexture";
            Directory.CreateDirectory(directory);
            using (var stream = new FileStream(Path.Combine(directory,result.file),FileMode.CreateNew,FileAccess.Write))
            using (var writer = new BinaryWriter(stream,Encoding.UTF8))
            {
                writer.Write(Encoding.ASCII.GetBytes("ASTEX001"));
                writer.Write(header.Length); writer.Write(header); writer.Write(data);
            }
            result.status = "exported";
            return result;
        }
    }
}
