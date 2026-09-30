using ACLLibs;

if (args.Length != 2)
    throw new ArgumentException("Usage: WeaponClipProbe <Ayus Appear .acl-tracks.bin> <Ayus Loop .acl-tracks.bin>");

int[] expectedSamples = { 14, 42 };
for (int i = 0; i < args.Length; i++)
{
    byte[] data = File.ReadAllBytes(args[i]);
    GenshinLegacyAclHeader.Validate(data, out int values, out int samples);
    if (samples != expectedSamples[i] || values != samples * 40)
        throw new InvalidDataException($"Unexpected Ayus legacy ACL dimensions: {args[i]}");

    void Reject(Action<byte[]> corrupt)
    {
        byte[] invalid = (byte[])data.Clone();
        corrupt(invalid);
        try { GenshinLegacyAclHeader.Validate(invalid, out _, out _); }
        catch (InvalidDataException) { return; }
        throw new Exception("Invalid legacy ACL header was accepted.");
    }
    Reject(invalid => invalid[8] = 0); // unknown tag
    Reject(invalid => invalid[12] = 0); // unsupported version
    Reject(invalid => { invalid[36] = 0xFE; invalid[37] = 0xFF; }); // segment pointer past buffer
    Console.WriteLine($"{Path.GetFileName(args[i])}: {samples} samples, {values} values; invalid headers rejected");
}
