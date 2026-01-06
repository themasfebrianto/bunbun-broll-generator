using BunbunBroll.Models;

namespace BunbunBroll.Services;

/// <summary>
/// Composite Asset Broker - Combines multiple video sources (Pexels + Pixabay)
/// with smart fallback logic, universal fallback keywords, and Halal filter support.
/// </summary>
public class CompositeAssetBroker : IAssetBroker
{
    private readonly PexelsAssetBroker _pexelsBroker;
    private readonly PixabayAssetBroker _pixabayBroker;
    private readonly IHalalVideoFilter _halalFilter;
    private readonly ILogger<CompositeAssetBroker> _logger;

    // Universal fallback keywords that almost always return results
    private static readonly string[] UniversalFallbacks = new[]
    {
        "city skyline",
        "nature landscape",
        "clouds timelapse", 
        "ocean waves",
        "sunset",
        "person silhouette",
        "abstract light",
        "rain window",
        "sunrise",
        "forest"
    };

    public CompositeAssetBroker(
        PexelsAssetBroker pexelsBroker,
        PixabayAssetBroker pixabayBroker,
        IHalalVideoFilter halalFilter,
        ILogger<CompositeAssetBroker> logger)
    {
        _pexelsBroker = pexelsBroker;
        _pixabayBroker = pixabayBroker;
        _halalFilter = halalFilter;
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
        
        // Apply Halal filter if enabled
        if (_halalFilter.IsEnabled)
        {
            keywordList = _halalFilter.FilterKeywords(keywordList);
            keywordList = _halalFilter.AddSafeModifiers(keywordList);
            _logger.LogDebug("Halal filter applied. Keywords: {Keywords}", string.Join(", ", keywordList));
        }
        
        var allAssets = new List<VideoAsset>();

        // TIER 1: Try original keywords on both sources
        _logger.LogDebug("Tier 1: Searching with keywords: {Keywords}", string.Join(", ", keywordList));
        
        allAssets.AddRange(await SearchBothSourcesAsync(keywordList, maxResults, minDuration, maxDuration, cancellationToken));

        if (allAssets.Count >= maxResults)
            return DeduplicateAndLimit(allAssets, maxResults);

        // TIER 2: Try simplified keywords (single words)
        var simplifiedKeywords = keywordList
            .SelectMany(k => k.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(w => w.Length > 3)
            .Distinct()
            .Take(5)
            .ToList();

        if (simplifiedKeywords.Count > 0 && allAssets.Count < maxResults)
        {
            _logger.LogDebug("Tier 2: Trying simplified keywords: {Keywords}", string.Join(", ", simplifiedKeywords));
            
            var needed = maxResults - allAssets.Count;
            allAssets.AddRange(await SearchBothSourcesAsync(simplifiedKeywords, needed, minDuration, maxDuration, cancellationToken));
        }

        if (allAssets.Count >= maxResults)
            return DeduplicateAndLimit(allAssets, maxResults);

        // TIER 3: Try universal fallback keywords
        _logger.LogDebug("Tier 3: Using universal fallback keywords");
        
        var fallbackKeywords = GetRandomFallbacks(3);
        var stillNeeded = maxResults - allAssets.Count;
        allAssets.AddRange(await SearchBothSourcesAsync(fallbackKeywords, stillNeeded, minDuration, maxDuration, cancellationToken));

        var results = DeduplicateAndLimit(allAssets, maxResults);
        _logger.LogInformation("Composite search complete: {Count} results", results.Count);
        
        return results;
    }

    private async Task<List<VideoAsset>> SearchBothSourcesAsync(
        List<string> keywords,
        int maxResults,
        int? minDuration,
        int? maxDuration,
        CancellationToken cancellationToken)
    {
        var assets = new List<VideoAsset>();

        // Search both sources in PARALLEL for speed
        var pexelsTask = SafeSearchAsync(
            () => _pexelsBroker.SearchVideosAsync(keywords, maxResults, minDuration, maxDuration, cancellationToken),
            "Pexels", keywords);
        
        var pixabayTask = SafeSearchAsync(
            () => _pixabayBroker.SearchVideosAsync(keywords, maxResults, minDuration, maxDuration, cancellationToken),
            "Pixabay", keywords);

        // Wait for both to complete
        await Task.WhenAll(pexelsTask, pixabayTask);

        // Combine results
        assets.AddRange(await pexelsTask);
        assets.AddRange(await pixabayTask);

        return assets;
    }

    private async Task<List<VideoAsset>> SafeSearchAsync(
        Func<Task<List<VideoAsset>>> searchFunc,
        string providerName,
        List<string> keywords)
    {
        try
        {
            return await searchFunc();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "{Provider} search failed for: {Keywords}", providerName, string.Join(", ", keywords));
            return new List<VideoAsset>();
        }
    }

    private static List<string> GetRandomFallbacks(int count)
    {
        return UniversalFallbacks
            .OrderBy(_ => Random.Shared.Next())
            .Take(count)
            .ToList();
    }

    private static List<VideoAsset> DeduplicateAndLimit(List<VideoAsset> assets, int maxResults)
    {
        return assets
            .GroupBy(a => a.DownloadUrl)
            .Select(g => g.First())
            .Take(maxResults)
            .ToList();
    }
}
