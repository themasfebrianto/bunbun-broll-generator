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
    /// Detect drama pauses and text overlays in script entries using LLM.
    /// Analyzes narrative flow for dramatic moments (contrasts, revelations, suspense)
    /// and identifies overlay-worthy content (Quran verses, key phrases, questions).
    /// </summary>
    Task<DramaDetectionResult> DetectDramaAsync(
        IEnumerable<(int Index, string Text)> entries,
        CancellationToken cancellationToken = default);
    
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
        List<(string Timestamp, string ScriptText, TextOverlay? Overlay)> segments,
        string topic,
        ImagePromptConfig? config = null,
        Func<List<BrollPromptItem>, Task>? onBatchComplete = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Classify segments ONLY (BROLL vs IMAGE_GEN), without generating any prompts.
    /// Returns items with MediaType set but Prompt empty.
    /// </summary>
    Task<List<BrollPromptItem>> ClassifySegmentsOnlyAsync(
        List<(string Timestamp, string ScriptText, TextOverlay? Overlay)> segments,
        string topic,
        ImagePromptConfig? config = null,
        Func<List<BrollPromptItem>, Task>? onBatchComplete = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate prompts in batch for segments of a specific media type.
    /// For IMAGE_GEN: generates detailed Whisk image prompts using config.
    /// For BROLL: generates concise search keywords.
    /// </summary>
    Task GeneratePromptsForTypeBatchAsync(
        List<BrollPromptItem> items,
        BrollMediaType targetType,
        string topic,
        ImagePromptConfig? config = null,
        Func<int, Task>? onProgress = null,
        CancellationToken cancellationToken = default,
        bool resumeOnly = false);

    /// <summary>
    /// Forces generation of a specific prompt type (B-Roll or Image Gen) for a given segment.
    /// This avoids re-classification errors during regeneration.
    /// </summary>
    Task<string> GeneratePromptForTypeAsync(
        string scriptText,
        BrollMediaType mediaType,
        string topic,
        ImagePromptConfig? config = null,
        CancellationToken cancellationToken = default,
        int segmentIndex = 0);

    /// <summary>
    /// Extract global storytelling context from the full script.
    /// Analyzes all segments to identify: locations, characters, era progression, mood beats with visual settings.
    /// </summary>
    Task<GlobalScriptContext?> ExtractGlobalContextAsync(
        List<BrollPromptItem> segments,
        string topic,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate prompts in batch with global narrative context.
    /// Each prompt uses: global context + sliding window of adjacent segments.
    /// </summary>
    Task GeneratePromptsWithContextAsync(
        List<BrollPromptItem> items,
        BrollMediaType targetType,
        string topic,
        GlobalScriptContext globalContext,
        ImagePromptConfig? config = null,
        Func<int, Task>? onProgress = null,
        int windowSize = 2,
        CancellationToken cancellationToken = default,
        bool resumeOnly = false);

    /// <summary>
    /// Generate a single prompt with context awareness.
    /// Uses global context + sliding window for narrative consistency.
    /// </summary>
    Task<string> GeneratePromptWithContextAsync(
        BrollPromptItem currentItem,
        List<BrollPromptItem> allItems,
        string topic,
        GlobalScriptContext globalContext,
        ImagePromptConfig? config = null,
        int windowSize = 2,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Intelligence Service — Core infrastructure (constructor, shared LLM call, helpers, constants).
/// Partial class: see also Classification, Keywords, PromptGeneration, ContextAware files.
/// </summary>
public partial class IntelligenceService : IIntelligenceService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<IntelligenceService> _logger;
    private readonly GeminiSettings _settings;
    private readonly ILlmSelectorService _selector;

    public IntelligenceService(
        HttpClient httpClient, 
        ILogger<IntelligenceService> logger, 
        IOptions<GeminiSettings> settings,
        ILlmSelectorService selector)
    {
        _httpClient = httpClient;
        _logger = logger;
        _settings = settings.Value;
        _selector = selector;
    }

    // =============================================
    // SHARED: Unified LLM call
    // =============================================

    /// <summary>
    /// Unified LLM chat call — uses LlmRouterService to select the best model, handle rate limits, and fallback on failure.
    /// </summary>
    private async Task<(string? Content, int TokensUsed)> SendChatAsync(
        string systemPrompt,
        string userPrompt,
        double temperature = 0.3,
        int maxTokens = 300,
        CancellationToken cancellationToken = default,
        bool requiresHighReasoning = true) // Parameter kept for backwards compatibility but ignored
    {
        const int maxRetries = 3;
        int attempt = 0;

        while (attempt < maxRetries)
        {
            attempt++;
            var modelId = _selector.CurrentModel;

            var request = new GeminiChatRequest
            {
                Model = modelId,
                Messages = new List<GeminiMessage>
                {
                    new() { Role = "system", Content = systemPrompt },
                    new() { Role = "user", Content = userPrompt }
                },
                Temperature = temperature,
                MaxTokens = maxTokens
            };

            try
            {
                var response = await _httpClient.PostAsJsonAsync(
                    "v1/chat/completions",
                    request,
                    cancellationToken);

                // Handle rate limits or specific API errors
                if (!response.IsSuccessStatusCode)
                {
                    var statusCode = (int)response.StatusCode;
                    var errorContent = await response.Content.ReadAsStringAsync();
                    
                    _logger.LogWarning($"LLM Error {statusCode} using {modelId}: {errorContent}");
                    
                    // 429 Too Many Requests, 500 Server Error, or "insufficient quota" -> simple backoff retry
                    if (statusCode == 429 || statusCode >= 500 || errorContent.Contains("quota", StringComparison.OrdinalIgnoreCase))
                    {
                        if (attempt < maxRetries)
                        {
                            var delaySeconds = (int)Math.Pow(2, attempt);
                            _logger.LogInformation($"Retrying in {delaySeconds}s...");
                            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
                        }
                        continue;
                    }

                    response.EnsureSuccessStatusCode(); // Force throw if it's a client bad request (e.g., 400)
                }

                var geminiResponse = await response.Content.ReadFromJsonAsync<GeminiChatResponse>(cancellationToken: cancellationToken);

                var content = geminiResponse?.Choices?.FirstOrDefault()?.Message?.Content;
                var tokens = geminiResponse?.Usage?.TotalTokens ?? 0;

                if (string.IsNullOrWhiteSpace(content))
                {
                    _logger.LogWarning($"LLM {modelId} returned an empty response.");
                    if (attempt < maxRetries)
                    {
                        await Task.Delay(1000, cancellationToken);
                    }
                    continue;
                }

                return (content, tokens);
            }
            catch (HttpRequestException ex) when (attempt < maxRetries)
            {
                _logger.LogWarning($"Network error communicating with {modelId}: {ex.Message}. Retrying...");
                await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < maxRetries)
            {
                _logger.LogWarning($"Timeout communicating with {modelId}. Retrying...");
                await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken);
            }
        }

        // Exhausted retries
        throw new Exception($"Failed to complete LLM request after {maxRetries} attempts manually using model: {_selector.CurrentModel}.");
    }

    // =============================================
    // SHARED: JSON cleaning
    // =============================================

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
        
        raw = raw.Trim();

        // --- LLM artifact repair ---
        // Remove stray `-` before `}` or `]` (e.g.  "-}" → "}")
        raw = System.Text.RegularExpressions.Regex.Replace(raw, @"-\s*}", "}");
        raw = System.Text.RegularExpressions.Regex.Replace(raw, @"-\s*]", "]");
        // Remove trailing commas before `}` or `]`  (e.g. ",}" → "}")
        raw = System.Text.RegularExpressions.Regex.Replace(raw, @",\s*}", "}");
        raw = System.Text.RegularExpressions.Regex.Replace(raw, @",\s*]", "]");

        // Ensure the response is a JSON array — if truncated, try to close it
        if (raw.StartsWith("[") && !raw.EndsWith("]"))
        {
            // Find last complete object "}," or "}" and close array
            var lastBrace = raw.LastIndexOf('}');
            if (lastBrace > 0)
            {
                raw = raw[..(lastBrace + 1)] + "]";
            }
        }

        return raw;
    }

    // =============================================
    // SHARED: Text overlay parsing
    // =============================================

    /// <summary>
    /// Parse a TextOverlayDto from LLM response into a Models.TextOverlay.
    /// Returns null if dto is null or has no text. Auto-logs on parse failure.
    /// </summary>
    private Models.TextOverlay? ParseTextOverlay(TextOverlayDto? dto, int globalIdx)
    {
        if (dto == null || string.IsNullOrEmpty(dto.Text))
            return null;

        try
        {
            var overlayType = dto.Type?.ToLowerInvariant()?.Replace("_", "") switch
            {
                "quranverse" => Models.TextOverlayType.QuranVerse,
                "hadith" => Models.TextOverlayType.Hadith,
                "rhetoricalquestion" => Models.TextOverlayType.RhetoricalQuestion,
                "keyphrase" => Models.TextOverlayType.KeyPhrase,
                _ => Models.TextOverlayType.KeyPhrase
            };

            var overlay = new Models.TextOverlay
            {
                Type = overlayType,
                Text = dto.Text,
                ArabicText = dto.Arabic,
                Reference = dto.Reference,
                Style = overlayType switch
                {
                    Models.TextOverlayType.QuranVerse => Models.TextStyle.Quran,
                    Models.TextOverlayType.Hadith => Models.TextStyle.Hadith,
                    Models.TextOverlayType.RhetoricalQuestion => Models.TextStyle.Question,
                    _ => Models.TextStyle.Default
                }
            };

            _logger.LogDebug("Text overlay detected for segment {Index}: {Type} — {Text}",
                globalIdx, overlayType, dto.Text);

            return overlay;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse text overlay for segment {Index}", globalIdx);
            return null;
        }
    }

    // =============================================
    // SHARED: Fallback item creation
    // =============================================

    /// <summary>
    /// Create a fallback BrollPromptItem with default BrollVideo type.
    /// </summary>
    private static BrollPromptItem CreateFallbackItem(
        int index, string timestamp, string scriptText, string defaultPrompt = "")
    {
        var words = scriptText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var duration = Math.Max(3.0, words / 2.5);

        return new BrollPromptItem
        {
            Index = index,
            Timestamp = timestamp,
            ScriptText = scriptText,
            MediaType = BrollMediaType.BrollVideo,
            Prompt = defaultPrompt,
            EstimatedDurationSeconds = duration
        };
    }

    // =============================================
    // SHARED: Batch processing helpers
    // =============================================

    /// <summary>
    /// Notify batch callback with a thread-safe snapshot of results.
    /// </summary>
    private static async Task NotifyBatchComplete(
        Func<List<BrollPromptItem>, Task>? onBatchComplete,
        List<BrollPromptItem> results,
        object resultsLock)
    {
        if (onBatchComplete == null) return;

        List<BrollPromptItem> snapshot;
        lock (resultsLock)
        {
            snapshot = results.OrderBy(r => r.Index).ToList();
        }
        await onBatchComplete(snapshot);
    }

    /// <summary>
    /// Fill missing segment indices with fallback items.
    /// </summary>
    private static void FillMissingSegments(
        List<BrollPromptItem> results,
        List<(string Timestamp, string ScriptText, TextOverlay? Overlay)> segments,
        string defaultPrompt = "")
    {
        for (int i = 0; i < segments.Count; i++)
        {
            if (!results.Any(r => r.Index == i))
            {
                results.Add(CreateFallbackItem(i, segments[i].Timestamp, segments[i].ScriptText, defaultPrompt));
            }
        }
    }

    // =============================================
    // SHARED: Dynamic composition rotation
    // =============================================

    private ImageComposition GetDynamicComposition(int index)
    {
        var sequence = new[] 
        {
            ImageComposition.UltraWideEstablishing,
            ImageComposition.GroundLevelWide,
            ImageComposition.LowAngleHero,
            ImageComposition.CloseUpEnvironmental,
            ImageComposition.HighAngleTopDown,
            ImageComposition.OverTheShoulder,
            ImageComposition.DynamicAction,
            ImageComposition.CinematicSilhouette,
            ImageComposition.InteriorPerspective,
            ImageComposition.DistantHorizon
        };
        return sequence[index % sequence.Length];
    }

    // =============================================
    // SHARED: Prompt constants
    // =============================================

    private const string IMAGE_GEN_COMPOSITION_RULES = @"
COMPOSITION RULES (CRITICAL - MUST FOLLOW):
- Generate ONE single unified scene. NEVER create split-screen, side-by-side, before/after, or montage compositions.
- The image must depict ONE moment, ONE location, ONE continuous scene.
- Do NOT use words like 'split', 'divided', 'left side / right side', 'juxtapose', 'contrast between two scenes', 'half and half'.
- If comparing eras, pick ONE era per image, not both.
- NO ERA BLENDING: Each image must exist in ONE single time period. NEVER combine ancient and modern elements in the same scene.
- FULL BLEED: The image must fill the entire frame edge-to-edge. NO black bars, NO letterboxing.
- NO TEXT: Pure visual scene only.
- VISUAL VARIETY (CRITICAL): NEVER repeat the same primary subject as adjacent segments. Cycle through different visual categories: wide landscape/environment, architectural detail, object close-up, atmospheric/sky, interior space, natural element. If the previous segment showed an object (e.g. clay jar), this segment MUST show something different (e.g. wide cave interior, desert landscape, dramatic sky).
- CAMERA ANGLE PROVIDED: The composition/angle for this shot will be locked via a style suffix. Just describe the scene that fits the scene.

REALISM VS SURREALISM (STRICT — CONTEXT-DEPENDENT):
- CONCRETE/HISTORICAL segments (people, events, places, battles, journeys, dialogues, narrating what happened): MUST be REALISTIC and PHYSICALLY GROUNDED like a professional documentary or historical film still.
  * NO surreal imagery: NO giant heads/figures emerging from ground, NO ghostly/translucent/phantom/spectral objects, NO impossible physics, NO dream-like distortions, NO symbolic objects unnaturally placed (e.g. hourglasses in desert, bones as decoration, floating chains).
  * For METAPHORICAL script language (e.g. 'chains of the mind', 'psychological prison', 'shackles of fear'): depict CONCRETE REALISTIC scenes. Use body language, facial expressions, posture, and environment to convey emotion — NOT surreal visual metaphors. Example: 'mental chains' → a man sitting alone head-bowed in empty desert. 'Freedom without direction' → a crowd standing still in open wilderness.
- ABSTRACT/PHILOSOPHICAL segments (pure existential reflection, metaphysical ideas with NO specific people/place/event): Surreal and symbolic imagery IS ALLOWED.
- SUPERNATURAL QURANIC EVENTS (Red Sea parting, manna from sky, etc.): depict as scripture describes but keep environment realistic. Do NOT add extra fantastical elements beyond what scripture states.
- Divine light on prophets' faces is ALWAYS required per Islamic rules.

PROMPT DISCIPLINE (STRICT — PROFESSIONAL QUALITY):
- CONCISE PROMPTS: Keep total prompt length between 80-150 words. Do NOT write essay-length prompts. Excessive length confuses the image generator and produces incoherent outputs.
- EARLY COMPOSITION CUES: Put important composition cues (e.g. 'ultra-wide cinematic shot') at the BEGINNING. Image models prioritize early tokens.
- CONCRETE VISUALS: Do NOT use abstract narrative concepts (e.g. 'prophetic confrontation'). Use specific visual cues ('lone robed prophet holding a staff at front of crowd').
- ATMOSPHERIC PERSPECTIVE: To make massive scale feel real, add small physical cues (e.g. 'mist between water walls', 'birds circling above', 'foam detail at base', 'small figures against massive scale').
- SINGLE AESTHETIC: Do NOT mix conflicting styles (e.g. 'semi-realistic painting' with '8k photoreal'). Pick ONE dominant visual style.
- NO REDUNDANT ADJECTIVES: Do NOT stack synonyms or near-synonyms. Write 'amber light' NOT 'warm amber golden bronze terracotta ochre burnt sienna sunlight'. Pick ONE or TWO color descriptors, not six.
- NO CONTRADICTORY DESCRIPTIONS: Do NOT describe both 'harsh dramatic light' and 'soft diffused ambient glow' in the same prompt. Pick ONE lighting mood.
- SIMPLE STRUCTURE: Follow this order exactly: [1] Camera angle + main subject [2] Key action or state [3] Environment/setting [4] ONE lighting description [5] ONE-TWO color tones [6] Mood in 2-3 words.
- PROFESSIONAL REFERENCE: Think like a film director giving a single clear shot description to a cinematographer, NOT like a poet writing flowery prose.
- AVOID THESE WORDS: 'ethereal', 'ghostly', 'phantom', 'spectral', 'translucent chains', 'impossible', 'supernatural glow' (except for prophet divine light), 'dream-like', 'otherworldly', 'cosmic', 'cathedral-like', 'frozen in time'.";

    private const string BROLL_NO_HUMAN_RULES = @"ABSOLUTE RULE: NO PEOPLE, NO HUMAN BODY PARTS, NO FACES, NO SILHOUETTES.
- Use NATURE or URBAN imagery depending on context.
- Avoid human-adjacent terms (walking, praying, hands, shadows).
- Examples: 'storm clouds timelapse', 'desert sand dunes', 'modern city skyline', 'flowing river', 'ancient ruins'.";
}
