namespace BunbunBroll.Models;

/// <summary>
/// Result of generating a single phase.
/// </summary>
public class GeneratedPhase
{
    public string PhaseId { get; set; } = string.Empty;
    public string PhaseName { get; set; } = string.Empty;
    public int Order { get; set; }
    public string Content { get; set; } = string.Empty;
    public int WordCount { get; set; }
    public double DurationSeconds { get; set; }
    public bool IsValidated { get; set; }
    public List<string> Warnings { get; set; } = new();
}
