namespace BunbunBroll.Models;

/// <summary>
/// Configuration settings for visual hooking phases.
/// Defines how timestamps are split and Ken Burns videos are generated
/// for each phase of the first 3 minutes.
/// </summary>
public class PhaseConfig
{
    /// <summary>
    /// Phase identifier (e.g., "opening-hook", "contextualization", "normal")
    /// </summary>
    public string PhaseId { get; set; } = string.Empty;

    /// <summary>
    /// End boundary of this phase in seconds from video start
    /// </summary>
    public double EndTimeSeconds { get; set; }

    /// <summary>
    /// Target duration for each Ken Burns video beat in seconds
    /// </summary>
    public double KenBurnsDuration { get; set; }

    /// <summary>
    /// Motion intensity for Ken Burns effect (affects zoom/pan speed)
    /// </summary>
    public MotionIntensity MotionIntensity { get; set; }

    /// <summary>
    /// Number of micro-beats to split each SRT segment into
    /// </summary>
    public int SplitFactor { get; set; }
}

/// <summary>
/// Motion intensity levels for Ken Burns effect.
/// Higher intensity = faster zoom/pan movements.
/// </summary>
public enum MotionIntensity
{
    /// <summary>
    /// Low intensity - slow, smooth movements for contemplative moments
    /// </summary>
    Low = 0,

    /// <summary>
    /// Medium intensity - moderate movements for balanced pacing
    /// </summary>
    Medium = 1,

    /// <summary>
    /// High intensity - aggressive movements for quick cuts and high engagement
    /// </summary>
    High = 2
}
