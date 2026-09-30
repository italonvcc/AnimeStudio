using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace AnimeStudio
{
    /// <summary>
    /// Resolve native clip blend-shape CRCs against the morph channels in the
    /// FBX being exported. Standalone game clips often have no controller link,
    /// so AnimationClip.FindRoots cannot recover these names on its own.
    /// </summary>
    public static class GenshinBlendShapeCurveBindings
    {
        public static int Resolve(IEnumerable<FloatCurve> curves, IEnumerable<ImportedMorph> morphs, string modelRootPath = null)
        {
            if (curves == null) throw new ArgumentNullException(nameof(curves));
            if (morphs == null) throw new ArgumentNullException(nameof(morphs));

            var channels = new Dictionary<string, Dictionary<uint, string>>(StringComparer.Ordinal);
            foreach (var morph in morphs)
            {
                if (morph == null || string.IsNullOrEmpty(morph.Path))
                    throw new InvalidOperationException("Exported morph has no renderer path.");
                // FBX mesh paths include the exported model root; native .anim
                // bindings are relative to the Animator GameObject.
                string path = morph.Path;
                if (!string.IsNullOrEmpty(modelRootPath))
                {
                    if (path == modelRootPath) path = string.Empty;
                    else if (path.StartsWith(modelRootPath + "/", StringComparison.Ordinal))
                        path = path.Substring(modelRootPath.Length + 1);
                    else throw new InvalidOperationException("Morph is outside exported model root: " + morph.Path);
                }
                if (!channels.TryGetValue(path, out var byCrc))
                    channels.Add(path, byCrc = new Dictionary<uint, string>());
                foreach (var channel in morph.Channels)
                {
                    if (channel == null || string.IsNullOrEmpty(channel.Name))
                        throw new InvalidOperationException("Exported morph channel has no name: " + morph.Path);
                    uint crc = Crc32(channel.Name);
                    if (byCrc.TryGetValue(crc, out string existing) && existing != channel.Name)
                        throw new InvalidOperationException("Ambiguous morph CRC at " + morph.Path + ": " + crc);
                    byCrc[crc] = channel.Name;
                }
            }

            int resolved = 0;
            foreach (var curve in curves)
            {
                if (curve == null || curve.classID != ClassIDType.SkinnedMeshRenderer ||
                    curve.attribute == null || !curve.attribute.StartsWith("blendShape.", StringComparison.Ordinal))
                    continue;
                string suffix = curve.attribute.Substring("blendShape.".Length);
                if (!uint.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out uint crc))
                    continue; // Already named by the source's own root resolver.
                if (!channels.TryGetValue(curve.path, out var byCrc))
                    continue; // The selected clip can also target a model part not exported here.
                if (!byCrc.TryGetValue(crc, out string name))
                    throw new InvalidOperationException($"Blend-shape CRC {crc} on {curve.path} has no channel in the exported model.");
                curve.attribute = "blendShape." + name;
                resolved++;
            }
            return resolved;
        }

        static uint Crc32(string text)
        {
            uint crc = ~0u;
            foreach (byte value in Encoding.UTF8.GetBytes(text))
            {
                crc ^= value;
                for (int bit = 0; bit < 8; bit++)
                    crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0u);
            }
            return ~crc;
        }
    }
}
