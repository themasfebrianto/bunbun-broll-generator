using BunBunBroll.Models;

namespace BunBunBroll.Services;

/// <summary>
/// Pipeline Orchestrator - Coordinates all services to process B-Roll per sentence.
/// </summary>
public interface IPipelineOrchestrator
{
    event EventHandler<SentenceProgressEventArgs>? OnSentenceProgress;
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

    public event EventHandler<SentenceProgressEventArgs>? OnSentenceProgress;
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
            RaiseJobProgress(job, "Segmenting script into sentences...");
            
            job.Segments = _scriptProcessor.SegmentScript(job.RawScript);
            
            if (job.Segments.Count == 0)
            {
                job.Status = JobStatus.Failed;
                job.ErrorMessage = "No segments found in script";
                return job;
            }

            _logger.LogInformation("Created {Segments} segments with {Sentences} sentences", 
                job.Segments.Count, job.TotalSentences);
            
            RaiseJobProgress(job, $"Found {job.TotalSentences} sentences. Estimated duration: {job.EstimatedDurationFormatted}");

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
            
            var totalSentences = job.TotalSentences;
            var completedSentences = job.CompletedSentences;
            var failedSentences = job.FailedSentences;
            
            if (completedSentences == 0)
            {
                job.Status = JobStatus.Failed;
                job.ErrorMessage = "All sentences failed to process";
            }
            else if (failedSentences > 0)
            {
                job.Status = JobStatus.PartiallyCompleted;
            }
            else
            {
                job.Status = JobStatus.Completed;
            }

            RaiseJobProgress(job, $"Job completed: {completedSentences}/{totalSentences} sentences. Coverage: {job.DurationCoverage:F0}%");
            _logger.LogInformation("Job {JobId} completed: {Status}, Duration: {Actual}/{Estimated}", 
                job.Id, job.Status, job.ActualDurationFormatted, job.EstimatedDurationFormatted);

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
        segment.Status = SegmentStatus.Processing;
        RaiseSegmentProgress(job, segment, $"Processing segment: {segment.Title}");
        
        foreach (var sentence in segment.Sentences)
        {
            if (cancellationToken.IsCancellationRequested)
                break;
                
            await ProcessSentenceAsync(job, segment, sentence, cancellationToken);
        }
        
        // Update segment status
        segment.ProcessedAt = DateTime.UtcNow;
        
        if (segment.FailedSentences == segment.Sentences.Count)
        {
            segment.Status = SegmentStatus.Failed;
        }
        else if (segment.FailedSentences > 0)
        {
            segment.Status = SegmentStatus.PartiallyCompleted;
        }
        else
        {
            segment.Status = SegmentStatus.Completed;
        }
        
        RaiseSegmentProgress(job, segment, 
            $"Segment completed: {segment.CompletedSentences}/{segment.Sentences.Count} sentences, {segment.DurationCoverage:F0}% coverage");
    }

    private async Task ProcessSentenceAsync(
        ProcessingJob job, 
        ScriptSegment segment, 
        ScriptSentence sentence, 
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Processing sentence {Id}: {Text}", sentence.Id, sentence.Text[..Math.Min(50, sentence.Text.Length)]);
            
            // Step 1: Extract keywords for this specific sentence
            sentence.Status = SentenceStatus.ExtractingKeywords;
            RaiseSentenceProgress(job, segment, sentence, "Extracting keywords...");

            var keywordResult = await _intelligenceService.ExtractKeywordsAsync(
                sentence.Text, 
                job.Mood, 
                cancellationToken);

            if (!keywordResult.Success || keywordResult.Keywords.Count == 0)
            {
                _logger.LogWarning("AI extraction failed for sentence {Id}, using fallback", sentence.Id);
                sentence.Keywords = ExtractFallbackKeywords(sentence.Text);
            }
            else
            {
                sentence.Keywords = keywordResult.Keywords;
            }

            if (sentence.Keywords.Count == 0)
            {
                sentence.Status = SentenceStatus.Failed;
                sentence.ErrorMessage = "Could not extract keywords";
                return;
            }

            _logger.LogDebug("Sentence {Id} keywords: {Keywords}", sentence.Id, string.Join(", ", sentence.Keywords));

            // Step 2: Search for B-Roll
            sentence.Status = SentenceStatus.SearchingBRoll;
            RaiseSentenceProgress(job, segment, sentence, $"Searching: {string.Join(", ", sentence.Keywords.Take(2))}...");

            // Target duration for this sentence
            var targetDuration = (int)Math.Ceiling(sentence.EstimatedDurationSeconds);
            
            var assets = await _assetBroker.SearchVideosAsync(
                sentence.Keywords, 
                maxResults: 3, 
                minDuration: Math.Max(3, targetDuration - 5),
                maxDuration: targetDuration + 10,
                cancellationToken: cancellationToken);

            if (assets.Count == 0)
            {
                // Fallback: Try broader search without duration filter
                _logger.LogWarning("No assets found for sentence {Id}, trying broader search", sentence.Id);
                assets = await _assetBroker.SearchVideosAsync(
                    sentence.Keywords, 
                    maxResults: 3,
                    cancellationToken: cancellationToken);
            }

            if (assets.Count == 0)
            {
                sentence.Status = SentenceStatus.NoResults;
                sentence.ErrorMessage = "No suitable videos found";
                RaiseSentenceProgress(job, segment, sentence, "No B-Roll found");
                return;
            }

            // Select best match (prefer closest to target duration)
            var bestAsset = assets
                .OrderBy(a => Math.Abs(a.DurationSeconds - targetDuration))
                .First();

            // Step 3: Download B-Roll
            sentence.Status = SentenceStatus.Downloading;
            sentence.BRollClip = bestAsset;
            
            RaiseSentenceProgress(job, segment, sentence, $"Downloading {bestAsset.DurationSeconds}s clip...");

            var keywordSlug = string.Join("_", sentence.Keywords.Take(2))
                .ToLower()
                .Replace(" ", "_");

            var localPath = await _downloaderService.DownloadVideoAsync(
                bestAsset,
                job.ProjectName,
                sentence.Id,
                keywordSlug,
                cancellationToken);

            if (localPath != null)
            {
                sentence.BRollClip.LocalPath = localPath;
                sentence.Status = SentenceStatus.Completed;
                RaiseSentenceProgress(job, segment, sentence, 
                    $"✓ {bestAsset.DurationSeconds}s clip ({sentence.DurationCoverage:F0}% coverage)");
            }
            else
            {
                sentence.Status = SentenceStatus.Failed;
                sentence.ErrorMessage = "Download failed";
                RaiseSentenceProgress(job, segment, sentence, "Download failed");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process sentence {Id}", sentence.Id);
            sentence.Status = SentenceStatus.Failed;
            sentence.ErrorMessage = ex.Message;
            RaiseSentenceProgress(job, segment, sentence, $"Error: {ex.Message}");
        }
    }

    private List<string> ExtractFallbackKeywords(string text)
    {
        var stopWords = new HashSet<string> { 
            "the", "a", "an", "in", "on", "at", "to", "for", "of", "and", "or", 
            "is", "are", "was", "were", "yang", "di", "ke", "dari", "dan", "atau",
            "ini", "itu", "dengan", "untuk", "pada", "ada", "tidak", "akan", "sudah"
        };
        
        return text
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim().ToLower())
            .Where(w => w.Length > 3)
            .Where(w => !stopWords.Contains(w))
            .Take(4)
            .ToList();
    }

    private void RaiseSentenceProgress(ProcessingJob job, ScriptSegment segment, ScriptSentence sentence, string message)
    {
        OnSentenceProgress?.Invoke(this, new SentenceProgressEventArgs(job, segment, sentence, message));
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

public class SentenceProgressEventArgs : EventArgs
{
    public ProcessingJob Job { get; }
    public ScriptSegment Segment { get; }
    public ScriptSentence Sentence { get; }
    public string Message { get; }

    public SentenceProgressEventArgs(ProcessingJob job, ScriptSegment segment, ScriptSentence sentence, string message)
    {
        Job = job;
        Segment = segment;
        Sentence = sentence;
        Message = message;
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
