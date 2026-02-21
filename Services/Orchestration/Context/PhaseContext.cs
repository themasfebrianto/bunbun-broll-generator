using BunbunBroll.Models;

namespace BunbunBroll.Services.Orchestration.Context;

/// <summary>
/// Per-phase context for generation
/// </summary>
public class PhaseContext
{
    /// <summary>
    /// Current phase definition
    /// </summary>
    public PhaseDefinition Phase { get; set; } = new();

    /// <summary>
    /// Previous phase content (if any)
    /// </summary>
    public string? PreviousContent { get; set; }

    /// <summary>
    /// Previous phase name (if any)
    /// </summary>
    public string? PreviousPhaseName { get; set; }

    /// <summary>
    /// Number of retry attempts
    /// </summary>
    public int RetryAttempt { get; set; }

    /// <summary>
    /// Validation feedback from previous attempt (if retrying)
    /// </summary>
    public string? ValidationFeedback { get; set; }

    /// <summary>
    /// Entity tracking context (anti-repetition)
    /// </summary>
    public string? EntityContext { get; set; }

    /// <summary>
    /// Story beats assigned to this phase
    /// </summary>
    public List<string> AssignedBeats { get; set; } = new();

    /// <summary>
    /// Outline points assigned to this phase by OutlinePlanner
    /// </summary>
    public List<string> AssignedOutlinePoints { get; set; } = new();

    /// <summary>
    /// Key concepts or summaries from ALL previous phases to prevent repetition.
    /// </summary>
    public List<string> GlobalContext { get; set; } = new();
}
