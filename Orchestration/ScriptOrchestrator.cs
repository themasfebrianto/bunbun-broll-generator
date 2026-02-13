using System.Text.Json;
using BunbunBroll.Data;
using BunbunBroll.Models;
using BunbunBroll.Orchestration.Events;
using BunbunBroll.Orchestration.Services;
using BunbunBroll.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BunbunBroll.Orchestration;

/// <summary>
/// Main orchestrator for pattern-based script generation.
/// Uses PhaseCoordinator with PromptBuilder, SectionFormatter, and PatternValidator
/// for structured prompt construction, output formatting, and validation-driven retry.
/// Ported from ScriptFlow's ScriptOrchestratorV2 architecture.
/// </summary>
public class ScriptOrchestrator : IScriptOrchestrator
{
    private readonly IPatternRegistry _patternRegistry;
    private readonly IIntelligenceService _intelligenceService;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ScriptOrchestrator> _logger;

    public event EventHandler<PhaseProgressEventArgs>? OnPhaseProgress;
    public event EventHandler<SessionProgressEventArgs>? OnSessionProgress;

    public ScriptOrchestrator(
        IPatternRegistry patternRegistry,
        IIntelligenceService intelligenceService,
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<ScriptOrchestrator> logger)
    {
        _patternRegistry = patternRegistry;
        _intelligenceService = intelligenceService;
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _logger = logger;
    }

    public IEnumerable<string> ListPatterns() => _patternRegistry.ListPatterns();

    public PatternConfiguration? GetPattern(string patternId) => _patternRegistry.Get(patternId);

    public async Task<(ScriptGenerationSession Session, GenerationContext Context)> InitializeSessionAsync(
        ScriptConfig config, string patternId, string? customId = null)
    {
        var pattern = _patternRegistry.Get(patternId)
            ?? throw new ArgumentException($"Pattern '{patternId}' not found");

        var sessionId = customId ?? Guid.NewGuid().ToString("N")[..8];
        var baseDir = _configuration["ScriptOutput:BaseDirectory"] ?? "output/scripts";
        var outputDir = Path.Combine(baseDir, sessionId);
        Directory.CreateDirectory(outputDir);

        // Truncate fields to prevent MaxLength violations from LLM-generated content
        var safeTopic = config.Topic.Length > 500 ? config.Topic[..497] + "..." : config.Topic;
        var safeChannel = config.ChannelName.Length > 100 ? config.ChannelName[..97] + "..." : config.ChannelName;
        var safePatternId = patternId.Length > 100 ? patternId[..100] : patternId;

        var session = new ScriptGenerationSession
        {
            Id = sessionId,
            PatternId = safePatternId,
            Topic = safeTopic,
            Outline = config.Outline,
            TargetDurationMinutes = config.TargetDurationMinutes,
            SourceReferences = config.SourceReferences,
            ChannelName = safeChannel,
            Status = SessionStatus.Pending,
            OutputDirectory = outputDir,
            CreatedAt = DateTime.UtcNow
        };

        // Mark last phase as IsFinalPhase
        var orderedPhases = pattern.GetOrderedPhases().ToList();
        if (orderedPhases.Count > 0)
        {
            orderedPhases.Last().IsFinalPhase = true;
        }

        // Create phase records from pattern
        foreach (var phaseDef in orderedPhases)
        {
            session.Phases.Add(new ScriptGenerationPhase
            {
                SessionId = sessionId,
                PhaseId = phaseDef.Id,
                PhaseName = phaseDef.Name,
                Order = phaseDef.Order,
                Status = PhaseStatus.Pending
            });
        }

        // Save to DB
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ScriptGenerationSessions.Add(session);
        await db.SaveChangesAsync();

        var context = new GenerationContext
        {
            SessionId = sessionId,
            Config = config,
            Pattern = pattern,
            OutputDirectory = outputDir
        };

        _logger.LogInformation("Initialized session {SessionId} with pattern {PatternId} ({PhaseCount} phases)",
            sessionId, patternId, pattern.Phases.Count);

        return (session, context);
    }

    public async Task<ScriptGenerationSession?> LoadSessionAsync(string sessionId)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ScriptGenerationSessions
            .Include(s => s.Phases)
            .FirstOrDefaultAsync(s => s.Id == sessionId);
    }

    public async Task SaveSessionAsync(ScriptGenerationSession session)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ScriptGenerationSessions.Update(session);
        await db.SaveChangesAsync();
    }

    public async Task<PatternResult> GenerateAllAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var session = await LoadSessionAsync(sessionId)
            ?? throw new ArgumentException($"Session '{sessionId}' not found");

        var pattern = _patternRegistry.Get(session.PatternId)
            ?? throw new InvalidOperationException($"Pattern '{session.PatternId}' not found");

        var context = new GenerationContext
        {
            SessionId = sessionId,
            Config = new ScriptConfig
            {
                Topic = session.Topic,
                Outline = session.Outline,
                TargetDurationMinutes = session.TargetDurationMinutes,
                SourceReferences = session.SourceReferences,
                ChannelName = session.ChannelName
            },
            Pattern = pattern,
            OutputDirectory = session.OutputDirectory
        };

        return await ExecuteGenerationAsync(session, context, cancellationToken);
    }

    public async Task<PatternResult> ResumeAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var session = await LoadSessionAsync(sessionId)
            ?? throw new ArgumentException($"Session '{sessionId}' not found");

        var pattern = _patternRegistry.Get(session.PatternId)
            ?? throw new InvalidOperationException($"Pattern '{session.PatternId}' not found");

        var context = new GenerationContext
        {
            SessionId = sessionId,
            Config = new ScriptConfig
            {
                Topic = session.Topic,
                Outline = session.Outline,
                TargetDurationMinutes = session.TargetDurationMinutes,
                SourceReferences = session.SourceReferences,
                ChannelName = session.ChannelName
            },
            Pattern = pattern,
            OutputDirectory = session.OutputDirectory
        };

        // Load completed phases into context
        foreach (var completedDbPhase in session.Phases.Where(p => p.Status == PhaseStatus.Completed).OrderBy(p => p.Order))
        {
            var content = "";
            if (!string.IsNullOrEmpty(completedDbPhase.ContentFilePath) && File.Exists(completedDbPhase.ContentFilePath))
            {
                content = await File.ReadAllTextAsync(completedDbPhase.ContentFilePath);
            }
            context.CompletedPhases.Add(new CompletedPhase
            {
                PhaseId = completedDbPhase.PhaseId,
                PhaseName = completedDbPhase.PhaseName,
                Order = completedDbPhase.Order,
                Content = content,
                WordCount = completedDbPhase.WordCount ?? 0,
                DurationSeconds = completedDbPhase.DurationSeconds ?? 0
            });
        }

        return await ExecuteGenerationAsync(session, context, cancellationToken);
    }

    private async Task<PatternResult> ExecuteGenerationAsync(ScriptGenerationSession session, GenerationContext context, CancellationToken cancellationToken = default)
    {
        var result = new PatternResult { SessionId = session.Id, IsSuccess = true };
        // Deep clone phases to avoid modifying the shared pattern instance
        var orderedPhases = context.Pattern.GetOrderedPhases()
            .Select(p => JsonSerializer.Deserialize<PhaseDefinition>(JsonSerializer.Serialize(p)))
            .Where(p => p != null)
            .Cast<PhaseDefinition>()
            .ToList();

        // Scale targets based on user preference
        if (context.Config.TargetDurationMinutes > 0)
        {
            // Calculate total base duration from pattern (average of min/max)
            double patternBaseMinutes = orderedPhases.Sum(p => (p.DurationTarget.Min + p.DurationTarget.Max) / 2.0) / 60.0;
            
            if (patternBaseMinutes > 0)
            {
                double scaleFactor = context.Config.TargetDurationMinutes / patternBaseMinutes;
                _logger.LogInformation("Scaling script targets by factor {Scale:F2} (Target: {Target}m / Base: {Base:F1}m)", 
                    scaleFactor, context.Config.TargetDurationMinutes, patternBaseMinutes);

                foreach (var phase in orderedPhases)
                {
                    // Scale Duration
                    phase.DurationTarget.Min = (int)(phase.DurationTarget.Min * scaleFactor);
                    phase.DurationTarget.Max = (int)(phase.DurationTarget.Max * scaleFactor);

                    // Scale Word Count (assuming 150 wpm average)
                    // We scale the original targets to preserve the phase's relative "density"
                    phase.WordCountTarget.Min = (int)(phase.WordCountTarget.Min * scaleFactor);
                    phase.WordCountTarget.Max = (int)(phase.WordCountTarget.Max * scaleFactor);
                }
            }
        }

        // Mark final phase
        if (orderedPhases.Count > 0)
        {
            orderedPhases.Last().IsFinalPhase = true;
        }

        // Create PhaseCoordinator (ScriptFlow's architecture)
        var coordinator = new PhaseCoordinator(_intelligenceService, _logger);

        // Distribute outline and beats across phases
        var hasOutline = !string.IsNullOrWhiteSpace(context.Config.Outline);
        var hasBeats = context.Config.MustHaveBeats?.Count > 0;

        if (hasOutline || hasBeats)
        {
            OnSessionProgress?.Invoke(this, new SessionProgressEventArgs
            {
                SessionId = session.Id,
                Status = "Planning",
                CompletedPhases = 0,
                TotalPhases = orderedPhases.Count,
                Message = "📋 Mendistribusikan outline dan story beats ke setiap fase..."
            });

            if (hasOutline)
            {
                var outlinePlanner = new OutlinePlanner(_intelligenceService, _logger);
                var distribution = await outlinePlanner.DistributeAsync(
                    context.Config.Outline!,
                    orderedPhases,
                    context.Config.Topic,
                    context.Config.MustHaveBeats);

                if (distribution.Count > 0)
                {
                    context.SetSharedData("outlineDistribution", distribution);
                    session.OutlineDistributionJson = JsonSerializer.Serialize(distribution);
                    _logger.LogInformation("Outline distributed across {Count} phases", distribution.Count);
                }
            }

            // Distribute beats proportionally across phases
            if (hasBeats)
            {
                var beatDistribution = DistributeBeatsAcrossPhases(context.Config.MustHaveBeats!, orderedPhases);
                context.SetSharedData("beatDistribution", beatDistribution);
                _logger.LogInformation("Distributed {BeatCount} beats across {PhaseCount} phases",
                    context.Config.MustHaveBeats!.Count, beatDistribution.Count);
            }

            OnSessionProgress?.Invoke(this, new SessionProgressEventArgs
            {
                SessionId = session.Id,
                Status = "Planning",
                CompletedPhases = 0,
                TotalPhases = orderedPhases.Count,
                Message = $"✅ Outline & beats terdistribusi ke {orderedPhases.Count} fase"
            });
        }

        // Update session status
        session.Status = SessionStatus.Running;
        session.UpdatedAt = DateTime.UtcNow;
        await SaveSessionAsync(session);

        OnSessionProgress?.Invoke(this, new SessionProgressEventArgs
        {
            SessionId = session.Id,
            Status = "Running",
            CompletedPhases = context.CompletedPhases.Count,
            TotalPhases = orderedPhases.Count,
            Message = "Memulai generasi script..."
        });

        foreach (var phaseDef in orderedPhases)
        {
            // Check for cancellation before each phase
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogInformation("Generation cancelled for session {SessionId} before phase {PhaseId}", session.Id, phaseDef.Id);
                session.Status = SessionStatus.Failed;
                session.ErrorMessage = "Generation dibatalkan oleh user";
                session.UpdatedAt = DateTime.UtcNow;
                await SaveSessionAsync(session);
                result.IsSuccess = false;
                result.Errors.Add("Generation dibatalkan oleh user");
                return result;
            }

            // Skip completed phases (for resume)
            if (context.CompletedPhases.Any(cp => cp.PhaseId == phaseDef.Id))
            {
                _logger.LogInformation("Skipping completed phase: {PhaseId}", phaseDef.Id);
                continue;
            }

            // Find DB phase record
            var dbPhase = session.Phases.FirstOrDefault(p => p.PhaseId == phaseDef.Id);
            if (dbPhase == null) continue;

            try
            {
                // Look up assigned outline points for this phase
                List<string>? phaseOutlinePoints = null;
                if (context.SharedData.TryGetValue("outlineDistribution", out var distObj)
                    && distObj is Dictionary<string, List<string>> dist
                    && dist.TryGetValue(phaseDef.Id, out var pts))
                {
                    phaseOutlinePoints = pts;
                }

                // Build Global Context (all previous phases except immediate one which is already in PreviousContent)
                var globalContext = context.CompletedPhases
                    .Where(cp => cp.Order < phaseDef.Order - 1) // Skip immediate predecessor
                    .OrderBy(cp => cp.Order)
                    .Select(cp => $"[Phase {cp.PhaseName} (FULL CONTENT PREVIEW)]: {cp.Content.Substring(0, Math.Min(cp.Content.Length, 2000))}...") // Take up to 2000 chars (effectively whole phase)
                    .ToList();

                // Pass outline points and global context to PhaseCoordinator via shared data or context
                // Note: PhaseCoordinator will need to read these. We can set them in a temporary way or update PhaseCoordinator.
                // Since PhaseCoordinator builds PhaseContext internally, we need to pass these via GenerationContext.SharedData
                // or pass them as arguments.
                
                // To avoid changing PhaseCoordinator signature too much, let's use SharedData to pass transient phase-specific data
                context.SetSharedData("currentPhaseOutline", phaseOutlinePoints);
                context.SetSharedData("currentGlobalContext", globalContext);

                // Look up assigned beats for this phase
                List<string>? phaseBeats = null;
                if (context.SharedData.TryGetValue("beatDistribution", out var beatDistObj)
                    && beatDistObj is Dictionary<string, List<string>> beatDist
                    && beatDist.TryGetValue(phaseDef.Id, out var beats))
                {
                    phaseBeats = beats;
                }
                context.SetSharedData("currentPhaseBeats", phaseBeats);

                OnPhaseProgress?.Invoke(this, new PhaseProgressEventArgs
                {
                    SessionId = session.Id,
                    PhaseId = phaseDef.Id,
                    PhaseName = phaseDef.Name,
                    PhaseOrder = phaseDef.Order,
                    TotalPhases = orderedPhases.Count,
                    Status = "InProgress",
                    Message = $"Menulis {phaseDef.Name}...",
                    OutlinePoints = phaseOutlinePoints,
                    DurationTarget = $"{(phaseDef.DurationTarget.Min / 60.0):0.#}-{(phaseDef.DurationTarget.Max / 60.0):0.#} m"
                });

                dbPhase.Status = PhaseStatus.InProgress;
                await SaveSessionAsync(session);

                // Use PhaseCoordinator for generation with retry + validation
                var generatedPhase = await coordinator.ExecutePhaseAsync(phaseDef, context);

                // Save content to file
                var filePath = Path.Combine(context.OutputDirectory, $"{phaseDef.Order:D2}-{phaseDef.Id}.md");
                await File.WriteAllTextAsync(filePath, generatedPhase.Content);

                // Update DB phase
                dbPhase.Status = PhaseStatus.Completed;
                dbPhase.ContentFilePath = filePath;
                dbPhase.WordCount = generatedPhase.WordCount;
                dbPhase.DurationSeconds = generatedPhase.DurationSeconds;
                dbPhase.IsValidated = generatedPhase.IsValidated;
                dbPhase.WarningsJson = generatedPhase.Warnings.Count > 0
                    ? JsonSerializer.Serialize(generatedPhase.Warnings) : null;
                dbPhase.CompletedAt = DateTime.UtcNow;
                session.UpdatedAt = DateTime.UtcNow;
                await SaveSessionAsync(session);

                // Add to result and context
                result.GeneratedPhases.Add(generatedPhase);

                context.CompletedPhases.Add(new CompletedPhase
                {
                    PhaseId = phaseDef.Id,
                    PhaseName = phaseDef.Name,
                    Order = phaseDef.Order,
                    Content = generatedPhase.Content,
                    WordCount = generatedPhase.WordCount,
                    DurationSeconds = generatedPhase.DurationSeconds
                });

                OnPhaseProgress?.Invoke(this, new PhaseProgressEventArgs
                {
                    SessionId = session.Id,
                    PhaseId = phaseDef.Id,
                    PhaseName = phaseDef.Name,
                    PhaseOrder = phaseDef.Order,
                    TotalPhases = orderedPhases.Count,
                    Status = "Completed",
                    Message = $"{phaseDef.Name} selesai ({generatedPhase.WordCount} kata)"
                });

                OnSessionProgress?.Invoke(this, new SessionProgressEventArgs
                {
                    SessionId = session.Id,
                    Status = "Running",
                    CompletedPhases = context.CompletedPhases.Count,
                    TotalPhases = orderedPhases.Count,
                    Message = $"Fase {context.CompletedPhases.Count}/{orderedPhases.Count} selesai"
                });

                _logger.LogInformation("Phase {PhaseId} completed: {WordCount} words, {Duration:F0}s (validated: {IsValidated})",
                    phaseDef.Id, generatedPhase.WordCount, generatedPhase.DurationSeconds, generatedPhase.IsValidated);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Phase {PhaseId} failed", phaseDef.Id);
                dbPhase.Status = PhaseStatus.Failed;
                session.Status = SessionStatus.Failed;
                session.ErrorMessage = $"Phase '{phaseDef.Name}' failed: {ex.Message}";
                session.UpdatedAt = DateTime.UtcNow;
                await SaveSessionAsync(session);

                result.IsSuccess = false;
                result.Errors.Add($"Phase '{phaseDef.Name}' failed: {ex.Message}");

                OnPhaseProgress?.Invoke(this, new PhaseProgressEventArgs
                {
                    SessionId = session.Id,
                    PhaseId = phaseDef.Id,
                    PhaseName = phaseDef.Name,
                    PhaseOrder = phaseDef.Order,
                    TotalPhases = orderedPhases.Count,
                    Status = "Failed",
                    Message = $"Gagal: {ex.Message}"
                });

                return result;
            }
        }

        // Mark session complete
        session.Status = SessionStatus.Completed;
        session.CompletedAt = DateTime.UtcNow;
        session.UpdatedAt = DateTime.UtcNow;
        await SaveSessionAsync(session);

        result.TotalWordCount = result.GeneratedPhases.Sum(p => p.WordCount);
        result.TotalDurationSeconds = result.GeneratedPhases.Sum(p => p.DurationSeconds);

        OnSessionProgress?.Invoke(this, new SessionProgressEventArgs
        {
            SessionId = session.Id,
            Status = "Completed",
            CompletedPhases = orderedPhases.Count,
            TotalPhases = orderedPhases.Count,
            Message = $"Script selesai! {result.TotalWordCount} kata, ~{result.TotalDurationSeconds / 60:F0} menit"
        });

        _logger.LogInformation("Session {SessionId} completed: {TotalWords} words, {TotalDuration:F0}s",
            session.Id, result.TotalWordCount, result.TotalDurationSeconds);

        // Auto-export session to git-tracked JSON
        try
        {
            var syncService = _serviceProvider.GetRequiredService<SessionSyncService>();
            await syncService.ExportSessionAsync(session.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to auto-export session {SessionId} after completion", session.Id);
        }

        return result;
    }

    public async Task<GeneratedPhase> RegeneratePhaseAsync(string sessionId, string phaseId, CancellationToken cancellationToken = default)
    {
        var session = await LoadSessionAsync(sessionId)
            ?? throw new ArgumentException($"Session '{sessionId}' not found");

        var pattern = _patternRegistry.Get(session.PatternId)
            ?? throw new InvalidOperationException($"Pattern '{session.PatternId}' not found");

        var phaseDef = pattern.Phases.FirstOrDefault(p => p.Id == phaseId)
            ?? throw new ArgumentException($"Phase '{phaseId}' not found in pattern");

        // Mark final phase
        var orderedPhases = pattern.GetOrderedPhases().ToList();
        if (orderedPhases.Count > 0)
            orderedPhases.Last().IsFinalPhase = true;

        // Build context with all completed phases
        var context = new GenerationContext
        {
            SessionId = sessionId,
            Config = new ScriptConfig
            {
                Topic = session.Topic,
                Outline = session.Outline,
                TargetDurationMinutes = session.TargetDurationMinutes,
                SourceReferences = session.SourceReferences,
                ChannelName = session.ChannelName
            },
            Pattern = pattern,
            OutputDirectory = session.OutputDirectory
        };

        // Load all OTHER completed phases into context (exclude the one being regenerated)
        foreach (var dbPhase in session.Phases.Where(p => p.Status == PhaseStatus.Completed && p.PhaseId != phaseId).OrderBy(p => p.Order))
        {
            var content = "";
            if (!string.IsNullOrEmpty(dbPhase.ContentFilePath) && File.Exists(dbPhase.ContentFilePath))
                content = await File.ReadAllTextAsync(dbPhase.ContentFilePath);

            context.CompletedPhases.Add(new CompletedPhase
            {
                PhaseId = dbPhase.PhaseId,
                PhaseName = dbPhase.PhaseName,
                Order = dbPhase.Order,
                Content = content,
                WordCount = dbPhase.WordCount ?? 0,
                DurationSeconds = dbPhase.DurationSeconds ?? 0
            });
        }

        var coordinator = new PhaseCoordinator(_intelligenceService, _logger);

        // Distribute outline if provided (so regenerated phase gets its outline points)
        // Distribute outline if provided (so regenerated phase gets its outline points)
        if (!string.IsNullOrWhiteSpace(context.Config.Outline))
        {
            Dictionary<string, List<string>> distribution = new();
            
            // Try to load from session first
            if (!string.IsNullOrEmpty(session.OutlineDistributionJson))
            {
                try 
                {
                    distribution = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(session.OutlineDistributionJson) 
                        ?? new Dictionary<string, List<string>>();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to deserialize saved outline distribution during regeneration");
                }
            }

            // Fallback to fresh distribution if needed
            if (distribution.Count == 0)
            {
                var outlinePlanner = new OutlinePlanner(_intelligenceService, _logger);
                distribution = await outlinePlanner.DistributeAsync(
                    context.Config.Outline,
                    orderedPhases,
                    context.Config.Topic);
                
                // Save for future use
                if (distribution.Count > 0)
                {
                    session.OutlineDistributionJson = JsonSerializer.Serialize(distribution);
                    await SaveSessionAsync(session);
                }
            }

            if (distribution.Count > 0)
            {
                context.SetSharedData("outlineDistribution", distribution);
            }
        }

        var generatedPhase = await coordinator.ExecutePhaseAsync(phaseDef, context);

        // Save content to file
        var filePath = Path.Combine(context.OutputDirectory, $"{phaseDef.Order:D2}-{phaseDef.Id}.md");
        await File.WriteAllTextAsync(filePath, generatedPhase.Content);

        // Update DB phase
        var targetPhase = session.Phases.FirstOrDefault(p => p.PhaseId == phaseId);
        if (targetPhase != null)
        {
            targetPhase.Status = PhaseStatus.Completed;
            targetPhase.ContentFilePath = filePath;
            targetPhase.WordCount = generatedPhase.WordCount;
            targetPhase.DurationSeconds = generatedPhase.DurationSeconds;
            targetPhase.IsValidated = generatedPhase.IsValidated;
            targetPhase.WarningsJson = generatedPhase.Warnings.Count > 0
                ? System.Text.Json.JsonSerializer.Serialize(generatedPhase.Warnings) : null;
            targetPhase.CompletedAt = DateTime.UtcNow;
            session.UpdatedAt = DateTime.UtcNow;
            await SaveSessionAsync(session);
        }

        _logger.LogInformation("Regenerated phase {PhaseId} for session {SessionId}: {WordCount} words",
            phaseId, sessionId, generatedPhase.WordCount);

        // Auto-export session after phase regeneration
        try
        {
            var syncService = _serviceProvider.GetRequiredService<SessionSyncService>();
            await syncService.ExportSessionAsync(sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to auto-export session {SessionId} after regeneration", sessionId);
        }

        return generatedPhase;
    }

    public async Task DeleteSessionAsync(string sessionId)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var session = await db.ScriptGenerationSessions
            .Include(s => s.Phases)
            .FirstOrDefaultAsync(s => s.Id == sessionId);

        if (session == null) return;

        // Delete output directory
        if (!string.IsNullOrEmpty(session.OutputDirectory) && Directory.Exists(session.OutputDirectory))
        {
            try { Directory.Delete(session.OutputDirectory, recursive: true); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to delete output directory for {SessionId}", sessionId); }
        }

        db.ScriptGenerationSessions.Remove(session);
        await db.SaveChangesAsync();

        _logger.LogInformation("Deleted session {SessionId}", sessionId);
    }

    /// <summary>
    /// Distribute story beats proportionally across phases based on their duration weights.
    /// Opening/closing get fewer beats; core content phases get more.
    /// </summary>
    private static Dictionary<string, List<string>> DistributeBeatsAcrossPhases(
        List<string> beats, List<PhaseDefinition> phases)
    {
        var result = new Dictionary<string, List<string>>();
        if (beats.Count == 0 || phases.Count == 0) return result;

        // Calculate weight for each phase based on avg duration
        var phaseWeights = phases.Select(p =>
            (p.DurationTarget.Min + p.DurationTarget.Max) / 2.0).ToList();
        var totalWeight = phaseWeights.Sum();

        // Distribute beats proportionally
        int beatIndex = 0;
        for (int i = 0; i < phases.Count; i++)
        {
            var proportion = phaseWeights[i] / totalWeight;
            var count = (int)Math.Round(beats.Count * proportion);

            // Ensure at least 1 beat per phase if beats remain, and don't exceed total
            if (count == 0 && beatIndex < beats.Count) count = 1;
            count = Math.Min(count, beats.Count - beatIndex);

            if (count > 0)
            {
                result[phases[i].Id] = beats.Skip(beatIndex).Take(count).ToList();
                beatIndex += count;
            }
        }

        // Assign any remaining beats to the largest phase
        if (beatIndex < beats.Count)
        {
            var largestPhase = phases.OrderByDescending(p =>
                (p.DurationTarget.Min + p.DurationTarget.Max) / 2.0).First();
            if (!result.ContainsKey(largestPhase.Id))
                result[largestPhase.Id] = new List<string>();
            result[largestPhase.Id].AddRange(beats.Skip(beatIndex));
        }

        return result;
    }
}
