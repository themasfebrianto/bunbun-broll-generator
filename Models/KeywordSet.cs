namespace BunbunBroll.Models;

/// <summary>
/// Represents a layered set of keywords for B-roll search optimization.
/// Keywords are organized by priority and type for cascading search strategies.
/// </summary>
public class KeywordSet
{
    /// <summary>
    /// Primary keywords - exact visual match with context (highest priority).
    /// Example: "person lying bed staring ceiling", "bedroom ceiling insomnia"
    /// </summary>
    public List<string> Primary { get; set; } = new();

    /// <summary>
    /// Mood/emotion keywords - visual representations of emotional states.
    /// Example: "dark room anxiety", "lonely night window"
    /// </summary>
    public List<string> Mood { get; set; } = new();

    /// <summary>
    /// Contextual keywords - setting and atmospheric descriptors.
    /// Example: "dim bedroom evening", "apartment room shadows"
    /// </summary>
    public List<string> Contextual { get; set; } = new();

    /// <summary>
    /// Action-based keywords - movement and activity descriptors.
    /// Example: "person walking slowly", "hands typing keyboard"
    /// </summary>
    public List<string> Action { get; set; } = new();

    /// <summary>
    /// Fallback keywords - safe, generic visuals that almost always return results.
    /// Example: "clouds timelapse", "city skyline night", "nature landscape"
    /// </summary>
    public List<string> Fallback { get; set; } = new();

    /// <summary>
    /// Suggested platform category for optimized search.
    /// Maps to Pexels/Pixabay categories: People, Nature, Urban, Business, Abstract, etc.
    /// </summary>
    public string? SuggestedCategory { get; set; }

    /// <summary>
    /// Detected dominant emotion/mood of the script segment.
    /// </summary>
    public string? DetectedMood { get; set; }

    /// <summary>
    /// Gets all keywords flattened in priority order (Primary → Mood → Contextual → Action → Fallback).
    /// </summary>
    public IEnumerable<string> GetAllByPriority()
    {
        foreach (var kw in Primary) yield return kw;
        foreach (var kw in Mood) yield return kw;
        foreach (var kw in Contextual) yield return kw;
        foreach (var kw in Action) yield return kw;
        foreach (var kw in Fallback) yield return kw;
    }

    /// <summary>
    /// Gets keywords for a specific search tier (used in cascading search).
    /// </summary>
    public List<string> GetTier(int tier) => tier switch
    {
        1 => Primary.Concat(Mood).Take(4).ToList(),
        2 => Contextual.Concat(Action).Take(4).ToList(),
        3 => Fallback.Take(3).ToList(),
        _ => Fallback.Take(2).ToList()
    };

    /// <summary>
    /// Total count of all keywords across all layers.
    /// </summary>
    public int TotalCount => Primary.Count + Mood.Count + Contextual.Count + Action.Count + Fallback.Count;

    /// <summary>
    /// Creates an empty keyword set.
    /// </summary>
    public static KeywordSet Empty => new();

    /// <summary>
    /// Creates a simple keyword set from a flat list (for backward compatibility).
    /// </summary>
    public static KeywordSet FromFlat(List<string> keywords)
    {
        var set = new KeywordSet();
        
        if (keywords.Count == 0) return set;
        
        // Distribute keywords across tiers based on position
        var count = keywords.Count;
        var primaryCount = Math.Min(2, count);
        var moodCount = Math.Min(2, Math.Max(0, count - primaryCount));
        var contextCount = Math.Min(2, Math.Max(0, count - primaryCount - moodCount));
        
        set.Primary = keywords.Take(primaryCount).ToList();
        set.Mood = keywords.Skip(primaryCount).Take(moodCount).ToList();
        set.Contextual = keywords.Skip(primaryCount + moodCount).Take(contextCount).ToList();
        set.Fallback = keywords.Skip(primaryCount + moodCount + contextCount).ToList();
        
        return set;
    }
}

/// <summary>
/// Response structure for AI keyword extraction with layered output.
/// </summary>
public class KeywordExtractionResponse
{
    public List<string>? PrimaryKeywords { get; set; }
    public List<string>? MoodKeywords { get; set; }
    public List<string>? ContextualKeywords { get; set; }
    public List<string>? ActionKeywords { get; set; }
    public List<string>? FallbackKeywords { get; set; }
    public string? SuggestedCategory { get; set; }
    public string? DetectedMood { get; set; }

    public KeywordSet ToKeywordSet()
    {
        return new KeywordSet
        {
            Primary = PrimaryKeywords ?? new List<string>(),
            Mood = MoodKeywords ?? new List<string>(),
            Contextual = ContextualKeywords ?? new List<string>(),
            Action = ActionKeywords ?? new List<string>(),
            Fallback = FallbackKeywords ?? new List<string>(),
            SuggestedCategory = SuggestedCategory,
            DetectedMood = DetectedMood
        };
    }
}

/// <summary>
/// Batch response for multiple sentence keyword extraction.
/// </summary>
public class BatchKeywordExtractionResponse
{
    public Dictionary<string, KeywordExtractionResponse>? Sentences { get; set; }
}
