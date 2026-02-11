# Script Generation & Session Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Integrate ScriptFlow's pattern-based script generation into BunBun Broll using Gemini Proxy LLM, database persistence, and separate UI page.

**Architecture:** Blazor Server (.NET 8) | SQLite Database | JSON Pattern Files | Existing IntelligenceService Integration

**Tech Stack:**
- .NET 8.0 / C#
- Blazor Server (InteractiveServer)
- Entity Framework Core (SQLite)
- System.Text.Json (pattern loading)
- Gemini Proxy LLM (existing)

---

## Task Structure

### Task 1: Create Pattern Configuration Models

**Files:**
- Create: `Models/ScriptPattern.cs`
- Create: `Models/PatternConfiguration.cs`
- Create: `Models/PhaseDefinition.cs`
- Create: `Models/GlobalRules.cs`
- Create: `Models/ProductionChecklist.cs`
- Create: `Models/DurationTarget.cs`
- Create: `Models/WordCountTarget.cs`
- Create: `Models/ScriptConfig.cs`

**Step 1: Write minimal test for PatternConfiguration deserialization**

Create `Tests/Models/PatternConfigurationTests.cs`:

```csharp
using BunbunBroll.Models;
using Xunit;

namespace BunbunBroll.Tests.Models;

public class PatternConfigurationTests
{
    [Fact]
    public void Deserialize_JazirahIlmuJson_CreatesValidConfiguration()
    {
        // Arrange
        var json = File.ReadAllText("patterns/jazirah-ilmu.json");
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        // Act
        var config = JsonSerializer.Deserialize<PatternConfiguration>(json, options);

        // Assert
        Assert.NotNull(config);
        Assert.Equal("jazirah-ilmu", config.Name);
        Assert.Equal(5, config.Phases.Count);
        Assert.NotNull(config.GlobalRules);
    }

    [Fact]
    public void GetOrderedPhases_ReturnsPhasesInOrder()
    {
        // Arrange
        var config = new PatternConfiguration
        {
            Phases = new List<PhaseDefinition>
            {
                new() { Id = "03_multi-dimensi", Order = 3 },
                new() { Id = "01_opening-hook", Order = 1 },
                new() { Id = "05_eschatology", Order = 5 }
            }
        };

        // Act
        var ordered = config.GetOrderedPhases().ToList();

        // Assert
        Assert.Equal(3, ordered[0].Order);
        Assert.Equal("01_opening-hook", ordered[0].Id);
        Assert.Equal(5, ordered[2].Order);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test Tests/Models/PatternConfigurationTests.cs -v n`
Expected: FAIL with "PatternConfiguration does not exist"

**Step 3: Implement PatternConfiguration model**

Create `Models/PatternConfiguration.cs`:

```csharp
using System.Text.Json.Serialization;

namespace BunbunBroll.Models;

/// <summary>
/// Pattern configuration loaded from JSON file
/// Defines phases, rules, and guidance for script generation
/// </summary>
public class PatternConfiguration
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("phases")]
    public List<PhaseDefinition> Phases { get; set; } = new();

    [JsonPropertyName("globalRules")]
    public GlobalRules GlobalRules { get; set; } = new();

    [JsonPropertyName("closingFormula")]
    public string ClosingFormula { get; set; } = string.Empty;

    [JsonPropertyName("productionChecklist")]
    public ProductionChecklist ProductionChecklist { get; set; } = new();

    /// <summary>
    /// Get phases ordered by their sequence
    /// </summary>
    public IEnumerable<PhaseDefinition> GetOrderedPhases()
        => Phases.OrderBy(p => p.Order);
}
```

**Step 4: Implement supporting models**

Create the remaining model files with full properties matching your pattern JSON structure.

**Step 5: Run tests to verify they pass**

Run: `dotnet test Tests/Models/`
Expected: PASS

**Step 6: Commit**

```bash
git add Models/ScriptPattern.cs Models/PatternConfiguration.cs Models/PhaseDefinition.cs Models/GlobalRules.cs Models/ProductionChecklist.cs Models/DurationTarget.cs Models/WordCountTarget.cs Models/ScriptConfig.cs Tests/Models/PatternConfigurationTests.cs
git commit -m "feat: add pattern configuration models"
```

---

### Task 2: Create Session Models

**Files:**
- Create: `Models/ScriptGenerationSession.cs`
- Create: `Models/ScriptGenerationPhase.cs`
- Create: `Models/SessionStatus.cs`
- Create: `Models/PhaseStatus.cs`
- Modify: `Data/AppDbContext.cs` - Add DbSets

**Step 1: Write failing test for session creation**

Create `Tests/Models/ScriptGenerationSessionTests.cs`:

```csharp
using BunbunBroll.Models;
using Xunit;

namespace BunbunBroll.Tests.Models;

public class ScriptGenerationSessionTests
{
    [Fact]
    public void NewSession_HasPendingStatus()
    {
        var session = new ScriptGenerationSession();
        Assert.Equal(SessionStatus.Pending, session.Status);
    }

    [Fact]
    public void CompleteSession_SetsCompletedAt()
    {
        var session = new ScriptGenerationSession();
        session.Status = SessionStatus.Completed;
        Assert.Null(session.CompletedAt);

        // This would be set by service layer
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test Tests/Models/ScriptGenerationSessionTests.cs -v n`
Expected: FAIL with "ScriptGenerationSession does not exist"

**Step 3: Implement ScriptGenerationSession model**

Create `Models/ScriptGenerationSession.cs` with all properties from design.

**Step 4: Implement ScriptGenerationPhase model**

Create `Models/ScriptGenerationPhase.cs` with all properties from design.

**Step 5: Create enums**

Create `Models/SessionStatus.cs` and `Models/PhaseStatus.cs`.

**Step 6: Update AppDbContext**

Modify `Data/AppDbContext.cs`:

```csharp
// Add these DbSets:
public DbSet<ScriptPattern> ScriptPatterns { get; set; }
public DbSet<ScriptGenerationSession> ScriptGenerationSessions { get; set; }
public DbSet<ScriptGenerationPhase> ScriptGenerationPhases { get; set; }

// Add to OnModelCreating:
builder.Entity<ScriptPattern>(entity =>
{
    entity.HasKey(e => e.Id);
    entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
    entity.Property(e => e.Description).HasMaxLength(500);
});

builder.Entity<ScriptGenerationSession>(entity =>
{
    entity.HasKey(e => e.Id);
    entity.Property(e => e.Topic).IsRequired().HasMaxLength(500);
    entity.Property(e => e.PatternId).IsRequired().HasMaxLength(100);
    entity.HasMany(e => e.Phases).WithOne().HasForeignKey(p => p.SessionId);
});

builder.Entity<ScriptGenerationPhase>(entity =>
{
    entity.HasKey(e => e.Id);
    entity.Property(e => e.PhaseId).IsRequired();
    entity.HasIndex(e => e.SessionId);
});
```

**Step 7: Create and apply migration**

Run: `dotnet ef migrations add AddScriptGeneration`

**Step 8: Run tests to verify they pass**

Run: `dotnet test Tests/Models/ScriptGenerationSessionTests.cs -v n`
Expected: PASS

**Step 9: Commit**

```bash
git add Models/ScriptGenerationSession.cs Models/ScriptGenerationPhase.cs Models/SessionStatus.cs Models/PhaseStatus.cs Data/AppDbContext.cs Migrations/ Tests/Models/ScriptGenerationSessionTests.cs
git commit -m "feat: add script generation session models with database persistence"
```

---

### Task 3: Implement Pattern Registry

**Files:**
- Create: `Services/IPatternRegistry.cs`
- Create: `Services/PatternRegistry.cs`
- Modify: `Program.cs` - Add DI registration

**Step 1: Write failing test for pattern loading**

Create `Tests/Services/PatternRegistryTests.cs`:

```csharp
using BunbunBroll.Services;
using Xunit;

namespace BunbunBroll.Tests.Services;

public class PatternRegistryTests
{
    [Fact]
    public void LoadFromDirectory_LoadsAllJsonFiles()
    {
        // This requires a test patterns directory
        var registry = new PatternRegistry();
        registry.LoadFromDirectory("patterns/test");
        Assert.Equal(2, registry.ListPatterns().Count());
    }

    [Fact]
    public void GetPattern_ReturnsCorrectConfiguration()
    {
        var registry = new PatternRegistry();
        registry.Register("test", new PatternConfiguration { Name = "Test" });
        var result = registry.Get("test");
        Assert.Equal("Test", result?.Name);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test Tests/Services/PatternRegistryTests.cs -v n`
Expected: FAIL with "PatternRegistry does not exist"

**Step 3: Implement IPatternRegistry interface**

Create `Services/IPatternRegistry.cs`:

```csharp
namespace BunbunBroll.Services;

/// <summary>
/// Pattern discovery and management service
/// Loads pattern configurations from JSON files
/// </summary>
public interface IPatternRegistry
{
    /// <summary>
    /// Register a pattern configuration
    /// </summary>
    void Register(string id, PatternConfiguration config);

    /// <summary>
    /// Get pattern by ID
    /// </summary>
    PatternConfiguration? Get(string id);

    /// <summary>
    /// List all registered pattern IDs
    /// </summary>
    IEnumerable<string> ListPatterns();

    /// <summary>
    /// Check if pattern exists
    /// </summary>
    bool Exists(string id);

    /// <summary>
    /// Load patterns from directory
    /// </summary>
    void LoadFromDirectory(string directory);
}
```

**Step 4: Implement PatternRegistry service**

Create `Services/PatternRegistry.cs` with full implementation from design.

**Step 5: Create test patterns directory**

Create `patterns/test/` directory with `test-pattern.json` for testing.

**Step 6: Update Program.cs for DI**

Modify `Program.cs`:

```csharp
// Pattern Registry (Singleton)
builder.Services.AddSingleton<IPatternRegistry>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var patternsDir = config["Patterns:Directory"] ?? "patterns";
    var registry = new PatternRegistry();
    registry.LoadFromDirectory(patternsDir);
    return registry;
});
```

**Step 7: Run tests to verify they pass**

Run: `dotnet test Tests/Services/PatternRegistryTests.cs -v n`
Expected: PASS

**Step 8: Commit**

```bash
git add Services/IPatternRegistry.cs Services/PatternRegistry.cs Program.cs Tests/Services/PatternRegistryTests.cs
git commit -m "feat: add pattern registry service"
```

---

### Task 4: Implement Script Orchestrator

**Files:**
- Create: `Orchestration/IScriptOrchestrator.cs`
- Create: `Orchestration/ScriptOrchestrator.cs`
- Create: `Models/GenerationContext.cs`
- Create: `Models/PatternResult.cs`
- Create: `Models/GeneratedPhase.cs`
- Create: `Models/CompletedPhase.cs`
- Create: `Orchestration/Events/PhaseProgressEventArgs.cs`
- Create: `Orchestration/Events/SessionProgressEventArgs.cs`

**Step 1: Write test for orchestrator session initialization**

Create `Tests/Services/ScriptOrchestratorTests.cs`:

```csharp
using BunbunBroll.Orchestration;
using BunbunBroll.Models;
using Xunit;

namespace BunbunBroll.Tests.Services;

public class ScriptOrchestratorTests
{
    [Fact]
    public async Task InitializeSessionAsync_CreatesSessionWithPhases()
    {
        var orchestrator = new ScriptOrchestrator(null!, null!);
        var config = new ScriptConfig { Topic = "Test Topic", TargetDurationMinutes = 30 };
        var (session, context) = await orchestrator.InitializeSessionAsync(config, "jazirah-ilmu");

        Assert.NotNull(session);
        Assert.Equal(5, session.Phases.Count); // jazirah-ilmu has 5 phases
        Assert.Equal("jazirah-ilmu", session.PatternId);
    }
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test Tests/Services/ScriptOrchestratorTests.cs -v n`
Expected: FAIL with "ScriptOrchestrator does not exist"

**Step 3: Implement event args models**

Create event args models in `Models/Orchestration/Events/`.

**Step 4: Implement IScriptOrchestrator interface**

Create `Orchestration/IScriptOrchestrator.cs`:

```csharp
using BunbunBroll.Models;
using BunbunBroll.Orchestration.Events;

namespace BunbunBroll.Orchestration;

/// <summary>
/// Main coordinator for pattern-based script generation
/// Manages sessions, patterns, and coordinates generation phases
/// </summary>
public interface IScriptOrchestrator
{
    // Pattern management
    IEnumerable<string> ListPatterns();
    PatternConfiguration? GetPattern(string patternId);
    void RegisterPattern(string patternId, PatternConfiguration config);

    // Session lifecycle
    Task<(ScriptGenerationSession Session, GenerationContext Context)> InitializeSessionAsync(
        ScriptConfig config, string patternId, string? customId = null);
    Task<ScriptGenerationSession?> LoadSessionAsync(string sessionId);
    Task SaveSessionAsync(ScriptGenerationSession session);

    // Generation
    Task<PatternResult> GenerateAllAsync(string sessionId);
    Task<PatternResult> ResumeAsync(string sessionId);

    // Progress events
    event EventHandler<PhaseProgressEventArgs>? OnPhaseProgress;
    event EventHandler<SessionProgressEventArgs>? OnSessionProgress;
}
```

**Step 5: Implement ScriptOrchestrator service**

Create `Orchestration/ScriptOrchestrator.cs` with full implementation.

**Step 6: Wire up IntelligenceService for LLM calls**

In ScriptOrchestrator, use existing `IIntelligenceService` for prompt generation.

**Step 7: Run tests to verify they pass**

Run: `dotnet test Tests/Services/ScriptOrchestratorTests.cs -v n`
Expected: PASS

**Step 8: Commit**

```bash
git add Orchestration/IScriptOrchestrator.cs Orchestration/ScriptOrchestrator.cs Models/GenerationContext.cs Models/PatternResult.cs Models/GeneratedPhase.cs Models/CompletedPhase.cs Orchestration/Events/*.cs Tests/Services/ScriptOrchestratorTests.cs
git commit -m "feat: add script orchestrator with pattern-based generation"
```

---

### Task 5: Implement Script Generation Service

**Files:**
- Create: `Services/IScriptGenerationService.cs`
- Create: `Services/ScriptGenerationService.cs`

**Step 1: Write test for session listing**

Add to `Tests/Services/ScriptOrchestratorTests.cs`:

```csharp
[Fact]
public async Task ListSessionsAsync_ReturnsAllSessions()
{
    var service = new ScriptGenerationService(null!);
    var sessions = await service.ListSessionsAsync();
    Assert.NotNull(sessions);
}
```

**Step 2: Run test to verify it fails**

Run: `dotnet test Tests/Services/ScriptOrchestratorTests.cs -v n`
Expected: FAIL with method not implemented

**Step 3: Implement IScriptGenerationService interface**

Create `Services/IScriptGenerationService.cs`:

```csharp
using BunbunBroll.Models;

namespace BunbunBroll.Services;

/// <summary>
/// High-level service for script generation UI
/// Manages sessions, patterns, and export functionality
/// </summary>
public interface IScriptGenerationService
{
    /// <summary>
    /// Get all available patterns
    /// </summary>
    Task<List<ScriptPattern>> GetAvailablePatternsAsync();

    /// <summary>
    /// Create a new generation session
    /// </summary>
    Task<ScriptGenerationSession> CreateSessionAsync(
        string patternId, string topic, string? outline, int targetDuration);

    /// <summary>
    /// Generate script for a session
    /// </summary>
    Task<ScriptGenerationSession> GenerateAsync(string sessionId);

    /// <summary>
    /// Get session by ID
    /// </summary>
    Task<ScriptGenerationSession?> GetSessionAsync(string sessionId);

    /// <summary>
    /// List all sessions
    /// </summary>
    Task<List<ScriptGenerationSession>> ListSessionsAsync();

    /// <summary>
    /// Export completed script
    /// </summary>
    Task<string> ExportScriptAsync(string sessionId, bool clean = false);
}
```

**Step 4: Implement ScriptGenerationService**

Create `Services/ScriptGenerationService.cs` with full implementation using `IScriptOrchestrator`.

**Step 5: Add export functionality**

Implement `ExportScriptAsync` method to combine all phase files into complete script.

**Step 6: Run tests to verify they pass**

Run: `dotnet test Tests/Services/ScriptOrchestratorTests.cs -v n`
Expected: PASS

**Step 7: Commit**

```bash
git add Services/IScriptGenerationService.cs Services/ScriptGenerationService.cs Tests/Services/ScriptOrchestratorTests.cs
git commit -m "feat: add script generation service with export"
```

---

### Task 6: Create Script Generator UI Page

**Files:**
- Create: `Components/Pages/ScriptGenerator.razor`
- Modify: `Components/NavMenu.razor` - Add navigation item
- Create: `Services/IScriptGenerationService.cs` (if not created)
- Modify: `Program.cs` - Register services

**Step 1: Create basic page structure**

Create `Components/Pages/ScriptGenerator.razor` with:
- Page route `@page "/script-generator"`
- Service injections
- Basic layout matching Home.razor style

**Step 2: Implement pattern selection view**

Grid of pattern cards with descriptions.

**Step 3: Implement config form view**

Form with topic, outline, duration, source references inputs.

**Step 4: Implement progress display**

Phase list with status indicators and progress bar.

**Step 5: Implement complete state**

Success message with stats and action buttons.

**Step 6: Add navigation menu item**

Modify `Components/NavMenu.razor`:

```razor
<div class="nav-item">
    <NavLink href="script-generator" Match="NavLinkMatch.All">
        <span class="icon">📝</span>
        <span>Script Generator</span>
    </NavLink>
</div>
```

**Step 7: Register services in Program.cs**

```csharp
builder.Services.AddScoped<IScriptOrchestrator, ScriptOrchestrator>();
builder.Services.AddScoped<IScriptGenerationService, ScriptGenerationService>();
```

**Step 8: Test UI manually**

Run: `dotnet run` and navigate to `/script-generator`
Expected: Page loads, patterns display

**Step 9: Commit**

```bash
git add Components/Pages/ScriptGenerator.razor Components/NavMenu.razor Program.cs
git commit -m "feat: add script generator UI page"
```

---

### Task 7: Port Jazirah Ilmu Pattern

**Files:**
- Create: `patterns/jazirah-ilmu.json` (with full 5-phase configuration)

**Step 1: Create patterns directory**

```bash
mkdir -p patterns
```

**Step 2: Create jazirah-ilmu.json pattern**

Create the complete pattern JSON file with all 5 phases as shown in design document.

**Step 3: Validate pattern structure**

Ensure JSON structure matches `PatternConfiguration` model.

**Step 4: Test pattern loading**

Run: `dotnet run` and verify pattern loads from registry.

**Step 5: Commit**

```bash
git add patterns/jazirah-ilmu.json
git commit -m "feat: add jazirah-ilmu pattern with 5 phases"
```

---

### Task 8: Add Configuration Settings

**Files:**
- Modify: `appsettings.json` - Add patterns and script output sections
- Modify: `appsettings.Development.json`
- Modify: `appsettings.Production.json`

**Step 1: Update appsettings.json**

Add configuration sections:

```json
{
  // ... existing settings ...

  "Patterns": {
    "Directory": "patterns",
    "AutoReload": true
  },
  "ScriptOutput": {
    "BaseDirectory": "output/scripts",
    "ExportDirectory": "output/exports"
  }
}
```

**Step 2: Update environment-specific files**

Same additions to `appsettings.Development.json` and `appsettings.Production.json`.

**Step 3: Test configuration loading**

Run: `dotnet run` and verify configuration loads.

**Step 4: Commit**

```bash
git add appsettings.json appsettings.Development.json appsettings.Production.json
git commit -m "feat: add pattern and script output configuration"
```

---

### Task 9: Create Output Directories

**Files:**
- Create directory: `output/scripts/`
- Create directory: `output/exports/`

**Step 1: Create base directories**

```bash
mkdir -p output/scripts
mkdir -p output/exports
```

**Step 2: Add .gitkeep**

Create `.gitkeep` files to ensure empty directories are tracked.

**Step 3: Commit**

```bash
git add output/.gitkeep output/scripts/.gitkeep output/exports/.gitkeep
git commit -m "feat: add script output directories"
```

---

### Task 10: Write Integration Tests

**Files:**
- Create: `Tests/Integration/ScriptGenerationEndToEndTests.cs`

**Step 1: Write end-to-end generation test**

```csharp
using BunbunBroll.Services;
using Xunit;

namespace BunbunBroll.Tests.Integration;

public class ScriptGenerationEndToEndTests
{
    [Fact]
    public async Task FullGeneration_CompletesAllPhases()
    {
        // Arrange
        var service = new ScriptGenerationService(null!);
        var session = await service.CreateSessionAsync(
            "jazirah-ilmu", "Kisah Perang Hunain", null, 30);

        // Act
        var result = await service.GenerateAsync(session.Id);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(SessionStatus.Completed, session.Status);
        Assert.All(session.Phases, p => p.Status == PhaseStatus.Completed);
    }
}
```

**Step 2: Run test to verify it fails (requires full setup)**

Run: `dotnet test Tests/Integration/ScriptGenerationEndToEndTests.cs -v n`
Expected: May fail if setup incomplete

**Step 3: Commit**

```bash
git add Tests/Integration/ScriptGenerationEndToEndTests.cs
git commit -m "test: add script generation integration tests"
```

---

### Task 11: Update Documentation

**Files:**
- Create: `docs/script-generation.md`

**Step 1: Write feature documentation**

```markdown
# Script Generation Feature

Generate video essay scripts using pattern-based AI generation.

## Usage

1. Navigate to **Script Generator** page
2. Select a pattern (e.g., Jazirah Ilmu)
3. Enter topic and optional outline
4. Start generation
5. Export or send to B-roll workflow

## Patterns

Patterns are loaded from `patterns/` directory.

- `jazirah-ilmu.json` - 5 phases for Islamic eschatology content
```

**Step 2: Update README**

Modify `README.md` to mention new feature.

**Step 3: Commit**

```bash
git add docs/script-generation.md README.md
git commit -m "docs: add script generation documentation"
```

---

## Remember

- **Existing Integration:** Use `IIntelligenceService` for LLM calls
- **Database:** SQLite via existing `AppDbContext`
- **UI Style:** Match existing `Home.razor` patterns
- **Language:** Bahasa Indonesia for default pattern
- **Configuration:** JSON-based patterns in `patterns/` directory
- **Frequent Commits:** Commit after each task
- **Testing:** Write test first, run to verify it fails, then implement

---

## Execution Handoff

Plan complete and saved to `docs/plans/2026-02-11-script-generation-implementation.md`.

**Which execution approach?**

**1. Subagent-Driven (this session)** - Fresh subagent per task, review between tasks
**2. Parallel Session (separate)** - New session with executing-plans skill

Recommended: **Subagent-Driven** for faster iteration with code review checkpoints.
