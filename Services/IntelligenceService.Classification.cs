using System.Diagnostics;
using System.Text.Json;
using BunbunBroll.Models;

namespace BunbunBroll.Services;

/// <summary>
/// Classification methods — classify segments as BROLL vs IMAGE_GEN.
/// Contains the shared batch core and both public-facing wrappers.
/// </summary>
public partial class IntelligenceService
{
    // =============================================
    // SHARED BATCH CORE
    // =============================================

    /// <summary>
    /// Core batch classification engine used by both ClassifyAndGeneratePromptsAsync and ClassifySegmentsOnlyAsync.
    /// Handles: batch slicing, semaphore throttling, user prompt building, JSON parse, text overlay, fallback, notification.
    /// </summary>
    private async Task<List<BrollPromptItem>> ClassifySegmentsBatchCoreAsync(
        List<(string Timestamp, string ScriptText)> segments,
        string systemPrompt,
        int batchSize,
        bool includePrompts,
        string defaultFallbackPrompt,
        Func<List<BrollPromptItem>, Task>? onBatchComplete,
        CancellationToken cancellationToken)
    {
        var results = new List<BrollPromptItem>();
        if (segments.Count == 0) return results;

        var stopwatch = Stopwatch.StartNew();
        var totalBatches = (int)Math.Ceiling((double)segments.Count / batchSize);
        var semaphore = new SemaphoreSlim(3, 3);
        var batchTasks = new List<Task>();
        var resultsLock = new object();

        var methodName = includePrompts ? "ClassifyBroll" : "ClassifyOnly";
        _logger.LogInformation("{Method}: Processing {Count} segments in {Batches} batches",
            methodName, segments.Count, totalBatches);

        for (int batchIdx = 0; batchIdx < totalBatches; batchIdx++)
        {
            var batchIdxCapture = batchIdx;
            var batchStart = batchIdx * batchSize;
            var batchSegments = segments.Skip(batchStart).Take(batchSize).ToList();

            var batchTask = Task.Run(async () =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    _logger.LogDebug("{Method}: Batch {Batch}/{Total} — segments {Start}-{End}",
                        methodName, batchIdxCapture + 1, totalBatches, batchStart, batchStart + batchSegments.Count - 1);

                    // Build user prompt
                    var userPrompt = new System.Text.StringBuilder();
                    userPrompt.AppendLine($"Topic: {segments[0].ScriptText.Split(' ').FirstOrDefault() ?? "Islamic essay"}");
                    userPrompt.AppendLine("\nSEGMENTS:");
                    for (int i = 0; i < batchSegments.Count; i++)
                    {
                        userPrompt.AppendLine($"[{i}] {batchSegments[i].Timestamp} {batchSegments[i].ScriptText}");
                    }

                    var maxTokens = includePrompts
                        ? Math.Min(batchSegments.Count * 200, 4000)
                        : Math.Min(batchSegments.Count * 150, 4000);

                    var (rawContent, _) = await SendChatAsync(
                        systemPrompt, userPrompt.ToString(),
                        temperature: includePrompts ? 0.4 : 0.3,
                        maxTokens: maxTokens,
                        cancellationToken: cancellationToken);

                    var batchResults = new List<BrollPromptItem>();

                    if (!string.IsNullOrEmpty(rawContent))
                    {
                        var cleanedJson = CleanJsonResponse(rawContent);
                        _logger.LogDebug("{Method} batch {Batch} response: {Content}",
                            methodName, batchIdxCapture + 1, rawContent.Length > 200 ? rawContent[..200] + "..." : rawContent);

                        try
                        {
                            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                            var parsed = JsonSerializer.Deserialize<List<BrollClassificationResponse>>(cleanedJson, options);

                            if (parsed != null)
                            {
                                foreach (var item in parsed)
                                {
                                    if (item.Index >= 0 && item.Index < batchSegments.Count)
                                    {
                                        var globalIdx = batchStart + item.Index;
                                        var promptItem = new BrollPromptItem
                                        {
                                            Index = globalIdx,
                                            Timestamp = segments[globalIdx].Timestamp,
                                            ScriptText = segments[globalIdx].ScriptText,
                                            MediaType = item.MediaType?.ToUpperInvariant() == "IMAGE_GEN"
                                                ? BrollMediaType.ImageGeneration
                                                : BrollMediaType.BrollVideo,
                                            Prompt = includePrompts ? (item.Prompt ?? string.Empty) : string.Empty
                                        };

                                        // Parse text overlay if present
                                        var overlay = ParseTextOverlay(item.TextOverlay, globalIdx);
                                        if (overlay != null)
                                        {
                                            promptItem.TextOverlay = overlay;
                                            // Auto-enforce: text overlays get B-roll backgrounds
                                            promptItem.MediaType = BrollMediaType.BrollVideo;
                                        }

                                        // Auto-detect era (only when prompts are generated)
                                        if (includePrompts)
                                            EraLibrary.AutoAssignEraStyle(promptItem);

                                        batchResults.Add(promptItem);
                                    }
                                }
                            }
                        }
                        catch (JsonException ex)
                        {
                            _logger.LogWarning("{Method} batch {Batch} JSON parse failed: {Error}",
                                methodName, batchIdxCapture + 1, ex.Message);
                        }
                    }

                    // Fill missing segments in this batch
                    for (int i = 0; i < batchSegments.Count; i++)
                    {
                        var globalIdx = batchStart + i;
                        if (!batchResults.Any(r => r.Index == globalIdx))
                        {
                            batchResults.Add(CreateFallbackItem(
                                globalIdx, segments[globalIdx].Timestamp,
                                segments[globalIdx].ScriptText, defaultFallbackPrompt));
                        }
                    }

                    lock (resultsLock) { results.AddRange(batchResults); }
                    await NotifyBatchComplete(onBatchComplete, results, resultsLock);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "{Method} batch {Batch} failed, using fallbacks",
                        methodName, batchIdxCapture + 1);

                    lock (resultsLock)
                    {
                        for (int i = 0; i < batchSegments.Count; i++)
                        {
                            var globalIdx = batchStart + i;
                            if (!results.Any(r => r.Index == globalIdx))
                            {
                                results.Add(CreateFallbackItem(
                                    globalIdx, segments[globalIdx].Timestamp,
                                    segments[globalIdx].ScriptText, defaultFallbackPrompt));
                            }
                        }
                    }
                    await NotifyBatchComplete(onBatchComplete, results, resultsLock);
                }
                finally
                {
                    semaphore.Release();
                }
            }, cancellationToken);

            batchTasks.Add(batchTask);
        }

        await Task.WhenAll(batchTasks);

        // Fill any globally missing segments
        FillMissingSegments(results, segments, defaultFallbackPrompt);
        results = results.OrderBy(r => r.Index).ToList();

        var brollCount = results.Count(r => r.MediaType == BrollMediaType.BrollVideo);
        var imageGenCount = results.Count(r => r.MediaType == BrollMediaType.ImageGeneration);
        _logger.LogInformation("{Method}: Classified {Total} segments ({Broll} broll, {ImageGen} image gen) in {Ms}ms",
            methodName, results.Count, brollCount, imageGenCount, stopwatch.ElapsedMilliseconds);

        return results;
    }

    // =============================================
    // PUBLIC: Classify + Generate Prompts
    // =============================================

    public async Task<List<BrollPromptItem>> ClassifyAndGeneratePromptsAsync(
        List<(string Timestamp, string ScriptText)> segments,
        string topic,
        ImagePromptConfig? config = null,
        Func<List<BrollPromptItem>, Task>? onBatchComplete = null,
        CancellationToken cancellationToken = default)
    {
        var systemPrompt = BuildClassifyWithPromptsSystemPrompt(topic, config);
        return await ClassifySegmentsBatchCoreAsync(
            segments, systemPrompt,
            batchSize: 10,
            includePrompts: true,
            defaultFallbackPrompt: "atmospheric cinematic footage",
            onBatchComplete, cancellationToken);
    }

    // =============================================
    // PUBLIC: Classify Only (no prompts)
    // =============================================

    public async Task<List<BrollPromptItem>> ClassifySegmentsOnlyAsync(
        List<(string Timestamp, string ScriptText)> segments,
        string topic,
        ImagePromptConfig? config = null,
        Func<List<BrollPromptItem>, Task>? onBatchComplete = null,
        CancellationToken cancellationToken = default)
    {
        var systemPrompt = BuildClassifyOnlySystemPrompt(config);
        return await ClassifySegmentsBatchCoreAsync(
            segments, systemPrompt,
            batchSize: 15,
            includePrompts: false,
            defaultFallbackPrompt: string.Empty,
            onBatchComplete, cancellationToken);
    }

    // =============================================
    // PRIVATE: System prompt builders
    // =============================================

    private string BuildClassifyWithPromptsSystemPrompt(string topic, ImagePromptConfig? config)
    {
        var effectiveStyleSuffix = config?.EffectiveStyleSuffix ?? Models.ImageVisualStyle.BASE_STYLE_SUFFIX;
        var eraBiasInstruction = BuildEraBiasInstruction(config);
        var customInstructionsSection = BuildCustomInstructionsSection(config);

        return $@"You are a visual content classifier for Islamic video essays. Your job is to analyze script segments and decide the best visual approach for each.
{eraBiasInstruction}
For each segment, classify as:
1. **BROLL** - Use stock footage (Pexels/Pixabay) with ABSOLUTELY NO HUMAN SUBJECTS when the content depicts:
   - Real-world landscapes, nature, atmospheric shots, cityscapes (empty), urban (architectural), textures
   - Atmospheric footage (rain, clouds, sunrise, ocean, forest, mountains)
   - Objects (non-human), technology (abstract), textures, abstract light
   - ABSOLUTE RULE: NO PEOPLE, NO SILHOUETTES, NO HUMAN ACTIVITY, NO PERSON, NO HANDS, NO FEET, NO CROWDS, NO EYES.
   - NO MANUAL LABOR: Do not show hands planting, digging, or working. Use NATURE METAPHOR (e.g., 'planting' -> 'seedling growing timelapse').
   - If the script implies human action, use a NATURE or URBAN METAPHOR (e.g., 'faith grows' -> 'growing plant timelapse', 'people gather' -> 'city skyline aerial')

2. **IMAGE_GEN** - Use AI image generation (Whisk / Imagen) when the content depicts:
   - Historical/ancient scenes (7th century Arabia, ancient civilizations)
   - Prophets or religious figures (requires divine light, no face)
   - Supernatural/eschatological events (Day of Judgment, afterlife, angels)
   - Human characters in specific Islamic historical contexts
   - Abstract spiritual concepts (soul, faith, divine light)
   - Scenes that stock footage cannot realistically portray

{EraLibrary.GetEraSelectionInstructions()}

CHARACTER RULES (ISLAMIC SYAR'I - ONLY for IMAGE_GEN):
{Models.CharacterRules.GENDER_RULES}

{Models.CharacterRules.PROPHET_RULES}

LOCKED VISUAL STYLE (REQUIRED FOR ALL IMAGE_GEN PROMPTS):
{effectiveStyleSuffix}

BROLL ERA-BASED VISUAL CONTEXT (CRITICAL FOR BROLL ONLY):
- When the script describes stories of PROPHETS, ANCIENT TIMES, or HISTORICAL ERAS:
  BROLL must use NATURE-BASED keywords ONLY: desert landscape, sand dunes, mountain range, vast sky, barren land, rocky terrain, ancient ruins without people, oasis, starry desert night, forest canopy, calm sea, sunrise horizon, windswept plains
- When the script shifts to MODERN TIMES or CONTEMPORARY topics:
  BROLL must use URBAN keywords: cityscape, modern buildings, skyline, highway, infrastructure, modern architecture, glass tower, empty urban street, traffic lights, bridge structure, aerial city view
- REGARDLESS OF ERA: NEVER include any human presence in BROLL prompts

For BROLL segments: Generate a concise English search query for cinematic footage (2-5 words).
ABSOLUTE RULE for BROLL: DO NOT INCLUDE PEOPLE, HUMANS, FACES, SILHOUETTES, PERSON, HANDS, FEET, EYES, or any HUMAN ACTIVITY in the prompt. Use nature, architecture, or abstract metaphors.
- For actions like planting/sowing: use 'seedling growing' or 'soil texture'. 
- For actions like traveling: use 'road aerial' or 'moving clouds'.
- Always prioritize WIDE SHOTS or MACRO (non-human).

For IMAGE_GEN segments: Generate a detailed Whisk-style prompt following this structure:
  [ERA PREFIX] [Detailed scene description: setting, action, lighting, atmosphere, characters]{{LOCKED_STYLE}}
  - Start with one era prefix from the list above
  - Include character descriptions with syar'i dress for females
  - If prophets appear: add 'face replaced by intense white-golden divine light, facial features not visible'
  - End with style suffix: '{effectiveStyleSuffix}'
{customInstructionsSection}
{TEXT_OVERLAY_RULES}

RESPOND WITH JSON ONLY (no markdown):
[
  {{
    ""index"": 0,
    ""mediaType"": ""BROLL"" or ""IMAGE_GEN"",
    ""prompt"": ""the generated prompt"",
    ""textOverlay"": {{
      ""type"": ""QuranVerse"",
      ""text"": ""In the name of Allah"",
      ""arabic"": ""بِسْمِ اللَّهِ"",
      ""reference"": ""Surah Al-Fatiha 1:1""
    }}
  }}
]
Note: textOverlay is null/omitted for MOST segments. Only add for truly impactful moments.

RULES:
- Translate all prompts to English
- For BROLL: Keep prompts short (2-5 words), focused on NATURE (for ancient/prophetic) or URBAN (for modern).
- For BROLL: NEVER mention 'person', 'man', 'woman', 'people', 'crowd', 'face', 'silhouette', 'hands', 'feet', 'shadow person'.
- For BROLL: ALSO AVOID human-adjacent terms that return human footage: 'mirror', 'reflection', 'shadow', 'window', 'doorway', 'selfie', 'walking', 'standing', 'sitting', 'running', 'praying', 'crying', 'laughing', 'embrace', 'handshake', 'footsteps', 'footprint'.
  Use nature/urban metaphors instead: 'broken mirror' -> 'cracked earth texture', 'reflection' -> 'water surface', 'shadow' -> 'dark clouds'
- For IMAGE_GEN: Include era prefix, detailed scene, locked style suffix
- Never depict prophet faces
- Avoid sensitive/haram visual triggers";
    }

    private string BuildClassifyOnlySystemPrompt(ImagePromptConfig? config)
    {
        var eraBiasInstruction = BuildEraBiasInstruction(config);

        return $@"You are a visual content classifier for Islamic video essays. Classify each segment as BROLL or IMAGE_GEN.
{eraBiasInstruction}
BROLL - Stock footage (no humans): landscapes, nature, textures, cityscapes, atmospheric shots
IMAGE_GEN - AI image generation: historical scenes, prophets, supernatural events, specific Islamic historical contexts, abstract spiritual concepts

{TEXT_OVERLAY_RULES.Replace("~20%", "~25%")}

RESPOND WITH JSON ONLY (no markdown):
[
  {{
    ""index"": 0,
    ""mediaType"": ""BROLL"" or ""IMAGE_GEN"",
    ""textOverlay"": {{
      ""type"": ""QuranVerse"",
      ""text"": ""In the name of Allah"",
      ""arabic"": ""بِسْمِ اللَّهِ"",
      ""reference"": ""Surah Al-Fatiha 1:1""
    }}
  }}
]
Note: textOverlay is null/omitted for MOST segments. Only add for truly impactful moments.

RULES:
- Return index, mediaType, and textOverlay (if applicable)
- Do NOT generate image/search prompts — just classify and detect overlays";
    }

    // =============================================
    // PRIVATE: Shared config extraction helpers
    // =============================================

    private static string BuildEraBiasInstruction(ImagePromptConfig? config)
    {
        if (config?.DefaultEra != VideoEra.None && config?.DefaultEra != null)
            return $"\nDEFAULT ERA CONTEXT: Unless a segment clearly belongs to a different era, default to {config.DefaultEra} era.\n";
        return string.Empty;
    }

    private static string BuildCustomInstructionsSection(ImagePromptConfig? config)
    {
        if (!string.IsNullOrWhiteSpace(config?.CustomInstructions))
            return $"\nUSER CUSTOM INSTRUCTIONS (PRIORITY):\n{config.CustomInstructions}\n";
        return string.Empty;
    }
}
