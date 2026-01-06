using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BunBunBroll.Models;
using Microsoft.Extensions.Options;

namespace BunBunBroll.Services;

/// <summary>
/// Intelligence Layer - Interfaces with Local Gemini LLM for keyword extraction.
/// </summary>
public interface IIntelligenceService
{
    Task<KeywordResult> ExtractKeywordsAsync(string text, string? mood = null, CancellationToken cancellationToken = default);
}

public class IntelligenceService : IIntelligenceService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<IntelligenceService> _logger;
    private readonly GeminiSettings _settings;

    private const string SystemPrompt = @"You are a B-Roll keyword extraction assistant for video editors. Your job is to convert video script segments (often in Indonesian) into optimized English search keywords for stock footage sites like Pexels.

CORE RULES:
1. Output ONLY a valid JSON array of strings. No explanation, no markdown, no extra text.
2. Keywords must be in English (translate Indonesian to English).
3. Generate 4-6 keywords per segment for better search coverage.
4. Keywords should be VISUAL and FILMABLE - things a camera can actually capture.
5. Use action-based keywords: ""person doing X"", ""hands typing"", ""man walking"".

HANDLING ABSTRACT/EMOTIONAL TEXT:
- Translate metaphors into concrete visuals:
  * ""langit-langit menatap balik"" (ceiling staring back) → ""person lying bed looking ceiling"", ""tired morning""
  * ""beban terberat"" (heaviest burden) → ""exhausted person"", ""tired waking up""
  * ""suara tertahan"" (voice stuck) → ""frustrated person"", ""silent scream""
- Convert emotions to body language:
  * Sadness → ""person sitting alone"", ""dark room silhouette"", ""head in hands""
  * Hope → ""sunlight through window"", ""person looking up"", ""sunrise""
  * Stress → ""person rubbing temples"", ""messy desk"", ""clock ticking""
  * Relief → ""deep breath"", ""person smiling"", ""weight lifted""

CONTEXT DETECTION:
- Office/Tech: ""coding screen"", ""laptop keyboard"", ""office late night""
- Urban: ""city crowd"", ""traffic jam"", ""subway station""
- Nature: ""storm clouds"", ""calm ocean"", ""sunrise mountain""
- Personal: ""bedroom morning"", ""coffee alone"", ""window rain""

EXAMPLES:
Input: ""Kadang, membuka mata di pagi hari terasa sebagai beban terberat. Langit-langit kamar seolah menatap balik.""
Output: [""tired person waking up"", ""morning bed ceiling"", ""gloomy bedroom"", ""exhausted man morning"", ""lying in bed staring""]

Input: ""Berjam-jam menatap layar monitor yang menyilaukan. Baris demi baris kode.""
Output: [""programmer coding night"", ""computer screen glow"", ""tired developer"", ""typing keyboard"", ""office late hours""]

Input: ""Namun, di sela-sela keputusasaan, ada satu tarikan napas panjang.""
Output: [""deep breath relief"", ""person closing eyes"", ""moment of calm"", ""sunlight hope"", ""peaceful exhale""]";

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
