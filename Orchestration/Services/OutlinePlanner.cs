using System.Text.Json;
using System.Text.RegularExpressions;
using BunbunBroll.Models;
using BunbunBroll.Services;
using Microsoft.Extensions.Logging;

namespace BunbunBroll.Orchestration.Services;

/// <summary>
/// Distributes outline content intelligently across phases using LLM.
/// Called once before phase generation to create per-phase outline assignments.
/// </summary>
public class OutlinePlanner
{
    private readonly IIntelligenceService _intelligenceService;
    private readonly ILogger? _logger;

    public OutlinePlanner(IIntelligenceService intelligenceService, ILogger? logger = null)
    {
        _intelligenceService = intelligenceService;
        _logger = logger;
    }

    /// <summary>
    /// Distribute outline across phases using LLM analysis.
    /// Returns a dictionary mapping phaseId → list of outline points for that phase.
    /// </summary>
    public async Task<Dictionary<string, List<string>>> DistributeAsync(
        string outline,
        List<PhaseDefinition> phases,
        string topic,
        List<string>? mustHaveBeats = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(outline) || phases.Count == 0)
            return new Dictionary<string, List<string>>();

        _logger?.LogInformation("Distributing outline across {PhaseCount} phases...", phases.Count);

        var systemPrompt = "You are a script structure planner. Return ONLY valid JSON. No explanations.";
        var userPrompt = BuildDistributionPrompt(outline, phases, topic, mustHaveBeats);

        try
        {
            var response = await _intelligenceService.GenerateContentAsync(
                systemPrompt,
                userPrompt,
                maxTokens: 4000,
                temperature: 0.3,
                cancellationToken: cancellationToken);

            if (string.IsNullOrEmpty(response))
            {
                _logger?.LogWarning("OutlinePlanner: LLM returned empty response, falling back to even distribution");
                return FallbackDistribution(outline, phases);
            }

            return ParseDistribution(response, phases);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "OutlinePlanner: LLM call failed, falling back to even distribution");
            return FallbackDistribution(outline, phases);
        }
    }

    private string BuildDistributionPrompt(
        string outline,
        List<PhaseDefinition> phases,
        string topic,
        List<string>? mustHaveBeats)
    {
        var phaseDescriptions = string.Join("\n", phases.Select(p =>
            $"- Phase \"{p.Id}\" (Order {p.Order}): {p.Name}" +
            (!string.IsNullOrWhiteSpace(p.GuidanceTemplate) ? $"\n  Purpose: {p.GuidanceTemplate}" : "")));

        var beatsSection = mustHaveBeats?.Count > 0
            ? $"\n\n=== EXISTING STORY BEATS (already assigned separately, avoid duplication) ===\n{string.Join("\n", mustHaveBeats.Select((b, i) => $"{i + 1}. {b}"))}"
            : "";

        return $@"=== TASK ===
Distribute the following outline across the script phases. Assign each outline point to the phase where it fits BEST contextually.

=== TOPIC ===
{topic}

=== OUTLINE ===
{outline}

=== AVAILABLE PHASES ===
{phaseDescriptions}
{beatsSection}

=== RULES ===
1. Every outline point MUST be assigned to exactly ONE phase
2. Respect the natural narrative flow (introduction material → early phases, deep analysis → middle phases, conclusion → final phase)
3. Each phase should get at least one outline point if possible
4. If an outline point spans multiple phases, split it into sub-points
5. Do NOT duplicate points across phases
6. If mustHaveBeats exist, avoid assigning outline points that are redundant with beats

=== OUTPUT FORMAT ===
Return ONLY a valid JSON object mapping phase IDs to arrays of outline points:

{{
  ""{phases[0].Id}"": [""outline point 1"", ""outline point 2""],
  ""{(phases.Count > 1 ? phases[1].Id : phases[0].Id)}"": [""outline point 3""]
}}";
    }

    private Dictionary<string, List<string>> ParseDistribution(string response, List<PhaseDefinition> phases)
    {
        // Strip markdown code blocks
        response = StripMarkdownCodeBlocks(response);

        // Find JSON object
        var jsonStart = response.IndexOf('{');
        var jsonEnd = response.LastIndexOf('}');

        if (jsonStart == -1 || jsonEnd == -1)
        {
            _logger?.LogWarning("OutlinePlanner: Could not find JSON object in response");
            return new Dictionary<string, List<string>>();
        }

        var jsonContent = response.Substring(jsonStart, jsonEnd - jsonStart + 1);

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(jsonContent, options)
                ?? new Dictionary<string, List<string>>();

            // Validate phase IDs exist
            var validPhaseIds = phases.Select(p => p.Id).ToHashSet();
            var validated = new Dictionary<string, List<string>>();

            foreach (var (phaseId, points) in result)
            {
                if (validPhaseIds.Contains(phaseId) && points.Count > 0)
                {
                    validated[phaseId] = points;
                }
            }

            _logger?.LogInformation("OutlinePlanner: Distributed outline across {PhaseCount} phases: {Distribution}",
                validated.Count,
                string.Join(", ", validated.Select(kv => $"{kv.Key}={kv.Value.Count} points")));

            return validated;
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex, "OutlinePlanner: Failed to parse JSON response");
            return new Dictionary<string, List<string>>();
        }
    }

    /// <summary>
    /// Fallback: parse outline into lines and distribute evenly across phases.
    /// </summary>
    private Dictionary<string, List<string>> FallbackDistribution(string outline, List<PhaseDefinition> phases)
    {
        var lines = outline
            .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim().TrimStart('-', '*', '•', ' '))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        if (lines.Count == 0)
            return new Dictionary<string, List<string>>();

        var result = new Dictionary<string, List<string>>();
        var pointsPerPhase = Math.Max(1, (int)Math.Ceiling((double)lines.Count / phases.Count));

        for (int i = 0; i < phases.Count; i++)
        {
            var start = i * pointsPerPhase;
            if (start >= lines.Count) break;

            var count = Math.Min(pointsPerPhase, lines.Count - start);
            result[phases[i].Id] = lines.Skip(start).Take(count).ToList();
        }

        _logger?.LogInformation("OutlinePlanner: Used fallback distribution across {PhaseCount} phases", result.Count);
        return result;
    }

    private static string StripMarkdownCodeBlocks(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
            return response;

        var match = Regex.Match(response, @"```(?:json)?\s*([\s\S]*?)\s*```");
        if (match.Success)
            return match.Groups[1].Value.Trim();

        return response;
    }
}
