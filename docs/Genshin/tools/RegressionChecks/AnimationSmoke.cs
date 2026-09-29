using AnimeStudio;
using Newtonsoft.Json.Linq;

internal static class AnimationSmoke
{
    public static int Run(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("Usage: --animations <AssetStudy report.json>");
        var entries = JObject.Parse(File.ReadAllText(args[1]))["entries"]!.ToObject<List<AssetEntry>>()!;
        if (entries.Count is < 1 or > 16 || entries.Any(e => e.Type != ClassIDType.AnimationClip || e.Offset < 0))
            throw new ArgumentException("Expected 1-16 animation entries with bundle offsets.");
        var manager = new AssetsManager { Game = GameManager.GetGameByType(GameType.GI) };
        manager.FilterData.Items = entries.Select(e => new AssetsManager.AssetFilterDataItem
        { Source = e.Source, Offset = e.Offset, PathID = e.PathID, Name = e.Name, Type = e.Type }).ToList();
        try
        {
            manager.LoadFiles(entries.Select(e => e.Source).Distinct().ToArray(), mergeSplitAssets: false);
            var clips = manager.assetsFileList.SelectMany(f => f.ObjectsDic.Values).OfType<AnimationClip>()
                .Where(c => entries.Any(e => e.PathID == c.m_PathID && e.Source == c.assetsFile.originalPath)).ToArray();
            if (clips.Length != entries.Count) throw new Exception("Selected clips did not all load.");
            foreach (var clip in clips)
            {
                var first = AnimationClipConverter.Process(clip);
                var second = AnimationClipConverter.Process(clip);
                var times = first.Translations.SelectMany(c => c.curve.m_Curve.Select(k => k.time))
                    .Concat(first.Rotations.SelectMany(c => c.curve.m_Curve.Select(k => k.time)))
                    .Concat(first.Scales.SelectMany(c => c.curve.m_Curve.Select(k => k.time)))
                    .Concat(first.Floats.SelectMany(c => c.curve.m_Curve.Select(k => k.time))).ToArray();
                if (times.Any(t => t > clip.m_MuscleClip.m_StopTime + .00001f) || Math.Abs(times.Max() - clip.m_MuscleClip.m_StopTime) > .00001f)
                    throw new Exception("Exported curve duration differs from the authored clip stop time.");
                var curves = first.Floats.Where(c => c.classID == ClassIDType.Animator).ToDictionary(c => c.attribute);
                if (curves.Count == 0) throw new Exception("No humanoid curves decoded.");
                foreach (var curve in curves.Values)
                {
                    var keys = curve.curve.m_Curve;
                    var repeated = second.Floats.Single(c => c.attribute == curve.attribute && c.classID == ClassIDType.Animator).curve.m_Curve;
                    if (!keys.Select(k => (k.time, (float)k.value)).SequenceEqual(repeated.Select(k => (k.time, (float)k.value))))
                        throw new Exception("Decode is not deterministic: " + curve.attribute);
                    if (keys.Any(k => !float.IsFinite((float)k.value) || !float.IsFinite(k.time))) throw new Exception("Non-finite sample.");
                }
                foreach (var prefix in new[] { "MotionQ", "RootQ", "LeftFootQ", "RightFootQ", "LeftHandQ", "RightHandQ" })
                {
                    if (!curves.ContainsKey(prefix + ".w")) continue;
                    var components = "xyzw".Select(c => curves[prefix + "." + c].curve.m_Curve).ToArray();
                    for (int i = 0; i < components[0].Count; i++)
                    {
                        var norm = components.Sum(c => Math.Pow((float)c[i].value, 2));
                        if (Math.Abs(norm - 1) > 0.02) throw new Exception($"Invalid quaternion {prefix} at sample {i}: {norm}");
                    }
                }
                if (!clip.Name.EndsWith("_Standby") && !curves.Values.Any(c => c.curve.m_Curve.Select(k => (float)k.value).Distinct().Count() > 1))
                    throw new Exception("Expected varying action/locomotion humanoid curves: " + clip.Name);
                Console.WriteLine($"PASS {clip.Name}: {curves.Count} deterministic, finite humanoid curves; unit quaternions across all samples");
            }
            return 0;
        }
        finally { manager.Clear(); }
    }
}
