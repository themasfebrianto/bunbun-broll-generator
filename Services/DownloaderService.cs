using BunbunBroll.Models;
using Microsoft.Extensions.Options;

namespace BunbunBroll.Services;

/// <summary>
/// Downloader Service - Streams video files to disk with proper naming.
/// </summary>
public interface IDownloaderService
{
    Task<string?> DownloadVideoAsync(
        VideoAsset asset, 
        string projectName, 
        int sequenceId, 
        string keywordSlug,
        CancellationToken cancellationToken = default);
}

public class DownloaderService : IDownloaderService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DownloaderService> _logger;
    private readonly DownloaderSettings _settings;

    public DownloaderService(
        HttpClient httpClient, 
        ILogger<DownloaderService> logger,
        IOptions<DownloaderSettings> settings)
    {
        _httpClient = httpClient;
        _logger = logger;
        _settings = settings.Value;
    }

    public async Task<string?> DownloadVideoAsync(
        VideoAsset asset, 
        string projectName, 
        int sequenceId, 
        string keywordSlug,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Create project directory structure
            var sanitizedProjectName = SanitizeFileName(projectName);
            var projectDir = Path.Combine(_settings.OutputDirectory, sanitizedProjectName);
            Directory.CreateDirectory(projectDir);

            // Format: /{ProjectName}/{SequenceID}_{KeywordSlug}.mp4
            var sanitizedSlug = SanitizeFileName(keywordSlug);
            var fileName = $"{sequenceId:D2}_{sanitizedSlug}.mp4";
            var filePath = Path.Combine(projectDir, fileName);

            // Check if file already exists (avoid re-download)
            if (File.Exists(filePath))
            {
                _logger.LogInformation("File already exists, skipping: {Path}", filePath);
                return filePath;
            }

            _logger.LogInformation("Downloading: {Url} -> {Path}", asset.DownloadUrl, filePath);

            // Stream download to minimize memory usage
            using var response = await _httpClient.GetAsync(
                asset.DownloadUrl, 
                HttpCompletionOption.ResponseHeadersRead, 
                cancellationToken);
            
            response.EnsureSuccessStatusCode();

            var contentLength = response.Content.Headers.ContentLength;
            
            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var fileStream = new FileStream(
                filePath, 
                FileMode.Create, 
                FileAccess.Write, 
                FileShare.None, 
                bufferSize: 81920, // 80KB buffer
                useAsync: true);

            var buffer = new byte[81920];
            long totalBytesRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken)) != 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                totalBytesRead += bytesRead;
            }

            _logger.LogInformation("Downloaded {Bytes:N0} bytes to {Path}", totalBytesRead, filePath);
            return filePath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download video: {Url}", asset.DownloadUrl);
            return null;
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name
            .Replace(' ', '_')
            .Where(c => !invalid.Contains(c))
            .ToArray());
        
        return sanitized.Length > 50 ? sanitized[..50] : sanitized;
    }
}

public class DownloaderSettings
{
    public string OutputDirectory { get; set; } = "Broll_Workspace";
}
