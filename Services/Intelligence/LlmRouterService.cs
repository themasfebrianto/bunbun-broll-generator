using System;
using System.Collections.Concurrent;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using BunbunBroll.Models; // For GeminiSettings

namespace BunbunBroll.Services;

public interface ILlmRouterService
{
    /// <summary>
    /// Event fired when the model is switched due to rotation or failure.
    /// Provides a user-friendly message, e.g., "Switched to claude-sonnet-4-6".
    /// </summary>
    event Action<string>? OnModelSwitched;

    /// <summary>
    /// Gets the next available model in the specified pool.
    /// </summary>
    /// <param name="requiresHighReasoning">If true, draws from the High Reasoning pool; otherwise, the Fast pool.</param>
    /// <returns>The model ID.</returns>
    string GetModel(bool requiresHighReasoning);

    /// <summary>
    /// Reports that a model failed (e.g., rate limit or token limit exhausted).
    /// Places the model on a temporary cooldown and triggers a rotation.
    /// </summary>
    /// <param name="modelId">The model that failed.</param>
    /// <param name="reason">The reason for failure (used for UI notification).</param>
    void ReportFailure(string modelId, string reason = "Token limit exhausted");
}

public class LlmRouterService : ILlmRouterService
{
    private readonly GeminiSettings _settings;
    private readonly ILogger<LlmRouterService> _logger;

    private readonly object _highLock = new();
    private int _highIndex = 0;

    private readonly object _fastLock = new();
    private int _fastIndex = 0;

    // ModelId -> Cooldown Expiration Time
    private readonly ConcurrentDictionary<string, DateTime> _cooldowns = new();
    
    // Cooldown duration when a model hits a limit
    private readonly TimeSpan _cooldownDuration = TimeSpan.FromMinutes(2);

    public event Action<string>? OnModelSwitched;

    public LlmRouterService(IOptions<GeminiSettings> settings, ILogger<LlmRouterService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    public string GetModel(bool requiresHighReasoning)
    {
        var pool = requiresHighReasoning ? _settings.HighReasoningModels : _settings.FastModels;
        
        // Fallback to default model if pool is empty or missing
        if (pool == null || pool.Length == 0)
        {
            return _settings.Model;
        }

        lock (requiresHighReasoning ? _highLock : _fastLock)
        {
            ref int currentIndex = ref (requiresHighReasoning ? ref _highIndex : ref _fastIndex);

            // Find the next available model that isn't on cooldown
            for (int i = 0; i < pool.Length; i++)
            {
                var candidate = pool[currentIndex];
                
                // Always advance for the NEXT call to ensure round-robin rotation
                currentIndex = (currentIndex + 1) % pool.Length;
                
                if (IsOnCooldown(candidate))
                {
                    continue;
                }

                return candidate;
            }

            // Fallback: If ALL models are on cooldown, just use the first one and hope for the best,
            // or return the primary fallback.
            _logger.LogWarning($"All models in {(requiresHighReasoning ? "High" : "Fast")} pool are on cooldown. Using fallback.");
            return pool[0]; // Force return the first one, breaking cooldown rules
        }
    }

    public void ReportFailure(string modelId, string reason = "Rate limit hit")
    {
        // Put model on cooldown
        var expiration = DateTime.UtcNow.Add(_cooldownDuration);
        _cooldowns[modelId] = expiration;

        _logger.LogWarning($"LLM Router: {modelId} placed on cooldown until {expiration.ToLocalTime()} due to: {reason}");

        // Advance indices if the current index was pointing to the failed model
        AdvanceIfMatches(_settings.HighReasoningModels, ref _highIndex, _highLock, modelId);
        AdvanceIfMatches(_settings.FastModels, ref _fastIndex, _fastLock, modelId);

        // Notify UI subscribers
        OnModelSwitched?.Invoke($"Rotated model from {modelId} due to: {reason}");
    }

    private bool IsOnCooldown(string modelId)
    {
        if (_cooldowns.TryGetValue(modelId, out var expiration))
        {
            if (DateTime.UtcNow < expiration)
            {
                return true;
            }
            // Cooldown expired, clean it up
            _cooldowns.TryRemove(modelId, out _);
        }
        return false;
    }

    private void AdvanceIfMatches(string[] pool, ref int index, object lockObj, string failedModelId)
    {
        if (pool == null || pool.Length == 0) return;

        lock (lockObj)
        {
            if (pool[index] == failedModelId)
            {
                index = (index + 1) % pool.Length;
            }
        }
    }
}
