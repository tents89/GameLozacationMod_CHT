using System.Text;
using System.Text.RegularExpressions;

namespace YetAnotherZombieSurvivors.TraditionalChinese.Conversion;

public enum ConversionProfile { S2T, S2TW, S2TWP }

public sealed record ConversionResult(string Original, string OpenCc, string Final, bool Completed = true)
{
    public bool CustomApplied => !string.Equals(OpenCc, Final, StringComparison.Ordinal);
}

/// <summary>
/// Managed, forward-longest-match conversion using OpenCC text dictionaries.
/// Supports the three bundled profiles, not arbitrary native OpenCC configs.
/// </summary>
public sealed class OpenCcConverter
{
    // Preserve TMP/Unity markup (including quoted '>' attributes), .NET/I2
    // placeholders and printf specifiers. Malformed plain '<' is not a tag.
    private static readonly Regex Protected = new(
        "</?[A-Za-z][A-Za-z0-9_-]*(?:\\s+(?:[^<>\"']|\"[^\"]*\"|'[^']*')*)?\\s*/?>|" +
        "<[A-Za-z][A-Za-z0-9_-]*=(?:[^<>\"']|\"[^\"]*\"|'[^']*')*>|" +
        "<#[0-9A-Fa-f]{3,8}>|\\{[^{}\\r\\n]*\\}|" +
        "%[1-9][0-9]*\\$[-+#0 ]*(?:[0-9]+)?(?:\\.[0-9]+)?[sdifFeEgGouxXc]|" +
        "%[-+#0 ]*(?:[0-9]+)?(?:\\.[0-9]+)?[sdifFeEgGouxXc%]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    private readonly TextDictionary[] stages;
    private readonly TextDictionary overrides;
    private readonly Dictionary<string, ConversionResult> cache = new(StringComparer.Ordinal);
    private readonly Queue<string> cacheOrder = new();
    private readonly object cacheLock = new();
    private readonly int cacheCapacity;
    public int DictionaryEntries { get; }
    public int CacheCount { get { lock (cacheLock) return cache.Count; } }

    public int CustomEntries => overrides.Count;

    public OpenCcConverter(string directory, ConversionProfile profile, string? overridesPath = null, int cacheCapacity = 4096)
        : this(name => File.OpenRead(Path.Combine(directory, name)), profile, overridesPath, cacheCapacity) { }

    public OpenCcConverter(Func<string, Stream> openDictionary, ConversionProfile profile,
        string? overridesPath = null, int cacheCapacity = 4096, TextDictionary? postDictionary = null)
    {
        if (!Enum.IsDefined(typeof(ConversionProfile), profile)) throw new ArgumentOutOfRangeException(nameof(profile));
        this.cacheCapacity = Math.Clamp(cacheCapacity, 0, 100000);
        var dictionaries = new List<TextDictionary>
        {
            TextDictionary.Load(openDictionary, "STPhrases.txt", "STCharacters.txt")
        };
        if (profile == ConversionProfile.S2TWP)
            dictionaries.Add(TextDictionary.Load(openDictionary, "TWPhrasesIT.txt", "TWPhrasesName.txt", "TWPhrasesOther.txt"));
        if (profile != ConversionProfile.S2T) dictionaries.Add(TextDictionary.Load(openDictionary, "TWVariants.txt"));
        stages = dictionaries.ToArray();
        if (postDictionary != null && overridesPath != null)
            throw new ArgumentException("Choose either embedded or external post rules, never both.");
        overrides = postDictionary ?? (overridesPath is null ? new TextDictionary() : TextDictionary.LoadOverrides(overridesPath));
        DictionaryEntries = stages.Sum(stage => stage.Count);
    }

    public string? Convert(string? text) => text == null ? null : ConvertDetailed(text).Final;

    public ConversionResult ConvertDetailed(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0 || (overrides.Count == 0 && !ContainsHan(text))) return new(text, text, text);
        bool cacheable = cacheCapacity > 0 && text.Length <= 4096;
        if (cacheable)
        {
            lock (cacheLock) if (cache.TryGetValue(text, out ConversionResult? cached)) return cached;
        }
        ConversionResult result;
        try
        {
            // Full OpenCC profile FIRST. Re-tokenize protection before applying
            // the custom dictionary so markup/identifiers remain untouched.
            string openCc = TransformPlain(text, ApplyStages);
            string final = overrides.Count == 0 ? openCc : TransformPlain(openCc, overrides.Replace);
            result = new(text, openCc, final);
        }
        catch (RegexMatchTimeoutException) { return new(text, text, text, false); }
        if (cacheable)
        {
            lock (cacheLock)
            {
                if (!cache.ContainsKey(text))
                {
                    while (cache.Count >= cacheCapacity) cache.Remove(cacheOrder.Dequeue());
                    cache.Add(text, result);
                    cacheOrder.Enqueue(text);
                }
            }
        }
        return result;
    }

    private static string TransformPlain(string text, Func<string, string> transform)
    {
        var result = new StringBuilder(text.Length);
        int cursor = 0;
        foreach (Match match in Protected.Matches(text))
        {
            result.Append(transform(text[cursor..match.Index]));
            result.Append(match.Value);
            cursor = match.Index + match.Length;
        }
        result.Append(transform(text[cursor..]));
        return result.ToString();
    }

    private string ApplyStages(string text)
    {
        foreach (TextDictionary stage in stages) text = stage.Replace(text);
        return text;
    }

    public static bool ContainsHan(string text)
    {
        foreach (Rune rune in text.EnumerateRunes())
        {
            int value = rune.Value;
            if (value is >= 0x3400 and <= 0x9FFF or >= 0xF900 and <= 0xFAFF or >= 0x20000 and <= 0x323AF)
                return true;
        }
        return false;
    }
}
