using System.Collections.Concurrent;
using System.Text.Json;
using BunbunBroll.Models;

namespace BunbunBroll.Services;

/// <summary>
/// Singleton pattern registry that loads configurations from JSON files.
/// Supports hot-reloading of patterns.
/// </summary>
public class PatternRegistry : IPatternRegistry
{
    private readonly ConcurrentDictionary<string, PatternConfiguration> _patterns = new();
    private readonly Dictionary<string, DateTime> _fileLastWriteTimes = new();
    private string? _patternsDirectory;
    private DateTime _lastReloadCheck = DateTime.MinValue;
    private readonly TimeSpan _reloadCheckInterval = TimeSpan.FromSeconds(2);
    private readonly PatternConfigValidator _validator = new();
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public void Register(string id, PatternConfiguration config)
    {
        _patterns[id] = config;
    }

    public PatternConfiguration? Get(string id)
    {
        // Try to reload patterns if directory is set and files have changed
        TryReloadIfChanged();
        
        return _patterns.TryGetValue(id, out var config) ? config : null;
    }

    public IEnumerable<string> ListPatterns()
    {
        TryReloadIfChanged();
        return _patterns.Keys;
    }

    public bool Exists(string id)
    {
        TryReloadIfChanged();
        return _patterns.ContainsKey(id);
    }

    public void LoadFromDirectory(string directory)
    {
        _patternsDirectory = directory;
        
        if (!Directory.Exists(directory))
        {
            Console.WriteLine($"Patterns directory not found: {directory}");
            return;
        }

        var jsonFiles = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly);
        foreach (var file in jsonFiles)
        {
            LoadPatternFromFile(file);
        }

        Console.WriteLine($"Pattern registry loaded {_patterns.Count} patterns from {directory}");
    }

    private void LoadPatternFromFile(string filePath)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            var config = JsonSerializer.Deserialize<PatternConfiguration>(json, _jsonOptions);
            if (config != null && !string.IsNullOrEmpty(config.Name))
            {
                // Validate before storing
                var validation = _validator.Validate(config);
                if (!validation.IsValid)
                {
                    Console.WriteLine($"❌ Pattern validation FAILED for {Path.GetFileName(filePath)}:");
                    Console.WriteLine(validation.GetSummary());
                    return; // Don't load invalid patterns
                }

                // Show warnings if any
                if (validation.Warnings.Count > 0)
                {
                    Console.WriteLine($"⚠️ Pattern warnings for {Path.GetFileName(filePath)}:");
                    Console.WriteLine(validation.GetSummary());
                }

                // Resolve rule templates before storing
                config.ResolveTemplates();
                _patterns[config.Name] = config;
                _fileLastWriteTimes[filePath] = File.GetLastWriteTime(filePath);
                Console.WriteLine($"✓ Loaded pattern: {config.Name} ({config.Phases.Count} phases, {config.RuleTemplates.Count} templates) from {Path.GetFileName(filePath)}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load pattern from {filePath}: {ex.Message}");
        }
    }

    private void TryReloadIfChanged()
    {
        // Debounce: Skip checking if we checked too recently
        var now = DateTime.UtcNow;
        if (now - _lastReloadCheck < _reloadCheckInterval)
            return;

        _lastReloadCheck = now;

        if (string.IsNullOrEmpty(_patternsDirectory) || !Directory.Exists(_patternsDirectory))
            return;

        var jsonFiles = Directory.GetFiles(_patternsDirectory, "*.json", SearchOption.TopDirectoryOnly);
        bool anyChanged = false;

        foreach (var file in jsonFiles)
        {
            var lastWrite = File.GetLastWriteTime(file);
            if (!_fileLastWriteTimes.TryGetValue(file, out var cachedWrite) || lastWrite > cachedWrite)
            {
                Console.WriteLine($"Pattern file changed, reloading: {Path.GetFileName(file)}");
                LoadPatternFromFile(file);
                anyChanged = true;
            }
        }

        if (anyChanged)
        {
            Console.WriteLine($"Pattern registry now has {_patterns.Count} patterns");
        }
    }

    /// <summary>
    /// Force reload of all patterns from the directory.
    /// </summary>
    public void ReloadAll()
    {
        if (!string.IsNullOrEmpty(_patternsDirectory))
        {
            Console.WriteLine("Force reloading all patterns...");
            _patterns.Clear();
            _fileLastWriteTimes.Clear();
            LoadFromDirectory(_patternsDirectory);
        }
    }
}
