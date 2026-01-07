namespace BunbunBroll.Models;

/// <summary>
/// Supported aspect ratios for short video output.
/// </summary>
public enum AspectRatio
{
    /// <summary>
    /// Portrait 9:16 (1080x1920) - TikTok, Reels, Shorts
    /// </summary>
    Portrait_9x16 = 1,

    /// <summary>
    /// Square 1:1 (1080x1080) - Instagram Feed, Facebook
    /// </summary>
    Square_1x1 = 2,

    /// <summary>
    /// Portrait 4:5 (1080x1350) - Instagram Feed optimal
    /// </summary>
    Portrait_4x5 = 3,

    /// <summary>
    /// Landscape 16:9 (1920x1080) - YouTube, standard video
    /// </summary>
    Landscape_16x9 = 4
}

/// <summary>
/// Helper extensions for AspectRatio enum.
/// </summary>
public static class AspectRatioExtensions
{
    /// <summary>
    /// Get the resolution (width, height) for the aspect ratio.
    /// </summary>
    public static (int Width, int Height) GetResolution(this AspectRatio ratio) => ratio switch
    {
        AspectRatio.Portrait_9x16 => (1080, 1920),
        AspectRatio.Square_1x1 => (1080, 1080),
        AspectRatio.Portrait_4x5 => (1080, 1350),
        AspectRatio.Landscape_16x9 => (1920, 1080),
        _ => (1080, 1920)
    };

    /// <summary>
    /// Get display name for the ratio.
    /// </summary>
    public static string GetDisplayName(this AspectRatio ratio) => ratio switch
    {
        AspectRatio.Portrait_9x16 => "9:16",
        AspectRatio.Square_1x1 => "1:1",
        AspectRatio.Portrait_4x5 => "4:5",
        AspectRatio.Landscape_16x9 => "16:9",
        _ => "9:16"
    };

    /// <summary>
    /// Get description for the ratio.
    /// </summary>
    public static string GetDescription(this AspectRatio ratio) => ratio switch
    {
        AspectRatio.Portrait_9x16 => "TikTok, Reels, Shorts",
        AspectRatio.Square_1x1 => "Instagram, Facebook",
        AspectRatio.Portrait_4x5 => "Instagram Feed",
        AspectRatio.Landscape_16x9 => "YouTube, Standard",
        _ => "Short Video"
    };

    /// <summary>
    /// Get icon for the ratio.
    /// </summary>
    public static string GetIcon(this AspectRatio ratio) => ratio switch
    {
        AspectRatio.Portrait_9x16 => "📱",
        AspectRatio.Square_1x1 => "⬜",
        AspectRatio.Portrait_4x5 => "📐",
        AspectRatio.Landscape_16x9 => "🖥️",
        _ => "📱"
    };
}
