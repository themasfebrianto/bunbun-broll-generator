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
    /// Convert an image to a video clip with Ken Burns motion.
    /// </summary>
    Task<string?> ConvertImageToVideoAsync(
        string imagePath,
        double durationSeconds,
        ShortVideoConfig config,
        KenBurnsMotionType motionType = KenBurnsMotionType.SlowZoomIn,
        CancellationToken cancellationToken = default
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
        ShortVideoConfig config,
        CancellationToken cancellationToken = default,
        bool isPreview = false
    );

    /// <summary>
    /// Apply separate filter and texture to a video.
    /// </summary>
    Task<string?> ApplyFilterAndTextureToVideoAsync(
        string inputPath,
        VideoFilter filter,
        VideoTexture texture,
        ShortVideoConfig config,
        CancellationToken cancellationToken = default,
        bool isPreview = false
    );
}

/// <summary>
/// Composes short videos using Xabe.FFmpeg wrapper.
/// </summary>
public class ShortVideoComposer : IShortVideoComposer
{
    private readonly ILogger<ShortVideoComposer> _logger;
    private readonly IConfiguration _config;
    private readonly KenBurnsService _kenBurnsService;
    private readonly VideoStyleSettings _styleSettings;
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

    public ShortVideoComposer(
        ILogger<ShortVideoComposer> logger, 
        IConfiguration config,
        KenBurnsService kenBurnsService,
        VideoStyleSettings styleSettings)
    {
        _logger = logger;
        _config = config;
        _kenBurnsService = kenBurnsService;
        _styleSettings = styleSettings;
        
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

        // Ensure texture directory exists and copy sample textures if needed
        EnsureTextureDirectory();
    }

    private void EnsureTextureDirectory()
    {
        var textureDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "wwwroot", "assets", "textures");
        
        if (!Directory.Exists(textureDir))
        {
            Directory.CreateDirectory(textureDir);
            _logger.LogInformation("Created texture directory: {Dir}", textureDir);
        }

        // Supported texture extensions (image + video)
        var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".tiff", ".webp" };
        var videoExtensions = new[] { ".mp4", ".mov", ".webm", ".avi", ".mkv", ".m4v" };
        var allExtensions = imageExtensions.Concat(videoExtensions).ToArray();
        
        bool IsTextureFile(string f) => allExtensions.Any(ext => 
            f.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

        // Cek apakah ada texture di project root (development)
        var sourceTextureDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "wwwroot", "assets", "textures");
        var sourceFullPath = Path.GetFullPath(sourceTextureDir);
        
        if (Directory.Exists(sourceFullPath))
        {
            var textureFiles = Directory.GetFiles(sourceFullPath, "*.*")
                .Where(IsTextureFile);
            
            foreach (var file in textureFiles)
            {
                var destPath = Path.Combine(textureDir, Path.GetFileName(file));
                if (!File.Exists(destPath))
                {
                    try
                    {
                        File.Copy(file, destPath, true);
                        _logger.LogInformation("Copied texture: {File}", Path.GetFileName(file));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("Failed to copy texture {File}: {Error}", Path.GetFileName(file), ex.Message);
                    }
                }
            }
        }

        // Generate default canvas texture jika masih kosong (no image or video textures)
        var existingTextures = Directory.GetFiles(textureDir, "*.*")
            .Where(IsTextureFile)
            .ToList();
        
        if (existingTextures.Count == 0)
        {
            _logger.LogInformation("No textures found. Generating default canvas texture...");
            GenerateDefaultTexture(textureDir);
        }
        else
        {
            _logger.LogInformation("Found {Count} existing textures: {Files}", 
                existingTextures.Count, 
                string.Join(", ", existingTextures.Select(Path.GetFileName)));
        }
    }

    private void GenerateDefaultTexture(string textureDir)
    {
        try
        {
            // Generate simple canvas texture using FFmpeg
            var texturePath = Path.Combine(textureDir, "canvas_texture.jpg");
            
            // Create a simple noise pattern as texture
            var ffmpegPath = FindFFmpegAsync().GetAwaiter().GetResult();
            if (!string.IsNullOrEmpty(ffmpegPath))
            {
                var args = $"-f lavfi -i color=c=gray:s=512x512 -f lavfi -i noise=alls=20:allf=t+u " +
                          $"-filter_complex \"[0:v][1:v]blend=all_mode=overlay,format=yuv420p\" " +
                          $"-frames:v 1 -y \"{texturePath}\"";
                
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = ffmpegPath,
                        Arguments = args,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                
                process.Start();
                process.WaitForExit();
                
                if (File.Exists(texturePath))
                {
                    _logger.LogInformation("Generated default canvas texture: {Path}", texturePath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to generate default texture: {Error}", ex.Message);
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
            
            // Read output streams asynchronously
            var stderrTask = process.StandardError.ReadToEndAsync();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            
            await process.WaitForExitAsync();
            var output = await stdoutTask;
            // We can ignore stderr for 'which' command usually, but we must read it to avoid deadlock

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

    public async Task<string?> ConvertImageToVideoAsync(
        string imagePath,
        double durationSeconds,
        ShortVideoConfig config,
        KenBurnsMotionType motionType = KenBurnsMotionType.SlowZoomIn,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await EnsureFFmpegAsync()) return null;

            // Generate output path in temp directory
            var outputPath = Path.Combine(_tempDirectory, $"kb_{Guid.NewGuid():N}.mp4");

            var success = await _kenBurnsService.ConvertImageToVideoAsync(
                imagePath,
                outputPath,
                durationSeconds,
                config.Width,
                config.Height,
                motionType,
                cancellationToken
            );

            return success ? outputPath : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to convert image to video: {Path}", imagePath);
            return null;
        }
    }

    /// <summary>
    /// Apply separate filter and texture to a video.
    /// </summary>
    public async Task<string?> ApplyFilterAndTextureToVideoAsync(
        string inputPath,
        VideoFilter filter,
        VideoTexture texture,
        ShortVideoConfig config,
        CancellationToken cancellationToken = default,
        bool isPreview = false)
    {
        try
        {
            if (!await EnsureFFmpegAsync()) return null;

            var outputPath = Path.Combine(_tempDirectory, $"styled_{Guid.NewGuid():N}.mp4");

            var mediaInfo = await FFmpeg.GetMediaInfo(inputPath, cancellationToken);
            var videoStream = mediaInfo.VideoStreams.FirstOrDefault();

            if (videoStream == null)
            {
                _logger.LogWarning("No video stream found in: {Path}", inputPath);
                return null;
            }

            var inputWidth = videoStream.Width;
            var inputHeight = videoStream.Height;
            var inputAspect = (double)inputWidth / inputHeight;
            
            // Preview Optimization: Downscale to 480p
            int targetWidth = config.Width;
            int targetHeight = config.Height;
            
            if (isPreview)
            {
                targetHeight = 480;
                targetWidth = (int)(targetHeight * inputAspect);
                targetWidth = (targetWidth / 2) * 2;
                if (targetWidth > 854) targetWidth = 854; 
            }

            var outputAspect = (double)targetWidth / targetHeight;

            // Get texture source (image or video)
            var textureSource = GetTexturePath(texture);
            bool hasTexture = textureSource != null && File.Exists(textureSource.Path);
            
            if (texture != VideoTexture.None)
            {
                _logger.LogInformation("Texture requested: {Texture}, Path: {Path}, IsVideo: {IsVideo}, Exists: {Exists}", 
                    texture, textureSource?.Path ?? "null", textureSource?.IsVideo ?? false, hasTexture);
            }

            // Build blur background filter
            var bgFilter = BuildBlurBackgroundFilter(
                inputWidth, inputHeight, 
                targetWidth, targetHeight,
                inputAspect, outputAspect
            );

            // Build artistic filter with separate filter + texture
            bool isVideoTexture = textureSource?.IsVideo ?? false;
            string filterComplex;
            var artFilter = BuildArtisticFilterComplex(filter, texture, "[vout]", "[styled]", hasTexture, isVideoTexture, targetWidth, targetHeight);
            
            filterComplex = bgFilter + ";" + artFilter;

            var ffmpegPath = await FindFFmpegExecutablePathAsync();
            if (string.IsNullOrEmpty(ffmpegPath)) return null;

            var inputs = $"-threads 0 -i \"{inputPath}\"";
            if (hasTexture && textureSource != null)
            {
                if (textureSource.IsVideo)
                {
                    // For video textures: use stream_loop to repeat the video infinitely
                    inputs += $" -stream_loop -1 -i \"{textureSource.Path}\"";
                }
                else
                {
                    // For image textures: loop the image
                    inputs += $" -f image2 -loop 1 -i \"{textureSource.Path}\"";
                }
            }

            var durationLimit = "";
            if (isPreview && mediaInfo.Duration.TotalSeconds > 15)
            {
                durationLimit = "-t 15";
            }
            
            var preset = isPreview ? "ultrafast" : _preset;
            var crf = isPreview ? 28 : _crf;

            var arguments = $"{inputs} -filter_complex \"{filterComplex}\" " +
                           $"-map \"[styled]\" -map 0:a? " +
                           $"-c:v libx264 -preset {preset} -crf {crf} -threads 0 " +
                           $"-c:a aac -b:a 128k -ac 2 -ar 44100 " +
                           $"{durationLimit} -y \"{outputPath}\"";

            _logger.LogInformation("Applying filter {Filter} + texture {Texture} to {Path} -> {Output}", 
                filter, texture, inputPath, outputPath);
            _logger.LogDebug("FFmpeg filter_complex: {FilterComplex}", filterComplex);
            _logger.LogDebug("FFmpeg inputs: {Inputs}", inputs);

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
            var stderrTask = process.StandardError.ReadToEndAsync();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(60));
            
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                var partialStderr = await stderrTask;
                _logger.LogWarning("FFmpeg filter preview timed out after 60s.");
                throw new TimeoutException($"Filter timed out after 60 seconds.");
            }
            
            var stderr = await stderrTask;
            var stdout = await stdoutTask;

            if (process.ExitCode != 0)
            {
                var stderrLines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var errorSummary = string.Join(" | ", stderrLines.TakeLast(3)).Trim();
                _logger.LogWarning("FFmpeg style failed [{Filter}+{Texture}]. Error: {Error}", 
                    filter, texture, errorSummary);
                throw new InvalidOperationException($"FFmpeg filter failed: {errorSummary}");
            }

            if (File.Exists(outputPath))
            {
                _logger.LogInformation("Filter + Texture applied successfully -> {Output}", outputPath);
                return outputPath;
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply filter/texture to video: {Path}", inputPath);
            return null;
        }
    }

    /// <summary>
    /// Represents a texture source - can be image or video
    /// </summary>
    private record TextureSource(string Path, bool IsVideo);

    private TextureSource? GetTexturePath(VideoTexture texture)
    {
        if (texture == VideoTexture.None)
            return null;

        // Cari texture di beberapa lokasi (dalam urutan prioritas)
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var searchPaths = new[]
        {
            // 1. Relative to executable (published/output folder)
            Path.Combine(baseDir, "wwwroot", "assets", "textures"),
            // 2. Project root folder (development)
            Path.Combine(baseDir, "..", "..", "..", "wwwroot", "assets", "textures"),
            // 3. Working directory
            Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "assets", "textures"),
            // 4. Absolute path from project root
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "wwwroot", "assets", "textures"),
        };

        string? textureDir = null;
        foreach (var path in searchPaths)
        {
            var fullPath = Path.GetFullPath(path);
            if (Directory.Exists(fullPath))
            {
                textureDir = fullPath;
                _logger.LogDebug("Texture directory found: {Dir}", fullPath);
                break;
            }
        }

        if (textureDir == null)
        {
            _logger.LogWarning("Texture directory not found in any of these locations: {Paths}", 
                string.Join(", ", searchPaths.Select(p => Path.GetFullPath(p))));
            return null;
        }

        // Available video texture files:
        // filmgrain.mp4, filmgrain_2.mp4, filmgrain_colorfull.mp4, 
        // fire.mp4, grunge.mp4, harsh.mp4, scratches.mp4, surreal.mp4
        
        // Explicit filename mapping for reliability
        var preferredFiles = texture switch
        {
            VideoTexture.Canvas => new[] { "surreal.mp4", "harsh.mp4" },
            VideoTexture.Paper => new[] { "harsh.mp4", "filmgrain.mp4" },
            VideoTexture.Grunge => new[] { "grunge.mp4", "harsh.mp4" },
            VideoTexture.FilmGrain => new[] { "filmgrain.mp4", "filmgrain_2.mp4", "filmgrain_colorfull.mp4" },
            VideoTexture.Dust => new[] { "fire.mp4", "harsh.mp4" },
            VideoTexture.Scratches => new[] { "scratches.mp4" },
            _ => Array.Empty<string>()
        };

        // Try explicit filenames first
        foreach (var filename in preferredFiles)
        {
            var fullPath = Path.Combine(textureDir, filename);
            if (File.Exists(fullPath))
            {
                _logger.LogInformation("Video texture found for {Texture}: {Path}", texture, fullPath);
                return new TextureSource(fullPath, true);
            }
        }

        var videoExtensions = new[] { ".mp4", ".mov", ".webm", ".avi", ".mkv", ".m4v" };
        var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".tiff", ".webp" };

        // List all files in texture dir for debugging
        var allFiles = Directory.GetFiles(textureDir, "*.*");
        _logger.LogDebug("Texture directory {Dir} contains {Count} files: {Files}", 
            textureDir, allFiles.Length, string.Join(", ", allFiles.Select(Path.GetFileName)));

        // Pattern fallback
        var patterns = texture switch
        {
            VideoTexture.Canvas => new[] { "surreal", "harsh", "grain", "canvas" },
            VideoTexture.Paper => new[] { "harsh", "grain", "paper" },
            VideoTexture.Grunge => new[] { "grunge", "harsh", "rough" },
            VideoTexture.FilmGrain => new[] { "filmgrain", "grain", "film" },
            VideoTexture.Dust => new[] { "fire", "harsh", "dust" },
            VideoTexture.Scratches => new[] { "scratches", "scratch" },
            _ => Array.Empty<string>()
        };

        // Try pattern matching on video files
        foreach (var pattern in patterns)
        {
            var searchPattern = $"*{pattern}*.*";
            var files = Directory.GetFiles(textureDir, searchPattern, SearchOption.TopDirectoryOnly)
                .Where(f => videoExtensions.Any(ext => 
                    f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            
            if (files.Length > 0)
            {
                var selected = files[Random.Shared.Next(files.Length)];
                _logger.LogInformation("Video texture found for {Texture}: {Path}", texture, selected);
                return new TextureSource(selected, true);
            }
        }

        // Fallback to image textures
        foreach (var pattern in patterns)
        {
            var files = Directory.GetFiles(textureDir, pattern + ".*", SearchOption.TopDirectoryOnly)
                .Where(f => imageExtensions.Any(ext => 
                    f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            
            if (files.Length > 0)
            {
                var selected = files[Random.Shared.Next(files.Length)];
                _logger.LogInformation("Image texture found for {Texture}: {Path}", texture, selected);
                return new TextureSource(selected, false);
            }
        }

        // Fallback to any texture file
        var allTextures = Directory.GetFiles(textureDir, "*.*")
            .Where(f => videoExtensions.Concat(imageExtensions).Any(ext => 
                f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        
        if (allTextures.Length > 0)
        {
            var fallback = allTextures[Random.Shared.Next(allTextures.Length)];
            var isVideo = videoExtensions.Any(ext => 
                fallback.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
            _logger.LogInformation("Using fallback texture: {Path} (Video: {IsVideo})", fallback, isVideo);
            return new TextureSource(fallback, isVideo);
        }
        
        _logger.LogWarning("No texture files found in: {Dir}", textureDir);
        return null;
    }

    public async Task<string?> ApplyStyleToVideoAsync(
        string inputPath,
        VideoStyle style,
        ShortVideoConfig config,
        CancellationToken cancellationToken = default,
        bool isPreview = false)
    {
        try
        {
            if (!await EnsureFFmpegAsync()) return null;

            var outputPath = Path.Combine(_tempDirectory, $"styled_{Guid.NewGuid():N}.mp4");

            var mediaInfo = await FFmpeg.GetMediaInfo(inputPath, cancellationToken);
            var videoStream = mediaInfo.VideoStreams.FirstOrDefault();

            if (videoStream == null)
            {
                _logger.LogWarning("No video stream found in: {Path}", inputPath);
                return null;
            }

            var inputWidth = videoStream.Width;
            var inputHeight = videoStream.Height;
            var inputAspect = (double)inputWidth / inputHeight;
            
            // Preview Optimization: Downscale to 480p
            int targetWidth = config.Width;
            int targetHeight = config.Height;
            
            if (isPreview)
            {
                targetHeight = 480;
                targetWidth = (int)(targetHeight * inputAspect);
                // Ensure even dimensions
                targetWidth = (targetWidth / 2) * 2;
                // Cap width if it gets too crazy (e.g. ultra-wide)
                if (targetWidth > 854) targetWidth = 854; 
            }

            var outputAspect = (double)targetWidth / targetHeight;

            // For legacy VideoStyle.Canvas, also try to find video textures
            TextureSource? textureSource = null;
            if (style == VideoStyle.Canvas)
            {
                textureSource = GetTexturePath(VideoTexture.Canvas);
            }
            
            bool hasTexture = textureSource != null;
            bool isVideoTexture = textureSource?.IsVideo ?? false;

            // reuse BlurBackground filter
            var bgFilter = BuildBlurBackgroundFilter(
                inputWidth, inputHeight, 
                targetWidth, targetHeight,
                inputAspect, outputAspect
            );

            string filterComplex;
            var artFilter = BuildArtisticFilter(style, "[vout]", "[styled]", hasTexture, isVideoTexture, targetWidth, targetHeight);
            
            if (hasTexture)
            {
                filterComplex = bgFilter + ";" + artFilter;
            }
            else
            {
                filterComplex = bgFilter + ";" + artFilter;
            }

            var ffmpegPath = await FindFFmpegExecutablePathAsync();
            if (string.IsNullOrEmpty(ffmpegPath)) return null;

            var inputs = $"-threads 0 -i \"{inputPath}\"";
            if (hasTexture && textureSource != null)
            {
                if (textureSource.IsVideo)
                {
                    // For video textures: use stream_loop to repeat the video infinitely
                    inputs += $" -stream_loop -1 -i \"{textureSource.Path}\"";
                }
                else
                {
                    // For image textures: loop the image
                    inputs += $" -f image2 -loop 1 -i \"{textureSource.Path}\"";
                }
            }

            // Cap preview duration to 15s to avoid long FFmpeg processing
            var durationLimit = "";
            if (mediaInfo.Duration.TotalSeconds > 15)
            {
                durationLimit = "-t 15";
            }
            
            // Preview Optimization: Use ultrafast preset
            var preset = isPreview ? "ultrafast" : _preset;
            var crf = isPreview ? 28 : _crf;

            var arguments = $"{inputs} -filter_complex \"{filterComplex}\" " +
                           $"-map \"[styled]\" -map 0:a? " +
                           $"-c:v libx264 -preset {preset} -crf {crf} -threads 0 " +
                           $"-c:a aac -b:a 128k -ac 2 -ar 44100 " +
                           $"{durationLimit} -y \"{outputPath}\"";

            _logger.LogInformation("Applying style {Style} to {Path} -> {Output}", style, inputPath, outputPath);
            _logger.LogDebug("FFmpeg filter command: {Cmd}", arguments);

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
            
            // Read output streams asynchronously to prevent deadlock
            var stderrTask = process.StandardError.ReadToEndAsync();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            
            // Timeout: 60 seconds max for preview filter
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(60));
            
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Timeout hit, not user cancellation
                try { process.Kill(entireProcessTree: true); } catch { }
                var partialStderr = await stderrTask;
                _logger.LogWarning("FFmpeg style preview timed out after 60s. Partial stderr: {Err}", 
                    partialStderr.Length > 500 ? partialStderr[^500..] : partialStderr);
                throw new TimeoutException($"Filter timed out after 60 seconds. The video may be too long or complex.");
            }
            
            var stderr = await stderrTask;
            var stdout = await stdoutTask;

            if (process.ExitCode != 0)
            {
                // Extract last few lines of stderr for a meaningful error
                var stderrLines = stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                var errorSummary = string.Join(" | ", stderrLines.TakeLast(3)).Trim();
                _logger.LogWarning("FFmpeg style failed [{Style}]. Exit: {Code}. Error: {Error}", 
                    style, process.ExitCode, errorSummary);
                throw new InvalidOperationException($"FFmpeg filter failed: {errorSummary}");
            }

            if (File.Exists(outputPath))
            {
                _logger.LogInformation("Style {Style} applied successfully -> {Output}", style, outputPath);
                return outputPath;
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply style to video: {Path}", inputPath);
            return null;
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
                    string? inputPath = task.Clip.LocalPath;
                    string? tempImageVideo = null;

                    // Support image-to-video with Ken Burns if it's an image
                    if (task.Clip.Original.IsImage)
                    {
                        tempImageVideo = await ConvertImageToVideoAsync(
                            task.Clip.Original.ImagePath,
                            task.Duration,
                            config,
                            task.Clip.Original.MotionType,
                            ct
                        );

                        if (string.IsNullOrEmpty(tempImageVideo) || !File.Exists(tempImageVideo))
                        {
                            _logger.LogWarning("Failed to convert image to video: {Path}", task.Clip.Original.ImagePath);
                            return;
                        }
                        inputPath = tempImageVideo;
                    }

                    if (!string.IsNullOrEmpty(inputPath))
                    {
                        await ProcessSingleClipAsync(
                            inputPath,
                            task.OutputPath,
                            task.Duration,
                            config,
                            ct,
                            task.Clip.Original.IsImage,                             // Pass source type
                            task.Clip.Original.Style ?? VideoStyle.None,            // Pass per-clip style
                            task.Clip.Original.Filter,                              // Pass per-clip filter
                            task.Clip.Original.Texture                              // Pass per-clip texture
                        );

                        if (File.Exists(task.OutputPath))
                        {
                            processedClips[task.Index] = task.OutputPath;
                        }
                    }

                    // Cleanup the temporary image-video if created
                    if (!string.IsNullOrEmpty(tempImageVideo) && File.Exists(tempImageVideo))
                    {
                        try { File.Delete(tempImageVideo); } catch { }
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
        var results = new (string? LocalPath, VideoClip Original)?[clips.Count];
        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromMinutes(5);
        var downloadedCount = 0;

        progress?.Report(new CompositionProgress
        {
            Stage = "Downloading",
            Percent = 10,
            Message = $"Downloading {clips.Count} clips in parallel..."
        });

        // Download clips in parallel for speed
        await Parallel.ForEachAsync(
            clips.Select((clip, index) => (clip, index)),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = _parallelClips,
                CancellationToken = cancellationToken
            },
            async (item, ct) =>
            {
                var (clip, index) = item;
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
                        
                        var response = await httpClient.GetAsync(clip.SourceUrl, ct);
                        response.EnsureSuccessStatusCode();
                        
                        var content = await response.Content.ReadAsByteArrayAsync(ct);
                        await File.WriteAllBytesAsync(localPath, content, ct);
                        
                        _logger.LogDebug("Downloaded clip {Index}: {Path} ({Size} bytes)", 
                            index, Path.GetFileName(localPath), content.Length);
                    }
                    else
                    {
                        _logger.LogWarning("Clip {Index} has no source path or URL, skipping", index);
                        return;
                    }

                    results[index] = (localPath, clip);
                    
                    var count = Interlocked.Increment(ref downloadedCount);
                    var percent = 10 + (int)(count * 20.0 / clips.Count);
                    progress?.Report(new CompositionProgress
                    {
                        Stage = "Downloading",
                        Percent = percent,
                        Message = $"Downloaded {count}/{clips.Count} clips..."
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to download clip {Index}: {Url}", index, clip.SourceUrl);
                }
            }
        );

        // Filter nulls and maintain order
        return results
            .Where(r => r.HasValue && r.Value.LocalPath != null)
            .Select(r => (r!.Value.LocalPath!, r.Value.Original))
            .ToList();
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
        CancellationToken cancellationToken,
        bool isImageSource = false,
        VideoStyle overrideStyle = VideoStyle.None,
        VideoFilter filter = VideoFilter.None,
        VideoTexture texture = VideoTexture.None)
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

            // Determine effective style: override takes precedence, otherwise fallback to config
            var effectiveStyle = overrideStyle != VideoStyle.None ? overrideStyle : config.Style;

            // Determine effective filter and texture
            // Explicit filter/texture takes precedence, otherwise convert from style
            var effectiveFilter = filter;
            var effectiveTexture = texture;
            
            // If filter is None but style is set, convert style to filter/texture
            if (effectiveFilter == VideoFilter.None && effectiveTexture == VideoTexture.None && effectiveStyle != VideoStyle.None)
            {
                (effectiveFilter, effectiveTexture) = effectiveStyle switch
                {
                    VideoStyle.Painting => (VideoFilter.Painting, VideoTexture.None),
                    VideoStyle.Sepia => (VideoFilter.Sepia, VideoTexture.None),
                    VideoStyle.Canvas => (VideoFilter.Painting, VideoTexture.Canvas),
                    _ => (VideoFilter.None, VideoTexture.None)
                };
            }

            // Determine if we should apply artistic style
            // NOW: Apply to BOTH B-roll videos AND Ken Burns (image) clips if filter/texture is set
            var applyFilter = effectiveFilter != VideoFilter.None || effectiveTexture != VideoTexture.None;
            
            // Check for texture file if texture is specified
            string? texturePath = null;
            bool isVideoTexture = false;
            if (effectiveTexture != VideoTexture.None)
            {
                var textureSource = GetTexturePath(effectiveTexture);
                if (textureSource != null)
                {
                    texturePath = textureSource.Path;
                    isVideoTexture = textureSource.IsVideo;
                }
            }
            // Legacy Canvas style texture lookup (only if no explicit filter/texture)
            else if (effectiveStyle == VideoStyle.Canvas && effectiveTexture == VideoTexture.None)
            {
                var textureSource = GetTexturePath(VideoTexture.Canvas);
                if (textureSource != null)
                {
                    texturePath = textureSource.Path;
                    isVideoTexture = textureSource.IsVideo;
                }
            }

            var inputWidth = videoStream.Width;
            var inputHeight = videoStream.Height;
            var inputAspect = (double)inputWidth / inputHeight;
            var outputAspect = (double)config.Width / config.Height;

            // Build FFmpeg filter for blur background effect
            var bgFilter = BuildBlurBackgroundFilter(
                inputWidth, inputHeight, 
                config.Width, config.Height,
                inputAspect, outputAspect
            );
            
            // Integrate Artistic Style if enabled
            string filterComplex;
            if (applyFilter)
            {
                // artistic filter takes [vout] from bgFilter and outputs [styled]
                var artFilter = BuildArtisticFilterComplex(effectiveFilter, effectiveTexture, "[vout]", "[styled]", texturePath != null, isVideoTexture, config.Width, config.Height);
                
                filterComplex = bgFilter + ";" + artFilter;
            }
            else if (effectiveStyle != VideoStyle.None)
            {
                // Legacy style support
                var artFilter = BuildArtisticFilter(effectiveStyle, "[vout]", "[styled]", texturePath != null, isVideoTexture, config.Width, config.Height);
                filterComplex = bgFilter + ";" + artFilter;
            }
            else
            {
                filterComplex = bgFilter;
            }

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
            // Build inputs
            var inputs = $"-threads 0 -i \"{inputPath}\"";

            if (texturePath != null)
            {
                if (isVideoTexture)
                {
                    // For video textures: use stream_loop to repeat the video infinitely
                    inputs += $" -stream_loop -1 -i \"{texturePath}\"";
                }
                else
                {
                    // For image textures: loop the image
                    inputs += $" -f image2 -loop 1 -i \"{texturePath}\"";
                }
            }
            
            // Map output
            var mapOutput = applyFilter || effectiveStyle != VideoStyle.None
                ? "-map \"[styled]\"" 
                : "-map \"[vout]\"";

            var arguments = $"{inputs} -filter_complex \"{filterComplex}\" " +
                           $"{mapOutput} -map 0:a? " +
                           $"-c:v libx264 -preset {_preset} -crf {_crf} -threads 0 " +
                           $"-c:a aac -b:a 128k -ac 2 -ar 44100 " +
                           $"-r 24 {durationArg} " +
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
            
            // Read output streams asynchronously to prevent deadlock
            var stderrTask = process.StandardError.ReadToEndAsync();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();

            await process.WaitForExitAsync(cancellationToken);
            
            var stderr = await stderrTask;
            var stdout = await stdoutTask;

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
    /// Builds FFmpeg filter chain for artistic effects (legacy VideoStyle support).
    /// </summary>
    private string BuildArtisticFilter(VideoStyle style, string inputPad, string outputPad, bool hasTexture, bool isVideoTexture = false, int? width = null, int? height = null)
    {
        // Map VideoStyle to Filter + Texture combination
        var (filter, texture) = style switch
        {
            VideoStyle.Painting => (VideoFilter.Painting, VideoTexture.None),
            VideoStyle.Sepia => (VideoFilter.Sepia, VideoTexture.None),
            VideoStyle.Canvas => (VideoFilter.Painting, VideoTexture.Canvas),
            _ => (VideoFilter.None, VideoTexture.None)
        };

        return BuildArtisticFilterComplex(filter, texture, inputPad, outputPad, hasTexture, isVideoTexture, width, height);
    }

    /// <summary>
    /// Builds FFmpeg filter chain for separate filter and texture.
    /// Best practice: use lut for opacity (faster than colorchannelmixer),
    /// use format with fallback (yuva420p|yuva444p|rgba) for best compatibility,
    /// use overlay filter for blending (more efficient than blend for simple opacity).
    /// Supports both image and video textures.
    /// </summary>
    private string BuildArtisticFilterComplex(VideoFilter filter, VideoTexture texture, string inputPad, string outputPad, bool hasTexture, bool isVideoTexture = false, int? width = null, int? height = null)
    {
        // Build the base filter chain
        var filterChain = BuildFilterChain(filter, inputPad, "[filtered]");
        
        // Check global vignette setting
        var vignetteEnabled = _styleSettings.VignetteEnabled;
        var vignetteFilter = vignetteEnabled ? ",vignette=PI/6:aspect=1" : "";
        
        // If no texture, add vignette (if enabled) and return
        if (!hasTexture || texture == VideoTexture.None)
        {
            // Handle case when filter is None (filterChain doesn't have [filtered] placeholder)
            if (filter == VideoFilter.None)
            {
                if (vignetteEnabled)
                    return $"{inputPad}vignette=PI/6:aspect=1{outputPad}";
                else
                    return $"{inputPad}null{outputPad}";
            }
            
            return filterChain.Replace("[filtered]", $"[vfiltered]") + vignetteFilter + $"{outputPad}";
        }

        // With texture: apply texture overlay using best practices
        // 1. Scale texture to match video size
        // 2. Convert to format with alpha (yuva420p|yuva444p|rgba - let ffmpeg choose best)
        // 3. Apply opacity using lut filter (faster than colorchannelmixer)
        // 4. Overlay on top of video
        
        // Opacity value (0-255 for lut filter)
        var opacityValue = texture switch
        {
            VideoTexture.Canvas => "102",      // 0.40 * 255
            VideoTexture.Paper => "77",       // 0.30 * 255
            VideoTexture.Grunge => "89",      // 0.35 * 255
            VideoTexture.FilmGrain => "64",   // 0.25 * 255
            VideoTexture.Dust => "51",        // 0.20 * 255
            VideoTexture.Scratches => "38",   // 0.15 * 255
            _ => "77"
        };

        // For image textures, use loop filter to hold the image; video textures use -stream_loop input option
        var loopFilter = !isVideoTexture && texture != VideoTexture.None ? "loop=loop=-1:size=1," : "";
        
        // Vignette filter for texture path (without comma prefix)
        var textureVignetteFilter = vignetteEnabled ? "vignette=PI/6:aspect=1" : "null";

        if (width.HasValue && height.HasValue)
        {
            // Best practice: format=yuva420p|yuva444p|rgba lets ffmpeg choose best available
            // lut=a={opacity} is faster than colorchannelmixer for setting alpha
            return $"{filterChain};" +
                   $"[1:v]{loopFilter}scale={width}:{height}:force_original_aspect_ratio=increase,crop={width}:{height}," +
                   $"format=yuva420p|yuva444p|rgba," +
                   $"lut=a={opacityValue}[tex];" + 
                   $"[filtered][tex]overlay=format=auto:shortest=1[ovout];" +
                   $"[ovout]{textureVignetteFilter}{outputPad}";
        }
        else
        {
            // Fallback: use scale2ref to match texture to base video size
            return $"{filterChain};" +
                   $"[1:v][filtered]scale2ref[tex][base_ref];" + 
                   $"[tex]{loopFilter}setsar=1,format=yuva420p|yuva444p|rgba,lut=a={opacityValue}[tex_adj];" + 
                   $"[base_ref][tex_adj]overlay=format=auto:shortest=1[ovout];" +
                   $"[ovout]{textureVignetteFilter}{outputPad}";
        }
    }

    /// <summary>
    /// Builds the filter chain for a specific filter type.
    /// </summary>
    private string BuildFilterChain(VideoFilter filter, string inputPad, string outputPad)
    {
        return filter switch
        {
            VideoFilter.Painting => 
                $"{inputPad}fps=12," +
                $"smartblur=lr=2.0:ls=-0.5:lt=-5.0," +
                $"eq=saturation=0.6:contrast=1.2:brightness=-0.05," +
                $"colorbalance=rs=0.1:gs=0.05:bs=-0.2," +
                $"vignette=PI/4" +
                $"{outputPad}",

            VideoFilter.Sepia => 
                $"{inputPad}fps=15," +
                $"colorchannelmixer=.393:.769:.189:0:.349:.686:.168:0:.272:.534:.131," +
                $"smartblur=lr=1.5:ls=-0.8," +
                $"noise=alls=10:allf=t," +
                $"vignette=PI/5" +
                $"{outputPad}",

            VideoFilter.Vintage => 
                $"{inputPad}fps=15," +
                $"curves=r='0/0 0.5/0.6 1/0.9':g='0/0 0.5/0.55 1/0.85':b='0/0 0.5/0.5 1/0.8'," +
                $"smartblur=lr=1.0:ls=-0.3," +
                $"noise=alls=8:allf=t," +
                $"vignette=PI/5" +
                $"{outputPad}",

            VideoFilter.Cinematic => 
                $"{inputPad}fps=24," +
                $"eq=saturation=0.85:contrast=1.15:brightness=-0.02," +
                $"colorbalance=rs=0.05:gs=0:bs=-0.05," +
                $"vignette=PI/6" +
                $"{outputPad}",

            VideoFilter.Warm => 
                $"{inputPad}eq=saturation=1.1:contrast=1.05:brightness=0.02," +
                $"colorbalance=rs=0.1:gs=0.03:bs=-0.08," +
                $"smartblur=lr=0.5:ls=-0.2," +
                $"vignette=PI/6" +
                $"{outputPad}",

            VideoFilter.Cool => 
                $"{inputPad}eq=saturation=0.95:contrast=1.05," +
                $"colorbalance=rs=-0.05:gs=0.02:bs=0.1," +
                $"smartblur=lr=0.5:ls=-0.2," +
                $"vignette=PI/6" +
                $"{outputPad}",

            VideoFilter.Noir => 
                $"{inputPad}fps=18," +
                $"colorchannelmixer=.33:.33:.33:0:.33:.33:.33:0:.33:.33:.33," +
                $"eq=contrast=1.4:brightness=-0.1," +
                $"smartblur=lr=1.0:ls=-0.5," +
                $"noise=alls=12:allf=t," +
                $"vignette=PI/3" +
                $"{outputPad}",

            _ => $"{inputPad}null{outputPad}"
        };
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
            
            // Read output streams asynchronously to prevent deadlock
            var stderrTask = process.StandardError.ReadToEndAsync();
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            
            await process.WaitForExitAsync(cancellationToken);
            
            var stderr = await stderrTask;
            var stdout = await stdoutTask;

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
