using System.Text.Json;
using System.Text.RegularExpressions;
using BunbunBroll.Models;

namespace BunbunBroll.Services;

/// <summary>
/// Generates multiple project configs using LLM based on a theme.
/// Ported from ScriptFlow's ConfigBatchGenerator.
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

    private const int MaxRetries = 3;

    public async Task<List<GeneratedConfig>> GenerateConfigsAsync(
        string theme, 
        string channelName,
        int count = 10, 
        string? seed = null,
        CancellationToken cancellationToken = default)
    {
        var systemPrompt = "You are a creative YouTube content strategist specializing in Islamic content. Return ONLY valid JSON arrays.";
        var userPrompt = BuildPrompt(theme, channelName, count, seed);

        _logger.LogInformation("Generating {Count} configs for theme '{Theme}'", count, theme);

        Exception? lastException = null;

        for (int attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (attempt > 1)
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)); // 2s, 4s
                    _logger.LogWarning("Retry attempt {Attempt}/{Max} after {Delay}s", attempt, MaxRetries, delay.TotalSeconds);
                    await Task.Delay(delay, cancellationToken);
                }

                var response = await _intelligenceService.GenerateContentAsync(
                    systemPrompt,
                    userPrompt,
                    maxTokens: 8000,
                    temperature: 0.8,
                    cancellationToken: cancellationToken);

                if (string.IsNullOrEmpty(response))
                    throw new InvalidOperationException("LLM returned empty response");

                // Strip markdown code blocks
                response = StripMarkdownCodeBlocks(response);

                // Extract JSON array
                var jsonStart = response.IndexOf('[');
                var jsonEnd = response.LastIndexOf(']');

                if (jsonStart == -1 || jsonEnd == -1)
                    throw new InvalidOperationException("Failed to extract JSON array from LLM response");

                var jsonContent = response.Substring(jsonStart, jsonEnd - jsonStart + 1);

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var configs = JsonSerializer.Deserialize<List<GeneratedConfig>>(jsonContent, options)
                    ?? throw new InvalidOperationException("Failed to deserialize config array");

                // Ensure channel name is set and fields are within DB limits
                foreach (var config in configs)
                {
                    if (string.IsNullOrEmpty(config.ChannelName))
                        config.ChannelName = channelName;

                    // Truncate to prevent MaxLength violations when saved to DB
                    if (config.Topic.Length > 500)
                        config.Topic = config.Topic[..497] + "...";
                    if (config.ChannelName.Length > 100)
                        config.ChannelName = config.ChannelName[..97] + "...";
                }

                _logger.LogInformation("Generated {Count} configs successfully (attempt {Attempt})", configs.Count, attempt);
                return configs;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                lastException = ex;
                _logger.LogWarning(ex, "Config generation attempt {Attempt}/{Max} failed", attempt, MaxRetries);
            }
        }

        throw new InvalidOperationException(
            $"Failed to generate configs after {MaxRetries} attempts: {lastException?.Message}", lastException);
    }

    private string BuildPrompt(string theme, string channelName, int count, string? seed = null)
    {
        var seedInstruction = string.IsNullOrEmpty(seed) ? "" : $"\n\n=== CULTURAL SEED INSTRUCTION ===\n\n{seed}\n";

        return $@"Generate {count} UNIQUE project configs for theme: ""{theme}"" on channel ""{channelName}"".{seedInstruction}

=== REQUIREMENTS ===

1. TOPICS must be:
   - UNIQUE and NOT generic — offer a fresh, specific angle
   - Written in calm, mature tone — like a thoughtful documentary title
   - Intriguing enough to spark genuine curiosity without being desperate or sensational
   - Relatable to modern daily life
   - NO CAPSLOCK, no shouting, no excessive punctuation (!!!, ???)

2. TITLE STYLE GUIDELINES:
   - Use lowercase/title case naturally — never all-caps words
   - Create a subtle curiosity gap: make people wonder, not scream
   - Feel intellectual and premium, like a well-crafted book chapter title
   - Avoid try-hard clickbait patterns (""SHOCKING"", ""You WON'T Believe"", etc.)
   - Can use a dash or colon to add a secondary hook

   GOOD EXAMPLES:
   - ""Di akhirat nanti, kita dibagi jadi 3 rombongan — kamu masuk yang mana?""
   - ""Satu amalan kecil yang ternyata lebih berat dari Gunung Uhud""
   - ""Kenapa orang zaman dulu bisa hidup 900 tahun?""
   - ""Harta yang kamu simpan hari ini, besok jadi ular di lehermu""
   - ""Malaikat pencabut nyawa punya prosedur yang sangat detail""
   - ""Ada satu doa yang tidak pernah ditolak — tapi jarang yang tahu""

   BAD EXAMPLES (DO NOT USE):
   - ""ILMU TANPA AMAL = BENCANA!!!""
   - ""The SHOCKING Truth About...""
   - ""WHY You NEED to Know This NOW""
   - ""WAJIB TONTON! Ini Akan MENGUBAH Hidupmu""

3. OUTLINE: Brief 2-3 sentence outline of the narrative arc

4. MUST HAVE BEATS: Generate story beats PROPORTIONAL to duration.
   - Use this formula: number of beats = targetDurationMinutes / 2.5 (rounded to nearest integer)
   - For example: 20 min = 8 beats, 30 min = 12 beats, 48 min = 19 beats, 60 min = 24 beats
   - Each beat MUST be SPECIFIC and SUBSTANTIVE (reference a hadith, event, statistic, or concrete example)
   - NEVER use vague filler beats like ""Explore the topic further"" or ""Discuss the implications""
   - Beats should give the writer ENOUGH MATERIAL so they don't need to fill with empty words

5. DURATION: 45-75 minutes (vary the durations)

6. SOURCE REFERENCES: Include 3-6 relevant sources per topic (as comma-separated string)

=== OUTPUT FORMAT ===

Return ONLY a valid JSON array. No markdown, no explanations.

[
  {{
    ""topic"": ""Judul yang tenang tapi bikin penasaran — dengan hook halus"",
    ""targetDurationMinutes"": 60,
    ""channelName"": ""{channelName}"",
    ""outline"": ""Brief narrative arc description"",
    ""sourceReferences"": ""Source 1, Source 2, Source 3"",
    ""mustHaveBeats"": [
      ""Specific story point 1"",
      ""Specific story point 2"",
      ""...up to 10-12 points""
    ]
  }}
]

=== THEME-SPECIFIC GUIDANCE ===

{GetThemeGuidance(theme)}

=== START GENERATING ===

Generate {count} configs now. Remember: UNIQUE + MATURE TONE + GENUINELY INTRIGUING (no capslock, no pick-me energy).";
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
        if (string.IsNullOrWhiteSpace(response))
            return response;

        var jsonMatch = Regex.Match(response, @"```(?:json)?\s*([\s\S]*?)\s*```");
        if (jsonMatch.Success)
            return jsonMatch.Groups[1].Value.Trim();

        return response;
    }
}

/// <summary>
/// A generated config from the LLM batch generation.
/// </summary>
public class GeneratedConfig
{
    public string Topic { get; set; } = "";
    public int TargetDurationMinutes { get; set; } = 60;
    public string ChannelName { get; set; } = "";
    public string? Outline { get; set; }
    public string? SourceReferences { get; set; }
    public List<string>? MustHaveBeats { get; set; }
}
