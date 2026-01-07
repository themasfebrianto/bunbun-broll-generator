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
    public string AssociatedText { get; init; } = "";
    public double DurationSeconds { get; init; }
    public double TrimStart { get; init; } = 0;
    public double TrimEnd { get; init; } = 0;

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
}
