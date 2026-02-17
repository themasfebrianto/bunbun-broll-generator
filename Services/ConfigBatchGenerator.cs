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

    public async Task<List<GeneratedConfig>> GenerateConfigsAsync(string theme, string channelName, int count, string? seed = null, Action<int, int>? onProgress = null, CancellationToken cancellationToken = default)
    {
        var generatedConfigs = new List<GeneratedConfig>();
        var generatedTopics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        _logger.LogInformation("Starting sequential generation of {Count} configs for theme '{Theme}'", count, theme);

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

                    // 1. Build context-aware prompt focusing on CREDIBLE SOURCES
                    var prompt = BuildSingleConfigPrompt(theme, channelName, seed, generatedTopics);

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

    private string BuildSingleConfigPrompt(string theme, string channelName, string? seed, HashSet<string> existingTopics)
    {
        var context = existingTopics.Any()
            ? $"\nCONTEXT - DO NOT REPEAT THESE TOPICS:\n- {string.Join("\n- ", existingTopics)}"
            : "";

        return $@"
Generate 1 (ONE) unique video configuration JSON for channel '{channelName}'.
Theme: '{theme}'.
Language: INDONESIAN (Bahasa Indonesia) for Topic, Outline, and Beats.
{context}
Seed/Instruction: {seed ?? "None"}

=== THEME GUIDANCE ===
{GetThemeGuidance(theme)}

=== REQUIREMENTS ===
1. TITLE (Topic): High CTR but Elegant. Use 'Storytelling' hooks. (e.g., 'Misteri...', 'Alasan Kenapa...', 'Detik-detik...'). No Clickbait shouting.
2. DURATION: Between 15 - 35 minutes.
3. SOURCES (SourceReferences): THIS IS CRITICAL. You must cite specific valid sources (Quran Surah:Ayat, Hadith Narrator/Number, Name of Classical Kitab/Book). Do NOT make this up.
4. BEATS: Create narrative beats based on duration (Duration / 2.5 = number of beats).
   - Beats must be narrative steps (Hook -> Conflict -> Dalil/Evidence -> Resolution).

=== OUTPUT FORMAT (STRICT JSON) ===
Return ONLY this JSON structure (no markdown text):
{{
  ""topic"": ""Judul video bahasa Indonesia"",
  ""targetDurationMinutes"": 20,
  ""outline"": ""Ringkasan alur cerita dalam 2-3 kalimat..."",
  ""sourceReferences"": ""QS. Al-Mulk: 1-5, HR. Muslim No. 203, Kitab Al-Bidaya wan Nihaya Vol 3, Jurnal Sains Ibnu Sina"",
  ""mustHaveBeats"": [
    ""Intro: Visualisasi masalah/konflik"",
    ""Pembahasan Dalil (Quran/Hadits)"",
    ""Analisa Sejarah/Sains"",
    ""... (lanjutkan sesuai durasi)""
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

            if (jsonClean.TrimStart().StartsWith("["))
            {
                var list = JsonSerializer.Deserialize<List<GeneratedConfig>>(jsonClean, options);
                return list?.FirstOrDefault();
            }
            else
            {
                return JsonSerializer.Deserialize<GeneratedConfig>(jsonClean, options);
            }
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