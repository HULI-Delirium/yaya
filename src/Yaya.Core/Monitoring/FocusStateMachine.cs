namespace Yaya.Core.Monitoring;

/// <summary>
/// 三态状态机 v2：规则决策表（纯逻辑：无 IO、无定时器、无 UI 依赖，可单测）。
/// 判定规则按序先命中先生效（完整表见 ARCHITECTURE.md 第 3 节）：
///   R1 微重启窗口内 → Uncertain 且不询问；
///   R2 前台类别刚切换 &lt; 宽限期 → Uncertain（防 Alt-Tab 路过误判）；
///   R3 学习类（Study/Utility）且空闲 &lt; 阈值 → Focused；
///   R4 娱乐/社交且番茄钟非休息段 → Distracted（新进入时询问，同类 10 分钟冷却）；
///   R5 学习类但空闲 ≥ 阈值（信号冲突）→ Uncertain；
///   R6 休息段 + 娱乐/社交（合法休息）→ Uncertain；
///   R7 其他（Unknown 等信号不足）→ Uncertain。
/// 产品红线：Uncertain 与微重启窗口内绝不发出任何打扰。
/// </summary>
public sealed class FocusStateMachine
{
    private readonly FocusStateMachineOptions _options;
    private readonly TimeProvider _clock;

    // 按询问类型分别记录上次询问时刻，实现“同类询问冷却期内不重复”
    private readonly Dictionary<NudgeKind, DateTimeOffset> _lastNudgeAt = new();

    private AppCategory? _lastCategory;         // null = 尚未采到第一个样本
    private DateTimeOffset _categoryChangedAt;  // 上次类别变化时刻（注入时钟时间）
    private DateTimeOffset? _microRestartUntil; // 微重启窗口结束时刻；null = 不在窗口内

    public FocusState Current { get; private set; } = FocusState.Uncertain;

    /// <summary>是否处于“逃避”后的微重启窗口（UI 可据此显示“重新开始”）。</summary>
    public bool InMicroRestart =>
        _microRestartUntil is { } until && _clock.GetUtcNow() < until;

    /// <summary>状态迁移事件：供 UI 播放动画。测试以 Evaluate 返回值断言为主、事件仅一例冒烟。</summary>
    public event EventHandler<FocusTransition>? StateChanged;

    public FocusStateMachine(FocusStateMachineOptions? options = null, TimeProvider? clock = null)
    {
        _options = options ?? new FocusStateMachineOptions();
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>喂入一次观测，返回本轮迁移结果（含询问门控决策）。</summary>
    public FocusTransition Evaluate(in Observation sample)
    {
        var now = _clock.GetUtcNow();
        var previous = Current;
        var next = Decide(in sample, now);

        // 询问门控：新进入游离 + 同类冷却已耗尽；Uncertain/微重启分支天然不会走到这里
        var shouldNudge = next == FocusState.Distracted
            && next != previous
            && NudgeCooldownElapsed(NudgeKind.ReturnToStudy, now);

        if (shouldNudge)
        {
            _lastNudgeAt[NudgeKind.ReturnToStudy] = now;
        }

        Current = next;
        var transition = new FocusTransition(previous, next, shouldNudge,
            shouldNudge ? NudgeKind.ReturnToStudy : null);

        if (previous != next)
        {
            StateChanged?.Invoke(this, transition);
        }

        return transition;
    }

    /// <summary>用户纠正“刚才算有效”：回退到 Focused 并结束微重启窗口；返回供落库的纠正记录。</summary>
    public CorrectionRecord MarkAsValid(string? note = null)
    {
        var record = new CorrectionRecord(_clock.GetUtcNow(), UserCorrectionKind.CountAsValid,
            Current, FocusState.Focused, note);
        _microRestartUntil = null;
        Current = FocusState.Focused;
        return record;
    }

    /// <summary>用户标记“逃避”：进入微重启窗口（默认 2 分钟，可配置），期间不询问；返回供落库的纠正记录。</summary>
    public CorrectionRecord MarkEscape(string? note = null)
    {
        var now = _clock.GetUtcNow();
        var record = new CorrectionRecord(now, UserCorrectionKind.Escape,
            Current, FocusState.Uncertain, note);
        _microRestartUntil = now + TimeSpan.FromMinutes(_options.MicroRestartMinutes);
        Current = FocusState.Uncertain;
        return record;
    }

    private FocusState Decide(in Observation sample, DateTimeOffset now)
    {
        // R1：微重启窗口内一律 Uncertain（连类别切换也不记录，窗口结束后再重新建立基线）
        if (_microRestartUntil is { } until && now < until)
        {
            return FocusState.Uncertain;
        }

        // R2：跟踪类别切换。首个样本不算“刚切换”（把变化时刻前拨一个宽限期，立即按正常规则判定）；
        // 之后的每次类别变化都重新起算宽限期
        if (_lastCategory is null)
        {
            _lastCategory = sample.Category;
            _categoryChangedAt = now - TimeSpan.FromMilliseconds(_options.CategorySwitchGraceMilliseconds);
        }
        else if (_lastCategory != sample.Category)
        {
            _lastCategory = sample.Category;
            _categoryChangedAt = now;
        }

        if (now - _categoryChangedAt < TimeSpan.FromMilliseconds(_options.CategorySwitchGraceMilliseconds))
        {
            return FocusState.Uncertain;
        }

        // R3 / R5：学习类（含工具）按空闲阈值二分支
        if (sample.Category is AppCategory.Study or AppCategory.Utility)
        {
            return sample.IdleMilliseconds < _options.IdleThresholdMilliseconds
                ? FocusState.Focused
                : FocusState.Uncertain;
        }

        // R4 / R6：娱乐社交按番茄钟相位二分支
        if (sample.Category is AppCategory.Entertainment or AppCategory.Social)
        {
            return sample.Phase == PomodoroPhase.Break
                ? FocusState.Uncertain
                : FocusState.Distracted;
        }

        // R7：Unknown 等信号不足
        return FocusState.Uncertain;
    }

    // 冷却判断：该类型从未询问过，或距上次已不少于配置间隔
    private bool NudgeCooldownElapsed(NudgeKind kind, DateTimeOffset now) =>
        !_lastNudgeAt.TryGetValue(kind, out var last)
        || now - last >= TimeSpan.FromMinutes(_options.NudgeCooldownMinutes);
}
