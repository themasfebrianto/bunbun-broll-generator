namespace BunbunBroll.Models;

/// <summary>
/// Configuration for generating a short video (TikTok/Reels/Shorts format).
/// </summary>
public record ShortVideoConfig
{
    // === Video Specifications ===

    /// <summary>
    /// Aspect ratio for the output video.
    /// </summary>
    public AspectRatio Ratio { get; init; } = AspectRatio.Portrait_9x16;

    /// <summary>
    /// Video width in pixels (computed from Ratio if not explicitly set).
    /// </summary>
    public int Width => Ratio.GetResolution().Width;

    /// <summary>
    /// Video height in pixels (computed from Ratio if not explicitly set).
    /// </summary>
    public int Height => Ratio.GetResolution().Height;

    /// <summary>
    /// Target duration in seconds.
    /// </summary>
    public int TargetDurationSeconds { get; init; } = 30;

    public int MinDurationSeconds { get; init; } = 15;
    public int MaxDurationSeconds { get; init; } = 60;

    // === Quality Settings ===

    public string VideoCodec { get; init; } = "libx264";
    public string AudioCodec { get; init; } = "aac";
    public int VideoBitrate { get; init; } = 5000; // kbps
    public int AudioBitrate { get; init; } = 192;  // kbps
    public int Fps { get; init; } = 30;

    // === Content Category ===

    public ContentCategory Category { get; init; } = ContentCategory.Islami;

    // === Editing Options ===

    /// <summary>
    /// Automatically cut/trim clips to fit target duration.
    /// </summary>
    public bool AutoCut { get; init; } = true;

    /// <summary>
    /// Add transitions between clips.
    /// </summary>
    public bool AddTransitions { get; init; } = true;

    /// <summary>
    /// Type of transition to use between clips.
    /// </summary>
    public TransitionType Transition { get; init; } = TransitionType.Fade;

    /// <summary>
    /// Transition duration in seconds (0.3 - 1.0 recommended).
    /// </summary>
    public double TransitionDuration { get; init; } = 0.5;

    /// <summary>
    /// Add text overlay (hook text at beginning).
    /// </summary>
    public bool AddTextOverlay { get; init; } = true;

    /// <summary>
    /// Add background music.
    /// </summary>
    public bool AddBackgroundMusic { get; init; } = false;

    /// <summary>
    /// Background music volume (0.0 - 1.0).
    /// </summary>
    public float MusicVolume { get; init; } = 0.3f;

    // === Hook/Intro Text ===

    /// <summary>
    /// Opening hook text displayed at the start.
    /// </summary>
    public string? HookText { get; init; }

    /// <summary>
    /// Hook text display duration in milliseconds.
    /// </summary>
    public int HookDurationMs { get; init; } = 2000;

    // === Artistic Style ===

    /// <summary>
    /// Artistic style filter to apply to B-roll videos.
    /// </summary>
    public VideoStyle Style { get; init; } = VideoStyle.None;
}

/// <summary>
/// Artistic styles for B-roll video processing.
/// </summary>
public enum VideoStyle
{
    None,
    Painting,
    Canvas,
    Sepia,
    FilmGrain
}

/// <summary>
/// Artistic filters that can be applied to B-roll videos (color/exposure adjustments).
/// </summary>
public enum VideoFilter
{
    None,
    Painting,
    Sepia,
    Vintage,
    Cinematic,
    Warm,
    Cool,
    Noir
}

/// <summary>
/// Texture overlays that can be applied on top of B-roll videos.
/// </summary>
public enum VideoTexture
{
    None,
    Canvas,
    Paper,
    Grunge,
    FilmGrain,
    Dust,
    Scratches
}

/// <summary>
/// Detected era/context from script content for auto-assigning appropriate filter/texture
/// </summary>
public enum VideoEra
{
    None,
    /// <summary>Ancient times: prophets, 7th century, Babylon, Egypt, etc</summary>
    Ancient,
    /// <summary>End times/eschatological: apocalyptic, Day of Judgment, etc</summary>
    Apocalyptic,
    /// <summary>Modern/contemporary: 21st century, urban, technology</summary>
    Modern,
    /// <summary>Abstract/symbolic: metaphysical, cosmic, spiritual concepts</summary>
    Abstract,
    /// <summary>Nature/landscape: forests, mountains, oceans (timeless)</summary>
    Nature
}

/// <summary>
/// Result of short video generation.
/// </summary>
public record ShortVideoResult
{
    public bool Success { get; init; }
    public string? OutputPath { get; init; }
    public string? ErrorMessage { get; init; }
    public int DurationSeconds { get; init; }
    public int ClipsUsed { get; init; }
    public long FileSizeBytes { get; init; }
    public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Progress info for video composition.
/// </summary>
public record CompositionProgress
{
    public string Stage { get; init; } = "";
    public int Percent { get; init; }
    public string Message { get; init; } = "";
}

/// <summary>
/// Represents a video clip to be used in composition.
/// </summary>
public record VideoClip
{
    public string SourcePath { get; init; } = "";
    public string SourceUrl { get; init; } = "";
    public string ImagePath { get; init; } = "";
    public bool IsImage => !string.IsNullOrEmpty(ImagePath);
    public KenBurnsMotionType MotionType { get; init; } = KenBurnsMotionType.SlowZoomIn;
    public string AssociatedText { get; init; } = "";
    public double DurationSeconds { get; init; }
    public VideoStyle? Style { get; init; }

    // === Separate Filter & Texture for Ken Burns and B-roll videos ===
    
    /// <summary>Artistic filter applied to the video (color/look adjustments)</summary>
    public VideoFilter Filter { get; init; } = VideoFilter.None;
    
    /// <summary>Intensity percentage of the filter effect (0-100)</summary>
    public int FilterIntensity { get; init; } = 100;
    
    /// <summary>Texture overlay applied on top of the video</summary>
    public VideoTexture Texture { get; init; } = VideoTexture.None;

    /// <summary>Opacity percentage of the texture effect (0-100)</summary>
    public int TextureOpacity { get; init; } = 30;

    public VideoClip() { }

    public VideoClip(string sourcePath, string text)
    {
        SourcePath = sourcePath;
        AssociatedText = text;
    }

    public VideoClip(string sourcePath, string sourceUrl, string text, double duration)
    {
        SourcePath = sourcePath;
        SourceUrl = sourceUrl;
        AssociatedText = text;
        DurationSeconds = duration;
    }

    /// <summary>
    /// Create a VideoClip from an image with optional Ken Burns motion, filter, and texture.
    /// </summary>
    public static VideoClip FromImage(
        string imagePath, 
        string text, 
        double duration, 
        KenBurnsMotionType motion = KenBurnsMotionType.SlowZoomIn,
        VideoFilter filter = VideoFilter.None,
        VideoTexture texture = VideoTexture.None,
        int filterIntensity = 100,
        int textureOpacity = 30)
    {
        return new VideoClip
        {
            ImagePath = imagePath,
            AssociatedText = text,
            DurationSeconds = duration,
            MotionType = motion,
            Filter = filter,
            FilterIntensity = filterIntensity,
            Texture = texture,
            TextureOpacity = textureOpacity
        };
    }
}
