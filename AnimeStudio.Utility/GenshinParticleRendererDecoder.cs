using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace AnimeStudio
{
    internal static class GenshinParticleRendererDecoder
    {
        public const string TypeHash = "EC270FAF17AE20EA92C009F528E47167";

        // The common Genshin Renderer base consumes 164 bytes, including two
        // material slots. Geometry fields here are bounded against the five
        // Idle renderers, two InFloor controls, and captured polygon UVs.
        public static JObject Decode(byte[] data)
        {
            if (data.Length != 396) throw new InvalidDataException("Unknown Genshin particle renderer length.");
            ushort renderMode = BitConverter.ToUInt16(data, 164);
            ushort sortMode = BitConverter.ToUInt16(data, 166);
            int alignment = BitConverter.ToInt32(data, 196);
            int outlineCount = BitConverter.ToInt32(data, 220);
            int streamCount = BitConverter.ToInt32(data, 320);
            if (renderMode is not (0 or 4) || sortMode != 0 ||
                alignment != (renderMode == 0 ? 0 : 2) || outlineCount != 8 ||
                streamCount is not (5 or 7) || data.Skip(324 + streamCount).Take(332 - 324 - streamCount).Any(b => b != 0))
                throw new InvalidDataException("Particle renderer layout invariants failed.");
            var meshPointers = new JArray();
            for (int slot = 0; slot < 4; slot++)
            {
                int offset = 332 + slot * 12;
                int fileId = BitConverter.ToInt32(data, offset);
                long pathId = BitConverter.ToInt64(data, offset + 4);
                meshPointers.Add(new JObject { ["fileId"] = fileId, ["pathId"] = pathId.ToString() });
                if (renderMode == 0 && (fileId != 0 || pathId != 0))
                    throw new InvalidDataException("Billboard renderer unexpectedly references a mesh.");
            }
            float F(int offset)
            {
                float value = BitConverter.ToSingle(data, offset);
                if (!float.IsFinite(value)) throw new InvalidDataException($"Nonfinite renderer float at {offset}.");
                return value;
            }
            var outline = new JArray();
            for (int i = 0; i < outlineCount; i++)
                outline.Add(new JObject { ["x"] = F(224 + i * 8), ["y"] = F(228 + i * 8) });
            // These offsets are the Genshin Renderer base parsed by Renderer.cs:
            // 12..36 header, 36..40 lightmap indices, 40..72 tiling, 72..80
            // distance ratios, 80..108 material slots, 108..152 batch/probes,
            // 152..164 sorting. Keep raw enum values when their game mapping is
            // not yet independently established.
            foreach (int offset in new[] { 12, 14, 15, 32, 33, 160, 212 })
                if (data[offset] > 1) throw new InvalidDataException($"Renderer bool at {offset} is invalid.");
            // The installed player's native field-binding order places these
            // fields directly after m_UseCustomVertexStreams and the octagon
            // coordinates. The byte layout agrees with that order exactly.
            foreach (int offset in new[] { 216, 217, 218, 288 })
                if (data[offset] > 1) throw new InvalidDataException($"Renderer bool at {offset} is invalid.");
            if (data.Skip(213).Take(3).Any(b => b != 0) || data[219] != 0 ||
                data.Skip(289).Take(3).Any(b => b != 0))
                throw new InvalidDataException("Particle renderer field padding is nonzero.");
            var sourceBase = new JObject
            {
                ["castShadowsRaw"] = data[13],
                ["receiveShadows"] = data[14] != 0,
                ["dynamicOccludee"] = data[15] != 0,
                ["motionVectorsRaw"] = data[31],
                ["lightProbeUsageRaw"] = data[32],
                ["reflectionProbeUsageRaw"] = data[33],
                ["lightmapIndex"] = BitConverter.ToUInt16(data, 36),
                ["dynamicLightmapIndex"] = BitConverter.ToUInt16(data, 38),
                ["sortingLayerId"] = BitConverter.ToUInt32(data, 152),
                ["sortingLayerValue"] = BitConverter.ToInt16(data, 156),
                ["sortingOrder"] = BitConverter.ToInt16(data, 158),
                ["useHighestMip"] = data[160] != 0,
                ["sourceOffsets"] = "Renderer base 12..164; see AnimeStudio Renderer.cs Genshin 2017 parser"
            };
            return new JObject
            {
                ["sourceRendererBase"] = sourceBase,
                ["renderMode"] = renderMode == 0 ? "Billboard" : "Mesh",
                ["sortModeRaw"] = sortMode,
                ["minParticleSize"] = F(168), ["maxParticleSize"] = F(172),
                ["cameraVelocityScale"] = F(176), ["velocityScale"] = F(180),
                ["lengthScale"] = F(184), ["sortingFudge"] = F(188),
                ["normalDirection"] = F(192),
                ["alignment"] = renderMode == 0 ? "View" : "Local",
                ["alignmentRaw"] = alignment,
                ["pivot"] = new JObject { ["x"] = F(200), ["y"] = F(204), ["z"] = F(208) },
                ["useCustomVertexStreams"] = data[212] != 0,
                ["enableGPUInstancing"] = data[216] != 0,
                ["enableGPUInstancingV2"] = data[217] != 0,
                ["useOctagonShape"] = data[218] != 0,
                ["sourceUvOutline"] = outline,
                ["sourceUvConvention"] = "Unity 2017 serialized particle outline, top-left V convention; captured D3D12 V = 1 - source V",
                ["rotateWithParent"] = data[288] != 0,
                ["parentScale"] = new JObject { ["x"] = F(292), ["y"] = F(296), ["z"] = F(300) },
                ["parentRotation"] = new JObject { ["x"] = F(304), ["y"] = F(308), ["z"] = F(312), ["w"] = F(316) },
                ["vertexStreamIds"] = new JArray(data.Skip(324).Take(streamCount).Select(b => (int)b)),
                ["meshPointers"] = meshPointers,
                ["maskInteractionRaw"] = BitConverter.ToInt32(data, 380),
                ["flip"] = new JObject { ["x"] = F(384), ["y"] = F(388), ["z"] = F(392) },
                ["unknownBlocks"] = new JArray()
            };
        }
    }
}
