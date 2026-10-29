namespace Yaya.Core.Services;

/// <summary>
/// 每日复盘聚合产物（结构化事实，与展示解耦）。
/// report_json 落库即此结构的序列化——第 3 周 LLM 版写出器同样只消费本结构。
/// </summary>
public sealed record DailyReportData(
    DateOnly Date,
    int FocusedMinutes,
    TimeSpan? PeakWindowStart,   // 最强 30 分钟对齐窗起点；当日无有效专注为 null
    TimeSpan? PeakWindowEnd,
    TimeOnly? AdviceStartTime,   // 明天第一个动作的建议起点；默认建议为 null
    int AdviceMinutes,           // 建议番茄分钟数（5–60；默认 12）
    int DistractedMinutes,
    int CorrectionCount)
{
    /// <summary>当前聚合口径版本；旧缓存（无此字段）默认 1，按 2 重算尾段。</summary>
    public const int CurrentVersion = 2;

    /// <summary>聚合口径版本：1 = 尾段兜底到日界；2 = 当日生成时截到 asOf。</summary>
    public int AggregationVersion { get; init; } = 1;
}
