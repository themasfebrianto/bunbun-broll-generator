using BunbunBroll.Models;

namespace BunbunBroll.Services;

/// <summary>
/// Pipeline Orchestrator - Coordinates all services to process B-Roll per sentence.
/// NEW: Preview-first workflow - search and preview before download.
/// </summary>
public interface IPipelineOrchestrator
{
    event EventHandler<SentenceProgressEventArgs>? OnSentenceProgress;
    event EventHandler<SegmentProgressEventArgs>? OnSegmentProgress;
    event EventHandler<JobProgressEventArgs>? OnJobProgress;
    
    // Phase 1: Search & Preview (no download)
    Task<ProcessingJob> SearchPreviewsAsync(ProcessingJob job, CancellationToken cancellationToken = default);
    
    // Phase 2: Re-search a specific sentence with new keywords
    Task ResearchSentenceAsync(ProcessingJob job, ScriptSentence sentence, List<string>? customKeywords = null, CancellationToken cancellationToken = default);
    
    // Phase 3: Download approved videos
    Task DownloadApprovedAsync(ProcessingJob job, CancellationToken cancellationToken = default);
    
    // Download a single sentence
    Task DownloadSentenceAsync(ProcessingJob job, ScriptSentence sentence, CancellationToken cancellationToken = default);
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

    /// <summary>
    /// Phase 1: Search and get previews for all sentences (NO download yet)
    /// </summary>
    public async Task<ProcessingJob> SearchPreviewsAsync(ProcessingJob job, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting preview search for job {JobId}: {ProjectName}", job.Id, job.ProjectName);
        
        try
        {
            // Step 1: Segmentation
            job.Status = JobStatus.Segmenting;
            RaiseJobProgress(job, "Lagi pecah script jadi kalimat...");

            job.Segments = _scriptProcessor.SegmentScript(job.RawScript);

            if (job.Segments.Count == 0)
            {
                job.Status = JobStatus.Failed;
                job.ErrorMessage = "Gak ada segmen yang ketemu";
                return job;
            }

            _logger.LogInformation("Created {Segments} segments with {Sentences} sentences",
                job.Segments.Count, job.TotalSentences);

            job.Status = JobStatus.Processing;

            // Step 2: BATCH EXTRACT LAYERED KEYWORDS FOR ALL SENTENCES IN ONE AI CALL
            var allSentences = job.Segments.SelectMany(s => s.Sentences).ToList();
            RaiseJobProgress(job, $"Lagi analisa keywords untuk {allSentences.Count} kalimat...");

            var sentencesToProcess = allSentences.Select(s => (s.Id, s.Text)).ToList();
            var batchKeywordSets = await _intelligenceService.ExtractKeywordSetBatchAsync(
                sentencesToProcess,
                job.Mood,
                cancellationToken);

            // Apply layered keywords to all sentences
            foreach (var sentence in allSentences)
            {
                if (batchKeywordSets.TryGetValue(sentence.Id, out var keywordSet) && keywordSet.TotalCount > 0)
                {
                    sentence.KeywordSet = keywordSet;
                    sentence.Status = SentenceStatus.SearchingBRoll;
                }
                else
                {
                    sentence.Keywords = ExtractFallbackKeywords(sentence.Text);
                    sentence.Status = SentenceStatus.SearchingBRoll;
                }
            }

            RaiseJobProgress(job, $"Lagi cari video untuk {allSentences.Count} kalimat...");

            // Step 3: SEARCH VIDEOS IN PARALLEL FOR ALL SENTENCES
            await Parallel.ForEachAsync(
                allSentences,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = MaxConcurrentSearches,
                    CancellationToken = cancellationToken
                },
                async (sentence, ct) =>
                {
                    var segment = job.Segments.First(s => s.Sentences.Contains(sentence));
                    await SearchVideoForSentenceAsync(job, segment, sentence, ct);
                });

            // Mark all segments as completed
            foreach (var segment in job.Segments)
            {
                segment.Status = SegmentStatus.Completed;
                segment.ProcessedAt = DateTime.UtcNow;
            }

            // Set to preview ready
            job.Status = JobStatus.PreviewReady;
            job.CompletedAt = DateTime.UtcNow;

            var readyCount = allSentences.Count(s => s.Status == SentenceStatus.PreviewReady);
            RaiseJobProgress(job, $"Selesai! {readyCount} dari {job.TotalSentences} kalimat ada videonya.");

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

    // Max concurrent video searches (balance speed vs API rate limits)
    private const int MaxConcurrentSearches = 6;
    
    // Batch size for AI keyword extraction
    private const int KeywordBatchSize = 10;

    private async Task SearchSegmentPreviewsAsync(ProcessingJob job, ScriptSegment segment, CancellationToken cancellationToken)
    {
        segment.Status = SegmentStatus.Processing;
        var totalSentences = segment.Sentences.Count;

        RaiseSegmentProgress(job, segment, $"Lagi analisa keywords untuk {totalSentences} kalimat...");

        // PHASE 1: Batch extract keywords for ALL sentences in segment (1 AI call instead of N)
        var sentencesToProcess = segment.Sentences
            .Select(s => (s.Id, s.Text))
            .ToList();

        var batchKeywords = await _intelligenceService.ExtractKeywordsBatchAsync(
            sentencesToProcess, 
            job.Mood, 
            cancellationToken);

        // Apply keywords to sentences
        foreach (var sentence in segment.Sentences)
        {
            if (batchKeywords.TryGetValue(sentence.Id, out var keywords) && keywords.Count > 0)
            {
                sentence.Keywords = keywords;
                sentence.Status = SentenceStatus.SearchingBRoll;
            }
            else
            {
                // Fallback for sentences that didn't get keywords
                sentence.Keywords = ExtractFallbackKeywords(sentence.Text);
                sentence.Status = SentenceStatus.SearchingBRoll;
            }
        }

        RaiseSegmentProgress(job, segment, $"Lagi cari video untuk {totalSentences} kalimat...");

        // PHASE 2: Search videos in parallel (much faster now that keywords are ready)
        await Parallel.ForEachAsync(
            segment.Sentences,
            new ParallelOptions 
            { 
                MaxDegreeOfParallelism = MaxConcurrentSearches,
                CancellationToken = cancellationToken 
            },
            async (sentence, ct) =>
            {
                await SearchVideoForSentenceAsync(job, segment, sentence, ct);
            });
        
        segment.ProcessedAt = DateTime.UtcNow;
        segment.Status = SegmentStatus.Completed;
        
        RaiseSegmentProgress(job, segment, $"Segment selesai: {segment.Sentences.Count(s => s.HasSearchResults)}/{totalSentences} ada videonya");
    }

    /// <summary>
    /// Search for video only (keywords already extracted)
    /// Uses tier-based cascading search if KeywordSet is available.
    /// </summary>
    private async Task SearchVideoForSentenceAsync(
        ProcessingJob job, 
        ScriptSegment segment, 
        ScriptSentence sentence, 
        CancellationToken cancellationToken)
    {
        try
        {
            var keywordPreview = string.Join(", ", sentence.KeywordSet.Primary.Take(2).DefaultIfEmpty(sentence.Keywords.FirstOrDefault() ?? "..."));
            var categoryInfo = sentence.KeywordSet.SuggestedCategory != null ? $" [{sentence.KeywordSet.SuggestedCategory}]" : "";
            RaiseSentenceProgress(job, segment, sentence, $"Lagi cari{categoryInfo}: {keywordPreview}...");

            var targetDuration = (int)Math.Ceiling(sentence.EstimatedDurationSeconds);
            var (minDuration, maxDuration) = CompositeAssetBroker.CalculateAdaptiveDurationRange(targetDuration);

            List<VideoAsset> assets;

            // Use tier-based search if broker supports KeywordSet
            if (_assetBroker is IAssetBrokerV2 brokerV2 && sentence.KeywordSet.TotalCount > 0)
            {
                _logger.LogDebug("Adaptive duration range: {Min}-{Max}s (target: {Target}s)",
                    minDuration, maxDuration, targetDuration);

                assets = await brokerV2.SearchVideosAsync(
                    sentence.KeywordSet,
                    maxResults: 6,
                    minDuration: minDuration,
                    maxDuration: maxDuration,
                    cancellationToken: cancellationToken);
            }
            else
            {
                // Fallback to flat keyword search
                assets = await _assetBroker.SearchVideosAsync(
                    sentence.Keywords,
                    maxResults: 6,
                    minDuration: minDuration,
                    maxDuration: maxDuration,
                    cancellationToken: cancellationToken);
            }

            if (assets.Count == 0)
            {
                _logger.LogDebug("No results with adaptive range, trying wider fallback");

                // Fallback: Use very wide range
                assets = await _assetBroker.SearchVideosAsync(
                    sentence.Keywords,
                    maxResults: 6,
                    minDuration: Math.Max(3, targetDuration - 10),
                    maxDuration: targetDuration + 30,
                    cancellationToken: cancellationToken);
            }

            if (assets.Count == 0)
            {
                sentence.Status = SentenceStatus.NoResults;
                sentence.ErrorMessage = "No videos found";
                RaiseSentenceProgress(job, segment, sentence, "Gak ada video, coba keyword lain");
                return;
            }

            sentence.SearchResults = assets;

            // Select best video using DurationMatchScore
            // Prefer videos that cover the sentence, not just closest duration
            sentence.SelectedVideo = assets
                .OrderByDescending(a => a.CalculateDurationMatchScore(targetDuration))
                .First();

            var selectedScore = sentence.SelectedVideo.CalculateDurationMatchScore(targetDuration);
            _logger.LogDebug("Selected video with duration score: {Score}/100 (video: {VideoDuration}s, target: {TargetDuration}s)",
                selectedScore, sentence.SelectedVideo.DurationSeconds, targetDuration);

            sentence.Status = SentenceStatus.PreviewReady;
            RaiseSentenceProgress(job, segment, sentence, $"✓ {assets.Count} pilihan ({sentence.SelectedVideo.DurationSeconds}detik)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Video search failed for sentence {Id}", sentence.Id);
            sentence.Status = SentenceStatus.Failed;
            sentence.ErrorMessage = ex.Message;
            RaiseSentenceProgress(job, segment, sentence, $"Error: {ex.Message}");
        }
    }


    private async Task SearchSentencePreviewAsync(
        ProcessingJob job, 
        ScriptSegment segment, 
        ScriptSentence sentence, 
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Searching preview for sentence {Id}", sentence.Id);
            
            // Step 1: Extract layered keywords
            sentence.Status = SentenceStatus.ExtractingKeywords;
            RaiseSentenceProgress(job, segment, sentence, "Lagi analisa keywords...");

            var keywordResult = await _intelligenceService.ExtractKeywordsAsync(
                sentence.Text, 
                job.Mood, 
                cancellationToken);

            if (!keywordResult.Success || keywordResult.KeywordSet.TotalCount == 0)
            {
                _logger.LogWarning("AI extraction failed for sentence {Id}, using fallback", sentence.Id);
                sentence.Keywords = ExtractFallbackKeywords(sentence.Text);
            }
            else
            {
                sentence.KeywordSet = keywordResult.KeywordSet;
            }

            if (sentence.Keywords.Count == 0)
            {
                sentence.Status = SentenceStatus.Failed;
                sentence.ErrorMessage = "Could not extract keywords";
                return;
            }

            _logger.LogDebug("Sentence {Id} keywords: {Keywords} (Category: {Category}, Mood: {Mood})", 
                sentence.Id, 
                string.Join(", ", sentence.KeywordSet.Primary),
                sentence.KeywordSet.SuggestedCategory ?? "N/A",
                sentence.KeywordSet.DetectedMood ?? "N/A");

            // Step 2: Search for videos (NO download)
            sentence.Status = SentenceStatus.SearchingBRoll;
            var keywordPreview = string.Join(", ", sentence.KeywordSet.Primary.Take(2).DefaultIfEmpty(sentence.Keywords.FirstOrDefault() ?? "..."));
            RaiseSentenceProgress(job, segment, sentence, $"Lagi cari: {keywordPreview}...");

            var targetDuration = (int)Math.Ceiling(sentence.EstimatedDurationSeconds);
            var (minDuration, maxDuration) = CompositeAssetBroker.CalculateAdaptiveDurationRange(targetDuration);

            List<VideoAsset> assets;

            // Use tier-based search if broker supports KeywordSet
            if (_assetBroker is IAssetBrokerV2 brokerV2 && sentence.KeywordSet.TotalCount > 0)
            {
                _logger.LogDebug("Adaptive duration range: {Min}-{Max}s (target: {Target}s)",
                    minDuration, maxDuration, targetDuration);

                assets = await brokerV2.SearchVideosAsync(
                    sentence.KeywordSet,
                    maxResults: 6,
                    minDuration: minDuration,
                    maxDuration: maxDuration,
                    cancellationToken: cancellationToken);
            }
            else
            {
                assets = await _assetBroker.SearchVideosAsync(
                    sentence.Keywords,
                    maxResults: 6,
                    minDuration: minDuration,
                    maxDuration: maxDuration,
                    cancellationToken: cancellationToken);
            }

            if (assets.Count == 0)
            {
                _logger.LogDebug("No results with adaptive range, trying wider fallback");

                // Fallback: Use very wide range
                assets = await _assetBroker.SearchVideosAsync(
                    sentence.Keywords,
                    maxResults: 6,
                    minDuration: Math.Max(3, targetDuration - 10),
                    maxDuration: targetDuration + 30,
                    cancellationToken: cancellationToken);
            }

            if (assets.Count == 0)
            {
                sentence.Status = SentenceStatus.NoResults;
                sentence.ErrorMessage = "No videos found";
                RaiseSentenceProgress(job, segment, sentence, "Gak ada video, coba keyword lain");
                return;
            }

            // Store all results for user to choose
            sentence.SearchResults = assets;

            // Auto-select best match using DurationMatchScore
            sentence.SelectedVideo = assets
                .OrderByDescending(a => a.CalculateDurationMatchScore(targetDuration))
                .First();

            var selectedScore = sentence.SelectedVideo.CalculateDurationMatchScore(targetDuration);
            _logger.LogDebug("Selected video with duration score: {Score}/100 (video: {VideoDuration}s, target: {TargetDuration}s)",
                selectedScore, sentence.SelectedVideo.DurationSeconds, targetDuration);

            sentence.Status = SentenceStatus.PreviewReady;
            RaiseSentenceProgress(job, segment, sentence, $"✓ {assets.Count} pilihan video ({sentence.SelectedVideo.DurationSeconds}detik)");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search sentence {Id}", sentence.Id);
            sentence.Status = SentenceStatus.Failed;
            sentence.ErrorMessage = ex.Message;
            RaiseSentenceProgress(job, segment, sentence, $"Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Re-search a specific sentence with optional custom keywords
    /// </summary>
    public async Task ResearchSentenceAsync(
        ProcessingJob job, 
        ScriptSentence sentence, 
        List<string>? customKeywords = null, 
        CancellationToken cancellationToken = default)
    {
        var segment = job.Segments.First(s => s.Sentences.Contains(sentence));
        
        // Use custom keywords if provided
        if (customKeywords != null && customKeywords.Count > 0)
        {
            sentence.Keywords = customKeywords;
        }
        
        sentence.Status = SentenceStatus.SearchingBRoll;
        sentence.SearchResults.Clear();
        sentence.SelectedVideo = null;
        
        RaiseSentenceProgress(job, segment, sentence, $"Lagi cari lagi: {string.Join(", ", sentence.Keywords.Take(2))}...");

        var targetDuration = (int)Math.Ceiling(sentence.EstimatedDurationSeconds);
        var (minDuration, maxDuration) = CompositeAssetBroker.CalculateAdaptiveDurationRange(targetDuration);

        _logger.LogDebug("Adaptive duration range: {Min}-{Max}s (target: {Target}s)",
            minDuration, maxDuration, targetDuration);

        var assets = await _assetBroker.SearchVideosAsync(
            sentence.Keywords,
            maxResults: 6,
            minDuration: minDuration,
            maxDuration: maxDuration,
            cancellationToken: cancellationToken);

        if (assets.Count == 0)
        {
            _logger.LogDebug("No results with adaptive range, trying wider fallback");

            // Fallback: Use very wide range
            assets = await _assetBroker.SearchVideosAsync(
                sentence.Keywords,
                maxResults: 6,
                minDuration: Math.Max(3, targetDuration - 10),
                maxDuration: targetDuration + 30,
                cancellationToken: cancellationToken);
        }

        if (assets.Count == 0)
        {
            sentence.Status = SentenceStatus.NoResults;
            sentence.ErrorMessage = "Gak ada video yang ketemu";
            RaiseSentenceProgress(job, segment, sentence, "Gak ada video");
            return;
        }

        sentence.SearchResults = assets;
        sentence.SelectedVideo = assets
            .OrderByDescending(a => a.CalculateDurationMatchScore(targetDuration))
            .First();

        var selectedScore = sentence.SelectedVideo.CalculateDurationMatchScore(targetDuration);
        _logger.LogDebug("Selected video with duration score: {Score}/100 (video: {VideoDuration}s, target: {TargetDuration}s)",
            selectedScore, sentence.SelectedVideo.DurationSeconds, targetDuration);

        sentence.Status = SentenceStatus.PreviewReady;
        RaiseSentenceProgress(job, segment, sentence, $"✓ {assets.Count} pilihan baru");
    }

    /// <summary>
    /// Download all approved sentences
    /// </summary>
    public async Task DownloadApprovedAsync(ProcessingJob job, CancellationToken cancellationToken = default)
    {
        var approvedSentences = job.Segments
            .SelectMany(s => s.Sentences)
            .Where(s => s.IsApproved && s.SelectedVideo != null && !s.IsDownloaded)
            .ToList();

        if (approvedSentences.Count == 0)
        {
            RaiseJobProgress(job, "Gak ada video yang mau didownload");
            return;
        }

        job.Status = JobStatus.Downloading;
        RaiseJobProgress(job, $"Lagi download {approvedSentences.Count} video...");

        foreach (var sentence in approvedSentences)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            await DownloadSentenceAsync(job, sentence, cancellationToken);
        }

        job.Status = JobStatus.Completed;
        var downloaded = approvedSentences.Count(s => s.IsDownloaded);
        RaiseJobProgress(job, $"Selesai! {downloaded} dari {approvedSentences.Count} video udah kesimpan");
    }

    /// <summary>
    /// Download a single sentence's selected video
    /// </summary>
    public async Task DownloadSentenceAsync(ProcessingJob job, ScriptSentence sentence, CancellationToken cancellationToken = default)
    {
        if (sentence.SelectedVideo == null)
        {
            sentence.ErrorMessage = "No video selected";
            return;
        }

        var segment = job.Segments.First(s => s.Sentences.Contains(sentence));

        sentence.Status = SentenceStatus.Downloading;
        RaiseSentenceProgress(job, segment, sentence, $"Lagi download video {sentence.SelectedVideo.DurationSeconds}detik...");

        try
        {
            var keywordSlug = string.Join("_", sentence.Keywords.Take(2))
                .ToLower()
                .Replace(" ", "_");

            var localPath = await _downloaderService.DownloadVideoAsync(
                sentence.SelectedVideo,
                job.ProjectName,
                sentence.Id,
                keywordSlug,
                cancellationToken);

            if (localPath != null)
            {
                sentence.DownloadedVideo = sentence.SelectedVideo;
                sentence.DownloadedVideo.LocalPath = localPath;
                sentence.Status = SentenceStatus.Completed;
                RaiseSentenceProgress(job, segment, sentence, $"✓ Kesimpan: {Path.GetFileName(localPath)}");
            }
            else
            {
                sentence.Status = SentenceStatus.Failed;
                sentence.ErrorMessage = "Download gagal";
                RaiseSentenceProgress(job, segment, sentence, "Download gagal");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download for sentence {Id}", sentence.Id);
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
            "ini", "itu", "dengan", "untuk", "pada", "ada", "tidak", "akan", "sudah",
            "saya", "kamu", "kami", "mereka", "aku", "kita", "nya", "sang", "para"
        };
        
        var translations = new Dictionary<string, string>
        {
            ["pagi"] = "morning", ["malam"] = "night", ["siang"] = "afternoon",
            ["sore"] = "evening", ["hujan"] = "rain", ["matahari"] = "sun",
            ["bulan"] = "moon", ["langit"] = "sky", ["kota"] = "city",
            ["jalan"] = "street", ["rumah"] = "house", ["kantor"] = "office",
            ["orang"] = "person", ["air"] = "water", ["pohon"] = "tree",
            ["laut"] = "ocean", ["gunung"] = "mountain", ["hutan"] = "forest",
            ["sedih"] = "sad", ["senang"] = "happy", ["lelah"] = "tired",
            ["sibuk"] = "busy", ["sepi"] = "lonely", ["gelap"] = "dark"
        };
        
        var keywords = new List<string>();
        
        var words = text.ToLower()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim('.', ',', '!', '?', '"', '\''))
            .Where(w => w.Length > 2)
            .Where(w => !stopWords.Contains(w))
            .Take(6);
            
        foreach (var word in words)
        {
            if (translations.TryGetValue(word, out var translation))
                keywords.Add(translation);
            else if (word.All(c => char.IsLetter(c)))
                keywords.Add(word);
        }
        
        if (keywords.Count < 3)
            keywords.AddRange(new[] { "nature landscape", "city life", "person silhouette" });
        
        return keywords.Distinct().Take(5).ToList();
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
