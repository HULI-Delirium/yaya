using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Hardcodet.Wpf.TaskbarNotification;
using Serilog;
using Yaya.App.Pet;
using Yaya.App.Services;
using Yaya.App.ViewModels;
using Yaya.Core.Monitoring;
using Yaya.Core.Services;
using Yaya.Core.Storage;

namespace Yaya.App;

public partial class App : Application
{
    // 数据只落在当前用户本机目录，不上传、不漫游
    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Yaya");

    private static readonly string DbPath = Path.Combine(DataDir, "yaya.db");
    private const string TrayIconResourceName = "Yaya.App.Assets.tray.ico";

    private const string DataUsageText =
        "芽芽的采集边界（隐私红线）\r\n\r\n" +
        "· 只读：前台窗口标题文本、前台进程名、键鼠空闲时长（系统级计数差）\r\n" +
        "· 绝不采集：屏幕内容、摄像头、麦克风、按键内容、进程路径/URL\r\n" +
        "· 数据全部保存在本机 %LOCALAPPDATA%\\Yaya（SQLite + 日志 + 设置），不上传不同步\r\n" +
        "· 一键清空即刻删空数据与日志；导出 JSON 会自动剔除 LLM api_key\r\n" +
        "· LLM 话术默认关闭；开启时也只发送 7 个聚合数字，不含任何行为明细\r\n\r\n" +
        "芽芽不做医疗/心理诊断，所有提醒仅供参考。";

    private TaskbarIcon? _trayIcon;
    private YayaDb? _db;
    private MonitoringLoop? _loop;
    private PomodoroService? _pomodoro;
    private MainViewModel? _viewModel;
    private PetAssets? _petAssets;
    private PetViewModel? _petViewModel;
    private PetWindow? _petWindow;
    private AppSettings? _settings;
    private RuleManager? _rules;
    private AppRulesViewModel? _appRules;
    private MenuItem? _trayToggleItem;
    private MenuItem? _trayPomodoroItem;
    private MenuItem? _trayPetVisibilityItem;
    private Window? _historyWindow;
    private bool _applyingPetVisible;
    private readonly HashSet<string> _suggestedProcesses = new(StringComparer.OrdinalIgnoreCase);

    // 历史窗数据窗口与条形图上限（App 层展示参数，不进 Core）
    private const int HistoryWindowDays = 14;
    private const int HistoryBarMaxWidth = 140;
    private const int HistoryFocusedDayThresholdMinutes = 30;   // 与 WeeklyReportOptions 默认一致

    // 周报当天内存缓存（重复打开同周不重复聚合；不建 weekly 表）
    private (DateOnly weekStart, WeeklyReportData data)? _weeklyCache;

    protected override void OnStartup(StartupEventArgs e)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                Path.Combine(DataDir, "logs", "yaya-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        try
        {
            Log.Information("芽芽启动");
            Directory.CreateDirectory(DataDir);

            _settings = AppSettings.Load(DataDir);

            _db = new YayaDb(DbPath);
            _db.Initialize();
            Log.Information("本地数据库就绪：{DbPath}", DbPath);

            _trayIcon = (TaskbarIcon)FindResource("TrayIcon");
            _trayIcon.IconSource = LoadTrayIcon();
            // 资源树中的 MenuItem 不生成字段：从 ContextMenu 命名范围查找
            _trayToggleItem = (MenuItem)_trayIcon.ContextMenu.FindName("TrayToggleMonitoringItem")!;
            _trayPomodoroItem = (MenuItem)_trayIcon.ContextMenu.FindName("TrayPomodoroItem")!;
            _trayPetVisibilityItem = (MenuItem)_trayIcon.ContextMenu.FindName("TrayPetVisibilityItem")!;
            Log.Information("托盘图标已加载（内嵌占位图标）");

            _pomodoro = new PomodoroService(() => _settings.FocusMinutes, () => _settings.BreakMinutes);
            _rules = new RuleManager(DataDir);
            _appRules = new AppRulesViewModel(_rules, ConfirmResetRules, OpenSettings);

            MonitoringLoop loop = null!;
            _viewModel = new MainViewModel(
                onMonitoringChanged: ApplyMonitoringEnabled,
                onNudgeChanged: ApplyNudgeEnabled,
                onPetVisibleChanged: ApplyPetVisible,
                onNudgeDisplayChanged: ApplyNudgeDisplay,
                onClickThroughChanged: ApplyClickThrough,
                onPomodoroMinutesChanged: ApplyPomodoroMinutes,
                clearData: ConfirmClearData,
                exportJson: ExportJson,
                exportCsv: ExportCsv,
                openDataFolder: OpenDataFolder,
                showDataUsage: ShowDataUsage,
                onLlmChanged: ApplyLlmConfig,
                onDiagnosticLoggingChanged: ApplyDiagnosticLogging,
                appRules: _appRules);
            loop = new MonitoringLoop(_db, _viewModel, () => _pomodoro.CurrentPhase,
                new AppClassifier(_rules.Current));
            loop.UpdateDiagnosticLogging(_settings.DiagnosticLogging);
            _loop = loop;

            // 规则变更 → 热替换分类器；分类变化 → 设置页当前应用卡片
            _rules.Changed += (_, _) =>
            {
                _loop?.UpdateClassifier(new AppClassifier(_rules.Current));
                Log.Information("分类规则已更新：{Count} 条", _rules.Current.Count);
            };
            loop.ClassificationChanged += (_, result) =>
            {
                _appRules.UpdateCurrent(result, loop.LastProcessName, loop.LastIsBrowserHost);
                if (_petWindow?.IsWhyOpen == true)
                {
                    _petWindow.UpdateWhy(BuildWhyInfo());
                }
            };

            _petAssets = new PetAssets();
            _petViewModel = new PetViewModel(_petAssets,
                note =>
                {
                    loop.ReportValid(note);
                    SuggestRuleAfterValid();
                },
                note => loop.ReportEscape(note));
            _petWindow = new PetWindow(_petViewModel);

            // Uncertain 角标每轮刷新（初始就是 Uncertain 也能出现）
            loop.StateEvaluated += (_, state) =>
            {
                _petViewModel.SetUncertain(state == FocusState.Uncertain);
                if (_petWindow.IsWhyOpen)
                {
                    _petWindow.UpdateWhy(BuildWhyInfo());
                }
            };
            _petWindow.WhyRequested += (_, _) => _petWindow.ShowWhy(BuildWhyInfo());

            // 桌宠菜单只抛事件，具体行为统一在 App 层（与托盘菜单共用同一套方法）
            _petWindow.TogglePomodoroRequested += (_, _) => TogglePomodoro();
            _petWindow.DailyReportRequested += (_, _) => OpenDailyReport();
            _petWindow.WeeklyReportRequested += (_, _) => OpenWeeklyReport();
            _petWindow.HistoryRequested += (_, _) => OpenHistory();
            _petWindow.SettingsRequested += (_, _) => OpenSettings();
            _petWindow.SetCurrentCategoryRequested += (_, category) => ApplyCurrentCategory(category);
            _petWindow.HidePetRequested += (_, _) => SetPetVisible(false);
            _petWindow.ExitRequested += (_, _) => Shutdown();

            loop.FocusTransitionOccurred += (_, t) => _petViewModel.OnFocusTransition(t);
            loop.NudgeRequested += (_, _) =>
            {
                // 展示门控（App 层）：提醒总开关 → 免打扰时段 → 每日上限；
                // 只过滤展示，Core 的冷却与判定完全不受影响
                if (_settings!.NudgeEnabled && TryReserveNudgeDisplay())
                {
                    _petViewModel!.OnNudgeRequested();
                }
            };

            // 番茄钟接线：相位 → 宠物 Relaxed；完成/中断 → events 表 + 托盘提示
            _pomodoro.PhaseChanged += phase =>
            {
                _petViewModel!.SetRelaxed(phase == PomodoroPhase.Break);
                RefreshPomodoroUi();
                Log.Information("番茄相位 -> {Phase}", phase);
            };
            // 每秒刷新状态条/托盘剩余时间（仅 UI 文案，不落库）
            _pomodoro.RemainingChanged += _ => RefreshPomodoroUi();
            _pomodoro.FocusCompleted += () =>
            {
                _db!.RecordEvent(DateTimeOffset.UtcNow, AppCategory.Unknown, loop.SessionId, "PomodoroFocusCompleted", 0);
                _trayIcon?.ShowBalloonTip("芽芽", $"专注完成，休息 {_settings!.BreakMinutes} 分钟～", BalloonIcon.Info);
                Log.Information("番茄：专注完成");
            };
            _pomodoro.BreakCompleted += () =>
            {
                _db!.RecordEvent(DateTimeOffset.UtcNow, AppCategory.Unknown, loop.SessionId, "PomodoroBreakCompleted", 0);
                _trayIcon?.ShowBalloonTip("芽芽", "休息结束，该回来啦。", BalloonIcon.Info);
                Log.Information("番茄：休息完成");
            };
            _pomodoro.Interrupted += () =>
            {
                _db!.RecordEvent(DateTimeOffset.UtcNow, AppCategory.Unknown, loop.SessionId, "PomodoroAborted", 0);
                Log.Information("番茄：中断");
            };

            MainWindow = new MainWindow { DataContext = _viewModel };

            // 设置初值（会触发回调完成应用与落盘；Start 幂等，穿透在 Show 后补句柄）
            _viewModel.IsMonitoringEnabled = _settings.MonitoringEnabled;
            _viewModel.IsNudgeEnabled = _settings.NudgeEnabled;
            _viewModel.IsPetVisible = _settings.PetVisible;
            _viewModel.NudgeDailyLimitText = _settings.NudgeDailyLimit.ToString();
            _viewModel.IsQuietHoursEnabled = _settings.QuietHoursEnabled;
            _viewModel.QuietStartText = _settings.QuietStart;
            _viewModel.QuietEndText = _settings.QuietEnd;
            _viewModel.IsClickThrough = _settings.ClickThrough;
            _viewModel.LoadPomodoroMinutes(_settings.FocusMinutes, _settings.BreakMinutes);
            _viewModel.DataDirText = DataDir;
            _viewModel.IsLlmEnabled = _settings.Llm.Enabled;
            _viewModel.LlmBaseUrlText = _settings.Llm.BaseUrl.Replace("https://", string.Empty);
            _viewModel.LlmModelText = _settings.Llm.Model;
            _viewModel.IsDiagnosticLoggingEnabled = _settings.DiagnosticLogging;
            _viewModel.UpdateApiKey(_settings.Llm.ApiKey);   // 回填密码框（等量保存一次，无害）

            // 宠物显隐持久化：隐藏后进程与采集继续，托盘/设置可恢复（低打扰红线的一部分）
            if (_settings.PetVisible)
            {
                _petWindow.Show();
            }

            _petWindow.SetClickThrough(_settings.ClickThrough);
            RefreshPomodoroUi();

            // 首启引导与当日复盘都等首帧后再弹，避免阻塞启动；顺序固定：欢迎 → 复盘
            Dispatcher.BeginInvoke(new Action(OnFirstRunAndDailyReport));

            base.OnStartup(e);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "启动失败");
            _ = MessageBox.Show("芽芽启动失败，详见日志。\r\n" + ex.Message, "芽芽",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // 依序释放：停轮询（写退出事件行）与退订事件 → 宠物定时器 → 番茄钟 → 托盘 → 数据库 → 冲刷日志
        _loop?.Dispose();
        _petViewModel?.Dispose();
        _pomodoro?.Dispose();
        _trayIcon?.Dispose();
        _db?.Dispose();
        Log.Information("芽芽退出");
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    // ---------- 设置项应用 ----------

    private void ApplyMonitoringEnabled(bool enabled)
    {
        if (_settings is null || _loop is null)
        {
            return;
        }

        _settings.MonitoringEnabled = enabled;
        _settings.Save(DataDir);
        if (enabled)
        {
            _loop.Start();
        }
        else
        {
            _loop.Stop();
        }

        TrayToggleMonitoringItem_Header(enabled);
        Log.Information("采集开关 -> {Enabled}", enabled);
    }

    private void TrayToggleMonitoringItem_Header(bool monitoringEnabled)
    {
        if (_trayToggleItem is not null)
        {
            _trayToggleItem.Header = monitoringEnabled ? "暂停采集" : "开始采集";
        }
    }

    private void ApplyNudgeEnabled(bool enabled)
    {
        if (_settings is null)
        {
            return;
        }

        _settings.NudgeEnabled = enabled;
        _settings.Save(DataDir);
        Log.Information("提醒开关 -> {Enabled}", enabled);
    }

    // 设置页 ↔ 托盘/桌宠菜单双向同步；重入保护在 SetPetVisible 内（幂等）
    private void ApplyPetVisible(bool visible) => SetPetVisible(visible);

    private void ApplyNudgeDisplay(int dailyLimit, bool quietEnabled, string quietStart, string quietEnd)
    {
        if (_settings is null)
        {
            return;
        }

        _settings.NudgeDailyLimit = Math.Clamp(dailyLimit, 1, 10);
        _settings.QuietHoursEnabled = quietEnabled;
        _settings.QuietStart = quietStart;
        _settings.QuietEnd = quietEnd;
        _settings.Save(DataDir);
        Log.Information("提醒门控 -> 每日上限 {Limit}，免打扰 {Enabled}（{Start}–{End}）",
            _settings.NudgeDailyLimit, quietEnabled, quietStart, quietEnd);
    }

    private void ApplyClickThrough(bool enabled)
    {
        if (_settings is null)
        {
            return;
        }

        _settings.ClickThrough = enabled;
        _settings.Save(DataDir);
        _petWindow?.SetClickThrough(enabled);
        if (enabled && _petWindow?.IsVisible == true)
        {
            // 用户主动开启时才提示（启动恢复配置不打扰）：穿透后菜单不可用，必须先讲清楚
            _trayIcon?.ShowBalloonTip("芽芽", "已开启鼠标穿透：宠物菜单不可用，可在设置里关闭。", BalloonIcon.Info);
        }

        Log.Information("鼠标穿透 -> {Enabled}", enabled);
    }

    private void ApplyPomodoroMinutes(int focusMinutes, int breakMinutes)
    {
        if (_settings is null)
        {
            return;
        }

        _settings.FocusMinutes = focusMinutes;
        _settings.BreakMinutes = breakMinutes;
        _settings.Save(DataDir);
        RefreshPomodoroUi();   // 空闲时菜单「开始专注 N 分钟」跟随新时长
        Log.Information("番茄时长 -> 专注 {Focus}min / 休息 {Break}min", focusMinutes, breakMinutes);
    }

    private void ApplyDiagnosticLogging(bool enabled)
    {
        if (_settings is null)
        {
            return;
        }

        _settings.DiagnosticLogging = enabled;
        _settings.Save(DataDir);
        _loop?.UpdateDiagnosticLogging(enabled);
        Log.Information("诊断日志 -> {Enabled}", enabled);
    }

    // 红线：日志只记 enabled/base/model，绝不记 api_key
    private void ApplyLlmConfig(bool enabled, string baseUrlText, string model, string apiKey)
    {
        if (_settings is null)
        {
            return;
        }

        var normalized = baseUrlText.Trim();
        if (normalized.Length > 0 && !normalized.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "https://" + normalized;
        }

        _settings.Llm = _settings.Llm with
        {
            Enabled = enabled,
            BaseUrl = normalized,
            Model = model.Trim(),
            ApiKey = apiKey,
        };
        _settings.Save(DataDir);
        Log.Information("LLM 设置 -> enabled={Enabled}, base={BaseUrl}, model={Model}",
            enabled, normalized, model.Trim());
    }

    // ---------- 数据与隐私 ----------

    private void ShowDataUsage() =>
        MessageBox.Show(DataUsageText, "芽芽 · 数据用途说明",
            MessageBoxButton.OK, MessageBoxImage.Information);

    private void OpenDataFolder()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = DataDir,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "打开数据文件夹失败");
        }
    }

    private void ExportJson()
    {
        if (_db is null || _settings is null)
        {
            return;
        }

        try
        {
            var path = DataExporter.WriteToFile(DataExporter.BuildJson(_db, _settings, _rules?.Current), DataDir, "json");
            _trayIcon?.ShowBalloonTip("芽芽", $"已导出 JSON（api_key 已剔除）：\r\n{path}", BalloonIcon.Info);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "JSON 导出失败");
            MessageBox.Show("导出失败，详见日志。", "芽芽", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ExportCsv()
    {
        if (_db is null)
        {
            return;
        }

        try
        {
            var path = DataExporter.WriteToFile(DataExporter.BuildDailySummaryCsv(_db), DataDir, "csv");
            _trayIcon?.ShowBalloonTip("芽芽", $"已导出 CSV：\r\n{path}", BalloonIcon.Info);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "CSV 导出失败");
            MessageBox.Show("导出失败，详见日志。", "芽芽", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---------- 托盘菜单 ----------

    private void TrayMenuToggleMonitoring(object sender, RoutedEventArgs e)
    {
        if (_settings is null)
        {
            return;
        }

        ApplyMonitoringEnabled(!_settings.MonitoringEnabled);
    }

    private void TrayMenuTogglePomodoro(object sender, RoutedEventArgs e) => TogglePomodoro();

    private void TrayMenuShowSettings(object sender, RoutedEventArgs e) => OpenSettings();

    private void TrayMenuShowReport(object sender, RoutedEventArgs e) => OpenDailyReport();

    private void TrayMenuShowWeeklyReport(object sender, RoutedEventArgs e) => OpenWeeklyReport();

    private void TrayMenuShowHistory(object sender, RoutedEventArgs e) => OpenHistory();

    private void TrayMenuTogglePetVisibility(object sender, RoutedEventArgs e)
    {
        if (_settings is null)
        {
            return;
        }

        SetPetVisible(!_settings.PetVisible);
    }

    private void TrayMenuClearData(object sender, RoutedEventArgs e) => ConfirmClearData();

    private void TrayMenuExit(object sender, RoutedEventArgs e) => Shutdown();

    private void OpenSettings()
    {
        if (MainWindow is not { } window)
        {
            window = new MainWindow { DataContext = _viewModel };
            MainWindow = window;
        }

        window.Show();
        window.Activate();
    }

    // ---------- 分类规则（设置页 / 桌宠菜单共用） ----------

    private void ApplyCurrentCategory(RuleCategory category)
    {
        if (_loop is null || _appRules is null || _rules is null)
        {
            return;
        }

        var process = _loop.LastProcessName;
        if (string.IsNullOrWhiteSpace(process))
        {
            _trayIcon?.ShowBalloonTip("芽芽", "还没识别到前台应用。", BalloonIcon.Info);
            return;
        }

        if (_loop.LastIsBrowserHost)
        {
            // 浏览器不能建进程规则（会把所有网页设成同一类），改为标题规则
            _appRules.PrefillTitleRule(_loop.LastClassification.MatchedSite);
            _trayIcon?.ShowBalloonTip("芽芽", "浏览器窗口请用标题规则，已在设置里预填。", BalloonIcon.Info);
            return;
        }

        _rules.AddProcessRule(process, category);
        _appRules.Reload();
        _trayIcon?.ShowBalloonTip("芽芽", $"已设为{CategoryText(category)}：{process}", BalloonIcon.Info);
        Log.Information("一键设置分类：{ProcessName} -> {Category}", process, category);
    }

    private void ConfirmResetRules()
    {
        var result = MessageBox.Show("确定清空全部自定义规则？内置表不受影响。", "芽芽",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _rules?.ResetToDefault();
        _appRules?.Reload();
        Log.Information("用户恢复默认分类规则");
    }

    private static string CategoryText(RuleCategory category) => category switch
    {
        RuleCategory.Study => "学习",
        RuleCategory.Utility => "工具",
        RuleCategory.Entertainment => "娱乐",
        RuleCategory.Social => "社交",
        _ => "不打扰",
    };

    // 纠正为「有效查资料」后，只在非浏览器、娱乐/社交、且该进程无规则时建议一次
    private void SuggestRuleAfterValid()
    {
        if (_petViewModel is null || _rules is null || _loop is null)
        {
            return;
        }

        var result = _loop.LastClassification;
        var process = _loop.LastProcessName;
        if (string.IsNullOrWhiteSpace(process) || _loop.LastIsBrowserHost)
        {
            return;
        }

        if (result.Category is not (AppCategory.Entertainment or AppCategory.Social))
        {
            return;
        }

        if (result.IsMuted || _rules.HasProcessRule(process) || !_suggestedProcesses.Add(process))
        {
            return;
        }

        _petViewModel.ShowRuleSuggestion(
            $"刚才那个是 {process}。\r\n以后把 {process} 算作学习？",
            () =>
            {
                _rules.AddProcessRule(process, RuleCategory.Study);
                _appRules?.Reload();
                _trayIcon?.ShowBalloonTip("芽芽", $"已设为学习：{process}", BalloonIcon.Info);
                Log.Information("接受纠错建议：{ProcessName} -> Study", process);
            });
    }

    /// <summary>「为什么」浮层快照：进程名/类别/判定/原因，不含整条标题。</summary>
    private WhyInfo BuildWhyInfo()
    {
        var result = _loop?.LastClassification ?? ClassificationResult.Unknown("尚未采集");
        var process = _loop?.LastProcessName ?? string.Empty;
        var browser = _loop?.LastIsBrowserHost == true;
        var state = _loop?.CurrentState ?? FocusState.Uncertain;
        var stateText = state switch
        {
            FocusState.Focused => "专注",
            FocusState.Distracted => "游离",
            _ => "不确定 — 不打扰",
        };
        var hasProcess = !string.IsNullOrWhiteSpace(process);
        var processText = hasProcess
            ? browser
                ? $"浏览器窗口（{result.MatchedSite ?? "未命中站点"}）"
                : process
            : "未识别到前台应用";
        var hint = !hasProcess
            ? "还没识别到前台应用，等等再看。"
            : browser
                ? "浏览器窗口请用标题规则（设置里添加站点词）。"
                : string.Empty;

        return new WhyInfo(
            processText,
            AppRulesViewModel.DescribeCategory(result),
            stateText,
            result.Reason,
            hint,
            hasProcess && !browser);
    }

    // ---------- 番茄控制（桌宠菜单 / 托盘共用） ----------

    /// <summary>开始/停止番茄：唯一入口收敛在这里，避免两套菜单行为分叉。</summary>
    private void TogglePomodoro()
    {
        if (_pomodoro is null)
        {
            return;
        }

        if (_pomodoro.CurrentPhase == PomodoroPhase.None)
        {
            StartPomodoro();
            return;
        }

        _pomodoro.Stop();   // 服务内部写 PomodoroAborted 事件（经 Interrupted 订阅）
        Log.Information("番茄：手动停止");
    }

    private void StartPomodoro()
    {
        if (_pomodoro is null || _settings is null)
        {
            return;
        }

        _pomodoro.StartFocus();
        Log.Information("番茄：开始（{Minutes} 分钟）", _settings.FocusMinutes);
    }

    /// <summary>刷新状态条、托盘提示与两份菜单标题；每秒由 RemainingChanged 调用。</summary>
    private void RefreshPomodoroUi()
    {
        if (_settings is null || _pomodoro is null)
        {
            return;
        }

        var phase = _pomodoro.CurrentPhase;
        var remaining = phase == PomodoroPhase.None ? TimeSpan.Zero : _pomodoro.Remaining;
        _petViewModel?.SetPomodoroStatus(phase, remaining);

        var header = phase switch
        {
            PomodoroPhase.Focus => $"停止专注 {FormatRemaining(remaining)}",
            PomodoroPhase.Break => $"停止休息 {FormatRemaining(remaining)}",
            _ => $"开始专注 {_settings.FocusMinutes} 分钟",
        };
        if (_trayPomodoroItem is not null)
        {
            _trayPomodoroItem.Header = header;
        }

        _petWindow?.UpdatePomodoroMenuHeader(header);
        UpdateTrayTooltip();
    }

    private static string FormatRemaining(TimeSpan remaining)
    {
        var seconds = Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds));
        return $"{seconds / 60:00}:{seconds % 60:00}";
    }

    /// <summary>托盘提示：番茄进行中给剩余时间；空闲且宠物隐藏时明确指路。</summary>
    private void UpdateTrayTooltip()
    {
        if (_trayIcon is null || _settings is null || _pomodoro is null)
        {
            return;
        }

        _trayIcon.ToolTipText = _pomodoro.CurrentPhase switch
        {
            PomodoroPhase.Focus => $"芽芽 · 专注 {FormatRemaining(_pomodoro.Remaining)}",
            PomodoroPhase.Break => $"芽芽 · 休息 {FormatRemaining(_pomodoro.Remaining)}",
            _ => _settings.PetVisible ? "芽芽 · 学习节律陪伴" : "芽芽 · 宠物已隐藏（托盘菜单可恢复）",
        };
    }

    // ---------- 宠物显隐 ----------

    private void SetPetVisible(bool visible)
    {
        if (_settings is null || _petWindow is null || _applyingPetVisible)
        {
            return;
        }

        _applyingPetVisible = true;
        try
        {
            _settings.PetVisible = visible;
            _settings.Save(DataDir);
            if (visible)
            {
                _petWindow.Show();
                _petWindow.Activate();
            }
            else
            {
                _petWindow.Hide();
            }

            // 设置页勾选同步（进入本方法时已置重入标记，回调不会再套一层）
            if (_viewModel is not null && _viewModel.IsPetVisible != visible)
            {
                _viewModel.IsPetVisible = visible;
            }

            if (_trayPetVisibilityItem is not null)
            {
                _trayPetVisibilityItem.Header = visible ? "隐藏宠物" : "显示宠物";
            }

            _petWindow.UpdatePetVisibilityMenuHeader(visible);
            UpdateTrayTooltip();
            Log.Information("宠物显示 -> {Visible}", visible);
        }
        finally
        {
            _applyingPetVisible = false;
        }
    }

    // ---------- 轻问展示门控（App 层，Core 不动） ----------

    /// <summary>
    /// 免打扰时段一律不展示；每日展示次数按本地日期计数并持久化（重启不重置）。
    /// 返回 false 只代表「这次不展示」，Core 的冷却与判定照常推进。
    /// </summary>
    private bool TryReserveNudgeDisplay()
    {
        if (_settings is null)
        {
            return false;
        }

        var now = DateTimeOffset.Now;
        if (IsInQuietHours(TimeOnly.FromDateTime(now.LocalDateTime)))
        {
            Log.Information("轻问未展示：处于免打扰时段");
            return false;
        }

        var today = now.LocalDateTime.ToString("yyyy-MM-dd");
        if (_settings.NudgeCountDate != today)
        {
            _settings.NudgeCountDate = today;
            _settings.NudgeCountToday = 0;
        }

        var limit = Math.Clamp(_settings.NudgeDailyLimit, 1, 10);
        if (_settings.NudgeCountToday >= limit)
        {
            Log.Information("轻问未展示：已达每日上限 {Limit}", limit);
            return false;
        }

        _settings.NudgeCountToday++;
        _settings.Save(DataDir);
        Log.Information("轻问展示计数 {Count}/{Limit}", _settings.NudgeCountToday, limit);
        return true;
    }

    private bool IsInQuietHours(TimeOnly now)
    {
        if (_settings is null || !_settings.QuietHoursEnabled)
        {
            return false;
        }

        if (!TimeOnly.TryParseExact(_settings.QuietStart, "HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var start)
            || !TimeOnly.TryParseExact(_settings.QuietEnd, "HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var end))
        {
            return false;   // 配置损坏按不免打扰处理，绝不因设置问题阻塞提醒
        }

        // 支持跨零点（默认 23:00–07:00）
        return start <= end
            ? now >= start && now < end
            : now >= start || now < end;
    }

    // ---------- 首启引导 ----------

    private void OnFirstRunAndDailyReport()
    {
        if (_settings is null || _db is null)
        {
            return;
        }

        if (!_settings.HasCompletedOnboarding)
        {
            ShowWelcome();
        }

        // 每天首次启动（当日复盘尚未生成）自动弹出；已有缓存则安静，走托盘可再看
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (!_db.TryGetDailyReport(today, out _, out _))
        {
            OpenDailyReport();
        }
    }

    private void ShowWelcome()
    {
        if (_settings is null)
        {
            return;
        }

        var welcome = new WelcomeWindow(DataUsageText);
        welcome.ShowDialog();
        _settings.HasCompletedOnboarding = true;
        _settings.Save(DataDir);
        Log.Information("首次引导已完成（开始第一次专注：{Start}）", welcome.StartFocusRequested);
        if (welcome.StartFocusRequested)
        {
            StartPomodoro();
        }
    }

    /// <summary>一键清空：二次确认后删空数据库 + 日志；设置（番茄时长/开关/LLM 配置）保留（产品红线）。</summary>
    private void ConfirmClearData()
    {
        var result = MessageBox.Show("确定清空全部本地数据与日志？设置会保留。此操作不可恢复。", "芽芽",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _db?.ClearAllData();
        ClearLogFiles();
        Log.Warning("用户一键清空本地数据（设置保留）");
        _trayIcon?.ShowBalloonTip("芽芽", "本地数据与日志已清空。", BalloonIcon.Info);
    }

    // 清空范围扩展：yaya.db + logs；settings.json 保留（用户偏好，非采集数据）
    private void ClearLogFiles()
    {
        try
        {
            var logsDir = Path.Combine(DataDir, "logs");
            if (!Directory.Exists(logsDir))
            {
                return;
            }

            foreach (var file in Directory.GetFiles(logsDir, "yaya-*.log"))
            {
                File.Delete(file);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "日志文件清理失败（不影响数据库清空结果）");
        }
    }

    // ---------- 每日复盘 ----------

    /// <summary>打开今日复盘：当天已缓存直接读；未生成则聚合落库（同日不重复聚合）。</summary>
    private void OpenDailyReport()
    {
        if (_db is null || _pomodoro is null || _settings is null)
        {
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        if (!_db.TryGetDailyReport(today, out var data, out var summary) || data is null || summary is null)
        {
            var events = _db.GetEventsForDate(today, TimeZoneInfo.Local);
            var corrections = _db.GetCorrectionsForDate(today, TimeZoneInfo.Local);
            data = DailyReportAggregator.Aggregate(events, corrections, today, TimeZoneInfo.Local);
            // LLM 开关即时生效：仅影响"当日首次生成"的 writer 选择；当天已冻结的报告不回头重写
            summary = ReportWriterFactory.Create(_settings.Llm).Write(data);
            _db.SaveDailyReport(today, data, summary);
            Log.Information("复盘已聚合并落库：专注 {Minutes}min，最强 {Peak}，话术源 {Writer}",
                data.FocusedMinutes, data.PeakWindowStart,
                _settings.Llm.IsEffectivelyEnabled ? "llm" : "rule");
        }

        // 展示文本按「。」拆三行（写出器硬约束恰好三句）
        var lines = summary.Split('。', StringSplitOptions.RemoveEmptyEntries);
        var window = new ReportWindow();
        window.DataContext = new ReportViewModel(
            lines.Length > 0 ? lines[0] + "。" : summary,
            lines.Length > 1 ? lines[1] + "。" : string.Empty,
            lines.Length > 2 ? lines[2] + "。" : string.Empty,
            data.AdviceMinutes,
            minutes =>
            {
                // 单次时长覆盖：只影响本个番茄，不回写用户默认设置
                _pomodoro!.StartFocus(minutes);
                Log.Information("明天开始：以 {Minutes} 分钟启动番茄", minutes);
                window.Close();
            });
        window.Show();
        window.Activate();
    }

    // ---------- 专注记录（近 14 天，只读） ----------

    private void OpenHistory()
    {
        if (_db is null)
        {
            return;
        }

        if (_historyWindow is { IsVisible: true })
        {
            _historyWindow.Activate();
            return;
        }

        var window = new HistoryWindow();
        window.DataContext = BuildHistoryViewModel(() =>
        {
            StartPomodoro();
            window.Close();
        });
        window.Closed += (_, _) => _historyWindow = null;
        _historyWindow = window;
        window.Show();
        window.Activate();
    }

    /// <summary>
    /// 近 14 天视图模型：daily_summary 缓存优先；无缓存但有 events 的日期现算；两者都没有留空。
    /// 只读，不落库、不改 Core；每行只含聚合数字与本地复盘首句（不含窗口标题/进程名）。
    /// </summary>
    private HistoryViewModel BuildHistoryViewModel(Action onStartFocus)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var days = new List<(DateOnly Day, DailyReportData? Data, string? Summary)>(HistoryWindowDays);
        for (var i = HistoryWindowDays - 1; i >= 0; i--)   // 旧→新，便于算连续天数
        {
            var day = today.AddDays(-i);
            DailyReportData? data = null;
            string? summary = null;
            if (_db!.TryGetDailyReport(day, out var cached, out var cachedText) && cached is not null)
            {
                data = cached;
                summary = cachedText;
            }
            else
            {
                var events = _db.GetEventsForDate(day, TimeZoneInfo.Local);
                var corrections = _db.GetCorrectionsForDate(day, TimeZoneInfo.Local);
                if (events.Count > 0 || corrections.Count > 0)
                {
                    data = DailyReportAggregator.Aggregate(events, corrections, day, TimeZoneInfo.Local);
                }
            }

            days.Add((day, data, summary));
        }

        var maxMinutes = days.Max(d => d.Data?.FocusedMinutes ?? 0);
        var rows = new List<HistoryDayRow>(days.Count);
        for (var i = days.Count - 1; i >= 0; i--)   // 展示新→旧
        {
            var (day, data, summary) = days[i];
            var minutes = data?.FocusedMinutes ?? 0;
            rows.Add(new HistoryDayRow(
                $"{day:MM-dd} {WeekdayLabel(day)}",
                maxMinutes == 0 || minutes == 0
                    ? 0
                    : Math.Max(4, (int)Math.Round(HistoryBarMaxWidth * minutes / (double)maxMinutes)),
                data is null ? "—" : $"{minutes} 分",
                data is null ? "—" : $"纠正 {data.CorrectionCount} 次",
                BuildHistoryNote(data, summary),
                data is null));
        }

        var total = days.Sum(d => d.Data?.FocusedMinutes ?? 0);
        var weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));   // 周一
        var weekTotal = days.Where(d => d.Day >= weekStart).Sum(d => d.Data?.FocusedMinutes ?? 0);

        var streak = 0;
        var current = 0;
        foreach (var (_, data, _) in days)   // 旧→新
        {
            if ((data?.FocusedMinutes ?? 0) >= HistoryFocusedDayThresholdMinutes)
            {
                current++;
                streak = Math.Max(streak, current);
            }
            else
            {
                current = 0;
            }
        }

        var average = (int)Math.Round(total / (double)HistoryWindowDays);
        var isEmpty = days.All(d => d.Data is null);
        return new HistoryViewModel(
            rows,
            $"本周合计 {weekTotal} 分钟",
            $"近 14 天日均 {average} 分钟",
            $"最长连续专注 {streak} 天",
            isEmpty,
            onStartFocus);
    }

    private static string WeekdayLabel(DateOnly day) => "周" + "日一二三四五六"[(int)day.DayOfWeek];

    private static string BuildHistoryNote(DailyReportData? data, string? summary)
    {
        if (!string.IsNullOrWhiteSpace(summary))
        {
            var end = summary.IndexOf('。');
            var line = end > 0 ? summary[..(end + 1)] : summary;
            return line.Length > 40 ? line[..40] + "…" : line;
        }

        if (data is null)
        {
            return "无记录";
        }

        return data.PeakWindowStart is { } start && data.PeakWindowEnd is { } peakEnd
            ? $"最强的一段 {start.ToString(@"hh\:mm")}–{peakEnd.ToString(@"hh\:mm")}"
            : "有记录，暂无文字复盘";
    }

    // ---------- 本周报告 ----------

    private void OpenWeeklyReport()
    {
        if (_db is null || _petAssets is null)
        {
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        var weekStart = today.AddDays(-((int)today.DayOfWeek + 6) % 7);   // 周一
        IReadOnlyList<DailyReportData?> reports;
        if (_weeklyCache is { } cached && cached.weekStart == weekStart)
        {
            reports = cachedReports!;
        }
        else
        {
            reports = BuildWeekReports(weekStart);
            var events = _db.GetAllEvents();
            var data = WeeklyReportAggregator.Aggregate(events, reports, weekStart, TimeZoneInfo.Local);
            _weeklyCache = (weekStart, data);
            cachedReports = reports;
        }

        var window = new WeeklyReportWindow
        {
            DataContext = new WeekReportViewModel(_weeklyCache.Value.data, reports, today, weekStart, _petAssets),
        };
        window.Show();
        window.Activate();
    }

    private IReadOnlyList<DailyReportData?>? cachedReports;

    // 7 天日聚合：有当日缓存（daily_summary.report_json）直接用，缺失的现场聚合
    private IReadOnlyList<DailyReportData?> BuildWeekReports(DateOnly weekStart)
    {
        var reports = new List<DailyReportData?>(7);
        for (var i = 0; i < 7; i++)
        {
            var day = weekStart.AddDays(i);
            if (_db!.TryGetDailyReport(day, out var cached, out _) && cached is not null)
            {
                reports.Add(cached);
                continue;
            }

            var events = _db.GetEventsForDate(day, TimeZoneInfo.Local);
            var corrections = _db.GetCorrectionsForDate(day, TimeZoneInfo.Local);
            reports.Add(events.Count == 0 && corrections.Count == 0
                ? null
                : DailyReportAggregator.Aggregate(events, corrections, day, TimeZoneInfo.Local));
        }

        return reports;
    }

    // ---------- 内嵌托盘图标 ----------

    private static BitmapSource LoadTrayIcon()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(TrayIconResourceName)
            ?? throw new InvalidOperationException($"找不到内嵌资源 {TrayIconResourceName}，请检查 Assets\\tray.ico 是否标记为 EmbeddedResource。");

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.StreamSource = stream;
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
