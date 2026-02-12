# Pages & UI Components

All pages are in `Components/Pages/` using Blazor Server interactive rendering.

---

## ScriptGenerator.razor (144KB — Main Workhorse)

**Route**: `/script-generator`
**Purpose**: Full script generation + B-roll image generation + Ken Burns video pipeline.

### Major Sections

| Section | Lines | Purpose |
|---------|-------|---------|
| Session List | ~1-400 | List existing script sessions, create new ones |
| Script Config | ~400-600 | Topic, pattern, duration, outline, channel selection |
| Generation Progress | ~600-800 | Real-time phase-by-phase progress display |
| Generated Script View | ~800-1000 | View/edit completed script phases + export to LRC/TXT |
| B-Roll Pipeline | ~1000-1200 | Classify segments, generate images, convert to video |
| Handlers | ~1200-2700 | All event handler methods |
| Utilities | ~2700-2770 | Helper methods (`GetAssetUrl`, `EstimateDuration`, etc.) |

### Key Handler Methods

| Handler | Purpose |
|---------|---------|
| `HandleGenerateScript` | Start async script generation |
| `HandleRegeneratePhase` | Regenerate specific phase |
| `HandleSendToBroll` | Classify segments → B-Roll vs Image Gen |
| `HandleGenerateSingleWhisk` | Generate one Whisk image |
| `HandleGenerateAllWhiskImages` | Generate all pending Whisk images |
| `HandleRegenPromptAndImage` | Regen prompt + image (combined) |
| `HandleGenerateKenBurnsVideo` | Convert image → Ken Burns video |
| `HandleSearchSingleSegment` | Search B-Roll video for one segment |
| `HandleToggleMediaType` | Toggle segment between B-Roll / Image Gen |
| `HandleRegenSegmentKeywords` | Regenerate keywords for B-Roll search |
| `GenerateWhiskImageForItem` | **Shared helper** for image generation (used by 3 handlers) |
| `GetAssetUrl(absolutePath)` | Convert absolute path → `/project-assets/` URL |
| `EstimateDuration(text)` | Estimate narration duration from text length |

### State Management
- Script sessions: SQLite via `ScriptOrchestrator`
- B-roll data: In-memory `List<BrollPromptItem>`, persisted to `broll-prompts.json`
- Progress: Via `GenerationEventBus` (singleton event bus)

---

## Home.razor (76KB)

**Route**: `/`
**Purpose**: B-Roll search pipeline — paste script → auto-extract keywords → search video → preview → download.

### Major Sections

| Section | Purpose |
|---------|---------|
| Script Input | Paste raw narration script |
| Processing | Segment extraction + keyword generation |
| Preview Grid | Video previews per sentence with selection |
| Download | Batch download approved videos |

### Key Handlers
- Script segmentation via `ScriptProcessor`
- Keyword extraction via `IntelligenceService`
- Video search via `PipelineOrchestrator`
- Video download via `DownloaderService`

---

## Projects.razor (5.8KB)

**Route**: `/projects`
**Purpose**: List, create, and manage B-roll projects (stored in SQLite).

---

## VideoProjects.razor (5.5KB)

**Route**: `/video-projects`
**Purpose**: Short video project management and composition UI.

---

## LoginPage.razor (3.5KB)

**Route**: `/login`
**Purpose**: Simple email/password authentication.

---

## About.razor (5KB)

**Route**: `/about`
**Purpose**: App information and help.

---

## Supporting Components

### Layout Components (`Components/Layout/`)
- `MainLayout.razor` — Main layout with navigation
- Sidebar, header, theme components

### Loading Components (`Components/Loading/`)
- Loading spinners and skeleton screens

### Shared Components
- `CategorySelector.razor` — Content category dropdown
- `ShortVideoSettings.razor` — Short video configuration form
- `App.razor` — Root app component with auth and routing

---

## Asset Display Pattern

### Images (from Whisk)
```html
<!-- Uses GetAssetUrl() helper to convert absolute path to /project-assets/ URL -->
<img src="@GetAssetUrl(item.WhiskImagePath!)" alt="Generated image" />
```

### Videos (Ken Burns)
```html
<!-- Served via /project-assets/ static files -->
<video controls autoplay muted loop>
    <source src="/project-assets/{session}/whisk_images/{filename}_kb.mp4" type="video/mp4" />
</video>
```

### B-Roll Search Results
```html
<!-- Preview URLs from Pexels/Pixabay -->
<video src="@asset.PreviewUrl" controls />
```
