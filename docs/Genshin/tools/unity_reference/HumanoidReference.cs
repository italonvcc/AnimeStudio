using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// Research oracle only. Production conversion remains in AnimeStudio.
public static class HumanoidReference
{
    [Serializable] public class Node { public string name, human; public int parent; public Vector3 position, scale; public Quaternion rotation; }
    [Serializable] public class Rig { public Node[] nodes; public float armTwist, foreArmTwist, upperLegTwist, legTwist, humanScale, armStretch, legStretch, feetSpacing; public bool hasTranslationDoF; }
    [Serializable] public class Axes { public string human, name; public int[] muscles; public float[] min, max; public Quaternion pre, post; public Vector3 sign; }
    [Serializable] public class Pose { public float time; public Vector3[] positions; public Quaternion[] rotations; public float[] muscles, evaluatedMuscles; public Vector3 bodyPosition; public Quaternion bodyRotation; }
    [Serializable] public class ClipResult { public string name; public bool human; public float length; public Pose[] poses; }
    [Serializable] public class Result { public string unityVersion, rigSha256; public float sourceScale, rebuiltScale; public bool valid, human; public string[] muscleNames; public Axes[] axes; public ClipResult[] clips; }

    public static void Run()
    {
        try
        {
            var rig = JsonUtility.FromJson<Rig>(File.ReadAllText("Assets/ReferenceInput/rig.json"));
            var nodes = new Transform[rig.nodes.Length];
            for (int i = 0; i < nodes.Length; i++)
            {
                var n = rig.nodes[i]; nodes[i] = new GameObject(n.name).transform;
                if (n.parent >= 0) nodes[i].SetParent(nodes[n.parent], false);
                nodes[i].localPosition = n.position; nodes[i].localRotation = n.rotation; nodes[i].localScale = n.scale;
            }
            var human = rig.nodes.Where(n => !string.IsNullOrEmpty(n.human)).Select(n => new HumanBone {
                boneName = n.name, humanName = HumanTrait.BoneName[(int)Enum.Parse(typeof(HumanBodyBones), n.human)],
                limit = new HumanLimit { useDefaultValues = true }
            }).ToArray();
            var description = new HumanDescription { human = human,
                skeleton = rig.nodes.Select(n => new SkeletonBone { name = n.name, position = n.position, rotation = n.rotation, scale = n.scale }).ToArray(),
                upperArmTwist = rig.armTwist, lowerArmTwist = rig.foreArmTwist, upperLegTwist = rig.upperLegTwist, lowerLegTwist = rig.legTwist,
                armStretch = rig.armStretch, legStretch = rig.legStretch, feetSpacing = rig.feetSpacing, hasTranslationDoF = rig.hasTranslationDoF };
            var avatar = AvatarBuilder.BuildHumanAvatar(nodes[0].gameObject, description);
            if (!avatar.isValid || !avatar.isHuman) throw new Exception("Reference AvatarBuilder did not produce a valid humanoid.");
            var animator = nodes[0].gameObject.AddComponent<Animator>(); animator.avatar = avatar;
            animator.applyRootMotion = true; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            object Invoke(string name, HumanBodyBones bone) => typeof(Avatar).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(avatar, new object[] { (int)bone });
            var axes = rig.nodes.Where(n => !string.IsNullOrEmpty(n.human)).Select(n => {
                var bone = (HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), n.human);
                var indices = Enumerable.Range(0, 3).Select(axis => HumanTrait.MuscleFromBone((int)bone, axis)).ToArray();
                return new Axes { human = n.human, name = n.name, muscles = indices,
                    min = indices.Select(m => m >= 0 ? HumanTrait.GetMuscleDefaultMin(m) : 0).ToArray(),
                    max = indices.Select(m => m >= 0 ? HumanTrait.GetMuscleDefaultMax(m) : 0).ToArray(),
                    pre = (Quaternion)Invoke("GetPreRotation", bone), post = (Quaternion)Invoke("GetPostRotation", bone), sign = (Vector3)Invoke("GetLimitSign", bone) };
            }).ToArray();
            var clips = new List<ClipResult>();
            foreach (var file in Directory.GetFiles("Assets/ReferenceInput", "*.anim"))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(file.Replace('\\', '/'));
                if (clip == null) throw new Exception("Could not import " + file);
                // Sample the closed source interval without looping back to the first frame.
                var settings = AnimationUtility.GetAnimationClipSettings(clip); settings.loopTime = false;
                AnimationUtility.SetAnimationClipSettings(clip, settings);
                var muscleCurves = HumanTrait.MuscleName.Select((name, index) => AnimationUtility.GetEditorCurve(clip,
                    EditorCurveBinding.FloatCurve("", typeof(Animator), index < 55 ? name : FingerAttribute(name)))).ToArray();
                var graph = PlayableGraph.Create("HumanoidReference"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                try
                {
                    var playable = AnimationClipPlayable.Create(graph, clip); playable.SetApplyFootIK(false); playable.SetApplyPlayableIK(false);
                    var output = AnimationPlayableOutput.Create(graph, "Animation", animator); output.SetSourcePlayable(playable);
                    graph.Play();
                    var poses = new List<Pose>();
                    using var poseHandler = new HumanPoseHandler(avatar, nodes[0]);
                    foreach (float fraction in new[] { 0f, .25f, .5f, .75f, 1f })
                    {
                        animator.Rebind();
                        float time = Mathf.Round(clip.length * fraction * clip.frameRate) / clip.frameRate;
                        playable.SetTime(time); graph.Evaluate(0);
                        var evaluated = new HumanPose(); poseHandler.GetHumanPose(ref evaluated);
                        poses.Add(new Pose { time = time, positions = nodes.Select(t => t.localPosition).ToArray(), rotations = nodes.Select(t => t.localRotation).ToArray(),
                            muscles = muscleCurves.Select(c => c?.Evaluate(time) ?? 0f).ToArray(), evaluatedMuscles = evaluated.muscles,
                            bodyPosition = evaluated.bodyPosition, bodyRotation = evaluated.bodyRotation });
                    }
                    clips.Add(new ClipResult { name = clip.name, human = clip.isHumanMotion, length = clip.length, poses = poses.ToArray() });
                }
                finally { graph.Destroy(); }
            }
            var result = new Result { unityVersion = Application.unityVersion, sourceScale = rig.humanScale, rebuiltScale = animator.humanScale,
                valid = avatar.isValid, human = avatar.isHuman, muscleNames = HumanTrait.MuscleName, axes = axes, clips = clips.ToArray() };
            using (var sha = System.Security.Cryptography.SHA256.Create())
                result.rigSha256 = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes("Assets/ReferenceInput/rig.json"))).Replace("-", "").ToLowerInvariant();
            File.WriteAllText("reference-result.json", JsonUtility.ToJson(result, true));
            Debug.Log("REFERENCE_COMPLETE " + clips.Count);
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    private static string FingerAttribute(string name)
    {
        var words = name.Split(' ');
        return words[0] + "Hand." + words[1] + "." + string.Join(" ", words.Skip(2));
    }
}
