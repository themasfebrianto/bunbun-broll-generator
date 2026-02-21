using BunbunBroll.Models;
using Xabe.FFmpeg;
using System.Diagnostics;

namespace BunbunBroll.Services;

/// <summary>
/// Composes videos using Xabe.FFmpeg wrapper.
/// Core infrastructure: constructor, fields, FFmpeg setup, and basic operations.
/// Partial class: see also Filters, Composition, Concatenation, TextOverlay files.
/// </summary>
public partial class VideoComposer : IVideoComposer
{
    private readonly ILogger<VideoComposer> _logger;
    private readonly IConfiguration _config;
    private readonly KenBurnsService _kenBurnsService;
    private readonly VideoStyleSettings _styleSettings;
    private readonly string _ffmpegDirectory;
    private readonly string _tempDirectory;
    private readonly string _outputDirectory;
    private readonly VoSyncService _voSyncService;
    private readonly ISrtService _srtService;
    private bool _isInitialized = false;
    
    // Optimization settings
    private readonly bool _useHardwareAccel;
    private readonly string _preset;
    private readonly int _parallelClips;
    private readonly int _crf;
    private string? _hwEncoder = null;
    private bool _hwEncoderChecked = false;

    public VideoComposer(
        ILogger<VideoComposer> logger, 
        IConfiguration config,
        KenBurnsService kenBurnsService,
        VideoStyleSettings styleSettings,
        VoSyncService voSyncService,
        ISrtService srtService)
    {
        _logger = logger;
        _config = config;
        _kenBurnsService = kenBurnsService;
        _styleSettings = styleSettings;
        _voSyncService = voSyncService;
        _srtService = srtService;
        
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
        VideoConfig config,
        KenBurnsMotionType motionType = KenBurnsMotionType.SlowZoomIn,
        CancellationToken cancellationToken = default,
        string? sessionId = null)
    {
        try
        {
            if (!await EnsureFFmpegAsync()) return null;

            // Use session-scoped temp directory if sessionId is provided
            var tempDir = !string.IsNullOrEmpty(sessionId)
                ? Path.Combine(_tempDirectory, sessionId)
                : _tempDirectory;
            Directory.CreateDirectory(tempDir);
            var outputPath = Path.Combine(tempDir, $"kb_{Guid.NewGuid():N}.mp4");

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
}
