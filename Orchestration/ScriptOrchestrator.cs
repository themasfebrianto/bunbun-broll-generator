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

        var session = new ScriptGenerationSession
        {
            Id = sessionId,
            PatternId = patternId,
            Topic = config.Topic,
            Outline = config.Outline,
            TargetDurationMinutes = config.TargetDurationMinutes,
            SourceReferences = config.SourceReferences,
            ChannelName = config.ChannelName,
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

    public async Task<PatternResult> GenerateAllAsync(string sessionId)
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

        return await ExecuteGenerationAsync(session, context);
    }

    public async Task<PatternResult> ResumeAsync(string sessionId)
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

        return await ExecuteGenerationAsync(session, context);
    }

    private async Task<PatternResult> ExecuteGenerationAsync(ScriptGenerationSession session, GenerationContext context)
    {
        var result = new PatternResult { SessionId = session.Id, IsSuccess = true };
        var orderedPhases = context.Pattern.GetOrderedPhases().ToList();

        // Mark final phase
        if (orderedPhases.Count > 0)
        {
            orderedPhases.Last().IsFinalPhase = true;
        }

        // Create PhaseCoordinator (ScriptFlow's architecture)
        var coordinator = new PhaseCoordinator(_intelligenceService, _logger);

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
                OnPhaseProgress?.Invoke(this, new PhaseProgressEventArgs
                {
                    SessionId = session.Id,
                    PhaseId = phaseDef.Id,
                    PhaseName = phaseDef.Name,
                    PhaseOrder = phaseDef.Order,
                    TotalPhases = orderedPhases.Count,
                    Status = "InProgress",
                    Message = $"Menulis {phaseDef.Name}..."
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

        return result;
    }

    public async Task<GeneratedPhase> RegeneratePhaseAsync(string sessionId, string phaseId)
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
}
