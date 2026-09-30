using AnimeStudio;
using System.Runtime.CompilerServices;

internal static class BindPoseChecks
{
    sealed class Model : IImported
    {
        public ImportedFrame RootFrame { get; set; }
        public List<ImportedMesh> MeshList { get; } = new();
        public List<ImportedMaterial> MaterialList { get; } = new();
        public List<ImportedTexture> TextureList { get; } = new();
        public List<ImportedKeyframedAnimation> AnimationList { get; } = new();
        public List<ImportedMorph> MorphList { get; } = new();
    }
    public static void Run(Action<bool,string> check)
    {
        ImportedFrame Frame(string name, float x, float y = 0) => new() { Name=name, LocalPosition=new Vector3(x,y,0), LocalRotation=new Quaternion(0,0,0,1),LocalScale=Vector3.One };
        Matrix4x4 Bind(float x,float y) => new() { M00=1,M11=1,M22=1,M33=1,M30=x,M31=y };
        var model = new Model { RootFrame=Frame("Root",3) };
        var mesh = Frame("Mesh",2); var hip = Frame("Hip",9); var tip=Frame("Tip",9,9);
        var attachment=Frame("Attachment",.5f); hip.AddChild(attachment);
        model.MeshList.Add(new ImportedMesh {Path=attachment.Path});
        model.RootFrame.AddChild(mesh); model.RootFrame.AddChild(hip); hip.AddChild(tip);
        model.MeshList[0].Path = attachment.Path;
        var skin = new ImportedMesh { Path=mesh.Path, BoneList=new() {
            new ImportedBone {Path=hip.Path,Matrix=Bind(1,0)}, new ImportedBone {Path=tip.Path,Matrix=Bind(1,-2)} } };
        model.MeshList.Add(skin);
        check(GenshinBindPose.Restore(model)==2, "bind restoration resolves both skin constraints");
        check(Math.Abs(hip.LocalPosition.X-1)<1e-6 && tip.LocalPosition.X==0 && Math.Abs(tip.LocalPosition.Y-2)<1e-6, "bind restoration accounts for renderer world matrix and updated parents");
        check(mesh.LocalPosition.X==2 && model.RootFrame.LocalPosition.X==3 && skin.BoneList[1].Matrix.M31==-2, "renderer placement and inverse skin matrices retained");
        check(attachment.LocalPosition.X==.5f, "rigid mesh keeps its bone-relative placement during bind restoration");
        check(GenshinBindPose.Restore(model)==2 && hip.LocalPosition.X==1, "bind restoration is idempotent");
        T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
        var helper=Frame("Helper",9); model.RootFrame.AddChild(helper);
        var avatar=Empty<Avatar>(); avatar.m_Avatar=Empty<AvatarConstant>(); avatar.m_TOS=new() {{7,"Helper"}};
        avatar.m_Avatar.m_AvatarSkeleton=Empty<Skeleton>(); avatar.m_Avatar.m_AvatarSkeleton.m_ID=new uint[]{7};
        avatar.m_Avatar.m_DefaultPose=Empty<SkeletonPose>();
        avatar.m_Avatar.m_DefaultPose.m_X=new[]{new XForm {t=new Vector3(2,3,4),q=new Quaternion(0,0,0,1),s=Vector3.One}};
        GenshinBindPose.Restore(model,avatar);
        check(helper.LocalPosition.X==-2 && helper.LocalPosition.Y==3 && helper.LocalPosition.Z==4,
            "unweighted helper bones use Avatar default pose with FBX handedness");
        model.MeshList.Add(new ImportedMesh {Path=mesh.Path,BoneList=new() {new ImportedBone {Path=hip.Path,Matrix=Bind(8,0)}}});
        bool rejected=false;
        try {GenshinBindPose.Restore(model);} catch(GenshinBindPoseConflictException conflict) {rejected=conflict.FramePath==hip.Path && conflict.First.RendererPath==mesh.Path && conflict.Second.RendererPath==mesh.Path;}
        check(rejected,"conflicting skin constraints are reported rather than arbitrarily choosing a mesh");
    }
}
