using CommunityToolkit.Mvvm.ComponentModel;

namespace WC4MapEditor.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private object? _currentViewModel;

    [ObservableProperty]
    private string _zoomText = "100%";

    [ObservableProperty]
    private string _positionText = "(0, 0)";

    [ObservableProperty]
    private string _sceneStatusText = "准备就绪";

    public MainViewModel()
    {
    }
}
