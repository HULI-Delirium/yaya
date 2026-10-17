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
using Yaya.Core.Storage;

namespace Yaya.App;

public partial class App : Application
{
    // 数据只落在当前用户本机目录，不上传、不漫游
    private static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Yaya");

    private static readonly string DbPath = Path.Combine(DataDir, "yaya.db");
    private const string TrayIconResourceName = "Yaya.App.Assets.tray.ico";

    private TaskbarIcon? _trayIcon;
    private YayaDb? _db;
    private MonitoringLoop? _loop;
    private MainViewModel? _viewModel;
    private PetAssets? _petAssets;
    private PetViewModel? _petViewModel;
    private PetWindow? _petWindow;
    private AppSettings? _settings;
    private MenuItem? _trayToggleItem;

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

            MonitoringLoop loop = null!;
            _viewModel = new MainViewModel(
                onMonitoringChanged: ApplyMonitoringEnabled,
                onNudgeChanged: ApplyNudgeEnabled,
                onClickThroughChanged: ApplyClickThrough,
                clearData: ConfirmClearData);
            loop = new MonitoringLoop(_db, _viewModel);
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

            MainWindow = new MainWindow { DataContext = _viewModel };

            // 设置初值（会触发回调完成应用与落盘；Start 幂等，穿透在 Show 后补句柄）
            _viewModel.IsMonitoringEnabled = _settings.MonitoringEnabled;
            _viewModel.IsNudgeEnabled = _settings.NudgeEnabled;
            _viewModel.IsClickThrough = _settings.ClickThrough;

            _petWindow.Show();
            _petWindow.SetClickThrough(_settings.ClickThrough);

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
        // 依序释放：停轮询与退订事件 → 宠物定时器 → 托盘 → 数据库 → 冲刷日志
        _loop?.Dispose();
        _petViewModel?.Dispose();
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

    private void TrayMenuClearData(object sender, RoutedEventArgs e) => ConfirmClearData();

    private void TrayMenuExit(object sender, RoutedEventArgs e) => Shutdown();

    /// <summary>一键清空：二次确认后删空本地数据（产品红线）。</summary>
    private void ConfirmClearData()
    {
        var result = MessageBox.Show("确定清空全部本地数据？此操作不可恢复。", "芽芽",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _db?.ClearAllData();
        Log.Warning("用户一键清空本地数据");
        _trayIcon?.ShowBalloonTip("芽芽", "本地数据已清空。", BalloonIcon.Info);
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
