namespace BunbunBroll.Models;

/// <summary>
/// Lightweight summary of a completed phase, used as context for subsequent phases.
/// </summary>
public class CompletedPhase
{
    public string PhaseId { get; set; } = string.Empty;
    public string PhaseName { get; set; } = string.Empty;
    public int Order { get; set; }
    public string Content { get; set; } = string.Empty;
    public int WordCount { get; set; }
    public double DurationSeconds { get; set; }
}
