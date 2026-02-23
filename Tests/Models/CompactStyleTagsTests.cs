using BunbunBroll.Models;
using Xunit;

namespace BunbunBroll.Tests.Models;

public class CompactStyleTagsTests
{
    [Theory]
    [InlineData(ImageArtStyle.OilPainting, "oil painting")]
    [InlineData(ImageArtStyle.Watercolor, "watercolor")]
    [InlineData(ImageArtStyle.DigitalArt, "digital art")]
    public void GetCompactArtStyleTag_ReturnsShortTag(ImageArtStyle style, string expected)
    {
        var result = CompactStyleTags.GetArtStyleTag(style);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(ImageLighting.DramaticHighContrast, "dramatic lighting")]
    [InlineData(ImageLighting.GoldenHour, "golden hour")]
    [InlineData(ImageLighting.SoftAmbient, "soft ambient")]
    public void GetCompactLightingTag_ReturnsShortTag(ImageLighting lighting, string expected)
    {
        var result = CompactStyleTags.GetLightingTag(lighting);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void BuildCompactSuffix_WithAllSettings_ReturnsConciseString()
    {
        var config = new ImagePromptConfig
        {
            ArtStyle = ImageArtStyle.OilPainting,
            Lighting = ImageLighting.GoldenHour,
            ColorPalette = ImageColorPalette.WarmEarthy,
            Composition = ImageComposition.UltraWideEstablishing
        };

        var result = CompactStyleTags.BuildCompactSuffix(config);

        Assert.Contains("oil painting", result);
        Assert.DoesNotContain("golden hour", result);
        Assert.Contains("warm earthy", result);
        Assert.DoesNotContain("ultra-wide establishing", result);
        // Should be concise - under 150 chars
        Assert.True(result.Length < 150, $"Suffix too long: {result.Length} chars");
    }

    [Fact]
    public void BuildCompactSuffix_WithAutoSettings_SkipsAutoTags()
    {
        var config = new ImagePromptConfig
        {
            ArtStyle = ImageArtStyle.SemiRealisticPainting,
            Lighting = ImageLighting.Auto,
            ColorPalette = ImageColorPalette.Auto,
            Composition = ImageComposition.Auto
        };

        var result = CompactStyleTags.BuildCompactSuffix(config);

        // Should only contain art style, no "auto" references
        Assert.Contains("semi-realistic", result);
        Assert.DoesNotContain("auto", result.ToLower());
    }

    [Fact]
    public void ImagePromptConfig_EffectiveStyleSuffix_UsesCompactFormat()
    {
        var config = new ImagePromptConfig
        {
            ArtStyle = ImageArtStyle.OilPainting,
            Lighting = ImageLighting.GoldenHour,
            ColorPalette = ImageColorPalette.WarmEarthy
        };

        var result = config.EffectiveStyleSuffix;

        // Should use compact format
        Assert.Contains("oil painting", result);
        Assert.DoesNotContain("golden hour", result);
        Assert.Contains("warm earthy", result);
        // Should NOT contain verbose old tags
        Assert.DoesNotContain("rich impasto textures", result);
        Assert.DoesNotContain("expressive painterly textures", result);
        Assert.DoesNotContain("atmospheric depth", result);
    }

    [Fact]
    public void ImagePromptConfig_EffectiveStyleSuffix_IsUnder200Chars()
    {
        var config = new ImagePromptConfig
        {
            ArtStyle = ImageArtStyle.SemiRealisticPainting,
            Lighting = ImageLighting.DramaticHighContrast,
            ColorPalette = ImageColorPalette.VibrantFocalMuted,
            Composition = ImageComposition.DynamicAction
        };

        var result = config.EffectiveStyleSuffix;

        Assert.True(result.Length < 200, $"Suffix too long: {result.Length} chars: {result}");
    }
}
