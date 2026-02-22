using BunbunBroll.Models;
using BunbunBroll.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace BunbunBroll.Tests.Integration;

public class KenBurnsDurationTests : IDisposable
{
    private ServiceProvider _serviceProvider;
    private string _tempDir;

    public KenBurnsDurationTests()
    {
        var services = new ServiceCollection();
        
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                {"FFmpeg:TempDirectory", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp", "kenburns_tests")},
            })
            .Build();

        services.AddSingleton<IConfiguration>(config);
        services.AddLogging(builder => builder.AddConsole());
        services.AddSingleton<VideoStyleSettings>();
        services.AddSingleton<KenBurnsService>();

        _serviceProvider = services.BuildServiceProvider();

        _tempDir = config["FFmpeg:TempDirectory"]!;
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    [Theory]
    [InlineData(3.145)] // Typical decimal precise request
    [InlineData(12.000)] // Baseline integer
    [InlineData(8.750)] // Mid-frame fractional
    [InlineData(4.032)] // Near-frame fractional
    public async Task ConvertImageToVideoAsync_EnforcesExactPrecisionDuration(double requestedDurationSeconds)
    {
        var kenBurns = _serviceProvider.GetRequiredService<KenBurnsService>();
        
        var imagePath = Path.Combine(_tempDir, "test_image.jpg");
        await CreateDummyImage(imagePath);
        
        var outputPath = Path.Combine(_tempDir, $"output_{requestedDurationSeconds.ToString("F3").Replace(".", "_")}.mp4");

        var success = await kenBurns.ConvertImageToVideoAsync(
            imagePath,
            outputPath,
            requestedDurationSeconds,
            outputWidth: 1280,
            outputHeight: 720,
            motionType: KenBurnsMotionType.SlowZoomIn);

        Assert.True(success, "KenBurns conversion failed.");
        Assert.True(File.Exists(outputPath), "Output file was not created.");

        // Read exact container duration via ffprobe
        double actualDuration = await GetPreciseVideoDurationAsync(outputPath);

        // Constant Frame Rate (30 FPS) means frames exist every 0.0333 seconds.
        // It is physically impossible to generate a video of precisely 8.750 seconds at 30 FPS.
        // The closest are 262 frames (8.733s) or 263 frames (8.766s).
        // Therefore, we assert that the actual duration is within a maximum of 1 frame (0.034s) 
        // from the specifically requested mathematical duration.
        double tolerance = 1.0 / 30.0 + 0.001; // ~0.034s
        double diff = Math.Abs(requestedDurationSeconds - actualDuration);
        
        Assert.True(diff <= tolerance, 
            $"Drift exceeded. Requested: {requestedDurationSeconds}s. Actual: {actualDuration}s. Diff: {diff}s (Max allowed 1 frame: {tolerance}s)");
    }

    private async Task CreateDummyImage(string path)
    {
        if (File.Exists(path)) return;
        
        // We rely on standard FFmpeg CLI existence which should have been found by KenBurns or manually here
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "ffmpeg", 
            Arguments = $"-y -f lavfi -i color=c=blue:s=1280x720 -frames:v 1 \"{path}\"",
            RedirectStandardOutput = false,
            RedirectStandardError = false, 
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = System.Diagnostics.Process.Start(psi);
        if (p != null) await p.WaitForExitAsync();
    }

    private async Task<double> GetPreciseVideoDurationAsync(string videoPath)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "ffprobe", 
            Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{videoPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = false, 
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var p = System.Diagnostics.Process.Start(psi);
        if (p == null) throw new InvalidOperationException("Failed to start ffprobe.");
        
        var outputTask = p.StandardOutput.ReadToEndAsync();
        await p.WaitForExitAsync();
        var output = await outputTask;

        if (double.TryParse(output.Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double duration))
        {
            return duration;
        }

        throw new InvalidOperationException($"Could not parse ffprobe duration output: '{output}'");
    }
}
