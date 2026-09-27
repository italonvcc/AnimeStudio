using System;
using Q = System.Numerics.Quaternion;

namespace AnimeStudio
{
    /// <summary>
    /// Converts one humanoid joint's signed muscle values to a Unity-space local rotation.
    /// This is a building block, not a complete humanoid baker: hips/body-root placement,
    /// hierarchy mapping, sampling, and optional IK must be handled by the caller.
    /// </summary>
    public static class HumanoidRotationMath
    {
        public static Quaternion Solve(Axes axes, Vector3 muscles, float twistWeight, out Quaternion childTwist)
        {
            if (axes?.m_Limit == null) throw new ArgumentException("A joint requires Avatar axes and limits.", nameof(axes));
            if (!float.IsFinite(twistWeight) || twistWeight < 0 || twistWeight > 1)
                throw new ArgumentOutOfRangeException(nameof(twistWeight));
            var sign = Vector(axes.m_Sgn);
            var min = Vector(axes.m_Limit.m_Min);
            var max = Vector(axes.m_Limit.m_Max);
            var angles = new Vector3();
            for (int i = 0; i < 3; i++)
            {
                if (!float.IsFinite(muscles[i]) || !float.IsFinite(sign[i]) || !float.IsFinite(min[i]) || !float.IsFinite(max[i]) || min[i] > 0 || max[i] < 0)
                    throw new ArgumentException("Muscles, signs and limits must be finite; limits must bracket zero.");
                // Muscle values may exceed [-1, 1]; these ranges are not clamps.
                angles[i] = muscles[i] * (muscles[i] >= 0 ? max[i] : -min[i]) * sign[i];
                if (!float.IsFinite(angles[i])) throw new ArgumentException("Muscle angle overflow.");
            }
            var pre = Normalize(axes.m_PreQ);
            var post = Normalize(axes.m_PostQ);
            var inversePost = Q.Conjugate(post);
            // The two swing components use half-angle tangents, not Euler angles or
            // an exponential-map angle-axis vector. Validated against Unity's solver.
            double y = Math.Tan(angles.Y * .5), z = Math.Tan(angles.Z * .5);
            double norm = Math.Sqrt(1 + y * y + z * z);
            var swing = new Q(0, (float)(y / norm), (float)(z / norm), (float)(1 / norm));
            var rotation = Q.Normalize(pre * swing * Twist(angles.X * twistWeight) * inversePost);
            // Apply this in the following joint's parent space. Intervening bones
            // require a basis conversion; callers must not blindly skip them.
            var remainder = Q.Normalize(post * Twist(angles.X * (1 - twistWeight)) * inversePost);
            childTwist = Convert(remainder);
            return Convert(rotation);
        }

        public static Quaternion ApplyParentTwist(Quaternion parentTwist, Quaternion localRotation)
        {
            var a = Normalize(new Vector4(parentTwist.X, parentTwist.Y, parentTwist.Z, parentTwist.W));
            var b = Normalize(new Vector4(localRotation.X, localRotation.Y, localRotation.Z, localRotation.W));
            return Convert(Q.Normalize(a * b));
        }

        private static Q Twist(float angle) => new Q((float)Math.Sin(angle * .5), 0, 0, (float)Math.Cos(angle * .5));
        private static Quaternion Convert(Q q) => new Quaternion(q.X, q.Y, q.Z, q.W);
        private static Q Normalize(Vector4 value)
        {
            var q = new Q(value.X, value.Y, value.Z, value.W);
            if (!float.IsFinite(q.LengthSquared()) || q.LengthSquared() < 1e-12f)
                throw new ArgumentException("Avatar axis quaternions must be finite and nonzero.");
            return Q.Normalize(q);
        }
        private static Vector3 Vector(object value) => value switch
        {
            Vector3 v => v,
            Vector4 v => new Vector3(v.X, v.Y, v.Z),
            _ => throw new ArgumentException("Expected a three- or four-component Avatar vector.")
        };
    }
}
