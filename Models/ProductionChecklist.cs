using System.Text.Json.Serialization;

namespace BunbunBroll.Models;

public class ProductionChecklist
{
    [JsonPropertyName("praProduksi")]
    public List<string> PraProduksi { get; set; } = new();

    [JsonPropertyName("penulisan")]
    public List<string> Penulisan { get; set; } = new();

    [JsonPropertyName("factCheck")]
    public List<string> FactCheck { get; set; } = new();

    [JsonPropertyName("toneCheck")]
    public List<string> ToneCheck { get; set; } = new();
}
