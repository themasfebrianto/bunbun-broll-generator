using Xunit;
using BunbunBroll.Services;
using BunbunBroll.Models;
using System.Text.Json;

namespace BunbunBroll.Tests.Services;

public class PhaseBeatTemplateBuilderTests
{
    [Fact]
    public void BuildTemplatesFromPattern_ShouldCreateTemplateForEachPhase()
    {
        // Arrange
        var patternJson = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "patterns", "jazirah-ilmu.json"));
        var pattern = JsonSerializer.Deserialize<PatternConfiguration>(patternJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        var builder = new PhaseBeatTemplateBuilder();

        // Act
        var templates = builder.BuildTemplatesFromPattern(pattern!);

        // Assert
        Assert.Equal(5, templates.Count);

        var coldOpen = templates.First(t => t.PhaseId == "opening-hook");
        Assert.Contains("Misteri", string.Join(" ", coldOpen.RequiredElements));
        Assert.NotEmpty(coldOpen.BeatExamples);
    }

    [Fact]
    public void GetBeatPrompt_ShouldIncludeRequiredElementsAndExamples()
    {
        // Arrange
        var template = new PhaseBeatTemplate
        {
            PhaseName = "The Cold Open (Hook)",
            PhaseId = "opening-hook",
            RequiredElements = new List<string>
            {
                "Cold Open: Langsung masuk ke narasi",
                "Misteri: Bangun ketegangan"
            },
            BeatExamples = new List<string>
            {
                "Visual hening sebuah kamar gelap..."
            }
        };

        // Act
        var prompt = template.GetBeatPrompt();

        // Assert
        Assert.Contains("The Cold Open", prompt);
        Assert.Contains("Cold Open: Langsung masuk ke narasi", prompt);
        Assert.Contains("Visual hening sebuah kamar gelap", prompt);
    }
}
