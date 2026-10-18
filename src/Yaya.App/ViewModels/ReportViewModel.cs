using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Yaya.App.ViewModels;

/// <summary>复盘窗视图模型：三行话 + 「明天开始」（单次建议分钟启动番茄，不改用户设置）。</summary>
public partial class ReportViewModel : ObservableObject
{
    private readonly int _adviceMinutes;
    private readonly Action<int> _onStartTomorrow;

    public ReportViewModel(string line1, string line2, string line3,
        int adviceMinutes, Action<int> onStartTomorrow)
    {
        Line1 = line1;
        Line2 = line2;
        Line3 = line3;
        _adviceMinutes = adviceMinutes;
        _onStartTomorrow = onStartTomorrow;
    }

    [ObservableProperty]
    private string _line1;

    [ObservableProperty]
    private string _line2;

    [ObservableProperty]
    private string _line3;

    [RelayCommand]
    private void StartTomorrow() => _onStartTomorrow(_adviceMinutes);
}
