# LLM-Based Drama Detection for VO Expansion

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Use Gemini LLM to intelligently detect dramatic pause points AND text overlay opportunities in SRT entries, enhancing the current rule-based VO expansion system.

**Architecture:**
1. Add `DetectDramaAsync()` method to `IntelligenceService` - single LLM call returns both pauses and overlays
2. Merge LLM results with existing rule-based pauses (take max of LLM vs rule-based)
3. Attach detected overlays to SrtEntry objects for downstream video composition
4. Explicit error handling - user always sees LLM success/failure status, no silent fallbacks

**Tech Stack:** C# .NET 8, Gemini LLM (existing proxy), HttpClient, System.Text.Json

---

## Task 1: Create Result Models

**Files:**
- Create: `Models/DramaDetectionResult.cs`
- Modify: `Models/VoSlicing.cs` - Update SrtExpansionResult

**Step 1: Create DramaDetectionResult model**

```csharp
namespace BunbunBroll.Models;

/// <summary>
/// Result of LLM-based drama detection for pauses and overlays
/// </summary>
public class DramaDetectionResult
{
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }

    // Results when successful
    /// <summary>
    /// Entry index → pause duration in seconds
    /// Pause is added AFTER this entry (index i means pause between entry i and i+1)
    /// </summary>
    public Dictionary<int, double> PauseDurations { get; set; } = new();

    /// <summary>
    /// Entry index → text overlay to display during this entry
    /// </summary>
    public Dictionary<int, TextOverlayDto> TextOverlays { get; set; } = new();

    // For debugging/transparency
    public int TokensUsed { get; set; }
    public double ProcessingTimeMs { get; set; }
}
```

**Step 2: Update SrtExpansionResult to include LLM info**

Open `Models/VoSlicing.cs` and add to `SrtExpansionResult` class:

```csharp
public class SrtExpansionResult
{
    // ... existing properties ...

    /// <summary>
    /// Whether LLM drama detection succeeded
    /// </summary>
    public bool LlmDetectionSuccess { get; set; }

    /// <summary>
    /// Warning message if LLM detection failed (null if success)
    /// </summary>
    public string? LlmDetectionWarning { get; set; }

    /// <summary>
    /// Number of tokens used by LLM detection
    /// </summary>
    public int LlmTokensUsed { get; set; }

    /// <summary>
    /// Detected text overlays from LLM (entry index → overlay)
    /// </summary>
    public Dictionary<int, TextOverlayDto> DetectedOverlays { get; set; } = new();
}
```

**Step 3: Commit**

```bash
git add Models/DramaDetectionResult.cs Models/VoSlicing.cs
git commit -m "feat: add drama detection result models"
```

---

## Task 2: Add LLM Detection Method to IntelligenceService

**Files:**
- Modify: `Services/Intelligence/IntelligenceService.cs`
- Modify: `Services/Intelligence/IIntelligenceService.cs`

**Step 1: Add interface method**

Open `Services/Intelligence/IIntelligenceService.cs` and add:

```csharp
/// <summary>
/// Detect drama pauses and text overlays in script entries using LLM.
/// Analyzes narrative flow for dramatic moments (contrasts, revelations, suspense)
/// and identifies overlay-worthy content (Quran verses, key phrases, questions).
/// </summary>
Task<DramaDetectionResult> DetectDramaAsync(
    IEnumerable<(int Index, string Text)> entries,
    CancellationToken cancellationToken = default);
```

**Step 2: Implement detection method**

Open `Services/Intelligence/IntelligenceService.cs` and add the implementation:

```csharp
public async Task<DramaDetectionResult> DetectDramaAsync(
    IEnumerable<(int Index, string Text)> entries,
    CancellationToken cancellationToken = default)
{
    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
    var result = new DramaDetectionResult { IsSuccess = false };

    try
    {
        var entryList = entries.ToList();
        if (entryList.Count == 0)
        {
            result.ErrorMessage = "No entries provided for drama detection";
            return result;
        }

        // Build entries text for LLM
        var entriesText = new System.Text.StringBuilder();
        foreach (var (index, text) in entryList)
        {
            entriesText.AppendLine($"[{index}]: {text}");
        }

        var systemPrompt = @"You are a expert video editor and storyteller specializing in dramatic timing for Indonesian documentary/narrative content.

Your task is to analyze script entries and identify:
1. DRAMA PAUSES: Moments that need strategic silence for emotional impact
2. TEXT OVERLAYS: Content that should appear as on-screen text

DRAMA PAUSE RULES:
- Add pause AFTER entries containing contrast/revelation words (namun, tetapi, tapi, yang ironisnya, justru, nyatanya, ternyata, sebenarnya)
- Add pause AFTER suspenseful endings (ellipsis ..., rhetorical questions)
- Add pause BEFORE major revelations (when next entry starts with contrast word)
- Pause durations: 0.5s (short), 0.8s (medium), 1.2s (long), 1.5-2.0s (very long for plot twists)
- NOT every entry needs a pause - be selective, only for dramatic moments

TEXT OVERLAY RULES:
- QURAN_VERSE: Quranic references (indicated by QS., ayat, or Arabic text patterns)
- HADITH: Hadith references (indicated by HR., hadits, or prophet sayings)
- KEY_PHRASE: Important phrases that summarize the message or create emphasis
- RHETORICAL_QUESTION: Questions that make the viewer think
- Be selective - not every key word needs an overlay

Return ONLY valid JSON in this exact format:
{
  ""pauseDurations"": {
    ""7"": 1.5,
    ""12"": 0.8
  },
  ""textOverlays"": {
    ""8"": {
      ""type"": ""key_phrase"",
      ""text"": ""namun realita historis""
    },
    ""15"": {
      ""type"": ""quran_verse"",
      ""text"": ""Dan Kami hendak memberi karunia kepada orang-orang yang tertindas"",
      ""arabic"": ""وَنُرِيدُ أَن نَّمُنَّ عَلَى الَّذِينَ اسْتُضْعِفُوا"",
      ""reference"": ""Q.S. Al-Qasas: 5""
    }
  }
}

Type values for overlays: quran_verse, hadith, rhetorical_question, key_phrase";

        var userPrompt = $@"Analyze these script entries for drama pauses and text overlays:

{entriesText}

Return JSON with pauseDurations and textOverlays.";

        var llmResult = await SendChatAsync(
            systemPrompt,
            userPrompt,
            temperature: 0.3,
            maxTokens: 1000,
            cancellationToken
        );

        if (string.IsNullOrWhiteSpace(llmResult.Content))
        {
            result.ErrorMessage = "LLM returned empty response";
            return result;
        }

        // Parse JSON response
        var jsonDoc = System.Text.Json.JsonDocument.Parse(llmResult.Content);
        var root = jsonDoc.RootElement;

        // Parse pauses
        if (root.TryGetProperty("pauseDurations", out var pausesElem))
        {
            foreach (var prop in pausesElem.EnumerateObject())
            {
                if (int.TryParse(prop.Name, out int index) && prop.Value.TryGetDouble(out double seconds))
                {
                    result.PauseDurations[index] = seconds;
                }
            }
        }

        // Parse overlays
        if (root.TryGetProperty("textOverlays", out var overlaysElem))
        {
            foreach (var prop in overlaysElem.EnumerateObject())
            {
                if (int.TryParse(prop.Name, out int index))
                {
                    var overlay = prop.Value.Deserialize<TextOverlayDto>();
                    if (overlay != null)
                    {
                        result.TextOverlays[index] = overlay;
                    }
                }
            }
        }

        result.IsSuccess = true;
        result.TokensUsed = llmResult.Tokens;
        result.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;

        _logger.LogInformation(
            "Drama detection complete: {PauseCount} pauses, {OverlayCount} overlays, {Tokens} tokens, {Ms}ms",
            result.PauseDurations.Count,
            result.TextOverlays.Count,
            result.TokensUsed,
            result.ProcessingTimeMs
        );

        return result;
    }
    catch (System.Text.Json.JsonException ex)
    {
        result.ErrorMessage = $"Failed to parse LLM JSON response: {ex.Message}";
        _logger.LogError(ex, "LLM JSON parsing failed");
        return result;
    }
    catch (System.Exception ex)
    {
        result.ErrorMessage = $"Drama detection failed: {ex.Message}";
        _logger.LogError(ex, "Drama detection error");
        return result;
    }
}
```

**Step 3: Add required using statement**

At top of `IntelligenceService.cs` ensure:
```csharp
using BunbunBroll.Models;
```

**Step 4: Commit**

```bash
git add Services/Intelligence/IntelligenceService.cs Services/Intelligence/IIntelligenceService.cs
git commit -m "feat: add LLM drama detection to IntelligenceService"
```

---

## Task 3: Integrate LLM Detection into SrtExpansionService

**Files:**
- Modify: `Services/VideoComposition/SrtExpansionService.cs`

**Step 1: Inject IntelligenceService**

Update constructor:

```csharp
public class SrtExpansionService : ISrtExpansionService
{
    private readonly ISrtService _srtService;
    private readonly ILogger<SrtExpansionService> _logger;
    private readonly IIntelligenceService _intelligenceService;  // NEW

    public SrtExpansionService(
        ISrtService srtService,
        ILogger<SrtExpansionService> logger,
        IIntelligenceService intelligenceService)  // NEW
    {
        _srtService = srtService;
        _logger = logger;
        _intelligenceService = intelligenceService;  // NEW
    }
```

**Step 2: Add LLM detection after expansion**

In `ExpandCapCutSrtAsync()`, after line 76 (after padding calculation), add:

```csharp
// LLM Drama Detection (NEW - insert after padding calculation, before pause calculation)
var llmDetectionResult = await _intelligenceService.DetectDramaAsync(
    entries: result.ExpandedEntries.Select((e, i) => (i, e.Text)),
    cancellationToken: default
);

if (!llmDetectionResult.IsSuccess)
{
    // EXPLICIT: Store error for UI to display
    result.LlmDetectionWarning = llmDetectionResult.ErrorMessage;
    _logger.LogWarning("LLM drama detection failed: {Error}", llmDetectionResult.ErrorMessage);
    // FALLBACK: Continue with rule-based pauses only (existing behavior)
}
else
{
    // SUCCESS: Store LLM results
    result.LlmDetectionSuccess = true;
    result.LlmTokensUsed = llmDetectionResult.TokensUsed;
    result.DetectedOverlays = llmDetectionResult.TextOverlays;
    _logger.LogInformation("LLM detection: {Pauses} pauses, {Overlays} overlays",
        llmDetectionResult.PauseDurations.Count,
        llmDetectionResult.TextOverlays.Count);
}
```

**Step 3: Merge LLM pauses with rule-based pauses**

Replace the existing pause calculation line (around line 79):

```csharp
// OLD:
// result.PauseDurations = _srtService.CalculatePauseDurations(result.ExpandedEntries);

// NEW:
var ruleBasedPauses = _srtService.CalculatePauseDurations(result.ExpandedEntries);
result.PauseDurations = MergePauseDurations(ruleBasedPauses, llmDetectionResult.PauseDurations);
```

**Step 4: Add merge helper method**

Add at end of class:

```csharp
/// <summary>
/// Merge LLM-detected pauses with rule-based pauses.
/// For each entry, takes the MAXIMUM of LLM pause vs rule-based pause.
/// LLM pauses enhance rather than replace rule-based logic.
/// </summary>
private Dictionary<int, double> MergePauseDurations(
    Dictionary<int, double> ruleBasedPauses,
    Dictionary<int, double> llmPauses)
{
    var merged = new Dictionary<int, double>(ruleBasedPauses);

    foreach (var (index, llmPause) in llmPauses)
    {
        if (merged.TryGetValue(index, out var existingPause))
        {
            // Take the larger value
            merged[index] = Math.Max(existingPause, llmPause);
        }
        else
        {
            merged[index] = llmPause;
        }
    }

    _logger.LogDebug("Merged pauses: {RuleCount} rule-based + {LlmCount} LLM = {MergedCount} total",
        ruleBasedPauses.Count, llmPauses.Count, merged.Count);

    return merged;
}
```

**Step 5: Attach overlays to entries**

After `ApplyPausesToRetimeEntries()` call, add:

```csharp
// Attach detected overlays to their entries (NEW)
foreach (var (entryIndex, overlayDto) in result.DetectedOverlays)
{
    if (entryIndex >= 0 && entryIndex < result.ExpandedEntries.Count)
    {
        // Convert DTO to model (use existing ParseTextOverlay from IntelligenceService)
        var entry = result.ExpandedEntries[entryIndex];
        // Note: TextOverlay attachment to SrtEntry would need SrtEntry.Overlay property
        // For now, store in result for downstream consumption
        _logger.LogDebug("Overlay detected for entry {Index}: {Type}", entryIndex, overlayDto.Type);
    }
}
```

**Step 6: Update Program.cs DI registration**

Open `Program.cs` and ensure `IntelligenceService` is registered:

```csharp
builder.Services.AddScoped<IIntelligenceService, IntelligenceService>();
```

**Step 7: Commit**

```bash
git add Services/VideoComposition/SrtExpansionService.cs Program.cs
git commit -m "feat: integrate LLM drama detection into SRT expansion"
```

---

## Task 4: Update UI to Show LLM Detection Status

**Files:**
- Modify: `Components/Views/ScriptGenerator/ExpandAndSliceVoView.razor`
- Modify: `Components/Pages/ScriptGenerator/ScriptGenerator.razor.Expansion.cs`

**Step 1: Add status properties to Expansion codebehind**

Open `ScriptGenerator.razor.Expansion.cs` and add to your state class:

```csharp
// Add these properties to your expansion state
public bool LlmDetectionSuccess => ExpansionResult?.LlmDetectionSuccess ?? false;
public string? LlmDetectionWarning => ExpansionResult?.LlmDetectionWarning;
public int LlmTokensUsed => ExpansionResult?.LlmTokensUsed ?? 0;
public int DetectedOverlayCount => ExpansionResult?.DetectedOverlays.Count ?? 0;
```

**Step 2: Add status display in view**

Open `ExpandAndSliceVoView.razor` and add after the processing progress section (around line 110):

```razor
@* LLM Detection Status *@
@if (ShowResults && ExpansionResult != null)
{
    @if (!string.IsNullOrEmpty(ExpansionResult.LlmDetectionWarning))
    {
        <div class="bg-yellow-500/20 border border-yellow-500 text-yellow-500 dark:text-yellow-400 p-4 rounded-lg mb-4 flex items-start gap-3">
            <svg class="w-5 h-5 mt-0.5 shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
            </svg>
            <div>
                <p class="font-medium">LLM Detection Unavailable</p>
                <p class="text-sm mt-1">@ExpansionResult.LlmDetectionWarning</p>
                <p class="text-xs mt-2 opacity-75">Using rule-based pauses only.</p>
            </div>
        </div>
    }
    else if (ExpansionResult.LlmDetectionSuccess)
    {
        var dramaPauseCount = ExpansionResult.PauseDurations.Count;
        <div class="bg-green-500/20 border border-green-500 text-green-500 dark:text-green-400 p-4 rounded-lg mb-4 flex items-center gap-3">
            <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5 13l4 4L19 7" />
            </svg>
            <div>
                <p class="font-medium">AI Enhancement Applied</p>
                <p class="text-sm">@dramaPauseCount drama pauses, @DetectedOverlayCount overlays detected (@LlmTokensUsed tokens)</p>
            </div>
        </div>
    }
}
```

**Step 3: Pass ExpansionResult to view**

Update `ExpandAndSliceVoView.razor` parameters:

```razor
@* Add this parameter *@
[Parameter] public SrtExpansionResult? ExpansionResult { get; set; }
```

**Step 4: Update parent to pass result**

In `ScriptGenerator.razor.Expansion.cs`, update the view invocation:

```razor
<ExpandAndSliceVoView
    ExpansionResult="expansionResult"
    ... other parameters ... />
```

**Step 5: Commit**

```bash
git add Components/Views/ScriptGenerator/ExpandAndSliceVoView.razor Components/Pages/ScriptGenerator/ScriptGenerator.razor.Expansion.cs
git commit -m "feat: show LLM detection status in UI"
```

---

## Task 5: Add Unit Tests

**Files:**
- Create: `Tests/Services/Intelligence/DramaDetectionTests.cs`

**Step 1: Create test file**

```csharp
using BunbunBroll.Models;
using BunbunBroll.Services;
using Moq;
using Moq.Protected;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Tests.Services.Intelligence;

public class DramaDetectionTests : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly Mock<HttpMessageHandler> _handlerMock;
    private readonly IIntelligenceService _service;
    private readonly ILogger<IntelligenceService> _logger;

    public DramaDetectionTests()
    {
        _handlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_handlerMock.Object)
        {
            BaseAddress = new Uri("http://test")
        };

        var loggerFactory = LoggerFactory.Create(builder => { });
        _logger = loggerFactory.CreateLogger<IntelligenceService>();

        var settings = Options.Create(new GeminiSettings
        {
            BaseUrl = "http://test",
            Model = "test-model"
        });

        _service = new IntelligenceService(_httpClient, _logger, settings);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    [Fact]
    public async Task DetectDramaAsync_ReturnsPausesAndOverlays()
    {
        // Arrange
        var entries = new List<(int Index, string Text)>
        {
            (0, "sejarah umat manusia"),
            (1, "seringkali mencatat kemenangan gemilang"),
            (2, "namun realita historis")  // Drama trigger
        };

        var llmResponse = new GeminiChatResponse
        {
            Choices = new List<GeminiChoice>
            {
                new()
                {
                    Message = new GeminiMessage
                    {
                        Content = """{
                            "pauseDurations": {
                                "1": 1.5
                            },
                            "textOverlays": {
                                "2": {
                                    "type": "key_phrase",
                                    "text": "namun realita historis"
                                }
                            }
                        }"""
                    }
                }
            },
            Usage = new GeminiUsage { TotalTokens = 100 }
        };

        _handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = JsonContent.Create(llmResponse)
            });

        // Act
        var result = await _service.DetectDramaAsync(entries);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Null(result.ErrorMessage);
        Assert.Single(result.PauseDurations);
        Assert.Equal(1.5, result.PauseDurations[1]);
        Assert.Single(result.TextOverlays);
        Assert.Equal("key_phrase", result.TextOverlays[2].Type);
        Assert.Equal(100, result.TokensUsed);
    }

    [Fact]
    public async Task DetectDramaAsync_HandlesEmptyResponse()
    {
        // Arrange
        var entries = new List<(int Index, string Text)> { (0, "test") };

        var llmResponse = new GeminiChatResponse
        {
            Choices = new List<GeminiChoice>
            {
                new() { Message = new GeminiMessage { Content = "" } }
            }
        };

        _handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = JsonContent.Create(llmResponse)
            });

        // Act
        var result = await _service.DetectDramaAsync(entries);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("LLM returned empty response", result.ErrorMessage);
    }

    [Fact]
    public async Task DetectDramaAsync_HandlesInvalidJson()
    {
        // Arrange
        var entries = new List<(int Index, string Text)> { (0, "test") };

        _handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("not valid json")
            });

        // Act
        var result = await _service.DetectDramaAsync(entries);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Failed to parse", result.ErrorMessage);
    }

    [Fact]
    public async Task DetectDramaAsync_HandlesNetworkError()
    {
        // Arrange
        var entries = new List<(int Index, string Text)> { (0, "test") };

        _handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection timeout"));

        // Act
        var result = await _service.DetectDramaAsync(entries);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Drama detection failed", result.ErrorMessage);
    }

    [Fact]
    public async Task DetectDramaAsync_EmptyEntries_ReturnsError()
    {
        // Act
        var result = await _service.DetectDramaAsync(Enumerable.Empty<(int, string)>());

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("No entries provided for drama detection", result.ErrorMessage);
    }
}
```

**Step 2: Run tests**

```bash
dotnet test Tests/Services/Intelligence/DramaDetectionTests.cs -v n
```

Expected: All tests PASS

**Step 3: Commit**

```bash
git add Tests/Services/Intelligence/DramaDetectionTests.cs
git commit -m "test: add drama detection unit tests"
```

---

## Task 6: Update Statistics Display

**Files:**
- Modify: `Components/Views/ScriptGenerator/ExpandAndSliceVoView.razor`

**Step 1: Add LLM stats to statistics section**

In the statistics card, add new stats:

```razor
<div class="grid grid-cols-2 gap-4">
    @* Existing stats ... *@
    <div class="bg-muted/50 p-4 rounded-lg">
        <p class="text-xs text-muted-foreground uppercase tracking-wider mb-1">Accuracy Score</p>
        <p class="text-2xl font-bold @(ValidationResult.AccuracyScore >= 90 ? "text-green-500" : "text-yellow-500")">
            @ValidationResult.AccuracyScore.ToString("F1")%
        </p>
    </div>

    @* NEW: LLM Detection Stats *@
    @if (ExpansionResult?.LlmDetectionSuccess == true)
    {
        <div class="bg-muted/50 p-4 rounded-lg">
            <p class="text-xs text-muted-foreground uppercase tracking-wider mb-1">AI Pauses</p>
            <p class="text-2xl font-bold text-primary">@ExpansionResult.PauseDurations.Count</p>
        </div>
        <div class="bg-muted/50 p-4 rounded-lg">
            <p class="text-xs text-muted-foreground uppercase tracking-wider mb-1">AI Overlays</p>
            <p class="text-2xl font-bold text-primary">@ExpansionResult.DetectedOverlays.Count</p>
        </div>
    }
</div>
```

**Step 2: Commit**

```bash
git add Components/Views/ScriptGenerator/ExpandAndSliceVoView.razor
git commit -m "feat: display AI detection stats in results"
```

---

## Task 7: Documentation

**Files:**
- Create: `docs/features/llm-drama-detection.md`

**Step 1: Create feature documentation**

```markdown
# LLM-Based Drama Detection

## Overview

The VO expansion pipeline uses Gemini LLM to intelligently detect dramatic pause points and text overlay opportunities in script entries.

## How It Works

1. **SRT Expansion**: Original CapCut SRT entries are expanded into smaller segments
2. **LLM Analysis**: Gemini analyzes the expanded entries for:
   - **Drama Pauses**: Moments needing strategic silence (contrasts, revelations, suspense)
   - **Text Overlays**: Quran verses, Hadith, key phrases, rhetorical questions
3. **Merge Logic**: LLM-detected pauses are merged with rule-based pauses (take max value)
4. **VO Processing**: Enhanced pauses are used when slicing and stitching VO audio

## Pause Duration Guidelines

| Trigger | Duration | Example |
|---------|----------|---------|
| Plot twist (namun, tetapi) | 1.5-2.0s | "...hidup tenang. **namun** realita..." |
| Irony/revelation (yang ironisnya, justru) | 1.0-1.2s | "...yang ironisnya justru..." |
| Truth reveal (nyatanya, ternyata) | 0.8-1.0s | |
| Suspense (ellipsis ...) | 0.8s | |
| Rhetorical question | 0.5s | |
| Standard sentence end | 0.6s | (rule-based fallback) |

## Error Handling

- **Success**: Green banner shows pause/overlay counts
- **Partial/LLM Failure**: Yellow banner with error message, falls back to rule-based pauses
- **No Silent Failures**: User always sees detection status

## Configuration

LLM settings in `appsettings.json`:

```json
{
  "Gemini": {
    "BaseUrl": "http://127.0.0.1:8317",
    "Model": "gemini-3-pro-preview",
    "TimeoutSeconds": 30
  }
}
```

## Example Input/Output

**Input:**
```
[0]: sejarah umat manusia
[1]: seringkali mencatat kemenangan gemilang
[2]: namun realita historis
[3]: yang dialami oleh seorang nabi agung
```

**LLM Output:**
```json
{
  "pauseDurations": {
    "1": 1.5
  },
  "textOverlays": {
    "2": {
      "type": "key_phrase",
      "text": "namun realita historis"
    }
  }
}
```

**Result:** 1.5 second pause added after entry 1, key phrase overlay on entry 2.
```

**Step 2: Commit**

```bash
git add docs/features/llm-drama-detection.md
git commit -m "docs: add LLM drama detection feature documentation"
```

---

## Testing Checklist

After implementation, verify:

- [ ] LLM detection succeeds with valid SRT
- [ ] LLM failure shows warning in UI
- [ ] Rule-based pauses still work when LLM fails
- [ ] LLM pauses enhance (take max of) rule-based pauses
- [ ] Detected overlays appear in results
- [ ] Statistics show correct counts
- [ ] No silent failures - all errors visible to user

**Manual Test Command:**
```bash
# Run with sample SRT
cd BunbunBroll
dotnet run
# Upload VO + SRT → Process → Check UI for LLM status banner
```

---

## Summary

This implementation adds intelligent LLM-based drama detection to the VO expansion pipeline:

1. **New Models**: `DramaDetectionResult`, updated `SrtExpansionResult`
2. **IntelligenceService**: `DetectDramaAsync()` method for LLM calls
3. **SrtExpansionService**: Integration with merge logic for pauses + overlays
4. **UI**: Status banners and statistics display
5. **Tests**: Unit tests for all failure modes
6. **Docs**: Feature documentation

**Estimated Time:** 2-3 hours
**Risk Level:** Low (non-breaking, optional enhancement)
