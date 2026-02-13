namespace BunbunBroll.Models;

/// <summary>
/// Represents a single script segment with its LLM-classified media type and generated prompt.
/// Used in the "Kirim ke B-Roll" flow to determine whether each segment
/// should use stock B-Roll video or AI image generation (Whisk).
/// </summary>
public class BrollPromptItem
{
    public int Index { get; set; }
    
    /// <summary>Timestamp string from script, e.g. "[00:15]"</summary>
    public string Timestamp { get; set; } = string.Empty;
    
    /// <summary>Original narration text for this segment</summary>
    public string ScriptText { get; set; } = string.Empty;
    
    /// <summary>LLM-determined media type: BrollVideo or ImageGeneration</summary>
    public BrollMediaType MediaType { get; set; }
    
    /// <summary>Generated prompt for Pexels/Pixabay search (broll) or Whisk (image gen)</summary>
    public string Prompt { get; set; } = string.Empty;
    
    /// <summary>LLM reasoning for why this media type was chosen</summary>
    public string Reasoning { get; set; } = string.Empty;

    /// <summary>Artistic style for this specific segment (legacy, use Filter + Texture)</summary>
    public VideoStyle Style { get; set; } = VideoStyle.None;

    // === Separate Edit Controls: Filter & Texture ===

    /// <summary>Artistic filter applied to the video (color/look adjustments)</summary>
    public VideoFilter Filter { get; set; } = VideoFilter.None;

    /// <summary>Texture overlay applied on top of the video</summary>
    public VideoTexture Texture { get; set; } = VideoTexture.None;

    /// <summary>Detected era/context from script/prompt for automatic styling</summary>
    public VideoEra Era { get; set; } = VideoEra.None;

    /// <summary>Get the effective filter to use (considers both Filter and legacy Style)</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public VideoFilter EffectiveFilter => Filter != VideoFilter.None ? Filter : Style switch
    {
        VideoStyle.Painting => VideoFilter.Painting,
        VideoStyle.Sepia => VideoFilter.Sepia,
        _ => VideoFilter.None
    };

    /// <summary>Get the effective texture to use (considers both Texture and legacy Style)</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public VideoTexture EffectiveTexture => Texture != VideoTexture.None ? Texture : Style switch
    {
        VideoStyle.Canvas => VideoTexture.Canvas,
        _ => VideoTexture.None
    };

    /// <summary>Check if any visual effect is applied</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasVisualEffect => EffectiveFilter != VideoFilter.None || EffectiveTexture != VideoTexture.None;

    // === Phase 1: Broll Search Results ===
    
    /// <summary>Search results from Pexels/Pixabay for BROLL segments</summary>
    public List<VideoAsset> SearchResults { get; set; } = new();
    
    /// <summary>Whether this segment is currently searching for broll videos</summary>
    public bool IsSearching { get; set; }
    
    /// <summary>Search error message if any</summary>
    public string? SearchError { get; set; }
    
    /// <summary>User-selected video URL from search results</summary>
    public string? SelectedVideoUrl { get; set; }
    
    /// <summary>Current search page for cycling through results</summary>
    public int SearchPage { get; set; }
    
    /// <summary>Full cached results from last API call, used for cycling pages</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public List<VideoAsset> AllSearchResults { get; set; } = new();

    // === Phase 2: Whisk Image Generation ===
    
    /// <summary>Status of Whisk image generation for IMAGE_GEN segments</summary>
    public WhiskGenerationStatus WhiskStatus { get; set; } = WhiskGenerationStatus.Pending;
    
    /// <summary>Path to generated Whisk image file</summary>
    public string? WhiskImagePath { get; set; }
    
    /// <summary>Whisk generation error message</summary>
    public string? WhiskError { get; set; }
    
    /// <summary>Whether this segment is currently generating via Whisk</summary>
    public bool IsGenerating { get; set; }

    /// <summary>Progress of combined Regen Prompt & Image action (0-100)</summary>
    public int CombinedRegenProgress { get; set; }

    /// <summary>Ken Burns motion type for generated images. Auto-assigned randomly on creation, user can override.</summary>
    public KenBurnsMotionType KenBurnsMotion { get; set; } = GetRandomMotion();

    // === Phase 3: Ken Burns Video Conversion ===
    
    /// <summary>Status of video conversion from image</summary>
    public WhiskGenerationStatus WhiskVideoStatus { get; set; } = WhiskGenerationStatus.Pending;
    
    /// <summary>Path to generated Ken Burns video file</summary>
    public string? WhiskVideoPath { get; set; }
    
    /// <summary>Video conversion error message</summary>
    public string? WhiskVideoError { get; set; }
    
    /// <summary>Whether this segment is currently converting to video</summary>
    public bool IsConvertingVideo { get; set; }

    // === Phase 4: Artistic Video Filter ===

    /// <summary>Path to video file with artistic filter applied (if any)</summary>
    public string? FilteredVideoPath { get; set; }

    /// <summary>Whether this segment is currently applying a video filter</summary>
    public bool IsFilteringVideo { get; set; }

    /// <summary>Filter application error message</summary>
    public string? FilterError { get; set; }

    /// <summary>Current progress of filter application (0-100)</summary>
    public int FilterProgress { get; set; }

    /// <summary>Status text for filter application (e.g. "Downloading...", "Rendering...")</summary>
    public string FilterStatus { get; set; } = string.Empty;

    private static readonly Random _random = new();
    public static KenBurnsMotionType GetRandomMotion()
    {
        var types = new[]
        {
            KenBurnsMotionType.SlowZoomIn,
            KenBurnsMotionType.SlowZoomOut,
            KenBurnsMotionType.PanLeftToRight,
            KenBurnsMotionType.PanRightToLeft,
            KenBurnsMotionType.DiagonalZoomIn,
            KenBurnsMotionType.DiagonalZoomOut,
        };
        return types[_random.Next(types.Length)];
    }
}

/// <summary>
/// Determines what kind of visual asset should be created for a script segment.
/// </summary>
public enum BrollMediaType
{
    /// <summary>Stock footage from Pexels/Pixabay — motion, landscapes, activities, urban, nature</summary>
    BrollVideo,
    
    /// <summary>AI-generated image via Whisk — abstract concepts, historical scenes, supernatural, unique visuals</summary>
    ImageGeneration
}

/// <summary>
/// Status of Whisk image generation for a segment.
/// </summary>
public enum WhiskGenerationStatus
{
    Pending,
    Generating,
    Done,
    Failed
}
