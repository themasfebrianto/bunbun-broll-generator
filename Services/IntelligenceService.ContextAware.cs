using System.Diagnostics;
using System.Text.Json;
using BunbunBroll.Models;

namespace BunbunBroll.Services;

/// <summary>
/// Context-aware generation — Pass 1 (global context extraction) + Pass 2 (context-enriched prompts).
/// </summary>
public partial class IntelligenceService
{
    private const string GlobalContextExtractionPrompt = @"
You are a visual narrative analyzer for Islamic video essays.
Analyze the ENTIRE script and extract GLOBAL storytelling context.

TASK: Extract persistent elements that span across ALL segments.

OUTPUT (JSON only, no markdown):
{
  ""primaryLocations"": [""Qumran limestone caves"", ""Judean Desert"", ""ancient Jerusalem""],
  ""identifiedCharacters"": [
    {""name"": ""Essene ascetic community"", ""description"": ""male monks in simple robes""},
    {""name"": ""Prophet Isa"", ""description"": ""face hidden by divine light""}
  ],
  ""eraTimeline"": [
    {""startSegment"": 0, ""endSegment"": 45, ""era"": ""Late Ancient Roman Empire"", ""description"": ""civilization decline""},
    {""startSegment"": 18, ""endSegment"": 20, ""era"": ""21st Century Modern"", ""description"": ""scientific analysis""},
    {""startSegment"": 40, ""era"": ""Islamic End Times"", ""description"": ""apocalyptic atmosphere""}
  ],
  ""moodBeats"": [
    {
      ""startSegment"": 0,
      ""endSegment"": 25,
      ""mood"": ""mysterious"",
      ""description"": ""contemplative discovery, ancient secrets, quiet wonder"",
      ""visualKeywords"": [""candlelight"", ""dust particles"", ""shadows""],
      ""suggestedLighting"": ""SoftAmbient"",
      ""suggestedPalette"": ""WarmEarthy"",
      ""suggestedAngle"": ""CloseUp"",
      ""visualRationale"": ""Intimate cave scenes with candlelight work best with close-up framing and soft warm lighting""
    },
    {
      ""startSegment"": 25,
      ""endSegment"": 50,
      ""mood"": ""tense"",
      ""description"": ""apocalyptic anticipation, war preparations, divine reckoning"",
      ""visualKeywords"": [""storm clouds"", ""dramatic lighting"", ""darkness""],
      ""suggestedLighting"": ""MoodyDark"",
      ""suggestedPalette"": ""Monochrome"",
      ""suggestedAngle"": ""WideShot"",
      ""visualRationale"": ""Epic war scenes require wide framing and dark dramatic lighting""
    }
  ],
  ""recurringVisuals"": [""clay jars"", ""parchment scrolls"", ""cave interiors"", ""divine light rays""],
  ""colorProgression"": ""Start with warm earthy tones (caves, scrolls), transition to dark dramatic (war), end with golden divine (climax)""
}

MOOD DETECTION RULES:
- Analyze EMOTIONAL ARC across the entire script, not just individual segments
- Look for tone shifts: mystery → tension → revelation → hope
- Consider Islamic storytelling structure: problem → struggle → divine solution
- Mood changes should align with NARRATIVE BEATS, not random
- Typically 4-6 mood beats for a 15-20 minute video
- Each mood should span 5-15+ segments (not every segment!)

VISUAL DECISION RULES (for suggestedLighting, suggestedPalette, suggestedAngle):
- LIGHTING: Match mood energy
  * Mysterious/contemplative → SoftAmbient or EtherealGlow
  * Tense/apocalyptic → MoodyDark or DramaticHighContrast
  * Hopeful/inspiring → GoldenHour
  * Epic/powerful → DramaticHighContrast

- PALETTE: Support emotional tone
  * Ancient/historical → WarmEarthy or GoldenDesert
  * Modern/tech → CoolBlue or VibrantFocalMuted
  * End times → Monochrome or MysticPurple
  * Nature → NaturalGreen

- ANGLE: Match subject scale
  * Intimate personal moments → CloseUp
  * Landscapes/environments → WideShot or CinematicWide
  * Powerful figures → LowAngle
  * Overview/expository → BirdsEye

ERA SELECTION (use these exact era names):
- Lost ancient civilization ruins era (for ancient times, mystery)
- Late Ancient Roman Empire era (for decline, fall of civilization)
- 6th century BC Ancient Babylon era (for ancient kings, mystery)
- 6th century Pre-Islamic Arabia era (for jahiliyya, ancient Arabian)
- 21st century modern urban era (for contemporary, technology)
- Islamic End Times era (for apocalypse, judgment day)
- Metaphysical void era (for abstract, existential reflection)
- Cosmic end-of-world era (for finality, cosmic scale)
";

    public async Task<GlobalScriptContext?> ExtractGlobalContextAsync(
        List<BrollPromptItem> segments,
        string topic,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("ExtractGlobalContext: Analyzing {Count} segments for topic: {Topic}", segments.Count, topic);

        try
        {
            // Build full script for analysis
            var fullScript = string.Join("\n", segments.Select((s, i) =>
                $"[Segment {i}] [{s.Timestamp}] {s.ScriptText}"));

            var userPrompt = $"TOPIC: {topic}\n\nFULL SCRIPT ({segments.Count} segments):\n{fullScript}";

            var response = await GenerateContentAsync(
                GlobalContextExtractionPrompt,
                userPrompt,
                maxTokens: 4000,
                temperature: 0.3,
                cancellationToken: cancellationToken);

            if (string.IsNullOrWhiteSpace(response))
            {
                _logger.LogWarning("ExtractGlobalContext: Empty response from LLM");
                return null;
            }

            // Parse JSON response
            var cleaned = response.Trim();
            if (cleaned.StartsWith("```")) cleaned = cleaned.Split('\n', 2).Last();
            if (cleaned.EndsWith("```")) cleaned = cleaned[..cleaned.LastIndexOf("```")];
            cleaned = cleaned.Trim();

            var dto = JsonSerializer.Deserialize<GlobalContextExtractionResponse>(
                cleaned,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (dto == null)
            {
                _logger.LogWarning("ExtractGlobalContext: Failed to deserialize response");
                return null;
            }

            var ctx = dto.ToGlobalScriptContext(topic);
            sw.Stop();
            _logger.LogInformation(
                "ExtractGlobalContext: Extracted {Locs} locations, {Chars} characters, {Eras} eras, {Moods} mood beats in {Ms}ms",
                ctx.PrimaryLocations.Count, ctx.IdentifiedCharacters.Count,
                ctx.EraTimeline.Count, ctx.MoodBeats.Count, sw.ElapsedMilliseconds);

            return ctx;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExtractGlobalContext: Failed to extract global context");
            return null;
        }
    }

    public async Task GeneratePromptsWithContextAsync(
        List<BrollPromptItem> items,
        BrollMediaType targetType,
        string topic,
        GlobalScriptContext globalContext,
        ImagePromptConfig? config = null,
        Func<int, Task>? onProgress = null,
        int windowSize = 2,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("GeneratePromptsWithContext: {Count} {Type} items with context (window=±{W})",
            items.Count(i => i.MediaType == targetType), targetType, windowSize);

        await GeneratePromptsBatchCoreAsync(
            items, targetType,
            promptGenerator: async item =>
            {
                var prompt = await GeneratePromptWithContextAsync(
                    item, items, topic, globalContext, config, windowSize, cancellationToken);
                return !string.IsNullOrWhiteSpace(prompt) ? prompt : null;
            },
            onProgress, cancellationToken);
    }

    public async Task<string> GeneratePromptWithContextAsync(
        BrollPromptItem currentItem,
        List<BrollPromptItem> allItems,
        string topic,
        GlobalScriptContext globalContext,
        ImagePromptConfig? config = null,
        int windowSize = 2,
        CancellationToken cancellationToken = default)
    {
        var systemPrompt = BuildContextualPrompt(
            currentItem, allItems, globalContext, topic, config, windowSize);

        var userPrompt = $"Generate prompt for segment {currentItem.Index}: {currentItem.ScriptText}";

        var result = await GenerateContentAsync(
            systemPrompt,
            userPrompt,
            maxTokens: 500,
            temperature: 0.7,
            cancellationToken: cancellationToken);

        return result?.Trim().Trim('"') ?? string.Empty;
    }

    private string BuildContextualPrompt(
        BrollPromptItem currentItem,
        List<BrollPromptItem> allItems,
        GlobalScriptContext ctx,
        string topic,
        ImagePromptConfig? config,
        int windowSize)
    {
        var sb = new System.Text.StringBuilder();
        var mediaType = currentItem.MediaType;

        // --- Global context ---
        sb.AppendLine($"TOPIC: {topic}");
        sb.AppendLine($"TOTAL SEGMENTS: {allItems.Count}");
        sb.AppendLine($"CURRENT SEGMENT INDEX: {currentItem.Index}");
        sb.AppendLine();

        if (ctx.PrimaryLocations.Count > 0)
            sb.AppendLine($"PRIMARY LOCATIONS: {string.Join(", ", ctx.PrimaryLocations)}");

        if (ctx.IdentifiedCharacters.Count > 0)
        {
            sb.AppendLine("CHARACTERS:");
            foreach (var c in ctx.IdentifiedCharacters)
                sb.AppendLine($"  - {c.Name}: {c.Description}");
        }

        if (ctx.RecurringVisuals.Count > 0)
            sb.AppendLine($"RECURRING VISUALS: {string.Join(", ", ctx.RecurringVisuals)}");

        if (!string.IsNullOrEmpty(ctx.ColorProgression))
            sb.AppendLine($"COLOR PROGRESSION: {ctx.ColorProgression}");

        sb.AppendLine();

        // --- Era for this segment ---
        var era = ctx.GetEraForSegment(currentItem.Index);
        if (era != null)
        {
            sb.AppendLine($"CURRENT ERA: {era.Era}");
            if (!string.IsNullOrEmpty(era.Description))
                sb.AppendLine($"ERA CONTEXT: {era.Description}");
        }

        // --- Mood beat for this segment ---
        var mood = ctx.GetMoodBeatForSegment(currentItem.Index);
        if (mood != null)
        {
            sb.AppendLine($"CURRENT MOOD: {mood.Mood} — {mood.Description}");
            if (mood.VisualKeywords.Count > 0)
                sb.AppendLine($"MOOD VISUALS: {string.Join(", ", mood.VisualKeywords)}");

            // Auto visual settings from mood
            if (config != null)
            {
                if (config.Lighting == ImageLighting.Auto && mood.SuggestedLighting.HasValue)
                    sb.AppendLine($"SUGGESTED LIGHTING: {ImageStyleMappings.GetLightingSuffix(mood.SuggestedLighting.Value)}");

                if (config.ColorPalette == ImageColorPalette.Auto && mood.SuggestedPalette.HasValue)
                    sb.AppendLine($"SUGGESTED PALETTE: {ImageStyleMappings.GetColorPaletteSuffix(mood.SuggestedPalette.Value)}");

                if (config.Composition == ImageComposition.Auto)
                {
                    var forcedAngle = GetDynamicComposition(currentItem.Index);
                    sb.AppendLine($"SUGGESTED ANGLE: {ImageStyleMappings.GetCompositionSuffix(forcedAngle)}");
                }
                else if (config.Composition != ImageComposition.Auto)
                {
                    sb.AppendLine($"SUGGESTED ANGLE: {ImageStyleMappings.GetCompositionSuffix(config.Composition)}");
                }

                if (!string.IsNullOrEmpty(mood.VisualRationale))
                    sb.AppendLine($"RATIONALE: {mood.VisualRationale}");
            }
        }

        sb.AppendLine();

        // --- Sliding window context ---
        var idx = currentItem.Index;
        var windowStart = Math.Max(0, idx - windowSize);
        var windowEnd = Math.Min(allItems.Count - 1, idx + windowSize);

        sb.AppendLine("SURROUNDING SEGMENTS:");
        for (int i = windowStart; i <= windowEnd; i++)
        {
            var seg = allItems[i];
            var marker = i == idx ? ">>>" : "   ";
            sb.AppendLine($"  {marker} [{i}] [{seg.Timestamp}] {seg.ScriptText}");
        }
        sb.AppendLine();

        // --- Type-specific instructions ---
        var effectiveStyleSuffix = config?.EffectiveStyleSuffix ?? Models.ImageVisualStyle.BASE_STYLE_SUFFIX;
        var customInstr = !string.IsNullOrWhiteSpace(config?.CustomInstructions)
            ? $"\nUSER INSTRUCTIONS: {config.CustomInstructions}\n"
            : string.Empty;

        if (mediaType == BrollMediaType.BrollVideo)
        {
            sb.AppendLine($@"TASK: Generate a concise English search query for STOCK FOOTAGE (B-Roll).
RULES:
- Output ONLY the search query (2-5 words).
- {BROLL_NO_HUMAN_RULES}
- Match the era and mood context above.
- Use NATURE imagery for ancient eras, URBAN imagery for modern.
- Examples: 'storm clouds timelapse', 'desert sand dunes', 'ancient ruins'.
OUTPUT (Just the search query, no quotes):");
        }
        else
        {
            sb.AppendLine($@"TASK: Generate a detailed, high-quality image generation prompt for Whisk/Imagen.
RULES:
- Output ONLY the prompt string.
- Follow this structure: [ERA PREFIX] [Detailed Description]{{LOCKED_STYLE}}
- ERA PREFIXES: {EraLibrary.GetEraSelectionInstructions()}
- CHARACTER RULES: {Models.CharacterRules.GENDER_RULES}
- PROPHET RULES: {Models.CharacterRules.PROPHET_RULES}
- LOCKED STYLE: {effectiveStyleSuffix}
- Use the MOOD and VISUAL KEYWORDS from context above to color the visual description.
- Maintain visual consistency with adjacent segments.
{IMAGE_GEN_COMPOSITION_RULES}
OUTPUT (Just the prompt, no quotes):");
        }

        if (!string.IsNullOrEmpty(customInstr))
            sb.AppendLine(customInstr);

        return sb.ToString();
    }
}
