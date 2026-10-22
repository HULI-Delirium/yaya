using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Serilog;
using Yaya.Core.Storage;

namespace Yaya.App.Services;

/// <summary>
/// 数据导出（合规开关「导出」）：JSON 全量 / CSV 汇总。
/// 红线：settings 导出时剔除 api_key；任何输出不得包含 key。
/// </summary>
public static class DataExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        // 中文不转 \uXXXX，便于用户直接查看导出文件
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>JSON 全量导出：events + corrections + daily_summary + settings（api_key 已剔除）。</summary>
    public static string BuildJson(YayaDb db, AppSettings settings)
    {
        var safeSettings = new AppSettings
        {
            MonitoringEnabled = settings.MonitoringEnabled,
            NudgeEnabled = settings.NudgeEnabled,
            ClickThrough = settings.ClickThrough,
            FocusMinutes = settings.FocusMinutes,
            BreakMinutes = settings.BreakMinutes,
            Llm = settings.Llm with { ApiKey = string.Empty },   // 红线：导出剔除 api_key
        };

        var payload = new
        {
            exportedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            events = db.GetAllEvents(),
            corrections = db.GetAllCorrections(),
            dailySummaries = db.GetAllDailySummaries(),
            settings = safeSettings,
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    /// <summary>CSV 汇总导出：daily_summary 各行（summary_text 做引号转义）。</summary>
    public static string BuildDailySummaryCsv(YayaDb db)
    {
        var builder = new StringBuilder();
        builder.AppendLine("date,focused_minutes,distracted_minutes,correction_count,summary_text");
        foreach (var row in db.GetAllDailySummaries())
        {
            builder.Append(row.Date).Append(',')
                .Append(row.FocusedMinutes).Append(',')
                .Append(row.DistractedMinutes).Append(',')
                .Append(row.CorrectionCount).Append(',')
                .Append('"').Append(row.SummaryText.Replace("\"", "\"\"")).Append('"')
                .AppendLine();
        }

        return builder.ToString();
    }

    /// <summary>写入 exports 目录并返回文件路径。</summary>
    public static string WriteToFile(string content, string dataDir, string extension)
    {
        var exportsDir = Path.Combine(dataDir, "exports");
        Directory.CreateDirectory(exportsDir);
        var path = Path.Combine(exportsDir, $"yaya-export-{DateTime.Now:yyyyMMdd-HHmmss}.{extension}");
        File.WriteAllText(path, content);
        Log.Information("数据已导出：{Path}", path);
        return path;
    }
}
