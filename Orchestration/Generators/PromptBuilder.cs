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

        // Note: Outline is distributed per-phase via OutlinePlanner, not placed here globally

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
            $"⚠️ BATAS KATA KERAS: JANGAN melebihi {phase.WordCountTarget.Max} kata. Konten yang melebihi batas akan DITOLAK. Lebih baik padat dan bermakna daripada panjang bertele-tele."
        };

        if (!string.IsNullOrWhiteSpace(phase.GuidanceTemplate))
        {
            parts.Add($"\nGuidance:\n{phase.GuidanceTemplate}");
        }

        if (!string.IsNullOrWhiteSpace(phase.EmotionalArc))
        {
            parts.Add($"\n### ALUR EMOSI FASE INI");
            parts.Add($"Ikuti kurva emosi berikut dalam penulisan: {phase.EmotionalArc}");
            parts.Add("Setiap transisi emosi (→) harus terasa NATURAL, bukan tiba-tiba. Gunakan kalimat jembatan emosional.");
        }

        if (!string.IsNullOrWhiteSpace(phase.TransitionHint) && !phase.IsFirstPhase)
        {
            parts.Add($"\n⚠️ TRANSISI WAJIB: Mulai fase ini dengan kalimat jembatan yang alami.");
            parts.Add($"Contoh transisi: \"{phase.TransitionHint}\"");
            parts.Add("Kamu BOLEH memodifikasi contoh di atas, tapi WAJIB ada kalimat penghubung di awal yang mengaitkan konteks sebelumnya ke konten baru.");
        }
        else if (!phase.IsFirstPhase)
        {
            parts.Add($"\n⚠️ TRANSISI WAJIB: Mulai fase ini dengan kalimat jembatan singkat yang menghubungkan konteks sebelumnya ke konten baru. Jangan langsung lompat ke materi baru tanpa pengait.");
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

        // Add channel greeting instruction ONLY for the first phase
        if (phase.IsFirstPhase && !string.IsNullOrEmpty(context.Config.ChannelName))
        {
            parts.Add($"\nNOTE: Saat memulai script ini (karena ini Fase 1), WAJIB menyapa pemirsa dengan menyebutkan nama channel \"{context.Config.ChannelName}\".");
        }
        else if (!phase.IsFirstPhase)
        {
            parts.Add("\n⛔ DILARANG: JANGAN ulangi salam pembuka (Assalamualaikum/Bismillah) atau menyebutkan nama channel lagi di fase ini. Salam pembuka sudah ada di fase pertama. Langsung mulai dengan kalimat jembatan/transisi ke konten baru.");
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

Gunakan timestamp format [MM:SS] HANYA di AWAL kalimat atau paragraf baru, JANGAN di tengah atau akhir kalimat.
Contoh yang BENAR: [01:10] Namun, untuk memahami mengapa riba begitu berbahaya...
Contoh yang SALAH: Namun, untuk memahami mengapa riba begitu berbahaya... [01:10]
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
- Hormati honorifics lengkap (SAW, AS, SWT)

### KESEIMBANGAN GAYA: INTELEKTUAL dengan SENTUHAN PUITIS MINIMAL
- MAYORITAS kalimat (90-95%) harus NATURAL, ANALITIS, dan TO THE POINT — fokus pada penyampaian ide dan argumen dengan jelas
- Gunakan pendekatan EDUKATIF: jelaskan konsep, berikan konteks, analisis sebab-akibat, bandingkan perspektif
- Kalimat puitis/metaforis BOLEH digunakan SANGAT SESEKALI (1 kali per fase, maksimal 2 di fase panjang) HANYA di momen puncak emosi yang sangat penting
- JANGAN membuka kalimat dengan metafora — mulailah dengan fakta, pertanyaan pemikir, atau pernyataan analitis
- HINDARI frasa-frasa 'halus AI' yang berlebihan seperti: 'menelusuri jejak', 'berdenyut', 'tergelar di hadapan', 'memeluk makna', 'merangkum hikmah', 'membentang cakrawala', 'menyelinap', 'menyatu dalam harmoni', 'meretas batas'
- GANTI dengan bahasa yang lugas: 'mari kita pelajari', 'perhatikan bahwa', 'analisis ini menunjukkan', 'fakta yang menarik adalah'
- Contoh BURUK: 'Selamat datang di ruang bagi kita untuk menelusuri jejak-jejak masa lalu yang masih berdenyut hingga hari ini.'
- Contoh BAIK: 'Mari kita telusuri fakta sejarah ini — karena di dalamnya tersimpan pelajaran yang masih relevan hingga kini.'
- Prinsip: tulis seolah kamu MEMBIMBING diskusi intelektual dengan teman yang cerdas — prioritaskan SUBSTANSI pemikiran atas keindahan kata-kata"
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
            "### ATURAN KUALITAS NARASI",
            "",
            "#### KONSISTENSI",
            "- Jika menyebutkan alat, istilah, atau atribut (mis. cincin Sulaiman → cahaya beriman, tongkat Musa → tanda hitam kafir), TETAPKAN satu versi di awal dan gunakan KONSISTEN.",
            "- JANGAN membalik peran/fungsi/atribut di paragraf selanjutnya.",
            "- Jika ada variasi riwayat, sebutkan SEKALI bahwa ada perbedaan pendapat, lalu pilih SATU versi untuk digunakan.",
            "",
            "#### ANTI-REDUNDANSI TEMATIK",
            "- Setiap fase memiliki FOKUS UNIK. JANGAN ulangi klaim dasar yang sama dari fase sebelumnya.",
            "- Contoh BURUK: mengulang 'Dabbah menyingkap topeng manusia' di setiap fase.",
            "- Contoh BAIK: Fase 2 jelaskan APA, Fase 3 jelaskan DAMPAK PSIKOLOGIS, Fase 4 jelaskan IMPLIKASI SOSIAL.",
            "",
            "#### PACING & DINAMIKA",
            "- Variasikan panjang kalimat: PENDEK (5-10 kata) untuk momen dramatis, PANJANG (20-30 kata) untuk narasi mengalir.",
            "- Gunakan jeda dramatis (...) SEBELUM momen puncak atau pengungkapan penting.",
            "- Turunkan intensitas sebelum klimaks, lalu naikkan tajam — buat kontras emosional.",
            "- Contoh: '...dan di sinilah... kebenaran itu terungkap.' (jeda → pengungkapan)",
            "",
            "#### JEDA NAFAS EMOSIONAL (BREATHING MARKS)",
            "- WAJIB: Setelah setiap 2-3 paragraf dengan intensitas tinggi, sisipkan 1-2 kalimat PELAN dan REFLEKTIF sebagai 'nafas' bagi pendengar.",
            "- WAJIB: Setelah kutipan ayat/hadits yang berat, beri 1 kalimat jeda perenungan sebelum melanjutkan.",
            "- WAJIB: Sebelum momen puncak (climax statement), TURUNKAN nada terlebih dahulu agar kontrasnya terasa kuat.",
            "- JANGAN: 5+ paragraf berturut-turut dengan intensitas tinggi — pendengar akan kebal dan kehilangan dampak.",
            "- Teknik jeda: Gunakan kalimat pendek bernada tenang, pertanyaan retoris lembut, atau deskripsi visual yang menenangkan.",
            "- PENTING: VARIASIKAN kalimat jeda Anda. JANGAN gunakan kalimat yang sama berulang kali.",
            "- Opsi Variasi (PILIH SATU atau BUAT SENDIRI, jangan diulang):",
            "  1. 'Tarik napas sejenak... dan rasakan beratnya fakta ini.'",
            "  2. 'Mari kita berhenti sebentar. Biarkan logika akal kita mencernanya.'",
            "  3. 'Bayangkan keheningan di detik itu...'",
            "  4. 'Di titik ini, ada baiknya kita bertanya pada nurani sendiri.'",
            "  5. 'Resapi kalimat tersebut. Bukan dengan telinga, tapi dengan hati.'",
            "",
            "#### KONSOLIDASI KUTIPAN",
            "- Kutip ayat Al-Quran, hadits, atau sumber referensi secara LENGKAP hanya SATU KALI di posisi paling strategis.",
            "- Setelah kutipan pertama, gunakan referensi singkat: 'sebagaimana ayat tadi', 'seperti yang telah disebutkan', dll.",
            "- JANGAN kutip teks yang sama secara penuh lebih dari sekali dalam keseluruhan script."
        };

        // Add transition bridge rule for non-first phases
        if (!phase.IsFirstPhase && phaseContext.PreviousContent != null)
        {
            rules.Insert(rules.IndexOf("", rules.IndexOf("#### ANTI-REDUNDANSI TEMATIK")), "#### KALIMAT JEMBATAN");
            rules.Insert(rules.IndexOf("#### KALIMAT JEMBATAN") + 1, "- WAJIB mulai fase ini dengan kalimat jembatan yang menghubungkan konteks sebelumnya ke konten baru.");
            rules.Insert(rules.IndexOf("- WAJIB mulai fase ini dengan kalimat jembatan yang menghubungkan konteks sebelumnya ke konten baru.") + 1, "- Contoh: 'Setelah kita tahu dari mana ia muncul, mari lihat rupa dan tanda yang dibawanya — karena di sanalah pesan ilahi itu terwujud.'");
            rules.Insert(rules.IndexOf("- Contoh: 'Setelah kita tahu dari mana ia muncul, mari lihat rupa dan tanda yang dibawanya — karena di sanalah pesan ilahi itu terwujud.'") + 1, "");
        }

        return string.Join("\n", rules);
    }
}
