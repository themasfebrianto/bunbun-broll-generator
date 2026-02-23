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
        List<(string Timestamp, string ScriptText, TextOverlay? Overlay)> segments,
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
        var semaphore = new SemaphoreSlim(15, 15);
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
                    var batchResults = new List<BrollPromptItem>();
                    var segmentsSentToLlm = 0;

                    for (int i = 0; i < batchSegments.Count; i++)
                    {
                        if (batchSegments[i].Overlay != null)
                        {
                            var words = batchSegments[i].ScriptText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                            var duration = Math.Max(3.0, words / 2.5);

                            var promptItem = new BrollPromptItem
                            {
                                Index = batchStart + i,
                                Timestamp = batchSegments[i].Timestamp,
                                ScriptText = batchSegments[i].ScriptText,
                                MediaType = BrollMediaType.BrollVideo,
                                Prompt = string.Empty,
                                TextOverlay = batchSegments[i].Overlay,
                                EstimatedDurationSeconds = duration
                            };
                            batchResults.Add(promptItem);
                            continue; // Skip LLM
                        }

                        userPrompt.AppendLine($"[{i}] {batchSegments[i].Timestamp} {batchSegments[i].ScriptText}");
                        segmentsSentToLlm++;
                    }

                    if (segmentsSentToLlm == 0)
                    {
                        // All segments in this batch already had overlays!
                        lock (resultsLock) { results.AddRange(batchResults); }
                        await NotifyBatchComplete(onBatchComplete, results, resultsLock);
                        return;
                    }

                    var maxTokens = includePrompts
                        ? Math.Min(batchSegments.Count * 200, 4000)
                        : Math.Min(batchSegments.Count * 150, 4000);

                    var (rawContent, _) = await SendChatAsync(
                        systemPrompt, userPrompt.ToString(),
                        temperature: includePrompts ? 0.4 : 0.3,
                        maxTokens: maxTokens,
                        cancellationToken: cancellationToken);

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
                                        var words = segments[globalIdx].ScriptText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                                        var duration = Math.Max(3.0, words / 2.5);

                                        var promptItem = new BrollPromptItem
                                        {
                                            Index = globalIdx,
                                            Timestamp = segments[globalIdx].Timestamp,
                                            ScriptText = segments[globalIdx].ScriptText,
                                            MediaType = BrollMediaType.ImageGeneration,
                                            Prompt = includePrompts ? (item.Prompt ?? string.Empty) : string.Empty,
                                            EstimatedDurationSeconds = duration
                                        };

                                        // Optionally, the LLM might hallucinate overlays if it's acting up.
                                        // We'll ignore the LLM's overlay data now since we already pre-parsed it 
                                        // or bypassed it.

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
        List<(string Timestamp, string ScriptText, TextOverlay? Overlay)> segments,
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
        List<(string Timestamp, string ScriptText, TextOverlay? Overlay)> segments,
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
        var lightingSuffix = config?.Lighting != ImageLighting.Auto && config != null ? 
            ImageStyleMappings.GetLightingSuffix(config.Lighting) : "appropriately matched lighting";
        var compositionSuffix = config?.Composition != ImageComposition.Auto && config != null ? 
            ImageStyleMappings.GetCompositionSuffix(config.Composition) : "cinematic shot";
            
        var eraBiasInstruction = BuildEraBiasInstruction(config);
        var customInstructionsSection = BuildCustomInstructionsSection(config);

        return $@"You are a visual prompt generator for Islamic video essays. Your job is to analyze script segments and generate detailed AI image generation prompts for each.
{eraBiasInstruction}
For EVERY segment, generate a detailed Whisk-style prompt for AI image generation (IMAGE_GEN). These images can depict:
   - Historical/ancient scenes (7th century Arabia, ancient civilizations)
   - Prophets or religious figures (requires divine light, no face)
   - Supernatural/eschatological events (Day of Judgment, afterlife, angels)
   - Human characters in specific Islamic historical contexts
   - Abstract spiritual concepts (soul, faith, divine light)
   - Atmospheric scenes, nature, or cinematic landscapes

{EraLibrary.GetEraSelectionInstructions()}

CHARACTER RULES (ISLAMIC SYAR'I - ONLY for IMAGE_GEN):
{Models.CharacterRules.GENDER_RULES}

{Models.CharacterRules.PROPHET_RULES}

LOCKED VISUAL STYLE (REQUIRED FOR ALL IMAGE_GEN PROMPTS):
{effectiveStyleSuffix}


For IMAGE_GEN segments: Generate a detailed Whisk-style prompt following this STRICT 7-tier hierarchy in order:
  1. Shot Type / Camera Perspective: {compositionSuffix}
  2. Primary Subject: [Who/what is the focus, concrete noun]
  3. Environment / Setting: [Physical world description, starting with one ERA PREFIX from the list above]
  4. Scale & Composition Cues: [Scale language, e.g. figures tiny against immense scale]
  5. Lighting: {lightingSuffix}
  6. Atmosphere & Mood: [1-3 descriptors max, e.g. epic scale, awe, tense]
  7. Style & Rendering: {effectiveStyleSuffix}
- Be specific but BRIEF. Avoid abstract narrative terms. Models render nouns better than philosophy.
{customInstructionsSection}

RESPOND WITH JSON ONLY (no markdown):
[
  {{
    ""index"": 0,
    ""mediaType"": ""IMAGE_GEN"",
    ""prompt"": ""the generated prompt""
  }}
]
Note: textOverlay is null/omitted for MOST segments. Only add for truly impactful moments.

RULES:
- Translate all prompts to English
- Include era prefix, detailed scene, locked style suffix
- Never depict prophet faces
- Avoid sensitive/haram visual triggers
- REALISM (STRICT): For CONCRETE/HISTORICAL segments: REALISTIC scenes only, like a documentary film still. NO surreal imagery (no giant heads, no ghostly/phantom objects, no impossible physics, no symbolic objects unnaturally placed). For metaphors, use body language and environment to convey emotion. For ABSTRACT/PHILOSOPHICAL segments (no specific people/place/event): surreal imagery IS allowed. Divine light on prophets always required.
- PROMPT DISCIPLINE (STRICT): Keep prompts 80-150 words MAX. ONE clear subject per prompt. NO contradictory lighting.
- ERA CONSISTENCY: Commit to ONE historical era. DO NOT blur or mix eras.
- AVOID ABSTRACTION: Do not use theological phrases (e.g., 'prophetic confrontation'). Describe visually.
- NO REDUNDANCY: If two phrases describe the same thing, delete one. Precision > poetry. No adjective stacking.
- SCALE DISCIPLINE: Use at most ONE scale cue per prompt (e.g. 'figures tiny against immense environment'). Delete ALL duplicate size/scale phrases. NEVER stack scale language.
- ERA LABELS: Do NOT insert the era as a textbook classification string (e.g. 'Late Ancient era Bronze Age coastal desert landscape'). Let visual elements imply the era. Use the era ONLY to guide your choice of architecture, clothing, and props.
- SPATIAL COHERENCE: Ensure spatial descriptions are internally consistent. If thousands walk through a space, it must be 'vast', not 'narrow'. Cross-check adjectives against quantities and subjects.
- STYLE COMMITMENT: Match the final style tag to the camera language. If describing cinematic camera angles (aerial, tracking, God's-eye), end with 'high-detail cinematic realism'. If describing a painted composition, end with the painting style tag. NEVER mix 'cinematic' camera with 'painting' style.
- PHYSICAL LOGIC: Ensure environments make physical sense (e.g., exposed seabeds are wet sand or rippled mud, not cracked dry clay).
- MOOD CLARITY: No abstract mood phrasing (e.g., 'wonder collapsing into foreboding irony'). Models do not render irony.";
    }

    private string BuildClassifyOnlySystemPrompt(ImagePromptConfig? config)
    {
        var eraBiasInstruction = BuildEraBiasInstruction(config);

        return $@"You are a visual content formatting system for Islamic video essays. Tag each segment as IMAGE_GEN.
{eraBiasInstruction}
IMAGE_GEN - AI image generation

RESPOND WITH JSON ONLY (no markdown):
[
  {{
    ""index"": 0,
    ""mediaType"": ""IMAGE_GEN""
  }}
]

RULES:
- Return index and mediaType
- Do NOT generate image/search prompts";
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
