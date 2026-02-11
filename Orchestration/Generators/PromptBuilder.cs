using BunbunBroll.Models;
using BunbunBroll.Orchestration.Context;

namespace BunbunBroll.Orchestration.Generators;

/// <summary>
/// Builds LLM prompts from pattern configuration.
/// Incorporates all pattern rules: globalRules, customRules, closingFormula, productionChecklist.
/// </summary>
public class PromptBuilder
{
    /// <summary>
    /// Build generation prompt for a phase
    /// </summary>
    public string BuildPrompt(
        PhaseDefinition phase,
        GenerationContext context,
        PhaseContext phaseContext)
    {
        var promptParts = new List<string>();

        // 1. System instruction (topic, channel, duration)
        promptParts.Add(BuildSystemInstruction(context));

        // 2. Phase-specific guidance (with customRules)
        promptParts.Add(BuildPhaseGuidance(phase, context));

        // 3. Context from previous phases
        if (phaseContext.PreviousContent != null)
        {
            promptParts.Add(BuildPreviousPhaseContext(phaseContext));
        }

        // 4. Entity tracking context (if available)
        if (phaseContext.EntityContext != null)
        {
            promptParts.Add(phaseContext.EntityContext);
        }

        // 5. Assigned beats (if available)
        if (phaseContext.AssignedBeats.Count > 0)
        {
            promptParts.Add(BuildAssignedBeats(phaseContext.AssignedBeats));
        }

        // 6. Requirements (requiredElements, forbiddenPatterns, closingFormula)
        promptParts.Add(BuildRequirements(phase, context));

        // 7. Output format + writing quality guidelines
        promptParts.Add(BuildOutputFormatInstructions(phase, context));

        return string.Join("\n\n", promptParts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    /// <summary>
    /// Build regeneration prompt with validation feedback
    /// </summary>
    public string BuildRegenerationPrompt(
        PhaseDefinition phase,
        GenerationContext context,
        PhaseContext phaseContext,
        string feedback)
    {
        var promptParts = new List<string>
        {
            "=== REGENERASI KONTEN ===",
            "Konten sebelumnya tidak memenuhi validasi. Berikut adalah feedback yang harus diperbaiki:",
            feedback,
            "",
            "Silakan generate ulang konten dengan memperbaiki masalah di atas."
        };

        var basePrompt = BuildPrompt(phase, context, phaseContext);

        return string.Join("\n\n", promptParts) + "\n\n" + basePrompt;
    }

    private string BuildSystemInstruction(GenerationContext context)
    {
        var instructions = new List<string>
        {
            "### SYSTEM INSTRUCTION",
            $"Topik: {context.Config.Topic}",
            $"Target Durasi: {context.Config.TargetDurationMinutes} menit"
        };

        if (!string.IsNullOrEmpty(context.Config.ChannelName))
        {
            instructions.Add($"Channel: {context.Config.ChannelName}");
            instructions.Add($"PENTING: Sebutkan nama channel \"{context.Config.ChannelName}\" saat salam pembuka di fase pertama.");
        }

        if (!string.IsNullOrEmpty(context.Config.Outline))
        {
            instructions.Add($"Outline: {context.Config.Outline}");
        }

        if (!string.IsNullOrEmpty(context.Config.SourceReferences))
        {
            instructions.Add($"Sumber: {context.Config.SourceReferences}");
        }

        return string.Join("\n", instructions);
    }

    private string BuildPhaseGuidance(PhaseDefinition phase, GenerationContext context)
    {
        var parts = new List<string>
        {
            $"### PHASE: {phase.Name} (Order {phase.Order})",
            $"Phase ID: {phase.Id}",
            $"Durasi Target: {phase.DurationTarget.Min}-{phase.DurationTarget.Max} detik",
            $"Target Kata: {phase.WordCountTarget.Min}-{phase.WordCountTarget.Max} kata"
        };

        if (!string.IsNullOrWhiteSpace(phase.GuidanceTemplate))
        {
            parts.Add($"\nGuidance:\n{phase.GuidanceTemplate}");
        }

        if (!string.IsNullOrWhiteSpace(phase.TransitionHint) && !phase.IsFirstPhase)
        {
            parts.Add($"\nTransition Hint: {phase.TransitionHint}");
        }

        // Include customRules as explicit instructions
        if (phase.CustomRules.Count > 0)
        {
            parts.Add("\n### ATURAN KHUSUS FASE INI");
            foreach (var rule in phase.CustomRules)
            {
                var ruleInstruction = FormatCustomRule(rule.Key, rule.Value, context);
                if (!string.IsNullOrEmpty(ruleInstruction))
                    parts.Add($"- {ruleInstruction}");
            }
        }

        if (phase.IsFinalPhase)
        {
            parts.Add("\n⚠️ IMPORTANT: This is the FINAL phase. Provide a conclusive ending, not a continuation.");
        }

        return string.Join("\n", parts);
    }

    /// <summary>
    /// Convert customRule key-value pairs into human-readable LLM instructions
    /// </summary>
    private string FormatCustomRule(string key, string value, GenerationContext context)
    {
        return key switch
        {
            "mustHaveGreeting" when value == "true" =>
                !string.IsNullOrEmpty(context.Config.ChannelName)
                    ? $"WAJIB: Mulai dengan salam pembuka dan sebutkan nama channel \"{context.Config.ChannelName}\""
                    : "WAJIB: Mulai dengan salam pembuka (Assalamualaikum)",
            "mustHaveAudienceAddress" when value == "true" =>
                "WAJIB: Sapa audiens secara personal (contoh: 'sahabat', 'saudara-saudaraku')",
            "cognitiveDisturbance" =>
                $"Tingkat gangguan kognitif: {value} — buat penonton terpancing rasa ingin tahu",
            "minNumericData" =>
                $"WAJIB: Sertakan minimal {value} data numerik/statistik relevan",
            "mustHaveHistoricalContext" when value == "true" =>
                "WAJIB: Sertakan konteks historis singkat",
            "minDimensions" =>
                $"WAJIB: Eksplorasi minimal {value} dimensi/perspektif berbeda",
            "mustUseLayering" when value != "false" =>
                "DISARANKAN: Gunakan teknik layering — narasi dominan → pendalaman makna → implikasi moral",
            "mustHaveRhetoricalQuestions" when value == "true" =>
                "WAJIB: Sertakan pertanyaan retoris di momen penting",
            "emotionalIntensity" =>
                $"Intensitas emosional: {value} — buat momen yang menghantam perasaan",
            "mustHaveDarkMetaphor" when value is "true" or "preferred" =>
                "DISARANKAN: Gunakan metafora gelap/kuat yang mudah divisualisasikan",
            "mustRevealHidden" when value == "true" =>
                "WAJIB: Ungkapkan kebenaran tersembunyi atau hal yang sering diabaikan",
            "mustHaveClosing" when value == "true" =>
                "WAJIB: Akhiri dengan penutup religius yang tenang",
            "mustConnectToUmmah" when value == "true" =>
                "WAJIB: Hubungkan penutup dengan kondisi umat secara universal",
            "openEnded" when value == "true" =>
                "WAJIB: Akhiri dengan pertanyaan terbuka yang mengundang refleksi, bukan jawaban final",
            _ => $"{key}: {value}"
        };
    }

    private string BuildPreviousPhaseContext(PhaseContext phaseContext)
    {
        return $"### KONTEKS PHASE SEBELUMNYA\n" +
               $"Phase Sebelumnya: {phaseContext.PreviousPhaseName}\n\n" +
               $"Ringkasan Konten:\n{phaseContext.PreviousContent}";
    }

    private string BuildAssignedBeats(List<string> beats)
    {
        return $"### STORY BEATS UNTUK PHASE INI\n" +
               string.Join("\n", beats.Select((b, i) => $"{i + 1}. {b}"));
    }

    private string BuildRequirements(PhaseDefinition phase, GenerationContext context)
    {
        var requirements = new List<string>();

        if (phase.RequiredElements.Count > 0)
        {
            requirements.Add("### ELEMEN YANG HARUS ADA");
            requirements.AddRange(phase.RequiredElements.Select(e => $"- [ ] {e}"));
        }

        if (phase.ForbiddenPatterns.Count > 0)
        {
            requirements.Add("\n### PATTERN YANG DILARANG");
            requirements.AddRange(phase.ForbiddenPatterns.Select(p => $"- JANGAN gunakan: {p}"));
        }

        // Add closing formula for final phase
        if (phase.IsFinalPhase && !string.IsNullOrEmpty(context.Pattern.ClosingFormula))
        {
            requirements.Add($"\n### FORMULA PENUTUP");
            requirements.Add($"WAJIB akhiri script dengan: \"{context.Pattern.ClosingFormula}\"");
        }

        return string.Join("\n", requirements);
    }

    private string BuildOutputFormatInstructions(PhaseDefinition phase, GenerationContext context)
    {
        var parts = new List<string>
        {
            @"### OUTPUT FORMAT

Output harus dalam format markdown dengan struktur berikut:

## [Nama Phase]

[Konten dalam bahasa Indonesia dengan gaya conversational]

Gunakan timestamp format [MM:SS] untuk momen penting.
Gunakan marker [Musik], [Efek] untuk transisi audio.
Gunakan (dengan suara bergetar), (tertawa) untuk TTS emotion.

### TTS OPTIMIZATION
- Preferable: 20-30 kata per kalimat
- Maximum: 35 kata per kalimat
- Gunakan ellipsis (...) untuk pause dramatis
- Berikan jeda (double newline) untuk ganti paragraf

### WRITING STYLE
- Bahasa Indonesia conversational, bukan formal
- Gunakan marker: 'mari kita', 'sahabat', 'simak'
- Hormati honorifics lengkap (SAW, AS, SWT)"
        };

        // Add production checklist as writing guidelines
        if (context.Pattern.ProductionChecklist?.Penulisan?.Count > 0)
        {
            parts.Add("\n### CHECKLIST KUALITAS PENULISAN");
            foreach (var item in context.Pattern.ProductionChecklist.Penulisan)
            {
                parts.Add($"- {item}");
            }
        }

        if (phase.IsFinalPhase)
        {
            parts.Add("\n### FINAL PHASE REQUIREMENTS\n" +
                "- This is the CONCLUSION of the script\n" +
                "- Provide closure and final reflection\n" +
                "- Do NOT introduce new topics or cliffhangers\n" +
                "- End with a thoughtful, conclusive message");
        }

        return string.Join("\n", parts);
    }
}
