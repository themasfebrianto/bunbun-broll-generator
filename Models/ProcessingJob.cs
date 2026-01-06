namespace BunBunBroll.Models;

/// <summary>
/// Represents a complete B-Roll generation job with all segments and sentences.
/// </summary>
public class ProcessingJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string ProjectName { get; set; } = string.Empty;
    public string RawScript { get; set; } = string.Empty;
    public JobStatus Status { get; set; } = JobStatus.Created;
    public List<ScriptSegment> Segments { get; set; } = new();
    public string OutputDirectory { get; set; } = string.Empty;
    public string? Mood { get; set; }  // "Cinematic", "Moody", "Bright"
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    
    // Aggregate stats across all segments and sentences
    public int TotalSegments => Segments.Count;
    public int TotalSentences => Segments.Sum(s => s.Sentences.Count);
    public int CompletedSentences => Segments.Sum(s => s.CompletedSentences);
    public int FailedSentences => Segments.Sum(s => s.FailedSentences);
    
    // Duration stats
    public int TotalWordCount => Segments.Sum(s => s.WordCount);
    public double TotalEstimatedDuration => Segments.Sum(s => s.TotalEstimatedDuration);
    public double TotalActualDuration => Segments.Sum(s => s.TotalActualDuration);
    public double DurationCoverage => TotalEstimatedDuration > 0 
        ? Math.Min(100, (TotalActualDuration / TotalEstimatedDuration) * 100) 
        : 0;
    
    // Progress tracking
    public double ProgressPercentage => TotalSentences == 0 ? 0 
        : (double)(CompletedSentences + FailedSentences) / TotalSentences * 100;
    
    // Segment-level progress
    public int CompletedSegments => Segments.Count(s => s.Status == SegmentStatus.Completed || s.Status == SegmentStatus.PartiallyCompleted);
    
    // Format duration as MM:SS
    public string EstimatedDurationFormatted => FormatDuration(TotalEstimatedDuration);
    public string ActualDurationFormatted => FormatDuration(TotalActualDuration);
    
    private static string FormatDuration(double seconds)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        return $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
    }
}

public enum JobStatus
{
    Created,
    Segmenting,
    Processing,
    Completed,
    PartiallyCompleted,
    Failed
}
