namespace BunBunBroll.Models;

/// <summary>
/// Represents a video asset from an external provider (e.g., Pexels).
/// </summary>
public class VideoAsset
{
    public string Id { get; set; } = string.Empty;
    public string Provider { get; set; } = "Pexels";
    public string Title { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;
    public string PreviewUrl { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public int DurationSeconds { get; set; }
    public string Quality { get; set; } = string.Empty;  // "hd", "sd", "4k"
    public string? LocalPath { get; set; }
    public bool IsDownloaded => !string.IsNullOrEmpty(LocalPath);
    public DateTime FetchedAt { get; set; } = DateTime.UtcNow;
}
