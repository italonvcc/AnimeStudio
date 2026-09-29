// Copy into an ignored scratch project's Assets/Editor. Uses the generated prefab and native clips.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.Animations;
using UnityEngine.Playables;

public static class UnityCharacterPlaybackCheck
{
    [Serializable] public class Motion { public string clip; public int movingBones, movingFingers; public float height; }
    [Serializable] public class Result { public bool validAvatar; public int meshes; public Motion[] motions; }
    public static void Run()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Mona/Avatar_Girl_Catalyst_Mona.prefab");
        if (prefab == null) throw new Exception("No generated character prefab.");
        var clips = AssetDatabase.FindAssets("t:AnimationClip", new[] { "Assets/Mona" }).Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<AnimationClip>).Where(c => c != null).ToArray();
        var motions = new List<Motion>(); int meshes = 0;
        foreach (string suffix in new[] { "Standby__WithBody", "RunCycle__WithBody", "Attack_01", "ElementalArt", "ElementalBurst" })
        {
            var clip = clips.First(c => c.name == "Ani_Avatar_Girl_Catalyst_Mona_" + suffix);
            var instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var animator = instance.GetComponent<Animator>();
                if (animator == null || !animator.avatar.isValid || !animator.avatar.isHuman) throw new Exception("Invalid prefab Avatar");
                animator.runtimeAnimatorController = null; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var graph = PlayableGraph.Create("Native clip validation"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                try
                {
                    var playable = AnimationClipPlayable.Create(graph, clip); playable.SetApplyFootIK(false); playable.SetApplyPlayableIK(false);
                    AnimationPlayableOutput.Create(graph, "Character", animator).SetSourcePlayable(playable); graph.Play();
                    var bones = instance.GetComponentsInChildren<Transform>(true);
                    playable.SetTime(clip.length * .2f); graph.Evaluate(0);
                    var positions = bones.Select(b => b.localPosition).ToArray(); var rotations = bones.Select(b => b.localRotation).ToArray();
                    playable.SetTime(clip.length * .65f); graph.Evaluate(0);
                    var moving = bones.Where((b,i) => Vector3.Distance(b.localPosition, positions[i]) > .00001f || Quaternion.Angle(b.localRotation, rotations[i]) > .01f).ToArray();
                    var skinned = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true); meshes = skinned.Length;
                    var bounds = new Bounds(instance.transform.position, Vector3.zero);
                    foreach (var skin in skinned)
                    {
                        var mesh = new Mesh(); skin.BakeMesh(mesh);
                        foreach (var vertex in mesh.vertices)
                        {
                            Vector3 point = skin.transform.TransformPoint(vertex);
                            if (float.IsNaN(point.x) || float.IsInfinity(point.x) || point.magnitude > 20) throw new Exception("Invalid posed skin vertex: " + clip.name);
                            bounds.Encapsulate(point);
                        }
                        UnityEngine.Object.DestroyImmediate(mesh);
                    }
                    int fingers = moving.Count(b => b.name.Contains("Finger") || b.name.Contains("Thumb") || b.name.Contains("Index") || b.name.Contains("Middle") || b.name.Contains("Ring") || b.name.Contains("Pinky"));
                    if (moving.Length < 10 || skinned.Length == 0) throw new Exception("Insufficient rig motion: " + clip.name);
                    motions.Add(new Motion { clip = clip.name, movingBones = moving.Length, movingFingers = fingers, height = bounds.size.y });
                }
                finally { graph.Destroy(); }
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }
        File.WriteAllText("native-playback-check.json", JsonUtility.ToJson(new Result { validAvatar = true, meshes = meshes, motions = motions.ToArray() }, true));
        Debug.Log("PASS native Unity clip playback and finite posed skins in five animation categories.");
    }
}
