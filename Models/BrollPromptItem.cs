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

    // === Phase 1: Broll Search Results ===
    
    /// <summary>Search results from Pexels/Pixabay for BROLL segments</summary>
    public List<VideoAsset> SearchResults { get; set; } = new();
    
    /// <summary>Whether this segment is currently searching for broll videos</summary>
    public bool IsSearching { get; set; }
    
    /// <summary>Search error message if any</summary>
    public string? SearchError { get; set; }

    // === Phase 2: Whisk Image Generation ===
    
    /// <summary>Status of Whisk image generation for IMAGE_GEN segments</summary>
    public WhiskGenerationStatus WhiskStatus { get; set; } = WhiskGenerationStatus.Pending;
    
    /// <summary>Path to generated Whisk image file</summary>
    public string? WhiskImagePath { get; set; }
    
    /// <summary>Whisk generation error message</summary>
    public string? WhiskError { get; set; }
    
    /// <summary>Whether this segment is currently generating via Whisk</summary>
    public bool IsGenerating { get; set; }
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
