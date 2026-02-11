using System.Text.Json.Serialization;

namespace BunbunBroll.Models;

public class GlobalRules
{
    [JsonPropertyName("tone")]
    public string Tone { get; set; } = string.Empty;

    [JsonPropertyName("perspective")]
    public string? Perspective { get; set; }

    [JsonPropertyName("structure")]
    public string? Structure { get; set; }

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
}
