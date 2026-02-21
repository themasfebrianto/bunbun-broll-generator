using BunbunBroll.Models;
using System.Diagnostics;

namespace BunbunBroll.Services;

/// <summary>
/// VideoComposer partial: Text overlay methods.
/// </summary>
public partial class VideoComposer
{
    // === Text Overlay Support ===

    /// <summary>
    /// Applies a text overlay to a video clip using FFmpeg drawtext filter.
    /// </summary>
    private async Task<string?> AddTextOverlayToVideoAsync(
        string inputPath, TextOverlay overlay, VideoConfig config,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var ffmpegPath = await FindFFmpegExecutablePathAsync();
            if (string.IsNullOrEmpty(ffmpegPath)) return null;

            var outputPath = Path.Combine(
                Path.GetDirectoryName(inputPath)!,
                $"overlay_{Guid.NewGuid():N}.mp4");

            var drawFilter = BuildTextDrawFilter(overlay, config.Width, config.Height);

            var arguments = $"-threads 0 -i \"{inputPath}\" " +
                           $"-vf \"{drawFilter}\" " +
                           $"-c:v libx264 -preset {_preset} -crf {_crf} " +
                           $"-c:a copy -y \"{outputPath}\"";

            _logger.LogDebug("Applying text overlay to {Path}: {Filter}", inputPath, drawFilter);

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
            await process.WaitForExitAsync(cancellationToken);

            var stderr = await stderrTask;
            await stdoutTask;

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("Text overlay FFmpeg failed. Exit: {Code}. Error: {Err}",
                    process.ExitCode, stderr.Length > 500 ? stderr[^500..] : stderr);
                return null;
            }

            if (File.Exists(outputPath))
            {
                _logger.LogInformation("Text overlay applied: {Output}", outputPath);
                return outputPath;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply text overlay to: {Path}", inputPath);
            return null;
        }
    }

    /// <summary>
    /// Builds the FFmpeg drawtext filter string for a text overlay.
    /// </summary>
    private string BuildTextDrawFilter(TextOverlay overlay, int videoWidth, int videoHeight)
    {
        var (x, y) = GetPositionCoordinates(overlay.Style.Position, videoWidth, videoHeight);
        var fontSize = overlay.Style.FontSize;
        var fontColor = overlay.Style.Color.TrimStart('#');

        // Escape special characters for FFmpeg drawtext
        var text = overlay.Text
            .Replace("'", "'\\''")
            .Replace("\\", "\\\\")
            .Replace(":", "\\:");

        var filter = $"drawtext=text='{text}':" +
                     $"x={x}:y={y}:" +
                     $"fontsize={fontSize}:" +
                     $"fontcolor=0x{fontColor}:";

        // Try to use the specified font, fall back to system default
        if (!string.IsNullOrEmpty(overlay.Style.FontFamily))
        {
            filter += $"font='{overlay.Style.FontFamily}':";
        }

        // Shadow for readability
        if (overlay.Style.HasShadow)
        {
            filter += "shadowcolor=0x000000@0.7:shadowx=2:shadowy=2:";
        }

        // Add background box for better visibility
        filter += "box=1:boxcolor=0x000000@0.4:boxborderw=15";

        // Handle Arabic text — render it on a separate line above the main text
        if (!string.IsNullOrEmpty(overlay.ArabicText))
        {
            var arabicText = overlay.ArabicText
                .Replace("'", "'\\''")
                .Replace("\\", "\\\\")
                .Replace(":", "\\:");

            var arabicY = $"({y}-{fontSize + 20})";

            filter += $",drawtext=text='{arabicText}':" +
                      $"x={x}:y={arabicY}:" +
                      $"fontsize={fontSize + 4}:" +
                      $"fontcolor=0xFFD700:";

            if (!string.IsNullOrEmpty(overlay.Style.FontFamily))
            {
                filter += $"font='{overlay.Style.FontFamily}':";
            }

            filter += "shadowcolor=0x000000@0.7:shadowx=2:shadowy=2:" +
                      "box=1:boxcolor=0x000000@0.4:boxborderw=15";
        }

        // Handle reference line below main text
        if (!string.IsNullOrEmpty(overlay.Reference))
        {
            var refText = overlay.Reference
                .Replace("'", "'\\''")
                .Replace("\\", "\\\\")
                .Replace(":", "\\:");

            var refY = $"({y}+{fontSize + 15})";

            filter += $",drawtext=text='{refText}':" +
                      $"x={x}:y={refY}:" +
                      $"fontsize={fontSize - 8}:" +
                      $"fontcolor=0xAAAAAA:";

            if (!string.IsNullOrEmpty(overlay.Style.FontFamily))
            {
                filter += $"font='{overlay.Style.FontFamily}':";
            }

            filter += "shadowcolor=0x000000@0.5:shadowx=1:shadowy=1";
        }

        return filter;
    }

    /// <summary>
    /// Maps TextPosition enum to FFmpeg x/y coordinate expressions.
    /// </summary>
    private static (string x, string y) GetPositionCoordinates(TextPosition position, int width, int height)
    {
        return position switch
        {
            TextPosition.Center => ("(w-text_w)/2", "(h-text_h)/2"),
            TextPosition.TopCenter => ("(w-text_w)/2", "h*0.15"),
            TextPosition.BottomCenter => ("(w-text_w)/2", "h*0.80"),
            TextPosition.TopLeft => ("w*0.05", "h*0.10"),
            TextPosition.TopRight => ("w*0.95-text_w", "h*0.10"),
            TextPosition.BottomLeft => ("w*0.05", "h*0.85"),
            TextPosition.BottomRight => ("w*0.95-text_w", "h*0.85"),
            _ => ("(w-text_w)/2", "(h-text_h)/2")
        };
    }
}
