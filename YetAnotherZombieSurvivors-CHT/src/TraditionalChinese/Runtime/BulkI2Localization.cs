using I2.Loc;
using YetAnotherZombieSurvivors.TraditionalChinese.Cache;
using YetAnotherZombieSurvivors.TraditionalChinese.Conversion;

namespace YetAnotherZombieSurvivors.TraditionalChinese.Runtime;

// All I2 access/mutation runs on Unity's main thread. Never change other-language
// columns, flags, term IDs, dictionaries, fallback fonts or serialized game files.
public sealed class BulkI2Localization
{
    private sealed record Slot(TermData Term, int LanguageIndex, string Id, string Original);
    private sealed record Snapshot(List<Slot> Slots, string Signature);
    private readonly VersionedTextCache disk;
    private readonly string gameVersion;
    private readonly string modVersion;
    private readonly Func<Func<string, string>> converterFactory;
    private readonly Action<string> info;
    private readonly Action<Exception> error;
    private readonly Action<TermData, int, string> writeText;
    private readonly int mainThread;
    private CacheDocument? cache;
    private string appliedSignature = "";
    private float nextScan;
    private float nextSourceCheck;
    private bool refreshPending;
    private bool inside;
    private bool disabled;
    private string? lastWarning;
    public bool Ready => cache != null;
    public int CachedTerms => cache?.Text.Count ?? 0;
    public int ConvertedOnBuild { get; private set; }
    public bool Reused { get; private set; }
    public int Applied { get; private set; }
    public int Unmatched { get; private set; }

    public BulkI2Localization(string path, string gameVersion, string modVersion,
        Func<Func<string, string>> converterFactory, Action<string> info, Action<Exception> error,
        Action<TermData, int, string> writeText)
    {
        disk = new VersionedTextCache(path); this.gameVersion = gameVersion; this.modVersion = modVersion;
        this.converterFactory = converterFactory; this.info = info; this.error = error;
        this.writeText = writeText ?? throw new ArgumentNullException(nameof(writeText));
        mainThread = Environment.CurrentManagedThreadId;
    }

    public void RequestRefresh() => refreshPending = true;

    public void Tick(float now)
    {
        if (Environment.CurrentManagedThreadId != mainThread || inside || disabled) return;
        if (!ShouldConvert(null)) { appliedSignature = ""; return; }
        if (now >= nextSourceCheck || now >= nextScan)
        {
            nextSourceCheck = now + .25f;
            Prepare(now, forceScan: now >= nextScan);
        }
        if (disabled) { refreshPending = false; return; }
        // Never call LocalizeAll from within a native lookup callback. Refresh
        // labels that initialized before Sources existed, after batch commit.
        // This is called only by Update, never by a patched translation query.
        if (refreshPending)
        {
            refreshPending = false;
            inside = true;
            try { LocalizationManager.LocalizeAll(true); }
            catch (Exception exception) { error(exception); }
            finally { inside = false; }
        }
    }

    public static bool ShouldConvert(string? language) => !string.IsNullOrWhiteSpace(language)
        ? LanguageGate.IsSimplifiedOverride(language)
        : LanguageGate.IsSimplifiedChinese(LocalizationManager.mLanguageCode, LocalizationManager.mCurrentLanguage);

    private void Prepare(float now, bool forceScan)
    {
        if (inside || disabled) return;
        inside = true;
        try
        {
            string signature = GetSignature();
            if (signature.Length == 0 || (!forceScan && cache != null && signature == appliedSignature)) return;
            nextScan = now + 10; // detect in-place source reloads without per-label conversions
            Snapshot? snapshot = ReadSnapshot(signature);
            if (snapshot == null) return; // wait for game-owned source/language loading
            if (cache == null)
            {
                CacheResult result = disk.LoadOrBuild(gameVersion, modVersion,
                    snapshot.Slots.Select(s => new SourceText(s.Id, s.Original)).ToArray(), () =>
                    {
                        Func<string, string> convert = converterFactory();
                        return text => { ConvertedOnBuild++; return convert(text); };
                    });
                cache = result.Document; Reused = result.Reused;
                info($"I2 full cache: {result.Reason}; version={gameVersion}; terms={cache.Text.Count}; convertedNow={ConvertedOnBuild}.");
                if (result.SaveError != null) info("Cache JSON write unavailable; full table remains in memory this run: " + result.SaveError);
            }
            int applied = 0, unmatched = 0;
            // Resolve all values before any writes; changes after the snapshot
            // are detected again on commit and left alone rather than overwritten.
            var writes = new List<(Slot Slot, string Final)>();
            foreach (Slot slot in snapshot.Slots)
            {
                if (!CachedTemplate.TryGet(cache, slot.Id, slot.Original, out string final)) { unmatched++; continue; }
                if (slot.Original != final) writes.Add((slot, final));
            }
            foreach (var write in writes)
            {
                var values = write.Slot.Term.Languages;
                if (values == null || write.Slot.LanguageIndex >= values.Length || values[write.Slot.LanguageIndex] != write.Slot.Original)
                { unmatched++; continue; }
                // Production uses native Array.SetValue and rooted IL2CPP strings.
                // Il2CppStringArray indexer writes raw pointers without a GC barrier.
                writeText(write.Slot.Term, write.Slot.LanguageIndex, write.Final);
                applied++;
            }
            Applied += applied; Unmatched = unmatched; appliedSignature = snapshot.Signature;
            if (applied > 0)
            {
                refreshPending = true;
                info($"Applied full I2 zh-CN table via GC-safe writer: changed={applied}; unmatched={unmatched}. No per-query OpenCC, no other-language writes.");
            }
            if (unmatched > 0)
                WarnOnce($"{unmatched} I2 slots do not match the same-version cache; left original. Delete the JSON to explicitly rebuild; it was not silently changed.");
        }
        catch (Exception exception)
        {
            // An invalid/ambiguous source must not become a partial cache or be
            // retried on every UI lookup. Unknown states fail without altering fonts.
            disabled = true;
            error(exception);
        }
        finally { inside = false; }
    }

    private void WarnOnce(string warning) { if (lastWarning == warning) return; lastWarning = warning; info(warning); }

    private static string GetSignature()
    {
        var sources = LocalizationManager.Sources;
        if (sources == null || sources.Count == 0) return "";
        var signature = new System.Text.StringBuilder();
        for (int i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            if (source == null) continue;
            var terms = source.mTerms; var languages = source.mLanguages;
            signature.Append(source.Pointer).Append(':').Append(terms?.Pointer ?? IntPtr.Zero)
                .Append(':').Append(terms?.Count ?? 0).Append(':').Append(languages?.Count ?? 0).Append(';');
        }
        signature.Append(LocalizationManager.mLanguageCode).Append('/').Append(LocalizationManager.mCurrentLanguage);
        return signature.ToString();
    }

    private static Snapshot? ReadSnapshot(string signature)
    {
        var sources = LocalizationManager.Sources;
        if (sources == null || sources.Count == 0) return null;
        var slots = new List<Slot>();
        for (int s = 0; s < sources.Count; s++)
        {
            var source = sources[s];
            if (source == null || source.mLanguages == null || source.mTerms == null) continue;
            var languages = source.mLanguages;
            int index = -1;
            for (int l = 0; l < languages.Count; l++)
            {
                var language = languages[l];
                if (language == null) continue;
                string code = (language.Code ?? "").Replace('_', '-');
                if (!code.Equals("zh-CN", StringComparison.OrdinalIgnoreCase)) continue;
                if (!language.IsLoaded()) return null;
                index = l; break;
            }
            if (index < 0) continue;
            var terms = source.mTerms;
            if (terms.Count == 0) return null; // known language source still initializing
            for (int t = 0; t < terms.Count; t++)
            {
                var term = terms[t];
                if (term == null || term.TermType != eTermType.Text) continue;
                var translations = term.Languages;
                if (translations == null || index >= translations.Length || translations[index] == null) return null;
                // Old Touch arrays are not used by this audited desktop build.
                // Do not flatten potentially different platform translations into one ID.
                var touch = term.Languages_Touch;
                if (touch != null && touch.Length != 0)
                    throw new InvalidOperationException("Unexpected I2 Touch variants: review schema before building this cache.");
                slots.Add(new Slot(term, index, term.Term, translations[index]));
                if (slots.Count > VersionedTextCache.MaxTerms) throw new InvalidOperationException("I2 term count exceeds cache safety limit.");
            }
        }
        return slots.Count == 0 ? null : new Snapshot(slots, signature);
    }
}