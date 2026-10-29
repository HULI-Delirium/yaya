using System.Globalization;
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
    private readonly Action<bool> _onPetVisibleChanged;
    private readonly Action<int, bool, string, string> _onNudgeDisplayChanged;   // limit, quietEnabled, start, end
    private readonly Action<bool> _onClickThroughChanged;
    private readonly Action<int, int> _onPomodoroMinutesChanged;
    private readonly Action _clearData;
    private readonly Action _exportJson;
    private readonly Action _exportCsv;
    private readonly Action _openDataFolder;
    private readonly Action _showDataUsage;
    private readonly Action<bool, string, string, string> _onLlmChanged;   // enabled, baseUrl, model, apiKey
    private readonly Action<bool> _onDiagnosticLoggingChanged;

    public MainViewModel(
        Action<bool> onMonitoringChanged,
        Action<bool> onNudgeChanged,
        Action<bool> onPetVisibleChanged,
        Action<int, bool, string, string> onNudgeDisplayChanged,
        Action<bool> onClickThroughChanged,
        Action<int, int> onPomodoroMinutesChanged,
        Action clearData,
        Action exportJson,
        Action exportCsv,
        Action openDataFolder,
        Action showDataUsage,
        Action<bool, string, string, string> onLlmChanged,
        Action<bool> onDiagnosticLoggingChanged,
        AppRulesViewModel appRules)
    {
        AppRules = appRules;
        _onDiagnosticLoggingChanged = onDiagnosticLoggingChanged;
        _onMonitoringChanged = onMonitoringChanged;
        _onNudgeChanged = onNudgeChanged;
        _onPetVisibleChanged = onPetVisibleChanged;
        _onNudgeDisplayChanged = onNudgeDisplayChanged;
        _onClickThroughChanged = onClickThroughChanged;
        _onPomodoroMinutesChanged = onPomodoroMinutesChanged;
        _clearData = clearData;
        _exportJson = exportJson;
        _exportCsv = exportCsv;
        _openDataFolder = openDataFolder;
        _showDataUsage = showDataUsage;
        _onLlmChanged = onLlmChanged;
    }

    /// <summary>「应用与网页」块：当前前台卡片 + 用户规则列表。</summary>
    public AppRulesViewModel AppRules { get; }

    /// <summary>当前状态一句话，由感知循环回写。</summary>
    [ObservableProperty]
    private string _statusText = "芽芽正在观察……";

    [ObservableProperty]
    private bool _isMonitoringEnabled;

    [ObservableProperty]
    private bool _isNudgeEnabled;

    /// <summary>宠物窗显隐（与托盘/桌宠菜单双向同步；隐藏不退出进程）。</summary>
    [ObservableProperty]
    private bool _isPetVisible = true;

    /// <summary>每日轻问展示上限输入（1–10，非法值不回调）。</summary>
    [ObservableProperty]
    private string _nudgeDailyLimitText = "3";

    /// <summary>免打扰时段开关。</summary>
    [ObservableProperty]
    private bool _isQuietHoursEnabled = true;

    /// <summary>免打扰开始（HH:mm）。</summary>
    [ObservableProperty]
    private string _quietStartText = "23:00";

    /// <summary>免打扰结束（HH:mm）。</summary>
    [ObservableProperty]
    private string _quietEndText = "07:00";

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
    private bool _isDiagnosticLoggingEnabled;

    [ObservableProperty]
    private bool _isLlmEnabled;

    /// <summary>Base URL 展示值（不含 https:// 前缀，输入框自带）。</summary>
    [ObservableProperty]
    private string _llmBaseUrlText = string.Empty;

    [ObservableProperty]
    private string _llmModelText = string.Empty;

    /// <summary>api_key 仅内存流转（密码框不回显绑定）；日志绝不打印（红线）。</summary>
    private string _llmApiKey = string.Empty;
    private bool _suppressPomodoroCallback;

    internal string CurrentApiKey => _llmApiKey;

    /// <summary>
    /// 启动时一次性回填两个番茄时长框，期间抑制回调；避免「先设专注、休息还是默认值」
    /// 把用户自定义休息时长覆盖掉。回填完再统一校验回调一次。
    /// </summary>
    public void LoadPomodoroMinutes(int focusMinutes, int breakMinutes)
    {
        _suppressPomodoroCallback = true;
        try
        {
            FocusMinutesText = focusMinutes.ToString();
            BreakMinutesText = breakMinutes.ToString();
        }
        finally
        {
            _suppressPomodoroCallback = false;
        }

        NotifyPomodoroMinutes();
    }

    public void UpdateApiKey(string apiKey)
    {
        _llmApiKey = apiKey;
        NotifyLlmChanged();
    }

    partial void OnIsMonitoringEnabledChanged(bool value) => _onMonitoringChanged(value);

    partial void OnIsNudgeEnabledChanged(bool value) => _onNudgeChanged(value);

    partial void OnIsPetVisibleChanged(bool value) => _onPetVisibleChanged(value);

    partial void OnNudgeDailyLimitTextChanged(string value) => NotifyNudgeDisplay();

    partial void OnIsQuietHoursEnabledChanged(bool value) => NotifyNudgeDisplay();

    partial void OnQuietStartTextChanged(string value) => NotifyNudgeDisplay();

    partial void OnQuietEndTextChanged(string value) => NotifyNudgeDisplay();

    partial void OnIsClickThroughChanged(bool value) => _onClickThroughChanged(value);

    partial void OnFocusMinutesTextChanged(string value) => NotifyPomodoroMinutes();

    partial void OnBreakMinutesTextChanged(string value) => NotifyPomodoroMinutes();

    partial void OnIsDiagnosticLoggingEnabledChanged(bool value) => _onDiagnosticLoggingChanged(value);

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
        if (_suppressPomodoroCallback)
        {
            return;
        }

        if (int.TryParse(FocusMinutesText, out var focus) && int.TryParse(BreakMinutesText, out var breakMinutes)
            && focus is >= 1 and <= 120 && breakMinutes is >= 1 and <= 120)
        {
            _onPomodoroMinutesChanged(focus, breakMinutes);
        }
    }

    // 上限 1–10 且两个时刻都能按 HH:mm 解析时才回调（输入过程中不完整不触发）
    private void NotifyNudgeDisplay()
    {
        if (int.TryParse(NudgeDailyLimitText, out var limit) && limit is >= 1 and <= 10
            && TimeOnly.TryParseExact(QuietStartText.Trim(), "HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _)
            && TimeOnly.TryParseExact(QuietEndText.Trim(), "HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _))
        {
            _onNudgeDisplayChanged(limit, IsQuietHoursEnabled,
                QuietStartText.Trim(), QuietEndText.Trim());
        }
    }

    private void NotifyLlmChanged() =>
        _onLlmChanged(IsLlmEnabled, LlmBaseUrlText, LlmModelText, _llmApiKey);
}
