using BunbunBroll.Models;

namespace BunbunBroll.Services;

/// <summary>
/// Predefined era prefixes for image prompt generation (ported from ScriptFlow)
/// and era detection with automatic filter/texture assignment
/// </summary>
public static class EraLibrary
{
    public static readonly IReadOnlyList<string> HistoricalPropheticEras = new List<string>
    {
        "7th century Arabia Islamic era, prophetic atmosphere, ",
        "6th century Pre-Islamic Arabia era, jahiliyya atmosphere, ",
        "1500 BC Ancient Egypt era, prophetic confrontation, ",
        "6th century BC Ancient Babylon era, ancient mystery, ",
        "Late Ancient Roman Empire era, civilization decline, "
    };

    public static readonly IReadOnlyList<string> EndTimesEras = new List<string>
    {
        "Islamic End Times era, apocalyptic atmosphere, ",
        "Dajjal deception era, false light and illusion, ",
        "Ya'juj and Ma'juj chaos era, overwhelming destruction, ",
        "Pre-Imam Mahdi era, global confusion and fear, ",
        "Post-Nabi Isa descent era, fragile peace, ",
        "Sun rising from the west era, final apocalyptic sign, "
    };

    public static readonly IReadOnlyList<string> ModernEras = new List<string>
    {
        "21st century modern urban era, digital technology, ",
        "Late modern civilization era, moral decay, ",
        "Global surveillance era, dystopian control, ",
        "AI-dominated future era, cold technocracy, "
    };

    public static readonly IReadOnlyList<string> AbstractEras = new List<string>
    {
        "Post-apocalyptic era, abandoned cities, ",
        "Lost ancient civilization ruins era, ",
        "Metaphysical void era, existential reflection, ",
        "Cosmic end-of-world era, cracked sky, "
    };

    public static string GetEraSelectionInstructions()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("ERA SELECTION INSTRUCTIONS:");
        sb.AppendLine("Select the appropriate era prefix from the available options below based on the scene's setting.");
        sb.AppendLine();
        sb.AppendLine("AVAILABLE ERAS (select one per prompt):");
        sb.AppendLine();

        sb.AppendLine("Historical/Prophetic Eras:");
        foreach (var era in HistoricalPropheticEras) sb.AppendLine($"  - \"{era}\"");
        sb.AppendLine();

        sb.AppendLine("End Times Eschatological Eras:");
        foreach (var era in EndTimesEras) sb.AppendLine($"  - \"{era}\"");
        sb.AppendLine();

        sb.AppendLine("Modern/Contemporary Eras:");
        foreach (var era in ModernEras) sb.AppendLine($"  - \"{era}\"");
        sb.AppendLine();

        sb.AppendLine("Abstract/Symbolic Eras:");
        foreach (var era in AbstractEras) sb.AppendLine($"  - \"{era}\"");

        return sb.ToString();
    }

    // === Era Detection Keywords ===
    
    private static readonly Dictionary<VideoEra, string[]> EraKeywords = new()
    {
        [VideoEra.Ancient] = new[]
        {
            "7th century", "6th century", "ancient", "prophet", "prophetic", "islamic era",
            "egypt", "babylon", "roman empire", "jahiliyya", "pre-islamic", "1500 bc",
            "nabi", "rasul", "messenger", "pharaoh", "fir'aun", "ka'bah", "makkah",
            "medina", "hijrah", "quraysh", "sahabah", "companion", "desert", "oasis",
            "historical", "old civilization", "classical era"
        },
        [VideoEra.Apocalyptic] = new[]
        {
            "end times", "apocalyptic", "apocalypse", "kiamat", "qiyamah", "judgment day",
            "dajjal", "mahdi", "isa", "jesus", "ya'juj", "ma'juj", "gog", "magog",
            "final hour", "last days", "destruction", "chaos", "sun rising from west",
            "trumpet", "sur", "resurrection", "hereafter", "akhirah", "doomsday"
        },
        [VideoEra.Modern] = new[]
        {
            "21st century", "modern", "urban", "city", "digital", "technology",
            "contemporary", "skyscraper", "highway", "traffic", "smartphone", "internet",
            "social media", "ai", "artificial intelligence", "surveillance", "dystopian",
            "globalization", "western", "secular", "liberalism", "capitalism", "consumerism"
        },
        [VideoEra.Abstract] = new[]
        {
            "abstract", "spiritual", "soul", "ruh", "light", "divine", "cosmic",
            "metaphysical", "existential", "void", "symbolic", "conceptual", "ethereal",
            "heavenly", "celestial", "paradise", "jannah", "hellfire", "jahannam",
            "angel", "mala'ikah", "jinn", "devil", "syaithan", "iblis"
        },
        [VideoEra.Nature] = new[]
        {
            "nature", "mountain", "forest", "ocean", "sea", "river", "lake",
            "desert", "sand", "dunes", "trees", "plants", "animal", "wildlife",
            "sunrise", "sunset", "clouds", "sky", "stars", "moon", "landscape",
            "timelapse", "scenic", "wilderness", "natural", "earth", "creation"
        }
    };

    /// <summary>
    /// Detect era from prompt text based on keyword matching
    /// </summary>
    public static VideoEra DetectEraFromPrompt(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return VideoEra.None;

        var lowerPrompt = prompt.ToLowerInvariant();
        var scores = new Dictionary<VideoEra, int>();

        foreach (var (era, keywords) in EraKeywords)
        {
            int score = keywords.Count(k => lowerPrompt.Contains(k));
            if (score > 0)
                scores[era] = score;
        }

        if (scores.Count == 0)
            return VideoEra.None;

        // Return era with highest score
        return scores.OrderByDescending(s => s.Value).First().Key;
    }

    /// <summary>
    /// Get recommended filter and texture for a specific era
    /// Designed to make Image Gen (KenBurns) and B-roll blend seamlessly
    /// Optimized for available texture files:
    /// - filmgrain.mp4, filmgrain_2.mp4, filmgrain_colorfull.mp4
    /// - fire.mp4, grunge.mp4, harsh.mp4, scratches.mp4, surreal.mp4
    /// </summary>
    public static (VideoFilter Filter, VideoTexture Texture) GetFilterAndTextureForEra(VideoEra era)
    {
        return era switch
        {
            // Ancient/Prophetic: Warm painterly look like classic Islamic art
            // Matches img-1.png vibe: warm ochre tones, use surreal.mp4 for canvas effect
            VideoEra.Ancient => (VideoFilter.Warm, VideoTexture.Canvas),
            
            // Apocalyptic: Dramatic cinematic with fire.mp4 for dust/embers effect
            VideoEra.Apocalyptic => (VideoFilter.Cinematic, VideoTexture.Dust),
            
            // Modern: Cool or clean look, minimal texture
            VideoEra.Modern => (VideoFilter.Cool, VideoTexture.None),
            
            // Abstract: Artistic painting style with surreal texture
            VideoEra.Abstract => (VideoFilter.Painting, VideoTexture.Canvas),
            
            // Nature: Vintage nostalgic with filmgrain for organic feel
            VideoEra.Nature => (VideoFilter.Vintage, VideoTexture.FilmGrain),
            
            _ => (VideoFilter.None, VideoTexture.None)
        };
    }

    /// <summary>
    /// Get display name for era
    /// </summary>
    public static string GetEraDisplayName(VideoEra era)
    {
        return era switch
        {
            VideoEra.Ancient => "🏛️ Ancient",
            VideoEra.Apocalyptic => "🔥 Apocalyptic", 
            VideoEra.Modern => "🏙️ Modern",
            VideoEra.Abstract => "✨ Abstract",
            VideoEra.Nature => "🌿 Nature",
            _ => "❓ Unknown"
        };
    }

    /// <summary>
    /// Detect era from both prompt and script text for better accuracy
    /// </summary>
    public static VideoEra DetectEraFromContent(string prompt, string scriptText)
    {
        // Combine both for detection (script text usually has more context)
        var combinedText = $"{prompt} {scriptText}";
        return DetectEraFromPrompt(combinedText);
    }

    /// <summary>
    /// Detect era and auto-assign filter/texture to a BrollPromptItem
    /// Uses both prompt and script text for better accuracy
    /// </summary>
    public static void AutoAssignEraStyle(BrollPromptItem item)
    {
        // Use both prompt and script text for more accurate era detection
        var era = DetectEraFromContent(item.Prompt, item.ScriptText);
        var (filter, texture) = GetFilterAndTextureForEra(era);
        
        item.Era = era;
        
        // Only auto-assign if user hasn't manually set them
        if (item.Filter == VideoFilter.None)
            item.Filter = filter;
        if (item.Texture == VideoTexture.None)
            item.Texture = texture;
    }
}
