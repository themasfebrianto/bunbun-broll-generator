using System.Linq;
using System.Text.RegularExpressions;
using BunbunBroll.Models;
using Microsoft.Extensions.Logging;

namespace BunbunBroll.Services;

public interface IOverlayDetectionService
{
    Dictionary<int, TextOverlayDto> DetectOverlaysFromSourceScripts(string sessionDirectory, List<SrtEntry> expandedEntries);
}

public class OverlayDetectionService : IOverlayDetectionService
{
    private readonly ILogger<OverlayDetectionService> _logger;

    // Pattern to catch overlays like [OVERLAY:QuranVerse] or [OVERLAY:KeyPhrase]
    // And also captures the following lines containing [REF], [ARABIC] or [MM:SS] timestamp with the actual text.
    private static readonly Regex OverlayPattern = new Regex(
        @"\[OVERLAY:(?<type>[A-Za-z]+)\]\s*(?<content>.*?)(?=\[OVERLAY:|\z)", 
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    public OverlayDetectionService(ILogger<OverlayDetectionService> logger)
    {
        _logger = logger;
    }

    public Dictionary<int, TextOverlayDto> DetectOverlaysFromSourceScripts(string sessionDirectory, List<SrtEntry> expandedEntries)
    {
        var overlays = new Dictionary<int, TextOverlayDto>();
        var scriptsDir = Path.Combine(sessionDirectory, "scripts");

        if (!Directory.Exists(scriptsDir))
        {
            _logger.LogWarning("Scripts directory not found for overlay detection: {Path}", scriptsDir);
            return overlays;
        }

        var mdFiles = Directory.GetFiles(scriptsDir, "*.md");
        var allSourceText = string.Join("\n\n", mdFiles.Select(File.ReadAllText));

        // Let's find all overlay blocks in the source markdown
        var matches = OverlayPattern.Matches(allSourceText);

        _logger.LogInformation("Found {Count} OVERLAY tags in source scripts.", matches.Count);

        foreach (Match match in matches)
        {
            var type = match.Groups["type"].Value;
            var contentBlock = match.Groups["content"].Value;
            
            // Extract Reference if any
            var refMatch = Regex.Match(contentBlock, @"\[REF\](?<ref>.*?)(?=\[|$)", RegexOptions.Singleline);
            var reference = refMatch.Success ? refMatch.Groups["ref"].Value.Trim() : null;

            // Extract Arabic if any
            var arabicMatch = Regex.Match(contentBlock, @"\[ARABIC\](?<arabic>.*?)(?=\[|$)", RegexOptions.Singleline);
            var arabic = arabicMatch.Success ? arabicMatch.Groups["arabic"].Value.Trim() : null;

            // Extract the actual spoken text that follows the [MM:SS] timestamp marker
            // We want to link the overlay to the beginning of this spoken text block.
            var textMatch = Regex.Match(contentBlock, @"\[\d{2}:\d{2}\](?<text>.*?)(?=\z)", RegexOptions.Singleline);
            var fullText = textMatch.Success ? textMatch.Groups["text"].Value.Trim() : contentBlock.Trim();

            // Find the best matching SrtEntry based on the first few words of the fullText
            var words = fullText.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) continue;

            // Just take the first 3-4 words to match the start of the sentence
            var searchPhrase = string.Join(" ", words.Take(Math.Min(4, words.Length))).ToLowerInvariant();
            
            // Clean punctuation for better matching
            searchPhrase = new string(searchPhrase.Where(c => !char.IsPunctuation(c)).ToArray());

            int foundIndex = -1;
            for (int i = 0; i < expandedEntries.Count; i++)
            {
                var entryTextRaw = expandedEntries[i].Text;
                // Combine current entry and next entry in case the search phrase spans across two SRT entries
                if (i < expandedEntries.Count - 1)
                {
                    entryTextRaw += " " + expandedEntries[i + 1].Text;
                }
                
                // Replace all whitespace components (newlines, tabs) with spaces so words don't squash together when stripping punctuation
                entryTextRaw = Regex.Replace(entryTextRaw, @"\s+", " ");
                var entryText = new string(entryTextRaw.ToLowerInvariant().Where(c => !char.IsPunctuation(c)).ToArray());
                
                if (entryText.Contains(searchPhrase))
                {
                    foundIndex = i;
                    break;
                }
            }

            if (foundIndex != -1)
            {
                // To create suspense or reading time, Quran and Hadith overlays often need a gap BEFORE they are spoken,
                // or at the exact index for generic overlays. We link it to the found index.
                overlays[foundIndex] = new TextOverlayDto
                {
                    Type = type,
                    Text = fullText, // The whole paragraph text
                    Arabic = arabic,
                    Reference = reference
                };
                
                _logger.LogInformation("Mapped Overlay {Type} to SRT Index {Index}: '{Phrase}'", type, foundIndex, searchPhrase);
            }
            else
            {
                // Try a very relaxed search as fallback (just the first two words)
                var fallbackPhrase = string.Join(" ", words.Take(2)).ToLowerInvariant();
                fallbackPhrase = new string(fallbackPhrase.Where(c => !char.IsPunctuation(c)).ToArray());
                
                for (int i = 0; i < expandedEntries.Count; i++)
                {
                    var entryTextRaw = expandedEntries[i].Text;
                    if (i < expandedEntries.Count - 1) entryTextRaw += " " + expandedEntries[i + 1].Text;
                    
                    entryTextRaw = Regex.Replace(entryTextRaw, @"\s+", " ");
                    var entryText = new string(entryTextRaw.ToLowerInvariant().Where(c => !char.IsPunctuation(c)).ToArray());
                    
                    if (entryText.Contains(fallbackPhrase))
                    {
                        foundIndex = i;
                        break;
                    }
                }
                
                if (foundIndex != -1)
                {
                    overlays[foundIndex] = new TextOverlayDto
                    {
                        Type = type,
                        Text = fullText,
                        Arabic = arabic,
                        Reference = reference
                    };
                    _logger.LogInformation("Mapped Overlay {Type} using Fallback to SRT Index {Index}: '{Phrase}'", type, foundIndex, fallbackPhrase);
                }
                else
                {
                    _logger.LogWarning("Could not map Overlay {Type} to any SRT entry. Search phrase: '{Phrase}', Fallback: '{Fallback}'", type, searchPhrase, fallbackPhrase);
                }
            }
        }

        return overlays;
    }
}
