using System.Text.Json.Serialization;

namespace SitecoreContentTransfer.Models;

public enum ConnectionType
{
    ClientCredentials,
    CliUserJson
}

public class ConnectionConfig
{
    [JsonPropertyName("type")]
    public ConnectionType Type { get; set; } = ConnectionType.ClientCredentials;

    [JsonPropertyName("clientId")]
    public string ClientId { get; set; } = string.Empty;

    [JsonPropertyName("clientSecret")]
    public string ClientSecret { get; set; } = string.Empty;

    [JsonPropertyName("hostname")]
    public string Hostname { get; set; } = string.Empty;

    [JsonPropertyName("userJsonPath")]
    public string UserJsonPath { get; set; } = string.Empty;

    [JsonPropertyName("selectedEndpoint")]
    public string SelectedEndpoint { get; set; } = string.Empty;

    [JsonPropertyName("authority")]
    public string Authority { get; set; } = "https://auth.sitecorecloud.io";

    [JsonPropertyName("audience")]
    public string Audience { get; set; } = "https://api.sitecorecloud.io";
}
