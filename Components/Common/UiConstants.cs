namespace BunbunBroll.Components.Common;

/// <summary>
/// Centralized Tailwind CSS class constants for consistent styling across components.
/// Using constants makes it easier to maintain and update styles globally.
/// </summary>
public static class UiConstants
{
    // Button styles
    public const string BtnPrimary = "btn-primary";
    public const string BtnSecondary = "btn-secondary";
    public const string BtnGhost = "btn-ghost";
    public const string BtnDestructive = "btn-destructive";
    
    // Card styles
    public const string Card = "card";
    public const string CardHover = "card card-hover";
    
    // Input styles
    public const string Input = "input";
    public const string InputError = "input border-destructive";
    
    // Badge styles
    public const string Badge = "badge";
    public const string BadgeMono = "badge badge-mono";
    
    // Layout styles
    public const string Container = "container mx-auto px-4";
    public const string PageContainer = "max-w-[90rem] mx-auto pt-6 pb-8 px-4";
    public const string NarrowContainer = "max-w-[52rem] mx-auto";
    
    // Animation
    public const string AnimateIn = "animate-in";
    public const string Skeleton = "skeleton";
    
    // Text colors
    public const string TextMuted = "text-muted-foreground";
    public const string TextPrimary = "text-primary";
    public const string TextSuccess = "text-success";
    public const string TextDestructive = "text-destructive";
    public const string TextWarning = "text-warning";
}

/// <summary>
/// Icon paths as constants for commonly used SVG icons
/// </summary>
public static class Icons
{
    public const string Spinner = @"<circle class=""opacity-25"" cx=""12"" cy=""12"" r=""10"" stroke=""currentColor"" stroke-width=""4""></circle><path class=""opacity-75"" fill=""currentColor"" d=""M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z""></path>";
    
    public const string Check = @"<path d=""M5 13l4 4L19 7""/>";
    public const string X = @"<path d=""M6 18L18 6M6 6l12 12""/>";
    public const string ChevronDown = @"<path d=""M19 9l-7 7-7-7""/>";
    public const string ChevronLeft = @"<path d=""M15 18l-6-6 6-6""/>";
    public const string ChevronRight = @"<path d=""M9 18l6-6-6-6""/>";
    public const string Download = @"<path d=""M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4""/><polyline points=""7 10 12 15 17 10""/><line x1=""12"" y1=""15"" x2=""12"" y2=""3""/>";
    public const string Refresh = @"<path d=""M21 12a9 9 0 1 1-9-9c2.52 0 4.85.99 6.57 2.57L21 8""/><path d=""M21 3v5h-5""/>";
    public const string Search = @"<circle cx=""11"" cy=""11"" r=""8""/><path d=""m21 21-4.35-4.35""/>";
    public const string Plus = @"<path d=""M12 5v14M5 12h14""/>";
    public const string Trash = @"<path d=""M3 6h18""/><path d=""M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6""/><path d=""M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2""/>";
    public const string Copy = @"<rect x=""9"" y=""9"" width=""13"" height=""13"" rx=""2"" ry=""2""/><path d=""M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1""/>";
    public const string Edit = @"<path d=""M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7""/><path d=""M18.5 2.5a2.121 2.121 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z""/>";
    public const string Home = @"<path d=""M3 12l2-2m0 0l7-7 7 7M5 10v10a1 1 0 001 1h3m10-11l2 2m-2-2v10a1 1 0 01-1 1h-3m-4 0h4""/>";
    public const string Video = @"<polygon points=""23 7 16 12 23 17 23 7""/><rect x=""1"" y=""5"" width=""15"" height=""14"" rx=""2"" ry=""2""/>";
    public const string Image = @"<rect x=""3"" y=""3"" width=""18"" height=""18"" rx=""2"" ry=""2""/><circle cx=""8.5"" cy=""8.5"" r=""1.5""/><polyline points=""21 15 16 10 5 21""/>";
    public const string Sun = @"<circle cx=""12"" cy=""12"" r=""4""/><path d=""M12 2v2""/><path d=""M12 20v2""/><path d=""m4.93 4.93 1.41 1.41""/><path d=""m17.66 17.66 1.41 1.41""/><path d=""M2 12h2""/><path d=""M20 12h2""/><path d=""m6.34 17.66-1.41 1.41""/><path d=""m19.07 4.93-1.41 1.41""/>";
    public const string Moon = @"<path d=""M12 3a6 6 0 0 0 9 9 9 9 0 1 1-9-9Z""/>";
    public const string Warning = @"<path d=""M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z""/><line x1=""12"" y1=""9"" x2=""12"" y2=""13""/><line x1=""12"" y1=""17"" x2=""12.01"" y2=""17""/>";
    public const string Info = @"<circle cx=""12"" cy=""12"" r=""10""/><line x1=""12"" y1=""16"" x2=""12"" y2=""12""/><line x1=""12"" y1=""8"" x2=""12.01"" y2=""8""/>";
    public const string Settings = @"<path d=""M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2z""/><circle cx=""12"" cy=""12"" r=""3""/>";
    public const string MoreHorizontal = @"<circle cx=""12"" cy=""12"" r=""1""/><circle cx=""19"" cy=""12"" r=""1""/><circle cx=""5"" cy=""12"" r=""1""/>";
    public const string Clock = @"<circle cx=""12"" cy=""12"" r=""10""/><polyline points=""12 6 12 12 16 14""/>";
    public const string Folder = @"<path d=""M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z""/>";
    public const string File = @"<path d=""M14.5 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V7.5L14.5 2z""/><polyline points=""14 2 14 8 20 8""/>";
    public const string Play = @"<polygon points=""5 3 19 12 5 21 5 3""/>";
    public const string Pause = @"<rect x=""6"" y=""4"" width=""4"" height=""16""/><rect x=""14"" y=""4"" width=""4"" height=""16""/>";
}

/// <summary>
/// CSS class combinations for common UI patterns
/// </summary>
public static class CssClasses
{
    // Form layouts
    public const string FormGroup = "space-y-2";
    public const string FormLabel = "text-sm font-medium leading-none";
    public const string FormDescription = "text-xs text-muted-foreground";
    public const string FormError = "text-xs text-destructive";
    
    // Grid layouts
    public const string Grid2Cols = "grid grid-cols-1 md:grid-cols-2 gap-4";
    public const string Grid3Cols = "grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4";
    public const string Grid4Cols = "grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4";
    
    // Flex layouts
    public const string FlexBetween = "flex items-center justify-between";
    public const string FlexCenter = "flex items-center justify-center";
    public const string FlexCol = "flex flex-col";
    public const string FlexRow = "flex flex-row";
    public const string FlexGap2 = "flex items-center gap-2";
    public const string FlexGap3 = "flex items-center gap-3";
    public const string FlexGap4 = "flex items-center gap-4";
    
    // Spacing
    public const string SpaceY2 = "space-y-2";
    public const string SpaceY3 = "space-y-3";
    public const string SpaceY4 = "space-y-4";
    public const string SpaceY6 = "space-y-6";
    
    // Text
    public const string TextXs = "text-xs";
    public const string TextSm = "text-sm";
    public const string TextBase = "text-base";
    public const string TextLg = "text-lg";
    public const string TextXl = "text-xl";
    public const string Text2xl = "text-2xl";
    public const string FontMedium = "font-medium";
    public const string FontSemibold = "font-semibold";
    public const string FontBold = "font-bold";
    public const string FontMono = "font-mono";
    
    // Utilities
    public const string Truncate = "truncate";
    public const string LineClamp2 = "line-clamp-2";
    public const string LineClamp3 = "line-clamp-3";
    public const string VisuallyHidden = "sr-only";
}
