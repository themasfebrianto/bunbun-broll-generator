namespace BunbunBroll.Models;

/// <summary>
/// Unified progress event for background generation.
/// Combines phase-level and session-level progress into one model.
/// </summary>
public class GenerationProgressEvent
{
    public string SessionId { get; set; } = "";
    public GenerationEventType Type { get; set; }
    public string Message { get; set; } = "";

    // Phase info (when Type == Phase)
    public string? PhaseId { get; set; }
    public string? PhaseName { get; set; }
    public int PhaseOrder { get; set; }
    public string? PhaseStatus { get; set; }
    public List<string>? OutlinePoints { get; set; }
    public string? DurationTarget { get; set; }

    // Session-level info
    public int CompletedPhases { get; set; }
    public int TotalPhases { get; set; }
    public double ProgressPercent => TotalPhases > 0 ? (double)CompletedPhases / TotalPhases * 100 : 0;
}

public enum GenerationEventType
{
    PhaseStarted,
    PhaseCompleted,
    PhaseFailed,
    SessionProgress,
    SessionCompleted,
    SessionFailed
}
