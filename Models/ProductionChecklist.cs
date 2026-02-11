using System.Text.Json.Serialization;

namespace BunbunBroll.Models;

public class ProductionChecklist
{
    [JsonPropertyName("praProduksi")]
    public List<string> PraProduksi { get; set; } = new();

    [JsonPropertyName("penulisan")]
    public List<string> Penulisan { get; set; } = new();
}
