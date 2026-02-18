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

            while (!success && retryCount < 3)
            {
                try
                {
                    onProgress?.Invoke(currentNumber, count);

                    // 1. Build context-aware prompt focusing on CREDIBLE SOURCES and PATTERN STRUCTURE
                    var prompt = BuildSingleConfigPrompt(theme, channelName, seed, generatedTopics, pattern);

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

    private string BuildSingleConfigPrompt(string theme, string channelName, string? seed, HashSet<string> existingTopics, ScriptPattern pattern)
    {
        var context = existingTopics.Any()
            ? $"\nCONTEXT - DO NOT REPEAT THESE TOPICS:\n- {string.Join("\n- ", existingTopics)}"
            : "";

        // Build phase beat templates
        var templateBuilder = new PhaseBeatTemplateBuilder();
        var phaseTemplates = templateBuilder.BuildTemplatesFromPattern(pattern.Configuration);
        var beatTemplateSection = string.Join("\n", phaseTemplates.Select(t => t.GetBeatPrompt()));

        return $@"
Generate 1 (ONE) unique video configuration JSON for channel '{channelName}'.
Theme: '{theme}'.
Language: INDONESIAN (Bahasa Indonesia) for Topic, Outline, and Beats.
{context}
Seed/Instruction: {seed ?? "None"}

=== THEME GUIDANCE ===
{GetThemeGuidance(theme)}

=== REQUIREMENTS ===
1. TITLE (Topic): CRITICAL - YOU MUST USE one of the following 10 formulas:

   Formula 1: Angka + Subjek + yang Bisa/Mungkin + Konsekuensi
   Formula 2: Durasi + Kata Kerja Memahami + Kenapa + Subjek + Kata Kunci Emosional
   Formula 3: Beginilah Nasib/Keadaan + [Tempat/Orang] Setelah + [X Tahun/Kejadian]
   Formula 4: Ketika + Ide/Agama/Metode + Untuk + Hasil/Peristiwa + [Tokoh]
   Formula 5: Mereka Dibilang X, Tapi Y. Apakah Kebetulan?
   Formula 6: Mitos Atau Fakta: [Klaim Provokatif]
   Formula 7: [Nama Orang] dan [Nama Orang] di Catatan [Pelaku/Tokoh Misterius]
   Formula 8: Seberapa [Adjektif Ekstrem] + [Periode/Peristiwa] + ?
   Formula 9: Bagaimana Jika + Hipotesis/Perubahan + [Konsekuensi Besar]
   Formula 10: Reportase singkat: [Tempat/Peristiwa] — [Frasa Menarik]

2. DURATION: Between 15 - 35 minutes.
3. SOURCES (SourceReferences): THIS IS CRITICAL. You must cite specific valid sources (Quran Surah:Ayat, Hadith Narrator/Number, Name of Classical Kitab/Book).

=== PHASE-SPECIFIC BEAT REQUIREMENTS ===

Each phase has specific REQUIRED ELEMENTS that must be reflected in the beats:

{beatTemplateSection}

=== BEAT QUALITY RULES ===

ATURAN PENULISAN BEAT YANG WAJIB DIPATUHI:

1. **SPESIFIK & KONKRET**: Gunakan deskripsi visual jelas (warna, suasana, adegan)
2. **REFERENSI JELAS**: Sebutkan QS. X:Y, HR. Nama#Nomor, Nama Kitab, Nama Tokoh, Tahun
3. **KONSEPSI ILMIAH/PSIKOLOGIS**: Nama teori, mekanisme, istilah teknis dengan konteks
4. **NARASI/KALIMAT CONTOH**: Tulis kalimat aktual yang bisa diucapkan, bukan ringkasan
5. **EMOSI**: Hubungkan dengan perasaan (takut, kagum, sedih, terkejut, gelisah)
6. **HINDARI FRASA UMUM**: Jangan gunakan 'analisis', 'penjelasan', 'membahas', 'mengulas'

CONTOH BEAT YANG BAIK (SUBSTANTIAL):
- [The Cold Open]: Visual hening sebuah kamar gelap, hanya diterangi cahaya biru layar smartphone yang menyorot wajah kosong seseorang.
- [The Cold Open]: Narasi paradoks: 'Dulu, berhala itu diam di tempat dan kita yang mendatanginya. Hari ini, berhala itu ada di saku...'
- [The Hidden Reality]: Penjelasan linguistik kata 'Ilah' merujuk Ibnu Taimiyah dalam Al-Ubudiyah: Bukan sekadar pencipta, tapi 'sesuatu yang hati terpaut padanya'.
- [The Systematic Breakdown]: Konsep 'Riya Digital': Bagaimana arsitektur 'Like' dan 'Comment' memfasilitasi penyakit hati (Ujub/Sum'ah).
- [The Critical Junction]: Pertanyaan tajam: 'Jika besok internet mati selamanya, siapa ''tuhan'' yang hilang dari hidupmu?'

CONTOH BEAT YANG BURUK (TERLALU UMUM) - HINDARI:
- [The Cold Open]: Hook visual yang kuat... ❌
- [The Hidden Reality]: Penjelasan konteks sejarah... ❌
- [The Systematic Breakdown]: Analisis mendalam tentang... ❌
- [The Critical Junction]: Pertanyaan reflektif... ❌

=== OUTPUT FORMAT (STRICT JSON) ===
Return ONLY this JSON structure (no markdown text):
{{
  ""topic"": ""Judul video bahasa Indonesia"",
  ""targetDurationMinutes"": 20,
  ""outline"": ""Ringkasan alur cerita dalam 2-3 kalimat..."",
  ""sourceReferences"": ""QS. Al-Mulk: 1-5, HR. Muslim No. 203, Kitab Al-Bidaya wan Nihaya Vol 3"",
  ""mustHaveBeats"": [
    ""[The Cold Open]: Visual spesifik dengan deskripsi mendetak..."",
    ""[The Cold Open]: Narasi paradoks dengan kutipan langsung..."",
    ""[The Hidden Reality]: Data konkret: Angka/Tahun/Nama spesifik..."",
    ""[The Hidden Reality]: Referensi jelas: QS. atau HR. atau Kitab..."",
    ""... (lanjutkan untuk SEMUA 5 phase, total 15-25 beats yang substansial)""
  ]
}}";
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