using BunbunBroll.Services;

namespace BunbunBroll.Models;

/// <summary>
/// Result of LLM-based drama detection for pauses and overlays
/// </summary>
public class DramaDetectionResult
{
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }

    // Results when successful
    /// <summary>
    /// Entry index → pause duration in seconds
    /// Pause is added AFTER this entry (index i means pause between entry i and i+1)
    /// </summary>
    public Dictionary<int, double> PauseDurations { get; set; } = new();

    /// <summary>
    /// Entry index → text overlay to display during this entry
    /// </summary>
    public Dictionary<int, TextOverlayDto> TextOverlays { get; set; } = new();

    // For debugging/transparency
    public int TokensUsed { get; set; }
    public double ProcessingTimeMs { get; set; }
}
