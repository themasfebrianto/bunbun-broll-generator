using BunbunBroll.Models;
using BunbunBroll.Orchestration.Context;
using BunbunBroll.Orchestration.Generators;
using Xunit;

namespace BunbunBroll.Tests.Orchestration;

public class PromptBuilderTests
{
    [Fact]
    public void BuildPrompt_ShouldInclude_AuthoritativeGuidelines_FromGlobalRules()
    {
        // Arrange
        var builder = new PromptBuilder();
        var phase = new PhaseDefinition { Id = "test-phase", Name = "Test Phase", Order = 1 };
        
        var pattern = new ScriptPattern
        {
            Id = "test-pattern",
            Configuration = new PatternConfiguration
            {
                GlobalRules = new GlobalRules
                {
                    Tone = "Test Tone",
                    AdditionalRules = new Dictionary<string, object>
                    {
                        { "authoritativeGuidelines", "TEST: Sebutkan perbedaan pendapat ulama" },
                        { "referenceRequirement", "TEST: Wajib menyertakan referensi" }
                    }
                }
            }
        };

        var context = new GenerationContext
        {
            SessionId = "test-session",
            Config = new ScriptConfig { Topic = "Test Topic", TargetDurationMinutes = 5 },
            Pattern = pattern.Configuration
        };

        var phaseContext = new PhaseContext
        {
            Phase = phase
        };

        // Act
        var prompt = builder.BuildPrompt(phase, context, phaseContext);

        // Assert
        Assert.Contains("TEST: Sebutkan perbedaan pendapat ulama", prompt);
        Assert.Contains("TEST: Wajib menyertakan referensi", prompt);
        Assert.Contains("- **Tone**: Test Tone", prompt);
    }
}
