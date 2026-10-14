namespace Yaya.Core.Monitoring;

/// <summary>
/// 番茄钟相位：由调用方（App 侧番茄钟）随观测传入，Core 只消费、不管理计时。
/// </summary>
public enum PomodoroPhase
{
    /// <summary>未启用番茄钟（v1 默认）。</summary>
    None = 0,

    /// <summary>工作段：处于应专注的番茄时段内。</summary>
    Focus = 1,

    /// <summary>休息段：娱乐/社交属合法休息，不判游离。</summary>
    Break = 2,
}
