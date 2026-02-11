using System.Text.Json.Serialization;

namespace BunbunBroll.Models;

public class WordCountTarget
{
    [JsonPropertyName("min")]
    public int Min { get; set; }

    [JsonPropertyName("max")]
    public int Max { get; set; }
}
