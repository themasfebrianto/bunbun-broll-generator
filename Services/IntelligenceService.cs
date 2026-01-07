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
}

public class IntelligenceService : IIntelligenceService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<IntelligenceService> _logger;
    private readonly GeminiSettings _settings;

    private const string SystemPrompt = @"You are a PROFESSIONAL B-Roll keyword extraction assistant for video editors. Your job is to analyze video scripts and generate LAYERED, OPTIMIZED English search keywords for stock footage platforms (Pexels/Pixabay).

=== ANALYSIS FRAMEWORK ===
For each script segment, analyze across 4 dimensions:
1. CONTEXT: Physical setting, location, time of day, environment
2. EMOTION: Mood, feeling, tone, emotional intensity
3. ACTION: Movement, activity, gestures, dynamics
4. TOPIC: Main subject, theme, object

=== OUTPUT FORMAT ===
Respond with ONLY valid JSON (no markdown, no explanation):
{
  ""primaryKeywords"": [""exact visual match with context"", ""main subject + setting""],
  ""moodKeywords"": [""emotion + visual representation"", ""atmospheric visual""],
  ""contextualKeywords"": [""setting + modifier"", ""environment + time""],
  ""actionKeywords"": [""movement + subject"", ""activity visual""],
  ""fallbackKeywords"": [""safe generic visual"", ""universal stock footage""],
  ""suggestedCategory"": ""People|Nature|Urban|Business|Abstract|Lifestyle"",
  ""detectedMood"": ""melancholic|anxious|hopeful|calm|energetic|neutral""
}

=== KEYWORD RULES ===
1. ALL keywords MUST be in English (translate Indonesian/other languages)
2. Use 2-3 word combinations ONLY - never single words
3. Add CONTEXT to every keyword: ""bedroom ceiling"" not ""ceiling""
4. primaryKeywords: 2-3 exact visual matches with full context
5. moodKeywords: 2 emotional visuals (emotion + setting/visual)
6. contextualKeywords: 2 setting/atmosphere keywords
7. actionKeywords: 1-2 movement/activity keywords
8. fallbackKeywords: 2 safe, universal keywords that always return results

=== AVOID ===
- Single generic words: ""ceiling"", ""room"", ""person""
- Abstract concepts alone: ""sadness"", ""anxiety"", ""hope""
- Religious/sensitive content triggers: use ""bedroom ceiling"" not ""ceiling"", ""city skyline"" not ""dome""

=== MOOD → VISUAL MAPPING ===
MELANCHOLIC: rain window apartment, empty street night, fog city morning, wilting flower
ANXIOUS: clock ticking closeup, crowded subway, messy desk papers, insomnia bedroom
HOPEFUL: sunrise city skyline, light through window, birds flying sky, spring flowers
CALM: lake reflection sunset, candle dark room, coffee morning quiet, gentle waves
ENERGETIC: fast traffic city, sports action, crowd cheering, dancing silhouette

=== SAFE FALLBACK KEYWORDS ===
Always include 2 from: clouds timelapse, city skyline night, nature landscape, ocean waves, person silhouette window, rain drops glass, sunset horizon, forest path

=== PLATFORM CATEGORIES ===
Map script intent to: People, Nature, Urban, Business, Technology, Abstract, Lifestyle, Travel

=== INDONESIAN CONTEXT HANDLING ===
- ""langit-langit kamar"" → ""bedroom ceiling staring"", ""person lying bed looking up""
- ""kamar gelap"" → ""dark bedroom night"", ""dim room shadows""
- ""jendela kamar"" → ""bedroom window rain"", ""apartment window night""
- ""sepi/sedih"" → ""lonely night window"", ""empty room solitude""
- ""takut/cemas"" → ""anxiety dark room"", ""worried person thinking""

=== EXAMPLES ===

Input: ""Langit-langit kamar seolah menatap balik, mengingatkan pada daftar masalah.""
Output: {
  ""primaryKeywords"": [""person lying bed staring ceiling"", ""bedroom ceiling insomnia""],
  ""moodKeywords"": [""dark room anxiety thoughts"", ""overwhelmed person night""],
  ""contextualKeywords"": [""dim bedroom evening"", ""apartment room shadows""],
  ""actionKeywords"": [""lying still bed"", ""staring up ceiling""],
  ""fallbackKeywords"": [""clouds timelapse"", ""rain window night""],
  ""suggestedCategory"": ""People"",
  ""detectedMood"": ""anxious""
}

Input: ""Di luar, dunia berputar tanpa henti. Orang-orang sibuk dengan urusan masing-masing.""
Output: {
  ""primaryKeywords"": [""busy city crowd walking"", ""people timelapse street""],
  ""moodKeywords"": [""urban rush disconnected"", ""city life overwhelm""],
  ""contextualKeywords"": [""downtown pedestrians day"", ""subway station crowd""],
  ""actionKeywords"": [""walking fast crowd"", ""commuters rushing""],
  ""fallbackKeywords"": [""city skyline night"", ""traffic flow timelapse""],
  ""suggestedCategory"": ""Urban"",
  ""detectedMood"": ""energetic""
}

Input: ""But then, a small light appeared. Maybe tomorrow will be different.""
Output: {
  ""primaryKeywords"": [""light through window morning"", ""sunrise bedroom curtains""],
  ""moodKeywords"": [""hope new beginning dawn"", ""optimistic person window""],
  ""contextualKeywords"": [""sun rays room golden"", ""morning light indoor""],
  ""actionKeywords"": [""light breaking darkness"", ""opening curtains morning""],
  ""fallbackKeywords"": [""sunrise timelapse"", ""clouds parting sun""],
  ""suggestedCategory"": ""Nature"",
  ""detectedMood"": ""hopeful""
}";

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
                MaxTokens = 500 // Increased for layered output
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
                "Extracted {Count} keywords (P:{Primary} M:{Mood} C:{Context} A:{Action} F:{Fallback}) for segment in {Ms}ms. Category: {Category}, Mood: {DetectedMood}", 
                result.KeywordSet.TotalCount,
                result.KeywordSet.Primary.Count,
                result.KeywordSet.Mood.Count,
                result.KeywordSet.Contextual.Count,
                result.KeywordSet.Action.Count,
                result.KeywordSet.Fallback.Count,
                stopwatch.ElapsedMilliseconds,
                result.SuggestedCategory ?? "N/A",
                result.DetectedMood ?? "N/A");
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
                MaxTokens = Math.Min(sentenceList.Count * 300, 8000)
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
    public string Model { get; set; } = "gemini-2.5-flash";
    public string ApiKey { get; set; } = "sk-dummy";
    public int TimeoutSeconds { get; set; } = 30;
}

public class AuthSettings
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
}
