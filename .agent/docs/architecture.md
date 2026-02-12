# BunBun B-Roll Generator — Architecture Overview

## Project Summary

A .NET 8 Blazor Server application for **Islamic content creators** that automates the entire video production workflow:

1. **Script Generation** — LLM-powered script writing with pattern-based phases
2. **B-Roll Search** — Automated stock footage search via Pexels + Pixabay
3. **AI Image Generation** — Whisk (Google Imagen) for abstract/historical visuals
4. **Ken Burns Video Conversion** — FFmpeg-based image-to-video with pan/zoom effects
5. **Short Video Composition** — Concatenates clips into final shorts with transitions

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Framework | .NET 8 Blazor Server (SSR + Interactive) |
| Database | SQLite via Entity Framework Core |
| LLM | Gemini (via CLI Proxy API, OpenAI-compatible endpoint) |
| Video | FFmpeg (via raw Process, Xabe.FFmpeg wrapper) |
| Images | Whisk CLI (`@rohitaryal/whisk-api`) |
| Stock Video | Pexels API, Pixabay API |
| Auth | Simple email/password via custom `AuthStateProvider` |
| Styling | Custom CSS (dark theme) |

## Directory Structure

```
bunbun-broll-generator/
├── Components/
│   ├── Pages/              # Razor pages (UI)
│   │   ├── Home.razor      # B-roll pipeline (76KB)
│   │   ├── ScriptGenerator.razor  # Script gen + image gen (144KB)
│   │   ├── Projects.razor  # Project list
│   │   ├── VideoProjects.razor  # Video project management
│   │   ├── LoginPage.razor # Authentication
│   │   └── About.razor     # App info
│   ├── Layout/             # Layout components
│   └── Loading/            # Loading indicators
├── Data/
│   └── AppDbContext.cs     # EF Core context (SQLite)
├── Models/                 # 32 model classes
├── Orchestration/          # Script generation orchestration
│   ├── ScriptOrchestrator.cs  # Main orchestrator
│   ├── Generators/         # PromptBuilder, SectionFormatter
│   ├── Validators/         # Phase validation with retry
│   ├── Events/             # Progress events
│   ├── Context/            # Generation context
│   └── Services/           # Phase coordination
├── Services/               # 23 service classes
├── patterns/               # Script pattern JSON configs
├── sessions/               # Git-synced session JSON files
├── output/                 # Generated assets (images, videos, scripts)
│   ├── whisk_images/       # AI-generated images
│   ├── shorts/             # Composed short videos
│   ├── scripts/            # Generated script files
│   └── exports/            # Exported LRC/TXT files
├── Program.cs              # DI registration + startup
├── appsettings.json        # Configuration
└── bunbun.db               # SQLite database
```

## Key Design Decisions

### 1. Two Main Pages, Two Pipelines
- **`Home.razor` (76KB)** — B-roll pipeline: parse script → extract keywords → search videos → preview → download
- **`ScriptGenerator.razor` (144KB)** — Everything else: generate scripts, classify segments, generate images, convert to Ken Burns video

### 2. Composite Asset Broker Pattern
Video search combines **Pexels + Pixabay** with tier-based cascading keywords. The `CompositeAssetBroker` tries Primary → Mood → Contextual → Fallback keyword tiers before giving up.

### 3. Halal Video Filter
A toggle-able filter that modifies search keywords to avoid non-Islamic content (e.g., adds "hijab" to female keywords, prefers nature/landscape content).

### 4. Background Generation
`BackgroundGenerationService` (Singleton) runs script generation in background threads. Uses `GenerationEventBus` for real-time progress updates to the Blazor UI.

### 5. Session Sync via JSON
`SessionSyncService` exports/imports sessions as JSON files in `sessions/` directory, enabling git-based cross-machine synchronization.

### 6. Pattern-Based Script Generation
Scripts follow patterns loaded from JSON files (e.g., `jazirah-ilmu.json`). Each pattern defines phases, global rules, closing formulas, and production checklists.

## Service Lifecycle (DI Registration)

| Lifetime | Services |
|----------|----------|
| Singleton | `KenBurnsService`, `BackgroundGenerationService`, `GenerationEventBus`, `HalalVideoFilter`, `WhiskConfig`, `PatternRegistry`, `ConfigBatchGenerator` |
| Scoped | `ScriptOrchestrator`, `ProjectService`, `IntelligenceService`, `CompositeAssetBroker`, `DownloaderService`, `PipelineOrchestrator`, `WhiskImageGenerator`, `ToastService` |

## Static File Serving

Assets are served via `/project-assets/` path, mapped to the `output/` directory using `PhysicalFileProvider` in `Program.cs`.

```csharp
app.UseStaticFiles(new StaticFileOptions {
    FileProvider = new PhysicalFileProvider(outputDir),
    RequestPath = "/project-assets"
});
```
