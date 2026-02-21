using BunbunBroll.Models;
using BunbunBroll.Services.Orchestration.Context;

namespace BunbunBroll.Services.Orchestration.Generators;

/// <summary>
/// Builds LLM prompts from pattern configuration.
/// Simplified to 4-part structure: TASK, RULES, STYLE (JI Patterns), OUTPUT FORMAT
/// </summary>
public class PromptBuilder
{
    private readonly RuleRenderer _ruleRenderer = new();

    /// <summary>
    /// Build generation prompt for a phase - simplified to 4 parts
    /// </summary>
    public string BuildPrompt(
        PhaseDefinition phase,
        GenerationContext context,
        PhaseContext phaseContext)
    {
        var promptParts = new List<string>
        {
            // PART 1: TASK (What to generate)
            BuildTaskSection(phase, context, phaseContext),

            // PART 2: RULES (Do's and Don'ts)
            BuildRulesSection(phase, context, phaseContext),

            // PART 3: STYLE (Jazirah Ilmu Patterns) - MOST IMPORTANT
            BuildStyleSection(phase, context),

            // PART 4: OUTPUT FORMAT
            BuildOutputFormatSection(phase)
        };

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
        var header = new List<string>
        {
            "=== REGENERASI KONTEN ===",
            "Konten sebelumnya tidak memenuhi validasi. Berikut adalah feedback yang harus diperbaiki:",
            feedback,
            "",
            "Silakan generate ulang konten dengan memperbaiki masalah di atas."
        };

        var basePrompt = BuildPrompt(phase, context, phaseContext);

        return string.Join("\n\n", header) + "\n\n" + basePrompt;
    }

    #region PART 1: TASK

    private string BuildTaskSection(PhaseDefinition phase, GenerationContext context, PhaseContext phaseContext)
    {
        var parts = new List<string>
        {
            "════════════════════════════════════════════════════════════════",
            "                           PART 1: TUGAS                                   ",
            "════════════════════════════════════════════════════════════════",
            "",
            "### SYSTEM INSTRUCTION",
            $"Topik: {context.Config.Topic}",
            $"Target Durasi: {context.Config.TargetDurationMinutes} menit"
        };

        if (!string.IsNullOrEmpty(context.Config.ChannelName))
            parts.Add($"Channel: {context.Config.ChannelName}");

        if (!string.IsNullOrEmpty(context.Config.Outline))
            parts.Add($"Outline: {context.Config.Outline}");

        if (!string.IsNullOrEmpty(context.Config.SourceReferences))
            parts.Add($"Sumber: {context.Config.SourceReferences}");

        parts.Add("");
        parts.Add($"### PHASE: {phase.Name} (Order {phase.Order})");
        parts.Add($"Phase ID: {phase.Id}");
        parts.Add($"Durasi Target: {phase.DurationTarget.Min}-{phase.DurationTarget.Max} detik");
        parts.Add($"Target Kata: {phase.WordCountTarget.Min}-{phase.WordCountTarget.Max} kata");
        parts.Add($"⚠️ BATAS KATA KERAS: JANGAN melebihi {phase.WordCountTarget.Max} kata.");

        if (!string.IsNullOrWhiteSpace(phase.GuidanceTemplate))
            parts.Add($"\nGuidance:\n{phase.GuidanceTemplate}");

        // Add assigned beats if available
        if (phaseContext.AssignedBeats.Count > 0)
        {
            parts.Add("");
            parts.Add("### STORY BEATS UNTUK PHASE INI");
            foreach (var beat in phaseContext.AssignedBeats.Select((b, i) => $"{i + 1}. {b}"))
                parts.Add(beat);
        }

        // Add assigned outline if available
        if (phaseContext.AssignedOutlinePoints.Count > 0)
        {
            parts.Add("");
            parts.Add("### OUTLINE UNTUK PHASE INI");
            parts.Add("Poin-poin berikut HARUS tercakup:");
            foreach (var point in phaseContext.AssignedOutlinePoints.Select((p, i) => $"{i + 1}. {p}"))
                parts.Add(point);
        }

        return string.Join("\n", parts);
    }

    #endregion

    #region PART 2: RULES

    private string BuildRulesSection(PhaseDefinition phase, GenerationContext context, PhaseContext phaseContext)
    {
        var parts = new List<string>
        {
            "",
            "════════════════════════════════════════════════════════════════",
            "                           PART 2: ATURAN                                 ",
            "════════════════════════════════════════════════════════════════",
            ""
        };

        // Required elements
        if (phase.RequiredElements.Count > 0)
        {
            parts.Add("### ✅ ELEMEN YANG HARUS ADA");
            foreach (var element in phase.RequiredElements)
                parts.Add($"- [ ] {element}");
            parts.Add("");
        }

        // Forbidden patterns
        if (phase.ForbiddenPatterns.Count > 0)
        {
            parts.Add("### ❌ PATTERN YANG DILARANG");
            foreach (var pattern in phase.ForbiddenPatterns)
                parts.Add($"- {pattern}");
            parts.Add("");
        }

        // Add closing formula for final phase
        if (phase.IsFinalPhase)
        {
            parts.Add("### FORMULA PENUTUP (WAJIB)");
            parts.Add("Akhiri dengan persis seperti ini:");
            parts.Add("\"Wallahuam bissawab. Semoga kisah ini bermanfaat. Lebih dan kurangnya mohon dimaafkan. Yang benar datangnya dari Allah Subhanahu wa taala. Khilaf atau keliru itu datangnya dari saya pribadi sebagai manusia biasa. Sampai ketemu di kisah-kisah seru yang penuh makna selanjutnya. Saya akhiri wassalamualaikum warahmatullahi wabarakatuh.\"");
            parts.Add("");
        }

        // Anti-repetition context (only for non-first phases)
        if (phaseContext.GlobalContext != null && phaseContext.GlobalContext.Count > 0)
        {
            parts.Add("### ANTI-PENGULANGAN");
            parts.Add("Konten dari fase sebelumnya (DILARANG mengulang):");
            foreach (var item in phaseContext.GlobalContext.Select((c, i) => $"{i + 1}. {c}"))
                parts.Add($"- {item}");
            parts.Add("");
        }

        // Previous phase context (only for non-first phases)
        if (phaseContext.PreviousContent != null)
        {
            parts.Add("### KONTEKS FASE SEBELUMNYA");
            parts.Add($"Phase: {phaseContext.PreviousPhaseName}");
            parts.Add($"Ringkasan: {phaseContext.PreviousContent}");
            parts.Add("");
        }

        return string.Join("\n", parts);
    }

    #endregion

    #region PART 3: STYLE (Jazirah Ilmu Patterns)

    private string BuildStyleSection(PhaseDefinition phase, GenerationContext context)
    {
        var globalRules = context.Pattern.GlobalRules;
        var parts = new List<string>
        {
            "",
            "════════════════════════════════════════════════════════════════",
            "                     PART 3: GAYA JAZIRAH ILMU (PENTING!)         ",
            "════════════════════════════════════════════════════════════════",
            "",
            "### 4 POLA PENTING (CONTOH ASLI DARI SRT)",
            ""
        };

        // Pattern 1: Long flowing sentences
        parts.Add("**PATTERN 1: KALIMAT PANJANG MENGALIR (Wajib)**");
        parts.Add("❌ JANGAN: 'Kita sering merasa aman. Kita tidak sujud. Tapi lihat.'");
        parts.Add("✅ LAKUKAN: 'Dunia hari ini terlihat tenang. Layar menyala, pasar bergerak, teknologi berkembang seolah semuanya terkendali. Tapi di balik itu semua, ada satu titik rapuh yang menahan seluruh sistem agar tidak runtuh.'");
        parts.Add("✅ LAKUKAN: 'Sejak manusia mengenal kekuasaan, ada satu pola yang selalu berulang di hampir semua peradaban, yaitu penguasa tidak pernah puas hanya disebut kuat. Mereka ingin disebut sah, suci, dan ditakdirkan.'");
        parts.Add("");

        // Pattern 2: Minimal rhetorical questions
        parts.Add("**PATTERN 2: PERTANYAAN RETORIS MINIMAL (Maks 1-2 per phase)**");
        parts.Add("❌ JANGAN: Setiap 2-3 kalimat bertanya 'Apakah...?', 'Siapa...?', 'Kapan...?'");
        parts.Add("✅ LAKUKAN: Hanya di transisi penting - 'Maka pertanyaannya bukan lagi siapa yang benar di masa lalu, melainkan satu hal yang lebih jujur dan lebih menyakitkan...'");
        parts.Add("");

        // Pattern 3: Natural Indonesian transitions
        parts.Add("**PATTERN 3: TRANSISI INDONESIA NATURAL**");
        parts.Add("❌ JANGAN: '(Hening 3 detik)', '(Layar gelap gulita)', dramatic pauses");
        parts.Add("✅ LAKUKAN: 'Di titik ini...', 'Dari sinilah...', 'Namun di balik...', 'Di sinilah letak ironi terbesarnya...', 'Di balik itu, ada satu sisi yang jarang dibicarakan'");
        parts.Add("");

        // Pattern 4: Religious content last
        parts.Add("**PATTERN 4: KONTEN ISLAMI TERAKHIR**");
        parts.Add("❌ JANGAN: Kutip ayat/hadits di tengah analisis sebagai 'bukti' atau 'dalil'");
        parts.Add("✅ LAKUKAN: Sebut tokoh/kitab sebagai FAKTA SEJARAH. Konten islami eksplisit HANYA di closing formula.");
        parts.Add("");

        // Phase 1 specific reinforcement
        if (phase.IsFirstPhase)
        {
            parts.Add("════════════════════════════════════════════════════════════════");
            parts.Add("         ⚠️ PHASE 1 SPECIAL: CONTOH OPENING JAZIRAH ILMU ⚠️          ");
            parts.Add("════════════════════════════════════════════════════════════════");
            parts.Add("");
            parts.Add("Pelajari dan TIRU pola opening berikut untuk Phase 1:");
            parts.Add("");

            // Get opening examples from phase if available
            if (phase.OpeningExamples != null && phase.OpeningExamples.Count > 0)
            {
                foreach (var example in phase.OpeningExamples)
                {
                    parts.Add($"✅ {example}");
                    parts.Add("");
                }
            }
            else
            {
                // Fallback examples if not in pattern
                parts.Add("✅ CONTOH 1: 'Dunia hari ini terlihat tenang. Layar menyala, pasar bergerak, teknologi berkembang seolah semuanya terkendali. Tapi di balik itu semua, ada satu titik rapuh yang menahan seluruh sistem agar tidak runtuh, dan titik rapuh itu berada tepat di telapak tangan kita.'");
                parts.Add("");
                parts.Add("✅ CONTOH 2: 'Sejak manusia mengenal kekuasaan, ada satu pola yang selalu berulang di hampir semua peradaban, yaitu penguasa tidak pernah puas hanya disebut kuat. Mereka ingin disebut sah, suci, dan ditakdirkan.'");
                parts.Add("");
            }

            parts.Add("❌ JANGAN: 'Bayangkan jika setiap suara notifikasi... Kita sering merasa aman. Menunduk dalam. Khusyuk. Mata tak berkedip...'");
            parts.Add("");
            parts.Add("KUNCI: Mulai dengan kalimat PANJANG (3-5 klausa) yang mengalir, bukan kalimat pendek terpisah.");
            parts.Add("");
        }

        // Additional global rules (tone, voice, perspective, etc.)
        parts.Add("### ATURAN TAMBAHAN");
        if (!string.IsNullOrWhiteSpace(globalRules.Tone))
            parts.Add($"- **Tone**: {globalRules.Tone}");
        if (!string.IsNullOrWhiteSpace(globalRules.Voice))
            parts.Add($"- **Voice**: {globalRules.Voice}");
        if (!string.IsNullOrWhiteSpace(globalRules.Perspective))
            parts.Add($"- **Perspective**: {globalRules.Perspective}");
        if (!string.IsNullOrWhiteSpace(globalRules.Vocabulary))
            parts.Add($"- **Vocabulary**: {globalRules.Vocabulary}");
        if (!string.IsNullOrWhiteSpace(globalRules.IntellectualSurprise))
            parts.Add($"- **Intellectual Surprise**: {globalRules.IntellectualSurprise}");

        // Additional rules (authoritative, references, humility)
        if (globalRules.AdditionalRules != null && globalRules.AdditionalRules.Count > 0)
        {
            parts.Add("");
            foreach (var rule in globalRules.AdditionalRules)
                parts.Add($"- **{rule.Key}**: {rule.Value}");
        }

        parts.Add("");
        parts.Add("### ⛔ NEGATIVE CONSTRAINTS");
        parts.Add("- **NO META-LABELS**: JANGAN ucap label struktur seperti 'Ini adalah The Domino Effect'");
        parts.Add("- **NO ENGLISH TERMS**: Hindari istilah Inggris jika ada padanan Indonesia");
        parts.Add("- **NO AI CLUTTER**: Hindari 'menelusuri jejak', 'merajut makna', 'simfoni kehidupan'");

        return string.Join("\n", parts);
    }

    #endregion

    #region PART 4: OUTPUT FORMAT

    private string BuildOutputFormatSection(PhaseDefinition phase)
    {
        var parts = new List<string>
        {
            "",
            "════════════════════════════════════════════════════════════════",
            "                        PART 4: FORMAT OUTPUT                          ",
            "════════════════════════════════════════════════════════════════",
            "",
            "### FORMAT OUTPUT",
            "",
            "HANYA tulis Timestamp dan Narasi. TANPA header, label, atau komentar.",
            "",
            "Contoh narasi biasa:",
            "[00:00] Narasi dimulai di sini...",
            "",
            "Contoh Text Overlay (Untuk Ayat, Hadits, atau Kutipan Pendek):",
            "[OVERLAY:QuranVerse]",
            "[ARABIC] بِسْمِ اللَّهِ الرَّحْمَنِ الرَّحِيمِ",
            "[REF] Surah Al-Fatiha 1:1",
            "[00:15] Dengan menyebut nama Allah Yang Maha Pengasih lagi Maha Penyayang.",
            "",
            "Contoh KeyPhrase Overlay (Kutipan Pendek/Punchy, WAJIB pakai [TEXT]):",
            "[OVERLAY:KeyPhrase]",
            "[TEXT] Musuh Terbesar Adalah Ketakutan",
            "[00:30] Narasi lengkap di sini yang akan dibacakan oleh voice over...",
            "",
            "PENTING TENTANG OVERLAY:",
            "1. Teks narasi di bawah tag `OVERLAY` akan DIBACAKAN ALOUD oleh Voice Over.",
            "2. Khusus untuk Quran/Hadits, Voice Over HANYA membacakan terjemahan Bahasa Indonesianya saja. JADI, jangan tulis teks bahasa Arab di baris narasi!",
            "3. Teks Arab khusus ditaruh di tag `[ARABIC]` (opsional, tidak dibacakan, hanya untuk ditampilkan di layar).",
            "4. Referensi ditaruh di tag `[REF]` (opsional, hanya untuk layar).",
            "5. Tipe overlay yang didukung: QuranVerse, Hadith, RhetoricalQuestion, KeyPhrase.",
            "6. Untuk KeyPhrase: WAJIB sertakan `[TEXT]` berisi frasa pendek dan punchy (maks 5-8 kata). JANGAN copy paste narasi panjang ke [TEXT].",
            "",
            "### ⛔ KONSTRAIN FORMAT",
            "- JANGAN gunakan header (## Judul)",
            "- JANGAN gunakan label [Visual], [Audio], **The Domino Effect**",
            "- JANGAN gunakan meta-comments (instruksi musik/suara)"
        };

        // Final phase requirements
        if (phase.IsFinalPhase)
        {
            parts.Add("");
            parts.Add("### CATATAN PHASE TERAKHIR");
            parts.Add("- Ini adalah KONKLUSI script");
            parts.Add("- Berikan penutup yang mendalam (open loop/reflective)");
            parts.Add("- JANGAN perkenalkan topik baru");
        }

        return string.Join("\n", parts);
    }

    #endregion
}
