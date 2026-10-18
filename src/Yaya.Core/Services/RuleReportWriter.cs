namespace Yaya.Core.Services;

/// <summary>
/// 规则模板版三句话写出器（本周实现；LLM 版实现 IReportWriter 后直接替换）。
/// 三形态：正常（有专注+有建议起点）/ 有专注无起点 / 空数据。
/// </summary>
public sealed class RuleReportWriter : IReportWriter
{
    public string Write(DailyReportData data)
    {
        if (data.PeakWindowStart is not { } peakStart || data.PeakWindowEnd is not { } peakEnd)
        {
            // 空数据 / 全天游离
            return "今天还没有有效专注记录。最强时段还没诞生，别在意。明天开始时先做最难的 12 分钟。";
        }

        var peak = $"{peakStart:hh\\:mm}–{peakEnd:hh\\:mm}";
        var first = $"今天有效专注 {data.FocusedMinutes} 分钟。";

        if (data.AdviceStartTime is not { } adviceTime)
        {
            return $"{first}最强时段 {peak}，{Encourage(data)}。明天开始时先做最难的 12 分钟。";
        }

        return $"{first}最强时段 {peak}，{Encourage(data)}。明天 {adviceTime:HH:mm} 先做最难的 {data.AdviceMinutes} 分钟。";
    }

    // 第二句按专注量级换口吻，避免 7 天同一句话
    private static string Encourage(DailyReportData data) =>
        data.FocusedMinutes >= 60 ? "那个状态值得复制" : "开局不错";
}
