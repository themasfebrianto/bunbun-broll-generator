using System.Diagnostics;
using System.Globalization;
using System.Text;
using BunbunBroll.Models;

namespace BunbunBroll.Services;

/// <summary>
/// Ken Burns motion types for image animation.
/// Ported from ScriptFlow's SrtModels.KenBurnsMotionType.
/// </summary>

/// <summary>
/// Service for converting static images to animated video clips using Ken Burns effect (zoompan).
/// Ported from ScriptFlow's VideoAssemblyService.
/// 
/// Uses FFmpeg's zoompan filter with speed optimizations:
/// - 6x vertical upscaling (balance between quality and speed)
/// - fast_bilinear scaling flags
/// - libx264 ultrafast preset
/// 
/// Based on: https://github.com/NapoleonWils0n/ffmpeg-rust-scripts/blob/master/src/bin/zoompan.rs
/// </summary>
public class KenBurnsService
{
    private readonly ILogger<KenBurnsService> _logger;
    private readonly string _ffmpegDirectory;
    private readonly VideoStyleSettings _styleSettings;

    private const int DefaultFps = 30;
    private const long MinValidFileSize = 1024; // 1KB minimum for valid video

    // Default motion parameters
    private const double DefaultStartScale = 100.0;
    private const double DefaultEndScale = 112.0;  // 12% zoom over duration
    private const double DefaultCenterPos = 50.0;   // Center position (50%)

    private static readonly Random _random = new();

    public KenBurnsService(ILogger<KenBurnsService> logger, IConfiguration config, VideoStyleSettings styleSettings)
    {
        _logger = logger;
        _styleSettings = styleSettings;

        _ffmpegDirectory = Path.GetFullPath(config["FFmpeg:BinaryDirectory"]
            ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg-binaries"));
    }

    /// <summary>
    /// Convert a static image to an animated video clip with Ken Burns effect.
    /// Output is written directly to outputPath with -movflags +faststart for web playback.
    /// </summary>
    /// <param name="imagePath">Path to the source image</param>
    /// <param name="outputPath">Path where the output .mp4 will be written</param>
    /// <param name="durationSeconds">Duration of the output video</param>
    /// <param name="outputWidth">Output video width</param>
    /// <param name="outputHeight">Output video height</param>
    /// <param name="motionType">Ken Burns motion type</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if video was generated and validated successfully</returns>
    public async Task<bool> ConvertImageToVideoAsync(
        string imagePath,
        string outputPath,
        double durationSeconds,
        int outputWidth,
        int outputHeight,
        KenBurnsMotionType motionType = KenBurnsMotionType.SlowZoomIn,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(imagePath))
        {
            _logger.LogWarning("Image not found: {Path}", imagePath);
            return false;
        }

        if (durationSeconds < 1) durationSeconds = 3; // Minimum 3 seconds

        // Resolve random motion type
        if (motionType == KenBurnsMotionType.Random)
        {
            motionType = GetRandomMotionType();
        }

        // Ensure output directory exists
        var outputDir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDir)) Directory.CreateDirectory(outputDir);

        // Delete existing file to avoid stale data
        if (File.Exists(outputPath)) File.Delete(outputPath);

        var filter = GetZoomPanFilter(
            durationSeconds, motionType,
            outputWidth, outputHeight);

        _logger.LogInformation(
            "Ken Burns: {Image} -> {Output}, {Motion}, {Duration:F1}s, {W}x{H}",
            Path.GetFileName(imagePath), Path.GetFileName(outputPath),
            motionType, durationSeconds, outputWidth, outputHeight);

        var ffmpegPath = await FindFFmpegExecutablePathAsync();
        if (string.IsNullOrEmpty(ffmpegPath))
        {
            _logger.LogError("FFmpeg executable not found");
            return false;
        }

        // Calculate the exact number of frames
        int d = (int)Math.Round(durationSeconds * DefaultFps);
        if (d < 2) d = 2;

        // Build FFmpeg command
        // -movflags +faststart: moves moov atom to front for instant browser playback
        var args = new StringBuilder();
        args.Append("-loop 1");
        args.Append($" -i \"{imagePath}\"");
        args.Append($" -vf \"{filter}\"");
        args.Append($" -frames:v {d}");
        args.Append(" -c:v libx264");
        args.Append(" -preset ultrafast");
        args.Append(" -crf 28");
        args.Append(" -pix_fmt yuv420p");
        args.Append($" -r {DefaultFps}");
        args.Append(" -fps_mode cfr");
        args.Append(" -threads 0");
        args.Append(" -movflags +faststart");
        args.Append($" -y \"{outputPath}\"");

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = args.ToString(),
                    RedirectStandardOutput = false, // Prevents deadlock as we don't read stdout
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(cancellationToken);
            var stderr = await stderrTask;

            if (process.ExitCode != 0)
            {
                _logger.LogWarning(
                    "Ken Burns FFmpeg failed. Exit: {Code}. Error: {Err}",
                    process.ExitCode, stderr);
                return false;
            }

            // Validate output: file must exist and be larger than minimum size
            if (!File.Exists(outputPath))
            {
                _logger.LogWarning("Ken Burns output not created: {Path}", outputPath);
                return false;
            }

            var fileInfo = new FileInfo(outputPath);
            if (fileInfo.Length < MinValidFileSize)
            {
                _logger.LogWarning(
                    "Ken Burns output too small ({Size} bytes), likely corrupt: {Path}",
                    fileInfo.Length, outputPath);
                File.Delete(outputPath);
                return false;
            }

            _logger.LogInformation(
                "Ken Burns done: {Output} ({Size} bytes)",
                Path.GetFileName(outputPath), fileInfo.Length);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ken Burns conversion failed: {Path}", imagePath);
            // Clean up partial output
            if (File.Exists(outputPath)) try { File.Delete(outputPath); } catch { }
            return false;
        }
    }

    /// <summary>
    /// Generate Ken Burns zoompan filter string.
    /// Ported from ScriptFlow's VideoAssemblyService.GetZoomPanFilter().
    /// 
    /// Speed optimizations:
    /// - 6x vertical upscaling (faster than 10x, still smooth)
    /// - fast_bilinear scaling flags
    /// </summary>
    private string GetZoomPanFilter(
        double durationSeconds,
        KenBurnsMotionType motionType,
        int outWidth,
        int outHeight,
        double startScale = DefaultStartScale,
        double endScale = DefaultEndScale,
        double startX = DefaultCenterPos,
        double startY = DefaultCenterPos,
        double endX = DefaultCenterPos,
        double endY = DefaultCenterPos)
    {
        // Set motion-specific parameters
        ConfigureMotionParameters(motionType, ref startScale, ref endScale,
            ref startX, ref startY, ref endX, ref endY);

        // Calculate exact total frames needed to match the requested duration
        // We do Math.Round to ensure we don't accidentally spit out an extra frame 
        // due to double precision drifting, e.g. 8.75s * 30fps = 262.5 frames. Wait!
        // Fractional frames don't exist in standard MKV/MP4 rendering easily without VFR.
        // But FFmpeg `-t` is failing. We MUST calculate `d` as strictly `duration * FPS`.
        // To hit exact fractional duration, we keep CFR, but we round to the nearest whole frame.
        // A duration of 8.75s @ 30 FPS is impossible to represent exactly. It's either 262 frames (8.733s) or 263 frames (8.766s).
        // Since B-roll items are stitched together by FFMPEG via concat demuxer, 
        // minor fractional deviations per-file get absorbed, BUT we want the file itself to be as close to requested duration as technically possible given the framerate.
        // Therefore, we use Math.Round.
        int d = (int)Math.Round(durationSeconds * DefaultFps);
        if (d < 2) d = 2;

        var inv = CultureInfo.InvariantCulture;
        string F(double val) => val.ToString("0.######", inv);

        // Calculate zoom values (1.0 = 100%, 1.5 = 150%)
        double z0 = startScale / 100.0;
        double z1 = endScale / 100.0;

        // Convert position percentages (0-100) to 0.0-1.0 range
        double x0 = startX / 100.0;
        double x1 = endX / 100.0;
        double y0 = startY / 100.0;
        double y1 = endY / 100.0;

        // Progress variable for interpolation: max(0, on-1)/(d-1)
        // 'on' is the output frame number, it starts at 1. (on-1) ensures frame 1 is t=0.0
        string t = $"max(0,on-1)/{d - 1}";

        // Zoom expression: linear interpolation from z0 to z1
        string zExpr = $"{F(z0)}+({F(z1)}-{F(z0)})*{t}";

        // 6x upscaling with fast_bilinear for speed
        string scaleUp = $"scale=-2:6*ih:flags=fast_bilinear";
        string scaleDown = $"scale={outWidth}:{outHeight}:flags=fast_bilinear";
        
        // Vignette filter - applied to all Ken Burns videos for cinematic look (if enabled globally)
        // Note: vignette filter requires specific pixel formats. Adding format=yuv420p ensures compatibility.
        string vignette = _styleSettings.VignetteEnabled ? ",format=yuv420p,vignette=PI/6:aspect=1" : "";

        return motionType switch
        {
            KenBurnsMotionType.None =>
                $"{scaleUp},zoompan=z='{F(z0)}':x='iw*{F(x0)}-iw/zoom/2':y='ih*{F(y0)}-ih/zoom/2':d={d}:s={outWidth}x{outHeight},{scaleDown}{vignette}",

            KenBurnsMotionType.SlowZoomIn or KenBurnsMotionType.SlowZoomOut =>
                $"{scaleUp},zoompan=z='{zExpr}':x='iw/2-iw/zoom/2':y='ih/2-ih/zoom/2':d={d}:s={outWidth}x{outHeight},{scaleDown}{vignette}",

            KenBurnsMotionType.PanLeftToRight or KenBurnsMotionType.PanRightToLeft =>
                $"{scaleUp},zoompan=z='{F(z0)}':x='iw*({F(x0)}+({F(x1)}-{F(x0)})*{t})-iw/{F(z0)}/2':y='ih*{F(y0)}-ih/{F(z0)}/2':d={d}:s={outWidth}x{outHeight},{scaleDown}{vignette}",

            KenBurnsMotionType.PanTopToBottom or KenBurnsMotionType.PanBottomToTop =>
                $"{scaleUp},zoompan=z='{F(z0)}':x='iw*{F(x0)}-iw/{F(z0)}/2':y='ih*({F(y0)}+({F(y1)}-{F(y0)})*{t})-ih/{F(z0)}/2':d={d}:s={outWidth}x{outHeight},{scaleDown}{vignette}",

            KenBurnsMotionType.DiagonalZoomIn or KenBurnsMotionType.DiagonalZoomOut =>
                $"{scaleUp},zoompan=z='{zExpr}':x='iw*({F(x0)}+({F(x1)}-{F(x0)})*{t})-iw/zoom/2':y='ih*({F(y0)}+({F(y1)}-{F(y0)})*{t})-ih/zoom/2':d={d}:s={outWidth}x{outHeight},{scaleDown}{vignette}",

            _ =>
                $"{scaleUp},zoompan=z='{F(z0)}':x='iw*{F(x0)}-iw/zoom/2':y='ih*{F(y0)}-ih/zoom/2':d={d}:s={outWidth}x{outHeight},{scaleDown}{vignette}"
        };
    }

    /// <summary>
    /// Configure motion-specific start/end parameters.
    /// Ported from ScriptFlow's ScriptFlowCli motion parameter setup.
    /// </summary>
    private static void ConfigureMotionParameters(
        KenBurnsMotionType motionType,
        ref double startScale, ref double endScale,
        ref double startX, ref double startY,
        ref double endX, ref double endY)
    {
        switch (motionType)
        {
            case KenBurnsMotionType.SlowZoomIn:
                startScale = 100; endScale = 115;
                startX = 50; startY = 50;
                endX = 50; endY = 50;
                break;

            case KenBurnsMotionType.SlowZoomOut:
                startScale = 115; endScale = 100;
                startX = 50; startY = 50;
                endX = 50; endY = 50;
                break;

            case KenBurnsMotionType.PanLeftToRight:
                // Needs sufficient zoom to allow panning.
                // At 135% zoom, we have 35% slack. Safe center range is approx 37-63%.
                startScale = 135; endScale = 135;
                startX = 40; startY = 50;
                endX = 60; endY = 50;
                break;

            case KenBurnsMotionType.PanRightToLeft:
                startScale = 135; endScale = 135;
                startX = 60; startY = 50;
                endX = 40; endY = 50;
                break;

            case KenBurnsMotionType.PanTopToBottom:
                startScale = 135; endScale = 135;
                startX = 50; startY = 40;
                endX = 50; endY = 60;
                break;

            case KenBurnsMotionType.PanBottomToTop:
                startScale = 135; endScale = 135;
                startX = 50; startY = 60;
                endX = 50; endY = 40;
                break;

            case KenBurnsMotionType.DiagonalZoomIn:
                // Start at full image (center 50), zoom in to 135% (center 40)
                startScale = 100; endScale = 135;
                startX = 50; startY = 50;
                endX = 40; endY = 40;
                break;

            case KenBurnsMotionType.DiagonalZoomOut:
                // Start zoomed in (center 60), zoom out to full image (center 50)
                startScale = 135; endScale = 100;
                startX = 60; startY = 60;
                endX = 50; endY = 50;
                break;

            case KenBurnsMotionType.None:
            default:
                startScale = 100; endScale = 100;
                startX = 50; startY = 50;
                endX = 50; endY = 50;
                break;
        }
    }

    /// <summary>
    /// Get a random motion type (excluding None and Random).
    /// </summary>
    private static KenBurnsMotionType GetRandomMotionType()
    {
        var types = new[]
        {
            KenBurnsMotionType.SlowZoomIn,
            KenBurnsMotionType.SlowZoomOut,
            KenBurnsMotionType.PanLeftToRight,
            KenBurnsMotionType.PanRightToLeft,
            KenBurnsMotionType.DiagonalZoomIn,
            KenBurnsMotionType.DiagonalZoomOut,
        };
        return types[_random.Next(types.Length)];
    }

    /// <summary>
    /// Find FFmpeg executable on the system.
    /// </summary>
    private async Task<string?> FindFFmpegExecutablePathAsync()
    {
        var isWindows = OperatingSystem.IsWindows();
        var ffmpegName = isWindows ? "ffmpeg.exe" : "ffmpeg";

        // Check configured directory
        var configuredPath = Path.Combine(_ffmpegDirectory, ffmpegName);
        if (File.Exists(configuredPath))
            return configuredPath;

        // Check PATH
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
                    return path;
            }
        }
        catch
        {
            // Ignore - try common paths
        }

        // Common paths
        var commonPaths = isWindows
            ? new[]
            {
                @"C:\ffmpeg\bin\ffmpeg.exe",
                @"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
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
                return path;
        }

        return null;
    }
}
