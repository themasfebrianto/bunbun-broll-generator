using System.Diagnostics;
using BunbunBroll.Models;

namespace BunbunBroll.Services;

/// <summary>
/// Service for generating images using Whisk API (Google's Imagen)
/// Wraps the whisk CLI tool (@rohitaryal/whisk-api)
/// Ported from ScriptFlow's WhiskImageGenerator
/// </summary>
public class WhiskImageGenerator
{
    private readonly WhiskConfig _config;
    private readonly ILogger<WhiskImageGenerator> _logger;

    public WhiskImageGenerator(WhiskConfig config, ILogger<WhiskImageGenerator> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;
    }

    public void UpdateCookie(string newCookie)
    {
        _config.Cookie = newCookie;
        _logger.LogInformation("WhiskImageGenerator cookie updated at runtime.");
    }

    /// <summary>
    /// Generate a single image from prompt
    /// </summary>
    public async Task<WhiskGenerationResult> GenerateImageAsync(
        string prompt,
        string outputDirectory,
        string filePrefix = "img",
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new WhiskGenerationResult { Prompt = prompt };

        if (!_config.IsValid())
        {
            result.Error = "Whisk config is invalid. Cookie is required when EnableImageGeneration is true.";
            result.Success = false;
            return result;
        }

        try
        {
            Directory.CreateDirectory(outputDirectory);

            var enhancedPrompt = BuildEnhancedPrompt(prompt);
            var args = BuildWhiskArguments(enhancedPrompt, outputDirectory);

            _logger.LogDebug("Running whisk CLI: {Command} {Args}", GetWhiskCommand(), args);

            var processInfo = new ProcessStartInfo
            {
                FileName = GetWhiskCommand(),
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process == null)
            {
                result.Error = "Failed to start whisk process";
                result.Success = false;
                return result;
            }

            using var registration = cancellationToken.Register(() =>
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch { }
            });

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken);

            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;

            if (process.ExitCode == 0)
            {
                var imageFiles = FindGeneratedImages(outputDirectory);
                if (imageFiles.Length > 0)
                {
                    var imageFile = imageFiles.OrderByDescending(f => File.GetCreationTime(f)).First();

                    // Rename to desired prefix
                    var newFileName = $"{filePrefix}{Path.GetExtension(imageFile)}";
                    var newPath = Path.Combine(outputDirectory, newFileName);

                    var retryCount = 0;
                    while (File.Exists(newPath) && retryCount < 100)
                    {
                        newFileName = $"{filePrefix}-{retryCount + 1}{Path.GetExtension(imageFile)}";
                        newPath = Path.Combine(outputDirectory, newFileName);
                        retryCount++;
                    }

                    File.Move(imageFile, newPath);
                    result.ImagePath = newPath;
                    result.Success = true;
                    _logger.LogInformation("Whisk image generated: {Path}", newPath);
                }
                else
                {
                    result.Error = "Image generation succeeded but output file not found";
                    result.Success = false;
                }
            }
            else
            {
                result.Error = error ?? output ?? "Unknown whisk error";
                result.Success = false;
                _logger.LogWarning("Whisk generation failed (exit {Code}): {Error}", process.ExitCode, result.Error);
            }
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;
            result.Error = "Operation cancelled";
            result.Success = false;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;
            result.Error = ex.Message;
            result.Success = false;
            _logger.LogError(ex, "Whisk image generation error");
        }

        return result;
    }

    /// <summary>
    /// Generate multiple images from prompts sequentially
    /// </summary>
    public async Task<List<WhiskGenerationResult>> GenerateImagesAsync(
        List<(string Prompt, string FilePrefix)> prompts,
        string outputDirectory,
        IProgress<(int Current, int Total, bool Success)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<WhiskGenerationResult>();

        for (int i = 0; i < prompts.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var (prompt, filePrefix) = prompts[i];
            var result = await GenerateImageAsync(prompt, outputDirectory, filePrefix, cancellationToken);
            results.Add(result);

            progress?.Report((i + 1, prompts.Count, result.Success));
        }

        return results;
    }

    private string BuildEnhancedPrompt(string originalPrompt)
    {
        if (!string.IsNullOrEmpty(_config.StylePrefix))
            return $"{_config.StylePrefix.Trim()}: {originalPrompt}";

        return originalPrompt;
    }

    private string BuildWhiskArguments(string prompt, string outputDirectory)
    {
        var args = $"generate --prompt \"{EscapeCommandLineArgument(prompt)}\"";

        // Add cookie (required for authentication)
        if (!string.IsNullOrEmpty(_config.Cookie))
            args += $" --cookie \"{_config.Cookie}\"";

        // Add aspect ratio
        if (!string.IsNullOrEmpty(_config.AspectRatio))
            args += $" --aspect {_config.AspectRatio}";

        // Add model
        if (!string.IsNullOrEmpty(_config.Model))
            args += $" --model {_config.Model}";

        // Add seed
        if (_config.Seed > 0)
            args += $" --seed {_config.Seed}";

        // Add output directory
        args += $" --dir \"{outputDirectory}\"";

        return args;
    }

    private static string EscapeCommandLineArgument(string arg) => arg.Replace("\"", "\\\"");

    private static string[] FindGeneratedImages(string directory)
    {
        if (!Directory.Exists(directory)) return Array.Empty<string>();

        return Directory.GetFiles(directory, "*.*")
            .Where(f => IsImageFile(f))
            .OrderByDescending(f => File.GetCreationTime(f))
            .ToArray();
    }

    private static bool IsImageFile(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLower();
        return ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif";
    }

    private static string GetWhiskCommand()
    {
        return Environment.OSVersion.Platform == PlatformID.Win32NT ? "whisk.cmd" : "whisk";
    }

    /// <summary>
    /// Check if whisk CLI is available
    /// </summary>
    public static async Task<bool> IsWhiskAvailableAsync()
    {
        try
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = GetWhiskCommand(),
                Arguments = "--version",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processInfo);
            if (process == null) return false;
            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch { return false; }
    }
}

/// <summary>
/// Result of a single Whisk image generation
/// </summary>
public class WhiskGenerationResult
{
    public string Prompt { get; set; } = string.Empty;
    public string? ImagePath { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
    public TimeSpan Duration { get; set; }
}
