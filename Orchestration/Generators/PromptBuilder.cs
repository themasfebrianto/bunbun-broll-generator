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

        // 6. Global Context (Anti-Repetition)
        if (phaseContext.GlobalContext != null && phaseContext.GlobalContext.Count > 0)
        {
            promptParts.Add(BuildGlobalContext(phaseContext.GlobalContext));
        }

        // 6. Assigned outline points (if available)
        if (phaseContext.AssignedOutlinePoints.Count > 0)
        {
            promptParts.Add(BuildAssignedOutline(phaseContext.AssignedOutlinePoints));
        }

        // 7. Requirements (requiredElements, forbiddenPatterns, closingFormula)
        promptParts.Add(BuildRequirements(phase, context));

        // 8. Narrative quality rules (consistency, anti-redundancy, transitions, pacing, citations)
        promptParts.Add(BuildNarrativeQualityRules(phase, phaseContext));

        // 9. Output format + writing quality guidelines
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
        }

        // Always include global outline for context
        if (!string.IsNullOrEmpty(context.Config.Outline))
        {
            instructions.Add($"Outline Global: {context.Config.Outline}");
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
            $"Target Kata: {phase.WordCountTarget.Min}-{phase.WordCountTarget.Max} kata",
            $"⚠️ BATAS KATA KERAS: JANGAN melebihi {phase.WordCountTarget.Max} kata."
        };

        if (!string.IsNullOrWhiteSpace(phase.GuidanceTemplate))
        {
            parts.Add($"\nGuidance:\n{phase.GuidanceTemplate}");
        }

        if (!string.IsNullOrWhiteSpace(phase.EmotionalArc))
        {
            parts.Add($"\n### ALUR EMOSI");
            parts.Add($"Kurva Emosi: {phase.EmotionalArc}");
            parts.Add("Transisi emosi harus mengalir alami (smooth liquid transition). Jangan patah-patah.");
        }

        if (!string.IsNullOrWhiteSpace(phase.TransitionHint) && !phase.IsFirstPhase)
        {
            parts.Add($"\n⚠️ TRANSISI: Mulai dengan 'Kyōkan Bridge' (Jembatan Empati).");
            parts.Add($"Contoh: \"{phase.TransitionHint}\"");
        }

        // Include customRules as explicit instructions
        if (phase.CustomRules.Count > 0)
        {
            parts.Add("\n### ATURAN KHUSUS");
            foreach (var rule in phase.CustomRules)
            {
                var ruleInstruction = FormatCustomRule(rule.Key, rule.Value, context);
                if (!string.IsNullOrEmpty(ruleInstruction))
                    parts.Add($"- {ruleInstruction}");
            }
        }

        // Add channel greeting instruction ONLY for the first phase
        bool isColdOpen = phase.CustomRules.TryGetValue("coldOpen", out var isCold) && isCold == "true";
        if (phase.IsFirstPhase && !string.IsNullOrEmpty(context.Config.ChannelName) && !isColdOpen)
        {
            parts.Add($"\nNOTE: Mulai dengan sapaan khas channel \"{context.Config.ChannelName}\".");
        }
        else if (!phase.IsFirstPhase)
        {
            parts.Add("\n⛔ NO GREETING: JANGAN mengulang salam pembuka. Langsung masuk ke konten.");
        }

        if (phase.IsFinalPhase)
        {
            parts.Add("\n⚠️ PHASE TERAKHIR: Berikan konklusi yang mendalam (open loop/reflective).");
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

    private string BuildAssignedOutline(List<string> outlinePoints)
    {
        return $"### OUTLINE UNTUK PHASE INI\n" +
               $"Poin-poin outline berikut HARUS tercakup dalam konten fase ini:\n" +
               string.Join("\n", outlinePoints.Select((p, i) => $"{i + 1}. {p}"));
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
        if (phase.IsFinalPhase)
        {
            requirements.Add($"\n### FORMULA PENUTUP");
            requirements.Add($"WAJIB akhiri script dengan kalimat persis berikut (Jazirah Ilmu Style):");
            requirements.Add("\"Wallahuam bissawab. Semoga kisah ini bermanfaat. Lebih dan kurangnya mohon dimaafkan. Yang benar datangnya dari Allah Subhanahu wa taala. Khilaf atau keliru itu datangnya dari saya pribadi sebagai manusia biasa. Sampai ketemu di kisah-kisah seru yang penuh makna selanjutnya. Saya akhiri wassalamualaikum warahmatullahi wabarakatuh.\"");
        }

        return string.Join("\n", requirements);
    }

    private string BuildOutputFormatInstructions(PhaseDefinition phase, GenerationContext context)
    {
        var parts = new List<string>
        {
            @"### OUTPUT FORMAT

Output harus SANGAT SEDERHANA.
HANYA tuliskan Timestamp dan Narasi.
JANGAN gunakan header (## Judul), JANGAN pakai label [Visual]/[Audio].
JANGAN gunakan label beat/meta-comments.

Format:
[00:00] Narasi dimulai di sini...

[00:15] Narasi berlanjut...

... dan seterusnya.

### ATURAN FORMAT PENTING:
1.  **No Headers**: JANGAN gunakan judul/header apapun. Langsung mulai dengan timestamp pertama.
2.  **No Labels**: Hapus semua label seperti `[Visual]`, `[Audio]`, `**The Domino Effect**`.
3.  **No Beat Labels**: Beat hanya untuk struktur pikiranmu. Jangan ditulis di output.
4.  **Meta-Comments**: Jangan masukkan instruksi audio/musik. Fokus pada teks yang akan dibaca.

### GAYA PENULISAN: PHILOSOPHICAL STORYTELLER (JAZIRAH ILMU STYLE)
- **Persona**: Kamu adalah seorang pencerita yang bijak, reflektif, dan mengajak pemirsa merenung (contemplative). Kamu bukan dosen yang kaku, tapi sahabat yang mengajak diskusi mendalam.
- **Tone**: Religius-Intelektual tapi Rendah Hati. Gunakan kata 'Kita' untuk merangkul pemirsa. Hindari nada menggurui.
- **Diction (Pilihan Kata)**:
    -   HINDARI: Istilah akademis kering ('signifikansi', 'implikasi', 'paradoks teologis', 'infrastruktur fisik').
    -   GUNAKAN: Kata-kata yang menyentuh hati ('titik rapuh', 'jejak', 'gema', 'kenyataan pahit', 'cahaya', 'kegelapan').
    -   HINDARI: Frasa 'AI-banget' ('menelusuri jejak', 'merajut makna', 'simfoni kehidupan'). Ganti dengan kalimat langsung yang kuat.
-   **Koneksi Emosional**: Setiap fakta harus punya relevansi emosional dengan pemirsa. Jangan cuma sampaikan data, sampaikan *rasanya*.
-   **Structure**: Mulai dari fenomena umum/filosofis -> Masuk ke topik spesifik -> Akhiri dengan refleksi moral/spiritual."
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

    private string BuildGlobalContext(List<string> globalContext)
    {
        var parts = new List<string>
        {
            "### KONTEKS GLOBAL (ANTI-REPETITION)",
            "Berikut adalah konten dari fase-fase sebelumnya. DILARANG MENGULANG poin/ide yang sudah dibahas:",
            string.Join("\n", globalContext.Select(c => $"- {c}")),
            "",
            "⚠️ ATURAN ANTI-PENGULANGAN:",
            "1. JANGAN ulangi klaim/tesis utama yang sudah disampaikan — cukup referensikan singkat jika perlu",
            "2. JANGAN kutip ayat/hadits/sumber yang sudah dikutip penuh di fase sebelumnya — gunakan referensi singkat saja",
            "3. JANGAN gunakan metafora atau frasa kunci yang sama — kembangkan metafora BARU",
            "4. JANGAN ceritakan ulang ANEKDOT/KISAH yang sama (misal: Kisah Nabi di Sirat). Jika sudah ada, cukup referensikan ('Ingatkah doa Nabi tadi?').",
            "5. Setiap fase harus membawa PERSPEKTIF/SUDUT PANDANG BARU (psikologis, sosial, etis, spiritual, historis)",
            "6. Jika ide dasar sama, kembangkan ASPEK BERBEDA — bukan mengulang dengan kata berbeda"
        };

        return string.Join("\n", parts);
    }

    private string BuildNarrativeQualityRules(PhaseDefinition phase, PhaseContext phaseContext)
    {
        var rules = new List<string>
        {
            "### ATURAN KUALITAS NARASI (JAZIRAH ILMU STYLE)",
            "",
            "#### ⛔ STRICT NEGATIVE CONSTRAINTS (JANGAN LAKUKAN)",
            "1. **NO META-LABELS**: JANGAN PERNAH mengucapkan label struktur seperti 'Ini adalah The Domino Effect' atau 'Kita masuk ke Critical Junction'. Label itu hanya untuk struktur pikiranmu.",
            "2. **NO ENGLISH TERMS**: Hindari istilah Inggris jika ada padanan Indonesia yang kuat (kecuali nama diri/tempat). Jangan pakai 'Cognitive Dissonance', pakai 'pertentangan batin'.",
            "3. **NO ADVERB CLUTTER**: Kurangi kata 'sesungguhnya', 'sejatinya', 'niscaya' yang berlebihan.",
            "",
            "#### STORYTELLING FLOW",
            "- **Show, Don't Just Tell**: Jangan cuma bilang 'neraka itu seram'. Deskripsikan suaranya, panasnya, sempitnya.",
            "- **The 'Kita' Perspective**: Selalu posisikan diri 'kita' (pembicara dan pendengar) di perahu yang sama.",
            "- **Pacing Dinamis**: Gunakan kalimat pendek (3-5 kata) untuk *punch* emosional. Gunakan kalimat panjang untuk penjelasan mengalir.",
            "- **Natural Transitions**: Perpindahan antar poin harus terasa seperti alur cerita yang menyambung.",
            "",
            "#### JEDA NAFAS (BREATHING SPACES)",
            "- **Reflective Pause**: Setelah fakta berat/mengerikan, berikan 1 kalimat jeda untuk pemirsa mencerna. Contoh: 'Tarik napas sejenak... bayangkan jika itu kita.'",
            "- **Variasi**: Jangan gunakan kalimat jeda yang sama berulang kali.",
            "",
            "#### REFERENSI & KUTIPAN",
            "- Kutip ayat/hadits dengan hormat. Terjemahkan intinya ke dalam alur cerita.",
            "- Jangan biarkan kutipan memutus flow emosi."
        };

        // Add transition bridge rule for non-first phases
        if (!phase.IsFirstPhase && phaseContext.PreviousContent != null)
        {
            rules.Add("");
            rules.Add("#### TRANSISI WAJIB (BRIDGE)");
            rules.Add("- WAJIB mulai fase ini dengan kalimat jembatan yang menghubungkan akhir fase sebelumnya dengan awal fase ini.");
            rules.Add("- Jangan melompat topik tiba-tiba. Kaitkan dengan apa yang baru saja dibahas.");
        }

        return string.Join("\n", rules);
    }
}
