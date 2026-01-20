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

### 5. UI Enhancement (`Home.razor`)

Added duration match score indicator below each video preview:
- Shows video duration vs sentence duration
- Color-coded match percentage (green 80%+, yellow 50-79%, red <50%)

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
