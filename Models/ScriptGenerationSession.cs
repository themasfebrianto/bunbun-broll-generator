using System.ComponentModel.DataAnnotations;

namespace BunbunBroll.Models;

/// <summary>
/// Database-backed session state for script generation.
/// </summary>
public class ScriptGenerationSession
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    [Required]
    [MaxLength(100)]
    public string PatternId { get; set; } = string.Empty;

    [Required]
    [MaxLength(500)]
    public string Topic { get; set; } = string.Empty;

    public string? FinalVideoPath { get; set; }

    public string? ExpandedSrtPath { get; set; }
    public string? VoSegmentsDirectory { get; set; }  // Directory containing sliced VO segments
    public string? StitchedVoPath { get; set; } // The final stitched VO path
    public List<VoSegment>? VoSegments { get; set; }
    public bool HasExpandedVersion => !string.IsNullOrEmpty(ExpandedSrtPath) && File.Exists(ExpandedSrtPath);
    public bool HasSlicedVo => VoSegments?.Count > 0;
    public DateTime? ExpandedAt { get; set; }
    public ExpansionStats? ExpansionStatistics { get; set; }
    public VoSliceValidationResult? SliceValidationResult { get; set; }

    public string? Outline { get; set; }

    /// <summary>
    /// JSON-serialized Dictionary&lt;string, List&lt;string&gt;&gt; mapping phaseId → outline points.
    /// Persisted after OutlinePlanner distributes the outline.
    /// </summary>
    public string? OutlineDistributionJson { get; set; }

    public int TargetDurationMinutes { get; set; }

    public string? SourceReferences { get; set; }

    [MaxLength(100)]
    public string ChannelName { get; set; } = string.Empty;

    public SessionStatus Status { get; set; } = SessionStatus.Pending;

    public List<ScriptGenerationPhase> Phases { get; set; } = new();

    private string _outputDirectory = string.Empty;
    public string OutputDirectory
    {
        get => _outputDirectory?.Replace('\\', '/') ?? string.Empty;
        set => _outputDirectory = value?.Replace('\\', '/') ?? string.Empty;
    }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public string? ErrorMessage { get; set; }
}
