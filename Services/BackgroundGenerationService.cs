using System.Collections.Concurrent;
using BunbunBroll.Models;
using BunbunBroll.Orchestration;
using BunbunBroll.Orchestration.Events;
using Microsoft.Extensions.Logging;

namespace BunbunBroll.Services;

/// <summary>
/// Singleton service that manages concurrent script generation jobs in the background.
/// Creates its own DI scope per job so ScriptOrchestrator (Scoped) works correctly.
/// Publishes progress via GenerationEventBus.
/// </summary>
public class BackgroundGenerationService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly GenerationEventBus _eventBus;
    private readonly ILogger<BackgroundGenerationService> _logger;
    private readonly ConcurrentDictionary<string, GenerationJob> _activeJobs = new();

    public BackgroundGenerationService(
        IServiceProvider serviceProvider,
        GenerationEventBus eventBus,
        ILogger<BackgroundGenerationService> logger)
    {
        _serviceProvider = serviceProvider;
        _eventBus = eventBus;
        _logger = logger;
    }

    /// <summary>
    /// Start full generation for a session in the background. Returns immediately.
    /// </summary>
    public void EnqueueGeneration(string sessionId)
    {
        if (_activeJobs.ContainsKey(sessionId))
        {
            _logger.LogWarning("Session {SessionId} is already generating", sessionId);
            return;
        }

        var cts = new CancellationTokenSource();
        var job = new GenerationJob
        {
            SessionId = sessionId,
            Status = GenerationJobStatus.Running,
            StartedAt = DateTime.UtcNow,
            CancellationTokenSource = cts
        };

        if (!_activeJobs.TryAdd(sessionId, job))
        {
            _logger.LogWarning("Failed to add job for session {SessionId}", sessionId);
            return;
        }

        _logger.LogInformation("Enqueued generation for session {SessionId}", sessionId);

        _ = Task.Run(async () =>
        {
            using var scope = _serviceProvider.CreateScope();
            var orchestrator = scope.ServiceProvider.GetRequiredService<IScriptOrchestrator>();

            // Wire orchestrator events → event bus
            void OnPhaseProgress(object? sender, PhaseProgressEventArgs e)
            {
                var eventType = e.Status switch
                {
                    "InProgress" => GenerationEventType.PhaseStarted,
                    "Completed" => GenerationEventType.PhaseCompleted,
                    "Failed" => GenerationEventType.PhaseFailed,
                    _ => GenerationEventType.SessionProgress
                };

                var evt = new GenerationProgressEvent
                {
                    SessionId = sessionId,
                    Type = eventType,
                    Message = e.Message ?? "",
                    PhaseId = e.PhaseId,
                    PhaseName = e.PhaseName,
                    PhaseOrder = e.PhaseOrder,
                    PhaseStatus = e.Status,
                    OutlinePoints = e.OutlinePoints,
                    DurationTarget = e.DurationTarget,
                    CompletedPhases = job.CompletedPhases,
                    TotalPhases = e.TotalPhases
                };

                if (e.Status == "Completed")
                    job.CompletedPhases++;

                _eventBus.Publish(sessionId, evt);
            }

            void OnSessionProgress(object? sender, SessionProgressEventArgs e)
            {
                job.CompletedPhases = e.CompletedPhases;
                job.TotalPhases = e.TotalPhases;
                job.Message = e.Message ?? "";

                _eventBus.Publish(sessionId, new GenerationProgressEvent
                {
                    SessionId = sessionId,
                    Type = GenerationEventType.SessionProgress,
                    Message = e.Message ?? "",
                    CompletedPhases = e.CompletedPhases,
                    TotalPhases = e.TotalPhases
                });
            }

            orchestrator.OnPhaseProgress += OnPhaseProgress;
            orchestrator.OnSessionProgress += OnSessionProgress;

            try
            {
                var result = await orchestrator.GenerateAllAsync(sessionId);
                job.Status = result.IsSuccess ? GenerationJobStatus.Completed : GenerationJobStatus.Failed;

                _eventBus.Publish(sessionId, new GenerationProgressEvent
                {
                    SessionId = sessionId,
                    Type = result.IsSuccess ? GenerationEventType.SessionCompleted : GenerationEventType.SessionFailed,
                    Message = result.IsSuccess
                        ? $"Script selesai! {result.TotalWordCount} kata, ~{result.TotalDurationSeconds / 60:F0} menit"
                        : string.Join("; ", result.Errors),
                    CompletedPhases = job.CompletedPhases,
                    TotalPhases = job.TotalPhases
                });

                _logger.LogInformation("Background generation for {SessionId} completed: {Status}",
                    sessionId, job.Status);
            }
            catch (Exception ex)
            {
                job.Status = GenerationJobStatus.Failed;
                job.Message = ex.Message;

                _eventBus.Publish(sessionId, new GenerationProgressEvent
                {
                    SessionId = sessionId,
                    Type = GenerationEventType.SessionFailed,
                    Message = $"Error: {ex.Message}",
                    CompletedPhases = job.CompletedPhases,
                    TotalPhases = job.TotalPhases
                });

                _logger.LogError(ex, "Background generation for {SessionId} failed", sessionId);
            }
            finally
            {
                orchestrator.OnPhaseProgress -= OnPhaseProgress;
                orchestrator.OnSessionProgress -= OnSessionProgress;

                // Keep job in dictionary briefly for status checks, then remove
                _ = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(5));
                    _activeJobs.TryRemove(sessionId, out _);
                });
            }
        });
    }

    /// <summary>
    /// Start single-phase regeneration in the background. Returns immediately.
    /// </summary>
    public void EnqueueRegeneration(string sessionId, string phaseId)
    {
        var jobKey = $"{sessionId}:{phaseId}";
        if (_activeJobs.ContainsKey(jobKey))
        {
            _logger.LogWarning("Regeneration for {SessionId}/{PhaseId} already running", sessionId, phaseId);
            return;
        }

        var job = new GenerationJob
        {
            SessionId = sessionId,
            Status = GenerationJobStatus.Running,
            StartedAt = DateTime.UtcNow,
            CancellationTokenSource = new CancellationTokenSource()
        };
        _activeJobs.TryAdd(jobKey, job);

        _logger.LogInformation("Enqueued regeneration for {SessionId}/{PhaseId}", sessionId, phaseId);

        _ = Task.Run(async () =>
        {
            using var scope = _serviceProvider.CreateScope();
            var orchestrator = scope.ServiceProvider.GetRequiredService<IScriptOrchestrator>();

            try
            {
                var result = await orchestrator.RegeneratePhaseAsync(sessionId, phaseId);

                _eventBus.Publish(sessionId, new GenerationProgressEvent
                {
                    SessionId = sessionId,
                    Type = GenerationEventType.PhaseCompleted,
                    Message = $"{result.PhaseName} regenerated ({result.WordCount} kata)",
                    PhaseId = phaseId,
                    PhaseName = result.PhaseName,
                    CompletedPhases = 1,
                    TotalPhases = 1
                });
            }
            catch (Exception ex)
            {
                _eventBus.Publish(sessionId, new GenerationProgressEvent
                {
                    SessionId = sessionId,
                    Type = GenerationEventType.PhaseFailed,
                    Message = $"Regenerate gagal: {ex.Message}",
                    PhaseId = phaseId
                });

                _logger.LogError(ex, "Regeneration for {SessionId}/{PhaseId} failed", sessionId, phaseId);
            }
            finally
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(3));
                    _activeJobs.TryRemove(jobKey, out _);
                });
            }
        });
    }

    /// <summary>
    /// Check if a session is currently generating.
    /// </summary>
    public bool IsRunning(string sessionId)
    {
        return _activeJobs.ContainsKey(sessionId) &&
               _activeJobs[sessionId].Status == GenerationJobStatus.Running;
    }

    /// <summary>
    /// Get all active jobs (for session list UI).
    /// </summary>
    public IReadOnlyDictionary<string, GenerationJob> GetActiveJobs()
    {
        return _activeJobs;
    }
}

public class GenerationJob
{
    public string SessionId { get; set; } = "";
    public GenerationJobStatus Status { get; set; }
    public int CompletedPhases { get; set; }
    public int TotalPhases { get; set; }
    public string Message { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public CancellationTokenSource CancellationTokenSource { get; set; } = new();
}

public enum GenerationJobStatus
{
    Running,
    Completed,
    Failed
}
