using System.Windows.Threading;
using Serilog;
using Yaya.App.ViewModels;
using Yaya.Core.Monitoring;
using Yaya.Core.Storage;

namespace Yaya.App.Services;

/// <summary>
/// 行为感知循环：按间隔读取“前台窗口标题 + 键鼠空闲时长”，
/// 交给 Core 层状态机判定，并在状态迁移时落库、允许打扰时回调提醒。
/// 运行在 UI 线程（DispatcherTimer），写入 SQLite 足够轻量。
/// </summary>
public sealed class MonitoringLoop
{
    // 采样间隔是 App 侧调度参数；机器阈值已全部迁入 FocusStateMachineOptions（TODO(校准)）
    private const double SampleIntervalSeconds = 5;

    private readonly YayaDb _db;
    private readonly MainViewModel _viewModel;
    private readonly Action<string> _onNudge;
    private readonly DispatcherTimer _timer;
    private readonly FocusStateMachine _machine;

    // 会话 ID：每次应用启动生成一个，串联当次运行产生的全部 events
    private readonly string _sessionId = Guid.NewGuid().ToString("N");
    private AppCategory _lastCategory = AppCategory.Unknown;

    public MonitoringLoop(YayaDb db, MainViewModel viewModel, Action<string> onNudge)
    {
        _db = db;
        _viewModel = viewModel;
        _onNudge = onNudge;
        _machine = new FocusStateMachine();
        // 状态迁移事件供 UI 播放动画；v1 先落日志（TODO(app): 接到主窗体动画）
        _machine.StateChanged += (_, t) =>
            Log.Information("状态迁移事件 {From} -> {To}", t.From, t.To);
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(SampleIntervalSeconds),
        };
        _timer.Tick += (_, _) => Tick();
    }

    public void Start()
    {
        Log.Information("感知循环启动：间隔 {Interval}s，会话 {SessionId}", SampleIntervalSeconds, _sessionId);
        _timer.Start();
        Tick();
    }

    public void Stop() => _timer.Stop();

    /// <summary>用户纠正“刚才算有效”：回退状态并把纠正记录落库。</summary>
    public void ReportValid(string note) => ApplyCorrection(_machine.MarkAsValid(note));

    /// <summary>用户标记“逃避”：进入微重启窗口并把纠正记录落库。</summary>
    public void ReportEscape(string note) => ApplyCorrection(_machine.MarkEscape(note));

    private void ApplyCorrection(CorrectionRecord record)
    {
        _db.RecordCorrection(record.AtUtc, record.OriginalState, record.ResultingState,
            _lastCategory, record.Kind, record.Note);
        Log.Information("用户纠正 {Kind}：{Original} -> {Result}（{Note}）",
            record.Kind, record.OriginalState, record.ResultingState, record.Note);
        _viewModel.StatusText = record.Kind == UserCorrectionKind.Escape
            ? "没关系，2 分钟微重启开始。"
            : "收到，刚才算有效。";
    }

    private void Tick()
    {
        try
        {
            var title = NativeMethods.GetForegroundWindowTitle();
            var idleSeconds = NativeMethods.GetIdleSeconds();
            var category = AppCategoryClassifier.Classify(title);
            _lastCategory = category;

            // v1 尚未接入番茄钟，相位恒为 None（TODO(app): 番茄钟接入后传入实际相位）
            var idleMilliseconds = (long)(idleSeconds * 1000);
            var observation = new Observation(category, idleMilliseconds);
            var transition = _machine.Evaluate(in observation);

            if (transition.From != transition.To)
            {
                _db.RecordEvent(DateTimeOffset.UtcNow, category, _sessionId, transition.To, idleSeconds);
                Log.Information("状态变化 {From} -> {To}（空闲 {Idle:0.0}s，标题“{Title}”）",
                    transition.From, transition.To, idleSeconds, title);
            }

            _viewModel.StatusText = transition.To switch
            {
                FocusState.Focused => "学习中，很好哦",
                FocusState.Distracted => "有点跑偏了？",
                _ => "让我再看看……",
            };

            if (transition.ShouldNudge)
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
