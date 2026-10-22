using Yaya.Core.Monitoring;
using Yaya.Core.Services;
using Yaya.Core.Storage;

namespace Yaya.Core.Tests;

public class WeeklyReportAggregatorTests
{
    private static readonly TimeZoneInfo Tz = TimeZoneInfo.CreateCustomTimeZone("UTC+8", TimeSpan.FromHours(8), "UTC+8", "UTC+8");
    private static readonly DateOnly Monday = new(2026, 10, 19);   // 周一

    private static DailyReportData Day(int focused) =>
        new(Monday, focused, null, null, null, 12, 0, 0);

    private static EventRow Pomo(int dayOffset, string state = WeeklyReportAggregator.PomodoroCompletedState)
    {
        // 本地周一 12:00 + dayOffset 天 → UTC
        var local = new DateTimeOffset(2026, 10, 19 + dayOffset, 12, 0, 0, TimeSpan.FromHours(8));
        return new EventRow(local.ToUniversalTime(), AppCategory.Unknown, state, 0);
    }

    [Fact]
    public void Aggregate_空周_全零且建议默认()
    {
        var data = WeeklyReportAggregator.Aggregate([], [], Monday, Tz);

        Assert.Equal(0, data.TotalFocusedMinutes);
        Assert.Equal(0, data.FocusedDays);
        Assert.Equal(0, data.PomodorosCompleted);
        Assert.Null(data.AdviceStartTime);
        Assert.Equal(12, data.AdviceMinutes);
    }

    [Fact]
    public void Aggregate_全部达标_总数与专注日正确()
    {
        var days = Enumerable.Range(0, 7).Select(_ => (DailyReportData?)Day(40)).ToList();
        var events = new List<EventRow> { Pomo(0), Pomo(3) };

        var data = WeeklyReportAggregator.Aggregate(events, days, Monday, Tz);

        Assert.Equal(280, data.TotalFocusedMinutes);
        Assert.Equal(7, data.FocusedDays);
        Assert.Equal(2, data.PomodorosCompleted);
    }

    [Fact]
    public void Aggregate_专注日阈值边界_29不算30算()
    {
        var days = Enumerable.Range(0, 7)
            .Select(i => (DailyReportData?)(i == 0 ? Day(29) : i == 1 ? Day(30) : null))
            .ToList();

        var data = WeeklyReportAggregator.Aggregate([], days, Monday, Tz, new WeeklyReportOptions { FocusedDayThresholdMinutes = 30 });

        Assert.Equal(1, data.FocusedDays);
        Assert.Equal(59, data.TotalFocusedMinutes);
    }

    [Fact]
    public void Aggregate_跨周事件不计入_周界过滤()
    {
        // 下周一 00:30（本地）的番茄完成 → 不属于本周
        var nextMonday = new DateTimeOffset(2026, 10, 26, 0, 30, 0, TimeSpan.FromHours(8)).ToUniversalTime();
        var events = new List<EventRow>
        {
            new(nextMonday, AppCategory.Unknown, WeeklyReportAggregator.PomodoroCompletedState, 0),
            Pomo(6),   // 周日 12:00，属于本周
        };
        var days = Enumerable.Range(0, 7).Select(_ => (DailyReportData?)null).ToList();
        days[6] = Day(10);

        var data = WeeklyReportAggregator.Aggregate(events, days, Monday, Tz);

        Assert.Equal(1, data.PomodorosCompleted);
        Assert.Equal(10, data.TotalFocusedMinutes);   // 仅周日段，跨零点段已由日聚合口径切干净
    }

    [Fact]
    public void Aggregate_番茄完成计数_Completed计Aborted不计()
    {
        var events = new List<EventRow>
        {
            Pomo(0),
            Pomo(1),
            Pomo(2, "PomodoroAborted"),
            Pomo(3, "PomodoroBreakCompleted"),
        };

        var data = WeeklyReportAggregator.Aggregate(events, [], Monday, Tz);

        Assert.Equal(2, data.PomodorosCompleted);
    }

    [Fact]
    public void Aggregate_下周建议_取本周最新一天的非空建议()
    {
        var days = Enumerable.Range(0, 7).Select(_ => (DailyReportData?)null).ToList();
        days[2] = new DailyReportData(Monday.AddDays(2), 60, null, null, new TimeOnly(10, 0), 20, 0, 0);
        days[5] = new DailyReportData(Monday.AddDays(5), 45, null, null, null, 12, 0, 0);   // 更新但无建议

        var data = WeeklyReportAggregator.Aggregate([], days, Monday, Tz);

        Assert.Equal(new TimeOnly(10, 0), data.AdviceStartTime);
        Assert.Equal(20, data.AdviceMinutes);
    }
}
