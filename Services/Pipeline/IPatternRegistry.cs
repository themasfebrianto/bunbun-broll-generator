using BunbunBroll.Models;

namespace BunbunBroll.Services;

/// <summary>
/// Pattern discovery and management service.
/// Loads pattern configurations from JSON files.
/// Supports hot-reloading when files change.
/// </summary>
public interface IPatternRegistry
{
    void Register(string id, PatternConfiguration config);
    PatternConfiguration? Get(string id);
    IEnumerable<string> ListPatterns();
    bool Exists(string id);
    void LoadFromDirectory(string directory);
    
    /// <summary>
    /// Force reload of all patterns from the directory.
    /// </summary>
    void ReloadAll();
}
