using BunbunBroll.Models;

namespace BunbunBroll.Orchestration.Validators.Rules;

/// <summary>
/// Validates that forbidden patterns are not present
/// </summary>
public class ForbiddenPatternRule : IValidationRule
{
    public string RuleName => "ForbiddenPatterns";

    public bool AppliesTo(PhaseDefinition phase)
    {
        return phase.ForbiddenPatterns != null && phase.ForbiddenPatterns.Count > 0;
    }

    public Task<List<ValidationIssue>> ValidateAsync(
        string content,
        PhaseDefinition phase,
        GenerationContext context)
    {
        var issues = new List<ValidationIssue>();

        if (phase.ForbiddenPatterns == null)
            return Task.FromResult(issues);

        foreach (var forbidden in phase.ForbiddenPatterns)
        {
            if (content.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new ValidationIssue
                {
                    Category = RuleName,
                    Message = $"Forbidden pattern found: '{forbidden}'",
                    Severity = IssueSeverity.Error
                });
            }
        }

        return Task.FromResult(issues);
    }
}
