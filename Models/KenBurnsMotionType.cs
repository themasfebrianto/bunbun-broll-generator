namespace BunbunBroll.Models;

/// <summary>
/// Ken Burns motion types for image animation.
/// Ported from ScriptFlow's SrtModels.KenBurnsMotionType.
/// </summary>
public enum KenBurnsMotionType
{
    None,
    SlowZoomIn,
    SlowZoomOut,
    PanLeftToRight,
    PanRightToLeft,
    PanTopToBottom,
    PanBottomToTop,
    DiagonalZoomIn,
    DiagonalZoomOut,
    Random
}
