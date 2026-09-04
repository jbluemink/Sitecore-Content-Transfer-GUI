using System.Text.Json.Serialization;

namespace SitecoreContentTransfer.Models;

public class TransferPreset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("config")]
    public TransferConfig Config { get; set; } = new();
}
