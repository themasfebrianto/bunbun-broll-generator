namespace BunbunBroll.Models;

/// <summary>
/// Represents a video asset from an external provider (e.g., Pexels).
/// </summary>
public class VideoAsset
{
    public string Id { get; set; } = string.Empty;
    public string Provider { get; set; } = "Pexels";
    public string Title { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;
    public string PreviewUrl { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public int DurationSeconds { get; set; }
    public string Quality { get; set; } = string.Empty;  // "hd", "sd", "4k"
    public string? LocalPath { get; set; }
    public bool IsDownloaded => !string.IsNullOrEmpty(LocalPath);
    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Calculate how well this video's duration matches the target sentence duration.
    /// Score 0-100 where 100 is perfect match.
    /// Heavily penalizes videos shorter than target (can't cover full sentence).
    /// Moderately penalizes videos more than 2x longer than target (boring).
    /// Videos up to 2x target duration are acceptable (50-100 score range).
    /// </summary>
    public int CalculateDurationMatchScore(int? targetDurationSeconds)
    {
        if (!targetDurationSeconds.HasValue || targetDurationSeconds.Value <= 0)
            return 0;

        var target = targetDurationSeconds.Value;
        var actual = DurationSeconds;

        // Perfect match
        if (actual == target)
            return 100;

        // Heavily penalize videos shorter than target (can't cover sentence)
        if (actual < target)
        {
            var deficit = target - actual;
            // Lose 20 points per second missing
            return Math.Max(0, 100 - (deficit * 20));
        }

        // Video is longer than target - calculate based on how much longer
        var excess = actual - target;

        // Up to 3 seconds excess is fine (90-99 score)
        if (excess <= 3)
            return 100 - (excess * 3);

        // Up to 10 seconds excess is acceptable (70-89 score)
        if (excess <= 10)
            return 90 - ((excess - 3) * 3);

        // More than 10 seconds excess - penalize more heavily
        // More than 2x target duration = very poor match
        if (actual > target * 2)
            return Math.Max(0, 50 - ((actual - target * 2) / 2));

        // 10 seconds to 2x target = 50-70 score
        return Math.Max(50, 70 - ((excess - 10) * 2));
    }
}
