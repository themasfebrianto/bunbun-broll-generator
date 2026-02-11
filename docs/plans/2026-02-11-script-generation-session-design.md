# Script Generation & Session Design

**Project:** BunBun Broll Generator
**Date:** 2026-02-11
**Status:** Design Approved

## Overview

Integrate ScriptFlow's pattern-based script generation system into BunBun Broll. Uses the existing Gemini Proxy LLM backend with database persistence and a separate UI page.

## Goals

1. Enable full video essay script generation within BunBun Broll
2. Support pluggable script patterns via JSON files
3. Provide database-backed session persistence
4. Maintain existing B-roll workflow compatibility
5. Use jazirah-ilmu pattern as default

---

## Architecture

### Component Diagram

```
┌─────────────────────────────────────────────────────────────────────┐
│                        Blazor UI Layer                              │
├─────────────────────────────────────────────────────────────────────┤
│  ScriptGenerator.razor  │  Home.razor (existing)                   │
│  - Pattern selection    │  - B-roll generation                     │
│  - Config form          │  - Video preview                         │
│  - Progress display     │                                         │
└─────────────────────────────────────────────────────────────────────┘
                                  │
                                  ▼
┌─────────────────────────────────────────────────────────────────────┐
│                      Service Layer                                  │
├─────────────────────────────────────────────────────────────────────┤
│  IScriptGenerationService  │  IPipelineOrchestrator (existing)      │
│  - Session management      │  - B-roll search & download            │
│  - Export logic            │                                         │
│                            │                                         │
│  IScriptOrchestrator       │  IIntelligenceService (existing)       │
│  - Pattern execution       │  - Gemini Proxy LLM calls              │
│  - Phase coordination      │                                         │
│                            │                                         │
│  IPatternRegistry          │  IProjectService (existing)            │
│  - Pattern discovery       │  - Project persistence                  │
└─────────────────────────────────────────────────────────────────────┘
                                  │
                                  ▼
┌─────────────────────────────────────────────────────────────────────┐
│                      Data Layer                                      │
├─────────────────────────────────────────────────────────────────────┤
│  AppDbContext (SQLite)     │  Pattern JSON Files                    │
│  - ScriptPattern           │  patterns/                              │
│  - ScriptGenerationSession │  - jazirah-ilmu.json                   │
│  - ScriptGenerationPhase   │                                         │
└─────────────────────────────────────────────────────────────────────┘
```

### Data Flow

```
User selects pattern → Enter config → Create session
→ Generate phases (async with DB updates)
→ Export script → Optional: Send to B-roll workflow
```

---

## Models

### ScriptPattern

Represents a pattern loaded from JSON.

```csharp
public class ScriptPattern
{
    public string Id { get; set; }              // "jazirah-ilmu"
    public string Name { get; set; }
    public string Description { get; set; }
    public int PhaseCount { get; set; }
    public string? FilePath { get; set; }

    [NotMapped]
    public PatternConfiguration Configuration { get; set; }
}
```

### ScriptGenerationSession

Database-backed session state.

```csharp
public class ScriptGenerationSession
{
    public string Id { get; set; }              // GUID
    public string PatternId { get; set; }
    public string Topic { get; set; }
    public string? Outline { get; set; }
    public int TargetDurationMinutes { get; set; }
    public string? SourceReferences { get; set; }

    public SessionStatus Status { get; set; }
    public List<ScriptGenerationPhase> Phases { get; set; }

    public string OutputDirectory { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
}
```

### ScriptGenerationPhase

Individual phase state.

```csharp
public class ScriptGenerationPhase
{
    public string Id { get; set; }
    public string SessionId { get; set; }
    public ScriptGenerationSession Session { get; set; }

    public string PhaseId { get; set; }         // "opening-hook", etc.
    public string PhaseName { get; set; }
    public int Order { get; set; }
    public PhaseStatus Status { get; set; }

    public string? ContentFilePath { get; set; }
    public int? WordCount { get; set; }
    public double? DurationSeconds { get; set; }
    public bool IsValidated { get; set; }
    public List<string> Warnings { get; set; }
    public DateTime? CompletedAt { get; set; }
}
```

### PatternConfiguration

Mirrors the pattern JSON structure.

```csharp
public class PatternConfiguration
{
    public string Name { get; set; }
    public string Description { get; set; }
    public List<PhaseDefinition> Phases { get; set; }
    public GlobalRules GlobalRules { get; set; }
    public string ClosingFormula { get; set; }
    public ProductionChecklist ProductionChecklist { get; set; }

    public IEnumerable<PhaseDefinition> GetOrderedPhases()
        => Phases.OrderBy(p => p.Order);
}
```

---

## Pattern Format

Patterns are stored as JSON in the `patterns/` directory.

**Example: `patterns/jazirah-ilmu.json`**

```json
{
  "name": "jazirah-ilmu",
  "description": "Video Essay Reflektif-Eskatologis-Kritik Sosial",
  "phases": [
    {
      "id": "opening-hook",
      "name": "Opening Hook",
      "order": 1,
      "durationTarget": { "min": 60, "max": 90 },
      "wordCountTarget": { "min": 180, "max": 320 },
      "requiredElements": [
        "Salam pembuka (Assalamualaikum)",
        "Pertanyaan provokatif atau paradoks"
      ],
      "forbiddenPatterns": ["Bayangkan", "Pada kesempatan kali ini"],
      "guidanceTemplate": "Gunakan kontras atau paradoks...",
      "customRules": {
        "mustHaveGreeting": "true",
        "cognitiveDisturbance": "moderate"
      }
    }
    // ... 4 more phases
  ],
  "globalRules": {
    "tone": "Serius, reflektif, kontemplatif",
    "language": "Bahasa Indonesia formal-naratif",
    "honorificsRequired": "true",
    "maxWordsPerSentence": "30"
  },
  "closingFormula": "Wallahuam bissawab"
}
```

---

## Services

### IScriptOrchestrator

Main coordinator for script generation.

```csharp
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

### IScriptGenerationService

High-level service for UI.

```csharp
public interface IScriptGenerationService
{
    Task<List<ScriptPattern>> GetAvailablePatternsAsync();
    Task<ScriptGenerationSession> CreateSessionAsync(
        string patternId, string topic, string? outline, int targetDuration);
    Task<ScriptGenerationSession> GenerateAsync(string sessionId);
    Task<ScriptGenerationSession?> GetSessionAsync(string sessionId);
    Task<List<ScriptGenerationSession>> ListSessionsAsync();
    Task<string> ExportScriptAsync(string sessionId, bool clean = false);
}
```

### IPatternRegistry

Pattern discovery and management.

```csharp
public interface IPatternRegistry
{
    void Register(string id, PatternConfiguration config);
    PatternConfiguration? Get(string id);
    IEnumerable<string> ListPatterns();
    bool Exists(string id);
    void LoadFromDirectory(string directory);
}
```

---

## UI Design

### ScriptGenerator.razor

Separate page with three main views:

1. **Pattern Selection** - Grid of available patterns
2. **Config Form** - Topic, outline, duration inputs
3. **Progress/Complete** - Phase progress and results

**Style:** Matches existing Home.razor with:
- `max-w-[42rem] mx-auto` layout
- `card` components
- `btn-primary` / `btn-secondary` buttons
- `input` form classes
- Indonesian labels
- Progress bar styling

---

## File Storage

### Output Structure

```
output/scripts/
├── {sessionId}/
│   ├── 01_opening-hook.md
│   ├── 02_contextualization.md
│   ├── 03_multi-dimensi.md
│   ├── 04_climax.md
│   ├── 05_eschatology.md
│   └── COMPLETE_SCRIPT.md
└── exports/
    └── {sessionId}_clean.md
```

### Pattern Storage

```
patterns/
├── jazirah-ilmu.json
├── narasi-sejarah.json (future)
└── kontemplatif.json (future)
```

---

## Configuration

**appsettings.json additions:**

```json
{
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

**DI Setup (Program.cs):**

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

// Script Generation Services
builder.Services.AddScoped<IScriptOrchestrator, ScriptOrchestrator>();
builder.Services.AddScoped<IScriptGenerationService, ScriptGenerationService>();
```

---

## Implementation Plan

### Phase 1: Foundation
- Create pattern configuration models
- Create session models
- Add DbSets to AppDbContext
- Create and run migration

### Phase 2: Core Services
- Implement PatternRegistry
- Implement ScriptOrchestrator
- Create LLM prompt builder
- Wire up IntelligenceService

### Phase 3: Business Logic
- Implement ScriptGenerationService
- Add session lifecycle methods
- Implement export functionality
- Add resume/pause logic

### Phase 4: UI
- Create ScriptGenerator.razor
- Implement pattern selection
- Implement config form
- Implement progress display

### Phase 5: Pattern Content
- Port jazirah-ilmu.json pattern
- Test end-to-end generation
- Refine prompts

### Phase 6: Polish
- Add error handling
- Add loading states
- Write tests
- Update documentation

---

## Enums

```csharp
public enum SessionStatus
{
    Pending,
    Running,
    Paused,
    Completed,
    Failed
}

public enum PhaseStatus
{
    Pending,
    InProgress,
    Completed,
    Failed
}
```

---

## Integration with Existing System

| Component | Integration Point |
|-----------|-------------------|
| IntelligenceService | LLM generation calls |
| AppDbContext | Session persistence |
| Home.razor | "Send to B-Roll" navigation |
| ToastService | Progress notifications |

---

## Testing Strategy

### Unit Tests
- PatternConfigurationTests
- ScriptGenerationSessionTests
- PatternRegistryTests
- ScriptOrchestratorTests

### Integration Tests
- End-to-end script generation
- Session resume logic
- Export functionality

---

## Notes

- Default pattern: jazirah-ilmu (5 phases)
- Language: Bahasa Indonesia
- LLM: Gemini Proxy via existing IntelligenceService
- Storage: SQLite database + JSON pattern files
- UI: Separate page, matches existing style
