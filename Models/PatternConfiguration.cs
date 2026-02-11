using System.Text.Json.Serialization;

namespace BunbunBroll.Models;

/// <summary>
/// Pattern configuration loaded from JSON file.
/// Defines phases, rules, and guidance for script generation.
/// </summary>
public class PatternConfiguration
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("phases")]
    public List<PhaseDefinition> Phases { get; set; } = new();

    [JsonPropertyName("globalRules")]
    public GlobalRules GlobalRules { get; set; } = new();

    [JsonPropertyName("closingFormula")]
    public string ClosingFormula { get; set; } = string.Empty;

    [JsonPropertyName("productionChecklist")]
    public ProductionChecklist ProductionChecklist { get; set; } = new();

    /// <summary>
    /// Get phases ordered by their sequence number.
    /// </summary>
    public IEnumerable<PhaseDefinition> GetOrderedPhases()
        => Phases.OrderBy(p => p.Order);
}
