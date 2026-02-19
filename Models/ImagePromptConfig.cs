namespace BunbunBroll.Models;

// === Enums for decomposed image generation controls ===

/// <summary>Art style preset for AI image generation</summary>
public enum ImageArtStyle
{
    /// <summary>Semi-realistic academic painting with visible brushstrokes + Islamic iconography</summary>
    SemiRealisticPainting,
    /// <summary>Classical oil painting with rich textures and depth</summary>
    OilPainting,
    /// <summary>Soft watercolor with translucent washes and bleeding edges</summary>
    Watercolor,
    /// <summary>Modern digital art, clean and polished</summary>
    DigitalArt,
    /// <summary>Photorealistic rendering, indistinguishable from photography</summary>
    Photorealistic,
    /// <summary>Cinematic film still aesthetic with depth of field</summary>
    Cinematic,
    /// <summary>Anime/manga-inspired stylized art</summary>
    Anime,
    /// <summary>Pencil/charcoal sketch with hand-drawn feel</summary>
    Sketch,
    /// <summary>User-defined freetext style</summary>
    Custom
}

/// <summary>Lighting preset for AI image generation</summary>
public enum ImageLighting
{
    /// <summary>Dramatic directional light with strong shadows</summary>
    DramaticHighContrast,
    /// <summary>Warm golden hour sunlight</summary>
    GoldenHour,
    /// <summary>Soft diffused ambient lighting</summary>
    SoftAmbient,
    /// <summary>Dark moody atmosphere with minimal light</summary>
    MoodyDark,
    /// <summary>Ethereal supernatural glow</summary>
    EtherealGlow,
    /// <summary>Flat even lighting, no strong shadows</summary>
    Flat
}

/// <summary>Color palette preset for AI image generation</summary>
public enum ImageColorPalette
{
    /// <summary>Vibrant focal colors against muted backgrounds (default)</summary>
    VibrantFocalMuted,
    /// <summary>Warm earthy tones: amber, brown, terracotta, ochre</summary>
    WarmEarthy,
    /// <summary>Cool blue tones: navy, teal, cerulean, silver</summary>
    CoolBlue,
    /// <summary>Black and white or sepia monochrome</summary>
    Monochrome,
    /// <summary>Golden desert palette: sand, gold, bronze, sunset orange</summary>
    GoldenDesert,
    /// <summary>Mystic purple: violet, indigo, deep magenta, cosmic</summary>
    MysticPurple,
    /// <summary>Natural green: forest, emerald, sage, earth</summary>
    NaturalGreen
}

/// <summary>Camera composition/angle preset for AI image generation</summary>
public enum ImageComposition
{
    /// <summary>Let AI decide the best angle per scene</summary>
    Auto,
    /// <summary>Wide establishing shot showing full environment</summary>
    WideShot,
    /// <summary>Close-up detail shot focusing on subject</summary>
    CloseUp,
    /// <summary>Overhead bird's eye view</summary>
    BirdsEye,
    /// <summary>Low angle looking upward for dramatic effect</summary>
    LowAngle,
    /// <summary>Ultra-wide cinematic aspect with dramatic framing</summary>
    CinematicWide
}

// === Style suffix string mappings ===

public static class ImageStyleMappings
{
    public static string GetArtStyleSuffix(ImageArtStyle style) => style switch
    {
        ImageArtStyle.SemiRealisticPainting => "semi-realistic academic painting style with visible brushstrokes, traditional Islamic iconography mixed with Western historical art influences",
        ImageArtStyle.OilPainting => "classical oil painting style with rich impasto textures, deep layered glazing, traditional fine art composition",
        ImageArtStyle.Watercolor => "soft watercolor painting style with translucent washes, bleeding edges, delicate color gradients, paper texture visible",
        ImageArtStyle.DigitalArt => "modern digital art style, clean polished rendering, vibrant saturated colors, sharp details, concept art quality",
        ImageArtStyle.Photorealistic => "photorealistic style, ultra-detailed photograph quality, natural textures, realistic proportions, shot on ARRI Alexa",
        ImageArtStyle.Cinematic => "cinematic film still style, anamorphic lens, shallow depth of field, film grain, color graded, movie quality",
        ImageArtStyle.Anime => "anime illustration style, clean linework, expressive characters, vibrant cel-shaded colors, detailed backgrounds",
        ImageArtStyle.Sketch => "pencil and charcoal sketch style, hand-drawn linework, crosshatching, high contrast, raw artistic texture",
        _ => ""
    };

    public static string GetLightingSuffix(ImageLighting lighting) => lighting switch
    {
        ImageLighting.DramaticHighContrast => "dramatic high-contrast lighting with directional illumination, deep shadows",
        ImageLighting.GoldenHour => "warm golden hour lighting, long soft shadows, glowing rim light, sunset warmth",
        ImageLighting.SoftAmbient => "soft ambient lighting, diffused and gentle, no harsh shadows, even illumination",
        ImageLighting.MoodyDark => "moody dark lighting, chiaroscuro, deep pools of shadow, minimal light sources",
        ImageLighting.EtherealGlow => "ethereal supernatural glow, divine light rays, luminescent atmosphere, heavenly radiance",
        ImageLighting.Flat => "flat even lighting, minimal shadows, clear visibility",
        _ => ""
    };

    public static string GetColorPaletteSuffix(ImageColorPalette palette) => palette switch
    {
        ImageColorPalette.VibrantFocalMuted => "vibrant focal colors against muted backgrounds",
        ImageColorPalette.WarmEarthy => "warm earthy color palette: amber, terracotta, ochre, bronze, burnt sienna",
        ImageColorPalette.CoolBlue => "cool blue color palette: navy, teal, cerulean, silver, ice blue",
        ImageColorPalette.Monochrome => "monochrome palette, black and white with subtle sepia undertones",
        ImageColorPalette.GoldenDesert => "golden desert palette: sand gold, bronze, sunset orange, warm amber",
        ImageColorPalette.MysticPurple => "mystic purple palette: deep violet, indigo, magenta, cosmic nebula tones",
        ImageColorPalette.NaturalGreen => "natural green palette: forest emerald, sage, moss, earth brown accents",
        _ => ""
    };

    public static string GetCompositionSuffix(ImageComposition composition) => composition switch
    {
        ImageComposition.Auto => "",
        ImageComposition.WideShot => "wide establishing shot, full environment visible, expansive framing",
        ImageComposition.CloseUp => "close-up shot, detailed focus on subject, shallow depth of field",
        ImageComposition.BirdsEye => "bird's eye view, overhead perspective, top-down angle",
        ImageComposition.LowAngle => "low angle shot looking upward, dramatic perspective, imposing framing",
        ImageComposition.CinematicWide => "ultra-wide cinematic framing, 2.39:1 aspect composition, dramatic horizontal staging",
        _ => ""
    };
}

/// <summary>
/// Global configuration for controlling AI image prompt generation.
/// Set before running classification to control art style, lighting, colors, composition, era, and custom instructions.
/// </summary>
public class ImagePromptConfig
{
    // === Art Style ===
    /// <summary>Art style preset for the image generator</summary>
    public ImageArtStyle ArtStyle { get; set; } = ImageArtStyle.SemiRealisticPainting;
    
    /// <summary>Custom art style freetext (used when ArtStyle == Custom)</summary>
    public string CustomArtStyle { get; set; } = string.Empty;

    // === Lighting ===
    /// <summary>Lighting preset for the image generator</summary>
    public ImageLighting Lighting { get; set; } = ImageLighting.DramaticHighContrast;

    // === Color Palette ===
    /// <summary>Color palette preset for the image generator</summary>
    public ImageColorPalette ColorPalette { get; set; } = ImageColorPalette.VibrantFocalMuted;

    // === Composition ===
    /// <summary>Camera composition/angle preset for the image generator</summary>
    public ImageComposition Composition { get; set; } = ImageComposition.Auto;

    // === Era ===
    /// <summary>Default era to bias AI toward (None = auto-detect per segment)</summary>
    public VideoEra DefaultEra { get; set; } = VideoEra.None;

    // === Custom Instructions ===
    /// <summary>Additional context/instructions injected into the classification system prompt</summary>
    public string CustomInstructions { get; set; } = string.Empty;

    // === Visual Hook ===
    /// <summary>Whether to force ImageGeneration for Phase 1 & 2 (first 3 minutes)</summary>
    public bool ForceVisualHook { get; set; } = true;

    /// <summary>Check if any custom config is set beyond defaults</summary>
    public bool HasCustomConfig =>
        ArtStyle != ImageArtStyle.SemiRealisticPainting
        || Lighting != ImageLighting.DramaticHighContrast
        || ColorPalette != ImageColorPalette.VibrantFocalMuted
        || Composition != ImageComposition.Auto
        || DefaultEra != VideoEra.None
        || !string.IsNullOrWhiteSpace(CustomInstructions);

    /// <summary>Build the effective style suffix from individual components</summary>
    public string EffectiveStyleSuffix
    {
        get
        {
            var parts = new List<string>();

            // Art style
            if (ArtStyle == ImageArtStyle.Custom && !string.IsNullOrWhiteSpace(CustomArtStyle))
                parts.Add(CustomArtStyle);
            else if (ArtStyle != ImageArtStyle.Custom)
            {
                var artSuffix = ImageStyleMappings.GetArtStyleSuffix(ArtStyle);
                if (!string.IsNullOrEmpty(artSuffix)) parts.Add(artSuffix);
            }

            // Lighting
            var lightSuffix = ImageStyleMappings.GetLightingSuffix(Lighting);
            if (!string.IsNullOrEmpty(lightSuffix)) parts.Add(lightSuffix);

            // Color palette
            var colorSuffix = ImageStyleMappings.GetColorPaletteSuffix(ColorPalette);
            if (!string.IsNullOrEmpty(colorSuffix)) parts.Add(colorSuffix);

            // Composition
            var compSuffix = ImageStyleMappings.GetCompositionSuffix(Composition);
            if (!string.IsNullOrEmpty(compSuffix)) parts.Add(compSuffix);

            // Always append quality tags
            parts.Add("expressive painterly textures, atmospheric depth, ultra-detailed, sharp focus, 8k quality, consistent visual tone");

            return ", " + string.Join(", ", parts);
        }
    }
}
