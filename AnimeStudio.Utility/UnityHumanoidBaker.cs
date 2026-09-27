using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AnimeStudio
{
    /// <summary>Optional local Unity Editor backend. The regular .anim and FBX exporters remain independent.</summary>
    public static class UnityHumanoidBaker
    {
        private static readonly string[] BodyNames = "Hips LeftUpperLeg RightUpperLeg LeftLowerLeg RightLowerLeg LeftFoot RightFoot Spine Chest UpperChest Neck Head LeftShoulder RightShoulder LeftUpperArm RightUpperArm LeftLowerArm RightLowerArm LeftHand RightHand LeftToes RightToes LeftEye RightEye Jaw".Split(' ');

        public static object Bake(Avatar avatar, AnimationClip[] clips, string destination, string editor, ModelConverter model, IReadOnlyDictionary<long, long> layers = null)
        {
            if (!File.Exists(editor)) throw new FileNotFoundException("Select an installed Unity Editor executable.", editor);
            if (avatar == null || clips.Length == 0) throw new ArgumentException("Humanoid baking needs the Animator's Avatar and selected clips.");
            var overlayIndices = new Dictionary<int, int>();
            bool HasBody(AnimationClip c) => c.m_ClipBindingConstant.genericBindings.Any(b => b.typeID == ClassIDType.Animator && b.customType == 8 && b.attribute >= 42 && b.attribute < 137);
            if (layers != null) foreach (var pair in layers)
            {
                var body = clips.Select((c, i) => (c, i)).Single(x => x.c.m_PathID == pair.Key);
                var secondary = clips.Select((c, i) => (c, i)).Single(x => x.c.m_PathID == pair.Value);
                if (!HasBody(body.c) || HasBody(secondary.c) || body.i == secondary.i) throw new ArgumentException("Each layer pair requires one humanoid body clip and one secondary-only clip.");
                if (Math.Abs(body.c.m_SampleRate - secondary.c.m_SampleRate) > .0001f || Math.Abs(body.c.m_MuscleClip.m_StopTime - secondary.c.m_MuscleClip.m_StopTime) > .0001f)
                    throw new ArgumentException("Explicit animation layers must have matching sample rate and authored duration.");
                overlayIndices.Add(body.i, secondary.i);
            }
            string project = Path.Combine(destination, "BakeWorkspace");
            string input = Path.Combine(project, "Assets", "Input");
            Directory.CreateDirectory(input);
            Directory.CreateDirectory(Path.Combine(project, "Assets", "Editor"));
            Directory.CreateDirectory(Path.Combine(project, "Packages"));
            Directory.CreateDirectory(Path.Combine(project, "ProjectSettings"));
            File.WriteAllText(Path.Combine(project, "Packages", "manifest.json"), "{\"dependencies\":{\"com.unity.modules.animation\":\"1.0.0\",\"com.unity.modules.jsonserialize\":\"1.0.0\"}}");
            string version = FileVersionInfo.GetVersionInfo(editor).ProductVersion?.Split('_')[0];
            if (string.IsNullOrEmpty(version)) throw new InvalidDataException("Cannot identify the selected Editor version.");
            File.WriteAllText(Path.Combine(project, "ProjectSettings", "ProjectVersion.txt"), "m_EditorVersion: " + version + "\n");
            using (var resource = typeof(UnityHumanoidBaker).Assembly.GetManifestResourceStream("AnimeStudio.UnityHumanoidBakeEditor.cs"))
            using (var reader = new StreamReader(resource ?? throw new InvalidOperationException("Missing Unity bake script resource.")))
                File.WriteAllText(Path.Combine(project, "Assets", "Editor", "AnimeStudioBake.cs"), reader.ReadToEnd());

            var a = avatar.m_Avatar; var h = a.m_Human;
            var names = new Dictionary<int, string>();
            void Map(int humanIndex, string name)
            {
                if (humanIndex >= 0) names.Add(a.m_HumanSkeletonIndexArray[humanIndex], name);
            }
            for (int i = 0; i < BodyNames.Length; i++) Map(h.m_HumanBoneIndex[i], BodyNames[i]);
            foreach (var side in new[] { "Left", "Right" })
            {
                var hand = side == "Left" ? h.m_LeftHand : h.m_RightHand;
                for (int i = 0; i < 15; i++) Map(hand.m_HandBoneIndex[i], side + new[] { "Thumb", "Index", "Middle", "Ring", "Little" }[i / 3] + new[] { "Proximal", "Intermediate", "Distal" }[i % 3]);
            }
            object Vector(object v) => v switch
            {
                Vector3 x => new { x = x.X, y = x.Y, z = x.Z },
                Vector4 x => new { x = x.X, y = x.Y, z = x.Z },
                _ => throw new InvalidDataException("Invalid Avatar vector.")
            };
            var nodes = a.m_AvatarSkeleton.m_Node.Select((node, i) =>
            {
                string path = avatar.m_TOS[a.m_AvatarSkeleton.m_ID[i]];
                var pose = a.m_AvatarSkeletonPose.m_X[i];
                int humanIndex = Array.IndexOf(a.m_HumanSkeletonIndexArray, i);
                var axes = names.ContainsKey(i) ? h.m_Skeleton.m_AxesArray[h.m_Skeleton.m_Node[humanIndex].m_AxesId] : null;
                object Q(Vector4 q) => new { x = q.X, y = q.Y, z = q.Z, w = q.W };
                return new { path, name = path.Length == 0 ? "BakeRoot" : path.Split('/').Last(), parent = node.m_ParentId,
                    human = names.GetValueOrDefault(i, ""), position = Vector(pose.t), scale = Vector(pose.s),
                    axes = axes == null ? null : new { pre = Q(axes.m_PreQ), post = Q(axes.m_PostQ), sign = Vector(axes.m_Sgn), min = Vector(axes.m_Limit.m_Min), max = Vector(axes.m_Limit.m_Max), length = axes.m_Length },
                    rotation = new { x = pose.q.X, y = pose.q.Y, z = pose.q.Z, w = pose.q.W } };
            }).ToArray();
            if (nodes.Select(n => n.name).Distinct().Count() != nodes.Length)
                throw new NotSupportedException("Unity AvatarBuilder requires unique bone names for this backend.");
            for (int i = 0; i < clips.Length; i++) File.WriteAllText(Path.Combine(input, $"clip-{i}.anim"), clips[i].Convert());
            var job = new { nodes, humanScale = h.m_Scale, armTwist = h.m_ArmTwist, foreArmTwist = h.m_ForeArmTwist,
                upperLegTwist = h.m_UpperLegTwist, legTwist = h.m_LegTwist, armStretch = h.m_ArmStretch,
                legStretch = h.m_LegStretch, feetSpacing = h.m_FeetSpacing, hasTranslationDoF = h.m_HasTDoF,
                clips = clips.Select((c, i) => new { file = $"Assets/Input/clip-{i}.anim", name = c.Name,
                    overlay = overlayIndices.TryGetValue(i, out var overlay) ? $"Assets/Input/clip-{overlay}.anim" : null,
                    sampleRate = c.m_SampleRate, stopTime = c.m_MuscleClip.m_StopTime }).ToArray() };
            File.WriteAllText(Path.Combine(input, "job.json"), JsonConvert.SerializeObject(job));
            var start = new ProcessStartInfo(editor) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = project };
            foreach (var arg in new[] { "-batchmode", "-quit", "-nographics", "-projectPath", project, "-executeMethod", "AnimeStudioBake.Run", "-logFile", Path.Combine(project, "bake.log") }) start.ArgumentList.Add(arg);
            Logger.Info("Baking humanoid poses with Unity " + version + ". See BakeWorkspace/bake.log for progress.");
            using var process = Process.Start(start) ?? throw new IOException("Could not start Unity.");
            if (!process.WaitForExit(600000)) { process.Kill(true); throw new TimeoutException("Unity bake exceeded ten minutes. Inspect BakeWorkspace/bake.log."); }
            string resultPath = Path.Combine(project, "baked.json");
            if (process.ExitCode != 0 || !File.Exists(resultPath)) throw new IOException("Unity bake failed. Inspect BakeWorkspace/bake.log; no baked FBX was written.");
            var result = JObject.Parse(File.ReadAllText(resultPath));
            if (Math.Abs((double)result["humanScale"] - h.m_Scale) > .0001) throw new InvalidDataException("Rebuilt Avatar scale differs from the source.");
            var summaries = new List<object>();
            foreach (var clip in result["clips"])
            {
                int index = (int)clip["index"];
                var animation = model.AnimationList.Single(x => x.Name == clips[index].Name);
                if (!(bool)clip["human"]) { summaries.Add(new { index, name = clips[index].Name, status = "No humanoid body curves; original Transform tracks retained" }); continue; }
                string composite = (string)clip["composite"];
                if (!string.IsNullOrEmpty(composite))
                {
                    animation.Name = (string)clip["name"];
                    Directory.CreateDirectory(Path.Combine(destination, "Animations"));
                    File.Copy(Path.Combine(project, composite), Path.Combine(destination, "Animations", Path.GetFileName(composite)));
                    var secondary = model.AnimationList.Single(x => x.Name == clips[overlayIndices[index]].Name);
                    foreach (var track in secondary.TrackList.Where(t => t.BlendShape != null))
                    {
                        var existing = animation.FindTrack(track.Path, track.BlendShape.ChannelName);
                        existing.BlendShape = track.BlendShape;
                    }
                }
                var tracks = nodes.Select(n => n.path.Length == 0 ? model.RootFrame : model.RootFrame.FindRelativeFrameWithPath(n.path)).ToArray();
                var absent = nodes.Select((n, i) => new { node = n, index = i }).Where(n => tracks[n.index] == null).ToArray();
                if (absent.Any(n => n.node.human.Length > 0 || nodes.Where((_, i) => tracks[i] != null).Any(p => p.path.StartsWith(n.node.path + "/", StringComparison.Ordinal))))
                    throw new InvalidDataException("Baked humanoid bones or their ancestors are absent from the exported model.");
                for (int i = 0; i < nodes.Length; i++)
                {
                    if (tracks[i] == null) continue; // Avatar may also describe unselected LOD mesh nodes.
                    var track = animation.FindTrack(tracks[i].Path);
                    track.Rotations.Clear(); track.EulerRotations.Clear(); track.Translations.Clear(); track.Scalings.Clear();
                    foreach (var sample in clip["poses"])
                    {
                        float time = (float)sample["time"]; var p = sample["positions"][i]; var q = sample["rotations"][i]; var s = sample["scales"][i];
                        float F(JToken value) { float x = (float)value; if (!float.IsFinite(x)) throw new InvalidDataException("Nonfinite baked transform."); return x; }
                        track.Translations.Add(new ImportedKeyframe<Vector3>(time, new Vector3(-F(p["x"]), F(p["y"]), F(p["z"]))));
                        track.Rotations.Add(new ImportedKeyframe<Quaternion>(time, new Quaternion(F(q["x"]), -F(q["y"]), -F(q["z"]), F(q["w"]))));
                        track.Scalings.Add(new ImportedKeyframe<Vector3>(time, new Vector3(F(s["x"]), F(s["y"]), F(s["z"]))));
                    }
                }
                summaries.Add(new { index, name = animation.Name, bodyPathID = clips[index].m_PathID.ToString(), secondaryPathID = overlayIndices.TryGetValue(index, out var secondaryIndex) ? clips[secondaryIndex].m_PathID.ToString() : null,
                    compositeAnim = string.IsNullOrEmpty(composite) ? null : "Animations/" + Path.GetFileName(composite), status = "Humanoid body and source Transform curves baked", samples = clip["poses"].Count(),
                    maxBodyPositionError = (float)clip["maxBodyPositionError"], maxBodyAngleError = (float)clip["maxBodyAngleError"],
                    unresolvedTransformPaths = clip["unresolvedPaths"], absentAvatarNodes = absent.Select(n => n.node.path).ToArray() });
            }
            return new { backend = "Unity", editorVersion = (string)result["unityVersion"], rootMode = "Authored RootT/RootQ body transform; no scene/controller root-motion extraction or IK", clips = summaries };
        }
    }
}
