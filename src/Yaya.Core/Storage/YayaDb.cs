using Microsoft.Data.Sqlite;
using Yaya.Core.Monitoring;

namespace Yaya.Core.Storage;

/// <summary>
/// SQLite 本地存储（纯托管代码，零 UI 依赖，App 与测试共用）。
/// 三张表：events 行为事件、corrections 用户纠正、daily_summary 每日复盘。
/// 约定：时间戳统一存 ISO 8601 UTC 字符串；每日复盘按本地日期 yyyy-MM-dd 一行。
/// 数据只写本机文件，连接串不含任何网络地址。
/// </summary>
public sealed class YayaDb : IDisposable
{
    /// <summary>建表 SQL 即数据库结构的事实来源，Schema 变更必须同步 ARCHITECTURE.md。</summary>
    public const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS events (
            id            INTEGER PRIMARY KEY AUTOINCREMENT,
            timestamp_utc TEXT    NOT NULL,
            app_category  TEXT    NOT NULL,
            session_id    TEXT    NOT NULL,
            state         TEXT    NOT NULL,
            idle_seconds  REAL    NOT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_events_timestamp ON events(timestamp_utc);
        CREATE INDEX IF NOT EXISTS idx_events_session ON events(session_id);

        CREATE TABLE IF NOT EXISTS corrections (
            id              INTEGER PRIMARY KEY AUTOINCREMENT,
            timestamp_utc   TEXT    NOT NULL,
            original_state  TEXT    NOT NULL,
            corrected_state TEXT    NOT NULL,
            app_category    TEXT    NOT NULL,
            note            TEXT
        );

        CREATE TABLE IF NOT EXISTS daily_summary (
            date               TEXT PRIMARY KEY,
            focused_minutes    INTEGER NOT NULL DEFAULT 0,
            distracted_minutes INTEGER NOT NULL DEFAULT 0,
            correction_count   INTEGER NOT NULL DEFAULT 0,
            summary_text       TEXT    NOT NULL,
            created_at_utc     TEXT    NOT NULL
        );
        """;

    private readonly SqliteConnection _connection;

    public YayaDb(string dataSourcePath)
    {
        // 关闭连接池：本类长期持有单条连接，池化无收益；
        // 关闭后 Dispose 即真正释放文件句柄，避免一键清空/删除数据库文件时出现句柄占用
        var builder = new SqliteConnectionStringBuilder { DataSource = dataSourcePath, Pooling = false };
        _connection = new SqliteConnection(builder.ConnectionString);
    }

    /// <summary>打开连接并按 SchemaSql 建表（幂等，可重复调用）。</summary>
    public void Initialize()
    {
        _connection.Open();
        ExecuteNonQuery(SchemaSql);
    }

    /// <summary>记录一次行为事件（状态迁移时由 App 调用）。</summary>
    public void RecordEvent(DateTimeOffset atUtc, AppCategory category, string sessionId, FocusState state, double idleSeconds)
    {
        const string sql = """
            INSERT INTO events (timestamp_utc, app_category, session_id, state, idle_seconds)
            VALUES ($ts, $cat, $sid, $state, $idle);
            """;

        using var command = new SqliteCommand(sql, _connection);
        command.Parameters.AddWithValue("$ts", atUtc.ToString("O"));
        command.Parameters.AddWithValue("$cat", category.ToString());
        command.Parameters.AddWithValue("$sid", sessionId);
        command.Parameters.AddWithValue("$state", state.ToString());
        command.Parameters.AddWithValue("$idle", idleSeconds);
        command.ExecuteNonQuery();
    }

    /// <summary>记录一条用户纠正（产品红线：用户可纠正）。</summary>
    public void RecordCorrection(DateTimeOffset atUtc, FocusState original, FocusState corrected, AppCategory category, string? note)
    {
        const string sql = """
            INSERT INTO corrections (timestamp_utc, original_state, corrected_state, app_category, note)
            VALUES ($ts, $orig, $corr, $cat, $note);
            """;

        using var command = new SqliteCommand(sql, _connection);
        command.Parameters.AddWithValue("$ts", atUtc.ToString("O"));
        command.Parameters.AddWithValue("$orig", original.ToString());
        command.Parameters.AddWithValue("$corr", corrected.ToString());
        command.Parameters.AddWithValue("$cat", category.ToString());
        command.Parameters.AddWithValue("$note", note is null ? DBNull.Value : note);
        command.ExecuteNonQuery();
    }

    /// <summary>保存（覆盖）某日的复盘，summaryText 固定为三句话。</summary>
    public void SaveDailySummary(DateOnly date, int focusedMinutes, int distractedMinutes, int correctionCount, string summaryText)
    {
        const string sql = """
            INSERT INTO daily_summary (date, focused_minutes, distracted_minutes, correction_count, summary_text, created_at_utc)
            VALUES ($date, $focused, $distracted, $corrections, $summary, $created)
            ON CONFLICT(date) DO UPDATE SET
                focused_minutes    = excluded.focused_minutes,
                distracted_minutes = excluded.distracted_minutes,
                correction_count   = excluded.correction_count,
                summary_text       = excluded.summary_text,
                created_at_utc     = excluded.created_at_utc;
            """;

        using var command = new SqliteCommand(sql, _connection);
        command.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$focused", focusedMinutes);
        command.Parameters.AddWithValue("$distracted", distractedMinutes);
        command.Parameters.AddWithValue("$corrections", correctionCount);
        command.Parameters.AddWithValue("$summary", summaryText);
        command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    /// <summary>一键清空：删除全部业务数据但保留表结构（产品红线：本地优先、可清空）。</summary>
    public void ClearAllData()
    {
        const string sql = """
            DELETE FROM events;
            DELETE FROM corrections;
            DELETE FROM daily_summary;
            VACUUM;
            """;
        ExecuteNonQuery(sql);
    }

    public int CountEvents() => CountRows("events");

    public int CountCorrections() => CountRows("corrections");

    public int CountDailySummaries() => CountRows("daily_summary");

    public void Dispose() => _connection.Dispose();

    private int CountRows(string table)
    {
        using var command = new SqliteCommand($"SELECT COUNT(*) FROM {table};", _connection);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private void ExecuteNonQuery(string sql)
    {
        using var command = new SqliteCommand(sql, _connection);
        command.ExecuteNonQuery();
    }
}
