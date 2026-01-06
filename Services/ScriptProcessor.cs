using BunBunBroll.Models;

namespace BunBunBroll.Services;

/// <summary>
/// Script Processor - Ingests raw text and breaks it into actionable segments.
/// </summary>
public interface IScriptProcessor
{
    List<ScriptSegment> SegmentScript(string rawScript);
}

public class ScriptProcessor : IScriptProcessor
{
    private readonly ILogger<ScriptProcessor> _logger;

    // Characters that indicate a natural scene break
    private static readonly char[] SentenceDelimiters = { '.', '!', '?', '\n' };
    
    // Minimum words per segment to avoid tiny clips
    private const int MinWordsPerSegment = 5;
    
    // Maximum words per segment before forcing a split
    private const int MaxWordsPerSegment = 50;

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
        
        // Step 1: Split by newlines first (natural scene breaks)
        var paragraphs = rawScript
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();

        int segmentId = 1;

        foreach (var paragraph in paragraphs)
        {
            // Step 2: Further split long paragraphs by sentences
            var sentences = SplitIntoSentences(paragraph);
            
            // Step 3: Merge short sentences, split long ones
            var normalizedSegments = NormalizeSegments(sentences);

            foreach (var text in normalizedSegments)
            {
                segments.Add(new ScriptSegment
                {
                    Id = segmentId++,
                    OriginalText = text.Trim(),
                    Status = SegmentStatus.Pending
                });
            }
        }

        _logger.LogInformation("Segmented script into {Count} segments", segments.Count);
        return segments;
    }

    private List<string> SplitIntoSentences(string text)
    {
        var sentences = new List<string>();
        var currentSentence = "";

        for (int i = 0; i < text.Length; i++)
        {
            currentSentence += text[i];

            if (SentenceDelimiters.Contains(text[i]))
            {
                if (!string.IsNullOrWhiteSpace(currentSentence))
                {
                    sentences.Add(currentSentence.Trim());
                }
                currentSentence = "";
            }
        }

        // Don't forget remaining text
        if (!string.IsNullOrWhiteSpace(currentSentence))
        {
            sentences.Add(currentSentence.Trim());
        }

        return sentences;
    }

    private List<string> NormalizeSegments(List<string> sentences)
    {
        var result = new List<string>();
        var buffer = "";

        foreach (var sentence in sentences)
        {
            var combined = string.IsNullOrEmpty(buffer) ? sentence : $"{buffer} {sentence}";
            var wordCount = combined.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

            if (wordCount > MaxWordsPerSegment)
            {
                // Flush buffer first
                if (!string.IsNullOrEmpty(buffer))
                {
                    result.Add(buffer);
                }
                
                // Split the long sentence into chunks
                var chunks = SplitLongSentence(sentence);
                result.AddRange(chunks);
                buffer = "";
            }
            else if (wordCount >= MinWordsPerSegment)
            {
                result.Add(combined);
                buffer = "";
            }
            else
            {
                buffer = combined;
            }
        }

        // Flush remaining buffer
        if (!string.IsNullOrEmpty(buffer))
        {
            result.Add(buffer);
        }

        return result;
    }

    private List<string> SplitLongSentence(string sentence)
    {
        var words = sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var chunks = new List<string>();
        
        for (int i = 0; i < words.Length; i += MaxWordsPerSegment)
        {
            var chunk = string.Join(' ', words.Skip(i).Take(MaxWordsPerSegment));
            chunks.Add(chunk);
        }

        return chunks;
    }
}
