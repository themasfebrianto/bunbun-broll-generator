using BunbunBroll.Models;
using Xabe.FFmpeg;
using System.Diagnostics;

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
    /// Ensure FFmpeg is available (tries to find or download).
    /// </summary>
    Task<bool> EnsureFFmpegAsync(IProgress<string>? progress = null);

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
    /// Get video duration.
    /// </summary>
    Task<double> GetVideoDurationAsync(string videoPath, CancellationToken cancellationToken = default);
}

/// <summary>
/// Composes short videos using Xabe.FFmpeg wrapper.
/// </summary>
public class ShortVideoComposer : IShortVideoComposer
{
    private readonly ILogger<ShortVideoComposer> _logger;
    private readonly IConfiguration _config;
    private readonly string _ffmpegDirectory;
    private readonly string _tempDirectory;
    private readonly string _outputDirectory;
    private bool _isInitialized = false;

    public ShortVideoComposer(ILogger<ShortVideoComposer> logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;
        
        // FFmpeg binaries directory
        _ffmpegDirectory = config["FFmpeg:BinaryDirectory"] 
            ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg-binaries");
        _tempDirectory = config["FFmpeg:TempDirectory"] 
            ?? Path.Combine(Path.GetTempPath(), "bunbun_ffmpeg");
        _outputDirectory = config["ShortVideo:OutputDirectory"] 
            ?? "./output/shorts";

        // Ensure directories exist
        Directory.CreateDirectory(_ffmpegDirectory);
        Directory.CreateDirectory(_tempDirectory);
        Directory.CreateDirectory(_outputDirectory);

        // Set FFmpeg path for Xabe.FFmpeg if binaries exist
        if (Directory.Exists(_ffmpegDirectory))
        {
            FFmpeg.SetExecutablesPath(_ffmpegDirectory);
        }
    }

    public async Task<bool> EnsureFFmpegAsync(IProgress<string>? progress = null)
    {
        if (_isInitialized) return true;

        try
        {
            // Check common locations for FFmpeg
            var ffmpegPath = await FindFFmpegAsync();
            
            if (!string.IsNullOrEmpty(ffmpegPath))
            {
                var directory = Path.GetDirectoryName(ffmpegPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    FFmpeg.SetExecutablesPath(directory);
                    _logger.LogInformation("FFmpeg found at: {Path}", ffmpegPath);
                    _isInitialized = true;
                    return true;
                }
            }

            // Try to use Xabe.FFmpeg's built-in downloader (if available)
            progress?.Report("Attempting to download FFmpeg...");
            
            try
            {
                // Xabe.FFmpeg.Downloader package is needed for auto-download
                // Since it may not be installed, we'll catch if it fails
                await Xabe.FFmpeg.Downloader.FFmpegDownloader.GetLatestVersion(
                    Xabe.FFmpeg.Downloader.FFmpegVersion.Official, 
                    _ffmpegDirectory
                );
                FFmpeg.SetExecutablesPath(_ffmpegDirectory);
                _isInitialized = true;
                progress?.Report("FFmpeg downloaded successfully!");
                return true;
            }
            catch
            {
                // Downloader not available or failed
                _logger.LogWarning("FFmpeg auto-download not available. Please install FFmpeg manually.");
                progress?.Report("Please install FFmpeg manually from https://ffmpeg.org/download.html");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure FFmpeg availability");
            return false;
        }
    }

    private async Task<string?> FindFFmpegAsync()
    {
        var isWindows = OperatingSystem.IsWindows();
        var ffmpegName = isWindows ? "ffmpeg.exe" : "ffmpeg";

        // Check in configured directory
        var configuredPath = Path.Combine(_ffmpegDirectory, ffmpegName);
        if (File.Exists(configuredPath))
        {
            return configuredPath;
        }

        // Check if ffmpeg is in PATH using 'where' (Windows) or 'which' (Linux/Mac)
        try
        {
            var whichCommand = isWindows ? "where" : "which";
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = whichCommand,
                    Arguments = "ffmpeg",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
            {
                var path = output.Split('\n').FirstOrDefault()?.Trim();
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    return path;
                }
            }
        }
        catch
        {
            // Ignore - try other methods
        }

        // Platform-specific common paths
        var commonPaths = isWindows 
            ? new[]
            {
                @"C:\ffmpeg\bin\ffmpeg.exe",
                @"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
                @"C:\Program Files (x86)\ffmpeg\bin\ffmpeg.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ffmpeg", "bin", "ffmpeg.exe")
            }
            : new[]
            {
                "/usr/bin/ffmpeg",
                "/usr/local/bin/ffmpeg",
                "/opt/ffmpeg/bin/ffmpeg"
            };

        foreach (var path in commonPaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    public async Task<bool> IsFFmpegAvailableAsync()
    {
        if (_isInitialized) return true;

        var ffmpegPath = await FindFFmpegAsync();
        if (!string.IsNullOrEmpty(ffmpegPath))
        {
            var directory = Path.GetDirectoryName(ffmpegPath);
            if (!string.IsNullOrEmpty(directory))
            {
                FFmpeg.SetExecutablesPath(directory);
            }
            _isInitialized = true;
            return true;
        }

        return false;
    }

    public async Task<double> GetVideoDurationAsync(string videoPath, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await EnsureFFmpegAsync()) return 0;
            
            var mediaInfo = await FFmpeg.GetMediaInfo(videoPath, cancellationToken);
            return mediaInfo.Duration.TotalSeconds;
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

        var sessionId = Guid.NewGuid().ToString("N")[..8];
        var outputFileName = $"short_{sessionId}_{DateTime.Now:yyyyMMdd_HHmmss}.mp4";
        var outputPath = Path.Combine(_outputDirectory, outputFileName);

        try
        {
            // Step 1: Ensure FFmpeg is available
            progress?.Report(new CompositionProgress { Stage = "Initializing", Percent = 5, Message = "Checking FFmpeg..." });
            
            if (!await EnsureFFmpegAsync())
            {
                return new ShortVideoResult
                {
                    Success = false,
                    ErrorMessage = "FFmpeg not available. Please install FFmpeg from https://ffmpeg.org/download.html"
                };
            }

            // Step 2: Download clips to temp directory if they are URLs
            progress?.Report(new CompositionProgress { Stage = "Downloading", Percent = 10, Message = "Downloading clips..." });
            var localClips = await DownloadClipsAsync(clips, progress, cancellationToken);

            if (localClips.Count == 0)
            {
                return new ShortVideoResult
                {
                    Success = false,
                    ErrorMessage = "Failed to download video clips"
                };
            }

            // Step 3: Calculate clip durations
            progress?.Report(new CompositionProgress { Stage = "Preparing", Percent = 30, Message = "Calculating durations..." });
            var clipDurations = CalculateClipDurations(localClips, config);

            // Step 4: Process and concatenate clips using Xabe.FFmpeg
            progress?.Report(new CompositionProgress { Stage = "Processing", Percent = 40, Message = "Processing clips..." });
            
            var processedClips = new List<string>();
            var clipIndex = 0;

            foreach (var (clip, clipDuration) in localClips.Zip(clipDurations))
            {
                var processedPath = Path.Combine(_tempDirectory, $"clip_{sessionId}_{clipIndex}.mp4");
                
                await ProcessSingleClipAsync(
                    clip.LocalPath, 
                    processedPath, 
                    clipDuration.Duration,
                    config,
                    cancellationToken
                );

                if (File.Exists(processedPath))
                {
                    processedClips.Add(processedPath);
                }

                clipIndex++;
                var percent = 40 + (int)(clipIndex * 30.0 / localClips.Count);
                progress?.Report(new CompositionProgress 
                { 
                    Stage = "Processing", 
                    Percent = percent, 
                    Message = $"Processing clip {clipIndex}/{localClips.Count}..." 
                });
            }

            if (processedClips.Count == 0)
            {
                return new ShortVideoResult
                {
                    Success = false,
                    ErrorMessage = "No clips were successfully processed"
                };
            }

            // Step 5: Concatenate all processed clips
            progress?.Report(new CompositionProgress { Stage = "Concatenating", Percent = 75, Message = "Joining clips..." });
            await ConcatenateClipsAsync(processedClips, outputPath, cancellationToken);

            // Step 6: Cleanup temp files
            progress?.Report(new CompositionProgress { Stage = "Cleanup", Percent = 90, Message = "Cleaning up..." });
            CleanupTempFiles(processedClips, localClips);

            if (!File.Exists(outputPath))
            {
                return new ShortVideoResult
                {
                    Success = false,
                    ErrorMessage = "Failed to create output video"
                };
            }

            var fileInfo = new FileInfo(outputPath);
            var videoDuration = await GetVideoDurationAsync(outputPath, cancellationToken);

            progress?.Report(new CompositionProgress { Stage = "Complete", Percent = 100, Message = "Video ready!" });

            return new ShortVideoResult
            {
                Success = true,
                OutputPath = outputPath,
                DurationSeconds = (int)videoDuration,
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

    private async Task<List<(string LocalPath, VideoClip Original)>> DownloadClipsAsync(
        List<VideoClip> clips,
        IProgress<CompositionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var result = new List<(string, VideoClip)>();
        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromMinutes(5);
        var index = 0;

        foreach (var clip in clips)
        {
            try
            {
                string localPath;

                // Check if already a local file
                if (!string.IsNullOrEmpty(clip.SourcePath) && File.Exists(clip.SourcePath))
                {
                    localPath = clip.SourcePath;
                }
                else if (!string.IsNullOrEmpty(clip.SourceUrl))
                {
                    // Download from URL
                    localPath = Path.Combine(_tempDirectory, $"download_{Guid.NewGuid():N}.mp4");
                    
                    var response = await httpClient.GetAsync(clip.SourceUrl, cancellationToken);
                    response.EnsureSuccessStatusCode();
                    
                    var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                    await File.WriteAllBytesAsync(localPath, content, cancellationToken);
                    
                    _logger.LogInformation("Downloaded clip to: {Path} ({Size} bytes)", localPath, content.Length);
                }
                else
                {
                    _logger.LogWarning("Clip has no source path or URL, skipping");
                    continue;
                }

                result.Add((localPath, clip));
                
                index++;
                var percent = 10 + (int)(index * 20.0 / clips.Count);
                progress?.Report(new CompositionProgress
                {
                    Stage = "Downloading",
                    Percent = percent,
                    Message = $"Downloaded {index}/{clips.Count} clips..."
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to download clip: {Url}", clip.SourceUrl);
            }
        }

        return result;
    }

    private List<(int ClipIndex, double Start, double Duration)> CalculateClipDurations(
        List<(string LocalPath, VideoClip Original)> clips,
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
            var clip = clips[i].Original;
            var clipDuration = clip.DurationSeconds > 0
                ? Math.Min(perClipDuration, clip.DurationSeconds)
                : perClipDuration;

            result.Add((i, 0, clipDuration));
        }

        return result;
    }

    private async Task ProcessSingleClipAsync(
        string inputPath,
        string outputPath,
        double targetDuration,
        ShortVideoConfig config,
        CancellationToken cancellationToken)
    {
        try
        {
            var mediaInfo = await FFmpeg.GetMediaInfo(inputPath, cancellationToken);
            var videoStream = mediaInfo.VideoStreams.FirstOrDefault();
            var audioStream = mediaInfo.AudioStreams.FirstOrDefault();

            if (videoStream == null)
            {
                _logger.LogWarning("No video stream found in: {Path}", inputPath);
                return;
            }

            var conversion = FFmpeg.Conversions.New();

            // Configure video stream - scale to 9:16 portrait with padding
            videoStream
                .SetSize(config.Width, config.Height)
                .SetFramerate(config.Fps)
                .SetCodec(VideoCodec.h264);

            // Trim to specified duration
            if (targetDuration > 0 && targetDuration < mediaInfo.Duration.TotalSeconds)
            {
                videoStream.Split(TimeSpan.Zero, TimeSpan.FromSeconds(targetDuration));
            }

            conversion.AddStream(videoStream);

            // Add audio if present
            if (audioStream != null)
            {
                audioStream.SetCodec(AudioCodec.aac);
                if (targetDuration > 0 && targetDuration < mediaInfo.Duration.TotalSeconds)
                {
                    audioStream.Split(TimeSpan.Zero, TimeSpan.FromSeconds(targetDuration));
                }
                conversion.AddStream(audioStream);
            }
            else
            {
                // Add silent audio if no audio stream
                conversion.AddParameter("-f lavfi -i anullsrc=channel_layout=stereo:sample_rate=44100 -shortest");
            }

            conversion
                .SetOutput(outputPath)
                .SetOverwriteOutput(true)
                .SetPreset(ConversionPreset.Fast)
                .UseMultiThread(true);

            await conversion.Start(cancellationToken);
            
            _logger.LogInformation("Processed clip: {Input} -> {Output}", inputPath, outputPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process clip: {Path}", inputPath);
        }
    }

    private async Task ConcatenateClipsAsync(
        List<string> clipPaths,
        string outputPath,
        CancellationToken cancellationToken)
    {
        try
        {
            // Create concat file list
            var concatListPath = Path.Combine(_tempDirectory, $"concat_{Guid.NewGuid():N}.txt");
            var concatContent = string.Join("\n", clipPaths.Select(p => $"file '{p.Replace("\\", "/").Replace("'", "'\\''")}'"));
            await File.WriteAllTextAsync(concatListPath, concatContent, cancellationToken);

            // Use concat demuxer for efficient concatenation
            var conversion = FFmpeg.Conversions.New()
                .AddParameter($"-f concat -safe 0 -i \"{concatListPath}\"")
                .AddParameter("-c copy")
                .SetOutput(outputPath)
                .SetOverwriteOutput(true);

            await conversion.Start(cancellationToken);

            // Cleanup concat list
            if (File.Exists(concatListPath))
            {
                File.Delete(concatListPath);
            }

            _logger.LogInformation("Concatenated {Count} clips to: {Path}", clipPaths.Count, outputPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to concatenate clips");
            throw;
        }
    }

    private void CleanupTempFiles(
        List<string> processedClips,
        List<(string LocalPath, VideoClip Original)> downloadedClips)
    {
        // Delete processed clips
        foreach (var path in processedClips)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete temp file: {Path}", path);
            }
        }

        // Delete downloaded clips (only if they were downloaded, not original local files)
        foreach (var (localPath, original) in downloadedClips)
        {
            // Only delete if this was a downloaded file (not from SourcePath)
            if (localPath != original.SourcePath && localPath.Contains(_tempDirectory))
            {
                try
                {
                    if (File.Exists(localPath))
                    {
                        File.Delete(localPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete downloaded file: {Path}", localPath);
                }
            }
        }
    }
}
