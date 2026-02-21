namespace BunbunBroll.Models;

public class SrtEntry 
{
    public int Index { get; set; }
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public TimeSpan OriginalStartTime { get; set; }
    public TimeSpan OriginalEndTime { get; set; }
    public TimeSpan PaddingStart { get; set; }
    public TimeSpan PaddingEnd { get; set; }
    public string Text { get; set; } = string.Empty;
    public TimeSpan Duration => EndTime - StartTime;
}
