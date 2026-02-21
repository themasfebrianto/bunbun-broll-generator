using BunbunBroll.Models;

namespace BunbunBroll.Services.Orchestration.Validators.Rules;

/// <summary>
/// Validates that required elements are present
/// </summary>
public class RequiredElementsRule : IValidationRule
{
    public string RuleName => "RequiredElements";

    public bool AppliesTo(PhaseDefinition phase)
    {
        return phase.RequiredElements != null && phase.RequiredElements.Count > 0;
    }

    public Task<List<ValidationIssue>> ValidateAsync(
        string content,
        PhaseDefinition phase,
        GenerationContext context)
    {
        var issues = new List<ValidationIssue>();

        if (phase.RequiredElements == null)
            return Task.FromResult(issues);

        foreach (var required in phase.RequiredElements)
        {
            var keyword = required.Replace("_", " ");
            if (!content.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new ValidationIssue
                {
                    Category = RuleName,
                    Message = $"Missing required element: '{required}'",
                    Severity = IssueSeverity.Warning
                });
            }
        }

        return Task.FromResult(issues);
    }
}
