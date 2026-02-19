using BunbunBroll.Models;

namespace BunbunBroll.Services;

/// <summary>
/// Service for splitting SRT segments into micro-beats based on phase configuration.
///
/// For Phase 1 (opening-hook): Splits long segments into 2.5s micro-beats
/// For Phase 2 (contextualization): Splits into 10s medium beats
/// For Phase 3+ (normal): No splitting, preserves original segments
/// </summary>
public interface ITimestampSplitterService
{
    /// <summary>
    /// Split SRT segments into phase-aware micro-beats
    /// </summary>
    /// <param name="segments">Original SRT segments with timestamps and text</param>
    /// <param name="phaseDetectionService">Service to detect phase for each timestamp</param>
    /// <returns>List of micro-beat segments with adjusted timestamps</returns>
    List<MicroBeatSegment> SplitIntoMicroBeats(
        List<(string Timestamp, string Text)> segments,
        IPhaseDetectionService phaseDetectionService);
}

/// <summary>
/// Represents a micro-beat segment with timing information for visual generation
/// </summary>
public class MicroBeatSegment
{
    /// <summary>
    /// Formatted timestamp string [MM:SS.mmm]
    /// </summary>
    public string Timestamp { get; set; } = string.Empty;

    /// <summary>
    /// Start time of this micro-beat
    /// </summary>
    public TimeSpan StartTime { get; set; }

    /// <summary>
    /// End time of this micro-beat
    /// </summary>
    public TimeSpan EndTime { get; set; }

    /// <summary>
    /// Script text for this micro-beat
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Phase ID this beat belongs to
    /// </summary>
    public string PhaseId { get; set; } = string.Empty;

    /// <summary>
    /// Beat index within the original segment (0-based)
    /// </summary>
    public int BeatIndex { get; set; }

    /// <summary>
    /// Total beats in the original segment
    /// </summary>
    public int TotalBeats { get; set; }

    /// <summary>
    /// Duration of this beat in seconds
    /// </summary>
    public double DurationSeconds => (EndTime - StartTime).TotalSeconds;
}

public class TimestampSplitterService : ITimestampSplitterService
{
    public List<MicroBeatSegment> SplitIntoMicroBeats(
        List<(string Timestamp, string Text)> segments,
        IPhaseDetectionService phaseDetectionService)
    {
        var result = new List<MicroBeatSegment>();

        foreach (var segment in segments)
        {
            if (!TryParseTimestamp(segment.Timestamp, out var startTime))
            {
                // Skip invalid segments
                result.Add(new MicroBeatSegment
                {
                    Timestamp = segment.Timestamp,
                    Text = segment.Text,
                    PhaseId = "normal",
                    BeatIndex = 0,
                    TotalBeats = 1
                });
                continue;
            }

            // Detect phase based on start time
            var phaseId = phaseDetectionService.DetectPhase(startTime);
            var phaseConfig = phaseDetectionService.GetPhaseConfig(phaseId);

            // Calculate segment duration
            var segmentDuration = EstimateSegmentDuration(segment.Text);

            // For normal phase, preserve original
            if (phaseId == "normal" || phaseConfig.SplitFactor <= 1)
            {
                result.Add(new MicroBeatSegment
                {
                    Timestamp = segment.Timestamp,
                    StartTime = startTime,
                    EndTime = startTime.Add(TimeSpan.FromSeconds(segmentDuration)),
                    Text = segment.Text,
                    PhaseId = phaseId,
                    BeatIndex = 0,
                    TotalBeats = 1
                });
                continue;
            }

            // Split into micro-beats
            var microBeats = CreateMicroBeats(
                segment,
                startTime,
                phaseConfig);

            result.AddRange(microBeats);
        }

        return result;
    }

    /// <summary>
    /// Create micro-beats from a single SRT segment based on phase config
    /// </summary>
    private List<MicroBeatSegment> CreateMicroBeats(
        (string Timestamp, string Text) segment,
        TimeSpan startTime,
        PhaseConfig phaseConfig)
    {
        var beats = new List<MicroBeatSegment>();
        var beatDuration = phaseConfig.KenBurnsDuration;
        var splitFactor = phaseConfig.SplitFactor;

        // Calculate total duration for this segment
        var totalDuration = beatDuration * splitFactor;
        var currentTime = startTime;

        // Split text into chunks if possible (by sentences)
        var textChunks = SplitTextIntoChunks(segment.Text, splitFactor);

        for (int i = 0; i < splitFactor; i++)
        {
            var beatEndTime = currentTime.Add(TimeSpan.FromSeconds(beatDuration));
            // Use chunk text if available and not empty, otherwise use full segment text
            var text = i < textChunks.Count && !string.IsNullOrWhiteSpace(textChunks[i])
                ? textChunks[i]
                : segment.Text;

            beats.Add(new MicroBeatSegment
            {
                Timestamp = FormatTimestamp(currentTime),
                StartTime = currentTime,
                EndTime = beatEndTime,
                Text = text,
                PhaseId = phaseConfig.PhaseId,
                BeatIndex = i,
                TotalBeats = splitFactor
            });

            currentTime = beatEndTime;
        }

        return beats;
    }

    /// <summary>
    /// Split text into chunks for micro-beats, preserving sentence boundaries where possible
    /// </summary>
    private List<string> SplitTextIntoChunks(string text, int targetChunks)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new List<string> { "" };

        // Try to split by sentences first
        var sentences = System.Text.RegularExpressions.Regex.Split(text, @"(?<=[.!?…])\s+")
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        if (sentences.Count <= 1)
        {
            // Single sentence - return as single chunk
            return new List<string> { text };
        }

        // If we have more sentences than target chunks, group them
        if (sentences.Count > targetChunks)
        {
            var chunks = new List<string>();
            var sentencesPerChunk = (int)Math.Ceiling((double)sentences.Count / targetChunks);

            for (int i = 0; i < sentences.Count; i += sentencesPerChunk)
            {
                var chunkSize = Math.Min(sentencesPerChunk, sentences.Count - i);
                var chunk = string.Join(" ", sentences.Skip(i).Take(chunkSize));
                chunks.Add(chunk);
            }

            return chunks;
        }

        // We have fewer or equal sentences to target chunks - distribute them
        var result = new List<string>();
        var sentencesIndex = 0;

        for (int i = 0; i < targetChunks && sentencesIndex < sentences.Count; i++)
        {
            // Distribute sentences evenly across chunks
            var remainingChunks = targetChunks - i;
            var remainingSentences = sentences.Count - sentencesIndex;
            var sentencesForThisChunk = (int)Math.Ceiling((double)remainingSentences / remainingChunks);

            var chunk = string.Join(" ", sentences.Skip(sentencesIndex).Take(sentencesForThisChunk));
            result.Add(chunk);
            sentencesIndex += sentencesForThisChunk;
        }

        // Don't fill remaining chunks - let CreateMicroBeats use full text for them
        return result;
    }

    /// <summary>
    /// Parse timestamp string [MM:SS.mmm] into TimeSpan
    /// </summary>
    private bool TryParseTimestamp(string timestamp, out TimeSpan result)
    {
        result = TimeSpan.Zero;

        if (string.IsNullOrWhiteSpace(timestamp))
            return false;

        // Remove brackets and parse
        var clean = timestamp.Trim().Trim('[', ']');

        // Format: [MM:SS.mmm] or [HH:MM:SS.mmm]
        var parts = clean.Split(':');
        if (parts.Length == 2)
        {
            // MM:SS.mmm format
            if (int.TryParse(parts[0], out var minutes) &&
                double.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var seconds))
            {
                result = TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
                return true;
            }
        }
        else if (parts.Length == 3)
        {
            // HH:MM:SS.mmm format
            if (int.TryParse(parts[0], out var hours) &&
                int.TryParse(parts[1], out var minutes) &&
                double.TryParse(parts[2], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var seconds))
            {
                result = TimeSpan.FromHours(hours) + TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Estimate segment duration based on text length
    /// Approximate: 150 words per minute = 2.5 words per second
    /// </summary>
    private double EstimateSegmentDuration(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 5.0; // Default 5 seconds

        var words = text.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
        var estimatedSeconds = words / 2.5; // 2.5 words per second

        return Math.Max(3.0, Math.Min(estimatedSeconds, 60.0)); // Clamp between 3-60 seconds
    }

    /// <summary>
    /// Format TimeSpan to [MM:SS.mmm] format
    /// </summary>
    private string FormatTimestamp(TimeSpan ts)
    {
        var minutes = (int)ts.TotalMinutes;
        return $"[{minutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}]";
    }
}
