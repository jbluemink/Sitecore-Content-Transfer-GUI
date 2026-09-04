using System;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace SitecoreContentTransfer.Login;

public class LoginHelper
{
    private const int UserJsonReadRetryCount = 5;
    private const int UserJsonReadRetryDelayMs = 200;

    public static EnvironmentConfiguration GetSitecoreEnvironment(string userJsonPath, string endpointName = "")
    {
        return ReadEnvironmentConfigurationWithRetry(userJsonPath, endpointName);
    }

    private static EnvironmentConfiguration ReadEnvironmentConfigurationWithRetry(string userJsonPath, string endpointName)
    {
        Exception? lastException = null;

        for (var attempt = 1; attempt <= UserJsonReadRetryCount; attempt++)
        {
            try
            {
                using var stream = new FileStream(
                    userJsonPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                string json = reader.ReadToEnd();
                var source = JsonSerializer.Deserialize<UserJson>(json);
                return GetEnvironmentConfiguration(source ?? new UserJson(), endpointName);
            }
            catch (IOException ex) when (attempt < UserJsonReadRetryCount)
            {
                lastException = ex;
                Thread.Sleep(UserJsonReadRetryDelayMs);
            }
            catch (UnauthorizedAccessException ex) when (attempt < UserJsonReadRetryCount)
            {
                lastException = ex;
                Thread.Sleep(UserJsonReadRetryDelayMs);
            }
        }

        if (lastException != null)
        {
            throw new IOException($"Failed to read '{userJsonPath}' after {UserJsonReadRetryCount} attempts.", lastException);
        }

        throw new IOException($"Failed to read '{userJsonPath}'.");
    }

    internal static EnvironmentConfiguration GetEnvironmentConfiguration(UserJson userJson, string endpointName)
    {
        if (string.IsNullOrEmpty(endpointName))
        {
            endpointName = userJson.DefaultEndpoint;
        }

        if (userJson.Endpoints.TryGetValue(endpointName, out var endpointConfig) && endpointConfig != null)
        {
            if (!string.IsNullOrEmpty(endpointConfig.Ref) && string.IsNullOrEmpty(endpointConfig.AccessToken))
            {
                return GetEnvironmentConfiguration(userJson, "xmCloud");
            }
            return endpointConfig;
        }

        throw new InvalidOperationException($"Endpoint '{endpointName}' could not be resolved from user.json.");
    }
}
