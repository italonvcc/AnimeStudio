using AnimeStudio;

internal static class YamlScalarRegression
{
    public static void Run(Action<bool, string> check)
    {
        var document = new YAMLDocument();
        var root = document.CreateMappingRoot();
        root.Add("floatPositive", new YAMLScalarNode(float.PositiveInfinity));
        root.Add("floatNegative", new YAMLScalarNode(float.NegativeInfinity));
        root.Add("floatNan", new YAMLScalarNode(float.NaN));
        root.Add("floatFinite", new YAMLScalarNode(1.25f));
        root.Add("doublePositive", new YAMLScalarNode(double.PositiveInfinity));
        root.Add("doubleNegative", new YAMLScalarNode(double.NegativeInfinity));
        root.Add("doubleNan", new YAMLScalarNode(double.NaN));
        root.Add("doubleFinite", new YAMLScalarNode(2.5d));
        var writer = new YAMLWriter();
        writer.AddDocument(document);
        using var output = new StringWriter();
        writer.Write(output);
        string yaml = output.ToString();
        check(yaml.Contains("floatPositive: Infinity") && yaml.Contains("floatNegative: -Infinity") &&
            yaml.Contains("doublePositive: Infinity") && yaml.Contains("doubleNegative: -Infinity") &&
            !yaml.Contains('∞'), "Unity YAML emits signed ASCII infinities for float and double");
        check(yaml.Contains("floatNan: NaN") && yaml.Contains("doubleNan: NaN"),
            "Unity YAML preserves NaN without culture-dependent spelling");
        check(yaml.Contains("floatFinite: 1.25") && yaml.Contains("doubleFinite: 2.5"),
            "Finite YAML scalar formatting is unchanged");
    }
}
