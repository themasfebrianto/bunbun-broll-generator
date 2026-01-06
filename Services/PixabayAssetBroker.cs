using System.Net.Http.Json;
using BunBunBroll.Models;
using Microsoft.Extensions.Options;

namespace BunBunBroll.Services;

/// <summary>
/// Pixabay Asset Broker - Searches Pixabay API for free stock videos.
/// https://pixabay.com/api/docs/#api_search_videos
/// </summary>
public class PixabayAssetBroker : IAssetBroker
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PixabayAssetBroker> _logger;
    private readonly PixabaySettings _settings;

    private const int DefaultMinDuration = 3;
    private const int DefaultMaxDuration = 60;

    public PixabayAssetBroker(
        HttpClient httpClient,
        ILogger<PixabayAssetBroker> logger,
        IOptions<PixabaySettings> settings)
    {
        _httpClient = httpClient;
        _logger = logger;
        _settings = settings.Value;
    }

    public async Task<List<VideoAsset>> SearchVideosAsync(
        IEnumerable<string> keywords,
        int maxResults = 3,
        int? minDuration = null,
        int? maxDuration = null,
        CancellationToken cancellationToken = default)
    {
        var assets = new List<VideoAsset>();
        var keywordList = keywords.ToList();

        var minDur = minDuration ?? DefaultMinDuration;
        var maxDur = maxDuration ?? DefaultMaxDuration;

        foreach (var keyword in keywordList)
        {
            try
            {
                var videos = await SearchSingleKeywordAsync(keyword, minDur, maxDur, cancellationToken);
                assets.AddRange(videos);

                if (assets.Count >= maxResults)
                    break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Pixabay search failed for: {Keyword}", keyword);
            }
        }

        return assets
            .GroupBy(a => a.Id)
            .Select(g => g.First())
            .Take(maxResults)
            .ToList();
    }

    private async Task<List<VideoAsset>> SearchSingleKeywordAsync(
        string keyword,
        int minDuration,
        int maxDuration,
        CancellationToken cancellationToken)
    {
        var query = Uri.EscapeDataString(keyword);
        var url = $"?key={_settings.ApiKey}&q={query}&video_type=film&per_page=15";

        _logger.LogDebug("Searching Pixabay: {Query}", keyword);

        var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var pixabayResponse = await response.Content.ReadFromJsonAsync<PixabaySearchResponse>(
            cancellationToken: cancellationToken);

        if (pixabayResponse?.Hits == null || pixabayResponse.Hits.Count == 0)
        {
            _logger.LogInformation("No Pixabay results for: {Keyword}", keyword);
            return new List<VideoAsset>();
        }

        var assets = new List<VideoAsset>();

        foreach (var video in pixabayResponse.Hits)
        {
            // Apply duration filter
            if (video.Duration < minDuration || video.Duration > maxDuration)
                continue;

            // Get the best quality video (prefer large, then medium)
            var videoUrl = video.Videos.Large?.Url 
                ?? video.Videos.Medium?.Url 
                ?? video.Videos.Small?.Url;

            if (string.IsNullOrEmpty(videoUrl))
                continue;

            var width = video.Videos.Large?.Width 
                ?? video.Videos.Medium?.Width 
                ?? video.Videos.Small?.Width ?? 0;

            var height = video.Videos.Large?.Height 
                ?? video.Videos.Medium?.Height 
                ?? video.Videos.Small?.Height ?? 0;

            assets.Add(new VideoAsset
            {
                Id = $"pixabay_{video.Id}",
                Provider = "Pixabay",
                Title = $"Pixabay Video {video.Id}",
                ThumbnailUrl = video.PictureId != null 
                    ? $"https://i.vimeocdn.com/video/{video.PictureId}_640x360.jpg"
                    : $"https://pixabay.com/videos/id-{video.Id}/",
                PreviewUrl = video.Videos.Tiny?.Url ?? videoUrl,
                DownloadUrl = videoUrl,
                Width = width,
                Height = height,
                DurationSeconds = video.Duration,
                Quality = video.Videos.Large != null ? "hd" : "sd"
            });
        }

        _logger.LogInformation("Pixabay found {Count} videos for: {Keyword}", assets.Count, keyword);
        return assets;
    }
}

public class PixabaySettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://pixabay.com/api/videos/";
}

// Pixabay API Response Models
public class PixabaySearchResponse
{
    public int Total { get; set; }
    public int TotalHits { get; set; }
    public List<PixabayVideo> Hits { get; set; } = new();
}

public class PixabayVideo
{
    public int Id { get; set; }
    public string PageURL { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Tags { get; set; } = string.Empty;
    public int Duration { get; set; }
    public string? PictureId { get; set; }
    public PixabayVideoFiles Videos { get; set; } = new();
    public int Views { get; set; }
    public int Downloads { get; set; }
    public int Likes { get; set; }
    public int Comments { get; set; }
    public int UserId { get; set; }
    public string User { get; set; } = string.Empty;
    public string UserImageURL { get; set; } = string.Empty;
}

public class PixabayVideoFiles
{
    public PixabayVideoFile? Large { get; set; }
    public PixabayVideoFile? Medium { get; set; }
    public PixabayVideoFile? Small { get; set; }
    public PixabayVideoFile? Tiny { get; set; }
}

public class PixabayVideoFile
{
    public string Url { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public int Size { get; set; }
}
