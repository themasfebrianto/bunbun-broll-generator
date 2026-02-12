# Models Reference

All models are in `Models/` namespace `BunbunBroll.Models`.

---

## Database Entities (EF Core / SQLite)

### Project
**File**: `Data/AppDbContext.cs`
**Table**: `Projects`

| Field | Type | Description |
|-------|------|-------------|
| `Id` | `string` (PK) | 8-char GUID |
| `Name` | `string` | Project name |
| `RawScript` | `string` | Original script text |
| `Mood` | `string?` | Detected/set mood |
| `CreatedAt` | `DateTime` | Created timestamp |
| `Segments` | `List<ProjectSegment>` | Child segments |

### ProjectSegment
**Table**: `Segments` — FK cascade-deletes with Project.

| Field | Type | Description |
|-------|------|-------------|
| `Id` | `int` (PK) | Auto-increment |
| `ProjectId` | `string` (FK) | Parent project |
| `Title` | `string` | Segment title |
| `Order` | `int` | Sort order |
| `Sentences` | `List<ProjectSentence>` | Child sentences |

### ProjectSentence
**Table**: `Sentences` — FK cascade-deletes with Segment.

| Field | Type | Description |
|-------|------|-------------|
| `Id` | `int` (PK) | Auto-increment |
| `SegmentId` | `int` (FK) | Parent segment |
| `Order` | `int` | Sort order |
| `Text` | `string` | Sentence text |
| `VideoId` | `string?` | Selected video ID |
| `VideoProvider` | `string?` | "pexels" or "pixabay" |
| `VideoUrl` | `string?` | Video download URL |
| `VideoPreviewUrl` | `string?` | Video preview URL |
| `VideoThumbUrl` | `string?` | Thumbnail URL |
| `KeywordsJson` | `string?` | JSON-serialized `KeywordSet` |
| `SuggestedCategory` | `string?` | AI-suggested content category |
| `DetectedMood` | `string?` | Detected mood |
| `WhiskImagePath` | `string?` | AI-generated image path |
| `WhiskMotionType` | `string?` | Ken Burns motion type |
| `VideoDuration` | `int` | Selected video duration (seconds) |

### ScriptGenerationSession
**File**: `Models/ScriptGenerationSession.cs`
**Table**: `ScriptGenerationSessions`

| Field | Type | Description |
|-------|------|-------------|
| `Id` | `string` (PK) | 8-char GUID |
| `PatternId` | `string` | Pattern config ID |
| `Topic` | `string` | Script topic |
| `Outline` | `string?` | Full outline text |
| `OutlineDistributionJson` | `string?` | JSON mapping phaseId → outline points |
| `TargetDurationMinutes` | `int` | Target video length |
| `SourceReferences` | `string?` | Reference material |
| `ChannelName` | `string` | YouTube channel name |
| `Status` | `SessionStatus` | Pending/InProgress/Completed/Failed |
| `OutputDirectory` | `string` | Output path |
| `CreatedAt` / `UpdatedAt` / `CompletedAt` | `DateTime` | Timestamps |
| `ErrorMessage` | `string?` | Error details |
| `Phases` | `List<ScriptGenerationPhase>` | Child phases |

### ScriptGenerationPhase
**Table**: `ScriptGenerationPhases` — FK cascade-deletes with Session.

| Field | Type | Description |
|-------|------|-------------|
| `Id` | `string` (PK) | GUID |
| `SessionId` | `string` (FK) | Parent session |
| `PhaseId` | `string` | Phase identifier from pattern |
| `PhaseName` | `string` | Display name |
| Plus content, status, duration, word count, timestamps | | |

---

## Pipeline Models (In-Memory)

### BrollPromptItem
**File**: `Models/BrollPromptItem.cs`
**Purpose**: State object for one script segment in the B-roll generation pipeline. Persisted to `broll-prompts.json`.

**3-Phase State**:

| Phase | Fields |
|-------|--------|
| **Classification** | `MediaType` (BrollVideo/ImageGeneration), `Prompt`, `Reasoning` |
| **B-Roll Search** | `SearchResults`, `IsSearching`, `SearchError`, `SelectedVideoUrl`, `SearchPage` |
| **Whisk Image Gen** | `WhiskStatus`, `WhiskImagePath`, `WhiskError`, `IsGenerating`, `CombinedRegenProgress` |
| **Ken Burns Video** | `WhiskVideoStatus`, `WhiskVideoPath`, `WhiskVideoError`, `IsConvertingVideo`, `KenBurnsMotion` |

### BrollMediaType (enum)
- `BrollVideo` — Stock footage (Pexels/Pixabay)
- `ImageGeneration` — AI image via Whisk

### WhiskGenerationStatus (enum)
- `Pending` → `Generating` → `Done` / `Failed`

### KenBurnsMotionType (enum)
**File**: `Models/KenBurnsMotionType.cs`
- `SlowZoomIn`, `SlowZoomOut`, `PanLeftToRight`, `PanRightToLeft`, `DiagonalZoomIn`, `DiagonalZoomOut`

---

## Search & Video Models

### KeywordSet
**File**: `Models/KeywordSet.cs` (5.2KB)
**Purpose**: Tiered keyword layers for video search.

| Tier | Use |
|------|-----|
| `Primary` | Main search terms |
| `Mood` | Emotional atmosphere keywords |
| `Contextual` | Scene/setting keywords |
| `Action` | Activity/movement keywords |
| `Fallback` | Generic safe keywords as last resort |

### VideoAsset
**File**: `Models/VideoAsset.cs` (2.6KB)
**Purpose**: Represents a video from Pexels or Pixabay.

### PexelsResponse / PixabayResponse
API response DTOs for video search.

---

## Script Generation Models

### PatternConfiguration
**File**: `Models/PatternConfiguration.cs`
**Purpose**: Loaded from `patterns/*.json`. Defines script structure.

| Field | Type | Description |
|-------|------|-------------|
| `Name` | `string` | Pattern display name |
| `Phases` | `List<PhaseDefinition>` | Ordered script phases |
| `GlobalRules` | `GlobalRules` | Writing rules applied to all phases |
| `ClosingFormula` | `string` | Standard closing text |
| `ProductionChecklist` | `ProductionChecklist` | Quality checklist |

### PhaseDefinition
**File**: `Models/PhaseDefinition.cs`
**Purpose**: Single phase within a pattern (e.g., "Opening", "Content 1", "Closing").

### GenerationContext
**File**: `Models/GenerationContext.cs`
**Purpose**: Shared state during generation (completed phases, shared data).

### ShortVideoConfig
**File**: `Models/ShortVideoConfig.cs` (4.6KB)
**Purpose**: Configuration for video composition (dimensions, FPS, transitions, text overlays).

### TransitionType (enum)
**File**: `Models/TransitionType.cs` (4KB)
**Purpose**: FFmpeg xfade transition types for video composition.

---

## Misc Models

| Model | File | Purpose |
|-------|------|---------|
| `WhiskConfig` | `Models/WhiskConfig.cs` | Whisk CLI configuration |
| `ScriptSegment` | `Models/ScriptSegment.cs` | Parsed script segment |
| `ScriptSentence` | `Models/ScriptSentence.cs` | Single sentence with metadata |
| `ProcessingJob` | `Models/ProcessingJob.cs` | B-roll processing job state |
| `ContentCategory` | `Models/ContentCategory.cs` | Content classification categories |
| `AspectRatio` | `Models/AspectRatio.cs` | Video aspect ratio options |
| `ImagePromptModels` | `Models/ImagePromptModels.cs` | DTOs for image prompt classification |
| `ScriptConfig` | `Models/ScriptConfig.cs` | Script generation configuration |
| `DurationTarget` | `Models/DurationTarget.cs` | Target duration for phases |
| `WordCountTarget` | `Models/WordCountTarget.cs` | Word count targets |
