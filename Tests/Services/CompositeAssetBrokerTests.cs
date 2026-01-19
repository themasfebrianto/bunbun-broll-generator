using BunbunBroll.Services;
using Xunit;

namespace BunbunBroll.Tests.Services;

public class AdaptiveDurationTests
{
    [Fact]
    public void CalculateAdaptiveDurationRange_ShortSentence_TightRange()
    {
        // 3 second sentence - tight range
        var (min, max) = CompositeAssetBroker.CalculateAdaptiveDurationRange(3);

        // Should be very tight for short sentences
        Assert.Equal(3, min);
        Assert.True(max <= 8); // Max 5 seconds excess
    }

    [Fact]
    public void CalculateAdaptiveDurationRange_LongSentence_WiderRange()
    {
        // 30 second sentence - wider range
        var (min, max) = CompositeAssetBroker.CalculateAdaptiveDurationRange(30);

        // Should allow more flexibility for long sentences
        Assert.True(min >= 25); // Allow 5 seconds short
        Assert.True(max >= 40); // Allow 10+ seconds long
    }

    [Fact]
    public void CalculateAdaptiveDurationRange_NeverBelowMinimum()
    {
        var (min, max) = CompositeAssetBroker.CalculateAdaptiveDurationRange(2);

        // Minimum 3 seconds regardless
        Assert.Equal(3, min);
    }
}
