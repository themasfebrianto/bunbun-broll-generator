using System.Text.Json.Serialization;
using BunbunBroll.Models;

namespace BunbunBroll.Models;

/// <summary>
/// Reusable rule template that can be extended by phases.
/// Allows DRY pattern definitions.
/// </summary>
public class RuleTemplate
{
    [JsonPropertyName("requiredElements")]
    public List<string>? RequiredElements { get; set; }

    [JsonPropertyName("forbiddenPatterns")]
    public List<string>? ForbiddenPatterns { get; set; }

    [JsonPropertyName("customRules")]
    public Dictionary<string, string>? CustomRules { get; set; }

    [JsonPropertyName("guidanceTemplate")]
    public string? GuidanceTemplate { get; set; }

    /// <summary>
    /// Merge this template's rules into a phase definition.
    /// Template rules are added before phase-specific rules.
    /// </summary>
    public void ApplyTo(PhaseDefinition phase)
    {
        if (RequiredElements != null)
        {
            phase.RequiredElements.InsertRange(0, RequiredElements);
        }

        if (ForbiddenPatterns != null)
        {
            phase.ForbiddenPatterns.InsertRange(0, ForbiddenPatterns);
        }

        if (CustomRules != null)
        {
            foreach (var rule in CustomRules)
            {
                // Phase-specific rules override template rules
                if (!phase.CustomRules.ContainsKey(rule.Key))
                {
                    phase.CustomRules[rule.Key] = rule.Value;
                }
            }
        }

        // Only apply guidance if phase doesn't have one
        if (!string.IsNullOrEmpty(GuidanceTemplate) && string.IsNullOrEmpty(phase.GuidanceTemplate))
        {
            phase.GuidanceTemplate = GuidanceTemplate;
        }
    }
}
