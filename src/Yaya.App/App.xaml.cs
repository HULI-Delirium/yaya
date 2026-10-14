using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media.Imaging;
using Hardcodet.Wpf.TaskbarNotification;
using Serilog;
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

            _db = new YayaDb(DbPath);
            _db.Initialize();
            Log.Information("本地数据库就绪：{DbPath}", DbPath);

            _trayIcon = (TaskbarIcon)FindResource("TrayIcon");
            _trayIcon.IconSource = LoadTrayIcon();
            Log.Information("托盘图标已加载（内嵌占位图标）");

            MonitoringLoop loop = null!;
            _viewModel = new MainViewModel(
                note => loop.ReportValid(note),
                note => loop.ReportEscape(note));
            loop = new MonitoringLoop(_db, _viewModel, OnNudge);
            _loop = loop;

            MainWindow = new MainWindow { DataContext = _viewModel };
            _loop.Start();
            MainWindow.Show();

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
        // 依序释放：停轮询与退订事件 → 托盘 → 数据库 → 冲刷日志
        _loop?.Dispose();
        _trayIcon?.Dispose();
        _db?.Dispose();
        Log.Information("芽芽退出");
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private void OnNudge(string message)
    {
        Log.Information("低打扰提醒：{Message}", message);
        _trayIcon?.ShowBalloonTip("芽芽", message, BalloonIcon.Info);
    }

    private void TrayMenuShowMainWindow(object sender, RoutedEventArgs e)
    {
        if (MainWindow is not { } window)
        {
            window = new MainWindow { DataContext = _viewModel };
            MainWindow = window;
        }

        window.Show();
        window.Activate();
    }

    private void TrayMenuClearData(object sender, RoutedEventArgs e)
    {
        _db?.ClearAllData();
        Log.Warning("用户一键清空本地数据");
        _trayIcon?.ShowBalloonTip("芽芽", "本地数据已清空。", BalloonIcon.Info);
    }

    private void TrayMenuExit(object sender, RoutedEventArgs e) => Shutdown();

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
