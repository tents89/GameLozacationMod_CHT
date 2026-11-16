using System.Text;

namespace WarhammerSurvivors.TraditionalChinese.Conversion;

public static class EmbeddedOpenCc
{
    public const string Prefix = "TraditionalChinese.OpenCC.";
    public const string PostResource = "post-overrides.tsv";
    public static Stream Open(string name) => typeof(EmbeddedOpenCc).Assembly.GetManifestResourceStream(Prefix + name)
        ?? throw new FileNotFoundException($"Missing embedded resource: {name}");

    // Pure OpenCC / explicitly supplied external rules for developer review tools.
    public static OpenCcConverter Create(ConversionProfile profile, string? postOverridesPath = null, int cacheCapacity = 4096) =>
        new(Open, profile, postOverridesPath, cacheCapacity);

    // Production entry point: no config-directory lookup, extraction, or second pass.
    public static OpenCcConverter CreateBundled(ConversionProfile profile, int cacheCapacity = 4096)
    {
        using Stream stream = Open(PostResource);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true);
        TextDictionary post = TextDictionary.LoadOverrides(reader, PostResource);
        return new OpenCcConverter(Open, profile, cacheCapacity: cacheCapacity, postDictionary: post);
    }
}
