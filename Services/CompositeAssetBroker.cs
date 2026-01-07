using BunbunBroll.Models;

namespace BunbunBroll.Services;

/// <summary>
/// Enhanced Asset Broker interface that supports layered keyword sets.
/// </summary>
public interface IAssetBrokerV2 : IAssetBroker
{
    /// <summary>
    /// Search videos using a layered KeywordSet for optimized cascading search.
    /// </summary>
    Task<List<VideoAsset>> SearchVideosAsync(
        KeywordSet keywordSet,
        int maxResults = 3,
        int? minDuration = null,
        int? maxDuration = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Composite Asset Broker - Combines multiple video sources (Pexels + Pixabay)
/// with smart tiered fallback logic, keyword layering, and Halal filter support.
/// </summary>
public class CompositeAssetBroker : IAssetBrokerV2
{
    private readonly PexelsAssetBroker _pexelsBroker;
    private readonly PixabayAssetBroker _pixabayBroker;
    private readonly IHalalVideoFilter _halalFilter;
    private readonly ILogger<CompositeAssetBroker> _logger;

    // Universal fallback keywords that almost always return results
    private static readonly string[] UniversalFallbacks = new[]
    {
        "city skyline night",
        "nature landscape",
        "clouds timelapse", 
        "ocean waves sunset",
        "sunset horizon",
        "person silhouette window",
        "abstract light bokeh",
        "rain window night",
        "sunrise timelapse",
        "forest path morning"
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

    /// <summary>
    /// Search using layered KeywordSet with tier-based cascading.
    /// </summary>
    public async Task<List<VideoAsset>> SearchVideosAsync(
        KeywordSet keywordSet,
        int maxResults = 3,
        int? minDuration = null,
        int? maxDuration = null,
        CancellationToken cancellationToken = default)
    {
        var allAssets = new List<VideoAsset>();
        
        // TIER 1: Primary + Mood keywords (highest relevance)
        var tier1Keywords = ApplyFilters(keywordSet.GetTier(1));
        if (tier1Keywords.Count > 0)
        {
            _logger.LogDebug("Tier 1 (Primary+Mood): {Keywords}", string.Join(", ", tier1Keywords));
            allAssets.AddRange(await SearchBothSourcesAsync(tier1Keywords, maxResults, minDuration, maxDuration, cancellationToken));
            
            if (allAssets.Count >= maxResults)
            {
                _logger.LogInformation("Found {Count} results in Tier 1", allAssets.Count);
                return DeduplicateAndLimit(allAssets, maxResults);
            }
        }

        // TIER 2: Contextual + Action keywords
        var tier2Keywords = ApplyFilters(keywordSet.GetTier(2));
        if (tier2Keywords.Count > 0 && allAssets.Count < maxResults)
        {
            _logger.LogDebug("Tier 2 (Contextual+Action): {Keywords}", string.Join(", ", tier2Keywords));
            var needed = maxResults - allAssets.Count;
            allAssets.AddRange(await SearchBothSourcesAsync(tier2Keywords, needed, minDuration, maxDuration, cancellationToken));
            
            if (allAssets.Count >= maxResults)
            {
                _logger.LogInformation("Found {Count} results in Tier 2", allAssets.Count);
                return DeduplicateAndLimit(allAssets, maxResults);
            }
        }

        // TIER 3: Fallback keywords from KeywordSet
        var tier3Keywords = ApplyFilters(keywordSet.GetTier(3));
        if (tier3Keywords.Count > 0 && allAssets.Count < maxResults)
        {
            _logger.LogDebug("Tier 3 (Fallback): {Keywords}", string.Join(", ", tier3Keywords));
            var needed = maxResults - allAssets.Count;
            allAssets.AddRange(await SearchBothSourcesAsync(tier3Keywords, needed, minDuration, maxDuration, cancellationToken));
            
            if (allAssets.Count >= maxResults)
            {
                _logger.LogInformation("Found {Count} results in Tier 3", allAssets.Count);
                return DeduplicateAndLimit(allAssets, maxResults);
            }
        }

        // TIER 4: Universal fallback (guaranteed results)
        if (allAssets.Count < maxResults)
        {
            _logger.LogDebug("Tier 4 (Universal Fallback)");
            var fallbackKeywords = GetRandomFallbacks(3);
            var stillNeeded = maxResults - allAssets.Count;
            allAssets.AddRange(await SearchBothSourcesAsync(fallbackKeywords, stillNeeded, minDuration, maxDuration, cancellationToken));
        }

        var results = DeduplicateAndLimit(allAssets, maxResults);
        _logger.LogInformation("Tiered search complete: {Count} results", results.Count);
        
        return results;
    }

    /// <summary>
    /// Legacy search interface for backward compatibility.
    /// </summary>
    public async Task<List<VideoAsset>> SearchVideosAsync(
        IEnumerable<string> keywords,
        int maxResults = 3,
        int? minDuration = null,
        int? maxDuration = null,
        CancellationToken cancellationToken = default)
    {
        var keywordList = keywords.ToList();
        
        // Apply Halal filter if enabled
        keywordList = ApplyFilters(keywordList);
        
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

    /// <summary>
    /// Apply Halal filter and safe modifiers to keywords.
    /// </summary>
    private List<string> ApplyFilters(List<string> keywords)
    {
        if (!_halalFilter.IsEnabled)
            return keywords;
            
        var filtered = _halalFilter.FilterKeywords(keywords);
        filtered = _halalFilter.AddSafeModifiers(filtered);
        _logger.LogDebug("Halal filter applied. Keywords: {Keywords}", string.Join(", ", filtered));
        return filtered;
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

