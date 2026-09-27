using System;
using System.Collections.Generic;

namespace AnimeStudio
{
    // Only reduce wholly constant curves. Preserve every key on moving curves,
    // including flat stretches: Unity's runtime representation depends on topology.
    public static class GenshinConstantCurveReduction
    {
        public static int Reduce<T>(List<Keyframe<T>> keys, Func<T, T, bool> same, Func<T, bool> zero) where T : IYAMLExportable
        {
            if (keys == null || keys.Count < 3) return 0;
            for (int i = 0; i < keys.Count; i++)
                if (!float.IsFinite(keys[i].time) || (i > 0 && keys[i].time <= keys[i - 1].time)) return 0;
            foreach (var key in keys)
                if (key.weightedMode != 0 || !same(keys[0].value,key.value) || !zero(key.inSlope) || !zero(key.outSlope)) return 0;
            int removed = keys.Count - 2;
            keys.RemoveRange(1, removed);
            return removed;
        }

        static bool Same(float a, float b) => float.IsFinite(a) && float.IsFinite(b) && BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);
        public static int Reduce(List<Keyframe<Float>> keys) => Reduce(keys, (a, b) => Same(a.Value, b.Value), a => a.Value == 0);
        public static int Reduce(List<Keyframe<Vector3>> keys) => Reduce(keys, (a, b) => Same(a.X,b.X) && Same(a.Y,b.Y) && Same(a.Z,b.Z), a => a.X == 0 && a.Y == 0 && a.Z == 0);
        public static int Reduce(List<Keyframe<Quaternion>> keys) => Reduce(keys, (a, b) => Same(a.X,b.X) && Same(a.Y,b.Y) && Same(a.Z,b.Z) && Same(a.W,b.W), a => a.X == 0 && a.Y == 0 && a.Z == 0 && a.W == 0);
        public static int Apply(AnimationClip clip)
        {
            int removed = 0;
            foreach (var c in clip.m_FloatCurves)
            {
                // Humanoid root/IK vectors and quaternions are serialized as
                // scalar channels but must retain their common sampling topology.
                string property = c.attribute;
                bool component = property.Length >= 2 && property[^2] == '.' && "xyzw".Contains(property[^1]);
                if (!component) removed += Reduce(c.curve.m_Curve);
            }
            // Keep every key on moving curves: reducing just their flat stretches
            // can change Unity's runtime interpolation despite equal editor samples.
            foreach (var c in clip.m_PositionCurves) removed += Reduce(c.curve.m_Curve);
            foreach (var c in clip.m_ScaleCurves) removed += Reduce(c.curve.m_Curve);
            foreach (var c in clip.m_EulerCurves) removed += Reduce(c.curve.m_Curve);
            foreach (var c in clip.m_RotationCurves) removed += Reduce(c.curve.m_Curve);
            return removed;
        }
    }
}
