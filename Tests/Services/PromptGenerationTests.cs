using BunbunBroll.Models;
using BunbunBroll.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BunbunBroll.Tests.Services;

public class PromptGenerationTests
{
    [Fact]
    public void GeneratePrompt_SystemPrompt_IsConcise()
    {
        // This test verifies the system prompt structure through the service
        // We can't easily test the private method, but we can verify the config
        var config = new ImagePromptConfig
        {
            ArtStyle = ImageArtStyle.OilPainting,
            Lighting = ImageLighting.GoldenHour
        };

        var suffix = config.EffectiveStyleSuffix;

        // System prompt uses EffectiveStyleSuffix - should be concise
        Assert.True(suffix.Length < 200,
            $"Style suffix too long for system prompt: {suffix.Length} chars");
    }

    [Fact]
    public void EffectiveStyleSuffix_ContainsNoVerbosePhrases()
    {
        var config = new ImagePromptConfig
        {
            ArtStyle = ImageArtStyle.SemiRealisticPainting,
            Lighting = ImageLighting.DramaticHighContrast,
            ColorPalette = ImageColorPalette.WarmEarthy,
            Composition = ImageComposition.UltraWideEstablishing
        };

        var suffix = config.EffectiveStyleSuffix;

        // Should not contain old verbose phrases
        Assert.DoesNotContain("traditional Islamic iconography mixed with Western historical art influences", suffix);
        Assert.DoesNotContain("visible brushstrokes", suffix);
        Assert.DoesNotContain("dramatic high-contrast lighting with directional illumination", suffix);
    }
}
