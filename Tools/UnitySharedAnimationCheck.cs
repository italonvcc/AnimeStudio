// Disposable project only. Assets/Mona is a bounded new package, Assets/Original
// contains the corresponding untouched source .anim files from the prior export.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine.Animations;
using UnityEngine.Playables;

public static class UnitySharedAnimationCheck
{
    [Serializable] public class ClipInfo { public string name, file; public bool body; }
    [Serializable] public class Recipe { public string character; public ClipInfo[] clips; }
    [Serializable] public class Motion { public string name; public float maxPositionError, maxRotationComponentError, maxScaleError, maxVertexError; public int poses, vertices; }
    [Serializable] public class Result { public bool passed; public string error; public int clips, curves, samples, sharedTextureSlots; public float maxCurveError; public Motion[] motions; }
    class Pose { public Vector3[] positions, scales, vertices; public Quaternion[] rotations; }
    static void Require(bool ok, string text) { if (!ok) throw new Exception(text); }
    public static void Run()
    {
        var result = new Result(); var motions = new List<Motion>();
        try
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            foreach (var type in assembly.GetTypes().Where(t => t.Name.StartsWith("AnimeStudioCharacterImport_")))
                type.GetMethod("Run").Invoke(null, null);
            CheckCompositionMetadata();
            var recipe = JsonUtility.FromJson<Recipe>(File.ReadAllText("Assets/Mona/Avatar_Girl_Catalyst_Mona.character.json"));
            var original = AssetDatabase.FindAssets("t:AnimationClip", new[] { "Assets/Original" }).Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<AnimationClip>).ToDictionary(c => c.name);
            string Resolve(string file) => "Assets/" + Path.GetRelativePath(Path.GetFullPath("Assets"), Path.GetFullPath("Assets/Mona/" + file)).Replace('\\','/');
            var exported = recipe.clips.ToDictionary(c => c.name, c => AssetDatabase.LoadAssetAtPath<AnimationClip>(Resolve(c.file)));
            foreach (var pair in exported)
            {
                var before = original[pair.Key]; var after = pair.Value;
                Require(after != null, "Missing imported clip " + pair.Key);
                Require(Math.Abs(before.length-after.length) < .00001f && before.frameRate == after.frameRate, "Clip timing changed: " + pair.Key);
                var bindings = AnimationUtility.GetCurveBindings(before); var newBindings = AnimationUtility.GetCurveBindings(after);
                Require(bindings.ToHashSet().SetEquals(newBindings), "Curve bindings changed: " + pair.Key);
                Require(JsonUtility.ToJson(AnimationUtility.GetAnimationClipSettings(before)) == JsonUtility.ToJson(AnimationUtility.GetAnimationClipSettings(after)), "Clip settings changed: " + pair.Key);
                Require(AnimationUtility.GetAnimationEvents(before).Select(JsonUtility.ToJson).SequenceEqual(AnimationUtility.GetAnimationEvents(after).Select(JsonUtility.ToJson)), "Events changed: " + pair.Key);
                var objectBindings = AnimationUtility.GetObjectReferenceCurveBindings(before);
                Require(objectBindings.ToHashSet().SetEquals(AnimationUtility.GetObjectReferenceCurveBindings(after)), "Object curve bindings changed");
                foreach (var binding in objectBindings)
                {
                    var a = AnimationUtility.GetObjectReferenceCurve(before,binding); var b = AnimationUtility.GetObjectReferenceCurve(after,binding);
                    Require(a.Length == b.Length && a.Zip(b,(x,y)=>x.time == y.time && x.value == y.value).All(v=>v), "Object keys changed");
                }
                foreach (var binding in bindings)
                {
                    var a = AnimationUtility.GetEditorCurve(before,binding); var b = AnimationUtility.GetEditorCurve(after,binding);
                    // Every source key and both quarter-frame offsets, not only authored sample times.
                    foreach (float time in a.keys.SelectMany(k => new[] { k.time, Mathf.Min(before.length,k.time+.25f/before.frameRate), Mathf.Min(before.length,k.time+.75f/before.frameRate) }))
                    {
                        float error = Mathf.Abs(a.Evaluate(time)-b.Evaluate(time)); result.samples++;
                        Require(!float.IsNaN(error) && error <= .00001f, "Curve changed: " + pair.Key + " " + binding.propertyName + " at " + time + ": " + error);
                        result.maxCurveError = Mathf.Max(result.maxCurveError,error);
                    }
                    result.curves++;
                }
                result.clips++;
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Mona/Avatar_Girl_Catalyst_Mona.prefab");
            Require(prefab != null, "No generated prefab");
            Require(!Directory.GetFiles("Assets/Mona/Rig", "*__WithBody.anim").Any(), "Merged clip copies still exist");
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            foreach (var material in renderer.sharedMaterials.Where(m=>m != null))
                if (material.mainTexture != null) { Require(AssetDatabase.GetAssetPath(material.mainTexture).StartsWith("Assets/Mona/Textures/"), "Texture was not bound from the character folder"); result.sharedTextureSlots++; }
            Require(result.sharedTextureSlots > 0, "No character texture bindings");
            foreach (string suffix in new[] { "Standby", "RunCycle", "Attack_01", "ElementalArt", "ElementalBurst", "Show_01" })
            {
                string name = "Ani_" + recipe.character + "_" + suffix;
                var info = recipe.clips.Single(c=>c.name == name);
                AnimationClip reference = original[name]; bool combined = false;
                if (!info.body && original.TryGetValue("Ani_Avatar_Girl_" + suffix, out var body))
                {
                    reference = new AnimationClip { name = "Reference " + suffix, frameRate = body.frameRate }; combined = true;
                    var curves = new Dictionary<EditorCurveBinding,AnimationCurve>();
                    foreach (var clip in new[] {body, original[name]}) foreach (var binding in AnimationUtility.GetCurveBindings(clip)) curves[binding] = AnimationUtility.GetEditorCurve(clip,binding);
                    AnimationUtility.SetEditorCurves(reference,curves.Keys.ToArray(),curves.Values.ToArray());
                    AnimationUtility.SetAnimationClipSettings(reference,AnimationUtility.GetAnimationClipSettings(body));
                }
                var motion = new Motion { name = name };
                try
                {
                    foreach (float normalized in new[] {0f,.01f,.2f,.35f,.65f,.99f})
                    {
                        var a = Sample(prefab,reference,name,normalized); var b = Sample(prefab,null,name,normalized);
                        for (int i=0;i<a.positions.Length;i++)
                        {
                            motion.maxPositionError = Mathf.Max(motion.maxPositionError,Vector3.Distance(a.positions[i],b.positions[i]));
                            motion.maxScaleError = Mathf.Max(motion.maxScaleError,Vector3.Distance(a.scales[i],b.scales[i]));
                            Quaternion qa=a.rotations[i], qb=b.rotations[i]; if (Quaternion.Dot(qa,qb)<0) qb=new Quaternion(-qb.x,-qb.y,-qb.z,-qb.w);
                            motion.maxRotationComponentError=Mathf.Max(motion.maxRotationComponentError,Mathf.Abs(qa.x-qb.x),Mathf.Abs(qa.y-qb.y),Mathf.Abs(qa.z-qb.z),Mathf.Abs(qa.w-qb.w));
                        }
                        for (int i=0;i<a.vertices.Length;i++) motion.maxVertexError = Mathf.Max(motion.maxVertexError,Vector3.Distance(a.vertices[i],b.vertices[i]));
                        motion.poses++; motion.vertices += a.vertices.Length;
                    }
                    motions.Add(motion);
                }
                finally { if (combined) UnityEngine.Object.DestroyImmediate(reference); }
            }
            Require(motions.All(m=>m.maxPositionError < .0001f && m.maxRotationComponentError < .0001f && m.maxScaleError < .0001f && m.maxVertexError < .0001f), "Playback mismatch; see motion measurements");
            result.passed = true;
        }
        catch (Exception e) { result.error=e.ToString(); Debug.LogException(e); }
        finally { result.motions=motions.ToArray(); File.WriteAllText("shared-animation-check.json",JsonUtility.ToJson(result,true)); }
        EditorApplication.Exit(result.passed ? 0 : 1);
    }
    static void CheckCompositionMetadata()
    {
        string root="Assets/CompositionFixture"; Directory.CreateDirectory(root); AssetDatabase.Refresh();
        AnimationClip Make(string name, float value)
        {
            var clip=new AnimationClip {name=name,frameRate=30};
            var a=new Keyframe(0,value,2,3,.2f,.4f) {weightedMode=WeightedMode.Both};
            var b=new Keyframe(1,value+1,4,5,.3f,.1f) {weightedMode=WeightedMode.Both};
            AnimationUtility.SetEditorCurve(clip,EditorCurveBinding.FloatCurve("",typeof(Transform),"m_LocalPosition.x"),new AnimationCurve(a,b));
            AnimationUtility.SetAnimationEvents(clip,new[] {new AnimationEvent {time=.25f,functionName=name,intParameter=17,floatParameter=2.5f,stringParameter="retained"}});
            string path=root+"/"+name+".anim"; var saved=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if(saved==null) AssetDatabase.CreateAsset(clip,path); else {EditorUtility.CopySerialized(clip,saved); UnityEngine.Object.DestroyImmediate(clip); clip=saved; EditorUtility.SetDirty(clip);}
            return clip;
        }
        var body=Make("BodyEvent",1); var secondary=Make("SecondaryEvent",3);
        var reference=AssetDatabase.LoadAssetAtPath<Material>(root+"/Reference.mat");
        if(reference==null) {reference=new Material(Shader.Find("Standard")); AssetDatabase.CreateAsset(reference,root+"/Reference.mat");}
        var objectBinding=EditorCurveBinding.PPtrCurve("",typeof(MeshRenderer),"m_Materials.Array.data[0]");
        AnimationUtility.SetObjectReferenceCurve(secondary,objectBinding,new[] {new ObjectReferenceKeyframe {time=0,value=reference}});
        var settings=AnimationUtility.GetAnimationClipSettings(body); settings.loopTime=true; AnimationUtility.SetAnimationClipSettings(body,settings);
        AssetDatabase.SaveAssets();
        string descriptor=root+"/Composed.genshinclip";
        File.WriteAllText(descriptor,"{\"name\":\"Fixture\",\"body\":\"BodyEvent.anim\",\"secondary\":\"SecondaryEvent.anim\"}");
        AssetDatabase.ImportAsset(descriptor,ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        var result=AssetDatabase.LoadAssetAtPath<AnimationClip>(descriptor); Require(result!=null,"Synthetic composition missing");
        var binding=AnimationUtility.GetCurveBindings(secondary).Single();
        var expected=AnimationUtility.GetEditorCurve(secondary,binding).keys;
        var actual=AnimationUtility.GetEditorCurve(result,binding).keys;
        Require(expected.Length==actual.Length && expected.Zip(actual,(a,b)=>a.time==b.time && a.value==b.value && a.inTangent==b.inTangent && a.outTangent==b.outTangent && a.inWeight==b.inWeight && a.outWeight==b.outWeight && a.weightedMode==b.weightedMode).All(v=>v),"Composition lost tangents/weights or secondary override");
        Require(AnimationUtility.GetAnimationEvents(result).Select(e=>e.functionName).SequenceEqual(new[]{"BodyEvent","SecondaryEvent"}),"Composition lost event ordering");
        Require(AnimationUtility.GetAnimationEvents(result).All(e=>e.intParameter==17 && e.floatParameter==2.5f && e.stringParameter=="retained"),"Composition lost event parameters");
        Require(AnimationUtility.GetObjectReferenceCurve(result,objectBinding).Single().value==reference,"Composition lost object reference");
        Require(AnimationUtility.GetAnimationClipSettings(result).loopTime,"Composition lost loop setting");
    }
    static Pose Sample(GameObject prefab, AnimationClip clip, string state, float normalized)
    {
        var instance = UnityEngine.Object.Instantiate(prefab); var graph=PlayableGraph.Create("Shared motion equivalence"); AnimatorController referenceController = null;
        try
        {
            var animator=instance.GetComponent<Animator>(); animator.cullingMode=AnimatorCullingMode.AlwaysAnimate; animator.fireEvents=false;
            Require(animator.avatar.isValid && animator.avatar.isHuman,"Invalid avatar");
            var controller=animator.runtimeAnimatorController; animator.runtimeAnimatorController=null;
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output=AnimationPlayableOutput.Create(graph,"pose",animator);
            if (clip != null)
            {
                referenceController = new AnimatorController(); referenceController.AddLayer("Base Layer");
                var referenceState=referenceController.layers[0].stateMachine.AddState(state); referenceState.motion=clip; referenceState.writeDefaultValues=true;
                referenceController.layers[0].stateMachine.defaultState=referenceState; controller=referenceController;
            }
            var playable=AnimatorControllerPlayable.Create(graph,controller); output.SetSourcePlayable(playable); graph.Play(); graph.Evaluate(0); playable.Play(state,0,normalized); graph.Evaluate(0);
            var bones=instance.GetComponentsInChildren<Transform>(true); var vertices=new List<Vector3>();
            foreach (var skin in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh=new Mesh(); skin.BakeMesh(mesh); vertices.AddRange(mesh.vertices.Select(skin.transform.TransformPoint)); UnityEngine.Object.DestroyImmediate(mesh);
            }
            Require(vertices.Count>0 && vertices.All(v=>float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z) && v.magnitude<20),"Invalid posed mesh");
            return new Pose {positions=bones.Select(b=>b.localPosition).ToArray(),rotations=bones.Select(b=>b.localRotation).ToArray(),scales=bones.Select(b=>b.localScale).ToArray(),vertices=vertices.ToArray()};
        }
        finally { graph.Destroy(); UnityEngine.Object.DestroyImmediate(instance); if (referenceController != null) UnityEngine.Object.DestroyImmediate(referenceController); }
    }
}
