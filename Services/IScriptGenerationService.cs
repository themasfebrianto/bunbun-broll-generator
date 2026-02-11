using BunbunBroll.Models;

namespace BunbunBroll.Services;

/// <summary>
/// High-level service for script generation UI.
/// Manages sessions, patterns, and export functionality.
/// </summary>
public interface IScriptGenerationService
{
    Task<List<ScriptPattern>> GetAvailablePatternsAsync();
    Task<ScriptGenerationSession> CreateSessionAsync(
        string patternId, string topic, string? outline, int targetDuration,
        string? sourceReferences = null, string channelName = "");
    Task<ScriptGenerationSession> GenerateAsync(string sessionId);
    Task<ScriptGenerationSession?> GetSessionAsync(string sessionId);
    Task<List<ScriptGenerationSession>> ListSessionsAsync();
    Task<string> ExportScriptAsync(string sessionId, bool clean = false);
    Task<ScriptGenerationSession> RegeneratePhaseAsync(string sessionId, string phaseId);
}
