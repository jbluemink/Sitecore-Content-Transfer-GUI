using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SitecoreContentTransfer.Login;
using SitecoreContentTransfer.Models;

namespace SitecoreContentTransfer.Services;

public class ConnectionValidator
{
    private readonly TokenService _tokenService;
    private readonly HttpClient _httpClient;

    public ConnectionValidator(TokenService tokenService)
    {
        _tokenService = tokenService;
        _httpClient = new HttpClient();
    }

    public async Task<ConnectionValidationResult> ValidateConnectionAsync(
        ConnectionConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        try
        {
            var context = await ResolveConnectionContextAsync(config, cancellationToken);
            if (context.Result != null)
            {
                return context.Result;
            }

            var graphQlEndpoint = $"{context.Hostname!.TrimEnd('/')}/sitecore/api/authoring/graphql/v1";

            var introspectionQuery = new
            {
                query = "{ __schema { queryType { name } } }"
            };

            var request = new HttpRequestMessage(HttpMethod.Post, graphQlEndpoint)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(introspectionQuery),
                    Encoding.UTF8,
                    "application/json")
            };

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);

            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return new ConnectionValidationResult
                {
                    IsValid = true,
                    Hostname = context.Hostname,
                    AccessToken = context.AccessToken,
                    Message = $"Connection successful to {context.Hostname}"
                };
            }

            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            return new ConnectionValidationResult
            {
                IsValid = false,
                ErrorMessage = $"GraphQL endpoint validation failed: {response.StatusCode} - {errorContent}"
            };
        }
        catch (Exception ex)
        {
            return new ConnectionValidationResult
            {
                IsValid = false,
                ErrorMessage = $"Connection validation error: {ex.Message}"
            };
        }
    }

    public async Task<ConnectionValidationResult> ValidateTransferAccessAsync(
        ConnectionConfig config,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        try
        {
            var context = await ResolveConnectionContextAsync(config, cancellationToken);
            if (context.Result != null)
            {
                return context.Result;
            }

            var probeTransferId = Guid.NewGuid();
            var statusEndpoint = $"{context.Hostname!.TrimEnd('/')}/sitecore/api/content/transfer/v1/transfers/{probeTransferId:D}/status";

            var request = new HttpRequestMessage(HttpMethod.Get, statusEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new ConnectionValidationResult
                {
                    IsValid = true,
                    Hostname = context.Hostname,
                    AccessToken = context.AccessToken,
                    Message = "Transfer API access OK (endpoint reachable, probe transfer not found as expected)."
                };
            }

            if (response.IsSuccessStatusCode)
            {
                return new ConnectionValidationResult
                {
                    IsValid = true,
                    Hostname = context.Hostname,
                    AccessToken = context.AccessToken,
                    Message = "Transfer API access OK."
                };
            }

            return new ConnectionValidationResult
            {
                IsValid = false,
                Hostname = context.Hostname,
                AccessToken = context.AccessToken,
                ErrorMessage = $"Transfer API access failed: {(int)response.StatusCode} ({response.ReasonPhrase}) - {body}"
            };
        }
        catch (Exception ex)
        {
            return new ConnectionValidationResult
            {
                IsValid = false,
                ErrorMessage = $"Transfer access validation error: {ex.Message}"
            };
        }
    }

    private async Task<(string? AccessToken, string? Hostname, ConnectionValidationResult? Result)> ResolveConnectionContextAsync(
        ConnectionConfig config,
        CancellationToken cancellationToken)
    {
        var accessToken = await _tokenService.GetTokenFromConnectionConfigAsync(config, cancellationToken);

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return (null, null, new ConnectionValidationResult
            {
                IsValid = false,
                ErrorMessage = "Failed to obtain access token."
            });
        }

        var hostname = config.Hostname;
        if (string.IsNullOrWhiteSpace(hostname))
        {
            hostname = JwtTokenHelper.GetInstanceUrlFromJwt(accessToken);
        }

        if (string.IsNullOrWhiteSpace(hostname))
        {
            return (accessToken, null, new ConnectionValidationResult
            {
                IsValid = false,
                ErrorMessage = "Could not determine hostname. Please enter hostname manually."
            });
        }

        if (!hostname.EndsWith('/'))
        {
            hostname += "/";
        }

        return (accessToken, hostname, null);
    }
}

public class ConnectionValidationResult
{
    public bool IsValid { get; set; }
    public string? Hostname { get; set; }
    public string? AccessToken { get; set; }
    public string? Message { get; set; }
    public string? ErrorMessage { get; set; }
}
