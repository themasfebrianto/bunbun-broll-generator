namespace BunbunBroll.Models;

/// <summary>
/// Represents a complete B-Roll generation job with all segments and sentences.
/// Supports preview-first workflow.
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
    
    // Preview workflow stats
    public int PreviewReadySentences => Segments.Sum(s => s.Sentences.Count(sent => sent.Status == SentenceStatus.PreviewReady));
    public int ApprovedSentences => Segments.Sum(s => s.Sentences.Count(sent => sent.IsApproved));
    public int DownloadedSentences => Segments.Sum(s => s.Sentences.Count(sent => sent.IsDownloaded));
    public int SkippedSentences => Segments.Sum(s => s.Sentences.Count(sent => sent.IsSkipped));
    public int FailedSentences => Segments.Sum(s => s.Sentences.Count(sent => sent.Status == SentenceStatus.Failed || sent.Status == SentenceStatus.NoResults));
    
    // Legacy compatibility
    public int CompletedSentences => DownloadedSentences;
    
    // Duration stats
    public int TotalWordCount => Segments.Sum(s => s.WordCount);
    public double TotalEstimatedDuration => Segments.Sum(s => s.TotalEstimatedDuration);
    public double TotalActualDuration => Segments.Sum(s => s.TotalActualDuration);
    public double DurationCoverage => TotalEstimatedDuration > 0 
        ? Math.Min(100, (TotalActualDuration / TotalEstimatedDuration) * 100) 
        : 0;
    
    // Progress tracking
    public double SearchProgress => TotalSentences == 0 ? 0 
        : (double)(PreviewReadySentences + FailedSentences) / TotalSentences * 100;
    
    public double DownloadProgress => ApprovedSentences == 0 ? 0 
        : (double)DownloadedSentences / ApprovedSentences * 100;
    
    // Legacy - overall progress
    public double ProgressPercentage => SearchProgress;
    
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
    PreviewReady,    // NEW: All previews loaded, waiting for user review
    Downloading,     // NEW: Downloading approved videos
    Completed,
    PartiallyCompleted,
    Failed
}
