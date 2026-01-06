using BunBunBroll.Models;

namespace BunBunBroll.Services;

/// <summary>
/// Composite Asset Broker - Combines multiple video sources (Pexels + Pixabay)
/// with fallback logic and deduplication.
/// </summary>
public class CompositeAssetBroker : IAssetBroker
{
    private readonly PexelsAssetBroker _pexelsBroker;
    private readonly PixabayAssetBroker _pixabayBroker;
    private readonly ILogger<CompositeAssetBroker> _logger;

    public CompositeAssetBroker(
        PexelsAssetBroker pexelsBroker,
        PixabayAssetBroker pixabayBroker,
        ILogger<CompositeAssetBroker> logger)
    {
        _pexelsBroker = pexelsBroker;
        _pixabayBroker = pixabayBroker;
        _logger = logger;
    }

    public async Task<List<VideoAsset>> SearchVideosAsync(
        IEnumerable<string> keywords,
        int maxResults = 3,
        int? minDuration = null,
        int? maxDuration = null,
        CancellationToken cancellationToken = default)
    {
        var keywordList = keywords.ToList();
        var allAssets = new List<VideoAsset>();

        // Try Pexels first (usually better quality)
        try
        {
            var pexelsResults = await _pexelsBroker.SearchVideosAsync(
                keywordList, 
                maxResults, 
                minDuration, 
                maxDuration, 
                cancellationToken);
            
            allAssets.AddRange(pexelsResults);
            _logger.LogDebug("Pexels returned {Count} results", pexelsResults.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Pexels search failed, trying Pixabay as fallback");
        }

        // If not enough results, try Pixabay
        if (allAssets.Count < maxResults)
        {
            try
            {
                var needed = maxResults - allAssets.Count;
                var pixabayResults = await _pixabayBroker.SearchVideosAsync(
                    keywordList, 
                    needed, 
                    minDuration, 
                    maxDuration, 
                    cancellationToken);
                
                allAssets.AddRange(pixabayResults);
                _logger.LogDebug("Pixabay returned {Count} results", pixabayResults.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Pixabay search also failed");
            }
        }

        // Deduplicate and return
        var results = allAssets
            .GroupBy(a => a.DownloadUrl)  // Dedupe by URL
            .Select(g => g.First())
            .Take(maxResults)
            .ToList();

        _logger.LogInformation("Composite search: {Total} total results (Pexels + Pixabay)", results.Count);
        return results;
    }
}
