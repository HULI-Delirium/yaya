using Yaya.Core.Monitoring;
using Yaya.Core.Services;
using Yaya.Core.Storage;

namespace Yaya.Core.Tests;

public class DailyReportAggregatorTests
{
    private static readonly TimeZoneInfo Tz = TimeZoneInfo.CreateCustomTimeZone("UTC+8", TimeSpan.FromHours(8), "UTC+8", "UTC+8");
    private static readonly DateOnly Day = new(2026, 10, 18);

    // 本地时刻 → UTC（与固定 +8 时区配套）
    private static DateTimeOffset Utc(int hh, int mm = 0, int dayOffset = 0) =>
        new DateTimeOffset(2026, 10, 18 + dayOffset, hh, mm, 0, TimeSpan.FromHours(8)).ToUniversalTime();

    private static EventRow E(int hh, int mm, FocusState state, int dayOffset = 0) =>
        new(Utc(hh, mm, dayOffset), AppCategory.Study, state.ToString(), 0);

    private static EventRow Raw(int hh, int mm, string state, int dayOffset = 0) =>
        new(Utc(hh, mm, dayOffset), AppCategory.Unknown, state, 0);

    [Fact]
    public void Aggregate_空数据_全零且建议取默认()
    {
        var data = DailyReportAggregator.Aggregate([], [], Day, Tz);

        Assert.Equal(0, data.FocusedMinutes);
        Assert.Null(data.PeakWindowStart);
        Assert.Null(data.AdviceStartTime);
        Assert.Equal(12, data.AdviceMinutes);
        Assert.Equal(0, data.CorrectionCount);
    }

    [Fact]
    public void Aggregate_单段专注_分钟与最强窗正确且尾段按日界截断()
    {
        var events = new List<EventRow>
        {
            E(10, 0, FocusState.Focused),
            E(10, 40, FocusState.Distracted),
        };

        var data = DailyReportAggregator.Aggregate(events, [], Day, Tz);

        Assert.Equal(40, data.FocusedMinutes);
        Assert.Equal(TimeSpan.FromHours(10), data.PeakWindowStart);
        Assert.Equal(TimeSpan.FromHours(10) + TimeSpan.FromMinutes(30), data.PeakWindowEnd);
        Assert.Null(data.AdviceStartTime);          // 最强窗后无新专注起点
        Assert.Equal(12, data.AdviceMinutes);
        Assert.Equal(800, data.DistractedMinutes);   // 10:40 → 24:00 = 13h20m
    }

    [Fact]
    public void Aggregate_多段专注_最强窗并列取最早且建议取窗后起点()
    {
        var events = new List<EventRow>
        {
            E(10, 0, FocusState.Focused),
            E(10, 20, FocusState.Distracted),
            E(10, 25, FocusState.Focused),
            E(11, 0, FocusState.Distracted),
            E(15, 0, FocusState.Focused),
            E(15, 10, FocusState.Distracted),
            Raw(16, 0, "AppExit"),
        };

        var data = DailyReportAggregator.Aggregate(events, [], Day, Tz);

        Assert.Equal(65, data.FocusedMinutes);      // 20 + 35 + 10
        Assert.Equal(TimeSpan.FromHours(10) + TimeSpan.FromMinutes(30), data.PeakWindowStart);   // 10:30–11:00 独占 30 分钟
        Assert.Equal(TimeSpan.FromHours(11), data.PeakWindowEnd);
        Assert.Equal(new TimeOnly(15, 0), data.AdviceStartTime);
        Assert.Equal(10, data.AdviceMinutes);
        Assert.Equal(295, data.DistractedMinutes);  // 5 + 240 + 50
    }

    [Fact]
    public void Aggregate_跨零点_专注段按日界切开()
    {
        var events = new List<EventRow>
        {
            E(23, 50, FocusState.Focused),
            E(0, 10, FocusState.Distracted, dayOffset: 1),
        };

        var data = DailyReportAggregator.Aggregate(events, [], Day, Tz);

        Assert.Equal(10, data.FocusedMinutes);      // 23:50 → 24:00 归当日，次日 10 分钟归次日
    }

    [Fact]
    public void Aggregate_退出事件行截断尾段_数字不膨胀()
    {
        var events = new List<EventRow>
        {
            E(15, 0, FocusState.Focused),
            Raw(15, 10, "AppExit"),
        };

        var data = DailyReportAggregator.Aggregate(events, [], Day, Tz);

        Assert.Equal(10, data.FocusedMinutes);      // 15:00–15:10，而非到 24:00
    }

    [Fact]
    public void Aggregate_尾段无退出事件_按兜底口径到日界()
    {
        var events = new List<EventRow> { E(15, 0, FocusState.Focused) };

        var data = DailyReportAggregator.Aggregate(events, [], Day, Tz);

        Assert.Equal(9 * 60, data.FocusedMinutes);  // 15:00 → 24:00（老数据/应用仍在运行的兜底）
    }

    [Fact]
    public void Aggregate_纠正为有效_段计入且截到下一事件()
    {
        var events = new List<EventRow>
        {
            E(14, 0, FocusState.Distracted),
            E(14, 20, FocusState.Distracted),
            Raw(23, 0, "AppExit"),
        };
        var corrections = new List<CorrectionRow>
        {
            new(Utc(14, 5), UserCorrectionKind.CountAsValid),
            new(Utc(14, 7), UserCorrectionKind.Escape),   // 不反向扣分，但计入 correction_count
        };

        var data = DailyReportAggregator.Aggregate(events, corrections, Day, Tz);

        Assert.Equal(15, data.FocusedMinutes);      // 14:05 → 14:20
        Assert.Equal(2, data.CorrectionCount);
    }

    [Fact]
    public void Aggregate_纠正为有效_单段封顶30分钟()
    {
        var events = new List<EventRow>
        {
            E(14, 0, FocusState.Distracted),
            E(16, 0, FocusState.Distracted),
        };
        var corrections = new List<CorrectionRow>
        {
            new(Utc(14, 5), UserCorrectionKind.CountAsValid),
        };

        var data = DailyReportAggregator.Aggregate(events, corrections, Day, Tz);

        Assert.Equal(30, data.FocusedMinutes);      // 14:05 → 14:35 封顶，不吞掉 14:35–16:00
    }

    [Fact]
    public void Aggregate_全天游离_零专注无最强窗()
    {
        var events = new List<EventRow>
        {
            E(9, 0, FocusState.Distracted),
            E(18, 0, FocusState.Distracted),
            Raw(18, 30, "AppExit"),
        };

        var data = DailyReportAggregator.Aggregate(events, [], Day, Tz);

        Assert.Equal(0, data.FocusedMinutes);
        Assert.Null(data.PeakWindowStart);
        Assert.Equal(12, data.AdviceMinutes);
    }

    [Fact]
    public void Aggregate_当日生成传入AsOf_尾段截到此刻不膨胀()
    {
        var events = new List<EventRow> { E(17, 54, FocusState.Focused) };

        var data = DailyReportAggregator.Aggregate(events, [], Day, Tz, asOfUtc: Utc(18, 5));

        Assert.Equal(11, data.FocusedMinutes);          // 17:54 → 18:05，而不是到 24:00
        Assert.Equal(TimeSpan.FromHours(17) + TimeSpan.FromMinutes(30), data.PeakWindowStart);
    }

    [Fact]
    public void Aggregate_AsOf晚于日界_仍按日界截断()
    {
        var events = new List<EventRow> { E(23, 0, FocusState.Focused) };

        var data = DailyReportAggregator.Aggregate(events, [], Day, Tz, asOfUtc: Utc(0, 30, dayOffset: 1));

        Assert.Equal(60, data.FocusedMinutes);          // 23:00 → 24:00
    }

    [Fact]
    public void Aggregate_AsOf截断纠正段()
    {
        var events = new List<EventRow>
        {
            E(14, 0, FocusState.Distracted),
            E(16, 0, FocusState.Distracted),
        };
        var corrections = new List<CorrectionRow>
        {
            new(Utc(14, 5), UserCorrectionKind.CountAsValid),
        };

        var data = DailyReportAggregator.Aggregate(events, corrections, Day, Tz, asOfUtc: Utc(14, 20));

        Assert.Equal(15, data.FocusedMinutes);          // 14:05 → 14:20，而不是 14:35 封顶
    }

    [Fact]
    public void Aggregate_AsOf早于首事件_零专注()
    {
        var events = new List<EventRow> { E(18, 0, FocusState.Focused) };

        var data = DailyReportAggregator.Aggregate(events, [], Day, Tz, asOfUtc: Utc(17, 0));

        Assert.Equal(0, data.FocusedMinutes);
        Assert.Null(data.PeakWindowStart);
    }

    [Fact]
    public void Aggregate_当前口径版本为2()
    {
        var data = DailyReportAggregator.Aggregate([], [], Day, Tz);

        Assert.Equal(DailyReportData.CurrentVersion, data.AggregationVersion);
    }
}
