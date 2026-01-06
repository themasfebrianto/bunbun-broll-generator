using System.Net.Http.Json;
using BunBunBroll.Models;
using Microsoft.Extensions.Options;

namespace BunBunBroll.Services;

/// <summary>
/// Asset Broker - Searches Pexels API for stock videos with quality filtering.
/// </summary>
public interface IAssetBroker
{
    Task<List<VideoAsset>> SearchVideosAsync(
        IEnumerable<string> keywords, 
        int maxResults = 3,
        CancellationToken cancellationToken = default);
}

public class PexelsAssetBroker : IAssetBroker
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PexelsAssetBroker> _logger;
    private readonly PexelsSettings _settings;

    // Filter constants from the context.md spec
    private const int MinDuration = 5;
    private const int MaxDuration = 30;
    private static readonly string[] PreferredQualities = { "hd", "sd" }; // Avoid 4K (too large)
    private const int MinWidth = 1280;  // HD-Ready minimum

    public PexelsAssetBroker(
        HttpClient httpClient, 
        ILogger<PexelsAssetBroker> logger,
        IOptions<PexelsSettings> settings)
    {
        _httpClient = httpClient;
        _logger = logger;
        _settings = settings.Value;
    }

    public async Task<List<VideoAsset>> SearchVideosAsync(
        IEnumerable<string> keywords, 
        int maxResults = 3,
        CancellationToken cancellationToken = default)
    {
        var assets = new List<VideoAsset>();
        var keywordList = keywords.ToList();

        foreach (var keyword in keywordList)
        {
            try
            {
                var videos = await SearchSingleKeywordAsync(keyword, cancellationToken);
                assets.AddRange(videos);

                // Stop if we have enough
                if (assets.Count >= maxResults)
                    break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to search for keyword: {Keyword}", keyword);
            }
        }

        // Deduplicate and limit
        return assets
            .GroupBy(a => a.Id)
            .Select(g => g.First())
            .Take(maxResults)
            .ToList();
    }

    private async Task<List<VideoAsset>> SearchSingleKeywordAsync(
        string keyword, 
        CancellationToken cancellationToken)
    {
        var query = Uri.EscapeDataString(keyword);
        var url = $"videos/search?query={query}&orientation=landscape&size=medium&per_page=10";

        _logger.LogDebug("Searching Pexels: {Query}", keyword);

        var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var pexelsResponse = await response.Content.ReadFromJsonAsync<PexelsSearchResponse>(cancellationToken: cancellationToken);
        
        if (pexelsResponse?.Videos == null || pexelsResponse.Videos.Count == 0)
        {
            _logger.LogInformation("No results for keyword: {Keyword}", keyword);
            return new List<VideoAsset>();
        }

        var assets = new List<VideoAsset>();

        foreach (var video in pexelsResponse.Videos)
        {
            // Apply duration filter
            if (video.Duration < MinDuration || video.Duration > MaxDuration)
                continue;

            // Find the best video file (prefer HD, avoid 4K)
            var bestFile = video.VideoFiles
                .Where(f => f.FileType == "video/mp4")
                .Where(f => PreferredQualities.Contains(f.Quality.ToLower()))
                .Where(f => f.Width >= MinWidth)
                .OrderByDescending(f => f.Width)
                .ThenBy(f => f.Quality == "hd" ? 0 : 1) // Prefer HD over SD
                .FirstOrDefault();

            if (bestFile == null)
                continue;

            assets.Add(new VideoAsset
            {
                Id = video.Id.ToString(),
                Provider = "Pexels",
                Title = $"Pexels Video {video.Id}",
                ThumbnailUrl = video.ThumbnailUrl,
                PreviewUrl = video.VideoPictures.FirstOrDefault()?.Picture ?? video.ThumbnailUrl,
                DownloadUrl = bestFile.Link,
                Width = bestFile.Width,
                Height = bestFile.Height,
                DurationSeconds = video.Duration,
                Quality = bestFile.Quality
            });
        }

        _logger.LogInformation("Found {Count} suitable videos for: {Keyword}", assets.Count, keyword);
        return assets;
    }
}

public class PexelsSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.pexels.com/";
}
