namespace YetAnotherZombieSurvivors.TraditionalChinese.Conversion;

public static class LanguageGate
{
    public static bool IsSimplifiedChinese(string? code, string? name)
    {
        string normalized = (code ?? "").Trim().Replace('_', '-').ToLowerInvariant();
        // Never convert Japanese or an explicit Traditional Chinese locale,
        // even if a stale language name still says Simplified Chinese.
        if (normalized == "ja" || normalized.StartsWith("ja-") ||
            normalized.Contains("hant") || normalized is "zh-tw" or "zh-hk" or "zh-mo") return false;
        if (normalized is "zh-cn" or "zh-sg" or "zh-chs" || normalized.StartsWith("zh-hans")) return true;
        if (normalized.Length > 0 && normalized != "zh") return false;
        string language = (name ?? "").Trim();
        if (language.Contains("Traditional", StringComparison.OrdinalIgnoreCase) ||
            language.Contains("繁體") || language.Contains("繁体")) return false;
        return language.Contains("Simplified", StringComparison.OrdinalIgnoreCase) ||
            language.Contains("简体") || language.Contains("簡體") ||
            language.Contains("Chinese (China)", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSimplifiedOverride(string language)
    {
        string normalized = language.Trim().Replace('_', '-');
        return normalized.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? IsSimplifiedChinese(normalized, null)
            : IsSimplifiedChinese(null, language);
    }
}
