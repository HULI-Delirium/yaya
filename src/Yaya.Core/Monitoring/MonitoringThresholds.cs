namespace Yaya.Core.Monitoring;

/// <summary>
/// 行为感知阈值集中管理。所有数值均为占位初值，
/// 需结合真实宿舍/自习场景校准，见各 TODO(校准) 标记。
/// </summary>
public static class MonitoringThresholds
{
    /// <summary>采样间隔（秒）。TODO(校准)：平衡及时性与系统开销。</summary>
    public const double SampleIntervalSeconds = 5;

    /// <summary>键鼠空闲超过该秒数即视为“人离开了键盘”。TODO(校准)。</summary>
    public const double IdleThresholdSeconds = 120;

    /// <summary>置信度低于该值不打扰用户（产品红线：低置信度不打扰）。TODO(校准)。</summary>
    public const double NotifyMinConfidence = 0.75;

    /// <summary>两次打扰之间的最短冷却（分钟），防止连环弹窗。TODO(校准)。</summary>
    public const double NudgeCooldownMinutes = 20;
}
