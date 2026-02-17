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
/// Enhanced with pattern-specific system prompt generation.
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

                // Build pattern-specific system prompt
                var systemPrompt = BuildEnhancedSystemPrompt(context, phase);

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
            parts.Add("=== KARAKTERISTIK GAYA JAZIRAH ILMU ===");
            parts.Add("");
            parts.Add("GAYA BAHASA:");
            parts.Add("- Gunakan bahasa Indonesia formal-naratif yang lugas dan menghindari ornamentasi berlebihan");
            parts.Add("- Prioritaskan SUBSTANSI pemikiran dan kedalaman argumentasi");
            parts.Add("- Gunakan kontras dan paradoks untuk memancing pemikiran kritis");
            parts.Add("- Variasikan panjang kalimat: pendek (5-10 kata) untuk impak, panjang (20-30 kata) untuk elaborasi");
            parts.Add("- Gunakan kata transisi kontras: namun, tetapi, justru, sebaliknya, ironisnya, di balik, tersembunyi");
            parts.Add("");
            parts.Add("STRUKTUR NARASI:");
            parts.Add("- Hook: Mulai dengan kontras antara realitas surface vs retakan tersembunyi");
            parts.Add("- Kontekstualisasi: Bangun pemahaman bertahap dengan data sebagai penopang cerita");
            parts.Add("- Multi-dimensi: Eksplorasi historis, religius, psikologis, dan geopolitik secara berlapis");
            parts.Add("- Climax: Momen kesadaran dengan metafora kuat yang menghantam");
            parts.Add("- Penutup: Refleksi eskatologis dengan pertanyaan terbuka");
            parts.Add("");
            parts.Add("TEKNIK PENULISAN:");
            parts.Add("- Pertanyaan retoris yang mengarahkan pemikiran, bukan sekadar drama");
            parts.Add("- Metafora visual SANGAT selektif (maksimal 1-2 per fase) hanya untuk momen puncak");
            parts.Add("- Jeda nafas emosional setelah 2-3 paragraf intensitas tinggi");
            parts.Add("- Gunakan triada (pola tiga) untuk ritme: 'bukan..., bukan..., melainkan...'");
            parts.Add("");
            parts.Add("PERSPEKTIF:");
            parts.Add("- Kritis terhadap kekuasaan, empati pada yang tertindas");
            parts.Add("- Narator sebagai pembimbing pemikiran, bukan pengajar");
            parts.Add("- Fokus pada analisis dan eksplanasi atas estetika kata-kata");
            parts.Add("");
            parts.Add("PENUTUP WAJIB (Fase Terakhir):");
            parts.Add("- Wallahu a'lam bish-shawab");
            parts.Add("- Semoga kisah ini bermanfaat. Lebih dan kurangnya mohon dimaafkan.");
            parts.Add("- Yang benar datangnya dari Allah Subhanahu wa ta'ala. Khilaf datangnya dari saya pribadi.");
            parts.Add("- Sampai ketemu di kisah-kisah seru yang penuh makna selanjutnya.");
            parts.Add("- Wassalamualaikum warahmatullahi wabarakatuh.");
        }
        
        // General rules
        if (!string.IsNullOrEmpty(context.Config.ChannelName))
            parts.Add($"");
            parts.Add($"Channel: {context.Config.ChannelName}");

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
        parts.Add("- Tulis dalam paragraf narasi yang mengalir natural");
        parts.Add("- JANGAN gunakan bullet point, numbering, atau formatting khusus");
        parts.Add("- Fokus pada kualitas tulisan dan kedalaman konten");
        parts.Add("- Hindari frasa AI yang klise: 'menelusuri jejak', 'berdenyut', 'tergelar', 'memeluk makna'");
        parts.Add("- Ganti dengan bahasa lugas: 'mari kita pelajari', 'perhatikan bahwa', 'analisis ini menunjukkan'");

        return string.Join("\n", parts);
    }
}
