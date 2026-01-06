namespace BunBunBroll.Models;

/// <summary>
/// Represents the result from the Intelligence Layer (Gemini).
/// </summary>
public class KeywordResult
{
    public bool Success { get; set; }
    public List<string> Keywords { get; set; } = new();
    public string? RawResponse { get; set; }
    public string? Error { get; set; }
    public int TokensUsed { get; set; }
    public TimeSpan ProcessingTime { get; set; }
}
