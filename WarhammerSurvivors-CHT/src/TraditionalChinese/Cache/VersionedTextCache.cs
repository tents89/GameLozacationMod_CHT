using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace WarhammerSurvivors.TraditionalChinese.Cache;

public sealed record SourceText(string Id, string Original);

public sealed class CacheDocument
{
    public string NOW_VERSION { get; set; } = "";
    public Dictionary<string, string> Text { get; set; } = new(StringComparer.Ordinal);
    public int SCHEMA { get; set; } = 1;
    public string PROFILE { get; set; } = "S2TW";
    public string GENERATED_BY { get; set; } = "";
    public Dictionary<string, string> SOURCE_SHA256 { get; set; } = new(StringComparer.Ordinal);
    public string CONTENT_SHA256 { get; set; } = "";
}

public sealed record CacheResult(CacheDocument Document, bool Reused, string Reason, string? SaveError);

// Pure managed, independent of Unity. Normal valid same-version loads do not
// instantiate OpenCC, convert any string, or rewrite the JSON.
public sealed class VersionedTextCache
{
    public const int MaxTerms = 100000;
    public const int MaxTextLength = 1000000;
    public const long MaxFileBytes = 64L * 1024 * 1024;
    private readonly string path;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        MaxDepth = 8
    };

    public VersionedTextCache(string path) { this.path = path; }

    public CacheResult LoadOrBuild(string version, string modVersion, IReadOnlyList<SourceText> sources,
        Func<Func<string, string>> createConverter)
    {
        if (string.IsNullOrWhiteSpace(version) || version.Length > 128 || version.Any(char.IsControl))
            throw new ArgumentException("Game version is unavailable; refusing an unversioned cache.", nameof(version));
        Dictionary<string, string> originals = ValidateSources(sources);
        string reason;
        CacheDocument? loaded = TryLoad(version, out reason);
        if (loaded != null) return new(loaded, true, reason, null);

        Func<string, string> convert = createConverter();
        var document = new CacheDocument { NOW_VERSION = version, GENERATED_BY = modVersion };
        foreach (var entry in originals)
        {
            string final = convert(entry.Value) ?? throw new InvalidOperationException("Null conversion result.");
            if (final.Length > MaxTextLength) throw new InvalidOperationException("Converted text exceeds cache limit.");
            document.Text.Add(entry.Key, final);
            document.SOURCE_SHA256.Add(entry.Key, HashText(entry.Value));
        }
        document.CONTENT_SHA256 = ContentHash(document);
        string? saveError = null;
        try { SaveAtomic(document); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { saveError = e.Message; } // keep the valid full table in memory this run
        return new(document, false, reason, saveError);
    }

    private static Dictionary<string, string> ValidateSources(IReadOnlyList<SourceText> sources)
    {
        if (sources.Count == 0 || sources.Count > MaxTerms) throw new InvalidOperationException("No complete source, or too many terms.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (SourceText term in sources)
        {
            if (string.IsNullOrEmpty(term.Id) || term.Id.Length > 4096 || term.Original == null || term.Original.Length > MaxTextLength)
                throw new InvalidOperationException("Invalid I2 source record; refusing partial cache.");
            if (values.TryGetValue(term.Id, out string? other) && other != term.Original)
                throw new InvalidOperationException($"Conflicting I2 ID across sources: {term.Id}; no ambiguous cache applied.");
            values[term.Id] = term.Original;
        }
        return values;
    }

    public CacheDocument? TryLoad(string version, out string reason)
    {
        if (!File.Exists(path)) { reason = "cache missing"; return null; }
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaxFileBytes) throw new InvalidDataException("Cache exceeds size limit.");
            using var json = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 8 });
            RejectDuplicateProperties(json.RootElement);
            CacheDocument document = json.RootElement.Deserialize<CacheDocument>() ?? throw new InvalidDataException("Empty JSON.");
            if (document.NOW_VERSION != version) { reason = $"game version changed: {document.NOW_VERSION} -> {version}"; return null; }
            if (document.SCHEMA != 1 || document.PROFILE != "S2TW" || document.Text == null || document.SOURCE_SHA256 == null ||
                document.Text.Count == 0 || document.Text.Count > MaxTerms || document.Text.Count != document.SOURCE_SHA256.Count)
                throw new InvalidDataException("Cache schema/table is invalid.");
            foreach (var entry in document.Text)
                if (string.IsNullOrEmpty(entry.Key) || entry.Key.Length > 4096 || entry.Value == null || entry.Value.Length > MaxTextLength ||
                    !document.SOURCE_SHA256.TryGetValue(entry.Key, out string? hash) || hash == null || hash.Length != 64 || !hash.All(Uri.IsHexDigit))
                    throw new InvalidDataException("Cache contains an invalid text/source hash.");
            if (document.CONTENT_SHA256 != ContentHash(document)) throw new InvalidDataException("Cache integrity mismatch.");
            reason = "same game version: JSON reused without conversion";
            return document;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or NotSupportedException or ArgumentException)
        { reason = "cache invalid/unreadable: " + e.Message; return null; }
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Expected a JSON object.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(property.Name)) throw new InvalidDataException("Duplicate JSON key: " + property.Name);
            if (property.Value.ValueKind == JsonValueKind.Object) RejectDuplicateProperties(property.Value);
        }
    }

    private void SaveAtomic(CacheDocument document)
    {
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temp = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, document, JsonOptions);
                if (stream.Length > MaxFileBytes) throw new IOException("Cache output exceeds size limit.");
                stream.Flush(flushToDisk: true);
            }
            File.Move(temp, fullPath, overwrite: true);
        }
        finally { if (File.Exists(temp)) try { File.Delete(temp); } catch { } }
    }

    public static string HashText(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static string ContentHash(CacheDocument document)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            hash.AppendData(BitConverter.GetBytes(bytes.Length)); hash.AppendData(bytes);
        }
        Add(document.NOW_VERSION); Add(document.PROFILE);
        foreach (var entry in document.Text.OrderBy(e => e.Key, StringComparer.Ordinal))
        { Add(entry.Key); Add(entry.Value); Add(document.SOURCE_SHA256[entry.Key]); }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}

// Writes only verified source templates, never completed/formatted query results.
// A stale/missing key under an unchanged game version stays original; no lazy
// conversion and no silent cache mutation. Deleting the JSON is the manual reset.
public static class CachedTemplate
{
    public static bool TryGet(CacheDocument cache, string id, string current, out string final)
    {
        final = current;
        if (!cache.Text.TryGetValue(id, out string? candidate)) return false;
        if (candidate == current) { final = candidate; return true; }
        if (!cache.SOURCE_SHA256.TryGetValue(id, out string? hash) || VersionedTextCache.HashText(current) != hash) return false;
        final = candidate; return true;
    }
}