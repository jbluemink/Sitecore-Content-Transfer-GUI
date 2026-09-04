using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SitecoreContentTransfer.ViewModels;

namespace SitecoreContentTransfer.Controls;

public sealed partial class ConnectionPanel : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(ConnectionViewModel),
            typeof(ConnectionPanel),
            new PropertyMetadata(null));

    public ConnectionViewModel? ViewModel
    {
        get => (ConnectionViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(
            nameof(Title),
            typeof(string),
            typeof(ConnectionPanel),
            new PropertyMetadata("Connection"));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public ConnectionPanel()
    {
        this.InitializeComponent();
    }
}
