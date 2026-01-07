namespace BunbunBroll.Models;

/// <summary>
/// Represents the result from the Intelligence Layer (Gemini).
/// Enhanced with layered keyword support for optimized B-roll search.
/// </summary>
public class KeywordResult
{
    public bool Success { get; set; }
    
    /// <summary>
    /// Layered keyword set for cascading search optimization.
    /// </summary>
    public KeywordSet KeywordSet { get; set; } = new();
    
    /// <summary>
    /// Flat list of all keywords (for backward compatibility).
    /// Returns all keywords from KeywordSet in priority order.
    /// </summary>
    public List<string> Keywords 
    { 
        get => KeywordSet.GetAllByPriority().ToList();
        set => KeywordSet = KeywordSet.FromFlat(value);
    }
    
    public string? RawResponse { get; set; }
    public string? Error { get; set; }
    public int TokensUsed { get; set; }
    public TimeSpan ProcessingTime { get; set; }
    
    /// <summary>
    /// Suggested category for platform-optimized search.
    /// </summary>
    public string? SuggestedCategory => KeywordSet.SuggestedCategory;
    
    /// <summary>
    /// Detected mood/emotion of the script segment.
    /// </summary>
    public string? DetectedMood => KeywordSet.DetectedMood;
}
