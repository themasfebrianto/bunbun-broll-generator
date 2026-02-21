using BunbunBroll.Models;
using Xunit;

namespace BunbunBroll.Tests.Integration;

public class DurationScoringTests
{
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
