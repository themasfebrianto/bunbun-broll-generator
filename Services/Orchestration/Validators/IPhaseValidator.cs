using BunbunBroll.Models;

namespace BunbunBroll.Services.Orchestration.Validators;

/// <summary>
/// Interface for phase validation
/// </summary>
public interface IPhaseValidator
{
    /// <summary>
    /// Validate generated content against phase requirements
    /// </summary>
    Task<PhaseValidationResult> ValidateAsync(
        string content,
        PhaseDefinition phase,
        GenerationContext context);

    /// <summary>
    /// Get validation feedback formatted for LLM regeneration prompt
    /// </summary>
    string GetFeedbackForRegeneration(PhaseValidationResult result);
}

/// <summary>
/// Result of phase validation
/// </summary>
public class PhaseValidationResult
{
    public bool IsValid { get; set; }
    public int WordCount { get; set; }
    public int EstimatedDurationSeconds { get; set; }
    public List<ValidationIssue> Issues { get; set; } = new();
}

/// <summary>
/// Single validation issue
/// </summary>
public class ValidationIssue
{
    public string Category { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public IssueSeverity Severity { get; set; }
}

/// <summary>
/// Severity of validation issue
/// </summary>
public enum IssueSeverity
{
    Error,
    Warning,
    Info
}
