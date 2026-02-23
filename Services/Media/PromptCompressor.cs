using System.Text.RegularExpressions;

namespace BunbunBroll.Services;

/// <summary>
/// Compresses verbose image prompts into streamlined format.
/// Reduces token usage while preserving essential visual direction.
/// </summary>
public static class PromptCompressor
{
    // Phrases to remove (redundant quality descriptors)
    private static readonly string[] RedundantPhrases = new[]
    {
        "expressive painterly textures",
        "atmospheric depth",
        "consistent visual tone",
        "ultra-detailed",
        "sharp focus",
        "8k quality",
        "highly detailed",
        "intricate details",
        "masterpiece",
        "trending on artstation",
        "award winning",
        // Scale stacking phrases
        "figures dwarfed by immense scale",
        "dwarfed by immense scale",
        "rising stories high",
        "establishing miracle and scale",
        // Era textbook labels
        "Late Ancient era Bronze Age",
        "Late Ancient Roman Empire era",
        "6th century BC Ancient Babylon",
        "coastal desert landscape stretching beyond"
    };

    // Redundant size adjectives (keep only first one found)
    private static readonly string[] SizeAdjectives = new[]
    {
        "massive", "huge", "large", "big", "enormous", "giant", "vast",
        "immense", "towering", "colossal", "mammoth", "gigantic",
        "imposing", "monumental"
    };

    /// <summary>
    /// Compresses a verbose prompt into a streamlined format.
    /// Target: 60-70% reduction in character count.
    /// </summary>
    public static string Compress(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
            return prompt;

        var compressed = prompt;

        // Step 1: Remove redundant quality phrases
        foreach (var phrase in RedundantPhrases)
        {
            compressed = Regex.Replace(compressed, $"\\b{Regex.Escape(phrase)}\\b,?\\s*", "",
                RegexOptions.IgnoreCase);
        }

        // Step 2: Deduplicate size adjectives (keep only first occurrence)
        compressed = DeduplicateSizeAdjectives(compressed);

        // Step 3: Clean up extra spaces and commas
        compressed = Regex.Replace(compressed, @"\s{2,}", " ");
        compressed = Regex.Replace(compressed, @",\s*,", ",");
        compressed = compressed.Trim(' ', ',');

        return compressed;
    }

    /// <summary>
    /// Extracts core elements from a prompt for reconstruction.
    /// Returns: (era, subject/action, style)
    /// </summary>
    public static (string Era, string Subject, string Style) ExtractCoreElements(string prompt)
    {
        var era = "";
        var subject = "";
        var style = "";

        // Extract era (typically at start: "1500 BC Ancient Egypt")
        var eraMatch = Regex.Match(prompt, @"^(\d+\s*(BC|AD|CE)?\s*[\w\s]+?era)[,\s]*",
            RegexOptions.IgnoreCase);
        if (eraMatch.Success)
            era = eraMatch.Groups[1].Value.Trim();

        // Extract style (typically at end after last comma)
        var lastCommaIndex = prompt.LastIndexOf(',');
        if (lastCommaIndex > 0)
        {
            style = prompt[(lastCommaIndex + 1)..].Trim();
            // Check if it looks like style tags
            if (!style.Contains("painting") && !style.Contains("art") &&
                !style.Contains("style") && !style.Contains("lighting"))
            {
                style = "";
            }
        }

        // Subject is everything between era and style
        var startIdx = eraMatch.Success ? eraMatch.Length : 0;
        var endIdx = lastCommaIndex > 0 && !string.IsNullOrEmpty(style)
            ? lastCommaIndex
            : prompt.Length;
            
        if (endIdx >= startIdx)
        {
            subject = prompt[startIdx..endIdx].Trim(' ', ',');
        }
        else
        {
            // Fallback for unexpected format
            subject = prompt.Trim(' ', ',');
        }

        return (era, subject, style);
    }

    /// <summary>
    /// Builds a streamlined prompt from extracted elements.
    /// Format: "[Era] [Subject], [Style]"
    /// </summary>
    public static string BuildStreamlinedPrompt(string era, string subject, string style)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(era))
            parts.Add(era);

        if (!string.IsNullOrWhiteSpace(subject))
            parts.Add(Compress(subject));

        if (!string.IsNullOrWhiteSpace(style))
            parts.Add(style);

        return string.Join(", ", parts);
    }

    private static string DeduplicateSizeAdjectives(string text)
    {
        var found = false;
        var result = text;

        foreach (var adj in SizeAdjectives)
        {
            var pattern = $"\\b{adj}\\b";
            if (Regex.IsMatch(result, pattern, RegexOptions.IgnoreCase))
            {
                if (found)
                {
                    // Remove subsequent occurrences
                    result = Regex.Replace(result, $"\\s*{pattern}\\s*", " ",
                        RegexOptions.IgnoreCase);
                }
                else
                {
                    found = true;
                }
            }
        }

        return result;
    }
}
