using BunbunBroll.Models;
using BunbunBroll.Services;
using Moq;
using Moq.Protected;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Tests.Services.Intelligence;

public class DramaDetectionTests : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly Mock<HttpMessageHandler> _handlerMock;
    private readonly IIntelligenceService _service;
    private readonly ILogger<IntelligenceService> _logger;

    public DramaDetectionTests()
    {
        _handlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_handlerMock.Object)
        {
            BaseAddress = new Uri("http://test")
        };

        var loggerFactory = LoggerFactory.Create(builder => { });
        _logger = loggerFactory.CreateLogger<IntelligenceService>();

        var settings = Options.Create(new GeminiSettings
        {
            BaseUrl = "http://test",
            Model = "test-model"
        });

        var routerMock = new Mock<ILlmRouterService>();
        routerMock.Setup(r => r.GetModel(It.IsAny<bool>())).Returns("test-model");

        _service = new IntelligenceService(_httpClient, _logger, settings, routerMock.Object);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    [Fact]
    public async Task DetectDramaAsync_ReturnsPausesAndOverlays()
    {
        // Arrange
        var entries = new List<(int Index, string Text)>
        {
            (0, "sejarah umat manusia"),
            (1, "seringkali mencatat kemenangan gemilang"),
            (2, "namun realita historis")  // Drama trigger
        };

        var llmResponse = new GeminiChatResponse
        {
            Choices = new List<GeminiChoice>
            {
                new()
                {
                    Message = new GeminiMessage
                    {
                        Content = @"{
                            ""pauseDurations"": {
                                ""1"": 1.5
                            },
                            ""textOverlays"": {
                                ""2"": {
                                    ""type"": ""key_phrase"",
                                    ""text"": ""namun realita historis""
                                }
                            }
                        }"
                    }
                }
            },
            Usage = new GeminiUsage { TotalTokens = 100 }
        };

        _handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = JsonContent.Create(llmResponse)
            });

        // Act
        var result = await _service.DetectDramaAsync(entries);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Null(result.ErrorMessage);
        Assert.Single(result.PauseDurations);
        Assert.Equal(1.5, result.PauseDurations[1]);
        Assert.Empty(result.TextOverlays); // Should be empty now that it's moved to regex
        Assert.Equal(100, result.TokensUsed);
    }

    [Fact]
    public async Task DetectDramaAsync_HandlesEmptyResponse()
    {
        // Arrange
        var entries = new List<(int Index, string Text)> { (0, "test") };

        var llmResponse = new GeminiChatResponse
        {
            Choices = new List<GeminiChoice>
            {
                new() { Message = new GeminiMessage { Content = "" } }
            }
        };

        _handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = JsonContent.Create(llmResponse)
            });

        // Act
        var result = await _service.DetectDramaAsync(entries);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("LLM returned empty response", result.ErrorMessage);
    }

    [Fact]
    public async Task DetectDramaAsync_HandlesInvalidJson()
    {
        // Arrange
        var entries = new List<(int Index, string Text)> { (0, "test") };

        _handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("not valid json")
            });

        // Act
        var result = await _service.DetectDramaAsync(entries);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Failed to parse", result.ErrorMessage);
    }

    [Fact]
    public async Task DetectDramaAsync_HandlesNetworkError()
    {
        // Arrange
        var entries = new List<(int Index, string Text)> { (0, "test") };

        _handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection timeout"));

        // Act
        var result = await _service.DetectDramaAsync(entries);

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("Drama detection failed", result.ErrorMessage);
    }

    [Fact]
    public async Task DetectDramaAsync_EmptyEntries_ReturnsError()
    {
        // Act
        var result = await _service.DetectDramaAsync(Enumerable.Empty<(int, string)>());

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Equal("No entries provided for drama detection", result.ErrorMessage);
    }
}
