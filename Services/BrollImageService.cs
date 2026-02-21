using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BunbunBroll.Models;

namespace BunbunBroll.Services;

public interface IBrollImageService
{
    Task GenerateWhiskImageForItem(BrollPromptItem item, WhiskImageGenerator generator, string? outputDirectory, string? sessionId);
    Task GenerateKenBurnsVideo(BrollPromptItem item, KenBurnsService kenBurnsService, Action? onStateChanged = null);
}

public class BrollImageService : IBrollImageService
{
    public async Task GenerateWhiskImageForItem(BrollPromptItem item, WhiskImageGenerator generator, string? outputDirectory, string? sessionId)
    {
        item.WhiskStatus = WhiskGenerationStatus.Generating;
        item.WhiskError = null;
        item.WhiskVideoStatus = WhiskGenerationStatus.Pending;
        item.WhiskVideoPath = null;
        item.WhiskVideoError = null;

        try
        {
            var outputDir = !string.IsNullOrEmpty(outputDirectory)
                ? Path.Combine(outputDirectory, "whisks_images")
                : Path.Combine(Directory.GetCurrentDirectory(), "output", sessionId ?? "temp", "whisks_images");
            Directory.CreateDirectory(outputDir);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            var filePrefix = $"seg-{item.Index:D3}";
            var result = await generator.GenerateImageAsync(item.Prompt, outputDir, filePrefix, cancellationToken: cts.Token);
            if (result.Success)
            {
                item.WhiskStatus = WhiskGenerationStatus.Done;
                item.WhiskImagePath = result.ImagePath;
            }
            else
            {
                item.WhiskStatus = WhiskGenerationStatus.Failed;
                item.WhiskError = result.Error ?? "Unknown error";
            }
        }
        catch (OperationCanceledException)
        {
            item.WhiskStatus = WhiskGenerationStatus.Failed;
            item.WhiskError = "Timeout: generasi gambar melebihi 120 detik";
        }
        catch (Exception ex)
        {
            item.WhiskStatus = WhiskGenerationStatus.Failed;
            item.WhiskError = ex.Message;
        }
    }

    public async Task GenerateKenBurnsVideo(BrollPromptItem item, KenBurnsService kenBurnsService, Action? onStateChanged = null)
    {
        if (string.IsNullOrEmpty(item.WhiskImagePath) || !File.Exists(item.WhiskImagePath)) return;

        item.IsConvertingVideo = true;
        item.WhiskVideoError = null;
        item.WhiskVideoStatus = WhiskGenerationStatus.Generating;
        onStateChanged?.Invoke();

        try
        {
            var outputDir = Path.GetDirectoryName(item.WhiskImagePath)!;
            var fileName = Path.GetFileNameWithoutExtension(item.WhiskImagePath) + "_kb.mp4";
            var outputPath = Path.Combine(outputDir, fileName);

            var success = await kenBurnsService.ConvertImageToVideoAsync(
                item.WhiskImagePath, outputPath, item.EstimatedDurationSeconds, 1920, 1080, item.KenBurnsMotion);

            if (success)
            {
                item.WhiskVideoPath = outputPath;
                item.WhiskVideoStatus = WhiskGenerationStatus.Done;
            }
            else
            {
                item.WhiskVideoStatus = WhiskGenerationStatus.Failed;
                item.WhiskVideoError = "FFmpeg conversion failed — check logs";
            }
        }
        catch (Exception ex)
        {
            item.WhiskVideoStatus = WhiskGenerationStatus.Failed;
            item.WhiskVideoError = ex.Message;
        }
        finally
        {
            item.IsConvertingVideo = false;
            onStateChanged?.Invoke();
        }
    }
}
