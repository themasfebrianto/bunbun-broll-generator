using BunbunBroll.Services;
using Microsoft.Extensions.Logging;
using Xunit;

namespace BunbunBroll.Tests.Services;

public class HalalVideoFilterTests
{
    private readonly HalalVideoFilter _filter;

    public HalalVideoFilterTests()
    {
        using var loggerFactory = LoggerFactory.Create(builder => { });
        var logger = loggerFactory.CreateLogger<HalalVideoFilter>();
        _filter = new HalalVideoFilter(logger);
        _filter.IsEnabled = true;
    }

    [Fact]
    public void FilterKeywords_BlocksRevealingClothing()
    {
        var keywords = new List<string> { "woman in dress", "lady fashion", "beach party" };
        var filtered = _filter.FilterKeywords(keywords);

        Assert.DoesNotContain("beach party", filtered);
    }

    [Fact]
    public void FilterKeywords_ReplacesWomanWithNatureKeyword()
    {
        var keywords = new List<string> { "woman walking alone" };
        var filtered = _filter.FilterKeywords(keywords);

        // Should replace with nature/urban keyword, no human references
        Assert.DoesNotContain("woman", string.Join(" ", filtered));
        Assert.DoesNotContain("person", string.Join(" ", filtered));
        Assert.DoesNotContain("silhouette", string.Join(" ", filtered));
    }

    [Fact]
    public void FilterKeywords_AddsCinematicFallbacks_WhenTooManyFiltered()
    {
        var keywords = new List<string> { "bikini", "nightclub", "drinking" };
        var filtered = _filter.FilterKeywords(keywords);

        // All blocked, should add cinematic fallbacks
        Assert.True(filtered.Count >= 3);
        Assert.Contains(filtered, k => k.Contains("skyline") || k.Contains("landscape") || k.Contains("timelapse") || k.Contains("mountain") || k.Contains("forest") || k.Contains("ocean"));
    }

    [Fact]
    public void FilterKeywords_HandlesIndonesianWords()
    {
        var keywords = new List<string> { "wanita", "perempuan" };
        var filtered = _filter.FilterKeywords(keywords);

        // Should filter or replace Indonesian terms for woman
        Assert.True(filtered.Count >= 3);
    }
}
