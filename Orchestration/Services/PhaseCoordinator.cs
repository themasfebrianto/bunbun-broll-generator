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
/// Enhanced with pattern-specific system prompt generation and prompt caching.
/// </summary>
public class PhaseCoordinator
{
    private readonly IIntelligenceService _intelligenceService;
    private readonly PromptBuilder _promptBuilder;
    private readonly SectionFormatter _formatter;
    private readonly IPhaseValidator _validator;
    private readonly ILogger? _logger;
    private readonly RuleRenderer _ruleRenderer = new();

    // System prompt cache: key = sessionId_patternId, value = cached prompt
    private string? _cachedSystemPromptKey;
    private string? _cachedSystemPrompt;

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

                // Populate assigned outline points from SharedData (set by Orchestrator)
                if (context.SharedData.TryGetValue("currentPhaseOutline", out var outObj) && outObj is List<string> outlinePoints)
                {
                    phaseContext.AssignedOutlinePoints = outlinePoints;
                }
                // Fallback to full distribution map if specific points not set
                else if (context.SharedData.TryGetValue("outlineDistribution", out var distObj)
                    && distObj is Dictionary<string, List<string>> distribution
                    && distribution.TryGetValue(phase.Id, out var distributionPoints))
                {
                    phaseContext.AssignedOutlinePoints = distributionPoints;
                }

                // Populate Global Context (anti-repetition)
                if (context.SharedData.TryGetValue("currentGlobalContext", out var globObj) && globObj is List<string> globalCtx)
                {
                    phaseContext.GlobalContext = globalCtx;
                }

                // Populate assigned beats from SharedData (set by Orchestrator)
                if (context.SharedData.TryGetValue("currentPhaseBeats", out var beatObj) && beatObj is List<string> phaseBeats)
                {
                    phaseContext.AssignedBeats = phaseBeats;
                }
                // Fallback to full beat distribution map
                else if (context.SharedData.TryGetValue("beatDistribution", out var beatDistObj2)
                    && beatDistObj2 is Dictionary<string, List<string>> beatDist2
                    && beatDist2.TryGetValue(phase.Id, out var beatPoints))
                {
                    phaseContext.AssignedBeats = beatPoints;
                }

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

                // Build or get cached pattern-specific system prompt
                var systemPrompt = GetOrBuildSystemPrompt(context, phase);

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

    /// <summary>
    /// Get or build the cached system prompt for this session/pattern combination.
    /// System prompts are static per session and can be reused across all phases.
    /// </summary>
    private string GetOrBuildSystemPrompt(GenerationContext context, PhaseDefinition phase)
    {
        var cacheKey = $"{context.SessionId}_{context.Pattern.Name}";

        if (_cachedSystemPromptKey == cacheKey && _cachedSystemPrompt != null)
        {
            return _cachedSystemPrompt;
        }

        _cachedSystemPrompt = BuildEnhancedSystemPrompt(context, phase);
        _cachedSystemPromptKey = cacheKey;
        return _cachedSystemPrompt;
    }

    private string BuildEnhancedSystemPrompt(GenerationContext context, PhaseDefinition phase)
    {
        var parts = new List<string>();

        // Identity and base role
        parts.Add("Anda adalah penulis script video essay profesional dengan keahlian dalam storytelling intelektual-edukatif.");
        parts.Add($"Gaya tulisan: {context.Pattern.GlobalRules.Tone}");
        parts.Add($"Bahasa: {context.Pattern.GlobalRules.Language}");

        // Channel-specific enhancement for Jazirah Ilmu style
        if (context.Pattern.Name == "jazirah-ilmu" ||
            context.Config.ChannelName?.Contains("Jazirah", StringComparison.OrdinalIgnoreCase) == true)
        {
            parts.Add("");
            parts.Add("=== INSTRUKSI KHUSUS GOLDEN STANDARD (JAZIRAH ILMU) ===");
            parts.Add("");
            parts.Add("ROLE: Investigative Documentary Journalist (Video Essayist). Bukan penceramah, bukan motivator.");
            parts.Add("");
            parts.Add("GAYA BAHASA & TONE:");
            parts.Add("- SMART BUT ACCESSIBLE: Cerdas tapi bisa dimengerti anak SMA. Jangan 'sok ilmiah'.");
            parts.Add("- HINDARI KESAN 'PRETENTIOUS': Dilarang menggunakan istilah teknis/sains jika ada padanan kata umum (misal: jangan pakai 'variabel determinan', pakai 'faktor penentu').");
            parts.Add("- DATA-DRIVEN: Setiap klaim harus didukung bukti, tapi sampaikan datanya secara natural (seperti bercerita), bukan seperti laporan statistik.");
            parts.Add("- VOCABULARY: Gunakan Bahasa Indonesia baku yang ENARIC (Enak Dibaca & Didengar).");
            parts.Add("");
            parts.Add("STRUKTUR NARASI (PROGRESSIVE):");
            parts.Add("- JANGAN CIRCULAR: Jangan ulangi poin yang sama. Setiap paragraf harus membuka layer baru.");
            parts.Add("- ESCALATION: Mulai dari masalah kecil -> masalah sistemik -> krisis eksistensial.");
            parts.Add("- KONTEKS MIKRO-MAKRO: Hubungkan cerita personal dengan fenomena global/sejarah.");
            parts.Add("");
            parts.Add("PERSPEKTIF:");
            parts.Add("- 'TELL THE TRUTH': Bongkar paradoks dan ironi yang tidak nyaman.");
            parts.Add("- 'SHOW, DON'T TELL': Jangan bilang 'ini menyedihkan', tapi ceritakan detail kejadiannya.");
            parts.Add("");
            parts.Add("INSTRUKSI KHUSUS:");
            parts.Add("1. WAJIB sertakan minimal 1 FAKTA UNIK/HISTORIS yang jarang diketahui di setiap fase.");
            parts.Add("2. Gunakan Metafora Visual hanya jika benar-benar perlu untuk menjelaskan konsep abstrak.");
            parts.Add("3. Penutup harus rendah hati (Humility), mengakui keterbatasan manusia di hadapan Tuhan.");
            // Note: Closing formula (Wallahu a'lam, etc.) is handled by PromptBuilder for final phase only
        }

        // General rules
        if (!string.IsNullOrEmpty(context.Config.ChannelName))
        {
            parts.Add("");
            parts.Add($"Channel: {context.Config.ChannelName}");
        }

        if (!string.IsNullOrEmpty(context.Pattern.GlobalRules.Perspective))
            parts.Add($"Perspektif: {context.Pattern.GlobalRules.Perspective}");

        // Technical constraints
        parts.Add("");
        parts.Add("=== BATASAN TEKNIS ===");

        if (!string.IsNullOrEmpty(context.Pattern.GlobalRules.MaxWordsPerSentence))
            parts.Add($"Maksimal kata per kalimat: {context.Pattern.GlobalRules.MaxWordsPerSentence}");
        if (!string.IsNullOrEmpty(context.Pattern.GlobalRules.PreferredWordsPerSentence))
            parts.Add($"Kata per kalimat: {context.Pattern.GlobalRules.PreferredWordsPerSentence}");
        if (!string.IsNullOrEmpty(context.Pattern.GlobalRules.MustUseKeywords))
            parts.Add($"Kata kunci yang digunakan: {context.Pattern.GlobalRules.MustUseKeywords}");
        if (context.Pattern.GlobalRules.HonorificsRequired == "true")
            parts.Add("Gunakan honorifik lengkap (SAW, AS, RA, SWT, dll).");

        // Output instructions
        parts.Add("");
        parts.Add("=== INSTRUKSI OUTPUT ===");
        parts.Add("- Tulis konten script langsung, TANPA markup, header, atau metadata");
        parts.Add("- JANGAN sebutkan label struktur seperti 'Analisis Layer 1', 'Poin 1', atau 'Bagian A'. Langsung masuk ke pembahasannya.");
        parts.Add("- Tulis dalam paragraf narasi yang mengalir natural");
        parts.Add("- JANGAN gunakan bullet point, numbering, atau formatting khusus");
        parts.Add("- Fokus pada kualitas tulisan dan kedalaman konten");
        parts.Add("- Hindari frasa AI yang klise: 'menelusuri jejak', 'berdenyut', 'tergelar', 'memeluk makna'");
        parts.Add("- Ganti dengan bahasa lugas: 'mari kita pelajari', 'perhatikan bahwa', 'analisis ini menunjukkan'");

        return string.Join("\n", parts);
    }
}
