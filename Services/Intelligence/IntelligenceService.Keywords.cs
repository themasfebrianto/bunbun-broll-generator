using System.Diagnostics;
using System.Text.Json;
using BunbunBroll.Models;

namespace BunbunBroll.Services;

/// <summary>
/// Keyword extraction methods — single, batch flat, and batch layered.
/// </summary>
public partial class IntelligenceService
{
    private const string KeywordSystemPrompt = @"B-Roll keyword extractor for cinematic footage with NO HUMAN SUBJECTS. Convert script to English stock footage keywords.

OUTPUT (JSON only, no markdown):
{
  ""primaryKeywords"": [""2-3 word visual match"", ""subject + setting""],
  ""moodKeywords"": [""emotion + visual""],
  ""contextualKeywords"": [""setting + time""],
  ""actionKeywords"": [""movement""],
  ""fallbackKeywords"": [""clouds timelapse"", ""mountain sunrise""],
  ""suggestedCategory"": ""Nature|Abstract|Urban"",
  ""detectedMood"": ""melancholic|anxious|hopeful|calm|energetic""
}

RULES:
- Translate ALL to English
- 2-3 words per keyword (not single words)
- ABSOLUTE RULE: NO PEOPLE, NO HUMAN FACES, NO HUMAN BODY PARTS, NO SILHOUETTES, NO HUMAN ACTIVITY, NO PERSON, NO HANDS, NO FEET, NO CROWDS, NO EYES.
- NO HUMAN-CENTRIC LABOR: No hands planting, no hands typing, no feet walking, no manual work being performed by people.
- ALSO AVOID HUMAN-ADJACENT TERMS that return human footage from stock sites: mirror, reflection, shadow, window, doorway, selfie, walking, standing, sitting, running, praying, crying, laughing, embrace, handshake, footsteps, footprint, tools, phone, computer, keyboard.
  Instead use nature/urban metaphors: 'broken mirror' -> 'cracked earth texture', 'reflection' -> 'water surface reflection', 'shadow' -> 'dark clouds moving', 'walking' -> 'path through forest', 'manual labor' -> 'nature's cycle', 'planting' -> 'green seedling macro timelapse'
- Avoid religious/sensitive triggers
- fallbackKeywords: always include safe universals like ""ocean waves"", ""sunset clouds"", ""mountain landscape""

ERA-BASED VISUAL CONTEXT (CRITICAL):
- When the script describes stories of PROPHETS, ANCIENT TIMES, or HISTORICAL ERAS:
  Use NATURE-BASED visuals ONLY: desert landscape, sand dunes, ancient ruins without people, mountain range, vast sky, barren land, rocky terrain, oasis, starry desert night, forest canopy, calm sea, sunrise horizon, windswept plains
- When the script shifts to MODERN TIMES or CONTEMPORARY topics:
  Use URBAN visuals: cityscape, modern buildings, skyline, highway, infrastructure, modern architecture, glass tower, urban street empty, traffic lights, bridge structure, aerial city view
- NEVER include any human presence regardless of era

VISUAL STYLE GUIDELINES (apply to ALL keyword layers when style is specified):
- Cinematic: Use keywords like ""cinematic lighting"", ""film grain"", ""dramatic shadows"", ""slow motion"", ""wide shot"", ""depth of field"", ""golden hour"", ""lens flare"", ""movie scene""
- Moody: Use keywords like ""dark atmosphere"", ""low key lighting"", ""shadows"", ""moody colors"", ""dim lighting"", ""dramatic contrast"", ""night scene"", ""film noir""
- Bright: Use keywords like ""bright lighting"", ""vibrant colors"", ""sunny day"", ""high key"", ""natural light"", ""clean background"", ""cheerful"", ""daylight""
- Auto: No specific style modifiers, let content dictate

EMOTIONAL MOOD DETECTION (separate from visual style):
melancholic=rain window, anxious=storm clouds, hopeful=sunrise, calm=placid lake, energetic=crashing waves

IMPORTANT: When user specifies a Visual Style, weave those terms into PRIMARY, MOOD, and CONTEXTUAL keywords. Use high-end cinematic terms. Match era context (nature for ancient, urban for modern).";

    public async Task<KeywordResult> ExtractKeywordsAsync(string text, string? mood = null, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new KeywordResult();

        try
        {
            var userPrompt = mood != null 
                ? $"Mood/Style: {mood}\n\nSegment: {text}" 
                : $"Segment: {text}";

            var (rawContent, tokensUsed) = await SendChatAsync(
                KeywordSystemPrompt, userPrompt,
                temperature: 0.3, maxTokens: 300,
                cancellationToken: cancellationToken);

            result.RawResponse = rawContent;
            result.TokensUsed = tokensUsed;

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

            var (rawContent, _) = await SendChatAsync(
                KeywordSystemPrompt, batchPrompt.ToString(),
                temperature: 0.3,
                maxTokens: Math.Min(sentenceList.Count * 100, 4000),
                cancellationToken: cancellationToken);

            _logger.LogDebug("Batch extracting keywords for {Count} sentences", sentenceList.Count);

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

            var (rawContent, _) = await SendChatAsync(
                KeywordSystemPrompt, batchPrompt.ToString(),
                temperature: 0.3,
                maxTokens: Math.Min(sentenceList.Count * 200, 6000),
                cancellationToken: cancellationToken);

            _logger.LogDebug("Batch extracting layered keywords for {Count} sentences", sentenceList.Count);

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
}
