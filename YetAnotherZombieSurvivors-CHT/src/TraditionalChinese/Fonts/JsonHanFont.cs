extern alias Il2Cpp;
using System.Text.Json;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;
using Native = Il2Cpp::Il2CppSystem.Collections.Generic;

namespace YetAnotherZombieSurvivors.TraditionalChinese.Fonts;

// Reconstruct typed glyphs, never JsonUtility-load external instance IDs.
// The glyph rectangles and the PNG are an inseparable pre-baked atlas pair.
internal sealed class JsonHanFont
{
    internal TMP_FontAsset Asset { get; }
    internal HashSet<uint> Characters { get; }
    internal FontBakeSettings Settings { get; }
    private JsonHanFont(TMP_FontAsset asset, HashSet<uint> characters, FontBakeSettings settings)
    { Asset = asset; Characters = characters; Settings = settings; }

    internal static bool IsHan(uint value) => value is >= 0x3400 and <= 0x9FFF
        or >= 0xF900 and <= 0xFAFF or >= 0x20000 and <= 0x323AF;

    private static byte[] Resource(string name)
    {
        using var stream = typeof(JsonHanFont).Assembly.GetManifestResourceStream("TraditionalChinese.Font." + name)
            ?? throw new FileNotFoundException("Missing Han font resource: " + name);
        using var memory = new MemoryStream(); stream.CopyTo(memory); return memory.ToArray();
    }

    internal static JsonHanFont Create(TMP_FontAsset original, Action<string> log)
    {
        using var json = JsonDocument.Parse(Resource("NotoSansTC.json"));
        var root = json.RootElement;
        var settings = new FontBakeSettings(root);
        int width = settings.Width, height = settings.Height, padding = settings.Padding;
        var sourceGlyphs = new Dictionary<uint, JsonElement>();
        foreach (var item in root.GetProperty("m_GlyphTable").GetProperty("Array").EnumerateArray())
        {
            uint id = item.GetProperty("m_Index").GetUInt32();
            if (!sourceGlyphs.TryAdd(id, item)) throw new InvalidDataException("Duplicate JSON glyph index.");
        }
        var selected = new List<(uint Unicode, uint Glyph, float Scale)>();
        var characters = new HashSet<uint>();
        foreach (var item in root.GetProperty("m_CharacterTable").GetProperty("Array").EnumerateArray())
        {
            uint unicode = item.GetProperty("m_Unicode").GetUInt32();
            if (!IsHan(unicode)) continue; // never substitute Latin, digits, symbols
            uint glyph = item.GetProperty("m_GlyphIndex").GetUInt32();
            float scale = item.GetProperty("m_Scale").GetSingle();
            if (!sourceGlyphs.ContainsKey(glyph) || !characters.Add(unicode) || !float.IsFinite(scale) || scale <= 0)
                throw new InvalidDataException("Invalid JSON Han character mapping.");
            selected.Add((unicode, glyph, scale));
        }
        if (selected.Count == 0 || selected.Count > 100000) throw new InvalidDataException("Invalid Han character count.");
        var asset = ScriptableObject.CreateInstance<TMP_FontAsset>();
        asset.name = "YAZS.TraditionalChinese.JsonHanAtlas";
        asset.version = "1.1.0";
        asset.atlasPopulationMode = AtlasPopulationMode.Static;
        asset.faceInfo = settings.CreateFaceInfo(); // own baked metrics; primary face remains untouched
        asset.atlasWidth = width; asset.atlasHeight = height; asset.atlasPadding = padding;
        asset.atlasRenderMode = settings.RenderMode;
        asset.isMultiAtlasTexturesEnabled = false;
        var glyphs = new Native.List<Glyph>();
        var glyphMap = new Dictionary<uint, Glyph>();
        foreach (uint id in selected.Select(c => c.Glyph).Distinct())
        {
            var item = sourceGlyphs[id]; var metrics = item.GetProperty("m_Metrics"); var rect = item.GetProperty("m_GlyphRect");
            int x = rect.GetProperty("m_X").GetInt32(), y = rect.GetProperty("m_Y").GetInt32();
            int w = rect.GetProperty("m_Width").GetInt32(), h = rect.GetProperty("m_Height").GetInt32();
            if (x < 0 || y < 0 || w < 0 || h < 0 || x + w > width || y + h > height || item.GetProperty("m_AtlasIndex").GetInt32() != 0)
                throw new InvalidDataException("JSON glyph rectangle does not match the single PNG atlas.");
            var m = new GlyphMetrics(metrics.GetProperty("m_Width").GetSingle(), metrics.GetProperty("m_Height").GetSingle(),
                metrics.GetProperty("m_HorizontalBearingX").GetSingle(), metrics.GetProperty("m_HorizontalBearingY").GetSingle(),
                metrics.GetProperty("m_HorizontalAdvance").GetSingle());
            var glyph = new Glyph(id, m, new GlyphRect(x, y, w, h), item.GetProperty("m_Scale").GetSingle(), 0);
            glyphs.Add(glyph); glyphMap.Add(id, glyph);
        }
        var table = new Native.List<TMP_Character>();
        foreach (var item in selected)
        {
            var character = new TMP_Character(item.Unicode, asset, glyphMap[item.Glyph]);
            character.scale = item.Scale; // point-size normalization is handled by TMP, once
            table.Add(character);
        }
        asset.glyphTable = glyphs;
        asset.characterTable = table;
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
        if (!ImageConversion.LoadImage(texture, new Il2CppStructArray<byte>(Resource("NotoSansTC.png")), false)
            || texture.width != width || texture.height != height)
            throw new InvalidDataException("Cannot load the matching JSON atlas PNG.");
        texture.name = "YAZS.TraditionalChinese.JsonHanAtlas.Texture";
        texture.filterMode = FilterMode.Bilinear; texture.wrapMode = TextureWrapMode.Clamp;
        asset.atlasTextures = new Il2CppReferenceArray<Texture2D>(new[] { texture });
        var material = settings.CreateMaterial(original, texture, "YAZS.TraditionalChinese.JsonHanAtlas.Material");
        asset.material = material;
        settings.CopyStyle(asset, original);
        asset.ReadFontAssetDefinition();
        foreach (uint unicode in characters)
            if (!asset.characterLookupTable.ContainsKey(unicode)) throw new InvalidOperationException("Imported Han lookup incomplete.");
        UnityEngine.Object.DontDestroyOnLoad(asset);
        UnityEngine.Object.DontDestroyOnLoad(texture);
        UnityEngine.Object.DontDestroyOnLoad(material);
        log($"Pre-baked JSON Han asset ready: chars={table.Count}; glyphs={glyphs.Count}; atlas={width}x{height}; pointSize={settings.PointSize}; faceScale={asset.faceInfo.scale:F4}; padding={padding}; renderMode={(int)settings.RenderMode}. PNG alpha/SDF preserved; no external instance IDs imported.");
        return new JsonHanFont(asset, characters, settings);
    }
}
