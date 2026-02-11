namespace BunbunBroll.Models;

/// <summary>
/// Configuration for Whisk API (Google's Imagen image generation)
/// Ported from ScriptFlow's WhiskConfig
/// </summary>
public class WhiskConfig
{
    /// <summary>
    /// Google account cookie for authentication
    /// Required for whisk API to work
    /// </summary>
    public string? Cookie { get; set; }

    /// <summary>
    /// Whether to enable image generation
    /// </summary>
    public bool EnableImageGeneration { get; set; } = false;

    /// <summary>
    /// Aspect ratio for generated images (default: LANDSCAPE)
    /// Options: SQUARE, PORTRAIT, LANDSCAPE
    /// </summary>
    public string AspectRatio { get; set; } = "LANDSCAPE";

    /// <summary>
    /// Model to use for image generation (default: IMAGEN_3_5)
    /// </summary>
    public string Model { get; set; } = "IMAGEN_3_5";

    /// <summary>
    /// Seed value for reproducible results (default: 0 for random)
    /// </summary>
    public int Seed { get; set; } = 0;

    /// <summary>
    /// Output directory for generated images
    /// </summary>
    public string? OutputDirectory { get; set; }

    /// <summary>
    /// Custom style prefix to prepend to all prompts
    /// </summary>
    public string? StylePrefix { get; set; }

    /// <summary>
    /// Validate configuration
    /// </summary>
    public bool IsValid()
    {
        if (EnableImageGeneration && string.IsNullOrWhiteSpace(Cookie))
            return false;
        return true;
    }
}
