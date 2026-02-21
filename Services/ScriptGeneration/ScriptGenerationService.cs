using System.Text;
using BunbunBroll.Data;
using BunbunBroll.Models;
using BunbunBroll.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BunbunBroll.Services;

/// <summary>
/// High-level service for the Script Generator UI page.
/// Coordinates between orchestrator and database for session management and export.
/// </summary>
public class ScriptGenerationService : IScriptGenerationService
{
    private readonly IScriptOrchestrator _orchestrator;
    private readonly IPatternRegistry _patternRegistry;
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ScriptGenerationService> _logger;

    public ScriptGenerationService(
        IScriptOrchestrator orchestrator,
        IPatternRegistry patternRegistry,
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<ScriptGenerationService> logger)
    {
        _orchestrator = orchestrator;
        _patternRegistry = patternRegistry;
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<List<ScriptPattern>> GetAvailablePatternsAsync()
    {
        var patterns = new List<ScriptPattern>();
        foreach (var patternId in _patternRegistry.ListPatterns())
        {
            var config = _patternRegistry.Get(patternId);
            if (config != null)
            {
                patterns.Add(new ScriptPattern
                {
                    Id = patternId,
                    Name = config.Name,
                    Description = config.Description,
                    PhaseCount = config.Phases.Count,
                    Configuration = config
                });
            }
        }
        return patterns;
    }

    public async Task<ScriptGenerationSession> CreateSessionAsync(
        string patternId, string topic, string? outline, int targetDuration,
        string? sourceReferences = null, string channelName = "")
    {
        if (!_patternRegistry.Exists(patternId))
        {
            throw new ArgumentException($"STRICT MODE: Pattern '{patternId}' not found. Cannot create session.");
        }

        var config = new ScriptConfig
        {
            Topic = topic,
            Outline = outline,
            TargetDurationMinutes = targetDuration,
            SourceReferences = sourceReferences,
            ChannelName = channelName
        };

        var (session, _) = await _orchestrator.InitializeSessionAsync(config, patternId);
        return session;
    }

    public async Task<ScriptGenerationSession> GenerateAsync(string sessionId)
    {
        var result = await _orchestrator.GenerateAllAsync(sessionId);
        
        var session = await _orchestrator.LoadSessionAsync(sessionId);
        if (session == null)
        {
            throw new InvalidOperationException($"Session '{sessionId}' not found after generation");
        }

        return session;
    }

    public async Task<ScriptGenerationSession?> GetSessionAsync(string sessionId)
    {
        return await _orchestrator.LoadSessionAsync(sessionId);
    }

    public async Task<List<ScriptGenerationSession>> ListSessionsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.ScriptGenerationSessions
            .Include(s => s.Phases)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();
    }

    public async Task<string> ExportScriptAsync(string sessionId, bool clean = false)
    {
        var session = await _orchestrator.LoadSessionAsync(sessionId)
            ?? throw new ArgumentException($"Session '{sessionId}' not found");

        if (session.Status != SessionStatus.Completed)
        {
            throw new InvalidOperationException("Cannot export incomplete session");
        }

        var sb = new StringBuilder();
        
        if (!clean)
        {
            sb.AppendLine($"# {session.Topic}");
            sb.AppendLine($"Pattern: {session.PatternId} | Generated: {session.CompletedAt:yyyy-MM-dd HH:mm}");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }

        foreach (var phase in session.Phases.OrderBy(p => p.Order))
        {
            if (!string.IsNullOrEmpty(phase.ContentFilePath) && File.Exists(phase.ContentFilePath))
            {
                var content = await File.ReadAllTextAsync(phase.ContentFilePath);
                
                if (!clean)
                {
                    sb.AppendLine($"## {phase.PhaseName}");
                    sb.AppendLine($"*{phase.WordCount} kata | ~{phase.DurationSeconds:F0} detik*");
                    sb.AppendLine();
                }

                sb.AppendLine(content);
                sb.AppendLine();
            }
        }

        if (!clean)
        {
            var totalWords = session.Phases.Sum(p => p.WordCount ?? 0);
            var totalSeconds = session.Phases.Sum(p => p.DurationSeconds ?? 0);
            sb.AppendLine("---");
            sb.AppendLine($"**Total: {totalWords} kata | ~{totalSeconds / 60:F0} menit**");
        }

        // Save to export directory
        var exportDir = _configuration["ScriptOutput:ExportDirectory"] ?? "output/exports";
        Directory.CreateDirectory(exportDir);
        
        var exportPath = Path.Combine(session.OutputDirectory, "COMPLETE_SCRIPT.md");
        await File.WriteAllTextAsync(exportPath, sb.ToString());
        
        _logger.LogInformation("Exported script for session {SessionId} to {Path}", sessionId, exportPath);

        return sb.ToString();
    }

    public async Task<ScriptGenerationSession> RegeneratePhaseAsync(string sessionId, string phaseId)
    {
        await _orchestrator.RegeneratePhaseAsync(sessionId, phaseId);

        var session = await _orchestrator.LoadSessionAsync(sessionId)
            ?? throw new InvalidOperationException($"Session '{sessionId}' not found after regeneration");

        return session;
    }

    public async Task DeleteSessionAsync(string sessionId)
    {
        await _orchestrator.DeleteSessionAsync(sessionId);
    }

    public async Task UpdateSessionAsync(string sessionId, ScriptConfig config)
    {
        var session = await _orchestrator.LoadSessionAsync(sessionId)
            ?? throw new ArgumentException($"Session '{sessionId}' not found");

        _logger.LogInformation("Updating session {SessionId} config: TargetDurationMinutes {OldValue} -> {NewValue}",
            sessionId, session.TargetDurationMinutes, config.TargetDurationMinutes);

        session.Topic = config.Topic;
        session.Outline = config.Outline;
        session.TargetDurationMinutes = config.TargetDurationMinutes;
        session.SourceReferences = config.SourceReferences;
        session.ChannelName = config.ChannelName;
        session.UpdatedAt = DateTime.UtcNow;

        await _orchestrator.SaveSessionAsync(session);

        _logger.LogInformation("Session {SessionId} config updated successfully", sessionId);

        // Immediately export to JSON so the change is persisted
        using var scope = _serviceProvider.CreateScope();
        var syncService = scope.ServiceProvider.GetRequiredService<SessionSyncService>();
        await syncService.ExportSessionAsync(sessionId);
    }

    public async Task ReplaceSessionPhasesAsync(string sessionId, List<ScriptGenerationPhase> newPhases)
    {
        var session = await _orchestrator.LoadSessionAsync(sessionId)
            ?? throw new ArgumentException($"Session '{sessionId}' not found");

        // Simple replace
        session.Phases = newPhases;
        session.UpdatedAt = DateTime.UtcNow;

        await _orchestrator.SaveSessionAsync(session);
    }

    public async Task UpdatePhaseContentAsync(string sessionId, string phaseId, string content)
    {
        var session = await _orchestrator.LoadSessionAsync(sessionId)
            ?? throw new ArgumentException($"Session '{sessionId}' not found");

        var phase = session.Phases.FirstOrDefault(p => p.PhaseId == phaseId)
            ?? throw new ArgumentException($"Phase '{phaseId}' not found in session");

        if (!string.IsNullOrEmpty(phase.ContentFilePath))
        {
            // Ensure directory exists (just in case)
            var dir = Path.GetDirectoryName(phase.ContentFilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            await File.WriteAllTextAsync(phase.ContentFilePath, content, Encoding.UTF8);
        }

        // Update phase metadata
        phase.WordCount = content.Split(new[] { ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
        // Simple estimation: 140 words per minute ~ 2.33 words per second
        phase.DurationSeconds = phase.WordCount.Value / 2.33;
        
        // Update session timestamp
        session.UpdatedAt = DateTime.UtcNow;

        await _orchestrator.SaveSessionAsync(session);
    }

    public async Task ResetSessionPhasesAsync(string sessionId)
    {
        var session = await _orchestrator.LoadSessionAsync(sessionId)
            ?? throw new ArgumentException($"Session '{sessionId}' not found");

        foreach (var phase in session.Phases)
        {
            phase.Status = PhaseStatus.Pending;
            phase.WordCount = null;
            phase.DurationSeconds = null;
            phase.IsValidated = false;
            phase.WarningsJson = null;
            phase.CompletedAt = null;
        }

        session.Status = SessionStatus.Pending;
        session.CompletedAt = null;
        session.UpdatedAt = DateTime.UtcNow;

        await _orchestrator.SaveSessionAsync(session);
    }
}
