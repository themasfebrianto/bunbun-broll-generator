using BunbunBroll.Models;

namespace BunbunBroll.Services.Orchestration.Validators.Rules;

/// <summary>
/// Validates word count is within phase target range
/// </summary>
public class WordCountRule : IValidationRule
{
    public string RuleName => "WordCount";

    public bool AppliesTo(PhaseDefinition phase) => true;

    public Task<List<ValidationIssue>> ValidateAsync(
        string content,
        PhaseDefinition phase,
        GenerationContext context)
    {
        var issues = new List<ValidationIssue>();
        var wordCount = CountWords(content);

        if (wordCount < phase.WordCountTarget.Min)
        {
            issues.Add(new ValidationIssue
            {
                Category = RuleName,
                Message = $"Word count ({wordCount}) below minimum ({phase.WordCountTarget.Min})",
                Severity = IssueSeverity.Error
            });
        }
        else if (wordCount > phase.WordCountTarget.Max)
        {
            issues.Add(new ValidationIssue
            {
                Category = RuleName,
                Message = $"Word count ({wordCount}) above maximum ({phase.WordCountTarget.Max})",
                Severity = IssueSeverity.Error
            });
        }

        return Task.FromResult(issues);
    }

    private int CountWords(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return 0;

        var lines = content.Split('\n');
        var contentLines = lines.Where(l =>
            !l.TrimStart().StartsWith("#") &&
            !l.TrimStart().StartsWith("---") &&
            !string.IsNullOrWhiteSpace(l));

        var text = string.Join(" ", contentLines);
        return text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
