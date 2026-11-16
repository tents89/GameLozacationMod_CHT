using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using Il2CppTMPro;
using Il2CppInterop.Runtime;

namespace FontReplacerMod;

public class FontPatcher
{
    private readonly MelonLogger.Instance _logger;
    private TMP_FontAsset _customFontAsset;
    private string _fontFolderPath;
    private bool _initialized;
    private readonly HashSet<int> _patchedFontIds = new();

    public FontPatcher(MelonLogger.Instance logger)
    {
        _logger = logger;
    }

    public void Initialize()
    {
        _fontFolderPath = Path.Combine(MelonEnvironment.UserDataDirectory, "FontReplacer");

        if (!Directory.Exists(_fontFolderPath))
        {
            Directory.CreateDirectory(_fontFolderPath);
            _logger.Msg($"Created font folder: {_fontFolderPath}");
            _logger.Msg("Please place your .ttf or .otf font file in this folder and restart the game.");
            return;
        }

        var fontFile = FindFontFile();
        if (fontFile == null)
        {
            _logger.Warning("No .ttf or .otf font file found in UserData/FontReplacer/");
            _logger.Msg("Please place your font file there and restart the game.");
            return;
        }

        _logger.Msg($"Found font file: {Path.GetFileName(fontFile)}");

        try
        {
            LoadAndCreateFontAsset(fontFile);
            _initialized = true;
            _logger.Msg("Font asset created successfully!");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to create font asset: {ex}");
        }
    }

    private string FindFontFile()
    {
        var extensions = new[] { "*.ttf", "*.otf" };
        foreach (var ext in extensions)
        {
            var files = Directory.GetFiles(_fontFolderPath, ext);
            if (files.Length > 0)
                return files[0];
        }
        return null;
    }

    private void LoadAndCreateFontAsset(string fontFilePath)
    {
        _customFontAsset = TMP_FontAsset.CreateFontAsset(
            fontFilePath,
            0,
            44,
            4,
            UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
            4096,
            4096,
            AtlasPopulationMode.Dynamic,
            true
        );

        if (_customFontAsset == null)
            throw new Exception("TMP_FontAsset.CreateFontAsset returned null");

        _customFontAsset.name = "CustomFont_" + Path.GetFileNameWithoutExtension(fontFilePath);
        _customFontAsset.hideFlags = HideFlags.DontUnloadUnusedAsset;

        _logger.Msg($"Created TMP_FontAsset: {_customFontAsset.name}");
    }

    public void OnSceneLoaded()
    {
        if (!_initialized || _customFontAsset == null)
            return;

        try
        {
            InjectFallbackToAllFonts();
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to inject fallback fonts: {ex}");
        }
    }

    private void InjectFallbackToAllFonts()
    {
        int injectedCount = 0;

        // Find every TMP_FontAsset loaded in the game
        var allFontAssets = Resources.FindObjectsOfTypeAll(Il2CppType.Of<TMP_FontAsset>());
        foreach (var obj in allFontAssets)
        {
            var fontAsset = obj.Cast<TMP_FontAsset>();
            if (fontAsset == null) continue;

            // Don't add fallback to our own custom font
            if (fontAsset.name == _customFontAsset.name) continue;

            int id = fontAsset.GetInstanceID();
            if (_patchedFontIds.Contains(id)) continue;

            // Inject custom font into this font's fallback table
            if (fontAsset.fallbackFontAssetTable == null)
                fontAsset.fallbackFontAssetTable = new Il2CppSystem.Collections.Generic.List<TMP_FontAsset>();

            // Check not already present
            bool alreadyPresent = false;
            for (int i = 0; i < fontAsset.fallbackFontAssetTable.Count; i++)
            {
                var entry = fontAsset.fallbackFontAssetTable[i];
                if (entry != null && entry.name == _customFontAsset.name)
                {
                    alreadyPresent = true;
                    break;
                }
            }

            if (!alreadyPresent)
            {
                fontAsset.fallbackFontAssetTable.Insert(0, _customFontAsset);
                injectedCount++;
            }

            _patchedFontIds.Add(id);
        }

        // Also inject into TMP_Settings global fallback list
        var globalFallbacks = TMP_Settings.fallbackFontAssets;
        if (globalFallbacks != null)
        {
            bool found = false;
            for (int i = 0; i < globalFallbacks.Count; i++)
            {
                if (globalFallbacks[i] != null && globalFallbacks[i].name == _customFontAsset.name)
                {
                    found = true;
                    break;
                }
            }
            if (!found)
                globalFallbacks.Insert(0, _customFontAsset);
        }

        if (injectedCount > 0)
            _logger.Msg($"Injected custom font as fallback into {injectedCount} font asset(s).");
    }
}
