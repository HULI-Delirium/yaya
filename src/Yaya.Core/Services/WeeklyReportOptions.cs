namespace Yaya.Core.Services;

/// <summary>周报聚合选项。</summary>
public sealed class WeeklyReportOptions
{
    private int _focusedDayThresholdMinutes = 30;

    /// <summary>专注日阈值：当日有效专注 ≥ 该分钟数计 1 个专注日。TODO(校准)。</summary>
    public int FocusedDayThresholdMinutes
    {
        get => _focusedDayThresholdMinutes;
        set => _focusedDayThresholdMinutes = value > 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(FocusedDayThresholdMinutes), value, "阈值必须为正数");
    }
}
