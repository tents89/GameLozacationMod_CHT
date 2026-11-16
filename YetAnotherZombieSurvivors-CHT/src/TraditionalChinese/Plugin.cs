using BepInEx;
using BepInEx.Unity.IL2CPP;
using I2.Loc;
using UnityEngine;
using YetAnotherZombieSurvivors.TraditionalChinese.Cache;
using YetAnotherZombieSurvivors.TraditionalChinese.Conversion;
using YetAnotherZombieSurvivors.TraditionalChinese.Runtime;
using YetAnotherZombieSurvivors.TraditionalChinese.Fonts;

namespace YetAnotherZombieSurvivors.TraditionalChinese;

[BepInPlugin(Id, Name, Version)]
[BepInProcess("Yet Another Zombie Survivors.exe")]
public sealed class Plugin : BasePlugin
{
    public const string Id = "local.yetanotherzombiesurvivors.traditionalchinese";
    public const string Name = "Yet Another Zombie Survivors Traditional Chinese";
    public const string Version = "1.0.6";
    internal static Plugin? Instance;
    private BulkI2Localization? bulk;
    private EmbeddedChineseFont? fonts;
    private NativeTextWriter? writer;
    private RuntimeMonitor? monitor;
    private int errors;
    private float nextHeartbeat = 60;

    public override void Load()
    {
        // Never bind/read/save Config. No Harmony/native detours are installed.
        string version = Application.version;
        if (string.IsNullOrWhiteSpace(version)) { Log.LogError("Game version unavailable; localization unchanged."); return; }
        string cachePath = PluginCachePath.ForAssembly(typeof(Plugin).Assembly.Location);
        writer = new NativeTextWriter();
        bulk = new BulkI2Localization(cachePath, version, Version, () =>
        {
            var converter = EmbeddedOpenCc.CreateBundled(ConversionProfile.S2TW, cacheCapacity: 0);
            Log.LogInfo($"Embedded post-overrides loaded: rules={converter.CustomEntries}; applied after OpenCC S2TW.");
            return text =>
            {
                var result = converter.ConvertDetailed(text);
                if (!result.Completed) throw new InvalidOperationException("Full-table conversion timed out; refusing a partial cache.");
                return result.Final;
            };
        }, message => Log.LogInfo(message), ReportError, writer.Set);
        fonts = new EmbeddedChineseFont(Path.GetDirectoryName(typeof(Plugin).Assembly.Location)!, message => Log.LogInfo(message), ReportError);
        Instance = this;
        try { monitor = AddComponent<RuntimeMonitor>(); }
        catch (Exception error) { ReportError(error); }
        Log.LogInfo($"Fixed options: OnlySimplifiedChinese=true, Profile=S2TW, EnablePostOverrides=true. Game version={version}.");
        Log.LogInfo("No I2/TMP/UI native hooks. Full I2 apply runs only in Update, with native Array.SetValue + strong string roots.");
        Log.LogInfo("JSON/PNG Han-only font routing: original Chinese primary/format/non-Han retained; prebaked Noto Han first, external _font/NotoSans.ttf fallback matched to JSON baking/face metrics for missing glyphs. No font mapping or label setter changes.");
        Log.LogInfo($"Full I2 JSON cache: {cachePath}");
        Log.LogInfo("Post-overrides embedded in DLL. Valid same-game-version text cache is reused; remove _cache/_translate.json once to apply updated glossary rules.");
    }

    private void ReportError(Exception error)
    {
        if (Interlocked.Increment(ref errors) <= 5) Log.LogWarning($"Traditional Chinese operation unavailable: {error}");
    }

    internal void Tick()
    {
        try
        {
            float now = Time.realtimeSinceStartup;
            // Do not mutate I2 in a native lookup or before the fallback is ready.
            fonts?.Tick(now, BulkI2Localization.ShouldConvert(null));
            if (fonts?.Ready == true)
            {
                if (fonts.ConsumeRefresh()) bulk?.RequestRefresh();
                bulk?.Tick(now);
            }
            if (now < nextHeartbeat) return;
            nextHeartbeat = now + 60;
            Log.LogInfo($"Full-cache heartbeat: uptime={now:F0}s; ready={bulk?.Ready}; terms={bulk?.CachedTerms}; reused={bulk?.Reused}; convertedThisRun={bulk?.ConvertedOnBuild}; applied={bulk?.Applied}; unmatched={bulk?.Unmatched}. chineseFontReady={fonts?.Ready}.");
        }
        catch (Exception error) { ReportError(error); }
    }
}

public sealed class RuntimeMonitor : MonoBehaviour
{
    public RuntimeMonitor(IntPtr pointer) : base(pointer) { }
    public void Update() => Plugin.Instance?.Tick();
}
