namespace BunBunBroll.Models;

/// <summary>
/// Represents a single segment of the video script.
/// </summary>
public class ScriptSegment
{
    public int Id { get; set; }
    public string OriginalText { get; set; } = string.Empty;
    public int WordCount => OriginalText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
    public SegmentStatus Status { get; set; } = SegmentStatus.Pending;
    public List<string> Keywords { get; set; } = new();
    public VideoAsset? SelectedAsset { get; set; }
    public List<VideoAsset> AlternativeAssets { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
}

public enum SegmentStatus
{
    Pending,
    ExtractingKeywords,
    SearchingAssets,
    Downloading,
    Completed,
    Failed,
    NoResults
}
