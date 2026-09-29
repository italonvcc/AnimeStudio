// Run in the same disposable Editor test assembly as UnityCharacterBatchCheck.
using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;

public class UnityCharacterDefaultsCheck
{
    [Serializable] public class Recipe { public string character, model; }
    [Serializable] public class MeshInfo { public string Path; }
    [Serializable] public class Manifest { public MeshInfo[] meshes; }

    [Test] public void AuxiliaryObjectsAndLocalMaterialsSurviveReimport()
    {
        foreach(string recipeFile in Directory.GetFiles("Assets", "*.character.json", SearchOption.AllDirectories)) {
            string folder=Path.GetDirectoryName(recipeFile).Replace('\\','/');
            var recipe=JsonUtility.FromJson<Recipe>(File.ReadAllText(recipeFile));
            var manifest=JsonUtility.FromJson<Manifest>(File.ReadAllText(folder+"/manifest.json"));
            string modelPath=folder+"/"+recipe.model;
            foreach(string path in new[]{modelPath,folder+"/"+recipe.character+".prefab"}) Check(path,folder,manifest);
            var materials=AssetDatabase.FindAssets("t:Material",new[]{folder+"/Materials"})
                .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<Material>).ToArray();
            Assert.Greater(materials.Length,0);
            var edited=materials.First(m=>m.HasProperty("_Color"));
            string materialPath=AssetDatabase.GetAssetPath(edited), guid=AssetDatabase.AssetPathToGUID(materialPath);
            var original=edited.color; var custom=new Color(.17f,.29f,.43f,.8f);
            try {
                edited.color=custom; EditorUtility.SetDirty(edited); AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(modelPath,ImportAssetOptions.ForceUpdate|ImportAssetOptions.ForceSynchronousImport);
                foreach(var type in AppDomain.CurrentDomain.GetAssemblies().SelectMany(a=>a.GetTypes()).Where(t=>t.Name.StartsWith("AnimeStudioCharacterImport_")))
                    type.GetMethod("Run").Invoke(null,null);
                Assert.AreEqual(guid,AssetDatabase.AssetPathToGUID(materialPath));
                Assert.Less(Vector4.Distance(custom,AssetDatabase.LoadAssetAtPath<Material>(materialPath).color),.000001f,"Local material edits must survive reimport");
                Assert.AreEqual(materials.Length,AssetDatabase.FindAssets("t:Material",new[]{folder+"/Materials"}).Length,"No duplicate materials on reimport");
                Check(modelPath,folder,manifest); Check(folder+"/"+recipe.character+".prefab",folder,manifest);
            } finally { edited.color=original; EditorUtility.SetDirty(edited); AssetDatabase.SaveAssets(); }
            Debug.Log(recipe.character+": "+materials.Length+" local materials; GUID/edit preservation and auxiliary defaults passed");
        }
    }
    static void Check(string path,string folder,Manifest manifest)
    {
        var model=AssetDatabase.LoadAssetAtPath<GameObject>(path); Assert.NotNull(model,path);
        int disabled=0;
        foreach(var mesh in manifest.meshes) {
            string relative=mesh.Path.Substring(mesh.Path.IndexOf('/')+1);
            var transform=model.transform.Find(relative); Assert.NotNull(transform,path+" preserves "+relative);
            var filter=transform.GetComponent<MeshFilter>(); var skin=transform.GetComponent<SkinnedMeshRenderer>();
            Assert.IsTrue((filter!=null && filter.sharedMesh!=null) || (skin!=null && skin.sharedMesh!=null));
            bool auxiliary=transform.name=="EffectMesh" || relative.StartsWith("Bip001/",StringComparison.Ordinal);
            Assert.AreEqual(!auxiliary,transform.gameObject.activeSelf,path+" visibility "+relative);
            if(auxiliary) disabled++;
        }
        Assert.Greater(disabled,0);
        Assert.IsTrue(model.transform.Find("Bip001").gameObject.activeSelf,"Armature remains active");
        foreach(var renderer in model.GetComponentsInChildren<Renderer>(true))
            foreach(var material in renderer.sharedMaterials) {
                Assert.NotNull(material,path+" "+renderer.name);
                string materialPath=AssetDatabase.GetAssetPath(material);
                // Meshes without a source material use Unity's built-in default;
                // it is not an embedded FBX material to extract.
                if(materialPath=="Resources/unity_builtin_extra") continue;
                StringAssert.StartsWith(folder+"/Materials/",materialPath,path+" "+renderer.name);
            }
        var importer=AssetImporter.GetAtPath(folder+"/"+Path.GetFileNameWithoutExtension(path)+".fbx") as ModelImporter;
        Assert.NotNull(importer);
        var remaps=importer.GetExternalObjectMap().Where(p=>p.Key.type==typeof(Material)).ToArray();
        Assert.Greater(remaps.Length,0);
        foreach(var remap in remaps) StringAssert.StartsWith(folder+"/Materials/",AssetDatabase.GetAssetPath(remap.Value));
        Debug.Log(path+": "+disabled+" auxiliary mesh objects disabled, all "+manifest.meshes.Length+" meshes retained");
    }
}
