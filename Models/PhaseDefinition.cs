using System.Text.Json.Serialization;

namespace BunbunBroll.Models;

public class PhaseDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("order")]
    public int Order { get; set; }

    [JsonPropertyName("durationTarget")]
    public DurationTarget DurationTarget { get; set; } = new();

    [JsonPropertyName("wordCountTarget")]
    public WordCountTarget WordCountTarget { get; set; } = new();

    [JsonPropertyName("requiredElements")]
    public List<string> RequiredElements { get; set; } = new();

    [JsonPropertyName("forbiddenPatterns")]
    public List<string> ForbiddenPatterns { get; set; } = new();

    [JsonPropertyName("guidanceTemplate")]
    public string GuidanceTemplate { get; set; } = string.Empty;

    [JsonPropertyName("transitionHint")]
    public string? TransitionHint { get; set; }

    [JsonPropertyName("customRules")]
    public Dictionary<string, string> CustomRules { get; set; } = new();

    [JsonPropertyName("emotionalArc")]
    public string? EmotionalArc { get; set; }

    /// <summary>
    /// Whether this is the first phase
    /// </summary>
    [JsonIgnore]
    public bool IsFirstPhase => Order == 1;

    /// <summary>
    /// Whether this is the final phase (set externally by knowing total count)
    /// </summary>
    [JsonIgnore]
    public bool IsFinalPhase { get; set; }
}
