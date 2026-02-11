using BunbunBroll.Models;
using BunbunBroll.Orchestration.Context;

namespace BunbunBroll.Orchestration.Generators;

/// <summary>
/// Builds LLM prompts from pattern configuration.
/// Ported from ScriptFlow's PromptBuilder with full 7-section prompt structure.
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

        // 1. System instruction
        promptParts.Add(BuildSystemInstruction(context));

        // 2. Phase-specific guidance
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

        // 6. Requirements
        promptParts.Add(BuildRequirements(phase));

        // 7. Output format instructions
        promptParts.Add(BuildOutputFormatInstructions(phase));

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

        if (phase.IsFinalPhase)
        {
            parts.Add("\n⚠️ IMPORTANT: This is the FINAL phase. Provide a conclusive ending, not a continuation.");
        }

        return string.Join("\n", parts);
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

    private string BuildRequirements(PhaseDefinition phase)
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

        return string.Join("\n", requirements);
    }

    private string BuildOutputFormatInstructions(PhaseDefinition phase)
    {
        var baseInstructions = @"### OUTPUT FORMAT

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
- Hormati honorifics lengkap (SAW, AS, SWT)";

        if (phase.IsFinalPhase)
        {
            baseInstructions += "\n\n### FINAL PHASE REQUIREMENTS\n" +
                "- This is the CONCLUSION of the script\n" +
                "- Provide closure and final reflection\n" +
                "- Do NOT introduce new topics or cliffhangers\n" +
                "- End with a thoughtful, conclusive message";
        }

        return baseInstructions;
    }
}
