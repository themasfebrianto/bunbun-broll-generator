# Feature: Auto Generate Short Video

> **Status:** Proposed  
> **Target Platform:** TikTok / Instagram Reels / YouTube Shorts  
> **Version:** MVP v1.0  
> **Created:** 2026-01-07

---

## 1. Tujuan Fitur

Menambahkan kemampuan untuk **secara otomatis menghasilkan video pendek** (15–60 detik) menggunakan pipeline B-Roll yang sudah ada, dengan editing minimal dan siap upload ke platform short-form content.

### Mengapa Fitur Ini Penting?

| Pain Point | Solusi |
|------------|--------|
| Proses editing manual memakan waktu | Auto-cut dan transisi sederhana |
| Perlu software editing terpisah | Output video langsung siap pakai |
| Tidak konsisten dengan brand/niche | Kategori konten dengan preset template |
| Butuh skill editing video | Satu klik generate, tanpa keahlian khusus |

### Target User

- Content creator yang butuh volume tinggi
- UMKM/Seller yang ingin promosi di social media
- Da'i/Ustadz yang ingin dakwah digital
- Siapapun yang ingin membuat short video cepat

---

## 2. Alur Kerja (Flow) Fitur

### 2.1 User Flow Diagram

```
┌─────────────────────────────────────────────────────────────────────┐
│                        AUTO SHORT VIDEO FLOW                        │
└─────────────────────────────────────────────────────────────────────┘

     ┌──────────┐      ┌──────────────┐      ┌─────────────────┐
     │  Input   │ ───▶ │   Kategori   │ ───▶ │  Generate       │
     │  Script  │      │   Konten     │      │  B-Roll (Existing) │
     └──────────┘      └──────────────┘      └────────┬────────┘
                                                      │
                                                      ▼
     ┌──────────┐      ┌──────────────┐      ┌─────────────────┐
     │  Output  │ ◀─── │   Auto Edit  │ ◀─── │  Cut & Assemble │
     │  .mp4    │      │   Pipeline   │      │  Clips          │
     └──────────┘      └──────────────┘      └─────────────────┘
```

### 2.2 Detail Step-by-Step

| Step | Aksi | Service/Component | Catatan |
|------|------|-------------------|---------|
| 1 | User input script (teks/voiceover) | `Home.razor` | Reuse komponen yang ada |
| 2 | Pilih kategori konten | `CategorySelector` (NEW) | Popup/dropdown sederhana |
| 3 | Pilih mood/style (opsional) | Existing mood selector | Sudah ada di flow |
| 4 | Generate B-Roll | `PipelineOrchestrator` | **Existing service** |
| 5 | Auto-compose video | `ShortVideoComposer` (NEW) | FFmpeg-based |
| 6 | Preview & Download | `VideoPreview.razor` (NEW) | Output final |

---

## 3. Integrasi dengan Flow Existing

### 3.1 Services yang Digunakan (Existing)

Fitur ini **TIDAK** membuat pipeline baru dari nol. Semua proses berikut menggunakan service yang sudah ada:

```csharp
// Existing Services - REUSE
├── ScriptProcessor.cs          // Memecah script → segments
├── IntelligenceService.cs      // AI keyword extraction
├── CompositeAssetBroker.cs     // Pexels/Pixabay search
├── PexelsAssetBroker.cs        // Pexels API
├── PixabayAssetBroker.cs       // Pixabay API
├── HalalVideoFilter.cs         // Content filtering
├── DownloaderService.cs        // Download video clips
└── PipelineOrchestrator.cs     // Orchestrate all above
```

### 3.2 New Components (Minimal Addition)

```csharp
// New Components - MINIMAL
├── Services/
│   └── ShortVideoComposer.cs   // FFmpeg wrapper untuk composing
├── Models/
│   ├── ContentCategory.cs      // Enum kategori konten
│   └── ShortVideoConfig.cs     // Konfigurasi output video
└── Components/
    ├── CategorySelector.razor  // UI pemilihan kategori
    └── ShortVideoPreview.razor // Preview hasil video
```

### 3.3 Integration Point

```
┌─────────────────────────────────────────────────────────────────┐
│                   EXISTING PIPELINE                              │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐       │
│  │ ScriptProc   │───▶│ Intelligence │───▶│ AssetBroker  │       │
│  └──────────────┘    └──────────────┘    └──────┬───────┘       │
│                                                  │               │
│                                          ┌──────▼───────┐       │
│                                          │  Downloaded   │       │
│                                          │  B-Roll Clips │       │
│                                          └──────┬───────┘       │
└─────────────────────────────────────────────────┼───────────────┘
                                                  │
                           ════════════════════════════════════════
                                    INTEGRATION POINT
                           ════════════════════════════════════════
                                                  │
┌─────────────────────────────────────────────────┼───────────────┐
│                   NEW: AUTO EDIT LAYER                          │
│                                          ┌──────▼───────┐       │
│                                          │ ShortVideo   │       │
│                                          │ Composer     │       │
│                                          └──────┬───────┘       │
│                                                  │               │
│  ┌──────────────┐    ┌──────────────┐    ┌──────▼───────┐       │
│  │ Output .mp4  │◀───│ Add Overlay  │◀───│ Cut & Merge  │       │
│  └──────────────┘    └──────────────┘    └──────────────┘       │
└─────────────────────────────────────────────────────────────────┘
```

---

## 4. Struktur Data & Konfigurasi Kategori

### 4.1 Content Category Enum

```csharp
// Models/ContentCategory.cs

public enum ContentCategory
{
    /// <summary>
    /// Konten Islami: Dakwah, Motivasi Islami, Reminder
    /// </summary>
    Islami = 1,
    
    /// <summary>
    /// Konten Jualan: Promosi produk, UMKM, Ads
    /// </summary>
    Jualan = 2,
    
    /// <summary>
    /// Konten Hiburan: Lucu, Meme, Entertainment
    /// </summary>
    Hiburan = 3
}
```

### 4.2 Category Configuration

```csharp
// Models/CategoryConfig.cs

public record CategoryConfig
{
    public ContentCategory Category { get; init; }
    public string DisplayName { get; init; } = "";
    public string Description { get; init; } = "";
    public string Icon { get; init; } = "";
    
    // AI Prompt Enhancement
    public string KeywordModifier { get; init; } = "";
    
    // Visual Style
    public string DefaultTransition { get; init; } = "fade";
    public string DefaultFontFamily { get; init; } = "Inter";
    public string AccentColor { get; init; } = "#ffffff";
    
    // Audio
    public bool IncludeBackgroundMusic { get; init; } = false;
    public string? DefaultMusicPath { get; init; }
    
    // Text Overlay
    public TextOverlayConfig TextConfig { get; init; } = new();
}

public record TextOverlayConfig
{
    public string Position { get; init; } = "bottom"; // top, center, bottom
    public int FontSize { get; init; } = 32;
    public string FontColor { get; init; } = "#ffffff";
    public bool HasShadow { get; init; } = true;
    public bool ShowHook { get; init; } = true; // Teks pembuka di awal
}
```

### 4.3 Default Category Presets

```csharp
// Data/CategoryPresets.cs

public static class CategoryPresets
{
    public static readonly Dictionary<ContentCategory, CategoryConfig> Defaults = new()
    {
        [ContentCategory.Islami] = new CategoryConfig
        {
            Category = ContentCategory.Islami,
            DisplayName = "Islami",
            Description = "Dakwah, Motivasi Islami, Reminder Akhirat",
            Icon = "🕌",
            KeywordModifier = "islamic, peaceful, spiritual, mosque, prayer",
            DefaultTransition = "fade",
            DefaultFontFamily = "Amiri",
            AccentColor = "#1a7f37",
            IncludeBackgroundMusic = true,
            DefaultMusicPath = "assets/music/nasheed_calm.mp3",
            TextConfig = new TextOverlayConfig
            {
                Position = "bottom",
                FontSize = 28,
                FontColor = "#ffffff",
                HasShadow = true,
                ShowHook = true
            }
        },
        
        [ContentCategory.Jualan] = new CategoryConfig
        {
            Category = ContentCategory.Jualan,
            DisplayName = "Jualan / Promosi",
            Description = "Promosi Produk, UMKM, Flash Sale",
            Icon = "🛒",
            KeywordModifier = "product, shopping, business, professional",
            DefaultTransition = "swipe",
            DefaultFontFamily = "Inter",
            AccentColor = "#ff6b35",
            IncludeBackgroundMusic = true,
            DefaultMusicPath = "assets/music/upbeat_promo.mp3",
            TextConfig = new TextOverlayConfig
            {
                Position = "center",
                FontSize = 36,
                FontColor = "#ffffff",
                HasShadow = true,
                ShowHook = true
            }
        },
        
        [ContentCategory.Hiburan] = new CategoryConfig
        {
            Category = ContentCategory.Hiburan,
            DisplayName = "Lucu / Hiburan",
            Description = "Konten Hiburan, Meme, Fun Content",
            Icon = "😂",
            KeywordModifier = "fun, colorful, happy, entertainment",
            DefaultTransition = "zoom",
            DefaultFontFamily = "Comic Neue",
            AccentColor = "#8b5cf6",
            IncludeBackgroundMusic = true,
            DefaultMusicPath = "assets/music/funny_bgm.mp3",
            TextConfig = new TextOverlayConfig
            {
                Position = "top",
                FontSize = 32,
                FontColor = "#ffff00",
                HasShadow = true,
                ShowHook = true
            }
        }
    };
}
```

### 4.4 Short Video Configuration

```csharp
// Models/ShortVideoConfig.cs

public record ShortVideoConfig
{
    // Video Specs
    public int Width { get; init; } = 1080;           // Portrait for shorts
    public int Height { get; init; } = 1920;          // 9:16 aspect ratio
    public int TargetDurationSeconds { get; init; } = 30;
    public int MinDurationSeconds { get; init; } = 15;
    public int MaxDurationSeconds { get; init; } = 60;
    
    // Quality
    public string VideoCodec { get; init; } = "libx264";
    public string AudioCodec { get; init; } = "aac";
    public int VideoBitrate { get; init; } = 5000; // kbps
    public int AudioBitrate { get; init; } = 192;  // kbps
    public int Fps { get; init; } = 30;
    
    // Editing
    public ContentCategory Category { get; init; } = ContentCategory.Islami;
    public bool AutoCut { get; init; } = true;
    public bool AddTransitions { get; init; } = true;
    public bool AddTextOverlay { get; init; } = true;
    public bool AddBackgroundMusic { get; init; } = false;
    public float MusicVolume { get; init; } = 0.3f;  // 30% volume
    
    // Hook/Intro
    public string? HookText { get; init; }           // Teks pembuka
    public int HookDurationMs { get; init; } = 2000; // 2 detik
}
```

---

## 5. Contoh Input & Output

### 5.1 Input Example: Konten Islami

**Script Input:**
```
Tahukah kamu, sholat malam itu obat untuk hati yang gundah.
Ketika dunia terasa berat, mengadulah kepada-Nya.
Bangun di sepertiga malam, rasakan ketenangan yang tiada tara.
```

**Selected Category:** `Islami`

**Configuration:**
```json
{
  "category": "Islami",
  "targetDuration": 30,
  "addTransitions": true,
  "addTextOverlay": true,
  "addBackgroundMusic": true,
  "hookText": "Pernah merasa hatimu berat? 💔"
}
```

### 5.2 Processing Flow

```
Input Script
    │
    ▼
┌──────────────────────────────────────────────┐
│ Step 1: ScriptProcessor.ProcessAsync()       │
│ Output: 3 segments                           │
└──────────────────────────────────────────────┘
    │
    ▼
┌──────────────────────────────────────────────┐
│ Step 2: IntelligenceService.ExtractKeywords()│
│ + CategoryModifier: "islamic, peaceful..."   │
│ Output: ["night prayer", "peace", "mosque"]  │
└──────────────────────────────────────────────┘
    │
    ▼
┌──────────────────────────────────────────────┐
│ Step 3: AssetBroker.SearchVideosAsync()      │
│ Output: 3 video clips (8-12 sec each)        │
└──────────────────────────────────────────────┘
    │
    ▼
┌──────────────────────────────────────────────┐
│ Step 4: ShortVideoComposer.ComposeAsync()    │
│ - Auto-cut clips to fit 30 sec total         │
│ - Apply fade transitions                     │
│ - Add text overlay with Amiri font           │
│ - Mix nasheed background music at 30%        │
│ - Add hook text at beginning                 │
└──────────────────────────────────────────────┘
    │
    ▼
Output: final_short_video.mp4 (30 sec, 1080x1920)
```

### 5.3 Output Example

**Generated Video Specs:**
```
📁 Output File: /Projects/Islami_Sholat_Malam/output_short.mp4
📐 Resolution: 1080 x 1920 (9:16 Portrait)
⏱️ Duration: 30 seconds
🎬 Clips Used: 3
🎵 Background Music: nasheed_calm.mp3
📝 Text Overlay: Enabled (Amiri font, white, shadow)

Timeline:
[0s-2s]   Hook: "Pernah merasa hatimu berat? 💔"
[2s-12s]  Clip 1: Night prayer scene + fade
[12s-22s] Clip 2: Peaceful mosque + fade  
[22s-30s] Clip 3: Person in contemplation
```

### 5.4 Input Example: Konten Jualan

**Script Input:**
```
Diskon gila-gilaan! Hanya hari ini!
Beli 2 gratis 1 untuk semua produk.
Buruan checkout sebelum kehabisan!
```

**Selected Category:** `Jualan`

**Output Preview:**
```
📁 Output File: /Projects/Promo_Diskon/output_short.mp4
📐 Resolution: 1080 x 1920
⏱️ Duration: 15 seconds
🎵 Background Music: upbeat_promo.mp3
📝 Text Overlay: "DISKON GILA!" (center, bold)

Timeline:
[0s-1.5s]  Hook: "🔥 FLASH SALE 🔥"
[1.5s-6s]  Clip 1: Shopping scene + swipe
[6s-11s]   Clip 2: Product display + swipe
[11s-15s]  Clip 3: Happy customer + CTA overlay
```

---

## 6. Technical Implementation

### 6.1 ShortVideoComposer Service

```csharp
// Services/ShortVideoComposer.cs

public interface IShortVideoComposer
{
    Task<string> ComposeAsync(
        List<VideoClip> clips,
        ShortVideoConfig config,
        IProgress<CompositionProgress>? progress = null,
        CancellationToken cancellationToken = default
    );
}

public class ShortVideoComposer : IShortVideoComposer
{
    private readonly ILogger<ShortVideoComposer> _logger;
    private readonly string _ffmpegPath;
    
    public ShortVideoComposer(ILogger<ShortVideoComposer> logger, IConfiguration config)
    {
        _logger = logger;
        _ffmpegPath = config["FFmpeg:Path"] ?? "ffmpeg";
    }
    
    public async Task<string> ComposeAsync(
        List<VideoClip> clips,
        ShortVideoConfig config,
        IProgress<CompositionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // 1. Calculate clip durations to fit target
        var clipDurations = CalculateClipDurations(clips, config);
        
        // 2. Build FFmpeg filter complex
        var filterComplex = BuildFilterComplex(clips, clipDurations, config);
        
        // 3. Execute FFmpeg
        var outputPath = await ExecuteFFmpegAsync(filterComplex, config, cancellationToken);
        
        return outputPath;
    }
    
    private List<(int ClipIndex, double Start, double Duration)> CalculateClipDurations(
        List<VideoClip> clips, 
        ShortVideoConfig config)
    {
        // Auto-cut logic: distribute target duration across clips
        var targetTotal = config.TargetDurationSeconds - (config.HookDurationMs / 1000.0);
        var perClip = targetTotal / clips.Count;
        
        return clips.Select((c, i) => (i, 0.0, Math.Min(perClip, c.Duration))).ToList();
    }
    
    private string BuildFilterComplex(
        List<VideoClip> clips,
        List<(int, double, double)> durations,
        ShortVideoConfig config)
    {
        // Build FFmpeg filter_complex for:
        // - Scaling to 1080x1920
        // - Trimming clips
        // - Adding transitions
        // - Overlaying text
        // - Mixing background music
        
        var sb = new StringBuilder();
        
        // ... FFmpeg filter logic ...
        
        return sb.ToString();
    }
}
```

### 6.2 Integration with PipelineOrchestrator

```csharp
// Modification to existing PipelineOrchestrator.cs

public class PipelineOrchestrator
{
    // Existing dependencies...
    private readonly IShortVideoComposer _shortVideoComposer; // NEW
    
    // NEW: Extended method for short video generation
    public async Task<ShortVideoResult> GenerateShortVideoAsync(
        string script,
        ShortVideoConfig config,
        IProgress<PipelineProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // Step 1-4: Use existing pipeline to get B-Roll clips
        var brollResult = await ProcessScriptAsync(
            script, 
            config.Category.GetMoodFromCategory(), // Map category to mood
            progress, 
            cancellationToken
        );
        
        // Step 5: NEW - Compose into short video
        var downloadedClips = brollResult.Segments
            .Where(s => s.SelectedVideo != null)
            .Select(s => new VideoClip(s.SelectedVideo!.LocalPath, s.Text))
            .ToList();
        
        var outputPath = await _shortVideoComposer.ComposeAsync(
            downloadedClips,
            config,
            new Progress<CompositionProgress>(p => 
                progress?.Report(new PipelineProgress { Stage = "Composing", Percent = p.Percent })),
            cancellationToken
        );
        
        return new ShortVideoResult
        {
            OutputPath = outputPath,
            Duration = config.TargetDurationSeconds,
            ClipsUsed = downloadedClips.Count
        };
    }
}
```

---

## 7. Extensibility (Catatan Pengembangan)

### 7.1 Menambah Kategori Baru

Untuk menambah kategori konten baru, cukup:

1. **Tambah enum value:**
```csharp
public enum ContentCategory
{
    Islami = 1,
    Jualan = 2,
    Hiburan = 3,
    Edukasi = 4,      // NEW
    Motivasi = 5,     // NEW
    Tutorial = 6      // NEW
}
```

2. **Tambah preset configuration:**
```csharp
CategoryPresets.Defaults[ContentCategory.Edukasi] = new CategoryConfig
{
    Category = ContentCategory.Edukasi,
    DisplayName = "Edukasi",
    Description = "Konten edukatif, tips, fakta menarik",
    Icon = "📚",
    KeywordModifier = "education, learning, knowledge, school",
    // ... rest of config
};
```

### 7.2 Menambah Jenis Transisi

```csharp
public enum TransitionType
{
    None,
    Fade,      // Existing
    Swipe,     // Existing
    Zoom,      // Existing
    Dissolve,  // NEW
    Wipe,      // NEW
    Glitch     // NEW
}
```

### 7.3 Menambah Text Effect

```csharp
public enum TextEffect
{
    Static,
    FadeIn,
    TypeWriter,   // NEW: Ketik satu per satu
    Bounce,       // NEW: Efek bouncy
    Glitch        // NEW: Efek glitch
}
```

### 7.4 Template System (Future)

```csharp
// Future: Template-based generation
public record VideoTemplate
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public ContentCategory Category { get; init; }
    public List<TemplateSection> Sections { get; init; } = new();
    public string ThumbnailPath { get; init; } = "";
}

public record TemplateSection
{
    public string Type { get; init; } = ""; // "hook", "content", "cta"
    public double DurationSeconds { get; init; }
    public TextOverlayConfig? TextConfig { get; init; }
}
```

---

## 8. MVP Scope & Limitations

### 8.1 Apa yang Termasuk MVP

| Feature | Status | Notes |
|---------|--------|-------|
| 3 kategori konten | ✅ MVP | Islami, Jualan, Hiburan |
| Auto-cut clips | ✅ MVP | Berdasarkan target durasi |
| Fade transition | ✅ MVP | Transisi paling universal |
| Text overlay (hook) | ✅ MVP | Teks pembuka saja |
| Background music | ✅ MVP | Opsional, volume 30% |
| Portrait output (9:16) | ✅ MVP | Standard short video |
| 15-60 detik durasi | ✅ MVP | Sesuai platform |

### 8.2 Apa yang BUKAN MVP (Future)

| Feature | Status | Target |
|---------|--------|--------|
| Voice-over integration | ❌ Future | v1.1 |
| Custom template builder | ❌ Future | v1.2 |
| Multiple output formats | ❌ Future | v1.1 |
| A/B testing variants | ❌ Future | v2.0 |
| Analytics integration | ❌ Future | v2.0 |
| AI-suggested hooks | ❌ Future | v1.2 |

### 8.3 Known Limitations

1. **FFmpeg dependency** - User harus install FFmpeg
2. **Processing time** - Composing video butuh waktu (estimasi 30-60 detik)
3. **Storage** - Temporary files selama proses
4. **Music licensing** - Perlu perhatikan lisensi untuk musik yang digunakan

---

## 9. UI/UX Considerations

### 9.1 Category Selector Component

```razor
@* Components/CategorySelector.razor *@

<div class="category-selector">
    <h3>Pilih Kategori Konten</h3>
    <div class="category-grid">
        @foreach (var category in Categories)
        {
            <button 
                class="category-card @(SelectedCategory == category.Category ? "selected" : "")"
                @onclick="() => SelectCategory(category.Category)">
                <span class="category-icon">@category.Icon</span>
                <span class="category-name">@category.DisplayName</span>
                <span class="category-desc">@category.Description</span>
            </button>
        }
    </div>
</div>
```

### 9.2 Vercel-Style Design (Sesuai IMPROVEMENT_PLAN.md)

- Clean, minimal cards untuk kategori
- 1px border, no shadows
- High contrast selected state
- Fast 150ms transitions
- Icon + text untuk clarity

---

## 10. Dependency & Requirements

### 10.1 System Requirements

| Requirement | Version | Notes |
|-------------|---------|-------|
| .NET | 8.0+ | Existing |
| FFmpeg | 6.0+ | **NEW DEPENDENCY** |
| Disk Space | +500MB | Untuk temporary files |

### 10.2 NuGet Packages

```xml
<!-- Existing -->
<PackageReference Include="Microsoft.AspNetCore.Components" Version="8.0.0" />

<!-- NEW: For FFmpeg integration -->
<PackageReference Include="Xabe.FFmpeg" Version="5.2.6" />
```

### 10.3 Configuration

```json
// appsettings.json
{
  "FFmpeg": {
    "Path": "ffmpeg",
    "TempDirectory": "./temp/ffmpeg",
    "MaxConcurrentJobs": 2
  },
  "ShortVideo": {
    "DefaultDuration": 30,
    "OutputDirectory": "./output/shorts"
  }
}
```

---

## 11. Summary

### Prinsip yang Diikuti

| Prinsip | Implementasi |
|---------|--------------|
| ✅ **Simple** | Hanya 1 service baru (ShortVideoComposer) |
| ✅ **Mudah di-extend** | Kategori via enum + dictionary config |
| ✅ **MVP Ready** | 3 kategori, auto-cut, fade only |
| ✅ **Tidak over-engineered** | Reuse 100% pipeline existing |

### Next Steps

1. [ ] Implementasi `ShortVideoComposer` service
2. [ ] Tambah FFmpeg dependency
3. [ ] Buat `CategorySelector.razor` component
4. [ ] Extend `PipelineOrchestrator` dengan method baru
5. [ ] Testing dengan 3 kategori konten
6. [ ] UI integration di `Home.razor`

---

*Document created for BunBun B-Roll Generator — Auto Short Video Feature MVP*
