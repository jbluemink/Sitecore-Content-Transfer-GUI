using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using SitecoreContentTransfer.Models;

namespace SitecoreContentTransfer.Services;

public class ConfigService
{
    private readonly string _configFilePath;
    private readonly JsonSerializerOptions _jsonOptions;

    public ConfigService()
    {
        var appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var appFolder = Path.Combine(appDataFolder, "SitecoreContentTransfer");

        if (!Directory.Exists(appFolder))
        {
            Directory.CreateDirectory(appFolder);
        }

        _configFilePath = Path.Combine(appFolder, "transferconfig.json");

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };
    }

    public async Task<AppConfig> LoadConfigAsync()
    {
        try
        {
            if (!File.Exists(_configFilePath))
            {
                return new AppConfig();
            }

            var json = await File.ReadAllTextAsync(_configFilePath);
            var config = JsonSerializer.Deserialize<AppConfig>(json, _jsonOptions);

            return config ?? new AppConfig();
        }
        catch (Exception ex)
        {
            // Log error or handle it appropriately
            System.Diagnostics.Debug.WriteLine($"Failed to load config: {ex.Message}");
            return new AppConfig();
        }
    }

    public async Task SaveConfigAsync(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        try
        {
            var json = JsonSerializer.Serialize(config, _jsonOptions);
            await File.WriteAllTextAsync(_configFilePath, json);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to save configuration: {ex.Message}", ex);
        }
    }

    public async Task SaveLastUsedConfigAsync(TransferConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var appConfig = await LoadConfigAsync();
        appConfig.LastUsed = config;
        await SaveConfigAsync(appConfig);
    }

    public async Task<TransferConfig?> LoadLastUsedConfigAsync()
    {
        var appConfig = await LoadConfigAsync();
        return appConfig.LastUsed;
    }

    public async Task SaveDebugLoggingEnabledAsync(bool isEnabled)
    {
        var appConfig = await LoadConfigAsync();
        appConfig.DebugLoggingEnabled = isEnabled;
        await SaveConfigAsync(appConfig);
    }

    public async Task<bool> LoadDebugLoggingEnabledAsync()
    {
        var appConfig = await LoadConfigAsync();
        return appConfig.DebugLoggingEnabled;
    }

    public async Task SavePresetAsync(string presetName, TransferConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (string.IsNullOrWhiteSpace(presetName))
        {
            throw new ArgumentException("Preset name cannot be empty.", nameof(presetName));
        }

        var appConfig = await LoadConfigAsync();

        // Remove existing preset with same name
        appConfig.Presets.RemoveAll(p => p.Name.Equals(presetName, StringComparison.OrdinalIgnoreCase));

        // Add new preset
        appConfig.Presets.Add(new TransferPreset
        {
            Name = presetName,
            Config = config
        });

        await SaveConfigAsync(appConfig);
    }

    public async Task<List<TransferPreset>> LoadPresetsAsync()
    {
        var appConfig = await LoadConfigAsync();
        return appConfig.Presets;
    }

    public async Task DeletePresetAsync(string presetName)
    {
        if (string.IsNullOrWhiteSpace(presetName))
        {
            throw new ArgumentException("Preset name cannot be empty.", nameof(presetName));
        }

        var appConfig = await LoadConfigAsync();
        appConfig.Presets.RemoveAll(p => p.Name.Equals(presetName, StringComparison.OrdinalIgnoreCase));
        await SaveConfigAsync(appConfig);
    }

    public string GetConfigFilePath() => _configFilePath;
}
