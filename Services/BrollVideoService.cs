using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BunbunBroll.Models;

namespace BunbunBroll.Services;

public interface IBrollVideoService
{
    Task SearchBrollForSegmentAsync(BrollPromptItem item, IAssetBroker assetBroker, bool forceRefresh = false);
    Task<string?> DownloadVideoAsync(BrollPromptItem item, VideoAsset video, IDownloaderService downloader, string? outputDirectory, string? sessionId);
    Task DownloadAllVideosAsync(List<BrollPromptItem> items, IDownloaderService downloader, string? outputDirectory, string? sessionId, Action? onStateChanged = null);
    Task ApplyFilterToVideoAsync(BrollPromptItem item, IVideoComposer composer, IDownloaderService downloader, string? outputDirectory, string? sessionId, Action? onStateChanged = null);
    Task ApplyFilterAllVideosAsync(List<BrollPromptItem> items, IVideoComposer composer, IDownloaderService downloader, string? outputDirectory, string? sessionId, Action? onStateChanged = null);
}

public class BrollVideoService : IBrollVideoService
{
    public async Task SearchBrollForSegmentAsync(BrollPromptItem item, IAssetBroker assetBroker, bool forceRefresh = false)
    {
        item.IsSearching = true;
        item.SearchError = null;

        try
        {
            const int pageSize = 4;

            if (!forceRefresh && item.AllSearchResults.Count > 0)
            {
                var totalPages = (int)Math.Ceiling((double)item.AllSearchResults.Count / pageSize);
                item.SearchPage = (item.SearchPage + 1) % totalPages;
            }
            else
            {
                item.SearchPage = 0;
                var keywords = new List<string> { item.Prompt };
                var results = await assetBroker.SearchVideosAsync(keywords, maxResults: 12);
                item.AllSearchResults = results;
            }

            item.SearchResults = item.AllSearchResults
                .Skip(item.SearchPage * pageSize)
                .Take(pageSize)
                .ToList();
        }
        catch (Exception ex)
        {
            item.SearchError = $"Search gagal: {ex.Message}";
        }
        finally
        {
            item.IsSearching = false;
        }
    }

    public async Task<string?> DownloadVideoAsync(BrollPromptItem item, VideoAsset video, IDownloaderService downloader, string? outputDirectory, string? sessionId)
    {
        outputDirectory = outputDirectory?.Replace('\\', '/');
        if (item.IsDownloading) return null;

        try
        {
            item.IsDownloading = true;
            item.DownloadError = null;

            var videosDir = !string.IsNullOrEmpty(outputDirectory)
                ? Path.Combine(outputDirectory, "broll")
                : Path.Combine(Directory.GetCurrentDirectory(), "output", sessionId ?? "temp", "broll");

            item.LocalVideoPath = await downloader.DownloadVideoToDirectoryAsync(
                video, videosDir, item.Index, "preview", CancellationToken.None);

            video.LocalPath = item.LocalVideoPath;
            return item.LocalVideoPath;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error downloading video for segment {item.Index}: {ex.Message}");
            item.DownloadError = "Failed to download";
            return null;
        }
        finally
        {
            item.IsDownloading = false;
        }
    }

    public async Task DownloadAllVideosAsync(List<BrollPromptItem> items, IDownloaderService downloader, string? outputDirectory, string? sessionId, Action? onStateChanged = null)
    {
        outputDirectory = outputDirectory?.Replace('\\', '/');
        var brollItems = items.Where(i => i.MediaType == BrollMediaType.BrollVideo).ToList();

        var videosDir = !string.IsNullOrEmpty(outputDirectory)
            ? Path.Combine(outputDirectory, "broll")
            : Path.Combine(Directory.GetCurrentDirectory(), "output", sessionId ?? "temp", "broll");

        var tasks = brollItems.Select(async item =>
        {
            if (!string.IsNullOrEmpty(item.LocalVideoPath) && File.Exists(item.LocalVideoPath)) return;

            var video = item.SearchResults.FirstOrDefault(v => v.DownloadUrl == item.SelectedVideoUrl)
                        ?? item.SearchResults.FirstOrDefault();

            if (video == null) return;

            try
            {
                item.IsDownloading = true;
                onStateChanged?.Invoke();

                item.LocalVideoPath = await downloader.DownloadVideoToDirectoryAsync(
                    video, videosDir, item.Index, "preview", CancellationToken.None);

                video.LocalPath = item.LocalVideoPath;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Batch download failed for segment {item.Index}: {ex.Message}");
                item.DownloadError = "Failed to download";
            }
            finally
            {
                item.IsDownloading = false;
                onStateChanged?.Invoke();
            }
        });

        await Task.WhenAll(tasks);
    }

    public async Task ApplyFilterToVideoAsync(BrollPromptItem item, IVideoComposer composer, IDownloaderService downloader, string? outputDirectory, string? sessionId, Action? onStateChanged = null)
    {
        outputDirectory = outputDirectory?.Replace('\\', '/');
        if (item.IsFilteringVideo) return;

        try
        {
            item.IsFilteringVideo = true;
            item.FilterError = null;
            item.FilterProgress = 0;
            item.FilterStatus = "Initializing...";
            onStateChanged?.Invoke();

            string? localPath = null;
            VideoAsset? selectedVideo = null;

            if (item.MediaType == BrollMediaType.ImageGeneration)
            {
                if (!string.IsNullOrEmpty(item.WhiskVideoPath) && File.Exists(item.WhiskVideoPath))
                    localPath = item.WhiskVideoPath;
                else
                    throw new InvalidOperationException("No generated video available to filter. Convert image to video first.");
            }
            else
            {
                if (item.SearchResults.Count > 0)
                {
                    selectedVideo = item.SearchResults.FirstOrDefault(v => v.DownloadUrl == item.SelectedVideoUrl)
                                    ?? item.SearchResults.First();

                    if (!string.IsNullOrEmpty(selectedVideo.LocalPath) && File.Exists(selectedVideo.LocalPath))
                        localPath = selectedVideo.LocalPath;
                }
                else
                    throw new InvalidOperationException("No video selected to filter.");

                if (string.IsNullOrEmpty(localPath) || !File.Exists(localPath))
                {
                    item.FilterProgress = 10;
                    item.FilterStatus = "Downloading source...";
                    onStateChanged?.Invoke();

                    var videosDir = !string.IsNullOrEmpty(outputDirectory)
                        ? Path.Combine(outputDirectory, "videos")
                        : Path.Combine(Directory.GetCurrentDirectory(), "output", sessionId ?? "temp", "videos");

                    localPath = await downloader.DownloadVideoToDirectoryAsync(
                        selectedVideo!, videosDir, item.Index, "preview-source", CancellationToken.None);

                    selectedVideo!.LocalPath = localPath;
                }

                if (string.IsNullOrEmpty(localPath) || !File.Exists(localPath))
                    throw new Exception("Could not download source video.");
            }

            item.FilterProgress = 40;
            item.FilterStatus = $"Applying {item.EffectiveFilter} + {item.EffectiveTexture}...";
            onStateChanged?.Invoke();

            var videoConfig = new VideoConfig { Ratio = AspectRatio.Landscape_16x9 };
            var filteredPath = await composer.ApplyFilterAndTextureToVideoAsync(
                localPath, item.EffectiveFilter, item.FilterIntensity, item.EffectiveTexture, item.TextureOpacity, videoConfig, CancellationToken.None, isPreview: false);

            if (!string.IsNullOrEmpty(filteredPath))
            {
                var outDir = !string.IsNullOrEmpty(outputDirectory)
                    ? Path.Combine(outputDirectory, "filtered")
                    : Path.Combine(Directory.GetCurrentDirectory(), "output", sessionId ?? "temp", "filtered");
                Directory.CreateDirectory(outDir);
                var finalFileName = $"filtered_{item.Index:D2}_{Guid.NewGuid().ToString("N")[..8]}.mp4";
                var finalPath = Path.Combine(outDir, finalFileName);

                File.Move(filteredPath, finalPath, overwrite: true);
                item.FilteredVideoPath = finalPath;

                item.FilterProgress = 100;
                item.FilterStatus = "Done!";
                onStateChanged?.Invoke();
                await Task.Delay(500);
            }
            else
            {
                throw new Exception("Filter application returned null path.");
            }
        }
        catch (Exception ex)
        {
            item.FilterError = $"Filter failed: {ex.Message}";
            Console.Error.WriteLine($"Filter error: {ex}");
        }
        finally
        {
            item.IsFilteringVideo = false;
            onStateChanged?.Invoke();
        }
    }

    public async Task ApplyFilterAllVideosAsync(List<BrollPromptItem> items, IVideoComposer composer, IDownloaderService downloader, string? outputDirectory, string? sessionId, Action? onStateChanged = null)
    {
        outputDirectory = outputDirectory?.Replace('\\', '/');
        var itemsToFilter = items.Where(i => i.HasVisualEffect &&
            (string.IsNullOrEmpty(i.FilteredVideoPath) || !File.Exists(i.FilteredVideoPath)) &&
            !i.IsFilteringVideo).ToList();

        if (itemsToFilter.Count == 0) return;

        var filterTasks = itemsToFilter.Select(item =>
            ApplyFilterToVideoAsync(item, composer, downloader, outputDirectory, sessionId, onStateChanged));
        await Task.WhenAll(filterTasks);
    }
}
