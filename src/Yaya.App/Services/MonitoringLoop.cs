using System.Windows.Threading;
using Serilog;
using Yaya.App.ViewModels;
using Yaya.Core.Monitoring;
using Yaya.Core.Storage;

namespace Yaya.App.Services;

/// <summary>
/// 行为采集与感知循环（后台轮询，默认每 2 秒一次）：
/// 每轮读取“前台进程名 + 窗口标题 + 键鼠空闲时长”，交给 Core 层状态机判定，
/// 状态迁移时写 Serilog 日志与 events 表，允许打扰时回调提醒。
/// 运行在 UI 线程（DispatcherTimer）：单轮是微秒级工作，无需后台线程与锁。
/// </summary>
public sealed class MonitoringLoop : IDisposable
{
    // 采样间隔（TODO(校准)：平衡及时性与 CPU 占用，资源红线见 ARCHITECTURE.md 第 6 节）
    private const double SampleIntervalSeconds = 2;

    private readonly YayaDb _db;
    private readonly MainViewModel _viewModel;
    private readonly Action<string> _onNudge;
    private readonly DispatcherTimer _timer;
    private readonly FocusStateMachine _machine;
    private bool _disposed;

    // 会话 ID：每次应用启动生成一个，串联当次运行产生的全部 events
    private readonly string _sessionId = Guid.NewGuid().ToString("N");
    private AppCategory _lastCategory = AppCategory.Unknown;

    public MonitoringLoop(YayaDb db, MainViewModel viewModel, Action<string> onNudge)
    {
        _db = db;
        _viewModel = viewModel;
        _onNudge = onNudge;
        _machine = new FocusStateMachine();
        _machine.StateChanged += OnStateChanged;
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
        Tick();   // 冷启动立即采一轮，不等首个 2 秒
    }

    /// <summary>用户纠正“刚才算有效”：回退状态并把纠正记录落库。</summary>
    public void ReportValid(string note) => ApplyCorrection(_machine.MarkAsValid(note));

    /// <summary>用户标记“逃避”：进入微重启窗口并把纠正记录落库。</summary>
    public void ReportEscape(string note) => ApplyCorrection(_machine.MarkEscape(note));

    /// <summary>释放轮询资源：停定时器 + 退订状态机事件。App 退出时唯一调用点。</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        _machine.StateChanged -= OnStateChanged;
    }

    private void OnStateChanged(object? sender, FocusTransition transition)
    {
        // 状态迁移事件：v1 先落日志，主窗体动画在此挂接（TODO(app)）
        Log.Information("状态迁移事件 {From} -> {To}", transition.From, transition.To);
    }

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
            // ① 采集：进程名（类别映射主键）+ 标题（浏览器兜底）+ 空闲毫秒
            var processName = NativeMethods.GetForegroundProcessName();
            var title = NativeMethods.GetForegroundWindowTitle();
            var idleSeconds = NativeMethods.GetIdleSeconds();
            var category = AppCatalog.Classify(processName, title);
            _lastCategory = category;

            // v1 尚未接入番茄钟，相位恒为 None（TODO(app): 番茄钟接入后传入实际相位）
            var idleMilliseconds = (long)(idleSeconds * 1000);
            var observation = new Observation(category, idleMilliseconds);
            var transition = _machine.Evaluate(in observation);

            // ② 迁移才落库与记日志（稳态零 I/O，资源红线达标的关键）
            if (transition.From != transition.To)
            {
                _db.RecordEvent(DateTimeOffset.UtcNow, category, _sessionId, transition.To, idleSeconds);
                Log.Information("状态变化 {From} -> {To}（前台类别 {Category}，空闲 {IdleMs}ms）",
                    transition.From, transition.To, category, idleMilliseconds);
                // 进程名与标题只进本地日志，用于校准映射词表（红线：不进提醒气泡）
                Log.Debug("迁移上下文：进程 {ProcessName}，标题“{Title}”", processName, title);
            }

            _viewModel.StatusText = transition.To switch
            {
                FocusState.Focused => "学习中，很好哦",
                FocusState.Distracted => "有点跑偏了？",
                _ => "让我再看看……",
            };

            // ③ 询问门控由状态机内部完成（冷却/宽限/微重启），这里只消费结果
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
