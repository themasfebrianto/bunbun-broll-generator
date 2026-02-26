using System.Text.Json;
using System.Text.RegularExpressions;
using BunbunBroll.Models;
using Microsoft.Extensions.Logging;

namespace BunbunBroll.Services;

/// <summary>
/// Generates multiple project configs using LLM based on a theme.
/// Optimized for 'ScriptFlow' automated video production.
/// </summary>
public class ConfigBatchGenerator
{
    private readonly IIntelligenceService _intelligenceService;
    private readonly ILogger<ConfigBatchGenerator> _logger;

    public ConfigBatchGenerator(IIntelligenceService intelligenceService, ILogger<ConfigBatchGenerator> logger)
    {
        _intelligenceService = intelligenceService;
        _logger = logger;
    }

    public async Task<List<GeneratedConfig>> GenerateConfigsAsync(string theme, string channelName, int count, ScriptPattern? pattern, string? seed = null, Action<int, int>? onProgress = null, CancellationToken cancellationToken = default)
    {
        var generatedConfigs = new List<GeneratedConfig>();
        var generatedTopics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        _logger.LogInformation("Starting sequential generation of {Count} configs for theme '{Theme}' using pattern '{Pattern}'", count, theme, pattern?.Name ?? "Unknown");

        if (pattern == null)
        {
            throw new ArgumentNullException(nameof(pattern), "Pattern configuration is missing or invalid. Batch generation requires a valid pattern.");
        }

        for (int i = 0; i < count; i++)
        {
            int currentNumber = i + 1;
            bool success = false;
            int retryCount = 0;

            // Determine assigned topic and formula based on index
            var exampleTopics = pattern.Configuration.ExampleTopics;
            bool hasTopics = exampleTopics != null && exampleTopics.Count > 0;
            string? assignedTopic = hasTopics ? exampleTopics[i % exampleTopics!.Count] : null;
            
            int formulaIndex = i % 10; // 0 to 9
            string assignedFormula = GetTitleFormula(formulaIndex);

            while (!success && retryCount < 3)
            {
                try
                {
                    onProgress?.Invoke(currentNumber, count);

                    // 1. Build context-aware prompt focusing on CREDIBLE SOURCES and PATTERN STRUCTURE
                    var prompt = BuildSingleConfigPrompt(theme, channelName, seed, generatedTopics, pattern, assignedTopic, assignedFormula);

                    _logger.LogInformation("Generating config {Current}/{Total} (Attempt {Retry})", currentNumber, count, retryCount + 1);

                    // 2. Call LLM
                    var response = await _intelligenceService.GenerateContentAsync(
                        systemPrompt: "You are a creative Director for a high-end Islamic Documentary YouTube channel. You prioritize ACCURACY (Dalil/Sources) and Storytelling. You output strictly valid JSON.",
                        userPrompt: prompt,
                        maxTokens: 2500,
                        temperature: 0.85,
                        cancellationToken: cancellationToken);

                    if (string.IsNullOrEmpty(response)) throw new InvalidOperationException("Empty response from LLM");

                    // 3. Parse Response
                    var config = ParseSingleConfig(response);

                    if (config != null)
                    {
                        // Post-processing & Validation
                        config.ChannelName = channelName;
                        if (config.Topic.Length > 100) config.Topic = config.Topic.Substring(0, 97) + "...";

                        generatedConfigs.Add(config);
                        generatedTopics.Add(config.Topic);
                        success = true;

                        Console.WriteLine($"[SUCCESS] Generated: {config.Topic}");

                        // Delay to prevent Rate Limits
                        if (i < count - 1) await Task.Delay(1500, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    retryCount++;
                    _logger.LogWarning("Failed to generate config {Current}: {Message}. Retrying...", currentNumber, ex.Message);

                    // Exponential backoff
                    await Task.Delay(2000 * retryCount, cancellationToken);
                }
            }
        }

        return generatedConfigs;
    }

    private string BuildSingleConfigPrompt(string theme, string channelName, string? seed, HashSet<string> existingTopics, ScriptPattern pattern, string? assignedTopic, string assignedFormula)
    {
        var context = existingTopics.Any()
            ? $"\nCONTEXT - DO NOT REPEAT THESE TOPICS:\n- {string.Join("\n- ", existingTopics)}"
            : "";

        var assignedTopicInstruction = assignedTopic != null
            ? $"DEVELOP video dari topik ini secara spesifik: '{assignedTopic}' (Tema umum: '{theme}')"
            : $"Theme: '{theme}'.";

        // Build phase beat templates
        var templateBuilder = new PhaseBeatTemplateBuilder();
        var phaseTemplates = templateBuilder.BuildTemplatesFromPattern(pattern.Configuration);
        var beatTemplateSection = string.Join("\n", phaseTemplates.Select(t => t.GetBeatPrompt()));

        return $@"
Generate 1 (ONE) unique video configuration JSON for channel '{channelName}'.
{assignedTopicInstruction}
Language: INDONESIAN (Bahasa Indonesia) for Topic, Outline, and Beats.
{context}
Seed/Instruction: {seed ?? "None"}

=== THEME GUIDANCE ===
{GetThemeGuidance(theme)}

=== REQUIREMENTS ===
1. TITLE (Topic): CRITICAL - YOU MUST USE THIS EXACT FORMULA:
   {assignedFormula}

2. DURATION: Between 15 - 35 minutes.
3. SOURCES (SourceReferences): THIS IS CRITICAL. You must cite specific valid sources (Quran Surah:Ayat, Hadith Narrator/Number, Name of Classical Kitab/Book).

=== PHASE-SPECIFIC BEAT REQUIREMENTS ===

Each phase has specific REQUIRED ELEMENTS that must be reflected in the beats:

{beatTemplateSection}

=== BEAT QUALITY RULES (JAZIRAH ILMU STYLE) ===

ATURAN PENULISAN BEAT YANG WAJIB DIPATUHI:

1. NARATIF-FOCUSED: Setiap beat harus berisi KONTEN CERITA, bukan instruksi visual. Tulis apa yang Diceritakan, bukan apa yang Dilihat.
2. KALIMAT PANJANG MENGALIR: Setiap beat harus berupa narasi panjang yang mengalir (3-5 klausa), bukan poin-poin pendek.
3. REFERENSI JELAS: Sebutkan QS. X:Y, HR. Nama#Nomor, Nama Kitab, Nama Tokoh, Tahun sebagai FAKTA SEJARAH.
4. DATA KONKRET: Sertakan angka, nama, tahun, atau fakta spesifik dalam setiap beat.
5. HINDARI: Visual instructions (close-up, zoom, fade), dramatic pauses (Hening 3 detik), direct confrontation (Siapa Tuanamu?).
6. JI STYLE: Gunakan pola Jazirah Ilmu - observasi luas -> hidden reality -> analisis mendalam -> refleksi.

KHUSUS UNTUK OPEN LOOP/REFLEKSI (Phase Terakhir):
- JANGAN gunakan pertanyaan langsung seperti Siapa Tuanmu? atau Apakah kamu siap?
- GUNAKAN pernyataan reflektif yang menggantungkan kesimpulan pada audiens
- CONTOH JI: Di titik inilah abad pertengahan benar-benar berakhir. Bukan karena kegelapan menghilang sepenuhnya, melainkan karena manusia mulai berani menyalakan cahaya mereka sendiri.

CONTOH BEAT YANG BAIK (JI STYLE):
- Beat 1: Narasi pembuka panjang yang mengalir - mulai dengan observasi luas tentang dunia yang tampak tenang, lalu perlahan mengarah ke anomali. Contoh JI: Dunia hari ini terlihat tenang. Layar menyala, pasar bergerak, teknologi berkembang seolah semuanya terkendali. Tapi di balik itu semua, ada satu titik rapuh.
- Beat 2: DATA KONKRET - Sajikan angka/statistik dengan naratif yang mengalir. Contoh: Jika dikalkulasi, hampir sepertiga usia produktif kita habis dalam posisi menunduk pada layar, melakukan sujud digital yang durasinya jauh melampaui waktu yang kita berikan untuk Tuhan pemilik semesta.
- Beat 3: REFERENSI KITAB - Sebut tokoh/kitab sebagai konsep analisis. Contoh: Dalam kitab Majmu Fatawa Jilid 10, Ibnu Taimiyah menjelaskan bahwa Ilah bukan sekadar yang kita sembah dalam ritual, melainkan apa yang membuat hati tenang karenanya, jiwamu bergantung padanya.
- Beat 4: OPEN LOOP - Pernyataan reflektif, BUKAN pertanyaan. Contoh: Pada akhirnya, pertanyaan bukan lagi tentang kebenaran klaim keagamaan di masa lalu, melainkan tentang apakah kita memiliki kebijaksanaan untuk membedakan antara keyakinan yang memerdekakan dan fanatisme yang memperbudak.

CONTOH BEAT YANG BURUK:
- Beat: Visual close-up iris mata yang memantulkan logo... (Ini instruksi visual, bukan narasi)
- Beat: Pertanyaan tajam: Siapa Tuanmu yang sebenarnya sekarang? (Direct confrontation, tidak JI style)
- Beat: Pertanyaan konfrontatif: Apakah kamu siap menghadapi hari ketika... (JANGAN gunakan pertanyaan langsung)
- Beat: Momen Hening: Layar menjadi hitam total selama 3 detik. (Dramatic pause, bukan narasi)

=== OUTPUT FORMAT (STRICT JSON) ===
Return ONLY this JSON structure (no markdown text):
{{
  ""topic"": ""Judul video bahasa Indonesia"",
  ""targetDurationMinutes"": 20,
  ""outline"": ""Ringkasan alur cerita dalam 2-3 kalimat..."",
  ""sourceReferences"": ""QS. Al-Mulk: 1-5, HR. Muslim No. 203, Kitab Al-Bidaya wan Nihaya Vol 3"",
  ""mustHaveBeats"": [
    ""Narasi pembuka panjang yang mengalir: 'Dunia hari ini terlihat tenang. Layar menyala...' (Gunakan kalimat panjang dengan klausa yang terhubung)"",
    ""DATA KONKRET: Sertakan angka/tahun/nama spesifik dengan narasi yang mengalir, bukan hanya sebut data mentah"",
    ""REFERENSI KITAB: Sebut tokoh/kitab sebagai FAKTA SEJARAH dalam analisis, bukan untuk dakwah"",
    ""OPEN LOOP/REFLEKSI: Pernyataan reflektif, BUKAN pertanyaan langsung. Contoh: Pada akhirnya, pertanyaan bukan lagi tentang kebenaran klaim keagamaan di masa lalu, melainkan tentang apakah kita memiliki kebijaksanaan..."",
    ""... (lanjutkan untuk SEMUA 5 phase, total 15-25 beats yang substansial dan naratif)""
  ]
}}";
    }

    private string GetTitleFormula(int index)
    {
        var formulas = new[]
        {
            "Formula 1: Angka + Subjek + yang Bisa/Mungkin + Konsekuensi",
            "Formula 2: Durasi + Kata Kerja Memahami + Kenapa + Subjek + Kata Kunci Emosional",
            "Formula 3: Beginilah Nasib/Keadaan + [Tempat/Orang] Setelah + [X Tahun/Kejadian]",
            "Formula 4: Ketika + Ide/Agama/Metode + Untuk + Hasil/Peristiwa + [Tokoh]",
            "Formula 5: Mereka Dibilang X, Tapi Y. Apakah Kebetulan?",
            "Formula 6: Mitos Atau Fakta: [Klaim Provokatif]",
            "Formula 7: [Nama Orang] dan [Nama Orang] di Catatan [Pelaku/Tokoh Misterius]",
            "Formula 8: Seberapa [Adjektif Ekstrem] + [Periode/Peristiwa] + ?",
            "Formula 9: Bagaimana Jika + Hipotesis/Perubahan + [Konsekuensi Besar]",
            "Formula 10: Reportase singkat: [Tempat/Peristiwa] — [Frasa Menarik]"
        };
        
        return formulas[index % formulas.Length];
    }

    private GeneratedConfig? ParseSingleConfig(string response)
    {
        try
        {
            response = StripMarkdownCodeBlocks(response);

            int idxStart = response.IndexOf('{');
            int idxEnd = response.LastIndexOf('}');

            if (idxStart == -1 || idxEnd == -1) return null;

            string jsonClean = response.Substring(idxStart, idxEnd - idxStart + 1);

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            };

            GeneratedConfig? config = null;
            if (jsonClean.TrimStart().StartsWith("["))
            {
                var list = JsonSerializer.Deserialize<List<GeneratedConfig>>(jsonClean, options);
                config = list?.FirstOrDefault();
            }
            else
            {
                config = JsonSerializer.Deserialize<GeneratedConfig>(jsonClean, options);
            }

            // Validate beat quality if beats exist
            if (config?.MustHaveBeats != null && config.MustHaveBeats.Count > 0)
            {
                var validator = new BeatQualityValidator();
                var validationResult = validator.Validate(config.MustHaveBeats);

                if (!validationResult.IsValid)
                {
                    _logger.LogWarning(
                        "Generated config has low-quality beats: {Ratio:P0} substantial. Issues: {Issues}",
                        validationResult.SubstantialRatio,
                        string.Join("; ", validationResult.Issues));

                    // Return null to trigger retry
                    return null;
                }

                _logger.LogInformation(
                    "Beat quality validation passed: {Ratio:P0} substantial beats",
                    validationResult.SubstantialRatio);
            }

            return config;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private string GetThemeGuidance(string theme)
    {
        var themeLower = theme.ToLower();

        if (themeLower.Contains("eschato") || themeLower.Contains("akhir zaman") || themeLower.Contains("kiamat"))
        {
            return @"ESCHATOLOGY THEME - Focus on:
- Lesser-discussed signs of Qiyamah
- Specific detail-oriented topics (Haudh Nabi, Mizan mechanics, Munkar Nakir details)
- Afterlife processes (Barzakh, Mahshar, hisab)
- Connect to modern fears/anxieties
- Use curiosity gaps: ""What happens when...?"", ""Why do we...?""

EXAMPLE UNIQUE ANGLES:
- The beast that can speak to each person individually
- The wind that gently takes believers' souls before horror begins
- Why Yaman is the epicenter of end-times events
- The pool that billions will rush to, but many will be blocked";
        }

        if (themeLower.Contains("sahabat") || themeLower.Contains("companion"))
        {
            return @"SAHABAT THEME - Focus on:
- Lesser-known Sahabat with incredible stories
- Specific emotional moments (sacrifices, struggles, transformations)
- Character traits that relate to modern struggles
- Before/after Islam transformations

EXAMPLE UNIQUE ANGLES:
- Sahabat who were poor but became wealthy
- Sahabat who were tortured but remained steadfast
- Female Sahabat with extraordinary stories";
        }

        if (themeLower.Contains("ekonomi") || themeLower.Contains("uang") || themeLower.Contains("dinar"))
        {
            return @"ECONOMY THEME - Focus on:
- Dinar/Dirham vs CBDC
- Modern financial systems from Islamic perspective
- Riba in modern forms
- Economic empowerment through Islamic principles

EXAMPLE UNIQUE ANGLES:
- Why digital currency could be Dajjal's system
- How paper money loses value while gold remains
- The hidden cost of Riba in daily life";
        }

        if (themeLower.Contains("nabi") || themeLower.Contains("prophet"))
        {
            return @"PROPHET THEME - Focus on:
- Specific events/stories rarely told in detail
- Emotional struggles of prophets
- Lessons from prophets' patience
- Prophets' dealings with difficult people

EXAMPLE UNIQUE ANGLES:
- Prophet's emotional moments
- How prophets handled betrayal
- Prophets' family dynamics and challenges";
        }

        return @"GENERAL THEME - Focus on:
- Stories with emotional resonance
- Lessons applicable to modern life
- Character transformations
- Overcoming struggles with faith
- Rare facts or surprising details";
    }

    private static string StripMarkdownCodeBlocks(string response)
    {
        if (string.IsNullOrWhiteSpace(response)) return response;
        var clean = Regex.Replace(response, @"```json\s*", "", RegexOptions.IgnoreCase);
        clean = Regex.Replace(clean, @"```\s*", "");
        return clean.Trim();
    }
}

/// <summary>
/// A generated config from the LLM batch generation.
/// </summary>
public class GeneratedConfig
{
    public string Topic { get; set; } = "";
    public int TargetDurationMinutes { get; set; } = 20;
    public string ChannelName { get; set; } = "";
    public string? Outline { get; set; }

    // Dikembalikan ke SourceReferences untuk menyimpan Dalil/Sumber Kitab
    public string? SourceReferences { get; set; }

    public List<string>? MustHaveBeats { get; set; }
}
