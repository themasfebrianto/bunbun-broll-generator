using BunbunBroll.Data;
using BunbunBroll.Models;
using BunbunBroll.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace BunbunBroll.Tests.Integration;

public class KeywordPersistenceTests
{
    [Fact]
    public async Task SaveAndLoad_Project_KeywordSetIsPreserved()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var projectService = new ProjectService(db);

        var job = new ProcessingJob
        {
            Id = "test-project",
            ProjectName = "Test Project",
            RawScript = "This is a test script for keyword persistence.",
            Mood = "Cinematic"
        };

        job.Segments.Add(new ScriptSegment
        {
            Id = 1,
            Title = "Test Segment",
            Sentences = new List<ScriptSentence>
            {
                new ScriptSentence
                {
                    Id = 1,
                    Text = "This is a test sentence with generated keywords.",
                    KeywordSet = new KeywordSet
                    {
                        Primary = new List<string> { "test keyword primary", "main subject" },
                        Mood = new List<string> { "happy", "bright" },
                        Contextual = new List<string> { "outdoor", "daytime" },
                        Action = new List<string> { "testing" },
                        Fallback = new List<string> { "generic footage" },
                        SuggestedCategory = "Technology",
                        DetectedMood = "Happy"
                    }
                }
            }
        });

        // Act: Save project
        await projectService.SaveJobAsProjectAsync(job);

        // Load project back
        var loadedProject = await projectService.GetProjectAsync("test-project");
        Assert.NotNull(loadedProject);
        Assert.Equal("Test Project", loadedProject.Name);

        var loadedSentence = loadedProject.Segments.First().Sentences.First();
        var reconstructedKeywordSet = loadedSentence.GetKeywordSet();

        // Assert: Keywords are preserved
        Assert.NotNull(reconstructedKeywordSet);
        Assert.Equal("test keyword primary", reconstructedKeywordSet.Primary.First());
        Assert.Equal("happy", reconstructedKeywordSet.Mood.First());
        Assert.Equal("Technology", reconstructedKeywordSet.SuggestedCategory);
        Assert.Equal("Happy", reconstructedKeywordSet.DetectedMood);
    }
}
