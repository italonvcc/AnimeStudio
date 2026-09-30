using AnimeStudio;
using Newtonsoft.Json;

// Bounded source evidence for rest hierarchy and clips that previously imported empty.
internal static class CharacterSourceCheck
{
    public static int Run(string[] args)
    {
        if (args.Length != 4) throw new ArgumentException("--character-source <map> <character[@pathID]> <report.json>");
        ResourceMap.FromFile(args[1]);
        var references = GenshinCharacterReferences.Prepare(args[1], ResourceMap.GetEntries());
        string[] selector=args[2].Split('@');
        var selected = new[]{references.Entries.Single(e => e.Name == selector[0] && e.Type == ClassIDType.Animator &&
            (selector.Length==1 || e.PathID==long.Parse(selector[1])))};
        var entries = selected.Concat(GenshinCharacterExporter.SelectClips(references.Entries, selector[0])
            .Where(e => e.Name.EndsWith("Sit02Loop_Adjust") || e.Name.EndsWith("Standby"))).ToArray();
        var manager = new AssetsManager { Game = GameManager.GetGameByType(GameType.GI), ResolveDependencies = false };
        try {
            manager.FilterData.Items = entries.Select(e => new AssetsManager.AssetFilterDataItem { Source=e.Source, Offset=e.Offset, Type=e.Type, PathID=e.PathID, Name=e.Name }).ToList();
            manager.LoadFiles(entries.Select(e => e.Source).Distinct().ToArray(), mergeSplitAssets:false);
            var animator = (Animator)manager.FindAsset(selected.Single());
            new AssetDependencyResolver(manager, references.Entries).Resolve(new AnimeStudio.Object[] {animator});
            animator.m_Avatar.TryGet(out var avatar);
            animator.m_GameObject.TryGet(out var go);
            var transforms = new List<object>();
            void Visit(Transform t, string path) {
                transforms.Add(new {path, t.m_LocalPosition, t.m_LocalRotation, t.m_LocalScale});
                foreach(var child in t.m_Children) if(child.TryGet(out var c)) { c.m_GameObject.TryGet(out var g); Visit(c,path.Length==0?g.m_Name:path+"/"+g.m_Name); }
            }
            Visit(go.m_Transform, "");
            var clips = entries.Where(e => e.Type == ClassIDType.AnimationClip).Select(e => {
                var c=(AnimationClip)manager.FindAsset(e); var data=c.m_MuscleClip.m_Clip; var acl=(GIACLClip)data.m_ACLClip;
                return new {source=e, c.Name, c.m_PathID, c.m_MuscleClip.m_StartTime,c.m_MuscleClip.m_StopTime,
                    acl.IsSet,acl.CurveCount,tracks=acl.m_ClipData.Length,database=acl.m_DatabaseData.Length,
                    denseFrames=data.m_DenseClip.m_FrameCount,denseCurves=data.m_DenseClip.m_CurveCount,
                    streamCurves=data.m_StreamedClip.curveCount, constants=data.m_ConstantClip.data.Length};
            }).ToArray();
            Directory.CreateDirectory(Path.GetDirectoryName(args[3])!);
            File.WriteAllText(args[3],JsonConvert.SerializeObject(new {character=selector[0], source=selected.Single(), avatar.m_TOS,avatar.m_Avatar,transforms,clips},Formatting.Indented));
            Console.WriteLine(JsonConvert.SerializeObject(clips,Formatting.Indented));
        } finally {manager.Clear();}
        return 0;
    }
}
