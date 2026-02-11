using System.Text.RegularExpressions;
using BunbunBroll.Models;

namespace BunbunBroll.Orchestration.Generators;

/// <summary>
/// Formats generated output to markdown.
/// Ported from ScriptFlow's SectionFormatter.
/// </summary>
public class SectionFormatter
{
    /// <summary>
    /// Format LLM output to structured markdown
    /// </summary>
    public string FormatToMarkdown(
        string llmOutput,
        PhaseDefinition phase,
        GenerationContext context)
    {
        // Clean up the output
        var cleaned = CleanOutput(llmOutput);

        // Check if LLM already included the phase header
        var phaseHeaderPattern = $"^##\\s*{Regex.Escape(phase.Name)}";
        var alreadyHasHeader = Regex.IsMatch(cleaned, phaseHeaderPattern, RegexOptions.IgnoreCase);

        // Add metadata header
        var header = BuildMetadataHeader(phase, context, cleaned);

        // Add content (only add phase header if LLM didn't include it)
        string content;
        if (alreadyHasHeader)
        {
            content = cleaned;
        }
        else
        {
            content = $"## {phase.Name}\n\n{cleaned}";
        }

        // Add closing if final phase
        var footer = phase.IsFinalPhase ? BuildFooter(context) : "";

        return string.Join("\n\n", new[] { header, content, footer }.Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    private string CleanOutput(string output)
    {
        // Remove markdown code blocks if present
        var cleaned = output.Trim();
        if (cleaned.StartsWith("```"))
        {
            var firstNewline = cleaned.IndexOf('\n');
            var lastBackticks = cleaned.LastIndexOf("```");
            if (firstNewline > 0 && lastBackticks > firstNewline)
            {
                cleaned = cleaned.Substring(firstNewline + 1, lastBackticks - firstNewline - 1).Trim();
            }
        }

        return cleaned;
    }

    private string BuildMetadataHeader(
        PhaseDefinition phase,
        GenerationContext context,
        string content)
    {
        var wordCount = CountWords(content);
        var durationSec = wordCount / 3; // ~180 words/min

        return $@"# Phase {phase.Order:00}: {phase.Name}

## Project
{context.Config.Topic}

## Phase
{phase.Name} ({phase.Order} of {context.Pattern.Phases.Count})

## Duration Target
{FormatDuration(phase.DurationTarget.Min)} - {FormatDuration(phase.DurationTarget.Max)}

## Word Count Target
{phase.WordCountTarget.Min} - {phase.WordCountTarget.Max} words

---";
    }

    private string BuildFooter(GenerationContext context)
    {
        var footer = new List<string>
        {
            "---",
            "## Status",
            "draft",
            "",
            "## TTS Ready",
            "NO",
            "",
            "## Last Updated",
            DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm")
        };

        if (context.Pattern.ClosingFormula != null)
        {
            footer.Add("");
            footer.Add("## Closing");
            footer.Add(context.Pattern.ClosingFormula);
        }

        return string.Join("\n", footer);
    }

    private int CountWords(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return 0;

        var words = content.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        return words.Length;
    }

    private string FormatDuration(int seconds)
    {
        var minutes = seconds / 60;
        var secs = seconds % 60;
        return $"{minutes:00}:{secs:00}";
    }
}
