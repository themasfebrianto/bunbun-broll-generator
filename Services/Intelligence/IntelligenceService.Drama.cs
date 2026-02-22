using System.Text.Json;
using BunbunBroll.Models;

namespace BunbunBroll.Services;

public partial class IntelligenceService
{
    public async Task<DramaDetectionResult> DetectDramaAsync(
        IEnumerable<(int Index, string Text)> entries,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = new DramaDetectionResult { IsSuccess = false };

        try
        {
            var entryList = entries.ToList();
            if (entryList.Count == 0)
            {
                result.ErrorMessage = "No entries provided for drama detection";
                return result;
            }

            // Build entries text for LLM
            var entriesText = new System.Text.StringBuilder();
            foreach (var (index, text) in entryList)
            {
                entriesText.AppendLine($"[{index}]: {text}");
            }

            var systemPrompt = @"You are a expert video editor and storyteller specializing in dramatic timing for Indonesian documentary/narrative content.

Your task is to analyze script entries and identify:
1. DRAMA PAUSES: Moments that need strategic silence for emotional impact
3. TEXT OVERLAYS: Content that should appear as on-screen text (NOTE: Overlays are now handled by Regex, but you should still consider their context if mentioned)

DRAMA PAUSE RULES (LONG VIDEO PACING):
- You MUST act as a Paragraph Segmentation Analyzer.
- Group the script entries into logical paragraphs or distinct thought sections.
- Add pauses ONLY at the END of a logical paragraph or when the topic significantly changes.
- DO NOT place pauses in the middle of sentences, continuous thoughts, or between dependent clauses.
- Pause durations: 
  - 1.5s for standard paragraph breaks.
  - 2.0s to 2.5s MAX for major chapter transitions or deep narrative shifts.
- NOT every entry needs a pause - be highly selective, aim for natural breathing room between large blocks of text.

Return ONLY valid JSON in this exact format:
{
  ""pauseDurations"": {
    ""7"": 1.5,
    ""12"": 2.0
  }
}";

            var userPrompt = $@"Analyze these script entries for drama pauses and text overlays:

{entriesText}

Return JSON with pauseDurations and textOverlays.";

            var llmResult = await SendChatAsync(
                systemPrompt,
                userPrompt,
                temperature: 0.3,
                maxTokens: 1500,
                cancellationToken
            );

            if (string.IsNullOrWhiteSpace(llmResult.Content))
            {
                result.ErrorMessage = "LLM returned empty response";
                return result;
            }

            // Cleanup LLM JSON
            var cleanJson = CleanJsonResponse(llmResult.Content);

            // Parse JSON response
            var jsonDoc = JsonDocument.Parse(cleanJson);
            var root = jsonDoc.RootElement;

            // Parse pauses
            if (root.TryGetProperty("pauseDurations", out var pausesElem))
            {
                foreach (var prop in pausesElem.EnumerateObject())
                {
                    if (int.TryParse(prop.Name, out int index) && prop.Value.TryGetDouble(out double seconds))
                    {
                        result.PauseDurations[index] = seconds;
                    }
                }
            }

            result.IsSuccess = true;
            result.TokensUsed = llmResult.TokensUsed;
            result.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;

            _logger.LogInformation(
                "Drama detection complete: {PauseCount} pauses, {Tokens} tokens, {Ms}ms",
                result.PauseDurations.Count,
                result.TokensUsed,
                result.ProcessingTimeMs
            );

            return result;
        }
        catch (JsonException ex)
        {
            result.ErrorMessage = $"Failed to parse LLM JSON response: {ex.Message}";
            _logger.LogError(ex, "LLM JSON parsing failed");
            return result;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = $"Drama detection failed: {ex.Message}";
            _logger.LogError(ex, "Drama detection error");
            return result;
        }
    }
}
