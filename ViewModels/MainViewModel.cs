using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SitecoreContentTransfer.Models;
using SitecoreContentTransfer.Services;

namespace SitecoreContentTransfer.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ConfigService _configService;
    private readonly ContentTransferApiClient _transferApiClient;
    private bool _isLoadingTransferConfig;
    private bool _isAutoSavingLastUsedConfig;
    private bool _isInitializationCompleted;

    [ObservableProperty]
    private ConnectionViewModel _sourceConnection;

    [ObservableProperty]
    private ConnectionViewModel _targetConnection;

    [ObservableProperty]
    private string _database = "master";

    [ObservableProperty]
    private ObservableCollection<TransferItemPathViewModel> _itemPaths = [];

    [ObservableProperty]
    private MergeStrategy _mergeStrategy = MergeStrategy.OverrideExistingItem;

    [ObservableProperty]
    private ObservableCollection<TransferPreset> _presets = [];

    [ObservableProperty]
    private TransferPreset? _selectedPreset;

    [ObservableProperty]
    private string _newPresetName = string.Empty;

    [ObservableProperty]
    private string _transferLog = string.Empty;

    [ObservableProperty]
    private double _transferProgress = 0;

    [ObservableProperty]
    private bool _isTransferring = false;

    [ObservableProperty]
    private bool _isTransferComplete = false;

    [ObservableProperty]
    private bool _isDebugLoggingEnabled = false;

    public string AppVersion { get; } = BuildVersionText();

    public List<string> AvailableDatabases { get; } = ["master", "core"];

    public MainViewModel(
        ConfigService configService,
        ContentTransferApiClient transferApiClient,
        ConnectionViewModel sourceConnection,
        ConnectionViewModel targetConnection)
    {
        _configService = configService;
        _transferApiClient = transferApiClient;
        _sourceConnection = sourceConnection;
        _targetConnection = targetConnection;

        SourceConnection.PropertyChanged += OnConnectionPropertyChanged;
        TargetConnection.PropertyChanged += OnConnectionPropertyChanged;

        ItemPaths.CollectionChanged += OnItemPathsCollectionChanged;

        // Add default item path
        ItemPaths.Add(new TransferItemPathViewModel("/sitecore/content/Home", TransferMode.ItemAndDescendants));
    }

    public async Task InitializeAsync()
    {
        await LoadDebugLoggingPreferenceAsync();
        await LoadPresetsAsync();
        await LoadLastUsedConfigAsync();
        _isInitializationCompleted = true;
    }

    partial void OnIsDebugLoggingEnabledChanged(bool value)
    {
        _ = SaveDebugLoggingPreferenceAsync(value);
    }

    partial void OnDatabaseChanged(string value)
    {
        TriggerAutoSaveLastUsedConfig();
    }

    partial void OnMergeStrategyChanged(MergeStrategy value)
    {
        TriggerAutoSaveLastUsedConfig();
    }

    partial void OnSourceConnectionChanged(ConnectionViewModel value)
    {
        value.PropertyChanged += OnConnectionPropertyChanged;
        TriggerAutoSaveLastUsedConfig();
    }

    partial void OnTargetConnectionChanged(ConnectionViewModel value)
    {
        value.PropertyChanged += OnConnectionPropertyChanged;
        TriggerAutoSaveLastUsedConfig();
    }

    private void OnConnectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isLoadingTransferConfig)
        {
            return;
        }

        var propertyName = e.PropertyName;
        if (propertyName == nameof(ConnectionViewModel.ClientId)
            || propertyName == nameof(ConnectionViewModel.ClientSecret)
            || propertyName == nameof(ConnectionViewModel.Hostname)
            || propertyName == nameof(ConnectionViewModel.UserJsonPath)
            || propertyName == nameof(ConnectionViewModel.SelectedEndpoint)
            || propertyName == nameof(ConnectionViewModel.Authority)
            || propertyName == nameof(ConnectionViewModel.Audience)
            || propertyName == nameof(ConnectionViewModel.ConnectionType))
        {
            TriggerAutoSaveLastUsedConfig();
        }
    }

    private void OnItemPathPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isLoadingTransferConfig)
        {
            return;
        }

        if (e.PropertyName == nameof(TransferItemPathViewModel.Path)
            || e.PropertyName == nameof(TransferItemPathViewModel.Mode))
        {
            TriggerAutoSaveLastUsedConfig();
        }
    }

    private void OnItemPathsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (var oldItem in e.OldItems.OfType<TransferItemPathViewModel>())
            {
                oldItem.PropertyChanged -= OnItemPathPropertyChanged;
            }
        }

        if (e.NewItems != null)
        {
            foreach (var newItem in e.NewItems.OfType<TransferItemPathViewModel>())
            {
                newItem.PropertyChanged += OnItemPathPropertyChanged;
            }
        }

        if (!_isLoadingTransferConfig)
        {
            TriggerAutoSaveLastUsedConfig();
        }
    }

    [RelayCommand]
    private void AddItemPath()
    {
        ItemPaths.Add(new TransferItemPathViewModel());
    }

    [RelayCommand]
    private void RemoveItemPath(TransferItemPathViewModel item)
    {
        if (item != null)
        {
            ItemPaths.Remove(item);
        }
    }

    [RelayCommand]
    private void ClearTransferLog()
    {
        TransferLog = string.Empty;
    }

    [RelayCommand]
    private async Task StartTransferAsync()
    {
        if (IsTransferring)
            return;

        // Validation
        if (!SourceConnection.IsConnectionValid)
        {
            AppendLog("Error: Source connection not validated. Please test the connection first.");
            return;
        }

        if (!TargetConnection.IsConnectionValid)
        {
            AppendLog("Error: Target connection not validated. Please test the connection first.");
            return;
        }

        if (ItemPaths.Count == 0)
        {
            AppendLog("Error: No item paths specified.");
            return;
        }

        if (ItemPaths.Any(p => string.IsNullOrWhiteSpace(p.Path)))
        {
            AppendLog("Error: One or more item paths are empty.");
            return;
        }

        // Confirm for destructive merge strategy
        if (MergeStrategy == MergeStrategy.OverrideExistingTree)
        {
            // In a real app, show a confirmation dialog
            AppendLog("Warning: Using OverrideExistingTree - this will delete existing items!");
        }

        IsTransferring = true;
        IsTransferComplete = false;
        TransferProgress = 0;
        TransferLog = string.Empty;

        try
        {
            AppendLog("=== Starting Content Transfer ===");

            var config = BuildTransferConfig();

            // Save as last used
            await _configService.SaveLastUsedConfigAsync(config);

            var progress = new Progress<string>(message =>
            {
                AppendLog(message);
                // Simulate progress
                if (TransferProgress < 90)
                {
                    TransferProgress += 5;
                }
            });

            var result = await _transferApiClient.StartTransferAsync(config, progress, IsDebugLoggingEnabled);

            if (result.Success)
            {
                TransferProgress = 100;
                AppendLog($"✓ Transfer completed successfully!");
                if (!string.IsNullOrWhiteSpace(result.TransferId))
                {
                    AppendLog($"Transfer ID: {result.TransferId}");
                }
                IsTransferComplete = true;

                // Clear dirty flags after successful transfer
                SourceConnection.MarkAsClean();
                TargetConnection.MarkAsClean();
            }
            else
            {
                AppendLog($"✗ Transfer failed: {result.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            AppendLog($"✗ Error: {ex.Message}");
        }
        finally
        {
            IsTransferring = false;
        }
    }

    [RelayCommand]
    private async Task SavePresetAsync()
    {
        if (string.IsNullOrWhiteSpace(NewPresetName))
        {
            AppendLog("Error: Please enter a preset name.");
            return;
        }

        try
        {
            var config = BuildTransferConfig();
            await _configService.SavePresetAsync(NewPresetName, config);
            AppendLog($"✓ Preset '{NewPresetName}' saved.");

            // Clear dirty flags after successful save
            SourceConnection.MarkAsClean();
            TargetConnection.MarkAsClean();

            await LoadPresetsAsync();
            NewPresetName = string.Empty;
        }
        catch (Exception ex)
        {
            AppendLog($"Error saving preset: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task LoadPresetAsync()
    {
        if (SelectedPreset == null)
        {
            AppendLog("Error: No preset selected.");
            return;
        }

        try
        {
            LoadTransferConfig(SelectedPreset.Config);
            AppendLog($"✓ Preset '{SelectedPreset.Name}' loaded.");
        }
        catch (Exception ex)
        {
            AppendLog($"Error loading preset: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task DeletePresetAsync()
    {
        if (SelectedPreset == null)
        {
            AppendLog("Error: No preset selected.");
            return;
        }

        try
        {
            var presetName = SelectedPreset.Name;
            await _configService.DeletePresetAsync(presetName);
            AppendLog($"✓ Preset '{presetName}' deleted.");

            await LoadPresetsAsync();
            SelectedPreset = null;
        }
        catch (Exception ex)
        {
            AppendLog($"Error deleting preset: {ex.Message}");
        }
    }

    private async Task SaveDebugLoggingPreferenceAsync(bool isEnabled)
    {
        try
        {
            await _configService.SaveDebugLoggingEnabledAsync(isEnabled);
        }
        catch (Exception ex)
        {
            AppendLog($"Error saving debug logging setting: {ex.Message}");
        }
    }

    private async Task LoadDebugLoggingPreferenceAsync()
    {
        try
        {
            IsDebugLoggingEnabled = await _configService.LoadDebugLoggingEnabledAsync();
            AppendLog($"Debug logging: {(IsDebugLoggingEnabled ? "ON" : "OFF")}");
        }
        catch (Exception ex)
        {
            AppendLog($"Error loading debug logging setting: {ex.Message}");
        }
    }

    private async Task LoadPresetsAsync()
    {
        try
        {
            var presets = await _configService.LoadPresetsAsync();
            Presets.Clear();
            foreach (var preset in presets)
            {
                Presets.Add(preset);
            }
        }
        catch (Exception ex)
        {
            AppendLog($"Error loading presets: {ex.Message}");
        }
    }

    private async Task LoadLastUsedConfigAsync()
    {
        try
        {
            var lastUsed = await _configService.LoadLastUsedConfigAsync();
            if (lastUsed != null)
            {
                LoadTransferConfig(lastUsed);
            }
        }
        catch (Exception ex)
        {
            AppendLog($"Error loading last used config: {ex.Message}");
        }
    }

    private void TriggerAutoSaveLastUsedConfig()
    {
        if (!_isInitializationCompleted)
        {
            return;
        }

        _ = AutoSaveLastUsedConfigAsync();
    }

    private async Task AutoSaveLastUsedConfigAsync()
    {
        if (_isLoadingTransferConfig || _isAutoSavingLastUsedConfig)
        {
            return;
        }

        try
        {
            _isAutoSavingLastUsedConfig = true;
            await _configService.SaveLastUsedConfigAsync(BuildTransferConfig());
        }
        catch (Exception ex)
        {
            AppendLog($"Error auto-saving last used config: {ex.Message}");
        }
        finally
        {
            _isAutoSavingLastUsedConfig = false;
        }
    }

    private TransferConfig BuildTransferConfig()
    {
        return new TransferConfig
        {
            Source = SourceConnection.ToConnectionConfig(),
            Target = TargetConnection.ToConnectionConfig(),
            Database = Database,
            Items = ItemPaths.Select(p => p.ToModel()).ToList(),
            MergeStrategy = MergeStrategy
        };
    }

    private void LoadTransferConfig(TransferConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        _isLoadingTransferConfig = true;
        try
        {
            SourceConnection.FromConnectionConfig(config.Source);
            TargetConnection.FromConnectionConfig(config.Target);
            Database = config.Database;
            MergeStrategy = config.MergeStrategy;

            ItemPaths.Clear();
            foreach (var item in config.Items)
            {
                ItemPaths.Add(TransferItemPathViewModel.FromModel(item));
            }
        }
        finally
        {
            _isLoadingTransferConfig = false;
        }
    }

    private void AppendLog(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        TransferLog += $"[{timestamp}] {message}\n";
    }

    private static string BuildVersionText()
    {
        var version = typeof(MainViewModel).Assembly.GetName().Version;
        return version == null
            ? "Version n/a"
            : $"Version {version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";
    }
}
