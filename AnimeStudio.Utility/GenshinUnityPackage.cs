using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace AnimeStudio
{
    public static class GenshinUnityPackage
    {
        private static readonly string[] BodyNames = "Hips LeftUpperLeg RightUpperLeg LeftLowerLeg RightLowerLeg LeftFoot RightFoot Spine Chest UpperChest Neck Head LeftShoulder RightShoulder LeftUpperArm RightUpperArm LeftLowerArm RightLowerArm LeftHand RightHand LeftToes RightToes LeftEye RightEye Jaw".Split(' ');
        public static void Write(Avatar avatar, string character, string model, AnimationClip[] clips, string directory)
        {
            var a = avatar.m_Avatar; var h = a.m_Human;
            var names = new Dictionary<int, string>();
            void Map(int index, string name) { if (index >= 0) names.Add(a.m_HumanSkeletonIndexArray[index], name); }
            for (int i = 0; i < BodyNames.Length; i++) Map(h.m_HumanBoneIndex[i], BodyNames[i]);
            foreach (var side in new[] { "Left", "Right" })
            {
                var hand = side == "Left" ? h.m_LeftHand : h.m_RightHand;
                for (int i = 0; i < 15; i++) Map(hand.m_HandBoneIndex[i], side + new[] { "Thumb", "Index", "Middle", "Ring", "Little" }[i / 3] + new[] { "Proximal", "Intermediate", "Distal" }[i % 3]);
            }
            object V(object v) => v switch { Vector3 x => new { x = x.X, y = x.Y, z = x.Z }, Vector4 x => new { x = x.X, y = x.Y, z = x.Z }, _ => throw new InvalidDataException("Invalid Avatar vector") };
            var nodes = a.m_AvatarSkeleton.m_Node.Select((node, i) =>
            {
                string path = avatar.m_TOS[a.m_AvatarSkeleton.m_ID[i]];
                var pose = a.m_AvatarSkeletonPose.m_X[i];
                int humanIndex = Array.IndexOf(a.m_HumanSkeletonIndexArray, i);
                var axes = names.ContainsKey(i) ? h.m_Skeleton.m_AxesArray[h.m_Skeleton.m_Node[humanIndex].m_AxesId] : null;
                return new { path, name = path.Length == 0 ? character : path.Split('/').Last(), parent = node.m_ParentId,
                    human = names.GetValueOrDefault(i, ""), position = V(pose.t), scale = V(pose.s), rotation = new { x = pose.q.X, y = pose.q.Y, z = pose.q.Z, w = pose.q.W },
                    axes = axes == null ? null : new { min = V(axes.m_Limit.m_Min), max = V(axes.m_Limit.m_Max), length = axes.m_Length } };
            }).ToArray();
            var info = new { character, model = Path.GetFileName(model), nodes, humanScale = h.m_Scale,
                armTwist = h.m_ArmTwist, foreArmTwist = h.m_ForeArmTwist, upperLegTwist = h.m_UpperLegTwist, legTwist = h.m_LegTwist,
                armStretch = h.m_ArmStretch, legStretch = h.m_LegStretch, feetSpacing = h.m_FeetSpacing, hasTranslationDoF = h.m_HasTDoF,
                clips = clips.Select(c => new { name = c.Name, file = "Animations/" + Safe(c.Name) + "_" + c.m_PathID + ".anim",
                    body = HasBody(c), duration = c.m_MuscleClip?.m_StopTime ?? 0, rate = c.m_SampleRate }).ToArray() };
            File.WriteAllText(Path.Combine(directory, character + ".character.json"), JsonConvert.SerializeObject(info, Formatting.Indented));
            Directory.CreateDirectory(Path.Combine(directory, "Editor"));
            using var stream = typeof(GenshinUnityPackage).Assembly.GetManifestResourceStream("AnimeStudio.GenshinUnityImport.cs");
            using var reader = new StreamReader(stream ?? throw new InvalidOperationException("Missing Unity importer resource."));
            string importer = "AnimeStudioCharacterImport_" + Guid.NewGuid().ToString("N");
            File.WriteAllText(Path.Combine(directory, "Editor", importer + ".cs"), reader.ReadToEnd().Replace("AnimeStudioCharacterImport", importer));
            string sharedEditor = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(directory)), "Generic", "Editor");
            Directory.CreateDirectory(sharedEditor);
            using var compositionStream = typeof(GenshinUnityPackage).Assembly.GetManifestResourceStream("AnimeStudio.GenshinClipCompositionImport.cs");
            using var compositionReader = new StreamReader(compositionStream ?? throw new InvalidOperationException("Missing composition importer resource."));
            string source = compositionReader.ReadToEnd(), compositionPath = Path.Combine(sharedEditor, "AnimeStudioClipCompositionImport.cs");
            if (File.Exists(compositionPath) && File.ReadAllText(compositionPath) != source)
                throw new IOException("The output parent contains a different composition importer version. Choose a new parent folder.");
            if (!File.Exists(compositionPath)) File.WriteAllText(compositionPath, source);
            File.WriteAllText(Path.Combine(directory, "UNITY-IMPORT.txt"), "Copy this character folder AND its sibling Generic folder under the same parent in Unity Assets. Keep Generic resources and existing .meta files when adding characters. Include Generic/Editor only once per project. The importer creates a Humanoid Avatar, Animator Controller and character prefab. Paired actions use small Rig/*.genshinclip recipes; Unity generates combined clips in Library rather than extra exported .anim files. See unity-import-report.json. Materials are previews; Genshin shaders/VFX simulation are not recreated. Embedded FBX materials are extracted to named Materials/*.mat assets and remapped on the FBX; existing local materials and edits are preserved. Textures and material JSON remain inside each character folder. EffectMesh and mesh objects attached beneath armature bones are inactive by default in both the imported model and prefab; enable their GameObjects to show them. Generic contains named shared body animations and their importer. The FBX and idle prefab use the mesh bind pose; source Avatar calibration remains separate.\n");
        }
        public static bool HasBody(AnimationClip c) => c.m_ClipBindingConstant?.genericBindings.Any(b => b.typeID == ClassIDType.Animator && b.customType == 8 && b.attribute >= 42 && b.attribute < 137) == true;
        private static string Safe(string s) => string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
    }
}
