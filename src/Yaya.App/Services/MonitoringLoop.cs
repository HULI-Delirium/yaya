using System.Diagnostics;
using System.Windows.Threading;
using Serilog;
using Yaya.App.ViewModels;
using Yaya.Core.Monitoring;
using Yaya.Core.Storage;

namespace Yaya.App.Services;

/// <summary>
/// 行为采集与感知循环（后台轮询，默认每 2 秒一次）：
/// 每轮读取“前台进程名 + 窗口标题 + 键鼠空闲时长”，交给 Core 层状态机判定；
/// 状态迁移时写 Serilog 与 events 表并抛出 FocusTransitionOccurred；
/// Core 允许询问时抛出 NudgeRequested（提醒总开关的过滤在订阅方，不影响冷却计数）。
/// 运行在 UI 线程（DispatcherTimer）：单轮微秒级工作，无需后台线程与锁。
/// </summary>
public sealed class MonitoringLoop : IDisposable
{
    // 采样间隔（TODO(校准)：平衡及时性与 CPU 占用，资源红线见 ARCHITECTURE.md 第 6 节）
    private const double SampleIntervalSeconds = 2;

    // 自己的进程名：宠物/设置/记录/浮层获得前台时不参与分类，保留上一个外部前台
    private static readonly string SelfProcessName = Process.GetCurrentProcess().ProcessName;

    private readonly YayaDb _db;
    private readonly MainViewModel _viewModel;
    private readonly Func<PomodoroPhase> _phaseProvider;
    private readonly DispatcherTimer _timer;
    private readonly FocusStateMachine _machine;
    private AppClassifier _classifier;
    private bool _diagnosticLogging;
    private bool _timerRunning;
    private bool _disposed;

    // 会话 ID：每次应用启动生成一个，串联当次运行产生的全部 events
    private readonly string _sessionId = Guid.NewGuid().ToString("N");
    private AppCategory _lastCategory = AppCategory.Unknown;
    private ClassificationResult _lastClassification = ClassificationResult.Unknown("尚未采集");
    private string _lastProcessName = string.Empty;
    private bool _lastIsBrowserHost;

    /// <summary>状态迁移事件（宠物窗动画/设置页挂接）。</summary>
    public event EventHandler<FocusTransition>? FocusTransitionOccurred;

    /// <summary>Core 判定应询问时抛出；是否展示由订阅方按提醒开关决定。</summary>
    public event EventHandler? NudgeRequested;

    /// <summary>分类结果变化（进程/来源/类别/规则变化时发，供设置页当前应用卡片）。</summary>
    public event EventHandler<ClassificationResult>? ClassificationChanged;

    /// <summary>每轮判定后都发（即使状态没迁移），供 Uncertain 角标与「为什么」浮层。</summary>
    public event EventHandler<FocusState>? StateEvaluated;

    public MonitoringLoop(
        YayaDb db,
        MainViewModel viewModel,
        Func<PomodoroPhase>? phaseProvider = null,
        AppClassifier? classifier = null)
    {
        _db = db;
        _viewModel = viewModel;
        _phaseProvider = phaseProvider ?? (() => PomodoroPhase.None);
        _classifier = classifier ?? AppClassifier.BuiltinOnly;
        _machine = new FocusStateMachine();
        _machine.StateChanged += OnMachineStateChanged;
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(SampleIntervalSeconds),
        };
        _timer.Tick += (_, _) => Tick();
    }

    /// <summary>本次运行的会话 ID（番茄事件等扩展事件复用同一会话）。</summary>
    public string SessionId => _sessionId;

    /// <summary>最近一次分类结果（UI 解释用；Reason 不进日志）。</summary>
    public ClassificationResult LastClassification => _lastClassification;

    /// <summary>最近一次前台进程名（仅 UI 展示；日志按分级策略处理）。</summary>
    public string LastProcessName => _lastProcessName;

    /// <summary>最近一次前台是否为浏览器宿主。</summary>
    public bool LastIsBrowserHost => _lastIsBrowserHost;

    /// <summary>当前三态（状态机内部值，只读）。</summary>
    public FocusState CurrentState => _machine.Current;

    /// <summary>规则变更后热替换分类器（UI 线程调用）。</summary>
    public void UpdateClassifier(AppClassifier classifier) =>
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));

    /// <summary>诊断开关：开启后才把窗口标题写入本地日志（默认关闭，红线）。</summary>
    public void UpdateDiagnosticLogging(bool enabled) => _diagnosticLogging = enabled;

    public void Start()
    {
        if (_timerRunning)
        {
            return;
        }

        _timerRunning = true;
        Log.Information("感知循环启动：间隔 {Interval}s，会话 {SessionId}", SampleIntervalSeconds, _sessionId);
        _timer.Start();
        Tick();   // 冷启动立即采一轮，不等首个 2 秒
    }

    public void Stop()
    {
        if (!_timerRunning)
        {
            return;
        }

        _timerRunning = false;
        _timer.Stop();
        Log.Information("感知循环已暂停");
    }

    /// <summary>用户纠正“刚才算有效”：回退状态并把纠正记录落库。</summary>
    public void ReportValid(string note) => ApplyCorrection(_machine.MarkAsValid(note));

    /// <summary>用户标记“逃避”：进入微重启窗口并把纠正记录落库。</summary>
    public void ReportEscape(string note) => ApplyCorrection(_machine.MarkEscape(note));

    /// <summary>释放轮询资源：写退出事件行（截断尾段）→ 停定时器 → 退订状态机事件。</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        WriteExitEvent();
        _machine.StateChanged -= OnMachineStateChanged;
    }

    // 退出事件行：把当日最后一个状态段的尾段截断在退出时刻，防止复盘把关机后的时间算进专注
    private void WriteExitEvent()
    {
        try
        {
            _db.RecordEvent(DateTimeOffset.UtcNow, _lastCategory, _sessionId, "AppExit", 0);
            Log.Information("退出事件行已写入（会话 {SessionId}）", _sessionId);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "退出事件行写入失败（不影响退出）");
        }
    }

    private void OnMachineStateChanged(object? sender, FocusTransition transition)
    {
        Log.Information("状态迁移事件 {From} -> {To}", transition.From, transition.To);
    }

    // 分类变化才通知 UI；日志只记进程名/来源/规则 Id（不记标题/规则值）
    private void PublishClassification(string processName, ClassificationResult classification)
    {
        var process = processName.Trim();
        var changed = !string.Equals(process, _lastProcessName, StringComparison.OrdinalIgnoreCase)
            || classification.Category != _lastClassification.Category
            || classification.Source != _lastClassification.Source
            || !string.Equals(classification.RuleId, _lastClassification.RuleId, StringComparison.Ordinal)
            || !string.Equals(classification.MatchedSite, _lastClassification.MatchedSite, StringComparison.OrdinalIgnoreCase)
            || classification.IsMuted != _lastClassification.IsMuted;

        if (!changed)
        {
            return;
        }

        _lastProcessName = process;
        _lastIsBrowserHost = AppCatalog.IsBrowserHost(process);
        _lastClassification = classification;
        Log.Debug("分类上下文：进程 {ProcessName}，来源 {Source}，规则 {RuleId}",
            process, classification.Source, classification.RuleId);
        ClassificationChanged?.Invoke(this, classification);
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
            // ① 采集：进程名（类别映射主键）+ 标题（浏览器站点兜底）+ 空闲毫秒
            var processName = NativeMethods.GetForegroundProcessName();

            // 芽芽自己的窗口（点桌宠/设置/记录）不参与分类：
            // 否则「把当前应用设为…」永远只能抓到 Yaya.App，也会把状态误打成 Unknown。
            if (!string.IsNullOrWhiteSpace(processName)
                && string.Equals(processName.Trim(), SelfProcessName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var title = NativeMethods.GetForegroundWindowTitle();
            var idleSeconds = NativeMethods.GetIdleSeconds();
            var classification = _classifier.Classify(processName, title);
            var category = classification.Category;
            _lastCategory = category;
            PublishClassification(processName, classification);

            // 红线：标题默认不进日志；仅用户显式开启诊断时才写（且截断到 512 字符）
            if (_diagnosticLogging && title.Length > 0)
            {
                var snippet = title.Length > 512 ? title[..512] : title;
                Log.Debug("诊断上下文：标题长度 {Length}，标题 {Title}", title.Length, snippet);
            }

            // 相位由番茄钟服务动态供给（未接入时恒为 None，行为与 v1 一致）
            var idleMilliseconds = (long)(idleSeconds * 1000);
            var observation = new Observation(category, idleMilliseconds, _phaseProvider());
            var transition = _machine.Evaluate(in observation);
            StateEvaluated?.Invoke(this, transition.To);

            // ② 迁移才落库与记日志（稳态零 I/O，资源红线达标的关键）
            if (transition.From != transition.To)
            {
                _db.RecordEvent(DateTimeOffset.UtcNow, category, _sessionId, transition.To, idleSeconds);
                // 红线：只记类别/来源/规则 Id，不记标题、不记规则值
                Log.Information("状态变化 {From} -> {To}（类别 {Category}，来源 {Source}，规则 {RuleId}，空闲 {IdleMs}ms）",
                    transition.From, transition.To, category, classification.Source, classification.RuleId, idleMilliseconds);
                FocusTransitionOccurred?.Invoke(this, transition);
            }

            _viewModel.StatusText = transition.To switch
            {
                FocusState.Focused => "学习中，很好哦",
                FocusState.Distracted => "有点跑偏了？",
                _ => "让我再看看……",
            };

            // ③ 询问门控由状态机内部完成（冷却/宽限/微重启），这里只转发结果
            if (transition.ShouldNudge)
            {
                Log.Information("低打扰询问已触发（同类冷却由 Core 保证）");
                NudgeRequested?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            // 感知循环任何异常都不允许击穿 UI
            Log.Error(ex, "感知循环异常");
        }
    }
}
