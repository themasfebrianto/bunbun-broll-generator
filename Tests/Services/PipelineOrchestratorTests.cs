using BunbunBroll.Models;
using Xunit;

namespace BunbunBroll.Tests.Services;

public class DurationSelectionTests
{
    [Fact]
    public void SelectBestVideoByDuration_PrefersExactMatch()
    {
        // Test with target duration of 8 seconds
        var targetDuration = 8;

        var videos = new List<VideoAsset>
        {
            new() { Id = "1", DurationSeconds = 8, DownloadUrl = "https://ex1.com" },
            new() { Id = "2", DurationSeconds = 5, DownloadUrl = "https://ex2.com" }, // Too short
            new() { Id = "3", DurationSeconds = 15, DownloadUrl = "https://ex3.com" } // Too long
        };

        var best = videos
            .OrderByDescending(v => v.CalculateDurationMatchScore(targetDuration))
            .First();

        Assert.Equal("1", best.Id); // 8 seconds = perfect match
    }

    [Fact]
    public void SelectBestVideoByDuration_PrefersSlightlyLonger_OverShorter()
    {
        var targetDuration = 10;

        var videos = new List<VideoAsset>
        {
            new() { Id = "short", DurationSeconds = 8, DownloadUrl = "https://ex1.com" }, // 2s short
            new() { Id = "long", DurationSeconds = 12, DownloadUrl = "https://ex2.com" } // 2s long
        };

        var shortScore = videos[0].CalculateDurationMatchScore(targetDuration);
        var longScore = videos[1].CalculateDurationMatchScore(targetDuration);

        // Slightly longer should score better than shorter
        Assert.True(longScore > shortScore);
    }

    [Fact]
    public void SelectBestVideoByDuration_HeavilyPenalizes_TooShort()
    {
        var targetDuration = 15;

        var video = new VideoAsset
        {
            Id = "1",
            DurationSeconds = 5, // Much too short
            DownloadUrl = "https://ex1.com"
        };

        var score = video.CalculateDurationMatchScore(targetDuration);

        // Should be heavily penalized (below 50)
        Assert.True(score < 50);
    }
}
