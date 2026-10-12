using System.Windows.Threading;
using Serilog;
using Yaya.App.ViewModels;
using Yaya.Core.Monitoring;
using Yaya.Core.Storage;

namespace Yaya.App.Services;

/// <summary>
/// 行为感知循环：按阈值定时间隔读取“前台窗口标题 + 键鼠空闲时长”，
/// 交给 Core 层状态机判定，并在状态迁移时落库、在允许打扰时回调提醒。
/// 运行在 UI 线程（DispatcherTimer），写入 SQLite 足够轻量。
/// </summary>
public sealed class MonitoringLoop
{
    private readonly YayaDb _db;
    private readonly MainViewModel _viewModel;
    private readonly Action<string> _onNudge;
    private readonly DispatcherTimer _timer;
    private readonly FocusStateMachine _machine = new();

    // 会话 ID：每次应用启动生成一个，串联当次运行产生的全部 events
    private readonly string _sessionId = Guid.NewGuid().ToString("N");
    private AppCategory _lastCategory = AppCategory.Unknown;

    public MonitoringLoop(YayaDb db, MainViewModel viewModel, Action<string> onNudge)
    {
        _db = db;
        _viewModel = viewModel;
        _onNudge = onNudge;
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(MonitoringThresholds.SampleIntervalSeconds),
        };
        _timer.Tick += (_, _) => Tick();
    }

    public void Start()
    {
        Log.Information("感知循环启动：间隔 {Interval}s，会话 {SessionId}",
            MonitoringThresholds.SampleIntervalSeconds, _sessionId);
        _timer.Start();
        Tick();
    }

    public void Stop() => _timer.Stop();

    /// <summary>用户纠正入口：重置状态机当前态，并落库 corrections 表。</summary>
    public void ReportUserCorrection(FocusState corrected, string note)
    {
        var original = _machine.CorrectTo(corrected);
        _db.RecordCorrection(DateTimeOffset.UtcNow, original, corrected, _lastCategory, note);
        Log.Information("用户纠正：{Original} -> {Corrected}（{Note}）", original, corrected, note);
        _viewModel.StatusText = "收到纠正，我会记住的。";
    }

    private void Tick()
    {
        try
        {
            var title = NativeMethods.GetForegroundWindowTitle();
            var idleSeconds = NativeMethods.GetIdleSeconds();
            var category = AppCategoryClassifier.Classify(title);
            _lastCategory = category;

            var sample = new FocusSample(category, idleSeconds, DateTimeOffset.UtcNow);
            var transition = _machine.Evaluate(in sample);

            if (transition.From != transition.To)
            {
                _db.RecordEvent(sample.AtUtc, category, _sessionId, transition.To, idleSeconds);
                Log.Information("状态变化 {From} -> {To}（置信度 {Confidence:0.00}，空闲 {Idle:0.0}s，标题“{Title}”）",
                    transition.From, transition.To, transition.Confidence, idleSeconds, title);
            }

            _viewModel.StatusText = transition.To switch
            {
                FocusState.Focused => "学习中，很好哦",
                FocusState.Distracted => "有点跑偏了？",
                _ => "让我再看看……",
            };

            if (transition.ShouldNotify)
            {
                _onNudge("检测到你可能在游离，要回到学习吗？");
            }
        }
        catch (Exception ex)
        {
            // 感知循环任何异常都不允许击穿 UI
            Log.Error(ex, "感知循环异常");
        }
    }
}
