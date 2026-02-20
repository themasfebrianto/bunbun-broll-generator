# Text Overlay with B-Roll Background - Implementation Plan

**Date:** 2025-02-20
**Status:** Ready for Implementation
**Design Doc:** `2025-02-20-text-overlay-with-broll-brainstorm.md`

---

## Overview

This implementation plan breaks down the text overlay feature into manageable phases, starting with core functionality and advancing to enhanced composition.

**Total Estimated Effort:** ~12-16 hours of development

---

## Phase 1: Core Text Overlay System (Priority: HIGH)

**Estimated Time:** 4-5 hours

### 1.1 Create Data Models

**File:** `Models/TextOverlay.cs` (NEW)

```csharp
namespace BunbunBroll.Models;

public class TextOverlay
{
    public TextOverlayType Type { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? ArabicText { get; set; }
    public string? Reference { get; set; }
    public int StartDelayMs { get; set; } = 500;
    public int TypingSpeedMs { get; set; } = 50;
    public TypingAnimationStyle AnimationStyle { get; set; } = TypingAnimationStyle.Typewriter;
    public TextStyle Style { get; set; } = TextStyle.Default;
}

public enum TextOverlayType
{
    QuranVerse,
    Hadith,
    RhetoricalQuestion,
    KeyPhrase
}

public enum TypingAnimationStyle
{
    Typewriter,
    WordByWord,
    FadeIn
}

public class TextStyle
{
    public string FontFamily { get; set; } = "Arial";
    public int FontSize { get; set; } = 32;
    public string Color { get; set; } = "#FFFFFF";
    public TextPosition Position { get; set; } = TextPosition.Center;
    public bool HasShadow { get; set; } = true;

    public static TextStyle Default => new();
    public static TextStyle Quran => new() { FontFamily = "Amiri", FontSize = 36, Color = "#FFD700", Position = TextPosition.Center, HasShadow = true };
    public static TextStyle Hadith => new() { FontFamily = "Times New Roman", FontSize = 32, Color = "#F5DEB3", Position = TextPosition.Center, HasShadow = true };
    public static TextStyle Question => new() { FontFamily = "Arial", FontSize = 40, Color = "#FFFFFF", Position = TextPosition.TopCenter, HasShadow = true };
}

public enum TextPosition
{
    Center, TopCenter, BottomCenter, TopLeft, TopRight, BottomLeft, BottomRight
}
```

### 1.2 Extend BrollPromptItem

**File:** `Models/BrollPromptItem.cs` (MODIFY)

Add to existing class:

```csharp
/// <summary>Text overlay for this segment (if any)</summary>
public TextOverlay? TextOverlay { get; set; }

/// <summary>Helper to check if segment has text overlay</summary>
public bool HasTextOverlay => TextOverlay != null;
```

### 1.3 Update LLM Classification

**File:** `Services/IntelligenceService.cs` (MODIFY)

**Location:** Line ~810 in `ClassifyAndGeneratePromptsAsync`

**Add to classification system prompt:**

```csharp
var overlayDetectionSection = @"

TEXT OVERLAY PRIORITY RULES:
Before deciding BROLL vs IMAGE_GEN, check if segment needs a text overlay:

SEGMENTS WITH OVERLAYS MUST HAVE:
1. Text overlay (Quran verse, hadith, question, or key phrase)
2. BROLL stock video as background (NOT image generation)

Automatically generate overlays for:
- QURAN VERSES: Script quotes/references Quranic ayat
  Type: QuranVerse
  Generate: Arabic text + translation + reference
- HADITH: Script references Prophet's sayings
  Type: Hadith
  Generate: Arabic (if available) + translation + source
- RHETORICAL QUESTIONS: Script asks questions to audience
  Type: RhetoricalQuestion
  Generate: The question text
- KEY DECLARATIONS: Important truths, core beliefs, critical points
  Type: KeyPhrase
  Generate: The key phrase

NARRATION-ONLY (no overlay):
- storytelling, explanations, transitions, context
- Use IMAGE_GEN for AI-generated visuals

OUTPUT FORMAT (JSON only):
[
  {
    ""index"": 0,
    ""mediaType"": ""BROLL"",
    ""prompt"": ""search keywords or image prompt"",
    ""textOverlay"": {
      ""type"": ""QuranVerse"",
      ""text"": ""In the name of Allah"",
      ""arabic"": ""بِسْمِ اللَّهِ"",
      ""reference"": ""Surah Al-Fatiha 1:1""
    }  // or null for narration-only
  }
]";
```

**Add to the classifySystemPrompt string before the existing rules.**

### 1.4 Update Classification Response Model

**File:** `Services/IntelligenceService.cs` (MODIFY)

**Location:** After line ~1761, add new DTO:

```csharp
public class TextOverlayDto
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("arabic")]
    public string? Arabic { get; set; }

    [JsonPropertyName("reference")]
    public string? Reference { get; set; }
}
```

**Modify `BrollClassificationResponse`:**

```csharp
public class BrollClassificationResponse
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("mediaType")]
    public string? MediaType { get; set; }

    [JsonPropertyName("prompt")]
    public string? Prompt { get; set; }

    // NEW FIELD
    [JsonPropertyName("textOverlay")]
    public TextOverlayDto? TextOverlay { get; set; }
}
```

### 1.5 Update Classification Parsing

**File:** `Services/IntelligenceService.cs` (MODIFY)

**Location:** Line ~950, in the batch processing loop

**After creating `promptItem`, add:**

```csharp
var promptItem = new BrollPromptItem
{
    Index = globalIdx,
    Timestamp = segments[globalIdx].Timestamp,
    ScriptText = segments[globalIdx].ScriptText,
    MediaType = item.MediaType?.ToUpperInvariant() == "IMAGE_GEN"
        ? BrollMediaType.ImageGeneration
        : BrollMediaType.BrollVideo,
    Prompt = item.Prompt ?? string.Empty
};

// NEW: Parse text overlay if present
if (item.TextOverlay != null)
{
    promptItem.TextOverlay = new TextOverlay
    {
        Type = Enum.Parse<TextOverlayType>(item.TextOverlay.Type, true),
        Text = item.TextOverlay.Text,
        ArabicText = item.TextOverlay.Arabic,
        Reference = item.TextOverlay.Reference,
        Style = item.TextOverlay.Type.ToLowerInvariant() switch
        {
            "quranverse" => TextStyle.Quran,
            "hadith" => TextStyle.Hadith,
            "rhetoricalquestion" => TextStyle.Question,
            _ => TextStyle.Default
        }
    };

    // Auto-enforce: Text overlays get B-roll backgrounds
    promptItem.MediaType = BrollMediaType.BrollVideo;
}

// Auto-detect era and assign appropriate filter/texture
EraLibrary.AutoAssignEraStyle(promptItem);
```

### 1.6 Update Save/Load

**File:** Check session serialization in `Program.cs` or relevant service

Ensure `TextOverlay` is included in JSON serialization. The `[JsonPropertyName]` attributes should handle this automatically.

**Verify:** Test that overlays persist after page refresh.

---

## Phase 2: Basic UI (Priority: HIGH)

**Estimated Time:** 3-4 hours

### 2.1 Update Segment Card

**File:** `Components/BrollPromptItemCard.razor` (MODIFY)

**Add callback parameters:**

```razor
@code {
    [Parameter] public EventCallback<BrollPromptItem> OnAddTextOverlay { get; set; }
    [Parameter] public EventCallback<BrollPromptItem> OnEditTextOverlay { get; set; }
    [Parameter] public EventCallback<BrollPromptItem> OnRemoveTextOverlay { get; set; }
}
```

**Add helper method:**

```razor
@code {
    private string GetOverlayTypeIcon(TextOverlayType type) => type switch
    {
        TextOverlayType.QuranVerse => "📖",
        TextOverlayType.Hadith => "🕌",
        TextOverlayType.RhetoricalQuestion => "❓",
        TextOverlayType.KeyPhrase => "⭐",
        _ => "📝"
    };
}
```

**Add text overlay section in card markup (after header, before media section):**

```razor
@* Text Overlay Section *@
@if (Item.HasTextOverlay)
{
    <div class="text-overlay-section bg-emerald-500/10 border border-emerald-500/20 rounded-lg p-3 mb-3">
        <div class="flex items-center justify-between mb-2">
            <span class="text-xs font-semibold text-emerald-400">
                @GetOverlayTypeIcon(Item.TextOverlay.Type) @Item.TextOverlay.Type
            </span>
            <div class="flex gap-1">
                <button class="text-xs px-2 py-1 bg-emerald-500/20 text-emerald-300 rounded hover:bg-emerald-500/30"
                        @onclick="() => OnEditTextOverlay.InvokeAsync(Item)">
                    ✏️ Edit
                </button>
                <button class="text-xs px-2 py-1 bg-red-500/20 text-red-300 rounded hover:bg-red-500/30"
                        @onclick="() => OnRemoveTextOverlay.InvokeAsync(Item)">
                    🗑️
                </button>
            </div>
        </div>

        <div class="space-y-1">
            @if (!string.IsNullOrEmpty(Item.TextOverlay.ArabicText))
            {
                <p class="text-right text-lg text-amber-300" dir="rtl" style="font-family: 'Amiri', serif;">
                    @Item.TextOverlay.ArabicText
                </p>
            }
            <p class="text-sm text-white">"@Item.TextOverlay.Text"</p>
            @if (!string.IsNullOrEmpty(Item.TextOverlay.Reference))
            {
                <p class="text-xs text-gray-400 italic">@Item.TextOverlay.Reference</p>
            }
        </div>
    </div>
}
else
{
    <button class="w-full text-xs py-2 border border-dashed border-gray-600 text-gray-400 rounded hover:border-emerald-500/50 hover:text-emerald-400 transition-colors mb-3"
            @onclick="() => OnAddTextOverlay.InvokeAsync(Item)">
        + Add Text Overlay
    </button>
}
```

### 2.2 Create Text Overlay Editor Modal

**File:** `Components/TextOverlayEditorModal.razor` (NEW)

```razor@inject IJSRuntime JS

<div class="fixed inset-0 bg-black/70 flex items-center justify-center z-50"
     @onclick="OnCancel">
    <div class="bg-card border border-border rounded-xl max-w-2xl w-full mx-4 max-h-[90vh] overflow-y-auto"
         @onclick:stopPropagation>

        @* Header *@
        <div class="sticky top-0 bg-card border-b border-border p-4 flex items-center justify-between">
            <h3 class="text-lg font-semibold">✏️ Edit Text Overlay</h3>
            <button class="text-muted-foreground hover:text-foreground" @onclick="OnCancel">
                <svg class="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                    <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12"/>
                </svg>
            </button>
        </div>

        <div class="p-6 space-y-5">
            @* Overlay Type *@
            <div class="space-y-2">
                <label class="text-sm font-medium">Overlay Type</label>
                <div class="flex flex-wrap gap-2">
                    @foreach (var type in Enum.GetValues<TextOverlayType>())
                    {
                        var isSelected = Overlay.Type == type;
                        <button class="@(isSelected ? "bg-emerald-600 text-white" : "bg-muted text-muted-foreground") text-xs px-3 py-1.5 rounded-full border transition-colors"
                                @onclick="() => { Overlay.Type = type; ApplyStylePreset(); }">
                            @GetTypeLabel(type)
                        </button>
                    }
                </div>
            </div>

            @* Primary Text *@
            <div class="space-y-2">
                <label class="text-sm font-medium">Text</label>
                <textarea class="w-full bg-muted/20 border border-border rounded-lg px-3 py-2 text-sm"
                          rows="2" @bind="Overlay.Text" placeholder="Enter overlay text..."></textarea>
            </div>

            @* Arabic Text (conditional) *@
            @if (Overlay.Type == TextOverlayType.QuranVerse || Overlay.Type == TextOverlayType.Hadith)
            {
                <div class="space-y-2">
                    <label class="text-sm font-medium">Arabic Text (Optional)</label>
                    <input type="text" class="w-full bg-muted/20 border border-border rounded-lg px-3 py-2 text-lg text-right"
                           dir="rtl" @bind="Overlay.ArabicText" placeholder="النص العربي..." />
                </div>

                <div class="space-y-2">
                    <label class="text-sm font-medium">Reference</label>
                    <input type="text" class="w-full bg-muted/20 border border-border rounded-lg px-3 py-2 text-sm"
                           @bind="Overlay.Reference" placeholder="e.g., Surah Al-Isra 17:70" />
                </div>
            }

            @* Animation Style *@
            <div class="space-y-2">
                <label class="text-sm font-medium">Animation</label>
                <select class="w-full bg-muted/20 border border-border rounded-lg px-3 py-2 text-sm"
                        @bind="Overlay.AnimationStyle">
                    @foreach (var style in Enum.GetValues<TypingAnimationStyle>())
                    {
                        <option value="@style">@style</option>
                    }
                </select>
            </div>

            @* Timing *@
            <div class="grid grid-cols-2 gap-4">
                <div class="space-y-2">
                    <label class="text-sm font-medium">Start Delay (ms)</label>
                    <input type="number" class="w-full bg-muted/20 border border-border rounded-lg px-3 py-2 text-sm"
                           @bind="Overlay.StartDelayMs" min="0" max="5000" step="100" />
                </div>
                <div class="space-y-2">
                    <label class="text-sm font-medium">Typing Speed (ms/char)</label>
                    <input type="number" class="w-full bg-muted/20 border border-border rounded-lg px-3 py-2 text-sm"
                           @bind="Overlay.TypingSpeedMs" min="10" max="200" step="10" />
                </div>
            </div>

            @* Style Presets *@
            <div class="space-y-2">
                <label class="text-sm font-medium">Style Preset</label>
                <div class="flex flex-wrap gap-2">
                    <button class="text-xs px-3 py-1.5 rounded-full border bg-amber-500/20 text-amber-300"
                            @onclick="() => Overlay.Style = TextStyle.Quran">
                        📖 Quran
                    </button>
                    <button class="text-xs px-3 py-1.5 rounded-full border bg-orange-500/20 text-orange-300"
                            @onclick="() => Overlay.Style = TextStyle.Hadith">
                        🕌 Hadith
                    </button>
                    <button class="text-xs px-3 py-1.5 rounded-full border bg-blue-500/20 text-blue-300"
                            @onclick="() => Overlay.Style = TextStyle.Question">
                        ❓ Question
                    </button>
                </div>
            </div>
        </div>

        @* Footer *@
        <div class="sticky bottom-0 bg-card border-t border-border p-4 flex justify-end gap-3">
            <button class="btn-secondary" @onclick="OnCancel">Cancel</button>
            <button class="btn-primary bg-emerald-600 hover:bg-emerald-500" @onclick="OnSave">
                Save Overlay
            </button>
        </div>
    </div>
</div>

@code {
    [Parameter] public TextOverlay Overlay { get; set; } = new();
    [Parameter] public EventCallback OnSave { get; set; }
    [Parameter] public EventCallback OnCancel { get; set; }

    private string GetTypeLabel(TextOverlayType type) => type switch
    {
        TextOverlayType.QuranVerse => "📖 Quran",
        TextOverlayType.Hadith => "🕌 Hadith",
        TextOverlayType.RhetoricalQuestion => "❓ Question",
        TextOverlayType.KeyPhrase => "⭐ Key Phrase",
        _ => "📝 Text"
    };

    private void ApplyStylePreset()
    {
        Overlay.Style = Overlay.Type switch
        {
            TextOverlayType.QuranVerse => TextStyle.Quran,
            TextOverlayType.Hadith => TextStyle.Hadith,
            TextOverlayType.RhetoricalQuestion => TextStyle.Question,
            _ => TextStyle.Default
        };
    }
}
```

### 2.3 Wire Up in Main View

**File:** `Components/Views/ScriptGenerator/BrollPromptsView.razor` (MODIFY)

**Add state variable:**

```razor
@code {
    private BrollPromptItem? _editingOverlayItem;
    private bool _showOverlayEditor = false;
}
```

**Add handler methods:**

```razor
@code {
    private void OnAddTextOverlay(BrollPromptItem item)
    {
        item.TextOverlay = new TextOverlay
        {
            Type = TextOverlayType.KeyPhrase,
            Style = TextStyle.Default
        };
        item.MediaType = BrollMediaType.BrollVideo; // Force B-roll
        _editingOverlayItem = item;
        _showOverlayEditor = true;
    }

    private void OnEditTextOverlay(BrollPromptItem item)
    {
        _editingOverlayItem = item;
        _showOverlayEditor = true;
    }

    private void OnRemoveTextOverlay(BrollPromptItem item)
    {
        item.TextOverlay = null;
        // Revert to ImageGeneration for narration
        item.MediaType = BrollMediaType.ImageGeneration;
    }

    private async Task SaveOverlay()
    {
        _showOverlayEditor = false;
        _editingOverlayItem = null;
        await OnSave.InvokeAsync();
    }
}
```

**Pass callbacks to segment cards:**

```razor
<BrollPromptItemCard
    Item="item"
    OnAddTextOverlay="OnAddTextOverlay"
    OnEditTextOverlay="OnEditTextOverlay"
    OnRemoveTextOverlay="OnRemoveTextOverlay"
    @* ... other existing callbacks ... *@
/>
```

**Add modal at end of component:**

```razor
@if (_showOverlayEditor && _editingOverlayItem?.TextOverlay != null)
{
    <TextOverlayEditorModal
        Overlay="@_editingOverlayItem.TextOverlay"
        OnSave="SaveOverlay"
        OnCancel="() => { _showOverlayEditor = false; _editingOverlayItem = null; }"
    />
}
```

---

## Phase 3: Video Composition (Priority: HIGH)

**Estimated Time:** 3-4 hours

### 3.1 Update VideoClip Model

**File:** `Models/ShortVideoConfig.cs` (MODIFY)

**Add to `VideoClip` record:**

```csharp
public record VideoClip
{
    // ... existing fields ...

    /// <summary>Text overlay (if any)</summary>
    public TextOverlay? TextOverlay { get; init; }

    /// <summary>Helper to check if clip has text overlay</summary>
    public bool HasTextOverlay => TextOverlay != null;
}
```

**Update `FromImage` factory:**

```csharp
public static VideoClip FromImage(
    string imagePath,
    string text,
    double duration,
    KenBurnsMotionType motion = KenBurnsMotionType.SlowZoomIn,
    VideoFilter filter = VideoFilter.None,
    VideoTexture texture = VideoTexture.None,
    int filterIntensity = 100,
    int textureOpacity = 30,
    TextOverlay? textOverlay = null)  // NEW parameter
{
    return new VideoClip
    {
        ImagePath = imagePath,
        AssociatedText = text,
        DurationSeconds = duration,
        MotionType = motion,
        Filter = filter,
        FilterIntensity = filterIntensity,
        Texture = texture,
        TextureOpacity = textureOpacity,
        TextOverlay = textOverlay  // NEW
    };
}
```

### 3.2 Add Text Rendering to Composer

**File:** `Services/ShortVideoComposer.cs` (MODIFY)

**Add new methods:**

```csharp
/// <summary>
/// Add text overlay to video using FFmpeg drawtext filter
/// </summary>
private async Task<string?> AddTextOverlayToVideoAsync(
    string inputVideo,
    TextOverlay overlay,
    string outputPath,
    CancellationToken cancellationToken = default)
{
    try
    {
        var videoInfo = await FFmpeg.GetMediaInfo(inputVideo, cancellationToken);
        var width = videoInfo.VideoStreams.First().Width;
        var height = videoInfo.VideoStreams.First().Height;
        var duration = videoInfo.Duration.TotalSeconds;

        // Build text draw filter
        var textFilter = BuildTextDrawFilter(overlay, width, height);

        // Add text with timing
        var startTime = overlay.StartDelayMs / 1000.0;

        var conversion = await FFmpeg.Conversions.FromSnippet.Input(inputVideo);
        conversion.AddParameter($"-vf \"{textFilter}:enable='between(t,{startTime},{duration})'\"");
        conversion.AddParameter($"-y \"{outputPath}\"");

        await conversion.Start(cancellationToken);

        if (File.Exists(outputPath))
            return outputPath;

        return null;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to add text overlay to video");
        return null;
    }
}

private string BuildTextDrawFilter(TextOverlay overlay, int videoWidth, int videoHeight)
{
    var (x, y) = GetPositionCoordinates(overlay.Style.Position, videoWidth, videoHeight);
    var fontColor = overlay.Style.Color.Replace("#", "");
    var fontSize = overlay.Style.FontSize;

    // Escape text
    var text = overlay.Text.Replace("'", "\\'").Replace(":", "\\:");

    // Arabic text if present
    var arabicFilter = !string.IsNullOrEmpty(overlay.ArabicText)
        ? $", drawtext=text='{overlay.ArabicText.Replace("'", "\\'")}':fontsize={fontSize + 8}:x=(w-text_w)/2:y={y - fontSize * 2}:fontcolor={fontColor}:shadowcolor=000000:shadowx=2:shadowy=2"
        : "";

    // Main text filter
    return $"drawtext=text='{text}':fontsize={fontSize}:x={x}:y={y}:fontcolor={fontColor}:shadowcolor=000000:shadowx=2:shadowy=2{arabicFilter}";
}

private (int x, int y) GetPositionCoordinates(TextPosition position, int videoWidth, int videoHeight)
{
    return position switch
    {
        TextPosition.Center => (videoWidth / 2, videoHeight / 2),
        TextPosition.TopCenter => (videoWidth / 2, videoHeight / 4),
        TextPosition.BottomCenter => (videoWidth / 2, videoHeight * 3 / 4),
        TextPosition.TopLeft => (50, videoHeight / 4),
        TextPosition.TopRight => (videoWidth - 50, videoHeight / 4),
        TextPosition.BottomLeft => (50, videoHeight * 3 / 4),
        TextPosition.BottomRight => (videoWidth - 50, videoHeight * 3 / 4),
        _ => (videoWidth / 2, videoHeight / 2)
    };
}
```

### 3.3 Integrate into Compose Pipeline

**File:** `Services/ShortVideoComposer.cs` (MODIFY)

**In `ComposeAsync` method, find where each clip is processed and add:**

```csharp
// After clip is converted/ready, check for text overlay
if (clip.HasTextOverlay && !string.IsNullOrEmpty(clipPath))
{
    var tempWithText = Path.Combine(_tempDir, $"clip_{index}_with_text.mp4");
    var withTextPath = await AddTextOverlayToVideoAsync(
        clipPath,
        clip.TextOverlay!,
        tempWithText,
        cancellationToken);

    if (withTextPath != null && File.Exists(withTextPath))
    {
        clipPath = withTextPath;  // Use version with text
        // Clean up original
        try { File.Delete(clipPath); } catch { }
    }
}
```

### 3.4 Add Arabic Font Support

**File:** `wwwroot/fonts/` (NEW directory)

**Action:** Download and add Arabic font file

```bash
# Amiri font for Quranic text
wget -O wwwroot/fonts/Amiri-Regular.ttf https://github.com/alif-type/amiri-font/releases/download/1.000/Amiri-Regular.ttf
```

**Update font path in `BuildTextDrawFilter`:**

```csharp
private string BuildTextDrawFilter(TextOverlay overlay, int videoWidth, int videoHeight)
{
    // Add fontfile parameter
    var fontPath = Path.Combine(_wwwroot, "fonts", "Amiri-Regular.ttf");
    // ... rest of method
}
```

---

## Phase 4: Enhanced Composition (Priority: MEDIUM)

**Estimated Time:** 2-3 hours

### 4.1 Create Content-Aware Composition Service

**File:** `Services/ContentAwareCompositionService.cs` (NEW)

```csharp
namespace BunbunBroll.Services;

public interface IContentAwareCompositionService
{
    ImageComposition GetCompositionForSegment(
        string scriptText,
        GlobalScriptContext? context,
        int segmentIndex,
        List<ImageComposition> recentCompositions);
}

public class ContentAwareCompositionService : IContentAwareCompositionService
{
    public ImageComposition GetCompositionForSegment(
        string scriptText,
        GlobalScriptContext? context,
        int segmentIndex,
        List<ImageComposition> recentCompositions)
    {
        // 1. Detect scene type
        var sceneType = DetectSceneType(scriptText, context, segmentIndex);

        // 2. Get base composition
        var baseComp = GetCompositionForScene(sceneType);

        // 3. Anti-repetition check
        if (recentCompositions.Take(2).Contains(baseComp))
        {
            baseComp = GetAlternativeComposition(baseComp, sceneType);
        }

        return baseComp;
    }

    private SceneType DetectSceneType(string text, GlobalScriptContext? ctx, int idx)
    {
        var lower = text.ToLower();

        // Intimate/personal
        if (lower.Contains("cave") || lower.Contains("holding") || lower.Contains("hand")
            || lower.Contains("face") || lower.Contains("intimate"))
            return SceneType.Intimate;

        // Landscape
        if (lower.Contains("desert") || lower.Contains("mountain") || lower.Contains("sea")
            || lower.Contains("horizon") || lower.Contains("vast"))
            return SceneType.Landscape;

        // Powerful/imposing
        if (lower.Contains("throne") || lower.Contains("king") || lower.Contains("army")
            || lower.Contains("battle") || lower.Contains("power"))
            return SceneType.Powerful;

        // Overview
        if (lower.Contains("region") || lower.Contains("kingdom") || lower.Contains("empire")
            || lower.Contains("map") || lower.Contains("territory"))
            return SceneType.Overview;

        // Default: medium/architectural
        return SceneType.Architectural;
    }

    private ImageComposition GetCompositionForScene(SceneType scene) => scene switch
    {
        SceneType.Intimate => ImageComposition.CloseUp,
        SceneType.Landscape => ImageComposition.CinematicWide,
        SceneType.Powerful => ImageComposition.LowAngle,
        SceneType.Overview => ImageComposition.BirdsEye,
        SceneType.Architectural => ImageComposition.WideShot,
        _ => ImageComposition.Auto
    };

    private ImageComposition GetAlternativeComposition(ImageComposition current, SceneType scene)
    {
        var alternatives = scene switch
        {
            SceneType.Intimate => new[] { ImageComposition.WideShot, ImageComposition.LowAngle },
            SceneType.Landscape => new[] { ImageComposition.WideShot, ImageComposition.BirdsEye },
            SceneType.Powerful => new[] { ImageComposition.CinematicWide, ImageComposition.LowAngle },
            SceneType.Overview => new[] { ImageComposition.WideShot, ImageComposition.CinematicWide },
            _ => new[] { ImageComposition.WideShot, ImageComposition.CloseUp }
        };

        return alternatives.FirstOrDefault(a => a != current) ?? ImageComposition.WideShot;
    }
}

internal enum SceneType
{
    Intimate,
    Landscape,
    Powerful,
    Overview,
    Architectural
}
```

### 4.2 Register Service

**File:** `Program.cs` (MODIFY)

```csharp
builder.Services.AddScoped<IContentAwareCompositionService, ContentAwareCompositionService>();
```

### 4.3 Integrate into Prompt Generation

**File:** `Services/IntelligenceService.cs` (MODIFY)

**Inject service:**

```csharp
private readonly IContentAwareCompositionService _compositionService;

public IntelligenceService(
    HttpClient httpClient,
    ILogger<IntelligenceService> logger,
    IOptions<GeminiSettings> settings,
    IContentAwareCompositionService compositionService)  // NEW
{
    _httpClient = httpClient;
    _logger = logger;
    _settings = settings.Value;
    _compositionService = compositionService;  // NEW
}
```

**Use in `GeneratePromptForTypeAsync`:**

```csharp
// Replace the existing GetDynamicComposition call with:
if (mediaType == BrollMediaType.ImageGeneration && activeConfig.Composition == ImageComposition.Auto)
{
    var recentCompositions = allItems.Take(Math.Max(0, segmentIndex - 5))
        .Where(i => i.MediaType == BrollMediaType.ImageGeneration)
        .Select(i => i.Composition)  // You'll need to track this
        .ToList();

    activeConfig = new ImagePromptConfig
    {
        ArtStyle = activeConfig.ArtStyle,
        CustomArtStyle = activeConfig.CustomArtStyle,
        Lighting = activeConfig.Lighting,
        ColorPalette = activeConfig.ColorPalette,
        Composition = _compositionService.GetCompositionForSegment(
            scriptText, null, segmentIndex, recentCompositions),
        DefaultEra = activeConfig.DefaultEra,
        CustomInstructions = activeConfig.CustomInstructions
    };
}
```

---

## Phase 5: Polish & Advanced Features (Priority: LOW)

**Estimated Time:** Optional / As needed

### 5.1 More Animation Styles

Add glitch, terminal, and other typing effects to `TypingAnimationStyle` enum.

### 5.2 Bulk Operations

Add buttons to apply style presets to all overlays at once.

### 5.3 Real-time Preview

Use Blazor's preview to show text overlay over selected B-roll thumbnail.

### 5.4 Export/Import Configurations

Allow saving and loading text overlay configurations as JSON.

---

## Testing Checklist

### Phase 1 Tests
- [ ] TextOverlay model compiles
- [ ] BrollPromptItem has TextOverlay field
- [ ] LLM classification returns textOverlay field
- [ ] Text overlay parsing works correctly
- [ ] Overlays persist after save/load

### Phase 2 Tests
- [ ] Segment card shows overlay badge
- [ ] Add overlay button appears for narration segments
- [ ] Edit/remove buttons appear for overlay segments
- [ ] Arabic text displays correctly (RTL)
- [ ] Modal opens and saves correctly
- [ ] Style presets apply correctly

### Phase 3 Tests
- [ ] VideoClip includes TextOverlay
- [ ] FFmpeg adds text to video
- [ ] Typing animation timing works
- [ ] Arabic font renders correctly
- [ ] Position coordinates are correct
- [ ] Text appears over B-roll background

### Phase 4 Tests
- [ ] Composition varies by content type
- [ ] Anti-repetition prevents consecutive same angles
- [ ] Service integrates with existing workflow

### Integration Tests
- [ ] Full workflow: Import → Classify → Edit → Compose
- [ ] Quran verse gets Arabic + reference
- [ ] Hadith gets styling
- [ ] Questions get question styling
- [ ] Narration segments get AI images
- [ ] Final video has correct overlays

---

## Rollback Plan

If issues arise, each phase can be independently rolled back:

1. **Phase 1**: Remove TextOverlay field from BrollPromptItem
2. **Phase 2**: Remove UI components and callbacks
3. **Phase 3**: Remove text rendering from composer
4. **Phase 4**: Remove composition service injection

No breaking changes to existing functionality - all additions are opt-in via the `TextOverlay` nullable field.
