# B-Roll Downloader Accuracy Improvements Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Perfect the RAW B-roll downloader by improving sentence-to-video matching accuracy, enhancing Halal filter, and implementing smart duration matching algorithm.

**Architecture:** Enhance existing services (HalalVideoFilter, CompositeAssetBroker, PipelineOrchestrator) without reimplementation. Downloads are direct from Pexels/Pixabay CDN - no server-side download changes needed.

**Tech Stack:** C# / .NET, Blazor Server, Pexels API, Pixabay API

---

## Task 1: Add DurationMatchScore to VideoAsset Model

**Files:**
- Modify: `Models/VideoAsset.cs`

**Step 1: Write the failing test**

Create `Tests/Models/VideoAssetTests.cs`:

```csharp
using BunbunBroll.Models;
using Xunit;

namespace BunbunBroll.Tests.Models;

public class VideoAssetTests
{
    [Fact]
    public void DurationMatchScore_ReturnsZero_WhenNoTargetDuration()
    {
        var asset = new VideoAsset
        {
            DurationSeconds = 10,
            DownloadUrl = "https://example.com/video.mp4"
        };

        var score = asset.CalculateDurationMatchScore(targetDurationSeconds: null);

        Assert.Equal(0, score);
    }

    [Fact]
    public void DurationMatchScore_PenalizesShortVideos_Heavily()
    {
        var asset = new VideoAsset
        {
            DurationSeconds = 5,
            DownloadUrl = "https://example.com/video.mp4"
        };

        // Sentence is 10 seconds, video is only 5 seconds
        var score = asset.CalculateDurationMatchScore(targetDurationSeconds: 10);

        // Should be heavily penalized (below 50)
        Assert.True(score < 50);
    }

    [Fact]
    public void DurationMatchScore_PrefersSlightlyLongerVideos()
    {
        var asset = new VideoAsset
        {
            DurationSeconds = 12,
            DownloadUrl = "https://example.com/video.mp4"
        };

        // Sentence is 10 seconds, video is 12 seconds (acceptable)
        var score = asset.CalculateDurationMatchScore(targetDurationSeconds: 10);

        // Should have good score (above 80)
        Assert.True(score > 80);
    }

    [Fact]
    public void DurationMatchScore_PenalizesVeryLongVideos()
    {
        var asset = new VideoAsset
        {
            DurationSeconds = 30,
            DownloadUrl = "https://example.com/video.mp4"
        };

        // Sentence is 10 seconds, video is 30 seconds (too long)
        var score = asset.CalculateDurationMatchScore(targetDurationSeconds: 10);

        // Should be penalized (below 70)
        Assert.True(score < 70);
    }

    [Fact]
    public void DurationMatchScore_PerfectMatch_Returns100()
    {
        var asset = new VideoAsset
        {
            DurationSeconds = 10,
            DownloadUrl = "https://example.com/video.mp4"
        };

        var score = asset.CalculateDurationMatchScore(targetDurationSeconds: 10);

        Assert.Equal(100, score);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test Tests/Models/VideoAssetTests.cs -v n`
Expected: FAIL with "CalculateDurationMatchScore does not exist"

**Step 3: Write minimal implementation**

Modify `Models/VideoAsset.cs` - add method:

```csharp
/// <summary>
/// Calculate how well this video's duration matches the target sentence duration.
/// Score 0-100 where 100 is perfect match.
/// Heavily penalizes videos shorter than target (can't cover full sentence).
/// Moderately penalizes videos 2x+ longer than target (boring).
/// </summary>
public int CalculateDurationMatchScore(int? targetDurationSeconds)
{
    if (!targetDurationSeconds.HasValue || targetDurationSeconds.Value <= 0)
        return 0;

    var target = targetDurationSeconds.Value;
    var actual = DurationSeconds;

    // Perfect match
    if (actual == target)
        return 100;

    // Heavily penalize videos shorter than target (can't cover sentence)
    if (actual < target)
    {
        var deficit = target - actual;
        // Lose 20 points per second missing
        return Math.Max(0, 100 - (deficit * 20));
    }

    // Video is longer than target - calculate based on how much longer
    var excess = actual - target;

    // Up to 3 seconds excess is fine (90-99 score)
    if (excess <= 3)
        return 100 - (excess * 3);

    // Up to 10 seconds excess is acceptable (70-89 score)
    if (excess <= 10)
        return 90 - ((excess - 3) * 3);

    // More than 10 seconds excess - penalize more heavily
    // More than 2x target duration = very poor match
    if (actual > target * 2)
        return Math.Max(0, 50 - ((actual - target * 2) / 2));

    // 10 seconds to 2x target = 50-70 score
    return Math.Max(50, 70 - ((excess - 10) * 2));
}
```

**Step 4: Run test to verify it passes**

Run: `dotnet test Tests/Models/VideoAssetTests.cs -v n`
Expected: PASS

**Step 5: Commit**

```bash
git add Models/VideoAsset.cs Tests/Models/VideoAssetTests.cs
git commit -m "feat: add duration match score calculation to VideoAsset"
```

---

## Task 2: Enhance HalalVideoFilter with Expanded Keywords

**Files:**
- Modify: `Services/HalalVideoFilter.cs`
- Test: `Tests/Services/HalalVideoFilterTests.cs`

**Step 1: Write the failing test**

Create `Tests/Services/HalalVideoFilterTests.cs`:

```csharp
using BunbunBroll.Services;
using Xunit;

namespace BunbunBroll.Tests.Services;

public class HalalVideoFilterTests
{
    private readonly HalalVideoFilter _filter;

    public HalalVideoFilterTests()
    {
        // Note: You'll need to create a test logger or use NullLogger
        _filter = new HalalVideoFilter(null!);
        _filter.IsEnabled = true;
    }

    [Fact]
    public void FilterKeywords_BlocksRevealingClothing()
    {
        var keywords = new List<string> { "woman in dress", "lady fashion", "beach party" };
        var filtered = _filter.FilterKeywords(keywords);

        Assert.DoesNotContain("beach party", filtered);
    }

    [Fact]
    public void FilterKeywords_ReplacesWomanWithPersonSilhouette()
    {
        var keywords = new List<string> { "woman walking alone" };
        var filtered = _filter.FilterKeywords(keywords);

        Assert.Contains("person silhouette", filtered);
        Assert.DoesNotContain("woman", string.Join(" ", filtered));
    }

    [Fact]
    public void FilterKeywords_AddsCinematicFallbacks_WhenTooManyFiltered()
    {
        var keywords = new List<string> { "bikini", "nightclub", "drinking" };
        var filtered = _filter.FilterKeywords(keywords);

        // All blocked, should add cinematic fallbacks
        Assert.True(filtered.Count >= 3);
        Assert.True(filtered.Any(k => k.Contains("skyline") || k.Contains("landscape") || k.Contains("timelapse")));
    }

    [Fact]
    public void FilterKeywords_HandlesIndonesianWords()
    {
        var keywords = new List<string> { "wanita", "perempuan" };
        var filtered = _filter.FilterKeywords(keywords);

        // Should filter or replace Indonesian terms for woman
        Assert.True(filtered.Count >= 3);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test Tests/Services/HalalVideoFilterTests.cs -v n`
Expected: FAIL - some tests may pass (existing code), some fail (new requirements)

**Step 3: Enhance HalalVideoFilter implementation**

Modify `Services/HalalVideoFilter.cs`:

Update `BlockedKeywords` HashSet:

```csharp
// Keywords to completely BLOCK
private static readonly HashSet<string> BlockedKeywords = new(StringComparer.OrdinalIgnoreCase)
{
    // Beach/swimwear (existing + expanded)
    "bikini", "swimsuit", "swimwear", "beach party", "pool party",
    "swimming pool", "bathing suit", "beach body", "sunbathing",
    "beach bikini", "pool bikini", "summer beach",

    // Nightlife/party (existing + expanded)
    "nightclub", "club party", "bar party", "disco", "rave",
    "drinking party", "alcohol", "beer", "wine", "cocktail",
    "pub", "bartender", "nightclub dancing", "party club",

    // Revealing/sensual (existing + expanded)
    "sexy", "sensual", "seductive", "revealing", "lingerie",
    "underwear", "bra", "cleavage", "low cut", "mini skirt",
    "short dress", "tight dress", "bodycon", "crop top",
    "tank top", "sleeveless", "strapless", "backless",
    "shorts", "hot pants", "midriff", "see through",

    // Dance with revealing content (existing)
    "pole dance", "strip", "twerk", "belly dance", "latin dance",

    // Romance/intimate (existing + expanded)
    "kissing", "romantic kiss", "couple bed", "intimate",
    "love scene", "passion", "making out", "embrace romantic",
    "honeymoon", "bedroom couple",

    // Avoid non-modest female depictions (existing + expanded)
    "model female", "fashion model", "beauty model",
    "woman hair flowing", "woman hair wind", "brunette", "blonde woman",
    "redhead woman", "long hair woman", "curly hair woman",
    "makeup tutorial", "beauty salon", "spa treatment",

    // NEW: Music/concert (often revealing)
    "concert crowd", "music festival", "rave festival"
};
```

Add Indonesian translation map (add near top of class):

```csharp
// Indonesian to English translations for filtering
private static readonly Dictionary<string, string> IndonesianTranslations = new(StringComparer.OrdinalIgnoreCase)
{
    ["wanita"] = "woman",
    ["perempuan"] = "woman",
    ["cewek"] = "girl",
    ["gadis"] = "girl",
    ["ibu"] = "mother",
    ["bunda"] = "mother"
};
```

Update `FilterKeywords` method to handle Indonesian:

```csharp
public List<string> FilterKeywords(List<string> keywords)
{
    if (!IsEnabled)
        return keywords;

    var filtered = new List<string>();

    foreach (var keyword in keywords)
    {
        // Translate Indonesian first
        var translatedKeyword = TranslateIndonesian(keyword);
        var lowerKeyword = translatedKeyword.ToLowerInvariant();

        // Check if keyword contains any blocked words
        var isBlocked = BlockedKeywords.Any(blocked =>
            lowerKeyword.Contains(blocked) || blocked.Contains(lowerKeyword));

        if (isBlocked)
        {
            _logger.LogDebug("Halal filter: Blocked '{Keyword}'", keyword);
            continue;
        }

        // Check if keyword needs replacement
        var replaced = TryReplaceFemaleKeyword(translatedKeyword);
        if (replaced != translatedKeyword)
        {
            _logger.LogDebug("Halal filter: Replaced '{Original}' -> '{Replaced}'", keyword, replaced);
            filtered.Add(replaced);
        }
        else
        {
            filtered.Add(keyword);
        }
    }

    // If too many keywords were filtered, add CINEMATIC fallbacks
    if (filtered.Count < 3)
    {
        _logger.LogDebug("Halal filter: Adding cinematic fallback keywords");
        var cinematicToAdd = CinematicFallbacks
            .OrderBy(_ => Random.Shared.Next())
            .Take(4 - filtered.Count);
        filtered.AddRange(cinematicToAdd);
    }

    _logger.LogInformation("Halal filter: {Original} keywords -> {Filtered} filtered",
        keywords.Count, filtered.Count);

    return filtered.Distinct().ToList();
}

private static string TranslateIndonesian(string keyword)
{
    var lower = keyword.ToLowerInvariant();
    foreach (var (indo, english) in IndonesianTranslations)
    {
        if (lower.Contains(indo))
        {
            return keyword.Replace(indo, english, StringComparison.OrdinalIgnoreCase);
        }
    }
    return keyword;
}
```

Add `CinematicFallbacks` property:

```csharp
// Cinematic fallback keywords for when everything is filtered
private static readonly string[] CinematicFallbacks = new[]
{
    "city skyline night",
    "nature landscape cinematic",
    "clouds timelapse",
    "ocean waves sunset",
    "forest morning light",
    "mountain sunrise",
    "abstract light bokeh",
    "rain window mood",
    "aerial view city",
    "stars night sky"
};
```

**Step 4: Run test to verify it passes**

Run: `dotnet test Tests/Services/HalalVideoFilterTests.cs -v n`
Expected: PASS

**Step 5: Commit**

```bash
git add Services/HalalVideoFilter.cs Tests/Services/HalalVideoFilterTests.cs
git commit -m "feat: enhance HalalVideoFilter with expanded keywords and cinematic fallbacks"
```

---

## Task 3: Improve Duration Scoring in PipelineOrchestrator

**Files:**
- Modify: `Services/PipelineOrchestrator.cs`
- Test: `Tests/Services/PipelineOrchestratorTests.cs`

**Step 1: Write the failing test**

Create `Tests/Services/PipelineOrchestratorTests.cs`:

```csharp
using BunbunBroll.Models;
using Xunit;

namespace BunbunBroll.Tests.Services;

public class DurationSelectionTests
{
    [Fact]
    public void SelectBestVideoByDuration_PrefersExactMatch()
    {
        var sentence = new ScriptSentence
        {
            Id = 1,
            Text = "This is a test sentence with ten words.", // ~4 seconds
            EstimatedDurationSeconds = 8
        };

        var videos = new List<VideoAsset>
        {
            new() { Id = "1", DurationSeconds = 8, DownloadUrl = "https://ex1.com" },
            new() { Id = "2", DurationSeconds = 5, DownloadUrl = "https://ex2.com" }, // Too short
            new() { Id = "3", DurationSeconds = 15, DownloadUrl = "https://ex3.com" } // Too long
        };

        var best = videos
            .OrderBy(v => v.CalculateDurationMatchScore(sentence.EstimatedDurationSeconds))
            .Last(); // Highest score wins

        Assert.Equal("1", best.Id); // 8 seconds = perfect match
    }

    [Fact]
    public void SelectBestVideoByDuration_PrefersSlightlyLonger_OverShorter()
    {
        var targetDuration = 10;

        var videos = new List<VideoAsset>
        {
            new() { Id = "short", DurationSeconds = 8, DownloadUrl = "https://ex1.com" }, // 2s short
            new() { Id = "long", DurationSeconds = 12, DownloadUrl = "https://ex2.com" } // 2s long
        };

        var shortScore = videos[0].CalculateDurationMatchScore(targetDuration);
        var longScore = videos[1].CalculateDurationMatchScore(targetDuration);

        // Slightly longer should score better than shorter
        Assert.True(longScore > shortScore);
    }

    [Fact]
    public void SelectBestVideoByDuration_HeavilyPenalizes_TooShort()
    {
        var targetDuration = 15;

        var video = new VideoAsset
        {
            Id = "1",
            DurationSeconds = 5, // Much too short
            DownloadUrl = "https://ex1.com"
        };

        var score = video.CalculateDurationMatchScore(targetDuration);

        // Should be heavily penalized (below 50)
        Assert.True(score < 50);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test Tests/Services/PipelineOrchestratorTests.cs -v n`
Expected: PASS (tests VideoAsset directly, but we need to update PipelineOrchestrator)

**Step 3: Update PipelineOrchestrator to use DurationMatchScore**

Modify `Services/PipelineOrchestrator.cs` - find the video selection code around line 264-267:

OLD CODE:
```csharp
sentence.SearchResults = assets;
sentence.SelectedVideo = assets
    .OrderBy(a => Math.Abs(a.DurationSeconds - targetDuration))
    .First();
```

NEW CODE:
```csharp
sentence.SearchResults = assets;

// Select best video using DurationMatchScore
// Prefer videos that cover the sentence, not just closest duration
sentence.SelectedVideo = assets
    .OrderByDescending(a => a.CalculateDurationMatchScore(targetDuration))
    .First();

var selectedScore = sentence.SelectedVideo.CalculateDurationMatchScore(targetDuration);
_logger.LogDebug("Selected video with duration score: {Score}/100 (video: {VideoDuration}s, target: {TargetDuration}s)",
    selectedScore, sentence.SelectedVideo.DurationSeconds, targetDuration);
```

Also update line 373-375 (similar pattern):

OLD CODE:
```csharp
// Auto-select best match (user can change)
sentence.SelectedVideo = assets
    .OrderBy(a => Math.Abs(a.DurationSeconds - targetDuration))
    .First();
```

NEW CODE:
```csharp
// Auto-select best match using DurationMatchScore
sentence.SelectedVideo = assets
    .OrderByDescending(a => a.CalculateDurationMatchScore(targetDuration))
    .First();
```

Also update line 427-430 (ResearchSentenceAsync):

OLD CODE:
```csharp
sentence.SearchResults = assets;
sentence.SelectedVideo = assets
    .OrderBy(a => Math.Abs(a.DurationSeconds - targetDuration))
    .First();
```

NEW CODE:
```csharp
sentence.SearchResults = assets;
sentence.SelectedVideo = assets
    .OrderByDescending(a => a.CalculateDurationMatchScore(targetDuration))
    .First();
```

**Step 4: Run test to verify it passes**

Run: `dotnet test Tests/Services/PipelineOrchestratorTests.cs -v n`
Expected: PASS

**Step 5: Commit**

```bash
git add Services/PipelineOrchestrator.cs Tests/Services/PipelineOrchestratorTests.cs
git commit -m "feat: use DurationMatchScore for video selection in PipelineOrchestrator"
```

---

## Task 4: Adaptive Duration Range in CompositeAssetBroker

**Files:**
- Modify: `Services/CompositeAssetBroker.cs`
- Test: `Tests/Services/CompositeAssetBrokerTests.cs`

**Step 1: Write the failing test**

Create `Tests/Services/CompositeAssetBrokerTests.cs`:

```csharp
using BunbunBroll.Models;
using BunbunBroll.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BunbunBroll.Tests.Services;

public class AdaptiveDurationTests
{
    [Fact]
    public void CalculateAdaptiveDurationRange_ShortSentence_TightRange()
    {
        // 3 second sentence - tight range
        var (min, max) = CompositeAssetBroker.CalculateAdaptiveDurationRange(3);

        // Should be very tight for short sentences
        Assert.Equal(3, min);
        Assert.True(max <= 8); // Max 5 seconds excess
    }

    [Fact]
    public void CalculateAdaptiveDurationRange_LongSentence_WiderRange()
    {
        // 30 second sentence - wider range
        var (min, max) = CompositeAssetBroker.CalculateAdaptiveDurationRange(30);

        // Should allow more flexibility for long sentences
        Assert.True(min >= 25); // Allow 5 seconds short
        Assert.True(max >= 40); // Allow 10+ seconds long
    }

    [Fact]
    public void CalculateAdaptiveDurationRange_NeverBelowMinimum()
    {
        var (min, max) = CompositeAssetBroker.CalculateAdaptiveDurationRange(2);

        // Minimum 3 seconds regardless
        Assert.Equal(3, min);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test Tests/Services/CompositeAssetBrokerTests.cs -v n`
Expected: FAIL with "CalculateAdaptiveDurationRange does not exist"

**Step 3: Add adaptive duration calculation to CompositeAssetBroker**

Modify `Services/CompositeAssetBroker.cs` - add static method:

```csharp
/// <summary>
/// Calculate adaptive min/max duration range based on target sentence length.
/// Short sentences get tight range (more precise matching).
/// Long sentences get wider range (more flexibility).
/// </summary>
public static (int minDuration, int maxDuration) CalculateAdaptiveDurationRange(int targetDuration)
{
    // Minimum 3 seconds always
    if (targetDuration < 3)
        return (3, 10);

    // Short sentences (3-10 seconds): Tight range
    if (targetDuration <= 10)
    {
        // Allow up to 5 seconds excess, but minimum target duration
        return (targetDuration, targetDuration + 5);
    }

    // Medium sentences (11-20 seconds): Moderate range
    if (targetDuration <= 20)
    {
        // Allow 3 seconds short, up to 10 seconds long
        return (Math.Max(3, targetDuration - 3), targetDuration + 10);
    }

    // Long sentences (21+ seconds): Wide range
    // Allow 5 seconds short, up to 15 seconds long
    return (targetDuration - 5, targetDuration + 15);
}
```

Now update `SearchVideoForSentenceAsync` method to use adaptive range:

Find line 223-234 (duration calculation in SearchVideoForSentenceAsync):

OLD CODE:
```csharp
var targetDuration = (int)Math.Ceiling(sentence.EstimatedDurationSeconds);
List<VideoAsset> assets;

// Use tier-based search if broker supports KeywordSet
if (_assetBroker is IAssetBrokerV2 brokerV2 && sentence.KeywordSet.TotalCount > 0)
{
    assets = await brokerV2.SearchVideosAsync(
        sentence.KeywordSet,
        maxResults: 6,
        minDuration: Math.Max(3, targetDuration - 5),
        maxDuration: targetDuration + 15,
        cancellationToken: cancellationToken);
}
```

NEW CODE:
```csharp
var targetDuration = (int)Math.Ceiling(sentence.EstimatedDurationSeconds);
var (minDuration, maxDuration) = CalculateAdaptiveDurationRange(targetDuration);

List<VideoAsset> assets;

// Use tier-based search if broker supports KeywordSet
if (_assetBroker is IAssetBrokerV2 brokerV2 && sentence.KeywordSet.TotalCount > 0)
{
    _logger.LogDebug("Adaptive duration range: {Min}-{Max}s (target: {Target}s)",
        minDuration, maxDuration, targetDuration);

    assets = await brokerV2.SearchVideosAsync(
        sentence.KeywordSet,
        maxResults: 6,
        minDuration: minDuration,
        maxDuration: maxDuration,
        cancellationToken: cancellationToken);
}
```

Also update the fallback search below it (around line 247-254):

OLD CODE:
```csharp
if (assets.Count == 0)
{
    // Fallback: broader search without duration filter
    assets = await _assetBroker.SearchVideosAsync(
        sentence.Keywords,
        maxResults: 6,
        cancellationToken: cancellationToken);
}
```

NEW CODE:
```csharp
if (assets.Count == 0)
{
    _logger.LogDebug("No results with adaptive range, trying wider fallback");

    // Fallback: Use very wide range
    assets = await _assetBroker.SearchVideosAsync(
        sentence.Keywords,
        maxResults: 6,
        minDuration: Math.Max(3, targetDuration - 10),
        maxDuration: targetDuration + 30,
        cancellationToken: cancellationToken);
}
```

Apply the same changes to `SearchSentencePreviewAsync` method (around line 329-350).

**Step 4: Run test to verify it passes**

Run: `dotnet test Tests/Services/CompositeAssetBrokerTests.cs -v n`
Expected: PASS

**Step 5: Commit**

```bash
git add Services/CompositeAssetBroker.cs Tests/Services/CompositeAssetBrokerTests.cs
git commit -m "feat: add adaptive duration range calculation to CompositeAssetBroker"
```

---

## Task 5: Add Duration Coverage Display to UI

**Files:**
- Modify: `Components/Pages/Home.razor`

**Step 1: Update UI to show duration match score**

Find the video display section in `Home.razor` (around line 493-512) and add duration coverage indicator:

Add this after the video element:

```html
<div class="aspect-video relative bg-black">
    <video src="@sentence.SelectedVideo.DownloadUrl" controls muted loop class="absolute inset-0 w-full h-full object-cover"></video>
</div>

<!-- NEW: Duration Match Indicator -->
<div class="flex items-center justify-between px-2 py-1 bg-muted/80 text-[10px] border-t">
    <div class="flex items-center gap-2">
        <span class="text-muted-foreground">@sentence.SelectedVideo.DurationSecondss video</span>
        <span class="text-muted-foreground">vs</span>
        <span class="text-muted-foreground">@sentence.EstimatedDurationSeconds:F1s sentence</span>
    </div>
    @{
        var matchScore = sentence.SelectedVideo.CalculateDurationMatchScore((int)Math.Ceiling(sentence.EstimatedDurationSeconds));
        var scoreColor = matchScore >= 80 ? "text-green-600" : matchScore >= 50 ? "text-yellow-600" : "text-red-600";
    }
    <span class="@scoreColor font-medium">@matchScore% match</span>
</div>
```

**Step 2: Test the UI**

Run: `dotnet run`
Navigate to home page, create a project, and verify:
- Duration match percentage appears below each video
- Color coding: Green (80%+), Yellow (50-79%), Red (<50%)

**Step 3: Commit**

```bash
git add Components/Pages/Home.razor
git commit -m "feat: add duration match score indicator to video preview"
```

---

## Task 6: Integration Testing

**Files:**
- Test: `Tests/Integration/EndToEndTests.cs`

**Step 1: Write integration test**

Create `Tests/Integration/EndToEndTests.cs`:

```csharp
using BunbunBroll.Services;
using Xunit;

namespace BunbunBroll.Tests.Integration;

public class HalalFilterIntegrationTests
{
    [Fact]
    public void FullWorkflow_HalalFilterWithDurationMatching_WorksEndToEnd()
    {
        // Simulate full workflow with Halal filter enabled
        var filter = new HalalVideoFilter(null!);
        filter.IsEnabled = true;

        var keywords = new List<string> { "woman walking alone", "beach party" };
        var filtered = filter.FilterKeywords(keywords);

        // Should block beach party
        Assert.DoesNotContain("beach party", filtered);

        // Should replace woman with silhouette
        Assert.Contains("silhouette", string.Join(" ", filtered));

        // Should have cinematic fallbacks
        Assert.True(filtered.Count >= 3);
    }

    [Fact]
    public void DurationScoring_RealWorldScenarios()
    {
        var shortVideo = new VideoAsset { DurationSeconds = 5, DownloadUrl = "x" };
        var perfectVideo = new VideoAsset { DurationSeconds = 10, DownloadUrl = "x" };
        var longVideo = new VideoAsset { DurationSeconds = 20, DownloadUrl = "x" };

        var target = 10;

        var shortScore = shortVideo.CalculateDurationMatchScore(target);
        var perfectScore = perfectVideo.CalculateDurationMatchScore(target);
        var longScore = longVideo.CalculateDurationMatchScore(target);

        // Perfect should win
        Assert.Equal(100, perfectScore);

        // Short should be heavily penalized
        Assert.True(shortScore < 50);

        // Long should be moderately penalized
        Assert.True(longScore > shortScore); // Better than too short
        Assert.True(longScore < perfectScore); // But not as good as perfect
    }
}
```

**Step 2: Run integration tests**

Run: `dotnet test Tests/Integration/EndToEndTests.cs -v n`
Expected: PASS

**Step 3: Commit**

```bash
git add Tests/Integration/EndToEndTests.cs
git commit -m "test: add integration tests for Halal filter and duration matching"
```

---

## Task 7: Documentation

**Files:**
- Create: `docs/broll-downloader-improvements.md`

**Step 1: Write documentation**

Create `docs/broll-downloader-improvements.md`:

```markdown
# B-Roll Downloader Accuracy Improvements

## Overview

This document describes improvements to the B-roll downloader feature focused on search accuracy, Halal filtering, and duration matching.

## Changes Made

### 1. Duration Match Scoring (`VideoAsset.cs`)

Added `CalculateDurationMatchScore()` method that scores how well a video's duration matches a sentence's length.

**Scoring Logic:**
- Perfect match = 100 points
- Up to 3 seconds longer = 97-99 points
- 3-10 seconds longer = 70-89 points
- More than 10 seconds longer = 50-70 points
- Shorter than target = Heavy penalty (-20 points per second)

### 2. Enhanced Halal Filter (`HalalVideoFilter.cs`)

**New Blocked Keywords:**
- Additional revealing clothing terms
- Music festival/concert scenes
- More dance styles

**Indonesian Translation Support:**
- wanita → woman
- perempuan → woman
- cewek → girl

**Cinematic Fallbacks:**
When keywords are too heavily filtered, automatically adds:
- city skyline night
- nature landscape cinematic
- clouds timelapse
- ocean waves sunset
- etc.

### 3. Adaptive Duration Ranges (`CompositeAssetBroker.cs`)

Short sentences (3-10s): Tight range (+5s max)
Medium sentences (11-20s): Moderate range (-3s to +10s)
Long sentences (21s+): Wide range (-5s to +15s)

### 4. Pipeline Orchestrator Updates

Changed video selection from:
```csharp
.OrderBy(a => Math.Abs(a.DurationSeconds - targetDuration))
```

To:
```csharp
.OrderByDescending(a => a.CalculateDurationMatchScore(targetDuration))
```

This prefers videos that fully cover the sentence rather than just being "closest" in duration.

## Usage

No API changes required. The improvements are automatic:

1. Halal filter automatically translates Indonesian terms
2. Duration matching is smarter about preferring slightly-longer videos
3. Cinematic fallbacks kick in when too many keywords are blocked
4. UI shows match percentage for each video

## Testing

Run tests with:
```bash
dotnet test
```

Specific test suites:
```bash
dotnet test Tests/Models/VideoAssetTests.cs
dotnet test Tests/Services/HalalVideoFilterTests.cs
dotnet test Tests/Services/CompositeAssetBrokerTests.cs
dotnet test Tests/Integration/EndToEndTests.cs
```
```

**Step 2: Commit documentation**

```bash
git add docs/broll-downloader-improvements.md
git commit -m "docs: add B-roll downloader improvements documentation"
```

---

## Summary

This implementation plan perfects the RAW B-roll downloader by:

1. **Duration Match Scoring** - Smart scoring that prefers videos covering full sentence
2. **Enhanced Halal Filter** - More blocked keywords, Indonesian translation, cinematic fallbacks
3. **Adaptive Duration Ranges** - Tighter ranges for short sentences, wider for long ones
4. **Improved Video Selection** - Uses score-based selection instead of simple diff
5. **UI Enhancement** - Shows match percentage to help users understand quality

All changes enhance existing services without reimplementation, maintaining backward compatibility.
