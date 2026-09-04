using System;
using System.IO;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using SitecoreContentTransfer.ViewModels;
using WinRT.Interop;
using Windows.Graphics;

namespace SitecoreContentTransfer;

public sealed partial class MainWindow : Window
{
    private bool _isInitialized;

    public MainViewModel ViewModel { get; }

    public MainWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        Title = "Sitecore Content Transfer";

        MainViewControl.ViewModel = viewModel;

        Activated += OnWindowActivated;
    }

    private void ConfigureWindowIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Square44x44Logo.ico");
        if (!File.Exists(iconPath))
        {
            iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "App Icon.ico");
        }

        if (!File.Exists(iconPath))
        {
            return;
        }

        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.SetIcon(iconPath);
    }

    private void ConfigureWindowPlacement()
    {
        const int preferredWidth = 1600;
        const int preferredHeight = 1020;
        const int topMargin = 20;

        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
        var workArea = displayArea.WorkArea;

        var width = Math.Min(preferredWidth, workArea.Width);
        var height = Math.Min(preferredHeight, Math.Max(600, workArea.Height - topMargin));
        var x = workArea.X + Math.Max(0, (workArea.Width - width) / 2);
        var y = workArea.Y + topMargin;

        appWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }

    private async void OnWindowActivated(object sender, WindowActivatedEventArgs e)
    {
        if (_isInitialized || e.WindowActivationState == WindowActivationState.Deactivated)
        {
            return;
        }

        _isInitialized = true;
        Activated -= OnWindowActivated;

        ConfigureWindowIcon();
        ConfigureWindowPlacement();

        await ViewModel.InitializeAsync();
    }
}
