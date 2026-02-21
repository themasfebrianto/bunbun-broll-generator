using BunbunBroll.Models;

namespace BunbunBroll.Services;

/// <summary>
/// Script Processor - Ingests raw text and breaks it into segments and sentences.
/// Each sentence will get its own B-Roll clip.
/// </summary>
public interface IScriptProcessor
{
    List<ScriptSegment> SegmentScript(string rawScript);
    TextOverlay? ExtractTextOverlay(ref string text);
}

public class ScriptProcessor : IScriptProcessor
{
    private readonly ILogger<ScriptProcessor> _logger;

    // Sentence delimiters (end of sentence)
    private static readonly char[] SentenceDelimiters = { '.', '!', '?' };
    
    // Segment delimiters (new paragraph/scene)
    private static readonly string[] SegmentDelimiters = { "\n\n", "\r\n\r\n" };
    
    // Maximum words per segment (for grouping)
    private const int MaxWordsPerSegment = 1200;
    
    // Minimum words per sentence to process (skip very short)
    private const int MinWordsPerSentence = 3;

    public ScriptProcessor(ILogger<ScriptProcessor> logger)
    {
        _logger = logger;
    }

    public List<ScriptSegment> SegmentScript(string rawScript)
    {
        if (string.IsNullOrWhiteSpace(rawScript))
        {
            _logger.LogWarning("Empty script provided for segmentation");
            return new List<ScriptSegment>();
        }

        var segments = new List<ScriptSegment>();
        
        // Step 1: Split by double newlines (paragraphs/scenes)
        var paragraphs = SplitIntoParagraphs(rawScript);
        
        int segmentId = 1;
        int globalSentenceId = 1;

        foreach (var paragraph in paragraphs)
        {
            if (string.IsNullOrWhiteSpace(paragraph))
                continue;
                
            var paragraphSegments = ProcessParagraph(paragraph, ref segmentId, ref globalSentenceId);
            segments.AddRange(paragraphSegments);
        }

        var totalSentences = segments.Sum(s => s.Sentences.Count);
        var totalWords = segments.Sum(s => s.WordCount);
        var totalDuration = segments.Sum(s => s.TotalEstimatedDuration);
        
        _logger.LogInformation(
            "Segmented script: {Segments} segments, {Sentences} sentences, {Words} words, ~{Duration:F0}s estimated", 
            segments.Count, totalSentences, totalWords, totalDuration);
            
        return segments;
    }
    
    private List<ScriptSegment> ProcessParagraph(string paragraph, ref int segmentId, ref int globalSentenceId)
    {
        var segments = new List<ScriptSegment>();
        
        // 1. Extract Overlay Tags
        var overlay = ExtractTextOverlay(ref paragraph);

        // 2. Extract Title
        var (title, content) = ExtractTitleIfPresent(paragraph);

        // 3. Split into sentences (which now contain just the naration)
        var sentences = SplitIntoSentences(content);
        
        if (sentences.Count == 0)
            return segments;

        // Clean up sentences
        for (int i = 0; i < sentences.Count; i++)
        {
            sentences[i] = sentences[i].Replace("[ARABIC]", "").Replace("[REF]", "");
        }
        
        // 4. Create segment(s)
        var wordCount = sentences.Sum(s => s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
        
        if (wordCount > MaxWordsPerSegment)
        {
            var subSegments = SplitLargeParagraph(sentences, title, segmentId, ref globalSentenceId);
            
            // Assign overlay only to the first segment
            if (overlay != null && subSegments.Count > 0 && subSegments[0].Sentences.Count > 0)
            {
                subSegments[0].Sentences[0].TextOverlay = overlay;
            }
            
            segments.AddRange(subSegments);
            segmentId += subSegments.Count;
        }
        else
        {
            var segment = CreateSegment(segmentId, title, content, sentences, ref globalSentenceId);
            
            // Assign overlay to the first sentence in the segment
            if (overlay != null && segment.Sentences.Count > 0)
            {
                segment.Sentences[0].TextOverlay = overlay;
                // If the TextOverlay text is empty (LLM didn't provide it inside the tag), fallback to the sentence text
                if (string.IsNullOrWhiteSpace(overlay.Text))
                {
                    overlay.Text = segment.Sentences[0].Text;
                }
            }
            
            segments.Add(segment);
            segmentId++;
        }

        return segments;
    }

    public TextOverlay? ExtractTextOverlay(ref string text)
    {
        TextOverlay? overlay = null;
        string? typeStr = null;
        string? arabic = null;
        string? reference = null;
        string? displayText = null;

        var overlayMatch = System.Text.RegularExpressions.Regex.Match(text, @"\[?OVERLAY:(\w+)\]?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        if (overlayMatch.Success)
        {
            typeStr = overlayMatch.Groups[1].Value;
            
            var arabicMatch = System.Text.RegularExpressions.Regex.Match(text, @"\[?ARABIC\]?:?\s*(.+?)(?=(?:\[?REF\]?|\bREF\b)|\[?TEXT\]?|\[?\d{2}:\d{2}\]?|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
            if (arabicMatch.Success)
            {
                arabic = arabicMatch.Groups[1].Value.Trim();
                text = text.Replace(arabicMatch.Value, "");
            }

            var refMatch = System.Text.RegularExpressions.Regex.Match(text, @"(?:\[?REF\]?|\bREF\b)\s*:?\s*(.+?)(?=\[?TEXT\]?|\[?\d{2}:\d{2}\]?|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
            if (refMatch.Success)
            {
                reference = refMatch.Groups[1].Value.Trim();
                text = text.Replace(refMatch.Value, "");
            }

            // Parse [TEXT] tag for explicit display text (used for KeyPhrase short text)
            var textTagMatch = System.Text.RegularExpressions.Regex.Match(text, @"\[?TEXT\]?\s*:?\s*(.+?)(?=\[?\d{2}:\d{2}\]?|$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
            if (textTagMatch.Success)
            {
                displayText = textTagMatch.Groups[1].Value.Trim();
                text = text.Replace(textTagMatch.Value, "");
            }

            text = text.Replace(overlayMatch.Value, "");
            
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\[?ARABIC\]?:?", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\[?REF\]?:?", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\[?TEXT\]?:?", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            text = text.Trim();

            if (!string.IsNullOrEmpty(typeStr) && Enum.TryParse<TextOverlayType>(typeStr, true, out var parsedType))
            {
                overlay = new TextOverlay
                {
                    Type = parsedType,
                    Text = displayText ?? string.Empty,
                    ArabicText = arabic,
                    Reference = reference,
                    Style = parsedType switch
                    {
                        TextOverlayType.QuranVerse => TextStyle.Quran,
                        TextOverlayType.Hadith => TextStyle.Hadith,
                        TextOverlayType.RhetoricalQuestion => TextStyle.Question,
                        _ => TextStyle.Default
                    }
                };
            }
        }
        else
        {
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).ToList();
            var cleanLines = lines.Where(l => 
                !l.StartsWith("[OVERLAY:", StringComparison.OrdinalIgnoreCase) &&
                !l.StartsWith("OVERLAY:", StringComparison.OrdinalIgnoreCase) &&
                !l.StartsWith("[ARABIC]", StringComparison.OrdinalIgnoreCase) &&
                !l.StartsWith("ARABIC ", StringComparison.OrdinalIgnoreCase) &&
                !l.StartsWith("[REF]", StringComparison.OrdinalIgnoreCase) &&
                !l.StartsWith("REF ", StringComparison.OrdinalIgnoreCase) &&
                !l.StartsWith("[TEXT]", StringComparison.OrdinalIgnoreCase) &&
                !l.StartsWith("TEXT ", StringComparison.OrdinalIgnoreCase)
            ).ToList();
            text = string.Join("\n", cleanLines).Trim();
        }

        return overlay;
    }

    /// <summary>
    /// For KeyPhrase overlays without an explicit [TEXT] tag, truncates the fallback
    /// text to first sentence or max ~60 characters for a punchy display.
    /// </summary>
    public static string TruncateForKeyPhrase(string fullText, int maxLength = 60)
    {
        if (string.IsNullOrWhiteSpace(fullText)) return fullText;
        
        // Try to take the first sentence
        var sentenceEnd = fullText.IndexOfAny(new[] { '.', '!', '?' });
        if (sentenceEnd > 0 && sentenceEnd <= maxLength)
        {
            return fullText[..(sentenceEnd + 1)].Trim();
        }
        
        // Otherwise truncate at word boundary
        if (fullText.Length <= maxLength) return fullText.Trim();
        
        var truncated = fullText[..maxLength];
        var lastSpace = truncated.LastIndexOf(' ');
        if (lastSpace > maxLength / 2)
        {
            truncated = truncated[..lastSpace];
        }
        return truncated.Trim() + "...";
    }

    private List<string> SplitIntoParagraphs(string text)
    {
        // Split by double newlines
        var result = new List<string>();
        var current = text;
        
        foreach (var delimiter in SegmentDelimiters)
        {
            current = current.Replace(delimiter, "\n§\n"); // Use § as marker
        }
        
        var parts = current.Split("§", StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
            {
                result.Add(trimmed);
            }
        }
        
        // If no paragraphs found, treat whole text as one
        if (result.Count == 0 && !string.IsNullOrWhiteSpace(text))
        {
            result.Add(text.Trim());
        }
        
        return result;
    }

    private (string title, string content) ExtractTitleIfPresent(string paragraph)
    {
        // Check for patterns like "Scene 1:", "Adegan 1:", "1.", etc.
        var lines = paragraph.Split('\n', 2);
        
        if (lines.Length >= 1)
        {
            var firstLine = lines[0].Trim();
            
            // Check if first line looks like a title (short, ends with colon, or is a number)
            if (firstLine.Length < 100 && 
                (firstLine.EndsWith(':') || 
                 firstLine.StartsWith("Scene", StringComparison.OrdinalIgnoreCase) ||
                 firstLine.StartsWith("Adegan", StringComparison.OrdinalIgnoreCase) ||
                 System.Text.RegularExpressions.Regex.IsMatch(firstLine, @"^\d+[\.\:]\s*\w+")))
            {
                var content = lines.Length > 1 ? lines[1].Trim() : "";
                
                // If no content after title, the whole paragraph is content
                if (string.IsNullOrWhiteSpace(content))
                {
                    return ("", paragraph);
                }
                
                return (firstLine.TrimEnd(':'), content);
            }
        }
        
        return ("", paragraph);
    }

    private List<string> SplitIntoSentences(string text)
    {
        var sentences = new List<string>();
        var current = new System.Text.StringBuilder();
        
        for (int i = 0; i < text.Length; i++)
        {
            current.Append(text[i]);
            
            if (SentenceDelimiters.Contains(text[i]))
            {
                // Check if this is actually end of sentence (not abbreviation like "Dr.")
                var sentence = current.ToString().Trim();
                
                if (!string.IsNullOrWhiteSpace(sentence))
                {
                    var wordCount = sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                    
                    if (wordCount >= MinWordsPerSentence)
                    {
                        sentences.Add(sentence);
                    }
                    else if (sentences.Count > 0)
                    {
                        // Merge short sentence with previous
                        sentences[^1] += " " + sentence;
                    }
                    else
                    {
                        sentences.Add(sentence);
                    }
                }
                
                current.Clear();
            }
        }
        
        // Don't forget remaining text
        var remaining = current.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(remaining))
        {
            var wordCount = remaining.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            
            if (wordCount >= MinWordsPerSentence)
            {
                sentences.Add(remaining);
            }
            else if (sentences.Count > 0)
            {
                sentences[^1] += " " + remaining;
            }
            else if (wordCount > 0)
            {
                sentences.Add(remaining);
            }
        }
        
        return sentences;
    }

    private List<ScriptSegment> SplitLargeParagraph(
        List<string> sentences, 
        string baseTitle, 
        int baseSegmentId,
        ref int globalSentenceId)
    {
        var segments = new List<ScriptSegment>();
        var currentSentences = new List<string>();
        var currentWordCount = 0;
        var partNumber = 1;
        
        foreach (var sentence in sentences)
        {
            var sentenceWords = sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            
            if (currentWordCount + sentenceWords > MaxWordsPerSegment && currentSentences.Count > 0)
            {
                // Create segment from current sentences
                var title = string.IsNullOrEmpty(baseTitle) 
                    ? $"Segment {baseSegmentId} Part {partNumber}"
                    : $"{baseTitle} (Part {partNumber})";
                    
                var content = string.Join(" ", currentSentences);
                var segment = CreateSegment(baseSegmentId, title, content, currentSentences, ref globalSentenceId);
                segments.Add(segment);
                
                currentSentences.Clear();
                currentWordCount = 0;
                partNumber++;
            }
            
            currentSentences.Add(sentence);
            currentWordCount += sentenceWords;
        }
        
        // Create final segment
        if (currentSentences.Count > 0)
        {
            var title = string.IsNullOrEmpty(baseTitle) 
                ? (partNumber > 1 ? $"Segment {baseSegmentId} Part {partNumber}" : $"Segment {baseSegmentId}")
                : (partNumber > 1 ? $"{baseTitle} (Part {partNumber})" : baseTitle);
                
            var content = string.Join(" ", currentSentences);
            var segment = CreateSegment(baseSegmentId + partNumber - 1, title, content, currentSentences, ref globalSentenceId);
            segments.Add(segment);
        }
        
        return segments;
    }

    private ScriptSegment CreateSegment(
        int segmentId, 
        string title, 
        string content, 
        List<string> sentenceTexts,
        ref int globalSentenceId)
    {
        var segment = new ScriptSegment
        {
            Id = segmentId,
            Title = string.IsNullOrEmpty(title) ? $"Segment {segmentId}" : title,
            OriginalText = content,
            Status = SegmentStatus.Pending
        };
        
        foreach (var sentenceText in sentenceTexts)
        {
            segment.Sentences.Add(new ScriptSentence
            {
                Id = globalSentenceId++,
                SegmentId = segmentId,
                Text = sentenceText,
                Status = SentenceStatus.Pending
            });
        }
        
        return segment;
    }
}
