using AnimeStudio;

internal static class SharedAssetChecks
{
    public static void Run(Action<bool,string> check)
    {
        Keyframe<Float> K(float time, float value, float input = 0, float output = 0) => new(time, value, input, output, 0);
        float Evaluate(List<Keyframe<Float>> keys, float time)
        {
            var a = keys.Last(k => k.time <= time); var b = keys.FirstOrDefault(k => k.time > time);
            if (b == null) return a.value;
            float dt = b.time-a.time, t = (time-a.time)/dt;
            return (2*t*t*t-3*t*t+1)*(float)a.value + (t*t*t-2*t*t+t)*dt*(float)a.outSlope + (-2*t*t*t+3*t*t)*(float)b.value + (t*t*t-t*t)*dt*(float)b.inSlope;
        }
        var keys = new List<Keyframe<Float>> { K(0,1), K(1,1), K(2,1), K(3,2,1,1), K(4,2), K(5,2), K(6,2) };
        var original = keys.ToList();
        check(GenshinConstantCurveReduction.Reduce(keys) == 0, "flat stretches of moving curves are retained");
        check(keys.Select(k => k.time).SequenceEqual(new float[] {0,1,2,3,4,5,6}), "entire moving-curve key topology preserved");
        check(Enumerable.Range(0, 601).All(i => Math.Abs(Evaluate(keys,i/100f)-Evaluate(original,i/100f)) <= 1e-6f), "reduced Hermite curve matches including subframe samples");
        var tangent = new List<Keyframe<Float>> { K(0,1,0,1), K(1,1), K(2,1) };
        check(GenshinConstantCurveReduction.Reduce(tangent) == 0, "equal values with nonzero tangent retained");
        var weighted = new List<Keyframe<Float>> { K(0,1), K(1,1), K(2,1) }; weighted[1].weightedMode = 1;
        check(GenshinConstantCurveReduction.Reduce(weighted) == 0, "weighted curves retained");
        var malformed = new List<Keyframe<Float>> { K(0,1), K(0,1), K(2,1) };
        check(GenshinConstantCurveReduction.Reduce(malformed) == 0, "duplicate-time keys retained");
        var nan = new List<Keyframe<Float>> { K(0,float.NaN), K(1,float.NaN), K(2,float.NaN) };
        check(GenshinConstantCurveReduction.Reduce(nan) == 0, "nonfinite values never collapsed");
        var signed = new List<Keyframe<Float>> { K(0,0), K(1,-0f), K(2,0) };
        check(GenshinConstantCurveReduction.Reduce(signed) == 0, "signed zero values preserved");
        var quaternion = Enumerable.Range(0, 61).Select(i => new Keyframe<Quaternion>(i/60f,new Quaternion(0,0,0,1),default,default,default)).ToList();
        check(GenshinConstantCurveReduction.Reduce(quaternion) == 59 && quaternion[0].time == 0 && quaternion[^1].time == 1, "constant quaternion keeps exact endpoints");
        var moving = original.ToList();
        check(GenshinConstantCurveReduction.Reduce(moving) == 0, "production constant-only policy preserves every key on a moving curve");
        var constant = new List<Keyframe<Float>> {K(0,1),K(1,1),K(2,1)};
        check(GenshinConstantCurveReduction.Reduce(constant) == 1, "whole constant scalar retains endpoints");
        string key = "      - serializedVersion: 2\n        time: 0.125\n        value: {x: 0.1, y: -0, z: 1}\n        inSlope: {x: 0, y: 0, z: 0}\n        outSlope: {x: 0, y: 0, z: 0}\n";
        check(GenshinAnimationText.Compact(key) == "      - {serializedVersion: 2, time: 0.125, value: {x: 0.1, y: -0, z: 1}, inSlope: {x: 0, y: 0, z: 0}, outSlope: {x: 0, y: 0, z: 0}}\n", "compact key YAML preserves every numeric token including signed zero");
        string weightedText = key + "        weightedMode: 1\n        inWeight: 0.3\n        outWeight: 0.3\n";
        check(GenshinAnimationText.Compact(weightedText) == weightedText, "weighted YAML left unchanged");

        string root = Path.Combine(Path.GetTempPath(), "AnimeStudio-shared-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string a = Path.Combine(root,"A"), b = Path.Combine(root,"B");
            Directory.CreateDirectory(a); Directory.CreateDirectory(b);
            string sa = Path.Combine(a,"same.png"), sb = Path.Combine(b,"same.png");
            File.WriteAllText(sa,"exact synthetic bytes"); File.WriteAllText(sb,"exact synthetic bytes");
            string pa = Path.GetFullPath(Path.Combine(a,GenshinSharedAssets.Store(a,sa,"Textures")));
            File.WriteAllText(pa + ".meta","stable guid fixture");
            string pb = Path.GetFullPath(Path.Combine(b,GenshinSharedAssets.Store(b,sb,"Textures")));
            check(Path.GetDirectoryName(pa) == Path.Combine(root,"Generic","Textures") && Path.GetFileName(pa).StartsWith("same__"), "shared files have readable names with no per-file hash directory");
            check(pa == pb && File.ReadAllText(pa + ".meta") == "stable guid fixture", "repeated exports reuse resource without touching meta");
            File.WriteAllText(sb,"different bytes");
            check(pb != Path.GetFullPath(Path.Combine(b,GenshinSharedAssets.Store(b,sb,"Textures"))), "same named different resource stays separate");
            File.WriteAllText(pa,"edited shared resource"); bool rejected = false;
            try { GenshinSharedAssets.Store(a,sa,"Textures"); } catch (InvalidDataException) { rejected = true; }
            check(rejected && File.ReadAllText(pa) == "edited shared resource", "modified shared resources are never overwritten");
            rejected = false;
            try { GenshinSharedAssets.Store(a,sb,"Textures"); } catch (ArgumentException) { rejected = true; }
            check(rejected, "external source rejected");
            rejected = false;
            try { GenshinSharedAssets.Store(a,sa,"../Escape"); } catch (ArgumentException) { rejected = true; }
            check(rejected, "category traversal rejected");
            string package = Path.Combine(root,"Avatar_Girl_Sword_Test");
            foreach (var folder in new[] {"Animations","Textures","Materials","VFX/Texture2D","VFX/Material"}) Directory.CreateDirectory(Path.Combine(package,folder));
            File.WriteAllText(Path.Combine(package,"Animations","Ani_Avatar_Girl_RunCycle_123.anim"),"synthetic shared animation");
            File.WriteAllText(Path.Combine(package,"Textures","local.png"),"synthetic texture");
            File.WriteAllText(Path.Combine(package,"Materials","local.json"),"{}");
            File.WriteAllText(Path.Combine(package,"VFX/Texture2D","effect.png"),"synthetic effect texture");
            File.WriteAllText(Path.Combine(package,"VFX/Material","effect.json"),"{}");
            File.WriteAllText(Path.Combine(package,"manifest.json"),"{\"sourceClips\":[{\"file\":\"Animations/Ani_Avatar_Girl_RunCycle_123.anim\"}]}");
            File.WriteAllText(Path.Combine(package,"Avatar_Girl_Sword_Test.character.json"),"{\"clips\":[{\"file\":\"Animations/Ani_Avatar_Girl_RunCycle_123.anim\"}]}");
            GenshinSharedAssets.Package(package,"Avatar_Girl_Sword_Test");
            var recipe = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(package,"Avatar_Girl_Sword_Test.character.json")));
            check((string)recipe["textures"][0]["file"] == "Textures/local.png" && File.Exists(Path.Combine(package,"Textures/local.png")), "character recipe binds local textures");
            check(new[] {"Materials/local.json","VFX/Texture2D/effect.png","VFX/Material/effect.json"}.All(p=>File.Exists(Path.Combine(package,p))), "model and VFX materials/textures stay character-local");
            check(!File.Exists(Path.Combine(package,"Animations/Ani_Avatar_Girl_RunCycle_123.anim")) && File.Exists(Path.Combine(package,(string)recipe["clips"][0]["file"])), "shared animation references resolve after local copy removal");
        }
        finally { Directory.Delete(root, true); }
    }
}
