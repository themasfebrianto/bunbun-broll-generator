using BunbunBroll.Models;
using Xunit;

namespace BunbunBroll.Tests.Models;

public class VideoAssetTests
{
    [Fact]
    public void DurationMatchScore_ReturnsZero_WhenNoTargetDuration()
    {
        var asset = new VideoAsset
        {
            DurationSeconds = 10,
            DownloadUrl = "https://example.com/video.mp4"
        };

        var score = asset.CalculateDurationMatchScore(targetDurationSeconds: null);

        Assert.Equal(0, score);
    }

    [Fact]
    public void DurationMatchScore_PenalizesShortVideos_Heavily()
    {
        var asset = new VideoAsset
        {
            DurationSeconds = 5,
            DownloadUrl = "https://example.com/video.mp4"
        };

        // Sentence is 10 seconds, video is only 5 seconds
        var score = asset.CalculateDurationMatchScore(targetDurationSeconds: 10);

        // Should be heavily penalized (below 50)
        Assert.True(score < 50);
    }

    [Fact]
    public void DurationMatchScore_PrefersSlightlyLongerVideos()
    {
        var asset = new VideoAsset
        {
            DurationSeconds = 12,
            DownloadUrl = "https://example.com/video.mp4"
        };

        // Sentence is 10 seconds, video is 12 seconds (acceptable)
        var score = asset.CalculateDurationMatchScore(targetDurationSeconds: 10);

        // Should have good score (above 80)
        Assert.True(score > 80);
    }

    [Fact]
    public void DurationMatchScore_PenalizesVeryLongVideos()
    {
        var asset = new VideoAsset
        {
            DurationSeconds = 30,
            DownloadUrl = "https://example.com/video.mp4"
        };

        // Sentence is 10 seconds, video is 30 seconds (too long)
        var score = asset.CalculateDurationMatchScore(targetDurationSeconds: 10);

        // Should be penalized (below 70)
        Assert.True(score < 70);
    }

    [Fact]
    public void DurationMatchScore_PerfectMatch_Returns100()
    {
        var asset = new VideoAsset
        {
            DurationSeconds = 10,
            DownloadUrl = "https://example.com/video.mp4"
        };

        var score = asset.CalculateDurationMatchScore(targetDurationSeconds: 10);

        Assert.Equal(100, score);
    }
}
