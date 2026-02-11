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

    public string OutputDirectory { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public string? ErrorMessage { get; set; }
}
