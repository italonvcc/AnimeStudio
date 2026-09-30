// Run only in a disposable export-validation project.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public static class UnityCharacterRestCheck
{
    [Serializable] public class Entry { public string asset; public int meshes, bones, vertices; public float maxBindMatrixError, maxRestVertexError; }
    [Serializable] public class Report { public bool passed; public string error; public Entry[] assets; }
    public static void Run()
    {
        var report = new Report(); var entries = new List<Entry>();
        try {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            foreach (var type in assembly.GetTypes().Where(t => t.Name.StartsWith("AnimeStudioCharacterImport_")))
                type.GetMethod("Run").Invoke(null, null);
            foreach (string recipe in Directory.GetFiles("Assets", "*.character.json", SearchOption.AllDirectories)) {
                string folder = Path.GetDirectoryName(recipe);
                foreach (string file in Directory.GetFiles(folder).Where(p => p.EndsWith(".fbx") || p.EndsWith(".prefab"))) {
                    var entry = new Entry { asset = file }; entries.Add(entry);
                    var instance = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(file.Replace('\\','/')));
                    try {
                        foreach (var animator in instance.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
                        foreach (var renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true)) {
                            var mesh = renderer.sharedMesh; entry.meshes++;
                            for (int i=0;i<mesh.bindposes.Length;i++) {
                                var matrix = renderer.transform.worldToLocalMatrix * renderer.bones[i].localToWorldMatrix * mesh.bindposes[i];
                                for (int j=0;j<16;j++) entry.maxBindMatrixError = Mathf.Max(entry.maxBindMatrixError, Mathf.Abs(matrix[j]-Matrix4x4.identity[j]));
                                entry.bones++;
                            }
                            var baked = new Mesh(); renderer.BakeMesh(baked);
                            var vertices = mesh.vertices; var actual = baked.vertices;
                            for(int i=0;i<vertices.Length;i++) entry.maxRestVertexError = Mathf.Max(entry.maxRestVertexError, Vector3.Distance(vertices[i],actual[i]));
                            entry.vertices += vertices.Length; UnityEngine.Object.DestroyImmediate(baked);
                        }
                    } finally { UnityEngine.Object.DestroyImmediate(instance); }
                }
            }
            report.passed = entries.Count > 0 && entries.All(e => e.meshes > 0 && e.bones > 0 && float.IsFinite(e.maxBindMatrixError) && e.maxBindMatrixError < .001f && e.maxRestVertexError < .0001f);
        } catch(Exception e) { report.error=e.ToString(); }
        report.assets = entries.ToArray(); File.WriteAllText("character-rest-check.json", JsonUtility.ToJson(report,true));
        EditorApplication.Exit(report.passed ? 0 : 1);
    }
}
