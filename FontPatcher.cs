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
    private TMP_FontAsset _originalDefaultFont;
    private string _fontFolderPath;
    private bool _initialized;

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
        // In IL2CPP, Font(string) constructor doesn't exist.
        // Use the file-path overload of TMP_FontAsset.CreateFontAsset directly.
        _customFontAsset = TMP_FontAsset.CreateFontAsset(
            fontFilePath,
            0,                                          // faceIndex
            44,                                         // samplingPointSize
            4,                                          // atlasPadding
            UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, // renderMode
            4096,                                       // atlasWidth
            4096,                                       // atlasHeight
            AtlasPopulationMode.Dynamic,                // dynamic glyph population
            true                                        // enableMultiAtlasSupport
        );

        if (_customFontAsset == null)
        {
            throw new Exception("TMP_FontAsset.CreateFontAsset returned null");
        }

        _customFontAsset.name = "CustomFont_" + Path.GetFileNameWithoutExtension(fontFilePath);

        // Hide the asset from Unity's garbage collection
        _customFontAsset.hideFlags = HideFlags.DontUnloadUnusedAsset;

        _logger.Msg($"Created TMP_FontAsset: {_customFontAsset.name}");
        _logger.Msg($"Atlas size: {_customFontAsset.atlasWidth}x{_customFontAsset.atlasHeight}");
    }

    public void OnSceneLoaded()
    {
        if (!_initialized || _customFontAsset == null)
            return;

        try
        {
            ReplaceGlobalDefault();
            ReplaceAllTextComponents();
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to replace fonts in scene: {ex}");
        }
    }

    private void ReplaceGlobalDefault()
    {
        var settings = TMP_Settings.instance;
        if (settings == null)
        {
            _logger.Warning("TMP_Settings.instance is null, skipping global default replacement");
            return;
        }

        if (_originalDefaultFont == null)
        {
            _originalDefaultFont = TMP_Settings.defaultFontAsset;
            if (_originalDefaultFont != null)
            {
                _logger.Msg($"Original default font: {_originalDefaultFont.name}");

                // Add original font as fallback so missing glyphs still render
                if (_customFontAsset.fallbackFontAssetTable == null)
                {
                    _customFontAsset.fallbackFontAssetTable = new Il2CppSystem.Collections.Generic.List<TMP_FontAsset>();
                }
                _customFontAsset.fallbackFontAssetTable.Add(_originalDefaultFont);
            }
        }

        TMP_Settings.defaultFontAsset = _customFontAsset;

        // Also add to global fallback list
        var globalFallbacks = TMP_Settings.fallbackFontAssets;
        if (globalFallbacks != null)
        {
            bool alreadyAdded = false;
            for (int i = 0; i < globalFallbacks.Count; i++)
            {
                if (globalFallbacks[i] != null && globalFallbacks[i].name == _customFontAsset.name)
                {
                    alreadyAdded = true;
                    break;
                }
            }
            if (!alreadyAdded)
            {
                globalFallbacks.Insert(0, _customFontAsset);
            }
        }

        _logger.Msg("Global default font replaced.");
    }

    private void ReplaceAllTextComponents()
    {
        int replacedCount = 0;

        // Find all TextMeshProUGUI components (UI text)
        var uguiTexts = Resources.FindObjectsOfTypeAll(Il2CppType.Of<TextMeshProUGUI>());
        foreach (var obj in uguiTexts)
        {
            var tmpText = obj.Cast<TextMeshProUGUI>();
            if (tmpText == null) continue;

            var currentFont = tmpText.font;
            if (currentFont != null && currentFont.name == _customFontAsset.name)
                continue;

            // Store original font as fallback if not already in the list
            if (currentFont != null && currentFont.name != _customFontAsset.name)
            {
                AddFallbackIfMissing(currentFont);
            }

            tmpText.font = _customFontAsset;
            replacedCount++;
        }

        // Find all TextMeshPro components (3D world text)
        var worldTexts = Resources.FindObjectsOfTypeAll(Il2CppType.Of<TextMeshPro>());
        foreach (var obj in worldTexts)
        {
            var tmpText = obj.Cast<TextMeshPro>();
            if (tmpText == null) continue;

            var currentFont = tmpText.font;
            if (currentFont != null && currentFont.name == _customFontAsset.name)
                continue;

            if (currentFont != null && currentFont.name != _customFontAsset.name)
            {
                AddFallbackIfMissing(currentFont);
            }

            tmpText.font = _customFontAsset;
            replacedCount++;
        }

        if (replacedCount > 0)
            _logger.Msg($"Replaced font on {replacedCount} text component(s).");
    }

    private void AddFallbackIfMissing(TMP_FontAsset fontAsset)
    {
        if (_customFontAsset.fallbackFontAssetTable == null)
        {
            _customFontAsset.fallbackFontAssetTable = new Il2CppSystem.Collections.Generic.List<TMP_FontAsset>();
        }

        for (int i = 0; i < _customFontAsset.fallbackFontAssetTable.Count; i++)
        {
            if (_customFontAsset.fallbackFontAssetTable[i] != null &&
                _customFontAsset.fallbackFontAssetTable[i].name == fontAsset.name)
                return;
        }

        _customFontAsset.fallbackFontAssetTable.Add(fontAsset);
    }
}
