using Microsoft.Data.Sqlite;
using Yaya.Core.Monitoring;
using Yaya.Core.Services;
using Yaya.Core.Storage;

namespace Yaya.Core.Tests;

public class YayaDbTests : IDisposable
{
    private static readonly TimeZoneInfo Tz = TimeZoneInfo.CreateCustomTimeZone("UTC+8", TimeSpan.FromHours(8), "UTC+8", "UTC+8");

    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(), $"yaya-test-{Guid.NewGuid():N}.db");

    [Fact]
    public void Initialize_首次运行_创建数据库文件和三张表()
    {
        using var db = CreateInitializedDb();

        Assert.True(File.Exists(_dbPath));
        Assert.Equal(0, db.CountEvents());
        Assert.Equal(3, CountTables());
    }

    [Fact]
    public void Record_写入三类数据_可正常计数()
    {
        using var db = CreateInitializedDb();

        db.RecordEvent(DateTimeOffset.UtcNow, AppCategory.Study, "session-1", FocusState.Focused, 0);
        db.RecordEvent(DateTimeOffset.UtcNow, AppCategory.Unknown, "session-1", "AppExit", 0);
        db.RecordCorrection(DateTimeOffset.UtcNow, FocusState.Distracted, FocusState.Focused,
            AppCategory.Study, UserCorrectionKind.CountAsValid, "测试纠正");
        db.SaveDailyReport(DateOnly.FromDateTime(DateTime.Today), SampleData(), "测试三句话。");

        Assert.Equal(2, db.CountEvents());
        Assert.Equal(1, db.CountCorrections());
        Assert.Equal(1, db.CountDailySummaries());
    }

    [Fact]
    public void SaveDailyReport_同日重复保存_覆盖而非新增()
    {
        using var db = CreateInitializedDb();
        var today = DateOnly.FromDateTime(DateTime.Today);

        db.SaveDailyReport(today, SampleData(60), "第一版。");
        db.SaveDailyReport(today, SampleData(90), "第二版。");

        Assert.Equal(1, db.CountDailySummaries());
    }

    [Fact]
    public void TryGetDailyReport_保存后命中且结构化数据完整回读()
    {
        using var db = CreateInitializedDb();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var expected = new DailyReportData(today, 65,
            TimeSpan.FromHours(10), TimeSpan.FromHours(10) + TimeSpan.FromMinutes(30),
            new TimeOnly(15, 0), 10, 40, 2);

        Assert.False(db.TryGetDailyReport(today, out _, out _));   // 未生成 = 未命中
        db.SaveDailyReport(today, expected, "展示文本。");

        Assert.True(db.TryGetDailyReport(today, out var data, out var text));
        Assert.Equal("展示文本。", text);
        Assert.NotNull(data);
        Assert.Equal(expected.FocusedMinutes, data!.FocusedMinutes);
        Assert.Equal(expected.PeakWindowStart, data.PeakWindowStart);
        Assert.Equal(expected.AdviceStartTime, data.AdviceStartTime);
        Assert.Equal(expected.AdviceMinutes, data.AdviceMinutes);
        Assert.Equal(expected.CorrectionCount, data.CorrectionCount);
    }

    [Fact]
    public void GetEventsForDate_按本地日界过滤并含次日首行()
    {
        using var db = CreateInitializedDb();
        // 本地 2026-10-18（UTC+8）日界 = UTC 2026-10-17 16:00
        var dayStartUtc = new DateTimeOffset(2026, 10, 17, 16, 0, 0, TimeSpan.Zero);
        db.RecordEvent(dayStartUtc.AddMinutes(-1), AppCategory.Study, "s", "AppExit", 0);  // 前一日
        db.RecordEvent(dayStartUtc, AppCategory.Study, "s", FocusState.Focused, 0);        // 当日 00:00
        db.RecordEvent(dayStartUtc.AddHours(23).AddMinutes(59), AppCategory.Study, "s", FocusState.Distracted, 0);
        db.RecordEvent(dayStartUtc.AddDays(1), AppCategory.Study, "s", "AppExit", 0);      // 次日首行（尾段截断用）

        var rows = db.GetEventsForDate(new DateOnly(2026, 10, 18), Tz);

        Assert.Equal(3, rows.Count);
        Assert.Equal(FocusState.Focused.ToString(), rows[0].State);
        Assert.Equal("AppExit", rows[^1].State);
    }

    [Fact]
    public void ClearAllData_一键清空_数据归零但表结构保留()
    {
        using var db = CreateInitializedDb();
        db.RecordEvent(DateTimeOffset.UtcNow, AppCategory.Entertainment, "session-1", FocusState.Distracted, 5);
        db.RecordCorrection(DateTimeOffset.UtcNow, FocusState.Distracted, FocusState.Focused, AppCategory.Entertainment, UserCorrectionKind.Escape, null);
        db.SaveDailyReport(DateOnly.FromDateTime(DateTime.Today), SampleData(), "文本。");

        db.ClearAllData();

        Assert.Equal(0, db.CountEvents());
        Assert.Equal(0, db.CountCorrections());
        Assert.Equal(0, db.CountDailySummaries());
        Assert.Equal(3, CountTables());
    }

    public void Dispose()
    {
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private static DailyReportData SampleData(int focused = 60) =>
        new(DateOnly.FromDateTime(DateTime.Today), focused, null, null, null, 12, 0, 0);

    private YayaDb CreateInitializedDb()
    {
        var db = new YayaDb(_dbPath);
        db.Initialize();
        return db;
    }

    // 测试用独立连接直接查 sqlite_master，验证表结构存在
    private int CountTables()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath};Pooling=false");
        connection.Open();
        using var command = new SqliteCommand(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('events','corrections','daily_summary');",
            connection);
        return Convert.ToInt32(command.ExecuteScalar());
    }
}
