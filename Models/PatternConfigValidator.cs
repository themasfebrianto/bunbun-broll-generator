using System.Text.Json;

namespace BunbunBroll.Models;

/// <summary>
/// Validates pattern configurations on load.
/// Catches common errors early before generation starts.
/// </summary>
public class PatternConfigValidator
{
    public ValidationResult Validate(PatternConfiguration pattern)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // Check required fields
        if (string.IsNullOrEmpty(pattern.Name))
        {
            errors.Add("Pattern must have a 'name' field");
        }

        if (pattern.Phases.Count == 0)
        {
            errors.Add("Pattern must have at least one phase");
        }

        // Check phase ordering
        var orders = pattern.Phases.Select(p => p.Order).ToList();
        var duplicateOrders = orders.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicateOrders.Any())
        {
            errors.Add($"Duplicate phase orders detected: {string.Join(", ", duplicateOrders)}");
        }

        // Check for gaps in phase ordering
        var sortedOrders = orders.Distinct().OrderBy(x => x).ToList();
        for (int i = 0; i < sortedOrders.Count - 1; i++)
        {
            if (sortedOrders[i + 1] - sortedOrders[i] > 1)
            {
                warnings.Add($"Gap in phase ordering between {sortedOrders[i]} and {sortedOrders[i + 1]}");
            }
        }

        // Check phase references in extends
        foreach (var phase in pattern.Phases)
        {
            if (phase.ExtendsTemplateNames != null)
            {
                foreach (var templateName in phase.ExtendsTemplateNames)
                {
                    if (!pattern.RuleTemplates.ContainsKey(templateName))
                    {
                        errors.Add($"Phase '{phase.Id}' (order {phase.Order}) extends unknown rule template '{templateName}'");
                    }
                }
            }
        }

        // Check phase IDs are unique
        var duplicateIds = pattern.Phases.GroupBy(p => p.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicateIds.Any())
        {
            errors.Add($"Duplicate phase IDs detected: {string.Join(", ", duplicateIds)}");
        }

        // Validate word count targets
        foreach (var phase in pattern.Phases)
        {
            if (phase.WordCountTarget.Min > phase.WordCountTarget.Max)
            {
                errors.Add($"Phase '{phase.Id}' has invalid word count target (min > max)");
            }

            if (phase.DurationTarget.Min > phase.DurationTarget.Max)
            {
                errors.Add($"Phase '{phase.Id}' has invalid duration target (min > max)");
            }
        }

        // Check global rules
        if (string.IsNullOrEmpty(pattern.GlobalRules.Language))
        {
            warnings.Add("Pattern missing 'globalRules.language' - may default incorrectly");
        }

        if (string.IsNullOrEmpty(pattern.GlobalRules.Tone))
        {
            warnings.Add("Pattern missing 'globalRules.tone' - using default tone");
        }

        return new ValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            Warnings = warnings
        };
    }

    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();

        public string GetSummary()
        {
            var parts = new List<string>();
            if (Errors.Count > 0)
            {
                parts.Add($"ERRORS ({Errors.Count}):\n  - {string.Join("\n  - ", Errors)}");
            }
            if (Warnings.Count > 0)
            {
                parts.Add($"WARNINGS ({Warnings.Count}):\n  - {string.Join("\n  - ", Warnings)}");
            }
            if (IsValid && Warnings.Count == 0)
            {
                parts.Add("Pattern is valid!");
            }
            return string.Join("\n\n", parts);
        }
    }
}
