using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SitecoreContentTransfer.ViewModels;

namespace SitecoreContentTransfer;

public sealed partial class MainView : UserControl
{
    private ScrollViewer? _transferLogScrollViewer;
    private bool _keepLogScrolledToBottom = true;

    public MainViewModel ViewModel { get; set; } = null!;

    public MainView()
    {
        this.InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _transferLogScrollViewer ??= FindDescendant<ScrollViewer>(TransferLogTextBox);
        if (_transferLogScrollViewer != null)
        {
            _transferLogScrollViewer.ViewChanged -= TransferLogScrollViewer_ViewChanged;
            _transferLogScrollViewer.ViewChanged += TransferLogScrollViewer_ViewChanged;
            _keepLogScrolledToBottom = true;
        }
    }

    private void TransferLogTextBox_OnTextChanged(object sender, TextChangedEventArgs e)
    {
        _transferLogScrollViewer ??= FindDescendant<ScrollViewer>(TransferLogTextBox);
        if (_transferLogScrollViewer == null || !_keepLogScrolledToBottom)
        {
            return;
        }

        _transferLogScrollViewer.ChangeView(null, _transferLogScrollViewer.ScrollableHeight, null, true);
    }

    private void TransferLogScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_transferLogScrollViewer == null)
        {
            return;
        }

        _keepLogScrolledToBottom = _transferLogScrollViewer.ScrollableHeight <= 0
            || _transferLogScrollViewer.VerticalOffset >= _transferLogScrollViewer.ScrollableHeight - 2;
    }

    private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                return match;
            }

            var nested = FindDescendant<T>(child);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }
}
