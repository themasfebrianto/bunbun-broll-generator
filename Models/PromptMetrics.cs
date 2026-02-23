namespace BunbunBroll.Models;

/// <summary>
/// Tracks prompt compression metrics for monitoring and optimization.
/// </summary>
public class PromptMetrics
{
    /// <summary>Original prompt length before compression</summary>
    public int OriginalLength { get; set; }

    /// <summary>Final prompt length after compression</summary>
    public int CompressedLength { get; set; }

    /// <summary>Maximum recommended prompt length (default: 500 chars)</summary>
    public int MaxRecommendedLength { get; set; } = 500;

    /// <summary>Compression ratio as percentage (0-100)</summary>
    public double CompressionRatio =>
        OriginalLength > 0
            ? (OriginalLength - CompressedLength) / (double)OriginalLength * 100
            : 0;

    /// <summary>Whether the compressed prompt is within recommended limits</summary>
    public bool IsWithinRecommendedLength => CompressedLength <= MaxRecommendedLength;

    /// <summary>Estimated token savings (approximate: 1 token ~ 4 chars)</summary>
    public int EstimatedTokenSavings => (OriginalLength - CompressedLength) / 4;
}
