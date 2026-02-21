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
