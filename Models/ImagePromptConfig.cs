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
    /// <summary>Auto-detect based on mood beat and context</summary>
    Auto,
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
    /// <summary>Auto-detect based on mood beat and era</summary>
    Auto,
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
    /// <summary>Ultra-Wide Establishing Shot (God’s-eye epic scale)</summary>
    UltraWideEstablishing,
    /// <summary>Ground-Level Wide Shot (Human perspective)</summary>
    GroundLevelWide,
    /// <summary>Low-Angle Hero Shot (Prophet focus)</summary>
    LowAngleHero,
    /// <summary>Over-the-Shoulder Shot (Emotional immersion)</summary>
    OverTheShoulder,
    /// <summary>High-Angle Top-Down Shot (Miracle geometry)</summary>
    HighAngleTopDown,
    /// <summary>Close-Up Environmental Shot (Texture realism)</summary>
    CloseUpEnvironmental,
    /// <summary>Action Shot (Dynamic motion)</summary>
    DynamicAction,
    /// <summary>Silhouette Shot (Mythic tone)</summary>
    CinematicSilhouette,
    /// <summary>Interior Perspective (Unique angle inside environment)</summary>
    InteriorPerspective,
    /// <summary>Wide Distant Horizon Shot (Aftermath/calm)</summary>
    DistantHorizon
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
        ImageLighting.Auto => "",
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
        ImageColorPalette.Auto => "",
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
        ImageComposition.UltraWideEstablishing => "ultra-wide aerial shot, God's-eye epic scale, tiny figures against immense environment, establishing miracle and scale",
        ImageComposition.GroundLevelWide => "wide ground-level shot from behind subject, immersive perspective inside the event, epic atmosphere",
        ImageComposition.LowAngleHero => "low-angle shot, heroic authority and leadership focus, imposing scale with background elements rising behind",
        ImageComposition.OverTheShoulder => "over-the-shoulder shot, emotional immersion, viewer placed inside the action looking toward the leader",
        ImageComposition.HighAngleTopDown => "high-angle top-down shot, showing geometric relationships and divine scale patterns from above",
        ImageComposition.CloseUpEnvironmental => "close-up of environmental texture and grounding physical details, intense depth of field",
        ImageComposition.DynamicAction => "dynamic action shot, violent energy and motion blur, disaster realism, contrast of forces",
        ImageComposition.CinematicSilhouette => "silhouette shot against glowing background, deep shadows, timeless mythic feel",
        ImageComposition.InteriorPerspective => "interior perspective looking outward through element, refracted light, unique striking angle",
        ImageComposition.DistantHorizon => "wide distant horizon shot, emotional release and calm aftermath, open space",
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
    public ImageLighting Lighting { get; set; } = ImageLighting.Auto;

    // === Color Palette ===
    /// <summary>Color palette preset for the image generator</summary>
    public ImageColorPalette ColorPalette { get; set; } = ImageColorPalette.Auto;

    // === Composition ===
    /// <summary>Camera composition/angle preset for the image generator</summary>
    public ImageComposition Composition { get; set; } = ImageComposition.Auto;

    // === Era ===
    /// <summary>Default era to bias AI toward (None = auto-detect per segment)</summary>
    public VideoEra DefaultEra { get; set; } = VideoEra.None;

    // === Custom Instructions ===
    /// <summary>Additional context/instructions injected into the classification system prompt</summary>
    public string CustomInstructions { get; set; } = string.Empty;

    /// <summary>Check if any custom config is set beyond defaults</summary>
    public bool HasCustomConfig =>
        ArtStyle != ImageArtStyle.SemiRealisticPainting
        || Lighting != ImageLighting.Auto
        || ColorPalette != ImageColorPalette.Auto
        || Composition != ImageComposition.Auto
        || DefaultEra != VideoEra.None
        || !string.IsNullOrWhiteSpace(CustomInstructions);

    /// <summary>
    /// Build the effective style suffix from individual components.
    /// Uses compact tag format for reduced token usage.
    /// </summary>
    public string EffectiveStyleSuffix => CompactStyleTags.BuildCompactSuffix(this);
}
