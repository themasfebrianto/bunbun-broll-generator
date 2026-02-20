namespace BunbunBroll.Models;

public class TextOverlay
{
    public TextOverlayType Type { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? ArabicText { get; set; }
    public string? Reference { get; set; }
    public int StartDelayMs { get; set; } = 500;
    public int TypingSpeedMs { get; set; } = 50;
    public TypingAnimationStyle AnimationStyle { get; set; } = TypingAnimationStyle.Typewriter;
    public TextStyle Style { get; set; } = TextStyle.Default;
}

public enum TextOverlayType
{
    QuranVerse,
    Hadith,
    RhetoricalQuestion,
    KeyPhrase
}

public enum TypingAnimationStyle
{
    Typewriter,
    WordByWord,
    FadeIn
}

public class TextStyle
{
    public string FontFamily { get; set; } = "Arial";
    public int FontSize { get; set; } = 32;
    public string Color { get; set; } = "#FFFFFF";
    public TextPosition Position { get; set; } = TextPosition.Center;
    public bool HasShadow { get; set; } = true;

    public static TextStyle Default => new();
    public static TextStyle Quran => new() { FontFamily = "Amiri", FontSize = 36, Color = "#FFD700", Position = TextPosition.Center, HasShadow = true };
    public static TextStyle Hadith => new() { FontFamily = "Times New Roman", FontSize = 32, Color = "#F5DEB3", Position = TextPosition.Center, HasShadow = true };
    public static TextStyle Question => new() { FontFamily = "Arial", FontSize = 40, Color = "#FFFFFF", Position = TextPosition.TopCenter, HasShadow = true };
}

public enum TextPosition
{
    Center, TopCenter, BottomCenter, TopLeft, TopRight, BottomLeft, BottomRight
}
