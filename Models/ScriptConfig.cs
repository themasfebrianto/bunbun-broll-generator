namespace BunbunBroll.Models;

/// <summary>
/// User input configuration for script generation.
/// </summary>
public class ScriptConfig
{
    public string Topic { get; set; } = string.Empty;
    public string? Outline { get; set; }
    public int TargetDurationMinutes { get; set; } = 30;
    public string? SourceReferences { get; set; }
    public string ChannelName { get; set; } = string.Empty;
    public List<string>? MustHaveBeats { get; set; }
}
