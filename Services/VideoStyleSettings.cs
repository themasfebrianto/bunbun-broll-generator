namespace BunbunBroll.Services;

/// <summary>
/// Global video style settings that apply to all video processing.
/// Stored in memory (session-based), can be toggled from UI.
/// </summary>
public class VideoStyleSettings
{
    private bool _vignetteEnabled = true;
    private readonly ILogger<VideoStyleSettings> _logger;

    public VideoStyleSettings(ILogger<VideoStyleSettings> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Global toggle for vignette effect on all videos
    /// </summary>
    public bool VignetteEnabled 
    { 
        get => _vignetteEnabled;
        set
        {
            if (_vignetteEnabled != value)
            {
                _vignetteEnabled = value;
                _logger.LogInformation("VignetteEnabled changed to: {Value}", value);
                OnSettingsChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// Event fired when any setting changes
    /// </summary>
    public event EventHandler? OnSettingsChanged;

    /// <summary>
    /// Reset all settings to defaults
    /// </summary>
    public void ResetToDefaults()
    {
        VignetteEnabled = true;
        _logger.LogInformation("VideoStyleSettings reset to defaults");
    }
}
