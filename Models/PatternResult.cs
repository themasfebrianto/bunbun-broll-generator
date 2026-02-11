namespace BunbunBroll.Models;

/// <summary>
/// Result of executing a full pattern generation.
/// </summary>
public class PatternResult
{
    public string SessionId { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
    public List<GeneratedPhase> GeneratedPhases { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public int TotalWordCount { get; set; }
    public double TotalDurationSeconds { get; set; }
}
