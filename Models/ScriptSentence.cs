namespace BunBunBroll.Models;

/// <summary>
/// Represents a single sentence within a segment - the atomic unit for B-Roll matching.
/// Each sentence gets exactly ONE B-Roll clip matched to its content.
/// </summary>
public class ScriptSentence
{
    public int Id { get; set; }
    public int SegmentId { get; set; }
    public string Text { get; set; } = string.Empty;
    
    // Duration estimation (150 words/minute = 0.4 seconds per word)
    public int WordCount => Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
    public double EstimatedDurationSeconds => Math.Max(3, WordCount * 0.4); // Minimum 3 seconds
    
    // AI-extracted keywords for this specific sentence
    public List<string> Keywords { get; set; } = new();
    
    // The matched B-Roll clip
    public VideoAsset? BRollClip { get; set; }
    
    // Processing status
    public SentenceStatus Status { get; set; } = SentenceStatus.Pending;
    public string? ErrorMessage { get; set; }
    
    // Duration coverage
    public double ActualDurationSeconds => BRollClip?.DurationSeconds ?? 0;
    public double DurationCoverage => EstimatedDurationSeconds > 0 
        ? Math.Min(1.0, ActualDurationSeconds / EstimatedDurationSeconds) * 100 
        : 0;
}

public enum SentenceStatus
{
    Pending,
    ExtractingKeywords,
    SearchingBRoll,
    Downloading,
    Completed,
    Failed,
    NoResults
}
