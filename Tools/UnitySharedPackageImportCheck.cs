// Full-library import acceptance in a disposable project with Assets/Mona + Assets/Generic.
using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;

public static class UnitySharedPackageImportCheck
{
    [Serializable] class Report { public bool success; public string error; public int clips, composedPairs, textureAssignments; public string[] missingCurvePaths; }
    public static void Run()
    {
        try
        {
            foreach(var type in TypeCache.GetTypesDerivedFrom<AssetPostprocessor>().Where(t=>t.Name.StartsWith("AnimeStudioCharacterImport_")))
                type.GetMethod("Run").Invoke(null,null);
            var report=JsonUtility.FromJson<Report>(File.ReadAllText("Assets/Mona/unity-import-report.json"));
            if(!report.success || report.clips<500 || report.composedPairs<2 || report.textureAssignments<1 || report.missingCurvePaths.Length!=0)
                throw new Exception("Full package failed: "+JsonUtility.ToJson(report));
            if(Directory.GetFiles("Assets/Mona/Rig","*__WithBody.anim").Length!=0) throw new Exception("Unexpected merged source copies");
            File.WriteAllText("full-shared-import-check.json",JsonUtility.ToJson(report,true));
            EditorApplication.Exit(0);
        }
        catch(Exception e) {File.WriteAllText("full-shared-import-check.json",JsonUtility.ToJson(new Report {success=false,error=e.ToString()},true));Debug.LogException(e);EditorApplication.Exit(1);}
    }
}
