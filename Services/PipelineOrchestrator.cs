using BunBunBroll.Models;

namespace BunBunBroll.Services;

/// <summary>
/// Pipeline Orchestrator - Coordinates all services to process a complete B-Roll job.
/// </summary>
public interface IPipelineOrchestrator
{
    event EventHandler<SegmentProgressEventArgs>? OnSegmentProgress;
    event EventHandler<JobProgressEventArgs>? OnJobProgress;
    
    Task<ProcessingJob> ProcessJobAsync(ProcessingJob job, CancellationToken cancellationToken = default);
}

public class PipelineOrchestrator : IPipelineOrchestrator
{
    private readonly IScriptProcessor _scriptProcessor;
    private readonly IIntelligenceService _intelligenceService;
    private readonly IAssetBroker _assetBroker;
    private readonly IDownloaderService _downloaderService;
    private readonly ILogger<PipelineOrchestrator> _logger;

    public event EventHandler<SegmentProgressEventArgs>? OnSegmentProgress;
    public event EventHandler<JobProgressEventArgs>? OnJobProgress;

    public PipelineOrchestrator(
        IScriptProcessor scriptProcessor,
        IIntelligenceService intelligenceService,
        IAssetBroker assetBroker,
        IDownloaderService downloaderService,
        ILogger<PipelineOrchestrator> logger)
    {
        _scriptProcessor = scriptProcessor;
        _intelligenceService = intelligenceService;
        _assetBroker = assetBroker;
        _downloaderService = downloaderService;
        _logger = logger;
    }

    public async Task<ProcessingJob> ProcessJobAsync(ProcessingJob job, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting job {JobId}: {ProjectName}", job.Id, job.ProjectName);
        
        try
        {
            // Step 1: Segmentation
            job.Status = JobStatus.Segmenting;
            RaiseJobProgress(job, "Segmenting script...");
            
            job.Segments = _scriptProcessor.SegmentScript(job.RawScript);
            
            if (job.Segments.Count == 0)
            {
                job.Status = JobStatus.Failed;
                job.ErrorMessage = "No segments found in script";
                return job;
            }

            _logger.LogInformation("Created {Count} segments", job.Segments.Count);

            // Step 2: Process each segment
            job.Status = JobStatus.Processing;
            
            foreach (var segment in job.Segments)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    job.Status = JobStatus.Failed;
                    job.ErrorMessage = "Job cancelled";
                    break;
                }

                await ProcessSegmentAsync(job, segment, cancellationToken);
            }

            // Final status
            job.CompletedAt = DateTime.UtcNow;
            
            if (job.FailedSegments == job.TotalSegments)
            {
                job.Status = JobStatus.Failed;
                job.ErrorMessage = "All segments failed to process";
            }
            else if (job.FailedSegments > 0)
            {
                job.Status = JobStatus.PartiallyCompleted;
            }
            else
            {
                job.Status = JobStatus.Completed;
            }

            RaiseJobProgress(job, $"Job completed: {job.CompletedSegments}/{job.TotalSegments} segments");
            _logger.LogInformation("Job {JobId} completed with status: {Status}", job.Id, job.Status);

            return job;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {JobId} failed with exception", job.Id);
            job.Status = JobStatus.Failed;
            job.ErrorMessage = ex.Message;
            return job;
        }
    }

    private async Task ProcessSegmentAsync(ProcessingJob job, ScriptSegment segment, CancellationToken cancellationToken)
    {
        try
        {
            // Step 2a: Extract keywords with AI
            segment.Status = SegmentStatus.ExtractingKeywords;
            RaiseSegmentProgress(job, segment, "Extracting keywords with AI...");

            var keywordResult = await _intelligenceService.ExtractKeywordsAsync(
                segment.OriginalText, 
                job.Mood, 
                cancellationToken);

            if (!keywordResult.Success || keywordResult.Keywords.Count == 0)
            {
                // Fallback: Use simple word extraction
                _logger.LogWarning("AI extraction failed for segment {Id}, using fallback", segment.Id);
                segment.Keywords = ExtractFallbackKeywords(segment.OriginalText);
            }
            else
            {
                segment.Keywords = keywordResult.Keywords;
            }

            _logger.LogDebug("Segment {Id} keywords: {Keywords}", segment.Id, string.Join(", ", segment.Keywords));

            // Step 2b: Search for assets
            segment.Status = SegmentStatus.SearchingAssets;
            RaiseSegmentProgress(job, segment, $"Searching videos for: {string.Join(", ", segment.Keywords.Take(2))}...");

            var assets = await _assetBroker.SearchVideosAsync(segment.Keywords, maxResults: 3, cancellationToken);

            if (assets.Count == 0)
            {
                // Fallback: Try broader keywords
                _logger.LogWarning("No assets found for segment {Id}, trying broader search", segment.Id);
                
                var broaderKeywords = await GetBroaderKeywordsAsync(segment.OriginalText, cancellationToken);
                assets = await _assetBroker.SearchVideosAsync(broaderKeywords, maxResults: 3, cancellationToken);
            }

            if (assets.Count == 0)
            {
                segment.Status = SegmentStatus.NoResults;
                segment.ErrorMessage = "No suitable videos found";
                segment.ProcessedAt = DateTime.UtcNow;
                RaiseSegmentProgress(job, segment, "No videos found");
                return;
            }

            // Step 2c: Download primary asset
            segment.Status = SegmentStatus.Downloading;
            segment.SelectedAsset = assets[0];
            segment.AlternativeAssets = assets.Skip(1).ToList();
            
            RaiseSegmentProgress(job, segment, "Downloading video...");

            var keywordSlug = string.Join("_", segment.Keywords.Take(2))
                .ToLower()
                .Replace(" ", "_");

            var localPath = await _downloaderService.DownloadVideoAsync(
                segment.SelectedAsset,
                job.ProjectName,
                segment.Id,
                keywordSlug,
                cancellationToken);

            if (localPath != null)
            {
                segment.SelectedAsset.LocalPath = localPath;
                segment.Status = SegmentStatus.Completed;
                segment.ProcessedAt = DateTime.UtcNow;
                RaiseSegmentProgress(job, segment, "Completed!");
            }
            else
            {
                segment.Status = SegmentStatus.Failed;
                segment.ErrorMessage = "Download failed";
                RaiseSegmentProgress(job, segment, "Download failed");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process segment {Id}", segment.Id);
            segment.Status = SegmentStatus.Failed;
            segment.ErrorMessage = ex.Message;
            segment.ProcessedAt = DateTime.UtcNow;
            RaiseSegmentProgress(job, segment, $"Error: {ex.Message}");
        }
    }

    private async Task<List<string>> GetBroaderKeywordsAsync(string text, CancellationToken cancellationToken)
    {
        // Ask AI for broader keywords
        var result = await _intelligenceService.ExtractKeywordsAsync(
            $"Give me very generic, simple keywords for this (single words preferred): {text}",
            null,
            cancellationToken);

        return result.Success ? result.Keywords : ExtractFallbackKeywords(text);
    }

    private List<string> ExtractFallbackKeywords(string text)
    {
        // Simple fallback: extract nouns and key words
        var stopWords = new HashSet<string> { "the", "a", "an", "in", "on", "at", "to", "for", "of", "and", "or", "is", "are", "was", "were" };
        
        return text
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim().ToLower())
            .Where(w => w.Length > 3)
            .Where(w => !stopWords.Contains(w))
            .Take(3)
            .ToList();
    }

    private void RaiseSegmentProgress(ProcessingJob job, ScriptSegment segment, string message)
    {
        OnSegmentProgress?.Invoke(this, new SegmentProgressEventArgs(job, segment, message));
    }

    private void RaiseJobProgress(ProcessingJob job, string message)
    {
        OnJobProgress?.Invoke(this, new JobProgressEventArgs(job, message));
    }
}

public class SegmentProgressEventArgs : EventArgs
{
    public ProcessingJob Job { get; }
    public ScriptSegment Segment { get; }
    public string Message { get; }

    public SegmentProgressEventArgs(ProcessingJob job, ScriptSegment segment, string message)
    {
        Job = job;
        Segment = segment;
        Message = message;
    }
}

public class JobProgressEventArgs : EventArgs
{
    public ProcessingJob Job { get; }
    public string Message { get; }

    public JobProgressEventArgs(ProcessingJob job, string message)
    {
        Job = job;
        Message = message;
    }
}
