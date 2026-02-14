namespace BunbunBroll.Models;

public class BRollSummary
{
    public int VideoCount { get; set; }
    public int ImageGenCount { get; set; }
    public int VideosSelected { get; set; }
    public int ImagesReady { get; set; }
    public int TotalSegments { get; set; }
    public List<string> ThumbnailPaths { get; set; } = new();
}
