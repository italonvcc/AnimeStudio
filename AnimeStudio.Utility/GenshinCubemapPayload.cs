using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace AnimeStudio
{
    // A Cubemap is six images and must not be passed through the Texture2D
    // payload writer. Preserve the original serialized header and image bytes.
    internal static class GenshinCubemapPayload
    {
        internal sealed class Result
        {
            public string MetadataPath { get; init; }
            public string RawPath { get; init; }
            public string ImageDataPath { get; init; }
            public string[] FacePaths { get; init; }
            public int[] FaceIndices { get; init; }
            public string[] PreviewErrors { get; init; }
            public string Status { get; init; }
        }

        public static Result Write(Cubemap cube, string basePath, object identity, object sourceGate = null)
        {
            if (cube == null) throw new ArgumentNullException(nameof(cube));
            if (basePath == null) throw new ArgumentNullException(nameof(basePath));
            byte[] raw, data = null;
            var layout = cube.ImageLayout;
            var gate = sourceGate ?? cube;
            lock (gate)
            {
                raw = cube.GetRawData();
                if (layout != null)
                {
                    data = layout.image_data.GetData();
                    if (data.Length != layout.image_data.Size)
                        throw new InvalidDataException("Truncated cubemap resource data.");
                }
            }

            string rawPath = basePath + ".bin";
            using (var output = new FileStream(rawPath, FileMode.CreateNew)) output.Write(raw);
            string imagePath = null;
            if (data != null)
            {
                imagePath = basePath + ".image-data.bin";
                using var output = new FileStream(imagePath, FileMode.CreateNew);
                output.Write(data);
            }

            var faces = new List<string>();
            var faceIndices = new List<int>();
            var errors = new List<string>();
            if (layout != null)
            {
                lock (gate)
                {
                    var originalReader = layout.image_data;
                    using var dataReader = new BinaryReader(new MemoryStream(data, writable: false));
                    try
                    {
                        for (int face = 0; face < 6; face++)
                        {
                            try
                            {
                                layout.image_data = new ResourceReader(dataReader, face * (data.Length / 6), data.Length / 6);
                                using var decoded = layout.ConvertToStream(ImageFormat.Png, true)
                                    ?? throw new InvalidDataException("Cubemap face decoder failed.");
                                if (decoded.Length == 0) throw new InvalidDataException("Cubemap face encoder returned an empty image.");
                                string facePath = basePath + $".face-{face}.png";
                                decoded.Position = 0;
                                using (var output = new FileStream(facePath, FileMode.CreateNew)) decoded.CopyTo(output);
                                faces.Add(facePath);
                                faceIndices.Add(face);
                            }
                            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or NotSupportedException)
                            {
                                errors.Add($"face {face}: {ex.Message}");
                            }
                        }
                    }
                    finally { layout.image_data = originalReader; }
                }
            }
            string status = layout == null ? "UnsupportedLayout" : errors.Count == 0 ? "RawAndSixPreviews" : "RawWithIncompletePreviews";
            string metadataPath = basePath + ".json";
            var settings = new JsonSerializerSettings { Converters = { new StringEnumConverter() } };
            File.WriteAllText(metadataPath, JsonConvert.SerializeObject(new
            {
                identity,
                raw = Path.GetFileName(rawPath), rawSha256 = Convert.ToHexString(SHA256.HashData(raw)),
                imageData = imagePath == null ? null : Path.GetFileName(imagePath),
                imageDataSha256 = data == null ? null : Convert.ToHexString(SHA256.HashData(data)),
                width = layout?.m_Width, height = layout?.m_Height,
                format = layout?.m_TextureFormat.ToString(), mips = layout?.m_MipCount, imageCount = layout?.m_ImageCount,
                // Keep the prior VFX metadata names for existing evidence readers.
                m_Width = layout?.m_Width, m_Height = layout?.m_Height,
                m_TextureFormat = layout?.m_TextureFormat.ToString(), m_MipCount = layout?.m_MipCount,
                m_ImageCount = layout?.m_ImageCount,
                faces = faces.ConvertAll(Path.GetFileName), previewErrors = errors, status, cube.LayoutError,
                interpretation = "Six serialized face indices, each with its source mip chain; PNG top-mip previews use the Texture2D export flip. Original compressed/HDR data is retained without quantization."
            }, Formatting.Indented, settings));
            return new Result { MetadataPath = metadataPath, RawPath = rawPath,
                ImageDataPath = imagePath, FacePaths = faces.ToArray(), FaceIndices = faceIndices.ToArray(),
                PreviewErrors = errors.ToArray(), Status = status };
        }
    }
}
