using BunbunBroll.Models;
using BunbunBroll.Orchestration.Events;

namespace BunbunBroll.Orchestration;

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
    Task<PatternResult> GenerateAllAsync(string sessionId);
    Task<PatternResult> ResumeAsync(string sessionId);
    Task<GeneratedPhase> RegeneratePhaseAsync(string sessionId, string phaseId);

    // Progress events
    event EventHandler<PhaseProgressEventArgs>? OnPhaseProgress;
    event EventHandler<SessionProgressEventArgs>? OnSessionProgress;
}
