using BunbunBroll.Models;

namespace BunbunBroll.Orchestration.Generators;

/// <summary>
/// Centralized rule rendering logic.
/// Converts rule key-value pairs into human-readable LLM instructions.
/// Consolidates logic previously scattered across PromptBuilder and PhaseCoordinator.
/// </summary>
public class RuleRenderer
{
    /// <summary>
    /// Render a single custom rule as an instruction string.
    /// Returns empty string if rule should not produce output.
    /// </summary>
    public string RenderRule(string key, string value, GenerationContext context)
    {
        return key switch
        {
            // Greeting rules
            "mustHaveGreeting" when value == "true" =>
                !string.IsNullOrEmpty(context.Config.ChannelName)
                    ? $"WAJIB: Mulai dengan salam pembuka dan sebutkan nama channel \"{context.Config.ChannelName}\""
                    : "WAJIB: Mulai dengan salam pembuka (Assalamualaikum)",

            "mustHaveAudienceAddress" when value == "true" =>
                "WAJIB: Sapa audiens secara personal (contoh: 'sahabat', 'saudara-saudaraku')",

            // Cognitive/Content rules
            "cognitiveDisturbance" =>
                $"Tingkat gangguan kognitif: {value} — buat penonton terpancing rasa ingin tahu",

            "minNumericData" =>
                $"WAJIB: Sertakan minimal {value} data numerik/statistik relevan",

            "mustHaveConcreteData" when value == "true" =>
                "WAJIB: Sertakan data konkret (angka, tahun, nama, lokasi) — bukan generalisasi",

            "mustHaveHistoricalContext" when value == "true" =>
                "WAJIB: Sertakan konteks historis singkat",

            "minDimensions" =>
                $"WAJIB: Eksplorasi minimal {value} dimensi/perspektif berbeda",

            // Structure rules
            "mustUseLayering" when value == "false" =>
                string.Empty, // Explicitly disabled

            "mustUseLayering" =>
                "DISARANKAN: Gunakan teknik layering — narasi dominan → pendalaman makna → implikasi moral",

            "progressiveStakes" when value == "true" =>
                "WAJIB: Setiap paragraf harus menaikkan taruhan (raise the stakes)",

            "interdisciplinary" when value == "true" =>
                "DISARANKAN: Gunakan perspektif multidisiplin (Sains + Agama + Sejarah)",

            // Rhetorical/Emotional rules
            "mustHaveRhetoricalQuestions" when value == "true" =>
                "DISARANKAN: Sertakan pertanyaan retoris di momen penting",

            "emotionalIntensity" =>
                $"Intensitas emosional: {value} — buat momen yang menghantam perasaan",

            "hookStyle" =>
                $"Gaya Hook: {value} — mulai dengan cara yang memikat",

            // Metaphor rules
            "mustHaveDarkMetaphor" when value is "true" or "preferred" =>
                "DISARANKAN: Gunakan metafora gelap/kuat yang mudah divisualisasikan",

            "visualMetaphor" when value == "required" =>
                "WAJIB: Satu metafora visual yang menyentuh perasaan",

            "mustRevealHidden" when value == "true" =>
                "WAJIB: Ungkapkan kebenaran tersembunyi atau hal yang sering diabaikan",

            // Narrative mode
            "narrativeMode" =>
                $"Mode Narasi: {value} — sesuaikan gaya penulisan",

            "sentenceStyle" when value == "Staccato" =>
                "Gunakan kalimat pendek, tajam, dan ritmik (Staccato) untuk efek dramatis",

            "coldOpen" when value == "true" =>
                "COLD OPEN: Langsung masuk ke narasi/cerita TANPA salam pembuka",

            // Closing rules
            "mustHaveClosing" when value == "true" =>
                "WAJIB: Akhiri dengan penutup religius yang tenang",

            "mustConnectToUmmah" when value == "true" =>
                "WAJIB: Hubungkan penutup dengan kondisi umat secara universal",

            "openEnded" when value == "true" =>
                "WAJIB: Akhiri dengan pertanyaan terbuka yang mengundang refleksi, bukan jawaban final",

            "humility" when value == "max" =>
                "Sikap RENDAH HATI: Posisi narator sebagai teman berpikir, bukan guru moral",

            "lingeringThought" when value == "true" =>
                "WAJIB: Akhiri dengan open loop — pertanyaan yang dibaca pulang (lingering thought)",

            // Fallback for unknown rules
            _ => !string.IsNullOrEmpty(value) ? $"{key}: {value}" : string.Empty
        };
    }

    /// <summary>
    /// Render all custom rules from a phase as a list of instruction strings.
    /// Filters out empty/ignored rules.
    /// </summary>
    public IEnumerable<string> RenderAllRules(PhaseDefinition phase, GenerationContext context)
    {
        foreach (var rule in phase.CustomRules)
        {
            var rendered = RenderRule(rule.Key, rule.Value, context);
            if (!string.IsNullOrEmpty(rendered))
            {
                yield return rendered;
            }
        }
    }

    /// <summary>
    /// Check if a rule key should be rendered as an instruction.
    /// </summary>
    public bool ShouldRenderRule(string key, string value)
    {
        return key switch
        {
            "mustUseLayering" when value == "false" => false,
            _ => !string.IsNullOrEmpty(value)
        };
    }
}
