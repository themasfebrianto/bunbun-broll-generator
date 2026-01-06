namespace BunBunBroll.Services;

/// <summary>
/// Halal Video Filter - Strict mode that:
/// 1. Avoids videos showing women without hijab
/// 2. Prefers nature/landscape/abstract content over people
/// 3. Uses silhouettes and back views instead of faces
/// 4. Adds "muslim hijab" to female keywords
/// Toggle-able feature for Islamic content creators.
/// </summary>
public interface IHalalVideoFilter
{
    bool IsEnabled { get; set; }
    List<string> FilterKeywords(List<string> keywords);
    List<string> AddSafeModifiers(List<string> keywords);
}

public class HalalVideoFilter : IHalalVideoFilter
{
    private readonly ILogger<HalalVideoFilter> _logger;
    
    public bool IsEnabled { get; set; } = false;

    // Keywords to completely BLOCK
    private static readonly HashSet<string> BlockedKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Beach/swimwear
        "bikini", "swimsuit", "swimwear", "beach party", "pool party",
        "swimming pool", "bathing suit", "beach body", "sunbathing",
        
        // Nightlife/party
        "nightclub", "club party", "bar party", "disco", "rave",
        "drinking party", "alcohol", "beer", "wine", "cocktail",
        "pub", "bartender",
        
        // Revealing/sensual
        "sexy", "sensual", "seductive", "revealing", "lingerie",
        "underwear", "bra", "cleavage", "low cut", "mini skirt",
        "short dress", "tight dress", "bodycon", "crop top",
        "tank top", "sleeveless", "strapless", "backless",
        "shorts", "hot pants",
        
        // Dance with revealing content
        "pole dance", "strip", "twerk", "belly dance", "latin dance",
        
        // Romance/intimate
        "kissing", "romantic kiss", "couple bed", "intimate",
        "love scene", "passion", "making out", "embrace romantic",
        
        // Avoid non-modest female depictions
        "model female", "fashion model", "beauty model",
        "woman hair flowing", "woman hair wind", "brunette", "blonde woman",
        "redhead woman", "long hair woman", "curly hair woman"
    };

    // Female-related keywords to REPLACE with safer alternatives
    private static readonly Dictionary<string, string> FemaleReplacements = new(StringComparer.OrdinalIgnoreCase)
    {
        // Replace with nature/abstract
        ["woman"] = "person silhouette",
        ["women"] = "people walking",
        ["girl"] = "young person",
        ["lady"] = "person",
        ["female"] = "person",
        ["wife"] = "family",
        ["mother"] = "family hands",
        ["mom"] = "family",
        
        // Replace beauty with modest alternatives
        ["woman face"] = "person silhouette window",
        ["woman portrait"] = "hands praying",
        ["woman smile"] = "peaceful morning",
        ["woman looking"] = "person looking horizon",
        ["woman walking"] = "person walking city",
        ["woman sitting"] = "person silhouette sitting",
        ["woman standing"] = "person standing nature",
        
        // Replace specific scenarios
        ["woman morning"] = "sunrise bedroom peaceful",
        ["woman night"] = "night city lights",
        ["woman alone"] = "solitude nature",
        ["woman thinking"] = "contemplation silhouette",
        ["woman sad"] = "rain window mood",
        ["woman happy"] = "nature sunshine",
        ["woman tired"] = "morning coffee cup",
        ["woman stressed"] = "clock ticking papers",
        ["woman working"] = "laptop hands typing",
        ["woman reading"] = "book pages hands",
        ["woman cooking"] = "kitchen hands cooking",
        ["woman praying"] = "muslim woman praying hijab"
    };

    // Preferred SAFE categories (no people or modest only)
    private static readonly string[] SafeCategories = new[]
    {
        // Nature (safest)
        "nature landscape", "clouds timelapse", "ocean waves", "forest trees",
        "mountains sunrise", "rain drops", "sunset sky", "fog morning",
        "flowers garden", "leaves falling", "river flowing", "snow falling",
        
        // Urban (no people focus)
        "city skyline", "aerial city night", "street lights", "empty street",
        "building architecture modern", "traffic lights", "train passing",
        
        // Objects/abstract
        "coffee steam cup", "book pages", "clock ticking", "candle flame",
        "water ripples", "light bokeh", "smoke motion", "writing pen paper",
        
        // Modest human content
        "hands praying", "hands typing keyboard", "silhouette person window",
        "person back view walking", "feet walking", "shadow person"
    };

    public HalalVideoFilter(ILogger<HalalVideoFilter> logger)
    {
        _logger = logger;
    }

    public List<string> FilterKeywords(List<string> keywords)
    {
        if (!IsEnabled)
            return keywords;

        var filtered = new List<string>();

        foreach (var keyword in keywords)
        {
            var lowerKeyword = keyword.ToLowerInvariant();
            
            // Check if keyword contains any blocked words
            var isBlocked = BlockedKeywords.Any(blocked => 
                lowerKeyword.Contains(blocked) || blocked.Contains(lowerKeyword));

            if (isBlocked)
            {
                _logger.LogDebug("Halal filter: Blocked '{Keyword}'", keyword);
                continue;
            }

            // Check if keyword needs replacement
            var replaced = TryReplaceFemaleKeyword(keyword);
            if (replaced != keyword)
            {
                _logger.LogDebug("Halal filter: Replaced '{Original}' -> '{Replaced}'", keyword, replaced);
                filtered.Add(replaced);
            }
            else
            {
                filtered.Add(keyword);
            }
        }

        // If too many keywords were filtered, add safe alternatives
        if (filtered.Count < 3)
        {
            _logger.LogDebug("Halal filter: Adding safe fallback keywords");
            var safeToAdd = SafeCategories
                .OrderBy(_ => Random.Shared.Next())
                .Take(4 - filtered.Count);
            filtered.AddRange(safeToAdd);
        }

        _logger.LogInformation("Halal filter: {Original} keywords -> {Filtered} filtered", 
            keywords.Count, filtered.Count);

        return filtered.Distinct().ToList();
    }

    public List<string> AddSafeModifiers(List<string> keywords)
    {
        if (!IsEnabled)
            return keywords;

        var enhanced = new List<string>();

        foreach (var keyword in keywords)
        {
            var lower = keyword.ToLowerInvariant();
            
            // If keyword still contains female terms, try to make it safer
            if (ContainsFemaleTerms(lower))
            {
                // Add muslim/hijab modifier or replace with silhouette
                if (lower.Contains("pray") || lower.Contains("worship"))
                {
                    enhanced.Add(keyword + " muslim hijab");
                }
                else
                {
                    // Replace with silhouette version
                    enhanced.Add(keyword.Replace("woman", "person silhouette", StringComparison.OrdinalIgnoreCase)
                                       .Replace("girl", "person", StringComparison.OrdinalIgnoreCase)
                                       .Replace("female", "person", StringComparison.OrdinalIgnoreCase));
                }
            }
            else
            {
                enhanced.Add(keyword);
            }
        }

        return enhanced.Distinct().ToList();
    }

    private string TryReplaceFemaleKeyword(string keyword)
    {
        var lower = keyword.ToLowerInvariant();
        
        // Check exact replacements first
        foreach (var (pattern, replacement) in FemaleReplacements)
        {
            if (lower.Contains(pattern.ToLowerInvariant()))
            {
                return replacement;
            }
        }
        
        // Generic replacement for any remaining female terms
        if (ContainsFemaleTerms(lower))
        {
            return keyword
                .Replace("woman", "person silhouette", StringComparison.OrdinalIgnoreCase)
                .Replace("women", "people", StringComparison.OrdinalIgnoreCase)
                .Replace("girl", "person", StringComparison.OrdinalIgnoreCase)
                .Replace("lady", "person", StringComparison.OrdinalIgnoreCase)
                .Replace("female", "person", StringComparison.OrdinalIgnoreCase);
        }

        return keyword;
    }

    private static bool ContainsFemaleTerms(string text)
    {
        var femaleTerms = new[] { "woman", "women", "girl", "lady", "female", "wife", "mother", "mom" };
        return femaleTerms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
