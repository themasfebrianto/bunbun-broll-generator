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
        ImagePromptConfig? config = null,
        Func<List<BrollPromptItem>, Task>? onBatchComplete = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Classify segments ONLY (BROLL vs IMAGE_GEN), without generating any prompts.
    /// Returns items with MediaType set but Prompt empty.
    /// </summary>
    Task<List<BrollPromptItem>> ClassifySegmentsOnlyAsync(
        List<(string Timestamp, string ScriptText)> segments,
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
        CancellationToken cancellationToken = default);

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
        CancellationToken cancellationToken = default);

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

    public IntelligenceService(
        HttpClient httpClient, 
        ILogger<IntelligenceService> logger, 
        IOptions<GeminiSettings> settings)
    {
        _httpClient = httpClient;
        _logger = logger;
        _settings = settings.Value;
    }

    // =============================================
    // SHARED: Unified LLM call
    // =============================================

    /// <summary>
    /// Unified LLM chat call — builds request, sends, parses response, returns content string.
    /// All methods should use this instead of duplicating HTTP + parse logic.
    /// </summary>
    private async Task<(string? Content, int TokensUsed)> SendChatAsync(
        string systemPrompt,
        string userPrompt,
        double temperature = 0.3,
        int maxTokens = 300,
        CancellationToken cancellationToken = default)
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

        var response = await _httpClient.PostAsJsonAsync(
            "v1/chat/completions",
            request,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var geminiResponse = await response.Content.ReadFromJsonAsync<GeminiChatResponse>(
            cancellationToken: cancellationToken);

        var content = geminiResponse?.Choices?.FirstOrDefault()?.Message?.Content;
        var tokens = geminiResponse?.Usage?.TotalTokens ?? 0;

        return (content, tokens);
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
        return new BrollPromptItem
        {
            Index = index,
            Timestamp = timestamp,
            ScriptText = scriptText,
            MediaType = BrollMediaType.BrollVideo,
            Prompt = defaultPrompt
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
        List<(string Timestamp, string ScriptText)> segments,
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
            ImageComposition.CinematicWide,
            ImageComposition.CloseUp,
            ImageComposition.LowAngle,
            ImageComposition.WideShot,
            ImageComposition.BirdsEye,
            ImageComposition.CloseUp
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
- NO ERA BLENDING: Each image must exist in ONE single time period. NEVER combine ancient and modern elements in the same scene (e.g. NO ancient scrolls next to computer monitors, NO clay pots in server rooms, NO castles behind modern screens). If the script mentions both eras, choose the PRIMARY era for this segment only.
- FULL BLEED: The image must fill the entire frame edge-to-edge. NO black bars, NO letterboxing, NO borders, NO cinematic bars at top/bottom or sides. The scene extends to all edges of the canvas.
- NO TEXT: The image must contain ZERO text, letters, words, numbers, captions, titles, watermarks, or any written content. Pure visual scene only.
- VISUAL VARIETY (CRITICAL): NEVER repeat the same primary subject as adjacent segments. Cycle through different visual categories: wide landscape/environment, architectural detail, object close-up, atmospheric/sky, interior space, natural element. If the previous segment showed an object (e.g. clay jar), this segment MUST show something different (e.g. wide cave interior, desert landscape, dramatic sky). Vary focal distance: alternate between wide shots, medium shots, and close-ups across consecutive segments.";

    private const string TEXT_OVERLAY_RULES = @"
TEXT OVERLAY DETECTION (SELECTIVE - USE SPARINGLY):
Only add textOverlay for HIGH-IMPACT moments. Segments with text overlays MUST use BROLL as background.

Overlay types (USE SPARINGLY — max ~20% of total segments):
- QURAN VERSES: ONLY explicit Quranic ayat quotations → type: ""QuranVerse"", include arabic text + translation + surah reference
- HADITH: ONLY explicit Prophet's sayings with known source → type: ""Hadith"", include arabic + translation + source
- RHETORICAL QUESTIONS: ONLY powerful, pivotal questions that define the video's thesis → type: ""RhetoricalQuestion""
- KEY DECLARATIONS: ONLY short, punchy declarations (max 8 words) that are a direct claim or thesis statement — NOT definitions, descriptions, or explanations. Example YES: ""Imam Mahdi akan muncul di akhir zaman"". Example NO: ""Gelar metaforis bagi raja yang adil..."" (that's a definition, not a declaration).

SPACING RULES (CRITICAL):
- NEVER place textOverlay on 2 consecutive segments. Minimum 2-3 segments gap between overlays.
- If two potential overlays are close, pick ONLY the stronger one.

EXCLUSIONS — NEVER add textOverlay for:
- Closing/farewell phrases (""Wallahu a'lam bish shawab"", ""Wassalamu'alaikum"", etc.)
- Opening greetings (""Assalamu'alaikum"", ""Bismillah"", etc.) unless it's a pivotal Quran verse
- General narration, transitions, storytelling, context, explanations
- Statements that are interesting but not scripture/hadith/pivotal thesis questions";

    private const string BROLL_NO_HUMAN_RULES = @"ABSOLUTE RULE: NO PEOPLE, NO HUMAN BODY PARTS, NO FACES, NO SILHOUETTES.
- Use NATURE or URBAN imagery depending on context.
- Avoid human-adjacent terms (walking, praying, hands, shadows).
- Examples: 'storm clouds timelapse', 'desert sand dunes', 'modern city skyline', 'flowing river', 'ancient ruins'.";
}
