using System.Reflection;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using I2.Loc;
using UnityEngine;
using WarhammerSurvivors.TraditionalChinese.Cache;
using WarhammerSurvivors.TraditionalChinese.Conversion;
using WarhammerSurvivors.TraditionalChinese.Patching;
using WarhammerSurvivors.TraditionalChinese.Runtime;

namespace WarhammerSurvivors.TraditionalChinese;

[BepInPlugin(Id, Name, Version)]
[BepInProcess("WarhammerSurvivors.exe")]
public sealed class Plugin : BasePlugin
{
    public const string Id = "local.warhammersurvivors.traditionalchinese";
    public const string Name = "Warhammer Survivors Traditional Chinese";
    public const string Version = "1.3.1";
    internal static Plugin? Instance;
    private BulkI2Localization? bulk;
    private Harmony? harmony;
    private RuntimeMonitor? monitor;
    private readonly HookTargetRegistry hooks = new();
    private int errors;
    private float nextHeartbeat = 60;

    public override void Load()
    {
        // Never bind/read/save Config. BasePlugin constructs an empty ConfigFile
        // with saveOnInit=false; with no old cfg present it creates no file.
        string version = Application.version;
        if (string.IsNullOrWhiteSpace(version)) { Log.LogError("Game version unavailable; localization unchanged."); return; }
        string cachePath = PluginCachePath.ForAssembly(typeof(Plugin).Assembly.Location);
        bulk = new BulkI2Localization(cachePath, version, Version, () =>
        {
            // Lazy factory called ONLY when a full JSON rebuild is required.
            var converter = EmbeddedOpenCc.CreateBundled(ConversionProfile.S2TW, cacheCapacity: 0);
            return text =>
            {
                var result = converter.ConvertDetailed(text);
                if (!result.Completed) throw new InvalidOperationException("Full-table conversion timed out; refusing a partial cache.");
                return result.Final;
            };
        }, message => Log.LogInfo(message), ReportError);
        Instance = this;
        harmony = new Harmony(Id);
        MethodInfo? original = AccessTools.Method(typeof(LocalizationManager), nameof(LocalizationManager.TryGetTranslation),
            new[] { typeof(string), typeof(string).MakeByRefType(), typeof(bool), typeof(int), typeof(bool),
                typeof(bool), typeof(GameObject), typeof(string), typeof(bool) });
        if (original != null && NativeHookPolicy.IsAllowed(original) && hooks.TryReserve(original, out var declared))
        {
            try
            {
                // One known static detour, PREFIX only. It never returns a cached
                // formatted result or skips I2's own parameters/plural handling.
                harmony.Patch(declared, prefix: new HarmonyMethod(typeof(Plugin), nameof(I2TranslationPrefix)));
                Log.LogInfo("Hooked I2.TryGetTranslation preflight only; no result conversion or UI hooks.");
            }
            catch (Exception error) { ReportError(error); }
        }
        try { monitor = AddComponent<RuntimeMonitor>(); }
        catch (Exception error) { ReportError(error); }
        Log.LogInfo($"Fixed options: OnlySimplifiedChinese=true, Profile=S2TW, EnablePostOverrides=true. Game version={version}.");
        Log.LogInfo("Game-owned font fallback is left completely untouched. No cfg, OS/custom fonts, scene polling or per-text conversion cache.");
        Log.LogInfo($"Full I2 JSON cache: {cachePath}");
    }

    private static void I2TranslationPrefix(string __7)
    {
        try { Instance?.bulk?.BeforeLookup(__7); }
        catch (Exception error) { Instance?.ReportError(error); }
    }

    private void ReportError(Exception error)
    {
        if (Interlocked.Increment(ref errors) <= 5) Log.LogWarning($"I2 full-cache operation unavailable; unmatched text remains original: {error.Message}");
    }

    internal void Tick()
    {
        try
        {
            float now = Time.realtimeSinceStartup;
            bulk?.Tick(now);
            if (now < nextHeartbeat) return;
            nextHeartbeat = now + 60;
            Log.LogInfo($"Full-cache heartbeat: uptime={now:F0}s; ready={bulk?.Ready}; terms={bulk?.CachedTerms}; reused={bulk?.Reused}; convertedThisRun={bulk?.ConvertedOnBuild}; applied={bulk?.Applied}; unmatched={bulk?.Unmatched}. No font changes.");
        }
        catch (Exception error) { ReportError(error); }
    }
}

public sealed class RuntimeMonitor : MonoBehaviour
{
    public RuntimeMonitor(IntPtr pointer) : base(pointer) { }
    public void Update() => Plugin.Instance?.Tick();
}