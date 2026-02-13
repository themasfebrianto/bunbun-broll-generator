namespace BunbunBroll.Services;

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
    
    public bool IsEnabled { get; set; } = true;

    // Indonesian to English translations for filtering
    private static readonly Dictionary<string, string> IndonesianTranslations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["wanita"] = "woman",
        ["perempuan"] = "woman",
        ["cewek"] = "girl",
        ["gadis"] = "girl",
        ["ibu"] = "mother",
        ["bunda"] = "mother"
    };

    // Keywords to completely BLOCK
    private static readonly HashSet<string> BlockedKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Beach/swimwear (existing + expanded)
        "bikini", "swimsuit", "swimwear", "beach party", "pool party",
        "swimming pool", "bathing suit", "beach body", "sunbathing",
        "beach bikini", "pool bikini", "summer beach",

        // Nightlife/party (existing + expanded)
        "nightclub", "club party", "bar party", "disco", "rave",
        "drinking party", "alcohol", "beer", "wine", "cocktail",
        "pub", "bartender", "nightclub dancing", "party club",

        // Revealing/sensual (existing + expanded)
        "sexy", "sensual", "seductive", "revealing", "lingerie",
        "underwear", "bra", "cleavage", "low cut", "mini skirt",
        "short dress", "tight dress", "bodycon", "crop top",
        "tank top", "sleeveless", "strapless", "backless",
        "shorts", "hot pants", "midriff", "see through",

        // Dance with revealing content (existing)
        "pole dance", "strip", "twerk", "belly dance", "latin dance",

        // Romance/intimate (existing + expanded)
        "kissing", "romantic kiss", "couple bed", "intimate",
        "love scene", "passion", "making out", "embrace romantic",
        "honeymoon", "bedroom couple",

        // Avoid non-modest female depictions (existing + expanded)
        "model female", "fashion model", "beauty model",
        "woman hair flowing", "woman hair wind", "brunette", "blonde woman",
        "redhead woman", "long hair woman", "curly hair woman",
        "makeup tutorial", "beauty salon", "spa treatment",

        // Music/concert (often revealing)
        "concert crowd", "music festival", "rave festival",

        // ALL HUMAN SUBJECTS - absolute block
        "person", "people", "human", "silhouette", "crowd",
        "man", "woman", "boy", "girl", "child", "children",
        "face", "portrait", "hands", "feet", "walking person",
        "people walking", "shadow person", "person standing",
        "person sitting", "person walking", "feet walking",
        "hands praying", "hands typing", "family"
    };

    // Female-related keywords to REPLACE with nature/urban alternatives (NO HUMAN SUBJECTS)
    private static readonly Dictionary<string, string> FemaleReplacements = new(StringComparer.OrdinalIgnoreCase)
    {
        // Replace all human terms with nature/urban equivalents
        ["woman"] = "sunset landscape",
        ["women"] = "ocean waves",
        ["girl"] = "flower garden",
        ["lady"] = "morning mist",
        ["female"] = "calm lake",
        ["wife"] = "warm sunlight",
        ["mother"] = "gentle river",
        ["mom"] = "sunrise meadow",
        
        // Replace scenarios with nature/urban visuals
        ["woman face"] = "sunset horizon",
        ["woman portrait"] = "mountain landscape",
        ["woman smile"] = "peaceful sunrise",
        ["woman looking"] = "vast desert horizon",
        ["woman walking"] = "empty street dusk",
        ["woman sitting"] = "calm lake reflection",
        ["woman standing"] = "mountain peak view",
        
        // Replace activities with nature/urban metaphors
        ["woman morning"] = "sunrise golden hour",
        ["woman night"] = "city lights night",
        ["woman alone"] = "solitary mountain",
        ["woman thinking"] = "clouds drifting sky",
        ["woman sad"] = "rain window mood",
        ["woman happy"] = "sunshine meadow",
        ["woman tired"] = "dimming evening sky",
        ["woman stressed"] = "storm clouds gathering",
        ["woman working"] = "modern office building",
        ["woman reading"] = "open book pages",
        ["woman cooking"] = "steaming kitchen",
        ["woman praying"] = "mosque interior light"
    };

    // Cinematic fallback keywords for when everything is filtered
    private static readonly string[] CinematicFallbacks = new[]
    {
        "city skyline night",
        "nature landscape cinematic",
        "clouds timelapse",
        "ocean waves sunset",
        "forest morning light",
        "mountain sunrise",
        "abstract light bokeh",
        "rain window mood",
        "aerial view city",
        "stars night sky"
    };

    // Preferred SAFE categories (no people at all)
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
        "water ripples", "light bokeh", "smoke motion", "writing pen paper"
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
            // Translate Indonesian first
            var translatedKeyword = TranslateIndonesian(keyword);
            var lowerKeyword = translatedKeyword.ToLowerInvariant();

            // Check if keyword contains any blocked words
            var isBlocked = BlockedKeywords.Any(blocked =>
                lowerKeyword.Contains(blocked) || blocked.Contains(lowerKeyword));

            if (isBlocked)
            {
                _logger.LogDebug("Halal filter: Blocked '{Keyword}'", keyword);
                continue;
            }

            // Check if keyword needs replacement
            var replaced = TryReplaceFemaleKeyword(translatedKeyword);
            if (replaced != translatedKeyword)
            {
                _logger.LogDebug("Halal filter: Replaced '{Original}' -> '{Replaced}'", keyword, replaced);
                filtered.Add(replaced);
            }
            else
            {
                filtered.Add(keyword);
            }
        }

        // If too many keywords were filtered, add CINEMATIC fallbacks
        if (filtered.Count < 3)
        {
            _logger.LogDebug("Halal filter: Adding cinematic fallback keywords");
            var cinematicToAdd = CinematicFallbacks
                .OrderBy(_ => Random.Shared.Next())
                .Take(4 - filtered.Count);
            filtered.AddRange(cinematicToAdd);
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
                    enhanced.Add("mosque interior light");
                }
                else
                {
                    // Replace with nature/urban equivalent
                    enhanced.Add("nature landscape cinematic");
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
        
        // Generic replacement for any remaining female terms — use nature
        if (ContainsFemaleTerms(lower))
        {
            return "nature landscape cinematic";
        }

        return keyword;
    }

    private static string TranslateIndonesian(string keyword)
    {
        var lower = keyword.ToLowerInvariant();
        foreach (var (indo, english) in IndonesianTranslations)
        {
            if (lower.Contains(indo))
            {
                return keyword.Replace(indo, english, StringComparison.OrdinalIgnoreCase);
            }
        }
        return keyword;
    }

    private static bool ContainsFemaleTerms(string text)
    {
        var femaleTerms = new[] { "woman", "women", "girl", "lady", "female", "wife", "mother", "mom" };
        return femaleTerms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
