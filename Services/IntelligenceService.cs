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
    /// </summary>
    Task<Dictionary<int, List<string>>> ExtractKeywordsBatchAsync(
        IEnumerable<(int Id, string Text)> sentences, 
        string? mood = null, 
        CancellationToken cancellationToken = default);
}

public class IntelligenceService : IIntelligenceService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<IntelligenceService> _logger;
    private readonly GeminiSettings _settings;

    private const string SystemPrompt = @"You are a CREATIVE B-Roll keyword extraction assistant for video editors. Your job is to convert video script sentences into CINEMATIC English search keywords for stock footage.

CRITICAL RULES:
1. Output ONLY a valid JSON array of strings. No explanation, no markdown.
2. Keywords must be in English (translate Indonesian).
3. Generate 5-7 keywords for maximum search coverage.
4. Be CREATIVE - think like a cinematographer.
5. ALWAYS add CONTEXT to keywords - never use single generic words!

IMPORTANT - KEYWORD SPECIFICITY:
- NEVER use single words like ""ceiling"", ""room"", ""window"" alone
- ALWAYS add context: ""bedroom ceiling"", ""dark room person"", ""rain window apartment""
- WRONG: ""ceiling"", ""architecture"" (too generic, might return mosque/temple footage)
- RIGHT: ""bedroom ceiling staring"", ""apartment room dark"", ""home interior night""

AVOID KEYWORDS THAT MIGHT RETURN RELIGIOUS CONTENT:
- Instead of ""ceiling"" → use ""bedroom ceiling"", ""apartment ceiling""
- Instead of ""dome"" → use ""city skyline"", ""building architecture""
- Instead of ""architecture"" → use ""modern building"", ""apartment interior""
- Add context words: ""home"", ""apartment"", ""bedroom"", ""office"", ""city""

KEYWORD STRATEGY (in order of priority):
1. PRIMARY: Exact visual match WITH CONTEXT (""person lying bedroom"" not just ""lying"")
2. MOOD: Emotional visuals with setting (""dark room anxiety"" not just ""anxiety"")
3. CINEMATIC: Beautiful generic shots (""city skyline night"", ""nature landscape"")
4. ABSTRACT: Safe symbolic visuals (""rain drops"", ""clock ticking"", ""clouds moving"")

MOOD-BASED CREATIVE KEYWORDS:
- MELANCHOLIC: ""rain window apartment"", ""empty street night"", ""fog city morning"", ""lonely person silhouette""
- STRESSFUL: ""overwhelmed person desk"", ""clock ticking stress"", ""messy room papers"", ""insomnia bedroom night""
- HOPEFUL: ""sunrise city"", ""light through window"", ""person looking horizon nature""
- CALM: ""calm lake nature"", ""candle flame dark room"", ""peaceful bedroom morning""

SAFE FALLBACK KEYWORDS (use 1-2 of these):
- ""clouds timelapse"", ""rain window"", ""city lights night"", ""person silhouette window""
- ""coffee morning mood"", ""dark room candle"", ""slow motion walking city""

HANDLING INDONESIAN BEDROOM/ROOM CONTEXT:
- ""langit-langit kamar"" → ""bedroom ceiling staring"", ""person lying bed looking up"", ""insomnia ceiling thoughts""
- ""kamar gelap"" → ""dark bedroom"", ""dim room night"", ""bedroom shadows""
- ""jendela kamar"" → ""bedroom window rain"", ""apartment window night""

EXAMPLES:
Input: ""Langit-langit kamar seolah menatap balik, mengingatkan pada daftar masalah.""
Output: [""person lying bed staring ceiling"", ""bedroom ceiling insomnia"", ""dark room thoughts"", ""overwhelmed person bed"", ""dim bedroom night"", ""apartment room anxiety"", ""sleepless night bedroom""]

Input: ""Kadang, membuka mata di pagi hari terasa sebagai beban terberat.""
Output: [""tired waking up bed"", ""person lying bedroom morning"", ""gloomy room waking"", ""slow motion morning bedroom"", ""alarm clock tired"", ""reluctant morning person"", ""dim bedroom sunrise""]

Input: ""Di luar, dunia berputar tanpa henti. Orang-orang sibuk dengan urusan masing-masing.""
Output: [""busy city crowd walking"", ""people timelapse street"", ""urban rush hour"", ""city skyline busy"", ""subway crowd commute"", ""fast motion city traffic"", ""office workers walking""]

Input: ""Malam itu terasa sangat panjang dan sepi.""
Output: [""lonely night bedroom"", ""empty street night city"", ""window rain night apartment"", ""insomnia person bed"", ""city lights night lonely"", ""candle dark room"", ""sleepless night window""]";

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
                MaxTokens = 200
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
                // Clean the response (remove markdown code blocks if present)
                var cleanedJson = CleanJsonResponse(rawContent);
                
                _logger.LogDebug("Raw AI response: {Raw}", rawContent);
                _logger.LogDebug("Cleaned JSON: {Cleaned}", cleanedJson);
                
                try
                {
                    var keywords = JsonSerializer.Deserialize<List<string>>(cleanedJson);
                    result.Keywords = keywords ?? new List<string>();
                    result.Success = result.Keywords.Count > 0;
                }
                catch (JsonException parseEx)
                {
                    _logger.LogWarning("JSON parse failed for: {Content}. Error: {Error}", cleanedJson, parseEx.Message);
                    // Try to extract keywords from non-JSON response
                    result.Keywords = ExtractKeywordsFromText(rawContent);
                    result.Success = result.Keywords.Count > 0;
                }
            }

            _logger.LogInformation("Extracted {Count} keywords for segment in {Ms}ms: [{Keywords}]", 
                result.Keywords.Count, stopwatch.ElapsedMilliseconds, string.Join(", ", result.Keywords));
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
