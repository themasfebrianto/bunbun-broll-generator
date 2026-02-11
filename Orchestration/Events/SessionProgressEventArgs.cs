namespace BunbunBroll.Orchestration.Events;

public class SessionProgressEventArgs : EventArgs
{
    public string SessionId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int CompletedPhases { get; set; }
    public int TotalPhases { get; set; }
    public double ProgressPercent => TotalPhases > 0 ? (double)CompletedPhases / TotalPhases * 100 : 0;
    public string? Message { get; set; }
}
