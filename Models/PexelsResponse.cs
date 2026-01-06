using System.Text.Json.Serialization;

namespace BunBunBroll.Models;

/// <summary>
/// Pexels API response models.
/// </summary>
public class PexelsSearchResponse
{
    [JsonPropertyName("page")]
    public int Page { get; set; }
    
    [JsonPropertyName("per_page")]
    public int PerPage { get; set; }
    
    [JsonPropertyName("total_results")]
    public int TotalResults { get; set; }
    
    [JsonPropertyName("videos")]
    public List<PexelsVideo> Videos { get; set; } = new();
}

public class PexelsVideo
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    
    [JsonPropertyName("width")]
    public int Width { get; set; }
    
    [JsonPropertyName("height")]
    public int Height { get; set; }
    
    [JsonPropertyName("duration")]
    public int Duration { get; set; }
    
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
    
    [JsonPropertyName("image")]
    public string ThumbnailUrl { get; set; } = string.Empty;
    
    [JsonPropertyName("video_files")]
    public List<PexelsVideoFile> VideoFiles { get; set; } = new();
    
    [JsonPropertyName("video_pictures")]
    public List<PexelsVideoPicture> VideoPictures { get; set; } = new();
}

public class PexelsVideoFile
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    
    [JsonPropertyName("quality")]
    public string Quality { get; set; } = string.Empty;
    
    [JsonPropertyName("file_type")]
    public string FileType { get; set; } = string.Empty;
    
    [JsonPropertyName("width")]
    public int Width { get; set; }
    
    [JsonPropertyName("height")]
    public int Height { get; set; }
    
    [JsonPropertyName("link")]
    public string Link { get; set; } = string.Empty;
}

public class PexelsVideoPicture
{
    [JsonPropertyName("id")]
    public int Id { get; set; }
    
    [JsonPropertyName("picture")]
    public string Picture { get; set; } = string.Empty;
}
