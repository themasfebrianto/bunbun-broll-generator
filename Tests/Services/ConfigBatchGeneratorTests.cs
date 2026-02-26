using System.Text.Json;
using System.Text.RegularExpressions;
using BunbunBroll.Models;
using BunbunBroll.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BunbunBroll.Tests.Services;

public class ConfigBatchGeneratorTests
{
    private readonly Mock<IIntelligenceService> _mockIntelligenceService;
    private readonly Mock<ILogger<ConfigBatchGenerator>> _mockLogger;
    private readonly ConfigBatchGenerator _generator;

    public ConfigBatchGeneratorTests()
    {
        _mockIntelligenceService = new Mock<IIntelligenceService>();
        _mockLogger = new Mock<ILogger<ConfigBatchGenerator>>();
        _generator = new ConfigBatchGenerator(_mockIntelligenceService.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task GenerateConfigsAsync_RotatesExampleTopics_And_Formulas()
    {
        // Arrange
        var pattern = new ScriptPattern
        {
            Name = "TestPattern",
            Configuration = new PatternConfiguration
            {
                ExampleTopics = new List<string> { "Topic A", "Topic B" },
            }
        };

        var promptsReceived = new List<string>();

        // Mock LLM to return valid JSON config, while capturing the prompt
        _mockIntelligenceService
            .Setup(s => s.GenerateContentAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<double>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, int, double, CancellationToken>((sys, user, maxTok, temp, ct) => 
            {
                promptsReceived.Add(user);
            })
            .ReturnsAsync(() => 
            {
                var dummyResponse = new GeneratedConfig
                {
                    Topic = "Generated Topic",
                    TargetDurationMinutes = 20,
                    ChannelName = "TestChannel"
                };
                return JsonSerializer.Serialize(dummyResponse);
            });

        // Act
        var configs = await _generator.GenerateConfigsAsync("test theme", "TestChannel", 3, pattern);

        // Assert
        Assert.Equal(3, configs.Count);
        Assert.Equal(3, promptsReceived.Count);

        // Check assigned topic per iteration (modulo 2)
        Assert.Contains("DEVELOP video dari topik ini secara spesifik: 'Topic A'", promptsReceived[0]);
        Assert.Contains("DEVELOP video dari topik ini secara spesifik: 'Topic B'", promptsReceived[1]);
        Assert.Contains("DEVELOP video dari topik ini secara spesifik: 'Topic A'", promptsReceived[2]); // Wraps around

        // Check assigned formula per iteration
        Assert.Contains("Formula 1: Angka + Subjek + yang Bisa/Mungkin + Konsekuensi", promptsReceived[0]);
        Assert.Contains("Formula 2: Durasi + Kata Kerja Memahami + Kenapa + Subjek + Kata Kunci Emosional", promptsReceived[1]);
        Assert.Contains("Formula 3: Beginilah Nasib/Keadaan + [Tempat/Orang] Setelah + [X Tahun/Kejadian]", promptsReceived[2]);
    }

    [Fact]
    public async Task GenerateConfigsAsync_NoExampleTopics_UsesThemeDirectly()
    {
        // Arrange
        var pattern = new ScriptPattern
        {
            Name = "TestPattern",
            Configuration = new PatternConfiguration() // Empty ExampleTopics
        };

        var promptsReceived = new List<string>();

        _mockIntelligenceService
            .Setup(s => s.GenerateContentAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<double>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, int, double, CancellationToken>((sys, user, maxTok, temp, ct) => 
            {
                promptsReceived.Add(user);
            })
            .ReturnsAsync(() => 
            {
                var dummyResponse = new GeneratedConfig { Topic = "Generated", TargetDurationMinutes = 20 };
                return JsonSerializer.Serialize(dummyResponse);
            });

        // Act
        var configs = await _generator.GenerateConfigsAsync("test theme", "TestChannel", 1, pattern);

        // Assert
        Assert.Single(configs);
        Assert.Single(promptsReceived);

        // Does NOT contain specific topic assignment, only theme
        Assert.DoesNotContain("DEVELOP video dari topik ini secara spesifik", promptsReceived[0]);
        Assert.Contains("Theme: 'test theme'", promptsReceived[0]);
        Assert.Contains("Formula 1: Angka + Subjek", promptsReceived[0]);
    }
}
