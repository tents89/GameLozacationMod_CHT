extern alias Il2Cpp;
using System.Runtime.CompilerServices;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace YetAnotherZombieSurvivors.TraditionalChinese.Fonts;

internal sealed class EmbeddedChineseFont
{
    private const string TargetName = "SourceHanSansSC-Normal_Chinese";
    private const string Probe = "\u7c21\u9ad4\u7e41\u9ad4\u904a\u6232\u88dd\u5099\u6bad\u5c4d\u64ca\u6bba\u8a2d\u5b9a";
    private readonly string pluginDirectory;
    private readonly Action<string> log;
    private readonly Action<Exception> error;
    private readonly int mainThread = Environment.CurrentManagedThreadId;
    private TMP_FontAsset? fallback;
    private Font? source;
    private float nextScan = 2;
    private bool attempted;
    private bool disabled;
    private bool refresh;
    private bool waitingLogged;
    private bool wasSimplified;
    private JsonHanFont? jsonFont;
    private readonly Dictionary<IntPtr, RoutedFont> routed = new();
    private sealed record RoutedFont(TMP_FontAsset Target,
        Il2Cpp::Il2CppSystem.Collections.Generic.List<TMP_Character> Characters,
        Il2Cpp::Il2CppSystem.Collections.Generic.List<TMP_FontAsset>? Fallbacks);
    public bool Ready { get; private set; }

    internal EmbeddedChineseFont(string pluginDirectory, Action<string> log, Action<Exception> error)
    { this.pluginDirectory = pluginDirectory; this.log = log; this.error = error; }

    internal bool ConsumeRefresh() { bool result = refresh; refresh = false; return result; }

    internal void Tick(float now, bool simplified)
    {
        if (disabled || Environment.CurrentManagedThreadId != mainThread) return;
        if (!simplified)
        {
            if (wasSimplified)
            {
                try { Restore(); } catch (Exception exception) { disabled = true; error(exception); }
            }
            wasSimplified = false;
            return;
        }
        if (!wasSimplified) { wasSimplified = true; nextScan = 0; }
        if (now < nextScan) return;
        nextScan = now + 2;
        try
        {
            var targets = new List<TMP_FontAsset>();
            foreach (var asset in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                if (asset != null && asset.name == TargetName) targets.Add(asset);
            if (targets.Count == 0)
            {
                if (!waitingLogged) { waitingLogged = true; log("Waiting for Chinese TMP asset before loading external Noto fallback or changing I2."); }
                return;
            }
            jsonFont ??= JsonHanFont.Create(targets[0], log);
            var settings = jsonFont.Settings;
            if (fallback == null)
            {
                if (attempted) return;
                attempted = true;
                string fontPath = ResolveFontPath();
                log("Loading external Noto Sans TC Font from DLL-adjacent _font directory: " + fontPath);
                // Initialize the verified Font interop type, allocate only its
                // IL2CPP managed shell, then invoke the actual file-path loader.
                // Do NOT construct a Font(name) or reinitialize a native Font
                // which a default constructor has already created.
                RuntimeHelpers.RunClassConstructor(typeof(Font).TypeHandle);
                IntPtr nativeFont = IL2CPP.il2cpp_object_new(Il2CppClassPointerStore<Font>.NativeClassPtr);
                if (nativeFont == IntPtr.Zero) throw new InvalidOperationException("Font managed-shell allocation failed.");
                source = new Font(nativeFont); // IntPtr wrapper, not Font(string)
                Font.Internal_CreateFontFromPath(source, fontPath);
                source.name = "YAZS.TraditionalChinese.NotoSansTC.Source";
                UnityEngine.Object.DontDestroyOnLoad(source);
                // Legacy Font.HasCharacter can fail independently of TextCore.
                // Validate the same native font face which TMP will use instead.
                FontEngineError initialized = FontEngine.InitializeFontEngine();
                log($"Noto FontEngine initialized={initialized}; loading native Font face.");
                FontEngineError loaded = FontEngine.LoadFontFace(source, settings.PointSize);
                if (loaded != FontEngineError.Success)
                    throw new InvalidOperationException($"Noto FontEngine.LoadFontFace failed: {loaded}; I2 remains original.");
                foreach (char c in Probe)
                    if (FontEngine.GetGlyphIndex(c) == 0)
                        throw new InvalidOperationException($"Noto FontEngine missing U+{(int)c:X4}; I2 remains original.");
                log($"Noto FontEngine face loaded; glyph probe={Probe.Length}/{Probe.Length}. Creating dynamic TMP fallback.");
                fallback = TMP_FontAsset.CreateFontAsset(source, settings.PointSize, settings.Padding,
                    settings.RenderMode, settings.Width, settings.Height, AtlasPopulationMode.Dynamic, true);
                if (fallback == null) throw new InvalidOperationException("Noto dynamic TMP fallback creation failed.");
                fallback.name = "YAZS.TraditionalChinese.NotoSansTC.Fallback";
                // Keep the native source sampling and TMP scaling denominator coherent.
                // This also applies to characters added by TMP later, without per-glyph patches.
                var generatedFace = fallback.faceInfo;
                log($"Dynamic native face: pointSize={generatedFace.pointSize}; scale={generatedFace.scale:F4}; ascent={generatedFace.ascentLine:F4}; descent={generatedFace.descentLine:F4}.");
                fallback.faceInfo = settings.CreateFaceInfo();
                settings.CopyStyle(fallback, targets[0]);
                UnityEngine.Object.DontDestroyOnLoad(fallback);
                foreach (char c in Probe)
                    if (!fallback.HasCharacter(c, false, true)) throw new InvalidOperationException($"Noto TMP glyph probe failed U+{(int)c:X4}; not attached.");
                var atlas = fallback.atlasTextures;
                if (atlas == null || atlas.Length == 0 || atlas[0] == null)
                    throw new InvalidDataException("Dynamic fallback atlas unavailable.");
                fallback.material = settings.CreateMaterial(targets[0], atlas[0], "YAZS.TraditionalChinese.NotoSansTC.Fallback.Material");
                fallback.ReadFontAssetDefinition();
                LogMatchedMetrics(jsonFont, fallback, log);
                // Retain assets which TMP creates so scene/resource unloading
                // cannot discard the fallback's atlas or material.
                if (fallback.material != null) UnityEngine.Object.DontDestroyOnLoad(fallback.material);
                var textures = fallback.atlasTextures;
                if (textures != null)
                    foreach (var texture in textures)
                        if (texture != null) UnityEngine.Object.DontDestroyOnLoad(texture);
            }
            int changed = 0;
            foreach (var target in targets)
            {
                if (routed.ContainsKey(target.Pointer)) continue;
                var original = target.characterTable;
                var originalFallbacks = target.fallbackFontAssetTable;
                if (original == null) throw new InvalidDataException("Original Chinese character table missing.");
                var retained = new Il2Cpp::Il2CppSystem.Collections.Generic.List<TMP_Character>();
                int routedHan = 0;
                for (int i = 0; i < original.Count; i++)
                {
                    var character = original[i];
                    if (character != null && JsonHanFont.IsHan(character.unicode) && jsonFont.Characters.Contains(character.unicode))
                    { routedHan++; continue; }
                    retained.Add(character!); // exact original objects for all other characters
                }
                var ordered = new Il2Cpp::Il2CppSystem.Collections.Generic.List<TMP_FontAsset>();
                ordered.Add(jsonFont.Asset);
                ordered.Add(fallback);
                if (originalFallbacks != null)
                    for (int i = 0; i < originalFallbacks.Count; i++)
                        if (originalFallbacks[i] != null && originalFallbacks[i].Pointer != target.Pointer)
                            ordered.Add(originalFallbacks[i]);
                // Snapshot before writes so failed lookups can be rolled back.
                routed.Add(target.Pointer, new RoutedFont(target, original, originalFallbacks));
                target.fallbackFontAssetTable = ordered;
                target.characterTable = retained;
                target.ReadFontAssetDefinition();
                foreach (char c in Probe)
                    if (!target.HasCharacter(c, true, true)) throw new InvalidOperationException($"Hybrid Han lookup failed U+{(int)c:X4}.");
                changed++;
                log($"JSON Han table routed: primary={target.name}; originalChars={original.Count}; retainedChars={retained.Count}; replacedExistingHan={routedHan}; jsonHan={jsonFont.Characters.Count}. Original primary, glyph table, face info, material and non-Han characters retained.");
            }
            Ready = true;
            if (changed > 0) refresh = true;
        }
        catch (Exception exception)
        {
            disabled = true; Ready = false;
            error(exception);
            try { Restore(); } catch (Exception rollback) { error(rollback); }
            log("JSON Han font operation failed; restored owned character tables and stopped attempts. Restart without the DLL if display remains altered.");
        }
    }

    private static void LogMatchedMetrics(JsonHanFont baked, TMP_FontAsset dynamicFont, Action<string> log)
    {
        // Compare common probe glyphs at the SAME sampling size; report native
        // hinting differences rather than silently applying a guessed multiplier.
        int compared = 0;
        float maxDelta = 0, maxScaleDelta = 0;
        foreach (uint unicode in Probe.Select(c => (uint)c).Distinct())
        {
            if (!baked.Asset.characterLookupTable.ContainsKey(unicode)
                || !dynamicFont.characterLookupTable.ContainsKey(unicode)) continue;
            var left = baked.Asset.characterLookupTable[unicode];
            var right = dynamicFont.characterLookupTable[unicode];
            var a = left.glyph.metrics; var b = right.glyph.metrics;
            maxDelta = MathF.Max(maxDelta, MathF.Abs(a.width - b.width));
            maxDelta = MathF.Max(maxDelta, MathF.Abs(a.height - b.height));
            maxDelta = MathF.Max(maxDelta, MathF.Abs(a.horizontalBearingX - b.horizontalBearingX));
            maxDelta = MathF.Max(maxDelta, MathF.Abs(a.horizontalBearingY - b.horizontalBearingY));
            maxDelta = MathF.Max(maxDelta, MathF.Abs(a.horizontalAdvance - b.horizontalAdvance));
            maxScaleDelta = MathF.Max(maxScaleDelta, MathF.Abs(left.scale - right.scale));
            maxScaleDelta = MathF.Max(maxScaleDelta, MathF.Abs(left.glyph.scale - right.glyph.scale));
            compared++;
        }
        var settings = baked.Settings;
        log($"Baked/dynamic alignment: pointSize={settings.PointSize}; padding={settings.Padding}; renderMode={(int)settings.RenderMode}; atlas={settings.Width}x{settings.Height}; shared JSON face metrics/style/material effects; comparedGlyphs={compared}; maxMetricDelta={maxDelta:F4}; maxScaleDelta={maxScaleDelta:F4}. Primary face/layout untouched.");
    }

    private void Restore()
    {
        foreach (var state in routed.Values)
        {
            if (state.Target == null) continue;
            state.Target.characterTable = state.Characters;
            state.Target.fallbackFontAssetTable = state.Fallbacks;
            state.Target.ReadFontAssetDefinition();
        }
        routed.Clear();
        refresh = false;
        log("Original Chinese character/fallback tables restored; no label, font mapping or material changes.");
    }

    private string ResolveFontPath()
    {
        // User-managed input, never extract, create, replace or migrate a font.
        // Resolve from the loaded DLL, not the current directory or _cache.
        string path = Path.Combine(pluginDirectory, "_font", "NotoSans.ttf");
        if (!File.Exists(path))
            throw new FileNotFoundException("Required fallback font missing. Place NotoSans.ttf in the _font folder beside the plugin DLL, then restart the game. I2 remains unchanged.", path);
        using var stream = File.OpenRead(path);
        if (stream.Length is < 12 or > 64L * 1024 * 1024)
            throw new InvalidDataException("Invalid external font file size: " + path);
        Span<byte> header = stackalloc byte[4];
        if (stream.Read(header) != header.Length
            || !(header.SequenceEqual(new byte[] { 0, 1, 0, 0 })
                || header.SequenceEqual(new byte[] { 0x4F, 0x54, 0x54, 0x4F })))
            throw new InvalidDataException("Invalid external TTF/OpenType font header: " + path);
        // Native FontEngine/TMP probes below validate actual glyph support.
        // No fixed hash: users own this file, but it should match the JSON bake.
        return path;
    }
}
