using BunbunBroll.Models;
using BunbunBroll.Orchestration.Context;
using BunbunBroll.Orchestration.Generators;
using BunbunBroll.Orchestration.Validators;
using BunbunBroll.Services;
using Microsoft.Extensions.Logging;

namespace BunbunBroll.Orchestration.Services;

/// <summary>
/// Coordinates execution of a single phase with retry logic.
/// Ported from ScriptFlow's PhaseCoordinator with validation-driven regeneration.
/// </summary>
public class PhaseCoordinator
{
    private readonly IIntelligenceService _intelligenceService;
    private readonly PromptBuilder _promptBuilder;
    private readonly SectionFormatter _formatter;
    private readonly IPhaseValidator _validator;
    private readonly ILogger? _logger;

    public PhaseCoordinator(
        IIntelligenceService intelligenceService,
        ILogger? logger = null)
    {
        _intelligenceService = intelligenceService;
        _promptBuilder = new PromptBuilder();
        _formatter = new SectionFormatter();
        _validator = new PatternValidator();
        _logger = logger;
    }

    /// <summary>
    /// Execute a phase with retry logic
    /// </summary>
    public async Task<GeneratedPhase> ExecutePhaseAsync(
        PhaseDefinition phase,
        GenerationContext context,
        int maxRetries = 2)
    {
        var attempt = 0;
        string? validationFeedback = null;
        List<string> validationIssues = new();

        while (attempt <= maxRetries)
        {
            try
            {
                // Build phase context
                var phaseContext = new PhaseContext
                {
                    Phase = phase,
                    PreviousContent = context.GetPreviousPhase(phase.Order)?.Content,
                    PreviousPhaseName = context.GetPreviousPhase(phase.Order)?.PhaseName,
                    RetryAttempt = attempt,
                    ValidationFeedback = validationFeedback
                };

                // Build prompt
                string prompt;
                if (attempt == 0)
                {
                    prompt = _promptBuilder.BuildPrompt(phase, context, phaseContext);
                }
                else
                {
                    prompt = _promptBuilder.BuildRegenerationPrompt(
                        phase, context, phaseContext, validationFeedback ?? string.Empty);
                }

                // Build system prompt from pattern
                var systemPrompt = BuildSystemPrompt(context);

                // Estimate max tokens
                var maxTokens = Math.Min(phase.WordCountTarget.Max * 2 + 500, 8000);

                // Generate via LLM
                var llmOutput = await _intelligenceService.GenerateContentAsync(
                    systemPrompt, prompt, maxTokens, 0.7);

                if (string.IsNullOrEmpty(llmOutput))
                {
                    throw new InvalidOperationException($"LLM returned empty content for phase '{phase.Name}'");
                }

                // Format output
                var formatted = _formatter.FormatToMarkdown(llmOutput, phase, context);

                // Validate the generated content
                var validationResult = await _validator.ValidateAsync(formatted, phase, context);

                // Store validation issues
                validationIssues = validationResult.Issues
                    .Select(i => $"[{i.Category}] {i.Message}")
                    .ToList();

                if (validationResult.IsValid)
                {
                    // Success
                    return new GeneratedPhase
                    {
                        PhaseId = phase.Id,
                        PhaseName = phase.Name,
                        Order = phase.Order,
                        Content = formatted,
                        WordCount = validationResult.WordCount,
                        DurationSeconds = validationResult.EstimatedDurationSeconds,
                        IsValidated = true,
                        Warnings = validationIssues
                    };
                }

                // Validation failed - prepare for retry
                if (attempt < maxRetries)
                {
                    _logger?.LogWarning(
                        "Phase '{PhaseName}' validation failed (attempt {Attempt}/{MaxRetries}), retrying",
                        phase.Name, attempt + 1, maxRetries);
                    
                    validationFeedback = _validator.GetFeedbackForRegeneration(validationResult);
                    attempt++;
                    continue;
                }
                else
                {
                    // Max retries reached - return with validation issues
                    _logger?.LogWarning(
                        "Phase '{PhaseName}' validation failed after {MaxAttempts} attempts",
                        phase.Name, maxRetries + 1);

                    return new GeneratedPhase
                    {
                        PhaseId = phase.Id,
                        PhaseName = phase.Name,
                        Order = phase.Order,
                        Content = formatted,
                        WordCount = validationResult.WordCount,
                        DurationSeconds = validationResult.EstimatedDurationSeconds,
                        IsValidated = false,
                        Warnings = validationIssues
                    };
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Phase '{PhaseName}' attempt {Attempt} failed", phase.Name, attempt + 1);

                // Check if this is a connection error
                var isConnectionError = ex.Message.Contains("connection", StringComparison.OrdinalIgnoreCase) ||
                                       ex.Message.Contains("Failed to reach", StringComparison.OrdinalIgnoreCase) ||
                                       ex is HttpRequestException;

                if (attempt >= maxRetries)
                {
                    // For connection errors, throw to stop generation
                    if (isConnectionError)
                    {
                        throw new InvalidOperationException(
                            $"Cannot generate phase '{phase.Name}': LLM service is not available after {maxRetries + 1} attempts.", ex);
                    }

                    return new GeneratedPhase
                    {
                        PhaseId = phase.Id,
                        PhaseName = phase.Name,
                        Order = phase.Order,
                        Content = string.Empty,
                        WordCount = 0,
                        DurationSeconds = 0,
                        IsValidated = false,
                        Warnings = new List<string> { $"Generation failed: {ex.Message}" }
                    };
                }

                // For connection errors, add delay before retry with exponential backoff
                if (isConnectionError)
                {
                    var delayMs = (int)Math.Pow(2, attempt) * 1000;
                    _logger?.LogInformation("Waiting {DelayMs}ms before retry due to connection error", delayMs);
                    await Task.Delay(delayMs);
                }

                attempt++;
                validationFeedback = $"Previous attempt failed with error: {ex.Message}";
            }
        }

        // Should never reach here
        return new GeneratedPhase
        {
            PhaseId = phase.Id,
            PhaseName = phase.Name,
            Order = phase.Order,
            Content = string.Empty,
            WordCount = 0,
            DurationSeconds = 0,
            IsValidated = false,
            Warnings = new List<string> { "Max retries exceeded" }
        };
    }

    private string BuildSystemPrompt(GenerationContext context)
    {
        var parts = new List<string>
        {
            "Anda adalah penulis script video essay profesional.",
            $"Bahasa: {context.Pattern.GlobalRules.Language}",
            $"Nada: {context.Pattern.GlobalRules.Tone}"
        };

        if (!string.IsNullOrEmpty(context.Pattern.GlobalRules.Perspective))
            parts.Add($"Perspektif: {context.Pattern.GlobalRules.Perspective}");
        if (!string.IsNullOrEmpty(context.Pattern.GlobalRules.MaxWordsPerSentence))
            parts.Add($"Maksimal kata per kalimat: {context.Pattern.GlobalRules.MaxWordsPerSentence}");
        if (!string.IsNullOrEmpty(context.Pattern.GlobalRules.PreferredWordsPerSentence))
            parts.Add($"Kata per kalimat: {context.Pattern.GlobalRules.PreferredWordsPerSentence}");
        if (!string.IsNullOrEmpty(context.Pattern.GlobalRules.MustUseKeywords))
            parts.Add($"Kata kunci yang digunakan: {context.Pattern.GlobalRules.MustUseKeywords}");
        if (context.Pattern.GlobalRules.HonorificsRequired == "true")
            parts.Add("Gunakan honorifik yang sesuai (SAW, RA, dll).");

        parts.Add("");
        parts.Add("INSTRUKSI:");
        parts.Add("- Tulis konten script langsung, TANPA markup, header, atau metadata");
        parts.Add("- Tulis dalam paragraf narasi yang mengalir natural");
        parts.Add("- JANGAN gunakan bullet point, numbering, atau formatting khusus");
        parts.Add("- Fokus pada kualitas tulisan dan kedalaman konten");

        return string.Join("\n", parts);
    }
}
