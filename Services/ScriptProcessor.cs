using BunBunBroll.Models;

namespace BunBunBroll.Services;

/// <summary>
/// Script Processor - Ingests raw text and breaks it into segments and sentences.
/// Each sentence will get its own B-Roll clip.
/// </summary>
public interface IScriptProcessor
{
    List<ScriptSegment> SegmentScript(string rawScript);
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
                
            // Check if paragraph starts with a title pattern (e.g., "Scene 1:", "Adegan 1:", etc.)
            var (title, content) = ExtractTitleIfPresent(paragraph);
            
            // Step 2: Split paragraph into sentences
            var sentences = SplitIntoSentences(content);
            
            if (sentences.Count == 0)
                continue;
            
            // Step 3: Check if we need to split this paragraph into multiple segments
            var wordCount = sentences.Sum(s => s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
            
            if (wordCount > MaxWordsPerSegment)
            {
                // Split large paragraph into multiple segments
                var subSegments = SplitLargeParagraph(sentences, title, segmentId, ref globalSentenceId);
                segments.AddRange(subSegments);
                segmentId += subSegments.Count;
            }
            else
            {
                // Create single segment
                var segment = CreateSegment(segmentId, title, content, sentences, ref globalSentenceId);
                segments.Add(segment);
                segmentId++;
            }
        }

        var totalSentences = segments.Sum(s => s.Sentences.Count);
        var totalWords = segments.Sum(s => s.WordCount);
        var totalDuration = segments.Sum(s => s.TotalEstimatedDuration);
        
        _logger.LogInformation(
            "Segmented script: {Segments} segments, {Sentences} sentences, {Words} words, ~{Duration:F0}s estimated", 
            segments.Count, totalSentences, totalWords, totalDuration);
            
        return segments;
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
