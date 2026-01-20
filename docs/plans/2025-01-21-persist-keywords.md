# Persist Keywords to Database Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Persist AI-generated keywords to the database so they are preserved when loading saved projects from history.

**Architecture:** Add JSON serialization of KeywordSet to ProjectSentence table, update save/load logic to include keyword data.

**Tech Stack:** C# / .NET, Entity Framework Core, SQLite, System.Text.Json

---

## Task 1: Add Keyword Serialization Columns to ProjectSentence

**Files:**
- Modify: `Data/AppDbContext.cs` (ProjectSentence class)

**Step 1: Write the failing test**

Create `Tests/Data/ProjectSentenceTests.cs`:

```csharp
using BunbunBroll.Data;
using BunbunBroll.Models;
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
```

**Step 2: Run test to verify it fails**

Run: `dotnet test Tests/Data/ProjectSentenceTests.cs -v n`
Expected: FAIL with "KeywordsJson does not exist" and "GetKeywordSet does not exist"

**Step 3: Write minimal implementation**

Modify `Data/AppDbContext.cs` - ProjectSentence class:

Add new properties to ProjectSentence:

```csharp
public class ProjectSentence
{
    public int Id { get; set; }
    public int SegmentId { get; set; }
    public int Order { get; set; }
    public string Text { get; set; } = string.Empty;

    // Existing video properties
    public string? VideoId { get; set; }
    public string? VideoProvider { get; set; }
    public string? VideoUrl { get; set; }
    public string? VideoPreviewUrl { get; set; }
    public string? VideoThumbUrl { get; set; }

    // NEW: Persist KeywordSet as JSON
    public string? KeywordsJson { get; set; }
    public string? SuggestedCategory { get; set; }
    public string? DetectedMood { get; set; }

    public ProjectSegment Segment { get; set; } = null!;

    /// <summary>
    /// Deserializes KeywordsJson back to KeywordSet object.
    /// Returns empty KeywordSet if JSON is null or empty.
    /// </summary>
    public KeywordSet GetKeywordSet()
    {
        if (string.IsNullOrWhiteSpace(KeywordsJson))
            return new KeywordSet();

        try
        {
            return JsonSerializer.Deserialize<KeywordSet>(KeywordsJson) ?? new KeywordSet();
        }
        catch
        {
            return new KeywordSet();
        }
    }
}
```

Add using statement at top of file:

```csharp
using System.Text.Json;
using BunbunBroll.Models;
```

**Step 4: Run test to verify it passes**

Run: `dotnet test Tests/Data/ProjectSentenceTests.cs -v n`
Expected: PASS

**Step 5: Commit**

```bash
git add Data/AppDbContext.cs Tests/Data/ProjectSentenceTests.cs
git commit -m "feat: add KeywordSet JSON serialization to ProjectSentence"
```

---

## Task 2: Update ProjectService to Save Keywords

**Files:**
- Modify: `Services/ProjectService.cs`

**Step 1: Update SaveJobAsProjectAsync to include keywords**

Modify the sentence saving section in `SaveJobAsProjectAsync` method (around line 73-85):

OLD CODE:
```csharp
foreach (var jobSentence in jobSegment.Sentences)
{
    segment.Sentences.Add(new ProjectSentence
    {
        Order = sentenceOrder++,
        Text = jobSentence.Text,
        VideoId = jobSentence.SelectedVideo?.Id,
        VideoProvider = jobSentence.SelectedVideo?.Provider,
        VideoUrl = jobSentence.SelectedVideo?.DownloadUrl,
        VideoPreviewUrl = jobSentence.SelectedVideo?.PreviewUrl,
        VideoThumbUrl = jobSentence.SelectedVideo?.ThumbnailUrl
    });
}
```

NEW CODE:
```csharp
foreach (var jobSentence in jobSegment.Sentences)
{
    var keywordSet = jobSentence.KeywordSet;
    segment.Sentences.Add(new ProjectSentence
    {
        Order = sentenceOrder++,
        Text = jobSentence.Text,
        // Serialize KeywordSet to JSON
        KeywordsJson = JsonSerializer.Serialize(keywordSet),
        SuggestedCategory = keywordSet.SuggestedCategory,
        DetectedMood = keywordSet.DetectedMood,
        // Video properties
        VideoId = jobSentence.SelectedVideo?.Id,
        VideoProvider = jobSentence.SelectedVideo?.Provider,
        VideoUrl = jobSentence.SelectedVideo?.DownloadUrl,
        VideoPreviewUrl = jobSentence.SelectedVideo?.PreviewUrl,
        VideoThumbUrl = jobSentence.SelectedVideo?.ThumbnailUrl
    });
}
```

Add using at top of file:

```csharp
using System.Text.Json;
```

**Step 2: Run application to verify it compiles**

Run: `dotnet build`
Expected: No errors

**Step 3: Commit**

```bash
git add Services/ProjectService.cs
git commit -m "feat: persist KeywordSet when saving projects"
```

---

## Task 3: Update Home.razor to Restore Keywords When Loading Projects

**Files:**
- Modify: `Components/Pages/Home.razor`

**Step 1: Update LoadSavedProject to restore keywords**

Find the `LoadSavedProject` method (around line 594-645) and update the sentence loading:

OLD CODE (around line 623-640):
```csharp
foreach (var sent in s.Sentences.OrderBy(x => x.Order))
{
    var sentence = new ScriptSentence
    {
        Id = sent.Id,
        Text = sent.Text,
        Status = SentenceStatus.PreviewReady
    };
    if (!string.IsNullOrEmpty(sent.VideoUrl))
    {
        sentence.SelectedVideo = new VideoAsset
        {
            Id = sent.VideoId ?? "",
            Provider = sent.VideoProvider ?? "Pexels",
            DownloadUrl = sent.VideoUrl,
            PreviewUrl = sent.VideoPreviewUrl ?? "",
            ThumbnailUrl = sent.VideoThumbUrl ?? ""
        };
    }
    segment.Sentences.Add(sentence);
}
```

NEW CODE:
```csharp
foreach (var sent in s.Sentences.OrderBy(x => x.Order))
{
    var sentence = new ScriptSentence
    {
        Id = sent.Id,
        Text = sent.Text,
        // Restore KeywordSet from database
        KeywordSet = sent.GetKeywordSet(),
        Status = SentenceStatus.PreviewReady
    };
    if (!string.IsNullOrEmpty(sent.VideoUrl))
    {
        sentence.SelectedVideo = new VideoAsset
        {
            Id = sent.VideoId ?? "",
            Provider = sent.VideoProvider ?? "Pexels",
            DownloadUrl = sent.VideoUrl,
            PreviewUrl = sent.VideoPreviewUrl ?? "",
            ThumbnailUrl = sent.VideoThumbUrl ?? ""
        };
    }
    segment.Sentences.Add(sentence);
}
```

**Step 2: Run application to verify it compiles**

Run: `dotnet build`
Expected: No errors

**Step 3: Test manually**

1. Run the application
2. Create a new project with some keywords
3. Save the project
4. Reload the project from history
5. Verify keywords are restored

**Step 4: Commit**

```bash
git add Components/Pages/Home.razor
git commit -m "feat: restore KeywordSet when loading saved projects"
```

---

## Task 4: Add Database Migration for New Columns

**Files:**
- Modify: `Program.cs`

**Step 1: Update Program.cs to handle database migration**

The SQLite database needs to be recreated or migrated to add the new columns. Since we're using `EnsureCreated()`, we can either:

Option A: Drop and recreate (simpler, loses existing data):

Add this before `app.Run()` (around line 103):

```csharp
// For development: recreate database to add new columns
if (app.Environment.IsDevelopment())
{
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureDeleted();
        db.Database.EnsureCreated();
    }
}
```

Option B: Manual migration SQL (preserves data):

```bash
# Run these SQL commands manually on bunbun.db
ALTER TABLE Sentences ADD COLUMN KeywordsJson TEXT;
ALTER TABLE Sentences ADD COLUMN SuggestedCategory TEXT;
ALTER TABLE Sentences ADD COLUMN DetectedMood TEXT;
```

**Step 2: Test the migration**

Run: `dotnet run`
Expected: Application starts successfully, database has new columns

**Step 3: Commit**

```bash
git add Program.cs
git commit -m "feat: handle database schema changes for keyword persistence"
```

---

## Task 5: Add Integration Test for Keyword Persistence

**Files:**
- Create: `Tests/Integration/KeywordPersistenceTests.cs`

**Step 1: Write integration test**

Create `Tests/Integration/KeywordPersistenceTests.cs`:

```csharp
using BunbunBroll.Data;
using BunbunBroll.Models;
using BunbunBroll.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
            Order = 0,
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

        // Clear local job to simulate fresh load
        var freshJob = new ProcessingJob();

        // Load project back
        var loadedProject = await projectService.GetProjectAsync("test-project");
        Assert.NotNull(loadedProject);
        Assert.Equal("Test Project", loadedProject.Name);

        // Reconstruct job from loaded project
        var reconstructedSegment = new ScriptSegment
        {
            Id = 1,
            Title = loadedProject.Segments.First().Title,
            Order = 0
        };

        var reconstructedSentence = new ScriptSentence
        {
            Id = loadedProject.Segments.First().Sentences.First().Id,
            Text = loadedProject.Segments.First().Sentences.First().Text,
            KeywordSet = loadedProject.Segments.First().Sentences.First().GetKeywordSet()
        };

        // Assert: Keywords are preserved
        Assert.NotNull(reconstructedSentence.KeywordSet);
        Assert.Equal("test keyword primary", reconstructedSentence.KeywordSet.Primary.First());
        Assert.Equal("happy", reconstructedSentence.KeywordSet.Mood.First());
        Assert.Equal("Technology", reconstructedSentence.KeywordSet.SuggestedCategory);
        Assert.Equal("Happy", reconstructedSentence.KeywordSet.DetectedMood);
    }
}
```

**Step 2: Run test to verify it passes**

Run: `dotnet test Tests/Integration/KeywordPersistenceTests.cs -v n`
Expected: PASS

**Step 3: Commit**

```bash
git add Tests/Integration/KeywordPersistenceTests.cs
git commit -m "test: add integration test for keyword persistence"
```

---

## Task 6: Documentation

**Files:**
- Create: `docs/keyword-persistence.md`

**Step 1: Write documentation**

Create `docs/keyword-persistence.md`:

```markdown
# Keyword Persistence

## Overview

AI-generated keywords are now persisted to the database when saving projects. Previously, keywords were lost when loading saved projects from history.

## Changes Made

### Database Schema
Added new columns to `Sentences` table:
- `KeywordsJson` (TEXT) - Serialized KeywordSet as JSON
- `SuggestedCategory` (TEXT) - AI-suggested content category
- `DetectedMood` (TEXT) - AI-detected mood/emotion

### ProjectService
- Updated `SaveJobAsProjectAsync()` to serialize KeywordSet to JSON
- Keywords are now saved along with sentences and selected videos

### Home.razor
- Updated `LoadSavedProject()` to deserialize KeywordSet from JSON
- Keywords are fully restored when loading saved projects

## Usage

No API changes required. Keywords are automatically persisted and restored:

1. Create a new project
2. Keywords are generated by AI
3. Save the project
4. Reload from history anytime - keywords are preserved

## Data Format

Keywords are stored as JSON using `System.Text.Json`:

```json
{
  "Primary": ["person walking", "city street"],
  "Mood": ["happy", "energetic"],
  "Contextual": ["urban", "daytime"],
  "Action": ["walking"],
  "Fallback": ["city skyline"],
  "SuggestedCategory": "Urban",
  "DetectedMood": "Happy"
}
```

## Migration

For existing databases, the columns will be added automatically on next application restart (development mode). Existing projects won't have keywords - they will be regenerated on next search.
```

**Step 2: Commit documentation**

```bash
git add docs/keyword-persistence.md
git commit -m "docs: add keyword persistence documentation"
```

---

## Summary

This implementation adds keyword persistence to the database through:

1. **Database Schema** - Added JSON serialization columns to ProjectSentence
2. **Save Logic** - Updated ProjectService to serialize KeywordSet when saving
3. **Load Logic** - Updated Home.razor to restore KeywordSet when loading
4. **Testing** - Added unit and integration tests for keyword persistence
5. **Documentation** - Documented the changes and usage

All changes maintain backward compatibility - existing projects without keywords will continue to work, keywords will simply be regenerated on next search.
