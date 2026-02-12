# Services Reference

All services are in `Services/` namespace `BunbunBroll.Services`.

---

## Core AI Services

### IntelligenceService (`IIntelligenceService`)
**File**: `Services/IntelligenceService.cs` (920 lines)
**Lifetime**: Scoped (via `AddHttpClient`)
**Purpose**: Interface to local Gemini LLM for all AI tasks.

**Key Methods**:
| Method | Purpose |
|--------|---------|
| `ExtractKeywordsAsync(sentence)` | Extract B-roll keywords from a single sentence |
| `ExtractKeywordsBatchAsync(sentences)` | Batch keyword extraction (faster) |
| `ExtractKeywordSetBatchAsync(sentences)` | Layered keyword sets: Primary, Mood, Contextual, Action, Fallback |
| `ClassifyAndGeneratePromptsAsync(segments, topic)` | Classify segments as B-Roll vs Image Gen, generate prompts. Batches of 10. |
| `GenerateContentAsync(systemPrompt, userPrompt)` | General-purpose LLM content generation |

**Configuration**: `Gemini` section in `appsettings.json`
```json
{
  "Gemini": {
    "BaseUrl": "http://127.0.0.1:8317",
    "Model": "gemini-3-pro-preview",
    "ApiKey": "scriptflow_gemini_pk_local",
    "TimeoutSeconds": 120
  }
}
```

**Note**: Uses OpenAI-compatible API format (via CLI Proxy at `http://127.0.0.1:8317`).

---

### WhiskImageGenerator
**File**: `Services/WhiskImageGenerator.cs` (256 lines)
**Lifetime**: Scoped
**Purpose**: Generate AI images via Whisk CLI (`@rohitaryal/whisk-api`, wraps Google Imagen).

**Key Methods**:
| Method | Purpose |
|--------|---------|
| `GenerateImageAsync(prompt, outputDir)` | Generate single image → returns `WhiskGenerationResult` |
| `GenerateImagesAsync(prompts, outputDir)` | Generate multiple images sequentially |
| `IsWhiskAvailableAsync()` | Check if whisk CLI is on PATH |

**Configuration**: `Whisk` section in `appsettings.json`
- `Cookie` — Auth cookie for Whisk API (can be overridden by `WHISK_COOKIE` env var)
- `AspectRatio` — `LANDSCAPE` (default)
- `Model` — `IMAGEN_3_5`
- `EnableImageGeneration` — Feature toggle

---

## Video Services

### KenBurnsService
**File**: `Services/KenBurnsService.cs` (165 lines)
**Lifetime**: Singleton
**Purpose**: Convert static images to videos with Ken Burns pan/zoom effects using raw FFmpeg.

**Key Method**:
```csharp
Task<bool> ConvertImageToVideoAsync(
    string imagePath,
    string outputPath,      // Caller controls output location
    double durationSeconds,
    int outputWidth,
    int outputHeight,
    KenBurnsMotionType motionType = KenBurnsMotionType.SlowZoomIn,
    CancellationToken cancellationToken = default)
```

**Motion Types**: `SlowZoomIn`, `SlowZoomOut`, `PanLeftToRight`, `PanRightToLeft`, `DiagonalZoomIn`, `DiagonalZoomOut`

**Important Flags**:
- `-movflags +faststart` — Moves moov atom to start for browser playback
- `-fps_mode cfr` — Constant frame rate (replaces deprecated `-vsync cfr`)
- Output validation: file must exist and be > 1KB

### ShortVideoComposer (`IShortVideoComposer`)
**File**: `Services/ShortVideoComposer.cs` (1280 lines)
**Lifetime**: Singleton
**Purpose**: Full short video composition pipeline using Xabe.FFmpeg wrapper.

**Key Methods**:
| Method | Purpose |
|--------|---------|
| `ComposeAsync(clips, config)` | Full pipeline: download → process → concatenate with transitions |
| `ConvertImageToVideoAsync(imagePath, duration, config)` | Delegates to `KenBurnsService` |
| `EnsureFFmpegAsync()` | Auto-download FFmpeg if not found |
| `GetVideoDurationAsync(path)` | Get video duration via ffprobe |

**Features**: Blur background for aspect ratio mismatch, parallel clip processing, xfade transitions.

**Configuration**: `FFmpeg` + `ShortVideo` sections
```json
{
  "FFmpeg": {
    "BinaryDirectory": "./ffmpeg-binaries",
    "TempDirectory": "./temp/ffmpeg",
    "UseHardwareAccel": true,
    "Preset": "veryfast",
    "ParallelClips": 3,
    "CRF": 23
  }
}
```

---

## Video Search Services

### CompositeAssetBroker (`IAssetBroker`, `IAssetBrokerV2`)
**File**: `Services/CompositeAssetBroker.cs` (293 lines)
**Lifetime**: Scoped
**Purpose**: Combines Pexels + Pixabay search with tiered keyword fallback and Halal filtering.

**Search Flow**:
1. Apply Halal filter to keywords
2. Try Primary keywords → search both Pexels + Pixabay
3. If < threshold results: try Mood keywords
4. If still < threshold: try Contextual keywords
5. If still < threshold: try Fallback keywords
6. Deduplicate and limit results

### PexelsAssetBroker
**File**: `Services/PexelsAssetBroker.cs` (5.3KB)
**Purpose**: Search Pexels API for stock videos.

### PixabayAssetBroker
**File**: `Services/PixabayAssetBroker.cs` (5.9KB)
**Purpose**: Search Pixabay API for stock videos.

### HalalVideoFilter (`IHalalVideoFilter`)
**File**: `Services/HalalVideoFilter.cs` (285 lines)
**Lifetime**: Singleton
**Purpose**: Modify search keywords to ensure Islamic-appropriate content.

**Features**:
- Replaces female keywords with "hijab" variants
- Adds safe modifiers (silhouette, nature, abstract)
- Translates Indonesian keywords to English
- Toggle-able feature flag

---

## Pipeline & Orchestration Services

### PipelineOrchestrator (`IPipelineOrchestrator`)
**File**: `Services/PipelineOrchestrator.cs` (673 lines)
**Lifetime**: Scoped
**Purpose**: Coordinates B-Roll pipeline: script → keywords → video search → preview → download.

**Key Methods**:
| Method | Purpose |
|--------|---------|
| `SearchPreviewsAsync(job)` | Phase 1: Search all sentences for video previews |
| `SearchVideoForSentenceAsync(sentence)` | Search with tier-based cascading keywords |
| `ResearchSentenceAsync(sentence, keywords)` | Re-search with custom keywords |
| `DownloadApprovedAsync(job)` | Download all approved sentence videos |
| `DownloadSentenceAsync(sentence)` | Download single sentence video |

### ScriptOrchestrator (`IScriptOrchestrator`)
**File**: `Orchestration/ScriptOrchestrator.cs` (703 lines)
**Lifetime**: Scoped
**Purpose**: Pattern-based script generation with phase coordination.

**Key Methods**:
| Method | Purpose |
|--------|---------|
| `InitializeSessionAsync(pattern, topic, ...)` | Create new generation session |
| `GenerateAllAsync(sessionId)` | Generate all phases sequentially |
| `ResumeAsync(sessionId)` | Resume incomplete generation |
| `RegeneratePhaseAsync(sessionId, phaseId)` | Regenerate specific phase |
| `ExecuteGenerationAsync(session, phases)` | Core generation loop with validation retry |
| `DistributeBeatsAcrossPhases(pattern, beats)` | Proportional beat distribution |

### BackgroundGenerationService
**File**: `Services/BackgroundGenerationService.cs` (266 lines)
**Lifetime**: Singleton
**Purpose**: Manages concurrent script generation jobs in background threads.

**Key Methods**:
| Method | Purpose |
|--------|---------|
| `EnqueueGeneration(sessionId)` | Start full generation (returns immediately) |
| `EnqueueRegeneration(sessionId, phaseId)` | Start single-phase regen |
| `IsRunning(sessionId)` | Check if session is generating |
| `GetActiveJobs()` | Get all active jobs for UI |

### GenerationEventBus
**File**: `Services/GenerationEventBus.cs` (2.7KB)
**Lifetime**: Singleton
**Purpose**: Pub/sub event bus for real-time generation progress updates to Blazor UI.

---

## Data & Utility Services

### ScriptProcessor (`IScriptProcessor`)
**File**: `Services/ScriptProcessor.cs` (296 lines)
**Purpose**: Parse raw script text → segments → sentences. Each sentence maps to one B-roll clip.

### ProjectService (`IProjectService`)
**File**: `Services/ProjectService.cs` (3.7KB)
**Lifetime**: Scoped
**Purpose**: CRUD operations for Projects in SQLite.

### SessionSyncService
**File**: `Services/SessionSyncService.cs` (327 lines)
**Purpose**: Export/import script sessions to/from `sessions/` JSON files for git-based sync.

### ConfigBatchGenerator
**File**: `Services/ConfigBatchGenerator.cs` (277 lines)
**Purpose**: Generate multiple script configs using LLM based on a theme.

### DownloaderService (`IDownloaderService`)
**File**: `Services/DownloaderService.cs` (3.9KB)
**Purpose**: Download video files from URLs to local disk.

### EraLibrary
**File**: `Services/EraLibrary.cs` (2.7KB)
**Purpose**: Islamic era/period reference data.

### PatternRegistry (`IPatternRegistry`)
**File**: `Services/PatternRegistry.cs` (2KB)
**Purpose**: Load and cache script patterns from `patterns/` directory.

### ToastService
**File**: `Services/ToastService.cs` (976B)
**Purpose**: UI toast notifications.
