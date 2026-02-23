namespace BunbunBroll.Models;

/// <summary>
/// Compact tag-based style system for streamlined image prompts.
/// Reduces token usage while preserving essential style direction.
/// </summary>
public static class CompactStyleTags
{
    // Compact art style tags (2-3 words max)
    public static string GetArtStyleTag(ImageArtStyle style) => style switch
    {
        ImageArtStyle.SemiRealisticPainting => "semi-realistic painting",
        ImageArtStyle.OilPainting => "oil painting",
        ImageArtStyle.Watercolor => "watercolor",
        ImageArtStyle.DigitalArt => "digital art",
        ImageArtStyle.Photorealistic => "photorealistic",
        ImageArtStyle.Cinematic => "cinematic",
        ImageArtStyle.Anime => "anime style",
        ImageArtStyle.Sketch => "pencil sketch",
        _ => ""
    };

    // Compact lighting tags (2 words max)
    public static string GetLightingTag(ImageLighting lighting) => lighting switch
    {
        ImageLighting.DramaticHighContrast => "dramatic lighting",
        ImageLighting.GoldenHour => "golden hour",
        ImageLighting.SoftAmbient => "soft ambient",
        ImageLighting.MoodyDark => "moody dark",
        ImageLighting.EtherealGlow => "ethereal glow",
        ImageLighting.Flat => "flat lighting",
        _ => ""
    };

    // Compact color palette tags (2 words max)
    public static string GetColorPaletteTag(ImageColorPalette palette) => palette switch
    {
        ImageColorPalette.VibrantFocalMuted => "vibrant focal",
        ImageColorPalette.WarmEarthy => "warm earthy",
        ImageColorPalette.CoolBlue => "cool blue",
        ImageColorPalette.Monochrome => "monochrome",
        ImageColorPalette.GoldenDesert => "golden desert",
        ImageColorPalette.MysticPurple => "mystic purple",
        ImageColorPalette.NaturalGreen => "natural green",
        _ => ""
    };

    // Compact composition tags (2-3 words max)
    public static string GetCompositionTag(ImageComposition composition) => composition switch
    {
        ImageComposition.UltraWideEstablishing => "ultra-wide establishing",
        ImageComposition.GroundLevelWide => "ground-level wide",
        ImageComposition.LowAngleHero => "low-angle hero",
        ImageComposition.OverTheShoulder => "over-the-shoulder",
        ImageComposition.HighAngleTopDown => "high-angle top-down",
        ImageComposition.CloseUpEnvironmental => "close-up texture",
        ImageComposition.DynamicAction => "dynamic action",
        ImageComposition.CinematicSilhouette => "cinematic silhouette",
        ImageComposition.InteriorPerspective => "interior perspective",
        ImageComposition.DistantHorizon => "distant horizon",
        _ => ""
    };

    /// <summary>
    /// Builds a compact style suffix under 150 characters.
    /// Format: "artStyle, lighting, colorPalette, composition, quality"
    /// </summary>
    public static string BuildCompactSuffix(ImagePromptConfig config)
    {
        var parts = new List<string>();

        // Art style (always included, default if not custom)
        if (config.ArtStyle == ImageArtStyle.Custom && !string.IsNullOrWhiteSpace(config.CustomArtStyle))
            parts.Add(config.CustomArtStyle.Trim());
        else if (config.ArtStyle != ImageArtStyle.Custom)
        {
            var artTag = GetArtStyleTag(config.ArtStyle);
            if (!string.IsNullOrEmpty(artTag)) parts.Add(artTag);
        }

        // Color palette (skip Auto)
        if (config.ColorPalette != ImageColorPalette.Auto)
        {
            var colorTag = GetColorPaletteTag(config.ColorPalette);
            if (!string.IsNullOrEmpty(colorTag)) parts.Add(colorTag);
        }

        // Quality tags tailored to aesthetics to avoid conflicting realism
        bool isPhotoreal = config.ArtStyle is ImageArtStyle.Photorealistic or ImageArtStyle.Cinematic;
        bool isStylized = !isPhotoreal && config.ArtStyle != ImageArtStyle.Custom;

        if (isPhotoreal)
            parts.Add("high-detail, 8k resolution, cinematic realism");
        else if (isStylized)
            parts.Add("high-detail");
        else
            parts.Add("high-detail, 8k"); // Default fallback

        return ", " + string.Join(", ", parts);
    }
}
