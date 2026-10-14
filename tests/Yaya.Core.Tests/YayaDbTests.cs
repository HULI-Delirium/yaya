using Microsoft.Data.Sqlite;
using Yaya.Core.Monitoring;
using Yaya.Core.Services;
using Yaya.Core.Storage;

namespace Yaya.Core.Tests;

public class YayaDbTests : IDisposable
{
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
        db.RecordCorrection(DateTimeOffset.UtcNow, FocusState.Distracted, FocusState.Focused,
            AppCategory.Study, UserCorrectionKind.CountAsValid, "测试纠正");
        var summary = DailySummaryComposer.Compose(120, 30, 1);
        db.SaveDailySummary(DateOnly.FromDateTime(DateTime.Today), 120, 30, 1, summary);

        Assert.Equal(1, db.CountEvents());
        Assert.Equal(1, db.CountCorrections());
        Assert.Equal(1, db.CountDailySummaries());
    }

    [Fact]
    public void SaveDailySummary_同日重复保存_覆盖而非新增()
    {
        using var db = CreateInitializedDb();
        var today = DateOnly.FromDateTime(DateTime.Today);

        db.SaveDailySummary(today, 60, 10, 0, DailySummaryComposer.Compose(60, 10, 0));
        db.SaveDailySummary(today, 90, 20, 0, DailySummaryComposer.Compose(90, 20, 0));

        Assert.Equal(1, db.CountDailySummaries());
    }

    [Fact]
    public void ClearAllData_一键清空_数据归零但表结构保留()
    {
        using var db = CreateInitializedDb();
        db.RecordEvent(DateTimeOffset.UtcNow, AppCategory.Entertainment, "session-1", FocusState.Distracted, 5);
        db.RecordCorrection(DateTimeOffset.UtcNow, FocusState.Distracted, FocusState.Focused,
            AppCategory.Entertainment, UserCorrectionKind.Escape, null);
        db.SaveDailySummary(DateOnly.FromDateTime(DateTime.Today), 10, 10, 1, DailySummaryComposer.Compose(10, 10, 1));

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
