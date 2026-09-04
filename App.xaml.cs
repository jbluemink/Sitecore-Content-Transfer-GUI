using Microsoft.UI.Xaml;
using SitecoreContentTransfer.Services;
using SitecoreContentTransfer.ViewModels;

namespace SitecoreContentTransfer;

public partial class App : Application
{
    public static Window? MainWindow { get; private set; }

    public App()
    {
        // InitializeComponent(); // Remove this line if App.xaml doesn't exist or isn't needed
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Initialize services
        var tokenService = new TokenService();
        var connectionValidator = new ConnectionValidator(tokenService);
        var configService = new ConfigService();
        var transferApiClient = new ContentTransferApiClient(tokenService);

        // Initialize ViewModels
        var sourceConnectionViewModel = new ConnectionViewModel(connectionValidator);
        var targetConnectionViewModel = new ConnectionViewModel(connectionValidator);

        var mainViewModel = new MainViewModel(
            configService,
            transferApiClient,
            sourceConnectionViewModel,
            targetConnectionViewModel);

        // Create and activate main window
        MainWindow = new MainWindow(mainViewModel);
        MainWindow.Activate();
    }
}
