namespace BunBunBroll.Models;

/// <summary>
/// Represents a segment (scene/paragraph) of the video script.
/// Each segment contains multiple sentences, each with its own B-Roll.
/// </summary>
public class ScriptSegment
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;  // e.g., "Scene 1: Heavy Morning"
    public string OriginalText { get; set; } = string.Empty;
    
    // Sentences within this segment - each gets its own B-Roll
    public List<ScriptSentence> Sentences { get; set; } = new();
    
    // Aggregate stats
    public int WordCount => Sentences.Sum(s => s.WordCount);
    public double TotalEstimatedDuration => Sentences.Sum(s => s.EstimatedDurationSeconds);
    public double TotalActualDuration => Sentences.Sum(s => s.ActualDurationSeconds);
    public double DurationCoverage => TotalEstimatedDuration > 0 
        ? Math.Min(100, (TotalActualDuration / TotalEstimatedDuration) * 100) 
        : 0;
    
    // Processing status
    public SegmentStatus Status { get; set; } = SegmentStatus.Pending;
    public int CompletedSentences => Sentences.Count(s => s.Status == SentenceStatus.Completed);
    public int FailedSentences => Sentences.Count(s => s.Status == SentenceStatus.Failed || s.Status == SentenceStatus.NoResults);
    public double ProgressPercentage => Sentences.Count == 0 ? 0 
        : (double)(CompletedSentences + FailedSentences) / Sentences.Count * 100;
    
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
}

public enum SegmentStatus
{
    Pending,
    Processing,
    Completed,
    PartiallyCompleted,
    Failed
}
