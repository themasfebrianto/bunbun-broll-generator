using System.Text.Json.Serialization;

namespace BunbunBroll.Models;

public class GlobalRules
{
    [JsonPropertyName("tone")]
    public string Tone { get; set; } = string.Empty;

    [JsonPropertyName("voice")]
    public string? Voice { get; set; }

    [JsonPropertyName("perspective")]
    public string? Perspective { get; set; }

    [JsonPropertyName("structure")]
    public string? Structure { get; set; }

    [JsonPropertyName("vocabulary")]
    public string? Vocabulary { get; set; }

    [JsonPropertyName("narrativeStructure")]
    public string? NarrativeStructure { get; set; }

    [JsonPropertyName("intellectualSurprise")]
    public string? IntellectualSurprise { get; set; }

    [JsonPropertyName("language")]
    public string Language { get; set; } = string.Empty;

    [JsonPropertyName("wordsPerMinute")]
    public string? WordsPerMinute { get; set; }

    [JsonPropertyName("targetDuration")]
    public string? TargetDuration { get; set; }

    [JsonPropertyName("honorificsRequired")]
    public string? HonorificsRequired { get; set; }

    [JsonPropertyName("maxWordsPerSentence")]
    public string? MaxWordsPerSentence { get; set; }

    [JsonPropertyName("preferredWordsPerSentence")]
    public string? PreferredWordsPerSentence { get; set; }

    [JsonPropertyName("mustUseKeywords")]
    public string? MustUseKeywords { get; set; }

    [JsonExtensionData]
    public Dictionary<string, object> AdditionalRules { get; set; } = new();
}
