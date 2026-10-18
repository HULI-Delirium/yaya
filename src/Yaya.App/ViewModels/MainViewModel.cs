using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Yaya.App.ViewModels;

/// <summary>
/// 设置页视图模型：采集 / 提醒 / 穿透三开关 + 番茄时长 + 一键清空。
/// 变更经回调交给 App 应用并落盘，本类不碰数据库与窗口句柄。
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly Action<bool> _onMonitoringChanged;
    private readonly Action<bool> _onNudgeChanged;
    private readonly Action<bool> _onClickThroughChanged;
    private readonly Action<int, int> _onPomodoroMinutesChanged;
    private readonly Action _clearData;

    public MainViewModel(Action<bool> onMonitoringChanged, Action<bool> onNudgeChanged,
        Action<bool> onClickThroughChanged, Action<int, int> onPomodoroMinutesChanged, Action clearData)
    {
        _onMonitoringChanged = onMonitoringChanged;
        _onNudgeChanged = onNudgeChanged;
        _onClickThroughChanged = onClickThroughChanged;
        _onPomodoroMinutesChanged = onPomodoroMinutesChanged;
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

    /// <summary>专注分钟输入（文本框；非法值不回调，由 App 回填合法值）。</summary>
    [ObservableProperty]
    private string _focusMinutesText = "25";

    /// <summary>休息分钟输入。</summary>
    [ObservableProperty]
    private string _breakMinutesText = "5";

    partial void OnIsMonitoringEnabledChanged(bool value) => _onMonitoringChanged(value);

    partial void OnIsNudgeEnabledChanged(bool value) => _onNudgeChanged(value);

    partial void OnIsClickThroughChanged(bool value) => _onClickThroughChanged(value);

    partial void OnFocusMinutesTextChanged(string value) => NotifyPomodoroMinutes();

    partial void OnBreakMinutesTextChanged(string value) => NotifyPomodoroMinutes();

    [RelayCommand]
    private void ClearData() => _clearData();

    // 两个框都解析为 1–120 的整数时才回调（输入过程中不完整不触发）
    private void NotifyPomodoroMinutes()
    {
        if (int.TryParse(FocusMinutesText, out var focus) && int.TryParse(BreakMinutesText, out var breakMinutes)
            && focus is >= 1 and <= 120 && breakMinutes is >= 1 and <= 120)
        {
            _onPomodoroMinutesChanged(focus, breakMinutes);
        }
    }
}
