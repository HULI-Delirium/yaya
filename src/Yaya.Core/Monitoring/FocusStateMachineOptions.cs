namespace Yaya.Core.Monitoring;

/// <summary>
/// 状态机阈值集中配置（产品要求：全部可配置）。所有默认值均为占位初值，
/// 需结合真实宿舍/自习场景校准，见各 TODO(校准) 标记；校准流程见 ARCHITECTURE.md 第 3 节。
/// </summary>
public sealed class FocusStateMachineOptions
{
    private long _idleThresholdMilliseconds = 120_000;
    private long _categorySwitchGraceMilliseconds = 8_000;
    private double _nudgeCooldownMinutes = 10;
    private double _microRestartMinutes = 2;

    /// <summary>
    /// 学习窗口空闲超过该毫秒数即视为“信号冲突”（挂着学习窗口人离开），判 Uncertain。
    /// TODO(校准)。
    /// </summary>
    public long IdleThresholdMilliseconds
    {
        get => _idleThresholdMilliseconds;
        set => _idleThresholdMilliseconds = RequirePositive(value, nameof(IdleThresholdMilliseconds));
    }

    /// <summary>
    /// 前台应用类别刚切换后的宽限期：期内一律 Uncertain，防止 Alt-Tab 路过误判。TODO(校准)。
    /// </summary>
    public long CategorySwitchGraceMilliseconds
    {
        get => _categorySwitchGraceMilliseconds;
        set => _categorySwitchGraceMilliseconds = RequirePositive(value, nameof(CategorySwitchGraceMilliseconds));
    }

    /// <summary>同类轻干预询问的最短间隔（分钟），默认 10 分钟（产品硬性要求）。TODO(校准)。</summary>
    public double NudgeCooldownMinutes
    {
        get => _nudgeCooldownMinutes;
        set => _nudgeCooldownMinutes = RequirePositive(value, nameof(NudgeCooldownMinutes));
    }

    /// <summary>标记“逃避”后的微重启窗口（分钟），窗口内不发出任何询问。TODO(校准)。</summary>
    public double MicroRestartMinutes
    {
        get => _microRestartMinutes;
        set => _microRestartMinutes = RequirePositive(value, nameof(MicroRestartMinutes));
    }

    // 阈值必须为正：0 或负值会让决策表出现“永远成立/永不成立”的静默错误，宁可启动即失败
    private static long RequirePositive(long value, string paramName) =>
        value > 0 ? value : throw new ArgumentOutOfRangeException(paramName, value, "阈值必须为正数");

    private static double RequirePositive(double value, string paramName) =>
        value > 0 ? value : throw new ArgumentOutOfRangeException(paramName, value, "阈值必须为正数");
}
