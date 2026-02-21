using System.Text;
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

            // Preserve original timestamps for VO slicing before they get shifted by pauses
            foreach (var entry in result.ExpandedEntries)
            {
                entry.OriginalStartTime = entry.StartTime;
                entry.OriginalEndTime = entry.EndTime;
            }

            // Calculate padding for each entry identically to the slicing logic
            for (int i = 0; i < result.ExpandedEntries.Count; i++)
            {
                var entry = result.ExpandedEntries[i];
                double startTime = entry.OriginalStartTime.TotalSeconds;
                double endTime = entry.OriginalEndTime.TotalSeconds;

                // Add small padding to prevent tight VAD cutting off the start/end of words
                double paddingStart = 0.05; // 50ms padding at the start
                double paddingEnd = 0.150;  // 150ms padding at the end

                if (i > 0)
                {
                    double prevEndTime = result.ExpandedEntries[i - 1].OriginalEndTime.TotalSeconds;
                    double maxStartPadding = Math.Max(0, startTime - prevEndTime);
                    paddingStart = Math.Min(paddingStart, maxStartPadding * 0.9);
                }
                paddingStart = Math.Min(paddingStart, startTime);

                if (i < result.ExpandedEntries.Count - 1)
                {
                    double nextStartTime = result.ExpandedEntries[i + 1].OriginalStartTime.TotalSeconds;
                    double maxEndPadding = Math.Max(0, nextStartTime - endTime);
                    paddingEnd = Math.Min(paddingEnd, maxEndPadding * 0.9);
                }
                
                entry.PaddingStart = TimeSpan.FromSeconds(paddingStart);
                entry.PaddingEnd = TimeSpan.FromSeconds(paddingEnd);
            }

            // Calculate pauses taking into account the natural padding gaps
            result.PauseDurations = _srtService.CalculatePauseDurations(result.ExpandedEntries);

            // Apply pauses to re-time the segments contiguously from 0.0s, fully synced to stitched audio
            ApplyPausesToRetimeEntries(result.ExpandedEntries, result.PauseDurations);

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

    private void ApplyPausesToRetimeEntries(List<SrtEntry> entries, Dictionary<int, double> pauseDurations)
    {
        TimeSpan currentTime = TimeSpan.Zero;

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
