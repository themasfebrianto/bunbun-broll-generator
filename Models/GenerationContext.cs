namespace BunbunBroll.Models;

/// <summary>
/// Shared context for generation session.
/// Enhanced from ScriptFlow with SharedData, GetPreviousPhase, and content summary.
/// </summary>
public class GenerationContext
{
    public string SessionId { get; set; } = string.Empty;
    public ScriptConfig Config { get; set; } = new();
    public PatternConfiguration Pattern { get; set; } = new();
    public List<CompletedPhase> CompletedPhases { get; set; } = new();
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Shared data dictionary for entity tracking, etc.
    /// </summary>
    public Dictionary<string, object> SharedData { get; set; } = new();

    /// <summary>
    /// Get previous phase (null if first)
    /// </summary>
    public CompletedPhase? GetPreviousPhase(int currentOrder)
    {
        return CompletedPhases.FirstOrDefault(p => p.Order == currentOrder - 1);
    }

    /// <summary>
    /// Get all previous content as summary
    /// </summary>
    public string GetPreviousContentSummary()
    {
        if (CompletedPhases.Count == 0) return string.Empty;

        var summary = CompletedPhases
            .OrderBy(p => p.Order)
            .Select(p => $"## {p.PhaseName}\n{p.Content.Substring(0, Math.Min(200, p.Content.Length))}...");

        return string.Join("\n\n", summary);
    }

    /// <summary>
    /// Get shared data value with type safety
    /// </summary>
    public T? GetSharedData<T>(string key) where T : class
    {
        if (SharedData.TryGetValue(key, out var value) && value is T typedValue)
            return typedValue;
        return default;
    }

    /// <summary>
    /// Set shared data value
    /// </summary>
    public void SetSharedData<T>(string key, T value) where T : class
    {
        SharedData[key] = value;
    }
}
