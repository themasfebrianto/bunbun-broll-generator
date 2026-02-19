using BunbunBroll.Models;

namespace BunbunBroll.Services;

/// <summary>
/// Service for detecting which visual hooking phase a timestamp belongs to.
///
/// Phase boundaries:
/// - Phase 1 (opening-hook): 0-45 seconds - Maximum hook with 2.5s beats
/// - Phase 2 (contextualization): 45-180 seconds - Progressive with 10s beats
/// - Phase 3+ (normal): 180+ seconds - Normal pacing with existing behavior
/// </summary>
public interface IPhaseDetectionService
{
    /// <summary>
    /// Detect which phase a given timestamp belongs to
    /// </summary>
    string DetectPhase(TimeSpan timestamp);

    /// <summary>
    /// Get configuration for a specific phase
    /// </summary>
    PhaseConfig GetPhaseConfig(string phaseId);

    /// <summary>
    /// Get all phase configurations
    /// </summary>
    IReadOnlyList<PhaseConfig> GetAllPhases();
}

public class PhaseDetectionService : IPhaseDetectionService
{
    private readonly List<PhaseConfig> _phases;

    public PhaseDetectionService()
    {
        _phases = new List<PhaseConfig>
        {
            new PhaseConfig
            {
                PhaseId = "opening-hook",
                EndTimeSeconds = 45,
                KenBurnsDuration = 2.5,
                MotionIntensity = MotionIntensity.High,
                SplitFactor = 8  // Split into 8 micro-beats for 2.5s each
            },
            new PhaseConfig
            {
                PhaseId = "contextualization",
                EndTimeSeconds = 180,
                KenBurnsDuration = 10,
                MotionIntensity = MotionIntensity.Medium,
                SplitFactor = 2  // Split into 2 medium beats
            }
        };
    }

    public string DetectPhase(TimeSpan timestamp)
    {
        var totalSeconds = timestamp.TotalSeconds;

        foreach (var phase in _phases)
        {
            if (totalSeconds <= phase.EndTimeSeconds)
                return phase.PhaseId;
        }

        return "normal";
    }

    public PhaseConfig GetPhaseConfig(string phaseId)
    {
        var config = _phases.FirstOrDefault(p => p.PhaseId == phaseId);
        if (config != null)
            return config;

        // Default config for "normal" phase
        return new PhaseConfig
        {
            PhaseId = "normal",
            EndTimeSeconds = double.MaxValue,
            KenBurnsDuration = 20,  // Default from existing system
            MotionIntensity = MotionIntensity.Low,
            SplitFactor = 1  // No splitting
        };
    }

    public IReadOnlyList<PhaseConfig> GetAllPhases()
    {
        return _phases;
    }
}
