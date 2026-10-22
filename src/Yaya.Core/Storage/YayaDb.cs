using System.Text.Json;
using Microsoft.Data.Sqlite;
using Yaya.Core.Monitoring;
using Yaya.Core.Services;

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
            kind            TEXT    NOT NULL,   -- CountAsValid / Escape（UserCorrectionKind）
            app_category    TEXT    NOT NULL,
            note            TEXT
        );

        CREATE TABLE IF NOT EXISTS daily_summary (
            date               TEXT PRIMARY KEY,
            focused_minutes    INTEGER NOT NULL DEFAULT 0,
            distracted_minutes INTEGER NOT NULL DEFAULT 0,
            correction_count   INTEGER NOT NULL DEFAULT 0,
            summary_text       TEXT    NOT NULL,   -- 三句话展示文本（IReportWriter 产出）
            created_at_utc     TEXT    NOT NULL,
            report_json        TEXT               -- DailyReportData 序列化（LLM 版写出器的稳定输入）
        );
        """;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

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
    public void RecordEvent(DateTimeOffset atUtc, AppCategory category, string sessionId, FocusState state, double idleSeconds) =>
        RecordEvent(atUtc, category, sessionId, state.ToString(), idleSeconds);

    /// <summary>记录一行事件，state 为自由字符串（'AppExit' / 'Pomodoro*' 等扩展事件）。</summary>
    public void RecordEvent(DateTimeOffset atUtc, AppCategory category, string sessionId, string state, double idleSeconds)
    {
        const string sql = """
            INSERT INTO events (timestamp_utc, app_category, session_id, state, idle_seconds)
            VALUES ($ts, $cat, $sid, $state, $idle);
            """;

        using var command = new SqliteCommand(sql, _connection);
        command.Parameters.AddWithValue("$ts", atUtc.ToString("O"));
        command.Parameters.AddWithValue("$cat", category.ToString());
        command.Parameters.AddWithValue("$sid", sessionId);
        command.Parameters.AddWithValue("$state", state);
        command.Parameters.AddWithValue("$idle", idleSeconds);
        command.ExecuteNonQuery();
    }

    /// <summary>记录一条用户纠正（产品红线：用户可纠正），kind 区分“算有效/逃避”。</summary>
    public void RecordCorrection(DateTimeOffset atUtc, FocusState original, FocusState corrected, AppCategory category, UserCorrectionKind kind, string? note)
    {
        const string sql = """
            INSERT INTO corrections (timestamp_utc, original_state, corrected_state, kind, app_category, note)
            VALUES ($ts, $orig, $corr, $kind, $cat, $note);
            """;

        using var command = new SqliteCommand(sql, _connection);
        command.Parameters.AddWithValue("$ts", atUtc.ToString("O"));
        command.Parameters.AddWithValue("$orig", original.ToString());
        command.Parameters.AddWithValue("$corr", corrected.ToString());
        command.Parameters.AddWithValue("$kind", kind.ToString());
        command.Parameters.AddWithValue("$cat", category.ToString());
        command.Parameters.AddWithValue("$note", note is null ? DBNull.Value : note);
        command.ExecuteNonQuery();
    }

    /// <summary>读取本地某日的 events（含次日首行，用于尾段截断），按时间升序。</summary>
    public IReadOnlyList<EventRow> GetEventsForDate(DateOnly localDate, TimeZoneInfo timeZone)
    {
        var fromUtc = ToUtc(localDate.ToDateTime(TimeOnly.MinValue), timeZone);
        // 多取一天：当日尾段的结束时刻可能是次日的第一行
        var toUtc = fromUtc.AddDays(2);

        const string sql = """
            SELECT timestamp_utc, app_category, state, idle_seconds
            FROM events
            WHERE timestamp_utc >= $from AND timestamp_utc < $to
            ORDER BY timestamp_utc, id;
            """;

        using var command = new SqliteCommand(sql, _connection);
        command.Parameters.AddWithValue("$from", fromUtc.ToString("O"));
        command.Parameters.AddWithValue("$to", toUtc.ToString("O"));

        var rows = new List<EventRow>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new EventRow(
                DateTimeOffset.Parse(reader.GetString(0)),
                Enum.Parse<AppCategory>(reader.GetString(1)),
                reader.GetString(2),
                reader.GetDouble(3)));
        }

        return rows;
    }

    /// <summary>读取本地某日的全部纠正（按时间升序）。</summary>
    public IReadOnlyList<CorrectionRow> GetCorrectionsForDate(DateOnly localDate, TimeZoneInfo timeZone)
    {
        var fromUtc = ToUtc(localDate.ToDateTime(TimeOnly.MinValue), timeZone);
        var toUtc = fromUtc.AddDays(1);

        const string sql = """
            SELECT timestamp_utc, kind
            FROM corrections
            WHERE timestamp_utc >= $from AND timestamp_utc < $to
            ORDER BY timestamp_utc, id;
            """;

        using var command = new SqliteCommand(sql, _connection);
        command.Parameters.AddWithValue("$from", fromUtc.ToString("O"));
        command.Parameters.AddWithValue("$to", toUtc.ToString("O"));

        var rows = new List<CorrectionRow>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new CorrectionRow(
                DateTimeOffset.Parse(reader.GetString(0)),
                Enum.Parse<UserCorrectionKind>(reader.GetString(1))));
        }

        return rows;
    }

    /// <summary>保存（覆盖）某日复盘：展示文本 + 聚合结构化数据。同一天重复调用为覆盖语义。</summary>
    public void SaveDailyReport(DateOnly date, DailyReportData data, string summaryText)
    {
        const string sql = """
            INSERT INTO daily_summary (date, focused_minutes, distracted_minutes, correction_count, summary_text, created_at_utc, report_json)
            VALUES ($date, $focused, $distracted, $corrections, $summary, $created, $json)
            ON CONFLICT(date) DO UPDATE SET
                focused_minutes    = excluded.focused_minutes,
                distracted_minutes = excluded.distracted_minutes,
                correction_count   = excluded.correction_count,
                summary_text       = excluded.summary_text,
                created_at_utc     = excluded.created_at_utc,
                report_json        = excluded.report_json;
            """;

        using var command = new SqliteCommand(sql, _connection);
        command.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$focused", data.FocusedMinutes);
        command.Parameters.AddWithValue("$distracted", data.DistractedMinutes);
        command.Parameters.AddWithValue("$corrections", data.CorrectionCount);
        command.Parameters.AddWithValue("$summary", summaryText);
        command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(data, JsonOptions));
        command.ExecuteNonQuery();
    }

    /// <summary>按日读缓存：report_json 存在且可解析才算命中（同一天不重复聚合）。</summary>
    public bool TryGetDailyReport(DateOnly date, out DailyReportData? data, out string? summaryText)
    {
        data = null;
        summaryText = null;

        const string sql = """
            SELECT summary_text, report_json
            FROM daily_summary
            WHERE date = $date;
            """;

        using var command = new SqliteCommand(sql, _connection);
        command.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd"));

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return false;
        }

        summaryText = reader.GetString(0);
        if (reader.IsDBNull(1))
        {
            return false;
        }

        try
        {
            data = JsonSerializer.Deserialize<DailyReportData>(reader.GetString(1));
        }
        catch (JsonException)
        {
            data = null;   // 缓存损坏按未命中处理，调用方重新聚合覆盖
        }

        return data is not null;
    }

    /// <summary>导出：全量 events（按时间升序）。</summary>
    public IReadOnlyList<EventRow> GetAllEvents()
    {
        const string sql = """
            SELECT timestamp_utc, app_category, state, idle_seconds
            FROM events
            ORDER BY timestamp_utc, id;
            """;

        var rows = new List<EventRow>();
        using var command = new SqliteCommand(sql, _connection);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new EventRow(
                DateTimeOffset.Parse(reader.GetString(0)),
                Enum.Parse<AppCategory>(reader.GetString(1)),
                reader.GetString(2),
                reader.GetDouble(3)));
        }

        return rows;
    }

    /// <summary>导出：全量 corrections（按时间升序，含全部列）。</summary>
    public IReadOnlyList<FullCorrectionRow> GetAllCorrections()
    {
        const string sql = """
            SELECT timestamp_utc, original_state, corrected_state, kind, app_category, note
            FROM corrections
            ORDER BY timestamp_utc, id;
            """;

        var rows = new List<FullCorrectionRow>();
        using var command = new SqliteCommand(sql, _connection);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new FullCorrectionRow(
                DateTimeOffset.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return rows;
    }

    /// <summary>导出：全量 daily_summary（按日期升序）。</summary>
    public IReadOnlyList<DailySummaryRow> GetAllDailySummaries()
    {
        const string sql = """
            SELECT date, focused_minutes, distracted_minutes, correction_count, summary_text, created_at_utc, report_json
            FROM daily_summary
            ORDER BY date;
            """;

        var rows = new List<DailySummaryRow>();
        using var command = new SqliteCommand(sql, _connection);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new DailySummaryRow(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetString(4),
                DateTimeOffset.Parse(reader.GetString(5)),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        return rows;
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

    private static DateTimeOffset ToUtc(DateTime localUnspecified, TimeZoneInfo timeZone) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localUnspecified, DateTimeKind.Unspecified), timeZone);

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
