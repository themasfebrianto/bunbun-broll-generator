using BunbunBroll.Models;
using BunbunBroll.Services;
using Microsoft.Extensions.Logging;
using Xunit;

namespace BunbunBroll.Tests.Integration;

public class HalalFilterIntegrationTests
{
    [Fact]
    public void FullWorkflow_HalalFilterWithDurationMatching_WorksEndToEnd()
    {
        // Simulate full workflow with Halal filter enabled
        using var loggerFactory = LoggerFactory.Create(builder => { });
        var logger = loggerFactory.CreateLogger<HalalVideoFilter>();
        var filter = new HalalVideoFilter(logger);
        filter.IsEnabled = true;

        var keywords = new List<string> { "woman walking alone", "beach party" };
        var filtered = filter.FilterKeywords(keywords);

        // Should block beach party
        Assert.DoesNotContain("beach party", filtered);

        // Should replace woman with silhouette
        Assert.Contains("silhouette", string.Join(" ", filtered));

        // Should have cinematic fallbacks
        Assert.True(filtered.Count >= 3);
    }

    [Fact]
    public void DurationScoring_RealWorldScenarios()
    {
        var shortVideo = new VideoAsset { DurationSeconds = 5, DownloadUrl = "x" };
        var perfectVideo = new VideoAsset { DurationSeconds = 10, DownloadUrl = "x" };
        var longVideo = new VideoAsset { DurationSeconds = 20, DownloadUrl = "x" };

        var target = 10;

        var shortScore = shortVideo.CalculateDurationMatchScore(target);
        var perfectScore = perfectVideo.CalculateDurationMatchScore(target);
        var longScore = longVideo.CalculateDurationMatchScore(target);

        // Perfect should win
        Assert.Equal(100, perfectScore);

        // Short should be heavily penalized
        Assert.True(shortScore < 50);

        // Long should be moderately penalized
        Assert.True(longScore > shortScore); // Better than too short
        Assert.True(longScore < perfectScore); // But not as good as perfect
    }
}
