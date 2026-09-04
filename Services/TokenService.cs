using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SitecoreContentTransfer.Models;

namespace SitecoreContentTransfer.Services;

public class TokenService
{
    private readonly HttpClient _httpClient;

    public TokenService()
    {
        _httpClient = new HttpClient();
    }

    public async Task<OAuthTokenResponse?> GetTokenFromClientCredentialsAsync(
        string clientId,
        string clientSecret,
        string authority,
        string audience,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tokenEndpoint = $"{authority.TrimEnd('/')}/oauth/token";

            var requestBody = new Dictionary<string, string>
            {
                { "grant_type", "client_credentials" },
                { "client_id", clientId },
                { "client_secret", clientSecret },
                { "audience", audience }
            };

            var content = new FormUrlEncodedContent(requestBody);

            var response = await _httpClient.PostAsync(tokenEndpoint, content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new HttpRequestException($"Token request failed: {response.StatusCode} - {errorContent}");
            }

            var jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken);
            var tokenResponse = JsonSerializer.Deserialize<OAuthTokenResponse>(jsonResponse);

            return tokenResponse;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to get OAuth token: {ex.Message}", ex);
        }
    }

    public async Task<string?> GetTokenFromConnectionConfigAsync(
        ConnectionConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (config.Type == ConnectionType.ClientCredentials)
        {
            if (string.IsNullOrWhiteSpace(config.ClientId) || string.IsNullOrWhiteSpace(config.ClientSecret))
            {
                throw new InvalidOperationException("ClientId and ClientSecret are required for Client Credentials flow.");
            }

            var tokenResponse = await GetTokenFromClientCredentialsAsync(
                config.ClientId,
                config.ClientSecret,
                config.Authority,
                config.Audience,
                cancellationToken);

            return tokenResponse?.AccessToken;
        }
        else if (config.Type == ConnectionType.CliUserJson)
        {
            if (string.IsNullOrWhiteSpace(config.UserJsonPath))
            {
                throw new InvalidOperationException("UserJsonPath is required for CLI user.json connection.");
            }

            if (!File.Exists(config.UserJsonPath))
            {
                throw new FileNotFoundException($"user.json file not found: {config.UserJsonPath}");
            }

            var envConfig = Login.LoginHelper.GetSitecoreEnvironment(
                config.UserJsonPath,
                config.SelectedEndpoint);

            return envConfig.AccessToken;
        }

        throw new InvalidOperationException($"Unknown connection type: {config.Type}");
    }
}

public class OAuthTokenResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [System.Text.Json.Serialization.JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("token_type")]
    public string TokenType { get; set; } = string.Empty;
}
