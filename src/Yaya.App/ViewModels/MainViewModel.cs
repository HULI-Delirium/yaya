using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Yaya.App.ViewModels;

/// <summary>
/// 设置页视图模型：采集 / 提醒 / 穿透三开关 + 一键清空。
/// 变更经回调交给 App 应用并落盘，本类不碰数据库与窗口句柄。
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly Action<bool> _onMonitoringChanged;
    private readonly Action<bool> _onNudgeChanged;
    private readonly Action<bool> _onClickThroughChanged;
    private readonly Action _clearData;

    public MainViewModel(Action<bool> onMonitoringChanged, Action<bool> onNudgeChanged,
        Action<bool> onClickThroughChanged, Action clearData)
    {
        _onMonitoringChanged = onMonitoringChanged;
        _onNudgeChanged = onNudgeChanged;
        _onClickThroughChanged = onClickThroughChanged;
        _clearData = clearData;
    }

    /// <summary>当前状态一句话，由感知循环回写。</summary>
    [ObservableProperty]
    private string _statusText = "芽芽正在观察……";

    [ObservableProperty]
    private bool _isMonitoringEnabled;

    [ObservableProperty]
    private bool _isNudgeEnabled;

    [ObservableProperty]
    private bool _isClickThrough;

    partial void OnIsMonitoringEnabledChanged(bool value) => _onMonitoringChanged(value);

    partial void OnIsNudgeEnabledChanged(bool value) => _onNudgeChanged(value);

    partial void OnIsClickThroughChanged(bool value) => _onClickThroughChanged(value);

    [RelayCommand]
    private void ClearData() => _clearData();
}
