namespace YetAnotherZombieSurvivors.TraditionalChinese.Cache;

// Resolve from the loaded plugin DLL, never the working directory or a fixed
// BepInEx folder. VersionedTextCache creates _cache when it first saves a table.
internal static class PluginCachePath
{
    internal static string ForAssembly(string assemblyPath)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath) || !Path.IsPathFullyQualified(assemblyPath))
            throw new ArgumentException("The plugin DLL must have an absolute file location.", nameof(assemblyPath));
        string directory = Path.GetDirectoryName(assemblyPath)
            ?? throw new ArgumentException("The plugin DLL directory is unavailable.", nameof(assemblyPath));
        return Path.Combine(directory, "_cache", "_translate.json");
    }
}
