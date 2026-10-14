namespace Yaya.Core.Monitoring;

/// <summary>
/// 一次行为观测样本：调用方传入的类别 + 系统空闲毫秒数 + 番茄钟相位。
/// Core 不认识具体窗口，只消费抽象信号（隐私红线见 ARCHITECTURE.md 第 2 节）。
/// </summary>
public readonly record struct Observation(AppCategory Category, long IdleMilliseconds, PomodoroPhase Phase)
{
    /// <summary>未接入番茄钟时的便捷构造：相位视为 None。</summary>
    public Observation(AppCategory category, long idleMilliseconds)
        : this(category, idleMilliseconds, PomodoroPhase.None)
    {
    }
}
