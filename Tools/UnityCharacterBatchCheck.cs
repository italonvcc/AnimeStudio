// Copy into an Editor test assembly in a disposable project, alongside source-pose fixtures.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine.TestTools;

public class UnityCharacterBatchCheck
{
    [Serializable] public class Pose { public string path; public Vector3 position, scale; public Quaternion rotation; }
    [Serializable] public class Clip { public string name, file; public float duration; }
    [Serializable] public class Recipe { public string character, model; public Clip[] clips; }
    [Serializable] public class Evidence { public Pose[] defaults, source; }
    static string[] Recipes => Directory.GetFiles("Assets", "*.character.json", SearchOption.AllDirectories);
    static Recipe Read(string p) => JsonUtility.FromJson<Recipe>(File.ReadAllText(p));
    static string Prefab(string p) => (Path.GetDirectoryName(p)+"/"+Read(p).character+".prefab").Replace('\\','/');

    [UnityTest] public IEnumerator BatchImportRestAndPlayback()
    {
        Assert.AreEqual(2, Recipes.Length);
        double deadline=EditorApplication.timeSinceStartup+180;
        while (Recipes.Any(p=>AssetDatabase.LoadAssetAtPath<GameObject>(Prefab(p))==null)) {
            Assert.Less(EditorApplication.timeSinceStartup,deadline,"Both prefabs must be generated automatically"); yield return null;
        }
        foreach(string path in Recipes) {
            var recipe=Read(path); string folder=Path.GetDirectoryName(path).Replace('\\','/');
            var evidence=JsonUtility.FromJson<Evidence>(File.ReadAllText("Assets/Tests/"+recipe.character+".json"));
            foreach(string asset in new[]{folder+"/"+recipe.model,Prefab(path)}) CheckRest(asset,evidence);
            foreach(var info in recipe.clips) {
                string file=Path.GetFullPath(Path.Combine(folder,info.file));
                string relative="Assets/"+Path.GetRelativePath(Path.GetFullPath("Assets"),file).Replace('\\','/');
                var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(relative);
                Assert.NotNull(clip,info.name); Assert.That(clip.length,Is.EqualTo(info.duration).Within(.002f),info.name);
                Assert.Greater(AnimationUtility.GetCurveBindings(clip).Length,0,info.name+" must contain decoded curves");
            }
            var go=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab(path))); go.name=recipe.character;
            var animator=go.GetComponent<Animator>();
            var controller=(AnimatorController)animator.runtimeAnimatorController;
            Assert.AreEqual("Ani_"+recipe.character+"_Standby",controller.layers[0].stateMachine.defaultState.name);
            SessionState.SetString("BatchController:"+folder,File.ReadAllText(folder+"/Rig/Character.controller"));
        }
        yield return new EnterPlayMode();
        // Keep captured locals in a fresh iterator: the EditMode runner restores
        // the program counter across domain reload, not compiler closure objects.
        yield return CheckPlayback();
        yield return new ExitPlayMode();
        foreach(var path in Recipes) {
            string folder=Path.GetDirectoryName(path).Replace('\\','/');
            Assert.AreEqual(SessionState.GetString("BatchController:"+folder,""),File.ReadAllText(folder+"/Rig/Character.controller"),"Play-mode reload must not rewrite controllers");
        }
        Debug.Log("PASS: both full libraries, automatic prefabs, skin/bone/attachment rest checks and full attack playback");
    }
    static IEnumerator CheckPlayback()
    {
        yield return null;
        Assert.IsTrue(Application.isPlaying);
        var animators=UnityEngine.Object.FindObjectsByType<Animator>(FindObjectsSortMode.None).Where(a=>a.name.StartsWith("Avatar_")).ToArray();
        Assert.AreEqual(2,animators.Length);
        foreach(var animator in animators) {
            Assert.IsTrue(animator.isHuman); Assert.IsTrue(animator.enabled);
            Assert.IsTrue(animator.GetCurrentAnimatorStateInfo(0).IsName("Ani_"+animator.name+"_Standby"));
        }
        yield return ObserveMotion(animators,1.1f,"Standby");
        foreach(var animator in animators) {
            animator.Play("Ani_"+animator.name+"_Attack_01",0,0);
        }
        yield return null;
        yield return ObserveMotion(animators,1.01f,"Attack_01");
    }
    static IEnumerator ObserveMotion(Animator[] animators,float cycles,string action)
    {
        var starts=animators.Select(a=>a.GetComponentsInChildren<Transform>(true).Select(t=>t.localRotation).ToArray()).ToArray();
        var movement=new int[animators.Length];
        var lengths=animators.Select(a=>a.GetCurrentAnimatorStateInfo(0).length).ToArray();
        float started=Time.time, next=Time.time;
        while(Time.time-started<lengths.Max()*cycles+.3f) {
            yield return null;
            if(Time.time<next) continue; next=Time.time+.15f;
            for(int i=0;i<animators.Length;i++) {
                var bones=animators[i].GetComponentsInChildren<Transform>(true);
                if(bones.Where((t,j)=>Quaternion.Angle(t.localRotation,starts[i][j])>.1f).Any()) movement[i]++;
                starts[i]=bones.Select(t=>t.localRotation).ToArray();
            }
        }
        for(int i=0;i<animators.Length;i++) {
            Assert.Greater(animators[i].GetCurrentAnimatorStateInfo(0).normalizedTime,cycles-.01f,animators[i].name+" reaches end of "+action);
            Assert.Greater(movement[i],4,animators[i].name+" animates across multiple frames");
            Debug.Log(animators[i].name+" "+action+": "+movement[i]+" moving samples, normalized time "+animators[i].GetCurrentAnimatorStateInfo(0).normalizedTime);
        }
    }
    static void CheckRest(string path,Evidence evidence)
    {
        var go=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
        try {
            foreach(var a in go.GetComponentsInChildren<Animator>(true)) a.enabled=false;
            var weighted=new HashSet<Transform>(); int vertices=0,helpers=0,rigid=0;
            foreach(var r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
                var mesh=r.sharedMesh;
                for(int i=0;i<mesh.bindposes.Length;i++) {
                    weighted.Add(r.bones[i]); var m=r.transform.worldToLocalMatrix*r.bones[i].localToWorldMatrix*mesh.bindposes[i];
                    for(int j=0;j<16;j++) Assert.That(m[j],Is.EqualTo(Matrix4x4.identity[j]).Within(.001f),path+" "+r.bones[i].name);
                }
                var baked=new Mesh(); r.BakeMesh(baked);
                var original=mesh.vertices; var actual=baked.vertices;
                for(int i=0;i<mesh.vertexCount;i++) Assert.Less(Vector3.Distance(original[i],actual[i]),.0001f,path+" rest vertices");
                vertices+=mesh.vertexCount; UnityEngine.Object.DestroyImmediate(baked);
            }
            foreach(var pose in evidence.defaults.Where(p=>p.path.Length>0)) {
                var bone=go.transform.Find(pose.path); if(bone==null || weighted.Contains(bone) || bone.GetComponent<Renderer>()!=null) continue;
                CheckLocal(bone,pose,path); helpers++;
            }
            foreach(var r in go.GetComponentsInChildren<MeshRenderer>(true)) {
                string relative=AnimationUtility.CalculateTransformPath(r.transform,go.transform);
                var pose=evidence.defaults.FirstOrDefault(p=>p.path==relative)??evidence.source.Single(p=>p.path==relative);
                CheckLocal(r.transform,pose,path); rigid++;
            }
            Assert.Greater(vertices,0); Assert.Greater(helpers,0); Assert.Greater(rigid,0);
            Debug.Log(path+": "+vertices+" rest vertices, "+weighted.Count+" skin bones, "+helpers+" other bones, "+rigid+" rigid meshes");
        } finally {UnityEngine.Object.DestroyImmediate(go);}
    }
    static void CheckLocal(Transform t,Pose p,string asset) {
        Assert.Less(Vector3.Distance(t.localPosition,p.position),.001f,asset+" "+p.path+" position");
        Assert.Less(Quaternion.Angle(t.localRotation,p.rotation),.1f,asset+" "+p.path+" rotation");
        Assert.Less(Vector3.Distance(t.localScale,p.scale),.001f,asset+" "+p.path+" scale");
    }
}
