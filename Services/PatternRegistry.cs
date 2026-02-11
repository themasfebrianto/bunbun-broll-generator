using System.Collections.Concurrent;
using System.Text.Json;
using BunbunBroll.Models;

namespace BunbunBroll.Services;

/// <summary>
/// Singleton pattern registry that loads configurations from JSON files.
/// </summary>
public class PatternRegistry : IPatternRegistry
{
    private readonly ConcurrentDictionary<string, PatternConfiguration> _patterns = new();
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public void Register(string id, PatternConfiguration config)
    {
        _patterns[id] = config;
    }

    public PatternConfiguration? Get(string id)
    {
        return _patterns.TryGetValue(id, out var config) ? config : null;
    }

    public IEnumerable<string> ListPatterns()
    {
        return _patterns.Keys;
    }

    public bool Exists(string id)
    {
        return _patterns.ContainsKey(id);
    }

    public void LoadFromDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            Console.WriteLine($"Patterns directory not found: {directory}");
            return;
        }

        var jsonFiles = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly);
        foreach (var file in jsonFiles)
        {
            try
            {
                var json = File.ReadAllText(file);
                var config = JsonSerializer.Deserialize<PatternConfiguration>(json, _jsonOptions);
                if (config != null && !string.IsNullOrEmpty(config.Name))
                {
                    _patterns[config.Name] = config;
                    Console.WriteLine($"Loaded pattern: {config.Name} ({config.Phases.Count} phases) from {Path.GetFileName(file)}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load pattern from {file}: {ex.Message}");
            }
        }

        Console.WriteLine($"Pattern registry loaded {_patterns.Count} patterns from {directory}");
    }
}
