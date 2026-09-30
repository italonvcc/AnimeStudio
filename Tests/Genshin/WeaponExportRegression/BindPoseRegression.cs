using AnimeStudio;

internal static class BindPoseRegression
{
    public static void Run(Action<bool, string> check)
    {
        var model = new Fixture();
        ImportedFrame Frame(string name) => new() { Name = name,
            LocalRotation = new Quaternion(0, 0, 0, 1), LocalScale = new Vector3(1, 1, 1) };
        model.RootFrame = Frame("rig");
        foreach (string name in new[] { "bone", "meshA", "meshB" }) model.RootFrame.AddChild(Frame(name));
        var identity = new Matrix4x4 { M00 = 1, M11 = 1, M22 = 1, M33 = 1 };
        var shifted = identity;
        shifted.M30 = 1;
        model.MeshList.Add(new ImportedMesh { Path = "rig/meshA", BoneList = new() { new() { Path = "rig/bone", Matrix = identity } } });
        model.MeshList.Add(new ImportedMesh { Path = "rig/meshB", BoneList = new() { new() { Path = "rig/bone", Matrix = shifted } } });
        GenshinBindPoseConflictException conflict = null;
        try { GenshinBindPose.Restore(model); }
        catch (GenshinBindPoseConflictException ex) { conflict = ex; }
        check(conflict?.FramePath == "rig/bone" && conflict.First.RendererPath == "rig/meshA"
            && conflict.Second.RendererPath == "rig/meshB", "Conflicting shared bone poses identify both source renderer constraints");
        check(conflict != null && conflict.First.DesiredWorld[12] == 0 && conflict.Second.DesiredWorld[12] == -1,
            "Conflict evidence retains the differing per-mesh world matrices");
        model.MeshList.RemoveAt(1);
        model.MeshList[0].BoneList[0].Path = "rig/missing";
        bool missingRejected = false;
        try { GenshinBindPose.Restore(model); }
        catch (InvalidDataException) { missingRejected = true; }
        check(missingRejected, "Detached bone remains an error distinct from a shared-pose conflict");
        model.MeshList[0].BoneList[0].Path = "rig/bone";
        model.RootFrame.AddChild(Frame("effect"));
        model.RootFrame.AddChild(Frame("effect"));
        check(GenshinBindPose.Restore(model) == 1, "Unreferenced duplicate effect names do not invalidate a unique skin rig");
        model.RootFrame.AddChild(Frame("bone"));
        bool ambiguousRejected = false;
        try { GenshinBindPose.Restore(model); }
        catch (InvalidDataException) { ambiguousRejected = true; }
        check(ambiguousRejected, "Duplicate names on a referenced bone remain an explicit ambiguity");
    }

    private sealed class Fixture : IImported
    {
        public ImportedFrame RootFrame { get; set; }
        public List<ImportedMesh> MeshList { get; } = new();
        public List<ImportedMaterial> MaterialList { get; } = new();
        public List<ImportedTexture> TextureList { get; } = new();
        public List<ImportedKeyframedAnimation> AnimationList { get; } = new();
        public List<ImportedMorph> MorphList { get; } = new();
    }
}
