using System.Diagnostics;
using BunbunBroll.Models;

namespace BunbunBroll.Services;

/// <summary>
/// Prompt generation methods — GenerateContentAsync, GeneratePromptForTypeAsync, GeneratePromptsForTypeBatchAsync.
/// </summary>
public partial class IntelligenceService
{
    /// <summary>
    /// General-purpose content generation via LLM.
    /// </summary>
    public async Task<string?> GenerateContentAsync(
        string systemPrompt, 
        string userPrompt, 
        int maxTokens = 4000, 
        double temperature = 0.7, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("GenerateContent: Sending request ({MaxTokens} max tokens)", maxTokens);

            var (content, tokens) = await SendChatAsync(
                systemPrompt, userPrompt,
                temperature: temperature, maxTokens: maxTokens,
                cancellationToken: cancellationToken);

            _logger.LogInformation("GenerateContent: Received {Length} chars, {Tokens} tokens",
                content?.Length ?? 0, tokens);

            return content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GenerateContent failed");
            return null;
        }
    }

    /// <summary>
    /// Forces generation of a specific prompt type (B-Roll or Image Gen) for a given segment.
    /// </summary>
    public async Task<string> GeneratePromptForTypeAsync(
        string scriptText,
        BrollMediaType mediaType,
        string topic,
        ImagePromptConfig? config = null,
        CancellationToken cancellationToken = default,
        int segmentIndex = 0)
    {
        var activeConfig = config ?? new ImagePromptConfig();
        
        // Force angle rotation if Auto
        if (mediaType == BrollMediaType.ImageGeneration && activeConfig.Composition == ImageComposition.Auto)
        {
            activeConfig = new ImagePromptConfig
            {
                ArtStyle = activeConfig.ArtStyle,
                CustomArtStyle = activeConfig.CustomArtStyle,
                Lighting = activeConfig.Lighting,
                ColorPalette = activeConfig.ColorPalette,
                Composition = GetDynamicComposition(segmentIndex),
                DefaultEra = activeConfig.DefaultEra,
                CustomInstructions = activeConfig.CustomInstructions
            };
        }

        var effectiveStyleSuffix = activeConfig.EffectiveStyleSuffix;
        var lightingSuffix = activeConfig.Lighting != ImageLighting.Auto ? 
            ImageStyleMappings.GetLightingSuffix(activeConfig.Lighting) : "appropriately matched lighting";
        var compositionSuffix = activeConfig.Composition != ImageComposition.Auto ? 
            ImageStyleMappings.GetCompositionSuffix(activeConfig.Composition) : "cinematic shot";

        var eraBias = activeConfig.DefaultEra != VideoEra.None
            ? $"\nDEFAULT ERA: Bias toward {activeConfig.DefaultEra} era visual style.\n"
            : string.Empty;
        var customInstr = !string.IsNullOrWhiteSpace(activeConfig.CustomInstructions)
            ? $"\nUSER INSTRUCTIONS: {activeConfig.CustomInstructions}\n"
            : string.Empty;

        string systemPrompt;
        
        if (mediaType == BrollMediaType.BrollVideo)
        {
            systemPrompt = $@"You are a visual content analyzer for Islamic video essays.
Your task: Generate a concise English search query for STOCK FOOTAGE (B-Roll) based on the script segment.

CONTEXT: {topic}
{eraBias}
RULES for BROLL:
- Output ONLY the search query (2-5 words).
- {BROLL_NO_HUMAN_RULES}
{customInstr}
SCRIPT SEGMENT: ""{scriptText}""

OUTPUT (Just the search query, no quotes):";
        }
        else // ImageGeneration
        {
            systemPrompt = $"""
You are an AI image prompt generator for Islamic video essays.
Your task: Generate a CONCISE image generation prompt (under 200 words).

CONTEXT: {topic}
{eraBias}
RULES for IMAGE_GEN:
- Output ONLY the prompt string, no explanations.
- STRICT PROMPT HIERARCHY (Follow exactly in this order):
  1. Shot Type / Camera Perspective: {compositionSuffix}
  2. Primary Subject: [Who/what is the focus, concrete noun]
  3. Environment / Setting: [Physical world description, starting with ERA PREFIX: {EraLibrary.GetEraSelectionInstructions()}]
  4. Scale & Composition Cues: [Scale language, e.g. figures tiny against immense scale]
  5. Lighting: {lightingSuffix}
  6. Atmosphere & Mood: [1-3 descriptors max, e.g. epic scale, awe, tense]
  7. Style & Rendering: {effectiveStyleSuffix}
- Be specific but BRIEF. Avoid abstract narrative terms. Models render nouns better than philosophy.
- ERA CONSISTENCY: Commit to ONE historical era. DO NOT blur or mix eras.
- AVOID ABSTRACTION: Do not use theological phrases (e.g., 'prophetic confrontation'). Describe visually.
- NO REDUNDANCY: If two phrases describe the same thing, delete one. Precision > poetry. No adjective stacking.
- SCALE DISCIPLINE: Use at most ONE scale cue per prompt (e.g. 'figures tiny against immense environment'). Delete ALL duplicate size/scale phrases. NEVER stack scale language.
- ERA LABELS: Do NOT insert the era as a textbook classification string (e.g. 'Late Ancient era Bronze Age coastal desert landscape'). Let visual elements imply the era. Use the era ONLY to guide your choice of architecture, clothing, and props.
- SPATIAL COHERENCE: Ensure spatial descriptions are internally consistent. If thousands walk through a space, it must be 'vast', not 'narrow'. Cross-check adjectives against quantities and subjects.
- STYLE COMMITMENT: Match the final style tag to the camera language. If describing cinematic camera angles (aerial, tracking, God's-eye), end with 'high-detail cinematic realism'. If describing a painted composition, end with the painting style tag. NEVER mix 'cinematic' camera with 'painting' style.
- PHYSICAL LOGIC: Ensure environments make physical sense (e.g., exposed seabeds are wet sand or rippled mud, not cracked dry clay).
- MOOD CLARITY: No abstract mood phrasing (e.g., 'wonder collapsing into foreboding irony'). Models do not render irony.
- CHARACTER RULES: {Models.CharacterRules.GENDER_RULES}
- PROPHET RULES: {Models.CharacterRules.PROPHET_RULES}
{customInstr}
SCRIPT SEGMENT: "{scriptText}"

OUTPUT (concise prompt, no quotes):
""";
        }

        var result = await GenerateContentAsync(systemPrompt, $"Generate prompt for: {scriptText}", maxTokens: 300, temperature: 0.7, cancellationToken: cancellationToken);
        return result?.Trim().Trim('"') ?? string.Empty;
    }

    /// <summary>
    /// Batch-generate prompts for segments of a specific media type.
    /// Updates items in-place with generated prompts.
    /// </summary>
    public async Task GeneratePromptsForTypeBatchAsync(
        List<BrollPromptItem> items,
        BrollMediaType targetType,
        string topic,
        ImagePromptConfig? config = null,
        Func<int, Task>? onProgress = null,
        CancellationToken cancellationToken = default,
        bool resumeOnly = false)
    {
        await GeneratePromptsBatchCoreAsync(
            items, targetType,
            promptGenerator: item => GeneratePromptForTypeAsync(
                item.ScriptText, targetType, topic, config, cancellationToken, item.Index)
                .ContinueWith(t => (string?)t.Result),
            onProgress, cancellationToken, resumeOnly);
    }

    /// <summary>
    /// Shared core for batch prompt generation — used by both ForType and WithContext variants.
    /// Filters items by targetType, throttles with semaphore, assigns prompts, auto-assigns era, reports progress.
    /// </summary>
    private async Task GeneratePromptsBatchCoreAsync(
        List<BrollPromptItem> items,
        BrollMediaType targetType,
        Func<BrollPromptItem, Task<string?>> promptGenerator,
        Func<int, Task>? onProgress,
        CancellationToken cancellationToken,
        bool resumeOnly = false)
    {
        var targetItems = items.Where(i => i.MediaType == targetType).ToList();
        
        // In resume mode, skip segments that already have prompts
        if (resumeOnly)
        {
            targetItems = targetItems.Where(i => string.IsNullOrWhiteSpace(i.Prompt)).ToList();
            _logger.LogInformation("Resume mode: {Count} segments need prompts", targetItems.Count);
        }

        if (targetItems.Count == 0) return;

        var stopwatch = Stopwatch.StartNew();
        var semaphore = new SemaphoreSlim(5, 5);
        int completedCount = 0;
        const int maxRetries = 3;

        // Shared CTS: cancel all remaining tasks when any segment fails
        using var failureCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        string? firstFailureMessage = null;

        var tasks = targetItems.Select(async item =>
        {
            await semaphore.WaitAsync(failureCts.Token);
            try
            {
                failureCts.Token.ThrowIfCancellationRequested();

                string? generatedPrompt = null;

                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    generatedPrompt = await promptGenerator(item);

                    if (!string.IsNullOrWhiteSpace(generatedPrompt))
                        break;

                    _logger.LogWarning(
                        "Prompt generation attempt {Attempt}/{Max} failed for segment #{Index}. {Action}",
                        attempt, maxRetries, item.Index,
                        attempt < maxRetries ? "Retrying in 2s..." : "STOPPING — all retries exhausted.");

                    if (attempt < maxRetries)
                        await Task.Delay(2000, failureCts.Token);
                }

                if (string.IsNullOrWhiteSpace(generatedPrompt))
                {
                    // Signal all other tasks to stop
                    var completed = Interlocked.CompareExchange(ref firstFailureMessage,
                        $"Prompt generation failed for segment #{item.Index} after {maxRetries} attempts. Use 'Resume' to continue.",
                        null);
                    failureCts.Cancel();
                    return;
                }

                item.Prompt = generatedPrompt;

                if (targetType == BrollMediaType.ImageGeneration)
                    EraLibrary.AutoAssignEraStyle(item);

                var count = Interlocked.Increment(ref completedCount);
                if (onProgress != null) await onProgress(count);
            }
            catch (OperationCanceledException) { /* Expected when failureCts is triggered */ }
            finally { semaphore.Release(); }
        });

        await Task.WhenAll(tasks);

        // If any segment failed, throw so the UI shows the error
        if (firstFailureMessage != null)
        {
            throw new InvalidOperationException(
                $"{firstFailureMessage} Completed {completedCount}/{targetItems.Count}.");
        }

        _logger.LogInformation("GeneratePromptsBatch: Generated {Count} {Type} prompts in {Ms}ms",
            targetItems.Count, targetType, stopwatch.ElapsedMilliseconds);
    }
}
