namespace BunbunBroll.Models;

/// <summary>
/// Content category for short video generation.
/// Each category has specific visual presets and keyword modifiers.
/// </summary>
public enum ContentCategory
{
    /// <summary>
    /// Konten Islami: Dakwah, Motivasi Islami, Reminder
    /// </summary>
    Islami = 1,

    /// <summary>
    /// Konten Jualan: Promosi produk, UMKM, Ads
    /// </summary>
    Jualan = 2,

    /// <summary>
    /// Konten Hiburan: Lucu, Meme, Entertainment
    /// </summary>
    Hiburan = 3
}

/// <summary>
/// Configuration for a content category, including visual style presets.
/// </summary>
public record CategoryConfig
{
    public ContentCategory Category { get; init; }
    public string DisplayName { get; init; } = "";
    public string Description { get; init; } = "";
    public string Icon { get; init; } = "";

    /// <summary>
    /// Keywords to enhance AI search for this category.
    /// </summary>
    public string KeywordModifier { get; init; } = "";

    /// <summary>
    /// Default transition type: fade, swipe, zoom
    /// </summary>
    public string DefaultTransition { get; init; } = "fade";

    public string DefaultFontFamily { get; init; } = "Inter";
    public string AccentColor { get; init; } = "#ffffff";

    public bool IncludeBackgroundMusic { get; init; } = false;
    public string? DefaultMusicPath { get; init; }

    public TextOverlayConfig TextConfig { get; init; } = new();
}

/// <summary>
/// Text overlay configuration for short videos.
/// </summary>
public record TextOverlayConfig
{
    /// <summary>
    /// Position: top, center, bottom
    /// </summary>
    public string Position { get; init; } = "bottom";
    public int FontSize { get; init; } = 32;
    public string FontColor { get; init; } = "#ffffff";
    public bool HasShadow { get; init; } = true;
    public bool ShowHook { get; init; } = true;
}

/// <summary>
/// Default presets for each content category.
/// </summary>
public static class CategoryPresets
{
    public static readonly Dictionary<ContentCategory, CategoryConfig> Defaults = new()
    {
        [ContentCategory.Islami] = new CategoryConfig
        {
            Category = ContentCategory.Islami,
            DisplayName = "Islami",
            Description = "Dakwah, Motivasi Islami, Reminder",
            Icon = "🕌",
            KeywordModifier = "islamic, peaceful, spiritual, mosque, prayer, muslim",
            DefaultTransition = "fade",
            DefaultFontFamily = "Amiri",
            AccentColor = "#1a7f37",
            IncludeBackgroundMusic = true,
            DefaultMusicPath = "assets/music/nasheed_calm.mp3",
            TextConfig = new TextOverlayConfig
            {
                Position = "bottom",
                FontSize = 28,
                FontColor = "#ffffff",
                HasShadow = true,
                ShowHook = true
            }
        },

        [ContentCategory.Jualan] = new CategoryConfig
        {
            Category = ContentCategory.Jualan,
            DisplayName = "Jualan / Promosi",
            Description = "Promosi Produk, UMKM, Flash Sale",
            Icon = "🛒",
            KeywordModifier = "product, shopping, business, professional, sale",
            DefaultTransition = "swipe",
            DefaultFontFamily = "Inter",
            AccentColor = "#ff6b35",
            IncludeBackgroundMusic = true,
            DefaultMusicPath = "assets/music/upbeat_promo.mp3",
            TextConfig = new TextOverlayConfig
            {
                Position = "center",
                FontSize = 36,
                FontColor = "#ffffff",
                HasShadow = true,
                ShowHook = true
            }
        },

        [ContentCategory.Hiburan] = new CategoryConfig
        {
            Category = ContentCategory.Hiburan,
            DisplayName = "Lucu / Hiburan",
            Description = "Konten Hiburan, Meme, Fun",
            Icon = "😂",
            KeywordModifier = "fun, colorful, happy, entertainment, comedy",
            DefaultTransition = "zoom",
            DefaultFontFamily = "Comic Neue",
            AccentColor = "#8b5cf6",
            IncludeBackgroundMusic = true,
            DefaultMusicPath = "assets/music/funny_bgm.mp3",
            TextConfig = new TextOverlayConfig
            {
                Position = "top",
                FontSize = 32,
                FontColor = "#ffff00",
                HasShadow = true,
                ShowHook = true
            }
        }
    };

    /// <summary>
    /// Get config for a category, with fallback to Islami if not found.
    /// </summary>
    public static CategoryConfig GetConfig(ContentCategory category)
    {
        return Defaults.TryGetValue(category, out var config) ? config : Defaults[ContentCategory.Islami];
    }

    /// <summary>
    /// Get all available categories for UI display.
    /// </summary>
    public static IEnumerable<CategoryConfig> GetAllCategories()
    {
        return Defaults.Values.OrderBy(c => (int)c.Category);
    }
}
