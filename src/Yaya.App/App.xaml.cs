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
    private MenuItem? _trayToggleItem;

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
            Log.Information("托盘图标已加载（内嵌占位图标）");

            _pomodoro = new PomodoroService(() => _settings.FocusMinutes, () => _settings.BreakMinutes);

            MonitoringLoop loop = null!;
            _viewModel = new MainViewModel(
                onMonitoringChanged: ApplyMonitoringEnabled,
                onNudgeChanged: ApplyNudgeEnabled,
                onClickThroughChanged: ApplyClickThrough,
                onPomodoroMinutesChanged: ApplyPomodoroMinutes,
                clearData: ConfirmClearData,
                exportJson: ExportJson,
                exportCsv: ExportCsv,
                openDataFolder: OpenDataFolder,
                showDataUsage: ShowDataUsage,
                onLlmChanged: ApplyLlmConfig);
            loop = new MonitoringLoop(_db, _viewModel, () => _pomodoro.CurrentPhase);
            _loop = loop;

            _petAssets = new PetAssets();
            _petViewModel = new PetViewModel(_petAssets,
                note => loop.ReportValid(note),
                note => loop.ReportEscape(note));
            _petWindow = new PetWindow(_petViewModel);

            loop.FocusTransitionOccurred += (_, t) => _petViewModel.OnFocusTransition(t);
            loop.NudgeRequested += (_, _) =>
            {
                // 提醒总开关只过滤展示；Core 的冷却与计数不受影响
                if (_settings!.NudgeEnabled)
                {
                    _petViewModel!.OnNudgeRequested();
                }
            };

            // 番茄钟接线：相位 → 宠物 Relaxed；完成/中断 → events 表 + 托盘提示
            _pomodoro.PhaseChanged += phase =>
            {
                _petViewModel!.SetRelaxed(phase == PomodoroPhase.Break);
                Log.Information("番茄相位 -> {Phase}", phase);
            };
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
            _viewModel.IsClickThrough = _settings.ClickThrough;
            _viewModel.FocusMinutesText = _settings.FocusMinutes.ToString();
            _viewModel.BreakMinutesText = _settings.BreakMinutes.ToString();
            _viewModel.DataDirText = DataDir;
            _viewModel.IsLlmEnabled = _settings.Llm.Enabled;
            _viewModel.LlmBaseUrlText = _settings.Llm.BaseUrl.Replace("https://", string.Empty);
            _viewModel.LlmModelText = _settings.Llm.Model;
            _viewModel.UpdateApiKey(_settings.Llm.ApiKey);   // 回填密码框（等量保存一次，无害）

            _petWindow.Show();
            _petWindow.SetClickThrough(_settings.ClickThrough);

            // 每天首次启动（当日复盘尚未生成）自动弹出；已有缓存则安静，走托盘可再看
            var today = DateOnly.FromDateTime(DateTime.Now);
            if (!_db.TryGetDailyReport(today, out _, out _))
            {
                Dispatcher.BeginInvoke(new Action(OpenDailyReport));
            }

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

    private void ApplyClickThrough(bool enabled)
    {
        if (_settings is null)
        {
            return;
        }

        _settings.ClickThrough = enabled;
        _settings.Save(DataDir);
        _petWindow?.SetClickThrough(enabled);
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
        Log.Information("番茄时长 -> 专注 {Focus}min / 休息 {Break}min", focusMinutes, breakMinutes);
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
            var path = DataExporter.WriteToFile(DataExporter.BuildJson(_db, _settings), DataDir, "json");
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

    private void TrayMenuShowSettings(object sender, RoutedEventArgs e)
    {
        if (MainWindow is not { } window)
        {
            window = new MainWindow { DataContext = _viewModel };
            MainWindow = window;
        }

        window.Show();
        window.Activate();
    }

    private void TrayMenuShowReport(object sender, RoutedEventArgs e) => OpenDailyReport();

    private void TrayMenuShowWeeklyReport(object sender, RoutedEventArgs e) => OpenWeeklyReport();

    private void TrayMenuClearData(object sender, RoutedEventArgs e) => ConfirmClearData();

    private void TrayMenuExit(object sender, RoutedEventArgs e) => Shutdown();

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
