using BunbunBroll.Services;
using BunbunBroll.Models;
using Xunit;

namespace BunbunBroll.Tests.Services;

public class SrtServiceTests
{
    private readonly SrtService _service = new();

    [Fact]
    public void ParseSrt_MaintainsPrecision()
    {
        var srt = "1\n00:00:01,123 --> 00:00:04,456\nHello World";
        var results = _service.ParseSrt(srt);

        Assert.Single(results);
        Assert.Equal(1, results[0].StartTime.Seconds);
        Assert.Equal(123, results[0].StartTime.Milliseconds);
        Assert.Equal(4, results[0].EndTime.Seconds);
        Assert.Equal(456, results[0].EndTime.Milliseconds);
    }

    [Fact]
    public void MergeToSegments_AggregatesUnder35s()
    {
        var entries = new List<SrtEntry>
        {
            new() { StartTime = TimeSpan.FromSeconds(0), EndTime = TimeSpan.FromSeconds(10), Text = "One" },
            new() { StartTime = TimeSpan.FromSeconds(10), EndTime = TimeSpan.FromSeconds(20), Text = "Two" },
            new() { StartTime = TimeSpan.FromSeconds(20), EndTime = TimeSpan.FromSeconds(30), Text = "Three" },
            new() { StartTime = TimeSpan.FromSeconds(30), EndTime = TimeSpan.FromSeconds(40), Text = "Four" }
        };

        var merged = _service.MergeToSegments(entries, 35.0);

        // Expected: 
        // 1. One + Two + Three (0s to 30s) = 30s < 35s. 
        // 2. Add Four (0s to 40s) = 40s > 35s. So [One, Two, Three] merged, [Four] separate.
        Assert.Equal(2, merged.Count);
        Assert.Equal("[00:00.000]", merged[0].Timestamp);
        Assert.Equal("One Two Three", merged[0].Text);
        Assert.Equal("[00:30.000]", merged[1].Timestamp);
        Assert.Equal("Four", merged[1].Text);
    }

    [Fact]
    public void MergeToSegments_PreservesMilliseconds()
    {
        var entries = new List<SrtEntry>
        {
            new() { StartTime = TimeSpan.FromMilliseconds(1500), EndTime = TimeSpan.FromSeconds(10), Text = "Test" }
        };

        var merged = _service.MergeToSegments(entries, 35.0);

        Assert.Equal("[00:01.500]", merged[0].Timestamp);
    }
}
