using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using M = System.Numerics.Matrix4x4;
using Q = System.Numerics.Quaternion;
using V = System.Numerics.Vector3;

namespace AnimeStudio
{
    public static class GenshinBindPose
    {
        // Work on converted data only. Source transforms, skin weights, inverse
        // binds and Avatar calibration metadata remain intact.
        public static int Restore(IImported model, Avatar avatar = null)
        {
            var frames = new List<ImportedFrame>();
            void Visit(ImportedFrame f) { frames.Add(f); for (int i = 0; i < f.Count; i++) Visit(f[i]); }
            Visit(model.RootFrame);
            var byPath = frames.ToDictionary(f => f.Path, StringComparer.Ordinal);
            var worlds = new Dictionary<ImportedFrame, M>();
            foreach (var f in frames) worlds[f] = Local(f) * (f.Parent == null ? M.Identity : worlds[f.Parent]);
            // Skin weights do not reference every armature/helper bone. Restore
            // those from the source default pose (not humanoid calibration).
            // Keep original renderer worlds above as the skin constraint basis.
            if (avatar?.m_Avatar.m_DefaultPose is { } pose)
            {
                var skeleton = avatar.m_Avatar.m_AvatarSkeleton;
                if (pose.m_X.Length != skeleton.m_ID.Length)
                    throw new InvalidDataException("Avatar default pose does not match its skeleton.");
                for (int i = 0; i < skeleton.m_ID.Length; i++)
                {
                    if (!avatar.m_TOS.TryGetValue(skeleton.m_ID[i], out var path) || path.Length == 0 ||
                        !byPath.TryGetValue(model.RootFrame.Path + "/" + path, out var f)) continue;
                    var p = pose.m_X[i]; var t = (Vector3)p.t; var q = p.q; var s = (Vector3)p.s;
                    f.LocalPosition = new Vector3(-t.X, t.Y, t.Z);
                    f.LocalRotation = new Quaternion(q.X, -q.Y, -q.Z, q.W);
                    f.LocalScale = s;
                }
            }
            var desired = new Dictionary<ImportedFrame, M>();
            void Constrain(ImportedFrame frame, M world)
            {
                if (desired.TryGetValue(frame, out var other) && Error(other, world) > .001f)
                    throw new InvalidDataException("Conflicting mesh bind poses: " + frame.Path);
                desired[frame] = world;
            }
            // Skin matrices are relative to the skinned renderer's frame. Rigid
            // attachments must instead follow their restored parent bones.
            foreach (var mesh in model.MeshList.Where(m => m.BoneList?.Count > 0))
                Constrain(byPath[mesh.Path], worlds[byPath[mesh.Path]]);
            var bones = new HashSet<ImportedFrame>();
            foreach (var mesh in model.MeshList)
            foreach (var bone in mesh.BoneList ?? Enumerable.Empty<ImportedBone>())
            {
                if (string.IsNullOrEmpty(bone.Path) || !byPath.TryGetValue(bone.Path, out var frame))
                    throw new InvalidDataException("Missing bind-pose bone: " + bone.Path);
                var b = bone.Matrix;
                // Imported skin matrices already carry translation in M30..M32.
                // Match the FBX wrapper's matrix convention (Numerics row vectors).
                var bind = new M(b.M00,b.M01,b.M02,b.M03, b.M10,b.M11,b.M12,b.M13,
                    b.M20,b.M21,b.M22,b.M23, b.M30,b.M31,b.M32,b.M33);
                if (!M.Invert(bind, out var inverse)) throw new InvalidDataException("Singular bind matrix: " + bone.Path);
                Constrain(frame, inverse * worlds[byPath[mesh.Path]]);
                bones.Add(frame);
            }
            foreach (var f in frames)
            {
                var parent = f.Parent == null ? M.Identity : worlds[f.Parent];
                if (desired.TryGetValue(f, out var target))
                {
                    if (!M.Invert(parent, out var inverse)) throw new InvalidDataException("Singular parent: " + f.Path);
                    var local = target * inverse;
                    if (!M.Decompose(local, out var scale, out var rotation, out var position) ||
                        Error(local, M.CreateScale(scale) * M.CreateFromQuaternion(rotation) * M.CreateTranslation(position)) > .001f)
                        throw new InvalidDataException("Unrepresentable bind transform: " + f.Path + " local=" + local + " parent=" + parent + " target=" + target);
                    f.LocalPosition = new Vector3(position.X, position.Y, position.Z);
                    f.LocalRotation = new Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W);
                    f.LocalScale = new Vector3(scale.X, scale.Y, scale.Z);
                }
                worlds[f] = Local(f) * parent;
            }
            return bones.Count;
        }
        private static M Local(ImportedFrame f) => M.CreateScale(f.LocalScale.X, f.LocalScale.Y, f.LocalScale.Z) *
            M.CreateFromQuaternion(new Q(f.LocalRotation.X, f.LocalRotation.Y, f.LocalRotation.Z, f.LocalRotation.W)) *
            M.CreateTranslation(f.LocalPosition.X, f.LocalPosition.Y, f.LocalPosition.Z);
        private static float Error(M a, M b)
        {
            float max = 0;
            for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) {
                float error = Math.Abs(a[r,c] - b[r,c]);
                if (!float.IsFinite(error)) return float.PositiveInfinity;
                max = Math.Max(max, error);
            }
            return max;
        }
    }
}
