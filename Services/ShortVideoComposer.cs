using BunbunBroll.Models;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace BunbunBroll.Services;

/// <summary>
/// Interface for composing short videos from B-Roll clips.
/// </summary>
public interface IShortVideoComposer
{
    /// <summary>
    /// Check if FFmpeg is available on the system.
    /// </summary>
    Task<bool> IsFFmpegAvailableAsync();

    /// <summary>
    /// Compose multiple video clips into a single short video.
    /// </summary>
    Task<ShortVideoResult> ComposeAsync(
        List<VideoClip> clips,
        ShortVideoConfig config,
        IProgress<CompositionProgress>? progress = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Get video duration using FFprobe.
    /// </summary>
    Task<double> GetVideoDurationAsync(string videoPath, CancellationToken cancellationToken = default);
}

/// <summary>
/// Composes short videos using FFmpeg.
/// </summary>
public class ShortVideoComposer : IShortVideoComposer
{
    private readonly ILogger<ShortVideoComposer> _logger;
    private readonly IConfiguration _config;
    private readonly string _ffmpegPath;
    private readonly string _ffprobePath;
    private readonly string _tempDirectory;
    private readonly string _outputDirectory;

    public ShortVideoComposer(ILogger<ShortVideoComposer> logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;
        _ffmpegPath = config["FFmpeg:Path"] ?? "ffmpeg";
        _ffprobePath = config["FFmpeg:ProbePath"] ?? "ffprobe";
        _tempDirectory = config["FFmpeg:TempDirectory"] ?? Path.Combine(Path.GetTempPath(), "bunbun_ffmpeg");
        _outputDirectory = config["ShortVideo:OutputDirectory"] ?? "./output/shorts";

        // Ensure directories exist
        Directory.CreateDirectory(_tempDirectory);
        Directory.CreateDirectory(_outputDirectory);
    }

    public async Task<bool> IsFFmpegAvailableAsync()
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _ffmpegPath,
                    Arguments = "-version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "FFmpeg not available at path: {Path}", _ffmpegPath);
            return false;
        }
    }

    public async Task<double> GetVideoDurationAsync(string videoPath, CancellationToken cancellationToken = default)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _ffprobePath,
                    Arguments = $"-v quiet -show_entries format=duration -of csv=p=0 \"{videoPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (double.TryParse(output.Trim(), out var duration))
            {
                return duration;
            }

            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get video duration for: {Path}", videoPath);
            return 0;
        }
    }

    public async Task<ShortVideoResult> ComposeAsync(
        List<VideoClip> clips,
        ShortVideoConfig config,
        IProgress<CompositionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (clips.Count == 0)
        {
            return new ShortVideoResult
            {
                Success = false,
                ErrorMessage = "No clips provided for composition"
            };
        }

        // Check FFmpeg availability
        if (!await IsFFmpegAvailableAsync())
        {
            return new ShortVideoResult
            {
                Success = false,
                ErrorMessage = "FFmpeg is not available. Please install FFmpeg and ensure it's in your PATH."
            };
        }

        var sessionId = Guid.NewGuid().ToString("N")[..8];
        var outputFileName = $"short_{sessionId}_{DateTime.Now:yyyyMMdd_HHmmss}.mp4";
        var outputPath = Path.Combine(_outputDirectory, outputFileName);

        try
        {
            progress?.Report(new CompositionProgress { Stage = "Preparing", Percent = 5, Message = "Preparing clips..." });

            // Calculate clip durations to fit target
            var clipDurations = CalculateClipDurations(clips, config);

            progress?.Report(new CompositionProgress { Stage = "Building", Percent = 15, Message = "Building filter graph..." });

            // Build FFmpeg command
            var (arguments, filterPath) = await BuildFFmpegCommandAsync(clips, clipDurations, config, outputPath, cancellationToken);

            progress?.Report(new CompositionProgress { Stage = "Composing", Percent = 30, Message = "Composing video..." });

            // Execute FFmpeg
            var success = await ExecuteFFmpegAsync(arguments, progress, cancellationToken);

            // Cleanup temp filter file
            if (!string.IsNullOrEmpty(filterPath) && File.Exists(filterPath))
            {
                File.Delete(filterPath);
            }

            if (!success || !File.Exists(outputPath))
            {
                return new ShortVideoResult
                {
                    Success = false,
                    ErrorMessage = "FFmpeg composition failed"
                };
            }

            var fileInfo = new FileInfo(outputPath);
            var duration = await GetVideoDurationAsync(outputPath, cancellationToken);

            progress?.Report(new CompositionProgress { Stage = "Complete", Percent = 100, Message = "Video ready!" });

            return new ShortVideoResult
            {
                Success = true,
                OutputPath = outputPath,
                DurationSeconds = (int)duration,
                ClipsUsed = clips.Count,
                FileSizeBytes = fileInfo.Length
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Video composition cancelled");
            return new ShortVideoResult
            {
                Success = false,
                ErrorMessage = "Composition was cancelled"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Video composition failed");
            return new ShortVideoResult
            {
                Success = false,
                ErrorMessage = $"Composition error: {ex.Message}"
            };
        }
    }

    private List<(int ClipIndex, double Start, double Duration)> CalculateClipDurations(
        List<VideoClip> clips,
        ShortVideoConfig config)
    {
        // Reserve time for hook if enabled
        var hookDuration = config.AddTextOverlay && !string.IsNullOrEmpty(config.HookText)
            ? config.HookDurationMs / 1000.0
            : 0;

        var availableTime = config.TargetDurationSeconds - hookDuration;
        var perClipDuration = availableTime / clips.Count;

        // Ensure minimum duration per clip (3 seconds)
        perClipDuration = Math.Max(3, perClipDuration);

        var result = new List<(int, double, double)>();

        for (int i = 0; i < clips.Count; i++)
        {
            var clip = clips[i];
            var clipDuration = clip.DurationSeconds > 0
                ? Math.Min(perClipDuration, clip.DurationSeconds)
                : perClipDuration;

            result.Add((i, 0, clipDuration));
        }

        return result;
    }

    private async Task<(string Arguments, string? FilterPath)> BuildFFmpegCommandAsync(
        List<VideoClip> clips,
        List<(int ClipIndex, double Start, double Duration)> durations,
        ShortVideoConfig config,
        string outputPath,
        CancellationToken cancellationToken)
    {
        var args = new StringBuilder();
        var filterComplex = new StringBuilder();

        // Input files
        for (int i = 0; i < clips.Count; i++)
        {
            var clip = clips[i];
            var source = !string.IsNullOrEmpty(clip.SourcePath) && File.Exists(clip.SourcePath)
                ? clip.SourcePath
                : clip.SourceUrl;

            args.Append($"-i \"{source}\" ");
        }

        // Build filter complex for scaling, trimming, and concatenation
        var scaleFilter = $"scale={config.Width}:{config.Height}:force_original_aspect_ratio=decrease,pad={config.Width}:{config.Height}:(ow-iw)/2:(oh-ih)/2,setsar=1";

        // Process each clip
        for (int i = 0; i < clips.Count; i++)
        {
            var (_, start, duration) = durations[i];
            filterComplex.Append($"[{i}:v]trim=start={start}:duration={duration},setpts=PTS-STARTPTS,{scaleFilter},fps={config.Fps}[v{i}];");
            filterComplex.Append($"[{i}:a]atrim=start={start}:duration={duration},asetpts=PTS-STARTPTS[a{i}];");
        }

        // Concatenate all streams
        for (int i = 0; i < clips.Count; i++)
        {
            filterComplex.Append($"[v{i}][a{i}]");
        }
        filterComplex.Append($"concat=n={clips.Count}:v=1:a=1[outv][outa]");

        // Add transitions if enabled (xfade filter)
        // Note: For MVP, we use simple concat without complex transitions

        // Write filter to temp file (for complex filters)
        var filterPath = Path.Combine(_tempDirectory, $"filter_{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(filterPath, filterComplex.ToString(), cancellationToken);

        // Build final arguments
        args.Append($"-filter_complex_script \"{filterPath}\" ");
        args.Append("-map \"[outv]\" -map \"[outa]\" ");
        args.Append($"-c:v {config.VideoCodec} -b:v {config.VideoBitrate}k ");
        args.Append($"-c:a {config.AudioCodec} -b:a {config.AudioBitrate}k ");
        args.Append($"-r {config.Fps} ");
        args.Append("-movflags +faststart ");
        args.Append($"-y \"{outputPath}\"");

        return (args.ToString(), filterPath);
    }

    private async Task<bool> ExecuteFFmpegAsync(
        string arguments,
        IProgress<CompositionProgress>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Executing FFmpeg: {Args}", arguments);

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _ffmpegPath,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();

            // Read stderr for progress (FFmpeg outputs progress to stderr)
            var errorTask = Task.Run(async () =>
            {
                var reader = process.StandardError;
                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync(cancellationToken);
                    if (line != null)
                    {
                        _logger.LogDebug("FFmpeg: {Line}", line);

                        // Parse progress from FFmpeg output
                        if (line.Contains("time="))
                        {
                            progress?.Report(new CompositionProgress
                            {
                                Stage = "Encoding",
                                Percent = 50,
                                Message = "Encoding video..."
                            });
                        }
                    }
                }
            }, cancellationToken);

            await process.WaitForExitAsync(cancellationToken);
            await errorTask;

            var exitCode = process.ExitCode;
            _logger.LogInformation("FFmpeg exited with code: {ExitCode}", exitCode);

            return exitCode == 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FFmpeg execution failed");
            return false;
        }
    }
}
