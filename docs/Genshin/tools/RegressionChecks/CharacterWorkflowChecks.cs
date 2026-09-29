using AnimeStudio;

internal static class CharacterWorkflowChecks
{
    public static void Run(Action<bool, string> check)
    {
        check(GenshinCharacterExporter.CharacterToken("Avatar_Girl_Catalyst_Mona") == "Mona", "character identity from selected name");
        check(GenshinCharacterExporter.CharacterToken("Avatar_Girl_Catalyst_MonaCostumeWic") == "Mona", "costume voice identity keeps base character token");
        bool rejected = false;
        try { GenshinCharacterExporter.CharacterToken("Beyd_Avatar_Boy_Suit"); } catch (ArgumentException) { rejected = true; }
        check(rejected, "character action rejects mannequin selection");
        AssetEntry Clip(string name, long id) => new() { Name = name, PathID = id, Type = ClassIDType.AnimationClip, Source = "fixture.blk", Offset = 12 };
        var entries = new[] { Clip("Ani_Avatar_Girl_Catalyst_Mona_RunCycle", 1), Clip("Ani_Avatar_Girl_RunCycle", 2),
            Clip("Ani_Avatar_Girl_Catalyst_Mona_Attack_01", 3), Clip("Ani_Avatar_Girl_Catalyst_Monarch_RunCycle", 4),
            Clip("Ani_Avatar_Girl_SomeUnrelatedAction", 5), Clip("Ani_Avatar_Lady_RunCycle", 6), Clip("Ani_Avatar_Girl_Catalyst_Mona_RunCycle", 1) };
        var selected = GenshinCharacterExporter.SelectClips(entries, "Avatar_Girl_Catalyst_Mona");
        check(selected.Select(e => e.PathID).Order().SequenceEqual(new long[] { 1, 2, 3 }), "automatic clip selection includes matching shared body and excludes unrelated names/rigs/duplicates");
        string folder = Path.Combine(Path.GetTempPath(), "AnimeStudio-reference-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string map = Path.Combine(folder, "map"), source = Path.Combine(folder, "source");
        try
        {
            File.WriteAllText(map, "map-a"); File.WriteAllText(source, "source-a");
            string Fingerprint(string version = "fixture-v1") => GenshinCharacterReferences.ComputeFingerprint(map, new[] { source }, version);
            string original = Fingerprint();
            check(original == Fingerprint(), "unchanged map and client reuse reference fingerprint");
            File.WriteAllText(map, "map-b");
            check(original != Fingerprint(), "changed map content refreshes references");
            File.WriteAllText(map, "map-a");
            check(original != Fingerprint("fixture-v2"), "changed client version refreshes references");
            File.SetLastWriteTimeUtc(source, File.GetLastWriteTimeUtc(source).AddSeconds(3));
            check(original != Fingerprint(), "changed source timestamp refreshes references");
            string timestampOnly = Fingerprint();
            File.AppendAllText(source, "-longer");
            check(timestampOnly != Fingerprint(), "changed source length refreshes references");
        }
        finally { File.Delete(map); File.Delete(source); Directory.Delete(folder); }
    }
}
