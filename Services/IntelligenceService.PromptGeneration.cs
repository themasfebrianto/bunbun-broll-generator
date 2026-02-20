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
            systemPrompt = $@"You are an AI image prompt generator for Islamic video essays.
Your task: Generate a detailed, high-quality image generation prompt for Whisk/Imagen.

CONTEXT: {topic}
{eraBias}
RULES for IMAGE_GEN:
- Output ONLY the prompt string.
- Follow this structure: [ERA PREFIX] [Detailed Description]{{LOCKED_STYLE}}
- ERA PREFIXES: {EraLibrary.GetEraSelectionInstructions()}
- CHARACTER RULES: {Models.CharacterRules.GENDER_RULES}
- PROPHET RULES: {Models.CharacterRules.PROPHET_RULES}
- LOCKED STYLE: {effectiveStyleSuffix}
{IMAGE_GEN_COMPOSITION_RULES}
{customInstr}
SCRIPT SEGMENT: ""{scriptText}""

OUTPUT (Just the prompt, no quotes):";
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
        CancellationToken cancellationToken = default)
    {
        await GeneratePromptsBatchCoreAsync(
            items, targetType,
            promptGenerator: item => GeneratePromptForTypeAsync(
                item.ScriptText, targetType, topic, config, cancellationToken, item.Index)
                .ContinueWith(t => (string?)t.Result),
            onProgress, cancellationToken);
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
        CancellationToken cancellationToken)
    {
        var targetItems = items.Where(i => i.MediaType == targetType).ToList();
        if (targetItems.Count == 0) return;

        var stopwatch = Stopwatch.StartNew();
        var semaphore = new SemaphoreSlim(3, 3);
        int completedCount = 0;

        var tasks = targetItems.Select(async item =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var generatedPrompt = await promptGenerator(item);
                    
                item.Prompt = generatedPrompt ?? (targetType == BrollMediaType.BrollVideo ? "cinematic footage" : "islamic historical scene");

                // Auto-detect era for IMAGE_GEN
                if (targetType == BrollMediaType.ImageGeneration)
                    EraLibrary.AutoAssignEraStyle(item);

                var count = Interlocked.Increment(ref completedCount);
                if (onProgress != null) await onProgress(count);
            }
            finally { semaphore.Release(); }
        });

        await Task.WhenAll(tasks);

        _logger.LogInformation("GeneratePromptsBatch: Generated {Count} {Type} prompts in {Ms}ms",
            targetItems.Count, targetType, stopwatch.ElapsedMilliseconds);
    }
}
