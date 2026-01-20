using BunbunBroll.Data;
using BunbunBroll.Models;
using System.Text.Json;
using Xunit;

namespace BunbunBroll.Tests.Data;

public class ProjectSentenceTests
{
    [Fact]
    public void ProjectSentence_CanSerializeKeywordSet()
    {
        var keywordSet = new KeywordSet
        {
            Primary = new List<string> { "person walking", "city street" },
            Mood = new List<string> { "happy", "energetic" },
            Contextual = new List<string> { "urban", "daytime" },
            Action = new List<string> { "walking" },
            Fallback = new List<string> { "city skyline" },
            SuggestedCategory = "Urban",
            DetectedMood = "Happy"
        };

        var projectSentence = new ProjectSentence
        {
            Text = "A person walks happily down the city street.",
            KeywordsJson = JsonSerializer.Serialize(keywordSet),
            SuggestedCategory = keywordSet.SuggestedCategory,
            DetectedMood = keywordSet.DetectedMood
        };

        Assert.NotNull(projectSentence.KeywordsJson);
        Assert.Contains("person walking", projectSentence.KeywordsJson);
        Assert.Equal("Urban", projectSentence.SuggestedCategory);
        Assert.Equal("Happy", projectSentence.DetectedMood);
    }

    [Fact]
    public void ProjectSentence_DeserializeKeywordSet()
    {
        var keywordSet = new KeywordSet
        {
            Primary = new List<string> { "test keyword" },
            SuggestedCategory = "Nature",
            DetectedMood = "Calm"
        };

        var json = JsonSerializer.Serialize(keywordSet);
        var projectSentence = new ProjectSentence
        {
            Text = "Test sentence",
            KeywordsJson = json
        };

        var deserialized = projectSentence.GetKeywordSet();

        Assert.NotNull(deserialized);
        Assert.Equal("test keyword", deserialized.Primary.First());
        Assert.Equal("Nature", deserialized.SuggestedCategory);
        Assert.Equal("Calm", deserialized.DetectedMood);
    }

    [Fact]
    public void ProjectSentence_GetKeywordSet_ReturnsEmpty_WhenJsonIsNull()
    {
        var projectSentence = new ProjectSentence
        {
            Text = "Test sentence"
        };

        var result = projectSentence.GetKeywordSet();

        Assert.NotNull(result);
        Assert.Empty(result.Primary);
        Assert.Empty(result.Mood);
    }
}
