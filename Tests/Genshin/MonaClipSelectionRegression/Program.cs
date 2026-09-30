using AnimeStudio;

const string character = "Avatar_Girl_Catalyst_Mona";
AssetEntry Clip(string name, long id, ClassIDType type = ClassIDType.AnimationClip) => new()
{
    Name = name, Source = "fixture.blk", Offset = id, PathID = id, Type = type
};

var own = Clip("Ani_Avatar_Girl_Catalyst_Mona_WeaponStandby", 1);
var classBody = Clip("Ani_Avatar_Girl_Catalyst_WeaponStandby", 2);
var genericBody = Clip("Ani_Avatar_Girl_WeaponStandby", 3);
var wrongSuffix = Clip("Ani_Avatar_Girl_Catalyst_Attack", 4);
var wrongType = Clip("Ani_Avatar_Girl_Catalyst_WeaponStandby", 5, ClassIDType.TextAsset);
var selected = GenshinCharacterExporter.SelectClips(new[]
    { own, classBody, genericBody, wrongSuffix, wrongType, classBody }, character);
if (selected.Count != 3 || selected[0] != own || selected[1] != classBody || selected[2] != genericBody)
    throw new Exception("Character, weapon-class, and generic clip selection or precedence regressed.");

var noClass = GenshinCharacterExporter.SelectClips(new[] { own, genericBody }, character);
if (noClass.Count != 2 || noClass[1] != genericBody)
    throw new Exception("Generic body fallback selection regressed.");

Console.WriteLine("PASS matching weapon-class clip selected before generic body fallback; unrelated suffix/type excluded.");

if (args.Length == 1)
{
    if (ResourceMap.FromFile(args[0]) < 0) throw new Exception("Could not load the installed-source map.");
    var real = GenshinCharacterExporter.SelectClips(ResourceMap.GetEntries(), character);
    var classClips = real.Where(e => e.Name == "Ani_Avatar_Girl_Catalyst_WeaponStandby").ToArray();
    if (classClips.Length != 1 || classClips[0].PathID != -5900740780320310201)
        throw new Exception("The installed map did not select the expected class combat-idle clip.");
    if (!real.Any(e => e.Name == "Ani_Avatar_Girl_Catalyst_Mona_WeaponStandby"))
        throw new Exception("The installed map did not select Mona's secondary combat-idle clip.");
    Console.WriteLine($"PASS installed-source selection: {real.Count} clips, exact class PathID {classClips[0].PathID}.");
}
