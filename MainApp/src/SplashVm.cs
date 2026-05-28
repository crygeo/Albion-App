using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Albion_App;

public partial class SplashVm : ObservableObject
{
    [ObservableProperty] private double _progress;

    public void UpdateProgress(int value)
        => Application.Current.Dispatcher.InvokeAsync(() => Progress = value);
}
