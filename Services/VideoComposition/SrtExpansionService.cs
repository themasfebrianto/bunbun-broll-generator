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

            // Calculate pauses
            result.PauseDurations = _srtService.CalculatePauseDurations(result.ExpandedEntries);

            // Apply pauses to re-time the segments
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
        TimeSpan accumulatedPause = TimeSpan.Zero;

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            
            // Shift start and end time by accumulated pause
            entry.StartTime = entry.StartTime.Add(accumulatedPause);
            entry.EndTime = entry.EndTime.Add(accumulatedPause);

            // Add the pause for THIS segment to the accumulated pause for the NEXT segments
            if (pauseDurations.TryGetValue(i, out double pauseSeconds))
            {
                accumulatedPause = accumulatedPause.Add(TimeSpan.FromSeconds(pauseSeconds));
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
