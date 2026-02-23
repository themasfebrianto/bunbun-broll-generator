namespace BunbunBroll.Models;

/// <summary>
/// Stores fingerprint of SRT state when B-Roll prompts were generated.
/// Used to detect if SRT has changed, avoiding destructive re-initialization.
/// </summary>
public class BrollSessionMetadata
{
    public int SrtEntryCount { get; set; }
    public double SrtTotalDuration { get; set; }  // seconds
    public string? SrtFilePath { get; set; }
    public DateTime GeneratedAt { get; set; }

    public BrollSessionMetadata()
    {
        GeneratedAt = DateTime.UtcNow;
    }
}
