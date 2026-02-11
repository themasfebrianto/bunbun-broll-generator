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
