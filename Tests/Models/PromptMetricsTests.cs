using BunbunBroll.Models;
using Xunit;

namespace BunbunBroll.Tests.Models;

public class PromptMetricsTests
{
    [Fact]
    public void PromptMetrics_TracksOriginalAndCompressedLength()
    {
        var metrics = new PromptMetrics
        {
            OriginalLength = 800,
            CompressedLength = 250
        };

        Assert.Equal(800, metrics.OriginalLength);
        Assert.Equal(250, metrics.CompressedLength);
        Assert.Equal(68.75, metrics.CompressionRatio); // (800-250)/800 * 100
    }

    [Fact]
    public void PromptMetrics_ValidatesMaxLength()
    {
        var metrics = new PromptMetrics
        {
            OriginalLength = 1000,
            CompressedLength = 400,
            MaxRecommendedLength = 500
        };

        Assert.True(metrics.IsWithinRecommendedLength);
    }

    [Fact]
    public void PromptMetrics_DetectsExcessiveLength()
    {
        var metrics = new PromptMetrics
        {
            OriginalLength = 1000,
            CompressedLength = 600,
            MaxRecommendedLength = 500
        };

        Assert.False(metrics.IsWithinRecommendedLength);
    }
}
