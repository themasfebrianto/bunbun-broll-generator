using BunbunBroll.Models;

namespace BunbunBroll.Services;

/// <summary>
/// Interface for composing videos from B-Roll clips.
/// </summary>
public interface IVideoComposer
{
    /// <summary>
    /// Check if FFmpeg is available on the system.
    /// </summary>
    Task<bool> IsFFmpegAvailableAsync();

    /// <summary>
    /// Ensure FFmpeg is available (tries to find or download).
    /// </summary>
    Task<bool> EnsureFFmpegAsync(IProgress<string>? progress = null);

    /// <summary>
    /// Compose multiple video clips into a single video.
    /// </summary>
    /// <param name="clips">Video clips to compose</param>
    /// <param name="config">Video configuration</param>
    /// <param name="sessionId">Optional session ID for scoped output directory. If not provided, a new GUID will be generated.</param>
    /// <param name="progress">Progress reporter</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<VideoResult> ComposeAsync(
        List<VideoClip> clips,
        VideoConfig config,
        string? sessionId = null,
        IProgress<CompositionProgress>? progress = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Convert an image to a video clip with Ken Burns motion.
    /// </summary>
    Task<string?> ConvertImageToVideoAsync(
        string imagePath,
        double durationSeconds,
        VideoConfig config,
        KenBurnsMotionType motionType = KenBurnsMotionType.SlowZoomIn,
        CancellationToken cancellationToken = default,
        string? sessionId = null
    );

    /// <summary>
    /// Get video duration.
    /// </summary>
    Task<double> GetVideoDurationAsync(string videoPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Apply an artistic style filter to a video.
    /// </summary>
    Task<string?> ApplyStyleToVideoAsync(
        string inputPath,
        VideoStyle style,
        VideoConfig config,
        CancellationToken cancellationToken = default,
        bool isPreview = false,
        string? sessionId = null
    );

    /// <summary>
    /// Apply separate filter and texture to a video.
    /// </summary>
    Task<string?> ApplyFilterAndTextureToVideoAsync(
        string inputPath,
        VideoFilter filter,
        int filterIntensity,
        VideoTexture texture,
        int textureOpacity,
        VideoConfig config,
        CancellationToken cancellationToken = default,
        bool isPreview = false,
        string? sessionId = null
    );
}
