using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SitecoreContentTransfer.Models;

public class TransferConfig
{
    [JsonPropertyName("source")]
    public ConnectionConfig Source { get; set; } = new();

    [JsonPropertyName("target")]
    public ConnectionConfig Target { get; set; } = new();

    [JsonPropertyName("database")]
    public string Database { get; set; } = "master";

    [JsonPropertyName("items")]
    public List<TransferItemPath> Items { get; set; } = [];

    [JsonPropertyName("mergeStrategy")]
    public MergeStrategy MergeStrategy { get; set; } = MergeStrategy.OverrideExistingItem;
}
