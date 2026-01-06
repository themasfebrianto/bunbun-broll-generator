namespace BunBunBroll.Models;

/// <summary>
/// Represents a complete B-Roll generation job with all segments.
/// </summary>
public class ProcessingJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string ProjectName { get; set; } = string.Empty;
    public string RawScript { get; set; } = string.Empty;
    public JobStatus Status { get; set; } = JobStatus.Created;
    public List<ScriptSegment> Segments { get; set; } = new();
    public string OutputDirectory { get; set; } = string.Empty;
    public string? Mood { get; set; }  // Future: "Cinematic", "Moody", "Bright"
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    
    // Progress tracking
    public int TotalSegments => Segments.Count;
    public int CompletedSegments => Segments.Count(s => s.Status == SegmentStatus.Completed);
    public int FailedSegments => Segments.Count(s => s.Status == SegmentStatus.Failed || s.Status == SegmentStatus.NoResults);
    public double ProgressPercentage => TotalSegments == 0 ? 0 : (double)(CompletedSegments + FailedSegments) / TotalSegments * 100;
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
