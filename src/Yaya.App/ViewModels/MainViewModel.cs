using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Yaya.App.ViewModels;

/// <summary>
/// 设置页视图模型：采集/提醒/穿透/番茄时长 + 数据与隐私（清空/导出/说明/文件夹）
/// + LLM 话术设置。变更经回调交给 App 应用并落盘，本类不碰数据库与窗口句柄。
/// api_key 仅以内存私有字段流转，不进入日志（红线）。
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly Action<bool> _onMonitoringChanged;
    private readonly Action<bool> _onNudgeChanged;
    private readonly Action<bool> _onClickThroughChanged;
    private readonly Action<int, int> _onPomodoroMinutesChanged;
    private readonly Action _clearData;
    private readonly Action _exportJson;
    private readonly Action _exportCsv;
    private readonly Action _openDataFolder;
    private readonly Action _showDataUsage;
    private readonly Action<bool, string, string, string> _onLlmChanged;   // enabled, baseUrl, model, apiKey

    public MainViewModel(
        Action<bool> onMonitoringChanged,
        Action<bool> onNudgeChanged,
        Action<bool> onClickThroughChanged,
        Action<int, int> onPomodoroMinutesChanged,
        Action clearData,
        Action exportJson,
        Action exportCsv,
        Action openDataFolder,
        Action showDataUsage,
        Action<bool, string, string, string> onLlmChanged)
    {
        _onMonitoringChanged = onMonitoringChanged;
        _onNudgeChanged = onNudgeChanged;
        _onClickThroughChanged = onClickThroughChanged;
        _onPomodoroMinutesChanged = onPomodoroMinutesChanged;
        _clearData = clearData;
        _exportJson = exportJson;
        _exportCsv = exportCsv;
        _openDataFolder = openDataFolder;
        _showDataUsage = showDataUsage;
        _onLlmChanged = onLlmChanged;
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

    /// <summary>本地数据目录展示文本（App 填入）。</summary>
    [ObservableProperty]
    private string _dataDirText = string.Empty;

    [ObservableProperty]
    private bool _isLlmEnabled;

    /// <summary>Base URL 展示值（不含 https:// 前缀，输入框自带）。</summary>
    [ObservableProperty]
    private string _llmBaseUrlText = string.Empty;

    [ObservableProperty]
    private string _llmModelText = string.Empty;

    /// <summary>api_key 仅内存流转（密码框不回显绑定）；日志绝不打印（红线）。</summary>
    private string _llmApiKey = string.Empty;

    internal string CurrentApiKey => _llmApiKey;

    public void UpdateApiKey(string apiKey)
    {
        _llmApiKey = apiKey;
        NotifyLlmChanged();
    }

    partial void OnIsMonitoringEnabledChanged(bool value) => _onMonitoringChanged(value);

    partial void OnIsNudgeEnabledChanged(bool value) => _onNudgeChanged(value);

    partial void OnIsClickThroughChanged(bool value) => _onClickThroughChanged(value);

    partial void OnFocusMinutesTextChanged(string value) => NotifyPomodoroMinutes();

    partial void OnBreakMinutesTextChanged(string value) => NotifyPomodoroMinutes();

    partial void OnIsLlmEnabledChanged(bool value) => NotifyLlmChanged();

    partial void OnLlmBaseUrlTextChanged(string value) => NotifyLlmChanged();

    partial void OnLlmModelTextChanged(string value) => NotifyLlmChanged();

    [RelayCommand]
    private void ClearData() => _clearData();

    [RelayCommand]
    private void ExportJson() => _exportJson();

    [RelayCommand]
    private void ExportCsv() => _exportCsv();

    [RelayCommand]
    private void OpenDataFolder() => _openDataFolder();

    [RelayCommand]
    private void ShowDataUsage() => _showDataUsage();

    // 两个框都解析为 1–120 的整数时才回调（输入过程中不完整不触发）
    private void NotifyPomodoroMinutes()
    {
        if (int.TryParse(FocusMinutesText, out var focus) && int.TryParse(BreakMinutesText, out var breakMinutes)
            && focus is >= 1 and <= 120 && breakMinutes is >= 1 and <= 120)
        {
            _onPomodoroMinutesChanged(focus, breakMinutes);
        }
    }

    private void NotifyLlmChanged() =>
        _onLlmChanged(IsLlmEnabled, LlmBaseUrlText, LlmModelText, _llmApiKey);
}
