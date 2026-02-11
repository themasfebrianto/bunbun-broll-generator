using System.Text.Json.Serialization;

namespace BunbunBroll.Models;

public class DurationTarget
{
    [JsonPropertyName("min")]
    public int Min { get; set; }

    [JsonPropertyName("max")]
    public int Max { get; set; }
}
