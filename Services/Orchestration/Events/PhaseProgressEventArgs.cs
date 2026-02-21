namespace BunbunBroll.Services.Orchestration.Events;

public class PhaseProgressEventArgs : EventArgs
{
    public string SessionId { get; set; } = string.Empty;
    public string PhaseId { get; set; } = string.Empty;
    public string PhaseName { get; set; } = string.Empty;
    public int PhaseOrder { get; set; }
    public int TotalPhases { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Message { get; set; }
    public List<string>? OutlinePoints { get; set; }
    public string? DurationTarget { get; set; }
}
