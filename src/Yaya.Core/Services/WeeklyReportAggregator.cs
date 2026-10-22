using Yaya.Core.Storage;

namespace Yaya.Core.Services;

/// <summary>
/// 周报聚合器（纯函数，可单测）：自然周（周一至周日，本地时界），
/// 复用日聚合口径（输入 7 天的 DailyReportData）+ events 计数番茄完成。
/// 建议规则：复用“明天第一个动作”——从本周最新一天往前找第一个非空建议起点，推到下周一。
/// </summary>
public static class WeeklyReportAggregator
{
    public const string PomodoroCompletedState = "PomodoroFocusCompleted";
    private const int AdviceDefaultMinutes = 12;

    /// <param name="weekEvents">调用方查询的周范围事件；此处按周界再过滤兜底（跨周事件不计入）。</param>
    /// <param name="weekDailyReports">周一至周日 7 个日聚合（无数据日为 null，由调用方补齐或留空）。</param>
    public static WeeklyReportData Aggregate(
        IReadOnlyList<EventRow> weekEvents,
        IReadOnlyList<DailyReportData?> weekDailyReports,
        DateOnly weekStart,
        TimeZoneInfo timeZone,
        WeeklyReportOptions? options = null)
    {
        var opt = options ?? new WeeklyReportOptions();
        var fromUtc = ToUtc(weekStart.ToDateTime(TimeOnly.MinValue), timeZone);
        var toUtc = fromUtc.AddDays(7);

        var totalFocused = 0;
        var focusedDays = 0;
        for (var i = 0; i < 7 && i < weekDailyReports.Count; i++)
        {
            var focused = weekDailyReports[i]?.FocusedMinutes ?? 0;
            totalFocused += focused;
            if (focused >= opt.FocusedDayThresholdMinutes)
            {
                focusedDays++;
            }
        }

        var pomodoros = 0;
        foreach (var e in weekEvents)
        {
            if (e.State == PomodoroCompletedState && e.AtUtc >= fromUtc && e.AtUtc < toUtc)
            {
                pomodoros++;
            }
        }

        TimeOnly? adviceTime = null;
        var adviceMinutes = AdviceDefaultMinutes;
        for (var i = weekDailyReports.Count - 1; i >= 0; i--)
        {
            if (weekDailyReports[i]?.AdviceStartTime is { } time)
            {
                adviceTime = time;
                adviceMinutes = weekDailyReports[i]!.AdviceMinutes;
                break;
            }
        }

        return new WeeklyReportData(weekStart, totalFocused, focusedDays, pomodoros, adviceTime, adviceMinutes);
    }

    private static DateTimeOffset ToUtc(DateTime localUnspecified, TimeZoneInfo timeZone) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localUnspecified, DateTimeKind.Unspecified), timeZone);
}
