using AnimeStudio;
using System.Runtime.CompilerServices;

internal static class AssemblyChecks
{
    private static ModelConverter Model(string name)
    {
        var result = (ModelConverter)RuntimeHelpers.GetUninitializedObject(typeof(ModelConverter));
        void Set(string property, object value) => typeof(ModelConverter).GetProperty(property)!.SetValue(result, value);
        ImportedFrame Frame(string n) => new() { Name = n, LocalPosition = Vector3.Zero, LocalRotation = new Quaternion(0,0,0,1), LocalScale = Vector3.One };
        var root = Frame(name); root.AddChild(Frame("Bone")); root.AddChild(Frame("Top"));
        Set("RootFrame", root);
        var identity = new Matrix4x4(new float[] { 1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1 });
        Set("MeshList", new List<ImportedMesh> { new() { Path = name + "/Top", VertexList = new(), SubmeshList = new(), BoneList = new() { new() { Path = name + "/Bone", Matrix = identity } } } });
        Set("MaterialList", new List<ImportedMaterial>()); Set("TextureList", new List<ImportedTexture>());
        Set("AnimationList", new List<ImportedKeyframedAnimation>()); Set("MorphList", new List<ImportedMorph>());
        return result;
    }
    public static void Run(Action<bool,string> check)
    {
        var target = Model("Base"); var part = Model("Part"); var replacement = part.MeshList.Single();
        GenshinPartAssembler.Replace(target, part, "Top", new[] { "Top" });
        check(target.MeshList.Count == 1 && ReferenceEquals(target.MeshList[0],replacement) && replacement.Path == "Base/Top" && replacement.BoneList[0].Path == "Base/Bone", "part assembly replaces only selected meshes and remaps qualified rig paths");
        void Reject(Action<ModelConverter> change, string label, bool retainBaseBind = false)
        {
            var model = Model("Part"); change(model); bool rejected=false;
            var baseline = Model("Base");
            if (retainBaseBind) baseline.MeshList.Add(new ImportedMesh { Path = "Base/Retained", BoneList = baseline.MeshList[0].BoneList });
            try { GenshinPartAssembler.Replace(baseline,model,"Top",new[] { "Top" }); } catch (InvalidDataException) { rejected=true; }
            check(rejected,label);
        }
        var unsharedPart = Model("Part");
        var unsharedBind = unsharedPart.MeshList[0].BoneList[0].Matrix; unsharedBind[0] = 2;
        unsharedPart.MeshList[0].BoneList[0].Matrix = unsharedBind;
        var unsharedTarget = Model("Base");
        GenshinPartAssembler.Replace(unsharedTarget, unsharedPart, "Top", new[] { "Top" });
        check(unsharedTarget.MeshList[0].BoneList[0].Matrix[0] == 2, "replaced mesh does not constrain incoming source bind");
        Reject(m => m.RootFrame.FindFrame("Bone").LocalPosition = new Vector3(.1f,0,0), "incompatible Manekin rest pose is rejected");
        Reject(m => { var matrix=m.MeshList[0].BoneList[0].Matrix; matrix[0]=2; m.MeshList[0].BoneList[0].Matrix=matrix; }, "part bind conflicting with a retained mesh is rejected", retainBaseBind: true);
        Reject(m => m.MeshList[0].BoneList[0].Path="Part/Missing", "missing part bone cannot be matched by guessing a name");
        Reject(m => m.RootFrame.LocalRotation=new Quaternion(0,0,0,2), "invalid nonunit root rotation is rejected");
    }
}
