# Context-Aware Image Generation Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Enable AI image prompt generation with global narrative context (mood beats, era progression, characters, locations) and sliding-window local context for consistent visual storytelling across all segments.

**Architecture:** Two-pass system - Pass 1 extracts global storytelling context from full script (locations, characters, era timeline, mood beats with auto-detected visual settings); Pass 2 generates prompts for each segment using global context + sliding window of adjacent segments.

**Tech Stack:** C# / .NET, HttpClient for LLM calls, System.Text.Json for serialization, existing Gemini LLM integration

---

## Task 1: Create Global Context Data Models

**Files:**
- Create: `Models/GlobalScriptContext.cs`

**Step 1: Create the model file**

Create `Models/GlobalScriptContext.cs` with these classes:

```csharp
namespace BunbunBroll.Models;

/// <summary>
/// Global storytelling context extracted from full script analysis.
/// Used to maintain visual consistency across all generated image prompts.
/// </summary>
public class GlobalScriptContext
{
    public List<string> PrimaryLocations { get; set; } = new();
    public List<StoryCharacter> IdentifiedCharacters { get; set; } = new();
    public List<EraTransition> EraTimeline { get; set; } = new();
    public List<MoodBeat> MoodBeats { get; set; } = new();
    public List<string> RecurringVisuals { get; set; } = new();
    public string ColorProgression { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public DateTime ExtractedAt { get; set; } = DateTime.UtcNow;

    public MoodBeat? GetMoodBeatForSegment(int segmentIndex)
    {
        return MoodBeats.LastOrDefault(m => segmentIndex >= m.StartSegment);
    }

    public EraTransition? GetEraForSegment(int segmentIndex)
    {
        return EraTimeline.LastOrDefault(e => segmentIndex >= e.StartSegment);
    }
}

public class ScriptSegmentRef
{
    public int Index { get; set; }
    public string Timestamp { get; set; } = string.Empty;
    public string ScriptText { get; set; } = string.Empty;
}

public class StoryCharacter
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? FirstAppearanceSegment { get; set; }
}

public class EraTransition
{
    public int StartSegment { get; set; }
    public int? EndSegment { get; set; }
    public string Era { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class MoodBeat
{
    public int StartSegment { get; set; }
    public int? EndSegment { get; set; }
    public string Mood { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> VisualKeywords { get; set; } = new();

    // Auto-detected visual settings (used when user selects "Auto")
    public ImageLighting? SuggestedLighting { get; set; }
    public ImageColorPalette? SuggestedPalette { get; set; }
    public ImageComposition? SuggestedAngle { get; set; }
    public string? VisualRationale { get; set; }
}

// Response DTOs for JSON parsing
public class GlobalContextExtractionResponse
{
    public List<string> PrimaryLocations { get; set; } = new();
    public List<StoryCharacterResponse> IdentifiedCharacters { get; set; } = new();
    public List<EraTransitionResponse> EraTimeline { get; set; } = new();
    public List<MoodBeatResponse> MoodBeats { get; set; } = new();
    public List<string> RecurringVisuals { get; set; } = new();
    public string ColorProgression { get; set; } = string.Empty;

    public GlobalScriptContext ToGlobalScriptContext(string topic)
    {
        return new GlobalScriptContext
        {
            PrimaryLocations = PrimaryLocations,
            IdentifiedCharacters = IdentifiedCharacters.Select(c => new StoryCharacter
            {
                Name = c.Name,
                Description = c.Description,
                FirstAppearanceSegment = c.FirstAppearanceSegment?.ToString()
            }).ToList(),
            EraTimeline = EraTimeline.Select(e => new EraTransition
            {
                StartSegment = e.StartSegment,
                EndSegment = e.EndSegment,
                Era = e.Era,
                Description = e.Description
            }).ToList(),
            MoodBeats = MoodBeats.Select(m => new MoodBeat
            {
                StartSegment = m.StartSegment,
                EndSegment = m.EndSegment,
                Mood = m.Mood,
                Description = m.Description,
                VisualKeywords = m.VisualKeywords,
                SuggestedLighting = ParseEnum<ImageLighting>(m.SuggestedLighting),
                SuggestedPalette = ParseEnum<ImageColorPalette>(m.SuggestedPalette),
                SuggestedAngle = ParseEnum<ImageComposition>(m.SuggestedAngle),
                VisualRationale = m.VisualRationale
            }).ToList(),
            RecurringVisuals = RecurringVisuals,
            ColorProgression = ColorProgression,
            Topic = topic
        };
    }

    private static T? ParseEnum<T>(string? value) where T : struct
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (Enum.TryParse<T>(value, true, out var result)) return result;
        return null;
    }
}

public class StoryCharacterResponse
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? FirstAppearanceSegment { get; set; }
}

public class EraTransitionResponse
{
    public int StartSegment { get; set; }
    public int? EndSegment { get; set; }
    public string Era { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class MoodBeatResponse
{
    public int StartSegment { get; set; }
    public int? EndSegment { get; set; }
    public string Mood { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> VisualKeywords { get; set; } = new();
    public string? SuggestedLighting { get; set; }
    public string? SuggestedPalette { get; set; }
    public string? SuggestedAngle { get; set; }
    public string? VisualRationale { get; set; }
}
```

**Step 2: Commit**

```bash
git add Models/GlobalScriptContext.cs
git commit -m "feat: add global context models for narrative-aware image generation"
```

---

## Task 2: Add Auto Enum Values to Image Enums

**Files:**
- Modify: `Models/ImagePromptModels.cs`

**Step 1: Add `Auto` value to lighting, palette, and composition enums**

In `Models/ImagePromptModels.cs`, add `Auto` as the first value to each enum:

```csharp
/// <summary>Lighting preset for AI image generation</summary>
public enum ImageLighting
{
    /// <summary>Auto-detect based on mood beat and context</summary>
    Auto,
    /// <summary>Dramatic directional light with strong shadows</summary>
    DramaticHighContrast,
    // ... rest of values
}
```

Do the same for `ImageColorPalette` and `ImageComposition`:

```csharp
public enum ImageColorPalette
{
    /// <summary>Auto-detect based on mood beat and era</summary>
    Auto,
    /// <summary>Vibrant focal colors against muted backgrounds (default)</summary>
    VibrantFocalMuted,
    // ... rest
}

public enum ImageComposition
{
    /// <summary>Auto-detect based on scene content</summary>
    Auto,
    /// <summary>Wide establishing shot showing full environment</summary>
    WideShot,
    // ... rest
}
```

**Step 2: Update ImagePromptConfig defaults**

In `ImagePromptConfig` class, change the defaults to `Auto`:

```csharp
public ImageLighting Lighting { get; set; } = ImageLighting.Auto;
public ImageColorPalette ColorPalette { get; set; } = ImageColorPalette.Auto;
public ImageComposition Composition { get; set; } = ImageComposition.Auto;
```

**Step 3: Update EffectiveStyleSuffix to handle Auto**

Modify the `EffectiveStyleSuffix` property to skip `Auto` values:

```csharp
public string EffectiveStyleSuffix
{
    get
    {
        var parts = new List<string>();

        // Art style (no Auto value for this one)
        if (ArtStyle == ImageArtStyle.Custom && !string.IsNullOrWhiteSpace(CustomArtStyle))
            parts.Add(CustomArtStyle);
        else if (ArtStyle != ImageArtStyle.Custom)
        {
            var artSuffix = ImageStyleMappings.GetArtStyleSuffix(ArtStyle);
            if (!string.IsNullOrEmpty(artSuffix)) parts.Add(artSuffix);
        }

        // Lighting - skip Auto
        if (Lighting != ImageLighting.Auto)
        {
            var lightSuffix = ImageStyleMappings.GetLightingSuffix(Lighting);
            if (!string.IsNullOrEmpty(lightSuffix)) parts.Add(lightSuffix);
        }

        // Color Palette - skip Auto
        if (ColorPalette != ImageColorPalette.Auto)
        {
            var colorSuffix = ImageStyleMappings.GetColorPaletteSuffix(ColorPalette);
            if (!string.IsNullOrEmpty(colorSuffix)) parts.Add(colorSuffix);
        }

        // Composition - skip Auto
        if (Composition != ImageComposition.Auto)
        {
            var compSuffix = ImageStyleMappings.GetCompositionSuffix(Composition);
            if (!string.IsNullOrEmpty(compSuffix)) parts.Add(compSuffix);
        }

        // Always append quality tags
        parts.Add("expressive painterly textures, atmospheric depth, ultra-detailed, sharp focus, 8k quality, consistent visual tone");

        return ", " + string.Join(", ", parts);
    }
}
```

**Step 4: Commit**

```bash
git add Models/ImagePromptModels.cs
git commit -m "feat: add Auto option to lighting, palette, and composition enums"
```

---

## Task 3: Add Global Context Extraction Service Method

**Files:**
- Modify: `Services/IntelligenceService.cs`

**Step 1: Add interface method to IIntelligenceService**

Add this method to the `IIntelligenceService` interface (around line 90, before the closing brace):

```csharp
/// <summary>
/// Extract global storytelling context from the full script.
/// Analyzes all segments to identify: locations, characters, era progression, mood beats with visual settings.
/// </summary>
Task<GlobalScriptContext?> ExtractGlobalContextAsync(
    List<BrollPromptItem> segments,
    string topic,
    CancellationToken cancellationToken = default);
```

**Step 2: Add the system prompt constant**

Add this constant after the existing `SystemPrompt` constant (around line 138):

```csharp
private const string GlobalContextExtractionPrompt = @"
You are a visual narrative analyzer for Islamic video essays.
Analyze the ENTIRE script and extract GLOBAL storytelling context.

TASK: Extract persistent elements that span across ALL segments.

OUTPUT (JSON only, no markdown):
{
  ""primaryLocations"": [""Qumran limestone caves"", ""Judean Desert"", ""ancient Jerusalem""],
  ""identifiedCharacters"": [
    {""name"": ""Essene ascetic community"", ""description"": ""male monks in simple robes""},
    {""name"": ""Prophet Isa"", ""description"": ""face hidden by divine light""}
  ],
  ""eraTimeline"": [
    {""startSegment"": 0, ""endSegment"": 45, ""era"": ""Late Ancient Roman Empire"", ""description"": ""civilization decline""},
    {""startSegment"": 18, ""endSegment"": 20, ""era"": ""21st Century Modern"", ""description"": ""scientific analysis""},
    {""startSegment"": 40, ""era"": ""Islamic End Times"", ""description"": ""apocalyptic atmosphere""}
  ],
  ""moodBeats"": [
    {
      ""startSegment"": 0,
      ""endSegment"": 25,
      ""mood"": ""mysterious"",
      ""description"": ""contemplative discovery, ancient secrets, quiet wonder"",
      ""visualKeywords"": [""candlelight"", ""dust particles"", ""shadows""],
      ""suggestedLighting"": ""SoftAmbient"",
      ""suggestedPalette"": ""WarmEarthy"",
      ""suggestedAngle"": ""CloseUp"",
      ""visualRationale"": ""Intimate cave scenes with candlelight work best with close-up framing and soft warm lighting""
    },
    {
      ""startSegment"": 25,
      ""endSegment"": 50,
      ""mood"": ""tense"",
      ""description"": ""apocalyptic anticipation, war preparations, divine reckoning"",
      ""visualKeywords"": [""storm clouds"", ""dramatic lighting"", ""darkness""],
      ""suggestedLighting"": ""MoodyDark"",
      ""suggestedPalette"": ""Monochrome"",
      ""suggestedAngle"": ""WideShot"",
      ""visualRationale"": ""Epic war scenes require wide framing and dark dramatic lighting""
    }
  ],
  ""recurringVisuals"": [""clay jars"", ""parchment scrolls"", ""cave interiors"", ""divine light rays""],
  ""colorProgression"": ""Start with warm earthy tones (caves, scrolls), transition to dark dramatic (war), end with golden divine (climax)""
}

MOOD DETECTION RULES:
- Analyze EMOTIONAL ARC across the entire script, not just individual segments
- Look for tone shifts: mystery → tension → revelation → hope
- Consider Islamic storytelling structure: problem → struggle → divine solution
- Mood changes should align with NARRATIVE BEATS, not random
- Typically 4-6 mood beats for a 15-20 minute video
- Each mood should span 5-15+ segments (not every segment!)

VISUAL DECISION RULES (for suggestedLighting, suggestedPalette, suggestedAngle):
- LIGHTING: Match mood energy
  * Mysterious/contemplative → SoftAmbient or EtherealGlow
  * Tense/apocalyptic → MoodyDark or DramaticHighContrast
  * Hopeful/inspiring → GoldenHour
  * Epic/powerful → DramaticHighContrast

- PALETTE: Support emotional tone
  * Ancient/historical → WarmEarthy or GoldenDesert
  * Modern/tech → CoolBlue or VibrantFocalMuted
  * End times → Monochrome or MysticPurple
  * Nature → NaturalGreen

- ANGLE: Match subject scale
  * Intimate personal moments → CloseUp
  * Landscapes/environments → WideShot or CinematicWide
  * Powerful figures → LowAngle
  * Overview/expository → BirdsEye

ERA SELECTION (use these exact era names):
- Lost ancient civilization ruins era (for ancient times, mystery)
- Late Ancient Roman Empire era (for decline, fall of civilization)
- 6th century BC Ancient Babylon era (for ancient kings, mystery)
- 6th century Pre-Islamic Arabia era (for jahiliyya, ancient Arabian)
- 21st century modern urban era (for contemporary, technology)
- Islamic End Times era (for apocalypse, judgment day)
- Metaphysical void era (for abstract, existential reflection)
- Cosmic end-of-world era (for finality, cosmic scale)
";
```

**Step 3: Implement the extraction method**

Add this method to the `IntelligenceService` class (after the `GeneratePromptForTypeAsync` method, around line 1260):

```csharp
public async Task<GlobalScriptContext?> ExtractGlobalContextAsync(
    List<BrollPromptItem> segments,
    string topic,
    CancellationToken cancellationToken = default)
{
    if (segments.Count == 0) return null;

    var stopwatch = Stopwatch.StartNew();

    try
    {
        // Build the user prompt with all segments
        var userPrompt = new System.Text.StringBuilder();
        userPrompt.AppendLine($"Topic: {topic}");
        userPrompt.AppendLine();
        userPrompt.AppendLine($"Total Segments: {segments.Count}");
        userPrompt.AppendLine();
        userPrompt.AppendLine("FULL SCRIPT:");
        for (int i = 0; i < segments.Count; i++)
        {
            userPrompt.AppendLine($"[{i}] {segments[i].Timestamp} {segments[i].ScriptText}");
        }

        var request = new GeminiChatRequest
        {
            Model = _settings.Model,
            Messages = new List<GeminiMessage>
            {
                new() { Role = "system", Content = GlobalContextExtractionPrompt },
                new() { Role = "user", Content = userPrompt.ToString() }
            },
            Temperature = 0.4,
            MaxTokens = 4000
        };

        var response = await _httpClient.PostAsJsonAsync(
            "v1/chat/completions",
            request,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var geminiResponse = await response.Content.ReadFromJsonAsync<GeminiChatResponse>(
            cancellationToken: cancellationToken);

        var rawContent = geminiResponse?.Choices?.FirstOrDefault()?.Message?.Content;

        if (string.IsNullOrEmpty(rawContent))
        {
            _logger.LogWarning("Global context extraction returned empty response");
            return null;
        }

        var cleanedJson = CleanJsonResponse(rawContent);
        _logger.LogDebug("Global context extraction response: {Content}",
            cleanedJson.Length > 500 ? cleanedJson[..500] + "..." : cleanedJson);

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var parsed = JsonSerializer.Deserialize<GlobalContextExtractionResponse>(cleanedJson, options);

        if (parsed == null)
        {
            _logger.LogWarning("Failed to deserialize global context response");
            return null;
        }

        var globalContext = parsed.ToGlobalScriptContext(topic);

        _logger.LogInformation(
            "Extracted global context in {Ms}ms: {LocationCount} locations, {CharacterCount} characters, {EraCount} eras, {MoodCount} mood beats",
            stopwatch.ElapsedMilliseconds,
            globalContext.PrimaryLocations.Count,
            globalContext.IdentifiedCharacters.Count,
            globalContext.EraTimeline.Count,
            globalContext.MoodBeats.Count);

        return globalContext;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Global context extraction failed");
        return null;
    }
}
```

**Step 4: Commit**

```bash
git add Services/IntelligenceService.cs
git commit -m "feat: add global context extraction for narrative-aware prompt generation"
```

---

## Task 4: Add Context-Aware Prompt Generation Methods

**Files:**
- Modify: `Services/IntelligenceService.cs`

**Step 1: Add context-aware prompt generation interface methods**

Add these methods to `IIntelligenceService` interface (after the `ExtractGlobalContextAsync` method):

```csharp
/// <summary>
/// Generate prompts in batch with global narrative context.
/// Each prompt uses: global context + sliding window of adjacent segments.
/// </summary>
Task GeneratePromptsWithContextAsync(
    List<BrollPromptItem> items,
    BrollMediaType targetType,
    string topic,
    GlobalScriptContext globalContext,
    ImagePromptConfig? config = null,
    Func<int, Task>? onProgress = null,
    int windowSize = 2,
    CancellationToken cancellationToken = default);

/// <summary>
/// Generate a single prompt with context awareness.
/// Uses global context + sliding window for narrative consistency.
/// </summary>
Task<string> GeneratePromptWithContextAsync(
    BrollPromptItem currentItem,
    List<BrollPromptItem> allItems,
    string topic,
    GlobalScriptContext globalContext,
    ImagePromptConfig? config = null,
    int windowSize = 2,
    CancellationToken cancellationToken = default);
```

**Step 2: Add helper method to build context prompt**

Add this private helper method to `IntelligenceService` class:

```csharp
private string BuildContextualPrompt(
    BrollPromptItem currentItem,
    List<BrollPromptItem> allItems,
    GlobalScriptContext globalContext,
    string topic,
    ImagePromptConfig? config,
    int windowSize)
{
    var prompt = new System.Text.StringBuilder();

    // === GLOBAL CONTEXT SECTION ===
    prompt.AppendLine("GLOBAL STORY CONTEXT (Applies to ALL segments):");
    prompt.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

    if (globalContext.PrimaryLocations.Count > 0)
    {
        prompt.AppendLine($"📍 Primary Locations: {string.Join(", ", globalContext.PrimaryLocations.Take(5))}");
    }

    if (globalContext.IdentifiedCharacters.Count > 0)
    {
        prompt.AppendLine($"👥 Characters: {string.Join(", ", globalContext.IdentifiedCharacters.Select(c => c.Name))}");
    }

    if (globalContext.EraTimeline.Count > 0)
    {
        prompt.AppendLine("🎭 Era Progression:");
        foreach (var era in globalContext.EraTimeline.Take(5))
        {
            var end = era.EndSegment.HasValue ? $"-{era.EndSegment}" : "+";
            prompt.AppendLine($"   - Segments {era.StartSegment}{end}: {era.Era}");
        }
    }

    if (globalContext.MoodBeats.Count > 0)
    {
        prompt.AppendLine("🎨 Mood Progression:");
        foreach (var mood in globalContext.MoodBeats.Take(6))
        {
            var end = mood.EndSegment.HasValue ? $"-{mood.EndSegment}" : "+";
            prompt.AppendLine($"   - Segments {mood.StartSegment}{end}: {mood.Mood} - {mood.Description}");
        }
    }

    if (globalContext.RecurringVisuals.Count > 0)
    {
        prompt.AppendLine($"🎨 Visual Leitmotifs: {string.Join(", ", globalContext.RecurringVisuals.Take(5))}");
    }

    prompt.AppendLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
    prompt.AppendLine();

    // === LOCAL CONTEXT SECTION (Sliding Window) ===
    var currentIndex = allItems.IndexOf(currentItem);
    prompt.AppendLine("LOCAL NARRATIVE CONTEXT:");

    // Previous segments
    var prevStart = Math.Max(0, currentIndex - windowSize);
    if (prevStart < currentIndex)
    {
        prompt.AppendLine("┌─ PREVIOUS (what just happened)");
        for (int i = prevStart; i < currentIndex; i++)
        {
            prompt.AppendLine($"│  [{allItems[i].Timestamp}] {allItems[i].ScriptText}");
        }
        prompt.AppendLine("│");
    }

    // Current segment
    prompt.AppendLine($"├─ CURRENT (generate visual for this)");
    prompt.AppendLine($"│  [{currentItem.Timestamp}] {currentItem.ScriptText}");
    prompt.AppendLine("│");

    // Next segments
    var nextEnd = Math.Min(allItems.Count - 1, currentIndex + windowSize);
    if (nextEnd > currentIndex)
    {
        prompt.AppendLine("└─ UPCOMING (what's coming next)");
        for (int i = currentIndex + 1; i <= nextEnd; i++)
        {
            prompt.AppendLine($"   [{allItems[i].Timestamp}] {allItems[i].ScriptText}");
        }
    }

    prompt.AppendLine();

    // === SEGMENT-SPECIFIC CONTEXT ===
    var currentMood = globalContext.GetMoodBeatForSegment(currentItem.Index);
    var currentEra = globalContext.GetEraForSegment(currentItem.Index);

    if (currentMood != null || currentEra != null)
    {
        prompt.AppendLine("SEGMENT-SPECIFIC CONTEXT:");

        if (currentEra != null)
        {
            prompt.AppendLine($"  📍 Era: {currentEra.Era}");
            if (!string.IsNullOrEmpty(currentEra.Description))
                prompt.AppendLine($"     ({currentEra.Description})");
        }

        if (currentMood != null)
        {
            prompt.AppendLine($"  🎭 Mood: {currentMood.Mood} - {currentMood.Description}");

            // Auto-detect visual settings if config has Auto values
            if (config != null)
            {
                var visualHints = new List<string>();

                if (config.Lighting == ImageLighting.Auto && currentMood.SuggestedLighting.HasValue)
                {
                    var lightingName = currentMood.SuggestedLighting.Value.ToString();
                    visualHints.Add($"lighting: {lightingName}");
                }

                if (config.ColorPalette == ImageColorPalette.Auto && currentMood.SuggestedPalette.HasValue)
                {
                    var paletteName = currentMood.SuggestedPalette.Value.ToString();
                    visualHints.Add($"palette: {paletteName}");
                }

                if (config.Composition == ImageComposition.Auto && currentMood.SuggestedAngle.HasValue)
                {
                    var angleName = currentMood.SuggestedAngle.Value.ToString();
                    visualHints.Add($"angle: {angleName}");
                }

                if (visualHints.Count > 0)
                {
                    prompt.AppendLine($"  🎨 Suggested Visuals: {string.Join(", ", visualHints)}");
                }

                if (!string.IsNullOrEmpty(currentMood.VisualRationale))
                {
                    prompt.AppendLine($"     ℹ️ {currentMood.VisualRationale}");
                }
            }

            if (currentMood.VisualKeywords.Count > 0)
            {
                prompt.AppendLine($"  🔑 Keywords: {string.Join(", ", currentMood.VisualKeywords)}");
            }
        }

        prompt.AppendLine();
    }

    // === TASK INSTRUCTION ===
    var effectiveStyleSuffix = config?.EffectiveStyleSuffix ?? ImageVisualStyle.BASE_STYLE_SUFFIX;
    var eraInstruction = currentEra != null ? $"Start with era prefix: {currentEra.Era}" : "Select appropriate era prefix from the options";
    var moodInstruction = currentMood != null ? $", {currentMood.Mood} mood - {currentMood.Description}" : "";

    prompt.AppendLine($"TASK: Generate image prompt for CURRENT segment considering:");
    prompt.AppendLine($"- Global context: locations, characters, overall narrative");
    prompt.AppendLine($"- Local flow: what just happened and what's coming next");
    prompt.AppendLine($"- Current mood/era: match the emotional tone{moodInstruction}");
    prompt.AppendLine();
    prompt.AppendLine($"STRUCTURE: [{eraInstruction}] [Detailed scene description: setting, action, lighting, atmosphere, characters]{{{effectiveStyleSuffix}}}");
    prompt.AppendLine();

    // Add existing rules
    prompt.AppendLine(CharacterRules.GENDER_RULES);
    prompt.AppendLine(CharacterRules.PROPHET_RULES);

    return prompt.ToString();
}
```

**Step 3: Implement single segment context-aware generation**

Add this method to `IntelligenceService` class:

```csharp
public async Task<string> GeneratePromptWithContextAsync(
    BrollPromptItem currentItem,
    List<BrollPromptItem> allItems,
    string topic,
    GlobalScriptContext globalContext,
    ImagePromptConfig? config = null,
    int windowSize = 2,
    CancellationToken cancellationToken = default)
{
    var systemPrompt = BuildContextualPrompt(
        currentItem, allItems, globalContext, topic, config, windowSize);

    var userPrompt = $"Generate prompt for segment {currentItem.Index}: {currentItem.ScriptText}";

    var result = await GenerateContentAsync(
        systemPrompt,
        userPrompt,
        maxTokens: 500,
        temperature: 0.7,
        cancellationToken: cancellationToken);

    return result?.Trim().Trim('"') ?? string.Empty;
}
```

**Step 4: Implement batch context-aware generation**

Add this method to `IntelligenceService` class:

```csharp
public async Task GeneratePromptsWithContextAsync(
    List<BrollPromptItem> items,
    BrollMediaType targetType,
    string topic,
    GlobalScriptContext globalContext,
    ImagePromptConfig? config = null,
    Func<int, Task>? onProgress = null,
    int windowSize = 2,
    CancellationToken cancellationToken = default)
{
    var targetItems = items.Where(i => i.MediaType == targetType).ToList();
    if (targetItems.Count == 0) return;

    var stopwatch = Stopwatch.StartNew();
    var semaphore = new SemaphoreSlim(3, 3);
    int completedCount = 0;

    var tasks = targetItems.Select(async item =>
    {
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            var generatedPrompt = await GeneratePromptWithContextAsync(
                item,
                items,
                topic,
                globalContext,
                config,
                windowSize,
                cancellationToken);

            item.Prompt = generatedPrompt ?? (targetType == BrollMediaType.BrollVideo
                ? "cinematic footage"
                : "islamic historical scene");

            // Auto-detect era for IMAGE_GEN
            if (targetType == BrollMediaType.ImageGeneration)
                EraLibrary.AutoAssignEraStyle(item);

            var count = Interlocked.Increment(ref completedCount);
            if (onProgress != null) await onProgress(count);
        }
        finally { semaphore.Release(); }
    });

    await Task.WhenAll(tasks);

    _logger.LogInformation("GeneratePromptsWithContext: Generated {Count} {Type} prompts with global context in {Ms}ms",
        targetItems.Count, targetType, stopwatch.ElapsedMilliseconds);
}
```

**Step 5: Commit**

```bash
git add Services/IntelligenceService.cs
git commit -m "feat: add context-aware prompt generation with global narrative and sliding window"
```

---

## Task 5: Update BrollPromptsView to Use Context-Aware Generation

**Files:**
- Modify: `Components/Views/ScriptGenerator/BrollPromptsView.razor`

**Step 1: Find the GeneratePromptsForTypeBatchAsync call**

Search for where the current batch generation is called (look for `GeneratePromptsForTypeBatchAsync`).

**Step 2: Add state for global context**

Add a field to store the global context:

```csharp
private GlobalScriptContext? _globalContext;
```

**Step 3: Update the "Generate Prompts" flow**

Modify the generation flow to:
1. First extract global context
2. Then use context-aware generation

Find the method that handles prompt generation and update it:

```csharp
private async Task GeneratePromptsWithTypeAsync(BrollMediaType targetType)
{
    // ... existing validation code ...

    // Step 1: Extract global context (if not already done)
    if (_globalContext == null || _globalContext.Topic != State.Topic)
    {
        State.Status = $"Extracting global narrative context...";

        _globalContext = await IntelligenceService.ExtractGlobalContextAsync(
            State.BrollItems,
            State.Topic);

        if (_globalContext == null)
        {
            // Fallback to non-context-aware generation
            await IntelligenceService.GeneratePromptsForTypeBatchAsync(
                State.BrollItems.Where(x => x.MediaType == targetType).ToList(),
                targetType,
                State.Topic,
                State.Config,
                progress => InvokeAsync(() => State.Progress = progress * 100 / total));
            return;
        }
    }

    // Step 2: Generate prompts with context
    State.Status = $"Generating {targetType} prompts with narrative context...";

    var targetItems = State.BrollItems.Where(x => x.MediaType == targetType).ToList();
    var total = targetItems.Count;

    await IntelligenceService.GeneratePromptsWithContextAsync(
        State.BrollItems,
        targetType,
        State.Topic,
        _globalContext,
        State.Config,
        async progress => await InvokeAsync(() => State.Progress = progress * 100 / total),
        windowSize: 2); // ±2 segments for local context

    State.Status = "Ready";
    State.Progress = 0;
}
```

**Step 4: Add a button to regenerate global context**

Add a button to allow users to re-extract global context:

```html
<button @onclick="RegenerateGlobalContext" class="btn btn-sm btn-secondary">
    <span class="icon">🔄</span> Re-analyze Context
</button>
```

With the handler:

```csharp
private async Task RegenerateGlobalContext()
{
    _globalContext = null;
    await GeneratePromptsWithTypeAsync(BrollMediaType.ImageGeneration);
}
```

**Step 5: Commit**

```bash
git add Components/Views/ScriptGenerator/BrollPromptsView.razor
git commit -m "feat: integrate context-aware prompt generation in BrollPromptsView"
```

---

## Task 6: Update Single Segment Regeneration

**Files:**
- Modify: `Components/Views/ScriptGenerator/BrollPromptItemCard.razor`

**Step 1: Find the regenerate prompt handler**

Look for the method that handles single segment regeneration (likely called `RegeneratePrompt` or similar).

**Step 2: Update to use context-aware generation**

Modify the regeneration to pass the global context and all items:

```csharp
private async Task RegeneratePrompt()
{
    if (Item is null) return;

    Item.IsGenerating = true;
    StateHasChanged();

    try
    {
        // Get global context from parent (will need to be passed down)
        var globalContext = ParentComponent.GlobalContext;

        if (globalContext != null)
        {
            // Use context-aware generation
            Item.Prompt = await IntelligenceService.GeneratePromptWithContextAsync(
                Item,
                ParentComponent.AllItems, // Need access to all items
                ParentComponent.Topic,
                globalContext,
                ParentComponent.Config,
                windowSize: 2);

            if (Item.MediaType == BrollMediaType.ImageGeneration)
                EraLibrary.AutoAssignEraStyle(Item);
        }
        else
        {
            // Fallback to non-context-aware
            Item.Prompt = await IntelligenceService.GeneratePromptForTypeAsync(
                Item.ScriptText,
                Item.MediaType,
                ParentComponent.Topic,
                ParentComponent.Config);

            if (Item.MediaType == BrollMediaType.ImageGeneration)
                EraLibrary.AutoAssignEraStyle(Item);
        }
    }
    finally
    {
        Item.IsGenerating = false;
        StateHasChanged();
    }
}
```

**Step 3: Update parent component to pass context**

In `BrollPromptsView.razor`, pass the global context to child cards:

```html
@foreach (var item in filteredItems)
{
    <BrollPromptItemCard @key="item.Index"
                        Item="@item"
                        AllItems="@State.BrollItems"
                        Topic="@State.Topic"
                        Config="@State.Config"
                        GlobalContext="@_globalContext"
                        OnRegenerated="OnPromptRegenerated" />
}
```

**Step 4: Update BrollPromptItemCard parameters**

Add the new parameters to the component:

```csharp
[Parameter] public List<BrollPromptItem> AllItems { get; set; } = new();
[Parameter] public GlobalScriptContext? GlobalContext { get; set; }
```

**Step 5: Commit**

```bash
git add Components/Views/ScriptGenerator/BrollPromptItemCard.razor
git add Components/Views/ScriptGenerator/BrollPromptsView.razor
git commit -m "feat: support context-aware regeneration for single segments"
```

---

## Task 7: Add Global Context Display UI (Optional)

**Files:**
- Create: `Components/Views/ScriptGenerator/GlobalContextView.razor`

**Step 1: Create the global context viewer component**

Create `Components/Views/ScriptGenerator/GlobalContextView.razor`:

```razor
@using BunbunBroll.Models
@using System.Text

<div class="global-context-panel card mb-3">
    <div class="card-header d-flex justify-content-between align-items-center">
        <h6 class="mb-0">📖 Global Narrative Context</h6>
        @if (Context != null)
        {
            <span class="badge bg-success">Analyzed</span>
        }
        else
        {
            <span class="badge bg-secondary">Not Analyzed</span>
        }
    </div>

    @if (Context != null)
    {
        <div class="card-body">
            <!-- Locations -->
            @if (Context.PrimaryLocations.Count > 0)
            {
                <div class="mb-3">
                    <small class="text-muted d-block mb-1">📍 Primary Locations</small>
                    <div class="d-flex flex-wrap gap-1">
                        @foreach (var loc in Context.PrimaryLocations)
                        {
                            <span class="badge bg-light text-dark border">@loc</span>
                        }
                    </div>
                </div>
            }

            <!-- Characters -->
            @if (Context.IdentifiedCharacters.Count > 0)
            {
                <div class="mb-3">
                    <small class="text-muted d-block mb-1">👥 Characters</small>
                    <div class="d-flex flex-wrap gap-1">
                        @foreach (var char in Context.IdentifiedCharacters)
                        {
                            <span class="badge bg-info text-dark" title="@char.Description">@char.Name</span>
                        }
                    </div>
                </div>
            }

            <!-- Mood Timeline -->
            @if (Context.MoodBeats.Count > 0)
            {
                <div class="mb-3">
                    <small class="text-muted d-block mb-1">🎭 Mood Progression</small>
                    <div class="mood-timeline">
                        @foreach (var mood in Context.MoodBeats)
                        {
                            var width = CalculateMoodWidth(mood, Context.MoodBeats);
                            <div class="mood-segment" style="width: @(width)%; background: @GetMoodColor(mood.Mood);"
                                 title="@mood.Description">
                                <small>@mood.Mood</small>
                            </div>
                        }
                    </div>
                    <small class="text-muted">@Context.ExtractedAt.ToString("HH:mm:ss")</small>
                </div>
            }

            <!-- Recurring Visuals -->
            @if (Context.RecurringVisuals.Count > 0)
            {
                <div class="mb-3">
                    <small class="text-muted d-block mb-1">🎨 Visual Leitmotifs</small>
                    <div class="d-flex flex-wrap gap-1">
                        @foreach (var visual in Context.RecurringVisuals.Take(8))
                        {
                            <span class="badge bg-secondary">@visual</span>
                        }
                    </div>
                </div>
            }
        </div>
    }
    else
    {
        <div class="card-body text-muted text-center py-3">
            <small>Run prompt generation to analyze narrative context</small>
        </div>
    }
</div>

@code {
    [Parameter] public GlobalScriptContext? Context { get; set; }

    private string GetMoodColor(string mood) => mood.ToLowerInvariant() switch
    {
        "mysterious" => "#6c757d",
        "tense" => "#dc3545",
        "hopeful" => "#198754",
        "epic" => "#ffc107",
        "serene" => "#0dcaf0",
        _ => "#adb5bd"
    };

    private double CalculateMoodWidth(MoodBeat mood, List<MoodBeat> allMoods)
    {
        var totalSegments = allMoods.Max(m => m.EndSegment ?? 999);
        var moodDuration = (mood.EndSegment ?? totalSegments) - mood.StartSegment;
        return (moodDuration * 100.0) / totalSegments;
    }
}
```

**Step 2: Add CSS for mood timeline**

Add to your component's CSS or shared stylesheet:

```css
.mood-timeline {
    display: flex;
    height: 24px;
    border-radius: 4px;
    overflow: hidden;
}

.mood-segment {
    display: flex;
    align-items: center;
    justify-content: center;
    font-size: 10px;
    color: white;
    cursor: help;
}
```

**Step 3: Add to BrollPromptsView**

Add the context viewer above the item list:

```html
<GlobalContextView Context="@_globalContext" />
```

**Step 4: Commit**

```bash
git add Components/Views/ScriptGenerator/GlobalContextView.razor
git add wwwroot/css/  (or wherever the CSS goes)
git commit -m "feat: add global context viewer UI component"
```

---

## Task 8: Testing and Validation

**Files:**
- No new files
- Manual testing with existing components

**Step 1: Test batch generation with context**

1. Open the ScriptGenerator page
2. Load a script with multiple segments
3. Click "Generate All Prompts"
4. Verify:
   - Global context is extracted first (check logs)
   - Prompts are generated with narrative consistency
   - Mood beats are displayed in the UI
   - Prompts reflect mood progression (mysterious → tense → hopeful)

**Step 2: Test single segment regeneration**

1. Click "Regenerate" on a specific segment (e.g., segment 30)
2. Verify:
   - The regenerated prompt considers previous/next segments
   - Uses the correct mood beat for that segment
   - Maintains consistency with global context

**Step 3: Test Auto visual settings**

1. Set `ImagePromptConfig` with `Lighting = Auto`, `Palette = Auto`, `Angle = Auto`
2. Generate prompts
3. Verify prompts include appropriate lighting/palette/angle for each mood beat

**Step 4: Test manual override**

1. Set `Lighting = DramaticHighContrast` (not Auto)
2. Generate prompts
3. Verify all prompts use dramatic lighting, ignoring mood beat suggestions

**Step 5: Check edge cases**

- Empty script segments
- Scripts with only 1-2 segments
- Scripts with 50+ segments
- Mixed BROLL and IMAGE_GEN segments

**Step 6: Document the feature**

Create/update documentation in `docs/` explaining:
- How global context extraction works
- What mood beats are and how they're detected
- How to use Auto vs Manual visual settings
- Sliding window context size configuration

---

## Summary

This implementation adds:

1. **Global Context Models** - Data structures for narrative context
2. **Auto Enum Values** - Auto option for lighting, palette, composition
3. **Context Extraction** - LLM-based analysis of full script
4. **Context-Aware Generation** - Prompts use global + local context
5. **UI Integration** - Context viewer and updated generation flows
6. **Single Segment Support** - Context-aware regeneration

**Key Benefits:**
- ✅ Narrative consistency across all segments
- ✅ Auto-detected mood beats with visual settings
- ✅ Sliding window for local narrative flow
- ✅ Works for both batch and single segment generation
- ✅ User can override any auto-detected setting

**Estimated Implementation Time:** 3-4 hours
**Testing Time:** 1-2 hours
