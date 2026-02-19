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
    /// Script text for this micro-beat (chunk)
    /// </summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Full original segment text (for prompt generation in Phase 1 & 2)
    /// </summary>
    public string OriginalText { get; set; } = string.Empty;

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
                    OriginalText = segment.Text, // Store full original text
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

        // Split text into chunks (progressive splitting)
        var textChunks = SplitTextIntoChunks(segment.Text, splitFactor);

        // Use actual chunk count instead of splitFactor
        var actualBeats = Math.Min(textChunks.Count, splitFactor);

        for (int i = 0; i < actualBeats; i++)
        {
            var beatEndTime = currentTime.Add(TimeSpan.FromSeconds(beatDuration));
            // Use chunk text (guaranteed to exist after progressive splitting)
            var text = i < textChunks.Count ? textChunks[i] : segment.Text;

            beats.Add(new MicroBeatSegment
            {
                Timestamp = FormatTimestamp(currentTime),
                StartTime = currentTime,
                EndTime = beatEndTime,
                Text = text,
                OriginalText = segment.Text, // Store full original text for prompt generation
                PhaseId = phaseConfig.PhaseId,
                BeatIndex = i,
                TotalBeats = actualBeats
            });

            currentTime = beatEndTime;
        }

        return beats;
    }

    /// <summary>
    /// Split text into chunks for micro-beats using progressive splitting.
    /// Each beat gets a different portion of the text (no duplicates).
    /// Word-aware: never cuts words in half.
    /// </summary>
    private List<string> SplitTextIntoChunks(string text, int targetChunks)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new List<string> { text };

        // For short text, don't split - return as is
        if (text.Length < 50)
            return new List<string> { text };

        var result = new List<string>();
        var totalLength = text.Length;
        var chunkSize = (int)Math.Ceiling((double)totalLength / targetChunks);
        var currentPosition = 0;

        while (currentPosition < totalLength && result.Count < targetChunks)
        {
            var end = Math.Min(currentPosition + chunkSize, totalLength);

            // If not at the end and not at a word boundary, find the last space
            if (end < totalLength && !char.IsWhiteSpace(text[end]))
            {
                var lastSpace = text.LastIndexOf(' ', end, Math.Min(end - currentPosition, chunkSize));
                if (lastSpace > currentPosition)
                {
                    end = lastSpace;
                }
            }

            var chunk = text.Substring(currentPosition, end - currentPosition).Trim();
            if (!string.IsNullOrEmpty(chunk))
            {
                result.Add(chunk);
            }

            // Move past the chunk and any whitespace
            currentPosition = end;
            while (currentPosition < totalLength && char.IsWhiteSpace(text[currentPosition]))
            {
                currentPosition++;
            }
        }

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
