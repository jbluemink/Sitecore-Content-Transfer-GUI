using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SitecoreContentTransfer.Models;

namespace SitecoreContentTransfer.Services;

public class ContentTransferApiClient
{
    private readonly HttpClient _httpClient;
    private readonly TokenService _tokenService;

    public ContentTransferApiClient(TokenService tokenService)
    {
        _tokenService = tokenService;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10) // Content transfer can take a while
        };
    }

    public async Task<TransferResult> StartTransferAsync(
        TransferConfig config,
        IProgress<string>? progress = null,
        bool enableDebugLogging = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(config);

        try
        {
            progress?.Report("Getting source access token...");
            var sourceToken = await _tokenService.GetTokenFromConnectionConfigAsync(config.Source, cancellationToken);

            if (string.IsNullOrWhiteSpace(sourceToken))
            {
                return new TransferResult
                {
                    Success = false,
                    ErrorMessage = "Failed to get source access token."
                };
            }

            progress?.Report("Getting target access token...");
            var targetToken = await _tokenService.GetTokenFromConnectionConfigAsync(config.Target, cancellationToken);

            if (string.IsNullOrWhiteSpace(targetToken))
            {
                return new TransferResult
                {
                    Success = false,
                    ErrorMessage = "Failed to get target access token."
                };
            }

            // Determine hostnames
            var sourceHostname = !string.IsNullOrWhiteSpace(config.Source.Hostname)
                ? config.Source.Hostname
                : Login.JwtTokenHelper.GetInstanceUrlFromJwt(sourceToken);

            var targetHostname = !string.IsNullOrWhiteSpace(config.Target.Hostname)
                ? config.Target.Hostname
                : (!string.IsNullOrWhiteSpace(targetToken)
                    ? Login.JwtTokenHelper.GetInstanceUrlFromJwt(targetToken)
                    : string.Empty);

            progress?.Report($"Source: {sourceHostname}");
            progress?.Report($"Target: {targetHostname}");

            var transferId = Guid.NewGuid().ToString("D");

            // Build transfer request for Sitecore Content Transfer API
            var transferRequest = new
            {
                Configuration = new
                {
                    Database = config.Database,
                    DataTrees = config.Items.Select(item => new
                    {
                        ItemPath = item.Path,
                        Scope = item.Mode.ToString(),
                        MergeStrategy = config.MergeStrategy.ToString()
                    }).ToArray()
                },
                TransferId = transferId
            };

            progress?.Report("Starting content transfer...");

            // POST to content transfer API
            var transferEndpoint = $"{sourceHostname?.TrimEnd('/')}/sitecore/api/content/transfer/v1/transfers";

            if (enableDebugLogging)
            {
                var requestPreview = new
                {
                    Configuration = new
                    {
                        Database = config.Database,
                        DataTrees = config.Items.Select(item => new
                        {
                            ItemPath = item.Path,
                            Scope = item.Mode.ToString(),
                            MergeStrategy = config.MergeStrategy.ToString()
                        }).ToArray()
                    },
                    TransferId = transferId,
                    SourceEndpoint = sourceHostname?.TrimEnd('/'),
                    TargetEndpoint = targetHostname?.TrimEnd('/'),
                    SourceAccessToken = "***",
                    TargetAccessToken = "***"
                };

                progress?.Report($"[DEBUG] Transfer endpoint: {transferEndpoint}");
                progress?.Report($"[DEBUG] Source token: {MaskToken(sourceToken)}");
                progress?.Report($"[DEBUG] Target token: {MaskToken(targetToken)}");
                progress?.Report($"[DEBUG] Request payload: {JsonSerializer.Serialize(requestPreview)}");
            }

            var request = new HttpRequestMessage(HttpMethod.Post, transferEndpoint)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(transferRequest),
                    Encoding.UTF8,
                    "application/json")
            };

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sourceToken);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                progress?.Report($"Transfer failed: {response.StatusCode}");

                if (enableDebugLogging)
                {
                    var authHeader = response.Headers.WwwAuthenticate.FirstOrDefault()?.ToString() ?? "<none>";
                    progress?.Report($"[DEBUG] Response reason: {response.ReasonPhrase}");
                    progress?.Report($"[DEBUG] WWW-Authenticate: {authHeader}");
                    progress?.Report($"[DEBUG] Response body: {SafeSnippet(responseContent)}");
                }

                return new TransferResult
                {
                    Success = false,
                    ErrorMessage = $"Transfer request failed: {response.StatusCode} - {responseContent}"
                };
            }

            progress?.Report($"Transfer response: {responseContent}");
            progress?.Report($"Transfer started with ID: {transferId}");

            var finalStatus = await PollTransferStatusAsync(
                sourceHostname,
                sourceToken,
                transferId,
                progress,
                enableDebugLogging,
                cancellationToken);

            if (!string.Equals(finalStatus.State, "Completed", StringComparison.OrdinalIgnoreCase))
            {
                return new TransferResult
                {
                    Success = false,
                    TransferId = transferId,
                    ErrorMessage = $"Transfer failed: {finalStatus.Message ?? finalStatus.State}"
                };
            }

            if (finalStatus.ChunkSetsMetadata.Count == 0)
            {
                return new TransferResult
                {
                    Success = false,
                    TransferId = transferId,
                    ErrorMessage = "Transfer completed without chunk metadata."
                };
            }

            var importedSources = new List<string>();
            foreach (var chunkSet in finalStatus.ChunkSetsMetadata)
            {
                progress?.Report($"Processing chunk set {chunkSet.ChunkSetId} ({chunkSet.ChunkCount} chunk(s))...");

                for (var chunkId = 0; chunkId < chunkSet.ChunkCount; chunkId++)
                {
                    var chunk = await GetChunkAsync(
                        sourceHostname,
                        sourceToken,
                        transferId,
                        chunkSet.ChunkSetId,
                        chunkId,
                        cancellationToken);

                    await SaveChunkAsync(
                        targetHostname,
                        targetToken,
                        transferId,
                        chunkSet.ChunkSetId,
                        chunkId,
                        chunk,
                        cancellationToken);
                }

                var completion = await CompleteChunkSetAsync(
                    targetHostname,
                    targetToken,
                    transferId,
                    chunkSet.ChunkSetId,
                    cancellationToken);

                if (string.IsNullOrWhiteSpace(completion.ContentTransferFileName))
                {
                    return new TransferResult
                    {
                        Success = false,
                        TransferId = transferId,
                        ErrorMessage = $"Chunk set '{chunkSet.ChunkSetId}' completed without ContentTransferFileName."
                    };
                }

                importedSources.Add(completion.ContentTransferFileName);
            }

            foreach (var sourceName in importedSources)
            {
                progress?.Report($"Waiting for destination blob source '{sourceName}'...");
                await WaitForBlobUploadedAsync(targetHostname, targetToken, sourceName, progress, enableDebugLogging, cancellationToken);

                progress?.Report($"Starting destination item transfer for '{sourceName}'...");
                var startedSourceName = await StartDestinationItemTransferAsync(
                    targetHostname,
                    targetToken,
                    config.Database,
                    sourceName,
                    cancellationToken);

                progress?.Report($"Waiting for destination item transfer completion for '{startedSourceName}'...");
                await WaitForItemTransferFinishedAsync(
                    targetHostname,
                    targetToken,
                    startedSourceName,
                    progress,
                    enableDebugLogging,
                    cancellationToken);
            }

            return new TransferResult
            {
                Success = true,
                TransferId = transferId,
                Message = "Transfer completed successfully and imported into destination."
            };
        }
        catch (Exception ex)
        {
            progress?.Report($"Error: {ex.Message}");
            if (enableDebugLogging)
            {
                progress?.Report($"[DEBUG] Exception details: {ex}");
            }

            return new TransferResult
            {
                Success = false,
                ErrorMessage = $"Transfer error: {ex.Message}"
            };
        }
    }

    private async Task<ContentTransferStatusSnapshot> PollTransferStatusAsync(
        string? hostname,
        string token,
        string transferId,
        IProgress<string>? progress,
        bool enableDebugLogging,
        CancellationToken cancellationToken)
    {
        var statusEndpoint = $"{hostname?.TrimEnd('/')}/sitecore/api/content/transfer/v1/transfers/{transferId}/status";
        var maxAttempts = 60; // Poll for up to 10 minutes (60 * 10 seconds)
        var pollInterval = TimeSpan.FromSeconds(10);

        if (enableDebugLogging)
        {
            progress?.Report($"[DEBUG] Status endpoint: {statusEndpoint}");
        }

        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                await Task.Delay(pollInterval, cancellationToken);

                var request = new HttpRequestMessage(HttpMethod.Get, statusEndpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var response = await _httpClient.SendAsync(request, cancellationToken);
                var statusContent = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    if (enableDebugLogging)
                    {
                        progress?.Report($"[DEBUG] Status polling failed: {(int)response.StatusCode} {response.ReasonPhrase}");
                        progress?.Report($"[DEBUG] Status response body: {SafeSnippet(statusContent)}");
                    }

                    continue;
                }

                var snapshot = ParseContentTransferStatusSnapshot(statusContent);

                if (enableDebugLogging)
                {
                    progress?.Report($"[DEBUG] Status response body: {SafeSnippet(statusContent)}");
                }

                progress?.Report($"Status: {snapshot.State} - {snapshot.Message}");

                if (string.Equals(snapshot.State, "Completed", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(snapshot.State, "Failed", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(snapshot.State, "NotFound", StringComparison.OrdinalIgnoreCase))
                {
                    return snapshot;
                }
            }
            catch (Exception ex)
            {
                progress?.Report($"Status check error: {ex.Message}");
                if (enableDebugLogging)
                {
                    progress?.Report($"[DEBUG] Status check exception: {ex}");
                }
            }
        }

        return new ContentTransferStatusSnapshot
        {
            State = "TimedOut",
            Message = "Transfer status polling timed out."
        };
    }

    private async Task<ContentTransferChunkSnapshot> GetChunkAsync(
        string? sourceHostname,
        string sourceToken,
        string transferId,
        Guid chunkSetId,
        int chunkId,
        CancellationToken cancellationToken)
    {
        var endpoint = $"{sourceHostname?.TrimEnd('/')}/sitecore/api/content/transfer/v1/transfers/{transferId}/chunksets/{chunkSetId:D}/chunks/{chunkId}";
        var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sourceToken);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var data = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = Encoding.UTF8.GetString(data);
            throw new InvalidOperationException($"Get chunk failed: {(int)response.StatusCode} ({response.ReasonPhrase}) - {SafeSnippet(body)}");
        }

        var isMedia = TryGetContentDispositionParameter(response.Content.Headers.ContentDisposition, "IsMedia", out var isMediaValue)
            && bool.TryParse(isMediaValue, out var parsed)
            && parsed;

        return new ContentTransferChunkSnapshot
        {
            Data = data,
            IsMedia = isMedia
        };
    }

    private async Task SaveChunkAsync(
        string? targetHostname,
        string targetToken,
        string transferId,
        Guid chunkSetId,
        int chunkId,
        ContentTransferChunkSnapshot chunk,
        CancellationToken cancellationToken)
    {
        var endpoint = $"{targetHostname?.TrimEnd('/')}/sitecore/api/content/transfer/v1/transfers/{transferId}/chunksets/{chunkSetId:D}/chunks/{chunkId}?isMedia={chunk.IsMedia.ToString().ToLowerInvariant()}";
        var request = new HttpRequestMessage(HttpMethod.Put, endpoint)
        {
            Content = new ByteArrayContent(chunk.Data)
        };

        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", targetToken);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = $"Save chunk failed: {(int)response.StatusCode} ({response.ReasonPhrase}) - {SafeSnippet(body)}";
            if (body.Contains("Azure Blob Storage container name is invalid", StringComparison.OrdinalIgnoreCase))
            {
                detail += " | Destination CM storage configuration is invalid. Verify the Content Transfer blob container name in local Sitecore config (lowercase, 3-63 chars, letters/numbers/hyphen, no leading or trailing hyphen).";
            }

            throw new InvalidOperationException(detail);
        }
    }

    private async Task<DestinationChunkCompletionResponse> CompleteChunkSetAsync(
        string? targetHostname,
        string targetToken,
        string transferId,
        Guid chunkSetId,
        CancellationToken cancellationToken)
    {
        var endpoint = $"{targetHostname?.TrimEnd('/')}/sitecore/api/content/transfer/v1/transfers/{transferId}/chunksets/{chunkSetId:D}/complete";
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", targetToken);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Complete chunk set failed: {(int)response.StatusCode} ({response.ReasonPhrase}) - {SafeSnippet(body)}");
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return new DestinationChunkCompletionResponse();
        }

        return JsonSerializer.Deserialize<DestinationChunkCompletionResponse>(body) ?? new DestinationChunkCompletionResponse();
    }

    private async Task WaitForBlobUploadedAsync(
        string? targetHostname,
        string targetToken,
        string blobName,
        IProgress<string>? progress,
        bool enableDebugLogging,
        CancellationToken cancellationToken)
    {
        var endpoint = $"{targetHostname?.TrimEnd('/')}/sitecore/shell/api/v3/ItemsTransfer/sources/blobs/{Uri.EscapeDataString(blobName)}";

        for (var attempt = 1; attempt <= 120; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);

            var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", targetToken);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                if (enableDebugLogging)
                {
                    progress?.Report($"[DEBUG] Blob state poll failed: {(int)response.StatusCode} ({response.ReasonPhrase}) - {SafeSnippet(body)}");
                }

                continue;
            }

            var blobState = ParseBlobDetails(body);
            progress?.Report($"Blob state: {blobState.BlobState}");

            if (string.Equals(blobState.BlobState, "Uploaded", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (string.Equals(blobState.BlobState, "Error", StringComparison.OrdinalIgnoreCase)
                || string.Equals(blobState.BlobState, "TransferredWithErrors", StringComparison.OrdinalIgnoreCase)
                || string.Equals(blobState.BlobState, "Discarded", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Blob source '{blobName}' ended in state '{blobState.BlobState}'. Error: {blobState.Error}");
            }
        }

        throw new TimeoutException($"Blob source '{blobName}' did not become Uploaded in time.");
    }

    private async Task<string> StartDestinationItemTransferAsync(
        string? targetHostname,
        string targetToken,
        string database,
        string blobName,
        CancellationToken cancellationToken)
    {
        var endpoint = $"{targetHostname?.TrimEnd('/')}/sitecore/shell/api/v3/ItemsTransfer/transfers/databases/{Uri.EscapeDataString(database)}/sources?blobName={Uri.EscapeDataString(blobName)}";
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", targetToken);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Start item transfer failed: {(int)response.StatusCode} ({response.ReasonPhrase}) - {SafeSnippet(body)}");
        }

        var location = response.Headers.Location?.ToString() ?? string.Empty;
        var sourceName = GetLastPathSegment(location);
        return string.IsNullOrWhiteSpace(sourceName) ? blobName : sourceName;
    }

    private async Task WaitForItemTransferFinishedAsync(
        string? targetHostname,
        string targetToken,
        string sourceName,
        IProgress<string>? progress,
        bool enableDebugLogging,
        CancellationToken cancellationToken)
    {
        var endpoint = $"{targetHostname?.TrimEnd('/')}/sitecore/shell/api/v3/ItemsTransfer/transfers?page=1&pageSize=50";

        for (var attempt = 1; attempt <= 120; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);

            var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", targetToken);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                if (enableDebugLogging)
                {
                    progress?.Report($"[DEBUG] Item transfer status poll failed: {(int)response.StatusCode} ({response.ReasonPhrase}) - {SafeSnippet(body)}");
                }

                continue;
            }

            var transfer = ParseItemTransferBySourceName(body, sourceName);
            if (transfer == null)
            {
                continue;
            }

            progress?.Report($"Destination item transfer state: {transfer.TransferState}");

            if (string.Equals(transfer.TransferState, "Finished", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (string.Equals(transfer.TransferState, "Failed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(transfer.TransferState, "Discarded", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Destination item transfer failed with state '{transfer.TransferState}'.");
            }
        }

        throw new TimeoutException($"Destination item transfer for '{sourceName}' did not finish in time.");
    }

    private static BlobDetailsSnapshot ParseBlobDetails(string body)
    {
        var result = new BlobDetailsSnapshot();

        if (string.IsNullOrWhiteSpace(body))
        {
            return result;
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (TryGetPropertyIgnoreCase(root, "blobState", out var stateElement))
        {
            result.BlobState = ParseStateValue(stateElement);
        }

        if (TryGetPropertyIgnoreCase(root, "error", out var errorElement)
            && errorElement.ValueKind == JsonValueKind.String)
        {
            result.Error = errorElement.GetString();
        }

        return result;
    }

    private static ItemTransferSummary? ParseItemTransferBySourceName(string body, string sourceName)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (!TryGetPropertyIgnoreCase(root, "transfers", out var transfersElement)
            || transfersElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var transferElement in transfersElement.EnumerateArray())
        {
            if (!TryGetPropertyIgnoreCase(transferElement, "sourceName", out var sourceElement)
                || sourceElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var currentSourceName = sourceElement.GetString() ?? string.Empty;
            if (!string.Equals(currentSourceName, sourceName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var transferState = "Unknown";
            if (TryGetPropertyIgnoreCase(transferElement, "transferState", out var stateElement))
            {
                transferState = ParseStateValue(stateElement);
            }

            return new ItemTransferSummary
            {
                SourceName = currentSourceName,
                TransferState = transferState
            };
        }

        return null;
    }

    private static string ParseStateValue(JsonElement stateElement)
    {
        if (stateElement.ValueKind == JsonValueKind.String)
        {
            return stateElement.GetString() ?? "Unknown";
        }

        if (stateElement.ValueKind == JsonValueKind.Number && stateElement.TryGetInt32(out var value))
        {
            return value switch
            {
                0 => "Unknown",
                1 => "InProgress",
                2 => "Finished",
                3 => "Failed",
                4 => "Queued",
                5 => "Discarded",
                6 => "Uploaded",
                7 => "Initializing",
                8 => "Error",
                9 => "Consumed",
                10 => "Transferred",
                11 => "TransferredWithErrors",
                _ => "Unknown"
            };
        }

        return "Unknown";
    }

    private static bool TryGetContentDispositionParameter(ContentDispositionHeaderValue? contentDisposition, string name, out string value)
    {
        value = string.Empty;
        if (contentDisposition == null)
        {
            return false;
        }

        var parameter = contentDisposition.Parameters.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (parameter == null || string.IsNullOrWhiteSpace(parameter.Value))
        {
            return false;
        }

        value = parameter.Value.Trim('"');
        return true;
    }

    private static string GetLastPathSegment(string location)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return string.Empty;
        }

        var parts = location.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? string.Empty : parts[^1];
    }

    private static string ExtractTransferState(string? statusContent, out string? message)
    {
        message = null;

        if (string.IsNullOrWhiteSpace(statusContent))
        {
            return "Unknown";
        }

        try
        {
            using var doc = JsonDocument.Parse(statusContent);
            var root = doc.RootElement;

            if (TryGetPropertyIgnoreCase(root, "message", out var messageElement)
                && messageElement.ValueKind == JsonValueKind.String)
            {
                message = messageElement.GetString();
            }

            if (TryGetPropertyIgnoreCase(root, "status", out var statusElement)
                && statusElement.ValueKind == JsonValueKind.String)
            {
                return statusElement.GetString() ?? "Unknown";
            }

            if (TryGetPropertyIgnoreCase(root, "state", out var stateElement))
            {
                if (stateElement.ValueKind == JsonValueKind.String)
                {
                    return stateElement.GetString() ?? "Unknown";
                }

                if (stateElement.ValueKind == JsonValueKind.Number && stateElement.TryGetInt32(out var numericState))
                {
                    return numericState switch
                    {
                        0 => "Running",
                        1 => "Completed",
                        2 => "Failed",
                        3 => "NotFound",
                        _ => "Unknown"
                    };
                }
            }
        }
        catch
        {
            // ignored, Unknown will be returned
        }

        return "Unknown";
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static ContentTransferStatusSnapshot ParseContentTransferStatusSnapshot(string statusContent)
    {
        var snapshot = new ContentTransferStatusSnapshot();

        if (string.IsNullOrWhiteSpace(statusContent))
        {
            return snapshot;
        }

        try
        {
            using var doc = JsonDocument.Parse(statusContent);
            var root = doc.RootElement;

            snapshot.State = ExtractTransferState(statusContent, out var message);
            snapshot.Message = message;

            if (TryGetPropertyIgnoreCase(root, "chunkSetsMetadata", out var chunkSetsElement)
                && chunkSetsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var chunkSetElement in chunkSetsElement.EnumerateArray())
                {
                    if (!TryGetPropertyIgnoreCase(chunkSetElement, "chunkSetId", out var chunkSetIdElement)
                        || chunkSetIdElement.ValueKind != JsonValueKind.String
                        || !Guid.TryParse(chunkSetIdElement.GetString(), out var chunkSetId))
                    {
                        continue;
                    }

                    var chunkCount = 0;
                    if (TryGetPropertyIgnoreCase(chunkSetElement, "chunkCount", out var chunkCountElement)
                        && chunkCountElement.ValueKind == JsonValueKind.Number)
                    {
                        chunkCount = chunkCountElement.GetInt32();
                    }

                    snapshot.ChunkSetsMetadata.Add(new ChunkSetMetadataSnapshot
                    {
                        ChunkSetId = chunkSetId,
                        ChunkCount = chunkCount
                    });
                }
            }
        }
        catch
        {
            // ignored, defaults are returned
        }

        return snapshot;
    }

    private static string MaskToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return "<empty>";
        }

        if (token.Length <= 12)
        {
            return "***";
        }

        return $"{token[..6]}...{token[^6..]} (len={token.Length})";
    }

    private static string SafeSnippet(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return "<empty>";
        }

        var oneLine = content.Replace("\r", " ").Replace("\n", " ").Trim();
        return oneLine.Length <= 500 ? oneLine : oneLine[..500] + "...";
    }
}

public class TransferResult
{
    public bool Success { get; set; }
    public string? TransferId { get; set; }
    public string? Message { get; set; }
    public string? ErrorMessage { get; set; }
}

public class TransferApiResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("transferId")]
    public string? TransferId { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("status")]
    public string? Status { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("message")]
    public string? Message { get; set; }
}

public class TransferStatusResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("status")]
    public string? Status { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("message")]
    public string? Message { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("progress")]
    public int? Progress { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("state")]
    public string? State { get; set; }
}

internal sealed class ContentTransferStatusSnapshot
{
    public string State { get; set; } = "Unknown";

    public string? Message { get; set; }

    public List<ChunkSetMetadataSnapshot> ChunkSetsMetadata { get; set; } = [];
}

internal sealed class ChunkSetMetadataSnapshot
{
    public Guid ChunkSetId { get; set; }

    public int ChunkCount { get; set; }
}

internal sealed class DestinationChunkCompletionResponse
{
    public string ContentTransferFileName { get; set; } = string.Empty;
}

internal sealed class BlobDetailsSnapshot
{
    public string BlobState { get; set; } = "Unknown";

    public string? Error { get; set; }
}

internal sealed class ItemTransferSummary
{
    public string SourceName { get; set; } = string.Empty;

    public string TransferState { get; set; } = "Unknown";
}

internal sealed class ContentTransferChunkSnapshot
{
    public byte[] Data { get; set; } = Array.Empty<byte>();

    public bool IsMedia { get; set; }
}
