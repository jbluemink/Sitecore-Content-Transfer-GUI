using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SitecoreContentTransfer.Models;

public class AppConfig
{
    [JsonPropertyName("lastUsed")]
    public TransferConfig? LastUsed { get; set; }

    [JsonPropertyName("presets")]
    public List<TransferPreset> Presets { get; set; } = [];

    [JsonPropertyName("debugLoggingEnabled")]
    public bool DebugLoggingEnabled { get; set; }
}
