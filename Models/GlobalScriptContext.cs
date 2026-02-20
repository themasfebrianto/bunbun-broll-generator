namespace BunbunBroll.Models;

/// <summary>
/// Global storytelling context extracted from full script analysis.
/// Used to maintain visual consistency across all generated image prompts.
/// </summary>
public class GlobalScriptContext
{
    public List<string> PrimaryLocations { get; set; } = new();
    public List<StoryCharacter> IdentifiedCharacters { get; set; } = new();
    public List<EraTransition> EraTimeline { get; set; } = new();
    public List<MoodBeat> MoodBeats { get; set; } = new();
    public List<string> RecurringVisuals { get; set; } = new();
    public string ColorProgression { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public DateTime ExtractedAt { get; set; } = DateTime.UtcNow;

    public MoodBeat? GetMoodBeatForSegment(int segmentIndex)
    {
        return MoodBeats.LastOrDefault(m => segmentIndex >= m.StartSegment);
    }

    public EraTransition? GetEraForSegment(int segmentIndex)
    {
        return EraTimeline.LastOrDefault(e => segmentIndex >= e.StartSegment);
    }
}

public class ScriptSegmentRef
{
    public int Index { get; set; }
    public string Timestamp { get; set; } = string.Empty;
    public string ScriptText { get; set; } = string.Empty;
}

public class StoryCharacter
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? FirstAppearanceSegment { get; set; }
}

public class EraTransition
{
    public int StartSegment { get; set; }
    public int? EndSegment { get; set; }
    public string Era { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class MoodBeat
{
    public int StartSegment { get; set; }
    public int? EndSegment { get; set; }
    public string Mood { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> VisualKeywords { get; set; } = new();

    // Auto-detected visual settings (used when user selects "Auto")
    public ImageLighting? SuggestedLighting { get; set; }
    public ImageColorPalette? SuggestedPalette { get; set; }
    public ImageComposition? SuggestedAngle { get; set; }
    public string? VisualRationale { get; set; }
}

// Response DTOs for JSON parsing
public class GlobalContextExtractionResponse
{
    public List<string> PrimaryLocations { get; set; } = new();
    public List<StoryCharacterResponse> IdentifiedCharacters { get; set; } = new();
    public List<EraTransitionResponse> EraTimeline { get; set; } = new();
    public List<MoodBeatResponse> MoodBeats { get; set; } = new();
    public List<string> RecurringVisuals { get; set; } = new();
    public string ColorProgression { get; set; } = string.Empty;

    public GlobalScriptContext ToGlobalScriptContext(string topic)
    {
        return new GlobalScriptContext
        {
            PrimaryLocations = PrimaryLocations,
            IdentifiedCharacters = IdentifiedCharacters.Select(c => new StoryCharacter
            {
                Name = c.Name,
                Description = c.Description,
                FirstAppearanceSegment = c.FirstAppearanceSegment?.ToString()
            }).ToList(),
            EraTimeline = EraTimeline.Select(e => new EraTransition
            {
                StartSegment = e.StartSegment,
                EndSegment = e.EndSegment,
                Era = e.Era,
                Description = e.Description
            }).ToList(),
            MoodBeats = MoodBeats.Select(m => new MoodBeat
            {
                StartSegment = m.StartSegment,
                EndSegment = m.EndSegment,
                Mood = m.Mood,
                Description = m.Description,
                VisualKeywords = m.VisualKeywords,
                SuggestedLighting = ParseEnum<ImageLighting>(m.SuggestedLighting),
                SuggestedPalette = ParseEnum<ImageColorPalette>(m.SuggestedPalette),
                SuggestedAngle = ParseEnum<ImageComposition>(m.SuggestedAngle),
                VisualRationale = m.VisualRationale
            }).ToList(),
            RecurringVisuals = RecurringVisuals,
            ColorProgression = ColorProgression,
            Topic = topic
        };
    }

    private static T? ParseEnum<T>(string? value) where T : struct
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (Enum.TryParse<T>(value, true, out var result)) return result;
        return null;
    }
}

public class StoryCharacterResponse
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? FirstAppearanceSegment { get; set; }
}

public class EraTransitionResponse
{
    public int StartSegment { get; set; }
    public int? EndSegment { get; set; }
    public string Era { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class MoodBeatResponse
{
    public int StartSegment { get; set; }
    public int? EndSegment { get; set; }
    public string Mood { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> VisualKeywords { get; set; } = new();
    public string? SuggestedLighting { get; set; }
    public string? SuggestedPalette { get; set; }
    public string? SuggestedAngle { get; set; }
    public string? VisualRationale { get; set; }
}
