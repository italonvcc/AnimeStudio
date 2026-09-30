using AnimeStudio;

// These eleven hash/name pairs come from the Boy UAngry02 native clip and
// exported Face FBX. They also cover the Girl clip's Face channel set.
var channels = new (string Name, uint Hash)[] {
    ("Eye_WinkA_L", 1821054144), ("Eye_WinkA_R", 2525243811),
    ("Eye_WinkB", 1976229525), ("Eye_Sad", 1024282926),
    ("Eye_Angry", 577241021), ("Eye_Ha", 1939358397),
    ("Mouth_A", 3997271097), ("Mouth_Smile", 2222047423),
    ("Mouth_Angry", 1178264873), ("Mouth_Fury", 1911712580),
    ("Mouth_Small", 4089325987)
};
var face = new ImportedMorph { Path = "Beyd_Avatar_Boy_Suit_S0017_Store/Face", Channels = channels.Select(pair =>
    new ImportedMorphChannel { Name = pair.Name, KeyframeList = new() }).ToList() };
const string modelRoot = "Beyd_Avatar_Boy_Suit_S0017_Store";
var curves = channels.Select(pair => Curve("Face", "blendShape." + pair.Hash)).ToList();
curves.Add(Curve("OtherFace", "blendShape.1821054144"));
curves.Add(Curve("Face", "blendShape.AlreadyNamed"));
int resolved = GenshinBlendShapeCurveBindings.Resolve(curves, new[] { face }, modelRoot);
Check(resolved == 11, "all source Face CRCs resolve");
for (int i = 0; i < channels.Length; i++)
    Check(curves[i].attribute == "blendShape." + channels[i].Name, channels[i].Name);
Check(curves[11].attribute == "blendShape.1821054144", "absent renderer remains explicit");
Check(curves[12].attribute == "blendShape.AlreadyNamed", "named binding preserved");
Check(GenshinBlendShapeCurveBindings.Resolve(curves, new[] { face }, modelRoot) == 0, "idempotent");
ExpectFailure(() => GenshinBlendShapeCurveBindings.Resolve(
    new[] { Curve("Face", "blendShape.123456789") }, new[] { face }, modelRoot), "missing source channel");
Console.WriteLine("PASS eleven real UAngry02 CRCs, exact path, named preservation, idempotence, missing channel");

static FloatCurve Curve(string path, string attribute) =>
    new(path, attribute, ClassIDType.SkinnedMeshRenderer, default);
static void Check(bool pass, string label)
{
    if (!pass) throw new Exception("FAIL " + label);
}
static void ExpectFailure(Action action, string label)
{
    try { action(); }
    catch (InvalidOperationException) { return; }
    throw new Exception("FAIL expected rejection: " + label);
}
