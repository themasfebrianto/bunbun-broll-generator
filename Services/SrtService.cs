using System.Text;
using System.Text.RegularExpressions;
using BunbunBroll.Models;

namespace BunbunBroll.Services;

public interface ISrtService
{
    List<SrtEntry> ParseSrt(string content);
    List<(string Timestamp, string Text)> MergeToSegments(List<SrtEntry> entries, double maxDurationSeconds = 35.0);
    List<MicroBeatSegment> ParseWithPhaseSplitting(string content, IPhaseDetectionService phaseDetectionService, ITimestampSplitterService splitterService);
}

public class SrtService : ISrtService
{
    private static readonly Regex TimestampRegex = new Regex(@"(\d{2}:\d{2}:\d{2}[,. ]\d{3})", RegexOptions.Compiled);

    public List<SrtEntry> ParseSrt(string content)
    {
        var result = new List<SrtEntry>();
        if (string.IsNullOrWhiteSpace(content)) return result;

        // Normalize line endings
        content = content.Replace("\r\n", "\n").Replace("\r", "\n");
        var blocks = content.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var block in blocks)
        {
            var lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2) continue;

            // Find time line (index 0 or 1 usually)
            int timeLineIndex = -1;
            for (int i = 0; i < Math.Min(lines.Length, 3); i++)
            {
                if (lines[i].Contains("-->"))
                {
                    timeLineIndex = i;
                    break;
                }
            }

            if (timeLineIndex == -1) continue;

            var timeLine = lines[timeLineIndex];
            var timeParts = timeLine.Split(new[] { "-->" }, StringSplitOptions.None);
            if (timeParts.Length != 2) continue;

            if (!TryParseTimestamp(timeParts[0], out var start)) continue;
            if (!TryParseTimestamp(timeParts[1], out var end)) continue;

            // Text is everything after time line
            var text = string.Join(" ", lines.Skip(timeLineIndex + 1)).Trim();
            
            // Cleanup text
            text = CleanSubtitleText(text);

            if (string.IsNullOrWhiteSpace(text)) continue;

            // Try to get index from previous line
            int index = 0;
            if (timeLineIndex > 0)
            {
                int.TryParse(lines[timeLineIndex - 1].Trim(), out index);
            }

            result.Add(new SrtEntry
            {
                Index = index,
                StartTime = start,
                EndTime = end,
                Text = text
            });
        }

        return result;
    }

    public List<(string Timestamp, string Text)> MergeToSegments(List<SrtEntry> entries, double maxDurationSeconds = 35.0)
    {
        var result = new List<(string Timestamp, string Text)>();
        if (entries == null || entries.Count == 0) return result;

        var currentText = new StringBuilder();
        TimeSpan? blockStart = null;
        var softLimitSeconds = maxDurationSeconds * 0.7; // Lower soft limit for fallbacks (target ~18s)

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var trimmedText = entry.Text.Trim();
            
            if (blockStart == null) 
            {
                blockStart = entry.StartTime;
                currentText.Append(trimmedText);
            }
            else
            {
                var potentialDuration = (entry.EndTime - blockStart.Value).TotalSeconds;
                
                // Detection 1: Punctuation (Preferred)
                bool isSentenceEnd = Regex.IsMatch(trimmedText, @"[\.\?\!…][""'\u201d\u2019\)]?$");

                // Detection 2: Silence Gaps (Fallback)
                bool hasSignificantGap = false;
                if (i < entries.Count - 1)
                {
                    var nextEntry = entries[i + 1];
                    var gap = (nextEntry.StartTime - entry.EndTime).TotalSeconds;
                    if (gap > 0.45) // Pause between speech > 450ms
                    {
                        hasSignificantGap = true;
                    }
                }

                if (potentialDuration > maxDurationSeconds)
                {
                    // Hard Split: Exceeds absolute max duration
                    result.Add((FormatTimestamp(blockStart.Value), currentText.ToString().Trim()));
                    
                    blockStart = entry.StartTime;
                    currentText.Clear();
                    currentText.Append(trimmedText);
                }
                else if (potentialDuration > softLimitSeconds && (isSentenceEnd || hasSignificantGap))
                {
                    // Smart Split: natural punctuation OR natural pause
                    if (currentText.Length > 0) currentText.Append(" ");
                    currentText.Append(trimmedText);
                    
                    result.Add((FormatTimestamp(blockStart.Value), currentText.ToString().Trim()));
                    
                    blockStart = null;
                    currentText.Clear();
                }
                else
                {
                    // Fits in current block
                    if (currentText.Length > 0) currentText.Append(" ");
                    currentText.Append(trimmedText);
                }
            }

            // Always add the last block if it wasn't just added and exists
            if (i == entries.Count - 1 && blockStart != null)
            {
                result.Add((FormatTimestamp(blockStart.Value), currentText.ToString().Trim()));
            }
        }

        return result;
    }

    private bool TryParseTimestamp(string timestampStr, out TimeSpan result)
    {
        result = TimeSpan.Zero;
        timestampStr = timestampStr.Trim().Replace(',', '.'); // Allow both comma and dot

        // Standard SRT: 00:00:00.000
        if (TimeSpan.TryParse(timestampStr, out result))
        {
            return true;
        }

        // Fallback or more lenient parsing if needed
        return false;
    }

    private string FormatTimestamp(TimeSpan ts)
    {
        var minutes = (int)ts.TotalMinutes;
        return $"[{minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}]";
    }

    private string CleanSubtitleText(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        // Remove HTML tags
        text = Regex.Replace(text, "<.*?>", "");

        // Remove SRT style tags like { ... }
        text = Regex.Replace(text, "{.*?\\}", "");

        // Replace dashes and extra whitespace
        text = text.Replace("-", " ").Replace("—", " ").Replace("–", " ");
        text = Regex.Replace(text, @"\s+", " ").Trim();

        return text;
    }

    /// <summary>
    /// Parse SRT content with phase-aware timestamp splitting.
    /// This is the main entry point for visual hooking - it splits segments
    /// into micro-beats based on phase configuration.
    /// </summary>
    public List<MicroBeatSegment> ParseWithPhaseSplitting(
        string content,
        IPhaseDetectionService phaseDetectionService,
        ITimestampSplitterService splitterService)
    {
        // First, parse SRT into entries
        var entries = ParseSrt(content);
        if (entries.Count == 0)
            return new List<MicroBeatSegment>();

        // Then, merge into segments (using existing logic)
        var segments = MergeToSegments(entries, maxDurationSeconds: 35.0);

        // Finally, split into micro-beats based on phase
        return splitterService.SplitIntoMicroBeats(segments, phaseDetectionService);
    }
}
