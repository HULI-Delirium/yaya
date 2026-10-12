namespace Yaya.Core.Monitoring;

/// <summary>
/// 三态状态机（纯逻辑：无 IO、无定时器、无 UI 依赖，可单测）。
/// 判定规则：
///   1. 键鼠空闲超过阈值 → 游离，置信度 1.0；
///   2. 学习/工作类窗口 → 有效学习，置信度 0.9；
///   3. 娱乐/社交类窗口 → 游离，置信度 0.9；
///   4. 其他（标题未知）→ 不确定，置信度 0.3。
/// 产品红线：ShouldNotify 仅在“新进入游离 + 置信度达标 + 冷却结束”时为 true，
/// 低置信度绝不打扰。
/// </summary>
public sealed class FocusStateMachine
{
    private readonly TimeProvider _clock;
    private DateTimeOffset? _lastNotifiedAt;

    public FocusState Current { get; private set; } = FocusState.Uncertain;

    public FocusStateMachine(TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>喂入一个观测样本，返回本轮状态迁移结果。</summary>
    public FocusTransition Evaluate(in FocusSample sample)
    {
        var previous = Current;
        var (next, confidence) = Decide(in sample);

        var becameDistracted = next == FocusState.Distracted && next != previous;
        var confidenceEnough = confidence >= MonitoringThresholds.NotifyMinConfidence;
        var cooldownOver = _lastNotifiedAt is null
            || (_clock.GetUtcNow() - _lastNotifiedAt.Value).TotalMinutes
               >= MonitoringThresholds.NudgeCooldownMinutes;

        var shouldNotify = becameDistracted && confidenceEnough && cooldownOver;
        if (shouldNotify)
        {
            _lastNotifiedAt = _clock.GetUtcNow();
        }

        Current = next;
        return new FocusTransition(previous, next, confidence, shouldNotify);
    }

    /// <summary>用户纠正当前判断。返回被纠正前的状态，供记录 corrections 表。</summary>
    public FocusState CorrectTo(FocusState corrected)
    {
        var original = Current;
        Current = corrected;
        return original;
    }

    private static (FocusState State, double Confidence) Decide(in FocusSample sample)
    {
        if (sample.IdleSeconds >= MonitoringThresholds.IdleThresholdSeconds)
        {
            return (FocusState.Distracted, 1.0);
        }

        return sample.Category switch
        {
            AppCategory.Study or AppCategory.Work => (FocusState.Focused, 0.9),
            AppCategory.Entertainment or AppCategory.Social => (FocusState.Distracted, 0.9),
            _ => (FocusState.Uncertain, 0.3),
        };
    }
}
