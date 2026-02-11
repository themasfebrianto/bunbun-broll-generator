using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BunbunBroll.Models;
using Microsoft.Extensions.Options;

namespace BunbunBroll.Services;

/// <summary>
/// Intelligence Layer - Interfaces with Local Gemini LLM for keyword extraction.
/// </summary>
public interface IIntelligenceService
{
    Task<KeywordResult> ExtractKeywordsAsync(string text, string? mood = null, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Extract keywords for multiple sentences in a single AI call (much faster!)
    /// Returns flat keyword lists for backward compatibility.
    /// </summary>
    Task<Dictionary<int, List<string>>> ExtractKeywordsBatchAsync(
        IEnumerable<(int Id, string Text)> sentences, 
        string? mood = null, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Extract layered keyword sets for multiple sentences in a single AI call.
    /// Returns KeywordSet with Primary, Mood, Contextual, Action, and Fallback layers.
    /// </summary>
    Task<Dictionary<int, KeywordSet>> ExtractKeywordSetBatchAsync(
        IEnumerable<(int Id, string Text)> sentences, 
        string? mood = null, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// General-purpose content generation via LLM.
    /// Used by ScriptOrchestrator for script phase generation.
    /// </summary>
    Task<string?> GenerateContentAsync(
        string systemPrompt, 
        string userPrompt, 
        int maxTokens = 4000, 
        double temperature = 0.7, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Classify script segments as B-Roll video or AI Image Generation (Whisk),
    /// and generate appropriate prompts for each.
    /// </summary>
    Task<List<BrollPromptItem>> ClassifyAndGeneratePromptsAsync(
        List<(string Timestamp, string ScriptText)> segments,
        string topic,
        Action<List<BrollPromptItem>>? onBatchComplete = null,
        CancellationToken cancellationToken = default);
}

public class IntelligenceService : IIntelligenceService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<IntelligenceService> _logger;
    private readonly GeminiSettings _settings;

    private const string SystemPrompt = @"B-Roll keyword extractor. Convert script to English stock footage keywords.

OUTPUT (JSON only, no markdown):
{
  ""primaryKeywords"": [""2-3 word visual match"", ""subject + setting""],
  ""moodKeywords"": [""emotion + visual""],
  ""contextualKeywords"": [""setting + time""],
  ""actionKeywords"": [""movement""],
  ""fallbackKeywords"": [""clouds timelapse"", ""city skyline""],
  ""suggestedCategory"": ""People|Nature|Urban|Business|Abstract"",
  ""detectedMood"": ""melancholic|anxious|hopeful|calm|energetic""
}

RULES:
- Translate ALL to English
- 2-3 words per keyword (not single words)
- Add context: ""bedroom ceiling"" not ""ceiling""
- Avoid religious/sensitive triggers
- fallbackKeywords: always include safe universals

VISUAL STYLE GUIDELINES (apply to ALL keyword layers when style is specified):
- Cinematic: Use keywords like ""cinematic lighting"", ""film grain"", ""dramatic shadows"", ""slow motion"", ""wide shot"", ""depth of field"", ""golden hour"", ""lens flare"", ""movie scene""
- Moody: Use keywords like ""dark atmosphere"", ""low key lighting"", ""shadows"", ""moody colors"", ""dim lighting"", ""dramatic contrast"", ""night scene"", ""film noir""
- Bright: Use keywords like ""bright lighting"", ""vibrant colors"", ""sunny day"", ""high key"", ""natural light"", ""clean background"", ""cheerful"", ""daylight""
- Auto: No specific style modifiers, let content dictate

EMOTIONAL MOOD DETECTION (separate from visual style):
melancholic=rain window, anxious=clock ticking, hopeful=sunrise, calm=lake reflection, energetic=city traffic

IMPORTANT: When user specifies a Visual Style, weave those terms into PRIMARY, MOOD, and CONTEXTUAL keywords. Example for Cinematic: ""person walking cinematic lighting"" not just ""person walking"".";

    public IntelligenceService(
        HttpClient httpClient, 
        ILogger<IntelligenceService> logger, 
        IOptions<GeminiSettings> settings)
    {
        _httpClient = httpClient;
        _logger = logger;
        _settings = settings.Value;
    }

    public async Task<KeywordResult> ExtractKeywordsAsync(string text, string? mood = null, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new KeywordResult();

        try
        {
            var userPrompt = mood != null 
                ? $"Mood/Style: {mood}\n\nSegment: {text}" 
                : $"Segment: {text}";

            var request = new GeminiChatRequest
            {
                Model = _settings.Model,
                Messages = new List<GeminiMessage>
                {
                    new() { Role = "system", Content = SystemPrompt },
                    new() { Role = "user", Content = userPrompt }
                },
                Temperature = 0.3,
                MaxTokens = 300 // Optimized for balanced mode
            };

            _logger.LogDebug("Sending request to Gemini: {Text}", text);

            var response = await _httpClient.PostAsJsonAsync(
                "v1/chat/completions", 
                request, 
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var geminiResponse = await response.Content.ReadFromJsonAsync<GeminiChatResponse>(cancellationToken: cancellationToken);
            
            var rawContent = geminiResponse?.Choices?.FirstOrDefault()?.Message?.Content;
            result.RawResponse = rawContent;
            result.TokensUsed = geminiResponse?.Usage?.TotalTokens ?? 0;

            if (!string.IsNullOrEmpty(rawContent))
            {
                var cleanedJson = CleanJsonResponse(rawContent);
                
                _logger.LogDebug("Raw AI response: {Raw}", rawContent);
                _logger.LogDebug("Cleaned JSON: {Cleaned}", cleanedJson);
                
                // Try parsing as new layered format first
                result.KeywordSet = ParseKeywordResponse(cleanedJson);
                result.Success = result.KeywordSet.TotalCount > 0;
                
                // If layered parsing failed, try legacy flat array format
                if (!result.Success)
                {
                    try
                    {
                        var keywords = JsonSerializer.Deserialize<List<string>>(cleanedJson);
                        if (keywords != null && keywords.Count > 0)
                        {
                            result.KeywordSet = KeywordSet.FromFlat(keywords);
                            result.Success = true;
                        }
                    }
                    catch (JsonException)
                    {
                        // Try text extraction as last resort
                        var extractedKeywords = ExtractKeywordsFromText(rawContent);
                        result.KeywordSet = KeywordSet.FromFlat(extractedKeywords);
                        result.Success = result.KeywordSet.TotalCount > 0;
                    }
                }
            }

            _logger.LogInformation(
                "Extracted {Count} keywords (P:{Primary} M:{Mood} C:{Context} A:{Action} F:{Fallback}) for segment in {Ms}ms. Category: {Category}, Mood: {DetectedMood}. STYLE: {Style}",
                result.KeywordSet.TotalCount,
                result.KeywordSet.Primary.Count,
                result.KeywordSet.Mood.Count,
                result.KeywordSet.Contextual.Count,
                result.KeywordSet.Action.Count,
                result.KeywordSet.Fallback.Count,
                stopwatch.ElapsedMilliseconds,
                result.SuggestedCategory ?? "N/A",
                result.DetectedMood ?? "N/A",
                mood ?? "Auto");

            // Log actual keywords for debugging
            _logger.LogDebug("PRIMARY: {Keywords}", string.Join(", ", result.KeywordSet.Primary));
            _logger.LogDebug("MOOD: {Keywords}", string.Join(", ", result.KeywordSet.Mood));
            _logger.LogDebug("CONTEXTUAL: {Keywords}", string.Join(", ", result.KeywordSet.Contextual));
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to connect to Gemini server");
            result.Error = "Failed to connect to local Gemini server. Is it running?";
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("Gemini request timed out");
            result.Error = "Request timed out. Check local server status.";
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse Gemini response: {Raw}", result.RawResponse);
            result.Error = "Invalid response format from AI";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during keyword extraction");
            result.Error = ex.Message;
        }
        finally
        {
            result.ProcessingTime = stopwatch.Elapsed;
        }

        return result;
    }

    /// <summary>
    /// Parse the AI response into a layered KeywordSet.
    /// </summary>
    private KeywordSet ParseKeywordResponse(string json)
    {
        try
        {
            var options = new JsonSerializerOptions 
            { 
                PropertyNameCaseInsensitive = true 
            };
            
            var response = JsonSerializer.Deserialize<KeywordExtractionResponse>(json, options);
            
            if (response != null)
            {
                return response.ToKeywordSet();
            }
        }
        catch (JsonException ex)
        {
            _logger.LogDebug("Layered keyword parsing failed: {Error}", ex.Message);
        }
        
        return KeywordSet.Empty;
    }

    private static string CleanJsonResponse(string raw)
    {
        raw = raw.Trim();
        
        // Remove markdown code blocks
        if (raw.StartsWith("```json"))
            raw = raw[7..];
        else if (raw.StartsWith("```"))
            raw = raw[3..];
        
        if (raw.EndsWith("```"))
            raw = raw[..^3];
        
        return raw.Trim();
    }

    /// <summary>
    /// Fallback keyword extraction when AI doesn't return valid JSON.
    /// Tries to extract quoted strings or comma-separated values.
    /// </summary>
    private static List<string> ExtractKeywordsFromText(string text)
    {
        var keywords = new List<string>();
        
        // Try to find quoted strings
        var quoteMatches = System.Text.RegularExpressions.Regex.Matches(text, "\"([^\"]+)\"");
        foreach (System.Text.RegularExpressions.Match match in quoteMatches)
        {
            var keyword = match.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(keyword) && keyword.Length > 2 && keyword.Length < 50)
            {
                keywords.Add(keyword);
            }
        }

        // If we found some, return them
        if (keywords.Count > 0)
            return keywords.Take(6).ToList();

        // Otherwise try comma-separated extraction
        var parts = text.Split(new[] { ',', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var cleaned = part.Trim().Trim('[', ']', '"', '\'');
            if (!string.IsNullOrWhiteSpace(cleaned) && cleaned.Length > 2 && cleaned.Length < 50)
            {
                keywords.Add(cleaned);
            }
        }

        return keywords.Take(6).ToList();
    }

    /// <summary>
    /// Extract keywords for multiple sentences in a single AI call.
    /// This is MUCH faster than calling ExtractKeywordsAsync for each sentence.
    /// </summary>
    public async Task<Dictionary<int, List<string>>> ExtractKeywordsBatchAsync(
        IEnumerable<(int Id, string Text)> sentences, 
        string? mood = null, 
        CancellationToken cancellationToken = default)
    {
        var sentenceList = sentences.ToList();
        var results = new Dictionary<int, List<string>>();
        
        if (sentenceList.Count == 0)
            return results;

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Build batch prompt
            var batchPrompt = new System.Text.StringBuilder();
            if (mood != null)
            {
                batchPrompt.AppendLine($"Mood/Style for ALL sentences: {mood}");
                batchPrompt.AppendLine();
            }
            
            batchPrompt.AppendLine("Extract B-Roll keywords for each sentence below. Return as JSON object with sentence IDs as keys and keyword arrays as values.");
            batchPrompt.AppendLine("Example output: {\"1\": [\"keyword1\", \"keyword2\"], \"2\": [\"keyword3\", \"keyword4\"]}");
            batchPrompt.AppendLine();
            batchPrompt.AppendLine("SENTENCES:");
            
            foreach (var (id, text) in sentenceList)
            {
                batchPrompt.AppendLine($"[{id}]: {text}");
            }

            var request = new GeminiChatRequest
            {
                Model = _settings.Model,
                Messages = new List<GeminiMessage>
                {
                    new() { Role = "system", Content = SystemPrompt },
                    new() { Role = "user", Content = batchPrompt.ToString() }
                },
                Temperature = 0.3,
                MaxTokens = Math.Min(sentenceList.Count * 100, 4000) // Scale tokens with batch size (up to 40 sentences)
            };

            _logger.LogDebug("Batch extracting keywords for {Count} sentences", sentenceList.Count);

            var response = await _httpClient.PostAsJsonAsync(
                "v1/chat/completions", 
                request, 
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var geminiResponse = await response.Content.ReadFromJsonAsync<GeminiChatResponse>(cancellationToken: cancellationToken);
            var rawContent = geminiResponse?.Choices?.FirstOrDefault()?.Message?.Content;

            if (!string.IsNullOrEmpty(rawContent))
            {
                var cleanedJson = CleanJsonResponse(rawContent);
                
                try
                {
                    // Try to parse as {id: [keywords]} format
                    var parsed = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(cleanedJson);
                    if (parsed != null)
                    {
                        foreach (var (key, keywords) in parsed)
                        {
                            if (int.TryParse(key, out var id))
                            {
                                results[id] = keywords;
                            }
                        }
                    }
                }
                catch (JsonException)
                {
                    _logger.LogWarning("Batch parse failed, falling back to individual extraction");
                    // If batch parsing fails, return empty and let caller fall back
                }
            }

            _logger.LogInformation("Batch extracted keywords for {Success}/{Total} sentences in {Ms}ms", 
                results.Count, sentenceList.Count, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch keyword extraction failed for {Count} sentences", sentenceList.Count);
        }

        // Fill in any missing results with empty lists
        foreach (var (id, _) in sentenceList)
        {
            if (!results.ContainsKey(id))
            {
                results[id] = new List<string>();
            }
        }

        return results;
    }

    /// <summary>
    /// Extract layered keyword sets for multiple sentences in a single AI call.
    /// Returns KeywordSet with Primary, Mood, Contextual, Action, and Fallback layers.
    /// </summary>
    public async Task<Dictionary<int, KeywordSet>> ExtractKeywordSetBatchAsync(
        IEnumerable<(int Id, string Text)> sentences,
        string? mood = null,
        CancellationToken cancellationToken = default)
    {
        var sentenceList = sentences.ToList();
        var results = new Dictionary<int, KeywordSet>();

        if (sentenceList.Count == 0)
            return results;

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Build batch prompt for layered extraction
            var batchPrompt = new System.Text.StringBuilder();
            if (mood != null)
            {
                batchPrompt.AppendLine($"Mood/Style for ALL sentences: {mood}");
                batchPrompt.AppendLine();
            }

            batchPrompt.AppendLine("Extract B-Roll keywords for each sentence below.");
            batchPrompt.AppendLine("Return as JSON object where each key is the sentence ID.");
            batchPrompt.AppendLine("Each value should have: primaryKeywords, moodKeywords, contextualKeywords, actionKeywords, fallbackKeywords arrays.");
            batchPrompt.AppendLine("Also include suggestedCategory and detectedMood strings.");
            batchPrompt.AppendLine();
            batchPrompt.AppendLine("SENTENCES:");

            foreach (var (id, text) in sentenceList)
            {
                batchPrompt.AppendLine($"[{id}]: {text}");
            }

            var request = new GeminiChatRequest
            {
                Model = _settings.Model,
                Messages = new List<GeminiMessage>
                {
                    new() { Role = "system", Content = SystemPrompt },
                    new() { Role = "user", Content = batchPrompt.ToString() }
                },
                Temperature = 0.3,
                MaxTokens = Math.Min(sentenceList.Count * 200, 6000) // Optimized for balanced mode
            };

            _logger.LogDebug("Batch extracting layered keywords for {Count} sentences", sentenceList.Count);

            var response = await _httpClient.PostAsJsonAsync(
                "v1/chat/completions",
                request,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var geminiResponse = await response.Content.ReadFromJsonAsync<GeminiChatResponse>(cancellationToken: cancellationToken);
            var rawContent = geminiResponse?.Choices?.FirstOrDefault()?.Message?.Content;

            if (!string.IsNullOrEmpty(rawContent))
            {
                _logger.LogDebug("Raw AI response for batch: {Content}", rawContent.Length > 200 ? rawContent[..200] + "..." : rawContent);
                var cleanedJson = CleanJsonResponse(rawContent);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

                try
                {
                    var parsed = JsonSerializer.Deserialize<Dictionary<string, KeywordExtractionResponse>>(cleanedJson, options);
                    if (parsed != null)
                    {
                        foreach (var (key, keywordResponse) in parsed)
                        {
                            if (int.TryParse(key, out var id) && keywordResponse != null)
                            {
                                results[id] = keywordResponse.ToKeywordSet();
                            }
                        }
                    }
                }
                catch (JsonException)
                {
                    _logger.LogWarning("Layered batch parse failed, trying flat format");
                    try
                    {
                        var flatParsed = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(cleanedJson);
                        if (flatParsed != null)
                        {
                            foreach (var (key, keywords) in flatParsed)
                            {
                                if (int.TryParse(key, out var id) && keywords != null)
                                {
                                    results[id] = KeywordSet.FromFlat(keywords);
                                }
                            }
                        }
                    }
                    catch (JsonException ex2)
                    {
                        _logger.LogWarning("Both batch parse attempts failed: {Error}", ex2.Message);
                    }
                }
            }
            else
            {
                _logger.LogWarning("AI returned empty response for batch extraction");
            }

            _logger.LogInformation("Batch extracted layered keywords for {Success}/{Total} sentences in {Ms}ms",
                results.Count, sentenceList.Count, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch layered keyword extraction failed for {Count} sentences", sentenceList.Count);
        }

        // Fill in any missing results with empty KeywordSets
        foreach (var (id, _) in sentenceList)
        {
            if (!results.ContainsKey(id))
            {
                results[id] = KeywordSet.Empty;
            }
        }

        return results;
    }

    /// <summary>
    /// General-purpose content generation via LLM.
    /// </summary>
    public async Task<string?> GenerateContentAsync(
        string systemPrompt, 
        string userPrompt, 
        int maxTokens = 4000, 
        double temperature = 0.7, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new GeminiChatRequest
            {
                Model = _settings.Model,
                Messages = new List<GeminiMessage>
                {
                    new() { Role = "system", Content = systemPrompt },
                    new() { Role = "user", Content = userPrompt }
                },
                Temperature = temperature,
                MaxTokens = maxTokens
            };

            _logger.LogDebug("GenerateContent: Sending request ({MaxTokens} max tokens)", maxTokens);

            var response = await _httpClient.PostAsJsonAsync(
                "v1/chat/completions",
                request,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var geminiResponse = await response.Content.ReadFromJsonAsync<GeminiChatResponse>(
                cancellationToken: cancellationToken);

            var content = geminiResponse?.Choices?.FirstOrDefault()?.Message?.Content;
            
            _logger.LogInformation("GenerateContent: Received {Length} chars, {Tokens} tokens",
                content?.Length ?? 0,
                geminiResponse?.Usage?.TotalTokens ?? 0);

            return content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GenerateContent failed");
            return null;
        }
    }

    /// <summary>
    /// Classify script segments as B-Roll video or AI Image Generation (Whisk),
    /// and generate appropriate prompts for each.
    /// Processes in batches of 10 segments to avoid LLM timeouts.
    /// </summary>
    public async Task<List<BrollPromptItem>> ClassifyAndGeneratePromptsAsync(
        List<(string Timestamp, string ScriptText)> segments,
        string topic,
        Action<List<BrollPromptItem>>? onBatchComplete = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<BrollPromptItem>();
        if (segments.Count == 0) return results;

        var stopwatch = Stopwatch.StartNew();
        const int batchSize = 10;

        // Build the system prompt once (reused across all batches)
        var classifySystemPrompt = $@"You are a visual content classifier for Islamic video essays. Your job is to analyze script segments and decide the best visual approach for each.

For each segment, classify as:
1. **BROLL** - Use stock footage (Pexels/Pixabay) when the content depicts:
   - Real-world landscapes, nature, cityscapes, urban
   - General human activities (working, walking, talking, studying)
   - Modern/contemporary scenes
   - Objects, food, technology, daily life
   - Atmospheric footage (rain, clouds, sunrise, ocean)

2. **IMAGE_GEN** - Use AI image generation (Whisk / Imagen) when the content depicts:
   - Historical/ancient scenes (7th century Arabia, ancient civilizations)
   - Prophets or religious figures (requires divine light, no face)
   - Supernatural/eschatological events (Day of Judgment, afterlife, angels)
   - Abstract spiritual concepts (soul, faith, divine light)
   - Scenes that stock footage cannot realistically portray
   - Specific artistic scenes needing cultural/historical accuracy

{EraLibrary.GetEraSelectionInstructions()}

CHARACTER RULES (ISLAMIC SYAR'I):
{Models.CharacterRules.GENDER_RULES}

{Models.CharacterRules.PROPHET_RULES}

LOCKED VISUAL STYLE (REQUIRED FOR ALL IMAGE_GEN PROMPTS):
{Models.ImageVisualStyle.BASE_STYLE_SUFFIX}

For BROLL segments: Generate a concise English search query for stock footage (2-5 words).
For IMAGE_GEN segments: Generate a detailed Whisk-style prompt following this structure:
  [ERA PREFIX] [Detailed scene description: setting, action, lighting, atmosphere, characters]{{LOCKED_STYLE}}
  - Start with one era prefix from the list above
  - Include character descriptions with syar'i dress for females
  - If prophets appear: add 'face completely veiled in soft white-golden divine light, facial features not visible'
  - End with style suffix: '{Models.ImageVisualStyle.BASE_STYLE_SUFFIX}'

RESPOND WITH JSON ONLY (no markdown):
[
  {{
    ""index"": 0,
    ""mediaType"": ""BROLL"" or ""IMAGE_GEN"",
    ""prompt"": ""the generated prompt"",
    ""reasoning"": ""brief reason for classification (in Bahasa Indonesia)""  
  }}
]

RULES:
- Translate all prompts to English
- For BROLL: Keep prompts short (2-5 words), focused on searchable stock footage terms
- For IMAGE_GEN: Include era prefix, detailed scene, locked style suffix
- Never depict prophet faces
- Avoid sensitive/haram visual triggers";

        // Process in batches
        var totalBatches = (int)Math.Ceiling((double)segments.Count / batchSize);
        _logger.LogInformation("ClassifyBroll: Processing {Count} segments in {Batches} batches for topic '{Topic}'",
            segments.Count, totalBatches, topic);

        for (int batchIdx = 0; batchIdx < totalBatches; batchIdx++)
        {
            var batchStart = batchIdx * batchSize;
            var batchSegments = segments.Skip(batchStart).Take(batchSize).ToList();

            _logger.LogDebug("ClassifyBroll: Batch {Batch}/{Total} — segments {Start}-{End}",
                batchIdx + 1, totalBatches, batchStart, batchStart + batchSegments.Count - 1);

            try
            {
                var userPrompt = new System.Text.StringBuilder();
                userPrompt.AppendLine($"Topic: {topic}");
                userPrompt.AppendLine();
                userPrompt.AppendLine("SEGMENTS:");
                for (int i = 0; i < batchSegments.Count; i++)
                {
                    userPrompt.AppendLine($"[{i}] {batchSegments[i].Timestamp} {batchSegments[i].ScriptText}");
                }

                var request = new GeminiChatRequest
                {
                    Model = _settings.Model,
                    Messages = new List<GeminiMessage>
                    {
                        new() { Role = "system", Content = classifySystemPrompt },
                        new() { Role = "user", Content = userPrompt.ToString() }
                    },
                    Temperature = 0.4,
                    MaxTokens = Math.Min(batchSegments.Count * 200, 4000)
                };

                var response = await _httpClient.PostAsJsonAsync(
                    "v1/chat/completions",
                    request,
                    cancellationToken);

                response.EnsureSuccessStatusCode();

                var geminiResponse = await response.Content.ReadFromJsonAsync<GeminiChatResponse>(
                    cancellationToken: cancellationToken);

                var rawContent = geminiResponse?.Choices?.FirstOrDefault()?.Message?.Content;

                if (!string.IsNullOrEmpty(rawContent))
                {
                    var cleanedJson = CleanJsonResponse(rawContent);
                    _logger.LogDebug("ClassifyBroll batch {Batch} response: {Content}",
                        batchIdx + 1, rawContent.Length > 200 ? rawContent[..200] + "..." : rawContent);

                    try
                    {
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        var parsed = JsonSerializer.Deserialize<List<BrollClassificationResponse>>(cleanedJson, options);

                        if (parsed != null)
                        {
                            foreach (var item in parsed)
                            {
                                var localIdx = item.Index;
                                if (localIdx >= 0 && localIdx < batchSegments.Count)
                                {
                                    var globalIdx = batchStart + localIdx;
                                    results.Add(new BrollPromptItem
                                    {
                                        Index = globalIdx,
                                        Timestamp = segments[globalIdx].Timestamp,
                                        ScriptText = segments[globalIdx].ScriptText,
                                        MediaType = item.MediaType?.ToUpperInvariant() == "IMAGE_GEN"
                                            ? BrollMediaType.ImageGeneration
                                            : BrollMediaType.BrollVideo,
                                        Prompt = item.Prompt ?? string.Empty,
                                        Reasoning = item.Reasoning ?? string.Empty
                                    });
                                }
                            }
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning("ClassifyBroll batch {Batch} JSON parse failed: {Error}", batchIdx + 1, ex.Message);
                    }
                }

                // Notify caller with current results so far
                onBatchComplete?.Invoke(results);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ClassifyBroll batch {Batch} failed, segments {Start}-{End} will use fallback",
                    batchIdx + 1, batchStart, batchStart + batchSegments.Count - 1);

                // Fallback only for this batch's segments
                for (int i = 0; i < batchSegments.Count; i++)
                {
                    var globalIdx = batchStart + i;
                    if (!results.Any(r => r.Index == globalIdx))
                    {
                        results.Add(new BrollPromptItem
                        {
                            Index = globalIdx,
                            Timestamp = segments[globalIdx].Timestamp,
                            ScriptText = segments[globalIdx].ScriptText,
                            MediaType = BrollMediaType.BrollVideo,
                            Prompt = "atmospheric cinematic footage",
                            Reasoning = $"Batch {batchIdx + 1} error: {ex.Message}"
                        });
                    }
                }

                // Notify caller with current results (including fallbacks)
                onBatchComplete?.Invoke(results);
            }
        }

        // Fill in any missing segments
        for (int i = 0; i < segments.Count; i++)
        {
            if (!results.Any(r => r.Index == i))
            {
                results.Add(new BrollPromptItem
                {
                    Index = i,
                    Timestamp = segments[i].Timestamp,
                    ScriptText = segments[i].ScriptText,
                    MediaType = BrollMediaType.BrollVideo,
                    Prompt = "atmospheric cinematic footage",
                    Reasoning = "Fallback: LLM tidak mengembalikan klasifikasi"
                });
            }
        }

        results = results.OrderBy(r => r.Index).ToList();

        var brollCount = results.Count(r => r.MediaType == BrollMediaType.BrollVideo);
        var imageGenCount = results.Count(r => r.MediaType == BrollMediaType.ImageGeneration);
        _logger.LogInformation(
            "ClassifyBroll: Classified {Total} segments ({Broll} broll, {ImageGen} image gen) in {Ms}ms across {Batches} batches",
            results.Count, brollCount, imageGenCount, stopwatch.ElapsedMilliseconds, totalBatches);

        return results;
    }

    /// <summary>Internal DTO for parsing LLM classification response</summary>
    private class BrollClassificationResponse
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("mediaType")]
        public string? MediaType { get; set; }

        [JsonPropertyName("prompt")]
        public string? Prompt { get; set; }

        [JsonPropertyName("reasoning")]
        public string? Reasoning { get; set; }
    }
}

// Request/Response models for OpenAI-compatible API
public class GeminiChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "gemini-2.5-flash";
    
    [JsonPropertyName("messages")]
    public List<GeminiMessage> Messages { get; set; } = new();
    
    [JsonPropertyName("temperature")]
    public double Temperature { get; set; } = 0.7;
    
    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; } = 200;
}

public class GeminiMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;
    
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}

public class GeminiChatResponse
{
    [JsonPropertyName("choices")]
    public List<GeminiChoice>? Choices { get; set; }
    
    [JsonPropertyName("usage")]
    public GeminiUsage? Usage { get; set; }
}

public class GeminiChoice
{
    [JsonPropertyName("message")]
    public GeminiMessage? Message { get; set; }
}

public class GeminiUsage
{
    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }
}

public class GeminiSettings
{
    public string BaseUrl { get; set; } = "http://127.0.0.1:8317";
    public string Model { get; set; } = "gemini-3-pro-preview";
    public string ApiKey { get; set; } = "sk-dummy";
    public int TimeoutSeconds { get; set; } = 30;
}

public class AuthSettings
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
}
