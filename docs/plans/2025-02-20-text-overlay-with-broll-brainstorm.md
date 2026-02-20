# Text Overlay with B-Roll Background - Design Brainstorm

**Date:** 2025-02-20
**Status:** Design Phase
**Related Issues:** Image composition accuracy, B-roll with typing text animation

---

## Executive Summary

This design introduces a **two-track visual system** where:

1. **Emphasized segments (20-30%)** receive **text overlays + B-roll stock video backgrounds**
2. **Narration segments (70-80%)** receive **AI-generated images** with Ken Burns motion

This approach:
- Reduces B-roll generation workload by 70-80%
- Creates clear visual hierarchy (emphasis vs. supporting)
- Maintains engagement through typographic animation
- Integrates cleanly with existing `BrollMediaType` classification

---

## The Vision

### Current Problem

- Every segment requires B-roll or AI image generation
- No visual distinction between key moments and ordinary narration
- Flat visual pacing - everything looks the same
- High generation/load for assets that may not be necessary

### Proposed Solution

```
┌─────────────────────────────────────────────────────────────┐
│                    SCRIPT SEGMENTS                          │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│  Track 1: EMPHASIZED SEGMENTS (20-30%)                      │
│  ┌─────────────────────────────────────────────────────┐    │
│  │ • Quran verses                                       │    │
│  │ • Hadith                                            │    │
│  │ • Rhetorical questions                              │    │
│  │ • Key phrases / declarations                        │    │
│  │                                                      │    │
│  │ Visual: TEXT OVERLAY + B-ROLL STOCK VIDEO BG        │    │
│  │         ↓                    ↓                      │    │
│  │    Typing animation     Pexels/Pixabay footage     │    │
│  └─────────────────────────────────────────────────────┘    │
│                                                              │
│  Track 2: NARRATION SEGMENTS (70-80%)                       │
│  ┌─────────────────────────────────────────────────────┐    │
│  │ • Storytelling                                       │    │
│  │ • Explanations                                       │    │
│  │ • Transitions                                        │    │
│  │ • Background context                                 │    │
│  │                                                      │    │
│  │ Visual: AI IMAGE GENERATION (Whisk)                  │    │
│  │         ↓                                           │    │
│  │    Context-aware paintings/digital art             │    │
│  └─────────────────────────────────────────────────────┘    │
│                                                              │
└─────────────────────────────────────────────────────────────┘
```

---

## Part 1: Text Overlay System

### What Gets an Overlay?

LLM automatically detects these segment types:

| Type | Examples | Text Content |
|------|----------|--------------|
| **Quran Verse** | "As stated in the Quran..." | Arabic + translation + reference |
| **Hadith** | "The Prophet (peace be upon him) said..." | Arabic (if available) + translation + source |
| **Rhetorical Question** | "Have you considered..." | The question text |
| **Key Phrase** | "Tawheed is the foundation..." | The emphasized statement |

### Timing Model

```
Segment Timeline (5 second example):

0.0s ──────────────────────────────────────────────► 5.0s
│                                                        │
│  [B-Roll visible from start]                           │
│  ┌───────────────────────────────────────────────┐   │
│  │   Video: Desert landscape with Ken Burns      │   │
│  └───────────────────────────────────────────────┘   │
│                                                        │
│  [Text typing starts at 0.5s]                          │
│       ┌─┐ ┌──┐ ┌───┐ ┌────┐ ┌─────┐ ┌──────┐       │
│       │I│  │n │  │the │  │name │  │of   │       │
│       └─┘ └──┘ └───┘ └────┘ └─────┘ └──────┘       │
│         0.5s  0.7s   1.0s    1.5s     2.0s           │
│                                                        │
│  [Full text displayed for remainder]                  │
│              ┌────────────────────────────┐           │
│              │ "In the name of Allah"     │           │
│              └────────────────────────────┘           │
│                   2.0s ────────────────► 5.0s         │
```

**Configurable Parameters:**
- `StartDelayMs`: When typing begins (default: 500ms)
- `TypingSpeedMs`: Per-character speed (default: 50ms)
- `FullTextHoldTime`: How long complete text shows (until segment end)

### Animation Styles

| Style | Description | Use Case |
|-------|-------------|----------|
| **Typewriter** | Character-by-character | Quran verses, formal hadith |
| **WordByWord** | Word-by-word fade | Questions, faster pacing |
| **FadeIn** | Simple fade | Subtle emphasis |

### Style Presets

| Preset | Font | Color | Position | Use For |
|--------|------|-------|----------|---------|
| **Quran** | Amiri | Gold (#FFD700) | Center | Quranic verses |
| **Hadith** | Times New Roman | Wheat (#F5DEB3) | Center | Hadith narrations |
| **Question** | Arial Bold | White | Top Center | Rhetorical questions |
| **Key Phrase** | Arial | Purple | Center/Emphasis | Important declarations |

---

## Part 2: Content-Aware Composition (Image Quality)

### Problem with Current System

Current `GetDynamicComposition()` in `IntelligenceService.cs` (line 1686-1698) just cycles through a fixed sequence:

```csharp
private ImageComposition GetDynamicComposition(int index)
{
    var sequence = new[]
    {
        ImageComposition.CinematicWide,
        ImageComposition.CloseUp,
        ImageComposition.LowAngle,
        ImageComposition.WideShot,
        ImageComposition.BirdsEye,
        ImageComposition.CloseUp
    };
    return sequence[index % sequence.Length];
}
```

**Issues:**
- Ignores content type
- May give bird's eye view to intimate cave scene
- No anti-repetition logic
- Doesn't consider mood or narrative beat

### Solution: Content-Aware Composition

**Scene Type Detection:**

| Scene Type | Best Angle | Example Content |
|------------|------------|-----------------|
| Intimate/personal | CloseUp | Cave interior, holding scroll |
| Landscape/establishing | WideShot, CinematicWide | Desert expanse, city skyline |
| Powerful/imposing figure | LowAngle | Caliph on throne, mountain |
| Overview/expository | BirdsEye | Map region, city layout |
| Action/movement | LowAngle, CinematicWide | Battle, journey |

**Anti-Repetition System:**

```csharp
// Track last N compositions
private List<ImageComposition> _recentCompositions = new();

private ImageComposition GetContentAwareComposition(
    string scriptText,
    GlobalScriptContext context,
    int segmentIndex)
{
    // 1. Detect scene type from content
    var sceneType = DetectSceneType(scriptText, context, segmentIndex);

    // 2. Get base composition for scene type
    var baseComposition = GetCompositionForScene(sceneType);

    // 3. Check for repetition - force variety if needed
    if (_recentCompositions.Take(2).Contains(baseComposition))
    {
        baseComposition = GetAlternativeComposition(baseComposition, sceneType);
    }

    // 4. Update tracking
    _recentCompositions.Insert(0, baseComposition);
    if (_recentCompositions.Count > 5) _recentCompositions.RemoveAt(5);

    return baseComposition;
}
```

**Focal Distance Cycling:**

```
Pattern (reset per mood beat):
Wide (landscape) → Medium (architecture) → Close (object) → Atmospheric (sky)
```

---

## Part 3: Mood-Based Lighting Enhancement

### Current System

Lighting enum exists but `Auto` mode relies on inconsistent LLM detection. Mood beats from `GlobalScriptContext` have lighting suggestions but aren't enforced.

### Solution: Multi-Layer Lighting

**Priority Hierarchy:**

```
1. User Override (in PromptConfig)
2. Mood Beat Suggestion (from GlobalScriptContext)
3. Era Default
4. Auto-Detect (LLM)
```

**Mood-Triggered Lighting:**

| Mood Beat | Suggested Lighting | Duration |
|-----------|-------------------|----------|
| Mysterious/contemplative | SoftAmbient or EtherealGlow | Segments 0-25 |
| Tense/apocalyptic | MoodyDark or DramaticHighContrast | Segments 25-50 |

**Era-Based Defaults:**

| Era | Default Lighting |
|-----|------------------|
| Ancient/Prophetic | SoftAmbient, GoldenHour (warm, reverent) |
| Apocalyptic | MoodyDark, DramaticHighContrast (intense) |
| Modern | Flat, Cinematic (clean, documentary) |
| Abstract | EtherealGlow (mystical) |

**Consistency Rule:**
- Don't randomize within a mood beat
- Use consistent lighting throughout the beat
- Only shift at mood beat boundaries

---

## Part 4: Integration with Existing Code

### No Breaking Changes

This design **extends** the existing system:

```csharp
// EXISTING: Keep using BrollMediaType enum
public enum BrollMediaType
{
    BrollVideo,       // Stock video
    ImageGeneration   // AI image
}

// NEW: Add text overlay detection
public class BrollPromptItem
{
    // All existing fields remain...

    // NEW: Text overlay support
    public TextOverlay? TextOverlay { get; set; }

    // Helper
    public bool HasTextOverlay => TextOverlay != null;
}
```

### Classification Logic

```
EXISTING (unchanged):
├── MediaType = BrollVideo       → Search Pexels/Pixabay
└── MediaType = ImageGeneration  → Generate with Whisk

NEW LAYER (added):
├── TextOverlay != null          → Force MediaType = BrollVideo
└── TextOverlay == null          → MediaType = ImageGeneration

Result:
- Quran/hadith/questions → Auto-get B-roll backgrounds
- Regular narration → Auto-get AI images
- User can still manually override if needed
```

---

## Data Models

### TextOverlay Model

```csharp
public class TextOverlay
{
    public TextOverlayType Type { get; set; }
    public string Text { get; set; }
    public string? ArabicText { get; set; }
    public string? Reference { get; set; }
    public int StartDelayMs { get; set; } = 500;
    public int TypingSpeedMs { get; set; } = 50;
    public TypingAnimationStyle AnimationStyle { get; set; }
    public TextStyle Style { get; set; }
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
```

---

## User Experience Flow

1. **Import SRT**
2. **LLM Classifies:**
   - Detects text overlays (Quran, hadith, questions, key phrases)
   - Sets `TextOverlay` property
   - Auto-assigns `MediaType` (BrollVideo for overlays, ImageGeneration for narration)
3. **UI Shows:**
   - Overlay segments marked with 📝 + 🎬 badge
   - Narration segments marked with 🎨 AI Image badge
4. **User Can:**
   - Edit generated overlays
   - Add/remove overlays
   - Adjust timing and styling
   - Select B-roll for overlay segments
   - Generate AI images for narration segments
5. **Compose:**
   - Overlay segments get text typing over B-roll
   - Narration segments get Ken Burns images

---

## Benefits

| Aspect | Improvement |
|--------|-------------|
| **Performance** | 70-80% fewer B-roll segments to generate/search |
| **Visual Hierarchy** | Clear distinction between emphasis and supporting |
| **Engagement** | Typographic animation creates rhythm |
| **Maintenance** | Integrates cleanly, no breaking changes |
| **Flexibility** | User retains full override control |

---

## Open Questions

1. **Arabic Font** - Need to add Arabic font file to wwwroot for rendering
2. **Typewriter Implementation** - FFmpeg textfile approach vs. complex filter chain
3. **Preview** - Should we add live preview of text overlays in the editor?
4. **Batch Operations** - Apply style preset to all overlays at once?

---

## References

- Existing: `Models/BrollPromptItem.cs` - Segment model
- Existing: `Models/ImagePromptConfig.cs` - Composition/Lighting enums
- Existing: `Services/IntelligenceService.cs` - LLM classification
- Existing: `Services/ShortVideoComposer.cs` - Video rendering
- Existing: `Components/Views/ScriptGenerator/BrollPromptsView.razor` - Main UI
