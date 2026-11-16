using System.Text.Json;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

namespace YetAnotherZombieSurvivors.TraditionalChinese.Fonts;

// The exported atlas is authoritative for BOTH static and future dynamic glyphs.
// Do not borrow the primary font face or compensate individual character scales:
// TMP already scales a fallback by its own faceInfo.pointSize / faceInfo.scale.
internal sealed class FontBakeSettings
{
    internal int PointSize { get; }
    internal int Width { get; }
    internal int Height { get; }
    internal int Padding { get; }
    internal GlyphRenderMode RenderMode { get; }
    private readonly JsonElement face;

    internal FontBakeSettings(JsonElement root)
    {
        face = root.GetProperty("m_FaceInfo").Clone();
        float pointSize = Number("m_PointSize");
        if (pointSize is < 1 or > 512 || pointSize != MathF.Truncate(pointSize))
            throw new InvalidDataException("Invalid pre-baked font sampling size.");
        PointSize = (int)pointSize;
        Width = root.GetProperty("m_AtlasWidth").GetInt32();
        Height = root.GetProperty("m_AtlasHeight").GetInt32();
        Padding = root.GetProperty("m_AtlasPadding").GetInt32();
        RenderMode = (GlyphRenderMode)root.GetProperty("m_AtlasRenderMode").GetInt32();
        if (Width is < 1 or > 8192 || Height is < 1 or > 8192 || Padding is < 0 or > 64
            || Number("m_Scale") <= 0 || !Enum.IsDefined(typeof(GlyphRenderMode), RenderMode))
            throw new InvalidDataException("Invalid pre-baked font settings.");
        var creation = root.GetProperty("m_CreationSettings");
        if (creation.GetProperty("pointSize").GetInt32() != PointSize
            || creation.GetProperty("padding").GetInt32() != Padding
            || creation.GetProperty("renderMode").GetInt32() != (int)RenderMode)
            throw new InvalidDataException("Inconsistent pre-baked font creation settings.");
    }

    private float Number(string key)
    {
        float value = face.GetProperty(key).GetSingle();
        if (!float.IsFinite(value)) throw new InvalidDataException("Non-finite JSON face metric: " + key);
        return value;
    }

    internal FaceInfo CreateFaceInfo() => new FaceInfo
    {
        faceIndex = face.GetProperty("m_FaceIndex").GetInt32(),
        familyName = face.GetProperty("m_FamilyName").GetString()!,
        styleName = face.GetProperty("m_StyleName").GetString()!,
        pointSize = PointSize, scale = Number("m_Scale"),
        unitsPerEM = face.GetProperty("m_UnitsPerEM").GetInt32(),
        lineHeight = Number("m_LineHeight"), ascentLine = Number("m_AscentLine"),
        capLine = Number("m_CapLine"), meanLine = Number("m_MeanLine"),
        baseline = Number("m_Baseline"), descentLine = Number("m_DescentLine"),
        superscriptOffset = Number("m_SuperscriptOffset"), superscriptSize = Number("m_SuperscriptSize"),
        subscriptOffset = Number("m_SubscriptOffset"), subscriptSize = Number("m_SubscriptSize"),
        underlineOffset = Number("m_UnderlineOffset"), underlineThickness = Number("m_UnderlineThickness"),
        strikethroughOffset = Number("m_StrikethroughOffset"), strikethroughThickness = Number("m_StrikethroughThickness"),
        tabWidth = Number("m_TabWidth")
    };

    internal void CopyStyle(TMP_FontAsset asset, TMP_FontAsset original)
    {
        asset.normalStyle = original.normalStyle; asset.normalSpacingOffset = original.normalSpacingOffset;
        asset.boldStyle = original.boldStyle; asset.boldSpacing = original.boldSpacing;
        asset.italicStyle = original.italicStyle; asset.tabSize = original.tabSize;
    }

    internal Material CreateMaterial(TMP_FontAsset original, Texture2D texture, string name)
    {
        if (original.material == null) throw new InvalidDataException("Original Chinese material unavailable.");
        var material = new Material(original.material) { name = name, mainTexture = texture };
        // Identical SDF spread / shader effects for baked and dynamic donor atlases.
        material.SetFloat("_TextureWidth", Width); material.SetFloat("_TextureHeight", Height);
        material.SetFloat("_GradientScale", Padding + 1);
        return material;
    }
}
