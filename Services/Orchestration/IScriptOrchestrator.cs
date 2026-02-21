using BunbunBroll.Models;
using BunbunBroll.Services.Orchestration.Events;

namespace BunbunBroll.Services.Orchestration;

/// <summary>
/// Main coordinator for pattern-based script generation.
/// Manages sessions, patterns, and coordinates generation phases.
/// </summary>
public interface IScriptOrchestrator
{
    // Pattern management
    IEnumerable<string> ListPatterns();
    PatternConfiguration? GetPattern(string patternId);

    // Session lifecycle
    Task<(ScriptGenerationSession Session, GenerationContext Context)> InitializeSessionAsync(
        ScriptConfig config, string patternId, string? customId = null);
    Task<ScriptGenerationSession?> LoadSessionAsync(string sessionId);
    Task SaveSessionAsync(ScriptGenerationSession session);

    // Generation
    Task<PatternResult> GenerateAllAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<PatternResult> ResumeAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<GeneratedPhase> RegeneratePhaseAsync(string sessionId, string phaseId, CancellationToken cancellationToken = default);
    Task DeleteSessionAsync(string sessionId);

    // Progress events
    event EventHandler<PhaseProgressEventArgs>? OnPhaseProgress;
    event EventHandler<SessionProgressEventArgs>? OnSessionProgress;
}
