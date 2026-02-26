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
        if (pattern == null)
        {
            throw new ArgumentNullException(nameof(pattern), "Pattern configuration is missing or invalid. Batch generation requires a valid pattern.");
        }

        _logger.LogInformation("Starting parallel generation of {Count} configs for theme '{Theme}' using pattern '{Pattern}' (max 3 concurrent)", count, theme, pattern?.Name ?? "Unknown");

        var semaphore = new SemaphoreSlim(3); // max 3 concurrent LLM calls
        var completedCount = 0;
        var results = new GeneratedConfig?[count];

        var tasks = Enumerable.Range(0, count).Select(async i =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                int currentNumber = i + 1;
                int retryCount = 0;

                // Determine assigned topic and formula based on index
                var exampleTopics = pattern.Configuration.ExampleTopics;
                bool hasTopics = exampleTopics != null && exampleTopics.Count > 0;
                string? assignedTopic = hasTopics ? exampleTopics[i % exampleTopics!.Count] : null;

                int formulaIndex = i % 10;
                string assignedFormula = GetTitleFormula(formulaIndex);

                while (retryCount < 3)
                {
                    try
                    {
                        // Build prompt (no shared topic dedup during parallel gen — dedup at end)
                        var prompt = BuildSingleConfigPrompt(theme, channelName, seed, new HashSet<string>(), pattern, assignedTopic, assignedFormula);

                        _logger.LogInformation("Generating config {Current}/{Total} (Attempt {Retry})", currentNumber, count, retryCount + 1);

                        var response = await _intelligenceService.GenerateContentAsync(
                            systemPrompt: "You are a creative Director for a high-end Islamic Documentary YouTube channel. You prioritize ACCURACY (Dalil/Sources) and Storytelling. You output strictly valid JSON.",
                            userPrompt: prompt,
                            maxTokens: 2500,
                            temperature: 0.85,
                            cancellationToken: cancellationToken);

                        if (string.IsNullOrEmpty(response)) throw new InvalidOperationException("Empty response from LLM");

                        var config = ParseSingleConfig(response);

                        if (config != null)
                        {
                            config.ChannelName = channelName;
                            if (config.Topic.Length > 100) config.Topic = config.Topic.Substring(0, 97) + "...";

                            results[i] = config;
                            var done = Interlocked.Increment(ref completedCount);
                            onProgress?.Invoke(done, count);
                            Console.WriteLine($"[SUCCESS] Config {currentNumber}: {config.Topic}");
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        retryCount++;
                        _logger.LogWarning("Failed to generate config {Current}: {Message}. Retrying ({Retry}/3)...", currentNumber, ex.Message, retryCount);
                        await Task.Delay(1000 * retryCount, cancellationToken);
                    }
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        // Post-generation: collect results and deduplicate by topic
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var generatedConfigs = new List<GeneratedConfig>();
        foreach (var config in results)
        {
            if (config != null && seen.Add(config.Topic))
            {
                generatedConfigs.Add(config);
            }
        }

        _logger.LogInformation("Parallel generation complete: {Success}/{Total} unique configs", generatedConfigs.Count, count);
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

=== BEAT FORMAT RULES ===

CRITICAL: Setiap beat HARUS berupa poin SINGKAT (maksimal 10-15 kata).
Beat adalah outline/peta konten, BUKAN narasi lengkap.

ATURAN:
1. HOOK PERTAMA: Beat PERTAMA WAJIB merangkum ide utama dari 'Outline' yang diberikan (sebagai pengenalan topik sebelum masuk detail).
2. SINGKAT & PADAT: Maks 15 kata per beat. Tulis sebagai poin outline, bukan paragraf.
3. KONTEN SPESIFIK: Sebutkan nama, angka, tahun, atau sumber konkret di setiap beat.
4. REFERENSI: Cantumkan QS. X:Y, HR. Nama No.X, Nama Kitab, Tokoh, Tahun.
5. HINDARI: Kalimat panjang, instruksi visual, dramatic pause, pertanyaan langsung.
6. TOTAL: 15-20 beats ringkas untuk seluruh video.

CONTOH BEAT YANG BENAR:
- ""Pembuka: ilusi kekayaan modern vs nilai intrinsik emas""
- ""Nixon Shock 1971 — dolar putus dari emas""
- ""Ibnu Khaldun, Al-Muqaddimah: emas-perak sebagai standar nilai""
- ""QS. Al-Baqarah:275 — riba vs jual beli""
- ""Inflasi = pajak tersembunyi, erosi daya beli kelas pekerja""
- ""HR. Ahmad 16244: masa di mana hanya Dinar-Dirham yang bermanfaat""
- ""CBDC — kontrol absolut transaksi oleh otoritas terpusat""
- ""Refleksi: kesejahteraan sejati tak lahir dari sistem berbasis riba""

CONTOH BEAT YANG SALAH (JANGAN SEPERTI INI):
- ""Dunia modern mendefinisikan kekayaan melalui deretan angka yang berkedip di layar digital dan tumpukan kertas berwarna yang tersusun rapi di lemari besi, sebuah ilusi massal yang kita sepakati bersama..."" (TERLALU PANJANG!)

=== OUTPUT FORMAT (STRICT JSON) ===
Return ONLY this JSON structure (no markdown text):
{{
  ""topic"": ""Judul video bahasa Indonesia"",
  ""targetDurationMinutes"": 20,
  ""outline"": ""Ringkasan alur cerita dalam 2-3 kalimat..."",
  ""sourceReferences"": ""QS. Al-Mulk: 1-5, HR. Muslim No. 203, Kitab Al-Bidaya wan Nihaya Vol 3"",
  ""mustHaveBeats"": [
    ""Hook Pembuka: [Rangkuman dari Outline yang diberikan]"",
    ""Data: angka/statistik spesifik tentang permasalahan"",
    ""QS. X:Y — konteks ayat terkait tema"",
    ""HR. Nama No.X — hadits pendukung argumen"",
    ""Kitab Z oleh Tokoh A — analisis klasik"",
    ""Studi kasus: peristiwa konkret tahun XXXX"",
    ""Refleksi: pernyataan penutup yang menggantung""
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
