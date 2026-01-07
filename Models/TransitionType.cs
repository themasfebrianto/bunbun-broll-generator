namespace BunbunBroll.Models;

/// <summary>
/// Video transition types supported by FFmpeg xfade filter.
/// These are the most commonly used transitions in short-form content.
/// </summary>
public enum TransitionType
{
    /// <summary>No transition, hard cut between clips.</summary>
    Cut = 0,
    
    /// <summary>Crossfade/dissolve - most universal, smooth blend.</summary>
    Fade = 1,
    
    /// <summary>Fade through black.</summary>
    FadeBlack = 2,
    
    /// <summary>Fade through white (flash effect).</summary>
    FadeWhite = 3,
    
    /// <summary>New clip wipes from left to right.</summary>
    WipeLeft = 4,
    
    /// <summary>New clip wipes from right to left.</summary>
    WipeRight = 5,
    
    /// <summary>New clip wipes from top to bottom.</summary>
    WipeUp = 6,
    
    /// <summary>New clip wipes from bottom to top.</summary>
    WipeDown = 7,
    
    /// <summary>New clip slides in from left.</summary>
    SlideLeft = 8,
    
    /// <summary>New clip slides in from right.</summary>
    SlideRight = 9,
    
    /// <summary>New clip zooms in (punch effect).</summary>
    ZoomIn = 10,
    
    /// <summary>Circular reveal from center.</summary>
    CircleOpen = 11,
    
    /// <summary>Circular close to center.</summary>
    CircleClose = 12,
    
    /// <summary>Pixelate transition.</summary>
    Pixelize = 13,
    
    /// <summary>Diagonal wipe from top-left.</summary>
    DiagonalTL = 14,
    
    /// <summary>Diagonal wipe from bottom-right.</summary>
    DiagonalBR = 15
}

/// <summary>
/// Extension methods for TransitionType.
/// </summary>
public static class TransitionTypeExtensions
{
    /// <summary>
    /// Get the FFmpeg xfade filter name for this transition.
    /// </summary>
    public static string GetFFmpegName(this TransitionType type) => type switch
    {
        TransitionType.Cut => "fade",  // Will use 0 duration for cut
        TransitionType.Fade => "fade",
        TransitionType.FadeBlack => "fadeblack",
        TransitionType.FadeWhite => "fadewhite",
        TransitionType.WipeLeft => "wipeleft",
        TransitionType.WipeRight => "wiperight",
        TransitionType.WipeUp => "wipeup",
        TransitionType.WipeDown => "wipedown",
        TransitionType.SlideLeft => "slideleft",
        TransitionType.SlideRight => "slideright",
        TransitionType.ZoomIn => "zoomin",
        TransitionType.CircleOpen => "circleopen",
        TransitionType.CircleClose => "circleclose",
        TransitionType.Pixelize => "pixelize",
        TransitionType.DiagonalTL => "diagtl",
        TransitionType.DiagonalBR => "diagbr",
        _ => "fade"
    };

    /// <summary>
    /// Get display name for UI.
    /// </summary>
    public static string GetDisplayName(this TransitionType type) => type switch
    {
        TransitionType.Cut => "Cut (No Transition)",
        TransitionType.Fade => "Fade/Dissolve",
        TransitionType.FadeBlack => "Fade to Black",
        TransitionType.FadeWhite => "Flash White",
        TransitionType.WipeLeft => "Wipe Left",
        TransitionType.WipeRight => "Wipe Right",
        TransitionType.WipeUp => "Wipe Up",
        TransitionType.WipeDown => "Wipe Down",
        TransitionType.SlideLeft => "Slide Left",
        TransitionType.SlideRight => "Slide Right",
        TransitionType.ZoomIn => "Zoom In",
        TransitionType.CircleOpen => "Circle Open",
        TransitionType.CircleClose => "Circle Close",
        TransitionType.Pixelize => "Pixelize",
        TransitionType.DiagonalTL => "Diagonal ↘",
        TransitionType.DiagonalBR => "Diagonal ↗",
        _ => "Fade"
    };

    /// <summary>
    /// Get recommended duration in seconds for this transition type.
    /// </summary>
    public static double GetRecommendedDuration(this TransitionType type) => type switch
    {
        TransitionType.Cut => 0.0,
        TransitionType.FadeWhite => 0.3,  // Flash should be quick
        TransitionType.ZoomIn => 0.4,
        TransitionType.Pixelize => 0.4,
        _ => 0.5  // Default half second for most transitions
    };
}
