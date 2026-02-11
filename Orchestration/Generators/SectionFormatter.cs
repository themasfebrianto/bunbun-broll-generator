using System.Text.RegularExpressions;
using BunbunBroll.Models;

namespace BunbunBroll.Orchestration.Generators;

/// <summary>
/// Formats generated output to clean markdown.
/// Metadata (phase info, word count, duration) is stored in the DB only — not embedded in content.
/// </summary>
public class SectionFormatter
{
    /// <summary>
    /// Format LLM output to clean markdown (no metadata headers/footers).
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
        var alreadyHasHeader = Regex.IsMatch(cleaned, phaseHeaderPattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);

        // Only add phase header if LLM didn't include it
        if (!alreadyHasHeader)
        {
            cleaned = $"## {phase.Name}\n\n{cleaned}";
        }

        return cleaned;
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
}
