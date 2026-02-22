using System.Text;
using System.Text.RegularExpressions;
using BunbunBroll.Models;
using BunbunBroll.Services;

namespace BunbunBroll.Services;

public interface ISrtService
{
    List<SrtEntry> ParseSrt(string content);
    List<(TimeSpan StartTime, TimeSpan EndTime, string Timestamp, string Text)> MergeToSegments(List<SrtEntry> entries, double maxDurationSeconds = 20.0);
    List<SrtEntry> ExpandSrtEntries(List<SrtEntry> originalEntries, double targetSegmentDuration = 12.0);
    string FormatExpandedSrt(List<SrtEntry> entries);
    string FormatExpandedSrt(List<SrtEntry> entries, Dictionary<int, TextOverlayDto>? overlays);
    Dictionary<int, double> CalculatePauseDurations(List<SrtEntry> entries);
    ExpansionStats CalculateExpansionStats(List<SrtEntry> original, List<SrtEntry> expanded, Dictionary<int, double> pauses);
    void RetimeEntriesWithActualDurations(List<SrtEntry> entries, List<VoSegment> segments, Dictionary<int, double> pauseDurations);
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

    public List<(TimeSpan StartTime, TimeSpan EndTime, string Timestamp, string Text)> MergeToSegments(List<SrtEntry> entries, double maxDurationSeconds = 20.0)
    {
        var result = new List<(TimeSpan StartTime, TimeSpan EndTime, string Timestamp, string Text)>();
        if (entries == null || entries.Count == 0) return result;

        var currentText = new StringBuilder();
        TimeSpan? blockStart = null;
        TimeSpan blockEnd = TimeSpan.Zero;
        var softLimitSeconds = maxDurationSeconds * 0.7; // Soft limit for smart splitting
        const int maxWordCount = 80; // Hard word-count limit (~32s of speech at 2.5 wps)

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var trimmedText = entry.Text.Trim();
            
            if (blockStart == null) 
            {
                blockStart = entry.StartTime;
                blockEnd = entry.EndTime;
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
                    if (gap > 0.3) // Pause between speech > 300ms
                    {
                        hasSignificantGap = true;
                    }
                }

                // Detection 3: Word count safety net
                var wordCount = currentText.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length
                              + trimmedText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                bool exceedsWordLimit = wordCount >= maxWordCount;

                if (potentialDuration > maxDurationSeconds || exceedsWordLimit)
                {
                    // Hard Split: Exceeds absolute max duration or word count limit
                    result.Add((blockStart.Value, blockEnd, FormatTimestamp(blockStart.Value), currentText.ToString().Trim()));
                    
                    blockStart = entry.StartTime;
                    blockEnd = entry.EndTime;
                    currentText.Clear();
                    currentText.Append(trimmedText);
                }
                else if (potentialDuration > softLimitSeconds && (isSentenceEnd || hasSignificantGap))
                {
                    // Smart Split: natural punctuation OR natural pause
                    if (currentText.Length > 0) currentText.Append(" ");
                    currentText.Append(trimmedText);
                    blockEnd = entry.EndTime;
                    
                    result.Add((blockStart.Value, blockEnd, FormatTimestamp(blockStart.Value), currentText.ToString().Trim()));
                    
                    blockStart = null;
                    currentText.Clear();
                }
                else
                {
                    // Fits in current block
                    if (currentText.Length > 0) currentText.Append(" ");
                    currentText.Append(trimmedText);
                    blockEnd = entry.EndTime;
                }
            }

            // Always add the last block if it wasn't just added and exists
            if (i == entries.Count - 1 && blockStart != null)
            {
                result.Add((blockStart.Value, blockEnd, FormatTimestamp(blockStart.Value), currentText.ToString().Trim()));
            }
        }

        return result;
    }

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

            var originalDuration = entry.Duration.TotalSeconds;

            // Count total words across all sentences for proportional distribution
            var wordCounts = sentences.Select(s => s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length).ToList();
            var totalWords = wordCounts.Sum();
            if (totalWords == 0) totalWords = 1; // safety

            // Check if any sentence needs further subdivision
            TimeSpan currentTime = entry.StartTime;

            for (int si = 0; si < sentences.Count; si++)
            {
                var sentence = sentences[si];
                // Proportional duration based on word count
                var sentenceDuration = originalDuration * ((double)wordCounts[si] / totalWords);

                // If sentence is too long, subdivide by word chunks
                if (sentenceDuration > targetSegmentDuration)
                {
                    var words = sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    var chunks = new List<string>();
                    var currentChunk = new StringBuilder();

                    // Target ~15 words per chunk
                    foreach (var word in words)
                    {
                        currentChunk.Append(word).Append(' ');
                        if (currentChunk.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 15)
                        {
                            chunks.Add(currentChunk.ToString().Trim());
                            currentChunk.Clear();
                        }
                    }
                    if (currentChunk.Length > 0) chunks.Add(currentChunk.ToString().Trim());

                    // Distribute chunk duration proportionally by word count
                    var chunkWordCounts = chunks.Select(c => c.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length).ToList();
                    var chunkTotalWords = chunkWordCounts.Sum();
                    if (chunkTotalWords == 0) chunkTotalWords = 1;

                    foreach (var (chunk, ci) in chunks.Select((c, i) => (c, i)))
                    {
                        var chunkDuration = Math.Max(1.0, sentenceDuration * ((double)chunkWordCounts[ci] / chunkTotalWords));
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
                else
                {
                    var safeDuration = Math.Max(0.5, sentenceDuration);
                    result.Add(new SrtEntry
                    {
                        Index = result.Count + 1,
                        StartTime = currentTime,
                        EndTime = currentTime.Add(TimeSpan.FromSeconds(safeDuration)),
                        Text = sentence.Trim()
                    });
                    currentTime = currentTime.Add(TimeSpan.FromSeconds(safeDuration));
                }
            }
        }

        return result;
    }

    public string FormatExpandedSrt(List<SrtEntry> entries)
        => FormatExpandedSrt(entries, null);

    public string FormatExpandedSrt(List<SrtEntry> entries, Dictionary<int, TextOverlayDto>? overlays)
    {
        var sb = new StringBuilder();
        int srtIndex = 1;
        
        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            
            // 1. Spoken text entry
            sb.AppendLine(srtIndex.ToString());
            sb.AppendLine($"{entry.StartTime.ToString("hh\\:mm\\:ss\\,fff")} --> {entry.EndTime.ToString("hh\\:mm\\:ss\\,fff")}");
            sb.AppendLine(entry.Text);
            sb.AppendLine();
            srtIndex++;

            // 2. Empty gap entry for Overlay (if present)
            if (overlays != null && overlays.TryGetValue(i, out var overlay))
            {
                TimeSpan gapStart = entry.EndTime;
                TimeSpan gapEnd = (i < entries.Count - 1) ? entries[i + 1].StartTime : gapStart.Add(TimeSpan.FromSeconds(2));
                
                // Only create the gap entry if there's actually a gap duration
                if (gapEnd > gapStart)
                {
                    sb.AppendLine(srtIndex.ToString());
                    sb.AppendLine($"{gapStart.ToString("hh\\:mm\\:ss\\,fff")} --> {gapEnd.ToString("hh\\:mm\\:ss\\,fff")}");
                    
                    sb.AppendLine($"[OVERLAY:{overlay.Type}]");
                    if (!string.IsNullOrWhiteSpace(overlay.Reference))
                    {
                        sb.AppendLine($"[REF] {overlay.Reference}");
                    }
                    if (!string.IsNullOrWhiteSpace(overlay.Arabic))
                    {
                        sb.AppendLine($"[ARABIC] {overlay.Arabic}");
                    }
                    
                    sb.AppendLine();
                    srtIndex++;
                }
            }
        }
        return sb.ToString();
    }

    public Dictionary<int, double> CalculatePauseDurations(List<SrtEntry> entries)
    {
        var pauses = new Dictionary<int, double>();

        for (int i = 0; i < entries.Count; i++)
        {
            // No pause after the very last entry
            if (i == entries.Count - 1) break;

            var current = entries[i];
            var next = entries[i + 1];

            // Calculate original natural gap from CapCut SRT
            double originalGap = (next.OriginalStartTime - current.OriginalEndTime).TotalSeconds;
            
            // Calculate padded gap (what will physically be missing between the WAV slices)
            double paddedGap = originalGap - current.PaddingEnd.TotalSeconds - next.PaddingStart.TotalSeconds;
            if (paddedGap < 0) paddedGap = 0;

            var text = current.Text.Trim();
            double requestedPause = 0.0;

            // Special content: extra long pauses
            if (text.Contains("QS.", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("[OVERLAY:QuranVerse]", StringComparison.OrdinalIgnoreCase))
            {
                requestedPause = 2.0;
            }
            else if (text.StartsWith("HR.", StringComparison.OrdinalIgnoreCase) ||
                     text.Contains("[OVERLAY:Hadith]", StringComparison.OrdinalIgnoreCase))
            {
                requestedPause = 1.5;
            }
            else if (text.EndsWith("?"))
            {
                requestedPause = 1.0;
            }
            else if (text.EndsWith("..."))
            {
                requestedPause = 0.8;
            }
            else if (text.EndsWith(".") || text.EndsWith("!"))
            {
                requestedPause = 0.6;
            }
            else if (text.EndsWith(",") || text.EndsWith(";") || text.EndsWith(":"))
            {
                requestedPause = 0.3; // Comma = brief pause
            }
            else
            {
                // No punctuation (typical CapCut SRT or mid-sentence chunks) = no artificial pause needed, 
                // just rely on the natural break that was captured in paddedGap.
                requestedPause = 0.0; 
            }

            // The required artificial silence we must concatenate is whichever is larger: 
            // the required punctuation pause OR the natural empty space leftover after padding.
            pauses[i] = Math.Round(Math.Max(paddedGap, requestedPause), 3);
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

    public void RetimeEntriesWithActualDurations(List<SrtEntry> entries, List<VoSegment> segments, Dictionary<int, double> pauseDurations)
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
            var segment = segments.FirstOrDefault(s => s.Index == i + 1);
            
            // Fallback to theoretical calculation if actual segment duration is unavailable
            double actualSegSecs = segment != null ? segment.ActualDurationSeconds : ((entry.OriginalEndTime - entry.OriginalStartTime) + entry.PaddingStart + entry.PaddingEnd).TotalSeconds;

            entry.StartTime = currentTime;
            entry.EndTime = currentTime.Add(TimeSpan.FromSeconds(actualSegSecs));

            currentTime = entry.EndTime;

            if (pauseDurations.TryGetValue(i, out double pauseSeconds))
            {
                currentTime = currentTime.Add(TimeSpan.FromSeconds(pauseSeconds));
            }
        }
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

}
