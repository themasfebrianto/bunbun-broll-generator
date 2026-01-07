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
    
    // Optimization settings
    private readonly bool _useHardwareAccel;
    private readonly string _preset;
    private readonly int _parallelClips;
    private readonly int _crf;
    private string? _hwEncoder = null;
    private bool _hwEncoderChecked = false;

    public ShortVideoComposer(ILogger<ShortVideoComposer> logger, IConfiguration config)
    {
        _logger = logger;
        _config = config;
        
        // FFmpeg binaries directory - use absolute paths
        _ffmpegDirectory = Path.GetFullPath(config["FFmpeg:BinaryDirectory"] 
            ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg-binaries"));
        _tempDirectory = Path.GetFullPath(config["FFmpeg:TempDirectory"] 
            ?? Path.Combine(Path.GetTempPath(), "bunbun_ffmpeg"));
        _outputDirectory = Path.GetFullPath(config["ShortVideo:OutputDirectory"] 
            ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "output", "shorts"));

        // Optimization settings
        _useHardwareAccel = config.GetValue("FFmpeg:UseHardwareAccel", true);
        _preset = config["FFmpeg:Preset"] ?? "veryfast";
        _parallelClips = Math.Clamp(config.GetValue("FFmpeg:ParallelClips", 3), 1, 8);
        _crf = Math.Clamp(config.GetValue("FFmpeg:CRF", 23), 18, 35);

        _logger.LogInformation("FFmpeg directories - Binaries: {Bin}, Temp: {Temp}, Output: {Out}", 
            _ffmpegDirectory, _tempDirectory, _outputDirectory);
        _logger.LogInformation("FFmpeg optimization - HwAccel: {Hw}, Preset: {Preset}, Parallel: {Para}, CRF: {Crf}",
            _useHardwareAccel, _preset, _parallelClips, _crf);

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
            // Check common locations for FFmpeg first
            progress?.Report("Checking for FFmpeg installation...");
            var ffmpegPath = await FindFFmpegAsync();
            
            if (!string.IsNullOrEmpty(ffmpegPath))
            {
                var directory = Path.GetDirectoryName(ffmpegPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    FFmpeg.SetExecutablesPath(directory);
                    _logger.LogInformation("FFmpeg found at: {Path}", ffmpegPath);
                    _isInitialized = true;
                    progress?.Report($"FFmpeg ready: {ffmpegPath}");
                    return true;
                }
            }

            // FFmpeg not found - try to download it automatically
            _logger.LogInformation("FFmpeg not found. Attempting automatic download to: {Dir}", _ffmpegDirectory);
            progress?.Report("FFmpeg not found. Downloading automatically (this may take a few minutes)...");
            
            try
            {
                // Ensure directory exists
                Directory.CreateDirectory(_ffmpegDirectory);
                
                // Use Xabe.FFmpeg.Downloader to get FFmpeg
                _logger.LogInformation("Starting FFmpeg download...");
                await Xabe.FFmpeg.Downloader.FFmpegDownloader.GetLatestVersion(
                    Xabe.FFmpeg.Downloader.FFmpegVersion.Official, 
                    _ffmpegDirectory
                );
                
                FFmpeg.SetExecutablesPath(_ffmpegDirectory);
                _isInitialized = true;
                
                _logger.LogInformation("FFmpeg downloaded successfully to: {Dir}", _ffmpegDirectory);
                progress?.Report("FFmpeg downloaded successfully!");
                return true;
            }
            catch (Exception downloadEx)
            {
                // Download failed - provide helpful error message
                _logger.LogError(downloadEx, "FFmpeg auto-download failed. Error: {Message}", downloadEx.Message);
                progress?.Report($"FFmpeg download failed: {downloadEx.Message}");
                
                // Provide manual download instructions
                var instructions = OperatingSystem.IsWindows()
                    ? "Please run: .\\download-ffmpeg.ps1 OR download from https://www.gyan.dev/ffmpeg/builds/"
                    : "Please install via: apt install ffmpeg OR brew install ffmpeg";
                
                _logger.LogWarning("Manual FFmpeg installation required. {Instructions}", instructions);
                progress?.Report(instructions);
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

            // Step 4: Process clips in PARALLEL for speedup
            progress?.Report(new CompositionProgress { Stage = "Processing", Percent = 40, Message = $"Processing {localClips.Count} clips (parallel x{_parallelClips})..." });
            
            // Prepare clip processing tasks
            var clipTasks = localClips.Zip(clipDurations).Select((pair, index) => new
            {
                Clip = pair.First,
                Duration = pair.Second.Duration,
                Index = index,
                OutputPath = Path.Combine(_tempDirectory, $"clip_{sessionId}_{index}.mp4")
            }).ToList();

            var processedClips = new string?[clipTasks.Count];
            var processedCount = 0;

            // Process clips in parallel with limited concurrency
            await Parallel.ForEachAsync(
                clipTasks,
                new ParallelOptions 
                { 
                    MaxDegreeOfParallelism = _parallelClips,
                    CancellationToken = cancellationToken 
                },
                async (task, ct) =>
                {
                    await ProcessSingleClipAsync(
                        task.Clip.LocalPath,
                        task.OutputPath,
                        task.Duration,
                        config,
                        ct
                    );

                    if (File.Exists(task.OutputPath))
                    {
                        processedClips[task.Index] = task.OutputPath;
                    }

                    var processed = Interlocked.Increment(ref processedCount);
                    var percent = 40 + (int)(processed * 30.0 / clipTasks.Count);
                    progress?.Report(new CompositionProgress
                    {
                        Stage = "Processing",
                        Percent = percent,
                        Message = $"Processed clip {processed}/{clipTasks.Count}..."
                    });
                }
            );

            // Filter out nulls and maintain order
            var orderedClips = processedClips.Where(p => p != null).Select(p => p!).ToList();

            if (orderedClips.Count == 0)
            {
                return new ShortVideoResult
                {
                    Success = false,
                    ErrorMessage = "No clips were successfully processed"
                };
            }

            // Step 5: Concatenate all processed clips with transitions
            progress?.Report(new CompositionProgress { Stage = "Concatenating", Percent = 75, Message = "Joining clips with transitions..." });
            await ConcatenateClipsWithTransitionsAsync(orderedClips, outputPath, config, cancellationToken);

            // Step 6: Cleanup temp files
            progress?.Report(new CompositionProgress { Stage = "Cleanup", Percent = 90, Message = "Cleaning up..." });
            CleanupTempFiles(orderedClips, localClips);

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

            if (videoStream == null)
            {
                _logger.LogWarning("No video stream found in: {Path}", inputPath);
                return;
            }

            var inputWidth = videoStream.Width;
            var inputHeight = videoStream.Height;
            var inputAspect = (double)inputWidth / inputHeight;
            var outputAspect = (double)config.Width / config.Height;

            // Build FFmpeg filter for blur background effect
            var filterComplex = BuildBlurBackgroundFilter(
                inputWidth, inputHeight, 
                config.Width, config.Height,
                inputAspect, outputAspect
            );

            // Build duration filter - use InvariantCulture to ensure dot as decimal separator
            var durationArg = targetDuration > 0 && targetDuration < mediaInfo.Duration.TotalSeconds
                ? $"-t {targetDuration.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}"
                : "";

            // Use raw FFmpeg command for complex filter
            var ffmpegPath = await FindFFmpegExecutablePathAsync();
            if (string.IsNullOrEmpty(ffmpegPath))
            {
                _logger.LogError("FFmpeg executable not found");
                return;
            }

            // Build FFmpeg arguments - use configurable preset and CRF for speed optimization
            // -threads 0 = use all available CPU cores
            var arguments = $"-threads 0 -i \"{inputPath}\" -filter_complex \"{filterComplex}\" " +
                           $"-map \"[vout]\" -map 0:a? " +
                           $"-c:v libx264 -preset {_preset} -crf {_crf} -threads 0 " +
                           $"-c:a aac -b:a 128k -ac 2 -ar 44100 " +
                           $"-r {config.Fps} {durationArg} " +
                           $"-y \"{outputPath}\"";

            _logger.LogDebug("FFmpeg command for clip processing: {Args}", arguments);

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("FFmpeg processing failed for {Path}. Exit code: {Code}. Error: {Error}", 
                    inputPath, process.ExitCode, stderr);
            }
            else if (File.Exists(outputPath))
            {
                var fileInfo = new FileInfo(outputPath);
                _logger.LogInformation("Processed clip: {Input} -> {Output} ({Size} bytes)", 
                    Path.GetFileName(inputPath), Path.GetFileName(outputPath), fileInfo.Length);
            }
            else
            {
                _logger.LogWarning("Output file not created for clip: {Path}", inputPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process clip: {Path}", inputPath);
        }
    }

    /// <summary>
    /// Builds FFmpeg filter_complex string for blur background effect.
    /// This creates a blurred, scaled background with the sharp video centered on top.
    /// </summary>
    private string BuildBlurBackgroundFilter(
        int inputWidth, int inputHeight,
        int outputWidth, int outputHeight,
        double inputAspect, double outputAspect)
    {
        // If input is already close to output aspect ratio, just scale
        if (Math.Abs(inputAspect - outputAspect) < 0.1)
        {
            return $"[0:v]scale={outputWidth}:{outputHeight}:force_original_aspect_ratio=decrease," +
                   $"pad={outputWidth}:{outputHeight}:(ow-iw)/2:(oh-ih)/2:black[vout]";
        }

        // Calculate foreground scale to fit within output while maintaining aspect ratio
        int fgWidth, fgHeight;
        if (inputAspect > outputAspect)
        {
            // Input is wider - fit to width
            fgWidth = outputWidth;
            fgHeight = (int)(outputWidth / inputAspect);
        }
        else
        {
            // Input is taller - fit to height
            fgHeight = outputHeight;
            fgWidth = (int)(outputHeight * inputAspect);
        }

        // Ensure even dimensions for video encoding
        fgWidth = (fgWidth / 2) * 2;
        fgHeight = (fgHeight / 2) * 2;

        // Build blur background filter (optimized for speed):
        // 1. Scale video to fill output (will crop) - using fast bilinear
        // 2. Apply lighter blur (radius 15 vs 25 for speed)
        // 3. Scale original video to fit (foreground)
        // 4. Overlay foreground centered on blurred background
        var filter = 
            // Background: scale to fill, crop to output size, then blur (lighter for speed)
            $"[0:v]scale={outputWidth}:{outputHeight}:force_original_aspect_ratio=increase:flags=fast_bilinear," +
            $"crop={outputWidth}:{outputHeight}," +
            $"boxblur=luma_radius=15:luma_power=1[bg];" +
            // Foreground: scale to fit within output
            $"[0:v]scale={fgWidth}:{fgHeight}:flags=fast_bilinear[fg];" +
            // Overlay foreground centered on background
            $"[bg][fg]overlay=(W-w)/2:(H-h)/2[vout]";

        return filter;
    }

    /// <summary>
    /// Find the FFmpeg executable path.
    /// </summary>
    private async Task<string?> FindFFmpegExecutablePathAsync()
    {
        var ffmpegPath = await FindFFmpegAsync();
        if (!string.IsNullOrEmpty(ffmpegPath))
        {
            return ffmpegPath;
        }

        // Try common locations
        var isWindows = OperatingSystem.IsWindows();
        var ffmpegName = isWindows ? "ffmpeg.exe" : "ffmpeg";
        var configuredPath = Path.Combine(_ffmpegDirectory, ffmpegName);
        
        return File.Exists(configuredPath) ? configuredPath : null;
    }

    /// <summary>
    /// Concatenate clips with transitions using FFmpeg xfade filter.
    /// </summary>
    private async Task ConcatenateClipsWithTransitionsAsync(
        List<string> clipPaths,
        string outputPath,
        ShortVideoConfig config,
        CancellationToken cancellationToken)
    {
        if (clipPaths.Count == 0)
        {
            _logger.LogWarning("No clips to concatenate");
            return;
        }

        // If only one clip, just copy it
        if (clipPaths.Count == 1)
        {
            _logger.LogInformation("Only one clip, copying directly to output");
            File.Copy(clipPaths[0], outputPath, overwrite: true);
            return;
        }

        // If transitions disabled or Cut type, use simple concatenation
        if (!config.AddTransitions || config.Transition == TransitionType.Cut)
        {
            await ConcatenateClipsAsync(clipPaths, outputPath, cancellationToken);
            return;
        }

        try
        {
            var ffmpegPath = await FindFFmpegExecutablePathAsync();
            if (string.IsNullOrEmpty(ffmpegPath))
            {
                throw new InvalidOperationException("FFmpeg executable not found");
            }

            var absoluteOutputPath = Path.GetFullPath(outputPath);
            var transitionName = config.Transition.GetFFmpegName();
            var transitionDurationStr = config.TransitionDuration.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

            // Get durations of all clips and check for audio streams
            var clipDurations = new List<double>();
            var hasAudioStreams = new List<bool>();
            
            foreach (var clipPath in clipPaths)
            {
                var duration = await GetVideoDurationAsync(clipPath, cancellationToken);
                clipDurations.Add(duration);
                
                // Check if clip has audio stream
                var hasAudio = await CheckHasAudioStreamAsync(clipPath, cancellationToken);
                hasAudioStreams.Add(hasAudio);
            }

            // If any clip is missing audio, use video-only transitions
            var allHaveAudio = hasAudioStreams.All(h => h);
            
            _logger.LogInformation("Transition processing: {Count} clips, all have audio: {HasAudio}", 
                clipPaths.Count, allHaveAudio);

            // Build xfade filter chain for multiple clips
            var filterParts = new List<string>();
            var inputArgs = new List<string>();
            
            // Add all input files
            for (int i = 0; i < clipPaths.Count; i++)
            {
                var escapedPath = clipPaths[i].Replace("\\", "/");
                inputArgs.Add($"-i \"{escapedPath}\"");
            }

            // Calculate offsets and build xfade chain
            // FIXED: Properly accumulate offset based on effective duration after overlap
            double cumulativeOffset = 0;
            string lastVideoLabel = "[0:v]";
            
            for (int i = 1; i < clipPaths.Count; i++)
            {
                var prevDuration = clipDurations[i - 1];
                
                // For the first clip, offset = duration - transition
                // For subsequent clips, we need to account for the overlap
                double offset;
                if (i == 1)
                {
                    offset = prevDuration - config.TransitionDuration;
                }
                else
                {
                    // Each previous clip's effective contribution is: duration - transitionDuration
                    // So cumulative offset = sum of (duration_k - transitionDuration) for k = 0 to i-1
                    cumulativeOffset += (clipDurations[i - 1] - config.TransitionDuration);
                    offset = cumulativeOffset;
                }
                
                if (i == 1) cumulativeOffset = offset;
                
                var offsetStr = Math.Max(0, offset).ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
                
                var outputVideoLabel = i == clipPaths.Count - 1 ? "[vout]" : $"[v{i}]";
                
                // Video transition using xfade
                filterParts.Add($"{lastVideoLabel}[{i}:v]xfade=transition={transitionName}:duration={transitionDurationStr}:offset={offsetStr}{outputVideoLabel}");
                
                lastVideoLabel = outputVideoLabel;
            }

            // Build audio filter - handle cases where clips may not have audio
            string audioFilter;
            if (allHaveAudio)
            {
                // All clips have audio - use acrossfade chain
                var audioFilterParts = new List<string>();
                string lastAudioLabel = "[0:a]";
                cumulativeOffset = 0;
                
                for (int i = 1; i < clipPaths.Count; i++)
                {
                    var prevDuration = clipDurations[i - 1];
                    
                    double offset;
                    if (i == 1)
                    {
                        offset = prevDuration - config.TransitionDuration;
                    }
                    else
                    {
                        cumulativeOffset += (clipDurations[i - 1] - config.TransitionDuration);
                        offset = cumulativeOffset;
                    }
                    if (i == 1) cumulativeOffset = offset;
                    
                    var outputAudioLabel = i == clipPaths.Count - 1 ? "[aout]" : $"[a{i}]";
                    
                    audioFilterParts.Add($"{lastAudioLabel}[{i}:a]acrossfade=d={transitionDurationStr}:c1=tri:c2=tri{outputAudioLabel}");
                    
                    lastAudioLabel = outputAudioLabel;
                }
                
                audioFilter = string.Join(";", audioFilterParts);
            }
            else
            {
                // Some clips missing audio - generate silent audio for the final output
                // Calculate total duration accounting for overlaps
                double totalDuration = clipDurations[0];
                for (int i = 1; i < clipDurations.Count; i++)
                {
                    totalDuration += clipDurations[i] - config.TransitionDuration;
                }
                
                var totalDurationStr = totalDuration.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
                audioFilter = $"anullsrc=channel_layout=stereo:sample_rate=44100,atrim=0:{totalDurationStr}[aout]";
                
                _logger.LogInformation("Some clips missing audio, generating silent audio track ({Duration}s)", totalDuration);
            }

            var filterComplex = string.Join(";", filterParts) + ";" + audioFilter;
            
            _logger.LogInformation("Concatenating {Count} clips with {Transition} transition ({Duration}s)", 
                clipPaths.Count, config.Transition.GetDisplayName(), config.TransitionDuration);
            _logger.LogDebug("Transition filter: {Filter}", filterComplex);

            var arguments = $"-threads 0 {string.Join(" ", inputArgs)} " +
                           $"-filter_complex \"{filterComplex}\" " +
                           $"-map \"[vout]\" -map \"[aout]\" " +
                           $"-c:v libx264 -preset {_preset} -crf {_crf} -threads 0 " +
                           $"-c:a aac -b:a 128k " +
                           $"-movflags +faststart " +
                           $"-y \"{absoluteOutputPath}\"";

            _logger.LogDebug("FFmpeg transition command: {Cmd}", arguments);

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("FFmpeg xfade with audio failed (Exit: {Code}). Trying video-only transition...", 
                    process.ExitCode);
                _logger.LogDebug("FFmpeg error: {Error}", stderr.Length > 1000 ? stderr[..1000] : stderr);
                
                // Try again with video-only transitions (no audio crossfade)
                var videoOnlySuccess = await TryVideoOnlyTransitionsAsync(
                    ffmpegPath, clipPaths, clipDurations, absoluteOutputPath, 
                    transitionName, config.TransitionDuration, cancellationToken);
                
                if (videoOnlySuccess)
                {
                    return;
                }
                
                _logger.LogWarning("Video-only transitions also failed. Falling back to simple concatenation.");
                await ConcatenateClipsAsync(clipPaths, outputPath, cancellationToken);
                return;
            }

            if (!File.Exists(absoluteOutputPath))
            {
                throw new InvalidOperationException("Output file was not created after transition concatenation");
            }

            var fileInfo = new FileInfo(absoluteOutputPath);
            _logger.LogInformation("Successfully concatenated {Count} clips with transitions to: {Path} ({Size} bytes)", 
                clipPaths.Count, absoluteOutputPath, fileInfo.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to concatenate clips with transitions, falling back to simple concat");
            await ConcatenateClipsAsync(clipPaths, outputPath, cancellationToken);
        }
    }

    /// <summary>
    /// Check if a video file has an audio stream.
    /// </summary>
    private async Task<bool> CheckHasAudioStreamAsync(string videoPath, CancellationToken cancellationToken)
    {
        try
        {
            var mediaInfo = await FFmpeg.GetMediaInfo(videoPath, cancellationToken);
            return mediaInfo.AudioStreams.Any();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check audio stream for {Path}, assuming no audio", videoPath);
            return false;
        }
    }

    /// <summary>
    /// Try to apply video-only transitions (without audio crossfade).
    /// </summary>
    private async Task<bool> TryVideoOnlyTransitionsAsync(
        string ffmpegPath,
        List<string> clipPaths,
        List<double> clipDurations,
        string outputPath,
        string transitionName,
        double transitionDuration,
        CancellationToken cancellationToken)
    {
        try
        {
            var transitionDurationStr = transitionDuration.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            
            var filterParts = new List<string>();
            var inputArgs = new List<string>();
            
            for (int i = 0; i < clipPaths.Count; i++)
            {
                var escapedPath = clipPaths[i].Replace("\\", "/");
                inputArgs.Add($"-i \"{escapedPath}\"");
            }

            // Video xfade chain
            double cumulativeOffset = 0;
            string lastVideoLabel = "[0:v]";
            
            for (int i = 1; i < clipPaths.Count; i++)
            {
                var prevDuration = clipDurations[i - 1];
                
                double offset;
                if (i == 1)
                {
                    offset = prevDuration - transitionDuration;
                }
                else
                {
                    cumulativeOffset += (clipDurations[i - 1] - transitionDuration);
                    offset = cumulativeOffset;
                }
                if (i == 1) cumulativeOffset = offset;
                
                var offsetStr = Math.Max(0, offset).ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
                var outputVideoLabel = i == clipPaths.Count - 1 ? "[vout]" : $"[v{i}]";
                
                filterParts.Add($"{lastVideoLabel}[{i}:v]xfade=transition={transitionName}:duration={transitionDurationStr}:offset={offsetStr}{outputVideoLabel}");
                lastVideoLabel = outputVideoLabel;
            }

            // Generate silent audio for the total duration
            double totalDuration = clipDurations[0];
            for (int i = 1; i < clipDurations.Count; i++)
            {
                totalDuration += clipDurations[i] - transitionDuration;
            }
            var totalDurationStr = totalDuration.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
            filterParts.Add($"anullsrc=channel_layout=stereo:sample_rate=44100,atrim=0:{totalDurationStr}[aout]");

            var filterComplex = string.Join(";", filterParts);
            
            var arguments = $"{string.Join(" ", inputArgs)} " +
                           $"-filter_complex \"{filterComplex}\" " +
                           $"-map \"[vout]\" -map \"[aout]\" " +
                           $"-c:v libx264 -preset {_preset} -crf {_crf} " +
                           $"-c:a aac -b:a 128k " +
                           $"-movflags +faststart " +
                           $"-y \"{outputPath}\"";

            _logger.LogDebug("FFmpeg video-only transition command: {Cmd}", arguments);

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode == 0 && File.Exists(outputPath))
            {
                var fileInfo = new FileInfo(outputPath);
                _logger.LogInformation("Successfully created video with video-only transitions: {Path} ({Size} bytes)", 
                    outputPath, fileInfo.Length);
                return true;
            }

            _logger.LogWarning("Video-only transition failed. Exit: {Code}", process.ExitCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Video-only transition attempt failed");
            return false;
        }
    }

    private async Task ConcatenateClipsAsync(
        List<string> clipPaths,
        string outputPath,
        CancellationToken cancellationToken)
    {
        if (clipPaths.Count == 0)
        {
            _logger.LogWarning("No clips to concatenate");
            return;
        }

        // If only one clip, just copy it
        if (clipPaths.Count == 1)
        {
            _logger.LogInformation("Only one clip, copying directly to output");
            File.Copy(clipPaths[0], outputPath, overwrite: true);
            return;
        }

        try
        {
            var ffmpegPath = await FindFFmpegExecutablePathAsync();
            if (string.IsNullOrEmpty(ffmpegPath))
            {
                throw new InvalidOperationException("FFmpeg executable not found");
            }

            // Create concat file list with absolute paths
            var concatListPath = Path.Combine(_tempDirectory, $"concat_{Guid.NewGuid():N}.txt");
            
            // Ensure all paths are absolute and escaped for FFmpeg
            var absolutePaths = clipPaths.Select(p => Path.GetFullPath(p)).ToList();
            
            // Create concat content with properly escaped paths (use forward slashes for FFmpeg)
            var concatContent = string.Join("\n", absolutePaths.Select(p => 
                $"file '{p.Replace("\\", "/").Replace("'", "'\\''")}'"));
            
            _logger.LogInformation("Concatenating {Count} clips", clipPaths.Count);
            _logger.LogDebug("Concat file content:\n{Content}", concatContent);
            
            await File.WriteAllTextAsync(concatListPath, concatContent, cancellationToken);

            // Use concat demuxer with re-encoding for reliability
            var absoluteOutputPath = Path.GetFullPath(outputPath);
            
            // FFmpeg command: concat with re-encoding to ensure compatibility
            var arguments = $"-f concat -safe 0 -i \"{concatListPath}\" " +
                           $"-c:v libx264 -preset {_preset} -crf {_crf} " +
                           $"-c:a aac -b:a 128k " +
                           $"-movflags +faststart " +
                           $"-y \"{absoluteOutputPath}\"";

            _logger.LogDebug("FFmpeg concat command: {Cmd}", $"{ffmpegPath} {arguments}");

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(cancellationToken);

            // Cleanup concat list
            if (File.Exists(concatListPath))
            {
                File.Delete(concatListPath);
            }

            if (process.ExitCode != 0)
            {
                _logger.LogError("FFmpeg concat failed. Exit code: {Code}. Error: {Error}", 
                    process.ExitCode, stderr);
                throw new InvalidOperationException($"FFmpeg concatenation failed: {stderr}");
            }

            if (!File.Exists(absoluteOutputPath))
            {
                throw new InvalidOperationException("Output file was not created after concatenation");
            }

            var fileInfo = new FileInfo(absoluteOutputPath);
            _logger.LogInformation("Successfully concatenated {Count} clips to: {Path} ({Size} bytes)", 
                clipPaths.Count, absoluteOutputPath, fileInfo.Length);
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
