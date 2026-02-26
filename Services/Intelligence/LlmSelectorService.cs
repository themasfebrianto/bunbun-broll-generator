using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;

namespace BunbunBroll.Services;

public interface ILlmSelectorService
{
    /// <summary>
    /// Event fired when the selected model changes.
    /// </summary>
    event Action<string>? OnModelChanged;

    /// <summary>
    /// Gets the currently selected model ID.
    /// </summary>
    string CurrentModel { get; }

    /// <summary>
    /// Gets a list of available models from configuration.
    /// </summary>
    IEnumerable<string> AvailableModels { get; }

    /// <summary>
    /// Sets the current model, triggering OnModelChanged.
    /// </summary>
    void SelectModel(string modelId);
}

public class LlmSelectorService : ILlmSelectorService
{
    private string _currentModel;
    private readonly List<string> _availableModels = new();

    public event Action<string>? OnModelChanged;

    public string CurrentModel => _currentModel;

    public IEnumerable<string> AvailableModels => _availableModels;

    public LlmSelectorService(IOptions<GeminiSettings> settings)
    {
        var cfg = settings.Value;

        // Combine unique models from both pools
        if (cfg.HighReasoningModels != null)
        {
            _availableModels.AddRange(cfg.HighReasoningModels);
        }
        
        if (cfg.FastModels != null)
        {
            var newModels = cfg.FastModels.Where(m => !_availableModels.Contains(m));
            _availableModels.AddRange(newModels);
        }

        // Fallback if empty
        if (_availableModels.Count == 0)
        {
            _availableModels.Add(cfg.Model);
        }

        // Default to the first high reasoning model, or fallback
        _currentModel = cfg.HighReasoningModels?.FirstOrDefault() ?? cfg.Model;
    }

    public void SelectModel(string modelId)
    {
        if (_currentModel != modelId && !string.IsNullOrWhiteSpace(modelId))
        {
            _currentModel = modelId;
            OnModelChanged?.Invoke(_currentModel);
        }
    }
}
