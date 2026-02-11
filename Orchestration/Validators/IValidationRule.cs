using BunbunBroll.Models;

namespace BunbunBroll.Orchestration.Validators;

/// <summary>
/// Interface for validation rules
/// </summary>
public interface IValidationRule
{
    string RuleName { get; }
    bool AppliesTo(PhaseDefinition phase);
    Task<List<ValidationIssue>> ValidateAsync(
        string content,
        PhaseDefinition phase,
        GenerationContext context);
}
