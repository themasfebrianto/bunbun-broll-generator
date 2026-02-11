using System.ComponentModel.DataAnnotations;

namespace BunbunBroll.Models;

/// <summary>
/// Individual phase state within a script generation session.
/// </summary>
public class ScriptGenerationPhase
{
    [Key]
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    [Required]
    public string SessionId { get; set; } = string.Empty;

    public ScriptGenerationSession Session { get; set; } = null!;

    [Required]
    public string PhaseId { get; set; } = string.Empty;

    public string PhaseName { get; set; } = string.Empty;

    public int Order { get; set; }

    public PhaseStatus Status { get; set; } = PhaseStatus.Pending;

    public string? ContentFilePath { get; set; }

    public int? WordCount { get; set; }

    public double? DurationSeconds { get; set; }

    public bool IsValidated { get; set; }

    /// <summary>
    /// Stored as JSON string in database.
    /// </summary>
    public string? WarningsJson { get; set; }

    public DateTime? CompletedAt { get; set; }
}
