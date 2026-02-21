# SRT Expand + VO Slice as Source of Truth Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a dedicated "Expand & Slice VO" step (Stepper 2) that transforms CapCut SRT into expanded segments, then slices the existing VO file to match each segment timing, creating validated audio-subtitle pairs as the single source of truth for B-roll timing.

**Architecture:** Current workflow has B-roll timing derived from estimates. This plan introduces Stepper 2: (1) Expand CapCut SRT into sentence-level chunks with calculated pauses, (2) Slice existing VO.mp3 using FFmpeg to match each expanded entry's timing, (3) Validate sliced audio segments against SRT timing. The result is validated VO segments + expanded SRT that become the authoritative timing source.

**Tech Stack:** C# / Blazor, FFmpeg (for audio slicing), ISrtService, VoSliceService, SrtEntry, BrollPromptItem, Stepper component

---

## Task 1: Add Stepper State Management for Expand Step

**Files:**
- Create: `Components/Pages/ScriptGenerator/ScriptGenerator.razor.Stepper.cs`
- Modify: `Components/Pages/ScriptGenerator/ScriptGenerator.razor`

**Step 1: Create stepper partial class**

```csharp
// Create new file: Components/Pages/ScriptGenerator/ScriptGenerator.razor.Stepper.cs

using Microsoft.AspNetCore.Components;

namespace BunbunBroll.Components.Pages.ScriptGenerator;

public partial class ScriptGenerator
{
    // Stepper steps definition
    private readonly string[] _stepperSteps = new[]
    {
        "1. Generate Script",
        "2. Expand & Slice VO",  // NEW: Expansion + VO slicing step
        "3. B-Roll Prompts",
        "4. Generate Media",
        "5. Compose Video"
    };

    private int _currentStep = 0;
    private bool _canProceedToStep2 = false;
    private bool _canProceedToStep3 = false;
    private bool _canProceedToStep4 = false;
    private bool _canProceedToStep5 = false;

    private void GoToStep(int step)
    {
        if (step < 0 || step >= _stepperSteps.Length) return;

        if (step == 1 && !_canProceedToStep2) return;
        if (step == 2 && !_canProceedToStep3) return;
        if (step == 3 && !_canProceedToStep4) return;
        if (step == 4 && !_canProceedToStep5) return;

        _currentStep = step;
        StateHasChanged();
    }

    private void NextStep() => GoToStep(_currentStep + 1);
    private void PreviousStep() => GoToStep(_currentStep - 1);

    private void OnScriptGenerationComplete()
    {
        _canProceedToStep2 = true;
        StateHasChanged();
    }

    private void OnExpansionComplete()
    {
        _canProceedToStep3 = true;
        StateHasChanged();
    }

    private void OnBrollPromptsComplete()
    {
        _canProceedToStep4 = true;
        StateHasChanged();
    }

    private void OnMediaGenerationComplete()
    {
        _canProceedToStep5 = true;
        StateHasChanged();
    }

    private bool IsNextStepDisabled()
    {
        return _currentStep switch
        {
            0 => !_canProceedToStep2,
            1 => !_canProceedToStep3,
            2 => !_canProceedToStep4,
            3 => !_canProceedToStep5,
            _ => true
        };
    }
}
```

**Step 2: Add stepper UI to main razor**

```razor
@* In Components/Pages/ScriptGenerator/ScriptGenerator.razor, add after page header *@

<div class="mb-8">
    <div class="flex items-center justify-between">
        @for (int i = 0; i < _stepperSteps.Length; i++)
        {
            var stepNum = i + 1;
            var isActive = _currentStep == i;
            var isCompleted = i switch
            {
                0 => _canProceedToStep2,
                1 => _canProceedToStep3,
                2 => _canProceedToStep4,
                3 => _canProceedToStep5,
                _ => false
            };
            var isClickable = i switch
            {
                0 => true,
                1 => _canProceedToStep2,
                2 => _canProceedToStep3,
                3 => _canProceedToStep4,
                4 => _canProceedToStep5,
                _ => false
            };

            <div class="flex items-center flex-1">
                @if (i > 0)
                {
                    <div class="flex-1 h-1 @((isCompleted || isActive) ? "bg-blue-500" : "bg-gray-700")"></div>
                }

                <button class="flex flex-col items-center gap-2 @(!isClickable ? "opacity-50 cursor-not-allowed" : "cursor-pointer")"
                        disabled="@(!isClickable)"
                        @onclick="() => GoToStep(i)">
                    <div class="w-10 h-10 rounded-full flex items-center justify-center font-semibold transition-all
                            @(isActive ? "bg-blue-500 text-white scale-110" :
                              isCompleted ? "bg-green-500 text-white" : "bg-gray-700 text-gray-400")">
                        @if (isCompleted)
                        {
                            <svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                                <path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5 13l4 4L19 7" />
                            </svg>
                        }
                        else
                        {
                            @stepNum
                        }
                    </div>
                    <span class="text-xs text-center hidden sm:block @(isActive ? "text-blue-400 font-semibold" : "text-gray-400")">
                        @_stepperSteps[i]
                    </span>
                </button>
            </div>
        }
    </div>
</div>

@* Step content *@
<div class="@(_currentStep == 0 ? "" : "hidden")">
    @* Existing script generation UI *@
</div>

<div class="@(_currentStep == 1 ? "" : "hidden")">
    @* NEW: Expand & Slice VO UI - Task 4 *@
    <ExpandAndSliceVoView />
</div>

<div class="@(_currentStep == 2 ? "" : "hidden")">
    @* Existing B-roll prompts UI *@
</div>

<div class="@(_currentStep == 3 ? "" : "hidden")">
    @* Existing media generation UI *@
</div>

<div class="@(_currentStep == 4 ? "" : "hidden")">
    @* Existing video composition UI *@
</div>

@* Navigation buttons *@
<div class="flex justify-between mt-8 pt-4 border-t border-gray-800">
    <button class="btn-secondary @(_currentStep == 0 ? "invisible" : "")"
            disabled="@(_currentStep == 0)"
            @onclick="PreviousStep">
        ← Previous
    </button>
    <button class="btn-primary @(_currentStep == _stepperSteps.Length - 1 ? "invisible" : "")"
            disabled="@IsNextStepDisabled()"
            @onclick="NextStep">
        Next →
    </button>
</div>
```

**Step 3: Run verification**

Run: `dotnet build`
Expected: Project compiles successfully

**Step 4: Commit**

```bash
git add Components/Pages/ScriptGenerator/ScriptGenerator.razor Components/Pages/ScriptGenerator/ScriptGenerator.razor.Stepper.cs
git commit -m "feat: add 5-step stepper with Expand & Slice VO step"
```

---

## Task 2: Add Models for Expanded Data and VO Slicing

**Files:**
- Create: `Models/VoSlicing.cs`
- Modify: `Models/ScriptGenerationSession.cs`
- Modify: `Models/VideoConfig.cs`

**Step 1: Create VO slicing models**

```csharp
// Create new file: Models/VoSlicing.cs

namespace BunbunBroll.Models;

/// <summary>
/// Represents a single sliced audio segment matching an SRT entry
/// </summary>
public class VoSegment
{
    public int Index { get; set; }
    public string AudioPath { get; set; } = string.Empty;  // Path to sliced audio file
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
    public double DurationSeconds { get; set; }
    public string Text { get; set; } = string.Empty;  // Corresponding SRT text
    public bool IsValid { get; set; }  // Validation status
    public string? ValidationError { get; set; }
    public double ActualDurationSeconds { get; set; }  // Actual audio duration
    public double DurationDifferenceMs { get; set; }  // Difference from expected
}

/// <summary>
/// Result of VO slicing operation
/// </summary>
public class VoSliceResult
{
    public bool IsSuccess { get; set; }
    public List<VoSegment> Segments { get; set; } = new();
    public string SourceVoPath { get; set; } = string.Empty;
    public string OutputDirectory { get; set; } = string.Empty;
    public int TotalSegments { get; set; }
    public double TotalDurationSeconds { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// Validation result for sliced VO against expanded SRT
/// </summary>
public class VoSliceValidationResult
{
    public bool IsValid { get; set; }
    public double AccuracyScore { get; set; }  // 0-100
    public int ValidSegments { get; set; }
    public int InvalidSegments { get; set; }
    public int WarningSegments { get; set; }
    public List<VoSegmentValidationIssue> Issues { get; set; } = new();
    public List<SegmentMismatch> Mismatches { get; set; } = new();
}

public class VoSegmentValidationIssue
{
    public int SegmentIndex { get; set; }
    public string Text { get; set; } = string.Empty;
    public string Issue { get; set; } = string.Empty;
    public string Severity { get; set; } = "Error";  // Error, Warning
}

public class SegmentMismatch
{
    public int SegmentIndex { get; set; }
    public string Text { get; set; } = string.Empty;
    public double ExpectedDuration { get; set; }
    public double ActualDuration { get; set; }
    public double DifferenceMs { get; set; }
    public double DifferencePercent { get; set; }
}

/// <summary>
/// Statistics for SRT expansion
/// </summary>
public class ExpansionStats
{
    public int OriginalEntryCount { get; set; }
    public int ExpandedEntryCount { get; set; }
    public double ExpansionRatio { get; set; }
    public double TotalDurationSeconds { get; set; }
    public double AverageSegmentDuration { get; set; }
    public int QuranVerseCount { get; set; }
    public int HadithCount { get; set; }
    public int KeyPhraseCount { get; set; }
    public int TotalPauseCount { get; set; }
    public double TotalPauseDuration { get; set; }
}

/// <summary>
/// Result of SRT expansion operation
/// </summary>
public class SrtExpansionResult
{
    public string ExpandedSrtPath { get; set; } = string.Empty;
    public string ExpandedLrcPath { get; set; } = string.Empty;
    public List<SrtEntry> ExpandedEntries { get; set; } = new();
    public Dictionary<int, double> PauseDurations { get; set; } = new();  // Entry index -> pause duration in seconds
    public ExpansionStats Statistics { get; set; } = new();
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
}
```

**Step 2: Update ScriptGenerationSession**

```csharp
// In Models/ScriptGenerationSession.cs, add:

public string? ExpandedSrtPath { get; set; }
public string? VoSegmentsDirectory { get; set; }  // Directory containing sliced VO segments
public List<VoSegment>? VoSegments { get; set; }
public bool HasExpandedVersion => !string.IsNullOrEmpty(ExpandedSrtPath) && File.Exists(ExpandedSrtPath);
public bool HasSlicedVo => VoSegments?.Count > 0;
public DateTime? ExpandedAt { get; set; }
public ExpansionStats? ExpansionStatistics { get; set; }
public VoSliceValidationResult? SliceValidationResult { get; set; }
```

**Step 3: Update VideoConfig**

```csharp
// In Models/VideoConfig.cs, add:

public string? ExpandedSrtPath { get; set; }
public List<VoSegment>? VoSegments { get; set; }  // Use sliced VO segments instead of single VO
```

**Step 4: Run verification**

Run: `dotnet build`
Expected: Project compiles successfully

**Step 5: Commit**

```bash
git add Models/VoSlicing.cs Models/ScriptGenerationSession.cs Models/VideoConfig.cs
git commit -m "feat: add models for VO slicing and expansion"
```

---

## Task 3: Create SRT Expansion Service

**Files:**
- Create: `Services/VideoComposition/SrtExpansionService.cs`
- Modify: `Services/VideoComposition/SrtService.cs`

**Step 1: Add expansion methods to ISrtService**

```csharp
// In Services/VideoComposition/SrtService.cs, add to interface:

List<SrtEntry> ExpandSrtEntries(List<SrtEntry> originalEntries, double targetSegmentDuration = 12.0);
string FormatExpandedSrt(List<SrtEntry> entries);
Dictionary<int, double> CalculatePauseDurations(List<SrtEntry> entries);
ExpansionStats CalculateExpansionStats(List<SrtEntry> original, List<SrtEntry> expanded, Dictionary<int, double> pauses);
```

**Step 2: Implement expansion logic**

```csharp
// In SrtService.cs, add implementations:

public List<SrtEntry> ExpandSrtEntries(List<SrtEntry> originalEntries, double targetSegmentDuration = 12.0)
{
    var result = new List<SrtEntry>();
    if (originalEntries == null || originalEntries.Count == 0) return result;

    foreach (var entry in originalEntries)
    {
        var text = entry.Text.Trim();
        if (string.IsNullOrWhiteSpace(text)) continue;

        // Split into sentences (preserving punctuation)
        var sentences = Regex.Split(text, @"(?<=[.!?])\s+", RegexOptions.Compiled)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        if (sentences.Count == 0)
        {
            result.Add(entry);
            continue;
        }

        // Calculate duration per sentence
        var originalDuration = entry.Duration.TotalSeconds;
        var durationPerSentence = originalDuration / sentences.Count;

        // If sentences are too long, subdivide by word chunks
        if (durationPerSentence > targetSegmentDuration)
        {
            TimeSpan currentTime = entry.StartTime;

            foreach (var sentence in sentences)
            {
                var words = sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var chunks = new List<string>();
                var currentChunk = new StringBuilder();

                // Target ~15 words per chunk
                foreach (var word in words)
                {
                    currentChunk.Append(word).Append(' ');
                    if (currentChunk.ToString().Split(' ').Length >= 15)
                    {
                        chunks.Add(currentChunk.ToString().Trim());
                        currentChunk.Clear();
                    }
                }
                if (currentChunk.Length > 0) chunks.Add(currentChunk.ToString().Trim());

                var sentenceDuration = durationPerSentence;
                var durationPerChunk = sentenceDuration / chunks.Count;

                foreach (var chunk in chunks)
                {
                    var chunkDuration = Math.Max(3.0, durationPerChunk);
                    result.Add(new SrtEntry
                    {
                        Index = result.Count + 1,
                        StartTime = currentTime,
                        EndTime = currentTime.Add(TimeSpan.FromSeconds(chunkDuration)),
                        Text = chunk
                    });
                    currentTime = currentTime.Add(TimeSpan.FromSeconds(chunkDuration));
                }
            }
        }
        else
        {
            // Use original sentence boundaries
            TimeSpan currentTime = entry.StartTime;
            foreach (var sentence in sentences)
            {
                result.Add(new SrtEntry
                {
                    Index = result.Count + 1,
                    StartTime = currentTime,
                    EndTime = currentTime.Add(TimeSpan.FromSeconds(durationPerSentence)),
                    Text = sentence.Trim()
                });
                currentTime = currentTime.Add(TimeSpan.FromSeconds(durationPerSentence));
            }
        }
    }

    return result;
}

public string FormatExpandedSrt(List<SrtEntry> entries)
{
    var sb = new StringBuilder();
    foreach (var entry in entries)
    {
        sb.AppendLine(entry.Index.ToString());
        sb.AppendLine($"{entry.StartTime.ToString("hh\\:mm\\:ss\\,fff")} --> {entry.EndTime.ToString("hh\\:mm\\:ss\\,fff")}");
        sb.AppendLine(entry.Text);
        sb.AppendLine();
    }
    return sb.ToString();
}

public Dictionary<int, double> CalculatePauseDurations(List<SrtEntry> entries)
{
    var pauses = new Dictionary<int, double>();

    for (int i = 0; i < entries.Count; i++)
    {
        var text = entries[i].Text.Trim();

        // Pause after Quran verses
        if (text.Contains("[OVERLAY:QuranVerse]", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("QS.", StringComparison.OrdinalIgnoreCase))
        {
            pauses[i] = 2.0;
        }
        // Pause after Hadith
        else if (text.Contains("[OVERLAY:Hadith]", StringComparison.OrdinalIgnoreCase) ||
                 text.StartsWith("HR.", StringComparison.OrdinalIgnoreCase))
        {
            pauses[i] = 1.5;
        }
        // Pause after rhetorical questions
        else if (text.Contains("[OVERLAY:RhetoricalQuestion]", StringComparison.OrdinalIgnoreCase) ||
                 text.TrimEnd().EndsWith("?"))
        {
            pauses[i] = 1.0;
        }
        // Pause after key phrases
        else if (text.Contains("[OVERLAY:KeyPhrase]", StringComparison.OrdinalIgnoreCase))
        {
            pauses[i] = 0.8;
        }
        // Ellipsis pause
        else if (text.Contains("..."))
        {
            pauses[i] = 0.5;
        }
    }

    return pauses;
}

public ExpansionStats CalculateExpansionStats(List<SrtEntry> original, List<SrtEntry> expanded, Dictionary<int, double> pauses)
{
    return new ExpansionStats
    {
        OriginalEntryCount = original.Count,
        ExpandedEntryCount = expanded.Count,
        ExpansionRatio = expanded.Count > 0 ? (double)expanded.Count / original.Count : 0,
        TotalDurationSeconds = expanded.Sum(e => e.Duration.TotalSeconds),
        AverageSegmentDuration = expanded.Count > 0 ? expanded.Average(e => e.Duration.TotalSeconds) : 0,
        QuranVerseCount = expanded.Count(e => e.Text.Contains("[OVERLAY:QuranVerse]", StringComparison.OrdinalIgnoreCase) ||
                                                 e.Text.Contains("QS.", StringComparison.OrdinalIgnoreCase)),
        HadithCount = expanded.Count(e => e.Text.Contains("[OVERLAY:Hadith]", StringComparison.OrdinalIgnoreCase) ||
                                             e.Text.StartsWith("HR.", StringComparison.OrdinalIgnoreCase)),
        KeyPhraseCount = expanded.Count(e => e.Text.Contains("[OVERLAY:KeyPhrase]", StringComparison.OrdinalIgnoreCase)),
        TotalPauseCount = pauses.Count,
        TotalPauseDuration = pauses.Values.Sum()
    };
}
```

**Step 3: Create SrtExpansionService**

```csharp
// Create new file: Services/VideoComposition/SrtExpansionService.cs

using BunbunBroll.Models;

namespace BunbunBroll.Services;

public interface ISrtExpansionService
{
    Task<SrtExpansionResult> ExpandCapCutSrtAsync(string capCutSrtPath, string sessionId, string outputDirectory);
}

public class SrtExpansionService : ISrtExpansionService
{
    private readonly ISrtService _srtService;
    private readonly ILogger<SrtExpansionService> _logger;

    public SrtExpansionService(ISrtService srtService, ILogger<SrtExpansionService> logger)
    {
        _srtService = srtService;
        _logger = logger;
    }

    public async Task<SrtExpansionResult> ExpandCapCutSrtAsync(string capCutSrtPath, string sessionId, string outputDirectory)
    {
        var result = new SrtExpansionResult { IsSuccess = false };

        try
        {
            // Read original CapCut SRT
            var originalSrtContent = await File.ReadAllTextAsync(capCutSrtPath);
            var originalEntries = _srtService.ParseSrt(originalSrtContent);

            if (originalEntries.Count == 0)
            {
                result.ErrorMessage = "No entries found in CapCut SRT";
                return result;
            }

            // Expand entries
            result.ExpandedEntries = _srtService.ExpandSrtEntries(originalEntries, targetSegmentDuration: 12.0);

            // Calculate pauses
            result.PauseDurations = _srtService.CalculatePauseDurations(result.ExpandedEntries);

            // Calculate statistics
            result.Statistics = _srtService.CalculateExpansionStats(originalEntries, result.ExpandedEntries, result.PauseDurations);

            // Save expanded SRT
            var sessionDir = Path.Combine(outputDirectory, sessionId);
            Directory.CreateDirectory(sessionDir);
            result.ExpandedSrtPath = Path.Combine(sessionDir, "expanded.srt");

            var expandedSrtContent = _srtService.FormatExpandedSrt(result.ExpandedEntries);
            await File.WriteAllTextAsync(result.ExpandedSrtPath, expandedSrtContent);

            // Generate LRC for reference
            result.ExpandedLrcPath = Path.Combine(sessionDir, "expanded.lrc");
            var lrcContent = await GenerateExpandedLrcAsync(result.ExpandedEntries);
            await File.WriteAllTextAsync(result.ExpandedLrcPath, lrcContent);

            result.IsSuccess = true;

            _logger.LogInformation("Expanded SRT: {Original} → {Expanded} entries ({Ratio:.##}x)",
                originalEntries.Count, result.ExpandedEntries.Count, result.Statistics.ExpansionRatio);

            return result;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = ex.Message;
            _logger.LogError(ex, "Failed to expand CapCut SRT");
            return result;
        }
    }

    private async Task<string> GenerateExpandedLrcAsync(List<SrtEntry> expandedEntries)
    {
        var sb = new StringBuilder();
        foreach (var entry in expandedEntries)
        {
            var minutes = (int)entry.StartTime.TotalMinutes;
            var seconds = entry.StartTime.Seconds;
            var centiseconds = entry.StartTime.Milliseconds / 10;
            sb.AppendLine($"[{minutes:D2}:{seconds:D2}.{centiseconds:D2}]{entry.Text}");
        }
        return await Task.FromResult(sb.ToString());
    }
}
```

**Step 4: Register service**

```csharp
// In Program.cs:
builder.Services.AddScoped<ISrtExpansionService, SrtExpansionService>();
```

**Step 5: Run verification**

Run: `dotnet build`
Expected: Project compiles successfully

**Step 6: Commit**

```bash
git add Services/VideoComposition/SrtExpansionService.cs Services/VideoComposition/SrtService.cs Program.cs
git commit -m "feat: add SRT expansion service"
```

---

## Task 4: Create VO Slicing Service (FFmpeg)

**Files:**
- Create: `Services/VideoComposition/VoSlicingService.cs`

**Step 1: Create VO slicing service**

```csharp
// Create new file: Services/VideoComposition/VoSlicingService.cs

using BunbunBroll.Models;

namespace BunbunBroll.Services;

public interface IVoSlicingService
{
    Task<VoSliceResult> SliceVoAsync(string voPath, List<SrtEntry> expandedEntries, string outputDirectory);
    Task<VoSliceValidationResult> ValidateSlicedSegmentsAsync(List<VoSegment> segments, List<SrtEntry> expandedEntries);
    Task<double> GetAudioDurationAsync(string audioPath);
}

public class VoSlicingService : IVoSlicingService
{
    private readonly ILogger<VoSlicingService> _logger;
    private readonly IFFmpegService _ffmpegService;

    public VoSlicingService(ILogger<VoSlicingService> logger, IFFmpegService ffmpegService)
    {
        _logger = logger;
        _ffmpegService = ffmpegService;
    }

    public async Task<VoSliceResult> SliceVoAsync(string voPath, List<SrtEntry> expandedEntries, string outputDirectory)
    {
        var result = new VoSliceResult
        {
            SourceVoPath = voPath,
            OutputDirectory = outputDirectory
        };

        try
        {
            if (!File.Exists(voPath))
            {
                result.Errors.Add($"VO file not found: {voPath}");
                return result;
            }

            // Create output directory for segments
            var segmentsDir = Path.Combine(outputDirectory, "vo_segments");
            Directory.CreateDirectory(segmentsDir);

            result.Segments = new List<VoSegment>();

            // Get source VO duration
            var sourceDuration = await GetAudioDurationAsync(voPath);
            _logger.LogInformation("Source VO duration: {Duration}s", sourceDuration);

            // Slice VO for each expanded SRT entry
            for (int i = 0; i < expandedEntries.Count; i++)
            {
                var entry = expandedEntries[i];
                var startTime = entry.StartTime.TotalSeconds;
                var endTime = entry.EndTime.TotalSeconds;
                var duration = endTime - startTime;

                // Validate timing is within source VO
                if (endTime > sourceDuration)
                {
                    result.Warnings.Add($"Entry {i + 1} ends at {endTime}s but VO is only {sourceDuration}s long");
                    // Adjust to available duration
                    endTime = sourceDuration;
                    duration = endTime - startTime;
                }

                var outputPath = Path.Combine(segmentsDir, $"segment_{i + 1:D3}.mp3");

                // Use FFmpeg to slice the audio
                var ffmpegArgs = $"-ss {startTime:F3} -i \"{voPath}\" -t {duration:F3} -c copy -y \"{outputPath}\"";

                var ffmpegResult = await _ffmpegService.RunAsync(ffmpegArgs);

                if (!ffmpegResult.IsSuccess)
                {
                    result.Errors.Add($"Failed to slice segment {i + 1}: {ffmpegResult.Error}");
                    continue;
                }

                // Get actual duration of sliced segment
                var actualDuration = await GetAudioDurationAsync(outputPath);
                var durationDiff = Math.Abs(actualDuration - duration) * 1000; // in ms

                var segment = new VoSegment
                {
                    Index = i + 1,
                    AudioPath = outputPath,
                    StartTime = entry.StartTime,
                    EndTime = entry.EndTime,
                    DurationSeconds = duration,
                    Text = entry.Text,
                    IsValid = Math.Abs(durationDiff) < 100, // Valid if within 100ms
                    ActualDurationSeconds = actualDuration,
                    DurationDifferenceMs = durationDiff
                };

                if (!segment.IsValid)
                {
                    segment.ValidationError = $"Duration mismatch: expected {duration:F3}s, got {actualDuration:F3}s";
                }

                result.Segments.Add(segment);
            }

            result.TotalSegments = result.Segments.Count(s => s.IsValid);
            result.TotalDurationSeconds = result.Segments.Sum(s => s.ActualDurationSeconds);
            result.IsSuccess = result.Segments.Count > 0;

            _logger.LogInformation("VO slicing complete: {Valid}/{Total} valid segments",
                result.TotalSegments, expandedEntries.Count);

            return result;
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Slicing failed: {ex.Message}");
            _logger.LogError(ex, "VO slicing failed");
            return result;
        }
    }

    public async Task<VoSliceValidationResult> ValidateSlicedSegmentsAsync(List<VoSegment> segments, List<SrtEntry> expandedEntries)
    {
        var result = new VoSliceValidationResult
        {
            IsValid = true,
            ValidSegments = 0,
            InvalidSegments = 0,
            WarningSegments = 0
        };

        try
        {
            for (int i = 0; i < segments.Count && i < expandedEntries.Count; i++)
            {
                var segment = segments[i];
                var entry = expandedEntries[i];

                var expectedDuration = entry.Duration.TotalSeconds;
                var actualDuration = segment.ActualDurationSeconds;
                var diffMs = Math.Abs(expectedDuration - actualDuration) * 1000;
                var diffPercent = (diffMs / expectedDuration) * 100;

                // Check validation rules
                if (diffMs > 200) // More than 200ms difference = error
                {
                    result.Issues.Add(new VoSegmentValidationIssue
                    {
                        SegmentIndex = i,
                        Text = entry.Text,
                        Issue = $"Duration mismatch: {diffMs:F0}ms ({diffPercent:F1}%)",
                        Severity = "Error"
                    });
                    result.InvalidSegments++;
                    segment.IsValid = false;
                }
                else if (diffMs > 100) // More than 100ms = warning
                {
                    result.Issues.Add(new VoSegmentValidationIssue
                    {
                        SegmentIndex = i,
                        Text = entry.Text,
                        Issue = $"Duration drift: {diffMs:F0}ms ({diffPercent:F1}%)",
                        Severity = "Warning"
                    });
                    result.WarningSegments++;
                }
                else
                {
                    result.ValidSegments++;
                    segment.IsValid = true;
                }

                // Track mismatches
                if (diffMs > 50)
                {
                    result.Mismatches.Add(new SegmentMismatch
                    {
                        SegmentIndex = i,
                        Text = entry.Text,
                        ExpectedDuration = expectedDuration,
                        ActualDuration = actualDuration,
                        DifferenceMs = diffMs,
                        DifferencePercent = diffPercent
                    });
                }
            }

            // Calculate accuracy score
            var totalSegments = segments.Count;
            if (totalSegments > 0)
            {
                result.AccuracyScore = ((double)result.ValidSegments / totalSegments) * 100
                    - (result.InvalidSegments * 5)
                    - (result.WarningSegments * 2);
                result.AccuracyScore = Math.Max(0, Math.Min(100, result.AccuracyScore));
            }

            result.IsValid = result.InvalidSegments == 0 && result.AccuracyScore >= 90;

            _logger.LogInformation("Validation: Score={Score}%, Valid={Valid}, Invalid={Invalid}, Warnings={Warn}",
                result.AccuracyScore, result.ValidSegments, result.InvalidSegments, result.WarningSegments);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Validation failed");
            result.IsValid = false;
            return result;
        }
    }

    public async Task<double> GetAudioDurationAsync(string audioPath)
    {
        try
        {
            // Use FFprobe to get duration
            var ffprobeArgs = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{audioPath}\"";
            var result = await _ffmpegService.RunFFprobeAsync(ffprobeArgs);

            if (double.TryParse(result.Output, out var duration))
            {
                return duration;
            }

            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get audio duration: {Path}", audioPath);
            return 0;
        }
    }
}
```

**Step 2: Register service**

```csharp
// In Program.cs:
builder.Services.AddScoped<IVoSlicingService, VoSlicingService>();
```

**Step 3: Run verification**

Run: `dotnet build`
Expected: Project compiles successfully

**Step 4: Commit**

```bash
git add Services/VideoComposition/VoSlicingService.cs Program.cs
git commit -m "feat: add VO slicing service with FFmpeg"
```

---

## Task 5: Create Expand & Slice VO View (Stepper 2 UI)

**Files:**
- Create: `Components/Views/ScriptGenerator/ExpandAndSliceVoView.razor`
- Modify: `Components/Pages/ScriptGenerator/ScriptGenerator.razor.Expansion.cs`

**Step 1: Create expansion partial class**

```csharp
// Create new file: Components/Pages/ScriptGenerator/ScriptGenerator.razor.Expansion.cs

using Microsoft.AspNetCore.Components;
using BunbunBroll.Services;
using BunbunBroll.Models;

namespace BunbunBroll.Components.Pages.ScriptGenerator;

public partial class ScriptGenerator
{
    // Expansion state
    private bool _isExpanding = false;
    private bool _isSlicing = false;
    private bool _isValidating = false;
    private string? _expansionError = null;
    private bool _showExpansionDetails = false;

    // File paths
    private string? _voFilePath = null;  // Existing VO file path

    // Expansion data
    private List<SrtEntry>? _expandedEntries = null;
    private Dictionary<int, double>? _pauseDurations = null;
    private ExpansionStats? _expansionStats = null;
    private List<VoSegment>? _voSegments = null;
    private VoSliceValidationResult? _validationResult = null;

    [Inject] private ISrtExpansionService SrtExpansionService { get; set; } = null!;
    [Inject] private IVoSlicingService VoSlicingService { get; set; } = null!;

    private async Task HandleExpandSrt()
    {
        if (string.IsNullOrEmpty(_capCutSrtPath))
        {
            _expansionError = "Please upload CapCut SRT first";
            return;
        }

        _isExpanding = true;
        _expansionError = null;
        StateHasChanged();

        try
        {
            var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "output");
            var expansionResult = await SrtExpansionService.ExpandCapCutSrtAsync(
                _capCutSrtPath,
                _sessionId ?? Guid.NewGuid().ToString(),
                outputDir
            );

            if (!expansionResult.IsSuccess)
            {
                _expansionError = expansionResult.ErrorMessage ?? "Expansion failed";
                return;
            }

            // Store expansion data
            _expandedEntries = expansionResult.ExpandedEntries;
            _pauseDurations = expansionResult.PauseDurations;
            _expansionStats = expansionResult.Statistics;

            // Update session
            if (_resultSession != null)
            {
                _resultSession.ExpandedSrtPath = expansionResult.ExpandedSrtPath;
                _resultSession.ExpandedAt = DateTime.UtcNow;
                _resultSession.ExpansionStatistics = expansionResult.Statistics;
            }

            _showExpansionDetails = true;
            _logger.LogInformation("SRT expansion complete");
        }
        catch (Exception ex)
        {
            _expansionError = $"Expansion failed: {ex.Message}";
            _logger.LogError(ex, "SRT expansion failed");
        }
        finally
        {
            _isExpanding = false;
            StateHasChanged();
        }
    }

    private async Task HandleSliceVo()
    {
        if (_resultSession?.ExpandedSrtPath == null || _expandedEntries == null)
        {
            _expansionError = "Please expand SRT first";
            return;
        }

        if (string.IsNullOrEmpty(_voFilePath) || !File.Exists(_voFilePath))
        {
            _expansionError = "Please upload VO file first";
            return;
        }

        _isSlicing = true;
        _expansionError = null;
        StateHasChanged();

        try
        {
            var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "output", _sessionId ?? "");
            var sliceResult = await VoSlicingService.SliceVoAsync(
                _voFilePath,
                _expandedEntries,
                outputDir
            );

            if (!sliceResult.IsSuccess)
            {
                _expansionError = $"Slicing failed: {string.Join(", ", sliceResult.Errors)}";
                return;
            }

            _voSegments = sliceResult.Segments;

            // Update session
            if (_resultSession != null)
            {
                _resultSession.VoSegments = _voSegments;
                _resultSession.VoSegmentsDirectory = sliceResult.OutputDirectory;
            }

            _logger.LogInformation("VO slicing complete: {Count} segments", _voSegments.Count);

            // Auto-validate after slicing
            await HandleValidateSlices();
        }
        catch (Exception ex)
        {
            _expansionError = $"Slicing failed: {ex.Message}";
            _logger.LogError(ex, "VO slicing failed");
        }
        finally
        {
            _isSlicing = false;
            StateHasChanged();
        }
    }

    private async Task HandleValidateSlices()
    {
        if (_voSegments == null || _expandedEntries == null)
        {
            return;
        }

        _isValidating = true;
        StateHasChanged();

        try
        {
            _validationResult = await VoSlicingService.ValidateSlicedSegmentsAsync(_voSegments, _expandedEntries);

            // Update session
            if (_resultSession != null)
            {
                _resultSession.SliceValidationResult = _validationResult;
            }

            // If validation passes, enable step 3
            if (_validationResult.IsValid && _validationResult.AccuracyScore >= 90)
            {
                OnExpansionComplete();
            }
        }
        catch (Exception ex)
        {
            _expansionError = $"Validation failed: {ex.Message}";
        }
        finally
        {
            _isValidating = false;
            StateHasChanged();
        }
    }

    private void HandleVoUpload(string filePath)
    {
        _voFilePath = filePath;
        StateHasChanged();
    }
}
```

**Step 2: Create the view component**<tool_call><arg_key>file_path</arg_key><arg_value>E:\VibeCode\ScriptFlow_workspace\bunbun-broll-generator\docs\plans\2026-02-21-srt-expand-stepper-v2.md