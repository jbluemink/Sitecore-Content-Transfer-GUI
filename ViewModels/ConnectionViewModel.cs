using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SitecoreContentTransfer.Models;
using SitecoreContentTransfer.Services;
using SitecoreContentTransfer.Login;

namespace SitecoreContentTransfer.ViewModels;

public partial class ConnectionViewModel : ObservableObject
{
    private readonly ConnectionValidator _connectionValidator;
    private readonly HashSet<string> _dirtyProperties = [];
    private bool _isLoadingFromConfig = false;

    [ObservableProperty]
    private ConnectionType _connectionType = ConnectionType.ClientCredentials;

    [ObservableProperty]
    private string _clientId = string.Empty;

    [ObservableProperty]
    private string _clientSecret = string.Empty;

    [ObservableProperty]
    private string _hostname = string.Empty;

    [ObservableProperty]
    private string _userJsonPath = string.Empty;

    [ObservableProperty]
    private string _selectedEndpoint = string.Empty;

    [ObservableProperty]
    private List<string> _availableEndpoints = [];

    [ObservableProperty]
    private string _authority = "https://auth.sitecorecloud.io";

    [ObservableProperty]
    private string _audience = "https://api.sitecorecloud.io";

    [ObservableProperty]
    private string _connectionStatus = string.Empty;

    [ObservableProperty]
    private bool _isConnectionValid = false;

    [ObservableProperty]
    private bool _isTesting = false;

    public ConnectionViewModel(ConnectionValidator connectionValidator)
    {
        _connectionValidator = connectionValidator;
    }

    public bool HasUnsavedChanges => _dirtyProperties.Count > 0;

    public string StatusMessage => HasUnsavedChanges ? "* Unsaved changes" : string.Empty;

    public void MarkAsClean()
    {
        _dirtyProperties.Clear();
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(StatusMessage));
    }

    private void MarkAsDirty(string propertyName)
    {
        if (_isLoadingFromConfig)
            return;

        var previousCount = _dirtyProperties.Count;
        _dirtyProperties.Add(propertyName);

        if (previousCount == 0 && _dirtyProperties.Count > 0)
        {
            OnPropertyChanged(nameof(HasUnsavedChanges));
            OnPropertyChanged(nameof(StatusMessage));
        }
    }

    partial void OnConnectionTypeChanged(ConnectionType value)
    {
        MarkAsDirty(nameof(ConnectionType));
        ConnectionStatus = string.Empty;
        IsConnectionValid = false;

        if (value == ConnectionType.CliUserJson)
        {
            LoadEndpointsFromUserJson();
        }
    }

    partial void OnClientIdChanged(string value)
    {
        MarkAsDirty(nameof(ClientId));
    }

    partial void OnClientSecretChanged(string value)
    {
        MarkAsDirty(nameof(ClientSecret));
    }

    partial void OnHostnameChanged(string value)
    {
        MarkAsDirty(nameof(Hostname));
    }

    partial void OnUserJsonPathChanged(string value)
    {
        MarkAsDirty(nameof(UserJsonPath));
        if (ConnectionType == ConnectionType.CliUserJson)
        {
            LoadEndpointsFromUserJson();
        }
    }

    partial void OnSelectedEndpointChanged(string value)
    {
        MarkAsDirty(nameof(SelectedEndpoint));
        if (ConnectionType == ConnectionType.CliUserJson)
        {
            UpdateHostnameFromEndpoint();
        }
    }

    partial void OnAuthorityChanged(string value)
    {
        MarkAsDirty(nameof(Authority));
    }

    partial void OnAudienceChanged(string value)
    {
        MarkAsDirty(nameof(Audience));
    }



    private void LoadEndpointsFromUserJson()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(UserJsonPath) || !File.Exists(UserJsonPath))
            {
                AvailableEndpoints = [];
                SelectedEndpoint = string.Empty;
                return;
            }

            var json = File.ReadAllText(UserJsonPath);
            var userJson = System.Text.Json.JsonSerializer.Deserialize<UserJson>(json);

            if (userJson?.Endpoints != null)
            {
                AvailableEndpoints = [.. userJson.Endpoints.Keys];

                if (!string.IsNullOrEmpty(userJson.DefaultEndpoint) && AvailableEndpoints.Contains(userJson.DefaultEndpoint))
                {
                    SelectedEndpoint = userJson.DefaultEndpoint;
                }
                else if (AvailableEndpoints.Count > 0)
                {
                    SelectedEndpoint = AvailableEndpoints[0];
                }

                // Load hostname from selected endpoint
                UpdateHostnameFromEndpoint();
            }
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Error loading user.json: {ex.Message}";
            AvailableEndpoints = [];
        }
    }

    private void UpdateHostnameFromEndpoint()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(UserJsonPath) || 
                string.IsNullOrWhiteSpace(SelectedEndpoint) ||
                !File.Exists(UserJsonPath))
            {
                return;
            }

            var envConfig = Login.LoginHelper.GetSitecoreEnvironment(UserJsonPath, SelectedEndpoint);

            if (!string.IsNullOrWhiteSpace(envConfig.Host))
            {
                Hostname = envConfig.Host;
            }
        }
        catch
        {
            // Ignore errors during hostname update
        }
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        IsTesting = true;
        ConnectionStatus = "Testing connection and transfer API access...";
        IsConnectionValid = false;

        try
        {
            var config = ToConnectionConfig();

            var connectionResult = await _connectionValidator.ValidateConnectionAsync(config);
            if (!connectionResult.IsValid)
            {
                ConnectionStatus = connectionResult.ErrorMessage ?? "Connection failed.";
                return;
            }

            var transferAccessResult = await _connectionValidator.ValidateTransferAccessAsync(config);
            if (!transferAccessResult.IsValid)
            {
                ConnectionStatus = transferAccessResult.ErrorMessage ?? "Transfer API access check failed.";
                return;
            }

            IsConnectionValid = true;
            ConnectionStatus = "Connection and transfer API access are valid.";

            if (!string.IsNullOrWhiteSpace(transferAccessResult.Hostname) && string.IsNullOrWhiteSpace(Hostname))
            {
                Hostname = transferAccessResult.Hostname;
            }

            MarkAsClean();
        }
        catch (Exception ex)
        {
            IsConnectionValid = false;
            ConnectionStatus = $"Error: {ex.Message}";
        }
        finally
        {
            IsTesting = false;
        }
    }

    [RelayCommand]
    private async Task BrowseUserJsonAsync()
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker
            {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary
            };
            picker.FileTypeFilter.Add(".json");

            // Get the current window's handle
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                UserJsonPath = file.Path;
            }
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Error selecting file: {ex.Message}";
        }
    }

    public ConnectionConfig ToConnectionConfig()
    {
        return new ConnectionConfig
        {
            Type = ConnectionType,
            ClientId = ClientId,
            ClientSecret = ClientSecret,
            Hostname = Hostname,
            UserJsonPath = UserJsonPath,
            SelectedEndpoint = SelectedEndpoint,
            Authority = Authority,
            Audience = Audience
        };
    }

    public void FromConnectionConfig(ConnectionConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        try
        {
            _isLoadingFromConfig = true;

            if (!_dirtyProperties.Contains(nameof(ConnectionType)))
            {
                ConnectionType = config.Type;
            }

            if (!_dirtyProperties.Contains(nameof(ClientId)))
            {
                ClientId = config.ClientId;
            }

            if (!_dirtyProperties.Contains(nameof(ClientSecret)))
            {
                ClientSecret = config.ClientSecret;
            }

            if (!_dirtyProperties.Contains(nameof(Hostname)))
            {
                Hostname = config.Hostname;
            }

            if (!_dirtyProperties.Contains(nameof(UserJsonPath)))
            {
                UserJsonPath = config.UserJsonPath;
            }

            if (!_dirtyProperties.Contains(nameof(SelectedEndpoint)))
            {
                SelectedEndpoint = config.SelectedEndpoint;
            }

            if (!_dirtyProperties.Contains(nameof(Authority)))
            {
                Authority = config.Authority;
            }

            if (!_dirtyProperties.Contains(nameof(Audience)))
            {
                Audience = config.Audience;
            }

            if (ConnectionType == ConnectionType.CliUserJson && 
                !string.IsNullOrWhiteSpace(UserJsonPath) &&
                !_dirtyProperties.Contains(nameof(UserJsonPath)))
            {
                LoadEndpointsFromUserJson();
            }
        }
        finally
        {
            _isLoadingFromConfig = false;
        }
    }
}
