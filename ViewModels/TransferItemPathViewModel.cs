using System;
using CommunityToolkit.Mvvm.ComponentModel;
using SitecoreContentTransfer.Models;

namespace SitecoreContentTransfer.ViewModels;

public partial class TransferItemPathViewModel : ObservableObject
{
    [ObservableProperty]
    private string _path = string.Empty;

    [ObservableProperty]
    private TransferMode _mode = TransferMode.ItemAndDescendants;

    public TransferItemPathViewModel()
    {
    }

    public TransferItemPathViewModel(string path, TransferMode mode)
    {
        Path = path;
        Mode = mode;
    }

    public TransferItemPath ToModel()
    {
        return new TransferItemPath
        {
            Path = Path,
            Mode = Mode
        };
    }

    public static TransferItemPathViewModel FromModel(TransferItemPath model)
    {
        ArgumentNullException.ThrowIfNull(model);

        return new TransferItemPathViewModel
        {
            Path = model.Path,
            Mode = model.Mode
        };
    }
}
