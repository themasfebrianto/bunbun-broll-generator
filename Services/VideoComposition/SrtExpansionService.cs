using System.Text;
using BunbunBroll.Models;

namespace BunbunBroll.Services;

public interface ISrtExpansionService
{
    Task<SrtExpansionResult> ExpandCapCutSrtAsync(string capCutSrtPath, string sessionId, string outputDirectory, bool usePadCap = true, double padCapMs = 300.0);
}

public class SrtExpansionService : ISrtExpansionService
{
    private readonly ISrtService _srtService;
    private readonly ILogger<SrtExpansionService> _logger;
    private readonly IIntelligenceService _intelligenceService;
    private readonly IOverlayDetectionService _overlayDetectionService;

    public SrtExpansionService(ISrtService srtService, ILogger<SrtExpansionService> logger, IIntelligenceService intelligenceService, IOverlayDetectionService overlayDetectionService)
    {
        _srtService = srtService;
        _logger = logger;
        _intelligenceService = intelligenceService;
        _overlayDetectionService = overlayDetectionService;
    }

    public async Task<SrtExpansionResult> ExpandCapCutSrtAsync(string capCutSrtPath, string sessionId, string outputDirectory, bool usePadCap = true, double padCapMs = 300.0)
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

            // Preserve original timestamps for VO slicing before they get shifted by pauses
            foreach (var entry in result.ExpandedEntries)
            {
                entry.OriginalStartTime = entry.StartTime;
                entry.OriginalEndTime = entry.EndTime;
            }

            // Calculate padding by splitting the gap between entries at the midpoint.
            // This prevents CapCut ASR from clipping consonant lead-ins (e.g. "namun") and trailing vowels.
            double maxPaddingSec = padCapMs / 1000.0;

            for (int i = 0; i < result.ExpandedEntries.Count; i++)
            {
                var entry = result.ExpandedEntries[i];
                double startTime = entry.OriginalStartTime.TotalSeconds;
                double endTime = entry.OriginalEndTime.TotalSeconds;

                // Start padding: half the gap to previous entry
                double paddingStart;
                if (i > 0)
                {
                    double prevEndTime = result.ExpandedEntries[i - 1].OriginalEndTime.TotalSeconds;
                    double gapBefore = Math.Max(0, startTime - prevEndTime);
                    paddingStart = gapBefore / 2.0;
                    if (usePadCap)
                    {
                        paddingStart = Math.Min(maxPaddingSec, paddingStart);
                    }
                }
                else
                {
                    paddingStart = startTime; // First entry absorbs all leading silence
                    if (usePadCap)
                    {
                        paddingStart = Math.Min(maxPaddingSec, paddingStart);
                    }
                }
                
                // End padding: half the gap to next entry
                double paddingEnd;
                if (i < result.ExpandedEntries.Count - 1)
                {
                    double nextStartTime = result.ExpandedEntries[i + 1].OriginalStartTime.TotalSeconds;
                    double gapAfter = Math.Max(0, nextStartTime - endTime);
                    paddingEnd = gapAfter / 2.0;
                    if (usePadCap)
                    {
                        paddingEnd = Math.Min(maxPaddingSec, paddingEnd);
                    }
                }
                else
                {
                    paddingEnd = 0.3; // Safe default for last entry
                    if (usePadCap)
                    {
                        paddingEnd = Math.Min(maxPaddingSec, paddingEnd);
                    }
                }
                
                entry.PaddingStart = TimeSpan.FromSeconds(paddingStart);
                entry.PaddingEnd = TimeSpan.FromSeconds(paddingEnd);
            }

            // LLM Drama Detection
            var llmDetectionResult = await _intelligenceService.DetectDramaAsync(
                entries: result.ExpandedEntries.Select((e, i) => (i, e.Text)),
                cancellationToken: default
            );

            // Regex Overlay Detection (use outputDirectory directly — it's already output/{sessionId})
            var sessionDir = Path.Combine(outputDirectory, sessionId);
            var detectedOverlays = _overlayDetectionService.DetectOverlaysFromSourceScripts(outputDirectory, result.ExpandedEntries);

            if (!llmDetectionResult.IsSuccess)
            {
                result.LlmDetectionWarning = llmDetectionResult.ErrorMessage;
                _logger.LogWarning("LLM drama detection failed: {Error}", llmDetectionResult.ErrorMessage);
                result.DetectedOverlays = detectedOverlays;
            }
            else
            {
                result.LlmDetectionSuccess = true;
                result.LlmTokensUsed = llmDetectionResult.TokensUsed;
                result.DetectedOverlays = detectedOverlays;
                _logger.LogInformation("LLM detection: {Pauses} pauses. Regex detection: {Overlays} overlays",
                    llmDetectionResult.PauseDurations.Count,
                    detectedOverlays.Count);
            }

            // Calculate pauses taking into account the natural padding gaps
            var ruleBasedPauses = _srtService.CalculatePauseDurations(result.ExpandedEntries);
            result.PauseDurations = MergePauseDurations(ruleBasedPauses, llmDetectionResult.PauseDurations, detectedOverlays);

            // Add head silence if the first entry doesn't start at 0.0
            if (result.ExpandedEntries.Count > 0)
            {
                var firstEntry = result.ExpandedEntries[0];
                var headSilence = firstEntry.OriginalStartTime.TotalSeconds - firstEntry.PaddingStart.TotalSeconds;
                if (headSilence > 0)
                {
                    result.PauseDurations[-1] = Math.Round(headSilence, 3);
                }
            }

            // Apply pauses to re-time the segments contiguously from 0.0s, fully synced to stitched audio
            ApplyPausesToRetimeEntries(result.ExpandedEntries, result.PauseDurations);

            // Calculate statistics
            result.Statistics = _srtService.CalculateExpansionStats(originalEntries, result.ExpandedEntries, result.PauseDurations);

            // Save expanded SRT
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

    private Dictionary<int, double> MergePauseDurations(
        Dictionary<int, double> ruleBasedPauses,
        Dictionary<int, double> llmPauses,
        Dictionary<int, TextOverlayDto> overlays)
    {
        var merged = new Dictionary<int, double>(ruleBasedPauses);

        // First apply LLM pauses
        foreach (var (index, llmPause) in llmPauses)
        {
            if (merged.TryGetValue(index, out var existingPause))
            {
                merged[index] = Math.Max(existingPause, llmPause);
            }
            else
            {
                merged[index] = llmPause;
            }
        }

        // Then enforce minimum gaps for Text Overlays
        foreach (var (index, overlay) in overlays)
        {
            // Calculate a rough reading time based on text length
            var wordCount = overlay.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            double minGap = 1.0; // Base gap

            if (wordCount > 10) minGap = 2.0;
            else if (wordCount > 5) minGap = 1.5;

            if (overlay.Type.Equals("quran_verse", StringComparison.OrdinalIgnoreCase) || 
                overlay.Type.Equals("hadith", StringComparison.OrdinalIgnoreCase))
            {
                minGap = Math.Max(minGap, 2.0); // Always give at least 2s for Quran/Hadith
            }

            if (merged.TryGetValue(index, out var existingPause))
            {
                merged[index] = Math.Max(existingPause, minGap);
            }
            else
            {
                merged[index] = minGap;
                _logger.LogInformation("Enforced minimum gap of {Gap}s for overlay at index {Index}", minGap, index);
            }
        }

        return merged;
    }

    private void ApplyPausesToRetimeEntries(List<SrtEntry> entries, Dictionary<int, double> pauseDurations)
    {
        TimeSpan currentTime = TimeSpan.Zero;

        // Apply head silence first
        if (pauseDurations.TryGetValue(-1, out double headPause))
        {
            currentTime = currentTime.Add(TimeSpan.FromSeconds(headPause));
        }

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            
            // The segment duration is the original speech PLUS the paddings we are adding in slicing
            var paddedDuration = (entry.OriginalEndTime - entry.OriginalStartTime) + entry.PaddingStart + entry.PaddingEnd;

            entry.StartTime = currentTime;
            entry.EndTime = currentTime.Add(paddedDuration);

            currentTime = entry.EndTime;

            if (pauseDurations.TryGetValue(i, out double pauseSeconds))
            {
                currentTime = currentTime.Add(TimeSpan.FromSeconds(pauseSeconds));
            }
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
