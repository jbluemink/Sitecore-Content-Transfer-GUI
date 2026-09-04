using System.Text.Json.Serialization;

namespace SitecoreContentTransfer.Models;

public class TransferItemPath
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("mode")]
    public TransferMode Mode { get; set; } = TransferMode.ItemAndDescendants;
}
