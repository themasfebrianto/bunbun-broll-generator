using BunbunBroll.Models;

namespace BunbunBroll.Services.Orchestration.Validators;

/// <summary>
/// Generic validator that runs applicable validation rules
/// </summary>
public class PatternValidator : IPhaseValidator
{
    private readonly List<IValidationRule> _rules;

    public PatternValidator(IEnumerable<IValidationRule> rules)
    {
        _rules = rules.ToList();
    }

    public PatternValidator() : this(GetDefaultRules())
    {
    }

    public async Task<PhaseValidationResult> ValidateAsync(
        string content,
        PhaseDefinition phase,
        GenerationContext context)
    {
        var allIssues = new List<ValidationIssue>();

        foreach (var rule in _rules.Where(r => r.AppliesTo(phase)))
        {
            var issues = await rule.ValidateAsync(content, phase, context);
            allIssues.AddRange(issues);
        }

        var wordCount = CountWords(content);

        return new PhaseValidationResult
        {
            IsValid = !allIssues.Any(i => i.Severity == IssueSeverity.Error),
            WordCount = wordCount,
            EstimatedDurationSeconds = (int)(wordCount / 2.33), // ~140 words/min = 2.33 words/sec
            Issues = allIssues
        };
    }

    public string GetFeedbackForRegeneration(PhaseValidationResult result)
    {
        if (result.IsValid)
            return string.Empty;

        var errors = result.Issues.Where(i => i.Severity == IssueSeverity.Error).ToList();
        var warnings = result.Issues.Where(i => i.Severity == IssueSeverity.Warning).ToList();

        var feedback = new List<string>();

        if (errors.Count > 0)
        {
            feedback.Add("MUST FIX:");
            foreach (var error in errors)
                feedback.Add($"  - [{error.Category}] {error.Message}");
        }

        if (warnings.Count > 0)
        {
            feedback.Add("SHOULD FIX:");
            foreach (var warning in warnings)
                feedback.Add($"  - [{warning.Category}] {warning.Message}");
        }

        feedback.Add($"Current word count: {result.WordCount}");

        return string.Join("\n", feedback);
    }

    private static int CountWords(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return 0;

        var lines = content.Split('\n');
        var contentLines = lines.Where(l =>
            !l.TrimStart().StartsWith("#") &&
            !l.TrimStart().StartsWith("---") &&
            !string.IsNullOrWhiteSpace(l));

        var text = string.Join(" ", contentLines);
        var words = text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        return words.Length;
    }

    private static List<IValidationRule> GetDefaultRules()
    {
        return new List<IValidationRule>
        {
            new Rules.WordCountRule(),
            new Rules.ForbiddenPatternRule(),
            new Rules.RequiredElementsRule()
        };
    }
}
