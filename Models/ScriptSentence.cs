namespace BunbunBroll.Models;

/// <summary>
/// Represents a single sentence within a segment - the atomic unit for B-Roll matching.
/// Each sentence gets exactly ONE B-Roll clip matched to its content.
/// Supports preview-first workflow: search → preview → approve → download.
/// </summary>
public class ScriptSentence
{
    public int Id { get; set; }
    public int SegmentId { get; set; }
    public string Text { get; set; } = string.Empty;
    
    // Duration estimation (150 words/minute = 0.4 seconds per word)
    public int WordCount => Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
    public double EstimatedDurationSeconds => Math.Max(3, WordCount * 0.4); // Minimum 3 seconds
    
    // AI-extracted layered keywords for optimized search
    public KeywordSet KeywordSet { get; set; } = new();
    
    // Flat keyword list (for backward compatibility - reads from KeywordSet)
    public List<string> Keywords 
    { 
        get => KeywordSet.GetAllByPriority().ToList();
        set => KeywordSet = KeywordSet.FromFlat(value);
    }
    
    // Search results from Pexels/Pixabay (preview from CDN)
    public List<VideoAsset> SearchResults { get; set; } = new();
    
    // User-selected video (from SearchResults)
    public VideoAsset? SelectedVideo { get; set; }
    
    // Downloaded/final video (only after user approves)
    public VideoAsset? DownloadedVideo { get; set; }
    
    // Processing status
    public SentenceStatus Status { get; set; } = SentenceStatus.Pending;
    public string? ErrorMessage { get; set; }
    
    // User actions
    public bool IsApproved { get; set; } = false;
    public bool IsSkipped { get; set; } = false;
    
    // Helpers
    public bool HasSearchResults => SearchResults.Count > 0;
    public bool HasSelection => SelectedVideo != null;
    public bool IsDownloaded => DownloadedVideo?.IsDownloaded == true;
    
    // Duration coverage
    public double ActualDurationSeconds => SelectedVideo?.DurationSeconds ?? DownloadedVideo?.DurationSeconds ?? 0;
    public double DurationCoverage => EstimatedDurationSeconds > 0 
        ? Math.Min(1.0, ActualDurationSeconds / EstimatedDurationSeconds) * 100 
        : 0;
}

public enum SentenceStatus
{
    Pending,
    ExtractingKeywords,
    SearchingBRoll,
    PreviewReady,      // NEW: Has search results, waiting for user selection
    Approved,          // NEW: User approved, ready to download
    Downloading,
    Completed,
    Skipped,           // NEW: User skipped this sentence
    Failed,
    NoResults
}
