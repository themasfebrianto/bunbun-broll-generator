# Pipelines & Workflows

This document describes the key end-to-end pipelines in the application.

---

## Pipeline 1: Script Generation

**Entry Point**: `ScriptGenerator.razor` → "Generate" button
**Orchestrator**: `ScriptOrchestrator` → `BackgroundGenerationService`

```mermaid
flowchart TD
    A["User creates session<br/>(topic, pattern, duration, outline)"] --> B["InitializeSessionAsync"]
    B --> C["DistributeBeatsAcrossPhases"]
    C --> D["BackgroundGenerationService.EnqueueGeneration"]
    D --> E["ExecuteGenerationAsync"]
    E --> F{"For each phase"}
    F --> G["PromptBuilder.BuildPrompt"]
    G --> H["IntelligenceService.GenerateContentAsync"]
    H --> I["PatternValidator.Validate"]
    I -->|Pass| J["Save phase to DB"]
    I -->|Fail| K["PromptBuilder.BuildRegenerationPrompt"]
    K --> H
    J --> F
    F -->|All done| L["ExportSessionAsync<br/>(to sessions/ JSON)"]
```

**Key Components**:
| Component | Role |
|-----------|------|
| `PatternConfiguration` | Defines phases, rules, and structure (from `patterns/*.json`) |
| `PromptBuilder` | Builds LLM prompts with phase guidance, previous context, and rules |
| `PatternValidator` | Validates output for duration, content, and format constraints |
| `SectionFormatter` | Formats generated sections |
| `BackgroundGenerationService` | Runs generation in background via DI scope |
| `GenerationEventBus` | Publishes real-time progress to Blazor UI |

**Persistence**: SQLite (`ScriptGenerationSessions` + `ScriptGenerationPhases`) + JSON export to `sessions/`

---

## Pipeline 2: B-Roll Search & Preview

**Entry Point**: `Home.razor` → "Mulai Proses" / "Process"
**Orchestrator**: `PipelineOrchestrator`

```mermaid
flowchart TD
    A["User pastes script"] --> B["ScriptProcessor.SegmentScript"]
    B --> C["Split into Segments → Sentences"]
    C --> D["IntelligenceService.ExtractKeywordSetBatchAsync"]
    D --> E["For each sentence"]
    E --> F["CompositeAssetBroker.SearchVideosAsync"]
    F --> G["Tiered keyword search<br/>Primary → Mood → Contextual → Fallback"]
    G --> H["HalalVideoFilter applied"]
    H --> I["Search Pexels + Pixabay"]
    I --> J["User previews & selects videos"]
    J --> K["DownloadApprovedAsync"]
    K --> L["Downloaded to output/"]
```

**Key Features**:
- Keyword extraction uses layered `KeywordSet` (Primary, Mood, Contextual, Action, Fallback)
- Composite broker searches both Pexels AND Pixabay, deduplicates results
- Halal filter modifies keywords for Islamic-appropriate content
- User can re-search with custom keywords per sentence

---

## Pipeline 3: B-Roll Classification → Image Generation → Ken Burns Video

**Entry Point**: `ScriptGenerator.razor` → "Kirim ke B-Roll" button
**UI Handler**: Various handlers in `ScriptGenerator.razor`

```mermaid
flowchart TD
    A["User clicks 'Kirim ke B-Roll'"] --> B["IntelligenceService.ClassifyAndGeneratePromptsAsync"]
    B --> C{"For each segment"}
    C -->|BrollVideo| D["Search Pexels/Pixabay<br/>(HandleSearchSingleSegment)"]
    C -->|ImageGeneration| E["Generate Whisk image<br/>(GenerateWhiskImageForItem)"]
    E --> F["WhiskImageGenerator.GenerateImageAsync"]
    F --> G["Image saved to output/{session}/whisk_images/"]
    G --> H["User clicks 'Convert Ken Burns'"]
    H --> I["HandleGenerateKenBurnsVideo"]
    I --> J["KenBurnsService.ConvertImageToVideoAsync"]
    J --> K["FFmpeg creates video with motion effect"]
    K --> L["Video served via /project-assets/ URL"]
```

**3-Phase State Machine (per segment)**:

| Phase | Status Field | Path Field | Error Field |
|-------|-------------|------------|-------------|
| 1. Classification | `MediaType` | `Prompt` | — |
| 2. Image Generation | `WhiskStatus` | `WhiskImagePath` | `WhiskError` |
| 3. Ken Burns Video | `WhiskVideoStatus` | `WhiskVideoPath` | `WhiskVideoError` |

**Batch Operations**:
- "Generate All Images" → `HandleGenerateAllWhiskImages` → calls `GenerateWhiskImageForItem` for each pending item
- "Convert All" → Iterates and calls `HandleGenerateKenBurnsVideo` for each done image

**Regeneration Flow**:
- "Regen Prompt + Image" → `HandleRegenPromptAndImage` → resets video state → regenerates prompt via LLM → calls `GenerateWhiskImageForItem`
- "Regen Image Only" → `HandleGenerateSingleWhisk` → calls `GenerateWhiskImageForItem`

---

## Pipeline 4: Short Video Composition

**Entry Point**: Video composition UI
**Orchestrator**: `ShortVideoComposer`

```mermaid
flowchart TD
    A["User selects clips + config"] --> B["ComposeAsync"]
    B --> C["EnsureFFmpegAsync"]
    C --> D["DownloadClipsAsync<br/>(parallel)"]
    D --> E["ProcessSingleClipAsync<br/>(parallel x3)"]
    E --> F["Scale + blur background"]
    E --> G{"Is image?"}
    G -->|Yes| H["KenBurnsService.ConvertImageToVideoAsync"]
    H --> E
    F --> I["ConcatenateClipsWithTransitionsAsync<br/>(xfade)"]
    I --> J["Output to output/shorts/"]
```

**Features**:
- Parallel clip processing (configurable `ParallelClips`)
- Blur background effect for aspect ratio mismatch
- xfade transitions between clips
- Supports both video clips and images (via Ken Burns)

---

## Data Flow Summary

```mermaid
flowchart LR
    subgraph Input
        Script["Raw Script"]
        Topic["Topic + Pattern"]
    end
    
    subgraph AI
        Gemini["Gemini LLM"]
        Whisk["Whisk/Imagen"]
    end
    
    subgraph Search
        Pexels
        Pixabay
    end
    
    subgraph Processing
        FFmpeg
        KenBurns["Ken Burns"]
    end
    
    subgraph Output
        DB["SQLite DB"]
        JSON["sessions/ JSON"]
        Videos["output/ videos"]
        Images["output/ images"]
    end
    
    Script --> Gemini
    Topic --> Gemini
    Gemini -->|Keywords| Search
    Gemini -->|Image Prompts| Whisk
    Gemini -->|Script Phases| DB
    Gemini -->|Script Phases| JSON
    Search -->|Stock Video| Videos
    Whisk -->|Images| Images
    Images -->|Convert| KenBurns
    KenBurns -->|Ken Burns Video| Videos
    Videos -->|Compose| FFmpeg
```
